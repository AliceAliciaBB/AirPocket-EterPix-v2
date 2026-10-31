using System.Collections.Generic;
using NudeNyang;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EterPixNudeNyang
{
    // アプリの画面(ヘッダー・起動画面・情報・グループ)を作る. 文面は合意書の初期案のとおり
    public static partial class EterPixAppBuilder
    {
        const float HeaderHeight = 64f;
        const float FooterHeight = 56f; // 下端にNudeNyangのホームバーが重なるため高めに取る

        const string AppDescription =
            "ようこそ、EterPix-appへ。\nこのアプリは、VRChatの投稿をスワイプで見ることができるアプリです。\n詳しくは右上の[ i ]ボタンを押してご確認ください。";

        const string InfoText =
            "EterPix-app について\n\n" +
            "EterPixで投稿された写真を、スワイプで見られるアプリです。\n" +
            "左スワイプ: 気にならない / 右スワイプ: 気になる\n" +
            "気になる投稿は、あとで一覧で確認できます。\n" +
            "気になる投稿は、このインスタンスを出ると消えます。\n\n" +
            "R18の投稿は表示されません。\n\n" +
            "最新情報は X @_alicilia";

        // 仮の文面(合意書でも「仮」)
        const string GroupText =
            "EterPix グループ\n\n" +
            "(ここにグループの説明が入ります。仮)";

        class Tokens
        {
            public List<Graphic> bg = new List<Graphic>();
            public List<Graphic> panel = new List<Graphic>();
            public List<Graphic> text = new List<Graphic>();
            public List<Graphic> sub = new List<Graphic>();
        }

        static void BuildUi(NudeNyangEterPix app, RectTransform root, TMP_FontAsset font, Sprite logo)
        {
            // 前回の画面を作り直す(冪等にするため、パネルの子を空にしてから積む)
            for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

            var tk = new Tokens();
            var bg = root.GetComponent<Image>();
            tk.bg.Add(bg);

            // ---- ヘッダー ----
            var header = Box(root, "Header", tk.panel);
            Anchor(header, 0, 1, 1, 1, new Vector2(0, -HeaderHeight), Vector2.zero);
            var titleLabel = Label(header, "Title", "EterPix-app", font, 34, TextAlignmentOptions.MidlineLeft, tk.text);
            Anchor(titleLabel.rectTransform, 0, 0, 1, 1, new Vector2(24, 0), new Vector2(-300, 0));
            app.title = titleLabel;

            // 現在/総数(スワイプ画面のときだけ出る)
            var counter = Label(header, "Counter", "", font, 28, TextAlignmentOptions.Center, tk.text);
            Anchor(counter.rectTransform, 0.35f, 0, 0.65f, 1, Vector2.zero, Vector2.zero);
            counter.gameObject.SetActive(false);
            app.counterLabel = counter;

            app.infoButtonLabel = HeaderButton(header, "Info Button", "i", font, 0, tk, app, "OnInfo");
            app.groupButtonLabel = HeaderButton(header, "Group Button", "G", font, 1, tk, app, "OnGroup");
            HeaderButton(header, "Theme Button", "T", font, 2, tk, app, "OnTheme");

            // ---- フッター(コピーライト) ----
            var footer = Box(root, "Footer", tk.panel);
            Anchor(footer, 0, 0, 1, 0, Vector2.zero, new Vector2(0, FooterHeight));
            var copy = Label(footer, "Copyright", "2026 EterPix All Rights Reserved.", font, 20, TextAlignmentOptions.Top, tk.sub);
            Anchor(copy.rectTransform, 0, 0, 1, 1, new Vector2(0, 0), new Vector2(0, -4));

            // ---- 画面(ヘッダーとフッターの間) ----
            app.startScreen = Screen(root, "Start Screen").gameObject;
            BuildStart(app, app.startScreen.GetComponent<RectTransform>(), font, logo, tk);

            app.swipeScreen = Screen(root, "Swipe Screen").gameObject;
            BuildSwipe(app, app.swipeScreen.GetComponent<RectTransform>(), font, tk);
            app.swipeScreen.SetActive(false);

            app.infoScreen = TextScreen(root, "Info Screen", InfoText, font, tk);
            app.groupScreen = TextScreen(root, "Group Screen", GroupText, font, tk);

            BuildZoom(app, root, font);
            BuildConfirm(app, root, font, tk);

            // 実行時の rect はスリープ中に0になることがあるため、ここでの大きさを焼く
            Canvas.ForceUpdateCanvases();
            var panelRect = root.rect.size;
            if (panelRect.x > 1f && panelRect.y > 1f) app.panelSize = panelRect;
            var cardRect = app.cardFrame.rect.size;
            if (cardRect.x > 1f && cardRect.y > 1f) app.cardSize = cardRect;

            app.bgGraphics = tk.bg.ToArray();
            app.panelGraphics = tk.panel.ToArray();
            app.textGraphics = tk.text.ToArray();
            app.subGraphics = tk.sub.ToArray();
        }

        static void BuildSwipe(NudeNyangEterPix app, RectTransform screen, TMP_FontAsset font, Tokens tk)
        {
            // カード(写真の枠)。16:9に近い大きさ
            var card = Box(screen, "Card", tk.panel);
            Anchor(card, 0.17f, 0, 0.83f, 1, new Vector2(0, 96), new Vector2(0, -12));
            var photoGo = new GameObject("Photo", typeof(RectTransform), typeof(RawImage));
            var photoRt = (RectTransform)photoGo.transform; photoRt.SetParent(card, false);
            photoRt.anchorMin = photoRt.anchorMax = new Vector2(0.5f, 0.5f); photoRt.sizeDelta = new Vector2(160, 90);
            var raw = photoGo.GetComponent<RawImage>(); raw.raycastTarget = false;
            photoGo.SetActive(false);

            var notice = Label(card, "Notice", "読み込み中...", font, 30, TextAlignmentOptions.Center, tk.sub);
            Anchor(notice.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);

            // 写真を押すと拡大する(透明のボタン)
            var hit = new GameObject("Photo Hit", typeof(RectTransform), typeof(Image));
            var hitRt = (RectTransform)hit.transform; hitRt.SetParent(card, false);
            Anchor(hitRt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            var hitImg = hit.GetComponent<Image>(); hitImg.color = new Color(0, 0, 0, 0);
            var hitBtn = hit.AddComponent<Button>(); hitBtn.targetGraphic = hitImg; hitBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            Wire(hitBtn, app, "OnPhoto");

            // 情報帯(半透明の黒。写真の上に重なるので、テーマの色には従わない)
            var band = new GameObject("Info Band", typeof(RectTransform), typeof(Image));
            var bandRt = (RectTransform)band.transform; bandRt.SetParent(card, false);
            Anchor(bandRt, 0, 0, 1, 0, Vector2.zero, new Vector2(0, 76));
            var bandImg = band.GetComponent<Image>(); bandImg.color = new Color(0, 0, 0, 0.5f); bandImg.raycastTarget = false;
            var user = Label(bandRt, "User", "", font, 26, TextAlignmentOptions.MidlineLeft, null);
            Anchor(user.rectTransform, 0, 0.45f, 1, 1, new Vector2(16, 0), new Vector2(-16, 0));
            var world = Label(bandRt, "World", "", font, 20, TextAlignmentOptions.MidlineLeft, null);
            world.color = new Color(1, 1, 1, 0.75f);
            Anchor(world.rectTransform, 0, 0, 1, 0.45f, new Vector2(16, 0), new Vector2(-16, 0));

            // 左(気にならない)・右(気になる)のボタン
            var row = new GameObject("Buttons", typeof(RectTransform)); var rowRt = (RectTransform)row.transform;
            rowRt.SetParent(screen, false);
            Anchor(rowRt, 0.17f, 0, 0.83f, 0, new Vector2(0, 8), new Vector2(0, 84));
            var pass = TextButton(rowRt, "Pass Button", "気にならない", font, tk, app, "OnPass", false);
            Anchor(pass, 0, 0, 0.48f, 1, Vector2.zero, Vector2.zero);
            var like = TextButton(rowRt, "Like Button", "気になる", font, tk, app, "OnLike", true);
            Anchor(like, 0.52f, 0, 1, 1, Vector2.zero, Vector2.zero);

            // すべて見終わったあとの画面
            var end = new GameObject("End", typeof(RectTransform)); var endRt = (RectTransform)end.transform;
            endRt.SetParent(screen, false);
            Anchor(endRt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            var summary = Label(endRt, "Summary", "", font, 34, TextAlignmentOptions.Center, tk.text);
            Anchor(summary.rectTransform, 0.1f, 0.5f, 0.9f, 0.9f, Vector2.zero, Vector2.zero);
            var again = TextButton(endRt, "Again Button", "もう一度見る", font, tk, app, "OnRestartAsk", false);
            Anchor(again, 0.32f, 0.18f, 0.68f, 0.34f, Vector2.zero, Vector2.zero);
            end.SetActive(false);

            app.cardFrame = card;
            app.photoRect = photoRt;
            app.photo = raw;
            app.userLabel = user;
            app.worldLabel = world;
            app.cardNotice = notice;
            app.buttonRow = row;
            app.endScreen = end;
            app.endSummary = summary;
        }

        // 拡大(ヘッダー・フッターも覆う全面。黒地は写真用なのでテーマに従わない)
        static void BuildZoom(NudeNyangEterPix app, RectTransform root, TMP_FontAsset font)
        {
            var zoom = new GameObject("Zoom", typeof(RectTransform), typeof(Image));
            var zoomRt = (RectTransform)zoom.transform; zoomRt.SetParent(root, false);
            Anchor(zoomRt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            zoom.GetComponent<Image>().color = Color.black;
            var photoGo = new GameObject("Photo", typeof(RectTransform), typeof(RawImage));
            var photoRt = (RectTransform)photoGo.transform; photoRt.SetParent(zoomRt, false);
            photoRt.anchorMin = photoRt.anchorMax = new Vector2(0.5f, 0.5f); photoRt.sizeDelta = new Vector2(160, 90);
            var raw = photoGo.GetComponent<RawImage>(); raw.raycastTarget = false;

            var band = new GameObject("Info Band", typeof(RectTransform), typeof(Image));
            var bandRt = (RectTransform)band.transform; bandRt.SetParent(zoomRt, false);
            Anchor(bandRt, 0, 0, 1, 0, Vector2.zero, new Vector2(0, 84));
            var bandImg = band.GetComponent<Image>(); bandImg.color = new Color(0, 0, 0, 0.5f); bandImg.raycastTarget = false;
            var user = Label(bandRt, "User", "", font, 28, TextAlignmentOptions.MidlineLeft, null);
            Anchor(user.rectTransform, 0, 0.45f, 1, 1, new Vector2(28, 0), new Vector2(-28, 0));
            var world = Label(bandRt, "World", "", font, 22, TextAlignmentOptions.MidlineLeft, null);
            world.color = new Color(1, 1, 1, 0.75f);
            Anchor(world.rectTransform, 0, 0, 1, 0.45f, new Vector2(28, 0), new Vector2(-28, 0));

            // 全面のタップで 情報帯あり → UIなし → 閉じる
            var tap = zoom.AddComponent<Button>(); tap.targetGraphic = zoom.GetComponent<Image>(); tap.navigation = new Navigation { mode = Navigation.Mode.None };
            Wire(tap, app, "OnZoomTap");

            zoom.SetActive(false);
            app.zoomRoot = zoom;
            app.zoomPhotoRect = photoRt;
            app.zoomPhoto = raw;
            app.zoomBand = band;
            app.zoomUser = user;
            app.zoomWorld = world;
        }

        // 「もう一度見る」の確認(全面を覆う)
        static void BuildConfirm(NudeNyangEterPix app, RectTransform root, TMP_FontAsset font, Tokens tk)
        {
            var dim = new GameObject("Confirm", typeof(RectTransform), typeof(Image));
            var dimRt = (RectTransform)dim.transform; dimRt.SetParent(root, false);
            Anchor(dimRt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            dim.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            var box = Box(dimRt, "Box", tk.bg); // ボタン(panel色)が沈まないよう背景色にする
            Anchor(box, 0.25f, 0.3f, 0.75f, 0.7f, Vector2.zero, Vector2.zero);
            var msg = Label(box, "Message", "気になる選択はなくなります。\nいいですか?", font, 30, TextAlignmentOptions.Center, tk.text);
            Anchor(msg.rectTransform, 0, 0.4f, 1, 1, new Vector2(16, 0), new Vector2(-16, -16));
            var yes = TextButton(box, "Yes Button", "はい", font, tk, app, "OnRestartYes", true);
            Anchor(yes, 0.06f, 0.08f, 0.48f, 0.36f, Vector2.zero, Vector2.zero);
            var no = TextButton(box, "No Button", "やめる", font, tk, app, "OnRestartNo", false);
            Anchor(no, 0.52f, 0.08f, 0.94f, 0.36f, Vector2.zero, Vector2.zero);
            dim.SetActive(false);
            app.confirmModal = dim;
        }

        // 文字だけの長方形ボタン. accent=true なら強調色
        static RectTransform TextButton(Transform parent, string name, string text, TMP_FontAsset font, Tokens tk, NudeNyangEterPix app, string eventName, bool accent)
        {
            var b = Box(parent, name, accent ? null : tk.panel);
            var img = b.GetComponent<Image>();
            if (accent) img.color = Accent;
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var l = Label(b, "Label", text, font, 28, TextAlignmentOptions.Center, accent ? null : tk.text);
            if (accent) l.color = Dark;
            Anchor(l.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            Wire(btn, app, eventName);
            return b;
        }

        static void BuildStart(NudeNyangEterPix app, RectTransform screen, TMP_FontAsset font, Sprite logo, Tokens tk)
        {
            var logoGo = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            var logoRt = (RectTransform)logoGo.transform; logoRt.SetParent(screen, false);
            Anchor(logoRt, 0.5f, 1, 0.5f, 1, new Vector2(-70, -190), new Vector2(70, -50));
            var li = logoGo.GetComponent<Image>(); li.sprite = logo; li.preserveAspect = true; li.color = Accent; li.raycastTarget = false;

            var des = Label(screen, "Description", AppDescription, font, 26, TextAlignmentOptions.Center, tk.text);
            Anchor(des.rectTransform, 0.05f, 0.34f, 0.95f, 0.72f, Vector2.zero, Vector2.zero);

            // ロード中の行(ロード中... 左 / 進行度 右 / プログレスバー)
            var row = new GameObject("Loading", typeof(RectTransform)); var rowRt = (RectTransform)row.transform;
            rowRt.SetParent(screen, false);
            Anchor(rowRt, 0.2f, 0.08f, 0.8f, 0.30f, Vector2.zero, Vector2.zero);
            var lab = Label(rowRt, "Label", "ロード中...", font, 24, TextAlignmentOptions.TopLeft, tk.text);
            Anchor(lab.rectTransform, 0, 0.5f, 0.5f, 1, Vector2.zero, Vector2.zero);
            var pct = Label(rowRt, "Percent", "0%", font, 24, TextAlignmentOptions.TopRight, tk.text);
            Anchor(pct.rectTransform, 0.5f, 0.5f, 1, 1, Vector2.zero, Vector2.zero);
            var track = Box(rowRt, "Bar Track", tk.panel);
            Anchor(track, 0, 0.18f, 1, 0.42f, Vector2.zero, Vector2.zero);
            var fill = Box(track, "Bar Fill", null);
            fill.GetComponent<Image>().color = Accent;
            Anchor(fill, 0, 0, 0, 1, Vector2.zero, Vector2.zero);
            fill.anchorMax = new Vector2(0, 1);

            // 開始ボタン(ロード完了後に出る)
            var start = Box(screen, "Start Button", null);
            Anchor(start, 0.35f, 0.10f, 0.65f, 0.26f, Vector2.zero, Vector2.zero);
            var sImg = start.GetComponent<Image>(); sImg.color = Accent;
            var sBtn = start.gameObject.AddComponent<Button>(); sBtn.targetGraphic = sImg; sBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            var sLabel = Label(start, "Label", "push to start", font, 30, TextAlignmentOptions.Center, null);
            sLabel.color = Dark;
            Anchor(sLabel.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            Wire(sBtn, app, "OnStart");
            start.gameObject.SetActive(false);

            // 取得に失敗したときの表示(ロード行の代わりに出る)
            var status = Label(screen, "Status", "", font, 26, TextAlignmentOptions.Center, tk.text);
            Anchor(status.rectTransform, 0.1f, 0.06f, 0.9f, 0.30f, Vector2.zero, Vector2.zero);
            status.gameObject.SetActive(false);

            app.loadingRow = row;
            app.loadingLabel = lab;
            app.percentLabel = pct;
            app.barFill = fill;
            app.startButton = start.gameObject;
            app.statusLabel = status;
        }

        static GameObject TextScreen(RectTransform root, string name, string body, TMP_FontAsset font, Tokens tk)
        {
            var screen = Screen(root, name);
            var t = Label(screen, "Body", body, font, 26, TextAlignmentOptions.TopLeft, tk.text);
            Anchor(t.rectTransform, 0, 0, 1, 1, new Vector2(48, 24), new Vector2(-48, -24));
            screen.gameObject.SetActive(false);
            return screen.gameObject;
        }

        // ヘッダーとフッターの間の面
        static RectTransform Screen(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            Anchor(rt, 0, 0, 1, 1, new Vector2(0, FooterHeight), new Vector2(0, -HeaderHeight));
            return rt;
        }

        static TMP_Text HeaderButton(RectTransform header, string name, string text, TMP_FontAsset font, int slot, Tokens tk, NudeNyangEterPix app, string eventName)
        {
            var b = Box(header, name, null);
            var img = b.GetComponent<Image>(); img.color = Accent;
            // 右端から詰めて並べる(幅56・間隔12)
            float right = -24 - slot * 68;
            Anchor(b, 1, 0.5f, 1, 0.5f, new Vector2(right - 56, -22), new Vector2(right, 22));
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var l = Label(b, "Label", text, font, 28, TextAlignmentOptions.Center, null);
            l.color = Dark;
            Anchor(l.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            Wire(btn, app, eventName);
            return l;
        }

        // 押したら U# のイベントを呼ぶ(NudeNyangのタイルと同じ永続リスナー方式)
        static void Wire(Button btn, NudeNyangEterPix app, string eventName)
        {
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(btn.onClick,
                UdonSharpEditorUtility.GetBackingUdonBehaviour(app).SendCustomEvent, eventName);
        }

        static RectTransform Box(Transform parent, string name, List<Graphic> token)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.raycastTarget = true;
            if (token != null) token.Add(img);
            return rt;
        }

        static TMP_Text Label(Transform parent, string name, string text, TMP_FontAsset font, float size, TextAlignmentOptions align, List<Graphic> token)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = align; t.raycastTarget = false;
            t.enableWordWrapping = true; t.color = Color.white;
            if (font != null) t.font = font;
            if (token != null) token.Add(t);
            return t;
        }

        // アンカーとオフセットを一度に指定する
        static void Anchor(RectTransform rt, float minX, float minY, float maxX, float maxY, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = new Vector2(minX, minY); rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }
    }
}
