# EterPix v2 (ダウンローダ/リクエスター/モニター) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Assets/EterPix/code/リファクタリング案.md` の決定事項に従い、EterPixの新しい実装(ダウンローダ/リクエスター/モニターの3層)を `Assets/EterPix/code/v2/` に、旧系統(新旧どちらの現行実装も含む)と並存する形で作る。旧系統は本計画のスコープ外(完成後に別途削除)。

**Architecture:** UdonSharpのシングルトンパターン(`GameObject.Find`で固定名オブジェクトを解決)を踏襲。`eterpix_downloader`(ワールドに1つ)がJSON/画像のダウンロードを一元管理し、複数の`eterpix_requester`(フィードごと、JSON URL 1本+256件の画像URL表を保持)がそれぞれ複数の`eterpix_monitor`(表示UI+ページング+トリガー+ポータル)を子に持つ。

**Tech Stack:** Unity + UdonSharp (VRChat SDK3 Worlds)。`VRC.SDK3.StringLoading` (JSON文字列DL)、`VRC.SDK3.Image` (画像DL)、`VRC.SDK3.Data` (DataDictionary/DataList/DataToken)、`[UdonSynced]` + `RequestSerialization`。

## Global Constraints

- UdonSharpの制約: interfaceによる多態は使わない(呼び出し元の型ごとにオーバーロードを追加する)。enumは名前空間直下に置く(ネスト不可)。`List<T>`は使わず配列をコピーして伸ばす。
- シングルトンは固定名GameObjectを`GameObject.Find`で解決する(`Start()`で2つ目以降は`gameObject.SetActive(false)`)。
- 非アクティブな子から`GetComponentInParent`で親を探さない。親が`GetComponentsInChildren(true)`で子を集めて`SetParent`相当の参照渡しをする。
- VRCStationに着席中は`transform.SetParent`を使わない。
- 画像URLは実行時に生成できないため、エディタの`Editor/eterpix_url_sync.cs`相当でPlay開始前に`vrcurllist`へ事前生成する。v2は**256件**(`00`〜`ff`)。
- 2×3コラージュのUV式・回転式(`imgRotation * -90f`)は既存コード(`eterpix_cell`等)と同じものを使う。
- サーバー: JSON `GET {baseUrl}`、画像 `GET {baseUrl}/{hex}`(`00`〜`ff`、256スロットのリングバッファ)。`posts`要素のキー: `collage_id`, `img_pos`, `img_rotation`, `uuid`, `description`, `user_name`, `world_vrc_id`, `is_r18`, `created_at`(`likes`は無い)。更新は10分刻み。
- UdonSharpの動作確認は自動テストではなく、`mcp__UnityMCP__refresh_unity`(compile: request)後に`mcp__UnityMCP__read_console`でエラー0件を確認する形で行う。実機の同期・マルチプレイヤー挙動はUnity Play Mode / VRChatクライアントでの手動確認が必要(このplanでは各タスクの最後に確認手順として明記する)。
- 旧系統(`eterpix_get_api`等、CLAUDE.md記載の表の「旧系統」列)には一切手を入れない。

---

### Task 1: `eterpix_downloader` — JSONフィード管理(複数フィード・定期/手動更新)

**Files:**
- Create: `Assets/EterPix/code/v2/eterpix_downloader.cs`
- Test: Unity Editor(コンパイル確認)。自動テストなし。

**Interfaces:**
- Consumes: なし(新規)。既存`eterpix_debug`(`eterpix_debug.SingletonObjectName = "EterpixDebug"`、`Log`/`LogWarning`/`LogError`)を流用。
- Produces:
  - `public const string SingletonObjectName = "EterpixDownloaderV2"`
  - `public int RegisterFeed(VRCUrl jsonUrl, eterpix_requester requester)` — フィードを登録し、そのフィードのインデックス(int)を返す。同じURL文字列なら既存インデックスを返す。
  - `public void RequestManualRefresh(int feedIndex)`
  - `public DataList GetFeedPosts(int feedIndex)` / `public DataDictionary GetFeedWorldData(int feedIndex)`

- [ ] **Step 1: `v2`フォルダを作成し、フィード登録とJSON取得の骨格を実装する**

