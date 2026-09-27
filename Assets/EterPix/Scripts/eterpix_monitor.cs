using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Data;

namespace ali.eterpix.v2
{
    // v2下位: モニター。リファクタリング案.md「モニター」を参照。
    // UIは開発用ビルダー(Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs)が組み立てる
    // prefab(EterpixMonitor_Stack / EterpixMonitor_Split)の要素を[SerializeField]で紐付ける。
    // 旧prefab(Prefabs/Legacy/EterpixMonitorUI)も動くように、新しい参照はすべてnull許容にしている。
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_monitor : UdonSharpBehaviour
    {
        [Header("画像")]
        [SerializeField] private RawImage image;
        [SerializeField] private GameObject[] rotationTargetObjects;
        [Header("画像の親(4:3枠。AspectRatioFitterで実際の縦横比に合わせて縮める対象)")]
        [SerializeField] private AspectRatioFitter figureAspectFitter;

        [Header("ページ送り(端でループする)")]
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_Text pageLabel; // "現在 / 全体"

        [Header("投稿情報")]
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text descriptionText;

        [Header("ワールド情報(ワールドが無い投稿では非表示にするルート)")]
        [SerializeField] private GameObject worldContextRoot;
        [SerializeField] private TMP_Text worldNameText;
        [SerializeField] private TMP_Text worldDescriptionText;
        [SerializeField] private Button openPortalButton;

        [Header("ポータル(ワールドに1つだけ置いた共通ポータルを呼び寄せる。押した本人にのみ見える)")]
        // 未指定なら固定名 PortalObjectName のGameObjectから解決する
        [SerializeField] private eterpix_porta_resize portal;
        [SerializeField] private Transform portalSpawnPoint; // 未指定ならこのtransform

        public const string PortalObjectName = "EterpixPortal";

        [Header("再読み込み(押した本人だけJSONを取り直す。連打防止のため押した後しばらく押せなくする)")]
        [SerializeField] private Button reloadButton;
        // VRCStringDownloaderは5秒に1回までのため、それより長く空ける
        public const float ReloadCooldownSeconds = 10f;

        [Header("情報ウィンドウ")]
        [SerializeField] private Button informationButton;
        [SerializeField] private GameObject informationWindowRoot;
        [Header("情報ボタンのアイコン(情報表示中はXに切り替える)")]
        [SerializeField] private GameObject infoIconOpen;
        [SerializeField] private GameObject infoIconClose;

        [Header("状態表示(読み込み中/エラー/投稿なし。写真枠の中に出す)")]
        [SerializeField] private GameObject statusRoot;
        [SerializeField] private TMP_Text statusText;
        // 通常表示のときだけ出す要素(ページ番号・ページ送り・画像・投稿欄など)
        [SerializeField] private GameObject[] normalOnlyObjects = new GameObject[0];

        // 状態表示の文言はギミック側で固定する(ワールド作者がInspectorで書き換えられないようconstにする)
        public const string LoadingMessage = "読み込み中…";
        public const string UntrustedUrlMessage =
            "「信頼されていないURL」が許可されていません\n\n設定 > 快適性とセーフティ >\n「信頼されていないURLを許可」をONにしてください\nONにしたら、右上の再読み込みボタンを押してください\n(表示されない場合はワールドに入り直してください)";
        public const string ServerErrorMessage =
            "サーバーに接続できませんでした\nメンテナンス中の可能性があります\n最新情報は X @_alicilia をご確認ください\n\n数分後に自動で再接続します";
        public const string EmptyMessage = "表示できる投稿がありません";

        [Header("画像読み込み中の明滅(LoadingPulseマテリアル。色はテーマのボタン色)")]
        [SerializeField] private Material loadingMaterial;
        [SerializeField] private eterpix_monitor_theme monitorTheme;

        [Header("開始位置(offset)。投稿数以上なら offset % 投稿数 のページから始まる")]
        [SerializeField] private int offset = 0;

        [Header("ページ位置(全員に同期)")]
        [UdonSynced] private int _syncedPageIndex = 0;

        [Header("デバッグログ")]
        [SerializeField] private ali.eterpix.eterpix_debug debugLog;

        [Header("診断用: トリガーColliderの範囲判定を無視して常に画像をリクエストする" +
            "(画像が読み込まれない不具合の切り分け用。原因特定後はfalseに戻すこと)")]
        [SerializeField] private bool debugIgnoreTriggerRange = false;

        [Header("診断用: ApplyPost実行時の内部状態(読み取り専用、外部から確認するためのもの)")]
        public string debugState = "";

        private eterpix_requester _requester;
        private eterpix_downloader _downloader;
        private int _feedIndex = -1;

        private int _currentSlot = -1; // 現在ApplyTexture済みのcoid(=collage_id=slot)
        private bool _hasRequestedTexture = false;
        private bool _isInViewRange = false;
        private bool _isImageLoading = false;

        private float _reloadAvailableTime = 0f;

        private DataDictionary _currentPost;
        // Init()より先にOnDeserialization()で本物の同期値を受け取っていた場合、
        // offsetで上書きしないためのフラグ(遅れて入ったプレイヤーの初回同期と
        // Init()の実行順は保証されないため)。
        private bool _hasReceivedSync = false;

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

            // offset(Inspectorでの開始ページ位置)は[UdonSynced]の_syncedPageIndexへ
            // Init時に一度だけ反映する。ただし、Init()より先に本物の初回同期
            // (OnDeserialization)を受け取っていた場合は、後から入ったプレイヤーの
            // 正しいページ位置をoffsetで上書きしてしまうため反映しない。
            // 投稿数以上のoffsetは、表示時にNormalizeIndexで offset % 投稿数 に収める。
            if (offset != 0 && !_hasReceivedSync) _syncedPageIndex = offset;

            // UdonSharpはUnityEvent.AddListener()(メソッドグループ・ラムダのいずれも)を
            // バインドできない。各ボタンのOnClick()はコードからではなく、
            // エディタ上でこのコンポーネントのUdonBehaviourのSendCustomEventに
            // PagePrev/PageNext/ToggleInformationWindow/OpenPortal/ReloadFeedを登録してある。
            if (portal == null)
            {
                GameObject portalObj = GameObject.Find(PortalObjectName);
                if (portalObj != null) portal = portalObj.GetComponent<eterpix_porta_resize>();
            }

            SetInformationOpen(false);

            BindViewRangeTriggers();
            RefreshDisplay();
        }

