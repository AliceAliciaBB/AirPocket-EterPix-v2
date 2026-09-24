# etp_photo_vew 構成メモ

`etp_photo_vew.prefab` のオブジェクト階層（順序どおり）と、各オブジェクト・スクリプトの役割をまとめる。

## オブジェクト階層

```
etp_photo_vew（ルート）
└─ Canvas（World Space Canvas。ローカル回転 -90°でクリップ全体の向きを決める）
   └─ Button（Buttonコンポーネント。BoxCollider(Trigger)あり）
      ├─ background（Image。前面の背景板）
      │  └─ Image（RawImage。写真テクスチャの表示先）
      └─ Canvas_Back（World Space Canvas。ローカルZ回転90°で背面に配置）
         └─ Caption_background（Image。背面の背景板）
            └─ Caption（TMP_Text。撮影情報キャプションの表示先）

#新バージョン(仮定 実装しない)
クリップprefab(別prefab)
└─ etp_photo_vew（ルート）<- ここでグラブ
   └─ Canvas（World Space Canvas。ローカル回転 -90°でクリップ全体の向きを決める）　<-この時点で16:9にする　ここを回転させる
      └─ Button（Buttonコンポーネント。BoxCollider(Trigger)あり）
         ├─ background（Image。前面の背景板）
         │  └─ Image（RawImage。写真テクスチャの表示先）
         └─ Canvas_Back（World Space Canvas。ローカルZ回転90°で背面に配置）
            └─ Caption_background（Image。背面の背景板）
               └─ Caption（TMP_Text。撮影情報キャプションの表示先）

```

## ルート（etp_photo_vew）のコンポーネントと役割

| コンポーネント | 役割 |
|---|---|
| Transform | scale 0.00175 でワールドクリップサイズに縮小配置 |
| `eterpix_photo_vew`（UdonSharp） | このオブジェクトの中核スクリプト。下記「スクリプト機能」参照 |
| BoxCollider（IsTrigger） | VRC Pickup用の掴み判定 |
| Rigidbody（Kinematic） | VRC Pickupの物理挙動に必要 |
| VRC_Pickup | `pickupable=1`, `UseText="ポータルを表示"`, `proximity=0.2`。掴んでuse（トリガー）すると`OnPickupUseDown`が発火 |
| VRCObjectSync | `AllowCollisionOwnershipTransfer=1`。掴んだ際の位置同期・所有権移譲 |

## 子オブジェクトの役割

- **Canvas**：写真の表と裏をまとめて回転させるための土台。World Space Canvasで、`rotationTargets`（スクリプト側フィールド）に登録された `Button` の RectTransform を img_rotationに応じて回転させる。
- **Button**：クリック（VRのレイキャストクリック）で `OnDesPortalViewClicked` を `SendCustomEvent` 経由で呼ぶ。BoxColliderはこのクリック判定用。
- **background / Image**：写真テクスチャ表示面（前面）。`Image`（RawImage）フィールドにアサインされ、`ApplyTexture()` でテクスチャが差し込まれる。UVRectでコラージュ内の該当セルを切り出す。
- **Canvas_Back**：写真の裏面。Buttonの子として配置され、Buttonと一緒に回転する（＝クリップの表裏で同じ向きに追従）。
- **Caption_background**：裏面の背景板（キャプション表示の下地）。
- **Caption**：`captionText` フィールドにアサインされたTMP_Text。説明文・撮影者名・ワールド名を改行区切りで表示。

## スクリプト機能（eterpix_photo_vew.cs）

| メソッド | 呼び出し元 | 役割 |
|---|---|---|
| `Start()` | Unity | 初期回転・サイズを記録し、非表示状態で開始 |
| `SetParent(eterpix_photo_get)` | 親（eterpix_photo_get.Start） | 親の参照を能動的に受け取る（非アクティブ子はGetComponentInParentが不安定なため） |
| `UpdateItemInfo(index, collageId, imgPos, imgRotation, description, userName, worldName)` | 親（eterpix_photo_get.DisplayItems） | 写真の回転・UV位置・キャプションを更新し、テクスチャをTextureManagerへリクエスト、表示をON |
| `UpdateCaption(...)` | 内部（UpdateItemInfoから） | 説明文・撮影者・ワールド名を組み立ててCaptionへ表示 |
| `ApplyTexture(Texture2D)` | TextureManager（コールバック） | 取得したテクスチャをImageに適用 |
| `OnDesPortalViewClicked()` | Buttonの OnClick、および `OnPickupUseDown()` | 親の `des_portal_view(index)` を呼び、説明文・ワールド情報・ポータル操作を親に委譲 |
| `OnPickupUseDown()`（VRC_Pickupイベント） | VRChat（掴んでUse/トリガー時） | `OnDesPortalViewClicked()` を呼び、クリックと同じ挙動にする |
| `SetEmptyItemInfo()` | 親（データなし時） | 状態をクリアして非表示 |
| `ResetItem()` | 親（再取得前のリセット時） | 見た目・状態を初期値に戻して非表示 |
