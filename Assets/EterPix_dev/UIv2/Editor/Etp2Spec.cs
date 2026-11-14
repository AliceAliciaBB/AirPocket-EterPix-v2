using UnityEngine;

namespace ali.eterpix.ui2.editor
{
    // v2 UI のモックの数字・文言・ダミーデータ. モックが変わったら、まずここを直してビルドし直す.
    // 出典: 統合管理(D:\git\eterpix_integrated_management, origin/main)
    //   docs/plans/unity-ui-v2-mock.md(章番号をコメントに書く)、docs/mocks/unity-ui-v2-mock.html
    // 色は実行時と同じ表を使うので、ここには無い(etp2_theme.GetPresetColor. モック 1.2)
    public static class Etp2Spec
    {
        // ---- 置き場所 ----
        public const string PackageRoot = "Packages/com.aliciliabb.eterpix/UIv2";
        public const string PrefabDir = PackageRoot + "/Prefabs";
        public const string SpriteDir = PackageRoot + "/Sprites";
        public const string ScriptDir = PackageRoot + "/Scripts";
        public const string FontPath = "Packages/com.aliciliabb.eterpix/Fonts/NotoSansJP-Bold SDF.asset";   // 1.1 フォントは1種だけ
        public const string TestScenePath = "Assets/EterPix_dev/UIv2/Etp2_Test.unity";

        // ---- 世界の大きさ(v1 のモニターと同じ縮尺. 560px = 0.7m) ----
        public const float WorldScale = 0.00125f;

        // ---- 2. 画面の大きさ ----
        public static readonly Vector2 MonitorSize = new Vector2(560, 792);
        public static readonly Vector2 ViewerSize = new Vector2(560, 672);
        public static readonly Vector2 PhotoFrameSize = new Vector2(200, 200);   // 仮

        // ---- 1.3 文字の大きさ ----
        public const float FsLogo = 30;
        public const float FsPageTitle = 24;
        public const float FsHeading = 18;
        public const float FsButton = 16;
        public const float FsBody = 13;
        public const float FsArrow = 12;
        public const float BodyLineHeight = 1.6f;   // 本文の行の高さ(文字の大きさの倍)

        // ---- 1.4 部品 / 3章 骨組み ----
        public const float Pad = 16;          // 画面の余白
        public const float Gap = 8;           // 部品の間
        public const float HeaderH = 40;
        public const float ButtonH = 40;
        public const float IconButton = 40;
        public const float Icon = 20;
        public const float TextButtonPadX = 16;
        public const float LinkButtonH = 36;  // リンクのページのアクセントのボタンだけ
        public const float UrlFieldH = 36;
        public const float UrlFieldPadX = 8;
        public const float DividerH = 2;
        public const float CircleIcon = 32;
        public const float ScrollbarW = 8;
        public const float NavW = 40;
        public const float NavExpandedW = 200;
        public const float NavBandW = 216;
        public const float PageTitleH = 40;
        public const float PageTitleGap = 16; // 見出しと中身の間
        public const float DimAlpha = 0.5f;

        // 1.5 ホバーの札
        public const float TipH = 32;
        public const float TipPadX = 8;

        // 4.3 ソートのドロップダウン
        public const int SortMaxCand = 16;    // etp2_sort_page.MaxCand と同じ
        public const int SortMaxSel = 16;     // etp2_sort_page.MaxSel と同じ
        public const float DropdownRowH = 40;
        public const float DropdownMaxRows = 5.5f;

        // 5.2 一覧の行の数(使い回し)
        public const int PostRowPool = 16;
        public const int WorldRowPool = 16;

        // 6.2 写真の左右を押して前/次(v1 の PrevHit / NextHit と同じ. モックには無い. 2026-11-14 ユーザー依頼)
        // 画面の端から幅 200 ずつ、高さは写真の 528. 真ん中は押しても何も起きない
        public const float PhotoHitW = 200;

        // 6.3 受け渡しのパネル
        public const float HandoverW = 400;
        public const float ProgressH = 8;

        // 7 フォトフレーム
        public const float WatchersMaxW = 528;

