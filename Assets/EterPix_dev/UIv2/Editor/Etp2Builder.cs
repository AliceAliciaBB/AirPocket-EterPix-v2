using System;
using System.Collections.Generic;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static ali.eterpix.ui2.editor.Etp2Parts;
using Object = UnityEngine.Object;

namespace ali.eterpix.ui2.editor
{
    // v2 UI の prefab を作る(ビルダーのコードが正. prefab は手で直さない).
    // 既存の prefab はルートのオブジェクトとルートの部品を残し、子だけ作り直す.
    // ルートの部品のうち「ワールド作者がつなぐ」欄はビルダーが触らないので、シーンのインスタンスの設定が残る.
    // 計画書: docs/superpowers/plans/2026-11-14-unity-ui-v2.md
    public static partial class Etp2Builder
    {
        [MenuItem("EterPix/UIv2/prefab をビルド")]
        public static void BuildAll()
        {
            if (EnsurePrograms())
            {
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                Debug.LogWarning("[Etp2Trace] builder: U# プログラムを作りました. コンパイルの完了を待って、もう一度実行してください");
                return;
            }
            EnsureSpriteImport();
            ResetCache();
            BuildPrefab("Etp2_Theme", BuildTheme);
            BuildPrefab("Etp2_MockData", BuildMockData);
            BuildPrefab("Etp2_MenuMonitor", BuildMenuMonitor);
            BuildPrefab("Etp2_DetailMonitor", BuildDetailMonitor);
            BuildPrefab("Etp2_Viewer", BuildViewer);
            BuildPrefab("Etp2_PhotoFrame", BuildPhotoFrame);
            AssetDatabase.SaveAssets();
            Debug.Log("[Etp2Trace] builder: ビルドが終わりました(" + Etp2Spec.PrefabDir + ")");
        }

        // ---- 共通 ----

        // アイコンの PNG を UI の Sprite として取り込む(白・透過、ミップなし)
        static void EnsureSpriteImport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Etp2Spec.SpriteDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                if (ti.textureType == TextureImporterType.Sprite && ti.spriteImportMode == SpriteImportMode.Single && !ti.mipmapEnabled) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
        }