```csharp
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

        // ---- JSON取得の実行(各クライアントがローカルで実行。全体で直列にするため、
        // 実行中のフィードがあれば完了後に取りこぼしなく処理されるようキューは持たず、
        // 短時間の重複要求は_feedPendingで弾く) ----
        private void RequestJson(int feedIndex)
        {
            if (feedIndex < 0 || feedIndex >= _feedCount) return;
            if (_feedPending[feedIndex]) return;

            _feedPending[feedIndex] = true;
            _pendingFeedIndex = feedIndex;
            if (debugLog != null) debugLog.Log($"[eterpix_downloader] Fetching JSON for feed {feedIndex}...");
            SendCustomEventDelayedSeconds(nameof(CheckRequestTimeout), RequestTimeoutSeconds);
            VRCStringDownloader.LoadUrl(_feedUrls[feedIndex], (IUdonEventReceiver)this);
        }

        public void CheckRequestTimeout()
        {
            int feedIndex = _pendingFeedIndex;
            if (feedIndex >= 0 && feedIndex < _feedCount && _feedPending[feedIndex])
            {
                if (debugLog != null) debugLog.LogWarning($"[eterpix_downloader] No response after {RequestTimeoutSeconds}s for feed {feedIndex}");
                _feedPending[feedIndex] = false;
            }
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            int feedIndex = _pendingFeedIndex;
            if (feedIndex < 0 || feedIndex >= _feedCount) return;
            _feedPending[feedIndex] = false;

            if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken token) ||
                token.TokenType != TokenType.DataDictionary)
            {
                if (debugLog != null) debugLog.LogError($"[eterpix_downloader] Failed to parse JSON for feed {feedIndex}");
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
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            int feedIndex = _pendingFeedIndex;
            if (feedIndex < 0 || feedIndex >= _feedCount) return;
            _feedPending[feedIndex] = false;
            if (debugLog != null) debugLog.LogError($"[eterpix_downloader] String load error for feed {feedIndex}: {result.Error}");
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
```

- [ ] **Step 2: `eterpix_requester`の前方参照が未定義のため、空のプレースホルダではなくTask 3で実装するクラスとして扱う**

Task 1単体ではまだ`eterpix_requester`クラスが存在せずコンパイルエラーになる。これは意図した状態であり、Task 3まで完了させて初めてコンパイルが通る。Task 1のコミットは「コンパイルエラーが`eterpix_requester`未定義によるものだけであること」をUnityコンソールで確認して進める。

- [ ] **Step 3: Unity側のコンパイルエラーを確認する**

```
mcp__UnityMCP__refresh_unity (mode: force, compile: request, wait_for_ready: true)
mcp__UnityMCP__read_console (action: get, types: ["error"], count: "20")
```
期待結果: `eterpix_requester`が見つからない旨のCS0246エラーのみ(他のエラーが無いこと)。

- [ ] **Step 4: Commit**

```bash
git add "Assets/EterPix/code/v2/eterpix_downloader.cs" "Assets/EterPix/code/v2/eterpix_downloader.cs.meta"
git commit -m "feat(eterpix-v2): add eterpix_downloader JSON feed management"
git push
```
(`.cs.meta`はUnityがファイル作成時に自動生成するため、コミット前に`git status`で存在を確認すること)

---

### Task 2: `eterpix_downloader` — 画像スロット管理(フィード×256、優先度キュー、参照カウント、M枚LRU)

**Files:**
- Modify: `Assets/EterPix/code/v2/eterpix_downloader.cs`

**Interfaces:**
- Consumes: Task 1の`_feedCount`, `maxFeeds`, `debugLog`。呼び出し元は`eterpix_monitor`(Task 4)の型でオーバーロードを追加する。
- Produces:
  - `public void RegisterFeedImageUrls(int feedIndex, ali.eterpix.vrcurllist urlList)`
  - `public void RequestTexture(int feedIndex, int slot, int priority, eterpix_monitor caller)`
  - `public void ReleaseTexture(int feedIndex, int slot, eterpix_monitor caller)`
  - `caller`側に`public void ApplyTexture(Texture2D texture)`が必要(Task 4で実装)。

- [ ] **Step 1: 画像スロット状態とURL表登録を追加する**

`eterpix_downloader.cs`の`namespace`直下(クラスの外)に状態enumを追加:

```csharp
    public enum eterpix_v2_image_state { Unrequested, Queued, Downloading, Loaded }
```

クラス内、既存フィールドの下に追加:

```csharp
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
        private int _loadedCount = 0;

        private const int MaxWaitersPerSlot = 30;
        private eterpix_monitor[] _waitingMonitorsFlat; // [key * MaxWaitersPerSlot + waiterSlot]

        private VRC.SDK3.Image.VRCImageDownloader _imageDownloader;
        private bool _isImageDownloading = false;
        private int _currentDownloadKey = -1;

        private int _pendingDiscardKey;
        private int _pendingDiscardGeneration;
```

`Start()`の末尾(`ScheduleNextUpdate();`の直前)に初期化を追加:

