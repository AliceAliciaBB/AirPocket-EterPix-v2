# プロジェクト名: EterPix (AirPocket-EterPix)

## このプロジェクトについて
- 種別: Unity / VRChatワールド (UdonSharp)
- 概要: 外部サーバー(eterpix.uk)の投稿JSONと、2列×3行のコラージュ画像を取得し、ワールド内に写真・キャプション・ポータルとして表示するギミック。
- 開発環境: (未記入)
- 関連する横断メモ: @C:\git\claude.global\domains\Unity.md
- 内部実装の調査記録: `C:\git\claude.global\知識\Unity\EterPix.md` (サブシステム別に5ファイル。2026-09-23 調査)
- **リファクタリング案(v2の仕様)**: [リファクタリング案.md](リファクタリング案.md)。ダウンローダ/リクエスター/モニターの3層で作り直す(現在は `Assets/EterPix/Scripts/`)。旧系統・現新系統は完成後に削除する(2026-09-23 決定)。
- **UIテンプレート(モック)**: `D:\git\eterpix-v10\vrc_ui`
  - ワールド内でEterPixを見る画面のHTMLモック。**本体は `#root` 部分のみ**で、実際はUnityのUI要素で構成する(HTMLはモック作成用)。
  - **`vrcetepixui.html` / `vrcetepixui.css` が正**。UIを組むときはこの2ファイルを参照する。
    - 画面構成(`#root` 内): `header`(ロゴ `#logo`、インフォメーションボタン `#Information`) → `figure`(画像 `#post_img`。常に4:3の領域を確保し、縦画像もトリミングせず全体を表示) → `#paging`(`<` / `現在ページ/ページ数` / `>`、両端ではボタン無効) → `#context`(`#world_context`: ワールド名・説明・「ポータルを開く」ボタン。ワールド情報が無い場合は要素ごと無い / `#post_context`: 投稿者名・投稿説明) → `#Information_window`(ⓘで開閉するサービス説明)。
    - 配色・角丸などは css の `:root` のCSS変数で定義されている(`--canvas` `--surface` `--text` `--highlight` `--radius-md` など)。
  - `DESIGN.md`: cssを作る元にしたデザイン定義。**その後cssを修正しているため、DESIGN.mdとcssが食い違う場合はcss(とhtml)を優先する**。
  - `テスト_img/`: 表示確認用のVRChatスクリーンショット(横長 2560x1440 と縦長 1440x2560)。追加・削除したら `node vrc_ui/gen-test-images.mjs` を実行して `test_images.js` を作り直す(手で編集しない)。

## フォルダ構成 (2026-09-24 配布用に再編)
- **`Assets/EterPix/` = 配布物だけを置く**(そのまま .unitypackage にする)。開発用のファイルを置かないこと。
  - `Etp_テンプレート.prefab` / `Prefabs/`(Downloader・Requester・MonitorUI・Portal) / `Scripts/`(v2の4本 + `eterpix_debug` / `eterpix_porta_resize` / `vrcurllist`) / `Editor/`(`eterpix_url_sync`[v2のみ] / `vrcurllistEditor`) / `UI/` / `Fonts/`(NotoSansJP-Bold + SDF + OFL.txt) / `テンプレ_シーン/`
  - 配布物から `Assets/EterPix` の外(v1のクラスなど)を参照しないこと。確認は `AssetDatabase.GetDependencies("Assets/EterPix/Etp_テンプレート.prefab", true)` で、外部に `Packages/` / `SerializedUdonPrograms/` / TMPのシェーダー以外が出ないこと。
- **`Assets/EterPix_dev/` = 開発用(配布しない)**。`code/`(v1一式、FlekSit、Tekkotsu、設計メモ、このCLAUDE.md)、`Editor/eterpix_url_sync_v1.cs`(v1 nav/ringのURL同期。配布側`eterpix_url_sync.SyncOne`を呼ぶ)、`コライダーの信頼性チェック/`。

## このプロジェクト固有のルール
- **旧系統と新系統の2世代が並存している**。どちらを触っているのかを意識すること。
  | 層 | 旧系統 | 新系統 |
  |---|---|---|
  | JSON取得(上位) | `eterpix_get_api` + `eterpix_json_api` + `eterpix_update` | `eterpix_json_loader` (シングルトン) |
  | 画像取得 | `TextureManager` | `eterpix_image_loader` (シングルトン) |
  | 並べ替え(中位) | `eterpix_json_get` / `eterpix_photo_get` | `eterpix_middle_nav` / `eterpix_middle_ring` |
  | 表示(下位) | `eterpix_item` / `eterpix_photo_vew` | `eterpix_cell` + `eterpix_listener_*` |