        static bool EnsurePrograms()
        {
            bool created = false;
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { Etp2Spec.ScriptDir }))
            {
                string cs = AssetDatabase.GUIDToAssetPath(guid);
                string asset = cs.Substring(0, cs.Length - 3) + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) continue;
                var p = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                p.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(cs);
                AssetDatabase.CreateAsset(p, asset);
                created = true;
            }
            if (created) AssetDatabase.SaveAssets();
            return created;
        }

        static void BuildPrefab(string name, Action<GameObject> build)
        {
            string path = Etp2Spec.PrefabDir + "/" + name + ".prefab";
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            if (!existing && !AssetDatabase.IsValidFolder(Etp2Spec.PrefabDir))
                AssetDatabase.CreateFolder(Etp2Spec.PackageRoot, "Prefabs");
            GameObject root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name);
            try
            {
                string before = existing ? Canonical(root) : null;
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                build(root);
                CopyAllProxies(root);
                // 作り直した子は fileID が毎回変わるので、中身が同じなら保存しない(同じ入力で差分を出さない)
                if (before != null && before == Canonical(root))
                {
                    Debug.Log("[Etp2Trace] builder: " + name + " は変更なし");
                    return;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[Etp2Trace] builder: " + name + " を保存しました");
            }
            finally
            {
                if (existing) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        // prefab の中身を、fileID・instanceID によらない文字列にする.
        // 階層の順に、名前・部品の種類・部品の値(参照は「階層のパス」か「アセットのパス」に置き換える)を並べる
        static readonly System.Text.RegularExpressions.Regex InstanceIdRx =
            new System.Text.RegularExpressions.Regex("\"instanceID\":(-?\\d+)");

        static string Canonical(GameObject root)
        {
            var sb = new System.Text.StringBuilder();
            var paths = new Dictionary<int, string>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string p = PathOf(root.transform, t);
                paths[t.gameObject.GetInstanceID()] = p;
                var comps = t.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                    if (comps[i] != null) paths[comps[i].GetInstanceID()] = p + "#" + i + ":" + comps[i].GetType().Name;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                sb.Append(paths[t.gameObject.GetInstanceID()]).Append(" active=").Append(t.gameObject.activeSelf).Append('\n');
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) { sb.Append("  (missing)\n"); continue; }
                    string json = EditorJsonUtility.ToJson(c);
                    json = InstanceIdRx.Replace(json, m =>
                    {
                        int id = int.Parse(m.Groups[1].Value);
                        if (id == 0) return "null";
                        if (paths.TryGetValue(id, out var local)) return "\"" + local + "\"";
                        var obj = EditorUtility.InstanceIDToObject(id);
                        return obj == null ? "\"?\"" : "\"" + AssetDatabase.GetAssetPath(obj) + ":" + obj.name + "\"";
                    });
                    sb.Append("  ").Append(c.GetType().Name).Append(' ').Append(json).Append('\n');
                }
            }
            return sb.ToString();
        }

        static string PathOf(Transform root, Transform t)
        {
            if (t == root) return "";
            string p = t.name + "[" + t.GetSiblingIndex() + "]";
            for (var x = t.parent; x != null && x != root; x = x.parent) p = x.name + "[" + x.GetSiblingIndex() + "]/" + p;
            return p;
        }

        static T RootComponent<T>(GameObject root) where T : UdonSharpBehaviour
        {
            var c = root.GetComponent<T>();
            if (c != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(c) != null) return c;
            if (c != null) Object.DestroyImmediate(c);
            return root.AddUdonSharpComponent<T>();
        }

        static T Add<T>(Component on) where T : UdonSharpBehaviour
        {
            return on.gameObject.AddUdonSharpComponent<T>();
        }

        // ワールドに置く Canvas(v1 のモニターと同じ: ルートは Canvas ではなく、子の Screen が World Space の Canvas)
        static RectTransform Screen(GameObject root, Vector2 size, Vector2 pivot)
        {
            root.transform.localScale = Vector3.one * Etp2Spec.WorldScale;
            var rt = NewRect(root.transform, "Screen");
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            rt.gameObject.AddComponent<GraphicRaycaster>();
            rt.gameObject.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            var box = rt.gameObject.GetComponent<BoxCollider>();
            if (box == null) box = rt.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, 1);
            box.center = new Vector3((0.5f - pivot.x) * size.x, (0.5f - pivot.y) * size.y, 0);
            return rt;
        }

        // 画面ごとのテーマの塗り先とホバーの札. 札(Tip)は中身を作り終えてから最後に作る(一番上に重ねるため)
        static etp2_theme_target BeginScreen(RectTransform screen, Etp2Ctx ctx)
        {
            var theme = Add<etp2_theme_target>(screen);
            ctx.tooltip = Add<etp2_tooltip>(screen);
            return theme;
        }

        static void EndScreen(RectTransform screen, Etp2Ctx ctx, etp2_theme_target theme)
        {
            // 1.5 ホバーの札: 地は背景色、文字と輪郭(1px)は文字色. 高さ 32、左右の余白 8、文字 13
            var tip = Img(ctx, screen, "tx_Tip", Role.Tx);
            Place(tip.rectTransform, 0, 0, 64, Etp2Spec.TipH);
            int px = (int)(Etp2Spec.TipPadX + 1);
            var g = HLG(tip, 0, TextAnchor.MiddleCenter, new RectOffset(px, px, 1, 1));
            g.childForceExpandHeight = true;
            var fit = tip.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var inner = Img(ctx, tip.transform, "bg_TipBg", Role.Bg);
            inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch(inner.rectTransform, 1, 1, 1, 1);
            var text = NewText(ctx, tip.transform, "tx_TipText", "", Etp2Spec.FsBody, Role.Tx, TextAlignmentOptions.Center, false);
            text.overflowMode = TextOverflowModes.Overflow;
            tip.gameObject.SetActive(false);

            ctx.tooltip.screenRect = screen;
            ctx.tooltip.tipRect = tip.rectTransform;
            ctx.tooltip.tipText = text;

            ctx.FillTheme(theme);
            EditorPaint(theme);
        }

        // エディタで見たときも黒テーマの色にしておく(実行時は etp2_theme_target が塗り直す)
        static void EditorPaint(etp2_theme_target t)
        {
            Color bg = etp2_theme.GetPresetColor(0, etp2_theme.RoleBackground);
            Color tx = etp2_theme.GetPresetColor(0, etp2_theme.RoleText);
            Color bt = etp2_theme.GetPresetColor(0, etp2_theme.RoleButton);
            Color pr = etp2_theme.GetPresetColor(0, etp2_theme.RolePressed);
            Color ac = etp2_theme.GetPresetColor(0, etp2_theme.RoleAccent);
            Color at = etp2_theme.GetPresetColor(0, etp2_theme.RoleAccentText);
            Color ds = etp2_theme.GetPresetColor(0, etp2_theme.RoleDisabledText);
            foreach (var i in t.bgImages) i.color = bg;
            foreach (var i in t.txTexts) i.color = tx;
            foreach (var i in t.txImages) i.color = tx;
            foreach (var i in t.btnImages) i.color = bt;
            foreach (var i in t.accImages) i.color = ac;
            foreach (var i in t.accTexts) i.color = at;
            foreach (var i in t.accTextImages) i.color = at;
            foreach (var i in t.disTexts) i.color = ds;
            foreach (var b in t.btnButtons) SetColors(b, bt, pr);
            foreach (var b in t.accButtons) SetColors(b, ac, ac);
            foreach (var s in t.scrollbars) SetColors(s, bt, pr);
        }

        static void SetColors(Selectable s, Color face, Color pressed)
        {
            var cb = s.colors;
            cb.normalColor = cb.highlightedColor = cb.selectedColor = cb.disabledColor = face;
            cb.pressedColor = pressed;
            cb.colorMultiplier = 1;
            cb.fadeDuration = 0;
            s.colors = cb;
        }

        // 動的に塗るボタン(選択中で色が変わるもの)を、エディタでは普通のボタン色にしておく
        static void EditorPaintDynamic(IEnumerable<Button> buttons, IEnumerable<TextMeshProUGUI> labels = null, IEnumerable<Image> icons = null)
        {
            Color bt = etp2_theme.GetPresetColor(0, etp2_theme.RoleButton);
            Color pr = etp2_theme.GetPresetColor(0, etp2_theme.RolePressed);
            Color tx = etp2_theme.GetPresetColor(0, etp2_theme.RoleText);
            foreach (var b in buttons) if (b != null) SetColors(b, bt, pr);
            if (labels != null) foreach (var l in labels) if (l != null) l.color = tx;
            if (icons != null) foreach (var i in icons) if (i != null) i.color = tx;
        }

        // ---- テーマ・データ ----

        static void BuildTheme(GameObject root)
        {
            RootComponent<etp2_theme>(root);
        }

        static void BuildMockData(GameObject root)
        {
            var d = RootComponent<etp2_mock_data>(root);
            int n = Etp2Spec.Posts.GetLength(0);
            d.postWorld = new string[n]; d.postUser = new string[n]; d.postDate = new string[n];
            d.postTitle = new string[n]; d.postDesc = new string[n];
            d.postViews = new int[n]; d.postStars = new int[n]; d.postLikes = new int[n];
            d.postColor = new Color[n];
            for (int i = 0; i < n; i++)
            {
                d.postWorld[i] = Etp2Spec.Posts[i, 0];
                d.postUser[i] = Etp2Spec.Posts[i, 1];
                d.postDate[i] = Etp2Spec.Posts[i, 2];
                d.postTitle[i] = Etp2Spec.PostTitle(i);
                d.postDesc[i] = Etp2Spec.PostDesc(i);
                d.postViews[i] = Etp2Spec.PostViews(i);
                d.postStars[i] = Etp2Spec.PostStars(i);
                d.postLikes[i] = Etp2Spec.PostLikes(i);
                d.postColor[i] = Etp2Spec.PostColor(i);
            }
            int w = Etp2Spec.Worlds.GetLength(0);
            d.worldName = new string[w]; d.worldAuthor = new string[w]; d.worldDesc = new string[w];
            for (int i = 0; i < w; i++)
            {
                d.worldName[i] = Etp2Spec.Worlds[i, 0];
                d.worldAuthor[i] = Etp2Spec.Worlds[i, 1];
                d.worldDesc[i] = Etp2Spec.WorldDesc;
            }
        }

        // ---- 3章 メニューモニター・詳細モニターの骨組み ----

        class Monitor
        {
            public RectTransform screen;
            public etp2_theme_target theme;
            public etp2_nav nav;
            public RectTransform[] contents;    // ページの中身(縦に並ぶ)
            public RectTransform[] titleRows;   // 見出しの行(右にアイコンボタンを足せる)
        }

        static void Header(Etp2Ctx ctx, RectTransform screen)
        {
            // ロゴの「E」: 地はアクセント、文字 30 の #FFFFFF(テーマに関係なく白)
            var logo = Img(ctx, screen, "acc_Logo", Role.Acc);
            Place(logo.rectTransform, Etp2Spec.Pad, Etp2Spec.Pad, Etp2Spec.HeaderH, Etp2Spec.HeaderH);
            var e = NewText(ctx, logo.transform, "LogoE", "E", Etp2Spec.FsLogo, Role.None, TextAlignmentOptions.Center, false);
            e.color = Color.white;
            e.overflowMode = TextOverflowModes.Overflow;
            Stretch(e.rectTransform);
            var name = NewText(ctx, screen, "tx_LogoText", "EterPix", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
            Place(name.rectTransform, Etp2Spec.Pad + Etp2Spec.HeaderH + Etp2Spec.Gap, Etp2Spec.Pad, 160, Etp2Spec.HeaderH);
        }

        static Monitor BuildMonitorSkeleton(GameObject root, Etp2Ctx ctx, string monitorName, string[,] items)
        {
            var m = new Monitor();
            Vector2 size = Etp2Spec.MonitorSize;
            m.screen = Screen(root, size, new Vector2(0.5f, 1f));
            m.theme = BeginScreen(m.screen, ctx);
            var bg = Img(ctx, m.screen, "bg_Background", Role.Bg);
            Stretch(bg.rectTransform);
            Header(ctx, m.screen);

            m.nav = Add<etp2_nav>(m.screen);
            m.nav.monitorName = monitorName;
            m.nav.theme = m.theme;
            m.nav.tooltip = ctx.tooltip;
            ctx.listeners.Add(m.nav);

            var close = CloseButton(ctx, m.screen, "Close");
            Place((RectTransform)close.transform, size.x - Etp2Spec.Pad - Etp2Spec.IconButton, Etp2Spec.Pad, Etp2Spec.IconButton, Etp2Spec.IconButton);
            Hover(ctx, close, "閉じる");
            OnClick(close, m.nav, nameof(etp2_nav.OnClose));

            // ×で隠す中身
            var body = NewRect(m.screen, "Body");
            Stretch(body);
            m.nav.body = body.gameObject;

            float top = Etp2Spec.Pad + Etp2Spec.HeaderH + Etp2Spec.Pad;          // 72
            float bottom = size.y - Etp2Spec.Pad;                                  // 776
            float pageX = Etp2Spec.Pad + Etp2Spec.NavW + Etp2Spec.Pad;             // 72
            var pageArea = NewRect(body, "Pages");
            Place(pageArea, pageX, top, size.x - Etp2Spec.Pad - pageX, bottom - top);

            int n = items.GetLength(0);
            m.contents = new RectTransform[n];
            m.titleRows = new RectTransform[n];
            var pages = new GameObject[n];
            for (int i = 0; i < n; i++)
            {
                var page = NewRect(pageArea, "Page" + i + "_" + items[i, 0]);
                Stretch(page);
                var content = ScrollArea(ctx, page, out _);
                VLG(content, Etp2Spec.Gap);
                // 1行目は見出し(24、行の高さ 40)
                var titleRow = NewRect(content, "TitleRow");
                HLG(titleRow, Etp2Spec.Gap);
                LE(titleRow, -1, Etp2Spec.PageTitleH);
                var title = NewText(ctx, titleRow, "tx_Title", items[i, 1], Etp2Spec.FsPageTitle, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
                LE(title, -1, Etp2Spec.PageTitleH, 1);
                Spacer(content, Etp2Spec.PageTitleGap - Etp2Spec.Gap * 2);   // 見出しの下 16(並びの間 8 × 2 で 16)
                pages[i] = page.gameObject;
                m.contents[i] = content;
                m.titleRows[i] = titleRow;
            }
            m.nav.pages = pages;

            // ナビの帯(広げたとき. 幅 216)と暗幕(帯より右)
            var band = Img(ctx, body, "bg_NavBand", Role.Bg, true);
            Place(band.rectTransform, 0, Etp2Spec.Pad + Etp2Spec.HeaderH, Etp2Spec.NavBandW, size.y - Etp2Spec.Pad - Etp2Spec.HeaderH);
            m.nav.band = band.gameObject;
            var dim = Img(ctx, body, "dim_NavDim", Role.None, true);
            dim.color = new Color(0, 0, 0, Etp2Spec.DimAlpha);
            Place(dim.rectTransform, Etp2Spec.NavBandW, Etp2Spec.Pad + Etp2Spec.HeaderH, size.x - Etp2Spec.NavBandW, size.y - Etp2Spec.Pad - Etp2Spec.HeaderH);
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.navigation = new Navigation { mode = Navigation.Mode.None };
            OnClick(dimButton, m.nav, nameof(etp2_nav.OnDim));
            m.nav.dim = dim.gameObject;

            // ナビボタン 40×40 を間 8 で縦に. 広げると幅 200 で名前が出る
            var navCol = NewRect(body, "Nav");
            Place(navCol, Etp2Spec.Pad, top, Etp2Spec.NavExpandedW, bottom - top);
            var buttons = new Button[n];
            var rects = new RectTransform[n];
            var icons = new Image[n];
            var labels = new TextMeshProUGUI[n];
            var hovers = new etp2_hover[n];
            for (int i = 0; i < n; i++)
            {
                var b = NewButton(ctx, navCol, "Nav" + i + "_" + items[i, 0], BtnStyle.Dynamic);
                Place((RectTransform)b.transform, 0, i * (Etp2Spec.IconButton + Etp2Spec.Gap), Etp2Spec.NavW, Etp2Spec.IconButton);
                var icon = Img(ctx, b.transform, "Icon", Role.None);
                icon.sprite = SpriteOf(items[i, 0]);
                icon.preserveAspect = true;
                float o = (Etp2Spec.IconButton - Etp2Spec.Icon) / 2f;
                Place(icon.rectTransform, o, o, Etp2Spec.Icon, Etp2Spec.Icon);
                var label = NewText(ctx, b.transform, "Label", items[i, 1], Etp2Spec.FsButton, Role.None, TextAlignmentOptions.MidlineLeft, false);
                Place(label.rectTransform, Etp2Spec.IconButton, 0, Etp2Spec.NavExpandedW - Etp2Spec.IconButton - Etp2Spec.Gap, Etp2Spec.IconButton);
                hovers[i] = Hover(ctx, b, items[i, 1], etp2_hover.SideRight);
                OnClickIndex(b, m.nav, nameof(etp2_nav.OnNav), i);
                buttons[i] = b; rects[i] = (RectTransform)b.transform; icons[i] = icon; labels[i] = label;
            }
            m.nav.navButtons = buttons;
            m.nav.navRects = rects;
            m.nav.navIcons = icons;
            m.nav.navLabels = labels;
            m.nav.navHovers = hovers;
            EditorPaintDynamic(buttons, labels, icons);

            // 展開ボタンは常に一番下
            var toggle = NewButton(ctx, navCol, "NavToggle", BtnStyle.Normal);
            Place((RectTransform)toggle.transform, 0, bottom - top - Etp2Spec.IconButton, Etp2Spec.NavW, Etp2Spec.IconButton);
            var tIcon = Img(ctx, toggle.transform, "tx_Icon", Role.Tx);
            tIcon.sprite = SpriteOf("expand");
            tIcon.preserveAspect = true;
            float to = (Etp2Spec.IconButton - Etp2Spec.Icon) / 2f;
            Place(tIcon.rectTransform, to, to, Etp2Spec.Icon, Etp2Spec.Icon);
            var tLabel = NewText(ctx, toggle.transform, "tx_Label", "たたむ", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
            Place(tLabel.rectTransform, Etp2Spec.IconButton, 0, Etp2Spec.NavExpandedW - Etp2Spec.IconButton - Etp2Spec.Gap, Etp2Spec.IconButton);
            m.nav.toggleButton = toggle;
            m.nav.toggleRect = (RectTransform)toggle.transform;
            m.nav.toggleIcon = tIcon;
            m.nav.toggleLabel = tLabel;
            m.nav.toggleHover = Hover(ctx, toggle, "ひろげる", etp2_hover.SideRight);
            m.nav.expandSprite = SpriteOf("expand");
            m.nav.collapseSprite = SpriteOf("collapse");
            OnClick(toggle, m.nav, nameof(etp2_nav.OnToggle));

            // 編集のときは畳んだ形・最初のページだけ見せる
            band.gameObject.SetActive(false);
            dim.gameObject.SetActive(false);
            foreach (var l in labels) l.gameObject.SetActive(false);
            tLabel.gameObject.SetActive(false);
            for (int i = 0; i < n; i++) pages[i].SetActive(i == 0);
            return m;
        }

        // ---- ページの中身の部品 ----

        static TextMeshProUGUI Heading(Etp2Ctx ctx, Transform parent, string text)
        {
            return NewText(ctx, parent, "tx_Heading", text, Etp2Spec.FsHeading, Role.Tx);
        }

        static TextMeshProUGUI Body(Etp2Ctx ctx, Transform parent, string text, string name = "tx_Body")
        {
            return NewText(ctx, parent, name, text, Etp2Spec.FsBody, Role.Tx, TextAlignmentOptions.TopLeft, true, true);
        }

        // 見出しの右端のアイコンボタン
        static Button TitleAction(Etp2Ctx ctx, RectTransform titleRow, string icon, string label, UdonSharpBehaviour target, string eventName)
        {
            var b = IconButton(ctx, titleRow, "Action_" + icon, icon, out _);
            Hover(ctx, b, label);
            OnClick(b, target, eventName);
            return b;
        }
    }
}
