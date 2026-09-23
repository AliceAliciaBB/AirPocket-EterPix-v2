using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using TMPro;

namespace ali.eterpix
{
    // U#はネストされた型宣言をサポートしないため、名前空間直下に定義する
    public enum eterpix_orientation { VerticalOnly, HorizontalOnly, Both }

    // 下位(表示セル)。方針.md「下位(表示セル)」を参照。
    // 画像/回転/対応向きの表示のみを担う。Use/レーザー時は自分の情報を
    // 各リスナー部品(キャプション/モニター/ポータル、それぞれ任意)へ横流しするだけ。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_cell : UdonSharpBehaviour
    {
        [Header("画像表示")]
        [SerializeField] private RawImage image;
        [SerializeField] private eterpix_orientation acceptedOrientation = eterpix_orientation.Both;

        [Header("回転対象 (img_rotationに応じて回転させる対象。未指定なら画像自身を回転)")]
        [SerializeField] private GameObject[] rotationTargetObjects;

        [Header("ポータル発生位置 (未指定なら自身のtransform)")]
        [SerializeField] private Transform portalSpawnPoint;
        public Transform PortalSpawnPoint => portalSpawnPoint != null ? portalSpawnPoint : transform;

        [Header("Use時の横流し先 (リスナー部品、それぞれ任意)")]
        [SerializeField] private eterpix_listener_caption listenerCaption;
        [SerializeField] private eterpix_listener_monitor listenerMonitor;
        [SerializeField] private eterpix_listener_portal listenerPortal;

        [Header("常時表示用 (任意。リスナー部品と同じprefabを流用)")]
        [SerializeField] private eterpix_listener_caption selfCaption;
        [SerializeField] private eterpix_listener_monitor selfMonitor;
        [SerializeField] private eterpix_listener_portal selfPortal;

        [Header("位置リセット (任意。VRCObjectSync連携)")]
        [SerializeField] private VRCObjectSync objectSync;

        [Header("デバッグログ")]
        [SerializeField] private eterpix_debug debugLog;

        private eterpix_image_loader _imageLoader;

        private Material _imageDefaultMaterial;
        private Vector3[] _baseLocalPositions;
        private Quaternion[] _baseLocalRotations;
        private Vector3[] _basePivotToCenter;

        private int _index = -1;
        private int _collageId = -1;
        private int _imgPos = -1;
        private int _imgRotation = 0;
        private string _worldId = "";
        private string _description = "";
        private string _userName = "";
        private string _worldName = "";
        private string _worldDescription = "";
        private bool _hasRequestedTexture = false;

        private void Start()
        {
            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<eterpix_debug>();
            }

            GameObject loaderObj = GameObject.Find(eterpix_image_loader.SingletonObjectName);
            if (loaderObj != null) _imageLoader = loaderObj.GetComponent<eterpix_image_loader>();

            if (image != null) _imageDefaultMaterial = image.material;

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

            gameObject.SetActive(false);
        }

        // ---- 中位から呼ばれる対応向き判定 ----
        // 注: img_rotationの偶奇で縦横を近似する(奇数=90/270度回転=縦横反転)。
        // 実際のクロップ矩形の縦横比までは見ていない簡易判定(要検証)。
        public bool AcceptsOrientation(int imgRotation)
        {
            if (acceptedOrientation == eterpix_orientation.Both) return true;

            bool isRotated90or270 = (((imgRotation % 4) + 4) % 4) % 2 == 1;
            if (acceptedOrientation == eterpix_orientation.VerticalOnly) return isRotated90or270;
            return !isRotated90or270; // HorizontalOnly
        }