        // ---- 4章 メニューモニターのナビ(アイコン名, 名前) ----
        public static readonly string[,] MenuNav =
        {
            { "info", "インフォメーション" },
            { "group", "リンク" },
            { "sort", "ソート" },
            { "pen", "ペン" },
            { "teleport", "テレポート" },
            { "users", "ユーザー" },
            { "pad", "Pad呼び出し" },
            { "compass", "モニターへ案内" },
            { "portal", "ワールド一覧" },
            { "gear", "設定" },
        };
        public const int MenuPageInfo = 0, MenuPageLink = 1, MenuPageSort = 2, MenuPageWorlds = 8, MenuPageSettings = 9;

        // ---- 5章 詳細モニターのナビ ----
        public static readonly string[,] DetailNav =
        {
            { "desc", "画像説明" },
            { "world", "同じワールドの投稿" },
            { "user", "同じユーザーの投稿" },
        };

        // ---- 4.1 インフォメーション(v1 の bg_InfoWindow から転記) ----
        public const string InfoHeading = "EterPix(エターピクス)について";
        // 小見出しの <b> は外した(フォントが Bold なので、モックでは本文と同じ太さ. TMP の <b> はさらに太くなる)
        public const string InfoBody =
            "EterPixは、VRChatで撮影した写真を共有できるSNSです。このモニターでは、EterPixに投稿された公開写真を閲覧できます。\n\n" +
            "ページを切り替える\n写真の左右を押すと、前後の投稿に切り替わります。表示中のページは、このインスタンスにいる全員で共有されます。\n\n" +
            "ワールドへ行く\n写真にワールド情報がある場合は「ポータルを開く」を押すと、撮影されたワールドへのポータルが開きます。\n\n" +
            "再読み込み\n右上の再読み込みボタンで、投稿の一覧を取り直します。あなたにだけ反映されます。一度押すと、しばらくは押せません。\n\n" +
            "表示色を変える\n右上の切替ボタンで、黒・白などの表示色を切り替えられます。切り替えはあなたにだけ反映され、次に来たときも保持されます(ワールドによっては切り替えできません)。\n\n" +
            "PadやWebで見る\n下の「Pad用URLを表示」を押すと、表示中の投稿をPadで見るためのURLが出ます。コピーしてPadのURL欄に貼り付けてください。「WebページのURLを表示」を押すと、投稿のWebページのURLが出ます。ブラウザで開いてください。\n\n" +
            "グループ\n右上のグループボタンで、EterPixのVRChatグループの案内が開きます。\n\n" +
            "写真を投稿する\n投稿はWebから行えます。下のURLをコピーして、ブラウザで開いてください。";
        public const string InfoUrl = "https://eterpix.com/";
        public const string InfoFooter = "最新情報は X @_alicilia をご確認ください";

        // ---- 4.2 リンク(v1 の bg_GroupWindow から転記) ----
        public const string LinkHeading = "EterPixのグループ";
        public const string LinkBody =
            "SNSサイトEterPix（エターピクス）並びに、ワールドギミックを製作しています！\n" +
            "イベントは、追加機能の募集や、一緒に投稿写真を見る会を想定しています！\n" +
            "ぜひご参加下さい！";
        public const string LinkSiteLabel = "Webサイト";
        public const string LinkSiteUrl = "https://eterpix.com/";
        public const string LinkBoothLabel = "ギミックの配布場所";
        public const string LinkBoothUrl = "https://alicilia.booth.pm/items/8894268";
        public const string LinkGroupButton = "グループのページを開く";

        // ---- 4.3 ソート ----
        public const string SortReset = "初期設定に戻す";
        public const string SortApply = "適用";
        public const string SortOrderHeading = "並び順";
        public static readonly string[] SortOrders = { "最新順", "いいね数順" };
        public const string SortPromoteHeading = "上に表示する投稿";
        public const string SortPromoteBody = "どれか1つに当てはまる投稿を、並び順を保ったまま上に表示します。";
        public static readonly string[] SortCategories = { "ワールド", "タグ", "ユーザー" };
        public const string SortAddLabel = "追加する";
        public const string SortAddRow = "追加";
        public const string SortEmpty = "追加できるものはありません";

