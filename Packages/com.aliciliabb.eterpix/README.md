# EterPix

外部サーバー (eterpix.com) の投稿と写真を、VRChat ワールド内のモニターに表示するギミック。

## 必要なもの

- VRChat SDK - Worlds 3.7.1 以上 (UdonSharp 同梱)
- TextMesh Pro Essentials (Window > TextMeshPro > Import TMP Essential Resources)

## 使い方

1. `Etp_テンプレート.prefab` をシーンに置く
2. `EterpixRequester` の Inspector の「取得先」ドロップダウンで JSON の URL を選ぶ
   - `パブリック投稿`: `https://eterpix.com/api/vrc/v1/public` が自動で入る
   - `自分で入力`: フォルダ等のリンクをそのまま貼り付ける(補間しない)。v1 形式 (`/api/vrc/v1/...`) のみ対応
3. 画像 URL (256 件、`requestUrl` + `/00`〜`/FF`) は URL を変えた瞬間に自動で反映される。
   メニュー `ali/eterpix/Sync URL Lists from requestUrl (自動で反映されます・触らないでください)` は念のための手動作り直し用で、通常は触らない
4. モニターを増やす場合は `Prefabs/EterpixMonitor_Stack.prefab`(縦積み)または `Prefabs/EterpixMonitor_Split.prefab`(下段が横並び)を `EterpixRequester` の子に置く
   - 大きさはモニターのルートの Scale で変える(子の Scale は変えない)
   - 旧デザインの `Prefabs/Legacy/EterpixMonitorUI.prefab` も引き続き使える(エラー表示・テーマには非対応)
5. 写真は `EterpixRequester` の BoxCollider の範囲内にいるときだけ読み込む。任意でサイズ調整できます

### 再読み込みボタン

- モニター右上の再読み込みボタン(Stack / Split のみ)を押すと、投稿の一覧(JSON)を取り直す
  - 押した本人にだけ反映される(同期しない)。同じ取得先のモニターはまとめて更新される
  - 連打防止のため、押した後 10 秒は押せない
  - 画像は取り直さない(表示中の画像はそのまま使う)
- アイコンは Material Symbols (Apache License 2.0, `UI/MaterialSymbols_LICENSE.txt`)

### ボタン列(モニターの一番下)

- Stack / Split の一番下に、ボタンだけを並べる列 `Screen/ActionBar` がある。ボタンは左詰めで、`ポータルを開く` だけは常に一番右に固定。左から次の 3 つ
  - `Pad用URLを表示`: 表示中の投稿の Pad 用 URL(`https://eterpix.com/api/vrc/v1/pad/<投稿ID>`)を VRChat の入力 UI に出す。コピーして Pad の URL 欄に貼ると、同じ投稿を Pad で見られる
  - `WebページのURLを表示`: 表示中の投稿の Web ページ(`https://eterpix.com/web/post/<投稿ID>`)の URL を出す。ブラウザで開いてもらう
  - `ポータルを開く`: 写真にワールド情報があるときだけ出る(ワールド情報が無い投稿では消え、右端が空く)
  - URL のドメインは `EterpixRequester` の取得先 URL から取る。同期はしない(表示中のページは同期済みのため、各自で作る)
- ボタンを増やすときは `ActionBar` の子に置くだけで左から並ぶ(`HorizontalLayoutGroup`)。`acc_PortalButton` は `LayoutElement` の Ignore Layout で並びから外して右端に固定し、列の右の余白(Padding Right = ポータルの幅 + 8)でポータルと重ならないようにしている。幅は各ボタンの `LayoutElement` の Preferred Width で決める(文字の幅 + 左右 10)
  - 今の 3 つで列の幅 528 をほぼ使い切っている。4 つ目を足すときは、文言を短くするか文字を小さくする
  - ボタンの背景は名前の接頭辞 `acc_` でテーマのアクセント色が当たる(`eterpix_monitor_theme` の「子から自動収集」で登録)。ボタンの文字はテーマに関わらず黒に固定するため、接頭辞を付けない(`Label`)
- URL を表示するボタンは、背景(`acc_`)と見出し(`Label`)の枠の中に、インフォメーションウィンドウの `https://eterpix.com/` 欄(`btn_UrlField`)をそのまま複製した読み取り専用の `TMP_InputField`(`UrlField`)を重ねている。URL の文字と欄の背景は透明。入力欄にレイアウトや色の変化などを足すと VRChat で押しても開かなくなったため、欄そのものは複製元から変えないこと

### グループ(右上のボタン)

