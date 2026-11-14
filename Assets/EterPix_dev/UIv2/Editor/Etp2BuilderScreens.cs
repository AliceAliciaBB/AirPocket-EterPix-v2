using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ali.eterpix.ui2.editor.Etp2Parts;

namespace ali.eterpix.ui2.editor
{
    public static partial class Etp2Builder
    {
        // ---- 4章 メニューモニター ----

        static void BuildMenuMonitor(GameObject root)
        {
            var menu = RootComponent<etp2_menu_monitor>(root);
            var ctx = new Etp2Ctx();
            var m = BuildMonitorSkeleton(root, ctx, "menu", Etp2Spec.MenuNav);
            menu.theme = m.theme;
            ctx.listeners.Add(menu);

            // 4.1 インフォメーション
            var info = m.contents[Etp2Spec.MenuPageInfo];
            Heading(ctx, info, Etp2Spec.InfoHeading);
            Body(ctx, info, Etp2Spec.InfoBody);
            UrlField(ctx, info, Etp2Spec.InfoUrl);
            Body(ctx, info, Etp2Spec.InfoFooter, "tx_Footer");

            // 4.2 リンク
            var link = m.contents[Etp2Spec.MenuPageLink];
            Heading(ctx, link, Etp2Spec.LinkHeading);
            Body(ctx, link, Etp2Spec.LinkBody);
            Body(ctx, link, Etp2Spec.LinkSiteLabel, "tx_SiteLabel");
            UrlField(ctx, link, Etp2Spec.LinkSiteUrl);
            Body(ctx, link, Etp2Spec.LinkBoothLabel, "tx_BoothLabel");
            UrlField(ctx, link, Etp2Spec.LinkBoothUrl);
            var group = TextButton(ctx, link, "OpenGroup", Etp2Spec.LinkGroupButton, BtnStyle.Accent, out _, Etp2Spec.LinkButtonH);
            OnClick(group, menu, nameof(etp2_menu_monitor.OpenGroupPage));

            // 4.3 ソート
            menu.sortPage = BuildSortPage(ctx, m.contents[Etp2Spec.MenuPageSort]);
            menu.sortPage.theme = m.theme;
            ctx.listeners.Add(menu.sortPage);

            // 4.4 ワールド一覧
            var worlds = m.contents[Etp2Spec.MenuPageWorlds];
            var wRows = new GameObject[Etp2Spec.WorldRowPool];
            var wNames = new TextMeshProUGUI[Etp2Spec.WorldRowPool];
            var wAuthors = new TextMeshProUGUI[Etp2Spec.WorldRowPool];
            for (int i = 0; i < Etp2Spec.WorldRowPool; i++)
            {
                var row = NewRect(worlds, "WorldRow" + i);
                HLG(row, Etp2Spec.Gap);
                LE(row, -1, Etp2Spec.ButtonH);
                // 名前の帯(押せない. 押しても色が変わらない)
                var band = Img(ctx, row, "btn_WorldBand", Role.Btn);
                LE(band, -1, Etp2Spec.ButtonH, 1);
                int p = (int)Etp2Spec.TextButtonPadX;
                var bandG = HLG(band, Etp2Spec.TextButtonPadX, TextAnchor.MiddleLeft, new RectOffset(p, p, 0, 0));
                bandG.childForceExpandHeight = true;
                var name = NewText(ctx, band.transform, "tx_Name", "", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
                LE(name, -1, -1, 1);
                var author = NewText(ctx, band.transform, "tx_Author", "", Etp2Spec.FsBody, Role.Tx, TextAlignmentOptions.MidlineRight, false);
                author.overflowMode = TextOverflowModes.Overflow;
                var portal = IconButton(ctx, row, "Portal", "portal", out _);
                Hover(ctx, portal, "ポータルを開く");
                OnClickIndex(portal, menu, nameof(etp2_menu_monitor.OnPortal), i);
                wRows[i] = row.gameObject; wNames[i] = name; wAuthors[i] = author;
                if (i < Etp2Spec.Worlds.GetLength(0)) { name.text = Etp2Spec.Worlds[i, 0]; author.text = Etp2Spec.Worlds[i, 1]; }
                else row.gameObject.SetActive(false);
            }
            menu.worldRows = wRows;
            menu.worldNames = wNames;
            menu.worldAuthors = wAuthors;

            // 4.5 設定
            var settings = m.contents[Etp2Spec.MenuPageSettings];
            Heading(ctx, settings, Etp2Spec.SettingsThemeHeading);
            Segment(ctx, settings, "ThemeSegment", Etp2Spec.SettingsThemes, menu, nameof(etp2_menu_monitor.OnTheme), out var tb, out var tl, out _);
            Body(ctx, settings, Etp2Spec.SettingsThemeBody);
            Spacer(settings, 0);   // 区切りの間 16
            Heading(ctx, settings, Etp2Spec.SettingsSwapHeading);
            Segment(ctx, settings, "SwapSegment", Etp2Spec.SettingsSwaps, menu, nameof(etp2_menu_monitor.OnSwap), out var sb, out var sl, out var si);
            Body(ctx, settings, Etp2Spec.SettingsSwapBody);
            menu.themeButtons = tb; menu.themeLabels = tl;
            menu.swapButtons = sb; menu.swapLabels = sl; menu.swapIcons = si;
            EditorPaintDynamic(tb, tl);
            EditorPaintDynamic(sb, sl, si);

            EndScreen(m.screen, ctx, m.theme);
        }

        // 4.3 ソート(モックは暫定)
        static etp2_sort_page BuildSortPage(Etp2Ctx ctx, RectTransform content)
        {
            var sort = Add<etp2_sort_page>(content);   // theme は呼び出し元が入れる

            // [初期設定に戻す] ... [適用]
            var actions = NewRect(content, "Actions");
            HLG(actions, Etp2Spec.Gap);
            LE(actions, -1, Etp2Spec.ButtonH);
            var reset = TextButton(ctx, actions, "Reset", Etp2Spec.SortReset, BtnStyle.Normal, out _);
            OnClick(reset, sort, nameof(etp2_sort_page.OnReset));
            LE(NewRect(actions, "Flex"), -1, -1, 1);
            var apply = TextButton(ctx, actions, "Apply", Etp2Spec.SortApply, BtnStyle.Dynamic, out var applyLabel);
            ctx.disTexts.Add(applyLabel);   // 最初は変更が無いので押せない
            OnClick(apply, sort, nameof(etp2_sort_page.OnApply));
            sort.applyButton = apply;
            sort.applyLabel = applyLabel;
            EditorPaintDynamic(new[] { apply });
            Spacer(content, 0);   // 下に 16

            // 並び順
            Heading(ctx, content, Etp2Spec.SortOrderHeading);
            Segment(ctx, content, "OrderSegment", Etp2Spec.SortOrders, sort, nameof(etp2_sort_page.OnOrder), out var ob, out var ol, out _);
            sort.orderButtons = ob;
            sort.orderLabels = ol;
            EditorPaintDynamic(ob, ol);
            Spacer(content, 0);

            // 上に表示する投稿
            Heading(ctx, content, Etp2Spec.SortPromoteHeading);
            Body(ctx, content, Etp2Spec.SortPromoteBody);

            int cats = Etp2Spec.SortCategories.Length;
            var arrows = new TextMeshProUGUI[cats];
            var listRoots = new GameObject[cats];
            var listRects = new RectTransform[cats];
            var emptyRows = new GameObject[cats];
            var candRows = new GameObject[cats * Etp2Spec.SortMaxCand];
            var candNames = new TextMeshProUGUI[cats * Etp2Spec.SortMaxCand];
            var candAdd = new Button[cats * Etp2Spec.SortMaxCand];
            var candAddLabels = new TextMeshProUGUI[cats * Etp2Spec.SortMaxCand];
            var selRows = new GameObject[cats * Etp2Spec.SortMaxSel];
            var selNames = new TextMeshProUGUI[cats * Etp2Spec.SortMaxSel];
            int pad = (int)Etp2Spec.TextButtonPadX;

            for (int c = 0; c < cats; c++)
            {
                Spacer(content, 0);   // 見出しの上は 16(モックの cond-label の margin-top 8)
                NewText(ctx, content, "tx_Category" + c, Etp2Spec.SortCategories[c], Etp2Spec.FsButton, Role.Tx);

                // ドロップダウン: [追加する ▲]. 一覧はボタンの上へ開き、ほかの要素の上に重ねる
                var dd = NewRect(content, "Dropdown" + c);
                LE(dd, -1, Etp2Spec.ButtonH);
                var ddButton = NewButton(ctx, dd, "DropdownButton", BtnStyle.Normal);
                Stretch((RectTransform)ddButton.transform);
                HLG(ddButton, Etp2Spec.Gap, TextAnchor.MiddleLeft, new RectOffset(pad, pad, 0, 0)).childForceExpandHeight = true;
                var value = NewText(ctx, ddButton.transform, "tx_Value", Etp2Spec.SortAddLabel, Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
                LE(value, -1, -1, 1);
                var arrow = NewText(ctx, ddButton.transform, "tx_Arrow", "▲", Etp2Spec.FsArrow, Role.Tx, TextAlignmentOptions.MidlineRight, false);
                arrow.overflowMode = TextOverflowModes.Overflow;
                OnClickIndex(ddButton, sort, nameof(etp2_sort_page.OnToggleDropdown), c);
                arrows[c] = arrow;

                var list = Img(ctx, dd, "btn_List", Role.Btn, true);
                var lr = list.rectTransform;
                lr.anchorMin = new Vector2(0, 1);
                lr.anchorMax = new Vector2(1, 1);
                lr.pivot = new Vector2(0.5f, 0);
                lr.anchoredPosition = Vector2.zero;
                lr.sizeDelta = new Vector2(0, Etp2Spec.DropdownRowH * Etp2Spec.DropdownMaxRows);
                var listContent = ScrollArea(ctx, lr, out var listScroll, false);
                // 一覧の中ではバーとの間を取らない(幅 8 のバーだけ)
                ((RectTransform)listScroll.viewport).offsetMax = new Vector2(-Etp2Spec.ScrollbarW, 0);
                VLG(listContent, 0);
                listRoots[c] = list.gameObject;
                listRects[c] = lr;

                var empty = NewRect(listContent, "Empty");
                LE(empty, -1, Etp2Spec.DropdownRowH);
                var emptyText = NewText(ctx, empty, "tx_Empty", Etp2Spec.SortEmpty, Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
                Stretch(emptyText.rectTransform, Etp2Spec.TextButtonPadX, 0, 0, 0);
                emptyRows[c] = empty.gameObject;

                for (int r = 0; r < Etp2Spec.SortMaxCand; r++)
                {
                    int k = c * Etp2Spec.SortMaxCand + r;
                    var row = NewRect(listContent, "Cand" + r);
                    HLG(row, Etp2Spec.Gap, TextAnchor.MiddleLeft, new RectOffset(pad, 0, 0, 0)).childForceExpandHeight = true;
                    LE(row, -1, Etp2Spec.DropdownRowH);
                    var nm = NewText(ctx, row, "tx_Name", "", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
                    LE(nm, -1, -1, 1);
                    var add = TextButton(ctx, row, "Add", Etp2Spec.SortAddRow, BtnStyle.Dynamic, out var addLabel);
                    OnClickIndex(add, sort, nameof(etp2_sort_page.OnAdd), k);
                    candRows[k] = row.gameObject; candNames[k] = nm; candAdd[k] = add; candAddLabels[k] = addLabel;
                    row.gameObject.SetActive(false);
                }
                list.gameObject.SetActive(false);

                // 追加したもの: [名前 16] [×(外す)]
                for (int r = 0; r < Etp2Spec.SortMaxSel; r++)
                {
                    int k = c * Etp2Spec.SortMaxSel + r;
                    var row = NewRect(content, "Selected" + c + "_" + r);
                    HLG(row, Etp2Spec.Gap);
                    LE(row, -1, Etp2Spec.ButtonH);
                    var nm = NewText(ctx, row, "tx_Name", "", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
                    LE(nm, -1, -1, 1);
                    var remove = CloseButton(ctx, row, "Remove");
                    Hover(ctx, remove, "外す");
                    OnClickIndex(remove, sort, nameof(etp2_sort_page.OnRemove), k);
                    selRows[k] = row.gameObject; selNames[k] = nm;
                    row.gameObject.SetActive(false);
                }
            }
            sort.arrowTexts = arrows;
            sort.listRoots = listRoots;
            sort.listRects = listRects;
            sort.emptyRows = emptyRows;
            sort.candRows = candRows;
            sort.candNames = candNames;
            sort.candAddButtons = candAdd;
            sort.candAddLabels = candAddLabels;
            sort.selRows = selRows;
            sort.selNames = selNames;
            return sort;
        }

        // ---- 5章 詳細モニター ----

        static void BuildDetailMonitor(GameObject root)
        {
            var detail = RootComponent<etp2_detail_monitor>(root);
            var ctx = new Etp2Ctx();
            var m = BuildMonitorSkeleton(root, ctx, "detail", Etp2Spec.DetailNav);
            detail.theme = m.theme;
            ctx.listeners.Add(detail);

            // 5.1 画像説明
            var desc = m.contents[0];
            TitleAction(ctx, m.titleRows[0], "pad", "Pad用URL", detail, nameof(etp2_detail_monitor.OnPadUrl));
            TitleAction(ctx, m.titleRows[0], "link", "WebページのURL", detail, nameof(etp2_detail_monitor.OnWebUrl));
            detail.dateText = NewText(ctx, desc, "tx_Date", "2026/11/13 07:21", Etp2Spec.FsBody, Role.Tx);

            var people = NewRect(desc, "People");
            HLG(people, 0).childForceExpandWidth = true;
            LE(people, -1, Etp2Spec.CircleIcon);
            for (int h = 0; h < 2; h++)
            {
                var half = NewRect(people, h == 0 ? "User" : "Title");
                HLG(half, Etp2Spec.Gap);
                LE(half, 0, Etp2Spec.CircleIcon, 1);
                Circle(ctx, half, "Icon");
                var t = NewText(ctx, half, "tx_Name", h == 0 ? "ユーザー名" : "称号名", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
                LE(t, -1, -1, 1);
                if (h == 0) detail.userText = t; else { detail.titleText = t; detail.titleRoot = half.gameObject; }
            }

            // 閲覧数・すーぱーいいね数・いいね数(3等分して各中央. アイコン 20 + 数 16)
            var stats = NewRect(desc, "Stats");
            HLG(stats, 0).childForceExpandWidth = true;
            LE(stats, -1, Etp2Spec.Icon + 4);
            string[] statIcons = { "views", "star", "heart" };
            var nums = new TextMeshProUGUI[3];
            for (int s = 0; s < 3; s++)
            {
                var third = NewRect(stats, "Stat_" + statIcons[s]);
                HLG(third, Etp2Spec.Gap, TextAnchor.MiddleCenter);
                LE(third, 0, -1, 1);
                var icon = Img(ctx, third, "tx_Icon", Role.Tx);
                icon.sprite = SpriteOf(statIcons[s]);
                icon.preserveAspect = true;
                LE(icon, Etp2Spec.Icon, Etp2Spec.Icon);
                nums[s] = NewText(ctx, third, "tx_Num", "0", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
                nums[s].overflowMode = TextOverflowModes.Overflow;
            }
            detail.viewsText = nums[0]; detail.starsText = nums[1]; detail.likesText = nums[2];

            Divider(ctx, desc);
            Spacer(desc, 0);   // 区切り線と説明文の間は 16
            detail.descText = Body(ctx, desc, "写真の説明文", "tx_Desc");
            Spacer(desc, 0);
            Divider(ctx, desc);

            var worldRow = NewRect(desc, "World");
            HLG(worldRow, Etp2Spec.Gap);
            LE(worldRow, -1, Etp2Spec.ButtonH);
            detail.worldText = NewText(ctx, worldRow, "tx_World", "ワールド名", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.MidlineLeft, false);
            LE(detail.worldText, -1, -1, 1);
            var portal = IconButton(ctx, worldRow, "Portal", "portal", out _);
            Hover(ctx, portal, "ポータルを開く");
            OnClick(portal, detail, nameof(etp2_detail_monitor.OnPortal));
            detail.authorText = NewText(ctx, desc, "tx_Author", "作者名", Etp2Spec.FsBody, Role.Tx, TextAlignmentOptions.TopLeft, false);
            detail.worldDescText = Body(ctx, desc, "ワールドの説明文", "tx_WorldDesc");

            // 5.2 同じワールドの投稿・同じユーザーの投稿
            detail.worldHeadText = Body(ctx, m.contents[1], "ワールド: ", "tx_Head");
            detail.worldRows = PostRows(ctx, m.contents[1], detail);
            detail.userHeadText = Body(ctx, m.contents[2], "ユーザー: ", "tx_Head");
            detail.userRows = PostRows(ctx, m.contents[2], detail);

            EndScreen(m.screen, ctx, m.theme);
        }

        static etp2_post_row[] PostRows(Etp2Ctx ctx, RectTransform content, etp2_detail_monitor detail)
        {
            var rows = new etp2_post_row[Etp2Spec.PostRowPool];
            var buttons = new List<Button>();
            int pad = (int)Etp2Spec.TextButtonPadX;
            int v = (int)Etp2Spec.Gap;
            for (int i = 0; i < rows.Length; i++)
            {
                // 1件 = 1つのボタン. 上下の余白 8、左右の余白 16
                var b = NewButton(ctx, content, "PostRow" + i, BtnStyle.Dynamic);
                VLG(b, 0, new RectOffset(pad, pad, v, v));
                var row = Add<etp2_post_row>(b);
                row.button = b;

                var head = NewRect(b.transform, "Head");
                HLG(head, Etp2Spec.Pad);
                row.nameText = NewText(ctx, head, "Name", "", Etp2Spec.FsButton, Role.None, TextAlignmentOptions.MidlineLeft, false);
                LE(row.nameText, -1, -1, 1);
                var stats = NewRect(head, "Stats");
                HLG(stats, Etp2Spec.Gap);
                string[] icons = { "views", "star", "heart" };
                var statTexts = new TextMeshProUGUI[3];
                var statImages = new Image[3];
                for (int s = 0; s < 3; s++)
                {
                    var st = NewRect(stats, "St_" + icons[s]);
                    HLG(st, 4);   // モックの .st の gap 4
                    var icon = Img(ctx, st, "Icon", Role.None);
                    icon.sprite = SpriteOf(icons[s]);
                    icon.preserveAspect = true;
                    LE(icon, 16, 16);
                    statImages[s] = icon;
                    statTexts[s] = NewText(ctx, st, "Num", "0", Etp2Spec.FsBody, Role.None, TextAlignmentOptions.MidlineLeft, false);
                    statTexts[s].overflowMode = TextOverflowModes.Overflow;
                }
                row.viewsText = statTexts[0]; row.starsText = statTexts[1]; row.likesText = statTexts[2];
                row.statIcons = statImages;

                var sub = NewRect(b.transform, "Sub");
                HLG(sub, Etp2Spec.Pad);
                row.titleText = NewText(ctx, sub, "Title", "", Etp2Spec.FsBody, Role.None, TextAlignmentOptions.MidlineLeft, false);
                LE(row.titleText, -1, -1, 1);
                LE(NewRect(sub, "Flex"), 0, -1, 0.0001f);
                row.dateText = NewText(ctx, sub, "Date", "", Etp2Spec.FsBody, Role.None, TextAlignmentOptions.MidlineRight, false);
                row.dateText.overflowMode = TextOverflowModes.Overflow;

                // 3行目から写真説明. 最大2行で自動改行し、あふれは「…」
                row.descText = NewText(ctx, b.transform, "Desc", "", Etp2Spec.FsBody, Role.None, TextAlignmentOptions.TopLeft, true, true);
                row.descText.overflowMode = TextOverflowModes.Ellipsis;
                row.descText.maxVisibleLines = 2;

                row.indexButton = OnClickIndex(b, detail, nameof(etp2_detail_monitor.OnRow), i);
                rows[i] = row;
                buttons.Add(b);
                b.gameObject.SetActive(false);
            }
            EditorPaintDynamic(buttons);
            return rows;
        }

        // ---- 6章 ビューア ----

        static void BuildViewer(GameObject root)
        {
            var viewer = RootComponent<etp2_viewer>(root);
            var ctx = new Etp2Ctx();
            Vector2 size = Etp2Spec.ViewerSize;
            var screen = Screen(root, size, new Vector2(0.5f, 1f));
            var theme = BeginScreen(screen, ctx);
            viewer.theme = theme;
            viewer.tooltip = ctx.tooltip;
            viewer.screen = screen.gameObject;
            ctx.listeners.Add(viewer);

            var bg = Img(ctx, screen, "bg_Background", Role.Bg);
            Stretch(bg.rectTransform);
            Header(ctx, screen);

            // ヘッダー中央「XXさんが操作中」. 幅は最大 240、長い名前は「…」
            var owner = NewText(ctx, screen, "tx_Owner", "XXさんが操作中", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.Center, false);
            Place(owner.rectTransform, (size.x - 240) / 2f, Etp2Spec.Pad, 240, Etp2Spec.HeaderH);
            viewer.ownerText = owner;

            float right = size.x - Etp2Spec.Pad;
            var close = CloseButton(ctx, screen, "Close");
            Place((RectTransform)close.transform, right - Etp2Spec.IconButton, Etp2Spec.Pad, Etp2Spec.IconButton, Etp2Spec.IconButton);
            Hover(ctx, close, "閉じる");
            OnClick(close, viewer, nameof(etp2_viewer.OnClose));
            var lockB = IconButton(ctx, screen, "Lock", "lock_open", out var lockIcon, BtnStyle.Dynamic);
            Place((RectTransform)lockB.transform, right - Etp2Spec.IconButton * 2 - Etp2Spec.Gap, Etp2Spec.Pad, Etp2Spec.IconButton, Etp2Spec.IconButton);
            viewer.lockHover = Hover(ctx, lockB, "鍵をかける");
            OnClick(lockB, viewer, nameof(etp2_viewer.OnLock));
            viewer.lockButton = lockB;
            viewer.lockIcon = lockIcon;
            viewer.lockOpenSprite = SpriteOf("lock_open");
            viewer.lockClosedSprite = SpriteOf("lock_closed");
            EditorPaintDynamic(new[] { lockB }, null, new[] { lockIcon });

            // 画像 528×528
            float top = Etp2Spec.Pad + Etp2Spec.HeaderH + Etp2Spec.Pad;
            float img = size.x - Etp2Spec.Pad * 2;
            var photoRt = NewRect(screen, "Photo");
            photoRt.gameObject.AddComponent<CanvasRenderer>();
            var photo = photoRt.gameObject.AddComponent<RawImage>();
            photo.raycastTarget = false;
            photo.color = Etp2Spec.PostColor(0);
            Place(photoRt, Etp2Spec.Pad, top, img, img);
            viewer.photo = photo;

            // 下の行: [◀ 前の画像]   1 / 12   [次の画像 ▶]
            var pager = NewRect(screen, "Pager");
            Place(pager, Etp2Spec.Pad, top + img + Etp2Spec.Pad, img, Etp2Spec.ButtonH);
            HLG(pager, Etp2Spec.Gap);
            var left = TextButton(ctx, pager, "Left", "◀ 前の画像", BtnStyle.Normal, out var leftLabel);
            OnClick(left, viewer, nameof(etp2_viewer.OnLeft));
            LE(NewRect(pager, "Flex"), -1, -1, 1);
            var rightB = TextButton(ctx, pager, "Right", "次の画像 ▶", BtnStyle.Normal, out var rightLabel);
            OnClick(rightB, viewer, nameof(etp2_viewer.OnRight));
            var page = NewText(ctx, pager, "tx_Page", "1 / 12", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.Center, false);
            page.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch(page.rectTransform, 160, 0, 160, 0);   // 真ん中に固定
            viewer.leftLabel = leftLabel;
            viewer.rightLabel = rightLabel;
            viewer.pageText = page;

            // 6.3 受け渡しのパネル: ヘッダーより下を暗幕で覆い、真ん中にパネル
            var hand = Img(ctx, screen, "dim_Handover", Role.None, true);
            hand.color = new Color(0, 0, 0, Etp2Spec.DimAlpha);
            Place(hand.rectTransform, 0, top, size.x, size.y - top);
            var panel = Img(ctx, hand.transform, "bg_Panel", Role.Bg, true);
            var pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
            pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(Etp2Spec.HandoverW, 200);
            int p16 = (int)Etp2Spec.Pad;
            VLG(panel, Etp2Spec.Pad, new RectOffset(p16, p16, p16, p16));
            FitVertical(panel);
            viewer.handoverText = NewText(ctx, panel.transform, "tx_Text", "XXさんが操作中です。\n操作を代わってもらいますか?", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.TopLeft, true, true);
            var track = Img(ctx, panel.transform, "btn_Progress", Role.Btn);
            LE(track, -1, Etp2Spec.ProgressH);
            var fill = Img(ctx, track.transform, "acc_Fill", Role.Acc);
            var fr = fill.rectTransform;
            fr.anchorMin = new Vector2(0, 0);
            fr.anchorMax = new Vector2(0, 1);
            fr.pivot = new Vector2(0, 0.5f);
            fr.anchoredPosition = Vector2.zero;
            fr.sizeDelta = new Vector2(0, 0);
            viewer.progressRoot = track.gameObject;
            viewer.progressFill = fr;
            viewer.progressWidth = Etp2Spec.HandoverW - Etp2Spec.Pad * 2;
            var btns = NewRect(panel.transform, "Buttons");
            HLG(btns, Etp2Spec.Gap, TextAnchor.MiddleRight);
            LE(btns, -1, Etp2Spec.ButtonH);
            var second = TextButton(ctx, btns, "Secondary", "やめる", BtnStyle.Dynamic, out var secondLabel);
            OnClick(second, viewer, nameof(etp2_viewer.OnSecondary));
            var primary = TextButton(ctx, btns, "Primary", "はい", BtnStyle.Dynamic, out var primaryLabel);
            OnClick(primary, viewer, nameof(etp2_viewer.OnPrimary));
            viewer.secondaryButton = second; viewer.secondaryLabel = secondLabel;
            viewer.primaryButton = primary; viewer.primaryLabel = primaryLabel;
            EditorPaintDynamic(new[] { second, primary }, new[] { secondLabel, primaryLabel });
            viewer.handoverRoot = hand.gameObject;
            hand.gameObject.SetActive(false);
            owner.gameObject.SetActive(false);

            EndScreen(screen, ctx, theme);
        }

        // ---- 7章 フォトフレーム ----

        static void BuildPhotoFrame(GameObject root)
        {
            var frame = RootComponent<etp2_photo_frame>(root);
            var ctx = new Etp2Ctx();
            Vector2 size = Etp2Spec.PhotoFrameSize;
            var screen = Screen(root, size, new Vector2(0.5f, 0.5f));
            var theme = BeginScreen(screen, ctx);

            var photoRt = NewRect(screen, "Photo");
            Stretch(photoRt);
            photoRt.gameObject.AddComponent<CanvasRenderer>();
            var photo = photoRt.gameObject.AddComponent<RawImage>();
            photo.color = Etp2Spec.PostColor(0);
            var b = photoRt.gameObject.AddComponent<Button>();
            b.targetGraphic = photo;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            OnClick(b, frame, nameof(etp2_photo_frame.OnClick));
            frame.photo = photo;

            // 写真の上 8 に「X人が見ています」. 幅は最大 528、長いときは「…」
            var watch = NewText(ctx, screen, "tx_Watchers", "3人が見ています", Etp2Spec.FsButton, Role.Tx, TextAlignmentOptions.Bottom, false);
            var wr = watch.rectTransform;
            wr.anchorMin = wr.anchorMax = new Vector2(0.5f, 1);
            wr.pivot = new Vector2(0.5f, 0);
            wr.anchoredPosition = new Vector2(0, Etp2Spec.Gap);
            wr.sizeDelta = new Vector2(Etp2Spec.WatchersMaxW, 24);
            frame.watchersText = watch;

            EndScreen(screen, ctx, theme);
        }
    }
}
