# ポータルを開く/閉じる トグル化 設計

- 日付: 2026-10-02
- 対象: v2 (`Assets/EterPix/Scripts/`) の `eterpix_monitor` と共通ポータル `eterpix_porta_resize`
- 状態: 計画のみ(未実装)

## 目的

モニターの「ポータルを開く」ボタンを、押すたびに「ポータルを開く」⇔「ポータルを閉じる」が切り替わるトグルにする。
一般の利用者の直感(開いたものは同じボタンで閉じられる)に合わせるため。

## 現状

- ボタン `acc_PortalButton`(ラベル `acctx_PortalLabel`)の OnClick → `eterpix_monitor.OpenPortal()`。
- `OpenPortal()` は共通ポータル(シーンに1つの `EterpixPortal` = `eterpix_porta_resize`)を
  `portal.SetParentObject(portalSpawnPoint, worldId)` でそのモニターの `PortalSpawnPoint` へ呼び寄せる。
- ポータルの処理はローカルのみ(`eterpix_porta_resize` は `BehaviourSyncMode.None`)。
- 閉じる手段は、`portal_clause_distance` 以上離れたときの自動非表示だけ。
- 位置の下方向オフセット(`offset × siz_value`)と大きさは、`SetParentObject` の0.2秒後の `ApplyScale` で同じフレームに適用している
  (`RefreshPortal` 直後に動かすとポータルの挙動がおかしくなるため)。
- 同じ共通ポータルを旧系統(`eterpix_item` / `eterpix_cell`)も `SetParentObject` で使っている。

## 仕様

| 状況 | 挙動 |
|---|---|
| このモニターで閉じている状態でボタンを押す | 今までどおり開く。ラベルは「ポータルを閉じる」 |
| このモニターで開いている状態でボタンを押す | 閉じる。ラベルは「ポータルを開く」 |
| 開いたまま、そのモニターのページが変わる(自分/他人のページ送り、再読み込み) | 閉じずに、位置・大きさはそのままで **world_id だけ** 新しいページのワールドに差し替える |
| 変わった先のページにワールド情報が無い | 閉じる(ボタンごと非表示になり閉じる手段が無くなるため) |
| 変わった先のページが同じワールド | 何もしない(`RefreshPortal` も呼ばない) |
| モニターAで開いた後、モニターBで開く | ポータルはBへ移動。Aのラベルは「ポータルを開く」に戻る |
| 離れて自動で非表示になる | 開いたモニターのラベルは「ポータルを開く」に戻る |

## 実装方針

### 1. `eterpix_porta_resize`(グローバル名前空間、旧系統と共用)

- `SetParentObject` に開いたモニターを渡せるようにし、`UdonSharpBehaviour _opener` として覚える。
  - 旧系統の呼び出し(`SetParentObject(Transform, string)`)はそのまま残し、`_opener = null` で動く。
  - v2 用にオーバーロード `SetParentObject(Transform parent, string roomId, UdonSharpBehaviour opener)` を追加する
    (UdonSharp ではinterfaceを使わず、型ではなくイベント名で通知する)。
- 追加するメソッド:
  - `Close()`: ポータルを非表示にし、前の `_opener` に通知して `_opener = null`。
  - `IsOpenAt(Transform spawn)`: `portal.gameObject.activeSelf && transform.parent == spawn`。
  - `ChangeWorld(string roomId)`: 位置・大きさは変えず `portal.roomId` を差し替えて `RefreshPortal()` のみ。
- 状態が変わったら、前の `_opener` に `SendCustomEvent("OnPortalStateChanged")` で知らせる:
  - 距離による自動非表示(`Update`)
  - 別のモニター(または旧系統)から `SetParentObject` された
  - `Close()`

### 2. `eterpix_monitor`

- `OpenPortal()` を `TogglePortal()` に変更する。
  - `portal.IsOpenAt(spawn)` なら `portal.Close()`、そうでなければ従来どおり開く(opener に自分を渡す)。
- `[SerializeField] TMP_Text portalLabel` を追加し、「ポータルを開く」/「ポータルを閉じる」を切り替える。
  - 更新タイミング: `OnPortalStateChanged()` を受けたとき、`TogglePortal()` の後、`ApplyPost` のとき。
- `ApplyPost` で、このモニターで開いていれば新しいページの `woid` で:
  - 空 → `portal.Close()`
  - 開いているワールドと同じ → 何もしない
  - 違う → `portal.ChangeWorld(woid)`
- `ShowStatus`(読み込み中/エラー表示)に切り替わったときも、このモニターで開いていれば閉じる。

### 3. prefab(builder は凍結中。prefab 本体を Unity MCP で部分編集する)

- `EterpixMonitor_Stack.prefab` / `EterpixMonitor_Split.prefab`:
  - `eterpix_monitor.portalLabel` に `acctx_PortalLabel` を紐付ける。
  - `acc_PortalButton` の OnClick の `SendCustomEvent` 引数を `OpenPortal` → `TogglePortal` に変更する。
  - 情報ウィンドウの説明文「ワールドへ行く」に「もう一度押すと閉じます」を追記する。
- シーン(テンプレ)内のインスタンスで OnClick を上書きしていないか確認する。

## 確認項目(ClientSim)

- [ ] 開く → 閉じる → 開く でラベルとポータルが切り替わる
- [ ] 開いたままページ送りすると、ポータルの位置は変わらず world_id だけ差し替わる
- [ ] ワールド情報の無いページへ送ると閉じる
- [ ] モニターAで開いた後にBで開くと、Aのラベルが「ポータルを開く」に戻る
- [ ] 離れて自動で消えたとき、ラベルが「ポータルを開く」に戻る
- [ ] 旧系統(`eterpix_item` / `eterpix_cell`)から開いた場合も従来どおり動く

## リスク

- `RefreshPortal` まわりの VRChat ポータルの挙動は不安定な前例がある(オフセットを `ApplyScale` へ移した件)。
  `ChangeWorld` は位置・大きさを動かさず `roomId` 差し替え + `RefreshPortal()` のみにするが、実機(VRChat)でしか確認できない部分が残る。
- 配布後に BOOTH の商品説明(「★ワールドへ行く」)と、`booth-release` の手順で版を上げる。
