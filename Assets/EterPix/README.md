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
4. モニターを増やす場合は `Prefabs/EterpixMonitorUI.prefab` を `EterpixRequester` の子に置く

### 注意

- 次のオブジェクト名は変更しないこと (スクリプトが名前で探す)
  - `EterpixDownloaderV2` / `EterpixPortal`
  - (任意) ログを出したい場合は `eterpix_debug` を付けた `EterpixDebug` という名前の GameObject を置く
- モニターの表示範囲は子の `collider` (トリガー) で決まる。大きさはこの BoxCollider で調整する
- UdonBehaviour を別の GameObject へ移動・追加・削除すると、ビルドが
  `Failed to assign network IDs` で失敗することがある。
  VRChat SDK の Network ID ユーティリティで競合を解消すること

## フォルダ構成

```
EterPix/
├─ Etp_テンプレート.prefab   … 一式 (Downloader / Requester + モニター4台 / Portal)
├─ Prefabs/                 … 個別の prefab
├─ Scripts/                 … UdonSharp スクリプト
├─ Editor/                  … URL 同期などのエディタ拡張
├─ UI/                      … UI 画像
├─ Fonts/                   … NotoSansJP-Bold (SIL Open Font License 1.1, OFL.txt)
└─ テンプレ_シーン/          … 設置例のシーン
```

## ライセンス

- `Fonts/NotoSansJP-Bold.ttf` とその SDF は SIL Open Font License 1.1 (`Fonts/OFL.txt`)