- 右上のボタン群のいちばん左(テーマ切替の左)が `グループ` ボタン(`btn_GroupButton`)。押すとインフォメーションと同じ位置に `グループ` ウィンドウ(`bg_GroupWindow`)が開く(インフォメーションとは同時に開かない。開いている間はアイコンが ×)
- ウィンドウには EterPix の紹介文、サイトと BOOTH の URL(コピー用の欄)、`グループのページを開く` ボタンがある。ボタンを押すと VRChat のメインメニューで EterPix の VRChat グループ(`grp_b3f71193-2ed1-4de0-a5f1-3237609f4aeb`)のページが開く(`Store.OpenGroupPage`。押した本人だけ。ClientSim では何も起きない)
- グループ ID はギミック側で固定(`eterpix_monitor.GroupId`)
- アイコン `UI/icon_group.png` は EterPix 用に作成したもの

### テーマ(表示色)

- `EterpixTheme`(テンプレートに含まれる。`Prefabs/EterpixTheme.prefab`)の Inspector で設定する
  - 切替モード: `切替あり`(モニターの切替ボタンで 黒 → 白 →(カスタム)→ 黒)/ `固定: 黒` / `固定: 白` / `固定: カスタム`
  - 切り替えは押した本人にだけ反映され、ワールドごとに保存される
  - `カスタム` の色は自由に入力できる
- モニターの `eterpix_monitor_theme` で `直接編集` を ON にすると、そのモニターはテーマの対象外になる(各 Image / TMP の色を直接編集する)

### ポータルの大きさ

- ポータル本体(`EterpixPortal`)は 1 つを共有し、開いたモニター/Pad の場所へ移動する
- 開く時の大きさ(倍率)は要求者の Inspector の `Portal Size` で決める
  - `EterpixRequester`: 配下のモニターから開く時の大きさ
  - `EterpixPadRequester`: Pad から開く時の大きさ
  - `0`(既定)なら `EterpixPortal` の `siz_value` を使う(従来どおり)
- 開く位置から下へずらす量(`EterpixPortal` の `offset`)も、開いた大きさに比例する

### 注意

- 次のオブジェクト名は変更しないこと (スクリプトが名前で探す)
  - `EterpixDownloaderV2` / `EterpixPortal` / `EterpixTheme`
  - (任意) ログを出したい場合は `eterpix_debug` を付けた `EterpixDebug` という名前の GameObject を置く
- `EterpixTheme` はワールドに1つだけ有効になる(テンプレートを複数置いた場合、最初に見つかったものの設定が使われ、他は無効になる)
- UdonBehaviour を別の GameObject へ移動・追加・削除すると、ビルドが
  `Failed to assign network IDs` で失敗することがある。
  VRChat SDK の Network ID ユーティリティで競合を解消すること

## 旧バージョンからの更新

2.3.0 の次の版から、モニターの一番下にボタン列が付き、モニターが 52 高くなった(Stack 684 → 736、Split 608 → 660)。上の端の位置は変わらず、下に伸びる。ポータルが開く位置(`PortalSpawnPoint`)も同じだけ下がる。モニターのすぐ下に物を置いている場合は位置を確かめること。

テンプレート内のモニターは `EterpixMonitor_Stack` に置き換わった。シーン上でモニターの位置や offset を変更していた場合、更新後にテンプレートの値へ戻るので、設定し直すか、旧デザインの `Prefabs/Legacy/EterpixMonitorUI.prefab` を使う。更新後は VRChat SDK の Network ID ユーティリティでシーンの Network ID を整理してから保存する。

## フォルダ構成

```
EterPix/
├─ Etp_テンプレート.prefab   … 一式 (Downloader / Requester + モニター4台 / Portal)
├─ Prefabs/                 … 個別の prefab(Legacy/ に旧モニター)
├─ Scripts/                 … UdonSharp スクリプト
├─ Editor/                  … URL 同期などのエディタ拡張
├─ UI/                      … 読み込み中の明滅シェーダー、角丸画像、再読み込みアイコン等
├─ Fonts/                   … NotoSansJP-Bold (SIL Open Font License 1.1, OFL.txt) / ロゴ用 StackSansNotch-Bold (SIL Open Font License 1.1, OFL-StackSansNotch.txt)
└─ テンプレ_シーン/          … 設置例のシーン
```

## ライセンス

- `Fonts/NotoSansJP-Bold.ttf` とその SDF は SIL Open Font License 1.1 (`Fonts/OFL.txt`)
- `Fonts/StackSansNotch-Bold.ttf` とその SDF は SIL Open Font License 1.1 (`Fonts/OFL-StackSansNotch.txt`)。サイトのロゴと同じ書体 Stack Sans Notch (Google Fonts) の可変フォントから、太さ 700 に固定して ASCII の文字だけに絞ったもの
- `UI/icon_reload.png` は Material Symbols の `refresh` を PNG にしたもの。Apache License 2.0 (`UI/MaterialSymbols_LICENSE.txt`)
