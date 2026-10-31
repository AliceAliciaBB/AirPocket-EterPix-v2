# EterPix-app (NudeNyang Tablet 用アドオン)

NudeNyang Tablet のホーム画面に「EterPix」アプリを1つ足す、**非公式**のアドオンです。
EterPix の写真を、タブレットの中でスワイプして見られます(気になる / 気にならない、拡大、気になる投稿の一覧、ポータル)。

> **これは NudeNyang の公式製品でも、公式サポート対象でもありません。**
> 不具合を NudeNyang 本体の作者へ問い合わせないでください。

## 必要なもの

- **NudeNyang Tablet の正規製品(別途購入)**。このリポジトリ/配布物には本体のファイルを含みません。
- 対応バージョン: **NudeNyang 1.0.2**(`Assets/NudeNyang/InstallManager/Editor/Updates/installed-release.json` の `version`)
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
- 取得先: WEB `https://eterpix.com/web/post/<poid>` / 高画質 `https://eterpix.com/api/images/<poid>`
- eterpix.com は VRChat の信頼済みドメインではないため、プレイヤー側で **「Allow Untrusted URLs」をON** にする必要があります(OFF のときはアプリ内にそう表示されます)

## 開発用メニュー

- **NudeNyang → EterPix-app → 幾何の自己テスト** — 縦横判定・回転・切り取りの純計算の自己テスト(コンソールに `[EtpTrace] test geometry PASS`)
- ログは `[EtpTrace]` の接頭辞でコンソールに出ます

## 既知の制約・未確認

- 実機(VRChat)での確認が未了: 画像キャッシュの上限 `maxCached`(仮 24)、既存モニターと同居したときの5秒制限の取り合い、入力欄でのコピー、ポータルの実配置と同期
- 「ロード完了」は先頭 `startupPreload`(仮 3)枚で判定し、残りは裏で取得します
- ヘッダーの `G` / `T` は文字の仮表示。♡ はフォントに無いため仮に ☆
- 未実装: ドラッグでのスワイプ、矢印キー、画像待ちの明滅
- 回転の向き(縦写真の見せ方)は合意書で未決のため、現在は「正立して枠に収める」
