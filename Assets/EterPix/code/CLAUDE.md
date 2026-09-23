# プロジェクト名: EterPix (AirPocket-EterPix)

## このプロジェクトについて
- 種別: Unity / VRChatワールド (UdonSharp)
- 概要: 外部サーバー(eterpix.uk)の投稿JSONと、2列×3行のコラージュ画像を取得し、ワールド内に写真・キャプション・ポータルとして表示するギミック。
- 開発環境: (未記入)
- 関連する横断メモ: @C:\git\claude.global\domains\Unity.md
- 内部実装の調査記録: `C:\git\claude.global\知識\Unity\EterPix.md` (サブシステム別に5ファイル。2026-09-23 調査)
- **リファクタリング案(v2の仕様)**: [リファクタリング案.md](リファクタリング案.md)。ダウンローダ/リクエスター/モニターの3層で `code/v2/` に作り直す。旧系統・現新系統は完成後に削除する(2026-09-23 決定)。
- **UIテンプレート(モック)**: `D:\git\eterpix-v10\vrc_ui`
  - ワールド内でEterPixを見る画面のHTMLモック。**本体は `#root` 部分のみ**で、実際はUnityのUI要素で構成する(HTMLはモック作成用)。
  - **`vrcetepixui.html` / `vrcetepixui.css` が正**。UIを組むときはこの2ファイルを参照する。
    - 画面構成(`#root` 内): `header`(ロゴ `#logo`、インフォメーションボタン `#Information`) → `figure`(画像 `#post_img`。常に4:3の領域を確保し、縦画像もトリミングせず全体を表示) → `#paging`(`<` / `現在ページ/ページ数` / `>`、両端ではボタン無効) → `#context`(`#world_context`: ワールド名・説明・「ポータルを開く」ボタン。ワールド情報が無い場合は要素ごと無い / `#post_context`: 投稿者名・投稿説明) → `#Information_window`(ⓘで開閉するサービス説明)。
    - 配色・角丸などは css の `:root` のCSS変数で定義されている(`--canvas` `--surface` `--text` `--highlight` `--radius-md` など)。
  - `DESIGN.md`: cssを作る元にしたデザイン定義。**その後cssを修正しているため、DESIGN.mdとcssが食い違う場合はcss(とhtml)を優先する**。
  - `テスト_img/`: 表示確認用のVRChatスクリーンショット(横長 2560x1440 と縦長 1440x2560)。追加・削除したら `node vrc_ui/gen-test-images.mjs` を実行して `test_images.js` を作り直す(手で編集しない)。

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
- 画像URLは実行時に作れないため、`vrcurllist` にエディタ上で事前生成する(`baseUrl/00`〜`0F`)。新系統では、Play開始前に `Editor/eterpix_url_sync.cs` が中位の `requestUrl` から自動で同期する。旧系統のURL(`requestUrl1..3` / `TextureManager.urlList1..3`)は手動で揃える。
- 2×3コラージュのUV計算式と回転の向き(`imgRotation * -90f`)は、`eterpix_item` / `eterpix_photo_vew` / `eterpix_cell` / `eterpix_listener_monitor` で同じ式を使う。1か所だけ変えないこと。
- VRCStationに着席している間は `SetParent` を使わない(トラッキングの復帰が壊れる)。`TekkotsuLiftController` のように、位置と回転の追従を自前で行う。

## ビルド・実行コマンド
- Unity Editor上でPlayする。Play開始時に `eterpix_url_sync` が自動で実行される。
- 手動でURLを同期する場合はメニューの `ali/eterpix/Sync URL Lists from requestUrl` を使う。

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
- [ ] 中位(nav/ring)に `OnDeserialization` がない。後から入ったプレイヤーが `sharedSeed` を受け取っても、次のJSON更新まで並べ直されない (発見日: 2026-09-23)
- [ ] `TextureManager.StartDownloadWithTimestamps` は、ダウンロード中に来た要求を保留せずに捨てる。ダウンロードに失敗した場合、待機中のアイテムには通知されない (発見日: 2026-09-23)
- [ ] `TextureManager` は `cachedTimestamps` をダウンロード成功前に確定させる。URLがnullまたは範囲外でスキップされたスロットは、キャッシュ済みとして残る (発見日: 2026-09-23)
- [ ] `eterpix_listener_monitor` の回転は `localRotation` を直接上書きしている。セル側と違い、基準回転とpivot補正が入っていない (発見日: 2026-09-23)
- [ ] `eterpix_porta_resize.Update` に `portal` のnullチェックがない。`eterpix_photo_vew.pos_reset` に `VRCObjectSync` のnullチェックがない (発見日: 2026-09-23)

## うまくいった進め方
記録形式はグローバルCLAUDE.mdの「記録フォーマット」に従う(PROBLEM/FIX形式)。
success_count が概ね20に達したら、スラッシュコマンド化や
Unity.mdなど横断メモへの昇格を検討する。

(まだ記録なし)

---

## Git運用メモ
- このプロジェクトはGit管理: はい (リポジトリのルートはUnityプロジェクト `V:\VRChatSDK-Unity\AirPocket-EterPix`。GitHub: AliceAliciaBB/AirPocket-EterPix (private) の `eterpix-dev` ブランチ。`main` は別系統の旧プロジェクトで履歴はつながっていない)
- 更新後は必ずコミット・プッシュを行う
- デスクトップPCとノーパソ間で作業を行き来する場合、
  作業開始前に必ず `git pull`、作業終了後に必ず `git push` を行う
