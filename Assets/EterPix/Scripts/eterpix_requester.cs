using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Data;

namespace ali.eterpix.v2
{
    // v2中位: リクエスター。リファクタリング案.md「リクエスター」を参照。
    // JSON URL 1本 + 画像URL表(256件)を持ち、ダウンローダにフィードとして登録する。
    // 子要素の複数モニターへ、表示対象の投稿(N件窓・R18フィルタ済み)を配る。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_requester : UdonSharpBehaviour
    {
        // JSON URLの選択肢(Editor/eterpix_requesterEditor.csのドロップダウンで選ぶ。実行時は未使用)。
        // 0=パブリック投稿, -1=自分で入力。値はIDとして保存されるため、項目を並べ替えても変えないこと
        [SerializeField] private int urlPreset = 0;

        [Header("JSON URL")]
        [SerializeField] private VRCUrl requestUrl;

        [Header("画像URL表 (256件、Editor/eterpix_url_sync.csで自動生成)")]
        [SerializeField] private ali.eterpix.vrcurllist urlList;

        [Header("扱う範囲: 新しい順に最大Nスロット")]
        [SerializeField] private int maxVisiblePosts = 60;

        [Header("R18投稿を表示するか(ゲーム内から変更不可)")]
        [SerializeField] private bool showR18Posts = false;

        [Header("デバッグログ")]
        [SerializeField] private ali.eterpix.eterpix_debug debugLog;

        private eterpix_downloader _downloader;
        private int _feedIndex = -1;
        public int FeedIndex => _feedIndex;

        private eterpix_monitor[] _monitors = new eterpix_monitor[0];

        // ローカルプレイヤーが写真を読み込む範囲(このGameObjectのトリガーCollider)の中にいるか
        private bool _isLocalPlayerInRange = false;
        public bool IsLocalPlayerInRange => _isLocalPlayerInRange;

        // ダウンローダのjsonArray(新しい順)のうち、表示対象インデックスだけを抽出した表
        private int[] _visibleIndices = new int[0];
        public int VisibleCount => _visibleIndices.Length;

        // ダウンローダのフィード状態(eterpix_downloader.FeedStatus*)。未登録なら読み込み中扱い
        public int FeedStatus => (_downloader != null && _feedIndex >= 0) ? _downloader.GetFeedStatus(_feedIndex) : eterpix_downloader.FeedStatusLoading;

        // 一度でも投稿配列を受け取ったか(取得に失敗しても前回のデータがあれば表示を続けるため)
        public bool HasEverSucceeded => _downloader != null && _feedIndex >= 0 && _downloader.GetFeedPosts(_feedIndex) != null;

        private void Start()
        {
            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(ali.eterpix.eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<ali.eterpix.eterpix_debug>();
            }

            // 子のモニターは非アクティブな場合もあるため、GetComponentsInChildren(true)で集める
            _monitors = GetComponentsInChildren<eterpix_monitor>(true);

            SendCustomEventDelayedFrames(nameof(FetchDownloader), 1);
        }

        public void FetchDownloader()
        {
            GameObject downloaderObj = GameObject.Find(eterpix_downloader.SingletonObjectName);
            if (downloaderObj == null)
            {
                if (debugLog != null) debugLog.LogError("[eterpix_requester] eterpix_downloader singleton not found");
                return;
            }

            _downloader = downloaderObj.GetComponent<eterpix_downloader>();
            _feedIndex = _downloader.RegisterFeed(requestUrl, this);
            if (urlList != null) _downloader.RegisterFeedImageUrls(_feedIndex, urlList);

            // 既に他のリクエスターによってこのフィードのJSONが取得済みの場合、
            // RegisterFeedの中では(_feedIndexがまだ代入されていないため)キャッチアップ通知を
            // 送れない。_feedIndex代入が完了したこの時点で改めて確認し、必要なら自分で呼ぶ。
            if (_downloader.GetFeedPosts(_feedIndex) != null || _downloader.GetFeedStatus(_feedIndex) != eterpix_downloader.FeedStatusLoading) OnJsonUpdated();

            for (int i = 0; i < _monitors.Length; i++)
            {
                if (_monitors[i] != null) _monitors[i].Init(this, _downloader, _feedIndex);
            }
        }

        // ---- ダウンローダからの更新通知 ----
        public void OnJsonUpdated()
        {
            RebuildVisibleIndices();
            for (int i = 0; i < _monitors.Length; i++)
            {
                if (_monitors[i] != null) _monitors[i].OnFeedUpdated();
            }
        }

        // ---- 表示範囲(このGameObjectのトリガーCollider)。配下の全モニターへ出入りを通知する ----
        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            SetLocalPlayerInRange(true);
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            SetLocalPlayerInRange(false);
        }

