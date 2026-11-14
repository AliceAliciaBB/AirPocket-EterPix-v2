using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static ali.eterpix.ui2.editor.Etp2Parts;

namespace ali.eterpix.ui2.editor
{
    // 確認用のテストシーン(配布しない). モックと同じく、左からメニューモニター・ビューア・詳細モニターを間 32 で並べる.
    // フォトフレームはビューアの後ろに重ねる(モックの都合と同じ. 閉じたときに見える)
    public static partial class Etp2Builder
    {
        [MenuItem("EterPix/UIv2/テストシーンを作り直す")]
        public static void BuildTestScene()
        {
            // 開いているシーンには触らない(未保存の変更があっても、保存も破棄もしない).
            // テストシーンは追加で開いて作り、保存したら閉じる
            var previous = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 1.5f, -1.5f);
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = 60;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.45f, 0.5f);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";

            var world = new GameObject("VRCWorld");
            world.transform.position = new Vector3(0, 0, -1.2f);
            var desc = world.AddComponent<VRC.SDK3.Components.VRCSceneDescriptor>();
            desc.spawns = new[] { world.transform };
            world.AddComponent<VRC.Core.PipelineManager>();   // 無いと Play できない

            var theme = PlaceInstance("Etp2_Theme", Vector3.zero);
            var data = PlaceInstance("Etp2_MockData", Vector3.zero);
            float px = Etp2Spec.WorldScale;
            float step = (Etp2Spec.MonitorSize.x + 32) * px;
            float top = 2.0f;
            // メニューモニターの「ふだんの場所」(玄関の代わり). ビューアを開くと左隣へ移る
            var menuGo = PlaceInstance("Etp2_MenuMonitor", new Vector3(-step * 2, top, 0.6f));
            var viewerGo = PlaceInstance("Etp2_Viewer", new Vector3(0, top, 0));
            var detailGo = PlaceInstance("Etp2_DetailMonitor", new Vector3(step, top, 0));
            var d = data.GetComponent<etp2_mock_data>();
            var menu = menuGo.GetComponent<etp2_menu_monitor>();
            var viewer = viewerGo.GetComponent<etp2_viewer>();
            var detail = detailGo.GetComponent<etp2_detail_monitor>();
            menu.data = d; menu.viewer = viewer;
            viewer.data = d; viewer.detail = detail; viewer.menu = menu;
            detail.data = d; detail.viewer = viewer;

            // フォトフレームを複数. ビューアの下に横に並べ、見せる投稿を変える(並び順・ページはフォトフレームごと)
            int n = Etp2Spec.Posts.GetLength(0);
            var even = new System.Collections.Generic.List<int>();
            var odd = new System.Collections.Generic.List<int>();
            for (int i = 0; i < n; i++) (i % 2 == 0 ? even : odd).Add(i);
            string[] frameNames = { "Etp2_PhotoFrame_全部", "Etp2_PhotoFrame_偶数", "Etp2_PhotoFrame_奇数" };
            int[][] frameLists = { new int[0], even.ToArray(), odd.ToArray() };
            float frameGap = (Etp2Spec.PhotoFrameSize.x + 80) * px;
            var behaviours = new System.Collections.Generic.List<UdonSharpBehaviour> { menu, viewer, detail };
            for (int f = 0; f < frameNames.Length; f++)
            {
                var frameGo = PlaceInstance("Etp2_PhotoFrame", new Vector3((f - 1) * frameGap, 0.85f, 0));
                frameGo.name = frameNames[f];
                var frame = frameGo.GetComponent<etp2_photo_frame>();
                frame.data = d; frame.viewer = viewer; frame.postIndices = frameLists[f];
                behaviours.Add(frame);
            }
            foreach (UdonSharpBehaviour b in behaviours)
            {
                UdonSharpEditorUtility.CopyProxyToUdon(b);
                PrefabUtility.RecordPrefabInstancePropertyModifications(b);
                PrefabUtility.RecordPrefabInstancePropertyModifications(UdonSharpEditorUtility.GetBackingUdonBehaviour(b));
            }

            BuildDebugPanel(viewer, new Vector3(0, top - (Etp2Spec.ViewerSize.y + 40) * px, 0));

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Etp2Spec.TestScenePath));
            EditorSceneManager.SaveScene(scene, Etp2Spec.TestScenePath);
            if (previous.IsValid() && previous != scene)
            {
                EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log("[Etp2Trace] builder: テストシーンを作りました " + Etp2Spec.TestScenePath);
        }

        static GameObject PlaceInstance(string prefab, Vector3 pos)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Etp2Spec.PrefabDir + "/" + prefab + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.position = pos;
            return go;
        }

        // 受け渡しのパネルの3場面を出すデバッグボタン(モックの「モック用ボタン」に当たる. テストシーンだけ)
        static void BuildDebugPanel(etp2_viewer viewer, Vector3 pos)
        {
            ResetCache();
            var ctx = new Etp2Ctx();
            var root = new GameObject("Etp2_Debug");
            root.transform.position = pos;
            root.transform.localScale = Vector3.one * Etp2Spec.WorldScale;
            var screen = NewRect(root.transform, "Screen");
            screen.sizeDelta = new Vector2(560, 56);
            screen.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            screen.gameObject.AddComponent<GraphicRaycaster>();
            screen.gameObject.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            HLG(screen, Etp2Spec.Gap, TextAnchor.MiddleCenter, new RectOffset(8, 8, 8, 8));
            string[] labels = { "場面1 確認", "場面2 返事待ち", "場面3 要求を受けた" };
            string[] events = { nameof(etp2_viewer.DebugScene1), nameof(etp2_viewer.DebugScene2), nameof(etp2_viewer.DebugScene3) };
            for (int i = 0; i < 3; i++)
            {
                var b = TextButton(ctx, screen, "Debug" + i, labels[i], BtnStyle.Normal, out _);
                OnClick(b, viewer, events[i]);
            }
            foreach (var b in ctx.btn) { var cb = b.colors; cb.normalColor = new Color(0.2f, 0.2f, 0.2f); cb.highlightedColor = cb.normalColor; cb.pressedColor = new Color(0.3f, 0.3f, 0.3f); b.colors = cb; }
            foreach (var t in ctx.txTexts) t.color = Color.white;
        }
    }
}
