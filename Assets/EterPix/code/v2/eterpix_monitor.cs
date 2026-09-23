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

        [Header("ポータル(1つずつ、押した本人にのみ見える)")]
        [SerializeField] private VRC.SDK3.Components.VRCPortalMarker portalMarker;
        [SerializeField] private Transform portalSpawnPoint; // 未指定ならこのtransform
        [SerializeField] private float portalAutoCloseDistance = 3f;

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
            if (openPortalButton != null) openPortalButton.onClick.AddListener(OpenPortal);
            if (portalMarker != null) portalMarker.gameObject.SetActive(false);

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
    }
}
