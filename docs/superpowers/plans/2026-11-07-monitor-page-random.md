# モニター: ページ表示の3桁予約 + ランダムボタン 実装計画

## 目的
1. ヘッダーの `pageLabel`(現在 / 全体)を、現在・全体をそれぞれ3桁分の幅で予約し、左寄せにする(桁数が変わっても位置がずれない).
2. リロードボタンの右隣に、サイコロアイコンのランダムボタンを追加する. 押すと全体から1枚をランダムに表示する.

## 前提(調査結果)
- 編集対象は `Packages/com.aliciliabb.eterpix/Prefabs/EterpixMonitor_Stack.prefab` と `EterpixMonitor_Split.prefab` の本体(builder は凍結中のため触らない).
- スクリプトは `Packages/com.aliciliabb.eterpix/Scripts/eterpix_monitor.cs`. ページ表示は `UpdatePagingLabel`(`$"{slotInWindow + 1} / {count}"`)、ページ移動は `MovePage`(`_syncedPageIndex` を同期).
- Header は幅528固定(写真幅と同じ)で、現在の並びは次のとおり(Stack / Split とも同じ座標).

| 要素 | x | 幅 |
|---|---|---|
| tx_PageLabel | 184 | 160 |
| btn_ReloadButton | 344 | 40 |
| btn_ThemeButton | 392 | 40 |
| btn_GroupButton | 440 | 40 |
| btn_InfoButton | 488 | 40 |

- 右端が 528 でぴったりのため、ボタンを足す余地は pageLabel の幅(160)から取る.

## 配置方針(見た目が変わる点. ここは承認が必要)
- pageLabel: 幅 160 → 112(184〜296). 16pt で「999 / 999」は約90pxに収まる想定.
- btn_ReloadButton: x 344 → **296**(48左へ).
- btn_RandomButton(新規): x **344**, 幅40(リロードの右隣).
- Theme / Group / Info は動かさない.
- 結果: リロードボタンの位置が48だけ左に動く. これが嫌なら代案は「Theme / Group / Info を右へ48ずらしてヘッダー幅を576に広げる」だが、写真幅(528)と揃わなくなるため推奨しない.

## ステップ
**Step 1. アイコン追加**
- `D:\ダウンロード-2\dice-solid (1).png` を `Packages/com.aliciliabb.eterpix/UI/icon_random.png` にコピーする.
- `icon_reload.png.meta` と同じ import 設定(Sprite 等)で `.meta` を用意する.

**Step 2. スクリプト(eterpix_monitor.cs)**
- 2-1. `UpdatePagingLabel` を、現在・全体を各3桁分の幅で固定する文字列に変える(TMP の `<mspace>` で数字を等幅にし、`<pos>` でスラッシュと全体の開始位置を固定する).
- 2-2. `public void PageRandom()` を追加する.
  - `_requester == null` または `VisibleCount <= 0` のときは何もしない.
  - `count >= 2` のときは今のページと違う値を選ぶ. `count == 1` のときは0のまま.
  - 次へ / 前へと同じく、`Networking.SetOwner` → `_syncedPageIndex` 更新 → `RequestSerialization()` → `RefreshDisplay()`(全員に同期).
- 2-3. インポートまで済ませ、コンソールのエラーを確認する.

**Step 3. prefab(Stack / Split の両方)**
- 3-1. `tx_PageLabel`: 左寄せ(`m_HorizontalAlignment` を Left)、幅112、リッチテキストは有効のまま.
- 3-2. `btn_ReloadButton` を x=296 へ移動する.
- 3-3. `btn_RandomButton` を新規追加する(`btn_ReloadButton` を複製して作る. 灰色ボタン、40x40、x=344、子に白アイコン `icon_random`、`PageRandom` を呼ぶ UdonBehaviour のイベント配線).

**Step 4. 確認**
- Unity で Stack / Split を開き、pageLabel が 1桁 / 2桁 / 3桁 で位置ずれしないことをスクリーンショットで確認する.
- ランダムボタンの押下で表示が切り替わることを確認する(Play モード. できない場合はその旨を報告).
- `EterPixAppTemplateCopy.unity` 側の prefab インスタンスに上書きが残っていないか確認する.

**Step 5. 後始末**
- 完了報告で、計画との差分と未完了ステップを報告する.
- 区切りでコミットし、main へ push する.

## 対象外
- builder(`eterpix_monitor_builder.cs`)の更新.
- ランダムボタンのクールダウン.
- ページ表示以外のヘッダー要素の見た目変更.
