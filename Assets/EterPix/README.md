# EterPix

外部サーバー (eterpix.uk) の投稿と写真を、VRChat ワールド内のモニターに表示するギミック。

## 必要なもの

- VRChat SDK - Worlds 3.7.1 以上 (UdonSharp 同梱)
- TextMesh Pro Essentials (Window > TextMeshPro > Import TMP Essential Resources)

## 使い方

1. `Etp_テンプレート.prefab` をシーンに置く
2. `EterpixRequester` の Inspector の「取得先」ドロップダウンで JSON の URL を選ぶ
   - `パブリック投稿`: `https://api.eterpix.uk/api/vrc/v1/public` が自動で入る
   - `自分で入力`: フォルダ等のリンクをそのまま貼り付ける(補間しない)。v1 形式 (`/api/vrc/v1/...`) のみ対応
3. 画像 URL (256 件、`requestUrl` + `/00`〜`/FF`) は URL を変えた瞬間に自動で反映される。
   メニュー `ali/eterpix/Sync URL Lists from requestUrl (自動で反映されます・触らないでください)` は念のための手動作り直し用で、通常は触らない
4. モニターを増やす場合は `Prefabs/EterpixMonitor_Stack.prefab`(縦積み)または `Prefabs/EterpixMonitor_Split.prefab`(下段が横並び)を `EterpixRequester` の子に置く
   - 大きさはモニターのルートの Scale で変える(子の Scale は変えない)
   - 旧デザインの `Prefabs/Legacy/EterpixMonitorUI.prefab` も引き続き使える(エラー表示・テーマには非対応)

### 再読み込みボタン

- モニター右上の再読み込みボタン(Stack / Split のみ)を押すと、投稿の一覧(JSON)を取り直す
  - 押した本人にだけ反映される(同期しない)。同じ取得先のモニターはまとめて更新される
  - 連打防止のため、押した後 10 秒は押せない
  - 画像は取り直さない(表示中の画像はそのまま使う)
- アイコンは Material Symbols (Apache License 2.0, `UI/MaterialSymbols_LICENSE.txt`)

### テーマ(表示色)

- `EterpixTheme`(テンプレートに含まれる。`Prefabs/EterpixTheme.prefab`)の Inspector で設定する
  - 切替モード: `切替あり`(モニターの切替ボタンで 黒 → 白 →(カスタム)→ 黒)/ `固定: 黒` / `固定: 白` / `固定: カスタム`
  - 切り替えは押した本人にだけ反映され、ワールドごとに保存される
  - `カスタム` の色は自由に入力できる
- モニターの `eterpix_monitor_theme` で `直接編集` を ON にすると、そのモニターはテーマの対象外になる(各 Image / TMP の色を直接編集する)

### 注意

- 次のオブジェクト名は変更しないこと (スクリプトが名前で探す)
  - `EterpixDownloaderV2` / `EterpixPortal` / `EterpixTheme`
  - (任意) ログを出したい場合は `eterpix_debug` を付けた `EterpixDebug` という名前の GameObject を置く
- モニターの表示範囲トリガー(子の `ViewRange`)は現在ビルドで削除される設定(EditorOnly)のため、実機では常に画像を読み込む(既知の不具合)
- `EterpixTheme` はワールドに1つだけ有効になる(テンプレートを複数置いた場合、最初に見つかったものの設定が使われ、他は無効になる)
- UdonBehaviour を別の GameObject へ移動・追加・削除すると、ビルドが
  `Failed to assign network IDs` で失敗することがある。
  VRChat SDK の Network ID ユーティリティで競合を解消すること

## 旧バージョンからの更新

テンプレート内のモニターは `EterpixMonitor_Stack` に置き換わった。シーン上でモニターの位置や offset を変更していた場合、更新後にテンプレートの値へ戻るので、設定し直すか、旧デザインの `Prefabs/Legacy/EterpixMonitorUI.prefab` を使う。更新後は VRChat SDK の Network ID ユーティリティでシーンの Network ID を整理してから保存する。

## フォルダ構成

```
EterPix/
├─ Etp_テンプレート.prefab   … 一式 (Downloader / Requester + モニター4台 / Portal)
├─ Prefabs/                 … 個別の prefab(Legacy/ に旧モニター)
├─ Scripts/                 … UdonSharp スクリプト
├─ Editor/                  … URL 同期などのエディタ拡張
├─ UI/                      … 読み込み中の明滅シェーダー等
├─ Fonts/                   … NotoSansJP-Bold (SIL Open Font License 1.1, OFL.txt)
└─ テンプレ_シーン/          … 設置例のシーン
```

## ライセンス

- `Fonts/NotoSansJP-Bold.ttf` とその SDF は SIL Open Font License 1.1 (`Fonts/OFL.txt`)
- `UI/icon_reload.png` は Material Symbols の `refresh` を PNG にしたもの。Apache License 2.0 (`UI/MaterialSymbols_LICENSE.txt`)
