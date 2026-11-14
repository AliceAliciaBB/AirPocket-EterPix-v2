# AirPocket-EterPix (Unity / VRChatワールドギミック, U#)

## このプロジェクトについて
- EterPix の VRChat ワールド側ギミック(UdonSharp)。Web側(`D:\git\eterpix-v10`, Cloudflare Workers)が出す vrc-api v1 の JSON を読む側.
- U#はC#と異なる制約が多い. 詳細は統合管理の `docs/projects/unity.md`.

## 関連リポジトリ(統合管理)
- 統合管理(仕様・設計・決定記録の正本): `D:\git\eterpix_integrated_management`
- 正本は `origin/main`. 作業前に `git -C D:\git\eterpix_integrated_management fetch -q` を実行し、
  `git show origin/main:docs/decisions.md` のように読む(フォルダを直接開かない. worktree で古い内容が見えるため).
- 読み方の詳細: `docs/child-repo-guide.md`(上記の `origin/main` から `git show` で読む)
- 統合管理と実装が食い違う場合は、黙って従わずユーザーに報告する.
- vrc-api JSON を使う実装の前に、`docs/impact-matrix.md` と `docs/contracts/vrc-api-v1-schema.md` を確認する.
- Web実装(`D:\git\eterpix-v10`)にも仕様の実体がある. 統合管理の文書だけで仕様を断定しない.
- 作業前に `git worktree list` で、今いるフォルダを確認する.

## BUGS (未修正)
- [ ] 共有フォント `NotoSansJP-Bold SDF` のアトラスが満杯(4096×2048・1枚・multi atlas 無効)で、新しい字を足せない. アトラスに無い字は □ になる. v2 の ◀▶ はアイコンで回避済み. 実データのユーザー名などで □ が出るおそれ (発見日: 2026-11-14)
- [ ] Play 開始時に UdonManager.OnSceneLoaded で `InvalidOperationException: IsNetworkingSupported cannot be changed after the UdonBehaviour has been initialized` が1件出る(Etp2_Test で確認. 動きへの影響は見つかっていない. 原因未調査) (発見日: 2026-11-14)
- [ ] v2 UI のアイコン(`UIv2/Sprites/etp2_icon_*.png`)はモックの仮の SVG から作った. 形の出典とライセンス表記を、配布前に確かめる (発見日: 2026-11-14)
