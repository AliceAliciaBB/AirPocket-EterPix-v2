using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;
using TMPro;

namespace ali.eterpix
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_photo_vew : UdonSharpBehaviour
    {
        [Header("親（画像取得・des/ポータル表示を委譲する先）")]
        [SerializeField] private eterpix_photo_get parentInstance;

        [Header("UI要素")]
        [SerializeField] private RawImage Image;
        public RawImage ItemImage => Image;

        [Header("img_rotationに合わせて回転させる対象（未指定ならImage自身を回転させる）")]
        [SerializeField] private GameObject[] rotationTargetObjects;

        [Header("裏面キャプション（撮影情報）表示用テキスト")]
        [SerializeField] private TMP_Text captionText;

        [Header("ポータルを飛ばす位置（未指定なら自身のtransform）")]
        [SerializeField] private Transform portalSpawnPoint;
        public Transform PortalSpawnPoint => portalSpawnPoint != null ? portalSpawnPoint : transform;

        private Material _imageDefaultMaterial;
        private Vector3[] _baseLocalPositions;
        private Quaternion[] _baseLocalRotations;
        private Vector3[] _basePivotToCenter;
        private int _index = -1;
        private int _collageId = -1;
        private string _worldId = "";
        private int _currentImgRotation = 0;

        [SerializeField]private VRCObjectSync VRCObjectSync;

        private void Start()
        {
            _imageDefaultMaterial = Image.material;

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

            // 初期状態は非表示（データがロードされるまで隠す）
            gameObject.SetActive(false);
        }
        public void pos_reset(){
            VRCObjectSync.Respawn();
            ApplyRotation(_currentImgRotation);
        }


        /// <summary>
        /// 回転対象を返す。rotationTargetObjects が未指定の場合はImage自身を対象にする（後方互換）。
        /// 対象はRectTransformを持たない通常のTransformでもよい。
        /// </summary>
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

            return Image != null ? new[] { Image.transform } : new Transform[0];
        }

        /// <summary>
        /// 対象のpivotから矩形中心までのオフセット（対象自身のローカル座標系）を求める。
        /// pivotが中心(0.5,0.5)からずれているほど、回転時に中心が弧を描いてズレて見えるため、
        /// このオフセットを使ってズレを打ち消す（UpdateItemInfo内の shift 計算を参照）。
        /// RectTransformを持たない対象はpivotの概念が無いため補正不要（ゼロ）とする。
        /// </summary>
        private Vector3 GetPivotToCenter(Transform target)
        {
            RectTransform rt = target.GetComponent<RectTransform>();
            if (rt == null) return Vector3.zero;

            Vector2 size = rt.rect.size;
            Vector2 pivot = rt.pivot;
            return new Vector3(size.x * (0.5f - pivot.x), size.y * (0.5f - pivot.y), 0f);
        }

        /// <summary>
        /// 対象の横幅を取得する。RectTransformを持たない場合はImageの横幅を代わりに使う。
        /// </summary>
        private float GetWidth(Transform target)
        {
            RectTransform rt = target.GetComponent<RectTransform>();
            if (rt != null) return rt.rect.size.x;

            return Image != null ? Image.rectTransform.rect.size.x : 0f;
        }

        /// <summary>
        /// img_rotationに応じてrotationTargetsのlocalRotation/localPositionを更新する。
        /// pos_reset()でのRespawn後の再適用にも使うため、現在のimgRotationを_currentImgRotationへ保持する。
        /// </summary>
        private void ApplyRotation(int imgRotation)
        {
            _currentImgRotation = imgRotation;

            // img_rotation: 0=そのまま,1=時計回り90度,2=180度,3=270度。
            // UnityのZ回転は正値が反時計回りに見えるため符号を反転する。
            Quaternion rotation = Quaternion.Euler(0, 0, imgRotation * -90f);

            Transform[] targets = GetRotationTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;

                // 反転演出などで対象が既に持っている基準回転を保ったまま、その上にZ回転を合成する。
                targets[i].localRotation = _baseLocalRotations[i] * rotation;

                // pivotが矩形の中心からずれている場合、回転だけを行うと矩形の中心がpivotを軸に
                // 弧を描いて動いて見える（例：pivot(0.5,1)＝上端固定だと中心が振れる）。
                // 「回転前と同じ位置に中心が留まる」ように、pivot→中心オフセットを
                // 基準回転・今回のZ回転それぞれで回してから差分をlocalPositionに加える。
                Vector3 c = _basePivotToCenter[i];
                Vector3 shift = _baseLocalRotations[i] * (c - rotation * c);

                // 縦向き（90°/270°）は元の横幅がそのまま縦の長さになるため、
                // その半分だけ追加で下げて中心を合わせる。
                // ただしこの補正はRectTransformのpivotズレ前提のため、RectTransformを
                // 持たない対象（例: 自身のTransform）に適用すると、Canvasのスケールを
                // 無視したピクセル単位の値がそのままワールド座標に加算され、巨大にズレる。
                if (targets[i].GetComponent<RectTransform>() != null &&
                    ((imgRotation % 4) + 4) % 4 % 2 == 1)
                {
                    float w = GetWidth(targets[i]);
                    shift.y = -w / 2f;
                }

                targets[i].localPosition = _baseLocalPositions[i] + shift;
            }
        }

        /// <summary>
        /// 親（eterpix_photo_get）から呼ばれ、自分自身を登録する。
        /// 子から GetComponentInParent で親を探す方式は、GameObjectが非アクティブな間は
        /// Udon実行時に正しく動作しないため、親から能動的に教える方式にしている。
        /// </summary>
        public void SetParent(eterpix_photo_get parent)
        {
            parentInstance = parent;
        }

        /// <summary>
        /// 親から index・collage_id・img_pos・img_rotation・撮影情報（説明文・撮影者名・ワールド名）を受け取り、
        /// 画像表示および裏面キャプション表示に反映する。
        /// DataDictionary本体は受け取らない（親が持つデータから必要な文字列だけを渡してもらう）。
        /// </summary>
        public void UpdateItemInfo(int index, int collageId, int imgPos, int imgRotation, string description, string userName, string worldName, string worldId)
        {
            UpdateCaption(description, userName, worldName);
            _index = index;
            _collageId = collageId;
            _worldId = worldId;

            ApplyRotation(imgRotation);

            if (Image != null)
            {
                // img_pos: コラージュ内の位置(1〜6)。配置は 1 2 / 3 4 / 5 6
                const float cellW = 0.5f;
                const float cellH = 1f / 3f;
                if (imgPos >= 1 && imgPos <= 6)
                {
                    int col = (imgPos - 1) % 2;
                    int row = (imgPos - 1) / 2; // 0=上段, 1=中段, 2=下段
                    float uvX = col * cellW;
                    float uvY = 1f - (row + 1) * cellH;
                    Image.uvRect = new Rect(uvX, uvY, cellW, cellH);
                }
            }

            if (parentInstance != null && _collageId >= 0)
            {
                TextureManager textureManager = parentInstance.GetTextureManager();
                if (textureManager != null)
                {
                    textureManager.RequestTexture(_collageId, this);
                }
            }

            // データがセットされたのでオブジェクトを表示する
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 裏面キャプション（撮影情報）を組み立てて表示する。
        /// 説明文・撮影者名・ワールド名のうち、空でないものだけを行として並べる。
        /// </summary>
        private void UpdateCaption(string description, string userName, string worldName)
        {
            if (captionText == null) return;

            string caption = "";

            if (!string.IsNullOrEmpty(description))
            {
                caption += description;
            }

            if (!string.IsNullOrEmpty(userName))
            {
                if (caption.Length > 0) caption += "\n";
                caption += "撮影者: " + userName;
            }

            if (!string.IsNullOrEmpty(worldName))
            {
                if (caption.Length > 0) caption += "\n";
                caption += "@ " + worldName;
            }

            captionText.text = caption;
        }

        public void ApplyTexture(Texture2D texture)
        {
            if (texture != null && Image != null)
            {
                Image.material = null;
                Image.texture = texture;
            }
        }

        /// <summary>
        /// ボタンから呼ばれる。自分のindexを渡して親の des_portal_view を呼ぶ。
        /// キャプション表示・ワールド情報解決・ポータル操作は行わず、すべて親に委譲する。
        /// </summary>
        public void OnDesPortalViewClicked()
        {
            Debug.Log($"[eterpix_photo_vew] OnDesPortalViewClicked called. index={_index} parentInstance={(parentInstance != null ? parentInstance.name : "null")}");

            if (parentInstance != null)
            {
                // getから継承したポータル本体(eterpix_porta_resize)を、自分の位置へ直接飛ばす
                eterpix_porta_resize portalResize = parentInstance.GetPortalResize();
                if (portalResize != null)
                {
                    portalResize.SetParentObject(PortalSpawnPoint, _worldId);
                }

                parentInstance.des_portal_view(_index);
            }
            else
            {
                Debug.LogWarning("[eterpix_photo_vew] parentInstance is null, cannot call des_portal_view");
            }
        }

        /// <summary>
        /// VRC Pickupとしてuse（トリガー等）された場合もクリック時と同じ挙動にする。
        /// このGameObjectにVRC PickupコンポーネントがアタッチされているUdon手動オブジェクト向け。
        /// </summary>
        public override void OnPickupUseDown()
        {
            OnDesPortalViewClicked();
        }

        public void SetEmptyItemInfo()
        {
            _index = -1;
            _collageId = -1;
            _worldId = "";
            _currentImgRotation = 0;

            if (captionText != null) captionText.text = "";

            // 表示するデータが無いので非表示にする
            gameObject.SetActive(false);
        }

        public void ResetItem()
        {
            _index = -1;
            _collageId = -1;
            _worldId = "";
            _currentImgRotation = 0;

            if (Image != null)
            {
                Image.texture = null;
                Image.material = _imageDefaultMaterial;
                Image.uvRect = new Rect(0, 0, 1, 1);
            }

            Transform[] targets = GetRotationTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                targets[i].localRotation = _baseLocalRotations[i];
                targets[i].localPosition = _baseLocalPositions[i];
            }

            if (captionText != null) captionText.text = "";

            // リセット時はロード待ち状態として非表示にする
            gameObject.SetActive(false);
        }
    }
}
