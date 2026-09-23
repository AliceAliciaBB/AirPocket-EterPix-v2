using UdonSharp;
using UnityEngine;
using VRC.SDK3.Image;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ali.eterpix
{
    // U#はネストされた型宣言をサポートしないため、名前空間直下に定義する
    public enum eterpix_image_slot_state { Unrequested, Queued, Downloading, Loaded }

    // 上位(loader)の画像担当。方針.md「Textureの状態遷移」「collage_id参照カウンター仕様」を参照。
    // collage_idは0〜(maxSlots-1)のスロット番号(合成画像。1枚に6投稿分がimg_pos 1〜6で収録)。
    // 実際に表示される画像のみをオンデマンドでロードする(先読み一括DLは行わない)。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_image_loader : UdonSharpBehaviour
    {
        public const string SingletonObjectName = "EterpixImageLoader";

        // URL設定(collage_id -> URL)。vrcurllist自体は中位が保持し、
        // FetchLoaders()時にSetUrlList()で登録される(方針.md「vrcurllist参照」は中位側)。
        private vrcurllist urlList;

        [Header("デバッグログ")]
        [SerializeField] private eterpix_debug debugLog;

        [Header("参照カウンターが0になってから実破棄までの保持秒数")]
        [SerializeField] private float releaseGraceSeconds = 10f;

        [SerializeField] private int maxSlots = 16;

        private eterpix_image_slot_state[] _state;
        private Texture2D[] _textures;
        private int[] _refCount;
        private int[] _priority;
        private int[] _graceGeneration; // グレース期間中の再要求検知用

        // 待機中の呼び出し元(型ごとに別配列。UdonSharpは多態が弱いため)
        private eterpix_cell[][] _waitingCells;
        private eterpix_listener_monitor[][] _waitingMonitors;

        private VRCImageDownloader _downloader;
        private bool _isDownloading = false;
        private int _currentDownloadSlot = -1;

        private void Start()
        {
            GameObject canonical = GameObject.Find(SingletonObjectName);
            if (canonical != null && canonical != this.gameObject)
            {
                Debug.LogWarning("[eterpix_image_loader] Duplicate instance detected. Disabling self.");
                gameObject.SetActive(false);
                return;
            }

            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<eterpix_debug>();
            }

            _state = new eterpix_image_slot_state[maxSlots];
            _textures = new Texture2D[maxSlots];
            _refCount = new int[maxSlots];
            _priority = new int[maxSlots];
            _graceGeneration = new int[maxSlots];
            _waitingCells = new eterpix_cell[maxSlots][];
            _waitingMonitors = new eterpix_listener_monitor[maxSlots][];
            for (int i = 0; i < maxSlots; i++)
            {
                _waitingCells[i] = new eterpix_cell[50];
                _waitingMonitors[i] = new eterpix_listener_monitor[50];
            }

            _downloader = new VRCImageDownloader();
        }

        // ---- 中位からのvrcurllist登録(最初に登録された1つを使う) ----
        public void SetUrlList(vrcurllist list)
        {
            if (urlList == null) urlList = list;
        }

        // ---- Request/Release (呼び出し元の型ごとにオーバーロード) ----

        public void RequestTexture(int collageId, int priority, eterpix_cell caller)
        {
            if (!ValidateSlot(collageId)) return;

            _refCount[collageId]++;
            RaisePriority(collageId, priority);

            if (_state[collageId] == eterpix_image_slot_state.Loaded)
            {
                caller.ApplyTexture(_textures[collageId]);
                return;
            }

            AddWaitingCell(collageId, caller);
            EnsureQueued(collageId);
        }

        public void RequestTexture(int collageId, int priority, eterpix_listener_monitor caller)
        {
            if (!ValidateSlot(collageId)) return;

            _refCount[collageId]++;
            RaisePriority(collageId, priority);

            if (_state[collageId] == eterpix_image_slot_state.Loaded)
            {
                caller.ApplyTexture(_textures[collageId]);
                return;
            }

            AddWaitingMonitor(collageId, caller);
            EnsureQueued(collageId);
        }

        public void ReleaseTexture(int collageId, eterpix_cell caller)
        {
            ReleaseInternal(collageId);
        }

        public void ReleaseTexture(int collageId, eterpix_listener_monitor caller)
        {
            ReleaseInternal(collageId);
        }

        private void ReleaseInternal(int collageId)
        {
            if (!ValidateSlot(collageId)) return;

            _refCount[collageId] = Mathf.Max(0, _refCount[collageId] - 1);
            if (_refCount[collageId] == 0 && _state[collageId] == eterpix_image_slot_state.Loaded)
            {
                // グレース期間経過後、まだ参照0のままなら破棄する
                _graceGeneration[collageId]++;
                int slotArg = collageId;
                int generation = _graceGeneration[collageId];
                _pendingDiscardSlot = slotArg;
                _pendingDiscardGeneration = generation;
                SendCustomEventDelayedSeconds(nameof(TryDiscardSlot), releaseGraceSeconds);
            }
        }

        // SendCustomEventDelayedSecondsは引数を渡せないため、直近の破棄予約を
        // 一時フィールドで受け渡す(複数の破棄要求が短時間に重なっても、
        // 各呼び出し時点のgenerationがずれていれば破棄しないので安全)。
        private int _pendingDiscardSlot;
        private int _pendingDiscardGeneration;

        public void TryDiscardSlot()
        {
            int slot = _pendingDiscardSlot;
            int generation = _pendingDiscardGeneration;

            if (_refCount[slot] != 0) return; // 期間中に再要求された
            if (_graceGeneration[slot] != generation) return; // さらに新しい破棄予約が上書き済み

            _textures[slot] = null;
            _state[slot] = eterpix_image_slot_state.Unrequested;
            if (debugLog != null) debugLog.Log($"[eterpix_image_loader] Discarded slot {slot}");
        }

        // ---- 優先度付きロードキュー ----

        private void RaisePriority(int collageId, int priority)
        {
            if (priority > _priority[collageId]) _priority[collageId] = priority;
        }

        private void EnsureQueued(int collageId)
        {
            if (_state[collageId] == eterpix_image_slot_state.Unrequested)
            {
                _state[collageId] = eterpix_image_slot_state.Queued;
            }
            StartNextDownloadIfIdle();
        }

        private void StartNextDownloadIfIdle()
        {
            if (_isDownloading) return;

            int best = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < maxSlots; i++)
            {
                if (_state[i] == eterpix_image_slot_state.Queued && _priority[i] > bestPriority)
                {
                    bestPriority = _priority[i];
                    best = i;
                }
            }

            if (best < 0) return;

            _state[best] = eterpix_image_slot_state.Downloading;
            _currentDownloadSlot = best;
            _isDownloading = true;

            VRCUrl url = urlList != null ? urlList.GetUrl(best) : null;
            if (url == null)
            {
                if (debugLog != null) debugLog.LogWarning($"[eterpix_image_loader] URL for slot {best} is null, skipping");
                _state[best] = eterpix_image_slot_state.Unrequested;
                _isDownloading = false;
                StartNextDownloadIfIdle();
                return;
            }

            if (debugLog != null) debugLog.Log($"[eterpix_image_loader] Downloading slot {best}: {url}");
            _downloader.DownloadImage(url, null, (IUdonEventReceiver)this, null);
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            int slot = _currentDownloadSlot;
            _isDownloading = false;

            _textures[slot] = result.Result;
            _state[slot] = eterpix_image_slot_state.Loaded;
            _priority[slot] = 0;

            if (debugLog != null) debugLog.Log($"[eterpix_image_loader] Success slot {slot}: {result.Result.width}x{result.Result.height}");

            NotifyWaiters(slot, result.Result);
            StartNextDownloadIfIdle();
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            int slot = _currentDownloadSlot;
            _isDownloading = false;

            if (debugLog != null) debugLog.LogWarning($"[eterpix_image_loader] Load error slot {slot}: {result.Error}. Retrying.");

            // リトライ: まだ参照があるならキューへ戻す
            if (_refCount[slot] > 0)
            {
                _state[slot] = eterpix_image_slot_state.Queued;
            }
            else
            {
                _state[slot] = eterpix_image_slot_state.Unrequested;
            }

            StartNextDownloadIfIdle();
        }

        private void NotifyWaiters(int slot, Texture2D texture)
        {
            eterpix_cell[] cells = _waitingCells[slot];
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != null)
                {
                    cells[i].ApplyTexture(texture);
                    cells[i] = null;
                }
            }

            eterpix_listener_monitor[] monitors = _waitingMonitors[slot];
            for (int i = 0; i < monitors.Length; i++)
            {
                if (monitors[i] != null)
                {
                    monitors[i].ApplyTexture(texture);
                    monitors[i] = null;
                }
            }
        }

        private void AddWaitingCell(int slot, eterpix_cell caller)
        {
            eterpix_cell[] arr = _waitingCells[slot];
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) { arr[i] = caller; return; }
            }
            if (debugLog != null) debugLog.LogWarning($"[eterpix_image_loader] Waiting cell list full for slot {slot}");
        }

        private void AddWaitingMonitor(int slot, eterpix_listener_monitor caller)
        {
            eterpix_listener_monitor[] arr = _waitingMonitors[slot];
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) { arr[i] = caller; return; }
            }
            if (debugLog != null) debugLog.LogWarning($"[eterpix_image_loader] Waiting monitor list full for slot {slot}");
        }

        private bool ValidateSlot(int collageId)
        {
            if (collageId < 0 || collageId >= maxSlots)
            {
                if (debugLog != null) debugLog.LogWarning($"[eterpix_image_loader] Invalid collageId: {collageId}");
                return false;
            }
            return true;
        }
    }
}
