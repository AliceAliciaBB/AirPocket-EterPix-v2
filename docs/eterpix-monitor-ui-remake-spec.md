# EterPix モニターUI リメイク仕様

作成: 2026-09-27 (grill-me による合意事項)
現状調査: [eterpix-monitor-ui-elements.md](eterpix-monitor-ui-elements.md)
参考: `Assets/Tesca/BOOTH_Poster` / `C:\git\claude.global\知識\Unity\Tesca-BOOTH_Poster.md`

## 1. 目的

- EterpixMonitorUI の見た目を作り直し、ワールド作者が好みで選べる **デザイン差分を複数** 提供する
- 通信エラー(信頼されていないURL未許可 / サーバー停止)をユーザーに分かる形で表示する
- 元の階層を踏襲する必要はない(きれいなデザイン差分を優先)

## 2. 必須要件

1. ルートは Canvas オブジェクトではないこと
2. エラーハンドリング
   - 「信頼されていないURLの許可」OFFによるアクセス不能
   - サーバー停止によるアクセス不能
   - offset 番目の投稿が無い場合は `offset % 全体` で対処
3. UI要素はすべて長方形で構成する(角丸不可)

## 3. 構造

```
EterpixMonitor_Stack / _Split   空GameObject, scale 0.00125 (=1px 1.25mm)
│                                eterpix_monitor, eterpix_monitor_theme
├ Screen                         Canvas(World Space) + GraphicRaycaster + VRC UiShape, scale 1, 幅560
├ PortalSpawnPoint               旧 portal
└ ViewRange                      旧 collider (BoxCollider + eterpix_monitor_trigger)
```

- 大きさはルートの scale だけで管理する。子は Canvas を含めすべて scale 1
- ポータルは `SetParent(worldPositionStays=true)` のためルート scale の影響を受けない(現状と同じ)
- 差分: `Prefabs/EterpixMonitor_Stack.prefab`(B-1 縦積み) / `Prefabs/EterpixMonitor_Split.prefab`(B-2 横並び)
  - 階層が異なるため Prefab Variant にはしない。スクリプトは共通の `eterpix_monitor`
  - SerializeField はすべて null 許容。イベント名 `PagePrev` / `PageNext` / `OpenPortal` / `ToggleInformationWindow` は共通
- 旧 `EterpixMonitorUI.prefab` は `Prefabs/Legacy/` へ移動(GUID維持、削除しない)。旧 prefab も従来どおり動くこと
- `Etp_テンプレート.prefab` の入れ子は Stack に差し替え

## 4. デザイン原則

- 要素はすべて長方形。線(枠線・区切り線)は使わない
- 情報のまとまりは **余白** で区切る。面の色を持つのはボタンだけ
- 外側の余白は 16(仮)で全要素統一。写真もこの余白を突き抜けない
- 余白の単位は 8。ブロック間 16
- 文字は不透明の1色のみ(薄い文字なし)。強弱は大きさと太さで付ける
- 写真を隠す要素を写真に重ねない(見えない判定領域のみ重ねてよい)

## 5. レイアウト

### 共通ヘッダー(高さ40)
```
│ EterPix-Beta        3 / 12        [◧] [i] │
```
- ロゴ: TMP `EterPix-Beta` 16 Bold 左寄せ(β はフォントに無いため使わない)
- ページ番号: 中央、16 Regular。Loading/エラー時は非表示
- `[◧]` テーマ切替 / `[i]` 情報: 各 40×40、ボタン色の面、間隔 8。切替ボタン非表示時は ⓘ が右端に寄る
- ⓘ ボタンの中身: `IconInfo`(TMP "i") と `IconClose`(細い長方形2枚を±45°回転したX)。情報表示中は X

