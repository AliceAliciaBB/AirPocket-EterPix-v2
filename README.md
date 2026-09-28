# AirPocket-EterPix

EterPix(https://eterpix.com/web/home)の投稿写真を VRChat ワールド内に表示するギミックの Unity プロジェクト。
サーバーから投稿一覧の JSON と、写真6枚を1枚にまとめたコラージュ画像を取得し、写真・キャプション・ポータルとして表示する。

## 必要なもの
- Unity 2022.3.22f1(`ProjectSettings/ProjectVersion.txt`)
- VRChat Creator Companion (VCC)

## セットアップ
1. このリポジトリをクローンする。
2. VCC の「Add Existing Project」でクローンしたフォルダを追加する。
3. VCC でプロジェクトを開くと、`Packages/vpm-manifest.json` に書かれたパッケージが自動で復元される。
   - `com.vrchat.worlds` / `com.vrchat.base`
   - `net.ureishi.qvpen`
   - `net.kwxxw.yama-stream`
4. 設置例のシーン `Assets/EterPix/テンプレ_シーン/` を開く。

## 使い方
- ギミック本体(v2)は `Assets/EterPix/` にある。設置方法・モニターの機能(再読み込みボタン・テーマ)は [Assets/EterPix/README.md](Assets/EterPix/README.md) を参照。
- 旧版(v1)のスクリプトと設計メモは `Assets/EterPix_dev/code/` にある。構成と注意点は [Assets/EterPix_dev/code/CLAUDE.md](Assets/EterPix_dev/code/CLAUDE.md) を参照。
- JSON の取得先 URL は、中位スクリプト(`eterpix_middle_nav` / `eterpix_middle_ring`)の `requestUrl` に設定する。
  Play 開始時に、画像 URL の一覧(`vrcurllist`)が `requestUrl` をもとに自動生成される。
  v2 (`Assets/EterPix`) では `EterpixRequester` の「取得先」ドロップダウンで URL を選び、画像 URL は URL を変えた瞬間に自動で反映される。
  メニューの `ali/eterpix/Sync URL Lists from requestUrl (自動で反映されます・触らないでください)` は念のための手動作り直し用。
- 作り直し(v2)の仕様は [Assets/EterPix_dev/code/リファクタリング案.md](Assets/EterPix_dev/code/リファクタリング案.md) にある。
- モニターの prefab(`EterpixMonitor_Stack` / `EterpixMonitor_Split` / `EterpixTheme`)は **prefab が正**。
  位置・色・ボタンなどの変更は prefab を直接編集する。
  最初はエディタ拡張 `Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs` で生成したが、現在は凍結中
  (作り直すと手で調整した値がすべて消えるため、作り直しメニューは外してある)。
  - `ali/eterpix/dev/Build Monitor Preview Scene` / `Close Monitor Preview Scene`: 見た目確認用のプレビューシーン(Stack / Split × 5状態)を今のシーンに追加で開く/閉じる
  - `ali/eterpix/dev/Migrate Template To Stack`: テンプレート内の旧モニターを Stack モニターへ置き換え、`EterpixTheme` を追加する(位置・offset 等は引き継ぐ)
- BOOTH 配布版(unitypackage と zip)の作成と商品説明の更新は、Claude Code で `/booth-release` を実行する。
  手順は [.claude/skills/booth-release/SKILL.md](.claude/skills/booth-release/SKILL.md) を参照。出力先は `V:\Etp-vrc-gimmick\`。

## リポジトリに含めないもの
- `Library/` などの Unity 自動生成フォルダ
- `Packages/` 配下の VPM パッケージ本体(VCC で復元する)
- `Assets/アセット(同期しない)/`(購入アセットなど、再配布できないもの)
- `Assets/Tesca/`(BOOTH_Poster。外部アセットのため各自で導入する)
- `ClientSimStorage/`(ClientSim のローカル再生データ)
