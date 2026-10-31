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
    public static class EterPixAppBuilder
    {
        const string FontPath = "Packages/com.aliciliabb.eterpix/Fonts/NotoSansJP-Bold SDF.asset";
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
            var pads = Object.FindObjectsOfType<NudeNyangTablet>(true).Where(p => !EditorUtility.IsPersistent(p) && p.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()).ToArray();
            foreach (var pad in pads) Build(pad);
            Debug.Log("[EtpTrace] builder: " + pads.Length + " 台のタブレットに EterPix-app を配線しました");
        }

        public static void Build(NudeNyangTablet pad)
        {
            // ホームに12個目のアイコンを作る(NudeNyang側の Builder に任せる)
            NudeNyang.Editor.NudeNyangTabletShellBuilder.Configure(pad);
            var shell = pad.shell;
            if (shell == null || shell.appFrame == null || shell.homeIcons == null || shell.homeIcons.Length < NudeNyangEterPix.AppId)
            {
                Debug.LogError("[EtpTrace] builder: " + pad.name + " の Shell を準備できませんでした");
                return;
            }

            // サービス(U#コンポーネント)
            var serviceT = pad.transform.Find("EterPix app service");
            if (serviceT == null)
            {
                var go = new GameObject("EterPix app service");
                go.transform.SetParent(pad.transform, false);
                serviceT = go.transform;
            }
            var app = serviceT.GetComponent<NudeNyangEterPix>();
            if (app == null) app = serviceT.gameObject.AddUdonSharpComponent<NudeNyangEterPix>();
            app.pad = pad;

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

            // 仮の表題(Task 4 で起動画面に置き換わる)
            var titleT = panelT.Find("Title") as RectTransform;
            if (titleT == null)
            {
                var go = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
                titleT = (RectTransform)go.transform; titleT.SetParent(panelT, false);
            }
            titleT.anchorMin = Vector2.zero; titleT.anchorMax = Vector2.one;
            titleT.offsetMin = new Vector2(16, 16); titleT.offsetMax = new Vector2(-16, -16);
            var tmp = titleT.GetComponent<TextMeshProUGUI>();
            tmp.text = "EterPix-app";
            tmp.fontSize = 48; tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white; tmp.raycastTarget = false;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null) tmp.font = font;
            app.title = tmp;

            panelT.gameObject.SetActive(false);
            panelT.SetAsLastSibling();

            // ホームのアイコン(ロゴ: 2x3の格子. 合意書で「仮」)
            var icon = EnsureIcon();
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

            shell.eterpix = app;
            UdonSharpEditorUtility.CopyProxyToUdon(app);
            UdonSharpEditorUtility.CopyProxyToUdon(shell);
            EditorUtility.SetDirty(app); EditorUtility.SetDirty(shell);
            Debug.Log("[EtpTrace] builder: " + pad.name + " 配線 homeIcons=" + shell.homeIcons.Length);
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