        // ---- 4.5 設定 ----
        public const string SettingsThemeHeading = "テーマ";
        public static readonly string[] SettingsThemes = { "黒", "白" };
        public const string SettingsThemeBody = "あなたにだけ反映され、次に来たときも保持されます(ワールドによっては切り替えできません)。";
        public const string SettingsSwapHeading = "前の画像・次の画像のボタン";
        public static readonly string[] SettingsSwaps = { "◀ 前 / 次 ▶", "◀ 次 / 前 ▶" };
        public const string SettingsSwapBody = "ビューアの下のボタンの左右を入れ替えます。あなたにだけ反映されます。";

        // ---- ダミーデータ(モック HTML の POSTS / WORLDS と同じ) ----
        static readonly string[] PostDes =
        {
            "夕暮れのワールドで友達と撮った一枚です。光の入り方がきれいだったので、みんなで並んで何枚も撮り直しました。最後の一枚がいちばん自然な表情になりました。 #夕暮れ #集合写真",
            "静かな場所で読書。",
            "灯台の上から街を見下ろしたところ。船がちょうど湾を一周して戻ってくるところでした。 #夜景",
            "新しい衣装のお披露目です! 背景の花がちょうど満開で、色がよく合いました。 #ポートレート",
        };
        public static readonly string[,] Posts =
        {
            { "海辺の街", "ありす", "2026/11/13 07:21" }, { "夜の図書館", "ぼぶ", "2026/11/12 23:05" },
            { "海辺の街", "ちゃーりー", "2026/11/12 21:40" }, { "空中庭園", "ありす", "2026/11/12 18:12" },
            { "海辺の街", "でいじー", "2026/11/11 22:58" }, { "雪の駅", "ありす", "2026/11/11 20:03" },
            { "海辺の街", "えりっく", "2026/11/10 19:47" }, { "星見の丘", "ぼぶ", "2026/11/10 01:16" },
            { "海辺の街", "ふらん", "2026/11/09 22:30" }, { "森のカフェ", "ありす", "2026/11/09 12:09" },
            { "海辺の街", "ぐれいす", "2026/11/08 23:51" }, { "水上マーケット", "ありす", "2026/11/08 20:27" },
        };
        static readonly string[] Titles = { "星を集める人", "", "", "撮影部", "", "夜更かし常連" };
        static readonly int[] Views = { 1234, 87, 452, 3021, 16, 640 };
        static readonly int[] Stars = { 12, 0, 3, 41, 0, 5 };
        static readonly int[] Likes = { 345, 9, 58, 702, 2, 77 };

        public static string PostDesc(int i) { return PostDes[(i >> 1) % PostDes.Length]; }
        public static string PostTitle(int i) { return Titles[(i >> 1) % Titles.Length]; }
        public static int PostViews(int i) { return Views[i % Views.Length]; }
        public static int PostStars(int i) { return Stars[i % Stars.Length]; }
        public static int PostLikes(int i) { return Likes[i % Likes.Length]; }
        // 写真の代わりの色(投稿ごとに色相をずらす)
        public static Color PostColor(int i) { return Color.HSVToRGB((i * 0.083f) % 1f, 0.35f, 0.75f); }

        public static readonly string[,] Worlds =
        {
            { "海辺の街", "作者A" }, { "夜の図書館", "作者B" }, { "空中庭園", "作者C" }, { "雪の駅", "作者A" },
            { "星見の丘", "作者D" }, { "森のカフェ", "作者E" }, { "水上マーケット", "作者F" }, { "古城の広間", "作者G" },
            { "ガラスの温室", "作者H" }, { "月面基地", "作者I" }, { "雨の商店街", "作者J" }, { "桜の参道", "作者K" },
        };
        public const string WorldDesc =
            "海辺の小さな街をイメージしたワールドです。時間の経過で空の色が変わり、夕方には街灯が灯ります。写真撮影向けのスポットを各所に用意しています。\n" +
            "ミラーは広場と灯台の2か所にあります。動作が重い場合は、設定から影の描画を切ってください。";
    }
}
