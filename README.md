# AirPocket-EterPix

EterPix(https://www.eterpix.uk)の投稿写真を VRChat ワールド内に表示するギミックの Unity プロジェクト。
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
4. `Assets/Scenes` のシーンを開く。

## 使い方
- ギミック本体は `Assets/EterPix/` にある。スクリプトの構成と注意点は [Assets/EterPix/code/CLAUDE.md](Assets/EterPix/code/CLAUDE.md) を参照。
- JSON の取得先 URL は、中位スクリプト(`eterpix_middle_nav` / `eterpix_middle_ring`)の `requestUrl` に設定する。
  Play 開始時に、画像 URL の一覧(`vrcurllist`)が `requestUrl` をもとに自動生成される。
  手動で生成し直す場合は、メニューの `ali/eterpix/Sync URL Lists from requestUrl` を使う。
- 作り直し(v2)の仕様は [Assets/EterPix/code/リファクタリング案.md](Assets/EterPix/code/リファクタリング案.md) にある。

## リポジトリに含めないもの
- `Library/` などの Unity 自動生成フォルダ
- `Packages/` 配下の VPM パッケージ本体(VCC で復元する)
- `Assets/アセット(同期しない)/`(購入アセットなど、再配布できないもの)
