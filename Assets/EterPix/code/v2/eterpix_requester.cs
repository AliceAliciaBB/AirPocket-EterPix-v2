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

        // ダウンローダのjsonArray(新しい順)のうち、表示対象インデックスだけを抽出した表
        private int[] _visibleIndices = new int[0];
        public int VisibleCount => _visibleIndices.Length;

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

                bool isR18 = false;
                if (postToken.DataDictionary.TryGetValue("is_r18", out DataToken isR18Token) && isR18Token.TokenType == TokenType.Boolean)
                {
                    isR18 = isR18Token.Boolean;
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
            return ReadString(entry, "world_name", "");
        }

        public string ResolveWorldDescription(string worldId)
        {
            DataDictionary entry = ResolveWorldEntry(worldId);
            if (entry == null) return "";
            return ReadString(entry, "confirmed_description", "");
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
