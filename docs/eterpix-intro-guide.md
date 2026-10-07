# EterPix ギミック導入ガイド

EterPixは、外部サービス(eterpix.com)の写真投稿をVRChatワールド内に自動表示するギミックです.
写真・キャプション・ポータルリンクをまとめたモニターをワールドに配置できます.

---

## 第1章: 用語集

### 1.1 VRChat / UdonSharp 関連 (EterPix の理解に必要な最小限)

| 用語 | 説明 |
|------|------|
| **UdonSharp** | VRChat向けのC#ライクなスクリプト言語。通常のC#と違い、いくつかの制限がある(後述) |
| **UdonBehaviour** | UdonSharpスクリプトがアタッチされたコンポーネントの総称 |
| **BehaviourSyncMode** | ネットワーク同期の方式。`Manual`=明示的に同期、`None`=同期しない |
| **VRCUrl** | VRChat内で使える外部URL型。実行時に文字列で生成できないため、エディタで事前に登録する必要がある |
| **VRCPortalMarker** | ワールド内にVRChatのワールドポータルを表示するコンポーネント |
| **Network ID** | VRChatがUdonBehaviourを識別する内部ID。追加・削除・移動すると食い違いが起き、ビルドエラーになることがある |

### 1.2 EterPix 固有の用語・概念

| 用語 | 説明 |
|------|------|
| **Downloader** | eterpix.comへのHTTPリクエストを一元管理するシングルトン。シーンに1つだけ置く。GameObjectの名前は`EterpixDownloaderV2`にする |
| **Requester** | 特定のフィード(投稿URLセット)を担当するコンポーネント。DownloaderからJSONと画像を受け取る |
| **Monitor** | 写真・キャプション・ポータルを表示するUIパネル。1つのRequesterの配下に複数置ける |
| **Portal** | 「ポータルを開く」ボタンで呼び寄せられるVRChatワールドポータル。シーンに1つだけ`EterpixPortal`という名前で置く |
| **vrcurllist** | 画像URLを事前登録しておくリスト用コンポーネント。`baseUrl`を設定するとエディタが自動で256件のURLを生成する |
| **コラージュ画像(2×3)** | EterPixサーバーが配信する、6枚の写真を2列×3行に並べた合成画像。1枚のVRCUrlで6枚ぶんをまとめて取得する |
| **フィード** | eterpix.comの投稿ストリーム。パブリック投稿と自分の投稿(指定IDのみ)を選べる |
| **テーマ** | モニターの配色セット。`bg_` / `tx_` / `btn_` / `acc_` / `acctx_` という名前接頭辞を持つ子オブジェクトに自動適用される |
| **表示範囲トリガー** | Requesterに付けるBoxCollider(isTrigger)。この範囲内にプレイヤーがいるときだけ画像を読み込む |

---

## 第2章: 導入前の準備

### 2.1 必要なパッケージ・依存関係

EterPixを使うには以下が必要です.

| パッケージ | 備考 |
|-----------|------|
| VRChat SDK — Worlds | VCC経由でインストール |
| UdonSharp | VCC経由でインストール |
| TextMeshPro | UnityのPackage Managerに含まれる |
| EterPix | VCCリポジトリから追加(次節) |

### 2.2 VCCへのEterPixリポジトリ追加とインストール

1. VCC (VRChat Creator Companion) を開く
2. **Settings → Packages → Add Repository** を選択
3. EterPixのVPMリポジトリURLを入力して追加
4. 対象プロジェクトの **Manage Packages** を開き、EterPixを **Add** する
5. Unityが再コンパイルを完了するまで待つ

> **確認ポイント:** Unityのコンソールにエラーが出ていないこと.
> `Packages/ali.eterpix/` フォルダが作成されていればインストール成功.

---

## 第3章: 基本的な導入手順

### 3.1 Prefabの配置

EterPixには以下のPrefabが含まれています. 用途に合わせて配置してください.

| Prefab | 説明 |
|--------|------|
| `Etp_テンプレート` | Downloader・Requester・Monitor・Portalがセットになったセット一式. 初回導入はこれを使う |
| `EterpixDownloaderV2` | Downloaderのみ. 複数フィードを追加する際などに使う |
| `EterpixRequester` | Requesterのみ |
| `EterpixMonitor_Stack` / `_Split` / `_EterpixTheme` | モニターのデザインバリアント |
| `EterpixPortal` | ポータル単体 |

**基本的な配置手順:**

1. `Etp_テンプレート.prefab` をシーンにドラッグ&ドロップ
2. 位置・回転・スケールをワールドに合わせて調整
3. 次節のInspector設定を行う

> **注意:** シーン内に`EterpixDownloaderV2`は**1つだけ**、`EterpixPortal`も**1つだけ**にすること.
> UdonBehaviourを別のGameObjectへ移動・コピーすると、Network IDの競合でビルドが失敗することがある.

### 3.2 EterPix固有の設定項目と意味

#### Requester の設定

| 設定項目 | 説明 |
|---------|------|
| **取得先 (urlPreset)** | `パブリック投稿` = eterpix.comの公開フィード. `自分で入力` = 任意のフィードURL |
| **requestUrl** | `自分で入力`選択時のJSONフィードURL |
| **urlList (vrcurllist)** | 画像URLリスト. `baseUrl`を設定すると自動生成される(手動編集不要) |

