# eterpix 詳細表示(1件)まわりの構成

対象は `eterpix_photo_vew` とその新規親のみ。`eterpix_item` / `eterpix_json_get` は別系統として今回のスコープ外。

## 全体の流れ

```
eterpix_get_api (既存) ─(DataList jsonArray / DataDictionary worldData)─> eterpix_photo_get(新規親)
                                                                              │
                                                              startIndexから photoViews[]分だけ
                                                        各 eterpix_photo_vew に index と collage_id だけを渡す
                                                        (DataDictionary本体は親が保持したまま渡さない)
                                                                              ▼
                                                          eterpix_photo_vew ×40程度(1個=1枠)
                                                                              │
                                                            collage_id で TextureManager(既存) に直接テクスチャ要求

ボタン押下時:
eterpix_photo_vew ──(自分のindexを渡してボタンから呼び出す)──> eterpix_photo_get.des_portal_view(index)
                                                                              │
                                                    親（eterpix_photo_get）自身が持つ
                                                    des表示用テキストUI・ポータルを更新
```

## 各スクリプトの役割

### `eterpix_get_api.cs` (既存・要修正)
- サーバーから取得したJSONをパースし、`DataList jsonArray` / `DataDictionary worldData` として保持する。
- `TextureManager` への参照も保有する。
- **`eterpix_photo_gets[]` 配列と `collme(eterpix_photo_get item)` オーバーロードを追加**。
  新着JSON取得完了時、既存の `eterpix_json_gets[i].GetItems()` 呼び出しと並んで
  `eterpix_photo_gets[i].DisplayItems()` を呼び、登録済みの `eterpix_photo_get` 全てに通知する。

### `TextureManager.cs` (既存・要修正)
- コラージュ画像のダウンロード・キャッシュを一元管理。
- `RequestTexture(setIndex, caller)` を受けて、揃ったら呼び出し元へ直接 `ApplyTexture()` でコールバックする。
- **`RequestTexture(int setIndex, eterpix_photo_vew item)` オーバーロードと、対応する
  `waitingPhotoViews[][]` 待機リストを追加**(既存の `eterpix_item` 用ロジックとは別配列で並行管理)。
  `NotifyWaitingItems` / `OnImageLoadSuccess` / `OnImageLoadError` それぞれで
  `eterpix_photo_vew` 側の待機リストにも通知するよう対応。

### `eterpix_photo_get.cs` (新規作成済み・`eterpix_json_get.cs`を複製して調整)
- `eterpix_photo_vew[] photoViews` として、複数(40程度を想定)の `eterpix_photo_vew` を保持する。
  Inspectorで未設定の場合は `Start()` で `GetComponentsInChildren<eterpix_photo_vew>(true)` により
  子階層から自動収集する。
- `DisplayItems()`: `startIndex` から `photoViews` の要素数分だけ `eterpix_get_api.jsonArray` を切り出し、
  各 `eterpix_photo_vew[i]` に `UpdateItemInfo(index, collageId)` で **index と collage_id だけ**を渡す。
  `DataDictionary` 本体は親(`eterpix_photo_get`)が保持したままで、子には渡さない
  (子は表示テキストUIを持たないため、必要な値だけを絞って渡す)。
- 複製元の `eterpix_json_get` とは別インスタンス・別系統として扱う(複製元は同じでも役割は別)。
- `photoViews` を収集した直後、各 `eterpix_photo_vew[i].SetParent(this)` を呼び、
  子に自分自身を教える(子側の `GetComponentInParent` は、GameObjectが非アクティブな間は
  Udon実行時に信頼できないため、親から能動的に教える方式にしている)。
- **`Start()` で `apiInstance.collme(this)` を呼び自己登録する**(`eterpix_get_api` に追加した
  `collme(eterpix_photo_get item)` オーバーロード経由)。新着JSON取得完了時、
  `eterpix_get_api` から `DisplayItems()` を呼んでもらえる。
- **`des_portal_view(index)`(実装済み)**: `eterpix_photo_vew` のボタンから index 付きで呼ばれる。
  指定indexのデータをもとに、**親自身が持つ**des表示用テキストUI(`descriptionText` /
  `userNameText` / `worldNameText` / `worldDescriptionText`)と、**親自身が持つ**
  `VRCPortalMarker` を更新する。キャプション表示・ワールド名解決・ポータル操作はすべてここに集約する。

### `eterpix_photo_vew.cs` (実装済み・責務を再定義)
機能は以下の2つ＋ボタン起点の1呼び出しのみ。**それ以外は行わない**:

0. `SetParent(eterpix_photo_get parent)` を親から呼ばれて `parentInstance` を保持する
   (Inspectorでの手動指定も可能。設定済みなら上書きしない想定)。
