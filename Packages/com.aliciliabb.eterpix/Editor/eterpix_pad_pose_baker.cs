#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

namespace ali.eterpix
{
    // EtrPix-Pad K-44 / D-3: Padの「初期位置に戻す」で戻る先(Gripのワールド位置・向き)を、
    // ビルド時にeterpix_padのinitialPosition / initialRotationへ焼き込む。
    // 実行時のStart()で覚える方式は、途中参加者が同期後の位置を覚えてしまうため採らない。
    // IProcessSceneWithReportはビルド時だけでなくPlayモード開始時にも呼ばれるため、ClientSimでも同じ値になる。
    // 処理中のシーン(ビルド用・Play用のコピー)にだけ書き込み、シーンファイルは書き換えない。
    public class eterpix_pad_pose_baker : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (var pad in root.GetComponentsInChildren<ali.eterpix.v2.eterpix_pad>(true))
                {
                    Bake(pad);
                }
            }
        }

        private static void Bake(ali.eterpix.v2.eterpix_pad pad)
        {
            var so = new SerializedObject(pad);
            var pickup = so.FindProperty("pickup").objectReferenceValue as Component;
            if (pickup == null)
            {
                Debug.LogWarning($"[eterpix_pad_pose_baker] pickupが未設定のため初期位置を焼き込めません: {pad.name}", pad);
                return;
            }

            so.FindProperty("hasInitialPose").boolValue = true;
            so.FindProperty("initialPosition").vector3Value = pickup.transform.position;
            so.FindProperty("initialRotation").quaternionValue = pickup.transform.rotation;
            so.ApplyModifiedPropertiesWithoutUndo();
            // U#の値はUdonBehaviour側へ写さないと実行時に使われない
            UdonSharpEditorUtility.CopyProxyToUdon(pad);
        }
    }
}
#endif
