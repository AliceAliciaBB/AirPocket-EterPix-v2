# EterPix-app (NudeNyang Tablet 用アドオン)

NudeNyang Tablet のホーム画面に「EterPix」アプリを1つ足す、**非公式**のアドオンです。
EterPix の写真を、タブレットの中でスワイプして見られます(気になる / 気にならない、拡大、気になる投稿の一覧、ポータル)。

> **これは NudeNyang の公式製品でも、公式サポート対象でもありません。**
> 不具合を NudeNyang 本体の作者へ問い合わせないでください。

## 必要なもの

- **NudeNyang Tablet の正規製品(別途購入)**。このリポジトリ/配布物には本体のファイルを含みません。
- 対応バージョン: **NudeNyang 1.0.4**(1.0.2 から更新して検証. 更新後は「Prepare pads in scene」と Builder をやり直す)(`Assets/NudeNyang/InstallManager/Editor/Updates/installed-release.json` の `version`)
- 本体は公式の場所 `Assets/NudeNyang/` に入れてください(旧い配置 `Assets/アセット(同期しない)/NudeNyang/` も検出しますが、非推奨)
- VRChat SDK(Worlds)+ UdonSharp

## 連絡先

- このアドオンの制作者: `@_alicilia`
- NudeNyang 本体の作者: `@NudeNyang_VRC`(本体についてのみ。このアドオンの問い合わせは不可)

## 入れ方

1. NudeNyang Tablet 本体を `Assets/NudeNyang/` に入れ、タブレットをシーンに置く
2. このアドオンのフォルダ `Assets/EterPixNudeNyang/` をプロジェクトに入れる
   (本体を入れる前でもコンパイルが通るように作ってあります。パッチを当てるメニューが出ないことはありません)
3. メニュー **NudeNyang → EterPix-app → パッチを検査** で、`未適用` / `適用済` / `アンカー不一致` を確認
4. メニュー **NudeNyang → EterPix-app → パッチを適用**(全部検査して、1つでも不一致なら何も書き換えません。元ファイルは `*.eterpix-orig` として隣に残ります)
5. スクリプトのコンパイルが終わるのを待つ(`isCompiling` が終わってから次へ)
6. メニュー **NudeNyang → EterPix-app → 配置 (シーンの全タブレット)** — シーン内のすべてのタブレットに EterPix アプリを配線します
7. ポータルを使うなら、シーンに `NudeNyangTravelPortal`(NudeNyang の Travel のポータル)を置いてから 6 を実行。無い場合は警告が出て、一覧の「ポータルを開く」ボタンだけ出ません

NudeNyang を更新したら、3 → 4 をもう一度行います。`アンカー不一致` が出たら、本体側の該当箇所が変わっています(パッチの見直しが要ります。何も書き換えずに止まります)。

## パッチ一覧(NudeNyang 本体へ当てる変更)

| ID | 対象 | 内容 |
|---|---|---|
| P1 | `NudeNyangTabletShell` | `AppCount` を 11 → 12 |
| P2 | 同上 | `eterpix` フィールドを追加 |
| P3 | 同上 | アプリ名 id=12 を「EterPix」に |
| P4 | 同上 | `OpenEterPix()` を追加 |
| P6 | 同上 | `eterpix.Present()` を呼ぶ |
| P7, P8 | `NudeNyangSharedControls` | ホーム/キーボードの許可メソッドに `OpenEterPix` を追加 |
| P9 | `NudeNyangTabletShellBuilder` | 12番目のアイコンの名前と action(誤って本体の Builder を走らせた場合の保険) |
| P10a〜i | `NudeNyangTravelPortal` | ポータルの配置元に EterPix-app を追加(Travel の経路は変えない) |
| P11 | `NudeNyangTabletThemeBuilder` | NudeNyang のテーマの自動配色から EterPix-app を外す |

(P5 は不要と判明して廃止)

## 使い方(アプリ内)

- ホームの「EterPix」を開く → 読み込みが終わると「push to start」
- スワイプ画面: 「気になる」「気にならない」で送る。写真を押すと拡大(情報帯あり → UIなし → 閉じる)
- ヘッダー: `i`(説明)/ `G`(グループ)/ `T`(黒⇔白)/ `☆`(気になる一覧)
- 気になる一覧: 各行に、サムネ・名前・ワールド名、`WEB` / `高画質` のコピー欄、`ポータルを開く`
  - `WEB` / `高画質` は押すと VRChat の入力欄が開くので、そこでコピーします(NudeNyang の他のコピー欄と同じ方式)
  - ポータル: 押す → 床を指して置く → 開く。置いている最中にもう一度押すと取り消し
- 「気になる」はそのセッション内だけ(保存しません)。**R18 の投稿は固定で除外**しています(コードに組み込み)

## 仕組みのメモ

- 新規コードは `Assets/EterPixNudeNyang/` のみ(本体のコードは含めない。`Assets/NudeNyang/` は gitignore)
- 画像は VRChat のダウンロード間隔(5秒)に合わせ、専用の取得キューで直列に取り、写真を「コラージュ」のアトラスから切り出して表示します(`EterPixAppImages` / `EterPixAppGeometry`)
- 画像の URL(VRCUrl)はビルド時に配列として焼き込みます
- 取得先: WEB `https://eterpix.com/web/post/<poid>` / 高画質(Pad 用 URL. モニターと同じ) `https://eterpix.com/api/vrc/v1/pad/<poid>`
- eterpix.com は VRChat の信頼済みドメインではないため、プレイヤー側で **「Allow Untrusted URLs」をON** にする必要があります(OFF のときはアプリ内にそう表示されます)

