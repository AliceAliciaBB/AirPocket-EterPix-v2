#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
using UnityEngine;

namespace ali.eterpix
{
    public static class eterpix_menu
    {
        private const string TemplatePrefabPath = "Assets/EterPix/Etp_テンプレート.prefab";

        [MenuItem("GameObject/EterPix/テンプレートを配置", false, 10)]
        static void PlaceTemplate(MenuCommand command)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[EterPix] テンプレートPrefabが見つかりません: {TemplatePrefabPath}");
                return;
            }

            var parent = command.context as GameObject;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent != null ? parent.transform : null);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObjectUtility.SetParentAndAlign(instance, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Place EterPix Template");
            Selection.activeObject = instance;
        }

        [MenuItem("GameObject/EterPix/テンプレートを配置", true)]
        static bool PlaceTemplateValidate() => true;

        // EtrPix-Pad (K-49): Pad本体と範囲Collider(Range)を一組にしたprefabを置く。
        // 範囲の指定は済んでおり、初期位置はビルド時に自動で焼き込まれる(eterpix_pad_pose_baker)
        private const string PadPrefabPath = "Assets/EterPix/Prefabs/EterpixPad.prefab";

        [MenuItem("GameObject/EterPix/Padを配置", false, 11)]
        static void PlacePad(MenuCommand command)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PadPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[EterPix] Pad Prefabが見つかりません: {PadPrefabPath}");
                return;
            }

            var parent = command.context as GameObject;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent != null ? parent.transform : null);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObjectUtility.SetParentAndAlign(instance, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Place EterPix Pad");
            EnsurePadRequester();
            Selection.activeObject = instance;
        }

        // Padの取得はワールドに1つの要求者が受け持つ(K-61)。シーンに無ければシーン直下に置く
        private const string PadRequesterPrefabPath = "Assets/EterPix/Prefabs/EterpixPadRequester.prefab";

        private static void EnsurePadRequester()
        {
            var existing = Object.FindObjectsByType<ali.eterpix.v2.eterpix_pad_requester>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 0) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PadRequesterPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[EterPix] Pad要求者のPrefabが見つかりません: {PadRequesterPrefabPath}");
                return;
            }

            var requester = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            requester.name = ali.eterpix.v2.eterpix_pad_requester.SingletonObjectName;
            Undo.RegisterCreatedObjectUndo(requester, "Place EterPix Pad Requester");
        }
    }
}
#endif
