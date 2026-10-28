using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.Data;

namespace ali.eterpix.v2
{
    // EtrPix-Pad: 手に持つタブレット型のギミック。計画書は eterpix_integrated_management の
    // docs/plans/etrpix-pad-plan.md (K-番号はそこの確定事項)。
    // プレイヤーが入力したURL 1本で、文字列取得ならJSON、画像取得なら写真を受け取り、1枚だけ表示する。
    // 役割は1台ぶんの表示・UI・同期だけ。取得と写真の共有はワールドに1つの eterpix_pad_requester が受け持つ(K-61)。
    //
    // このコンポーネントは「範囲用のトリガーBoxCollider」と同じGameObjectに置く
    // (OnPlayerTriggerEnter/ExitはColliderと同じGameObjectにしか届かないため)。
    // 範囲のGameObjectはPickupの外(兄弟)に置き、毎フレームPickupの位置へ追従させる(K-31・K-55)。
    // Pickupの子に入れると遠くから掴めてしまうため、子には入れない。
    //
    // ボタン・スライダー・入力欄のイベントはAddListenerできないため、Inspectorで
    // このUdonBehaviourのSendCustomEventに次を登録する:
    //   入力欄(onEndEdit)=OnUrlEntered / 大きさスライダー=OnSizeSliderChanged / 最小/最大=ToggleSize /
    //   初期位置に固定したリセット=ResetPose / ポータル=TogglePortal / R18のOK=ConfirmR18 / UIの表示切替=ToggleUiPanel
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_pad : UdonSharpBehaviour
    {
        [Header("URL")]
        [SerializeField] private VRCUrlInputField urlInput;
        [Tooltip("誰も入力していない間に表示するURL(Pad用URLでも外部の画像URLでもよい。空なら「URLを入力してください」)")]
        [SerializeField] private VRCUrl initialUrl;
        [Tooltip("EterPix以外の画像URLを表示するか(OFFなら「このURLは表示できません」)")]
        [SerializeField] private bool allowExternalImageUrls = true;

        [Header("R18投稿を一切表示しない(ONなら確認UIを出さずにメッセージだけ出す)")]
        [SerializeField] private bool blockR18Posts = false;

        [Header("Pickup(タブレット本体を持つオブジェクト)")]
        [SerializeField] private VRC_Pickup pickup;
        [SerializeField] private VRCObjectSync objectSync;
        [Tooltip("Pickupの当たり判定。大きさに合わせて、掴まれていない時に更新する")]
        [SerializeField] private BoxCollider pickupCollider;

        [Header("大きさ(左下を起点に右上へ拡大する。pivotが左下の、タブレット本体のルート)")]
        [SerializeField] private Transform scaleRoot;
        [SerializeField] private Slider sizeSlider;
        [Tooltip("最大倍率(起動時の大きさが1倍。1未満は1にする)")]
        [SerializeField] private float maxScale = 3f;
        [Tooltip("最小/最大ボタンの文字(1倍より大きければ「最小」、1倍なら「最大」。K-60)")]
        [SerializeField] private TMP_Text sizeToggleLabel;

        [Header("範囲(このGameObjectのトリガーBoxCollider。未指定なら自分のBoxCollider)")]
        [SerializeField] private BoxCollider rangeCollider;

        [Header("写真(写真枠は16:9。RawImageはその子で、中央に置く)")]
        [SerializeField] private RectTransform imageFrame;
        [SerializeField] private RawImage image;

        [Header("状態表示(タブレット本体の中央)")]
        [SerializeField] private GameObject statusRoot;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private GameObject r18ConfirmRoot;
        [SerializeField] private TMP_Text r18ConfirmText;

        [Header("左下のUIパネル(写真を押すと表示/非表示。ローカル)")]
        [SerializeField] private GameObject uiPanelRoot;
        [Tooltip("投稿情報が無い間のパネルの高さ(URL入力と大きさの2段。パネルは下から積み、pivotは左下)")]
        [SerializeField] private float panelCompactHeight = 96f;

        [Header("投稿情報(EterPixの投稿のときだけ出す)")]
        [SerializeField] private GameObject postInfoRoot;
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text dateText;
        [SerializeField] private TMP_Text worldNameText;

        [Header("ポータル(ワールドに1つだけ置いた共通ポータルを呼び寄せる。押した本人にのみ見える)")]
        [SerializeField] private eterpix_porta_resize portal;
        [SerializeField] private Transform portalSpawnPoint; // 未指定ならこのtransform
        [SerializeField] private Button portalButton;
        [SerializeField] private TMP_Text portalLabel;

        [Header("リセット(誰かが掴んでいる間は「利用中です」を数秒出す)")]
        [SerializeField] private TMP_Text busyText;
        [Tooltip("初期位置に固定したリセットボタンの横の「利用中です」(Padが持ち去られても元の場所からリセットするため)")]
        [SerializeField] private TMP_Text stationBusyText;

        [Header("初期位置(ビルド時にEditorが焼き込む。K-44)")]
        [SerializeField] private bool hasInitialPose = false;
        [SerializeField] private Vector3 initialPosition;
        [SerializeField] private Quaternion initialRotation = Quaternion.identity;

        [Header("デバッグログ")]
        [SerializeField] private ali.eterpix.eterpix_debug debugLog;

        // 状態表示の文言はギミック側で固定する(K-46)
        public const string LoadingMessage = "読み込み中…";
        public const string UntrustedUrlMessage = "「Allow Untrusted URLs」をONにしてください";
        public const string NotFoundMessage = "見つかりませんでした";
        public const string LoadFailedMessage = "読み込みに失敗しました";
        public const string ExternalBlockedMessage = "このURLは表示できません";
        public const string R18ConfirmMessage = "R18の投稿です。表示しますか？";
        public const string R18BlockedMessage = "このワールドではR18投稿は見れません";
        public const string BusyMessage = "利用中です";
        public const string EnterUrlMessage = "URLを入力してください";
        public const string NoWorldMessage = "ワールド情報なし";
        public const string SizeLabelMin = "最小";
        public const string SizeLabelMax = "最大";
        public const string PortalLabelOpen = "ポータルを開く";
        public const string PortalLabelClose = "ポータルを閉じる";

        // このパスを含むURLはPad用(JSON → 写真の順に取得)。それ以外は写真だけを取得する(K-33)
        public const string PadUrlMarker = "/api/vrc/v1/pad/";
        // VRChatの「信頼されていないURLを許可」がOFFのときのエラーに含まれる文字列(downloaderと同じ)
        private const string UntrustedUrlErrorMarker = "Not trusted url hit";

        private const float ScaleSyncInterval = 0.2f;   // K-57
        private const float RangeCheckInterval = 2f;    // K-55
        private const float BusyMessageSeconds = 3f;

        [UdonSynced] private VRCUrl _syncedUrl = VRCUrl.Empty;
        [UdonSynced] private float _syncedScale = 1f;

        private eterpix_pad_requester _requester;
        private bool _initialized = false;

        private bool _isInRange = false;
        private float _nextRangeCheckTime = 0f;

        // 今表示しようとしているURL(古い取得結果を捨てる判定に使う)
        private string _appliedUrl = "";
        private bool _isPadUrl = false;
        private bool _r18Confirmed = false; // ローカルのみ。URLが変わったら戻す(K-24)
        private DataDictionary _currentPost;
        private DataDictionary _currentWorldData;
        private DataDictionary _currentWorldPrefixTable; // ルートのwopt(印のID→文字列)
        private Texture2D _texture;
        private bool _portalShouldBeOpen = false;
        private string _shownWorldId = "";

        private Vector3 _baseScale = Vector3.one;
        private Vector3 _baseColliderSize;
        private Vector3 _baseColliderCenter;
        private Vector3 _scalePivotInCollider; // 拡大の起点(scaleRootの位置)をColliderのローカル座標で
        private float _appliedScale = 1f;
        private bool _colliderDirty = false;
        private bool _suppressSliderEvent = false;
        private float _lastScaleSyncTime = -100f;
        private bool _scaleSyncScheduled = false;

        private float _busyHideTime = 0f;

        private RectTransform _panelRect;
        private BoxCollider _panelCollider;
        private float _panelFullHeight;

        private void Start()
        {
            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(ali.eterpix.eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<ali.eterpix.eterpix_debug>();
            }
            if (portal == null)
            {
                GameObject portalObj = GameObject.Find(eterpix_monitor.PortalObjectName);
                if (portalObj != null) portal = portalObj.GetComponent<eterpix_porta_resize>();
            }
            if (rangeCollider == null) rangeCollider = GetComponent<BoxCollider>();
            if (maxScale < 1f) maxScale = 1f; // K-19

            if (scaleRoot != null) _baseScale = scaleRoot.localScale;
            if (pickupCollider != null)
            {
                _baseColliderSize = pickupCollider.size;
                _baseColliderCenter = pickupCollider.center;
            }
            if (sizeSlider != null)
            {
                _suppressSliderEvent = true;
                sizeSlider.minValue = 1f;
                sizeSlider.maxValue = maxScale;
                sizeSlider.value = Mathf.Clamp(_syncedScale, 1f, maxScale);
                _suppressSliderEvent = false;
            }

            if (r18ConfirmText != null) r18ConfirmText.text = R18ConfirmMessage;
            if (busyText != null) busyText.gameObject.SetActive(false);
            if (stationBusyText != null) stationBusyText.gameObject.SetActive(false);
            if (uiPanelRoot != null)
            {
                uiPanelRoot.SetActive(true); // 初期状態は表示(K-36)
                _panelRect = uiPanelRoot.GetComponent<RectTransform>();
                _panelCollider = uiPanelRoot.GetComponent<BoxCollider>();
                if (_panelRect != null) _panelFullHeight = _panelRect.sizeDelta.y;
            }
            ClearContent();

            // 要求者のStart(重複の判定)が済んでから探すため、1フレーム遅らせる(requesterと同じ)
            SendCustomEventDelayedFrames(nameof(Initialize), 1);
        }

        public void Initialize()
        {
            // 取得はワールドに1つの要求者に頼む(K-61。同じURLを読むPad同士で取得と写真を共有する)
            GameObject requesterObj = GameObject.Find(eterpix_pad_requester.SingletonObjectName);
            if (requesterObj != null) _requester = requesterObj.GetComponent<eterpix_pad_requester>();
            if (_requester == null) Debug.LogError($"[eterpix_pad] {eterpix_pad_requester.SingletonObjectName} が見つかりません。「GameObject/EterPix/Padを配置」で置くか、Prefabs/EterpixPadRequester をワールドに1つ置いてください");

            // 拡大の起点(本体の左下)を、掴むColliderのローカル座標で覚える。本体はPickupの子ではなく
            // VRCParentConstraintで追従する(K-59)ため、Constraintが一度効いた後(このフレーム)に測る
            if (pickupCollider != null)
            {
                _scalePivotInCollider = scaleRoot != null
                    ? pickupCollider.transform.InverseTransformPoint(scaleRoot.position)
                    : _baseColliderCenter;
            }

            _initialized = true;
            _appliedUrl = GetEffectiveUrlString();
            ApplyScale(_syncedScale);
            UpdateSliderValue();

            // 初期化前に届いたEnterで既に範囲内なら、確かめ直しても変化が無く取得が始まらないため、ここで始める
            // (範囲外 → 範囲内に変わった場合はSetInRangeの中で始まる)
            bool wasInRange = _isInRange;
            CheckLocalPlayerInRange();
            if (wasInRange && _isInRange) BeginLoad();
        }

        private void Update()
        {
            // 範囲をPadの位置だけに追従させる(向きは回さない。K-55)
            if (pickup != null) transform.position = pickup.transform.position;

            if (_colliderDirty && (pickup == null || !pickup.IsHeld)) ApplyColliderSize(); // K-21

            if (_initialized && Time.time >= _nextRangeCheckTime)
            {
                _nextRangeCheckTime = Time.time + RangeCheckInterval;
                CheckLocalPlayerInRange();
            }

            if (Time.time >= _busyHideTime)
            {
                if (busyText != null && busyText.gameObject.activeSelf) busyText.gameObject.SetActive(false);
                if (stationBusyText != null && stationBusyText.gameObject.activeSelf) stationBusyText.gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            SetInRange(false);
        }

        private void OnEnable()
        {
            if (_initialized) _nextRangeCheckTime = 0f; // 次のUpdateで範囲を確かめ直す
        }

        // ---- 範囲(K-30〜K-32・K-55) ----
        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            SetInRange(true);
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            SetInRange(false);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            _nextRangeCheckTime = 0f;
        }

        // OnPlayerTriggerEnterは範囲内でスポーンした時や、範囲の側が動いた時に届かないことがあるため、
        // 範囲の箱と自分の体(PlayerLocalレイヤー)が重なっているかを数秒おきに確かめる(requesterと同じ方法)
        public void CheckLocalPlayerInRange()
        {
            if (!_initialized || !gameObject.activeInHierarchy) return;
            if (rangeCollider == null || !rangeCollider.enabled) return;

            Transform t = rangeCollider.transform;
            Vector3 center = t.TransformPoint(rangeCollider.center);
            Vector3 halfExtents = Vector3.Scale(rangeCollider.size, t.lossyScale) * 0.5f;
            int layerMask = 1 << LayerMask.NameToLayer("PlayerLocal");
            bool inside = Physics.CheckBox(center, halfExtents, t.rotation, layerMask, QueryTriggerInteraction.Collide);
            SetInRange(inside);
        }

        private void SetInRange(bool inRange)
        {
            if (_isInRange == inRange) return;
            _isInRange = inRange;
            if (!_initialized) return;

            if (inRange)
            {
                // 範囲外にいる間に変わったURLも、ここで今の同期値を取得する(K-32)
                _appliedUrl = GetEffectiveUrlString();
                BeginLoad();
            }
            else
            {
                // 範囲外の人には何も出さず、写真も手放す(K-30・K-46。要求者への返却はClearContentの中)
                ClearContent();
            }
        }

        // ---- URL(K-7・K-11・K-47) ----
        public void OnUrlEntered()
        {
            if (urlInput == null) return;
            VRCUrl url = urlInput.GetUrl();
            string urlString = url != null ? url.Get() : "";
            urlInput.SetUrl(VRCUrl.Empty);
            if (string.IsNullOrEmpty(urlString)) return;

            // 同じURLの入れ直しは、自分だけ取り直す(Untrustedを許可した後の再試行など)
            if (urlString == _appliedUrl)
            {
                if (_initialized && _isInRange) BeginLoad();
                return;
            }

            // 誰でも書き換えられる。押した人がオーナーになる(K-34)
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            _syncedUrl = url;
            RequestSerialization();
            OnEffectiveUrlChanged();
        }

        public override void OnDeserialization()
        {
            ApplyScale(_syncedScale);
            UpdateSliderValue();
            OnEffectiveUrlChanged();
        }

        // 誰かが入力したURLがあればそれ、無ければ初期表示のURL(K-47)
        private VRCUrl GetEffectiveUrl()
        {
            if (_syncedUrl != null && !string.IsNullOrEmpty(_syncedUrl.Get())) return _syncedUrl;
            return initialUrl;
        }

        private string GetEffectiveUrlString()
        {
            VRCUrl url = GetEffectiveUrl();
            return url != null ? url.Get() : "";
        }

        private void OnEffectiveUrlChanged()
        {
            string urlString = GetEffectiveUrlString();
            if (urlString == _appliedUrl) return;

            _appliedUrl = urlString;
            _r18Confirmed = false;
            if (_initialized && _isInRange) BeginLoad();
        }

        // ---- 取得(K-6・K-12・K-33・B-1a) ----
        private void BeginLoad()
        {
            ClearContent();

            VRCUrl url = GetEffectiveUrl();
            string urlString = url != null ? url.Get() : "";
            if (string.IsNullOrEmpty(urlString))
            {
                ShowStatus(EnterUrlMessage);
                return;
            }

            _isPadUrl = urlString.Contains(PadUrlMarker);
            if (_isPadUrl)
            {
                // まずJSONを取得し、R18判定・表示可否を決めてから写真を取得する
                ShowStatus(LoadingMessage);
                RequestString(url);
            }
            else if (!allowExternalImageUrls)
            {
                ShowStatus(ExternalBlockedMessage);
            }
            else
            {
                // EterPix以外のURLは写真だけ(テキスト・ポータルは出さない)
                ShowStatus(LoadingMessage);
                RequestImage(url);
            }
        }

        private void RequestString(VRCUrl url)
        {
            if (_requester != null) _requester.RequestString(url, this);
            else ShowStatus(LoadFailedMessage);
        }

        private void RequestImage(VRCUrl url)
        {
            if (_requester != null) _requester.RequestImage(url, this);
            else ShowStatus(LoadFailedMessage);
        }

        // ---- 取得結果(要求者から、同じURLを待っているPad全員に届く) ----
        public void OnPadStringSuccess(string url, string text)
        {
            if (!IsCurrentRequest(url)) return;

            if (!VRCJson.TryDeserializeFromJson(text, out DataToken token) || token.TokenType != TokenType.DataDictionary)
            {
                ShowStatus(LoadFailedMessage);
                return;
            }

            DataDictionary root = token.DataDictionary;
            if (root.ContainsKey("erro"))
            {
                ShowStatus(ReadString(root, "erro", "") == "not_found" ? NotFoundMessage : LoadFailedMessage);
                return;
            }

            // postはv1と同じ配列で、要素は常に1件(K-27)
            if (!root.TryGetValue("post", out DataToken postList) || postList.TokenType != TokenType.DataList ||
                !postList.DataList.TryGetValue(0, out DataToken postToken) || postToken.TokenType != TokenType.DataDictionary)
            {
                ShowStatus(LoadFailedMessage);
                return;
            }

            _currentPost = postToken.DataDictionary;
            _currentWorldData = null;
            _currentWorldPrefixTable = null;
            if (root.TryGetValue("wopt", out DataToken woptToken) && woptToken.TokenType == TokenType.DataDictionary)
            {
                _currentWorldPrefixTable = woptToken.DataDictionary;
            }
            if (root.TryGetValue("woda", out DataToken wodaToken) && wodaToken.TokenType == TokenType.DataDictionary)
            {
                _currentWorldData = wodaToken.DataDictionary;
            }

            // R18(K-23〜K-26)。OKを押すまで写真もテキストも出さず、写真の取得もしない
            if (ReadInt(_currentPost, "po18", 0) == 1)
            {
                if (blockR18Posts)
                {
                    ShowStatus(R18BlockedMessage);
                    return;
                }
                if (!_r18Confirmed)
                {
                    ShowR18Confirm();
                    return;
                }
            }

            ShowPostAndLoadImage();
        }

        public void OnPadStringError(string url, int errorCode, string error)
        {
            if (!IsCurrentRequest(url)) return;
            if (debugLog != null) debugLog.LogWarning($"[eterpix_pad] String load error ({errorCode}): {error}");

            if (!string.IsNullOrEmpty(error) && error.Contains(UntrustedUrlErrorMarker)) ShowStatus(UntrustedUrlMessage);
            else if (errorCode == 404) ShowStatus(NotFoundMessage);
            else ShowStatus(LoadFailedMessage);
        }

        public void OnPadImageSuccess(string url, Texture2D texture)
        {
            // 古い要求の結果・範囲外に出た後の結果・R18の確認前の結果は貼らない。
            // 写真は要求者が他のPadと共有しているため、ここでは破棄しない(手放すのはReleaseTextureでまとめて行う)
            if (!IsCurrentRequest(url) || (_isPadUrl && _currentPost == null)) return;

            DetachTexture();
            _texture = texture;
            if (image != null)
            {
                image.texture = texture;
                image.color = Color.white;
                image.enabled = texture != null;
            }
            ApplyImageLayout(_isPadUrl ? ReadInt(_currentPost, "poro", 0) : 0);
            HideStatus();
        }

        public void OnPadImageError(string url, string error)
        {
            if (!IsCurrentRequest(url)) return;
            if (debugLog != null) debugLog.LogWarning($"[eterpix_pad] Image load error: {error}");

            if (!string.IsNullOrEmpty(error) && error.Contains(UntrustedUrlErrorMarker)) ShowStatus(UntrustedUrlMessage);
            else ShowStatus(LoadFailedMessage);
        }

        private bool IsCurrentRequest(string url)
        {
            return _initialized && _isInRange && url == _appliedUrl;
        }

        // ---- R18の確認(ローカルのみ。K-24) ----
        private void ShowR18Confirm()
        {
            HideStatus();
            if (r18ConfirmRoot != null) r18ConfirmRoot.SetActive(true);
        }

        public void ConfirmR18()
        {
            if (_currentPost == null) return;
            _r18Confirmed = true;
            if (r18ConfirmRoot != null) r18ConfirmRoot.SetActive(false);
            ShowPostAndLoadImage();
        }

        // ---- 投稿の表示(K-35・K-42) ----
        private void ShowPostAndLoadImage()
        {
            string worldId = ReadString(_currentPost, "woid", "");

            SetPostInfoVisible(true);
            if (userNameText != null) userNameText.text = ReadString(_currentPost, "usna", "");
            if (descriptionText != null) descriptionText.text = ReadString(_currentPost, "pode", "");
            if (dateText != null) dateText.text = FormatDate(_currentPost);
            if (worldNameText != null)
            {
                string worldName = ResolveWorldName(worldId);
                worldNameText.text = string.IsNullOrEmpty(worldId) ? NoWorldMessage : worldName;
            }
            ApplyWorldContext(worldId);

            ShowStatus(LoadingMessage);
            RequestImage(GetEffectiveUrl());
        }

        // pocaはUTCのcreated_atから作ったUNIX秒。プレイヤーのローカル時刻に直して日付だけ出す(K-35)
        private string FormatDate(DataDictionary post)
        {
            if (!post.TryGetValue("poca", out DataToken token) || token.TokenType != TokenType.Double) return "";
            System.DateTime utc = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(token.Double);
            return utc.ToLocalTime().ToString("yyyy/MM/dd");
        }

        private string ResolveWorldName(string worldId)
        {
            if (_currentWorldData == null || string.IsNullOrEmpty(worldId)) return "";
            if (_currentWorldData.TryGetValue(worldId, out DataToken entry) && entry.TokenType == TokenType.DataDictionary)
            {
                string worldName = ReadString(entry.DataDictionary, "wona", "");
                if (string.IsNullOrEmpty(worldName)) return "";
                return BuildWorldPrefix(_currentPost, _currentWorldPrefixTable) + worldName;
            }
            return "";
        }

        // 追記などの印(D-057). 投稿のwopf(印のIDの配列)の順に、ルートのwopt(ID→文字列)を引いて結合する。
        // woptに無いID・数値でないIDは読み飛ばす(古いJSONや、サーバーが後から足した印でも壊れない)
        private string BuildWorldPrefix(DataDictionary post, DataDictionary prefixTable)
        {
            if (post == null || prefixTable == null) return "";
            if (!post.TryGetValue("wopf", out DataToken listToken) || listToken.TokenType != TokenType.DataList) return "";

            DataList ids = listToken.DataList;
            string prefix = "";
            for (int i = 0; i < ids.Count; i++)
            {
                if (!ids.TryGetValue(i, out DataToken idToken) || idToken.TokenType != TokenType.Double) continue;
                if (prefixTable.TryGetValue(((int)idToken.Double).ToString(), out DataToken textToken) && textToken.TokenType == TokenType.String)
                {
                    prefix += textToken.String;
                }
            }
            return prefix;
        }

        // ---- 表示の切り替え ----
        // 写真・テキスト・確認UIを消し、写真を解放する(状態表示も消す)
        private void ClearContent()
        {
            ReleaseTexture();
            _currentPost = null;
            _currentWorldData = null;
            _currentWorldPrefixTable = null;
            SetPostInfoVisible(false);
            if (r18ConfirmRoot != null) r18ConfirmRoot.SetActive(false);
            HideStatus();
            HidePortalTemporarily();
        }

        // 写真が出ていない間のメッセージ(タブレット本体の中央。K-46)
        private void ShowStatus(string message)
        {
            if (statusRoot != null) statusRoot.SetActive(true);
            if (statusText != null) statusText.text = message;
            // 投稿が決まる前のエラーではテキスト・ポータルも出さない
            if (_currentPost == null)
            {
                SetPostInfoVisible(false);
                HidePortalTemporarily();
            }
        }

        // 投稿情報が無い間は、パネルをURL入力と大きさの2段に縮める(背景とUI操作の判定も縮め、
        // 空いた写真の部分を押してUIを切り替えられるようにする)。パネルは下から積んであり、pivotは左下
        private void SetPostInfoVisible(bool visible)
        {
            if (postInfoRoot != null) postInfoRoot.SetActive(visible);
            if (_panelRect == null) return;

            float height = visible ? _panelFullHeight : Mathf.Min(panelCompactHeight, _panelFullHeight);
            _panelRect.sizeDelta = new Vector2(_panelRect.sizeDelta.x, height);
            if (_panelCollider != null)
            {
                Vector3 size = _panelCollider.size;
                _panelCollider.size = new Vector3(size.x, height, size.z);
                _panelCollider.center = new Vector3(_panelCollider.center.x, height / 2f, _panelCollider.center.z);
            }
        }

        private void HideStatus()
        {
            if (statusRoot != null) statusRoot.SetActive(false);
        }

        // 写真と取得中の要求を要求者へまとめて返す(URLを変えた時・範囲を出た時)。
        // 写真は他のPadと共有しているため自分では破棄せず、誰も使わなくなった時に要求者が破棄する(K-61)
        private void ReleaseTexture()
        {
            DetachTexture();
            if (_requester != null) _requester.ReleaseAll(this);
        }

        // RawImageから外すだけ(共有の写真なので破棄はしない)
        private void DetachTexture()
        {
            if (image != null)
            {
                image.texture = null;
                image.enabled = false;
            }
            _texture = null;
        }

        // ---- 写真の向きと大きさ(K-4・K-38・K-58・B-2a・B-2c) ----
        // pororで回した後の縦横がタブレットの縦横と食い違うときは、さらに90°回して長辺どうしを合わせる。
        // 写真の縦横比は保ったまま、写真枠(16:9)に収める
        private void ApplyImageLayout(int poro)
        {
            if (image == null || imageFrame == null || _texture == null) return;

            int rotation = ((poro % 4) + 4) % 4;
            float w = _texture.width;
            float h = _texture.height;
            Vector2 frame = imageFrame.rect.size;

            bool rotatedByPoro = rotation % 2 == 1;
            float shownW = rotatedByPoro ? h : w;
            float shownH = rotatedByPoro ? w : h;
            // 足す90°は反時計回り。poroが奇数のとき、時計回りに足すと合計180°(上下逆さ)になるため
            if ((shownW >= shownH) != (frame.x >= frame.y)) rotation = (rotation + 3) % 4;

            bool rotated = rotation % 2 == 1;
            float fitW = rotated ? h : w;
            float fitH = rotated ? w : h;
            float scale = Mathf.Min(frame.x / fitW, frame.y / fitH);

            RectTransform rt = image.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w * scale, h * scale); // 回す前の向きでの大きさ
            rt.localRotation = Quaternion.Euler(0, 0, rotation * -90f);
        }

