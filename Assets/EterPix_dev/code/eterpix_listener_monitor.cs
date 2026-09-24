using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ali.eterpix
{
    // リスナー部品(共通、Aタイプ): 拡大表示用モニター。方針.md「モニターコード指定Aタイプ」参照。
    // 下位セルと同じ回転/UV切り出しロジックを再現し、collage_idを受けてimage loaderに
    // テクスチャを要求する(下位でのApplyTexture相当をここでも行う)。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_listener_monitor : UdonSharpBehaviour
    {
        [SerializeField] private RawImage image;
        [SerializeField] private GameObject[] rotationTargetObjects;

        [Header("デバッグログ")]
        [SerializeField] private eterpix_debug debugLog;

        private eterpix_image_loader _imageLoader;
        private int _currentCollageId = -1;
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
        }

        public void RequestDisplay(int collageId, int imgPos, int imgRotation)
        {
            ReleaseCurrentIfAny();

            ApplyRotation(imgRotation);
            ApplyUvRect(imgPos);

            _currentCollageId = collageId;
            if (_imageLoader != null && collageId >= 0)
            {
                _hasRequestedTexture = true;
                _imageLoader.RequestTexture(collageId, 100, this); // Use起因のため優先度高め
            }
        }

        private void ReleaseCurrentIfAny()
        {
            if (_hasRequestedTexture && _imageLoader != null && _currentCollageId >= 0)
            {
                _imageLoader.ReleaseTexture(_currentCollageId, this);
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

        private void ApplyRotation(int imgRotation)
        {
            Quaternion rotation = Quaternion.Euler(0, 0, imgRotation * -90f);
            Transform[] targets = GetRotationTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
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
    }
}
