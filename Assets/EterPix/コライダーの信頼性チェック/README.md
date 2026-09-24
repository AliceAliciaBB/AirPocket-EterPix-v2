# コライダーの信頼性チェック

トリガーコライダーにプレイヤーが入った瞬間・出た瞬間をテキストに表示し、
Enter/Exit の取りこぼしがないかを確認するためのデバッグ用スクリプト。

## 使い方

1. isTrigger = ON のコライダーを持つ GameObject に `ColliderReliabilityChecker` (UdonSharp) を追加する
2. インスペクターで以下を設定する
   - `Log Text` : 表示先の `TextMeshProUGUI`
   - `Max Lines` : 表示する直近ログの行数 (既定 20)
   - `Log Objects` : ON にするとプレイヤー以外の物体の出入りも記録する
3. 実行してコライダーに出入りする

## 表示内容

```
ENTER 3 / EXIT 2 / 中にいる 1
-----
18:52:10.123 f4521  ENTER  PlayerName [1] (自分)
18:52:08.456 f4400  EXIT   PlayerName [1] (自分)
```

- 1行目: 入った回数 / 出た回数 / その差(中にいる人数)。差がマイナスなら ENTER の取りこぼしを疑う
- ログ: 時刻(ミリ秒) / フレーム番号 / 種別 / プレイヤー名 [playerId]。新しいものが上
- `ClearLog` を SendCustomEvent で呼ぶとログとカウンタを消去する

## シーン上の構成 (20260923-etp)

```
Etp_テンプレート/EterpixRequester/EterpixMonitorUI (3)
└─ BoxCollider(isTrigger, 3.5×5×5m) + ColliderReliabilityChecker  ← 検査対象

コライダーの信頼性チェック
├─ Cube   … BoxCollider(isTrigger)。予備の検査用
└─ Canvas … Screen Space - Overlay (デスクトップ確認用。VRでは表示されない)
   └─ Text (TMP)  ← ColliderReliabilityChecker の表示先
```

## 注意

UdonBehaviour を別の GameObject に移動・追加・削除すると、VRCWorld の Network ID 表と
食い違ってビルドが `Failed to assign network IDs` で失敗する。
VRChat SDK > Utilities > Network ID Import and Export Utility で競合を解消するか、
不要になったエントリを削除すること。