        // ---- リクエスターからのJSON更新通知(成功・失敗どちらでも呼ばれる) ----
        public void OnFeedUpdated()
        {
            // 新着で表示がずれるのは仕様上許容する(リファクタリング案.md「ページ位置」参照)
            RefreshDisplay();
        }

        // ---- トリガーColliderによるオンデマンド画像要求 ----
        // 表示範囲のトリガーは子のGameObject(ViewRange)にColliderとeterpix_monitor_triggerを付けて分ける。
        // 出入りはそこから中継される。中継側からGetComponentInParentで親を探さず、こちらから子を集めて
        // 参照を渡す。Init()がStart()より先に呼ばれる場合があるため、両方から呼ぶ(何度呼んでもよい)。
        private void Start()
        {
            BindViewRangeTriggers();
            // requesterから初期化されるまでの間、prefabの見本テキストを見せないよう読み込み中にする
            if (_requester == null) ShowStatus(LoadingMessage);
        }

        private void BindViewRangeTriggers()
        {
            eterpix_monitor_trigger[] triggers = GetComponentsInChildren<eterpix_monitor_trigger>(true);
            for (int i = 0; i < triggers.Length; i++)
            {
                triggers[i].SetMonitor(this);
            }
        }

        // eterpix_monitor_triggerから、ローカルプレイヤーが表示範囲に入ったときに呼ばれる
        public void OnViewRangeEnter()
        {
            _isInViewRange = true;

            if (_downloader != null && _currentSlot >= 0 && !_hasRequestedTexture)
            {
                _hasRequestedTexture = true;
                _downloader.RequestTexture(_feedIndex, _currentSlot, 100, this);
            }
        }