- 新系統のシングルトンは、固定名のGameObject(`"EterpixJsonLoader"` / `"EterpixImageLoader"` / `"EterpixDebug"`)を `GameObject.Find` で探して解決する。オブジェクト名を変えないこと。
- UdonSharpの制約に合わせた書き方を続けること。
  - interfaceによる多態は使わず、呼び出し元の型ごとにオーバーロードを追加する。
  - enumはネストせず、名前空間の直下に置く。
  - `List<T>` は使わず、配列をコピーして伸ばす。
- 非アクティブな子から `GetComponentInParent` で親を探さない。親が `GetComponentsInChildren(true)` で子を集め、`SetParent(this)` で参照を渡す。
- 画像URLは実行時に作れないため、`vrcurllist` にエディタ上で事前生成する(`baseUrl/00`〜`0F`)。新系統では、Play開始前に `Assets/EterPix/Editor/eterpix_url_sync.cs`(v2 requester) と `Assets/EterPix_dev/Editor/eterpix_url_sync_v1.cs`(v1 nav/ring) が `requestUrl` から自動で同期する。旧系統のURL(`requestUrl1..3` / `TextureManager.urlList1..3`)は手動で揃える。
- 2×3コラージュのUV計算式と回転の向き(`imgRotation * -90f`)は、`eterpix_item` / `eterpix_photo_vew` / `eterpix_cell` / `eterpix_listener_monitor` で同じ式を使う。1か所だけ変えないこと。
- VRCStationに着席している間は `SetParent` を使わない(トラッキングの復帰が壊れる)。`TekkotsuLiftController` のように、位置と回転の追従を自前で行う。

## v2 (`Assets/EterPix/Scripts/` + `Prefabs/`) prefab組み立てメモ
- `EterpixDownloaderV2`という名前のGameObjectに`eterpix_downloader`を1つだけ配置する。
- `eterpix_requester`ごとに、Inspectorで`requestUrl`(JSON URL)と`urlList`(`vrcurllist`、baseUrlのみ設定・arraySizeは256に統一)を設定する。
- `eterpix_requester`の子に`eterpix_monitor`を1つ以上配置する(非アクティブでも`GetComponentsInChildren(true)`で拾われる)。
- `eterpix_monitor`のUIはvrc_uiのhtmlモック(`D:\git\eterpix-v10\vrc_ui\vrcetepixui.html`/`.css`)に合わせてUnity UIを手組みし、`image`/`prevButton`/`nextButton`/`pageLabel`/`userNameText`/`descriptionText`/`worldContextRoot`/`worldNameText`/`worldDescriptionText`/`openPortalButton`/`informationButton`/`informationWindowRoot`をInspectorで紐付ける。
- 画像を「4:3の枠に切らずに全体を収める」表示は`eterpix_monitor`のコードでは行わない。`image`の親に`AspectRatioFitter`(Fit Mode: Fit In Parent)をアタッチし、4:3のコンテナ内に収める形でEditor上で設定する。
  - 構成は `Panel`(VerticalLayoutGroup) → `Figure`(LayoutElementで4:3の高さを確保。**LayoutGroupは付けない**) → `FigureInner`(AspectRatioFitter: Fit In Parent、anchor全面stretch・pivot中央。縦横比はコードで16:9/9:16に切替) → `PostImage`。
  - `AspectRatioFitter`の直接の親にLayoutGroupがあると、Unityが「A child of a layout group should not have an Aspect Ratio Fitter」と警告し、位置をLayoutGroupが・サイズをFitterが決める競合で画像が上寄せ(下にズレた見た目)になる。Fit In Parentは縦画像も4:3枠に収めるために必要なので、Width Controls Heightには変えない。