```csharp
            int totalSlots = maxFeeds * ImageSlotsPerFeed;
            _feedUrlLists = new ali.eterpix.vrcurllist[maxFeeds];
            _imgState = new eterpix_v2_image_state[totalSlots];
            _imgTexture = new Texture2D[totalSlots];
            _imgRefCount = new int[totalSlots];
            _imgPriority = new int[totalSlots];
            _imgGraceGeneration = new int[totalSlots];
            _waitingMonitorsFlat = new eterpix_monitor[totalSlots * MaxWaitersPerSlot];
            _imageDownloader = new VRC.SDK3.Image.VRCImageDownloader();
```

- [ ] **Step 2: URL表登録とキー変換ヘルパーを追加する**

```csharp
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
```

- [ ] **Step 3: Request/Release/優先度キュー/ダウンロード実行/LRU破棄を追加する**

```csharp
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
            if (_imgRefCount[key] == 0 && _imgState[key] == eterpix_v2_image_state.Loaded)
            {
                _imgGraceGeneration[key]++;
                _pendingDiscardKey = key;
                _pendingDiscardGeneration = _imgGraceGeneration[key];
                SendCustomEventDelayedSeconds(nameof(TryDiscardSlot), releaseGraceSeconds);
            }
        }

        public void TryDiscardSlot()
        {
            int key = _pendingDiscardKey;
            int generation = _pendingDiscardGeneration;

            if (_imgRefCount[key] != 0) return;
            if (_imgGraceGeneration[key] != generation) return;

            DiscardSlot(key);
        }

        private void DiscardSlot(int key)
        {
            if (_imgState[key] != eterpix_v2_image_state.Loaded) return;
            _imgTexture[key] = null;
            _imgState[key] = eterpix_v2_image_state.Unrequested;
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
```

`using VRC.SDK3.Image;` と `using VRC.Udon.Common.Interfaces;` は既にTask 1で`using`済みか確認し、無ければファイル先頭に追加する。

- [ ] **Step 4: コンパイル確認**

```
mcp__UnityMCP__refresh_unity (mode: force, compile: request, wait_for_ready: true)
mcp__UnityMCP__read_console (action: get, types: ["error"], count: "20")
```
期待結果: `eterpix_requester`未定義と`eterpix_monitor`未定義のCS0246のみ(Task 3, 4で解消される)。

- [ ] **Step 5: Commit**

```bash
git add "Assets/EterPix/code/v2/eterpix_downloader.cs"
git commit -m "feat(eterpix-v2): add image slot manager with priority queue and LRU eviction"
git push
```

---

### Task 3: `eterpix_requester` — フィード登録・256件URL表・Nスロット窓・R18フィルタ

**Files:**
- Create: `Assets/EterPix/code/v2/eterpix_requester.cs`

**Interfaces:**
- Consumes: `eterpix_downloader.SingletonObjectName`, `RegisterFeed(VRCUrl, eterpix_requester)`, `RegisterFeedImageUrls(int, vrcurllist)`, `GetFeedPosts(int)`, `GetFeedWorldData(int)`, `RequestManualRefresh(int)`。
- Produces:
  - `public void OnJsonUpdated()` — ダウンローダから呼ばれる。
  - `public int FeedIndex { get; }` — モニターがダウンローダへ画像要求する際に使う。
  - `public DataDictionary GetVisiblePost(int slotInWindow)` — 表示対象(N件窓・R18フィルタ済み)の投稿を返す。
  - `public int VisibleCount { get; }`
  - `public string ResolveWorldName(string worldId)` / `ResolveWorldDescription(string worldId)`

- [ ] **Step 1: リクエスターの骨格・フィード登録・URL表登録を実装する**

```csharp
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
```

- [ ] **Step 2: N件窓・R18フィルタの構築ロジックを追加する**

```csharp
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
```

(Step 1のクラス開始波括弧とStep 2の内容・末尾の`}}`を合わせて1ファイルにする)

- [ ] **Step 3: コンパイル確認**

```
mcp__UnityMCP__refresh_unity (mode: force, compile: request, wait_for_ready: true)
mcp__UnityMCP__read_console (action: get, types: ["error"], count: "20")
```
期待結果: `eterpix_monitor`未定義のCS0246のみ(Task 4で解消)。

- [ ] **Step 4: Commit**

```bash
git add "Assets/EterPix/code/v2/eterpix_requester.cs"
git commit -m "feat(eterpix-v2): add eterpix_requester with N-slot window and R18 filter"
git push
```

---

### Task 4: `eterpix_monitor` — 画像表示(回転/UV/4:3収め)・投稿情報・同期ページ送り

**Files:**
- Create: `Assets/EterPix/code/v2/eterpix_monitor.cs`