        // テレポート等でExitが届かないまま非アクティブになった場合の保険。
        // 配下のモニターも同時に非アクティブになり各自のOnDisableで画像を解放するため、通知はしない
        private void OnDisable()
        {
            _isLocalPlayerInRange = false;
        }

        private void SetLocalPlayerInRange(bool inRange)
        {
            if (_isLocalPlayerInRange == inRange) return;
            _isLocalPlayerInRange = inRange;

            for (int i = 0; i < _monitors.Length; i++)
            {
                if (_monitors[i] != null) _monitors[i].SetInViewRange(inRange);
            }
        }

        // ---- モニターからの手動更新依頼 ----
        public void ManualRefresh()
        {
            if (_downloader != null && _feedIndex >= 0) _downloader.RequestManualRefresh(_feedIndex);
        }

        private void RebuildVisibleIndices()
        {
            if (_downloader == null || _feedIndex < 0)
            {
                _visibleIndices = new int[0];
                return;
            }

            DataList posts = _downloader.GetFeedPosts(_feedIndex);
            if (posts == null)
            {
                _visibleIndices = new int[0];
                return;
            }

            // 一旦全件からR18を除外した番号リストを作り、先頭からmaxVisiblePosts件を採用する
            // (サーバーの/publicは新しい順なので、そのまま先頭=最新)
            int[] filtered = new int[posts.Count];
            int filteredCount = 0;
            for (int i = 0; i < posts.Count; i++)
            {
                if (!posts.TryGetValue(i, out DataToken postToken) || postToken.TokenType != TokenType.DataDictionary) continue;

                // v1のpo18は1/0の数値(VRCJsonではDouble)。1のときだけR18とみなす
                bool isR18 = false;
                if (postToken.DataDictionary.TryGetValue("po18", out DataToken isR18Token) && isR18Token.TokenType == TokenType.Double)
                {
                    isR18 = (int)isR18Token.Double == 1;
                }

                if (isR18 && !showR18Posts) continue;

                filtered[filteredCount] = i;
                filteredCount++;
            }

            int visibleCount = Mathf.Min(filteredCount, maxVisiblePosts);
            _visibleIndices = new int[visibleCount];
            for (int i = 0; i < visibleCount; i++) _visibleIndices[i] = filtered[i];
        }

        // ---- モニターからの投稿データ取得 ----
        public DataDictionary GetVisiblePost(int slotInWindow)
        {
            if (_downloader == null || slotInWindow < 0 || slotInWindow >= _visibleIndices.Length) return null;

            DataList posts = _downloader.GetFeedPosts(_feedIndex);
            if (posts == null) return null;

            int postIndex = _visibleIndices[slotInWindow];
            if (!posts.TryGetValue(postIndex, out DataToken token) || token.TokenType != TokenType.DataDictionary) return null;
            return token.DataDictionary;
        }

        public string ResolveWorldName(string worldId)
        {
            DataDictionary entry = ResolveWorldEntry(worldId);
            if (entry == null) return "";
            return ReadString(entry, "wona", "");
        }

        public string ResolveWorldDescription(string worldId)
        {
            DataDictionary entry = ResolveWorldEntry(worldId);
            if (entry == null) return "";
            return ReadString(entry, "wode", "");
        }

        private DataDictionary ResolveWorldEntry(string worldId)
        {
            if (_downloader == null || string.IsNullOrEmpty(worldId)) return null;

            DataDictionary worldData = _downloader.GetFeedWorldData(_feedIndex);
            if (worldData == null) return null;

            if (worldData.TryGetValue(worldId, out DataToken entryToken) && entryToken.TokenType == TokenType.DataDictionary)
            {
                return entryToken.DataDictionary;
            }
            return null;
        }

        private string ReadString(DataDictionary data, string key, string fallback)
        {
            if (data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.String) return token.String;
            return fallback;
        }
    }
}