        // ---- 大きさ(K-15〜K-21・K-57) ----
        public void OnSizeSliderChanged()
        {
            if (_suppressSliderEvent || sizeSlider == null) return;
            SetScaleByLocalPlayer(sizeSlider.value);
        }

        // 最小/最大ボタン(K-60): 1倍より少しでも大きければ1倍に、1倍なら最大倍率にする
        public void ToggleSize()
        {
            SetScaleByLocalPlayer(IsMinScale() ? maxScale : 1f);
            UpdateSliderValue();
        }

        private bool IsMinScale()
        {
            return _appliedScale <= 1f + 0.001f;
        }

        // 操作した人がオーナーになって書き換える(K-34)
        private void SetScaleByLocalPlayer(float requested)
        {
            float value = Mathf.Clamp(requested, 1f, maxScale);
            if (Mathf.Approximately(value, _syncedScale)) return;

            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
            _syncedScale = value;
            ApplyScale(value);

            // 他の人へは最大0.2秒に1回送る。最後の値も必ず送る
            float wait = ScaleSyncInterval - (Time.time - _lastScaleSyncTime);
            if (wait <= 0f)
            {
                FlushScaleSync();
            }
            else if (!_scaleSyncScheduled)
            {
                _scaleSyncScheduled = true;
                SendCustomEventDelayedSeconds(nameof(FlushScaleSync), wait);
            }
        }

