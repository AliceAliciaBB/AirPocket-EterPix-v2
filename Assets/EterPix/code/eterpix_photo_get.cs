using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.Components;
using VRC.SDKBase;
using TMPro;

namespace ali.eterpix
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_photo_get : UdonSharpBehaviour
    {
        [Header("データソース")]
        [SerializeField] private eterpix_get_api apiInstance;

        [Header("TextureManager")]
        [SerializeField] private TextureManager textureManager;

        [Header("表示先（image/indexのみを持つ子。40個程度を想定。未設定ならStart()で子階層から自動収集）")]
        [SerializeField] private eterpix_photo_vew[] photoViews;

        // リングバッファーの書き込み位置（次に上書きするphotoViewsのスロット）
        private int _nextRingSlot = 0;
        // 前回DisplayItems()時点でのjsonArray件数（この件数以降が新規登録分）
        private int _lastTotalCount = 0;

        [Header("des表示用テキストUI（des_portal_viewで更新）")]
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text worldNameText;
        [SerializeField] private TMP_Text worldDescriptionText;

        [Header("ポータル（des_portal_viewで更新）")]
        [SerializeField] private VRCPortalMarker portalMarker;

        // フレームつき
        [SerializeField] private PortalStand portalStand;

        [Header("ポータル本体（vewの位置へSetParentObjectで飛ばす対象）")]
        [SerializeField] private eterpix_porta_resize portalResize;

        [Header("表示先（デバッグ用）")]
        [SerializeField] private TMP_Text itemNameText;

        private void Start()
        {
            // Inspectorで未設定なら、子階層から eterpix_photo_vew を自動収集する
            if (photoViews == null || photoViews.Length == 0)
            {
                photoViews = GetComponentsInChildren<eterpix_photo_vew>(true);
            }

            // 収集した各子に自分自身を教える（子側でのGetComponentInParentは
            // 非アクティブなGameObjectだとUdon実行時に信頼できないため）
            for (int i = 0; i < photoViews.Length; i++)
            {
                if (photoViews[i] != null)
                {
                    photoViews[i].SetParent(this);
                }
            }

            if (apiInstance != null)
                apiInstance.collme(this);
        }

        public void pos_reset()
        {
            for (int i = 0; i < photoViews.Length; i++)
            {
                if (photoViews[i] != null)
                {
                    photoViews[i].pos_reset();
                }
            }
        }

        /// <summary>
        /// 指定インデックスの1件を取得
        /// </summary>
        public DataDictionary GetItem(int index)
        {
            if (apiInstance == null || apiInstance.jsonArray == null || apiInstance.jsonArray.Count == 0)
            {
                return null;
            }

            DataList sourceArray = apiInstance.jsonArray;

            if (index < 0 || index >= sourceArray.Count)
            {
                return null;
            }

            if (sourceArray.TryGetValue(index, out DataToken item) &&
                item.TokenType == TokenType.DataDictionary)
            {
                return item.DataDictionary;
            }

            return null;
        }

        /// <summary>
        /// 前回呼び出し以降に新規登録された件数のみをjsonArrayの末尾から取得し、
        /// リングバッファー方式で photoViews に反映する（一番古いスロットから新しいもので上書き）。
        /// </summary>
        public void DisplayItems()
        {
            if (photoViews == null || photoViews.Length == 0) return;

            int totalCount = GetTotalCount();
            if (totalCount <= _lastTotalCount) return;

            for (int index = _lastTotalCount; index < totalCount; index++)
            {
                DataDictionary data = GetItem(index);
                if (data == null) continue;

                ApplyItemToView(photoViews[_nextRingSlot], index, data);
                _nextRingSlot = (_nextRingSlot + 1) % photoViews.Length;
            }

            _lastTotalCount = totalCount;
        }

        /// <summary>
        /// 1件分のデータをphoto_vewへ反映する（index・collage_id・img_pos・img_rotationおよび撮影情報を渡す）
        /// </summary>
        private void ApplyItemToView(eterpix_photo_vew view, int index, DataDictionary data)
        {
            if (view == null) return;

            int collageId = -1;
            if (data.TryGetValue("collage_id", out var collageIdToken))
            {
                if (collageIdToken.TokenType == TokenType.Double)
                {
                    collageId = (int)collageIdToken.Double;
                }
                else if (collageIdToken.TokenType == TokenType.String)
                {
                    int.TryParse(collageIdToken.String, out collageId);
                }
            }

            int imgPos = -1;
            if (data.TryGetValue("img_pos", out var imgPosToken))
            {
                if (imgPosToken.TokenType == TokenType.Double)
                {
                    imgPos = (int)imgPosToken.Double;
                }
                else if (imgPosToken.TokenType == TokenType.String)
                {
                    int.TryParse(imgPosToken.String, out imgPos);
                }
            }

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

            string description = data.TryGetValue("description", out var descToken) &&
                descToken.TokenType == TokenType.String ? descToken.String : "";

            string userName = data.TryGetValue("user_name", out var userToken) &&
                userToken.TokenType == TokenType.String ? userToken.String : "";

            string worldId = data.TryGetValue("world_vrc_id", out var worldIdToken) &&
                worldIdToken.TokenType == TokenType.String ? worldIdToken.String : "";

            string worldName = ResolveWorldName(worldId);

            view.UpdateItemInfo(index, collageId, imgPos, imgRotation, description, userName, worldName, worldId);
        }

        /// <summary>
        /// world_vrc_id から world_data を引いてワールド名を解決する（見つからなければ空文字）
        /// </summary>
        private string ResolveWorldName(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return "";

            DataDictionary worldData = GetWorldData();
            if (worldData != null &&
                worldData.TryGetValue(worldId, out var worldEntryToken) &&
                worldEntryToken.TokenType == TokenType.DataDictionary)
            {
                DataDictionary worldEntry = worldEntryToken.DataDictionary;
                if (worldEntry.TryGetValue("world_name", out var worldNameToken) &&
                    worldNameToken.TokenType == TokenType.String)
                {
                    return worldNameToken.String;
                }
            }

            return "";
        }

        /// <summary>
        /// eterpix_photo_vew のボタンから呼ばれる。
        /// 指定indexのデータをもとに、自分（親）が持つdes表示用テキストUIとポータルを更新する。
        /// </summary>
        public void des_portal_view(int index)
        {
            DataDictionary data = GetItem(index);
            Debug.Log($"[eterpix_photo_get] des_portal_view called. index={index} data={(data != null ? "found" : "NULL")} descriptionText={(descriptionText != null ? "set" : "NULL")}");

            if (data == null)
            {
                if (descriptionText != null) descriptionText.text = "";
                if (userNameText != null) userNameText.text = "";
                if (worldNameText != null) worldNameText.text = "";
                if (worldDescriptionText != null) worldDescriptionText.text = "";
                return;
            }

            if (descriptionText != null)
            {
                descriptionText.text = data.TryGetValue("description", out var descToken) &&
                    descToken.TokenType == TokenType.String ? descToken.String : "";
            }

            if (userNameText != null)
            {
                userNameText.text = data.TryGetValue("user_name", out var userToken) &&
                    userToken.TokenType == TokenType.String ? userToken.String : "";
            }

            string worldId = null;
            if (data.TryGetValue("world_vrc_id", out var worldIdToken) &&
                worldIdToken.TokenType == TokenType.String)
            {
                worldId = worldIdToken.String;
            }

            string worldName = "";
            string worldDescription = "";
            if (!string.IsNullOrEmpty(worldId))
            {
                DataDictionary worldData = GetWorldData();
                if (worldData != null &&
                    worldData.TryGetValue(worldId, out var worldEntryToken) &&
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

            if (worldNameText != null) worldNameText.text = worldName;
            if (worldDescriptionText != null) worldDescriptionText.text = worldDescription;

            // ポータルの位置設定は呼び出し元（eterpix_photo_vew）が直接行う。ここではroomId解決とテキストUI更新のみ。
            if (portalMarker != null && !string.IsNullOrEmpty(worldId))
            {
                portalMarker.roomId = worldId;
                portalMarker.gameObject.SetActive(true);
                portalMarker.RefreshPortal();
            }
            if (portalStand != null && !string.IsNullOrEmpty(worldId))
            {
                portalStand.SetTargetRoomId(worldId);
            }
        }

        /// <summary>
        /// 配列の総数を取得
        /// </summary>
        public int GetTotalCount()
        {
            if (apiInstance == null || apiInstance.jsonArray == null)
                return 0;

            return apiInstance.jsonArray.Count;
        }

        /// <summary>
        /// TextureManagerを取得
        /// </summary>
        public TextureManager GetTextureManager()
        {
            return textureManager;
        }

        /// <summary>
        /// ポータル本体を取得（vewが直接位置を設定するために継承する）
        /// </summary>
        public VRCPortalMarker GetPortalMarker()
        {
            return portalMarker;
        }

        /// <summary>
        /// フレーム付きポータルスタンドを取得（vewが直接位置を設定するために継承する）
        /// </summary>
        public PortalStand GetPortalStand()
        {
            return portalStand;
        }

        /// <summary>
        /// ポータル本体（リサイズ制御込み）を取得（vewがSetParentObjectで自分の位置へ飛ばすために継承する）
        /// </summary>
        public eterpix_porta_resize GetPortalResize()
        {
            return portalResize;
        }

        /// <summary>
        /// world_vrc_id -> {world_name, confirmed_description, is_provisional} のワールド情報辞書を取得
        /// </summary>
        public DataDictionary GetWorldData()
        {
            return apiInstance != null ? apiInstance.worldData : null;
        }
    }
}