- 写真を読み込む範囲は**`EterpixRequester`自身のトリガーBoxCollider 1つ**で判定する(モニター個別には持たない。設計: `docs/superpowers/specs/2026-09-27-requester-view-range-design.md`)。`eterpix_requester`が`OnPlayerTriggerEnter/Exit`でローカルプレイヤーの出入りを受け、配下の全モニターへ`SetInViewRange(bool)`を送る。Enterは範囲内でスポーンすると届かないことがあるため、ローカルプレイヤーの`OnPlayerJoined`の次のフレームで一度だけ、`Physics.CheckBox`(範囲の箱 × PlayerLocalレイヤー)で重なりを確かめて補う(`CheckLocalPlayerInRange`)。Playerレイヤーは他人なので含めない。モニターは`Init`/`OnEnable`で`requester.IsLocalPlayerInRange`を読み直す(通知の取りこぼし対策)。Colliderは`isTrigger=true`、レイヤーは`Ignore Raycast`(UIのレーザーを遮らない)。**`EditorOnly`タグを付けない**(ビルドで削除され、範囲判定が効かなくなる)。大きさはワールド制作者が任意に調整する。
- UdonBehaviourを別のGameObjectへ移動・追加・削除すると、VRCWorldのNetwork ID表と食い違い、ビルドが`Failed to assign network IDs`で失敗する。VRChat SDKのNetwork IDユーティリティで競合を解消するか、実体の無いエントリを削除する。
- ポータルは旧系統と同じく**共通ポータルを1つだけ置いて呼び寄せる**方式。シーンに`Assets/EterPix/Prefabs/EterpixPortal.prefab`(`eterpix_porta_resize`で`VRCPortalMarker`を包んだもの)を1つ配置し、名前は`EterpixPortal`のままにする(`eterpix_monitor.portal`が未設定なら`GameObject.Find("EterpixPortal")`で解決する)。「ポータルを開く」ボタン(`OpenPortal()`)で`SetParentObject(portalSpawnPoint, world_vrc_id)`が呼ばれ、ポータルがそのモニターの`portalSpawnPoint`(prefab内の`portal`)へ移動する。`portal_clause_distance`(prefabでは10m)離れると自動で非表示になる。大きさは`siz_value`(prefabでは0.1)。
- **VRChatのWorld Space UIはCanvasに`GraphicRaycaster`と`VRC.SDK3.Components.VRCUiShape`が無いと、レーザーポインター/インタラクトでボタンを押せない。** `eterpix_monitor`(Canvasを持つルート)に両方アタッチすること。実装時にこれを付け忘れて「ボタンが反応しない」不具合が発生したため、忘れずに確認する(発見日: 2026-09-24)。
- **UdonSharpはコードから`Button.onClick.AddListener()`(メソッドグループもラムダも)をバインドできず、コンパイルエラー(またはコンパイラのクラッシュ)になる。** `prevButton`/`nextButton`/`informationButton`/`openPortalButton`のOnClick()は、Inspector上でこの`eterpix_monitor`コンポーネントを直接ドラッグし、`PagePrev()`/`PageNext()`/`ToggleInformationWindow()`/`OpenPortal()`をそれぞれ手動登録すること(Prefabを使い回す場合はPrefab側で一度登録すればよい)。
- v2の`eterpix_requester`はInspectorの「取得先」ドロップダウン(`Editor/eterpix_requesterEditor.cs`、保存値`urlPreset`: 0=パブリック投稿, -1=自分で入力。IDは並べ替えない)でURLを選び、画像URL表はURL変更時に自動で作り直される。念のためのメニュー`ali/eterpix/Sync URL Lists from requestUrl (自動で反映されます・触らないでください)`(v2の`eterpix_requester`用。v1の`eterpix_middle_nav`/`eterpix_middle_ring`は`ali/eterpix/Sync URL Lists from requestUrl (v1 nav/ring)`に分離した。Play開始時はどちらも自動で実行される)もある。Play前に256件のURLが同期されていることを確認する。
- **新モニター(Stack / Split / EterpixTheme)は prefab が正。** ユーザーは prefab を直接編集して調整する。Claude も変更は prefab に直接入れる(Unity MCP で `PrefabUtility.LoadPrefabContents` → 必要な箇所だけ変更 → `SaveAsPrefabAsset`)。**builder(`Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs`)は凍結中**で、作り直しメニューは外してある(prefab をゼロから作り直すと、ユーザーが手で調整した位置・色などが消える。2026-09-27 にポータル出現位置を消した)。builder のコードは prefab 本体と一致していないので、builder を直しても変更にはならない。見た目の確認は `Build Monitor Preview Scene` → PreviewCamera のスクリーンショット → `Close Monitor Preview Scene`。
- 新モニターのルートはCanvasではない(空のGameObject、scale 0.00125 = 1px 1.25mm)。Canvas・GraphicRaycaster・VRCUiShape は子の `Screen` にある。
- テーマの配色先は子オブジェクト名の接頭辞(`bg_` / `tx_` / `btn_` / `acc_` / `acctx_`)で決まる。要素を足したら `eterpix_monitor_theme` の「子から自動収集」を押す。
- ボタンの押下色(ColorBlock)はテーマで変えない(U#から ColorBlock を書き換えるのが未検証のため)。塗りを 0.75 倍に暗くするだけ。

## ビルド・実行コマンド
- Unity Editor上でPlayする。Play開始時に `eterpix_url_sync` が自動で実行される。
- 手動でURLを同期する場合はメニューの `ali/eterpix/Sync URL Lists from requestUrl (自動で反映されます・触らないでください)`(v2) / `… (v1 nav/ring)`(v1) を使う。

## 注意が必要な箇所
- `architecture.md` は現在のコードより古い。回転補正は現在 `localPosition` + `_basePivotToCenter` 方式で、`eterpix_photo_get` はリングバッファ方式に変わっている。仕様はコードを正とする。
- `予備.-cs` は `eterpix_porta_resize` の旧版のバックアップ。拡張子が `.-cs` なのでコンパイルされない。拡張子を `.cs` に戻すとクラス名が衝突する。
- `eterpix_porta_resize` だけはグローバル名前空間にあり、旧系統と新系統の両方から使われている。
- `Controller_TransformChange` / `PortalStand` / `FlekSit` の定義はこのフォルダの外(外部アセット)にある。

---

## BUGS (未修正)
まだ直っていない既知のバグをここに記録する。修正が完了したら削除し、
再発防止に値する内容であれば下記「うまくいった進め方」にPROBLEM/FIX形式で昇格させる。
(以下はいずれもコードを読んで見つけた候補で、実機では未検証)

- [ ] サーバーのスロットは256個(`00`〜`ff`)だが、Unity側は16個前提(`vrcurllist.arraySize` の既定が16、`eterpix_get_api.ParseTimestamps` が `"0"`〜`"15"` 固定)。17番目以降のスロットの画像を取得できない。v2で対応予定 (発見日: 2026-09-23)
- [ ] `eterpix_middle_ring.AssignCells` は、表示が変わったかを投稿IDではなくjsonArray内の位置で判定している。新しい投稿が先頭に入ると、セルが更新されない可能性がある (発見日: 2026-09-23)
- [ ] `eterpix_image_loader` は破棄予約用の一時フィールド(`_pendingDiscardSlot` / `_pendingDiscardGeneration`)を1組しか持たない。10秒の猶予中に別のスロットが解放されると、先に予約したスロットが破棄されずに残る (発見日: 2026-09-23)
- [ ] `eterpix_image_loader` はReleaseしても待機リストから外さない。参照が0のQueuedスロットもダウンロードされ、Loadedになった後も破棄されずに残る (発見日: 2026-09-23)
- [ ] `eterpix_json_loader.CheckRequestTimeout` は `_isRequestPending` を戻さない。コールバックが来なかった場合、それ以降のJSON取得がすべてスキップされる (発見日: 2026-09-23)
- [ ] `eterpix_downloader.CheckRequestTimeout` (v2) も同様に何もしない(あえての制約)。応答が無いフィードは以後取得されず、キューで後ろに並んだ他フィードの取得も進まなくなる (発見日: 2026-09-23)
- [ ] 中位(nav/ring)に `OnDeserialization` がない。後から入ったプレイヤーが `sharedSeed` を受け取っても、次のJSON更新まで並べ直されない (発見日: 2026-09-23)
- [ ] `TextureManager.StartDownloadWithTimestamps` は、ダウンロード中に来た要求を保留せずに捨てる。ダウンロードに失敗した場合、待機中のアイテムには通知されない (発見日: 2026-09-23)
- [ ] `TextureManager` は `cachedTimestamps` をダウンロード成功前に確定させる。URLがnullまたは範囲外でスキップされたスロットは、キャッシュ済みとして残る (発見日: 2026-09-23)
- [ ] `eterpix_listener_monitor` の回転は `localRotation` を直接上書きしている。セル側と違い、基準回転とpivot補正が入っていない (発見日: 2026-09-23)
- [ ] `eterpix_photo_vew.pos_reset` に `VRCObjectSync` のnullチェックがない (発見日: 2026-09-23)
- [ ] `Assets/EterPix/Editor/eterpix_url_sync.cs` の `GenerateUrls` は `X2` フォーマットで大文字16進数(`00`〜`FF`)のURLを生成するが、サーバーのスロットは小文字表記(`00`〜`ff`)の想定。サーバーがURLの大文字小文字を区別する場合、画像取得に失敗する (発見日: 2026-09-23)
- [ ] Play 停止時に `[Image Download] Leaked an IVRCImageDownload!` が出る。`eterpix_downloader` が `VRCImageDownloader` を Dispose していない (発見日: 2026-09-27)
- [ ] requester が maxFeeds 超過で登録できない場合や、EterpixDownloaderV2 が無い場合、モニターは「読み込み中…」のまま止まる(設定ミスをエラー表示しない) (発見日: 2026-09-27)
- [ ] モニターのルートに同期モードの違う UdonBehaviour が2つ(eterpix_monitor=Manual / eterpix_monitor_theme=None)ある。2クライアントの Build & Test でページ同期とテーマ切替を未確認 (発見日: 2026-09-27)
- [ ] LoadingPulse.shader は Mask / RectMask2D(_Stencil, UNITY_UI_CLIP_RECT)に未対応。PostImage をマスク配下に置く場合は対応が必要 (発見日: 2026-09-27)

## うまくいった進め方
記録形式はグローバルCLAUDE.mdの「記録フォーマット」に従う(PROBLEM/FIX形式)。
success_count が概ね20に達したら、スラッシュコマンド化や
Unity.mdなど横断メモへの昇格を検討する。

```yaml
---
name: aspect_ratio_fitter_under_layout_group
success_count: 1
promoted_to:
---

PROBLEM: FigureInner(AspectRatioFitter: Fit In Parent)の親FigureにHorizontalLayoutGroup(UpperLeft)が付いていて、画像が枠の上に寄り下に余白ができた(「A child of a layout group should not have an Aspect Ratio Fitter」警告)。
FIX: Figure(LayoutGroup管理される側、LayoutElementのみ)からLayoutGroupを削除し、FigureInnerをanchor全面stretch・pivot(0.5,0.5)にしてFitterだけにサイズを決めさせた。
```

```yaml
---
name: view_range_trigger_editoronly
success_count: 1
promoted_to:
---

PROBLEM: モニター個別の表示範囲トリガーに EditorOnly タグが付いていてビルドで削除され、回避のため debugIgnoreTriggerRange=true(常に画像を読み込む)のままになっていた。
FIX: 範囲判定を EterpixRequester 自身のトリガーBoxCollider(タグなし、Ignore Raycast)1つにまとめ、requester が配下モニターへ SetInViewRange を通知する形にした。debugIgnoreTriggerRange は false に戻した。
```

```yaml
---
name: texture_not_discarded_after_leaving_range
success_count: 1
promoted_to:
---

PROBLEM: 範囲から出ても写真が表示されたままで、ダウンローダの猶予破棄も起きず、テクスチャ(2048x1728)がメモリに残った。原因は3つ: (1)モニターが解放時にRawImageからテクスチャを外していない、(2)SendCustomEventDelayedSecondsで予約したTryDiscardSlotがTime.time上わずかに早く届き「経過>=猶予」をぎりぎり満たさず、再スキャンが無いのでLoadedのまま残る、(3)DiscardSlotが参照をnullにするだけでDestroyしていない。
FIX: (1)ReleaseCurrentTextureIfAnyで解放したらBeginImageLoading()で表示を外し、ApplyTextureは_hasRequestedTextureがfalseなら貼らない。(2)TryDiscardSlotで猶予中のキーが残っていれば残り時間+0.1秒後に再予約。(3)DiscardSlotでDestroy(texture)する(Udonで UnityEngine.Object.Destroy は使える)。確認はResources.FindObjectsOfTypeAll<Texture2D>()で"ImageFrom:"のテクスチャが消えることを見る。
```

```yaml
---
name: player_trigger_enter_missed_on_spawn
success_count: 1
promoted_to:
---

PROBLEM: トリガーColliderの内側でスポーンすると OnPlayerTriggerEnter が届かず(Udon初期化前から重なっていたため)、入室時に写真が読み込まれなかった(ClientSimで確認)。
FIX: イベントは残したまま、ローカルプレイヤーの OnPlayerJoined の次のフレームで一度だけ Physics.CheckBox(BoxColliderの中心・サイズ・回転をワールドに変換、layerMaskはPlayerLocalのみ、QueryTriggerInteraction.Collide)で自分の体と範囲が重なっているかを確かめて補正した。CheckBoxはトリガーと同じ「体と箱の重なり」なので境界での食い違いが無い。Playerレイヤーを含めると他人に反応するので含めない。
```

---

## Git運用メモ
- このプロジェクトはGit管理: はい (リポジトリのルートはUnityプロジェクト `V:\VRChatSDK-Unity\AirPocket-EterPix`。GitHub: AliceAliciaBB/AirPocket-EterPix (private) の `eterpix-dev` ブランチ。`main` は別系統の旧プロジェクトで履歴はつながっていない)
- 更新後は必ずコミット・プッシュを行う
- デスクトップPCとノーパソ間で作業を行き来する場合、
  作業開始前に必ず `git pull`、作業終了後に必ず `git push` を行う
