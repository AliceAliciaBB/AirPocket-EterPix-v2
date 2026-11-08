using System.Linq;
using NudeNyang;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EterPixNudeNyang
{
    // シーン内の全NudeNyangタブレットへ、EterPix-appを配線する.
    // 前提: NudeNyangEterPixPatcher のパッチが適用済み(メニュー NudeNyang/EterPix-app/パッチを適用).
    public static partial class EterPixAppBuilder
    {
        const string FontPath = "Packages/com.aliciliabb.eterpix/Fonts/NotoSansJP-Bold SDF.asset";
        // モニターと同じロゴ用フォント(StackSansNotch-Bold. ASCIIのみ)
        const string LogoFontPath = "Packages/com.aliciliabb.eterpix/Fonts/StackSansNotch-Bold SDF.asset";
        const string GroupIconPath = "Assets/EterPixNudeNyang/Art/EterPixGroupIcon.png";
        const string DarkModeIconPath = "Assets/EterPixNudeNyang/Art/EterPixDarkModeIcon.png";   // Material Symbols "dark_mode"
        const string LightModeIconPath = "Assets/EterPixNudeNyang/Art/EterPixLightModeIcon.png"; // Material Symbols "light_mode"
        const string LikedListIconPath = "Assets/EterPixNudeNyang/Art/EterPixLikedListIcon.png"; // Material Symbols "hotel_class"(気になる一覧)
        const string SettingsIconPath = "Assets/EterPixNudeNyang/Art/EterPixSettingsIcon.png"; // Google Fonts の Material Symbols "settings"(Apache-2.0)
        const string StarIconPath = "Assets/EterPixNudeNyang/Art/EterPixStarIcon.png"; // Material Symbols "star"(輪郭)
        const string StarFilledIconPath = "Assets/EterPixNudeNyang/Art/EterPixStarFilledIcon.png"; // 同(塗り)
        const string ReloadIconPath = "Assets/EterPixNudeNyang/Art/EterPixReloadIcon.png"; // モニターの icon_reload.png と同じ(Material Symbols, Apache-2.0)
        const string PortalIconPath = "Assets/EterPixNudeNyang/Art/EterPixPortalIcon.png"; // Material Symbols "public"(Apache-2.0)
        const string FeedUrl = "https://eterpix.com/api/vrc/v1/public";
        const string IconPath = "Assets/EterPixNudeNyang/Art/EterPixAppIcon.png";
        static readonly Color Dark = new Color32(0x14, 0x14, 0x14, 0xFF);
        static readonly Color Accent = new Color32(0x9D, 0x6F, 0xFF, 0xFF);

        [MenuItem("NudeNyang/EterPix-app/配置 (シーンの全タブレット)")]
        public static void BuildAll()
        {
            if (!NudeNyangEterPixPatcher.AllApplied())
            {
                Debug.LogError("[EtpTrace] builder: パッチが未適用です. 先にメニュー「パッチを適用」を実行してください");
                return;
            }
            // 新しい U# スクリプトにはプログラムアセットが要り、作成直後は未コンパイルで配線に失敗する
            bool created = EnsureProgram("Assets/EterPixNudeNyang/Scripts/NudeNyangEterPix");
            created |= EnsureProgram("Assets/EterPixNudeNyang/Scripts/EterPixAppFeed");
            created |= EnsureProgram("Assets/EterPixNudeNyang/Scripts/EterPixAppImages");
            if (created)
            {
                UdonSharp.UdonSharpProgramAsset.CompileAllCsPrograms(true);
                Debug.LogWarning("[EtpTrace] builder: U#プログラムを作成しました. コンパイルの完了を待って、もう一度実行してください");
                return;
            }
            var pads = Object.FindObjectsOfType<NudeNyangTablet>(true).Where(p => !EditorUtility.IsPersistent(p) && p.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()).ToArray();
            foreach (var pad in pads) Build(pad);
            Debug.Log("[EtpTrace] builder: " + pads.Length + " 台のタブレットに EterPix-app を配線しました");
        }

        // 「配置」の逆: アクティブなシーンの全タブレットから EterPix-app を取り除く(パッチと本体のファイルには触らない).
        // 「配置」をやり直せば、最新版だけが入る状態になる
        [MenuItem("NudeNyang/EterPix-app/削除 (シーンの全タブレット)")]
        public static void RemoveAllMenu() { RemoveAll(true); }

        public static void RemoveAll(bool confirm)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var pads = Object.FindObjectsOfType<NudeNyangTablet>(true).Where(p => !EditorUtility.IsPersistent(p) && p.gameObject.scene == scene).ToArray();
            if (confirm && !EditorUtility.DisplayDialog("EterPix-app の削除",
                "シーン「" + scene.name + "」の " + pads.Length + " 台のタブレットから、EterPix-app(画面・サービス・ホームのアイコン)を取り除きます.\n"
                + "パッチと NudeNyang 本体のファイルは変更しません. 「配置」をやり直せば、最新版が入ります.\n\nUndo で戻せます. 実行しますか?", "削除する", "やめる")) return;
            int removed = 0;
            foreach (var pad in pads) if (Remove(pad)) removed++;
            if (removed > 0) UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            Debug.Log("[EtpTrace] builder: " + removed + " / " + pads.Length + " 台のタブレットから EterPix-app を削除しました");
        }

        static bool Remove(NudeNyangTablet pad)
        {
            var shell = pad.shell;
            bool did = false;
            Undo.SetCurrentGroupName("EterPix-app を削除");
            // サービス(Feed / Images / Geometry を子に持つ)
            var service = pad.transform.Find("EterPix app service");
            if (service != null) { Undo.DestroyObjectImmediate(service.gameObject); did = true; }
            if (shell == null) return did;
            // 画面本体(コピーページ・Zoom・触り判定ゲート・面の入力も中にある)
            if (shell.appFrame != null)
            {
                var panel = shell.appFrame.Find("EterPix app");
                if (panel != null) { Undo.DestroyObjectImmediate(panel.gameObject); did = true; }
            }
            Undo.RecordObject(shell, "EterPix-app を削除");
            var eterpixField = typeof(NudeNyangTabletShell).GetField("eterpix");
            if (eterpixField != null && eterpixField.GetValue(shell) != null) { eterpixField.SetValue(shell, null); did = true; }
            // ホームの12番目: 削除せず、非表示の「空きタイル」に戻す(パッチの AppCount=12 のまま配列を縮めると、本体が全要素を使うため壊れる)
            int index = NudeNyangEterPix.AppId - 1;
            if (shell.homeIcons != null && shell.homeIcons.Length > index && shell.homeIcons[index] != null)
            {
                var tile = shell.homeIcons[index];
                Undo.RecordObject(tile.gameObject, "EterPix-app を削除");
                tile.name = "(未使用 " + (index + 1) + ")";
                var img = tile.GetComponent<Image>();
                if (img != null) { Undo.RecordObject(img, "EterPix-app を削除"); img.color = Color.clear; }
                var glyph = tile.Find("Glyph");
                var gi = glyph != null ? glyph.GetComponent<Image>() : null;
                if (gi != null) { Undo.RecordObject(gi, "EterPix-app を削除"); gi.sprite = null; }
                var label = tile.Find("Label");
                var lt = label != null ? label.GetComponent<Text>() : null;
                if (lt != null) { Undo.RecordObject(lt, "EterPix-app を削除"); lt.text = ""; }
                var btn = tile.GetComponent<Button>();
                if (btn != null)
                {
                    Undo.RecordObject(btn, "EterPix-app を削除");
                    for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEditor.Events.UnityEventTools.RemovePersistentListener(btn.onClick, i);
                }
                tile.gameObject.SetActive(false);
                did = true;
            }
            if (shell.appSprites != null && shell.appSprites.Length > index) shell.appSprites[index] = null;
            if (shell.appColors != null && shell.appColors.Length > index) shell.appColors[index] = Color.clear;
            if (shell.homeLabels != null && shell.homeLabels.Length > index && shell.homeLabels[index] != null) shell.homeLabels[index].text = "";
            UdonSharpEditorUtility.CopyProxyToUdon(shell);
            EditorUtility.SetDirty(shell);
            Debug.Log("[EtpTrace] builder: " + pad.name + " から EterPix-app を削除");
            return did;
        }

        public static void Build(NudeNyangTablet pad)
        {
            var shell = pad.shell;
            if (shell != null && shell.homeRoot != null && shell.homeIcons != null && shell.homeIcons.Length > 0) EnsureHomeTiles(shell);
            if (shell == null || shell.appFrame == null || shell.homeIcons == null || shell.homeIcons.Length < NudeNyangEterPix.AppId)
            {
                Debug.LogError("[EtpTrace] builder: " + pad.name + " の Shell を準備できませんでした");
                return;
            }

            // サービス(U#コンポーネント)
            var app = EnsureBehaviour<NudeNyangEterPix>(pad.transform, "EterPix app service");
            app.pad = pad;
            var feed = EnsureBehaviour<EterPixAppFeed>(app.transform, "Feed");
            feed.feedUrl = new VRC.SDKBase.VRCUrl(FeedUrl);
            feed.owner = app;
            app.feed = feed;
            UdonSharpEditorUtility.CopyProxyToUdon(feed);
            EditorUtility.SetDirty(feed);

            // 画像の取得(VRCUrl は実行時に作れないので、coid 0〜255 の256件をここで焼く)
            var images = EnsureBehaviour<EterPixAppImages>(app.transform, "Images");
            images.imageUrls = new VRC.SDKBase.VRCUrl[EterPixAppImages.Slots];
            for (int i = 0; i < images.imageUrls.Length; i++) images.imageUrls[i] = new VRC.SDKBase.VRCUrl(FeedUrl + "/" + i.ToString("X2"));
            images.owner = app;
            app.images = images;
            // ポータル: シーン内の既存の NudeNyangTravelPortal につなぐ(無ければ、ポータルのボタンは出さない)
            var portalService = Object.FindObjectsOfType<NudeNyangTravelPortal>(true)
                .FirstOrDefault(x => x.gameObject.scene == pad.gameObject.scene);
            app.portal = portalService;
            app.portalEnabled = portalService != null;
            if (portalService == null) Debug.LogWarning("[EtpTrace] builder: シーンに NudeNyangTravelPortal がありません. 一覧の「ポータルを開く」は出ません(NudeNyang の Travel の準備でポータルを作ると出ます)");
            var geometry = EnsureBehaviour<EterPixAppGeometry>(app.transform, "Geometry");
            app.geometry = geometry;
            UdonSharpEditorUtility.CopyProxyToUdon(images);
            EditorUtility.SetDirty(images);

            // パネル(アプリの面いっぱい)
            var panelT = shell.appFrame.Find("EterPix app") as RectTransform;
            if (panelT == null)
            {
                var go = new GameObject("EterPix app", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                panelT = (RectTransform)go.transform;
                panelT.SetParent(shell.appFrame, false);
            }
            panelT.anchorMin = Vector2.zero; panelT.anchorMax = Vector2.one;
            panelT.offsetMin = panelT.offsetMax = Vector2.zero; panelT.localScale = Vector3.one;
            var bg = panelT.GetComponent<Image>(); bg.color = Dark;
            app.panel = panelT;
            app.group = panelT.GetComponent<CanvasGroup>();

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var icon = EnsureIcon();
            BuildUi(app, panelT, font, icon);

            panelT.gameObject.SetActive(false);
            panelT.SetAsLastSibling();

            // ホームのアイコン(サイトの favicon. 元の色のまま出す)
            var index = NudeNyangEterPix.AppId - 1;
            if (shell.appSprites != null && shell.appSprites.Length > index) shell.appSprites[index] = icon;
            if (shell.appColors != null && shell.appColors.Length > index) shell.appColors[index] = Accent;
            var tile = shell.homeIcons[index];
            if (tile != null)
            {
                // 「削除」で空きタイルに戻された後でも元どおりになるよう、名前・表示・ラベル・クリックの呼び先を保証する
                tile.name = "EterPix";
                tile.gameObject.SetActive(true);
                var tileLabel = tile.Find("Label");
                var tileText = tileLabel != null ? tileLabel.GetComponent<Text>() : null;
                if (tileText != null) tileText.text = "EterPix";
                var tileButton = tile.GetComponent<Button>();
                if (tileButton != null && tileButton.onClick.GetPersistentEventCount() == 0)
                    UnityEditor.Events.UnityEventTools.AddStringPersistentListener(tileButton.onClick,
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(shell).SendCustomEvent, "OpenEterPix");
                var tileImage = tile.GetComponent<Image>();
                if (tileImage != null) { tileImage.color = Accent; EditorUtility.SetDirty(tileImage); }
                var glyph = tile.Find("Glyph");
                var glyphImage = glyph != null ? glyph.GetComponent<Image>() : null;
                if (glyphImage != null) { glyphImage.sprite = icon; glyphImage.color = Color.white; EditorUtility.SetDirty(glyphImage); }
            }

            // パッチ前の素の製品でもこのファイルがコンパイルできるよう、パッチで足されるフィールドはリフレクションで設定する
            // (コンパイルが通らないとパッチを当てるメニュー自体が出ないため)
            var eterpixField = typeof(NudeNyangTabletShell).GetField("eterpix");
            if (eterpixField == null) { Debug.LogError("[EtpTrace] builder: Shell に eterpix フィールドがありません(パッチ未適用)"); return; }
            eterpixField.SetValue(shell, app);
            UdonSharpEditorUtility.CopyProxyToUdon(app);
            UdonSharpEditorUtility.CopyProxyToUdon(shell);
            EditorUtility.SetDirty(app); EditorUtility.SetDirty(shell);
            ConfigureTouchGates(pad, app);
            Debug.Log("[EtpTrace] builder: " + pad.name + " 配線 homeIcons=" + shell.homeIcons.Length);
        }

        // 本体の触り判定ゲート(NudeNyangTouchSlop)を、作り直した画面のカード・一覧に付ける.
        // ゲートはボタン付きの ScrollRect すべてに付く本体の標準の仕組み(タップの許容値・離し検出). 本体の Editor 側にあるためリフレクションで呼ぶ
        static void ConfigureTouchGates(NudeNyangTablet pad, NudeNyangEterPix app)
        {
            System.Type t = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("NudeNyang.Editor.NudeNyangTouchSlopBuilder");
                if (t != null) break;
            }
            var m = t != null ? t.GetMethod("Configure", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) : null;
            if (m == null) { Debug.LogWarning("[EtpTrace] builder: NudeNyangTouchSlopBuilder.Configure が見つかりません(ゲート未設定)"); return; }
            m.Invoke(null, new object[] { pad });
            Debug.Log("[EtpTrace] builder: " + pad.name + " 触り判定ゲートを設定");

            // ボタンの無い所(行の余白・一覧の下の空き・情報ページ全体)でもレーザーでスクロールできるよう、
            // 本体が X フィードの空白部に使う「面の入力」(ConfigureViewportInput)を、一覧と情報ページにも付ける
            var viewportInput = t.GetMethod("ConfigureViewportInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (viewportInput == null) { Debug.LogWarning("[EtpTrace] builder: ConfigureViewportInput が見つかりません(面の入力は未設定)"); return; }
            SurfaceInput(pad, viewportInput, app.listScreen);
            SurfaceInput(pad, viewportInput, app.infoScreen);
            SurfaceInput(pad, viewportInput, app.groupScreen);
        }

        // ScrollRect を持つ画面に、面の入力(押す→ドラッグ→離す)を付ける. ボタンが無くてゲートが無い画面には、ボタン0個のゲートを作る
        static void SurfaceInput(NudeNyangTablet pad, System.Reflection.MethodInfo viewportInput, GameObject screen)
        {
            if (screen == null) return;
            var scroll = screen.GetComponent<ScrollRect>();
            if (scroll == null || scroll.viewport == null) return;
            var gate = screen.GetComponent<NudeNyangTouchSlop>();
            if (gate == null)
            {
                gate = screen.AddUdonSharpComponent<NudeNyangTouchSlop>();
                gate.scroll = scroll;
                gate.buttons = new Button[0];
                gate.eventTargets = new VRC.Udon.UdonBehaviour[0]; gate.eventNames = new string[0]; gate.eventKinds = new int[0];
                gate.gestureSettings = pad.shell == null ? null : pad.shell.gestureSettings;
                var go = new GameObject("Touch displacement", typeof(RectTransform));
                go.transform.SetParent(screen.transform, false);
                gate.sample = (RectTransform)go.transform; gate.sample.sizeDelta = Vector2.one;
            }
            // 面が入力を受けられるよう、viewport の画像を当たり判定ありにする(マスクの見た目は変わらない)
            var vpImg = scroll.viewport.GetComponent<Image>();
            if (vpImg != null) vpImg.raycastTarget = true;
            viewportInput.Invoke(null, new object[] { gate });
            UdonSharpEditorUtility.CopyProxyToUdon(gate);
            EditorUtility.SetDirty(gate);
            Debug.Log("[EtpTrace] builder: " + screen.name + " に面の入力を設定");
        }

        // U#のスクリプトには UdonSharpProgramAsset が要る(無いと AddUdonSharpComponent が失敗する)
        static bool EnsureProgram(string pathWithoutExt)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(pathWithoutExt + ".asset") != null) return false;
            var p = ScriptableObject.CreateInstance<UdonSharp.UdonSharpProgramAsset>();
            p.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(pathWithoutExt + ".cs");
            AssetDatabase.CreateAsset(p, pathWithoutExt + ".asset");
            AssetDatabase.SaveAssets();
            return true;
        }

        // 子オブジェクトに U# コンポーネントを1つ持たせる. プログラムが無い状態で作られた空の UdonBehaviour は作り直す
        static T EnsureBehaviour<T>(Transform parent, string name) where T : UdonSharp.UdonSharpBehaviour
        {
            var t = parent.Find(name);
            if (t != null)
            {
                var existing = t.GetComponent<T>();
                var backing = existing != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(existing) : null;
                if (existing != null && backing != null && backing.programSource != null) return existing;
                Object.DestroyImmediate(t.gameObject);
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddUdonSharpComponent<T>();
        }

        // ホームの配列を AppCount まで伸ばし、12番目のタイルを作る.
        // NudeNyang側の Shell Builder は、アセットが Assets/NudeNyang/ 以外にあると画像を見つけられず動かないため使わない
        // (NudeNyangTabletShellBuilder.Icons() が固定パスを読む). 空きの10・11番目は、実行時のレイアウト処理が
        // homeIcons[i].anchoredPosition を全要素に対して呼ぶので、null にせず非表示のタイルで埋める
        static void EnsureHomeTiles(NudeNyangTabletShell shell)
        {
            int n = NudeNyangTabletShell.AppCount;
            var round = shell.homeIcons[0].GetComponent<Image>().sprite;
            var labelFont = shell.homeLabels != null && shell.homeLabels.Length > 0 && shell.homeLabels[0] != null ? shell.homeLabels[0].font : null;
            int oldIcons = shell.homeIcons.Length;
            System.Array.Resize(ref shell.homeIcons, n);
            System.Array.Resize(ref shell.homeLabels, n);
            System.Array.Resize(ref shell.appSprites, n);
            System.Array.Resize(ref shell.appColors, n);
            for (int i = oldIcons; i < n; i++)
            {
                if (shell.homeIcons[i] != null) continue;
                bool real = i == NudeNyangEterPix.AppId - 1;
                string name = real ? "EterPix" : "(未使用 " + (i + 1) + ")";
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)go.transform;
                rt.SetParent(shell.homeRoot, false);
                rt.sizeDelta = new Vector2(84, 84);
                var img = go.GetComponent<Image>();
                img.sprite = round; img.type = Image.Type.Sliced; img.color = real ? Accent : Color.clear;
                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img; btn.navigation = new Navigation { mode = Navigation.Mode.None };
                if (real)
                    UnityEditor.Events.UnityEventTools.AddStringPersistentListener(btn.onClick,
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(shell).SendCustomEvent, "OpenEterPix");
                var glyph = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
                var gr = (RectTransform)glyph.transform; gr.SetParent(rt, false); gr.sizeDelta = new Vector2(84 * .65f, 84 * .65f);
                var gi = glyph.GetComponent<Image>(); gi.type = Image.Type.Simple; gi.preserveAspect = true; gi.raycastTarget = false;
                var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var lr = (RectTransform)label.transform; lr.SetParent(rt, false);
                lr.sizeDelta = new Vector2(150, 30); lr.anchoredPosition = new Vector2(0, -62);
                var lt = label.GetComponent<Text>();
                lt.font = labelFont; lt.text = real ? "EterPix" : ""; lt.fontSize = 19;
                lt.alignment = TextAnchor.MiddleCenter; lt.color = Color.white; lt.raycastTarget = false; lt.supportRichText = false;
                shell.homeIcons[i] = rt; shell.homeLabels[i] = lt;
                shell.appColors[i] = real ? Accent : Color.clear;
                go.SetActive(real);
            }
        }

        // Art 内の PNG を Sprite として読み込む(Sprite 設定でなければ直す)
        static Sprite EnsureSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { AssetDatabase.ImportAsset(path); importer = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // (旧)2列×3行の長方形のロゴ. ホームのアイコンは favicon に替えたので、Art に無いときだけ作る
        static Sprite EnsureIcon()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            if (existing != null) return existing;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(IconPath));
            const int size = 96;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;
            int cw = 24, ch = 16, gap = 8;
            int left = (size - (cw * 2 + gap)) / 2, top = (size - (ch * 3 + gap * 2)) / 2;
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 2; col++)
                    for (int y = 0; y < ch; y++)
                        for (int x = 0; x < cw; x++)
                            px[(top + row * (ch + gap) + y) * size + left + col * (cw + gap) + x] = Color.white;
            t.SetPixels(px); t.Apply();
            System.IO.File.WriteAllBytes(IconPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(IconPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        }
    }
}