        // eterpix_monitor_triggerから、ローカルプレイヤーが表示範囲から出たときに呼ばれる
        public void OnViewRangeExit()
        {
            _isInViewRange = false;
            ReleaseCurrentTextureIfAny();
        }

        // オブジェクトの非アクティブ化/破棄(VRChatがOnPlayerTriggerExitを配送し損ねる
        // テレポート/リスポーン等の境界ケースを含む)で、保持中のテクスチャ参照が
        // 解放されないまま残ると_imgRefCountが恒久的に0へ戻らず、猶予破棄/EvictIfOverBudget
        // の両方から永久に除外されてしまう(レビュー指摘)。
        private void OnDisable()
        {
            ReleaseCurrentTextureIfAny();
            _isInViewRange = false;
        }

        // 非アクティブ中にOnDisableで参照を解放しているため、再表示時に取り直す(解放後にテクスチャが破棄されている可能性がある)
        private void OnEnable()
        {
            if (_requester != null) RefreshDisplay();
            // 非アクティブ中にクールダウンの解除イベントを取りこぼしていても押せるように戻す
            if (Time.time >= _reloadAvailableTime) SetReloadInteractable(true);
        }

        // ---- 再読み込み(ローカルのみ。取得結果はOnFeedUpdated経由で同じフィードの全モニターに反映される) ----
        public void ReloadFeed()
        {
            if (_requester == null || Time.time < _reloadAvailableTime) return;

            _reloadAvailableTime = Time.time + ReloadCooldownSeconds;
            SetReloadInteractable(false);
            SendCustomEventDelayedSeconds(nameof(EndReloadCooldown), ReloadCooldownSeconds);

            _requester.ManualRefresh();
        }

        public void EndReloadCooldown()
        {
            if (Time.time >= _reloadAvailableTime) SetReloadInteractable(true);
        }

        private void SetReloadInteractable(bool interactable)
        {
            if (reloadButton != null) reloadButton.interactable = interactable;
        }

        // ---- ページ送り(端でループする) ----
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
            int current = NormalizeIndex(_syncedPageIndex, count);
            int next = NormalizeIndex(current + step, count);

            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            _syncedPageIndex = next;
            RequestSerialization();