### 写真エリア
```
│ ┌──────────────────────────┐ │
│<│░░░░       PHOTO    ░░░░│>│   528×396 (4:3)
│ └──────────────────────────┘ │
```
- 写真枠 528×396、AspectRatioFitter で 16:9 / 9:16 に縮める。帯部分は背景色(枠を見せない)
- `<` `>` は左右の余白の中央に置く TMP テキスト(文字色)。記号自体もボタン
- 写真の左右35%ずつに見えない判定領域(Image α0, raycastTarget on, Button Transition None)。中央30%は判定なし
- ページ送りはループ。前後ボタンは常に有効
- 画像読み込み中: 写真の大きさ(アスペクト比に合わせた長方形)を明滅マテリアルで表示

### B-1 Stack(約 560×712)
```
│ ヘッダー                               │
│ 写真                                   │
│ 投稿者名                               │  16 Bold, 1行
│ 投稿の説明文 (3行まで)                  │  13
│ ワールド名              [ポータルを開く] │  16 Bold 1行 / ボタン 140×36
│ ワールドの説明 (2行まで)                │  13
```

### B-2 Split(約 560×620)
```
│ ヘッダー                               │
│ 写真                                   │
│ 投稿者名            │ ワールド名        │
│ 説明文 (4行まで)     │ 説明 (2行まで)    │
│                     │ [ポータルを開く]  │  列幅いっぱい, 高さ36
   投稿 60%               ワールド 40%
```
- ワールド情報が無い(woid 空)場合は右列ごと非表示にし、投稿が全幅に広がる
- B-1 でもワールド欄ごと非表示

### テキスト
- はみ出しは TMP Overflow `Ellipsis` + 行数上限。高さは固定(ContentSizeFitter で伸縮させない)
- 投稿者名とワールド名は同じ 16 Bold

### ポータルボタン
- 面 `#9D6FFF`、文字 `#141414` 16 SemiBold「ポータルを開く」(コントラスト 5.4:1)
- 押すたびに現在の投稿のワールドでポータルを出し直す(現状の `OpenPortal()` と同じ)

## 6. 状態とエラー

| 状態 | 条件 | 表示 |
|---|---|---|
| Loading | 初回取得が未完了 | 写真枠内に「読み込み中…」 |
| UntrustedUrl | 取得失敗 かつ Error に `Not trusted url hit` を含む | 写真枠内に案内文 |
| ServerError | 上記以外の取得失敗 / JSON不正 / `post` キー無し | 写真枠内にエラー文 |
| Empty | 取得成功だが投稿0件 | 「表示できる投稿がありません」 |
| Normal | 投稿1件以上 | 通常表示 |

- Normal 以外では投稿欄・ワールド欄・ページ番号・`<` `>`・判定領域を非表示。ヘッダーは常に表示
- **一度成功した後の失敗ではエラーを出さず、前回データを表示し続ける**
- 再試行は downloader の定期取得(`intervalMinutes`)に任せる。再試行ボタンは無し
- 画像1枚ごとの読み込み失敗は状態に含めない(downloader の既存自動再試行)

### 伝達経路
- `eterpix_downloader` にフィードごとの `_feedStatus[feedIndex]`(Loading / Ok / UntrustedUrl / ServerError)を持たせる
- 成功・失敗どちらでも `NotifyFeedRequesters` → requester `OnJsonUpdated` → monitor `OnFeedUpdated`
- monitor は `requester.FeedStatus` と `HasEverSucceeded`(`_feedPosts[feedIndex] != null`)で状態を決める

### 文言
**UntrustedUrl**
```
「信頼されていないURL」が許可されていません

設定 > 快適性とセーフティ >
「信頼されていないURLを許可」をONにしてください
ONにすると、しばらくして表示されます
(表示されない場合はワールドに入り直してください)
```
**ServerError**
```
サーバーに接続できませんでした
メンテナンス中の可能性があります
最新情報は X @_alicilia をご確認ください

数分後に自動で再接続します
```

## 7. offset / ページ

- `_syncedPageIndex` は生の値で保持し、表示時に毎回 `((i % n) + n) % n` で正規化(n = VisibleCount)
- 現状の `Mathf.Clamp` を置き換える。負の offset も同じ式で扱う
- `MovePage` はループのまま。`interactable` による端での無効化はやめる

## 8. テーマ