        public void FlushScaleSync()
        {
            _scaleSyncScheduled = false;
            _lastScaleSyncTime = Time.time;
            if (Networking.IsOwner(Networking.LocalPlayer, gameObject)) RequestSerialization();
        }

        // 見た目はすぐ変え、当たり判定は掴まれていない時に合わせる(K-21)
        private void ApplyScale(float value)
        {
            value = Mathf.Clamp(value, 1f, maxScale);
            _appliedScale = value;
            if (scaleRoot != null) scaleRoot.localScale = _baseScale * value;
            _colliderDirty = true;
            if (sizeToggleLabel != null) sizeToggleLabel.text = IsMinScale() ? SizeLabelMax : SizeLabelMin;
        }

        private void ApplyColliderSize()
        {
            _colliderDirty = false;
            if (pickupCollider == null) return;
            pickupCollider.size = _baseColliderSize * _appliedScale;
            // 左下(scaleRootの位置)を起点に広げる
            pickupCollider.center = _scalePivotInCollider + (_baseColliderCenter - _scalePivotInCollider) * _appliedScale;
        }

        private void UpdateSliderValue()
        {
            if (sizeSlider == null) return;
            _suppressSliderEvent = true;
            sizeSlider.value = _appliedScale;
            _suppressSliderEvent = false;
        }

