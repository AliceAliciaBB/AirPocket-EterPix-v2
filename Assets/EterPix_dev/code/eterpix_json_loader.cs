using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Data;

namespace ali.eterpix
{
    // 上位(loader)のJSON担当。方針.md「上位(loader)」「JSONデータ構造」「JSON再取得タイミング」
    // 「VRChat同期範囲」を参照。1ワールドに1つのみ配置(シングルトン、2つ目以降は自身を無効化)。
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_json_loader : UdonSharpBehaviour
    {
        public const string SingletonObjectName = "EterpixJsonLoader";

        [Header("デバッグログ (EterpixDebugという名前のGameObjectを検索して参照する)")]
        [SerializeField] private eterpix_debug debugLog;

        // json_URL。vrcurllist同様、中位(nav/ring)が[SerializeField]で保持し、
        // FetchLoaders()時にSetRequestUrl()で登録される(方針.md「vrcurllist参照」と同じ考え方)。
        private VRCUrl requestUrl;

        [Header("R18投稿(is_r18=true)をワールドに表示するか (Inspectorのみで変更可能)")]
        [SerializeField] private bool showR18Posts = false;

        [Header("定期自動再取得")]
        [SerializeField] private int intervalMinutes = 10;
        [SerializeField] private int offsetMinutes = 1;

        // 取得済みデータ(中位から参照される)
        [HideInInspector] public DataList jsonArray;
        [HideInInspector] public DataDictionary worldData;

        // 登録された中位(型ごとに別配列で保持。UdonSharpは共通interfaceでの多態が弱いため)
        private eterpix_middle_nav[] _middleNavs = new eterpix_middle_nav[0];
        private eterpix_middle_ring[] _middleRings = new eterpix_middle_ring[0];

        private bool _isRequestPending = false;
        private const float RequestTimeoutSeconds = 15f;

        private void Start()
        {
            // 2つ目以降の自己無効化(UdonSharpはstaticフィールド非対応のため、
            // GameObject.Findで先着オブジェクトを判定する)
            GameObject canonical = GameObject.Find(SingletonObjectName);
            if (canonical != null && canonical != this.gameObject)
            {
                Debug.LogWarning("[eterpix_json_loader] Duplicate instance detected. Disabling self.");
                gameObject.SetActive(false);
                return;
            }

            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<eterpix_debug>();
            }

            // URLは中位からSetRequestUrls()で登録されるまで未確定のため、
            // ここではRequestJson()を呼ばない(登録時に初回取得する)。
        }

        // ---- 中位からのjson_URL登録(最初に登録された1件のみ有効) ----
        public void SetRequestUrl(VRCUrl url)
        {
            if (requestUrl != null) return;

            requestUrl = url;

            RequestJson();
            ScheduleNextUpdate();
        }

        // ---- 中位からの登録 ----
        public void RegisterMiddle(eterpix_middle_nav middle)
        {
            int len = _middleNavs.Length;
            var arr = new eterpix_middle_nav[len + 1];
            for (int i = 0; i < len; i++) arr[i] = _middleNavs[i];
            arr[len] = middle;
            _middleNavs = arr;
        }

        public void RegisterMiddle(eterpix_middle_ring middle)
        {
            int len = _middleRings.Length;
            var arr = new eterpix_middle_ring[len + 1];
            for (int i = 0; i < len; i++) arr[i] = _middleRings[i];
            arr[len] = middle;
            _middleRings = arr;
        }

        // ---- 定期自動再取得(既存eterpix_update.cs相当。オーナーが全員に同期して指示する) ----
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
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(RequestJson));
            }

            ScheduleNextUpdate();
        }

        // ---- 中位からの手動更新依頼 ----
        public void RequestManualRefresh()
        {
            RequestJson();
        }

        // ---- JSON取得の実行(各クライアントがローカルで実行する。同期は「今取得しろ」の指示のみ) ----
        public void RequestJson()
        {
            if (_isRequestPending)
            {
                if (debugLog != null) debugLog.Log("[eterpix_json_loader] Request already pending, skip.");
                return;
            }

            _isRequestPending = true;
            if (debugLog != null) debugLog.Log("[eterpix_json_loader] Fetching JSON...");
            SendCustomEventDelayedSeconds(nameof(CheckRequestTimeout), RequestTimeoutSeconds);
            VRCStringDownloader.LoadUrl(requestUrl, (IUdonEventReceiver)this);
        }

        public void CheckRequestTimeout()
        {
            if (_isRequestPending)
            {
                if (debugLog != null) debugLog.LogWarning($"[eterpix_json_loader] No response after {RequestTimeoutSeconds}s (request may be hanging)");
            }
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            _isRequestPending = false;

            if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken token) ||
                token.TokenType != TokenType.DataDictionary)
            {
                if (debugLog != null) debugLog.LogError("[eterpix_json_loader] Failed to parse JSON or unexpected format");
                return;
            }

            DataDictionary rootDict = token.DataDictionary;

            if (!rootDict.TryGetValue("posts", out DataToken postToken) ||
                postToken.TokenType != TokenType.DataList)
            {
                if (debugLog != null) debugLog.LogError("[eterpix_json_loader] 'posts' key not found or not a list");
                return;
            }

            jsonArray = showR18Posts ? postToken.DataList : FilterOutR18Posts(postToken.DataList);

            if (rootDict.TryGetValue("world_data", out DataToken worldDataToken) &&
                worldDataToken.TokenType == TokenType.DataDictionary)
            {
                worldData = worldDataToken.DataDictionary;
            }
            else
            {
                worldData = null;
            }

            if (debugLog != null) debugLog.Log($"[eterpix_json_loader] jsonArray populated with {jsonArray.Count} posts");

            NotifyMiddles();
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            _isRequestPending = false;
            if (debugLog != null) debugLog.LogError($"[eterpix_json_loader] String load error: {result.Error}");
        }

        private void NotifyMiddles()
        {
            for (int i = 0; i < _middleNavs.Length; i++)
            {
                if (_middleNavs[i] != null) _middleNavs[i].OnJsonUpdated();
            }
            for (int i = 0; i < _middleRings.Length; i++)
            {
                if (_middleRings[i] != null) _middleRings[i].OnJsonUpdated();
            }
        }

        // is_r18=trueの投稿を除外した新しいリストを返す
        private DataList FilterOutR18Posts(DataList posts)
        {
            DataList filtered = new DataList();

            for (int i = 0; i < posts.Count; i++)
            {
                if (!posts.TryGetValue(i, out DataToken postToken)) continue;

                bool isR18 = false;
                if (postToken.TokenType == TokenType.DataDictionary &&
                    postToken.DataDictionary.TryGetValue("is_r18", out DataToken isR18Token) &&
                    isR18Token.TokenType == TokenType.Boolean)
                {
                    isR18 = isR18Token.Boolean;
                }

                if (!isR18) filtered.Add(postToken);
            }

            return filtered;
        }
    }
}
