#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ali.eterpix.v2;

namespace ali.eterpix
{
    // eterpix_theme のInspector。切替モード等を日本語のドロップダウンで選べるようにし、
    // 変更したらシーン内の全モニターへ開始時のテーマを塗る。
    [CustomEditor(typeof(eterpix_theme))]
    public class eterpix_themeEditor : Editor
    {
        private static readonly string[] ModeLabels = { "切替あり", "固定: 黒", "固定: 白", "固定: カスタム" };
        private static readonly string[] ThemeLabels = { "黒", "白", "カスタム" };
        private static readonly string[] RoleLabels = { "背景", "文字", "ボタン", "アクセント", "アクセント上の文字" };
        private static readonly string[] CustomProps = { "customBackground", "customText", "customButton", "customAccent", "customAccentText" };

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            serializedObject.Update();

            SerializedProperty mode = serializedObject.FindProperty("switchMode");
            SerializedProperty include = serializedObject.FindProperty("includeCustomInCycle");
            SerializedProperty initial = serializedObject.FindProperty("initialTheme");

            EditorGUILayout.LabelField("切替", EditorStyles.boldLabel);
            mode.intValue = EditorGUILayout.Popup("切替モード", mode.intValue, ModeLabels);
            bool switchable = mode.intValue == eterpix_theme.ModeSwitchable;
            using (new EditorGUI.DisabledScope(!switchable))
            {
                include.boolValue = EditorGUILayout.Toggle("カスタムを切替に含める", include.boolValue);
                initial.intValue = EditorGUILayout.Popup("初期テーマ", initial.intValue, ThemeLabels);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プリセット (左: 黒 / 右: 白、編集不可)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                for (int role = 0; role < RoleLabels.Length; role++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ColorField(new GUIContent(RoleLabels[role]), eterpix_theme.GetPresetColor(eterpix_theme.ThemeBlack, role), false, false, false);
                    EditorGUILayout.ColorField(GUIContent.none, eterpix_theme.GetPresetColor(eterpix_theme.ThemeWhite, role), false, false, false);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("カスタム", EditorStyles.boldLabel);
            for (int role = 0; role < CustomProps.Length; role++)
            {
                SerializedProperty p = serializedObject.FindProperty(CustomProps[role]);
                p.colorValue = EditorGUILayout.ColorField(new GUIContent(RoleLabels[role]), p.colorValue, true, false, false);
            }

            bool changed = serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            bool pressed = GUILayout.Button("シーン内の全モニターに開始時のテーマを適用");
            if (changed || pressed)
            {
                foreach (Object t in targets) ((eterpix_theme)t).EditorApplyToScene();
            }

            EditorGUILayout.HelpBox(
                "切替あり: モニターの切替ボタンで 黒 → 白 →(カスタム)→ 黒 と切り替わります。切り替えはその人にだけ反映され、ワールドごとに保存されます。\n" +
                "固定: 切替ボタンは表示されません。\n" +
                "モニター側で「直接編集」をONにすると、そのモニターはテーマの対象外になります。\n" +
                "このオブジェクトの名前は EterpixTheme のままにしてください(名前で探します)。",
                MessageType.None);
        }
    }
}
#endif