        // ---- 初期位置に戻す(K-44・K-45) ----
        // VRCObjectSync.Respawn()は使わない(動かした人がオーナーのままだと戻らないことがあるため)
        public void ResetPose()
        {
            if (pickup != null && pickup.IsHeld)
            {
                ShowBusy();
                return;
            }

            if (pickup != null && hasInitialPose)
            {
                Networking.SetOwner(Networking.LocalPlayer, pickup.gameObject);
                pickup.transform.SetPositionAndRotation(initialPosition, initialRotation);
                if (objectSync != null) objectSync.FlagDiscontinuity();
            }

            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
            _syncedScale = 1f;
            ApplyScale(1f);
            ApplyColliderSize(); // 掴まれていないので即時
            UpdateSliderValue();
            FlushScaleSync();
        }

        // 指定されている欄すべてに出す(現在のprefabでは固定ボタン横のstationBusyTextだけ)
        private void ShowBusy()
        {
            if (busyText != null)
            {
                busyText.text = BusyMessage;
                busyText.gameObject.SetActive(true);
            }
            if (stationBusyText != null)
            {
                stationBusyText.text = BusyMessage;
                stationBusyText.gameObject.SetActive(true);
            }
            _busyHideTime = Time.time + BusyMessageSeconds;
        }

        // ---- UIパネルの表示切替(ローカル。写真だけが残る。K-36) ----
        public void ToggleUiPanel()
        {
            if (uiPanelRoot != null) uiPanelRoot.SetActive(!uiPanelRoot.activeSelf);
        }

