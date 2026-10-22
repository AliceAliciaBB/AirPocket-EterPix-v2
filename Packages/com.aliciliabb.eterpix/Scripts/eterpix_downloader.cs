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

        // フィードの取得状態(モニターのエラー表示に使う)
        public const int FeedStatusLoading = 0;
        public const int FeedStatusOk = 1;
        public const int FeedStatusUntrustedUrl = 2;
        public const int FeedStatusServerError = 3;

        // VRChatの「信頼されていないURLを許可」がOFFのときの result.Error に含まれる文字列
        // (Tesca BOOTH_Poster と同じ判定)
        private const string UntrustedUrlErrorMarker = "Not trusted url hit";

        [Header("最大フィード数(固定サイズ配列の上限)")]
        [SerializeField] private int maxFeeds = 4;

        [Header("定期自動再取得(サーバーの10分枠に合わせる)")]
        [SerializeField] private int intervalMinutes = 10;
        [SerializeField] private int offsetMinutes = 1;

        [Header("デバッグログ")]
        [SerializeField] private ali.eterpix.eterpix_debug debugLog;

        private const float RequestTimeoutSeconds = 15f;

        // サーバーがリングを更新中のとき、JSONは 423 Locked + Retry-After: 5 で返る
        // (統合管理 docs/plans/ring-busy-status.md)。U#ではヘッダーを読めないため、待ち時間は固定にする
        private const int BusyStatusCode = 423;
        private const float BusyRetryDelaySeconds = 5f;
        private const int BusyMaxRetries = 2;
        private int[] _feedBusyRetryCount;   // フィードごと。今の取得で423の取り直しを何回したか
        private float[] _feedBusyRetryAt;    // フィードごと。取り直す時刻(待っていなければ-1)

        private string[] _feedKeys;      // VRCUrl.Get()の文字列(重複登録の判定キー)
        private VRCUrl[] _feedUrls;
        private DataList[] _feedPosts;       // 生データ(フィルタ前)
        private DataDictionary[] _feedWorldData;
        private bool[] _feedPending;
        private int[] _feedStatus;
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
        // 手動更新でフィードの画像を破棄したとき、そのフィードのダウンロードが進行中だった(結果を捨てる)
        private bool _discardCurrentDownload = false;

        // 破棄予約は「どのキーを見るか」をスケジューリング時の引数として渡せない(UdonSharpの
        // SendCustomEventDelayedSecondsは引数を運べず、常に同じメソッド名を呼ぶだけ)。
        // かつ複数キーの猶予期間が同時に進行しうるため、単一のスカラーで次に見るキーを覚える方式では
        // 「後から解放されたキーが先に解放されたキーの予約を上書きしてしまう」問題(リーク/二重判定)が起きる。
        // 対策として、TryDiscardSlotは呼ばれるたびに全スロットを走査し、各キー自身が持つ
        // 「このキーの直近の解放が何世代目か(_imgGraceGeneration)」と「このスケジュール呼び出しが
        // 待っていた世代(_imgDiscardTargetGeneration)」を突き合わせて判定する。スロット総数は
        // maxFeeds * ImageSlotsPerFeed(既定で高々1024程度)と小さいため、毎回全走査しても軽い。
        private int[] _imgDiscardTargetGeneration;

        // ---- Pad用要求者(eterpix_pad_requester)の優先取得(EtrPix-Pad K-12・K-61) ----
        // Padは入力されたURL 1本で、文字列取得ならJSON、画像取得なら写真を受け取る。
        // フィード・256スロットの管理には乗せず、専用のキューに積んで既存のフィード/画像より先に処理する
        // (VRChatへ投げる順番を先にするだけ。VRChat側のキューの順番までは制御できない)。
        // 相手はPadではなく要求者で、同じURLの重複は要求者がまとめるため、ここでは受けた順に積むだけ。
        // 受け取った写真は要求者へそのまま渡し、破棄も要求者側で行う(_loadedCountにも数えない)
        private eterpix_pad_requester[] _padStringReceivers = new eterpix_pad_requester[0];
        private VRCUrl[] _padStringUrls = new VRCUrl[0];
        private int _padStringCount = 0;
        private eterpix_pad_requester _pendingPadReceiver; // 応答待ちの文字列取得がPad用のものなら、その要求者

        private eterpix_pad_requester[] _padImageReceivers = new eterpix_pad_requester[0];
        private VRCUrl[] _padImageUrls = new VRCUrl[0];
        private int _padImageCount = 0;
        private eterpix_pad_requester _currentPadImageReceiver; // 進行中の画像取得がPad用のものなら、その要求者

        // ---- 調査用トレース(eterpix_debug.TraceEnabled) ----
        // 写真が白くなる/古い写真が残る不具合の切り分け用。JSONを取得し直すたびにフィードの世代(gen)を進め、
        // 各スロットの画像が「どの世代のJSONの時に取得したか」「スロットに載っている投稿が世代間で変わったか」を記録する
        private int[] _feedJsonGen;          // フィードごと。JSON取得に成功するたびに+1
        private string[][] _feedSlotSig;     // [feed][slot] そのスロットに載っている投稿(copo:poid)の並び。前回と比べて画像を取り直す(InvalidateChangedSlots。トレース以外でも使う)
        private int[] _imgLoadGen;           // キーごと。画像のダウンロードを始めた時点のフィードの世代
        private int[] _imgSerial;            // キーごと。何番目に届いた画像か(同じキーの画像が入れ替わったかを見分ける)
        private string[] _imgLoadedAt;       // キーごと。画像が届いた時刻
        // キーごと。持っている(または取得中の)画像より後のJSONで、そのスロットの投稿の並びが変わった
        // (=サーバー側の画像が作り直されているはずなのに古い画像を使っている)
        private bool[] _imgStaleContent;
        private int _imgSerialCounter = 0;
        private int _currentDownloadGen = 0;

        private void Trace(string message)
        {
            // TraceEnabled(const)がfalseの時の「到達不能コード」警告を抑える
#pragma warning disable CS0162
            if (ali.eterpix.eterpix_debug.TraceEnabled) Debug.Log(ali.eterpix.eterpix_debug.TracePrefix + "t=" + Time.time.ToString("F2") + " " + System.DateTime.Now.ToString("HH:mm:ss") + " DL " + message);
#pragma warning restore CS0162
        }

        private string StateName(int key)
        {
            eterpix_v2_image_state state = _imgState[key];
            if (state == eterpix_v2_image_state.Unrequested) return "Unrequested";
            if (state == eterpix_v2_image_state.Queued) return "Queued";
            if (state == eterpix_v2_image_state.Downloading) return "Downloading";
            return "Loaded";
        }

        private string KeyText(int key)
        {
            return "key=" + key + "(feed=" + (key / ImageSlotsPerFeed) + ",slot=" + (key % ImageSlotsPerFeed) + ")";
        }

        // モニターのトレースから参照する
        public int GetFeedGeneration(int feedIndex)
        {
            if (_feedJsonGen == null || feedIndex < 0 || feedIndex >= maxFeeds) return -1;
            return _feedJsonGen[feedIndex];
        }

        public int GetTextureGeneration(int feedIndex, int slot)
        {
            if (_imgLoadGen == null || feedIndex < 0 || feedIndex >= maxFeeds || slot < 0 || slot >= ImageSlotsPerFeed) return -1;
            return _imgLoadGen[ToKey(feedIndex, slot)];
        }

        public bool IsTextureStale(int feedIndex, int slot)
        {
            if (_imgStaleContent == null || feedIndex < 0 || feedIndex >= maxFeeds || slot < 0 || slot >= ImageSlotsPerFeed) return false;
            return _imgStaleContent[ToKey(feedIndex, slot)];
        }

        public int GetTextureSerial(int feedIndex, int slot)
        {
            if (_imgSerial == null || feedIndex < 0 || feedIndex >= maxFeeds || slot < 0 || slot >= ImageSlotsPerFeed) return -1;
            return _imgSerial[ToKey(feedIndex, slot)];
        }

        public string GetSlotTraceText(int feedIndex, int slot)
        {
            if (_imgState == null || feedIndex < 0 || feedIndex >= maxFeeds || slot < 0 || slot >= ImageSlotsPerFeed) return "slot=invalid";
            int key = ToKey(feedIndex, slot);
            return KeyText(key) + " state=" + StateName(key) + " ref=" + _imgRefCount[key] + " serial=" + _imgSerial[key] +
                " texGen=" + _imgLoadGen[key] + " feedGen=" + _feedJsonGen[feedIndex] + " stale=" + _imgStaleContent[key] + " texNull=" + (_imgTexture[key] == null);
        }

        // ---- 投稿の並びが変わったスロットの画像の取り直し ----
        // 画像URL(/00〜/FF)はスロットごとに固定で、サーバーは新しい投稿を空いている位置(copo)に足して
        // そのスロットの画像を作り直す。JSONだけ取り直して画像を使い回すと、新しい投稿が古い画像の
        // 空いている位置(白)で表示される。JSONを取得するたびに、スロットごとの投稿の並び(copo:poid)を
        // 前回と比べ、変わったスロットの画像を無効にする(モニターへの通知の前に呼ぶ)。
        //   - 取得済み: キャッシュから外す。テクスチャはモニターが通知で手放した後に破棄する(DestroyInvalidatedTextures)
        //   - 取得中: 届いた画像を捨てて、見ている人がいれば取り直す(手動更新と同じ仕組み)
        private Texture2D[] _invalidatedTextures = new Texture2D[ImageSlotsPerFeed];
        private int _invalidatedTextureCount = 0;

        private void InvalidateChangedSlots(int feedIndex)
        {
            DataList posts = _feedPosts[feedIndex];
            string[] sig = new string[ImageSlotsPerFeed];
            string mapping = "";
            for (int i = 0; i < posts.Count; i++)
            {
                if (!posts.TryGetValue(i, out DataToken postToken) || postToken.TokenType != TokenType.DataDictionary) continue;
                DataDictionary post = postToken.DataDictionary;
                int coid = ReadPostInt(post, "coid");
                int copo = ReadPostInt(post, "copo");
                string poid = ReadPostString(post, "poid");
                mapping += " " + i + ":" + coid + "/" + copo + "/" + poid;
                if (coid >= 0 && coid < ImageSlotsPerFeed) sig[coid] = sig[coid] + copo + ":" + poid + ",";
            }
            Trace("json ok feed=" + feedIndex + " gen=" + _feedJsonGen[feedIndex] + " posts=" + posts.Count + " map[i:coid/copo/poid]=" + mapping);

            string[] prevSig = _feedSlotSig[feedIndex];
            int start = feedIndex * ImageSlotsPerFeed;
            for (int slot = 0; slot < ImageSlotsPerFeed; slot++)
            {
                int key = start + slot;
                string before = prevSig != null ? prevSig[slot] : null;
                bool changed = prevSig != null && before != sig[slot];
                if (!changed)
                {
                    if (_imgState[key] == eterpix_v2_image_state.Loaded || _imgState[key] == eterpix_v2_image_state.Downloading)
                    {
                        Trace("cache-kept " + KeyText(key) + " state=" + StateName(key) + " ref=" + _imgRefCount[key] + " serial=" + _imgSerial[key] + " texGen=" + _imgLoadGen[key] + " sig=[" + sig[slot] + "]");
                    }
                    continue;
                }

                Trace("slot-changed " + KeyText(key) + " state=" + StateName(key) + " ref=" + _imgRefCount[key] +
                    " texGen=" + _imgLoadGen[key] + " before=[" + before + "] after=[" + sig[slot] + "]");

                if (_imgState[key] == eterpix_v2_image_state.Loaded)
                {
                    if (_imgTexture[key] != null)
                    {
                        _invalidatedTextures[_invalidatedTextureCount] = _imgTexture[key];
                        _invalidatedTextureCount++;
                    }
                    _imgTexture[key] = null;
                    _imgState[key] = eterpix_v2_image_state.Unrequested;
                    _imgPriority[key] = 0;
                    _loadedCount = Mathf.Max(0, _loadedCount - 1);
                    Trace("invalidate loaded " + KeyText(key) + " serial=" + _imgSerial[key]);
                }
                else if (_imgState[key] == eterpix_v2_image_state.Downloading)
                {
                    // 取得開始時点のJSONより後で並びが変わったので、届く画像も古い
                    _discardCurrentDownload = true;
                    _imgStaleContent[key] = true;
                    Trace("invalidate downloading " + KeyText(key));
                }
            }
            _feedSlotSig[feedIndex] = sig;
        }

        // 通知でモニターがRawImageから外した後に、無効にした古いテクスチャを破棄する
        private void DestroyInvalidatedTextures()
        {
            for (int i = 0; i < _invalidatedTextureCount; i++)
            {
                if (_invalidatedTextures[i] != null) Destroy(_invalidatedTextures[i]);
                _invalidatedTextures[i] = null;
            }
            if (_invalidatedTextureCount > 0) Trace("destroyed invalidated textures count=" + _invalidatedTextureCount);
            _invalidatedTextureCount = 0;
        }

        private int ReadPostInt(DataDictionary data, string key)
        {
            if (!data.TryGetValue(key, out DataToken token)) return -1;
            if (token.TokenType == TokenType.Double) return (int)token.Double;
            if (token.TokenType == TokenType.String && int.TryParse(token.String, out int parsed)) return parsed;
            return -1;
        }

        private string ReadPostString(DataDictionary data, string key)
        {
            if (data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.String) return token.String;
            return "";
        }

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
            _feedStatus = new int[maxFeeds];
            _feedRequesters = new eterpix_requester[maxFeeds][];
            _feedRequesterCounts = new int[maxFeeds];
            _fetchQueue = new int[maxFeeds];
            _feedBusyRetryCount = new int[maxFeeds];
            _feedBusyRetryAt = new float[maxFeeds];
            for (int i = 0; i < maxFeeds; i++)
            {
                _feedRequesters[i] = new eterpix_requester[16];
                _feedBusyRetryAt[i] = -1f;
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

            _feedJsonGen = new int[maxFeeds];
            _feedSlotSig = new string[maxFeeds][];
            _imgLoadGen = new int[totalSlots];
            _imgSerial = new int[totalSlots];
            _imgLoadedAt = new string[totalSlots];
            _imgStaleContent = new bool[totalSlots];
            Trace("start maxFeeds=" + maxFeeds + " maxLoadedTextures=" + maxLoadedTextures + " releaseGraceSeconds=" + releaseGraceSeconds);

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

            // 既にJSONが届いているフィードへの後発登録時のキャッチアップ通知は、
            // ここでは行わない(このメソッドはRegisterFeed内、つまり呼び出し元の
            // eterpix_requester.FetchDownloaderが_feedIndexへの代入を完了する「前」に
            // 実行される。この時点でrequester.OnJsonUpdated()を呼ぶとRebuildVisibleIndices
            // が古い/未初期化の_feedIndexを参照し、表示件数0のまま次回定期更新まで
            // 復旧しない: レビュー指摘の重大バグ)。
            // 代わりに、呼び出し元のFetchDownloaderが_feedIndex代入完了後に
            // 自分自身でGetFeedPosts()を確認してOnJsonUpdated()を呼ぶ方式に変更した。
        }

        // ---- 定期自動更新(オーナーが「今取得しろ」の指示のみ全員に同期) ----
        // 予約した呼び出しは数百ms〜1秒ほど早く届くことがある。今の時刻のすぐ後の枠で予約し直すと
        // 同じ枠で2回取得してしまうため、今からScheduleSlackSeconds秒より後の最初の枠を選ぶ
        private const double ScheduleSlackSeconds = 30.0;

        private void ScheduleNextUpdate()
        {
            System.DateTime now = System.DateTime.Now;
            System.DateTime earliest = now.AddSeconds(ScheduleSlackSeconds);
            System.DateTime candidate = new System.DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            while (candidate <= earliest ||((candidate.Minute - offsetMinutes) % intervalMinutes + intervalMinutes) % intervalMinutes != 0)
            {
                candidate = candidate.AddMinutes(1);
            }

            float delay = (float)(candidate - now).TotalSeconds;
            SendCustomEventDelayedSeconds(nameof(TriggerScheduledUpdate), delay);
        }

        public void TriggerScheduledUpdate()
        {
            bool isOwner = Networking.IsOwner(Networking.LocalPlayer, this.gameObject);
            Trace("scheduled-update fire isOwner=" + isOwner);
            if (isOwner)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(RequestAllFeeds));
            }
            ScheduleNextUpdate();
        }

        public void RequestAllFeeds()
        {
            Trace("RequestAllFeeds (network) feedCount=" + _feedCount);
            for (int i = 0; i < _feedCount; i++) RequestJson(i);
        }

        // ---- リクエスターからの手動更新(ローカルのみ。そのフィードのJSONと画像を破棄してから取り直す) ----
        // モニターのページ位置(_syncedPageIndex)は破棄しないため、取得後は各モニターが元のindexで表示し直す。
        public void RequestManualRefresh(int feedIndex)
        {
            if (feedIndex < 0 || feedIndex >= _feedCount) return;
            // 取得中(応答待ち)なら、その結果で表示し直されるので何もしない
            if (_feedPending[feedIndex])
            {
                Trace("manual-refresh ignored (pending) feed=" + feedIndex);
                return;
            }
            Trace("manual-refresh feed=" + feedIndex);

            _feedPosts[feedIndex] = null;
            _feedWorldData[feedIndex] = null;
            _feedStatus[feedIndex] = FeedStatusLoading;
            // 先にモニターを読み込み中表示にして、RawImageからテクスチャを外させ参照を解放させる
            NotifyFeedRequesters(feedIndex);
            DiscardFeedTextures(feedIndex);

            RequestJson(feedIndex);
        }

        private void DiscardFeedTextures(int feedIndex)
        {
            int start = feedIndex * ImageSlotsPerFeed;
            for (int key = start; key < start + ImageSlotsPerFeed; key++)
            {
                if (_imgState[key] == eterpix_v2_image_state.Loaded)
                {
                    DiscardSlot(key, "manual-refresh");
                }
                else if (_imgState[key] == eterpix_v2_image_state.Downloading)
                {
                    Trace("manual-refresh mark discard-current-download " + KeyText(key));
                    // 破棄前に始めたダウンロードの結果は使わず、届いたら捨てて取り直す
                    _discardCurrentDownload = true;
                }
            }
        }

        // ---- JSON取得の実行(各クライアントがローカルで実行。OnStringLoadSuccess/Errorが
        // どのフィードの応答か判別できないため、実際にLoadUrlするのは常に1件だけになるよう
        // _fetchQueueで直列化する。短時間の重複要求は_feedPendingで弾く) ----
        private void RequestJson(int feedIndex)
        {
            if (feedIndex < 0 || feedIndex >= _feedCount) return;
            if (_feedPending[feedIndex])
            {
                Trace("RequestJson skipped (pending) feed=" + feedIndex);
                return;
            }

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

            // Padの要求を先に処理する(K-12)
            if (_padStringCount > 0)
            {
                eterpix_pad_requester receiver = _padStringReceivers[0];
                VRCUrl padUrl = _padStringUrls[0];
                RemovePadEntryAt(_padStringReceivers, _padStringUrls, 0, _padStringCount);
                _padStringCount--;

                _isFetching = true;
                _pendingFeedIndex = -1;
                _pendingPadReceiver = receiver;
                if (debugLog != null) debugLog.Log($"[eterpix_downloader] Fetching pad JSON: {padUrl}");
                Trace("fetch pad json url=" + padUrl);
                VRCStringDownloader.LoadUrl(padUrl, (IUdonEventReceiver)this);
                return;
            }

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
            Trace("fetch json feed=" + feedIndex + " url=" + _feedUrls[feedIndex]);
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
                Trace("json timeout(no response yet) feed=" + feedIndex);
            }
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (_pendingPadReceiver != null)
            {
                eterpix_pad_requester receiver = _pendingPadReceiver;
                _pendingPadReceiver = null;
                _isFetching = false;
                receiver.OnTransportStringSuccess(result.Url.Get(), result.Result);
                StartNextFetchIfIdle();
                return;
            }

            int feedIndex = _pendingFeedIndex;
            _isFetching = false;
            if (feedIndex < 0 || feedIndex >= _feedCount)
            {
                Trace("json ok but no pending feed (ignored) pendingFeedIndex=" + feedIndex);
                StartNextFetchIfIdle();
                return;
            }
            _feedPending[feedIndex] = false;
            _feedBusyRetryCount[feedIndex] = 0;

            if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken token) ||
                token.TokenType != TokenType.DataDictionary)
            {
                if (debugLog != null) debugLog.LogError($"[eterpix_downloader] Failed to parse JSON for feed {feedIndex}");
                Trace("json parse error feed=" + feedIndex);
                _feedStatus[feedIndex] = FeedStatusServerError;
                NotifyFeedRequesters(feedIndex);
                StartNextFetchIfIdle();
                return;
            }

            // v1形式(/api/vrc/v1/...)。投稿配列は"post"、ワールド情報は"woda"。
            // 知らないkeyは読まずに無視する(v1はkey追加のみ行われるため)。
            DataDictionary rootDict = token.DataDictionary;

            if (rootDict.TryGetValue("post", out DataToken postToken) && postToken.TokenType == TokenType.DataList)
            {
                _feedPosts[feedIndex] = postToken.DataList;
            }
            else
            {
                Trace("json no post key feed=" + feedIndex);
                if (debugLog != null) debugLog.LogError($"[eterpix_downloader] 'post' key not found for feed {feedIndex} (v1形式のURL /api/vrc/v1/... か確認)");
                _feedStatus[feedIndex] = FeedStatusServerError;
                NotifyFeedRequesters(feedIndex);
                StartNextFetchIfIdle();
                return;
            }

            if (rootDict.TryGetValue("woda", out DataToken worldDataToken) && worldDataToken.TokenType == TokenType.DataDictionary)
            {
                _feedWorldData[feedIndex] = worldDataToken.DataDictionary;
            }
            else
            {
                _feedWorldData[feedIndex] = null;
            }

            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Feed {feedIndex}: {_feedPosts[feedIndex].Count} posts");
            _feedJsonGen[feedIndex]++;
            InvalidateChangedSlots(feedIndex);

            _feedStatus[feedIndex] = FeedStatusOk;
            NotifyFeedRequesters(feedIndex);
            DestroyInvalidatedTextures();
            StartNextFetchIfIdle();
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (_pendingPadReceiver != null)
            {
                eterpix_pad_requester receiver = _pendingPadReceiver;
                _pendingPadReceiver = null;
                _isFetching = false;
                if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] Pad string load error: {result.Error}");
                receiver.OnTransportStringError(result.Url.Get(), result.ErrorCode, result.Error);
                StartNextFetchIfIdle();
                return;
            }

            int feedIndex = _pendingFeedIndex;
            _isFetching = false;
            if (feedIndex < 0 || feedIndex >= _feedCount)
            {
                StartNextFetchIfIdle();
                return;
            }
            Trace("json load error feed=" + feedIndex + " code=" + result.ErrorCode + " error=" + result.Error + " busyRetry=" + _feedBusyRetryCount[feedIndex]);

            // サーバーがリングを更新中(423 Locked)なら、取得中のまま少し待って取り直す。
            // 待っている間は_feedPendingを立てたままにして、自動更新・再読み込みの要求をこの取り直しにまとめる
            if (result.ErrorCode == BusyStatusCode && _feedBusyRetryCount[feedIndex] < BusyMaxRetries)
            {
                _feedBusyRetryCount[feedIndex]++;
                _feedBusyRetryAt[feedIndex] = Time.time + BusyRetryDelaySeconds;
                Trace("json busy(423) feed=" + feedIndex + " retry " + _feedBusyRetryCount[feedIndex] + "/" + BusyMaxRetries + " in " + BusyRetryDelaySeconds + "s");
                SendCustomEventDelayedSeconds(nameof(RetryBusyFeeds), BusyRetryDelaySeconds);
                StartNextFetchIfIdle();
                return;
            }
            _feedBusyRetryCount[feedIndex] = 0;
            _feedPending[feedIndex] = false;

            string error = result.Error;
            bool isUntrusted = !string.IsNullOrEmpty(error) && error.Contains(UntrustedUrlErrorMarker);
            _feedStatus[feedIndex] = isUntrusted ? FeedStatusUntrustedUrl : FeedStatusServerError;
            if (debugLog != null) debugLog.LogError($"[eterpix_downloader] String load error for feed {feedIndex}: {error}");

            // 一度でも成功していれば、requester/モニター側は前回のデータを表示し続ける
            NotifyFeedRequesters(feedIndex);
            StartNextFetchIfIdle();
        }

        // 423の取り直し。引数を運べないため、待っているフィードを全部見て、時刻が来たものをキューへ戻す。
        // 予約した呼び出しが Time.time 上わずかに早く届くことがある(TryDiscardSlotと同じ)ため、
        // 残っているものがあれば残り時間後に予約し直す
        public void RetryBusyFeeds()
        {
            float soonestRemaining = -1f;
            for (int i = 0; i < _feedCount; i++)
            {
                if (_feedBusyRetryAt[i] < 0f) continue;
                float remaining = _feedBusyRetryAt[i] - Time.time;
                if (remaining <= 0f)
                {
                    _feedBusyRetryAt[i] = -1f;
                    Trace("json busy retry fetch feed=" + i);
                    EnqueueFetch(i);
                }
                else if (soonestRemaining < 0f || remaining < soonestRemaining)
                {
                    soonestRemaining = remaining;
                }
            }
            if (soonestRemaining >= 0f) SendCustomEventDelayedSeconds(nameof(RetryBusyFeeds), soonestRemaining + 0.1f);
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

        // ---- Pad用要求者からの優先取得の受付(K-12・K-61) ----
        // 結果は要求者のOnTransportStringSuccess/OnTransportStringErrorで、どのURLの結果かと一緒に返す。
        // 同じURLの重複は要求者がまとめるため、ここでは受けた順に積むだけ
        public void RequestPadString(VRCUrl url, eterpix_pad_requester receiver)
        {
            if (url == null || receiver == null) return;

            if (_padStringCount >= _padStringReceivers.Length)
            {
                _padStringReceivers = GrowReceiverArray(_padStringReceivers);
                _padStringUrls = GrowUrlArray(_padStringUrls);
            }
            _padStringReceivers[_padStringCount] = receiver;
            _padStringUrls[_padStringCount] = url;
            _padStringCount++;
            StartNextFetchIfIdle();
        }

        // 受け取った写真はOnTransportImageSuccessで要求者へ渡す。破棄は要求者側で行う
        public void RequestPadImage(VRCUrl url, eterpix_pad_requester receiver)
        {
            if (url == null || receiver == null) return;

            if (_padImageCount >= _padImageReceivers.Length)
            {
                _padImageReceivers = GrowReceiverArray(_padImageReceivers);
                _padImageUrls = GrowUrlArray(_padImageUrls);
            }
            _padImageReceivers[_padImageCount] = receiver;
            _padImageUrls[_padImageCount] = url;
            _padImageCount++;
            StartNextImageDownloadIfIdle();
        }

        // まだVRChatへ投げていない要求だけをURLで取り消す。取り消せたらtrue
        // (falseなら投げ済みで、結果はあとで要求者に届く)
        public bool CancelPadString(string url)
        {
            int index = FindPadEntryByUrl(_padStringUrls, _padStringCount, url);
            if (index < 0) return false;
            RemovePadEntryAt(_padStringReceivers, _padStringUrls, index, _padStringCount);
            _padStringCount--;
            return true;
        }

        public bool CancelPadImage(string url)
        {
            int index = FindPadEntryByUrl(_padImageUrls, _padImageCount, url);
            if (index < 0) return false;
            RemovePadEntryAt(_padImageReceivers, _padImageUrls, index, _padImageCount);
            _padImageCount--;
            return true;
        }

        private int FindPadEntryByUrl(VRCUrl[] urls, int count, string url)
        {
            for (int i = 0; i < count; i++)
            {
                if (urls[i] != null && urls[i].Get() == url) return i;
            }
            return -1;
        }

        // index番目を詰めて消す(件数の更新は呼び出し側で行う)
        private void RemovePadEntryAt(eterpix_pad_requester[] receivers, VRCUrl[] urls, int index, int count)
        {
            for (int i = index + 1; i < count; i++)
            {
                receivers[i - 1] = receivers[i];
                urls[i - 1] = urls[i];
            }
            receivers[count - 1] = null;
            urls[count - 1] = null;
        }

        private eterpix_pad_requester[] GrowReceiverArray(eterpix_pad_requester[] arr)
        {
            eterpix_pad_requester[] bigger = new eterpix_pad_requester[arr.Length + 8];
            for (int i = 0; i < arr.Length; i++) bigger[i] = arr[i];
            return bigger;
        }

        private VRCUrl[] GrowUrlArray(VRCUrl[] arr)
        {
            VRCUrl[] bigger = new VRCUrl[arr.Length + 8];
            for (int i = 0; i < arr.Length; i++) bigger[i] = arr[i];
            return bigger;
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

        public int GetFeedStatus(int feedIndex)
        {
            if (feedIndex < 0 || feedIndex >= _feedCount) return FeedStatusLoading;
            return _feedStatus[feedIndex];
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

            int feedGen = _feedJsonGen[feedIndex];
            bool isLoaded = _imgState[key] == eterpix_v2_image_state.Loaded;
            Trace("RequestTexture " + KeyText(key) + " by=" + caller.TraceName + " state=" + StateName(key) + " ref=" + _imgRefCount[key] +
                (isLoaded ? " CACHE-HIT serial=" + _imgSerial[key] + " texGen=" + _imgLoadGen[key] + " feedGen=" + feedGen + " loadedAt=" + _imgLoadedAt[key] +
                    " texNull=" + (_imgTexture[key] == null) + (_imgStaleContent[key] ? " STALE-CONTENT" : "") : ""));

            if (_imgState[key] == eterpix_v2_image_state.Loaded)
            {
                caller.ApplyTexture(feedIndex, slot, _imgTexture[key]);
                return;
            }

            AddWaiter(key, caller);
            EnsureQueued(key);
        }

        public void ReleaseTexture(int feedIndex, int slot, eterpix_monitor caller)
        {
            if (!ValidateSlot(feedIndex, slot)) return;
            int key = ToKey(feedIndex, slot);

            if (_imgRefCount[key] <= 0) Trace("REFCOUNT-UNDERFLOW ReleaseTexture " + KeyText(key) + " by=" + caller.TraceName);
            _imgRefCount[key] = Mathf.Max(0, _imgRefCount[key] - 1);
            Trace("ReleaseTexture " + KeyText(key) + " by=" + caller.TraceName + " state=" + StateName(key) + " ref=" + _imgRefCount[key]);
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
        //
        // 予約した呼び出しが Time.time 上わずかに早く届き、経過時間条件をぎりぎり満たさないことがある
        // (ClientSimで確認)。その場合に後続の解放が無いと、二度とスキャンされずLoadedのまま残るため、
        // 猶予中のキーが残っていれば、最も早く期限が来るキーの残り時間後に再スキャンを予約する。
        public void TryDiscardSlot()
        {
            float soonestRemaining = -1f;
            for (int key = 0; key < _imgState.Length; key++)
            {
                if (_imgRefCount[key] != 0 ||
                    _imgState[key] != eterpix_v2_image_state.Loaded ||
                    _imgGraceGeneration[key] != _imgDiscardTargetGeneration[key]) continue;

                float remaining = releaseGraceSeconds - (Time.time - _imgReleaseTime[key]);
                if (remaining <= 0f)
                {
                    DiscardSlot(key, "grace");
                }
                else if (soonestRemaining < 0f || remaining < soonestRemaining)
                {
                    soonestRemaining = remaining;
                }
            }

            if (soonestRemaining >= 0f)
            {
                SendCustomEventDelayedSeconds(nameof(TryDiscardSlot), soonestRemaining + 0.1f);
            }
        }

        private void DiscardSlot(int key, string reason)
        {
            if (_imgState[key] != eterpix_v2_image_state.Loaded) return;
            Trace((_imgRefCount[key] > 0 ? "DESTROY-WHILE-REFERENCED " : "discard ") + KeyText(key) + " reason=" + reason + " ref=" + _imgRefCount[key] +
                " serial=" + _imgSerial[key] + " texGen=" + _imgLoadGen[key] + " loadedCount(before)=" + _loadedCount);
            // 参照を捨てるだけではVRCImageDownloaderが作ったテクスチャはメモリに残るため、明示的に破棄する。
            // 参照0のスロットだけが破棄対象で、モニターは解放時にRawImageからテクスチャを外している
            if (_imgTexture[key] != null) Destroy(_imgTexture[key]);
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
            Trace("WAITER-LIST-FULL " + KeyText(key) + " by=" + caller.TraceName);
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
                DiscardSlot(worstKey, "evict");
            }
        }

        private void StartNextImageDownloadIfIdle()
        {
            if (_isImageDownloading) return;

            // Padの要求を先に処理する(K-12)
            if (_padImageCount > 0)
            {
                eterpix_pad_requester receiver = _padImageReceivers[0];
                VRCUrl padUrl = _padImageUrls[0];
                RemovePadEntryAt(_padImageReceivers, _padImageUrls, 0, _padImageCount);
                _padImageCount--;

                if (_imageDownloader == null) _imageDownloader = new VRC.SDK3.Image.VRCImageDownloader();
                _isImageDownloading = true;
                _currentDownloadKey = -1;
                _currentPadImageReceiver = receiver;
                if (debugLog != null) debugLog.Log($"[eterpix_downloader] Downloading pad image: {padUrl}");
                Trace("download pad image url=" + padUrl);
                _imageDownloader.DownloadImage(padUrl, null, (IUdonEventReceiver)this, null);
                return;
            }

            // Start()より先にPadから呼ばれた場合、スロット管理の配列がまだ無い
            if (_imgState == null) return;

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
                Trace("URL-MISSING " + KeyText(best));
                _imgState[best] = eterpix_v2_image_state.Unrequested;
                StartNextImageDownloadIfIdle();
                return;
            }

            _imgState[best] = eterpix_v2_image_state.Downloading;
            _currentDownloadKey = best;
            _isImageDownloading = true;
            _discardCurrentDownload = false;
            _currentDownloadGen = _feedJsonGen[feedIndex];

            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Downloading feed={feedIndex} slot={slot}: {url}");
            Trace("download start " + KeyText(best) + " ref=" + _imgRefCount[best] + " priority=" + bestPriority + " feedGen=" + _currentDownloadGen + " url=" + url);
            _imageDownloader.DownloadImage(url, null, (IUdonEventReceiver)this, null);
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            if (_currentPadImageReceiver != null)
            {
                eterpix_pad_requester receiver = _currentPadImageReceiver;
                _currentPadImageReceiver = null;
                _isImageDownloading = false;
                Trace("pad image ok url=" + result.Url.Get() + " texNull=" + (result.Result == null));
                receiver.OnTransportImageSuccess(result.Url.Get(), result.Result);
                StartNextImageDownloadIfIdle();
                return;
            }

            int key = _currentDownloadKey;
            _isImageDownloading = false;
            if (key < 0 || key >= _imgState.Length)
            {
                Trace("image ok but no current key (ignored) key=" + key + " url=" + result.Url.Get());
                StartNextImageDownloadIfIdle();
                return;
            }

            if (_discardCurrentDownload)
            {
                Trace("image ok but discarded (manual-refresh) " + KeyText(key) + " ref=" + _imgRefCount[key]);
                // 手動更新より前に始めたダウンロード。古い画像の可能性があるため捨て、見ている人がいれば取り直す
                _discardCurrentDownload = false;
                if (result.Result != null) Destroy(result.Result);
                _imgState[key] = _imgRefCount[key] > 0 ? eterpix_v2_image_state.Queued : eterpix_v2_image_state.Unrequested;
                StartNextImageDownloadIfIdle();
                return;
            }

            if (_imgState[key] == eterpix_v2_image_state.Loaded) Trace("OVERWRITE-LOADED " + KeyText(key) + " oldSerial=" + _imgSerial[key]);
            _imgTexture[key] = result.Result;
            _imgState[key] = eterpix_v2_image_state.Loaded;
            _loadedCount++;
            _imgSerialCounter++;
            _imgSerial[key] = _imgSerialCounter;
            _imgLoadGen[key] = _currentDownloadGen;
            _imgLoadedAt[key] = System.DateTime.Now.ToString("HH:mm:ss");
            // 取得開始より後のJSONで並びが変わっていたら、届いた画像もまだ古い
            _imgStaleContent[key] = _imgStaleContent[key] && _currentDownloadGen < _feedJsonGen[key / ImageSlotsPerFeed];
            Trace("image ok " + KeyText(key) + " serial=" + _imgSerial[key] + " texGen=" + _imgLoadGen[key] + " feedGen=" + _feedJsonGen[key / ImageSlotsPerFeed] +
                " ref=" + _imgRefCount[key] + (_imgStaleContent[key] ? " STALE-CONTENT" : "") + " size=" + (result.Result != null ? result.Result.width + "x" + result.Result.height : "null") +
                " loadedCount=" + _loadedCount + " url=" + result.Url.Get());

            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Success key={key}: {result.Result.width}x{result.Result.height}");

            NotifyWaiters(key, result.Result);
            EvictIfOverBudget();
            StartNextImageDownloadIfIdle();
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            if (_currentPadImageReceiver != null)
            {
                eterpix_pad_requester receiver = _currentPadImageReceiver;
                _currentPadImageReceiver = null;
                _isImageDownloading = false;
                if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] Pad image load error: {result.ErrorMessage}");
                Trace("pad image error url=" + result.Url.Get() + " error=" + result.ErrorMessage);
                receiver.OnTransportImageError(result.Url.Get(), result.ErrorMessage);
                StartNextImageDownloadIfIdle();
                return;
            }

            int key = _currentDownloadKey;
            _isImageDownloading = false;
            _discardCurrentDownload = false;
            if (key < 0 || key >= _imgState.Length)
            {
                StartNextImageDownloadIfIdle();
                return;
            }

            if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] Image load error key={key}: {result.Error}. Retrying if still referenced.");
            Trace("image error " + KeyText(key) + " ref=" + _imgRefCount[key] + " error=" + result.Error + " msg=" + result.ErrorMessage + " url=" + result.Url.Get());

            _imgState[key] = _imgRefCount[key] > 0 ? eterpix_v2_image_state.Queued : eterpix_v2_image_state.Unrequested;
            StartNextImageDownloadIfIdle();
        }

        private void NotifyWaiters(int key, Texture2D texture)
        {
            int feedIndex = key / ImageSlotsPerFeed;
            int slot = key % ImageSlotsPerFeed;
            int baseIndex = key * MaxWaitersPerSlot;
            for (int i = 0; i < MaxWaitersPerSlot; i++)
            {
                eterpix_monitor waiter = _waitingMonitorsFlat[baseIndex + i];
                if (waiter != null)
                {
                    Trace("notify waiter " + KeyText(key) + " to=" + waiter.TraceName + " serial=" + _imgSerial[key]);
                    // waiterがその後別のスロットへページ送りしていた場合でも、ここではキーごとの
                    // 待機リストから外れないまま残っている(ReleaseTextureは待機リストを
                    // 積極的には掃除しない)。ApplyTexture側でfeedIndex/slotの一致を検証させ、
                    // 現在表示中のスロットと異なる場合は安全にno-opさせる(レビュー指摘の
                    // 「古い待機者が別スロットの画像を上書きする」バグへの対策)。
                    waiter.ApplyTexture(feedIndex, slot, texture);
                    _waitingMonitorsFlat[baseIndex + i] = null;
                }
            }
        }
    }
}
