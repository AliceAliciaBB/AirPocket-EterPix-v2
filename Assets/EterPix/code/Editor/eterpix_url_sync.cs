#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace ali.eterpix
{
    // Play開始前に、中位(nav/ring)のrequestUrl(json_URL)を元にvrcurllist.baseUrlを
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
            foreach (var nav in Object.FindObjectsByType<eterpix_middle_nav>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SyncOne(nav, "urlList", "requestUrl");
            }

            foreach (var ring in Object.FindObjectsByType<eterpix_middle_ring>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SyncOne(ring, "urlList", "requestUrl");
            }
        }

        private static void SyncOne(Object middle, string urlListFieldName, string requestUrlFieldName)
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
