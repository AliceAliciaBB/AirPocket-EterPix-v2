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

            // ホームのアイコン(ロゴ: 2x3の格子. 合意書で「仮」)
            var index = NudeNyangEterPix.AppId - 1;
            if (shell.appSprites != null && shell.appSprites.Length > index) shell.appSprites[index] = icon;
            if (shell.appColors != null && shell.appColors.Length > index) shell.appColors[index] = Accent;
            var tile = shell.homeIcons[index];
            if (tile != null)
            {
                var tileImage = tile.GetComponent<Image>();
                if (tileImage != null) { tileImage.color = Accent; EditorUtility.SetDirty(tileImage); }
                var glyph = tile.Find("Glyph");
                var glyphImage = glyph != null ? glyph.GetComponent<Image>() : null;
                if (glyphImage != null) { glyphImage.sprite = icon; glyphImage.color = Dark; EditorUtility.SetDirty(glyphImage); }
            }

            // パッチ前の素の製品でもこのファイルがコンパイルできるよう、パッチで足されるフィールドはリフレクションで設定する
            // (コンパイルが通らないとパッチを当てるメニュー自体が出ないため)
            var eterpixField = typeof(NudeNyangTabletShell).GetField("eterpix");
            if (eterpixField == null) { Debug.LogError("[EtpTrace] builder: Shell に eterpix フィールドがありません(パッチ未適用)"); return; }
            eterpixField.SetValue(shell, app);
            UdonSharpEditorUtility.CopyProxyToUdon(app);
            UdonSharpEditorUtility.CopyProxyToUdon(shell);
            EditorUtility.SetDirty(app); EditorUtility.SetDirty(shell);
            Debug.Log("[EtpTrace] builder: " + pad.name + " 配線 homeIcons=" + shell.homeIcons.Length);
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

        // 2列×3行の長方形だけのロゴ. 長方形・線なしの規則に合わせる
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
