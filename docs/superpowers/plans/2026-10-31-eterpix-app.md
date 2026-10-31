# EterPix-app (NudeNyang 12番目のアプリ) 実装計画

> **実装者へ**: `superpowers:subagent-driven-development`(推奨)または `superpowers:executing-plans` でタスク単位に進める。チェックボックスで進捗を管理する。
> **状態**: **承認済み(2026-10-31)**。D1〜D3は推奨どおり。R18は「コード中に固定で除外」(設定項目にしない)。NudeNyangテーマとの連動は無し(独立)の仮置きのまま。

**Goal:** VRChat内のNudeNyangタブレットに、EterPixの公開投稿をスワイプで見て「気になる」を溜められるアプリ「EterPix-app」を載せる。

**Architecture:** 新規コードは追跡される `Assets/EterPixNudeNyang/` に置き、取得は既存の `eterpix_downloader` を再利用せず、vrc-api v1 の `GET /api/vrc/v1/public` と `/public/<hex>` を、このアプリ専用の小さな取得キューで読む(理由は「決めること」D2)。NudeNyang本体(gitignore下・第三者製 v1.0.2)への変更は、**冪等な Editor パッチャ**に閉じ込めて追跡する。

**Tech Stack:** UdonSharp / VRChat SDK 3.10.x(VRCStringDownloader・VRCImageDownloader・VRCUrl)、Unity UGUI(Text/RawImage/Button、NudeNyang流の Editor Builder)、既存 `NudeNyangTravelPortal`。

## 一次資料
- 合意書: `D:\git\eterpix_integrated_management\.claude\worktrees\docs-item-count-note\docs\VRC-NudeNyangTablet_app.md`(ブランチ `worktree-docs-item-count-note`。**main の同名ファイルは古い初期案**なので、まずこのブランチを main へ取り込む = Task 0)
- HTMLモック: 同ブランチの `docs/mocks/eterpix-app-mock.html`
- UI規則: `docs/eterpix-monitor-ui-remake-spec.md`(長方形・線なし・余白8/16・面の色はボタンだけ・NotoSansJP-Bold)
- API: `docs/contracts/vrc-api-v1-schema.md` / `image-formats.md`(統合管理リポジトリ)

## Global Constraints
- 全要素は長方形(角丸・ピル形なし)、線なし、区切りは余白(単位8・外側16)、ボタンだけが面の色を持つ、文字は不透明1色
- フォントは `Packages/com.aliciliabb.eterpix/Fonts/NotoSansJP-Bold SDF.asset`
- 色: 黒テーマ #141414 / 白テーマ #FAFAFA、ボタン #2A2A2A / #E6E6E6、押下 #3A3A3A / #D4D4D4、アクセント #9D6FFF(上の文字 #141414)
- 例外: 写真の下端の情報帯だけ、文字を50%半透明で重ねてよい
- ヘッダー高さ40・ボタン40×40・上余白16。左ロゴ「EterPix-app」/ 中央(スワイプ画面のみ)「現在 / 総数」/ 右は左から ♡(ロード後)→テーマ→グループ→i。♡・グループ・i は開いている間 ✕ に変わり同じボタンで戻る
- 情報画面(i)の末尾に「最新情報は X @_alicilia」
- 状態表示: 通常 / 読み込み中(写真枠が明滅)/ URL未許可 / サーバー停止 / 投稿0件。ヘッダーは常に表示
- ポータルは既存 `NudeNyangTravelPortal` を使い、新しいポータルは作らない(床指し配置・同時1つ・再配置30秒・15秒で消える)
- U# には static フィールド・インターフェース・ジェネリクスの制約がある(`docs/projects/unity.md` 参照)。NudeNyangのコードは1行詰めの書式だが、**我々の新規コードは通常の書式**で書く
- 作業は専用 worktree / ブランチで行う。**共有リポジトリで `commit -a` しない**(自分のファイルだけ add)。スクリプト編集後は Unity で import まで済ませる。デバッグログは `[EtpTrace]` 接頭辞でコンソールへ常時出す

## ファイル構成