1. `UpdateItemInfo(int index, int collageId)` で親から index と collage_id を受け取って保持する
   (`DataDictionary` 本体は受け取らない)。
2. 画像を表示する(`_collageId` を使い `parentInstance.GetTextureManager()` 経由で
   `TextureManager.RequestTexture(collageId, this)` を呼び、`ApplyTexture()` でテクスチャを反映する)。
3. `OnDesPortalViewClicked()`(ボタンから呼ばれる)で **親**の `des_portal_view(_index)` を呼ぶ。
   - キャプション表示・ワールド名解決・ポータル操作(`VRCPortalMarker`等)は本スクリプトでは行わない。
     すべて親側の `des_portal_view(index)` に委譲する。

## コラージュ画像の切り出し・回転表示(実装済み)

`eterpix_photo_vew` に、2x3コラージュ画像から自分の1枚だけを16:9で正しく切り出し、
クリップの位置を軸に回転させる機能を追加する。

1. **データ受け渡しの拡張**
   - `UpdateItemInfo` のシグネチャを `UpdateItemInfo(int index, int collageId, int imgPos, int imgRotation)`
     に拡張する。
   - `eterpix_photo_get.DisplayItems()` 側で `img_pos` / `img_rotation` を
     `data.TryGetValue(...)` でパースし(Double/String両対応、`eterpix_item.cs`と同じロジック)、
     子に渡す。`DataDictionary` 本体を渡さない方針は維持する。

2. **UVクロップ**
   - `eterpix_item.cs` と同じ計算式を移植する:
     `cellW=0.5f`, `cellH=1/3f`, `col=(imgPos-1)%2`, `row=(imgPos-1)/2`,
     `uvY = 1 - (row+1) * cellH`
   - `Image.uvRect = new Rect(uvX, uvY, cellW, cellH)`

3. **16:9表示**
   - コラージュ全体が32:27比率(16×2列 : 9×3行)で作られている前提のため、
     UVクロップのみで各セルは自然に16:9になる想定。
   - `eterpix_photo_vew` の `RawImage` の RectTransform 自体も16:9固定にしておく
     (Prefab側の確認・調整が必要、コード側の対応は不要)。

4. **回転とクリップ位置(実装済み・2回修正)**
   - 実際のPrefab(`Assets/アリシリア/etp_photo_vew.prefab`)をMCPで確認したところ、階層は
     `etp_photo_vew → Canvas(100x100, pivot (0.5, 0.95)) → Image(RawImage, 100x56.25=16:9, pivot (0.5,1))`。
     `Canvas`のpivotが既にクリップ位置(=クリップが写真に少し食い込んだ位置)に設定済みだった。
   - **1回目の修正(撤回済み)**: `Image`ではなく親`Canvas`を回転させる方式にしたが、
     クリップの留め位置(Canvasのpivot)が写真の中心から外れているため、90°/270°回転時に
     写真の重心がクリップ直下から横にずれてしまう(物理的な留め具としては正しい挙動だが、
     見た目として望ましくないとフィードバックを受けた)。
   - **2回目の修正(現行)**: 回転はやはり`Image`自身の`transform.localRotation`に適用するが、
     回転で生じる重心のズレを打ち消すよう`Image.rectTransform.anchoredPosition`を補正する。
     `Start()`で元の`anchoredPosition`(`_baseAnchoredPosition`)と`sizeDelta`(`_baseSizeDelta`、
     W/H)を保持し、`imgRotation`(0/1/2/3)ごとに以下のシフト量を`_baseAnchoredPosition`に
     加算する(回転行列から導出、90°/270°では縦横が入れ替わる分を補正):
     - 0: `(0, 0)`
     - 1: `(H/2, -W/2)`
     - 2: `(0, -H)`
     - 3: `(-H/2, -W/2)`
     これにより、回転後も常にクリップ直下・水平中心を保ったまま、中身(縦横比)だけが
     正しく切り替わる。`ResetItem()`でも`_baseAnchoredPosition`・回転ゼロに戻す。

## 前提条件

- `eterpix_photo_get` をアタッチしたGameObjectが、Hierarchy上で40個の `eterpix_photo_vew` の
  **親(祖先)** である必要がある(`GetComponentsInChildren` / `GetComponentInParent` は
  Transform階層を辿るAPIのため)。この配置が崩れる場合は、各Inspectorで
  `photoViews[]` / `parentInstance` を手動設定すればそちらが優先される。

## 未確定事項

- `index` が必要な理由(後述予定)
- ボタンの `OnDesPortalViewClicked()` への接続はまだ未実施
- 16:9固定・pivot `(0.5, 0.95)` はPrefab側で既に設定済みであることをMCPで確認済み
