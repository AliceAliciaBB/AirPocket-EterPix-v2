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
        const float Margin = 16f;       // 画面の端からの余白(上下左右とも共通)
        const float HeaderBtn = 44f;    // ヘッダーのボタンの一辺
        const float HeaderGap = 12f;    // ヘッダーのボタンの間隔
        const float HeaderHeight = Margin + HeaderBtn + Margin; // 76: ボタンの上下にも余白を取る
        const float FooterHeight = 56f; // 下端にNudeNyangのホームバーが重なるため高めに取る

        const string AppDescription =
            "ようこそ、EterPix-appへ。\nこのアプリは、VRChatの投稿をスワイプで見ることができるアプリです。\n詳しくは右上の[ i ]ボタンを押してご確認ください。";

        const string InfoText =
            "EterPixについて\n\n" +
            "EterPixは、VRChatで撮影した写真を共有できるSNSです。このアプリでは、EterPixに投稿された公開写真を、スワイプで閲覧できます。R18の投稿は表示されません。\n\n" +
            "<b>スワイプで選ぶ</b>\n写真を左右にドラッグして離すと、次の写真へ進みます。右へ離すと「気になる」、左へ離すと「気にならない」です。下のボタンを押しても同じ操作ができます。\n\n" +
            "<b>写真を拡大する</b>\n写真を押すと拡大します。もう一度押すと情報が消え、さらに押すと閉じます。\n\n" +
            "<b>気になる投稿の一覧</b>\n右上の☆で、気になる投稿の一覧を開きます。「WEB」「高画質」を押すと、それぞれのURLをコピーできます。気になる印は、このインスタンスを出ると消えます。\n\n" +
            "<b>ワールドへ行く</b>\n一覧の「ポータルを開く」を押すと、撮影されたワールドへのポータルが開きます。\n\n" +
            "<b>表示色を変える</b>\n右上の切替ボタンで、黒と白を切り替えられます。\n\n" +
            "<b>写真を投稿する</b>\n投稿はWebから行えます。下のURLを押すとコピーできます。ブラウザで開いてください。";
        // 本文のあとに、コピーできるURLの欄が入る(URL は NudeNyangEterPix.InfoUrl)
        const string InfoFooter = "最新情報は X @_alicilia をご確認ください";

        // 仮の文面(合意書でも「仮」)
        const string GroupText =
            "グループ\n\n" +
            "ここにグループの説明が入ります(文面は仮)。\n\n" +
            "EterPix は、VRChatで撮った写真を共有する人たちの集まりです。投稿された写真は、このアプリのほか、ワールドのモニターでも見られます。";

        class Tokens
        {
            public List<Graphic> bg = new List<Graphic>();
            public List<Graphic> panel = new List<Graphic>();
            public List<Graphic> text = new List<Graphic>();
            public List<Graphic> sub = new List<Graphic>();
            public List<Graphic> btn = new List<Graphic>(); // ボタン(モニターの --btn: 黒 #2a2a2a / 白 #e6e6e6)
        }

        static void BuildUi(NudeNyangEterPix app, RectTransform root, TMP_FontAsset font, Sprite logo)
        {
            // 前回の画面を作り直す(冪等にするため、パネルの子を空にしてから積む)
            for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

            var tk = new Tokens();
            var bg = root.GetComponent<Image>();
            tk.bg.Add(bg);

            // ---- ヘッダー ----
            // 左から タイトル・カウンタ(現在/全体. 左寄せ. 3桁ぶんの幅を確保). 右から i・グループ・歯車・★. 端から Margin(16)ずつ空ける
            var header = Box(root, "Header", tk.panel);
            Anchor(header, 0, 1, 1, 1, new Vector2(0, -HeaderHeight), Vector2.zero);
            var logoFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LogoFontPath);
            var titleLabel = Label(header, "Title", "EterPix-app", logoFont != null ? logoFont : font, 34, TextAlignmentOptions.MidlineLeft, tk.text);
            titleLabel.enableWordWrapping = false;
            float titleW = Mathf.Ceil(titleLabel.GetPreferredValues("EterPix-app").x) + 4f;
            titleLabel.rectTransform.anchorMin = titleLabel.rectTransform.anchorMax = new Vector2(0, 0.5f);
            titleLabel.rectTransform.pivot = new Vector2(0, 0.5f);
            titleLabel.rectTransform.sizeDelta = new Vector2(titleW, HeaderBtn);
            titleLabel.rectTransform.anchoredPosition = new Vector2(Margin, 0);
            app.title = titleLabel;

            // 現在/全体(スワイプ画面のときだけ出る). 左寄せ。「000/000」が入る幅を常に取っておき、数字が増えても隣の部品がずれない
            // 現在値・「/」・全体数をそれぞれ別の枠にし、現在値と全体数には3桁ぶんの幅を取る(現在値は右寄せ・全体数は左寄せ)ので、桁が変わっても「/」が動かない
            var counterGo = new GameObject("Counter", typeof(RectTransform)); var counterRt = (RectTransform)counterGo.transform;
            counterRt.SetParent(header, false);
            var cur = Label(counterRt, "Current", "", font, 28, TextAlignmentOptions.MidlineRight, tk.text);
            var slash = Label(counterRt, "Slash", "/", font, 28, TextAlignmentOptions.Midline, tk.text);
            var tot = Label(counterRt, "Total", "", font, 28, TextAlignmentOptions.MidlineLeft, tk.text);
            cur.enableWordWrapping = slash.enableWordWrapping = tot.enableWordWrapping = false;
            float digitsW = Mathf.Ceil(cur.GetPreferredValues("000").x) + 2f;
            float slashW = Mathf.Ceil(slash.GetPreferredValues("/").x) + 2f;
            float counterW = digitsW * 2f + slashW;
            counterRt.anchorMin = counterRt.anchorMax = new Vector2(0, 0.5f);
            counterRt.pivot = new Vector2(0, 0.5f);
            counterRt.sizeDelta = new Vector2(counterW, HeaderBtn);
            counterRt.anchoredPosition = new Vector2(Margin + titleW + Margin, 0);
            SetCounterPart(cur.rectTransform, 0f, digitsW);
            SetCounterPart(slash.rectTransform, digitsW, slashW);
            SetCounterPart(tot.rectTransform, digitsW + slashW, digitsW);
            counterGo.SetActive(false);
            app.counterRoot = counterGo;
            app.counterCurrent = cur;
            app.counterTotal = tot;
            Debug.Log("[EtpTrace] builder: header title=" + titleW + " counter=" + counterW + " buttons=" + (4 * (HeaderBtn + HeaderGap)));

            app.infoButtonLabel = HeaderButton(header, "Info Button", "i", font, 0, tk, app, "OnInfo");
            app.groupButtonLabel = HeaderButton(header, "Group Button", "", font, 1, tk, app, "OnGroup");
            // グループ・歯車のアイコンは画像(開いている間は×の文字に替わる). 色は文字色に従う(モニターの currentColor)
            app.groupIcon = HeaderIcon(header.Find("Group Button"), GroupIconPath, tk);
            app.settingsButtonLabel = HeaderButton(header, "Settings Button", "", font, 2, tk, app, "OnSettings");
            app.settingsIcon = HeaderIcon(header.Find("Settings Button"), SettingsIconPath, tk);
            // 気になる一覧(ロード完了後に出る). 画像アイコン. 一覧を開いている間は×
            app.heartLabel = HeaderButton(header, "Heart Button", "", font, 3, tk, app, "OnHeart");
            app.heartIcon = HeaderIcon(header.Find("Heart Button"), LikedListIconPath, tk);
            app.heartButton = header.Find("Heart Button").gameObject;
            app.heartButton.SetActive(false);

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

            BuildList(app, root, font, tk);

            app.infoScreen = BuildInfo(app, root, font, tk);
            app.groupScreen = TextScreen(root, "Group Screen", GroupText, font, tk);
            app.settingsScreen = BuildSettings(app, root, font, tk);

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
            app.btnGraphics = tk.btn.ToArray();
            app.textGraphics = tk.text.ToArray();
            app.subGraphics = tk.sub.ToArray();
        }

        static void BuildSwipe(NudeNyangEterPix app, RectTransform screen, TMP_FontAsset font, Tokens tk)
        {
            // カード(写真の枠)。16:9に近い大きさ
            var card = Box(screen, "Card", tk.panel);
            Anchor(card, 0.17f, 0, 0.83f, 1, new Vector2(0, 96), new Vector2(0, -12));
            // ドラッグで動く本体(Card の ScrollRect の content). 写真・情報帯などはこの下
            var body = new GameObject("Body", typeof(RectTransform)); var bodyRt = (RectTransform)body.transform;
            bodyRt.SetParent(card, false);
            Anchor(bodyRt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            var swipe = card.gameObject.AddComponent<ScrollRect>();
            swipe.viewport = card; swipe.content = bodyRt;
            swipe.horizontal = true; swipe.vertical = true; // 上下も引ける(軸は実行時にロックする) swipe.movementType = ScrollRect.MovementType.Elastic;
            swipe.elasticity = 0.1f; swipe.inertia = false; swipe.scrollSensitivity = 0f;
            // 指(レーザー)を離したときに確定する. ScrollRect と同じ Card 上の EventTrigger の EndDrag で U# を呼ぶ
            var trig = card.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var endEntry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.EndDrag };
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(endEntry.callback,
                UdonSharpEditorUtility.GetBackingUdonBehaviour(app).SendCustomEvent, "OnSwipeRelease");
            trig.triggers.Add(endEntry);
            var photoGo = new GameObject("Photo", typeof(RectTransform), typeof(RawImage));
            var photoRt = (RectTransform)photoGo.transform; photoRt.SetParent(bodyRt, false);
            photoRt.anchorMin = photoRt.anchorMax = new Vector2(0.5f, 0.5f); photoRt.sizeDelta = new Vector2(160, 90);
            var raw = photoGo.GetComponent<RawImage>(); raw.raycastTarget = false;
            photoGo.SetActive(false);

            var notice = Label(bodyRt, "Notice", "読み込み中…", font, 30, TextAlignmentOptions.Center, tk.sub);
            Anchor(notice.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);

            // 写真を押すと拡大する(透明のボタン)
            var hit = new GameObject("Photo Hit", typeof(RectTransform), typeof(Image));
            var hitRt = (RectTransform)hit.transform; hitRt.SetParent(bodyRt, false);
            Anchor(hitRt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            var hitImg = hit.GetComponent<Image>(); hitImg.color = new Color(0, 0, 0, 0);
            var hitBtn = hit.AddComponent<Button>(); hitBtn.targetGraphic = hitImg; hitBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            Wire(hitBtn, app, "OnPhoto");

            // 情報帯(半透明の黒。写真の上に重なるので、テーマの色には従わない)
            var band = new GameObject("Info Band", typeof(RectTransform), typeof(Image));
            var bandRt = (RectTransform)band.transform; bandRt.SetParent(bodyRt, false);
            Anchor(bandRt, 0, 0, 1, 0, Vector2.zero, new Vector2(0, 76)); // 説明があるときは実行時に高くする
            var bandImg = band.GetComponent<Image>(); bandImg.color = new Color(0, 0, 0, 0.5f); bandImg.raycastTarget = false;
            // 上から 投稿者[称号]・説明(2行まで)・ワールド. 説明が無い投稿は帯を低くして詰める
            var user = Label(bandRt, "User", "", font, 26, TextAlignmentOptions.MidlineLeft, null);
            Anchor(user.rectTransform, 0, 1, 1, 1, new Vector2(16, -38), new Vector2(-16, -4));
            var des = Label(bandRt, "Description", "", font, 20, TextAlignmentOptions.TopLeft, null);
            des.color = new Color(1, 1, 1, 0.9f); des.overflowMode = TextOverflowModes.Ellipsis; des.maxVisibleLines = 2;
            Anchor(des.rectTransform, 0, 1, 1, 1, new Vector2(16, -96), new Vector2(-16, -38)); // 2行(20pt×1.45×2=58)が入る高さ
            des.gameObject.SetActive(false);
            var world = Label(bandRt, "World", "", font, 20, TextAlignmentOptions.MidlineLeft, null);
            world.color = new Color(1, 1, 1, 0.75f);
            Anchor(world.rectTransform, 0, 0, 1, 0, new Vector2(16, 4), new Vector2(-16, 34));
            app.swipeBandRect = bandRt; app.swipeDes = des;

            // 左(気にならない)・右(気になる)のボタン
            var row = new GameObject("Buttons", typeof(RectTransform)); var rowRt = (RectTransform)row.transform;
            rowRt.SetParent(screen, false);
            Anchor(rowRt, 0.17f, 0, 0.83f, 0, new Vector2(0, 8), new Vector2(0, 84));
            // [戻る][リロード][ポータル][☆/★][進む](入れ替え時は左右の文字と動作だけ替わる. 実行時に文字を付ける)
            var left = NavButton(rowRt, "Back Button", "戻る", font, tk, app, "OnNavLeft", 0);
            var reload = NavButton(rowRt, "Reload Button", "", font, tk, app, "OnReload", 1);
            var reloadIcon = (RectTransform)HeaderIcon(reload, ReloadIconPath, tk).transform; // モニターと同じ再読み込みアイコン(文字色に従う)
            reloadIcon.offsetMin = new Vector2(-20, -20); reloadIcon.offsetMax = new Vector2(20, 20);
            var portalBtn = NavButton(rowRt, "Portal Button", "◎", font, tk, app, "OnSwipePortal", 2);
            var star = NavButton(rowRt, "Star Button", "", font, tk, app, "OnStarToggle", 3);
            var right = NavButton(rowRt, "Next Button", "進む", font, tk, app, "OnNavRight", 4);
            app.navLeftLabel = left.GetComponentInChildren<TMP_Text>();
            app.navRightLabel = right.GetComponentInChildren<TMP_Text>();
            app.navPortalLabel = portalBtn.GetComponentInChildren<TMP_Text>();
            app.navLeftGroup = left.GetComponent<CanvasGroup>();
            app.navRightGroup = right.GetComponent<CanvasGroup>();
            app.navReloadGroup = reload.GetComponent<CanvasGroup>();
            app.navPortalGroup = portalBtn.GetComponent<CanvasGroup>();
            app.navStarGroup = star.GetComponent<CanvasGroup>();
            // ☆(輪郭)と ★(塗り). 気になる状態で切り替える
            app.starOutline = StarIcon(star, StarIconPath, "Star Outline", tk, true);
            app.starFilled = StarIcon(star, StarFilledIconPath, "Star Filled", tk, false);
            // ドラッグ中に方向のボタンを明るくする重ね(透明から始まり、ドラッグ量で濃くなる)
            app.leftHot = BuildHot(left);
            app.rightHot = BuildHot(right);
            app.starHot = BuildHot(star);
            // 下スワイプでワールド情報が無いときの一瞬の表示(カードの下端に重ねる. 動かない)
            var nw = new GameObject("No World Notice", typeof(RectTransform), typeof(Image));
            var nwRt = (RectTransform)nw.transform; nwRt.SetParent(screen, false);
            Anchor(nwRt, 0.5f, 0, 0.5f, 0, new Vector2(-130, 110), new Vector2(130, 154));
            nw.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f); nw.GetComponent<Image>().raycastTarget = false;
            var nwLabel = Label(nwRt, "Label", "ワールド情報なし", font, 24, TextAlignmentOptions.Center, null);
            nwLabel.color = Color.white;
            Anchor(nwLabel.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            nw.SetActive(false);
            app.noWorldNotice = nw;
            app.swipeScroll = swipe;
            app.swipeBody = bodyRt;

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

        // 気になる投稿一覧: ScrollRect + 使い回す行(RowPool 行)
        static void BuildList(NudeNyangEterPix app, RectTransform root, TMP_FontAsset font, Tokens tk)
        {
            var screen = Screen(root, "List Screen");

            // RectMask2D は、NudeNyang が縦向きのときに回す viewport の中では範囲が負になり、すべて消える.
            // NudeNyang 本体(NudeNyangScrollBuilder)と同じく、ステンシルの Mask(マスク自体は見せない)で切る
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewportRt = (RectTransform)viewport.transform; viewportRt.SetParent(screen, false);
            var vpImg = viewport.GetComponent<Image>(); vpImg.color = Color.white; vpImg.raycastTarget = true; // 行の無い所でも触れてスクロールできる(面の入力. Builder が付ける)
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            // モックの .lmain: 左右の余白 19(16×1.2)、見出しは上から 10 の位置に高さ 43・文字 29、行は上から 72 から
            Anchor(viewportRt, 0, 0, 1, 1, new Vector2(19, 0), new Vector2(-19, -72));
            // 見出し「気になる投稿 N 件」(スクロールしない固定の見出し)
            var heading = Label(screen, "Heading", "気になる投稿 0 件", font, 29, TextAlignmentOptions.MidlineLeft, tk.text);
            Anchor(heading.rectTransform, 0, 1, 1, 1, new Vector2(19, -53), new Vector2(-19, -10));
            app.listHeading = heading;
            var content = new GameObject("Content", typeof(RectTransform));
            var contentRt = (RectTransform)content.transform; contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1); contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.offsetMin = new Vector2(0, 0); contentRt.offsetMax = new Vector2(0, 0); contentRt.sizeDelta = new Vector2(0, 540);
            var scroll = screen.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRt; scroll.content = contentRt;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f; scroll.inertia = true;
            // ScrollRect が全面のドラッグを受けられるよう、透明の面を敷く
            var catcher = screen.gameObject.AddComponent<Image>(); catcher.color = new Color(0, 0, 0, 0);

            int pool = NudeNyangEterPix.RowPool;
            app.rowRoots = new GameObject[pool]; app.rowThumbs = new RawImage[pool];
            app.rowNames = new TMP_Text[pool]; app.rowWorlds = new TMP_Text[pool]; app.rowDes = new TMP_Text[pool];
            app.rowWebFields = new InputField[pool]; app.rowHqFields = new InputField[pool];
            app.rowPortalButtons = new GameObject[pool]; app.rowNoWorld = new TMP_Text[pool]; app.rowPortalLabels = new TMP_Text[pool];
            for (int k = 0; k < pool; k++) BuildRow(app, contentRt, k, font, tk);

            var empty = Label(screen, "Empty", "気になる投稿はありません", font, 30, TextAlignmentOptions.Center, tk.sub);
            Anchor(empty.rectTransform, 0, 0.3f, 1, 0.7f, Vector2.zero, Vector2.zero);
            empty.gameObject.SetActive(false);

            screen.gameObject.SetActive(false);
            app.listScreen = screen.gameObject;
            app.listContent = contentRt;
            app.listViewport = viewportRt;
            app.listEmpty = empty;
        }

        // 1行 = モックの .li と同じ(モックの px を 1.2 倍して整数に丸めた値).
        // 画像(192角)は左、右の列は上から 名前・ワールド・説明(2行まで)、下端にボタンを右寄せで1行(WEB | 高画質 | ポータルを開く).
        // 行の背景は無し(モックは透明). 向きによる違いは右の列の幅だけで、位置はすべてアンカーで決まる
        const float RowThumb = 192f;   // モック 160
        const float RowTx = 211f;      // 画像 192 + 間隔 19(モック 16)
        const float BtnH = 43f;        // モック 36
        const float BtnGap = 10f;      // モック 8
        const float WebW = 81f, HqW = 96f, PortalW = 173f; // モック 67.4 / 80 / 144
        const float NameFs = 19f, SubFs = 16f, BtnFs = 19f; // モック 16 / 13 / 16
        static void BuildRow(NudeNyangEterPix app, RectTransform content, int k, TMP_FontAsset font, Tokens tk)
        {
            var row = Box(content, "Row " + k, null);
            row.GetComponent<Image>().color = Color.clear;
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1);
            row.sizeDelta = new Vector2(0, RowThumb); row.anchoredPosition = new Vector2(0, -k * NudeNyangEterPix.RowHeight);

            // 写真(正方形に切り取る). 押すと拡大
            var thumbBox = new GameObject("Thumb Mask", typeof(RectTransform), typeof(Image), typeof(Mask));
            var thumbBoxRt = (RectTransform)thumbBox.transform; thumbBoxRt.SetParent(row, false);
            var tbImg = thumbBox.GetComponent<Image>(); tbImg.color = Color.white; tbImg.raycastTarget = false;
            thumbBox.GetComponent<Mask>().showMaskGraphic = false;
            thumbBoxRt.anchorMin = thumbBoxRt.anchorMax = new Vector2(0, 1); thumbBoxRt.pivot = new Vector2(0, 1);
            thumbBoxRt.sizeDelta = new Vector2(RowThumb, RowThumb); thumbBoxRt.anchoredPosition = Vector2.zero;
            var thumb = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage));
            var thumbRt = (RectTransform)thumb.transform; thumbRt.SetParent(thumbBoxRt, false);
            thumbRt.anchorMin = thumbRt.anchorMax = new Vector2(0.5f, 0.5f); thumbRt.sizeDelta = thumbBoxRt.sizeDelta;
            var raw = thumb.GetComponent<RawImage>(); raw.raycastTarget = false;
            var hit = new GameObject("Thumb Hit", typeof(RectTransform), typeof(Image));
            var hitRt = (RectTransform)hit.transform; hitRt.SetParent(thumbBoxRt, false);
            Anchor(hitRt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            var hitImg = hit.GetComponent<Image>(); hitImg.color = new Color(0, 0, 0, 0);
            var hitBtn = hit.AddComponent<Button>(); hitBtn.targetGraphic = hitImg; hitBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            Wire(hitBtn, app, "OnRowPhoto" + k);

            // 右の列: 名前[称号](y 0, 高さ 29)・ワールド(y 34, 高さ 24)・説明(y 63, 2行 48). 高さはフォントの1行(約 1.45 倍)が収まる値(足りないと Ellipsis で0文字になる)
            var name = Label(row, "Name", "", font, NameFs, TextAlignmentOptions.MidlineLeft, tk.text);
            name.overflowMode = TextOverflowModes.Ellipsis; name.enableWordWrapping = false;
            Anchor(name.rectTransform, 0, 1, 1, 1, new Vector2(RowTx, -29), new Vector2(0, 0));
            var world = Label(row, "World", "", font, SubFs, TextAlignmentOptions.MidlineLeft, tk.sub);
            world.overflowMode = TextOverflowModes.Ellipsis; world.enableWordWrapping = false;
            Anchor(world.rectTransform, 0, 1, 1, 1, new Vector2(RowTx, -58), new Vector2(0, -34));
            var des = Label(row, "Description", "", font, SubFs, TextAlignmentOptions.TopLeft, tk.sub);
            des.overflowMode = TextOverflowModes.Ellipsis; des.maxVisibleLines = 2;
            Anchor(des.rectTransform, 0, 1, 1, 1, new Vector2(RowTx, -111), new Vector2(0, -63));
            app.rowDes[k] = des;
            // モックの一覧は、ワールドが無いとその行を出さない(「ワールド情報なし」は出さない). 欄だけ残して常に隠す
            var noWorld = Label(row, "No World", "", font, SubFs, TextAlignmentOptions.MidlineLeft, tk.sub);
            Anchor(noWorld.rectTransform, 0, 1, 1, 1, new Vector2(RowTx, -57), new Vector2(0, -34));
            noWorld.gameObject.SetActive(false);

            // ボタン: 行の下端に右寄せで1行. 右から ポータルを開く | 高画質 | WEB
            var portal = Box(row, "Portal Button", null);
            portal.GetComponent<Image>().color = Accent;
            portal.anchorMin = portal.anchorMax = new Vector2(1, 0); portal.pivot = new Vector2(1, 0);
            portal.sizeDelta = new Vector2(PortalW, BtnH); portal.anchoredPosition = Vector2.zero;
            var pBtn = portal.gameObject.AddComponent<Button>(); pBtn.targetGraphic = portal.GetComponent<Image>(); pBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            var pLabel = Label(portal, "Label", "ポータルを開く", font, BtnFs, TextAlignmentOptions.Center, null);
            pLabel.color = Dark; pLabel.enableWordWrapping = false;
            Anchor(pLabel.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            Wire(pBtn, app, "OnRowPortal" + k);
            app.rowPortalLabels[k] = pLabel;
            portal.gameObject.SetActive(false);

            var hq = CopyButton(app, row, "HQ Button", "高画質", font, tk, -(PortalW + BtnGap), HqW, BtnH, BtnFs, 0f);
            var web = CopyButton(app, row, "Web Button", "WEB", font, tk, -(PortalW + BtnGap + HqW + BtnGap), WebW, BtnH, BtnFs, 0f);

            app.rowRoots[k] = row.gameObject; app.rowThumbs[k] = raw;
            app.rowNames[k] = name; app.rowWorlds[k] = world; app.rowNoWorld[k] = noWorld;
            app.rowWebFields[k] = web; app.rowHqFields[k] = hq; app.rowPortalButtons[k] = portal.gameObject;
        }

        // コピー用ボタン: 見た目はラベル、中身は文字を隠した InputField(押すとVRChatの入力UIが開き、そこでコピーできる).
        // 編集されても、入力を終えたら元の値へ戻す(NudeNyangの copyField と同じ方式)
        static InputField CopyButton(NudeNyangEterPix app, RectTransform row, string name, string label, TMP_FontAsset font, Tokens tk, float x, float w, float h, float fs, float y)
        {
            var b = Box(row, name, tk.btn);
            b.anchorMin = b.anchorMax = new Vector2(1, 0); b.pivot = new Vector2(1, 0);
            b.sizeDelta = new Vector2(w, h); b.anchoredPosition = new Vector2(x, y);
            var img = b.GetComponent<Image>();
            var field = b.gameObject.AddComponent<InputField>();
            var valueGo = new GameObject("Value", typeof(RectTransform), typeof(Text), typeof(CanvasGroup));
            var valueRt = (RectTransform)valueGo.transform; valueRt.SetParent(b, false);
            Anchor(valueRt, 0, 0, 1, 1, new Vector2(8, 4), new Vector2(-8, -4));
            var valueText = valueGo.GetComponent<Text>();
            valueText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); valueText.fontSize = 18; valueText.raycastTarget = false;
            var cg = valueGo.GetComponent<CanvasGroup>(); cg.alpha = 0; cg.blocksRaycasts = false; cg.interactable = false;
            field.targetGraphic = img; field.textComponent = valueText;
            field.lineType = InputField.LineType.SingleLine; field.contentType = InputField.ContentType.Standard;
            field.customCaretColor = true; field.caretColor = Color.clear; field.selectionColor = Color.clear;
            field.transition = Selectable.Transition.None; field.navigation = new Navigation { mode = Navigation.Mode.None };
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(field.onEndEdit,
                UdonSharpEditorUtility.GetBackingUdonBehaviour(app).SendCustomEvent, "OnCopyEnd");
            var l = Label(b, "Label", label, font, fs, TextAlignmentOptions.Center, tk.text);
            l.enableWordWrapping = false;
            Anchor(l.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            return field;
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
            Anchor(bandRt, 0, 0, 1, 0, Vector2.zero, new Vector2(0, 84)); // 説明があるときは実行時に高くする
            var bandImg = band.GetComponent<Image>(); bandImg.color = new Color(0, 0, 0, 0.5f); bandImg.raycastTarget = false;
            var user = Label(bandRt, "User", "", font, 28, TextAlignmentOptions.MidlineLeft, null);
            Anchor(user.rectTransform, 0, 1, 1, 1, new Vector2(28, -40), new Vector2(-28, -4));
            var des = Label(bandRt, "Description", "", font, 22, TextAlignmentOptions.TopLeft, null);
            des.color = new Color(1, 1, 1, 0.9f); des.overflowMode = TextOverflowModes.Ellipsis; des.maxVisibleLines = 2;
            Anchor(des.rectTransform, 0, 1, 1, 1, new Vector2(28, -104), new Vector2(-28, -40)); // 2行(22pt×1.45×2=64)が入る高さ
            des.gameObject.SetActive(false);
            var world = Label(bandRt, "World", "", font, 22, TextAlignmentOptions.MidlineLeft, null);
            world.color = new Color(1, 1, 1, 0.75f);
            Anchor(world.rectTransform, 0, 0, 1, 0, new Vector2(28, 4), new Vector2(-28, 38));
            app.zoomDes = des;

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

        static void SetCounterPart(RectTransform rt, float x, float w)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(w, HeaderBtn);
            rt.anchoredPosition = new Vector2(x, 0);
        }

        // ボタン列の1つ(5等分の i 番目. 薄くして押せなくするための CanvasGroup 付き)
        static RectTransform NavButton(Transform parent, string name, string text, TMP_FontAsset font, Tokens tk, NudeNyangEterPix app, string eventName, int i)
        {
            var b = TextButton(parent, name, text, font, tk, app, eventName, false);
            Anchor(b, i * 0.2f, 0, (i + 1) * 0.2f, 1, new Vector2(4, 0), new Vector2(-4, 0));
            b.gameObject.AddComponent<CanvasGroup>();
            return b;
        }

        // ☆/★ の画像(文字色。塗りは accent). 片方だけ表示する
        static GameObject StarIcon(RectTransform button, string path, string name, Tokens tk, bool active)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(button, false);
            Anchor(rt, 0.5f, 0.5f, 0.5f, 0.5f, new Vector2(-20, -20), new Vector2(20, 20));
            var img = go.GetComponent<Image>();
            img.sprite = EnsureSprite(path); img.preserveAspect = true; img.raycastTarget = false;
            if (active) { img.color = Color.white; tk.text.Add(img); } else img.color = Accent;
            go.SetActive(active);
            return go;
        }

        static Image BuildHot(RectTransform button)
        {
            var go = new GameObject("Hot", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(button, false);
            Anchor(rt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            rt.SetAsFirstSibling();
            var img = go.GetComponent<Image>(); img.color = new Color(Accent.r, Accent.g, Accent.b, 0); img.raycastTarget = false; // ドラッグ中だけ accent に色づく
            return img;
        }

        // 文字だけの長方形ボタン. accent=true なら強調色
        static RectTransform TextButton(Transform parent, string name, string text, TMP_FontAsset font, Tokens tk, NudeNyangEterPix app, string eventName, bool accent)
        {
            var b = Box(parent, name, accent ? null : tk.btn);
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
            // ロゴ: モニターと同じロゴ用フォントの文字
            var logoFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LogoFontPath);
            var logoText = Label(screen, "Logo", "EterPix", logoFont != null ? logoFont : font, 88, TextAlignmentOptions.Center, null);
            logoText.color = Accent;
            Anchor(logoText.rectTransform, 0.1f, 1, 0.9f, 1, new Vector2(0, -200), new Vector2(0, -60));

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

        // 情報ページ: 縦にスクロールできる面に、本文・コピーできるURL欄・最新情報の順で並べる
        static GameObject BuildInfo(NudeNyangEterPix app, RectTransform root, TMP_FontAsset font, Tokens tk)
        {
            var screen = Screen(root, "Info Screen");
            // 縦向きの viewport 回転でも消えないよう、一覧と同じステンシルの Mask で切る
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewportRt = (RectTransform)viewport.transform; viewportRt.SetParent(screen, false);
            var vpImg = viewport.GetComponent<Image>(); vpImg.color = Color.white; vpImg.raycastTarget = true;
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            Anchor(viewportRt, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRt = (RectTransform)content.transform; contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1); contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.offsetMin = Vector2.zero; contentRt.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(48, 48, 24, 24); vlg.spacing = 20;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = screen.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRt; scroll.content = contentRt;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f; scroll.inertia = true;
            var catcher = screen.gameObject.AddComponent<Image>(); catcher.color = new Color(0, 0, 0, 0);

            var body = Label(contentRt, "Body", InfoText, font, 26, TextAlignmentOptions.TopLeft, tk.text);
            // コピーできるURL欄(一覧の WEB / 高画質 と同じ方式)
            var field = CopyButton(app, contentRt, "Url Button", NudeNyangEterPix.InfoUrl, font, tk, 0, 520, 52, 24, 10);
            var fieldRt = (RectTransform)field.transform;
            fieldRt.anchorMin = new Vector2(0, 1); fieldRt.anchorMax = new Vector2(0, 1); fieldRt.pivot = new Vector2(0, 1);
            var le = field.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 520; le.preferredHeight = 52;
            field.text = NudeNyangEterPix.InfoUrl;
            var footer = Label(contentRt, "Footer Text", InfoFooter, font, 26, TextAlignmentOptions.TopLeft, tk.text);
            app.infoUrlField = field;
            screen.gameObject.SetActive(false);
            return screen.gameObject;
        }

        // 設定画面: 見出し「設定」・行1「テーマ [黒|白]」・行2「進む・戻るを入れ替え [しない|する]」. 端から Margin(16)ずつ.
        // 選択中の側は accent 色. 色は実行時(RefreshSettings)に決める(テーマの塗り直しで上書きされないよう、トークンには入れない)
        const float SetRowH = 56f, SetBtnW = 120f, SetBtnH = 44f;
        static GameObject BuildSettings(NudeNyangEterPix app, RectTransform root, TMP_FontAsset font, Tokens tk)
        {
            var screen = Screen(root, "Settings Screen");
            var heading = Label(screen, "Heading", "設定", font, 29, TextAlignmentOptions.MidlineLeft, tk.text);
            Anchor(heading.rectTransform, 0, 1, 1, 1, new Vector2(Margin, -(Margin + 43f)), new Vector2(-Margin, -Margin));
            var images = new Image[4]; var labels = new TMP_Text[4]; var icons = new Image[4];
            BuildSettingRow(app, screen, 0, "テーマ", "", "OnThemeDark", "", "OnThemeLight", font, tk, images, labels, icons, 0, DarkModeIconPath, LightModeIconPath);
            BuildSettingRow(app, screen, 1, "進む・戻るを入れ替え", "しない", "OnSwapOff", "する", "OnSwapOn", font, tk, images, labels, icons, 2, null, null);
            app.settingImages = images; app.settingLabels = labels; app.settingIcons = icons;
            screen.gameObject.SetActive(false);
            return screen.gameObject;
        }

        static void BuildSettingRow(NudeNyangEterPix app, RectTransform screen, int index, string title, string leftText, string leftEvent,
            string rightText, string rightEvent, TMP_FontAsset font, Tokens tk, Image[] images, TMP_Text[] labels, Image[] icons, int slot, string leftIcon, string rightIcon)
        {
            float top = Margin + 43f + Margin + index * (SetRowH + Margin);
            var row = new GameObject("Row " + index, typeof(RectTransform)); var rowRt = (RectTransform)row.transform;
            rowRt.SetParent(screen, false);
            Anchor(rowRt, 0, 1, 1, 1, new Vector2(Margin, -(top + SetRowH)), new Vector2(-Margin, -top));
            var lab = Label(rowRt, "Title", title, font, 26, TextAlignmentOptions.MidlineLeft, tk.text);
            lab.enableWordWrapping = false;
            Anchor(lab.rectTransform, 0, 0, 1, 1, Vector2.zero, new Vector2(-(SetBtnW * 2 + HeaderGap), 0));
            SetSegment(app, rowRt, "Right Button", rightText, rightEvent, font, 0, images, labels, icons, slot + 1, rightIcon);
            SetSegment(app, rowRt, "Left Button", leftText, leftEvent, font, SetBtnW + HeaderGap, images, labels, icons, slot, leftIcon);
        }

        static void SetSegment(NudeNyangEterPix app, RectTransform row, string name, string text, string eventName, TMP_FontAsset font, float fromRight,
            Image[] images, TMP_Text[] labels, Image[] icons, int slot, string iconPath)
        {
            var b = Box(row, name, null);
            b.anchorMin = b.anchorMax = new Vector2(1, 0.5f); b.pivot = new Vector2(1, 0.5f);
            b.sizeDelta = new Vector2(SetBtnW, SetBtnH); b.anchoredPosition = new Vector2(-fromRight, 0);
            var img = b.GetComponent<Image>();
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var l = Label(b, "Label", text, font, 24, TextAlignmentOptions.Center, null);
            l.enableWordWrapping = false;
            Anchor(l.rectTransform, 0, 0, 1, 1, Vector2.zero, Vector2.zero);
            Wire(btn, app, eventName);
            images[slot] = img; labels[slot] = l;
            if (!string.IsNullOrEmpty(iconPath))
            {
                // 文字の代わりにアイコン(100px の画像を 32px で). 色は選択状態で実行時に決める
                var ig = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var irt = (RectTransform)ig.transform; irt.SetParent(b, false);
                Anchor(irt, 0.5f, 0.5f, 0.5f, 0.5f, new Vector2(-16, -16), new Vector2(16, 16));
                var iimg = ig.GetComponent<Image>();
                iimg.sprite = EnsureSprite(iconPath); iimg.preserveAspect = true; iimg.raycastTarget = false;
                icons[slot] = iimg;
            }
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

        // ヘッダーのボタンの中の画像アイコン(文字色に従う)
        static GameObject HeaderIcon(Transform button, string path, Tokens tk)
        {
            var sprite = EnsureSprite(path);
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(button, false);
            Anchor(rt, 0.5f, 0.5f, 0.5f, 0.5f, new Vector2(-14, -14), new Vector2(14, 14));
            var img = go.GetComponent<Image>();
            img.sprite = sprite; img.color = Color.white; img.preserveAspect = true; img.raycastTarget = false;
            tk.text.Add(img);
            return go;
        }

        static TMP_Text HeaderButton(RectTransform header, string name, string text, TMP_FontAsset font, int slot, Tokens tk, NudeNyangEterPix app, string eventName)
        {
            var b = Box(header, name, tk.btn);
            var img = b.GetComponent<Image>();
            // 右端から詰めて並べる(一辺 HeaderBtn・間隔 HeaderGap. 右端と上下は Margin)
            float right = -Margin - slot * (HeaderBtn + HeaderGap);
            Anchor(b, 1, 0.5f, 1, 0.5f, new Vector2(right - HeaderBtn, -HeaderBtn / 2f), new Vector2(right, HeaderBtn / 2f));
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var l = Label(b, "Label", text, font, 28, TextAlignmentOptions.Center, tk.text);
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
