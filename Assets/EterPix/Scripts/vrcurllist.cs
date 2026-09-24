using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace ali.eterpix
{
    public class vrcurllist : UdonSharpBehaviour
    {
        [Header("URL設定")]
        [Header("↓ img_URL-imgのAPIをURLでここで指定してください。(末尾の数字は不要です)")]
        public string baseUrl = "";

        [Header("配列サイズ設定")]
        public int arraySize = 16;

        [Header("生成されたURL配列")]
        public VRCUrl[] urlArray;

        void Start()
        {
            // 実行時の初期化
        }

        // デバッグ用：特定のインデックスのURLを取得
        public VRCUrl GetUrl(int index)
        {
            if (urlArray != null && index >= 0 && index < urlArray.Length)
            {
                return urlArray[index];
            }

            return null;
        }
    }
}