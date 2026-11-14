# v2 UI のビルダー(開発用・配布しない)

v2 UI の prefab(`Packages/com.aliciliabb.eterpix/UIv2/Prefabs/Etp2_*.prefab`)は、このフォルダのビルダーで作ります.
**ビルダーのコードが正**で、prefab は生成物です. prefab を手で直さないでください(次のビルドで消えます).

- 計画: `docs/superpowers/plans/2026-11-14-unity-ui-v2.md`
- 元のモック: 統合管理 `docs/plans/unity-ui-v2-mock.md`(数字・色・部品)と `docs/mocks/unity-ui-v2-mock.html`(動き)

## メニュー

| メニュー | すること |
|---|---|
| EterPix → UIv2 → prefab をビルド | 6つの prefab を作り直す. 中身が同じなら保存しない(2回ビルドしても差分ゼロ) |
| EterPix → UIv2 → テストシーンを作り直す | `Etp2_Test.unity` を作り直す(4画面・配線・受け渡しパネルのデバッグボタン付き). 開いているシーンには触らない |

## モックが変わったときの直し方

1. 統合管理の `unity-ui-v2-mock.md` の変更を読む
2. 数字・色・文言・ダミーデータだけの変更 → `Editor/Etp2Spec.cs` を直す(章番号をコメントに書いてある). 色は実行時と共通の `Packages/.../UIv2/Scripts/etp2_theme.cs` の `GetPresetColor`
3. 形が変わる → `Editor/Etp2BuilderScreens.cs`(画面ごと)、`Editor/Etp2Builder.cs`(骨組み)、`Editor/Etp2BuilderParts.cs`(部品)を直す
4. 動きが変わる → `Packages/.../UIv2/Scripts/etp2_*.cs`(U#)を直し、インポートとU#のコンパイルを済ませる
5. メニュー「prefab をビルド」→ コンソールの `[Etp2Trace] builder:` で、保存された prefab を確かめる
6. テストシーンで Play して確かめる(ClientSim. コンソールに `[Etp2Trace]` が出る)

## ビルドで消えないもの

ビルダーは prefab の**ルートのオブジェクトとルートの部品は残し、子だけ作り直す**. なので、シーンに置いたインスタンスの位置・回転・縮尺と、ルートの部品の「ワールド作者がつなぐ」欄は残ります. ワールド作者が触る設定は、必ずルートの部品に置いてください.

## 気をつけること

- 共有フォント `NotoSansJP-Bold SDF` のアトラスは満杯で、新しい字を足せない. ◀▶ は文字ではなく三角のアイコン(`Etp2Parts.ArrowLabel`)で出している
- U# で使えなかったもの: `TMP_Text.GetPreferredValues`・`preferredWidth`・`rectTransform`. 文字の大きさは ContentSizeFitter に任せ、1フレーム後に `RectTransform.rect` を読む(ホバーの札・一覧の写真説明)
- `Etp2Parts.LE(高さだけ指定)` は最小の高さにも同じ値を入れる. 実行時に低くする箱は最小を 0 にする