        // ---- ポータル(K-13・K-42。monitorと同じ制御) ----
        public void TogglePortal()
        {
            if (portal == null || _currentPost == null) return;

            string worldId = ReadString(_currentPost, "woid", "");
            if (string.IsNullOrEmpty(worldId)) return;

            if (_portalShouldBeOpen)
            {
                _portalShouldBeOpen = false;
                portal.Close();
            }
            else
            {
                _portalShouldBeOpen = true;
                portal.SetParentObjectWithOpenerSized(GetPortalSpawn(), worldId, this, GetPortalSize());
            }
            UpdatePortalLabel();
        }

        // eterpix_porta_resize から SendCustomEvent で呼ばれる(距離で消えた/別のモニターが開いた/Close)
        public void OnPortalStateChanged()
        {
            _portalShouldBeOpen = false;
            UpdatePortalLabel();
        }

        // 写真が切り替わった時、開いているポータルは次の投稿にワールドがあれば行き先を差し替え、無ければ閉じる
        private void ApplyWorldContext(string worldId)
        {
            bool hasWorld = !string.IsNullOrEmpty(worldId);
            if (portalButton != null) portalButton.gameObject.SetActive(hasWorld);

            if (portal != null && _portalShouldBeOpen)
            {
                Transform spawn = GetPortalSpawn();
                if (!hasWorld)
                {
                    _portalShouldBeOpen = false;
                    portal.Close();
                }
                else if (portal.IsOpenAt(spawn))
                {
                    if (worldId != _shownWorldId) portal.ChangeWorld(worldId);
                }
                else
                {
                    portal.SetParentObjectWithOpenerSized(spawn, worldId, this, GetPortalSize());
                }
            }
            _shownWorldId = worldId;
            UpdatePortalLabel();
        }