**Interfaces:**
- Consumes: `eterpix_requester.GetVisiblePost/VisibleCount/ResolveWorldName/ResolveWorldDescription/ManualRefresh`、`eterpix_downloader.RequestTexture/ReleaseTexture(feedIndex, slot, eterpix_monitor)`。
- Produces:
  - `public void Init(eterpix_requester requester, eterpix_downloader downloader, int feedIndex)` — リクエスターから呼ばれる。
  - `public void OnFeedUpdated()` — リクエスターから呼ばれる(JSON更新時)。
  - `public void ApplyTexture(Texture2D texture)` — ダウンローダから呼ばれる。
  - `public void PageNext()` / `public void PagePrev()` — UIボタンから呼ぶ。

- [ ] **Step 1: 画面バインド用フィールドと初期化・ページ位置の同期を実装する**

```csharp
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Data;

namespace ali.eterpix.v2
{
    // v2下位: モニター。リファクタリング案.md「モニター」を参照。
    // UIはvrc_uiのhtmlモック(header/figure/paging/context/Information_window)に合わせて
    // Unity UI要素をEditor上で組み、このスクリプトの[SerializeField]で紐付ける。
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_monitor : UdonSharpBehaviour
    {
        [Header("画像 (figure #post_img)")]
        [SerializeField] private RawImage image;
        [SerializeField] private GameObject[] rotationTargetObjects;

        [Header("ページ送り (#paging)")]
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_Text pageLabel; // "現在/全体"

        [Header("投稿情報 (#post_context)")]
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text descriptionText;

        [Header("ワールド情報 (#world_context。無ければ非表示にするルート)")]
        [SerializeField] private GameObject worldContextRoot;
        [SerializeField] private TMP_Text worldNameText;
        [SerializeField] private TMP_Text worldDescriptionText;
        [SerializeField] private Button openPortalButton;

        [Header("情報ウィンドウ (#Information / #Information_window)")]
        [SerializeField] private Button informationButton;
        [SerializeField] private GameObject informationWindowRoot;

        [Header("開始位置(offset)")]
        [SerializeField] private int offset = 0;

        [Header("ページ位置(全員に同期)")]
        [UdonSynced] private int _syncedPageIndex = 0;

        [Header("デバッグログ")]
        [SerializeField] private ali.eterpix.eterpix_debug debugLog;

        private eterpix_requester _requester;
        private eterpix_downloader _downloader;
        private int _feedIndex = -1;

        private int _currentSlot = -1; // 現在ApplyTexture済みのcollage_id(=slot)
        private bool _hasRequestedTexture = false;
        private bool _isInViewRange = false;

        private DataDictionary _currentPost;

        private Vector3[] _baseLocalPositions;
        private Quaternion[] _baseLocalRotations;
        private Vector3[] _basePivotToCenter;

        // ---- リクエスターからの初期化 ----
        public void Init(eterpix_requester requester, eterpix_downloader downloader, int feedIndex)
        {
            _requester = requester;
            _downloader = downloader;
            _feedIndex = feedIndex;

            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(ali.eterpix.eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<ali.eterpix.eterpix_debug>();
            }

            CacheBaseTransforms();

            if (prevButton != null) prevButton.onClick.AddListener(PagePrev);
            if (nextButton != null) nextButton.onClick.AddListener(PageNext);
            if (informationButton != null) informationButton.onClick.AddListener(ToggleInformationWindow);
            if (informationWindowRoot != null) informationWindowRoot.SetActive(false);

            RefreshDisplay();
        }

        private void CacheBaseTransforms()
        {
            Transform[] targets = GetRotationTargets();
            _baseLocalPositions = new Vector3[targets.Length];
            _baseLocalRotations = new Quaternion[targets.Length];
            _basePivotToCenter = new Vector3[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                _baseLocalPositions[i] = targets[i].localPosition;
                _baseLocalRotations[i] = targets[i].localRotation;
                _basePivotToCenter[i] = GetPivotToCenter(targets[i]);
            }
        }

        // ---- リクエスターからのJSON更新通知 ----
        public void OnFeedUpdated()
        {
            // 新着で表示がずれるのは仕様上許容する(リファクタリング案.md「ページ位置」参照)
            RefreshDisplay();
        }
```

- [ ] **Step 2: ページ送り(同期)・投稿の反映・回転/UV/4:3収めを実装する**

