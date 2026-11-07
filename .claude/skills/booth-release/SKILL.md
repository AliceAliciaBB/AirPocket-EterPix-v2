---
name: booth-release
description: Use when the EterPix VRChat gimmick needs a new BOOTH release - the user asks to make a new zip / unitypackage for BOOTH, update the BOOTH product description (docs/booth-description.md), or says the gimmick changed (API URL, prefab, script) and the distributed package must be rebuilt.
---

# booth-release

EterPix ギミック (`Packages/com.aliciliabb.eterpix`) を BOOTH 配布用に書き出し、商品説明を更新する手順.

**重要**: 2026-10-06 の VPM 移行 (`a7210c6e`) で本体は `Assets/EterPix` から `Packages/com.aliciliabb.eterpix` に移った.
BOOTH の unitypackage は過去版と同じ **`Assets/EterPix/...` 配置** で配布する (VCC 版との二重導入・GUID 衝突を避け、購入者の更新時に置き場所が変わらないようにするため).
そのため Unity では Packages から書き出し、手順 2.5 で pathname を書き換える.

## 事前準備 (最初にやる)

- Unity で `V:\VRChatSDK-Unity\AirPocket-EterPix` を開いておく
- Unity MCP が Connected か確認する (`claude mcp list`). 未接続なら `/unity_mcp` を実行し、
  Unity 再起動 → `Window > Unity MCP` で Start Server → `claude --continue` → `/mcp` 承認 を行う.
  ポートは `Library/MCPForUnity/RunState/mcp_http_<port>.pid` のものと `.mcp.json` を合わせる
- main に未コミット変更が大量にあることが多い. 書き出し対象 (`Packages/com.aliciliabb.eterpix`) の変更だけを確認する

## 場所

| 用途 | パス |
|------|------|
| 配布元フォルダ (Unity) | `V:\VRChatSDK-Unity\AirPocket-EterPix\Packages\com.aliciliabb.eterpix` (配布物内では `Assets/EterPix`) |
| 出力先 (過去版もここ) | `V:\Etp-vrc-gimmick\` |
| 商品説明の正本 (git: eterpix_integrated_management の main) | `D:\git\eterpix_integrated_management\docs\booth-description.md` |

商品説明は統合管理リポジトリの main に置く (Unity側はブランチごとに文書が食い違うため). 編集はそのリポジトリの規則どおり専用 worktree で行う.
**VCC版だけの更新 (BOOTHのファイルを差し替えない更新) でも、商品説明の「バージョン」欄は更新する.**

## バージョン名

`YYYYMMDD-etp-vrc`。同日2回目以降は `YYYYMMDD-2-etp-vrc`, `-3-` …。
`V:\Etp-vrc-gimmick\` の既存ファイル名と被らないこと (上書き禁止).

## 手順

1. **書き出し前チェック**: `Packages/com.aliciliabb.eterpix` に古い URL / 変更漏れがないか grep する。
   例: API URL 変更時は旧パス (`api/vrc/public` 等) が 0 件であること。
   `git status -- Packages/com.aliciliabb.eterpix` で未コミット変更があればユーザーに伝える (そのまま書き出しに含まれる).
2. **unitypackage 書き出し (Unity MCP `execute_code`)**: Packages のまま一時ファイルに出す.
   ```csharp
   AssetDatabase.SaveAssets();
   AssetDatabase.Refresh();
   var path = "V:/Etp-vrc-gimmick/_raw_<版名>.unitypackage";
   AssetDatabase.ExportPackage("Packages/com.aliciliabb.eterpix", path, ExportPackageOptions.Recurse);
   return System.IO.File.Exists(path) ? ("ok " + new System.IO.FileInfo(path).Length) : "missing";
   ```
   `IncludeDependencies` は付けない (VRChat SDK 等を巻き込むため).
   2.5. **pathname 書き換え (bash)**: `Packages/com.aliciliabb.eterpix` → `Assets/EterPix` に直し、`package.json` は除外して再パックする.
   ```bash
   cd /v/Etp-vrc-gimmick
   RAW=_raw_<版名>.unitypackage; OUT=<版名>.unitypackage
   T=$(mktemp -d) && tar -xzf $RAW -C $T
   for d in $T/*/; do
     p=$(cat $d/pathname)
     case "$p" in
       */package.json) rm -rf $d; continue;;
     esac
     printf '%s' "${p/#Packages\/com.aliciliabb.eterpix/Assets\/EterPix}" > $d/pathname
   done
   tar -czf $OUT -C $T $(ls $T) && rm -rf $T $RAW
   ```
   ※ `pathname` は末尾改行なしで書く. 初回は手順 3 に加え、直近の過去版 (`20261018-etp-vrc` 等) とパス一覧を diff して差が意図どおりか確認する.
3. **中身の検証 (bash)**: パス一覧が `Assets/EterPix/...` のみ (`Packages/` や `package.json` が 0 件) であること、変更点が反映されていること。
   ```bash
   P=<版名>.unitypackage
   for d in $(tar -tzf $P | grep /pathname); do tar -xzOf $P "$d"; echo; done | sort
   ```
   特定アセットの中身は `tar -xzOf $P "<guid>/asset" | grep ...` で確認する.
4. **zip 化**: zip の中身は unitypackage 1 つだけ (過去版と同じ形).
   ```powershell
   Compress-Archive -Path "V:\Etp-vrc-gimmick\<版名>.unitypackage" -DestinationPath "V:\Etp-vrc-gimmick\<版名>.zip"
   ```
5. **商品説明の更新** (`docs/booth-description.md`):
   - メタデータの `更新日` を今日に
   - 「バージョン」欄の先頭に `<版名> : <利用者向けの変更内容>` を追加 (新しい順)
   - 旧版が動かなくなる変更 (API 形式変更など) のときは、「## 内容」の先頭に
     `【重要】<直前の版名> 以前のバージョンをお使いの方へ` の告知を置く。
     不要になった古い告知は削除する
   - 文面は BOOTH にそのまま貼る前提: 利用者向けの平易な日本語、内部実装の用語は最小限
6. **コミット・プッシュ**: `D:\git\eterpix_integrated_management` の専用 worktree で `docs/booth-description.md` だけを add してコミットし、`origin/main` へ反映する (マージはユーザーの完了指示後).
7. **報告**: 出力ファイル名とサイズ、検証結果、「## 内容 以下を BOOTH に貼り、ダウンロードファイルを差し替える」旨を伝える.

## よくある間違い

- 同名ファイルへの上書き → 過去版が消える。必ず新しい版名にする
- zip に unitypackage 以外 (README 等) を入れる → 過去版と形が変わる
- 検証を省いて報告する → 必ず手順3の結果を根拠にする
- 旧記述の `Assets/EterPix` をそのまま `ExportPackage` / grep / git status に使う → 現在は存在しないので空振りする. 配布元は Packages
- 商品説明を Unity リポジトリや `V:\Etp-vrc-gimmick\` のコピーで編集する → 正本は eterpix_integrated_management の main (専用 worktree で編集)
- 手順 2.5 を飛ばして Packages 配置のまま配布する → VCC 版と二重導入になる
- 調べずに「ファイルが無い」と断定する → 現在のブランチだけでなく `origin/main` と worktree も確認する
