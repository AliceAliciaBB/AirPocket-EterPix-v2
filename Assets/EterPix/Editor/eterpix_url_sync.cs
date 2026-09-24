#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace ali.eterpix
{
    // Play開始前に、eterpix_requesterのrequestUrl(json_URL)を元にvrcurllist.baseUrlを
    // 自動生成する(img_URLとjson_URLはベースが同一のため、手動の二重入力を避ける)。
    // UdonはUdon実行時にVRCUrlを文字列から生成する手段を持たないため、この同期は
    // Editor時(Play開始前)に行い、生成結果をシーンに保存する。
    [InitializeOnLoad]
    public static class eterpix_url_sync
    {
        static eterpix_url_sync()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            SyncAll();
        }

        [MenuItem("ali/eterpix/Sync URL Lists from requestUrl")]
        private static void SyncAllMenuItem()
        {
            SyncAll();
        }

        private static void SyncAll()
        {
            // v2は256スロット固定が設計上の前提のため、Inspectorの既定値(16)のまま放置されて
            // 静かに16件しか生成されない事故(レビュー指摘: v2導入の目的そのものを損なう)を防ぐため、
            // SyncOneに渡す前にarraySizeを強制的に256へ揃える。
            // (旧系統v1のnav/ringは開発用フォルダのeterpix_url_sync_v1がSyncOneを使って同期する)
            foreach (var requester in Object.FindObjectsByType<ali.eterpix.v2.eterpix_requester>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ForceArraySize(requester, "urlList", ali.eterpix.v2.eterpix_downloader.ImageSlotsPerFeed);
                SyncOne(requester, "urlList", "requestUrl");
            }
        }

        // SyncOneと同じリフレクション経由のフィールド取得手法で、指定コンポーネントが参照する
        // vrcurllist.arraySizeを強制的に指定値へ揃える(既存のGenerateUrlsのクランプ処理とは別に、
        // v2 eterpix_requesterのInspector既定値16を確実に上書きするための専用処理)。
        private static void ForceArraySize(Object middle, string urlListFieldName, int requiredArraySize)
        {
            var type = middle.GetType();
            var urlListField = type.GetField(urlListFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (urlListField == null) return;

            var urlList = urlListField.GetValue(middle) as vrcurllist;
            if (urlList == null) return;

            if (urlList.arraySize != requiredArraySize)
            {
                urlList.arraySize = requiredArraySize;
                EditorUtility.SetDirty(urlList);
            }
        }

        public static void SyncOne(Object middle, string urlListFieldName, string requestUrlFieldName)
        {
            var type = middle.GetType();
            var urlListField = type.GetField(urlListFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var requestUrlField = type.GetField(requestUrlFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (urlListField == null || requestUrlField == null) return;

            var urlList = urlListField.GetValue(middle) as vrcurllist;
            var requestUrl = requestUrlField.GetValue(middle) as VRCUrl;
            if (urlList == null || requestUrl == null || VRCUrl.IsNullOrEmpty(requestUrl)) return;

            string newBaseUrl = requestUrl.Get();
            if (urlList.baseUrl == newBaseUrl && urlList.urlArray != null && urlList.urlArray.Length == urlList.arraySize) return;

            GenerateUrls(urlList, newBaseUrl);
        }

        private static void GenerateUrls(vrcurllist urlList, string newBaseUrl)
        {
            urlList.baseUrl = newBaseUrl;

            if (urlList.arraySize < 1 || urlList.arraySize > 256)
            {
                urlList.arraySize = Mathf.Clamp(urlList.arraySize, 1, 256);
            }

            // baseUrlの末尾に"/"が無ければ補う(無いと"public01"のように連結されてしまう)
            string normalizedBaseUrl = urlList.baseUrl.EndsWith("/") ? urlList.baseUrl : urlList.baseUrl + "/";

            urlList.urlArray = new VRCUrl[urlList.arraySize];
            for (int i = 0; i < urlList.arraySize; i++)
            {
                string hexValue = i.ToString("X2");
                urlList.urlArray[i] = new VRCUrl(normalizedBaseUrl + hexValue);
            }

            EditorUtility.SetDirty(urlList);
            Debug.Log($"[eterpix_url_sync] {urlList.name}: requestUrlから{urlList.arraySize}個のURLを同期しました(base={normalizedBaseUrl})");
        }
    }
}
#endif
