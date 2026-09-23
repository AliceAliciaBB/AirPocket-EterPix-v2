using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Image;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Data;

namespace ali.eterpix.v2
{
    public enum eterpix_v2_image_state { Unrequested, Queued, Downloading, Loaded }

    // v2上位: ダウンローダ。リファクタリング案.md「ダウンローダ」を参照。
    // 1ワールドに1つのみ配置(シングルトン)。複数フィード(JSON URLごと)を管理する。
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_downloader : UdonSharpBehaviour
    {
        public const string SingletonObjectName = "EterpixDownloaderV2";
        public const int ImageSlotsPerFeed = 256;

        [Header("最大フィード数(固定サイズ配列の上限)")]
        [SerializeField] private int maxFeeds = 4;

        [Header("定期自動再取得(サーバーの10分枠に合わせる)")]
        [SerializeField] private int intervalMinutes = 10;
        [SerializeField] private int offsetMinutes = 1;

        [Header("デバッグログ")]
        [SerializeField] private ali.eterpix.eterpix_debug debugLog;

        private const float RequestTimeoutSeconds = 15f;

        private string[] _feedKeys;      // VRCUrl.Get()の文字列(重複登録の判定キー)
        private VRCUrl[] _feedUrls;
        private DataList[] _feedPosts;       // 生データ(フィルタ前)
        private DataDictionary[] _feedWorldData;
        private bool[] _feedPending;
        private eterpix_requester[][] _feedRequesters; // フィードごとに登録されたリクエスター(可変長を模した固定長+件数管理)
        private int[] _feedRequesterCounts;
        private int _feedCount = 0;

        // OnStringLoadSuccess/Error はどのフィードの応答か引数で判別できないため、
        // 直近リクエストしたフィードIndexを一時保持する(直列ダウンロード前提)。
        private int _pendingFeedIndex = -1;

        // 実際に直列化するためのFIFOキュー(複数フィードへ同時にRequestJsonが呼ばれても、
        // 実際にLoadUrlするのは1件ずつにする)。
        private int[] _fetchQueue;
        private int _fetchQueueCount = 0;
        private bool _isFetching = false;

        [Header("画像: 同時保持テクスチャの上限枚数(既定8枚≒150MB)")]
        [SerializeField] private int maxLoadedTextures = 8;

        [Header("画像: 参照0になってから実破棄までの猶予秒数")]
        [SerializeField] private float releaseGraceSeconds = 10f;

        private ali.eterpix.vrcurllist[] _feedUrlLists; // フィードごとの画像URL表(先着1つ)

        // [feedIndex][slot] の2次元をフラットな1次元(feedIndex * ImageSlotsPerFeed + slot)で管理する
        // (UdonSharpはジャグ配列も使えるが、フラット配列の方が走査ロジックを単純化できる)
        private eterpix_v2_image_state[] _imgState;
        private Texture2D[] _imgTexture;
        private int[] _imgRefCount;
        private int[] _imgPriority;
        private int[] _imgGraceGeneration;
        // 各キーが最後に「参照0になった瞬間」の絶対時刻(Time.time)。
        // 世代一致チェックだけでは「たまたま他のキーのスキャンが走った」ことしか分からず、
        // このキー自身のreleaseGraceSecondsが本当に経過したかは判定できない
        // (releaseGraceSeconds未満の間隔で複数キーが解放されると、後発キーが先発キーの
        // スキャンに巻き込まれて猶予期間を待たずに破棄されてしまう: レビュー指摘のクロスキー早期破棄)。
        // このタイムスタンプとTime.timeの差で、キー自身の経過時間を検証する。
        private float[] _imgReleaseTime;
        private int _loadedCount = 0;

        private const int MaxWaitersPerSlot = 30;
        private eterpix_monitor[] _waitingMonitorsFlat; // [key * MaxWaitersPerSlot + waiterSlot]

        private VRC.SDK3.Image.VRCImageDownloader _imageDownloader;
        private bool _isImageDownloading = false;
        private int _currentDownloadKey = -1;

        // 破棄予約は「どのキーを見るか」をスケジューリング時の引数として渡せない(UdonSharpの
        // SendCustomEventDelayedSecondsは引数を運べず、常に同じメソッド名を呼ぶだけ)。
        // かつ複数キーの猶予期間が同時に進行しうるため、単一のスカラーで次に見るキーを覚える方式では
        // 「後から解放されたキーが先に解放されたキーの予約を上書きしてしまう」問題(リーク/二重判定)が起きる。
        // 対策として、TryDiscardSlotは呼ばれるたびに全スロットを走査し、各キー自身が持つ
        // 「このキーの直近の解放が何世代目か(_imgGraceGeneration)」と「このスケジュール呼び出しが
        // 待っていた世代(_imgDiscardTargetGeneration)」を突き合わせて判定する。スロット総数は
        // maxFeeds * ImageSlotsPerFeed(既定で高々1024程度)と小さいため、毎回全走査しても軽い。
        private int[] _imgDiscardTargetGeneration;

        private void Start()
        {
            GameObject canonical = GameObject.Find(SingletonObjectName);
            if (canonical != null && canonical != this.gameObject)
            {
                Debug.LogWarning("[eterpix_downloader] Duplicate instance detected. Disabling self.");
                gameObject.SetActive(false);
                return;
            }

            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(ali.eterpix.eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<ali.eterpix.eterpix_debug>();
            }

            _feedKeys = new string[maxFeeds];
            _feedUrls = new VRCUrl[maxFeeds];
            _feedPosts = new DataList[maxFeeds];
            _feedWorldData = new DataDictionary[maxFeeds];
            _feedPending = new bool[maxFeeds];
            _feedRequesters = new eterpix_requester[maxFeeds][];
            _feedRequesterCounts = new int[maxFeeds];
            _fetchQueue = new int[maxFeeds];
            for (int i = 0; i < maxFeeds; i++)
            {
                _feedRequesters[i] = new eterpix_requester[16];
            }

            int totalSlots = maxFeeds * ImageSlotsPerFeed;
            _feedUrlLists = new ali.eterpix.vrcurllist[maxFeeds];
            _imgState = new eterpix_v2_image_state[totalSlots];
            _imgTexture = new Texture2D[totalSlots];
            _imgRefCount = new int[totalSlots];
            _imgPriority = new int[totalSlots];
            _imgGraceGeneration = new int[totalSlots];
            _imgDiscardTargetGeneration = new int[totalSlots];
            _imgReleaseTime = new float[totalSlots];
            for (int i = 0; i < totalSlots; i++) _imgReleaseTime[i] = -Mathf.Infinity;
            _waitingMonitorsFlat = new eterpix_monitor[totalSlots * MaxWaitersPerSlot];
            _imageDownloader = new VRC.SDK3.Image.VRCImageDownloader();

            ScheduleNextUpdate();
        }

        // ---- リクエスターからのフィード登録 ----
        public int RegisterFeed(VRCUrl jsonUrl, eterpix_requester requester)
        {
            string key = jsonUrl != null ? jsonUrl.Get() : "";
            int index = FindFeedIndex(key);

            if (index < 0)
            {
                if (_feedCount >= maxFeeds)
                {
                    if (debugLog != null) debugLog.LogError("[eterpix_downloader] maxFeeds exceeded, cannot register new feed.");
                    return -1;
                }

                index = _feedCount;
                _feedCount++;
                _feedKeys[index] = key;
                _feedUrls[index] = jsonUrl;

                RequestJson(index);
            }

            AddRequester(index, requester);
            return index;
        }

        private int FindFeedIndex(string key)
        {
            for (int i = 0; i < _feedCount; i++)
            {
                if (_feedKeys[i] == key) return i;
            }
            return -1;
        }

        private void AddRequester(int feedIndex, eterpix_requester requester)
        {
            int count = _feedRequesterCounts[feedIndex];
            eterpix_requester[] arr = _feedRequesters[feedIndex];
            if (count >= arr.Length)
            {
                eterpix_requester[] bigger = new eterpix_requester[arr.Length + 16];
                for (int i = 0; i < arr.Length; i++) bigger[i] = arr[i];
                arr = bigger;
                _feedRequesters[feedIndex] = arr;
            }
            arr[count] = requester;
            _feedRequesterCounts[feedIndex] = count + 1;

            // 既にJSONが届いているフィードに後から登録された場合は即座に通知する
            if (_feedPosts[feedIndex] != null) requester.OnJsonUpdated();
        }

        // ---- 定期自動更新(オーナーが「今取得しろ」の指示のみ全員に同期) ----
        private void ScheduleNextUpdate()
        {
            System.DateTime now = System.DateTime.Now;
            System.DateTime candidate = new System.DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            while (candidate <= now || ((candidate.Minute - offsetMinutes) % intervalMinutes + intervalMinutes) % intervalMinutes != 0)
            {
                candidate = candidate.AddMinutes(1);
            }

            float delay = (float)(candidate - now).TotalSeconds;
            SendCustomEventDelayedSeconds(nameof(TriggerScheduledUpdate), delay);
        }

        public void TriggerScheduledUpdate()
        {
            if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(RequestAllFeeds));
            }
            ScheduleNextUpdate();
        }

        public void RequestAllFeeds()
        {
            for (int i = 0; i < _feedCount; i++) RequestJson(i);
        }

        // ---- リクエスターからの手動更新(そのフィードだけ取り直す) ----
        public void RequestManualRefresh(int feedIndex)
        {
            RequestJson(feedIndex);
        }

        // ---- JSON取得の実行(各クライアントがローカルで実行。OnStringLoadSuccess/Errorが
        // どのフィードの応答か判別できないため、実際にLoadUrlするのは常に1件だけになるよう
        // _fetchQueueで直列化する。短時間の重複要求は_feedPendingで弾く) ----
        private void RequestJson(int feedIndex)
        {
            if (feedIndex < 0 || feedIndex >= _feedCount) return;
            if (_feedPending[feedIndex]) return;

            _feedPending[feedIndex] = true;
            EnqueueFetch(feedIndex);
            StartNextFetchIfIdle();
        }

        private void EnqueueFetch(int feedIndex)
        {
            // RequestJsonが_feedPendingで早期returnするため、通常はここに重複が来ることはない。
            // 念のための防御的ガード(保険)であり、これ自体が動作の前提ではない。
            for (int i = 0; i < _fetchQueueCount; i++)
            {
                if (_fetchQueue[i] == feedIndex) return; // 二重登録防止
            }
            _fetchQueue[_fetchQueueCount] = feedIndex;
            _fetchQueueCount++;
        }

        private void StartNextFetchIfIdle()
        {
            if (_isFetching) return;
            if (_fetchQueueCount <= 0) return;

            int feedIndex = _fetchQueue[0];
            for (int i = 1; i < _fetchQueueCount; i++)
            {
                _fetchQueue[i - 1] = _fetchQueue[i];
            }
            _fetchQueueCount--;

            _isFetching = true;
            _pendingFeedIndex = feedIndex;
            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Fetching JSON for feed {feedIndex}...");
            SendCustomEventDelayedSeconds(nameof(CheckRequestTimeout), RequestTimeoutSeconds);
            VRCStringDownloader.LoadUrl(_feedUrls[feedIndex], (IUdonEventReceiver)this);
        }

        // VRCのダウンロードコールバック(OnStringLoadSuccess/Error)にはリクエスト識別子が無く、
        // 「今どのフィードの応答を待っているか」は_pendingFeedIndexという単一の共有フィールドでしか
        // 判別できない。タイムアウトでキューを次に進めてしまうと、後から届く(単に遅かっただけの)
        // 本来の応答が、その時点で「現在のフィード」になっている別フィードのものとして誤登録され、
        // データを取り違える。これを避けるため、タイムアウトはログ警告のみで実際には何もしない
        // (eterpix_json_loader.CheckRequestTimeoutと同じ、あえての制約。詰まったフィード自身だけでなく
        // キューに並んでいる後続フィードの取得も止まってしまうが、データ破損より安全なため許容する)。
        public void CheckRequestTimeout()
        {
            int feedIndex = _pendingFeedIndex;
            if (feedIndex >= 0 && feedIndex < _feedCount && _feedPending[feedIndex] && _isFetching)
            {
                if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] No response after {RequestTimeoutSeconds}s for feed {feedIndex} (request may be hanging)");
            }
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            int feedIndex = _pendingFeedIndex;
            _feedPending[feedIndex] = false;
            _isFetching = false;

            if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken token) ||
                token.TokenType != TokenType.DataDictionary)
            {
                if (debugLog != null) debugLog.LogError($"[eterpix_downloader] Failed to parse JSON for feed {feedIndex}");
                StartNextFetchIfIdle();
                return;
            }

            DataDictionary rootDict = token.DataDictionary;

            if (rootDict.TryGetValue("posts", out DataToken postToken) && postToken.TokenType == TokenType.DataList)
            {
                _feedPosts[feedIndex] = postToken.DataList;
            }
            else
            {
                if (debugLog != null) debugLog.LogError($"[eterpix_downloader] 'posts' key not found for feed {feedIndex}");
                StartNextFetchIfIdle();
                return;
            }

            if (rootDict.TryGetValue("world_data", out DataToken worldDataToken) && worldDataToken.TokenType == TokenType.DataDictionary)
            {
                _feedWorldData[feedIndex] = worldDataToken.DataDictionary;
            }
            else
            {
                _feedWorldData[feedIndex] = null;
            }

            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Feed {feedIndex}: {_feedPosts[feedIndex].Count} posts");

            NotifyFeedRequesters(feedIndex);
            StartNextFetchIfIdle();
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            int feedIndex = _pendingFeedIndex;
            _feedPending[feedIndex] = false;
            _isFetching = false;
            if (debugLog != null) debugLog.LogError($"[eterpix_downloader] String load error for feed {feedIndex}: {result.Error}");
            StartNextFetchIfIdle();
        }

        private void NotifyFeedRequesters(int feedIndex)
        {
            eterpix_requester[] arr = _feedRequesters[feedIndex];
            int count = _feedRequesterCounts[feedIndex];
            for (int i = 0; i < count; i++)
            {
                if (arr[i] != null) arr[i].OnJsonUpdated();
            }
        }

        // ---- リクエスターからの取得(生データ。R18フィルタはリクエスター側の責務) ----
        public DataList GetFeedPosts(int feedIndex)
        {
            if (feedIndex < 0 || feedIndex >= _feedCount) return null;
            return _feedPosts[feedIndex];
        }

        public DataDictionary GetFeedWorldData(int feedIndex)
        {
            if (feedIndex < 0 || feedIndex >= _feedCount) return null;
            return _feedWorldData[feedIndex];
        }

        // ---- 画像スロット管理: URL表登録 ----
        public void RegisterFeedImageUrls(int feedIndex, ali.eterpix.vrcurllist urlList)
        {
            if (feedIndex < 0 || feedIndex >= maxFeeds) return;
            if (_feedUrlLists[feedIndex] == null) _feedUrlLists[feedIndex] = urlList;
        }

        private int ToKey(int feedIndex, int slot)
        {
            return feedIndex * ImageSlotsPerFeed + slot;
        }

        private bool ValidateSlot(int feedIndex, int slot)
        {
            if (feedIndex < 0 || feedIndex >= maxFeeds || slot < 0 || slot >= ImageSlotsPerFeed)
            {
                if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] Invalid slot request feed={feedIndex} slot={slot}");
                return false;
            }
            return true;
        }

        public void RequestTexture(int feedIndex, int slot, int priority, eterpix_monitor caller)
        {
            if (!ValidateSlot(feedIndex, slot)) return;
            int key = ToKey(feedIndex, slot);

            _imgRefCount[key]++;
            if (priority > _imgPriority[key]) _imgPriority[key] = priority;

            if (_imgState[key] == eterpix_v2_image_state.Loaded)
            {
                caller.ApplyTexture(_imgTexture[key]);
                return;
            }

            AddWaiter(key, caller);
            EnsureQueued(key);
        }

        public void ReleaseTexture(int feedIndex, int slot, eterpix_monitor caller)
        {
            if (!ValidateSlot(feedIndex, slot)) return;
            int key = ToKey(feedIndex, slot);

            _imgRefCount[key] = Mathf.Max(0, _imgRefCount[key] - 1);
            if (_imgRefCount[key] == 0)
            {
                // 誰も見ていないスロットは、猶予破棄が実際に発生するより前に優先度をリセットしておく。
                // これによりEvictIfOverBudgetの「参照0の中で最低優先度」比較が、
                // 「過去に高優先度で見られていた」という古い値ではなく「今は誰も欲しがっていない」を
                // 正しく反映するようになる(参照0チェックの後段なので、Loaded以外の状態でも安全)。
                _imgPriority[key] = 0;

                if (_imgState[key] == eterpix_v2_image_state.Loaded)
                {
                    _imgGraceGeneration[key]++;
                    _imgDiscardTargetGeneration[key] = _imgGraceGeneration[key];
                    _imgReleaseTime[key] = Time.time;
                    SendCustomEventDelayedSeconds(nameof(TryDiscardSlot), releaseGraceSeconds);
                }
            }
        }

        // ReleaseTextureで解放されるたびに(引数を運べないSendCustomEventDelayedSeconds経由で)
        // 遅延スケジュールされる。呼ばれた時点で全スロットを走査し、「参照0のまま・Loaded状態を維持していて・
        // このスケジュール呼び出しが待っていた世代(_imgDiscardTargetGeneration)がそのキーの最新の解放世代
        // (_imgGraceGeneration)と一致する」キーだけを破棄する。世代が一致しないキーは、
        // 猶予期間中に再リクエストされた(refcountが0でなくなった時点で以後のスキャンから除外される)か、
        // 再リクエスト後にさらに解放されて新しい猶予タイマーが動いている(世代が進んでいるので、
        // この古いスキャンは無視して新しいスキャンに任せる)ケースであり、誤って破棄しない。
        // 一度も解放されたことのないキーは_imgGraceGeneration/_imgDiscardTargetGenerationが
        // 共に既定値0で一致してしまうが、そのようなキーはLoaded状態になり得ない(Unrequested/Queued/
        // Downloadingのいずれか)ため、_imgState==Loadedのチェックで誤破棄を防いでいる。
        //
        // ただし世代一致だけでは不十分だった(レビュー指摘): 世代一致は「このキーの解放が
        // その後再リクエストで上書きされていないか」しか見ておらず、「スキャンが今たまたま
        // 走った」ことと「このキー自身のreleaseGraceSecondsが経過したこと」は別物である。
        // 例えばキーXがt=0で解放され(t=grace秒後にスキャン予約)、キーYが独立にt=grace-ε秒で
        // 解放される(t=2*grace-ε秒後にスキャン予約)と、t=grace秒でXのスキャンが走った際に
        // 世代一致条件だけならYも(まだε秒しか経っていないのに)巻き込んで破棄してしまい、
        // 猶予期間が実質ゼロに潰れる。これを防ぐため、_imgReleaseTime(このキーが最後に
        // 参照0になった絶対時刻)からの経過時間が実際にreleaseGraceSeconds以上であることを
        // 追加で要求する。これにより、他キーのスキャンに巻き込まれた場合はLoadedのまま
        // 世代も変えずにスキップされるだけなので、そのキー自身に予約されたスキャン(または
        // それ以降の任意のスキャン)が来た時点で必ず経過時間条件を満たし破棄される。
        // つまりリーク(Bug1)を再導入することはない: RequestTexture/ReleaseTextureのサイクルが
        // 続く限り将来のスキャンは必ず発生し続けるため、猶予期限を過ぎた瞬間以降の
        // 最初のスキャンで確実に回収される。
        public void TryDiscardSlot()
        {
            for (int key = 0; key < _imgState.Length; key++)
            {
                if (_imgRefCount[key] == 0 &&
                    _imgState[key] == eterpix_v2_image_state.Loaded &&
                    _imgGraceGeneration[key] == _imgDiscardTargetGeneration[key] &&
                    Time.time - _imgReleaseTime[key] >= releaseGraceSeconds)
                {
                    DiscardSlot(key);
                }
            }
        }

        private void DiscardSlot(int key)
        {
            if (_imgState[key] != eterpix_v2_image_state.Loaded) return;
            _imgTexture[key] = null;
            _imgState[key] = eterpix_v2_image_state.Unrequested;
            _imgPriority[key] = 0; // 破棄されたスロットの優先度は次の再リクエストへ持ち越さない
            _loadedCount = Mathf.Max(0, _loadedCount - 1);
            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Discarded key {key}");
        }

        private void AddWaiter(int key, eterpix_monitor caller)
        {
            int baseIndex = key * MaxWaitersPerSlot;
            for (int i = 0; i < MaxWaitersPerSlot; i++)
            {
                if (_waitingMonitorsFlat[baseIndex + i] == null)
                {
                    _waitingMonitorsFlat[baseIndex + i] = caller;
                    return;
                }
            }
            if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] Waiter list full for key {key}");
        }

        private void EnsureQueued(int key)
        {
            if (_imgState[key] == eterpix_v2_image_state.Unrequested)
            {
                _imgState[key] = eterpix_v2_image_state.Queued;
            }
            StartNextImageDownloadIfIdle();
        }

        // 参照0のスロットがmaxLoadedTexturesを超えて溜まった場合、優先度最低のものから破棄してから
        // 次のダウンロードに進む(単純なLRU代替: 参照0かつ優先度最低を破棄対象とする)。
        private void EvictIfOverBudget()
        {
            while (_loadedCount > maxLoadedTextures)
            {
                int worstKey = -1;
                int worstPriority = int.MaxValue;
                for (int i = 0; i < _imgState.Length; i++)
                {
                    if (_imgState[i] == eterpix_v2_image_state.Loaded && _imgRefCount[i] == 0 && _imgPriority[i] < worstPriority)
                    {
                        worstPriority = _imgPriority[i];
                        worstKey = i;
                    }
                }
                if (worstKey < 0) return; // 破棄可能な(参照0の)スロットが無い
                DiscardSlot(worstKey);
            }
        }

        private void StartNextImageDownloadIfIdle()
        {
            if (_isImageDownloading) return;

            int best = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < _imgState.Length; i++)
            {
                if (_imgState[i] == eterpix_v2_image_state.Queued && _imgPriority[i] > bestPriority)
                {
                    bestPriority = _imgPriority[i];
                    best = i;
                }
            }
            if (best < 0) return;

            int feedIndex = best / ImageSlotsPerFeed;
            int slot = best % ImageSlotsPerFeed;

            ali.eterpix.vrcurllist urlList = _feedUrlLists[feedIndex];
            VRCUrl url = urlList != null ? urlList.GetUrl(slot) : null;
            if (url == null)
            {
                if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] URL missing feed={feedIndex} slot={slot}, skipping");
                _imgState[best] = eterpix_v2_image_state.Unrequested;
                StartNextImageDownloadIfIdle();
                return;
            }

            _imgState[best] = eterpix_v2_image_state.Downloading;
            _currentDownloadKey = best;
            _isImageDownloading = true;

            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Downloading feed={feedIndex} slot={slot}: {url}");
            _imageDownloader.DownloadImage(url, null, (IUdonEventReceiver)this, null);
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            int key = _currentDownloadKey;
            _isImageDownloading = false;

            _imgTexture[key] = result.Result;
            _imgState[key] = eterpix_v2_image_state.Loaded;
            _loadedCount++;

            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Success key={key}: {result.Result.width}x{result.Result.height}");

            NotifyWaiters(key, result.Result);
            EvictIfOverBudget();
            StartNextImageDownloadIfIdle();
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            int key = _currentDownloadKey;
            _isImageDownloading = false;

            if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] Image load error key={key}: {result.Error}. Retrying if still referenced.");

            _imgState[key] = _imgRefCount[key] > 0 ? eterpix_v2_image_state.Queued : eterpix_v2_image_state.Unrequested;
            StartNextImageDownloadIfIdle();
        }

        private void NotifyWaiters(int key, Texture2D texture)
        {
            int baseIndex = key * MaxWaitersPerSlot;
            for (int i = 0; i < MaxWaitersPerSlot; i++)
            {
                eterpix_monitor waiter = _waitingMonitorsFlat[baseIndex + i];
                if (waiter != null)
                {
                    waiter.ApplyTexture(texture);
                    _waitingMonitorsFlat[baseIndex + i] = null;
                }
            }
        }
    }
}
