#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEditor;
using VRC.SDKBase;
using UdonSharpEditor;

namespace ali.eterpix
{
    [CustomEditor(typeof(vrcurllist))]
    public class vrcurllistEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            
            vrcurllist urlList = (vrcurllist)target;

            // eterpix_requesterが参照しているリストはrequestUrlから自動生成されるため編集させない
            // (旧系統TextureManager用のリストは手動で揃えるため、従来どおり編集できるようにしておく)
            bool isAutoManaged = IsReferencedByRequester(urlList);
            if (isAutoManaged)
            {
                EditorGUILayout.HelpBox("requestUrl から自動で反映されます(requestUrl + /00〜/FF)。触らないでください。", MessageType.Info);
            }

            EditorGUI.BeginChangeCheck();

            using (new EditorGUI.DisabledScope(isAutoManaged))
            {
                urlList.baseUrl = EditorGUILayout.TextField("Base URL", urlList.baseUrl);
                urlList.arraySize = EditorGUILayout.IntSlider("Array Size", urlList.arraySize, 1, 256);
            }

            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(isAutoManaged))
            {
                if (GUILayout.Button("Generate URLs", GUILayout.Height(30)))
                {
                    GenerateUrls(urlList);
                }
            }
            
            if (changed)
            {
                GenerateUrls(urlList);
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated URLs Preview", EditorStyles.boldLabel);
            
            if (urlList.urlArray != null && urlList.urlArray.Length > 0)
            {
                EditorGUILayout.LabelField($"Total URLs: {urlList.urlArray.Length}", EditorStyles.helpBox);
                
                EditorGUILayout.Space(5);
                
                int displayCount = Mathf.Min(3, urlList.urlArray.Length);
                for (int i = 0; i < displayCount; i++)
                {
                    if (urlList.urlArray[i] != null)
                    {
                        string hexIndex = i.ToString("X2");
                        EditorGUILayout.LabelField($"[{hexIndex}]", urlList.urlArray[i].ToString());
                    }
                }
                
                if (urlList.urlArray.Length > 4)
                {
                    EditorGUILayout.LabelField("  ...", EditorStyles.centeredGreyMiniLabel);
                }
                
                if (urlList.urlArray.Length > 1)
                {
                    int lastIndex = urlList.urlArray.Length - 1;
                    if (urlList.urlArray[lastIndex] != null)
                    {
                        string hexIndex = lastIndex.ToString("X2");
                        EditorGUILayout.LabelField($"[{hexIndex}]", urlList.urlArray[lastIndex].ToString());
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("URLが生成されていません。「Generate URLs」ボタンを押すか、値を変更してください。", MessageType.Info);
            }
            
            if (changed)
            {
                EditorUtility.SetDirty(urlList);
            }
        }
        
        // 同じルート配下(シーン上のprefabインスタンス/prefab編集画面のどちらでも)の
        // eterpix_requesterのurlListに、このリストが設定されているか
        private static bool IsReferencedByRequester(vrcurllist urlList)
        {
            var requesters = urlList.transform.root.GetComponentsInChildren<ali.eterpix.v2.eterpix_requester>(true);
            foreach (var requester in requesters)
            {
                var so = new SerializedObject(requester);
                SerializedProperty prop = so.FindProperty("urlList");
                if (prop != null && prop.objectReferenceValue == urlList) return true;
            }
            return false;
        }

        private void GenerateUrls(vrcurllist urlList)
        {
            if (string.IsNullOrEmpty(urlList.baseUrl))
            {
                Debug.LogWarning("[vrcurllist] Base URLが空です。");
                return;
            }
            
            if (urlList.arraySize < 1 || urlList.arraySize > 256)
            {
                Debug.LogWarning("[vrcurllist] Array Sizeは1～256の範囲で設定してください。");
                urlList.arraySize = Mathf.Clamp(urlList.arraySize, 1, 256);
            }
            
            urlList.urlArray = new VRCUrl[urlList.arraySize];

            // baseUrlの末尾に"/"が無ければ補う(無いと"public01"のように連結されてしまう)
            string normalizedBaseUrl = urlList.baseUrl.EndsWith("/") ? urlList.baseUrl : urlList.baseUrl + "/";

            for (int i = 0; i < urlList.arraySize; i++)
            {
                string hexValue = i.ToString("X2");

                string fullUrl = normalizedBaseUrl + hexValue;
                urlList.urlArray[i] = new VRCUrl(fullUrl);
            }
            
            EditorUtility.SetDirty(urlList);
            
            string firstUrl = urlList.urlArray[0].ToString();
            string lastUrl = urlList.urlArray[urlList.arraySize - 1].ToString();
            Debug.Log($"[vrcurllist] {urlList.arraySize}個のURLを生成しました\n最初: {firstUrl}\n最後: {lastUrl}");
        }
    }
}
#endif