## 開発用メニュー

- **NudeNyang → EterPix-app → 幾何の自己テスト** — 縦横判定・回転・切り取りの純計算の自己テスト(コンソールに `[EtpTrace] test geometry PASS`)
- ログは `[EtpTrace]` の接頭辞でコンソールに出ます

## 既知の制約・未確認

- 実機(VRChat)での確認が未了: 画像キャッシュの上限 `maxCached`(仮 24)、既存モニターと同居したときの5秒制限の取り合い、入力欄でのコピー、ポータルの実配置と同期
- 「ロード完了」は先頭 `startupPreload`(仮 3)枚で判定し、残りは裏で取得します
- ヘッダーの `G` / `T` は文字の仮表示。♡ はフォントに無いため仮に ☆
- ドラッグでのスワイプ: カードを横に引く(ScrollRect の弾性)と、方向のボタンが明るくなり、カード幅の12%を越えた状態で**指を離すと**確定して次へ(引いている間は今のカードのまま。閾値未満で離すと戻るだけ)。離した検出は Card 上の EventTrigger(EndDrag)→`OnSwipeRelease`。エディタでは擬似入力で確認済み。VRChat で EventTrigger が呼ばれない場合は `swipe release` ログが出ない。**実際のレーザー/タッチでの手触りと閾値(`swipeHotStart` 0.04 / `swipeCommit` 0.12)は実機で調整**。離したときに `[EtpTrace] eterpix-app swipe release dx=... commit=...` が出る
- スワイプは NudeNyang 本体のジェスチャと同じ作法に合わせています(2026-11-01). カード・一覧には本体の触り判定ゲート(`NudeNyangTouchSlop`)が付き(ビルダーが `NudeNyangTouchSlopBuilder.Configure` を呼ぶ)、離し(PointerUp / EndDrag)は `OnSwipeRelease` に届きます。加えて、感度は本体の共通設定 `swipeSensitivity`(1.2)を掛けた量で閾値と比べる / 横が縦の 1.3 倍より大きいときだけスワイプ(軸ロック。縦に引いたら確定しない) / VR はトリガーを両手とも離したら確定(`InputUse`。EndDrag が来ない場合の保険) / 引いたあとの 0.25 秒は写真のタップ(拡大)を抑止 / 無効になったら引いている途中を取り消す。ログ: `swipe release dx=... sens=... horizontal=... commit=...`
- 未実装(ドラッグ関連): カードが飛ぶアニメ、次カードが下に見える重なり(矢印キーは実機未確認)
- 縦写真(`coro` が奇数)は、**スワイプ画面では縦のまま(正立)枠に収めます**(2026-11-01 のユーザー指示で、決定 D-112 の「Pad と同じ=倒す」から変更。拡大画面は従来どおり Pad の K-4=枠の長辺に合わせて反時計回りに倒す)
- タブレットを縦向きにしたときは、実際の枠の大きさ(`panel.rect`)で写真を作り直し、カード・ボタンを横いっぱいに広げます(縦写真が大きくなる)。縦向きになると `[EtpTrace] eterpix-app layout panel=... portrait=True rowHeight=...` が出ます
- 気になる一覧の行は、モックの縦向きと同じ構成です(画像は左で全段ぶん、右に名前・ワールド・ボタン。右寄せのボタンは、幅に収まらなければ次の段へ折り返す=縦向きは WEB/高画質 の下にポータルを開く)。行の高さは向きで変わります(`rowHeight`)
- ボタンの配色はモニターと同じです(通常=`--btn` 黒 `#2a2a2a` / 白 `#e6e6e6`、強調=accent `#9d6fff`。ヘッダーの4ボタン・スワイプの左右・WEB/高画質・「やめる」「もう一度見る」は通常色、「ポータルを開く」「はい」「push to start」は accent)。スワイプのドラッグ中だけ、方向のボタンが accent に色づきます
- 投稿の説明(API の `pode`)を、スワイプの情報帯(投稿者の下・2行まで)・拡大の情報帯・一覧の各行(ワールドの下・2行まで)に出します。改行は空白に直し、説明が無い投稿は欄を出さず情報帯を低いままにします。一覧の日付は出しません(説明で置き換え)
- 一覧と情報ページは、ボタンの無い所(行の余白・一覧の下の空き・情報ページ全体)でもレーザーでスクロールできます。本体が X フィードの空白部に使う「面の入力」(`NudeNyangTouchSlopBuilder.ConfigureViewportInput`: viewport に ScrollRect・Slider・EventTrigger を付けて触り判定ゲートへ結ぶ)を、ビルダーが一覧・情報ページにも付けます
- 一覧の上に固定の見出し「気になる投稿 N 件」があります
- 情報ページはモニターの案内文から、モニター固有の操作(ページ切替・再読み込み)を除き、アプリの操作説明を足した文面です。縦にスクロールでき、投稿ページの URL は一覧の WEB/高画質と同じコピー用の欄です。グループページの文面もモニターと同じ構成です
- 画面の行き来: 一覧(☆)・i・グループは重なる画面です。一覧→i→一覧のあとに×を押しても、スワイプへ戻れます(`returnScreen`=基準の画面、`overlayBack`=i・グループを閉じたときの戻り先)
- 一覧のスクロール枠とサムネ枠は、`RectMask2D` ではなくステンシルの `Mask` で切ります(NudeNyang は縦向きのとき viewport を回し、`RectMask2D` だと範囲が負になって行が全部消える。NudeNyang 本体の NudeNyangScrollBuilder と同じ対処)