### `eterpix_theme`(ワールドに1つのシングルトン、固定名で解決)
- 配色: 黒 / 白 / カスタム
- `切替モード`: 切替あり / 固定: 黒 / 固定: 白 / 固定: カスタム
  - 切替あり: 黒 → 白 → (カスタム) → 黒。カスタムを含めるかは ON/OFF
  - 固定: 切替ボタンを全モニターで非表示
- `初期テーマ`
- 現在のテーマはローカル(非同期)。切替時に登録済みの全モニターへ適用
- 永続化: PlayerData キー `eterpix.theme`(int)。切替ありモードのみ。`OnPlayerRestored`(ローカル)で復元。無効な値なら初期テーマ
- エディタでは OnValidate で初期テーマを即時反映

### `eterpix_monitor_theme`(モニターごと)
- 役割ごとの Graphic 配列: `bg` / `tx` / `btn` / `acc` / `acctx`
- 子オブジェクト名の接頭辞(`bg_` / `tx_` / `btn_` / `acc_` / `acctx_`)からエディタのボタンで自動収集(null・空なら再収集)
- `直接編集` ON: シングルトンに登録しない。そのモニターの切替ボタンも非表示
- Button の ColorTint(normal / pressed)もテーマから設定
- 読み込み中マテリアルの2色(背景色・ボタン色)もテーマ切替時に更新

### プリセット
| 役割 | 黒 | 白 |
|---|---|---|
| 背景 `bg` | `#141414` | `#FAFAFA` |
| 文字 `tx` | `#FFFFFF` | `#141414` |
| ボタン `btn` | `#2A2A2A` | `#E6E6E6` |
| ボタン押下 | `#3A3A3A` | `#D4D4D4` |
| アクセント `acc` | `#9D6FFF` | `#9D6FFF` |
| アクセント上の文字 `acctx` | `#141414` | `#141414` |

- 色はすべて不透明(判定領域のみ α0)。カスタムの初期値は黒と同じ

## 9. インフォメーション画面

- ヘッダーより下の全体を覆う。背景はテーマの背景色
- 本文は現行 InfoBody を短く整える。★は使わず見出しは Bold のみ
- 投稿ページ URL `https://api.eterpix.uk/app` は読み取り専用 TMP_InputField(編集しても即元に戻す、Tesca 方式)
- 末尾に「最新情報は X @_alicilia」
- 高さ固定、溢れる場合は ScrollRect で縦スクロール

## 10. 読み込み中シェーダー

- EterPix 用の極小 Unlit UI シェーダー(Tesca `FadeColorSwapShader` と同方式: `sin(_Time.y * _Speed)` で2色 lerp)
- 読み込み中は PostImage にマテリアルを設定、成功時に `material = null`
- Udon の Update は使わない

## 11. 実装計画での調整(2026-09-27)

- フォントは NotoSansJP-Bold しか無いため、SemiBold / Regular の指定は使わず、強弱は大きさで付ける
- ボタン押下色はテーマで変えず、塗りを 0.75 倍に暗くする(U#から ColorBlock を書き換えるのが未検証のため)
- 読み込み中の明滅は、マテリアルの2色ではなく「RawImage.color(ボタン色)のαを揺らす」方式。背景の上に描くので見た目は同じ
- URL欄は Tesca の「編集されたら戻す」方式ではなく `TMP_InputField.readOnly` を使う(ランタイムのコード不要)
- テーマのエディタ反映は OnValidate ではなく Inspector の変更検出で行う
- 表示範囲トリガーは旧prefabの値(EditorOnly タグ、debugIgnoreTriggerRange=true)を再現する(挙動を変えないため。BUGSに記録)
- prefab はコード(開発用ビルダー)で生成する

## 12. Tesca から取り入れる実装パターン

- `result.Error.Contains("Not trusted url hit")` による判定
- 読み取り専用 TMP_InputField
- `#if !COMPILER_UDONSHARP && UNITY_EDITOR` + `#region Editor` による U# 内エディタコード
- enum 日本語化 PropertyDrawer(`切替モード` 等)
- 配列の自動復旧(Repair)
