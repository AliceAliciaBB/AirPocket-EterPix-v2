using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

namespace ali.eterpix
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_item : UdonSharpBehaviour
    {
        [Header("データソース")]
        [SerializeField] private eterpix_json_get jsonGetInstance;

        [Header("UI要素")]
        [SerializeField] private RawImage Image;
        [SerializeField] private TMP_Text postDesText;
        [SerializeField] private TMP_Text userNameText;

        [SerializeField] private TMP_Text worldNameText;
        [SerializeField] private TMP_Text world_des;

        [Header("ワールドへ行くパネル")]
        [SerializeField] private GameObject worldDetailbutton;
        [SerializeField] private GameObject worldDetailPanel;

        [Header("VRCポータル")]
        [SerializeField] private VRCPortalMarker portalMarker;

        [Header("VRCポータル置く場所")]

        [SerializeField] private Transform portalSpawnPoint;
        public Transform PortalSpawnPoint => portalSpawnPoint != null ? portalSpawnPoint : transform;

        [SerializeField] private eterpix_porta_resize portalResize;


        [Header("移動予約URL（コピー元 → ユーザーがコピペ → コピー先）")]
        [SerializeField] private TMP_InputField tpUrlCopySource;
        [SerializeField] private VRCUrlInputField tpVrcUrlInput;

        public RawImage ItemImage => Image;

        private string _defaultPostDesText;
        private string _defaultUserNameText;
        private string _defaultWorldName;
        private string _defaultWorldDescription;

        private Material _imageDefaultMaterial;
        private int _currentSetIndex = -1;
        private int _gridPosition = -1;

        private string _worldId;
        private string _photoUuid;

        private void Start()
        {
            _defaultPostDesText = postDesText.text;
            _defaultUserNameText = userNameText.text;

            if (worldNameText != null)
                _defaultWorldName = worldNameText.text;

            if (world_des != null)
                _defaultWorldDescription = world_des.text;

            _imageDefaultMaterial = Image.material;

            if (worldDetailPanel != null)
                worldDetailPanel.SetActive(false);

            // 初期状態は非表示（データがロードされるまで隠す）
            gameObject.SetActive(false);
        }




        public void UpdateItemInfo(DataDictionary data, int itemIndex)
        {
            if (data.TryGetValue("description", out var descriptionToken))
            {
                postDesText.text = descriptionToken.TokenType == TokenType.String ? descriptionToken.String : "";
            }

            if (data.TryGetValue("user_name", out var userNameToken))
            {
                userNameText.text = userNameToken.TokenType == TokenType.String ? userNameToken.String : "";
            }

            _photoUuid = null;
            if (data.TryGetValue("uuid", out var uuidToken) &&
                uuidToken.TokenType == TokenType.String)
            {
                _photoUuid = uuidToken.String;
            }

            _worldId = null;
            if (data.TryGetValue("world_vrc_id", out var worldVrcIdToken) &&
                worldVrcIdToken.TokenType == TokenType.String)
            {
                _worldId = worldVrcIdToken.String;
                if (worldDetailbutton != null)
                    worldDetailbutton.SetActive(true);

            }
            else
            {
                if (worldDetailbutton != null)
                    worldDetailbutton.SetActive(false);
            }

            // world_data: world_vrc_id をキーとするワールド情報辞書から名前・説明文を引く
            // (is_provisional=trueの場合はEXIF由来の仮登録で、情報が古い可能性がある)
            string worldName = "";
            string worldDescription = "";
            if (!string.IsNullOrEmpty(_worldId) && jsonGetInstance != null)
            {
                DataDictionary worldData = jsonGetInstance.GetWorldData();
                if (worldData != null &&
                    worldData.TryGetValue(_worldId, out var worldEntryToken) &&
                    worldEntryToken.TokenType == TokenType.DataDictionary)
                {
                    DataDictionary worldEntry = worldEntryToken.DataDictionary;

                    if (worldEntry.TryGetValue("world_name", out var worldNameToken) &&
                        worldNameToken.TokenType == TokenType.String)
                    {
                        worldName = worldNameToken.String;
                    }

                    if (worldEntry.TryGetValue("confirmed_description", out var confirmedDescToken) &&
                        confirmedDescToken.TokenType == TokenType.String)
                    {
                        worldDescription = confirmedDescToken.String;
                    }
                }
            }

            if (worldNameText != null)
            {
                worldNameText.text = worldName;
            }

            if (world_des != null)
            {
                world_des.text = worldDescription;
            }

            // img_rotation: コラージュ内で実際に見える回転角度(0〜3)。
            // 0=そのまま,1=時計回り90度,2=180度,3=270度
            int imgRotation = 0;
            if (data.TryGetValue("img_rotation", out var imgRotationToken))
            {
                if (imgRotationToken.TokenType == TokenType.Double)
                {
                    imgRotation = (int)imgRotationToken.Double;
                }
                else if (imgRotationToken.TokenType == TokenType.String)
                {
                    int.TryParse(imgRotationToken.String, out imgRotation);
                }
            }

            _gridPosition = -1;
            float uvX = 0f;
            float uvY = 0f;
            const float cellW = 0.5f;
            const float cellH = 1f / 3f;

            // img_pos: コラージュ内の位置(1〜6)。配置は 1 2 / 3 4 / 5 6
            if (data.TryGetValue("img_pos", out var imgPosToken))
            {
                int imgPos = -1;
                if (imgPosToken.TokenType == TokenType.Double)
                {
                    imgPos = (int)imgPosToken.Double;
                }
                else if (imgPosToken.TokenType == TokenType.String)
                {
                    int.TryParse(imgPosToken.String, out imgPos);
                }

                if (imgPos >= 1 && imgPos <= 6)
                {
                    int col = (imgPos - 1) % 2;
                    int row = (imgPos - 1) / 2; // 0=上段, 1=中段, 2=下段
                    uvX = col * cellW;
                    uvY = 1f - (row + 1) * cellH;
                    _gridPosition = imgPos;
                }
            }

            if (Image != null)
            {
                // img_rotation: 0=そのまま,1=時計回り90度,2=180度,3=270度。
                // UnityのZ回転は正値が反時計回りに見えるため符号を反転する
                Image.transform.localRotation = Quaternion.Euler(0, 0, imgRotation * -90f);
                Image.uvRect = new Rect(uvX, uvY, cellW, cellH);
            }

            _currentSetIndex = -1;
            if (data.TryGetValue("collage_id", out var collageIdToken))
            {
                if (collageIdToken.TokenType == TokenType.Double)
                {
                    _currentSetIndex = (int)collageIdToken.Double;
                }
                else if (collageIdToken.TokenType == TokenType.String)
                {
                    int.TryParse(collageIdToken.String, out _currentSetIndex);
                }

                if (jsonGetInstance != null && _currentSetIndex >= 0)
                {
                    TextureManager textureManager = jsonGetInstance.GetTextureManager();
                    if (textureManager != null)
                    {
                        textureManager.RequestTexture(_currentSetIndex, this);
                    }
                }
            }

            if (worldDetailPanel != null && worldDetailPanel.activeSelf)
            {
                RefreshWorldDetailPanel();
            }

            // データがセットされたのでオブジェクトを表示する
            gameObject.SetActive(true);
        }

        public void ApplyTexture(Texture2D texture)
        {
            if (texture != null && Image != null)
            {
                Image.material = null;
                Image.texture = texture;
            }
        }

        public void OpenWorldDetailPanel()
        {
            if (worldDetailPanel == null) return;

            worldDetailPanel.SetActive(true);
            RefreshWorldDetailPanel();
        }

        public void CloseWorldDetailPanel()
        {
            if (worldDetailPanel != null)
                worldDetailPanel.SetActive(false);
        }

        public void OnGoClicked()
        {
            if (portalMarker != null && !string.IsNullOrEmpty(_worldId))
            {
                portalMarker.roomId = _worldId;
                portalMarker.gameObject.SetActive(true);
                portalMarker.RefreshPortal();
            }

            if (tpVrcUrlInput != null)
            {
                VRCUrl tpUrl = tpVrcUrlInput.GetUrl();
                if (tpUrl != null && !string.IsNullOrEmpty(tpUrl.Get()))
                {
                    VRCStringDownloader.LoadUrl(tpUrl, (VRC.Udon.Common.Interfaces.IUdonEventReceiver)this);
                }
            }
            // getから継承したポータル本体(eterpix_porta_resize)を、自分の位置へ直接飛ばす
            if (portalResize != null)
            {
                portalResize.SetParentObject(PortalSpawnPoint, _worldId);
            }

            CloseWorldDetailPanel();
        }

        private void RefreshWorldDetailPanel()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && !string.IsNullOrEmpty(_photoUuid))
            {
                string playerName = localPlayer.displayName;
                string tpUrl = $"https://www.eterpix.uk/vrc/ingame_api/tp/register/{playerName}/{_photoUuid}";

                if (tpUrlCopySource != null)
                {
                    tpUrlCopySource.text = tpUrl;
                }
            }
            else
            {
                if (tpUrlCopySource != null)
                    tpUrlCopySource.text = "";
            }
        }

        public void SetEmptyItemInfo()
        {
            postDesText.text = "";
            userNameText.text = "";

            if (worldNameText != null)
                worldNameText.text = "";

            if (world_des != null)
                world_des.text = "";

            _worldId = null;
            _photoUuid = null;
            _currentSetIndex = -1;
            _gridPosition = -1;

            if (worldDetailPanel != null)
                worldDetailPanel.SetActive(false);

            // 表示するデータが無いので非表示にする
            gameObject.SetActive(false);
        }

        public void ResetItem()
        {
            postDesText.text = _defaultPostDesText;
            userNameText.text = _defaultUserNameText;

            if (worldNameText != null)
                worldNameText.text = _defaultWorldName;

            if (world_des != null)
                world_des.text = _defaultWorldDescription;

            _worldId = null;
            _photoUuid = null;
            _currentSetIndex = -1;
            _gridPosition = -1;

            if (Image != null)
            {
                Image.texture = null;
                Image.material = _imageDefaultMaterial;
                Image.transform.localRotation = Quaternion.identity;
                Image.uvRect = new Rect(0, 0, 1, 1);
            }

            if (worldDetailPanel != null)
                worldDetailPanel.SetActive(false);

            // リセット時はロード待ち状態として非表示にする
            gameObject.SetActive(false);
        }
    }
}