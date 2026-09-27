# EterpixMonitorUI 既存要素一覧

対象: `Assets/EterPix/Prefabs/EterpixMonitorUI.prefab` (UIリメイク前の現状調査, 2026-09-27)

## ルート

| 要素 | 内容 |
|---|---|
| **EterpixMonitorUI** | World Space Canvas 560×716.5。`eterpix_monitor`(U#), GraphicRaycaster, VRC UiShape |
| ├ **portal** | ポータル出現位置 (pos -233, -548)。UIではなく `portalSpawnPoint` 用Transform |
| └ **collider** (非アクティブ) | BoxCollider + `eterpix_monitor_trigger`。視聴範囲判定用 (UIではない) |

## 階層 (PanelBorder 以下)

```
PanelBorder            枠線 (角丸sprite, 白 α0.12, 560×716.5)
└ Panel                背景 (#1A1A1A, 1px内側) / VerticalLayoutGroup + ContentSizeFitter
  ├ Header             高さ56, #222222 / HorizontalLayoutGroup
  │ ├ Logo             TMP "EterPix-beta" 22pt 白
  │ └ InformationButton 36×36 角丸 白α0.1 → SendCustomEvent("ToggleInformationWindow")
  │   └ Label          TMP "i" 18pt
  ├ Figure             526×394.5 (4:3枠) / LayoutElement
  │ └ FigureInner      AspectRatioFitter, 黒背景
  │   └ PostImage      RawImage 494×278 (未読込時は黒、読込後は白に戻す)
  ├ Paging             高さ64 / HorizontalLayoutGroup
  │ ├ Hairline_bottom  1px区切り線 (白 α0.12)
  │ ├ PrevButton       40×40 角丸 白α0.1 → SendCustomEvent("PagePrev")
  │ │ └ Label          TMP "<" 16pt
  │ ├ PageLabel        TMP "1/12" 16pt, 200×40
  │ └ NextButton       40×40 → SendCustomEvent("PageNext")
  │   └ Label          TMP ">" 16pt
  ├ Context            高さ200 / HorizontalLayoutGroup
  │ ├ WorldContext     VerticalLayoutGroup (woid無しなら非表示)
  │ │ ├ WorldName          TMP "ワールド名" 14pt, 高さ45
  │ │ ├ WorldDescription   TMP "ワールドの説明文がここに入ります。" 12pt, 高さ55
  │ │ └ OpenPortalButton   140×40 角丸 白 → SendCustomEvent("OpenPortal")
  │ │   └ Label            TMP "ポータルを開く" 14pt
  │ └ PostContext      VerticalLayoutGroup
  │   ├ PostUserName       TMP "投稿者名" 14pt, 高さ25
  │   └ PostDescription    TMP "投稿の説明文がここに入ります。#タグ" 12pt, 高さ120
  └ InformationWindow  (初期非アクティブ) Header下を全面覆う #1A1A1A / VerticalLayoutGroup
    ├ InfoHeading      TMP "EterPixについて" 18pt
    └ InfoBody         TMP 14pt (説明文、下記)
```

## スクリプト (`eterpix_monitor.cs`) との紐付け

| SerializeField | 対応要素 | スクリプト側の動作 |
|---|---|---|
| `image` (RawImage) | PostImage | テクスチャ設定、色を白へ。回転・UVRect適用 |
| `rotationTargetObjects` | (回転対象) | coro奇数で縦画像として回転 |
| `figureAspectFitter` | FigureInner | 16:9 / 9:16 切替 |
| `prevButton` / `nextButton` | PrevButton / NextButton | 端で `interactable=false` |
| `pageLabel` | PageLabel | `"{現在}/{全体}"`、データ無しで "0/0" |
| `userNameText` | PostUserName | usna |
| `descriptionText` | PostDescription | pode |
| `worldContextRoot` | WorldContext | woid有無で表示切替 |
| `worldNameText` / `worldDescriptionText` | WorldName / WorldDescription | requesterで名前・説明を解決 |
| `openPortalButton` | OpenPortalButton | `OpenPortal()` |
| `portal` / `portalSpawnPoint` | EterpixPortal / portal | ポータル生成 |
| `informationButton` / `informationWindowRoot` | InformationButton / InformationWindow | `ToggleInformationWindow()` |

## InfoBody 本文

```
EterPixは、VRChatで撮影した写真を共有できるSNSです。

このモニターについて
このモニターでは、EterPixに投稿された公開写真を閲覧できます。
写真を見ながら、ページを切り替えたり、写真が撮影されたワールドへ移動するためのポータルを開いたりできます。

できること
★写真を見る
EterPixに投稿された公開写真を閲覧できます。
★ページを切り替える
ページ送りボタンで、他の投稿を見ることができます。
★ワールドへ行く
写真にワールド情報が登録されている場合、その写真が撮影されたワールドへのポータルを開くことができます。
★写真を投稿する
EterPixへの写真投稿は、WEBから行えます。

投稿ページ
https://api.eterpix.uk/app

VRChatで撮影した写真を、ぜひEterPixに投稿してみてください。
```

## リメイク時の注意点

- 共通の角丸sprite: guid `ae0738faaa4727b46a78e01a6daf5f2a` (ボタン類・PanelBorder)
- 配色: 背景 #1A1A1A / ヘッダー #222222 / 枠線・区切り線 白 α0.12 の3色構成
- ボタンのOnClickはすべて `UdonBehaviour.SendCustomEvent(文字列)`。イベント名 `PagePrev` / `PageNext` / `OpenPortal` / `ToggleInformationWindow` はリメイク後も維持が必要
- レイアウトは Vertical/HorizontalLayoutGroup + LayoutElement 主体のため、子要素の pos/size は多くが0
- InformationWindow は Header下を覆う作り (offset -28, size -56)