            RefreshDisplay();
        }

        // 同期値は生のまま持ち、表示のたびに投稿数で割った余りに収める(負の値も後ろから数える)
        private int NormalizeIndex(int index, int count)
        {
            return ((index % count) + count) % count;
        }

        public override void OnDeserialization()
        {
            _hasReceivedSync = true;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_requester == null)
            {
                ShowStatus(LoadingMessage);
                return;
            }

            // 一度も取得に成功していない間だけ、読み込み中/エラーを出す。
            // 一度成功していれば、その後の取得失敗では前回のデータを表示し続ける。
            if (!_requester.HasEverSucceeded)
            {
                int status = _requester.FeedStatus;
                if (status == eterpix_downloader.FeedStatusUntrustedUrl) ShowStatus(UntrustedUrlMessage);
                else if (status == eterpix_downloader.FeedStatusServerError) ShowStatus(ServerErrorMessage);
                else ShowStatus(LoadingMessage); // Loading(未取得)。Okは投稿配列の代入後にしか立たないため、ここには来ない
                return;
            }

            int count = _requester.VisibleCount;
            if (count <= 0)
            {
                ShowStatus(EmptyMessage);
                return;
            }

            int slotInWindow = NormalizeIndex(_syncedPageIndex, count);
            DataDictionary post = _requester.GetVisiblePost(slotInWindow);
            if (post == null)
            {
                ShowStatus(EmptyMessage);
                return;
            }

            ShowNormal();
            ApplyPost(post);
            UpdatePagingLabel(slotInWindow, count);
        }

        private void UpdatePagingLabel(int slotInWindow, int count)
        {
            if (pageLabel != null) pageLabel.text = $"{slotInWindow + 1} / {count}";
        }

        // ---- 状態表示 ----
        private void ShowStatus(string message)
        {
            ReleaseCurrentTextureIfAny();
            _currentSlot = -1;
            _currentPost = null;
            _isImageLoading = false;

            if (userNameText != null) userNameText.text = "";
            if (descriptionText != null) descriptionText.text = "";
            if (worldContextRoot != null) worldContextRoot.SetActive(false);
            if (pageLabel != null) pageLabel.text = "";
            SetNormalObjectsActive(false);

            if (statusRoot != null) statusRoot.SetActive(true);
            if (statusText != null) statusText.text = message;
        }

        private void ShowNormal()
        {
            if (statusRoot != null) statusRoot.SetActive(false);
            SetNormalObjectsActive(true);
        }

        private void SetNormalObjectsActive(bool active)
        {
            if (normalOnlyObjects == null) return;
            for (int i = 0; i < normalOnlyObjects.Length; i++)
            {
                if (normalOnlyObjects[i] != null) normalOnlyObjects[i].SetActive(active);
            }
        }

        private void ApplyPost(DataDictionary data)
        {
            ReleaseCurrentTextureIfAny();
            _currentPost = data;

            // v1形式のkey(coid=collage_id, copo=img_pos, coro=img_rotation,
            // pode=description, usna=user_name, woid=world_vrc_id)
            int collageId = ReadInt(data, "coid", -1);
            int imgPos = ReadInt(data, "copo", -1);
            int imgRotation = ReadInt(data, "coro", 0);
            string description = ReadString(data, "pode", "");
            string userName = ReadString(data, "usna", "");
            string worldId = ReadString(data, "woid", "");

            ApplyRotation(imgRotation);
            ApplyUvRect(imgPos);
            // 前の投稿の写真を残さないよう、読み込みが終わるまで明滅表示にする
            // (キャッシュ済みならRequestTexture内で即ApplyTextureされる)
            BeginImageLoading();

            _currentSlot = collageId;
            debugState = $"isInViewRange={_isInViewRange} debugIgnore={debugIgnoreTriggerRange} downloaderNull={(_downloader == null)} feedIndex={_feedIndex} collageId={collageId}";
            if ((_isInViewRange || debugIgnoreTriggerRange) && _downloader != null && collageId >= 0)
            {
                _hasRequestedTexture = true;
                debugState += " -> RequestTexture called";
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

        private void ReleaseCurrentTextureIfAny()
        {
            if (_hasRequestedTexture && _downloader != null && _currentSlot >= 0)
            {
                _downloader.ReleaseTexture(_feedIndex, _currentSlot, this);
            }
            _hasRequestedTexture = false;
        }

        // ---- 画像の読み込み中表示 ----
        private void BeginImageLoading()
        {
            if (image == null) return;
            _isImageLoading = true;
            image.texture = null;
            if (loadingMaterial != null)
            {
                image.material = loadingMaterial;
                image.color = GetLoadingColor();
            }
            else
            {
                // 旧prefab: 従来どおり黒いプレースホルダー
                image.material = null;
                image.color = Color.black;
            }
        }

        private Color GetLoadingColor()
        {
            if (monitorTheme != null) return monitorTheme.LoadingColor;
            return new Color(42f / 255f, 42f / 255f, 42f / 255f, 1f);
        }

        // eterpix_monitor_themeから、配色が変わったときに呼ばれる
        public void OnThemeChanged()
        {
            if (_isImageLoading && image != null && loadingMaterial != null) image.color = GetLoadingColor();
        }

        // feedIndex/slotは、この通知が発行された時点でダウンローダが解決していたキーの内訳。
        // このモニターが待機している間にページ送り等で別スロットへ切り替わっていた場合、
        // 呼び出し元(ダウンローダ)の待機リストには古いキーの通知がまだ残っていることがある
        // (ReleaseTextureは待機リストを積極的には掃除しない: レビュー指摘)。
        // 現在表示中のフィード/スロットと一致しない通知は、誤って別の画像(R18フィルタを
        // 経由していない可能性がある)を貼り付けてしまわないよう、ここで安全に無視する。
        public void ApplyTexture(int feedIndex, int slot, Texture2D texture)
        {
            if (feedIndex != _feedIndex || slot != _currentSlot) return;
            if (texture != null && image != null)
            {
                _isImageLoading = false;
                image.material = null;
                image.texture = texture;
                // 読み込み中は明滅色(または旧prefabの黒)にしてある。この色はRawImageの
                // テクスチャに乗算されるため、白に戻さないと実際の写真が正しく表示されない。
                image.color = Color.white;
            }
        }

        // ---- 情報ウィンドウ(表示中はボタンのアイコンをXにする) ----
        public void ToggleInformationWindow()
        {
            if (informationWindowRoot == null) return;
            SetInformationOpen(!informationWindowRoot.activeSelf);
        }

        private void SetInformationOpen(bool open)
        {
            if (informationWindowRoot != null) informationWindowRoot.SetActive(open);
            if (infoIconOpen != null) infoIconOpen.SetActive(!open);
            if (infoIconClose != null) infoIconClose.SetActive(open);
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

        // コラージュの1セルは1024x576(16:9)。coro(img_rotation)が奇数(90/270度)のときは
        // 縦画像として梱包されているため、見た目のアスペクト比は9:16に反転する。
        // 4:3の枠(Figure)いっぱいにAspectRatioFitterで実際の縦横比まで縮め、
        // 画像自体もその縮んだ後のサイズに合わせて回転・リサイズすることで、
        // 「枠からはみ出さず」「引き伸ばさず」全体を収める。
        private void ApplyRotation(int imgRotation)
        {
            bool isRotated = (((imgRotation % 4) + 4) % 4) % 2 == 1;

            if (figureAspectFitter != null)
            {
                figureAspectFitter.aspectRatio = isRotated ? (9f / 16f) : (16f / 9f);
            }

            // AspectRatioFitterがついているRectTransform(=4:3枠の中で実際に縮んだ箱)の
            // 直後のサイズを取得するため、直ちにレイアウトを再計算する。
            RectTransform fitterRect = figureAspectFitter != null ? figureAspectFitter.GetComponent<RectTransform>() : null;
            if (fitterRect != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(fitterRect);
            }

            Quaternion rotation = Quaternion.Euler(0, 0, imgRotation * -90f);
            Vector2 containerSize = fitterRect != null ? fitterRect.rect.size : Vector2.zero;
            Vector2 imageSize = isRotated ? new Vector2(containerSize.y, containerSize.x) : containerSize;

            Transform[] targets = GetRotationTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                RectTransform rt = targets[i].GetComponent<RectTransform>();
                if (rt != null && fitterRect != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = imageSize;
                    rt.anchoredPosition = Vector2.zero;
                }
                targets[i].localRotation = rotation;
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

        // ---- ポータル ----
        // 旧系統(eterpix_item/eterpix_photo_vew)と同じく、共通ポータル(eterpix_porta_resize)を
        // spawn位置へ親付け替えで呼び寄せる。距離による自動非表示・スケール適用はポータル側が行う。
        public void OpenPortal()
        {
            if (portal == null || _currentPost == null) return;

            string worldId = ReadString(_currentPost, "woid", "");
            if (string.IsNullOrEmpty(worldId)) return;

            Transform spawn = portalSpawnPoint != null ? portalSpawnPoint : transform;
            portal.SetParentObject(spawn, worldId);
        }

        #region Editor
#if !COMPILER_UDONSHARP && UNITY_EDITOR
        // eterpix_monitor_theme.EditorApplyColorsから呼ぶ。エディタ上の読み込み中プレビュー色をテーマに合わせる
        public void EditorSetLoadingPreviewColor(Color color)
        {
            if (image == null || loadingMaterial == null || image.texture != null) return;
            if (image.color == color) return;
            UnityEditor.Undo.RecordObject(image, "Apply theme");
            image.color = color;
            UnityEditor.EditorUtility.SetDirty(image);
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(image);
        }
#endif
        #endregion
    }
}