```csharp
        // ---- ページ送り ----
        public void PageNext()
        {
            MovePage(1);
        }

        public void PagePrev()
        {
            MovePage(-1);
        }

        private void MovePage(int step)
        {
            if (_requester == null || _requester.VisibleCount <= 0) return;

            int count = _requester.VisibleCount;
            int next = ((_syncedPageIndex + step) % count + count) % count;

            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            _syncedPageIndex = next;
            RequestSerialization();

            RefreshDisplay();
        }

        public override void OnDeserialization()
        {
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_requester == null) return;

            int count = _requester.VisibleCount;
            if (count <= 0)
            {
                SetPlaceholder();
                return;
            }

            int slotInWindow = Mathf.Clamp(_syncedPageIndex, 0, count - 1);
            DataDictionary post = _requester.GetVisiblePost(slotInWindow);
            if (post == null)
            {
                SetPlaceholder();
                return;
            }

            ApplyPost(post);
            UpdatePagingLabel(slotInWindow, count);
        }

        private void UpdatePagingLabel(int slotInWindow, int count)
        {
            if (pageLabel != null) pageLabel.text = $"{slotInWindow + 1}/{count}";
            if (prevButton != null) prevButton.interactable = slotInWindow > 0;
            if (nextButton != null) nextButton.interactable = slotInWindow < count - 1;
        }

        private void ApplyPost(DataDictionary data)
        {
            ReleaseCurrentTextureIfAny();
            _currentPost = data;

            int collageId = ReadInt(data, "collage_id", -1);
            int imgPos = ReadInt(data, "img_pos", -1);
            int imgRotation = ReadInt(data, "img_rotation", 0);
            string description = ReadString(data, "description", "");
            string userName = ReadString(data, "user_name", "");
            string worldId = ReadString(data, "world_vrc_id", "");

            ApplyRotation(imgRotation);
            ApplyUvRect(imgPos);

            _currentSlot = collageId;
            if (_isInViewRange && _downloader != null && collageId >= 0)
            {
                _hasRequestedTexture = true;
                _downloader.RequestTexture(_feedIndex, collageId, 100, this);
            }

            if (userNameText != null) userNameText.text = userName;
            if (descriptionText != null) descriptionText.text = description;

            ApplyWorldContext(worldId);
        }

        private void ApplyWorldContext(string worldId)
        {
            bool hasWorld = !string.IsNullOrEmpty(worldId);
            if (worldContextRoot != null) worldContextRoot.SetActive(hasWorld);
            if (!hasWorld) return;

            if (worldNameText != null) worldNameText.text = _requester.ResolveWorldName(worldId);
            if (worldDescriptionText != null) worldDescriptionText.text = _requester.ResolveWorldDescription(worldId);
        }

        private void SetPlaceholder()
        {
            ReleaseCurrentTextureIfAny();
            if (userNameText != null) userNameText.text = "";
            if (descriptionText != null) descriptionText.text = "";
            if (worldContextRoot != null) worldContextRoot.SetActive(false);
            if (pageLabel != null) pageLabel.text = "0/0";
        }

        private void ReleaseCurrentTextureIfAny()
        {
            if (_hasRequestedTexture && _downloader != null && _currentSlot >= 0)
            {
                _downloader.ReleaseTexture(_feedIndex, _currentSlot, this);
            }
            _hasRequestedTexture = false;
        }

        public void ApplyTexture(Texture2D texture)
        {
            if (texture != null && image != null)
            {
                image.material = null;
                image.texture = texture;
            }
        }

        // ---- ⓘ情報ウィンドウ ----
        public void ToggleInformationWindow()
        {
            if (informationWindowRoot == null) return;
            informationWindowRoot.SetActive(!informationWindowRoot.activeSelf);
        }

        // ---- 回転/UV (eterpix_cellと同じ式。4:3の枠に収め、縦画像は左右に余白を出す) ----
        private Transform[] GetRotationTargets()
        {
            if (rotationTargetObjects != null && rotationTargetObjects.Length > 0)
            {
                Transform[] result = new Transform[rotationTargetObjects.Length];
                for (int i = 0; i < rotationTargetObjects.Length; i++)
                {
                    result[i] = rotationTargetObjects[i] != null ? rotationTargetObjects[i].transform : null;
                }
                return result;
            }
            return image != null ? new[] { image.transform } : new Transform[0];
        }

        private Vector3 GetPivotToCenter(Transform target)
        {
            RectTransform rt = target.GetComponent<RectTransform>();
            if (rt == null) return Vector3.zero;
            Vector2 size = rt.rect.size;
            Vector2 pivot = rt.pivot;
            return new Vector3(size.x * (0.5f - pivot.x), size.y * (0.5f - pivot.y), 0f);
        }

        private void ApplyRotation(int imgRotation)
        {
            Quaternion rotation = Quaternion.Euler(0, 0, imgRotation * -90f);
            Transform[] targets = GetRotationTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                targets[i].localRotation = _baseLocalRotations[i] * rotation;

                Vector3 c = _basePivotToCenter[i];
                Vector3 shift = _baseLocalRotations[i] * (c - rotation * c);
                targets[i].localPosition = _baseLocalPositions[i] + shift;
            }
        }

        private void ApplyUvRect(int imgPos)
        {
            if (image == null) return;
            const float cellW = 0.5f;
            const float cellH = 1f / 3f;
            if (imgPos >= 1 && imgPos <= 6)
            {
                int col = (imgPos - 1) % 2;
                int row = (imgPos - 1) / 2;
                float uvX = col * cellW;
                float uvY = 1f - (row + 1) * cellH;
                image.uvRect = new Rect(uvX, uvY, cellW, cellH);
            }
        }

        private int ReadInt(DataDictionary data, string key, int fallback)
        {
            if (!data.TryGetValue(key, out DataToken token)) return fallback;
            if (token.TokenType == TokenType.Double) return (int)token.Double;
            if (token.TokenType == TokenType.String && int.TryParse(token.String, out int parsed)) return parsed;
            return fallback;
        }

        private string ReadString(DataDictionary data, string key, string fallback)
        {
            if (data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.String) return token.String;
            return fallback;
        }
    }
}
```

