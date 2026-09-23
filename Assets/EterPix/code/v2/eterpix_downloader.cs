using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Data;

namespace ali.eterpix.v2
{
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
    }
}
