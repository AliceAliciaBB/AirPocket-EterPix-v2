#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace ali.eterpix
{
    // eterpix_requesterのInspector。JSON URLをドロップダウンで選べるようにし、
    // URLが変わった瞬間に画像URL表(vrcurllist: requestUrl + /00〜/FF)を作り直す。
    // UdonはVRCUrlを実行時に作れないため、切り替えはEditor上でのみ行う(ゲーム内では切り替えない)。
    [CustomEditor(typeof(ali.eterpix.v2.eterpix_requester))]
    public class eterpix_requesterEditor : Editor
    {
        // urlPresetに保存されるID。並べ替えると既存のシーン/prefabの設定が別の項目になるため、
        // 項目を追加するときは既存のIDを変えずに新しいIDで末尾へ足す。
        private const int CustomPresetId = -1;
        private static readonly int[] PresetIds = { 0 };
        private static readonly string[] PresetLabels = { "パブリック投稿" };
        private static readonly string[] PresetUrls = { "https://eterpix.com/api/vrc/v1/public" };
        private const string CustomLabel = "自分で入力";

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            serializedObject.Update();
            SerializedProperty presetProp = serializedObject.FindProperty("urlPreset");
            SerializedProperty urlProp = serializedObject.FindProperty("requestUrl").FindPropertyRelative("url");

            EditorGUILayout.LabelField("JSON URL", EditorStyles.boldLabel);

            string[] labels = new string[PresetLabels.Length + 1];
            for (int i = 0; i < PresetLabels.Length; i++) labels[i] = PresetLabels[i];
            labels[PresetLabels.Length] = CustomLabel;

            int displayIndex = PresetLabels.Length; // 未知のIDは「自分で入力」として扱う
            for (int i = 0; i < PresetIds.Length; i++)
            {
                if (PresetIds[i] == presetProp.intValue) displayIndex = i;
            }

            int newDisplayIndex = EditorGUILayout.Popup("取得先", displayIndex, labels);
            bool isCustom = newDisplayIndex >= PresetIds.Length;
            presetProp.intValue = isCustom ? CustomPresetId : PresetIds[newDisplayIndex];

            if (isCustom)
            {
                // 補間しない。フォルダ等のリンクをそのまま貼り付けてもらう
                // (DelayedTextFieldで、入力確定時だけ画像URL表を作り直す)
                urlProp.stringValue = EditorGUILayout.DelayedTextField("URL", urlProp.stringValue);
                EditorGUILayout.HelpBox("URLをそのまま貼り付けてください(自動補間しません)。v1形式(/api/vrc/v1/...)のみ対応。", MessageType.None);
            }
            else
            {
                string presetUrl = PresetUrls[newDisplayIndex];
                if (urlProp.stringValue != presetUrl) urlProp.stringValue = presetUrl;
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("URL", presetUrl);
                }
            }

            EditorGUILayout.Space();
            DrawPropertiesExcluding(serializedObject, "m_Script", "urlPreset", "requestUrl");

            serializedObject.ApplyModifiedProperties();

            // 画像URL表がrequestUrlとずれていれば作り直す(一致していれば何もしない)
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                foreach (Object t in targets)
                {
                    eterpix_url_sync.SyncRequester((ali.eterpix.v2.eterpix_requester)t);
                }
            }
        }
    }
}
#endif