(Step 1とStep 2をつなげて1ファイルにする。`_isInViewRange`の設定はTask 5で実装するトリガーColliderから行う。Task 4完了時点では常に`false`のままなので、画像は要求されない=プレースホルダー表示のままになる。これはTask 5完了までの暫定状態として許容する)

- [ ] **Step 3: コンパイル確認とUnity上のPrefab仮組み**

```
mcp__UnityMCP__refresh_unity (mode: force, compile: request, wait_for_ready: true)
mcp__UnityMCP__read_console (action: get, types: ["error"], count: "20")
```
期待結果: エラー0件(Task 1〜4のクラスが揃うため、この時点でv2の3クラスすべてがコンパイルエラー無しになるはず)。

エラーが0件になったら、Unity Editor上で最小構成のシーン(`EterpixDownloaderV2`という名前のGameObjectに`eterpix_downloader`をアタッチ、その下に`eterpix_requester`+子に`eterpix_monitor`を1つ、UIはRawImage+TMP_Textだけの最小構成)を仮組みし、Playモードで`eterpix_debug`の`debugEnabled`を`true`にしてログを見ながら、JSON取得→投稿表示までを手動確認する。この手動確認はUnityMCPの`manage_gameobject`等で自動化してもよいが、UI要素の見た目確認は目視が必要なため、ユーザー自身によるPlayモード確認を挟むこと。

- [ ] **Step 4: Commit**

```bash
git add "Assets/EterPix/code/v2/eterpix_monitor.cs"
git commit -m "feat(eterpix-v2): add eterpix_monitor display, paging sync, and info window"
git push
```

---

### Task 5: `eterpix_monitor` — トリガーColliderによるオンデマンド画像要求

**Files:**
- Modify: `Assets/EterPix/code/v2/eterpix_monitor.cs`

**Interfaces:**
- Consumes: Task 4の`_currentSlot`, `_hasRequestedTexture`, `_downloader.RequestTexture/ReleaseTexture`。
- Produces: `OnPlayerTriggerEnter(VRCPlayerApi)` / `OnPlayerTriggerExit(VRCPlayerApi)`(UdonSharpの標準イベント)。

- [ ] **Step 1: トリガー範囲判定を追加する**

`eterpix_monitor`クラス内、`OnFeedUpdated()`の下に追加:

```csharp
        // ---- トリガーColliderによるオンデマンド画像要求 ----
        // このGameObject(またはInspectorで割り当てた子)にColliderを持たせ、isTrigger=true、
        // レイキャストを遮らないレイヤー(Ignore Raycast等)に置く。
        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (!player.isLocal) return;
            _isInViewRange = true;

            if (_downloader != null && _currentSlot >= 0 && !_hasRequestedTexture)
            {
                _hasRequestedTexture = true;
                _downloader.RequestTexture(_feedIndex, _currentSlot, 100, this);
            }
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (!player.isLocal) return;
            _isInViewRange = false;
            ReleaseCurrentTextureIfAny();
        }
```

`using VRC.SDKBase;`は既にTask 4で追加済みのため`VRCPlayerApi`はそのまま使える。

- [ ] **Step 2: `ApplyPost`の画像要求条件が二重に発火しないことを確認する**

Task 4の`ApplyPost`内、以下の行:
```csharp
            if (_isInViewRange && _downloader != null && collageId >= 0)
            {
                _hasRequestedTexture = true;
                _downloader.RequestTexture(_feedIndex, collageId, 100, this);
            }
```
はそのままでよい(ページが変わった瞬間に範囲内なら新しいスロットを要求し、`ReleaseCurrentTextureIfAny()`が呼ばれた後に`_currentSlot`が更新されるため、古いスロットの解放と新しいスロットの要求が正しい順序で行われる)。追加の変更は不要。

- [ ] **Step 3: コンパイル確認**

```
mcp__UnityMCP__refresh_unity (mode: force, compile: request, wait_for_ready: true)
mcp__UnityMCP__read_console (action: get, types: ["error"], count: "20")
```
期待結果: エラー0件。

