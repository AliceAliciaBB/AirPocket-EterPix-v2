#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
using UnityEngine;

namespace ali.eterpix
{
    // 旧系統(v1)の中位(nav/ring)用のURL同期。配布物(Assets/EterPix)には含めないため、
    // v1のクラスを参照する部分だけをeterpix_url_syncから分離したもの。
    // 同期処理そのものは配布側のeterpix_url_sync.SyncOneを使う。
    [InitializeOnLoad]
    public static class eterpix_url_sync_v1
    {
        static eterpix_url_sync_v1()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            SyncAll();
        }

        [MenuItem("ali/eterpix/Sync URL Lists from requestUrl (v1 nav/ring)")]
        private static void SyncAllMenuItem()
        {
            SyncAll();
        }

        private static void SyncAll()
        {
            foreach (var nav in Object.FindObjectsByType<eterpix_middle_nav>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                eterpix_url_sync.SyncOne(nav, "urlList", "requestUrl");
            }

            foreach (var ring in Object.FindObjectsByType<eterpix_middle_ring>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                eterpix_url_sync.SyncOne(ring, "urlList", "requestUrl");
            }
        }
    }
}
#endif
