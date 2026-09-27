# 表示範囲の判定を EterpixRequester にまとめる

日付: 2026-09-27

## 目的

モニターごとに持っていた表示範囲トリガーをやめ、`EterpixRequester` 1か所で「ローカルプレイヤーが範囲内にいるか」を判定する。
範囲内にいるときだけ、配下の全モニターが写真(コラージュ画像)を読み込み、範囲外に出たら解放する。

あわせて、現在の既知の不具合「モニターの `ViewRange` が `EditorOnly` タグのためビルドで削除され、実機では常に画像を読み込む」を解消する。

## 現状

- Stack / Split モニターは子に `ViewRange`(トリガーの BoxCollider + `eterpix_monitor_trigger`)を持つ。
  旧モニター `Prefabs/Legacy/EterpixMonitorUI.prefab` も子 `collider` に同じ構成を持つ。
- `eterpix_monitor_trigger` がローカルプレイヤーの出入りを親の `eterpix_monitor` へ中継する(モニターのルートは VRCUiShape が BoxCollider を使うため、子に分けていた)。
- どちらも `EditorOnly` タグのため、ビルドでは削除されている。

## 設計

### 1. 範囲判定(`eterpix_requester`)

- `EterpixRequester` の GameObject 自体にトリガーの BoxCollider を付ける。
  - レイヤーは Ignore Raycast(2)。UI を指すレーザーを遮らないため。
  - タグは付けない(`EditorOnly` にしない)。
  - Requester には VRCUiShape が無いので、中継スクリプトは不要。`eterpix_requester` が直接 `OnPlayerTriggerEnter` / `OnPlayerTriggerExit` を受け取る。
- ローカルプレイヤーのときだけ反応する(`Utilities.IsValid(player) && player.isLocal`)。
- 状態を `IsLocalPlayerInRange`(読み取り専用プロパティ)として保持する。
- 状態が変わったときだけ、配下の全モニターへ `SetInViewRange(bool)` を送る。
- Requester の `OnDisable` では状態を「範囲外」に戻す(テレポート等で Exit が届かない場合の保険)。
  配下のモニターも同時に非アクティブになり、各モニターの `OnDisable` でテクスチャが解放されるため、Requester からの通知は不要。

### 2. モニター(`eterpix_monitor`)

- 自前のトリガー連携(`BindViewRangeTriggers`、`OnViewRangeEnter` / `OnViewRangeExit`)を削除し、`SetInViewRange(bool inRange)` 1本にする。
  - `true`: `_isInViewRange = true`。表示中の投稿があり未要求なら `RequestTexture` する(現在の `OnViewRangeEnter` と同じ処理)。
  - `false`: `_isInViewRange = false`。`ReleaseCurrentTextureIfAny()`(現在の `OnViewRangeExit` と同じ処理)。
- 通知の取りこぼし対策として、次のタイミングで Requester の `IsLocalPlayerInRange` を読み直す。
  - `Init()`: 範囲内でスポーンした等、Init より前に Enter が起きていた場合。
  - `OnEnable()`: モニターが非表示の間に範囲へ出入りしていた場合。
- `debugIgnoreTriggerRange`(範囲を無視して常に読み込む診断用フラグ)は残す。

### 3. 削除するもの

- `Scripts/eterpix_monitor_trigger.cs`(と `.asset`・`.meta`)
- `eterpix_monitor_builder.cs` の `ViewRange` 生成処理(Stack / Split を作り直すと消える)
- `Prefabs/Legacy/EterpixMonitorUI.prefab` の子 `collider`
  (スクリプト削除より先に消す。逆順だと Missing Script が残る)

### 4. prefab と範囲の大きさ

- `Prefabs/EterpixRequester.prefab` に上記の BoxCollider を追加する。
- 大きさはワールド制作者が BoxCollider で任意に調整する(手動設定)。
- テンプレート(`Etp_テンプレート.prefab`)の初期値は、これまでの各モニターの範囲(1台あたり幅0.875m・高さ1.25m・画面の前方1.25m。モニターのscale 0.00125 で換算)を8台分まとめて覆う箱にする。
  実装時の値: size (6.125, 1.2975, 1.25)、center (2.625, -0.4038, -0.625)。
- `EterpixRequester.prefab` 単体の初期値は、モニター1台(Stack)を原点に置いた場合の範囲: size (0.875, 1.25, 1.25)、center (0, -0.4275, -0.625)。

### 実装時に追加した変更

- `debugIgnoreTriggerRange` が Stack / Split / Legacy / テンプレートの全モニターで `true`(常に読み込む)になっていた(builder が旧 prefab の値を再現していた)。範囲判定を効かせるため `false` にし、builder も `false` を設定するよう直した。

### 5. ドキュメント

- `Assets/EterPix/README.md`
  - 使い方に「`EterpixRequester` の BoxCollider が写真を読み込む範囲。任意でサイズ調整できます」と書く。
  - 注意欄の「`ViewRange` はビルドで削除される(既知の不具合)」を削除する。
- プロジェクトの CLAUDE.md の BUGS に同じ項目があれば削除する。

## 確認方法(ClientSim)

1. プレイヤーを範囲の外に置いて開始し、どのモニターも画像を要求しない(`_hasRequestedTexture == false`)ことを確認する。
2. 範囲に入ると、配下の全モニターが画像を要求・表示することを確認する。
3. 範囲から出ると、全モニターが画像を解放することを確認する。
4. 範囲内でモニターを非表示にし、再表示すると画像を要求し直すことを確認する。
5. Stack / Split / Legacy の各 prefab に `eterpix_monitor_trigger` と Missing Script が残っていないことを確認する。

## 範囲外(今回はやらない)

- 範囲の大きさを自動で計算する機能(エディタのボタン、実行時の計算)
- 複数の BoxCollider の組み合わせによる範囲(1つの BoxCollider のみ対応)