- [ ] **Step 4: Commit**

```bash
git add "Assets/EterPix/code/v2/eterpix_monitor.cs"
git commit -m "feat(eterpix-v2): request textures only while player is in monitor trigger range"
git push
```

---

### Task 6: `eterpix_monitor` — ポータル(VRCPortalMarker、ローカル限定表示、3m自動消去)

**Files:**
- Modify: `Assets/EterPix/code/v2/eterpix_monitor.cs`

**Interfaces:**
- Consumes: `openPortalButton`(Task 4で定義済み)、`_currentPost`の`world_vrc_id`。
- Produces: `public void OpenPortal()`(ボタンから呼ぶ)。

- [ ] **Step 1: ポータル用フィールドと開閉ロジックを追加する**

`eterpix_monitor`クラスのヘッダ部、`[Header("ワールド情報 (#world_context...")]`ブロックの直後に追加:

```csharp
        [Header("ポータル(1つずつ、押した本人にのみ見える)")]
        [SerializeField] private VRC.SDK3.Components.VRCPortalMarker portalMarker;
        [SerializeField] private Transform portalSpawnPoint; // 未指定ならこのtransform
        [SerializeField] private float portalAutoCloseDistance = 3f;
```

`Init()`内、`if (openPortalButton != null) ...`のボタン登録行の近くに追加:

```csharp
            if (openPortalButton != null) openPortalButton.onClick.AddListener(OpenPortal);
            if (portalMarker != null) portalMarker.gameObject.SetActive(false);
```

クラス末尾(`ReadString`メソッドの後)に追加:

```csharp
        // ---- ポータル ----
        public void OpenPortal()
        {
            if (portalMarker == null || _currentPost == null) return;

            string worldId = ReadString(_currentPost, "world_vrc_id", "");
            if (string.IsNullOrEmpty(worldId)) return;

            Transform spawn = portalSpawnPoint != null ? portalSpawnPoint : transform;
            portalMarker.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            portalMarker.roomId = worldId;
            portalMarker.gameObject.SetActive(true);
            portalMarker.RefreshPortal();

            SendCustomEventDelayedSeconds(nameof(CheckPortalDistance), 1f);
        }

        public void CheckPortalDistance()
        {
            if (portalMarker == null || !portalMarker.gameObject.activeSelf) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;

            float distance = Vector3.Distance(local.GetPosition(), portalMarker.transform.position);
            if (distance > portalAutoCloseDistance)
            {
                portalMarker.gameObject.SetActive(false);
                return;
            }

            SendCustomEventDelayedSeconds(nameof(CheckPortalDistance), 1f);
        }
```

`VRCPortalMarker`はローカルプレイヤーごとに独立して開閉できる想定のコンポーネントであり、`gameObject.SetActive`はローカルのみの操作(同期しない)。これは「押した本人にだけ見える」という仕様に合致する(既存`eterpix_listener_portal.cs`と同じ考え方)。

- [ ] **Step 2: コンパイル確認**

```
mcp__UnityMCP__refresh_unity (mode: force, compile: request, wait_for_ready: true)
mcp__UnityMCP__read_console (action: get, types: ["error"], count: "20")
```
期待結果: エラー0件。

- [ ] **Step 3: Commit**

```bash
git add "Assets/EterPix/code/v2/eterpix_monitor.cs"
git commit -m "feat(eterpix-v2): add local-only portal with auto-close on distance"
git push
```

---

### Task 7: エディタ同期メニューのv2対応 + シーン組み立てチェックリスト

**Files:**
- Read: `Assets/EterPix/code/Editor/eterpix_url_sync.cs`(既存。中身を確認してから対応方針を決める)
- Modify: 既存`Editor/eterpix_url_sync.cs`、または新規`Assets/EterPix/code/Editor/eterpix_url_sync_v2.cs`

**Interfaces:**
- Consumes: `ali.eterpix.v2.eterpix_requester`(の`SerializeField`な`urlList`/`requestUrl`、privateのためエディタから`SerializedObject`経由でアクセスする)。
- Produces: メニュー`ali/eterpix/Sync URL Lists from requestUrl (v2)`、またはv1と共通化した1メニューでv2の`eterpix_requester`も対象に含める。

- [ ] **Step 1: 既存エディタスクリプトを読み、256件対応のパターンを確認する**

```
Read: Assets/EterPix/code/Editor/eterpix_url_sync.cs
```
既存が「シーン内の中位コンポーネントを`FindObjectsOfType`等で列挙し、`requestUrl`から`vrcurllist.baseUrl`/`arraySize`/`urlArray`を`baseUrl/00`〜`baseUrl/0F`(16件)の形式で自動生成する」実装になっているはずである。この処理を、v2の`eterpix_requester`にも適用できるよう拡張する(`arraySize`を256にし、16進数2桁`00`〜`ff`で生成する部分を256件ループに変更、または既存の16進数生成ロジックがそのまま256件に対応済みか確認する)。