        // ---- 中位から呼ばれるデータ反映 ----
        public void UpdateItemInfo(int index, int collageId, int imgPos, int imgRotation,
            string description, string userName, string worldName, string worldDescription, string worldId)
        {
            ReleaseCurrentTextureIfAny();

            _index = index;
            _collageId = collageId;
            _imgPos = imgPos;
            _imgRotation = imgRotation;
            _worldId = worldId;
            _description = description;
            _userName = userName;
            _worldName = worldName;
            _worldDescription = worldDescription;

            ApplyRotation(imgRotation);
            ApplyUvRect(imgPos);

            if (_imageLoader != null && collageId >= 0)
            {
                _hasRequestedTexture = true;
                _imageLoader.RequestTexture(collageId, 0, this);
            }

            if (selfCaption != null) selfCaption.ApplyCaption(userName, description, worldName, worldDescription);
            if (selfMonitor != null && collageId >= 0) selfMonitor.RequestDisplay(collageId, imgPos, imgRotation);
            if (selfPortal != null) selfPortal.ApplyPortal(worldId);

            gameObject.SetActive(true);
        }

        private void ReleaseCurrentTextureIfAny()
        {
            if (_hasRequestedTexture && _imageLoader != null && _collageId >= 0)
            {
                _imageLoader.ReleaseTexture(_collageId, this);
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

        // ---- Use/レーザー入口 (2経路とも同じ処理へ) ----
        public void OnCellUseClicked()
        {
            if (debugLog != null) debugLog.Log($"[eterpix_cell] Use clicked. index={_index}");

            if (listenerPortal != null)
            {
                eterpix_porta_resize portalResize = listenerPortal.GetPortalResize();
                if (portalResize != null) portalResize.SetParentObject(PortalSpawnPoint, _worldId);
                listenerPortal.ApplyPortal(_worldId);
            }

            if (listenerCaption != null) listenerCaption.ApplyCaption(_userName, _description, _worldName, _worldDescription);

            if (listenerMonitor != null && _collageId >= 0) listenerMonitor.RequestDisplay(_collageId, _imgPos, _imgRotation);
        }

        public override void OnPickupUseDown()
        {
            OnCellUseClicked();
        }

        // ---- 位置リセット (任意) ----
        public void PosReset()
        {
            if (objectSync != null) objectSync.Respawn();
            ApplyRotation(_imgRotation);
        }

        // ---- 空にする処理 ----
        public void ResetItem()
        {
            ReleaseCurrentTextureIfAny();

            _index = -1;
            _collageId = -1;
            _imgPos = -1;
            _imgRotation = 0;
            _worldId = "";

            if (image != null)
            {
                image.texture = null;
                image.material = _imageDefaultMaterial;
                image.uvRect = new Rect(0, 0, 1, 1);
            }

            Transform[] targets = GetRotationTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                targets[i].localRotation = _baseLocalRotations[i];
                targets[i].localPosition = _baseLocalPositions[i];
            }

            gameObject.SetActive(false);
        }

        public void SetEmptyItemInfo()
        {
            ResetItem();
        }

        // ---- 回転/UV (旧eterpix_photo_vewのロジックを移植) ----
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

        private float GetWidth(Transform target)
        {
            RectTransform rt = target.GetComponent<RectTransform>();
            if (rt != null) return rt.rect.size.x;

            return image != null ? image.rectTransform.rect.size.x : 0f;
        }

        private void ApplyRotation(int imgRotation)
        {
            // 固定向き(Both以外)のセルは編集時に手動で仕込んだ回転を維持するため、
            // img_rotationによる自動回転補正を行わない。
            if (acceptedOrientation != eterpix_orientation.Both) return;

            Quaternion rotation = Quaternion.Euler(0, 0, imgRotation * -90f);

            Transform[] targets = GetRotationTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;

                targets[i].localRotation = _baseLocalRotations[i] * rotation;

                Vector3 c = _basePivotToCenter[i];
                Vector3 shift = _baseLocalRotations[i] * (c - rotation * c);

                // 幅による中心補正はRectTransformのpivotズレを打ち消すためのものなので、
                // RectTransformを持たない対象（例: セル自身のTransform）には適用しない。
                // image.rectTransform.rect.size はCanvasのUI空間(ピクセル)の値であり、
                // それを持たない対象のワールド空間localPositionへそのまま足すと、
                // Canvasのスケール分だけ桁違いに巨大なズレになる（要修正だった実バグ）。
                if (targets[i].GetComponent<RectTransform>() != null &&
                    ((imgRotation % 4) + 4) % 4 % 2 == 1)
                {
                    float w = GetWidth(targets[i]);
                    shift.y = -w / 2f;
                }

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
    }
}