> **URL同期について:** `requestUrl`を変更した後、Playを開始すると自動でURLリストが同期される.
> 手動で実行する場合は Unity メニューの `ali / eterpix / Sync URL Lists from requestUrl` を使う.

#### Monitor の設定

| 設定項目 | 説明 |
|---------|------|
| **image** | 写真を表示する`RawImage`コンポーネント |
| **prevButton / nextButton** | ページ送りボタン |
| **pageLabel** | `現在ページ/合計ページ`表示 |
| **userNameText** | 投稿者名 |
| **descriptionText** | 投稿説明文 |
| **worldContextRoot** | ワールド情報エリア(ワールド投稿でない場合は非表示) |
| **worldNameText / worldDescriptionText** | ワールド名・説明 |
| **openPortalButton** | 「ポータルを開く」ボタン |
| **informationButton** | ⓘボタン(サービス説明ウィンドウの開閉) |
| **informationWindowRoot** | サービス説明ウィンドウのルートGameObject |

> **ボタンのOnClick登録:** UdonSharpの制約により、コードからボタンにイベントをバインドできない.
> 各ボタンのInspector上の`OnClick()`に、このMonitorコンポーネントを**手動でドラッグ**して関数を登録すること.
>
> | ボタン | 登録する関数 |
> |--------|------------|
> | prevButton | `PagePrev()` |
> | nextButton | `PageNext()` |
> | informationButton | `ToggleInformationWindow()` |
> | openPortalButton | `OpenPortal()` |

#### 表示範囲トリガーの設定

Requesterには**BoxCollider**を1つアタッチして表示範囲を設定します.

- `Is Trigger` = **オン**
- LayerはIgnore Raycast(UIレーザーを遮らない)
- **`EditorOnly` タグを付けない**(ビルドで削除されて範囲判定が効かなくなる)
- コライダーの大きさはワールドに合わせて自由に調整してよい

### 3.3 動作確認

#### Play Mode での確認

1. Unity上で **Play** ボタンを押す
2. ClientSimのプレイヤーがRequesterのBoxCollider範囲内に入るか確認
3. 数秒後にモニターに写真が表示されること
4. `<` / `>` ボタンでページが切り替わること

#### Build & Test での確認

1. VRChat SDKのコントロールパネルから **Build & Test** を実行
2. ローカルのVRChatクライアントでワールドに入る
3. モニターの前に近づき、ページ送り・ポータル呼び出しを確認

> **よくある忘れ:** World Space UIのモニターが**ボタンに反応しない**場合、
> モニターの`Screen`オブジェクトに `GraphicRaycaster` と `VRC.SDK3.Components.VRCUiShape` が
> 両方アタッチされているか確認すること.

---

## 第4章: ギミック別ガイド

> 個別ギミックのガイドは別ページで順次追加予定.

---

## 第5章: トラブルシューティング

### 5.1 よくあるエラーと対処法

#### `Failed to assign network IDs`

UdonBehaviourを別のGameObjectへ移動・コピーしたときに起きる.
VRChat SDKの **Network ID Utility** (SDKコントロールパネル内) を開き、競合しているIDを解消するか、不要なエントリを削除する.

#### モニターに写真が表示されない (「読み込み中…」のまま)

- プレイヤーがRequesterのBoxCollider範囲内にいるか確認
- `EterpixDownloaderV2`がシーンに存在するか確認
- `urlList`のURLが256件同期されているか確認(メニュー`ali / eterpix / Sync URL Lists from requestUrl`)
- コンソールでURL関連のエラーを確認

#### ボタンを押しても反応しない

- `Screen`オブジェクトに`GraphicRaycaster`と`VRCUiShape`があるか確認
- 各ボタンの`OnClick()`にMonitorコンポーネントと関数が手動登録されているか確認

#### ポータルが出ない

- シーンに`EterpixPortal`という名前のGameObjectが**1つ**あるか確認
- `eterpix_monitor`の`portal`フィールドが未設定の場合、`GameObject.Find("EterpixPortal")`で自動解決されるが、名前が違うと見つからない

### 5.2 導入チェックリスト

```
[ ] EterPixパッケージがVCC経由でインストール済み
[ ] シーンに EterpixDownloaderV2 が1つだけある
[ ] シーンに EterpixPortal が1つだけある (名前は変えない)
[ ] Requesterの取得先URLが設定されている
[ ] URLリストが256件同期されている
[ ] RequesterにBoxCollider(Is Trigger=ON, EditorOnlyタグなし)がある
[ ] モニターのScreen子オブジェクトに GraphicRaycaster と VRCUiShape がある
[ ] 各ボタンのOnClick()に関数が手動登録されている
[ ] ビルドエラーが出ていない (Network ID競合に注意)
```

---

## 第6章: 更新履歴・バージョンノート

| バージョン | 主な変更 |
|-----------|---------|
| v2.1.x | v2系(Downloader/Requester/Monitor 3層構成)に刷新. 表示範囲トリガーをRequester単体に統合 |
| v1.x | 旧系統(eterpix_get_api / eterpix_json_api / eterpix_update). 現在も並存しているが新規導入はv2を推奨 |