        // 読み込み中・エラー・範囲外の間はポータルを隠す(_portalShouldBeOpenは維持し、投稿が出たら戻す)
        private void HidePortalTemporarily()
        {
            if (portal != null && _portalShouldBeOpen && portal.IsOpenAt(GetPortalSpawn())) portal.HideTemporarily();
        }

        private Transform GetPortalSpawn()
        {
            return portalSpawnPoint != null ? portalSpawnPoint : transform;
        }

        // ポータルの大きさはワールドに1つの要求者の Inspector で決める(0ならポータル側の siz_value)
        private float GetPortalSize()
        {
            return _requester != null ? _requester.PortalSize : 0f;
        }

        private void UpdatePortalLabel()
        {
            if (portalLabel == null) return;
            portalLabel.text = _portalShouldBeOpen ? PortalLabelClose : PortalLabelOpen;
        }

        // ---- JSONの読み取り ----
        private int ReadInt(DataDictionary data, string key, int fallback)
        {
            if (data == null || !data.TryGetValue(key, out DataToken token)) return fallback;
            if (token.TokenType == TokenType.Double) return (int)token.Double;
            if (token.TokenType == TokenType.String && int.TryParse(token.String, out int parsed)) return parsed;
            return fallback;
        }

        private string ReadString(DataDictionary data, string key, string fallback)
        {
            if (data != null && data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.String) return token.String;
            return fallback;
        }
    }
}
