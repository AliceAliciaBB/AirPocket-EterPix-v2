---
name: booth-release
description: Use when the EterPix VRChat gimmick needs a new BOOTH release - the user asks to make a new zip / unitypackage for BOOTH, update booth_商品説明.md, or says the gimmick changed (API URL, prefab, script) and the distributed package must be rebuilt.
---

# booth-release

EterPix ギミック (`Assets/EterPix`) を BOOTH 配布用に書き出し、商品説明を更新する手順.

## 場所

| 用途 | パス |
|------|------|
| 配布元フォルダ (Unity) | `V:\VRChatSDK-Unity\AirPocket-EterPix\Assets\EterPix` |
| 出力先 (過去版もここ) | `V:\Etp-vrc-gimmick\` |
| 商品説明 (git: eterpix-v10) | `D:\git\eterpix-v10\docs\booth_商品説明.md` |

## バージョン名

`YYYYMMDD-etp-vrc`。同日2回目以降は `YYYYMMDD-2-etp-vrc`, `-3-` …。
`V:\Etp-vrc-gimmick\` の既存ファイル名と被らないこと (上書き禁止).

## 手順

1. **書き出し前チェック**: `Assets/EterPix` に古い URL / 変更漏れがないか grep する。
   例: API URL 変更時は旧パス (`api/vrc/public` 等) が 0 件であること。
   `git status -- Assets/EterPix` で未コミット変更があればユーザーに伝える (そのまま書き出しに含まれる).
2. **unitypackage 書き出し (Unity MCP `execute_code`)**:
   ```csharp
   AssetDatabase.SaveAssets();
   AssetDatabase.Refresh();
   var path = "V:/Etp-vrc-gimmick/<版名>.unitypackage";
   AssetDatabase.ExportPackage("Assets/EterPix", path, ExportPackageOptions.Recurse);
   return System.IO.File.Exists(path) ? ("ok " + new System.IO.FileInfo(path).Length) : "missing";
   ```
   `IncludeDependencies` は付けない (VRChat SDK 等を巻き込むため).
3. **中身の検証 (bash)**: パス一覧が `Assets/EterPix/...` のみであること、変更点が反映されていること。
   ```bash
   P=<版名>.unitypackage
   for d in $(tar -tzf $P | grep /pathname); do tar -xzOf $P "$d"; echo; done | sort
   ```
   特定アセットの中身は `tar -xzOf $P "<guid>/asset" | grep ...` で確認する.
4. **zip 化**: zip の中身は unitypackage 1 つだけ (過去版と同じ形).
   ```powershell
   Compress-Archive -Path "V:\Etp-vrc-gimmick\<版名>.unitypackage" -DestinationPath "V:\Etp-vrc-gimmick\<版名>.zip"
   ```
5. **商品説明の更新** (`booth_商品説明.md`):
   - メタデータの `更新日` を今日に
   - 「バージョン」欄の先頭に `<版名> : <利用者向けの変更内容>` を追加 (新しい順)
   - 旧版が動かなくなる変更 (API 形式変更など) のときは、「## 内容」の先頭に
     `【重要】<直前の版名> 以前のバージョンをお使いの方へ` の告知を置く。
     不要になった古い告知は削除する
   - 文面は BOOTH にそのまま貼る前提: 利用者向けの平易な日本語、内部実装の用語は最小限
6. **コミット・プッシュ**: `D:\git\eterpix-v10` で `booth_商品説明.md` だけを add してコミット・プッシュ.
7. **報告**: 出力ファイル名とサイズ、検証結果、「## 内容 以下を BOOTH に貼り、ダウンロードファイルを差し替える」旨を伝える.

## よくある間違い

- 同名ファイルへの上書き → 過去版が消える。必ず新しい版名にする
- zip に unitypackage 以外 (README 等) を入れる → 過去版と形が変わる
- 検証を省いて報告する → 必ず手順3の結果を根拠にする