| パス | 役割 |
|---|---|
| `Assets/EterPixNudeNyang/Scripts/EterPixAppFeed.cs` | JSON取得・パース・投稿配列(poid/coid/copo/coro/表示名/ワールド/説明)。状態(0読込中/1正常/2URL未許可/3サーバー停止/4 0件) |
| `Assets/EterPixNudeNyang/Scripts/EterPixAppImages.cs` | コラージュ画像の直列DL(5秒制限)、テクスチャのキャッシュ予算・追い出し、UV/回転計算 |
| `Assets/EterPixNudeNyang/Scripts/NudeNyangEterPix.cs` | アプリ本体(画面遷移・ヘッダー・起動/スワイプ/拡大/一覧/共通画面・気になる集合)。`namespace NudeNyang`。`Present()` / `Open()` / `PlacementNotice` / `PlacementAllowed` をTravelと同じ形で持つ |
| `Assets/EterPixNudeNyang/Scripts/EterPixAppGeometry.cs` | 純粋な計算(UV、K-4の長辺合わせ・回転、1:1切り取り)。static を使えないので U# 内の公開メソッド |
| `Assets/EterPixNudeNyang/Editor/EterPixAppBuilder.cs` | UI階層の生成・配線(NudeNyangTravelBuilder と同じ流儀)。メニュー `NudeNyang/EterPix-appを配置` |
| `Assets/EterPixNudeNyang/Editor/NudeNyangEterPixPatcher.cs` | NudeNyang本体への変更を冪等に当てる/外す/検査するツール |
| `Assets/EterPixNudeNyang/Editor/Tests/EterPixAppGeometryTests.cs` | 幾何計算のEditModeテスト(U#の外に切り出した同一式を検証。下記Task 3) |
| `Assets/EterPixNudeNyang/README.md` | 使い方・パッチ手順・NudeNyang更新時の再適用 |

## 決めること(承認時に確認。推奨を既定として計画を組んでいる)

- **D1 配置**: 全部 `Assets/EterPixNudeNyang/`(推奨)か、`Packages/com.aliciliabb.eterpix` に入れるか。→ 推奨は前者。EterPix本体パッケージはVPMで配るため、有償の第三者アセットに依存させない。NudeNyang側も asmdef が無く(Assembly-CSharp)、パッケージ asmdef からは参照できない
- **D2 取得層**: `eterpix_downloader` を拡張して共用(画像の受け手は `eterpix_monitor` 型固定で、U#にインターフェースが無く拡張が大きい)か、専用の取得キュー(推奨)か。→ 推奨は専用。代償は、同じワールドの既存モニターと VRChat の5秒制限を取り合うこと(Pad の K-12 のような優先は付かない)。**ワールドにモニターを置く場合は影響を実機で確認する**
- **D3 合意書の「未決」**: ポータル方式 A/B、サーバー保存、起動→開始の関係など。本計画は「**気になる = ローカルのみ(セッション内、再起動で消える)**」「**ポータルは方式A(`BeginPlacement`)**」を仮に採る
- **D4 WEB / 高画質 = コピペ方式(ユーザー決定 2026-10-31)**: EterPix-beta のモニター(`eterpix_monitor.cs:545-562`)と同じく、**読み取り専用の入力欄に URL を入れ、押すと VRChat の入力 UI が開いてコピーできる**方式。UI は NudeNyang が持つ同じ型の部品に合わせる(NudeNyang側は `UnityEngine.UI.InputField` に実行時にテキストを入れる作り: `NudeNyangBrowserShare.publicAddress`(「원문 주소를 복사하여…」の原文アドレス欄)と `NudeNyangMediaKeyboard.copyField`)。新しいコピー機構は作らない。URL は `https://eterpix.com` + WEB `/web/post/<poid>`(モニターの `WebPostPath`)/ 高画質 `/api/images/<poid>`(`image-formats.md`。サフィックスなし = フル画質)。`VRCUrlInputField` は `VRCUrl`(ビルド時固定)が要るので**使わない**。`InputField` なら投稿ごとの動的な文字列を入れられる

---

### Task 0: 合意書を main へ取り込み、NudeNyang変更点を確定する(調査・実装なし)

**Files:**
- 統合管理リポジトリ: ブランチ `worktree-docs-item-count-note` を main へ(自分の worktree で、`node scripts/check-decisions.js` を通してから)
- Modify: 本計画書の「パッチ一覧」節(下の Task 1 の表を実測で埋め直す)

**Produces:** 確定したパッチ一覧(ファイル・行・アンカー文字列)。

- [x] **Step 1(完了 2026-10-31. origin/main `0810999`。fast-forward、check-decisions OK)**: 統合管理リポジトリで上記ブランチを `origin/main` にマージする方針をユーザーに確認し、マージ。これで合意書の正本が main になる
- [x] **Step 2(完了。下の「確定パッチ一覧」)**: NudeNyang の次の箇所を grep し、**12番目のアプリを足すのに変える全箇所**を書き出す(下はこれまでに見つけた分。漏れを探す)
  - `NudeNyangTabletShell.cs:18` `AppCount=11` / `:556` `AppName(id)` / `:611` `OpenTravel` 相当の `OpenEterPix` / `:629-` `Launch(int id)` の分岐 / `:1515` `travel.Present()` / `:1582,:1629` 最近使った・ドックのアイコン色(`appSprites` `appColors` の長さ)/ `homeIcons`(アイコン配列の長さ)
  - `NudeNyangSharedControls.cs:352,1472,1554`(`AppCount` 検証。4ビット×5=12は収まる)/ `:1271,:1288,:1292`(`methods="|…|OpenTravel|"` の許可リスト)/ `:117,:127,:518,:1200,:1389`(app==11 のスクロール連携。EterPix-appがスクロールを共有しないなら不要)
  - `NudeNyangMediaKeyboard.cs:298`(`OpenTravel`→11 の対応表。キーボードを使わないなら不要)
  - `NudeNyangTabletShellBuilder.cs:173,:215`(ホームのアイコン生成 `i==10`)/ `NudeNyangHomeAppIconsBuilder.cs` / `NudeNyangTravelIconBuilder.cs`(アイコン)
  - `NudeNyangTravelPortal.cs` の `BeginPlacement(string, NudeNyangTravel)`・`CancelFor(NudeNyangTravel)`・`placingApp`(型が `NudeNyangTravel` 固定)
- [x] **Step 3(完了。MCP検証で収まる)**: 「ホームのアイコンが12個目に収まるか」「お気に入り/ドックの `favoriteApps`」「`ShellDisplay`」の挙動を Unity 上のテンプレシーンで確認し、収まらない場合はユーザーに相談する(**見た目が変わるので勝手に直さない**)
- [x] **Step 4(完了)**: 結果を本計画書に反映(Edit)してコミット

**確定パッチ一覧(NudeNyang 1.0.2。アンカーは現物の文字列)** — Task 1 のパッチャが当てる。EterPix-app は `id=12`、`homeIcons[11]`

| # | ファイル | アンカー(置換前) | 変更 |
|---|---|---|---|
| P1 | Scripts/NudeNyangTabletShell.cs | `public const int AppCount=11;` | `=12` |
| P2 | 同上 | `public NudeNyangTravel travel;` | 後ろに `public NudeNyangEterPix eterpix;` |
| P3 | 同上 | `if(id==11)return pad.T("트래블 월드");` | 前に `if(id==12)return "EterPix";` |
| P4 | 同上 | `public void OpenTravel(){if(Route("OpenTravel"))LaunchFrom(11,homeIcons[10]);}` | 後ろに `public void OpenEterPix(){if(Route("OpenEterPix"))LaunchFrom(12,homeIcons[11]);}` |
| P5 | 同上 | `else if(id==9&&booth!=null)booth.Open();` | 後ろに `else if(id==12&&eterpix!=null)eterpix.Open();` |
| P6 | 同上 | `if(travel!=null)travel.Present();` | 後ろに `if(eterpix!=null)eterpix.Present();` |
| P7 | Scripts/NudeNyangSharedControls.cs | `target==9` の許可リスト内 `|OpenTravel|` | `|OpenTravel|OpenEterPix|` |
| P8 | 同上 | `else if(target==5)methods="|Open|OpenX|OpenBooth|OpenTravel|";` | `|OpenEterPix|` を追加 |
| P9 | Editor/NudeNyangTabletShellBuilder.cs(2箇所 `:173` `:215`) | `i==9?"화면 공유":"트래블 월드"` / `i==9?"OpenScreenShare":"OpenTravel"` | `i==11?"EterPix":` と `i==11?"OpenEterPix":` を前に足す |
| P10 | Scripts/NudeNyangTravelPortal.cs | `private NudeNyangTravel placingApp;` ほか `BeginPlacement(string,NudeNyangTravel)` `CancelFor(NudeNyangTravel)` | 型を `UdonSharpBehaviour` に。`PlacementNotice` / `PlacementAllowed` は呼べないため、`NudeNyangTravel` 版の引数を残したまま EterPix 用に **別名のメソッド** `BeginPlacementEterPix(string,NudeNyangEterPix)` / `CancelForEterPix` を足し、内部状態 `placingEterPix` を持たせる(U#は多態不可のため、型の差し替えより安全。既存の Travel の挙動は変えない) |

- **共有操作(together/follow)について**: Travel の各ボタンは `pad.RouteAction(13, action)` を通る(他プレイヤーの操作に追従する機能の関所)。EterPix-app は v1 では**ローカルのみ**とし、`RouteAction` は通さない代わりに、`pad.FollowingUI()` が true の間は入力を無効にする。`P7/P8` の許可リスト追加は、ホームアイコンの `Route("OpenEterPix")`(`target==9`)に必要
- P10 を当てる前に、`NudeNyangTravelPortal.cs` の `placingApp` の参照箇所(`Update` の `placingApp.PlacementAllowed()`、`ConfirmPlacement` の `app.PlacementNotice`)も同様に分岐が要る。Task 1 のパッチャでは P1〜P9 を先に当て、P10 は Task 8 で当てる(Task 1 の縦串スモークにポータルは不要)

---

### Task 1: 空のアプリがタブレットから開く(縦串スモーク) + パッチャ

**Files:**
- Create: `Assets/EterPixNudeNyang/Editor/NudeNyangEterPixPatcher.cs`
- Create: `Assets/EterPixNudeNyang/Scripts/NudeNyangEterPix.cs`(今は `Open` `Present` `Close` と空パネルだけ)
- Create: `Assets/EterPixNudeNyang/Editor/EterPixAppBuilder.cs`(空パネルを作って `shell.eterpix` に接続)

**Interfaces:**
- Produces(パッチ後の NudeNyang 側): `NudeNyangTabletShell.eterpix : NudeNyangEterPix`、`OpenEterPix()`、`AppCount=12`、`AppName(12)="EterPix"`。`NudeNyangTravelPortal.BeginPlacement(string worldId, UdonSharpBehaviour app)` と `CancelFor(UdonSharpBehaviour app)`(既存の `NudeNyangTravel` 版は残し、中で新版へ委譲する。EterPix-app は `PlacementNotice(string)` / `PlacementAllowed()` を持つ)
- Produces(我々): `NudeNyangEterPix.Open()`, `Present()`, `Close()`, `PlacementNotice(string)`, `PlacementAllowed()`

パッチャの仕様(ここが肝):
```csharp
// 1パッチ = ファイル + アンカー(現在の文字列)+ 置換後 + 適用済み判定の目印.
// 適用: 目印が既にあれば何もしない(冪等). アンカーが1回ちょうど無ければ「NudeNyangが更新された可能性」で停止して報告する.
// 初回は元ファイルを <名前>.eterpix-orig として隣に退避する(gitignore下でも手元には残る).
// 検査: Menu "NudeNyang/EterPix-app/パッチを検査" で全パッチの状態(適用済/未適用/アンカー不一致)をログに出す.
// installed-release.json の version が 1.0.2 でなければ警告する.
```

- [ ] **Step 1**: パッチャを書く。まずは Task 0 の一覧のうち「コンパイルが通る最小」(`AppCount`、`eterpix` フィールド、`OpenEterPix`、`Launch` の分岐、`AppName`、ポータル型の汎用化、SharedControls の許可リスト)だけ
- [ ] **Step 2**: `NudeNyang/EterPix-app/パッチを検査` を実行 → 未適用が列挙される(失敗の確認)
- [ ] **Step 3**: 適用 → 検査で全て「適用済」、Unity のコンソールにコンパイルエラーなし(`read_console`)。再度適用しても差分が出ないこと(冪等)を確認
- [ ] **Step 4**: `NudeNyangEterPix` の最小実装と Builder を書き、テンプレシーンで「ホームに12個目のアイコン → タップ → 空パネル(「EterPix-app」とだけ表示)→ ホームへ戻る」をClient Sim(またはPlayモード)で確認。**Travel等の既存アプリが従来どおり開くこと**も確認
- [ ] **Step 5**: コミット(`Assets/EterPixNudeNyang/` と本計画書だけ add。NudeNyang本体は gitignore なので入らないことを `git status` で確認)

---

### Task 2: 投稿データ(JSON取得とパース)

**Files:**
- Create: `Assets/EterPixNudeNyang/Scripts/EterPixAppFeed.cs`
- Modify: `NudeNyangEterPix.cs`(フィードを保持し `Open()` で取得開始)
- Modify: `EterPixAppBuilder.cs`(`VRCUrl feedUrl` を設定)

**Interfaces:**
- Produces: `EterPixAppFeed`
  - `public VRCUrl feedUrl;`(= `https://eterpix.com/api/vrc/v1/public`。Builder が設定。**VRCUrl はビルド時に焼く必要がある**)
  - `public int Status` (0読込中 / 1正常 / 2URL未許可 / 3サーバー停止 / 4 0件)
  - `public int Count`、`public string[] Poid, Pode, UserLabel, WorldLabel, WorldId;` `public int[] Coid, Copo, Coro;`
  - `public void Fetch()`、完了時に `owner.SendCustomEvent("OnFeedChanged")`
- 表示名の合成(契約どおり): `UserLabel = usna + (ussf があれば "[" + usst[ussf[0]] + "]")`、`WorldLabel = (wopf の各IDを wopt で引いて連結。未知IDは読み飛ばす) + woda[woid].wona`。`woid` が null/空なら `WorldId=""`
- 取得: `VRCStringDownloader.LoadUrl(feedUrl, (IUdonEventReceiver)this)`。`OnStringLoadError` で `result.ErrorCode` を見て、URL未許可(`401`系/例外)とサーバー停止を分ける(既存 `eterpix_downloader.cs:661` の判定を踏襲)。R18(`po18==1`)は**コード中に固定で除外する**(Inspector等の設定項目にしない。ユーザー決定 2026-10-31。パース時に読み飛ばし、`Count` にも含めない)

- [ ] **Step 1**: v1の実レスポンス(`curl https://eterpix.com/api/vrc/v1/public`。出力は件数とキーの確認のみで、投稿者名等は記録に残さない)でキーを確認する
- [ ] **Step 2**: `EterPixAppFeed` を書く。パース後に `[EtpTrace] feed count=N status=S` を必ず出す
- [ ] **Step 3**: Playモードで開き、コンソールに件数が出る・Untrusted OFF では status=2 になる・オフラインで status=3 になることを確認
- [ ] **Step 4**: コミット

---

### Task 3: 画像(コラージュ取得・キャッシュ・UV/回転)

**Files:**
- Create: `Assets/EterPixNudeNyang/Scripts/EterPixAppImages.cs`、`EterPixAppGeometry.cs`
- Create: `Assets/EterPixNudeNyang/Editor/Tests/EterPixAppGeometryTests.cs`
- Modify: `EterPixAppBuilder.cs`(256件の `VRCUrl[] imageUrls` = `https://eterpix.com/api/vrc/v1/public/00`〜`FF` を設定。`vrcurllist` と同じ生成)

**Interfaces:**
- `EterPixAppGeometry`(U#の外でも同じ式で検証できるよう、式は `public static` ではなく **インスタンスメソッド**にし、テストは同じ式を書いた純C#ヘルパ `EterPixAppMath`(Editor側 `Tests/` 内)と突き合わせる)
  - `Rect CellUv(int copo)`: `col=(copo-1)%2; row=(copo-1)/2; new Rect(col*0.5f, 1f-(row+1)/3f, 0.5f, 1f/3f)`
  - `bool IsPortrait(int coro)`: `coro % 2 == 1`
  - `Vector2 FitSize(Vector2 box, bool portrait)`: K-4。写真の長辺を箱の長辺に、短辺を短辺に合わせ、食い違うときは90°回す(縦写真を横長の箱へ入れるなら回して長辺を合わせる)。戻り値は回転後の表示サイズ、回すかどうかは別の `bool NeedsQuarterTurn(Vector2 box, bool portrait)`
  - `Rect SquareCropUv(Rect cell, bool portrait)`: 一覧用の中央1:1切り取り(横写真は左右、縦写真は上下が切れる)
- `EterPixAppImages`
  - `Texture2D GetTexture(int coid)`(未取得は null)、`void Want(int coid)`、`int maxCached`(既定は仮 24。**実機で決める**。D3/合意書「件数」)
  - 直列DL: `VRCImageDownloader.DownloadImage(url, null, this, null)`。1件ずつ、完了するまで次を始めない(コールバックに識別子が無いため `pendingCoid` を1つ持つ。`eterpix_downloader` と同じ方針)。タイムアウトは警告ログのみ
  - 追い出し: 現在のカードの前後 `LookAhead` 件が使う `coid` を優先して残し、超過分は `Destroy`/`Dispose`(`IVRCImageDownload.Dispose()`)
  - 進行度: `Loaded / Total` を返す(起動画面の「ロード中...〇%」。Total = 現在の投稿が使う異なる `coid` の数)

- [ ] **Step 1**: 失敗するテストを書く。`CellUv(1)=(0,2/3,0.5,1/3)`、`CellUv(6)=(0.5,0,0.5,1/3)`、`IsPortrait(1)=true/IsPortrait(2)=false`、`FitSize` が「横長の箱 + 縦写真」で回転ありを返す、`SquareCropUv` が横写真で左右が切れる、を各1ケース以上
- [ ] **Step 2**: `Run: mcp run_tests (EditMode)` で FAIL を確認
- [ ] **Step 3**: 式を実装(EditorのヘルパとU#側に同じ式を置く。コメントで相互参照)
- [ ] **Step 4**: テストが PASS
- [ ] **Step 5**: `EterPixAppImages` を実装し、`[EtpTrace] img coid=N loaded loadedCount=a/b evicted=c` を出す。Playモードで最初の数枚が順に取得され、5秒間隔で進むことを確認
- [ ] **Step 6**: コミット

---

### Task 4: 起動画面と共通ヘッダー

**Files:** Modify `NudeNyangEterPix.cs`、`EterPixAppBuilder.cs`

**Interfaces:** `NudeNyangEterPix` に画面状態 `screen`(0起動 / 1スワイプ / 2一覧 / 3情報 / 4グループ)と、押す前の画面 `returnScreen` を持つ。`ShowScreen(int)`、`ToggleOverlay(int)`(♡/グループ/i は同じボタンで元へ戻る)。テーマ `isDark`(ローカル)

- [ ] **Step 1**: Builder にヘッダー(ロゴ・ボタン4つ)と起動画面(ロゴ・紹介文・進行バー・「push to start」・フッター)を足す。文面は合意書の初期案のとおり
- [ ] **Step 2**: ロード中は `push to start` を隠し「ロード中... 〇%」とバーを出す。`Images.Loaded/Total` と `Feed.Status` から更新。完了で `push to start` を出し、押すと `ShowScreen(1)`。**0件・URL未許可・サーバー停止ではロードが終わらない代わりにその状態を写真枠に出す**(合意書「状態の表示」)
- [ ] **Step 3**: i / グループ / テーマ の共通画面。✕ への切り替え。情報画面末尾に「最新情報は X @_alicilia」。グループ説明は仮文面(合意書で「仮」)
- [ ] **Step 4**: テーマ切替(黒/白)を、色のトークン(上の Global Constraints)を Builder で作った `Graphic[]` に配って切り替える。NudeNyang本体のテーマ(`NudeNyangTabletTheme`)とは**独立**にする(合意書にNudeNyangテーマ連動の記載なし。要確認)
- [ ] **Step 5**: Playモードで 起動→(ロード)→開始 / i・グループの開閉 / テーマ切替 を目視確認し、スクリーンショットを `docs/superpowers/plans/screens/` に残す
- [ ] **Step 6**: コミット

**実施メモ(Task 3・4)**
- Task 3: テストは Test Runner ではなく Editor 自己テスト(メニュー `NudeNyang/EterPix-app/幾何の自己テスト`、`[EtpTrace] test geometry PASS`)にした(asmdef 制約。内部実装の変更)。回転は Pad の K-4 と同じ**反時計回りの追加**を採用(時計回りだと縦写真が横枠で上下逆さになるため)。→ 合意書「回転の向き」未決として要報告。
- Task 4: パッチ P11 を追加(NudeNyang のテーマが EterPix-app の色を上書きするため、`NudeNyangTabletThemeBuilder` の走査から本アプリのパネルを除外)。黒/白テーマは独立のまま。
- Task 4: 起動時の先読み `startupPreload`=3(仮)。全26枚だと約130秒かかるため。「ロード完了」の定義は合意書で未決 → 要確認。
- Task 4: ヘッダーのアイコン(G/T)は仮の文字。ロゴ・ヘッダーの最終アイコンは未決。
- 検証: スクショ `docs/superpowers/plans/screens/task4-*.png`(起動画面・情報画面・白テーマ・×切替)、`start → screen=1` のログ確認済み。

---

### Task 5: スワイプ画面

**Files:** Modify `NudeNyangEterPix.cs`、`EterPixAppBuilder.cs`

**Interfaces:** `int cursor`(現在のカード)、`bool[] liked`(`Count` 長)、`int likedCount`。`SwipeLeft()` / `SwipeRight()`(= 気にならない / 気になる)、`OnDragCard()`(ドラッグ量)。カードは2枚だけ実体化して使い回す(現在 + 真下に隠れた次)

- [ ] **Step 1**: カード(RawImage + UV)と情報帯(投稿者名[称号]・ワールド名、50%半透明)を作る。写真は枠いっぱい・縦横比維持、縦判定は `IsPortrait(coro)`
- [ ] **Step 2**: ヘッダー中央に「現在 / 総数」。写真の下の帯に左右ボタン(左 = 気にならない / 右 = 気になる)。ドラッグ中は色づく。**VRChatのレーザー/タッチでのドラッグ**は、NudeNyang流の `NudeNyangTouchSlop` / ジェスチャの扱いに合わせる(Task 0 で読んだ結果に従い、ここでは「ボタン操作を必須、ドラッグ・矢印キーを追加」の順で実装する)
- [ ] **Step 3**: すべて見終わったら「気になる投稿一覧を見る」と「もう一度見る」を出す。「もう一度見る」→モーダル「気になる選択はなくなります。いいですか?」→「はい」で `liked` を全消去して `cursor=0`、「やめる」で戻る
- [ ] **Step 4**: 画像が来ていないカードは写真枠を明滅(マテリアルは `NudeNyangInk.mat` ではなく、既存モニターの明滅マテリアルを参照して流用)。先読みは `Want(coid)` を `cursor±LookAhead` に対して呼ぶ
- [ ] **Step 5**: Playモードで 20件前後を最後まで送り、気になる件数・「もう一度見る」の挙動を確認。スクリーンショットを残す
- [ ] **Step 6**: コミット

**実施メモ(Task 5)**
- 実装: カード1枚を使い回す(2枚実体化は不要と判断。内部実装)、写真は正立(coro 回)して枠に収まる最大で表示(縦写真は縦のまま左右に余白)、情報帯は半透明、左右ボタン、現在/総数、終了画面、「もう一度見る」確認モーダル。先読みは `lookAhead=6` 件。
- 未実装(後回し): ドラッグでのスワイプと矢印キー(計画どおり「ボタン操作を必須、ドラッグ・矢印を追加」の追加分)、画像待ちの明滅(今は「読み込み中...」の文字)。ドラッグ中の色づきも未実装。
- 検証: 1枚目(縦)・3枚目(横)の見た目、144件を最後まで送る→終了画面→「もう一度見る」→やめる/はいで気になる0件・先頭へ戻る、を Play で確認(スクショ `screens/task5-*.png`)。
- 検証の注意: エディタ再生では一定時間でタブレットの画面がスリープ(ScreenCanvas のスケール0)する。撮影時はスケールを一時的に戻してから撮る。

---

### Task 6: 拡大(UIあり → UIなし → 戻る)

**Files:** Modify `NudeNyangEterPix.cs`、`EterPixAppBuilder.cs`

- [ ] **Step 1**: 写真を押す → 拡大(情報帯だけ。ヘッダー・フッターは出さない)→ 押す → UIなし → 押す → 元の画面。`zoomMode`(0なし/1UIあり/2UIなし)と、`zoomReturn`(スワイプ画面 or 一覧)を持つ
- [ ] **Step 2**: 表示サイズは `FitSize(アプリ面の寸法, portrait)`、`NeedsQuarterTurn` のとき RawImage を時計回りに90°回す(回転の向きは合意書「未決」。仮に時計回り)
- [ ] **Step 3**: 横写真・縦写真の両方で確認(Padと同じ見え方になること)
- [ ] **Step 4**: コミット

**実施メモ(Task 6)**
- 実装: 写真(透明ボタン)を押す → 全面の拡大(情報帯あり)→ 押す → UIなし → 押す → 閉じる。`OpenZoom(index)` は Task 7 の一覧からも使う。画像未着の投稿は開かない。
- 回転は計画の「時計回りに仮」ではなく、スワイプと同じ「正立(coro 回)して枠に収める」にした(縦写真は縦のまま左右に余白)。Pad のように枠に合わせて倒す見え方にはしていない。→ 合意書「回転の向き」と併せて要確認。
- 発見: タブレットがスリープすると `appFrame` の大きさが0になり、実行時の `RectTransform.rect` が0を返す。大きさは Builder が焼いた `panelSize`/`cardSize` を使う(`rect` を実行時に読まない)。
- 検証: Play で 縦写真(1枚目)を拡大 → UIなし → 閉じる を確認(スクショ `screens/task6-zoom-noui.png` 相当は `task6-zoom-ui.png`)。情報帯ありの表示はログ(`band=True`)のみで、画像は未撮影。

---

### Task 7: 気になる投稿一覧

**Files:** Modify `NudeNyangEterPix.cs`、`EterPixAppBuilder.cs`

- [ ] **Step 1**: ♡で開く。`ScrollRect` + 行テンプレート(件数分は作らず、`likedCount` 分だけ有効化。上限は `Count`)。1件は2段「画像 | 説明」「画像(続き) | ボタン」
- [ ] **Step 2**: 画像は `SquareCropUv` で中央1:1。押すと Task 6 の拡大に入り、最後は一覧へ戻る
- [ ] **Step 3**: ボタンは右寄せ、左から「WEB | 高画質 | ポータルを開く」。WEB・高画質は **D4 のコピペ方式**: ボタンの枠の中に、読み取り専用の `InputField`(文字は透明、枠いっぱいに重ねる。モニターの `SetShareField` と同じく、枠ごと出し入れする)を置き、行が有効になるときに `field.text = "https://eterpix.com/web/post/" + poid` / `"https://eterpix.com/api/images/" + poid` を入れる。押すと VRChat の入力 UI が開き、そこでコピーできる。文字は変えない。高画質は押すと面の色だけ変わる(押下の見た目はボタン側の色遷移)
  - 実装の手本はこの2つを読んでから書く: `NudeNyangBrowserShare.cs` の `publicAddress` の使われ方(`:1327`, `:1572`)と、`NudeNyangMediaKeyboard.cs:905-915` の `RefreshHandoff`(「ラベルが空のまま残る」対策に `ForceLabelUpdate()` と `textComponent.RecalculateMasking()` を呼んでいる。同じ現象が出るので同じ対策を入れる)
  - 一覧の行は使い回すので、行を再利用するたびに `field.text` を入れ直す(`copyField` と同じく、導出される値は毎回合わせる)
- [ ] **Step 4**: `woid` が空の行は、ポータルのボタンを出さず「ワールド情報なし」
- [ ] **Step 5**: スワイプ画面で気になるを数件付け、一覧に出る・順番・0件のときの表示(合意書に記載なし。「気になる投稿はありません」と出す案をユーザーに確認)を確認
- [ ] **Step 6**: コミット

**実施メモ(Task 7)**
- 実装: 一覧は ScrollRect + 使い回す8行(見えている分だけ実体。スクロール位置から先頭を決めて行を付け替える。行数の上限なし)。1行は2段「写真 | 名前・ワールド」「写真(続き) | WEB・高画質・(ポータル)」。写真は `SquareCropUv` の正方形、押すと拡大(閉じると一覧へ戻る)。順番は feed の順(気になるを付けた順ではない)。0件は「気になる投稿はありません」(合意書に記載なし、仮)。
- D4(コピペ方式): 計画の「読み取り専用」ではなく、NudeNyang の `copyField` と同じ「通常の InputField(文字は透明)+入力を終えたら元の値へ戻す(`OnCopyEnd` → 2フレーム後にも再設定)」にした。`ForceLabelUpdate()` と `RecalculateMasking()` も入れた。VRChat 実機での「入力UIが開いてコピーできる」ことは Client Sim では確認できない → Task 10 の実機確認に残す。
- ヘッダーの ♡ は `NotoSansJP-Bold SDF` に無く □ になるため、仮に ☆ にした(合意書の ♡ から見た目が変わる → 要確認。♡を使うなら画像アイコン化が要る)。
- ポータルのボタンは行に作ってあるが、Task 8 まで非表示(`portalEnabled=false`)。`woid` が空の行は「ワールド情報なし」。
- 検証: Play で 3件に気になる → 一覧で 3行・サムネ(縦写真は正立)・URL(`web/post/<poid>`・`api/images/<poid>`)・拡大→戻る・☆の開閉を確認(スクショ `screens/task7-list.png`)。未確認: 8行を超えるスクロール(件数が少なく)、0件の表示。

---

### Task 8: ポータル(方式A)

**Files:** Modify `NudeNyangEterPix.cs`、`EterPixAppBuilder.cs`、(Task 1 のパッチで済んでいなければ)`NudeNyangEterPixPatcher.cs`

- [ ] **Step 1**: Builder で `NudeNyangEterPix.portal` に、シーン内の既存 `NudeNyangTravelPortal` を接続する(無ければビルド時にエラー)
- [ ] **Step 2**: ボタン文言の遷移「ポータルを開く」→(押す)「ポータルを置く」→(置いた)「ポータル開いた」→元に戻る。`PlacementNotice(string)` で受け取り、`PlacementAllowed()` は「このアプリを表示中」のとき true
- [ ] **Step 3**: `Close()` / 別画面への遷移で `portal.CancelFor(this)` を呼ぶ(Travelと同じ。置きかけのポータルが残らないように)
- [ ] **Step 4**: 実機相当(VR/デスクトップ)で、床を指して配置できること、30秒の再配置待ち中に押したときの通知、他のプレイヤーに同期されることを確認(2アカウント or Client Sim の複数クライアント)
- [ ] **Step 5**: コミット

**実施メモ(Task 8)**
- NudeNyang 本体の `NudeNyangTravelPortal` は Travel 専用の型なので、パッチ **P10a〜P10i** で「置いているアプリ」に EterPix-app(`placingEterPix`)を足した(Travel の経路は変えない)。公開されるのは `eterApp` / `eterWorld` / `eterResult` / `EterPixBegin()` / `EterPixCancel()` / `BeginPlacementEterPix(string, NudeNyangEterPix)`。通知は `placing` / `opened` / `cancelled`。
- コンパイル順の鶏卵問題(アプリが未パッチの型を参照すると、パッチを当てるメニュー自体が出ない)を避けるため、アプリ側の `portal` は `UdonSharpBehaviour` 型で持ち、`SetProgramVariable` + `SendCustomEvent("EterPixBegin"/"EterPixCancel")` + `GetProgramVariable("eterResult")` で呼ぶ。
- Builder はシーン内の既存 `NudeNyangTravelPortal` に接続する。無い場合は警告ログを出し、ポータルのボタンは出さない(計画のStep 1の「ビルド時にエラー」から変更。エラーで止めるとポータル無しのシーンで配置自体ができなくなるため)。
- 文言: 「ポータルを開く」→「ポータルを置く」→「ポータル開いた」(3秒後に戻る)。置けない場合は「置けません」(3秒後に戻る)。置いている最中にもう一度押すと取消。一覧を離れる・アプリを閉じると `EterPixCancel`。
- 検証(Play): 一覧でポータルボタン → `placing`、再押下 → `cancelled`、置き始めて一覧を閉じる → `cancelled` を確認。**未確認(Task 10の実機確認へ)**: 床を指しての配置、`opened` の通知、30秒クールダウン中の `failed` 表示、他プレイヤーへの同期。

---

### Task 9: 状態表示の仕上げ

**Files:** Modify `NudeNyangEterPix.cs`

- [ ] **Step 1**: 通常 / 読み込み中 / URL未許可(「Allow Untrusted URLs」をONにしてください)/ サーバー停止 / 投稿0件 の文言を、Padの K-46 の文言に揃えて写真枠の中に出す。ヘッダーは常に表示
- [ ] **Step 2**: 各状態を、Untrusted OFF・ネットワーク切断・空フィード(モックのURLを差し替え)で再現して確認
- [ ] **Step 3**: コミット

---

### Task 10: 実機確認・上限調査・文書

**Files:** Create `Assets/EterPixNudeNyang/README.md`、Modify `V:\...\CLAUDE.md`(使い方・既知の制約)、統合管理リポジトリ `docs/projects/unity.md` と `docs/decisions.md`(決定の追記は統合管理の手順どおり、承認後)

- [ ] **Step 1**: VRChat にアップロードして実機のタブレットで一通り操作。**画像キャッシュの上限(`maxCached`)を、メモリ使用量とクラッシュ有無を見ながら決める**(合意書「投稿の件数」の未確認事項)
- [ ] **Step 2**: 既存モニター/Padと同居するワールドで、5秒制限の取り合いによる遅延を測る(D2 の代償の確認)
- [ ] **Step 3**: D3 の未決(サーバー保存・縦写真の見せ方・回転向き)の結果を反映する Task を追加する(未決のまま閉じない)
- [ ] **Step 4**: README(パッチ手順・NudeNyang更新時の再適用・VRCUrlの焼き込み)を `.md` で書き、BUGS(未修正) を CLAUDE.md に載せる
- [ ] **Step 5**: コミット・プッシュ

---

## Unity MCP での検証結果(2026-10-31. テンプレシーン `20260923-etp` を読み取りのみで確認)

| 項目 | 結果 | 計画への影響 |
|---|---|---|
| ホームに12個目が収まるか | **収まる**。アイコン位置は `left+(slot%columns)*gap, firstY-(slot/columns)*158`(`NudeNyangTabletShell.cs:1384`)で、シーンは6列×2段=12枠、現在9個使用 | Task 0 Step 3 は解決。見た目の相談は不要 |
| シーンのタブレット | `Tablet Services` が **3台**(`NudeNyangTablet_DockReady_Ivory` ×3)。それぞれ独自の `NudeNyangTabletShell` を持つ | EterPix-appは**タブレットごとに1つ**配線する。Builder は全 Shell を走査する |
| `homeIcons` / `appSprites` / `appColors` の長さ | **9**(AppCount=11 に未達。`screenShare`=null、`travel`=null、`booth` のみ設定済み) | 12個目の追加は、`ConfigureNewApps` の拡張(`Array.Resize` で AppCount まで伸ばす)に乗る。**10番目・11番目のダミーアイコンも一緒に作られる**ため、Travel/画面共有を使わないシーンでは空の枠が出ないか要確認(Task 1 Step 4 の確認項目に追加) |
| ポータル | シーンに `NudeNyangTravelPortal` は**0個**(`NudeNyangTravel` も0個) | Task 8 で、Travel を使わないシーンにもポータルを置く手段が要る。`NudeNyangTravelBuilder.ConnectServices` が作る「Travel portal position」+ `ConfigurePortal(anchor)` と同等の処理を我々のBuilderで呼ぶ(`internal` なのでパッチャで公開するか、同じ手順を複製する) |
| Builder のアイコン対応 | `NudeNyangTabletShellBuilder.cs:173,:215` が `i==7/8/9/else` の三項で名前と action を決めている | パッチ点: `i==11 ? "EterPix" : …` と `"OpenEterPix"` を両箇所に追加(`else` が `OpenTravel` なので、そのままだと 12 番目が Travel 扱いになる) |
| コンソール | エラー0件(検証前) | — |

## 実施記録

### Task 1(途中。2026-10-31)
- Step 1〜3 完了: パッチャ・最小アプリ・P1〜P4,P6〜P9 を適用(P5 は Travel と同じ経路で不要と判明し廃止)。コンパイルエラー0、再適用0件(冪等)
- **方針変更A(内部実装。見た目は変わらない)**: NudeNyang の Shell Builder(`Configure`)は `Icons()` が `Assets/NudeNyang/...` の固定パスを読むため、この配置(`Assets/アセット(同期しない)/NudeNyang/`)では例外で動かない。よって **自前の Builder でホームの12個目のタイルを作る**(`EterPixAppBuilder.EnsureHomeTiles`)。P9 は NudeNyang の Builder を将来誤って走らせた場合の保険として残す
- 10・11番目(Travel/画面共有)は、実行時のレイアウト処理が `homeIcons[i].anchoredPosition` を全要素に呼ぶため null にできない → **非表示の空タイル**で埋める。表示位置は `slots` 配列(11要素)の外の `i=11` なので `slot=11`(横6列の2段目右端)に出る
- U# の落とし穴: 新しい U# スクリプトには `UdonSharpProgramAsset` が必要(`EnsureProgram`)。作成直後は未コンパイルのため `AddUdonSharpComponent` が空の UdonBehaviour を作る → `CompileAllCsPrograms(true)` で非同期コンパイル後に、空のサービスを作り直す(Builder が自動で作り直す)
- 配線の成功を確認: スモーク用シーンで `builder: 1 台のタブレットに EterPix-app を配線しました homeIcons=12`
- **ブロッカー(未解決)**: スモーク用シーンの再生で NudeNyang の「タブレット準備」が `Assets/NudeNyang/Art/TabletMedia/MediaLineIcons.asset` の作成に失敗(`Parent directory must exist`、同じ固定パス問題)。NudeNyang の準備処理は、未準備のタブレット(prefab から新規配置したもの)を再生開始時に準備しようとする。テンプレシーンのタブレットは準備済みのためこの問題が出ていない可能性が高い
- 副作用の片付け: 私の最初の試行が作った `Assets/NudeNyang/`(`App0.png`)と、再生時に作られた `SearchUrls_*` オブジェクトを削除済み

- **検証用シーン**: テンプレシーンを `Assets/EterPixNudeNyang/Scenes/EterPixAppTemplateCopy.unity` に複製し、Builder で3台のタブレットに配線(`3 台のタブレットに EterPix-app を配線しました`)。元のテンプレには Builder を適用していない
- **事故の記録**: 再生の試行で NudeNyang が `SearchUrls_*` オブジェクトを自動生成し、私の `SaveScene` がテンプレシーン本体(`Packages/com.aliciliabb.eterpix/テンプレ_シーン/20260923-etp.unity`)に保存してしまい、さらに私が `Assets/NudeNyang/` を消したため参照切れになった。バックアップ(`%TEMP%\etp_backup60923-etp.template.on-disk.unity`)を取ってから、そのオブジェクト1つ(93行)だけを Unity 上で取り除いて保存し直した。ルート数は開始時と同じ16。再発防止: 以後 `SaveScene` は対象シーンのパスを明示し、テンプレシーンを再生・保存の対象にしない
- **ブロッカー(確認待ち)**: NudeNyang は再生開始・VRChatビルド時に `NudeNyangTabletSetup.PrepareIfNeeded()` を走らせ、編集セッション内のスナップショットと一致しないと「全体準備」を行う。この準備は `Assets/NudeNyang/...` の固定パスに資産を作るため、現在の配置(`Assets/アセット(同期しない)/NudeNyang/`)では失敗し、再生が中止される(`タブレットの準備を完了できませんでした`)。スクリプトの再読み込みやシーン変更でスナップショットは外れるため、**この配置のままでは再生・ビルドが通らなくなる恐れがある**

### Task 1 完了(2026-10-31. NudeNyang を `Assets/NudeNyang/` に入れ直した後)
- ユーザーが公式の配置 `Assets/NudeNyang/`(v1.0.2)へ入れ直した。旧い `アセット(同期しない)/NudeNyang/` は消えた。**NudeNyang の全体準備が 32.6秒で正常完了**(固定パス問題は解消)
- 新規インストールの素のファイルに、パッチのアンカー8件が全て一致 → 適用 → コンパイルエラー0 → U# 全体を再コンパイル
- `.gitignore` に `/Assets/NudeNyang/` と `/Assets/NudeNyang Package Installer/` を追加(元ファイルの再配布禁止のため)
- パッチャの `Root` は `Assets/NudeNyang` を優先し、無ければ旧配置を見る。Builder はパッチで足されるフィールド `eterpix` をリフレクションで設定する(素の製品でもコンパイルが通り、メニューからパッチを当てられるようにするため)
- Step 4 の確認(検証用シーン `EterPixAppTemplateCopy.unity`、ClientSim):
  - `GoHome` → `view=1`、`OpenEterPix` → `view=2 app=12`、`AppWindowVisible(12)=True`、パネル有効、`[EtpTrace] eterpix-app visible=True`
  - 3台とも `homeIcons=12` / `eterpix` 配線済み
  - **未確認**: アプリ画面の見た目(撮影では対象のタブレットが映らなかった。画面の中身が出来る Task 4 で撮影する)、既存アプリ(ライブラリ等)が従来どおり開くこと
- 再生時に出る `IsNetworkingSupported cannot be changed after the UdonBehaviour has been initialized` は VRChat SDK の `UdonManager.OnSceneLoaded` 由来で、本作業とは無関係

### Task 2 完了(2026-10-31)
- `EterPixAppFeed.cs`: `GET https://eterpix.com/api/vrc/v1/public` を `VRCStringDownloader` で取得し、投稿を配列(`Poid/Pode/UserLabel/WorldLabel/WorldId/Coid/Copo/Coro`)に展開。表示名の合成(`usna[称号]`、`wopf`+`wona`)は契約どおり。**R18(`po18==1`)はコード中に固定で除外**、`coid` が無い投稿も使わない。`Status`: 0読込中/1正常/2URL未許可/3サーバー停止/4 0件
- 本番で検証(ClientSim): `feed count=144 skippedR18=3 status=ok`。展開の統計: コラージュ26枚、`copo` 1〜6、`coro` 0/1/3、縦写真48件、ワールド名128件、称号付き投稿者名113件、投稿者名・`poid` の空は0
- **サーバーの 423 Locked**: 初回の取得で `423 Locked`(リング更新中)が返り、`5秒×2回` の再試行(既存 downloader と同じ予算)でも3回とも 423 になって `status=3` で終わった。約1分後の再取得は成功。curl でも「1回目 423 → 以降 200(83KB)」を確認。→ **Task 9 で、サーバー停止の表示に「もう一度取得」の手段を付ける**。再試行の予算を増やすかは Task 9 で決める(合意書に記載なし = 変える場合は確認する)
- 数字の含意(Task 3 へ): 144件 = コラージュ26枚。VRChatの5秒間隔なので全部取ると約130秒。**最初の数枚を優先して取り、起動画面の進行度は「先読み分」で出す**必要がある(合意書の「ロードは約4秒(モックの値)」と実機の差は未決として報告する)
- 配線: `EterPixAppBuilder` が `Feed` 子オブジェクトを作り `feedUrl` を焼く。新しいU#スクリプトを足したときは、Builder が プログラムアセットを作って `CompileAllCsPrograms` を呼び、コンパイル完了後にもう一度実行する流れ

### ライセンスと連絡先(確認済み)
- 取扱説明書(`guide.nudenyang.com`)は利用者向けで、アドオン開発手順の記載なし。ただし第26章に「SDKのダウンロードは5秒間隔のため画像をアトラスにまとめる」とあり、本計画のコラージュ取得と同じ前提
- 製品ライセンスの文面(ユーザー提示 2026-10-31): 追加アプリ・アドオンの制作と、そのための**製品の改変は許可**。配布は**自作ファイル + パッチ/インストールツール**で、製品の元ファイル・改変済み本体の再配布は不可。商品説明には「正規製品が別途必要」「対応バージョン」「アドオン制作者の連絡先」を明記し、公式製品・公式サポート対象と誤認させない。→ 本計画の設計(`Assets/EterPixNudeNyang/` + パッチャ、本体はgitignoreのまま)は適合。README(Task 10)に上記3点を書く
- NudeNyang 作者の連絡先: X `@NudeNyang_VRC`(ユーザー提示)。アドオン制作者の連絡先は `@_alicilia`(合意書の情報画面)で、README では両者を分けて書く

## 確認事項(未決だが計画には影響する)
- NudeNyangのテーマ(白/黒)と連動させるか(Task 4 は独立で仮置き)
- 「気になる」を再起動後も残すか(PlayerData)。合意書は「サーバー保存するか」が未決

## セルフレビュー結果
- 仕様網羅: 起動/スワイプ/拡大/一覧/ポータル/共通画面/状態表示/件数上限調査 → Task 4–10 に対応。WEB・高画質は D4 のコピペ方式で Task 7 に含めた
- 型の一貫性: `Status`(0–4)、`Count`、`Coid/Copo/Coro`、`liked`、`zoomMode`、`PlacementNotice/PlacementAllowed` を各Taskで同名で使用
- 弱点: NudeNyang本体(第三者・1行詰め・gitignore)へのパッチは、Task 0 の実測まで確定できない。Task 1 の縦串スモークで早期に可否が分かる構成にしてある
