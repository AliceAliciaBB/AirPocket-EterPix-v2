using System.Collections.Generic;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ali.eterpix.ui2.editor
{
    // 画面1つぶんの組み立ての文脈. 色の塗り先を集め、最後に etp2_theme_target へ入れる
    public class Etp2Ctx
    {
        public etp2_tooltip tooltip;
        public readonly List<Image> bg = new List<Image>();
        public readonly List<TextMeshProUGUI> txTexts = new List<TextMeshProUGUI>();
        public readonly List<Image> txImages = new List<Image>();
        public readonly List<Button> btn = new List<Button>();
        public readonly List<Image> btnImages = new List<Image>();
        public readonly List<Scrollbar> scrollbars = new List<Scrollbar>();
        public readonly List<Button> accButtons = new List<Button>();
        public readonly List<Image> accImages = new List<Image>();
        public readonly List<TextMeshProUGUI> accTexts = new List<TextMeshProUGUI>();
        public readonly List<Image> accTextImages = new List<Image>();
        public readonly List<TextMeshProUGUI> disTexts = new List<TextMeshProUGUI>();
        public readonly List<UdonSharpBehaviour> listeners = new List<UdonSharpBehaviour>();

        public void FillTheme(etp2_theme_target t)
        {
            t.bgImages = bg.ToArray();
            t.txTexts = txTexts.ToArray();
            t.txImages = txImages.ToArray();
            t.btnButtons = btn.ToArray();
            t.btnImages = btnImages.ToArray();
            t.scrollbars = scrollbars.ToArray();
            t.accButtons = accButtons.ToArray();
            t.accImages = accImages.ToArray();
            t.accTexts = accTexts.ToArray();
            t.accTextImages = accTextImages.ToArray();
            t.disTexts = disTexts.ToArray();
            t.listeners = listeners.ToArray();
        }
    }

    // 色の役(オブジェクト名の接頭辞と同じ意味)
    public enum Role { None, Bg, Tx, Btn, Acc, AccTx, Dis }

    // ボタンの種類
    public enum BtnStyle { Normal, Accent, Dynamic }   // Dynamic: 色はスクリプトが塗る(選択中で変わるもの)

    public static class Etp2Parts
    {
        static TMP_FontAsset _font;
        static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        public static void ResetCache() { _font = null; _sprites.Clear(); }

        public static TMP_FontAsset Font
        {
            get
            {
                if (_font == null) _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Etp2Spec.FontPath);
                return _font;
            }
        }

        public static Sprite SpriteOf(string icon)
        {
            if (_sprites.TryGetValue(icon, out var s) && s != null) return s;
            string path = Etp2Spec.SpriteDir + "/etp2_icon_" + icon + ".png";
            s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) Debug.LogError("[Etp2Trace] builder: スプライトがありません " + path);
            _sprites[icon] = s;
            return s;
        }

        // ---- 形 ----

        public static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        // 左上を原点(下が +)として置く(モックの座標のまま書ける)
        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static LayoutElement LE(Component c, float prefW = -1, float prefH = -1, float flexW = -1, float minH = -1, float minW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = prefW;
            le.preferredHeight = prefH;
            le.flexibleWidth = flexW;
            le.flexibleHeight = flexH;
            le.minHeight = minH >= 0 ? minH : (prefH >= 0 ? prefH : -1);
            le.minWidth = minW;
            return le;
        }

        public static VerticalLayoutGroup VLG(Component c, float spacing, RectOffset pad = null)
        {
            var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = pad ?? new RectOffset(0, 0, 0, 0);
            g.childAlignment = TextAnchor.UpperLeft;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HLG(Component c, float spacing, TextAnchor align = TextAnchor.MiddleLeft, RectOffset pad = null)
        {
            var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = pad ?? new RectOffset(0, 0, 0, 0);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        public static void FitVertical(Component c)
        {
            var f = c.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        // ---- 色 ----

        public static Image Img(Etp2Ctx ctx, Transform parent, string name, Role role, bool raycast = false)
        {
            var rt = NewRect(parent, name);
            rt.gameObject.AddComponent<CanvasRenderer>();
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = raycast;
            Register(ctx, img, role);
            return img;
        }

        public static void Register(Etp2Ctx ctx, Image img, Role role)
        {
            switch (role)
            {
                case Role.Bg: ctx.bg.Add(img); break;
                case Role.Tx: ctx.txImages.Add(img); break;
                case Role.Btn: ctx.btnImages.Add(img); break;
                case Role.Acc: ctx.accImages.Add(img); break;
                case Role.AccTx: ctx.accTextImages.Add(img); break;
            }
        }

        public static TextMeshProUGUI NewText(Etp2Ctx ctx, Transform parent, string name, string text, float size, Role role,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool wrap = true, bool lineHeight16 = false)
        {
            var rt = NewRect(parent, name);
            rt.gameObject.AddComponent<CanvasRenderer>();
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.fontSize = size;
            t.text = text;
            t.color = Color.white;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            t.margin = Vector4.zero;
            if (wrap)
            {
                t.enableWordWrapping = true;
                t.overflowMode = TextOverflowModes.Overflow;
            }
            else
            {
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (lineHeight16) t.lineSpacing = LineSpacingFor(Etp2Spec.BodyLineHeight);
            switch (role)
            {
                case Role.Tx: ctx.txTexts.Add(t); break;
                case Role.AccTx: ctx.accTexts.Add(t); break;
                case Role.Dis: ctx.disTexts.Add(t); break;
            }
            return t;
        }

        // 行の高さを「文字の大きさの n 倍」にするための TMP の lineSpacing(単位は 0.01em)
        public static float LineSpacingFor(float multiple)
        {
            var fi = Font.faceInfo;
            float natural = fi.lineHeight / fi.pointSize;
            return (multiple - natural) * 100f;
        }

        // ---- ボタン ----

        public static Button NewButton(Etp2Ctx ctx, Transform parent, string name, BtnStyle style)
        {
            string prefix = style == BtnStyle.Accent ? "acc_" : "btn_";
            var img = Img(ctx, parent, prefix + name, Role.None, true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.ColorTint;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (style == BtnStyle.Normal) ctx.btn.Add(b);
            else if (style == BtnStyle.Accent) ctx.accButtons.Add(b);
            return b;
        }

        public static void OnClick(Button b, UdonSharpBehaviour target, string eventName)
        {
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
            UnityEventTools.AddStringPersistentListener(b.onClick, backing.SendCustomEvent, eventName);
        }

        // 行ごとに番号を持つボタン(etp2_index_button)
        public static etp2_index_button OnClickIndex(Button b, UdonSharpBehaviour target, string eventName, int index)
        {
            var ib = b.gameObject.AddUdonSharpComponent<etp2_index_button>();
            ib.target = target;
            ib.eventName = eventName;
            ib.index = index;
            OnClick(b, ib, nameof(etp2_index_button.Press));
            return ib;
        }

        // ホバーの札(1.5). EventTrigger から etp2_hover を呼ぶ
        public static etp2_hover Hover(Etp2Ctx ctx, Component button, string label, int side = etp2_hover.SideBelow)
        {
            var h = button.gameObject.AddUdonSharpComponent<etp2_hover>();
            h.tooltip = ctx.tooltip;
            h.label = label;
            h.side = side;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(h);
            var et = button.gameObject.AddComponent<EventTrigger>();
            et.triggers = new List<EventTrigger.Entry>();
            AddTrigger(et, EventTriggerType.PointerEnter, backing, nameof(etp2_hover.OnPointerEnterEvent));
            AddTrigger(et, EventTriggerType.PointerExit, backing, nameof(etp2_hover.OnPointerExitEvent));
            AddTrigger(et, EventTriggerType.PointerDown, backing, nameof(etp2_hover.OnPointerDownEvent));
            return h;
        }

        static void AddTrigger(EventTrigger et, EventTriggerType type, VRC.Udon.UdonBehaviour backing, string eventName)
        {
            var e = new EventTrigger.Entry { eventID = type, callback = new EventTrigger.TriggerEvent() };
            UnityEventTools.AddStringPersistentListener(e.callback, backing.SendCustomEvent, eventName);
            et.triggers.Add(e);
        }

        // 1.4 アイコンボタン 40×40、アイコン 20×20
        public static Button IconButton(Etp2Ctx ctx, Transform parent, string name, string icon, out Image iconImage, BtnStyle style = BtnStyle.Normal)
        {
            var b = NewButton(ctx, parent, name, style);
            LE(b, Etp2Spec.IconButton, Etp2Spec.IconButton, 0, -1, Etp2Spec.IconButton);
            ((RectTransform)b.transform).sizeDelta = new Vector2(Etp2Spec.IconButton, Etp2Spec.IconButton);
            float o = (Etp2Spec.IconButton - Etp2Spec.Icon) / 2f;
            iconImage = Img(ctx, b.transform, "tx_Icon", style == BtnStyle.Normal ? Role.Tx : (style == BtnStyle.Accent ? Role.AccTx : Role.None));
            iconImage.sprite = SpriteOf(icon);
            iconImage.preserveAspect = true;
            Place(iconImage.rectTransform, o, o, Etp2Spec.Icon, Etp2Spec.Icon);
            return b;
        }

        // 1.4 ×ボタン: 長さ 20・太さ 2 の長方形2本を ±45°
        public static Button CloseButton(Etp2Ctx ctx, Transform parent, string name)
        {
            var b = NewButton(ctx, parent, name, BtnStyle.Normal);
            LE(b, Etp2Spec.IconButton, Etp2Spec.IconButton, 0, -1, Etp2Spec.IconButton);
            ((RectTransform)b.transform).sizeDelta = new Vector2(Etp2Spec.IconButton, Etp2Spec.IconButton);
            for (int i = 0; i < 2; i++)
            {
                var bar = Img(ctx, b.transform, "tx_Bar" + i, Role.Tx);
                var rt = bar.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(Etp2Spec.Icon, Etp2Spec.DividerH);
                rt.localRotation = Quaternion.Euler(0, 0, i == 0 ? 45 : -45);
            }
            return b;
        }

        // 1.4 文字のボタン: 高さ 40、左右の余白 16. 幅は文字に合わせる
        public static Button TextButton(Etp2Ctx ctx, Transform parent, string name, string label, BtnStyle style, out TextMeshProUGUI text, float height = Etp2Spec.ButtonH)
        {
            var b = NewButton(ctx, parent, name, style);
            int p = (int)Etp2Spec.TextButtonPadX;
            HLG(b, 0, TextAnchor.MiddleCenter, new RectOffset(p, p, 0, 0));
            LE(b, -1, height);
            Role role = style == BtnStyle.Normal ? Role.Tx : (style == BtnStyle.Accent ? Role.AccTx : Role.None);
            ArrowLabel(ctx, b.transform, label, role, out text, out _, out _);
            return b;
        }

        // 文字の前後の ◀ / ▶ はアイコン(三角のスプライト)で出す.
        // 共有フォント(NotoSansJP-Bold SDF)のアトラスが満杯で、◀▶ を足せないため. ▲▼ はアトラスにあるので文字のまま
        public const string ArrowLeft = "◀ ";
        public const string ArrowRight = " ▶";
        public const float ArrowIcon = 16;

        public static void ArrowLabel(Etp2Ctx ctx, Transform parent, string label, Role role,
            out TextMeshProUGUI text, out Image leftIcon, out Image rightIcon)
        {
            leftIcon = rightIcon = null;
            bool left = label.StartsWith(ArrowLeft);
            bool right = label.EndsWith(ArrowRight);
            if (left) label = label.Substring(ArrowLeft.Length);
            if (right) label = label.Substring(0, label.Length - ArrowRight.Length);
            string prefix = role == Role.AccTx ? "acctx_" : (role == Role.Tx ? "tx_" : "");
            if (left) leftIcon = ArrowIconImage(ctx, parent, prefix + "ArrowLeft", "collapse", role);
            text = NewText(ctx, parent, prefix + "Label", label, Etp2Spec.FsButton, role, TextAlignmentOptions.Center, false);
            if (right) rightIcon = ArrowIconImage(ctx, parent, prefix + "ArrowRight", "expand", role);
            var g = parent.GetComponent<HorizontalLayoutGroup>();
            if (g != null && (left || right)) g.spacing = 4;   // 文字の空白1つぶん
        }

        static Image ArrowIconImage(Etp2Ctx ctx, Transform parent, string name, string sprite, Role role)
        {
            var i = Img(ctx, parent, name, role);
            i.sprite = SpriteOf(sprite);
            i.preserveAspect = true;
            LE(i, ArrowIcon, ArrowIcon, 0, -1, ArrowIcon);
            return i;
        }

        // 1.4 切り替え(セグメント): 高さ 40、間 8 で等分
        public static RectTransform Segment(Etp2Ctx ctx, Transform parent, string name, string[] labels,
            UdonSharpBehaviour target, string eventName, out Button[] buttons, out TextMeshProUGUI[] texts, out Image[] arrowIcons)
        {
            var row = NewRect(parent, name);
            var g = HLG(row, Etp2Spec.Gap);
            g.childForceExpandWidth = true;
            LE(row, -1, Etp2Spec.ButtonH);
            buttons = new Button[labels.Length];
            texts = new TextMeshProUGUI[labels.Length];
            arrowIcons = new Image[labels.Length * 2];   // ボタン i の左 = i*2、右 = i*2+1(無ければ null)
            for (int i = 0; i < labels.Length; i++)
            {
                var b = NewButton(ctx, row, "Seg" + i, BtnStyle.Dynamic);
                LE(b, 0, Etp2Spec.ButtonH, 1);
                var inner = NewRect(b.transform, "Inner");
                Stretch(inner);
                HLG(inner, 0, TextAnchor.MiddleCenter);
                ArrowLabel(ctx, inner, labels[i], Role.None, out var t, out arrowIcons[i * 2], out arrowIcons[i * 2 + 1]);
                OnClickIndex(b, target, eventName, i);
                buttons[i] = b;
                texts[i] = t;
            }
            return row;
        }

        // 1.4 URL 欄: 高さ 36、読み取り専用の TMP_InputField(v1 と同じ)
        public static TMP_InputField UrlField(Etp2Ctx ctx, Transform parent, string url)
        {
            var bgImg = Img(ctx, parent, "btn_UrlField", Role.Btn, true);
            LE(bgImg, -1, Etp2Spec.UrlFieldH);
            var area = NewRect(bgImg.transform, "Text Area");
            Stretch(area, Etp2Spec.UrlFieldPadX, 0, Etp2Spec.UrlFieldPadX, 0);
            area.gameObject.AddComponent<RectMask2D>();
            var t = NewText(ctx, area, "tx_UrlText", url, Etp2Spec.FsBody, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
            t.overflowMode = TextOverflowModes.Overflow;
            Stretch(t.rectTransform);
            var f = bgImg.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = t;
            f.text = url;
            f.readOnly = true;
            f.lineType = TMP_InputField.LineType.SingleLine;
            f.targetGraphic = bgImg;
            f.transition = Selectable.Transition.None;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            f.fontAsset = Font;
            f.pointSize = Etp2Spec.FsBody;
            return f;
        }

        public static Image Divider(Etp2Ctx ctx, Transform parent)
        {
            var d = Img(ctx, parent, "btn_Divider", Role.Btn);
            LE(d, -1, Etp2Spec.DividerH);
            return d;
        }

        public static Image Circle(Etp2Ctx ctx, Transform parent, string name)
        {
            var c = Img(ctx, parent, "btn_" + name, Role.Btn);
            c.sprite = SpriteOf("circle");
            LE(c, Etp2Spec.CircleIcon, Etp2Spec.CircleIcon, 0, -1, Etp2Spec.CircleIcon);
            return c;
        }

        public static RectTransform Spacer(Transform parent, float h)
        {
            var s = NewRect(parent, "Space" + (int)h);
            LE(s, -1, h);
            return s;
        }

        // 1.4 スクロールバー: 幅 8、溝は背景、つまみはボタン. 中身は ScrollRect.
        // バーの幅は最初から取っておき、出ても中身がずれない. 戻り値は中身(縦に並ぶ)
        public static RectTransform ScrollArea(Etp2Ctx ctx, RectTransform area, out ScrollRect scroll, bool permanentBar = true)
        {
            scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24;
            scroll.inertia = true;

            var viewport = NewRect(area, "Viewport");
            Stretch(viewport, 0, 0, Etp2Spec.ScrollbarW + Etp2Spec.Gap, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            // 中身のすき間でもドラッグできるよう、透明の当たり判定を置く
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            var content = NewRect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            FitVertical(content);

            var track = Img(ctx, area, "bg_ScrollTrack", Role.Bg, true);
            var tr = track.rectTransform;
            tr.anchorMin = new Vector2(1, 0);
            tr.anchorMax = new Vector2(1, 1);
            tr.pivot = new Vector2(1, 0.5f);
            tr.sizeDelta = new Vector2(Etp2Spec.ScrollbarW, 0);
            tr.anchoredPosition = Vector2.zero;
            var slide = NewRect(track.transform, "Sliding Area");
            Stretch(slide);
            var handle = Img(ctx, slide, "btn_Handle", Role.None, true);
            Stretch(handle.rectTransform);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.transition = Selectable.Transition.ColorTint;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };
            ctx.scrollbars.Add(bar);

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = permanentBar ? ScrollRect.ScrollbarVisibility.Permanent : ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 0;
            return content;
        }

        // ---- U# の値を裏の UdonBehaviour へ写す ----
        public static void CopyAllProxies(GameObject root)
        {
            foreach (var b in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                UdonSharpEditorUtility.CopyProxyToUdon(b);
                EditorUtility.SetDirty(b);
            }
        }
    }
}
