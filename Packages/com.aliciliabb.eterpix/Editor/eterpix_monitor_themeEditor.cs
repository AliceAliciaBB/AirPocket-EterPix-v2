#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ali.eterpix.v2;

namespace ali.eterpix
{
    // eterpix_monitor_theme のInspector。子の名前の接頭辞から配色の適用先を集め直すボタンと、
    // 黒/白のプレビューボタンを付ける。
    [CustomEditor(typeof(eterpix_monitor_theme))]
    public class eterpix_monitor_themeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button("子から自動収集"))
            {
                foreach (Object t in targets) ((eterpix_monitor_theme)t).EditorCollectFromChildren();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("黒でプレビュー"))
            {
                foreach (Object t in targets) ((eterpix_monitor_theme)t).EditorApplyPreset(eterpix_theme.ThemeBlack);
            }
            if (GUILayout.Button("白でプレビュー"))
            {
                foreach (Object t in targets) ((eterpix_monitor_theme)t).EditorApplyPreset(eterpix_theme.ThemeWhite);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "子オブジェクト名の接頭辞で役割が決まります: bg_ 背景 / tx_ 文字 / btn_ ボタン / acc_ アクセント / acctx_ アクセント上の文字。\n" +
                "要素を追加・改名したら「子から自動収集」を押してください。\n" +
                "実行中の色はシーンの EterpixTheme が決めます。",
                MessageType.None);
        }
    }
}
#endif
