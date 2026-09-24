using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using TMPro;

namespace ali.eterpix
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_json_get : UdonSharpBehaviour
    {
        [Header("データソース")]
        [SerializeField] private eterpix_get_api apiInstance;

        [Header("TextureManager")]
        [SerializeField] private TextureManager textureManager;

        [Header("アイテム表示コンテナ")]
        [SerializeField] private eterpix_item[] eterpix_items;

        [Header("取得範囲設定")]
        [SerializeField] private int startIndex = 0;   // 開始位置
        private int itemCount = 4;   // 取得個数

        [Header("表示先")]
        [SerializeField] private TMP_Text itemNameText;

        private void Start()
        {
            apiInstance.collme(this);
        }

        /// <summary>
        /// 指定位置から指定個数のアイテムを取得
        /// </summary>
        public DataList GetItems(int start, int count)
        {
            DataList resultList = new DataList();

            // UIをリセット
            if (eterpix_items != null)
            {
                foreach (var boothItem in eterpix_items)
                {
                    if (boothItem != null)
                        boothItem.ResetItem();
                }
            }

            if (apiInstance == null || apiInstance.jsonArray == null || apiInstance.jsonArray.Count == 0)
            {

                return resultList;
            }

            DataList sourceArray = apiInstance.jsonArray;
            int totalCount = sourceArray.Count;

            // 範囲チェック
            if (start < 0 || start >= totalCount)
            {

                return resultList;
            }

            // 実際に取得可能な数
            int actualCount = Mathf.Min(count, totalCount - start);

            // 指定範囲のアイテムを取得
            for (int i = 0; i < actualCount; i++)
            {
                int index = start + i;
                if (sourceArray.TryGetValue(index, out DataToken item))
                {
                    resultList.Add(item);
                }
            }


            return resultList;
        }

        /// <summary>
        /// SerializeFieldの設定値で取得して表示
        /// </summary>
        public DataList GetItems()
        {
            DataList items = GetItems(startIndex, itemCount);
            DisplayItems(items);
            return items;
        }

        /// <summary>
        /// 取得したアイテムをテキストに表示し、eterpix_items に反映
        /// </summary>
        public void DisplayItems(DataList items)
        {
            if (items == null || items.Count == 0)
            {
                if (itemNameText != null)
                    itemNameText.text = "データがありません";
                return;
            }

            if (itemNameText != null)
            {
                string displayText = "";

                for (int i = 0; i < items.Count; i++)
                {
                    if (items.TryGetValue(i, out DataToken item))
                    {
                        // 表示用テキスト作成
                        if (item.TokenType == TokenType.String)
                        {
                            displayText += item.String + "\n";
                        }
                        else if (item.TokenType == TokenType.DataDictionary)
                        {
                            if (VRCJson.TrySerializeToJson(item, JsonExportType.Minify, out DataToken jsonToken))
                            {
                                displayText += jsonToken.String + "\n";
                            }
                        }
                        else
                        {
                            displayText += item.ToString() + "\n";
                        }
                    }
                }

                itemNameText.text = displayText.TrimEnd('\n');
            }

            // eterpix_item に反映
            for (int i = 0; i < items.Count && i < eterpix_items.Length; i++)
            {
                if (eterpix_items[i] == null) continue;

                if (!items.TryGetValue(i, out var item))
                {
                    eterpix_items[i].SetEmptyItemInfo();
                    continue;
                }

                if (item.TokenType == TokenType.DataDictionary)
                {
                    // startIndexからの通し番号を渡す
                    int itemIndex = startIndex + i;
                    eterpix_items[i].UpdateItemInfo(item.DataDictionary, itemIndex);
                }
                else
                {
                    eterpix_items[i].SetEmptyItemInfo();
                }
            }

            // ★ ここから StartDownloadAll() を削除
            // TextureManagerは eterpix_get_api.UpdateItems() で一度だけ呼ばれるべき
            // if (textureManager != null)
            // {
            //     textureManager.StartDownloadAll();
            // }
        }

        public void SetStartIndex(int index)
        {
            startIndex = Mathf.Max(0, index);
        }

        public void SetItemCount(int count)
        {
            itemCount = Mathf.Max(1, count);
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
        /// world_vrc_id -> {world_name, confirmed_description, is_provisional} のワールド情報辞書を取得
        /// </summary>
        public DataDictionary GetWorldData()
        {
            return apiInstance != null ? apiInstance.worldData : null;
        }
    }
}