- [ ] **Step 2: v2の`eterpix_requester`を列挙してURL同期する処理を追加する**

既存の実装パターンに合わせて(具体的なコードは既存ファイルの中身を見てから決定する)、`ali.eterpix.v2.eterpix_requester`型を`FindObjectsOfType`で列挙し、各インスタンスの`requestUrl`(private、`SerializedProperty`経由)から`urlList`(`vrcurllist`)の`baseUrl`/`arraySize=256`/`urlArray`を同期するメニュー項目、または既存メニューへの統合を実装する。

- [ ] **Step 3: メニュー実行して動作確認する**

```
mcp__UnityMCP__execute_menu_item (menu_path: "ali/eterpix/Sync URL Lists from requestUrl") // v2対応後のメニュー名に置き換える
mcp__UnityMCP__read_console (action: get, types: ["error", "warning"], count: "20")
```
期待結果: エラー0件。仮組みしたシーンの`eterpix_requester`の`urlList`に256件のURLが生成されていることを、Unity Editor上で目視確認する(`vrcurllist.urlArray`のInspector表示、またはUnityMCPの`manage_scriptable_object`等でシリアライズ内容を読み出して確認する)。

- [ ] **Step 4: 実際のprefab組み立てチェックリストをCLAUDE.mdに追記する**

`Assets/EterPix/code/CLAUDE.md`の「## このプロジェクト固有のルール」の下、または新規セクションとして、v2 prefab組み立て時にEditorで手作業が必要な項目を列挙する(スクリプトでは自動化できない部分):

```markdown
## v2 (`code/v2/`) prefab組み立てメモ
- `EterpixDownloaderV2`という名前のGameObjectに`eterpix_downloader`を1つだけ配置する。
- `eterpix_requester`ごとに、Inspectorで`requestUrl`(JSON URL)と`urlList`(`vrcurllist`、baseUrlのみ設定・arraySizeは256に統一)を設定する。
- `eterpix_requester`の子に`eterpix_monitor`を1つ以上配置する(非アクティブでも`GetComponentsInChildren(true)`で拾われる)。
- 画像を「4:3の枠に切らずに全体を収める」表示は`eterpix_monitor`のコードでは行わない。`image`の親に`AspectRatioFitter`(Fit Mode: Fit In Parent)をアタッチし、4:3のコンテナ内に収める形でEditor上で設定する。
- `eterpix_monitor`のUIはvrc_uiのhtmlモック(`D:\git\eterpix-v10\vrc_ui\vrcetepixui.html`/`.css`)に合わせてUnity UIを手組みし、`image`/`prevButton`/`nextButton`/`pageLabel`/`userNameText`/`descriptionText`/`worldContextRoot`/`worldNameText`/`worldDescriptionText`/`openPortalButton`/`informationButton`/`informationWindowRoot`をInspectorで紐付ける。
- `eterpix_monitor`(またはInspectorで割り当てた子)にトリガーColliderを追加し、`isTrigger=true`、レイキャストを遮らないレイヤー(例: `Ignore Raycast`)に設定する。そのレイヤーがPlayerLocalと衝突判定する設定になっていることをProject Settings > Physicsで確認する。
- `eterpix_monitor`に`VRCPortalMarker`を1つ子として持たせ、`portalMarker`に紐付ける。
- Play開始前に必ずメニュー`ali/eterpix/Sync URL Lists from requestUrl`(v2対応後)を実行し、256件のURLが同期されていることを確認する。
```

- [ ] **Step 5: Commit**

```bash
git add "Assets/EterPix/code/Editor/eterpix_url_sync.cs" "Assets/EterPix/code/CLAUDE.md"
git commit -m "feat(eterpix-v2): extend URL sync editor tool to 256-slot v2 requesters"
git push
```

---

## 完了後のスコープ外事項(このplanには含まない)

- 旧系統(`eterpix_get_api`等)の削除は、v2がVRChatクライアント上で実際に動作確認できてから別途行う(リファクタリング案.md「全体方針」)。
- 情報ウィンドウの文面・ロゴ画像、Nスロット・M枚・猶予時間・トリガー範囲の既定値の最終調整は「実装時に決めること(未確定)」のままであり、実装後にUnity Editor上でInspector値として調整する。
- ダウンロード失敗時の再試行の間隔・回数上限は、Task 2の実装では即座に1回再キュー(無限リトライ)にしてある。実運用で問題が出れば、リトライ回数カウンタを`_imgRetryCount[key]`として追加する対応をBUGSリストに追記して別タスク化する。
