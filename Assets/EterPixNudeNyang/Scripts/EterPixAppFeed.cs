using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace EterPixNudeNyang
{
    // EterPix-appの投稿データ。vrc-api v1 の GET /api/vrc/v1/public を1回読み、
    // 投稿を配列に展開する(契約: docs/contracts/vrc-api-v1-schema.md)。
    // R18(po18==1)はコード中に固定で除外する(設定項目にしない。ユーザー決定 2026-10-31)。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class EterPixAppFeed : UdonSharpBehaviour
    {
        public const int StatusLoading = 0;
        public const int StatusOk = 1;
        public const int StatusUntrusted = 2;
        public const int StatusServerError = 3;
        public const int StatusEmpty = 4;

        private const string UntrustedMarker = "Not trusted url hit";
        private const int BusyStatusCode = 423;
        private const float BusyRetryDelaySeconds = 5f;
        private const int BusyMaxRetries = 2;

        // ビルド時に焼く(VRCUrlは実行時に作れない)。https://eterpix.com/api/vrc/v1/public
        public VRCUrl feedUrl;
        // 完了時に "OnFeedChanged" を送る相手
        public UdonSharpBehaviour owner;

        [HideInInspector] public int Status = StatusLoading;
        [HideInInspector] public int Count;
        [HideInInspector] public string[] Poid = new string[0];
        [HideInInspector] public string[] Pode = new string[0];
        [HideInInspector] public string[] UserLabel = new string[0];
        [HideInInspector] public string[] WorldLabel = new string[0];
        [HideInInspector] public string[] WorldId = new string[0];
        [HideInInspector] public int[] Coid = new int[0];
        [HideInInspector] public int[] Copo = new int[0];
        [HideInInspector] public int[] Coro = new int[0];

        private bool _fetching;
        private int _busyRetry;

        public void Fetch()
        {
            if (_fetching) return;
            if (feedUrl == null || VRCUrl.IsNullOrEmpty(feedUrl))
            {
                Debug.Log("[EtpTrace] feed: feedUrl が未設定です");
                Finish(StatusServerError);
                return;
            }
            _fetching = true;
            Status = StatusLoading;
            Debug.Log("[EtpTrace] feed: fetch " + feedUrl.Get());
            VRCStringDownloader.LoadUrl(feedUrl, (IUdonEventReceiver)this);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            _fetching = false;
            _busyRetry = 0;
            Parse(result.Result);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            _fetching = false;
            Debug.Log("[EtpTrace] feed: load error code=" + result.ErrorCode + " error=" + result.Error);
            // サーバーがリングを更新中(423 Locked)なら、少し待って取り直す
            if (result.ErrorCode == BusyStatusCode && _busyRetry < BusyMaxRetries)
            {
                _busyRetry++;
                SendCustomEventDelayedSeconds(nameof(Fetch), BusyRetryDelaySeconds);
                return;
            }
            _busyRetry = 0;
            string error = result.Error;
            bool untrusted = !string.IsNullOrEmpty(error) && error.Contains(UntrustedMarker);
            Finish(untrusted ? StatusUntrusted : StatusServerError);
        }

        private void Parse(string json)
        {
            if (!VRCJson.TryDeserializeFromJson(json, out DataToken token) || token.TokenType != TokenType.DataDictionary)
            {
                Debug.Log("[EtpTrace] feed: JSONを読めませんでした");
                Finish(StatusServerError);
                return;
            }
            DataDictionary root = token.DataDictionary;
            if (!root.TryGetValue("post", out DataToken postToken) || postToken.TokenType != TokenType.DataList)
            {
                Debug.Log("[EtpTrace] feed: post キーがありません(v1形式のURLか確認)");
                Finish(StatusServerError);
                return;
            }
            DataList posts = postToken.DataList;
            DataDictionary titleTable = ReadDict(root, "usst");
            DataDictionary prefixTable = ReadDict(root, "wopt");
            DataDictionary worldData = ReadDict(root, "woda");

            // 1周目: 使う投稿(R18でない)を数える
            int usable = 0;
            for (int i = 0; i < posts.Count; i++)
            {
                if (IsUsable(posts, i)) usable++;
            }

            Poid = new string[usable];
            Pode = new string[usable];
            UserLabel = new string[usable];
            WorldLabel = new string[usable];
            WorldId = new string[usable];
            Coid = new int[usable];
            Copo = new int[usable];
            Coro = new int[usable];

            // 2周目: 展開する
            int n = 0;
            int skippedR18 = 0;
            for (int i = 0; i < posts.Count; i++)
            {
                if (!IsUsable(posts, i))
                {
                    if (IsR18(posts, i)) skippedR18++;
                    continue;
                }
                posts.TryGetValue(i, out DataToken postEntry);
                DataDictionary post = postEntry.DataDictionary;
                Poid[n] = ReadString(post, "poid", "");
                Pode[n] = ReadString(post, "pode", "");
                Coid[n] = ReadInt(post, "coid", -1);
                Copo[n] = ReadInt(post, "copo", 1);
                Coro[n] = ReadInt(post, "coro", 0);

                string userName = ReadString(post, "usna", "");
                string title = BuildMarkText(post, "ussf", titleTable);
                UserLabel[n] = string.IsNullOrEmpty(title) ? userName : userName + "[" + title + "]";

                string worldId = ReadString(post, "woid", "");
                WorldId[n] = worldId;
                string worldName = "";
                if (!string.IsNullOrEmpty(worldId) && worldData != null
                    && worldData.TryGetValue(worldId, out DataToken entryToken) && entryToken.TokenType == TokenType.DataDictionary)
                {
                    worldName = ReadString(entryToken.DataDictionary, "wona", "");
                }
                // 名前が引けないときは印も付けない(既存モニターと同じ)
                WorldLabel[n] = string.IsNullOrEmpty(worldName) ? "" : BuildMarkText(post, "wopf", prefixTable) + worldName;
                n++;
            }
            Count = n;
            Debug.Log("[EtpTrace] feed count=" + Count + " skippedR18=" + skippedR18 + " status=" + (Count > 0 ? "ok" : "empty"));
            Finish(Count > 0 ? StatusOk : StatusEmpty);
        }

        private void Finish(int status)
        {
            Status = status;
            if (owner != null) owner.SendCustomEvent("OnFeedChanged");
        }

        private bool IsUsable(DataList posts, int index)
        {
            if (!posts.TryGetValue(index, out DataToken t) || t.TokenType != TokenType.DataDictionary) return false;
            // 写真の位置が引けない投稿は使えない(coid が無い)
            if (ReadInt(t.DataDictionary, "coid", -1) < 0) return false;
            return !IsR18(posts, index);
        }

        private bool IsR18(DataList posts, int index)
        {
            if (!posts.TryGetValue(index, out DataToken t) || t.TokenType != TokenType.DataDictionary) return false;
            return ReadInt(t.DataDictionary, "po18", 0) == 1;
        }

        // 印・称号: IDの配列を対応表で文字列にして順に結合する(D-057・D-058)。未知のIDは読み飛ばす
        private string BuildMarkText(DataDictionary post, string listKey, DataDictionary table)
        {
            if (post == null || table == null) return "";
            if (!post.TryGetValue(listKey, out DataToken listToken) || listToken.TokenType != TokenType.DataList) return "";
            DataList ids = listToken.DataList;
            string text = "";
            for (int i = 0; i < ids.Count; i++)
            {
                if (!ids.TryGetValue(i, out DataToken idToken) || idToken.TokenType != TokenType.Double) continue;
                if (table.TryGetValue(((int)idToken.Double).ToString(), out DataToken textToken) && textToken.TokenType == TokenType.String)
                {
                    text += textToken.String;
                }
            }
            return text;
        }

        private DataDictionary ReadDict(DataDictionary data, string key)
        {
            if (data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.DataDictionary) return token.DataDictionary;
            return null;
        }

        private string ReadString(DataDictionary data, string key, string fallback)
        {
            if (data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.String) return token.String;
            return fallback;
        }

        private int ReadInt(DataDictionary data, string key, int fallback)
        {
            if (data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.Double) return (int)token.Double;
            return fallback;
        }
    }
}
