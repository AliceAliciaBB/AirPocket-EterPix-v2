# EterPix モニターUI リメイク Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `docs/eterpix-monitor-ui-remake-spec.md` の仕様どおりに、EterPixモニターの新しいprefab 2種(Stack / Split)、エラー表示、テーマ切替を実装する。

**Architecture:** prefabは手で組まず、開発用エディタスクリプト `eterpix_monitor_builder` がコードから組み立てる(寸法はすべてこのplanの定数)。ランタイムは既存の downloader → requester → monitor の通知経路に「フィード状態」を足し、テーマはシングルトン `eterpix_theme` + モニターごとの `eterpix_monitor_theme` で色を配る。見た目の確認は、ビルダーが作るプレビューシーンのスクリーンショットで行う。

**Tech Stack:** Unity 2022.3.22f1 / VRChat SDK Worlds 3.10.5 (UdonSharp 1.x) / TextMeshPro 3.0.6 / uGUI。Unity操作は UnityMCP (`mcp__UnityMCP__*`) 経由。

## Global Constraints

- 仕様書: `docs/eterpix-monitor-ui-remake-spec.md`(このplanと食い違う場合はこのplanを優先。planは仕様を具体値に落としたもの)
- `Assets/EterPix/` は配布物のみ。開発用ツール(ビルダー、プレビューシーン)は `Assets/EterPix_dev/` に置く
- UdonSharpの制約: `Button.onClick.AddListener` は使えない(OnClickはエディタで `UdonBehaviour.SendCustomEvent` に永続登録する)。ユーザー定義メソッドに `out`/`ref` 引数を使わない。`List<T>` はU#コードで使わない(エディタ専用ブロック内はOK)
- エディタ専用コードは `#if !COMPILER_UDONSHARP && UNITY_EDITOR` で囲む
- UIはすべて長方形(`Image.sprite = null`)。角丸spriteは使わない。線は使わない
- フォントは `Assets/EterPix/Fonts/NotoSansJP-Bold SDF.asset` の1種類のみ(Bold しか無い。太さの差は付けられないので、強弱は大きさで付ける)
- 文字は不透明(α1)のみ。α0 は見えない判定領域だけ
- イベント名は `PagePrev` / `PageNext` / `OpenPortal` / `ToggleInformationWindow`(monitor)、`CycleTheme`(monitor_theme)
- 旧prefab `EterpixMonitorUI.prefab` が引き続き動くこと(新しいSerializeFieldはすべてnull許容)
- 表示範囲トリガー(`ViewRange`)は旧prefabと同じ値を再現する: layer 2 (Ignore Raycast)、tag `EditorOnly`、BoxCollider trigger size (700,1000,1000) center (0,0,-500)、monitor の `debugIgnoreTriggerRange = true`(挙動を変えないため。見直しはこのplanのスコープ外)
- コンパイル確認手順(以下「コンパイル確認」と呼ぶ):
  1. `mcp__UnityMCP__read_console(action="clear")`
  2. `mcp__UnityMCP__refresh_unity(mode="force", scope="all", compile="request", wait_for_ready=true)`
  3. `mcp__UnityMCP__read_console(action="get", types=["error"], count="50", format="detailed")`
  4. 期待: `EterPix` / `eterpix` / `UdonSharp` / `[U#]` を含むエラーが0件。エラーがあれば直してから次へ進む
- コミット: 各タスクの最後に、そのタスクで作成・変更したファイル(と対応する `.meta`、U#の `.asset`)だけを `git add` する。`Assets/SerializedUdonPrograms/`、`ProjectSettings/`、`Packages/`、`Assets/セーブ/`、`Assets/Tesca/`、`Assets/TextMesh Pro/` は絶対に add しない。メッセージ末尾に `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` を付け、コミット後に `git push` する
- コミット前に `git status --short` で、addしたファイル以外が staged に入っていないことを確認する

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `Assets/EterPix/Scripts/eterpix_downloader.cs` | 変更 | フィード状態 `_feedStatus` を持ち、失敗時も requester に通知する |
| `Assets/EterPix/Scripts/eterpix_requester.cs` | 変更 | `FeedStatus` / `HasEverSucceeded` をモニターに公開する |
| `Assets/EterPix/Scripts/eterpix_theme.cs` (+`.asset`) | 新規 | テーマのシングルトン(切替モード、PlayerData保存、配色) |
| `Assets/EterPix/Scripts/eterpix_monitor_theme.cs` (+`.asset`) | 新規 | モニター1台の配色適用先、切替ボタンの中継 |
| `Assets/EterPix/Scripts/eterpix_monitor.cs` | 変更(全面書き換え) | 状態表示、読み込み中の明滅、情報アイコン、ループ送り、offset正規化 |
| `Assets/EterPix/UI/LoadingPulse.shader` | 新規 | 読み込み中の明滅シェーダー |
| `Assets/EterPix/UI/LoadingPulse.mat` | 新規(ビルダーが生成) | 上記のマテリアル |
| `Assets/EterPix/Editor/eterpix_themeEditor.cs` | 新規 | テーマのInspector(日本語表示、シーンへ適用) |
| `Assets/EterPix/Editor/eterpix_monitor_themeEditor.cs` | 新規 | モニターテーマのInspector(自動収集、プレビュー) |
| `Assets/EterPix/Prefabs/EterpixTheme.prefab` | 新規(ビルダーが生成) | テーマのシングルトン |
| `Assets/EterPix/Prefabs/EterpixMonitor_Stack.prefab` / `_Split.prefab` | 新規(ビルダーが生成) | 新モニター |
| `Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs` | 新規 | prefab・プレビューシーン・テンプレート移行のビルダー(開発用) |
| `Assets/EterPix_dev/Preview/MonitorPreview.unity` | 新規(ビルダーが生成) | 見た目確認用シーン |
| `Assets/EterPix/Prefabs/Legacy/EterpixMonitorUI.prefab` | 移動 | 旧prefab(GUID維持) |
| `Assets/EterPix/Etp_テンプレート.prefab` | 変更 | 中のモニターを Stack に差し替え、EterpixTheme を追加 |
| `Assets/EterPix/README.md` / `Assets/EterPix_dev/code/CLAUDE.md` | 変更 | 使い方と組み立てメモ |

タスクの依存: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8(順番に実行する)。

---

### Task 1: フィード状態を downloader → requester に通す

**Files:**
- Modify: `Assets/EterPix/Scripts/eterpix_downloader.cs`
- Modify: `Assets/EterPix/Scripts/eterpix_requester.cs`

**Interfaces:**
- Produces:
  - `eterpix_downloader.FeedStatusLoading = 0` / `FeedStatusOk = 1` / `FeedStatusUntrustedUrl = 2` / `FeedStatusServerError = 3`(`public const int`)
  - `public int eterpix_downloader.GetFeedStatus(int feedIndex)`(範囲外は `FeedStatusLoading`)
  - `public int eterpix_requester.FeedStatus`(getter)
  - `public bool eterpix_requester.HasEverSucceeded`(getter。一度でも投稿配列を受け取ったら true)

- [ ] **Step 1: downloader に定数とフィールドを追加する**

`public const int ImageSlotsPerFeed = 256;` の直後に追加:

```csharp
        // フィードの取得状態(モニターのエラー表示に使う)
        public const int FeedStatusLoading = 0;
        public const int FeedStatusOk = 1;
        public const int FeedStatusUntrustedUrl = 2;
        public const int FeedStatusServerError = 3;

        // VRChatの「信頼されていないURLを許可」がOFFのときの result.Error に含まれる文字列
        // (Tesca BOOTH_Poster と同じ判定)
        private const string UntrustedUrlErrorMarker = "Not trusted url hit";
```

`private bool[] _feedPending;` の直後に追加:

```csharp
        private int[] _feedStatus;
```

`Start()` の `_feedPending = new bool[maxFeeds];` の直後に追加:

```csharp
            _feedStatus = new int[maxFeeds];
```

- [ ] **Step 2: 状態の取得メソッドを追加する**

`public DataDictionary GetFeedWorldData(int feedIndex)` メソッドの直後に追加:

```csharp
        public int GetFeedStatus(int feedIndex)
        {
            if (feedIndex < 0 || feedIndex >= _feedCount) return FeedStatusLoading;
            return _feedStatus[feedIndex];
        }
```

- [ ] **Step 3: `OnStringLoadSuccess` で状態を更新し、失敗でも通知する**

JSON解析失敗の分岐を次に置き換える(`LogError` の行はそのまま残し、2行足す):

```csharp
            if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken token) ||
                token.TokenType != TokenType.DataDictionary)
            {
                if (debugLog != null) debugLog.LogError($"[eterpix_downloader] Failed to parse JSON for feed {feedIndex}");
                _feedStatus[feedIndex] = FeedStatusServerError;
                NotifyFeedRequesters(feedIndex);
                StartNextFetchIfIdle();
                return;
            }
```

`'post' key not found` の分岐を次に置き換える:

```csharp
            else
            {
                if (debugLog != null) debugLog.LogError($"[eterpix_downloader] 'post' key not found for feed {feedIndex} (v1形式のURL /api/vrc/v1/... か確認)");
                _feedStatus[feedIndex] = FeedStatusServerError;
                NotifyFeedRequesters(feedIndex);
                StartNextFetchIfIdle();
                return;
            }
```

成功時の `NotifyFeedRequesters(feedIndex);`(`Feed {feedIndex}: ... posts` ログの直後)の直前に1行追加:

```csharp
            _feedStatus[feedIndex] = FeedStatusOk;
```

- [ ] **Step 4: `OnStringLoadError` を置き換える**

メソッド全体を次に置き換える:

```csharp
        public override void OnStringLoadError(IVRCStringDownload result)
        {
            int feedIndex = _pendingFeedIndex;
            _isFetching = false;
            if (feedIndex < 0 || feedIndex >= _feedCount)
            {
                StartNextFetchIfIdle();
                return;
            }
            _feedPending[feedIndex] = false;

            string error = result.Error;
            bool isUntrusted = !string.IsNullOrEmpty(error) && error.Contains(UntrustedUrlErrorMarker);
            _feedStatus[feedIndex] = isUntrusted ? FeedStatusUntrustedUrl : FeedStatusServerError;
            if (debugLog != null) debugLog.LogError($"[eterpix_downloader] String load error for feed {feedIndex}: {error}");

            // 一度でも成功していれば、requester/モニター側は前回のデータを表示し続ける
            NotifyFeedRequesters(feedIndex);
            StartNextFetchIfIdle();
        }
```

- [ ] **Step 5: requester に公開プロパティを追加する**

`public int VisibleCount => _visibleIndices.Length;` の直後に追加:

```csharp
        // ダウンローダのフィード状態(eterpix_downloader.FeedStatus*)。未登録なら読み込み中扱い
        public int FeedStatus => (_downloader != null && _feedIndex >= 0) ? _downloader.GetFeedStatus(_feedIndex) : eterpix_downloader.FeedStatusLoading;

        // 一度でも投稿配列を受け取ったか(取得に失敗しても前回のデータがあれば表示を続けるため)
        public bool HasEverSucceeded => _downloader != null && _feedIndex >= 0 && _downloader.GetFeedPosts(_feedIndex) != null;
```

`FetchDownloader()` の
```csharp
            if (_downloader.GetFeedPosts(_feedIndex) != null) OnJsonUpdated();
```
を次に置き換える(同じフィードが他のrequesterで既に失敗していた場合も、状態を反映させるため):

```csharp
            if (_downloader.GetFeedPosts(_feedIndex) != null || _downloader.GetFeedStatus(_feedIndex) != eterpix_downloader.FeedStatusLoading) OnJsonUpdated();
```

- [ ] **Step 6: コンパイル確認**

Global Constraints の「コンパイル確認」を行う。期待: EterPix 関連のエラー0件。

- [ ] **Step 7: コミット**

```bash
git add Assets/EterPix/Scripts/eterpix_downloader.cs Assets/EterPix/Scripts/eterpix_requester.cs Assets/EterPix/Scripts/eterpix_downloader.asset Assets/EterPix/Scripts/eterpix_requester.asset
git status --short
git commit -m "feat(eterpix-v2): expose feed load status (untrusted URL / server error) to requesters

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```
(`.asset` に差分が無ければ add されないだけなので問題ない)

---

### Task 2: テーマのスクリプト(`eterpix_theme` / `eterpix_monitor_theme`)

**Files:**
- Create: `Assets/EterPix/Scripts/eterpix_theme.cs` と U# program asset `Assets/EterPix/Scripts/eterpix_theme.asset`
- Create: `Assets/EterPix/Scripts/eterpix_monitor_theme.cs` と `Assets/EterPix/Scripts/eterpix_monitor_theme.asset`

**Interfaces:**
- Consumes: なし(`eterpix_monitor` は Task 3 で `OnThemeChanged()` / `EditorSetLoadingPreviewColor(Color)` を実装する。このタスクの時点では `eterpix_monitor_theme` がそれらを呼ぶため、**Task 2 と Task 3 はまとめてコンパイル確認する**。Task 2 の Step 5 では Task 3 のメソッドを先に空で追加しておく)
- Produces:
  - `eterpix_theme.SingletonObjectName = "EterpixTheme"`、`PersistenceKey = "eterpix.theme"`
  - 定数 `ThemeBlack=0, ThemeWhite=1, ThemeCustom=2`、`ModeSwitchable=0, ModeFixedBlack=1, ModeFixedWhite=2, ModeFixedCustom=3`、`RoleBackground=0, RoleText=1, RoleButton=2, RoleAccent=3, RoleAccentText=4`
  - `public void eterpix_theme.Register(eterpix_monitor_theme m)` / `public void CycleTheme()` / `public Color GetColor(int theme, int role)` / `public static Color GetPresetColor(int theme, int role)` / `public int GetStartTheme()`
  - エディタ専用: `public void eterpix_theme.EditorApplyToScene()`
  - `public void eterpix_monitor_theme.CycleTheme()` / `public void ApplyColors(Color bg, Color text, Color button, Color accent, Color accentText, bool switchVisible)` / `public Color LoadingColor` / `public bool IsDirectEdit`
  - エディタ専用: `public void eterpix_monitor_theme.EditorCollectFromChildren()` / `EditorApplyColors(Color,Color,Color,Color,Color)` / `EditorApplyPreset(int theme)`

- [ ] **Step 1: `eterpix_theme.cs` を作成する**

```csharp
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace ali.eterpix.v2
{
    // モニターの配色テーマ(ワールドに1つだけ置くシングルトン。固定名 SingletonObjectName で解決する)。
    // 切替はローカルのみ(同期しない)。切替ありモードでは、選んだテーマを PlayerData に保存する。
    // 色の役割: 背景 / 文字 / ボタン / アクセント(ポータルボタン) / アクセント上の文字
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_theme : UdonSharpBehaviour
    {
        public const string SingletonObjectName = "EterpixTheme";
        public const string PersistenceKey = "eterpix.theme";

        public const int ThemeBlack = 0;
        public const int ThemeWhite = 1;
        public const int ThemeCustom = 2;

        public const int ModeSwitchable = 0;
        public const int ModeFixedBlack = 1;
        public const int ModeFixedWhite = 2;
        public const int ModeFixedCustom = 3;

        public const int RoleBackground = 0;
        public const int RoleText = 1;
        public const int RoleButton = 2;
        public const int RoleAccent = 3;
        public const int RoleAccentText = 4;

        // 値の意味は上の Mode* / Theme* 定数(Inspectorは Editor/eterpix_themeEditor.cs が日本語で表示する)
        [SerializeField] private int switchMode = ModeSwitchable;
        [SerializeField] private bool includeCustomInCycle = false;
        [SerializeField] private int initialTheme = ThemeBlack;

        [SerializeField] private Color customBackground = new Color(20f / 255f, 20f / 255f, 20f / 255f, 1f);
        [SerializeField] private Color customText = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color customButton = new Color(42f / 255f, 42f / 255f, 42f / 255f, 1f);
        [SerializeField] private Color customAccent = new Color(157f / 255f, 111f / 255f, 1f, 1f);
        [SerializeField] private Color customAccentText = new Color(20f / 255f, 20f / 255f, 20f / 255f, 1f);

        private eterpix_monitor_theme[] _monitors = new eterpix_monitor_theme[0];
        private int _monitorCount = 0;
        private int _currentTheme = ThemeBlack;
        private bool _initialized = false;

        public bool IsSwitchable => switchMode == ModeSwitchable;

        private void Start()
        {
            GameObject canonical = GameObject.Find(SingletonObjectName);
            if (canonical != null && canonical != this.gameObject)
            {
                Debug.LogWarning("[eterpix_theme] Duplicate instance detected. Disabling self.");
                gameObject.SetActive(false);
                return;
            }
            EnsureInitialized();
            ApplyAll();
        }

        // モニター側のStartが先に走ることがあるため、Register/Startのどちらからでも初期化する
        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _currentTheme = GetStartTheme();
        }

        public int GetStartTheme()
        {
            if (switchMode == ModeFixedBlack) return ThemeBlack;
            if (switchMode == ModeFixedWhite) return ThemeWhite;
            if (switchMode == ModeFixedCustom) return ThemeCustom;
            return IsSelectable(initialTheme) ? initialTheme : ThemeBlack;
        }

        public void Register(eterpix_monitor_theme monitorTheme)
        {
            if (monitorTheme == null) return;
            EnsureInitialized();

            if (_monitorCount >= _monitors.Length)
            {
                eterpix_monitor_theme[] bigger = new eterpix_monitor_theme[_monitors.Length + 8];
                for (int i = 0; i < _monitors.Length; i++) bigger[i] = _monitors[i];
                _monitors = bigger;
            }
            _monitors[_monitorCount] = monitorTheme;
            _monitorCount++;

            ApplyTo(monitorTheme);
        }

        // モニターの切替ボタンから(eterpix_monitor_theme.CycleTheme経由で)呼ばれる。ローカルのみ
        public void CycleTheme()
        {
            if (!IsSwitchable) return;
            EnsureInitialized();
            _currentTheme = GetNextTheme(_currentTheme);
            ApplyAll();
            PlayerData.SetInt(PersistenceKey, _currentTheme);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || !IsSwitchable) return;
            if (PlayerData.TryGetInt(player, PersistenceKey, out int saved) && IsSelectable(saved))
            {
                EnsureInitialized();
                _currentTheme = saved;
                ApplyAll();
            }
        }

        private int GetNextTheme(int theme)
        {
            if (theme == ThemeBlack) return ThemeWhite;
            if (theme == ThemeWhite) return includeCustomInCycle ? ThemeCustom : ThemeBlack;
            return ThemeBlack;
        }

        private bool IsSelectable(int theme)
        {
            return theme == ThemeBlack || theme == ThemeWhite || (theme == ThemeCustom && includeCustomInCycle);
        }

        private void ApplyAll()
        {
            for (int i = 0; i < _monitorCount; i++) ApplyTo(_monitors[i]);
        }

        private void ApplyTo(eterpix_monitor_theme monitorTheme)
        {
            if (monitorTheme == null) return;
            monitorTheme.ApplyColors(
                GetColor(_currentTheme, RoleBackground),
                GetColor(_currentTheme, RoleText),
                GetColor(_currentTheme, RoleButton),
                GetColor(_currentTheme, RoleAccent),
                GetColor(_currentTheme, RoleAccentText),
                IsSwitchable);
        }

        public Color GetColor(int theme, int role)
        {
            if (theme != ThemeCustom) return GetPresetColor(theme, role);
            if (role == RoleBackground) return customBackground;
            if (role == RoleText) return customText;
            if (role == RoleButton) return customButton;
            if (role == RoleAccent) return customAccent;
            return customAccentText;
        }

        // 黒/白プリセット(仕様書「8. テーマ」の表)。色はすべて不透明
        public static Color GetPresetColor(int theme, int role)
        {
            bool white = theme == ThemeWhite;
            if (role == RoleBackground) return white ? Rgb(250, 250, 250) : Rgb(20, 20, 20);
            if (role == RoleText) return white ? Rgb(20, 20, 20) : Rgb(255, 255, 255);
            if (role == RoleButton) return white ? Rgb(230, 230, 230) : Rgb(42, 42, 42);
            if (role == RoleAccent) return Rgb(157, 111, 255);
            return Rgb(20, 20, 20);
        }

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        #region Editor
#if !COMPILER_UDONSHARP && UNITY_EDITOR
        // Inspector(eterpix_themeEditor)から呼ぶ。シーン内の全モニターへ開始時のテーマを塗る
        public void EditorApplyToScene()
        {
            int theme = GetStartTheme();
            eterpix_monitor_theme[] all = Object.FindObjectsOfType<eterpix_monitor_theme>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].IsDirectEdit) continue;
                all[i].EditorApplyColors(
                    GetColor(theme, RoleBackground),
                    GetColor(theme, RoleText),
                    GetColor(theme, RoleButton),
                    GetColor(theme, RoleAccent),
                    GetColor(theme, RoleAccentText));
            }
        }
#endif
        #endregion
    }
}
```

- [ ] **Step 2: `eterpix_monitor_theme.cs` を作成する**

```csharp
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace ali.eterpix.v2
{
    // モニター1台ぶんの配色の適用先。子オブジェクト名の接頭辞で役割を決める:
    //   bg_ = 背景(Image) / tx_ = 文字(TMP、またはアイコンのImage) / btn_ = ボタン(Image)
    //   acc_ = アクセント(Image) / acctx_ = アクセント上の文字(TMP)
    // 配列はInspectorの「子から自動収集」で作る(Editor/eterpix_monitor_themeEditor.cs)。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_monitor_theme : UdonSharpBehaviour
    {
        [Header("直接編集: ONにするとテーマの対象外(色は各Image/TMPを直接編集する)")]
        [SerializeField] private bool directEdit = false;

        [Header("テーマ切替ボタン(切替できないときは非表示にする)")]
        [SerializeField] private GameObject themeSwitchButton;

        [Header("配色が変わったことを伝えるモニター(読み込み中の明滅色の更新)")]
        [SerializeField] private eterpix_monitor monitor;

        [Header("役割ごとの適用先(「子から自動収集」で作る)")]
        [SerializeField] private Image[] backgroundImages = new Image[0];
        [SerializeField] private TMP_Text[] texts = new TMP_Text[0];
        [SerializeField] private Image[] textImages = new Image[0];
        [SerializeField] private Image[] buttonImages = new Image[0];
        [SerializeField] private Image[] accentImages = new Image[0];
        [SerializeField] private TMP_Text[] accentTexts = new TMP_Text[0];

        private eterpix_theme _theme;
        private Color _buttonColor = new Color(42f / 255f, 42f / 255f, 42f / 255f, 1f);

        // 画像の読み込み中にPostImageを明滅させる色(=テーマのボタン色)
        public Color LoadingColor => _buttonColor;
        public bool IsDirectEdit => directEdit;

        private void Start()
        {
            if (buttonImages != null && buttonImages.Length > 0 && buttonImages[0] != null)
            {
                _buttonColor = buttonImages[0].color;
            }

            if (directEdit)
            {
                SetSwitchVisible(false);
                return;
            }

            GameObject themeObj = GameObject.Find(eterpix_theme.SingletonObjectName);
            if (themeObj != null) _theme = themeObj.GetComponent<eterpix_theme>();
            if (_theme == null)
            {
                // テーマのシングルトンが無いワールドでは、prefabに焼き込んだ色のまま切替ボタンを隠す
                SetSwitchVisible(false);
                return;
            }
            _theme.Register(this);
        }

        // テーマ切替ボタンのOnClick(SendCustomEvent "CycleTheme")
        public void CycleTheme()
        {
            if (_theme != null) _theme.CycleTheme();
        }

        public void ApplyColors(Color background, Color text, Color button, Color accent, Color accentText, bool switchVisible)
        {
            SetImageColors(backgroundImages, background);
            SetTextColors(texts, text);
            SetImageColors(textImages, text);
            SetImageColors(buttonImages, button);
            SetImageColors(accentImages, accent);
            SetTextColors(accentTexts, accentText);
            _buttonColor = button;
            SetSwitchVisible(switchVisible);
            if (monitor != null) monitor.OnThemeChanged();
        }

        private void SetSwitchVisible(bool visible)
        {
            if (themeSwitchButton != null) themeSwitchButton.SetActive(visible);
        }

        private void SetImageColors(Image[] targets, Color color)
        {
            if (targets == null) return;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targets[i].color = color;
            }
        }

        private void SetTextColors(TMP_Text[] targets, Color color)
        {
            if (targets == null) return;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targets[i].color = color;
            }
        }

        #region Editor
#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorCollectFromChildren()
        {
            List<Image> bg = new List<Image>();
            List<TMP_Text> tx = new List<TMP_Text>();
            List<Image> txImages = new List<Image>();
            List<Image> btn = new List<Image>();
            List<Image> acc = new List<Image>();
            List<TMP_Text> accTx = new List<TMP_Text>();
            GameObject switchButton = null;

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                Image img = t.GetComponent<Image>();
                TMP_Text text = t.GetComponent<TMP_Text>();
                if (n == "btn_ThemeButton") switchButton = t.gameObject;

                if (n.StartsWith("bg_")) { if (img != null) bg.Add(img); }
                else if (n.StartsWith("tx_")) { if (text != null) tx.Add(text); else if (img != null) txImages.Add(img); }
                else if (n.StartsWith("btn_")) { if (img != null) btn.Add(img); }
                else if (n.StartsWith("acctx_")) { if (text != null) accTx.Add(text); }
                else if (n.StartsWith("acc_")) { if (img != null) acc.Add(img); }
            }

            Undo.RecordObject(this, "Collect theme targets");
            backgroundImages = bg.ToArray();
            texts = tx.ToArray();
            textImages = txImages.ToArray();
            buttonImages = btn.ToArray();
            accentImages = acc.ToArray();
            accentTexts = accTx.ToArray();
            if (themeSwitchButton == null) themeSwitchButton = switchButton;
            if (monitor == null) monitor = GetComponent<eterpix_monitor>();
            EditorUtility.SetDirty(this);
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        public void EditorApplyPreset(int theme)
        {
            EditorApplyColors(
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleBackground),
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleText),
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleButton),
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleAccent),
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleAccentText));
        }

        public void EditorApplyColors(Color background, Color text, Color button, Color accent, Color accentText)
        {
            EditorSetImages(backgroundImages, background);
            EditorSetTexts(texts, text);
            EditorSetImages(textImages, text);
            EditorSetImages(buttonImages, button);
            EditorSetImages(accentImages, accent);
            EditorSetTexts(accentTexts, accentText);
            if (monitor != null) monitor.EditorSetLoadingPreviewColor(button);
        }

        private static void EditorSetImages(Image[] targets, Color color)
        {
            if (targets == null) return;
            foreach (Image g in targets)
            {
                if (g == null || g.color == color) continue;
                Undo.RecordObject(g, "Apply theme");
                g.color = color;
                EditorUtility.SetDirty(g);
                PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            }
        }

        private static void EditorSetTexts(TMP_Text[] targets, Color color)
        {
            if (targets == null) return;
            foreach (TMP_Text g in targets)
            {
                if (g == null || g.color == color) continue;
                Undo.RecordObject(g, "Apply theme");
                g.color = color;
                EditorUtility.SetDirty(g);
                PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            }
        }
#endif
        #endregion
    }
}
```

- [ ] **Step 3: `eterpix_monitor.cs` に Task 3 のメソッドを仮に追加する**

`eterpix_monitor_theme` が参照するため、`eterpix_monitor.cs` のクラス末尾(`OpenPortal()` メソッドの後、クラスの閉じ括弧の前)に次を追加する。Task 3 で全面置き換えするので、中身は空でよい:

```csharp
        public void OnThemeChanged()
        {
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetLoadingPreviewColor(Color color)
        {
        }
#endif
```

- [ ] **Step 4: U# program asset を作成する**

`mcp__UnityMCP__execute_code` で次を実行する(スキーマは ToolSearch `select:mcp__UnityMCP__execute_code` で読み込む):

```csharp
foreach (string n in new[] { "eterpix_theme", "eterpix_monitor_theme" })
{
    string assetPath = "Assets/EterPix/Scripts/" + n + ".asset";
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null) continue;
    UnityEditor.AssetDatabase.ImportAsset("Assets/EterPix/Scripts/" + n + ".cs");
    var script = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>("Assets/EterPix/Scripts/" + n + ".cs");
    var asset = UnityEngine.ScriptableObject.CreateInstance<UdonSharp.UdonSharpProgramAsset>();
    asset.sourceCsScript = script;
    UnityEditor.AssetDatabase.CreateAsset(asset, assetPath);
}
UnityEditor.AssetDatabase.SaveAssets();
return "ok";
```

期待: `Assets/EterPix/Scripts/eterpix_theme.asset` と `eterpix_monitor_theme.asset` が作られる。`execute_code` が使えない場合は、Unityのメニュー `Assets/Create/U# Script` は使わず(別名のスクリプトが作られるため)、エラー内容を報告して止まる。

- [ ] **Step 5: コンパイル確認**

「コンパイル確認」を行う。期待: EterPix 関連エラー0件。`UdonSharp` の「program asset が無い」系の警告が `eterpix_theme` / `eterpix_monitor_theme` について出ていないこと(`types=["warning"]` でも確認する)。

- [ ] **Step 6: コミット**

```bash
git add Assets/EterPix/Scripts/eterpix_theme.cs Assets/EterPix/Scripts/eterpix_theme.cs.meta Assets/EterPix/Scripts/eterpix_theme.asset Assets/EterPix/Scripts/eterpix_theme.asset.meta Assets/EterPix/Scripts/eterpix_monitor_theme.cs Assets/EterPix/Scripts/eterpix_monitor_theme.cs.meta Assets/EterPix/Scripts/eterpix_monitor_theme.asset Assets/EterPix/Scripts/eterpix_monitor_theme.asset.meta Assets/EterPix/Scripts/eterpix_monitor.cs Assets/EterPix/Scripts/eterpix_monitor.asset
git status --short
git commit -m "feat(eterpix-v2): add theme singleton and per-monitor theme targets

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```

---

### Task 3: `eterpix_monitor` の書き換え(状態表示・明滅・情報アイコン・ループ送り)

**Files:**
- Modify (全面置き換え): `Assets/EterPix/Scripts/eterpix_monitor.cs`

**Interfaces:**
- Consumes: Task 1 の `eterpix_requester.FeedStatus` / `HasEverSucceeded` / `eterpix_downloader.FeedStatus*`、Task 2 の `eterpix_monitor_theme.LoadingColor`
- Produces(ビルダーが SerializedObject で設定するフィールド名。**名前を変えないこと**):
  `image`, `rotationTargetObjects`, `figureAspectFitter`, `prevButton`, `nextButton`, `pageLabel`, `userNameText`, `descriptionText`, `worldContextRoot`, `worldNameText`, `worldDescriptionText`, `openPortalButton`, `portal`, `portalSpawnPoint`, `informationButton`, `informationWindowRoot`, `infoIconOpen`, `infoIconClose`, `statusRoot`, `statusText`, `normalOnlyObjects`, `loadingMessage`, `untrustedUrlMessage`, `serverErrorMessage`, `emptyMessage`, `loadingMaterial`, `monitorTheme`, `offset`, `debugLog`, `debugIgnoreTriggerRange`
- Produces(メソッド): `public void OnThemeChanged()`、エディタ専用 `public void EditorSetLoadingPreviewColor(Color color)`

- [ ] **Step 1: ファイル全体を次の内容に置き換える**

```csharp
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Data;

namespace ali.eterpix.v2
{
    // v2下位: モニター。リファクタリング案.md「モニター」を参照。
    // UIは開発用ビルダー(Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs)が組み立てる
    // prefab(EterpixMonitor_Stack / EterpixMonitor_Split)の要素を[SerializeField]で紐付ける。
    // 旧prefab(Prefabs/Legacy/EterpixMonitorUI)も動くように、新しい参照はすべてnull許容にしている。
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_monitor : UdonSharpBehaviour
    {
        [Header("画像")]
        [SerializeField] private RawImage image;
        [SerializeField] private GameObject[] rotationTargetObjects;
        [Header("画像の親(4:3枠。AspectRatioFitterで実際の縦横比に合わせて縮める対象)")]
        [SerializeField] private AspectRatioFitter figureAspectFitter;

        [Header("ページ送り(端でループする)")]
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_Text pageLabel; // "現在 / 全体"

        [Header("投稿情報")]
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text descriptionText;

        [Header("ワールド情報(ワールドが無い投稿では非表示にするルート)")]
        [SerializeField] private GameObject worldContextRoot;
        [SerializeField] private TMP_Text worldNameText;
        [SerializeField] private TMP_Text worldDescriptionText;
        [SerializeField] private Button openPortalButton;

        [Header("ポータル(ワールドに1つだけ置いた共通ポータルを呼び寄せる。押した本人にのみ見える)")]
        // 未指定なら固定名 PortalObjectName のGameObjectから解決する
        [SerializeField] private eterpix_porta_resize portal;
        [SerializeField] private Transform portalSpawnPoint; // 未指定ならこのtransform

        public const string PortalObjectName = "EterpixPortal";

        [Header("情報ウィンドウ")]
        [SerializeField] private Button informationButton;
        [SerializeField] private GameObject informationWindowRoot;
        [Header("情報ボタンのアイコン(情報表示中はXに切り替える)")]
        [SerializeField] private GameObject infoIconOpen;
        [SerializeField] private GameObject infoIconClose;

        [Header("状態表示(読み込み中/エラー/投稿なし。写真枠の中に出す)")]
        [SerializeField] private GameObject statusRoot;
        [SerializeField] private TMP_Text statusText;
        // 通常表示のときだけ出す要素(ページ番号・ページ送り・画像・投稿欄など)
        [SerializeField] private GameObject[] normalOnlyObjects = new GameObject[0];
        [SerializeField, TextArea(2, 8)] private string loadingMessage = "読み込み中…";
        [SerializeField, TextArea(2, 8)] private string untrustedUrlMessage =
            "「信頼されていないURL」が許可されていません\n\n設定 > 快適性とセーフティ >\n「信頼されていないURLを許可」をONにしてください\nONにすると、しばらくして表示されます\n(表示されない場合はワールドに入り直してください)";
        [SerializeField, TextArea(2, 8)] private string serverErrorMessage =
            "サーバーに接続できませんでした\nメンテナンス中の可能性があります\n最新情報は X @_alicilia をご確認ください\n\n数分後に自動で再接続します";
        [SerializeField, TextArea(2, 8)] private string emptyMessage = "表示できる投稿がありません";

        [Header("画像読み込み中の明滅(LoadingPulseマテリアル。色はテーマのボタン色)")]
        [SerializeField] private Material loadingMaterial;
        [SerializeField] private eterpix_monitor_theme monitorTheme;

        [Header("開始位置(offset)。投稿数以上なら offset % 投稿数 のページから始まる")]
        [SerializeField] private int offset = 0;

        [Header("ページ位置(全員に同期)")]
        [UdonSynced] private int _syncedPageIndex = 0;

        [Header("デバッグログ")]
        [SerializeField] private ali.eterpix.eterpix_debug debugLog;

        [Header("診断用: トリガーColliderの範囲判定を無視して常に画像をリクエストする" +
            "(画像が読み込まれない不具合の切り分け用。原因特定後はfalseに戻すこと)")]
        [SerializeField] private bool debugIgnoreTriggerRange = false;

        [Header("診断用: ApplyPost実行時の内部状態(読み取り専用、外部から確認するためのもの)")]
        public string debugState = "";

        private eterpix_requester _requester;
        private eterpix_downloader _downloader;
        private int _feedIndex = -1;

        private int _currentSlot = -1; // 現在ApplyTexture済みのcoid(=collage_id=slot)
        private bool _hasRequestedTexture = false;
        private bool _isInViewRange = false;
        private bool _isImageLoading = false;

        private DataDictionary _currentPost;
        // Init()より先にOnDeserialization()で本物の同期値を受け取っていた場合、
        // offsetで上書きしないためのフラグ(遅れて入ったプレイヤーの初回同期と
        // Init()の実行順は保証されないため)。
        private bool _hasReceivedSync = false;

        // ---- リクエスターからの初期化 ----
        public void Init(eterpix_requester requester, eterpix_downloader downloader, int feedIndex)
        {
            _requester = requester;
            _downloader = downloader;
            _feedIndex = feedIndex;

            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(ali.eterpix.eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<ali.eterpix.eterpix_debug>();
            }

            // offset(Inspectorでの開始ページ位置)は[UdonSynced]の_syncedPageIndexへ
            // Init時に一度だけ反映する。ただし、Init()より先に本物の初回同期
            // (OnDeserialization)を受け取っていた場合は、後から入ったプレイヤーの
            // 正しいページ位置をoffsetで上書きしてしまうため反映しない。
            // 投稿数以上のoffsetは、表示時にNormalizeIndexで offset % 投稿数 に収める。
            if (offset != 0 && !_hasReceivedSync) _syncedPageIndex = offset;

            // UdonSharpはUnityEvent.AddListener()(メソッドグループ・ラムダのいずれも)を
            // バインドできない。各ボタンのOnClick()はコードからではなく、
            // エディタ上でこのコンポーネントのUdonBehaviourのSendCustomEventに
            // PagePrev/PageNext/ToggleInformationWindow/OpenPortalを登録してある。
            if (portal == null)
            {
                GameObject portalObj = GameObject.Find(PortalObjectName);
                if (portalObj != null) portal = portalObj.GetComponent<eterpix_porta_resize>();
            }

            SetInformationOpen(false);

            BindViewRangeTriggers();
            RefreshDisplay();
        }

        // ---- リクエスターからのJSON更新通知(成功・失敗どちらでも呼ばれる) ----
        public void OnFeedUpdated()
        {
            // 新着で表示がずれるのは仕様上許容する(リファクタリング案.md「ページ位置」参照)
            RefreshDisplay();
        }

        // ---- トリガーColliderによるオンデマンド画像要求 ----
        // 表示範囲のトリガーは子のGameObject(ViewRange)にColliderとeterpix_monitor_triggerを付けて分ける。
        // 出入りはそこから中継される。中継側からGetComponentInParentで親を探さず、こちらから子を集めて
        // 参照を渡す。Init()がStart()より先に呼ばれる場合があるため、両方から呼ぶ(何度呼んでもよい)。
        private void Start()
        {
            BindViewRangeTriggers();
            // requesterから初期化されるまでの間、prefabの見本テキストを見せないよう読み込み中にする
            if (_requester == null) ShowStatus(loadingMessage);
        }

        private void BindViewRangeTriggers()
        {
            eterpix_monitor_trigger[] triggers = GetComponentsInChildren<eterpix_monitor_trigger>(true);
            for (int i = 0; i < triggers.Length; i++)
            {
                triggers[i].SetMonitor(this);
            }
        }

        // eterpix_monitor_triggerから、ローカルプレイヤーが表示範囲に入ったときに呼ばれる
        public void OnViewRangeEnter()
        {
            _isInViewRange = true;

            if (_downloader != null && _currentSlot >= 0 && !_hasRequestedTexture)
            {
                _hasRequestedTexture = true;
                _downloader.RequestTexture(_feedIndex, _currentSlot, 100, this);
            }
        }

        // eterpix_monitor_triggerから、ローカルプレイヤーが表示範囲から出たときに呼ばれる
        public void OnViewRangeExit()
        {
            _isInViewRange = false;
            ReleaseCurrentTextureIfAny();
        }

        // オブジェクトの非アクティブ化/破棄(VRChatがOnPlayerTriggerExitを配送し損ねる
        // テレポート/リスポーン等の境界ケースを含む)で、保持中のテクスチャ参照が
        // 解放されないまま残ると_imgRefCountが恒久的に0へ戻らず、猶予破棄/EvictIfOverBudget
        // の両方から永久に除外されてしまう(レビュー指摘)。
        private void OnDisable()
        {
            ReleaseCurrentTextureIfAny();
            _isInViewRange = false;
        }

        // ---- ページ送り(端でループする) ----
        public void PageNext()
        {
            MovePage(1);
        }

        public void PagePrev()
        {
            MovePage(-1);
        }

        private void MovePage(int step)
        {
            if (_requester == null || _requester.VisibleCount <= 0) return;

            int count = _requester.VisibleCount;
            int current = NormalizeIndex(_syncedPageIndex, count);
            int next = NormalizeIndex(current + step, count);

            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            _syncedPageIndex = next;
            RequestSerialization();

            RefreshDisplay();
        }

        // 同期値は生のまま持ち、表示のたびに投稿数で割った余りに収める(負の値も後ろから数える)
        private int NormalizeIndex(int index, int count)
        {
            return ((index % count) + count) % count;
        }

        public override void OnDeserialization()
        {
            _hasReceivedSync = true;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_requester == null)
            {
                ShowStatus(loadingMessage);
                return;
            }

            // 一度も取得に成功していない間だけ、読み込み中/エラーを出す。
            // 一度成功していれば、その後の取得失敗では前回のデータを表示し続ける。
            if (!_requester.HasEverSucceeded)
            {
                int status = _requester.FeedStatus;
                if (status == eterpix_downloader.FeedStatusUntrustedUrl) ShowStatus(untrustedUrlMessage);
                else if (status == eterpix_downloader.FeedStatusServerError) ShowStatus(serverErrorMessage);
                else ShowStatus(loadingMessage);
                return;
            }

            int count = _requester.VisibleCount;
            if (count <= 0)
            {
                ShowStatus(emptyMessage);
                return;
            }

            int slotInWindow = NormalizeIndex(_syncedPageIndex, count);
            DataDictionary post = _requester.GetVisiblePost(slotInWindow);
            if (post == null)
            {
                ShowStatus(emptyMessage);
                return;
            }

            ShowNormal();
            ApplyPost(post);
            UpdatePagingLabel(slotInWindow, count);
        }

        private void UpdatePagingLabel(int slotInWindow, int count)
        {
            if (pageLabel != null) pageLabel.text = $"{slotInWindow + 1} / {count}";
        }

        // ---- 状態表示 ----
        private void ShowStatus(string message)
        {
            ReleaseCurrentTextureIfAny();
            _currentSlot = -1;
            _currentPost = null;
            _isImageLoading = false;

            if (userNameText != null) userNameText.text = "";
            if (descriptionText != null) descriptionText.text = "";
            if (worldContextRoot != null) worldContextRoot.SetActive(false);
            if (pageLabel != null) pageLabel.text = "";
            SetNormalObjectsActive(false);

            if (statusRoot != null) statusRoot.SetActive(true);
            if (statusText != null) statusText.text = message;
        }

        private void ShowNormal()
        {
            if (statusRoot != null) statusRoot.SetActive(false);
            SetNormalObjectsActive(true);
        }

        private void SetNormalObjectsActive(bool active)
        {
            if (normalOnlyObjects == null) return;
            for (int i = 0; i < normalOnlyObjects.Length; i++)
            {
                if (normalOnlyObjects[i] != null) normalOnlyObjects[i].SetActive(active);
            }
        }

        private void ApplyPost(DataDictionary data)
        {
            ReleaseCurrentTextureIfAny();
            _currentPost = data;

            // v1形式のkey(coid=collage_id, copo=img_pos, coro=img_rotation,
            // pode=description, usna=user_name, woid=world_vrc_id)
            int collageId = ReadInt(data, "coid", -1);
            int imgPos = ReadInt(data, "copo", -1);
            int imgRotation = ReadInt(data, "coro", 0);
            string description = ReadString(data, "pode", "");
            string userName = ReadString(data, "usna", "");
            string worldId = ReadString(data, "woid", "");

            ApplyRotation(imgRotation);
            ApplyUvRect(imgPos);
            // 前の投稿の写真を残さないよう、読み込みが終わるまで明滅表示にする
            // (キャッシュ済みならRequestTexture内で即ApplyTextureされる)
            BeginImageLoading();

            _currentSlot = collageId;
            debugState = $"isInViewRange={_isInViewRange} debugIgnore={debugIgnoreTriggerRange} downloaderNull={(_downloader == null)} feedIndex={_feedIndex} collageId={collageId}";
            if ((_isInViewRange || debugIgnoreTriggerRange) && _downloader != null && collageId >= 0)
            {
                _hasRequestedTexture = true;
                debugState += " -> RequestTexture called";
                _downloader.RequestTexture(_feedIndex, collageId, 100, this);
            }

            if (userNameText != null) userNameText.text = userName;
            if (descriptionText != null) descriptionText.text = description;

            ApplyWorldContext(worldId);
        }

        private void ApplyWorldContext(string worldId)
        {
            bool hasWorld = !string.IsNullOrEmpty(worldId);
            if (worldContextRoot != null) worldContextRoot.SetActive(hasWorld);
            if (!hasWorld) return;

            if (worldNameText != null) worldNameText.text = _requester.ResolveWorldName(worldId);
            if (worldDescriptionText != null) worldDescriptionText.text = _requester.ResolveWorldDescription(worldId);
        }

        private void ReleaseCurrentTextureIfAny()
        {
            if (_hasRequestedTexture && _downloader != null && _currentSlot >= 0)
            {
                _downloader.ReleaseTexture(_feedIndex, _currentSlot, this);
            }
            _hasRequestedTexture = false;
        }

        // ---- 画像の読み込み中表示 ----
        private void BeginImageLoading()
        {
            if (image == null) return;
            _isImageLoading = true;
            image.texture = null;
            if (loadingMaterial != null)
            {
                image.material = loadingMaterial;
                image.color = GetLoadingColor();
            }
            else
            {
                // 旧prefab: 従来どおり黒いプレースホルダー
                image.material = null;
                image.color = Color.black;
            }
        }

        private Color GetLoadingColor()
        {
            if (monitorTheme != null) return monitorTheme.LoadingColor;
            return new Color(42f / 255f, 42f / 255f, 42f / 255f, 1f);
        }

        // eterpix_monitor_themeから、配色が変わったときに呼ばれる
        public void OnThemeChanged()
        {
            if (_isImageLoading && image != null && loadingMaterial != null) image.color = GetLoadingColor();
        }

        // feedIndex/slotは、この通知が発行された時点でダウンローダが解決していたキーの内訳。
        // このモニターが待機している間にページ送り等で別スロットへ切り替わっていた場合、
        // 呼び出し元(ダウンローダ)の待機リストには古いキーの通知がまだ残っていることがある
        // (ReleaseTextureは待機リストを積極的には掃除しない: レビュー指摘)。
        // 現在表示中のフィード/スロットと一致しない通知は、誤って別の画像(R18フィルタを
        // 経由していない可能性がある)を貼り付けてしまわないよう、ここで安全に無視する。
        public void ApplyTexture(int feedIndex, int slot, Texture2D texture)
        {
            if (feedIndex != _feedIndex || slot != _currentSlot) return;
            if (texture != null && image != null)
            {
                _isImageLoading = false;
                image.material = null;
                image.texture = texture;
                // 読み込み中は明滅色(または旧prefabの黒)にしてある。この色はRawImageの
                // テクスチャに乗算されるため、白に戻さないと実際の写真が正しく表示されない。
                image.color = Color.white;
            }
        }

        // ---- 情報ウィンドウ(表示中はボタンのアイコンをXにする) ----
        public void ToggleInformationWindow()
        {
            if (informationWindowRoot == null) return;
            SetInformationOpen(!informationWindowRoot.activeSelf);
        }

        private void SetInformationOpen(bool open)
        {
            if (informationWindowRoot != null) informationWindowRoot.SetActive(open);
            if (infoIconOpen != null) infoIconOpen.SetActive(!open);
            if (infoIconClose != null) infoIconClose.SetActive(open);
        }

        // ---- 回転/UV (eterpix_cellと同じ式。4:3の枠に収め、縦画像は左右に余白を出す) ----
        private Transform[] GetRotationTargets()
        {
            if (rotationTargetObjects != null && rotationTargetObjects.Length > 0)
            {
                Transform[] result = new Transform[rotationTargetObjects.Length];
                for (int i = 0; i < rotationTargetObjects.Length; i++)
                {
                    result[i] = rotationTargetObjects[i] != null ? rotationTargetObjects[i].transform : null;
                }
                return result;
            }
            return image != null ? new[] { image.transform } : new Transform[0];
        }

        // コラージュの1セルは1024x576(16:9)。coro(img_rotation)が奇数(90/270度)のときは
        // 縦画像として梱包されているため、見た目のアスペクト比は9:16に反転する。
        // 4:3の枠(Figure)いっぱいにAspectRatioFitterで実際の縦横比まで縮め、
        // 画像自体もその縮んだ後のサイズに合わせて回転・リサイズすることで、
        // 「枠からはみ出さず」「引き伸ばさず」全体を収める。
        private void ApplyRotation(int imgRotation)
        {
            bool isRotated = (((imgRotation % 4) + 4) % 4) % 2 == 1;

            if (figureAspectFitter != null)
            {
                figureAspectFitter.aspectRatio = isRotated ? (9f / 16f) : (16f / 9f);
            }

            // AspectRatioFitterがついているRectTransform(=4:3枠の中で実際に縮んだ箱)の
            // 直後のサイズを取得するため、直ちにレイアウトを再計算する。
            RectTransform fitterRect = figureAspectFitter != null ? figureAspectFitter.GetComponent<RectTransform>() : null;
            if (fitterRect != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(fitterRect);
            }

            Quaternion rotation = Quaternion.Euler(0, 0, imgRotation * -90f);
            Vector2 containerSize = fitterRect != null ? fitterRect.rect.size : Vector2.zero;
            Vector2 imageSize = isRotated ? new Vector2(containerSize.y, containerSize.x) : containerSize;

            Transform[] targets = GetRotationTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                RectTransform rt = targets[i].GetComponent<RectTransform>();
                if (rt != null && fitterRect != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = imageSize;
                    rt.anchoredPosition = Vector2.zero;
                }
                targets[i].localRotation = rotation;
            }
        }

        private void ApplyUvRect(int imgPos)
        {
            if (image == null) return;
            const float cellW = 0.5f;
            const float cellH = 1f / 3f;
            if (imgPos >= 1 && imgPos <= 6)
            {
                int col = (imgPos - 1) % 2;
                int row = (imgPos - 1) / 2;
                float uvX = col * cellW;
                float uvY = 1f - (row + 1) * cellH;
                image.uvRect = new Rect(uvX, uvY, cellW, cellH);
            }
        }

        private int ReadInt(DataDictionary data, string key, int fallback)
        {
            if (!data.TryGetValue(key, out DataToken token)) return fallback;
            if (token.TokenType == TokenType.Double) return (int)token.Double;
            if (token.TokenType == TokenType.String && int.TryParse(token.String, out int parsed)) return parsed;
            return fallback;
        }

        private string ReadString(DataDictionary data, string key, string fallback)
        {
            if (data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.String) return token.String;
            return fallback;
        }

        // ---- ポータル ----
        // 旧系統(eterpix_item/eterpix_photo_vew)と同じく、共通ポータル(eterpix_porta_resize)を
        // spawn位置へ親付け替えで呼び寄せる。距離による自動非表示・スケール適用はポータル側が行う。
        public void OpenPortal()
        {
            if (portal == null || _currentPost == null) return;

            string worldId = ReadString(_currentPost, "woid", "");
            if (string.IsNullOrEmpty(worldId)) return;

            Transform spawn = portalSpawnPoint != null ? portalSpawnPoint : transform;
            portal.SetParentObject(spawn, worldId);
        }

        #region Editor
#if !COMPILER_UDONSHARP && UNITY_EDITOR
        // eterpix_monitor_theme.EditorApplyColorsから呼ぶ。エディタ上の読み込み中プレビュー色をテーマに合わせる
        public void EditorSetLoadingPreviewColor(Color color)
        {
            if (image == null || loadingMaterial == null || image.texture != null) return;
            if (image.color == color) return;
            UnityEditor.Undo.RecordObject(image, "Apply theme");
            image.color = color;
            UnityEditor.EditorUtility.SetDirty(image);
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(image);
        }
#endif
        #endregion
    }
}
```

- [ ] **Step 2: 旧 `SetPlaceholder` が残っていないことを確認する**

Grep で `SetPlaceholder` を `Assets/EterPix/Scripts` から検索する。期待: 0件。

- [ ] **Step 3: コンパイル確認**

「コンパイル確認」を行う。期待: EterPix 関連エラー0件。

- [ ] **Step 4: 旧prefabのフィールドが失われていないことを確認する**

`mcp__UnityMCP__execute_code` で次を実行する:

```csharp
var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/EterPix/Prefabs/EterpixMonitorUI.prefab");
var m = go.GetComponent<ali.eterpix.v2.eterpix_monitor>();
var so = new UnityEditor.SerializedObject(m);
var sb = new System.Text.StringBuilder();
foreach (var n in new[] { "image", "prevButton", "pageLabel", "userNameText", "informationWindowRoot" })
    sb.Append(n + "=" + (so.FindProperty(n).objectReferenceValue != null) + ",");
return sb.ToString();
```

期待: すべて `=True`(旧prefabの紐付けが残っている)。

- [ ] **Step 5: コミット**

```bash
git add Assets/EterPix/Scripts/eterpix_monitor.cs Assets/EterPix/Scripts/eterpix_monitor.asset
git status --short
git commit -m "feat(eterpix-v2): monitor status screens, loading pulse, info X icon, looping pages

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```

---

### Task 4: 読み込み中の明滅シェーダー

**Files:**
- Create: `Assets/EterPix/UI/LoadingPulse.shader`

**Interfaces:**
- Produces: シェーダー名 `EterPix/UI/LoadingPulse`(Task 6 のビルダーが `Shader.Find` で使い、`Assets/EterPix/UI/LoadingPulse.mat` を作る)

仕組み: 頂点カラー(= RawImage.color = テーマのボタン色)を塗り、αだけを sin で揺らす。背景の上に描かれるので、見た目は「ボタン色 ⇔ 背景色」の明滅になる(テーマ切替でマテリアルを書き換える必要が無い)。

- [ ] **Step 1: シェーダーを作成する**

```hlsl
// 画像の読み込み中にRawImageを明滅させる極小UIシェーダー。
// 色はRawImage.color(頂点カラー)をそのまま使い、αだけを時間で揺らす。
// 背景の上に描かれるため、見た目は「ボタン色 ⇔ 背景色」の明滅になる。
Shader "EterPix/UI/LoadingPulse"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture (unused)", 2D) = "white" {}
        _Speed ("Speed", Float) = 2.5
        _MinAlpha ("Min Alpha", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _Speed;
            float _MinAlpha;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = 0.5 + 0.5 * sin(_Time.y * _Speed);
                fixed4 c = i.color;
                c.a *= lerp(_MinAlpha, 1.0, t);
                return c;
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 2: コンパイル確認**

「コンパイル確認」を行う。加えて `mcp__UnityMCP__read_console(action="get", types=["error","warning"], filter_text="LoadingPulse")` で、シェーダーのエラー・警告が0件であること。

- [ ] **Step 3: コミット**

```bash
git add Assets/EterPix/UI/LoadingPulse.shader Assets/EterPix/UI/LoadingPulse.shader.meta
git status --short
git commit -m "feat(eterpix-v2): add LoadingPulse UI shader for image loading placeholder

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```

---

### Task 5: テーマの Inspector(日本語表示・自動収集・プレビュー)

**Files:**
- Create: `Assets/EterPix/Editor/eterpix_themeEditor.cs`
- Create: `Assets/EterPix/Editor/eterpix_monitor_themeEditor.cs`

**Interfaces:**
- Consumes: Task 2 の `eterpix_theme`(SerializeField名 `switchMode` / `includeCustomInCycle` / `initialTheme` / `customBackground` / `customText` / `customButton` / `customAccent` / `customAccentText`、`EditorApplyToScene()`、`GetPresetColor`)、`eterpix_monitor_theme`(`EditorCollectFromChildren()` / `EditorApplyPreset(int)`)

- [ ] **Step 1: `eterpix_themeEditor.cs` を作成する**

```csharp
#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ali.eterpix.v2;

namespace ali.eterpix
{
    // eterpix_theme のInspector。切替モード等を日本語のドロップダウンで選べるようにし、
    // 変更したらシーン内の全モニターへ開始時のテーマを塗る。
    [CustomEditor(typeof(eterpix_theme))]
    public class eterpix_themeEditor : Editor
    {
        private static readonly string[] ModeLabels = { "切替あり", "固定: 黒", "固定: 白", "固定: カスタム" };
        private static readonly string[] ThemeLabels = { "黒", "白", "カスタム" };
        private static readonly string[] RoleLabels = { "背景", "文字", "ボタン", "アクセント", "アクセント上の文字" };
        private static readonly string[] CustomProps = { "customBackground", "customText", "customButton", "customAccent", "customAccentText" };

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            serializedObject.Update();

            SerializedProperty mode = serializedObject.FindProperty("switchMode");
            SerializedProperty include = serializedObject.FindProperty("includeCustomInCycle");
            SerializedProperty initial = serializedObject.FindProperty("initialTheme");

            EditorGUILayout.LabelField("切替", EditorStyles.boldLabel);
            mode.intValue = EditorGUILayout.Popup("切替モード", mode.intValue, ModeLabels);
            bool switchable = mode.intValue == eterpix_theme.ModeSwitchable;
            using (new EditorGUI.DisabledScope(!switchable))
            {
                include.boolValue = EditorGUILayout.Toggle("カスタムを切替に含める", include.boolValue);
                initial.intValue = EditorGUILayout.Popup("初期テーマ", initial.intValue, ThemeLabels);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プリセット (左: 黒 / 右: 白、編集不可)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                for (int role = 0; role < RoleLabels.Length; role++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ColorField(new GUIContent(RoleLabels[role]), eterpix_theme.GetPresetColor(eterpix_theme.ThemeBlack, role), false, false, false);
                    EditorGUILayout.ColorField(GUIContent.none, eterpix_theme.GetPresetColor(eterpix_theme.ThemeWhite, role), false, false, false);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("カスタム", EditorStyles.boldLabel);
            for (int role = 0; role < CustomProps.Length; role++)
            {
                SerializedProperty p = serializedObject.FindProperty(CustomProps[role]);
                p.colorValue = EditorGUILayout.ColorField(new GUIContent(RoleLabels[role]), p.colorValue, true, false, false);
            }

            bool changed = serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            bool pressed = GUILayout.Button("シーン内の全モニターに開始時のテーマを適用");
            if (changed || pressed)
            {
                foreach (Object t in targets) ((eterpix_theme)t).EditorApplyToScene();
            }

            EditorGUILayout.HelpBox(
                "切替あり: モニターの切替ボタンで 黒 → 白 →(カスタム)→ 黒 と切り替わります。切り替えはその人にだけ反映され、ワールドごとに保存されます。\n" +
                "固定: 切替ボタンは表示されません。\n" +
                "モニター側で「直接編集」をONにすると、そのモニターはテーマの対象外になります。\n" +
                "このオブジェクトの名前は EterpixTheme のままにしてください(名前で探します)。",
                MessageType.None);
        }
    }
}
#endif
```

- [ ] **Step 2: `eterpix_monitor_themeEditor.cs` を作成する**

```csharp
#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ali.eterpix.v2;

namespace ali.eterpix
{
    // eterpix_monitor_theme のInspector。子の名前の接頭辞から配色の適用先を集め直すボタンと、
    // 黒/白のプレビューボタンを付ける。
    [CustomEditor(typeof(eterpix_monitor_theme))]
    public class eterpix_monitor_themeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button("子から自動収集"))
            {
                foreach (Object t in targets) ((eterpix_monitor_theme)t).EditorCollectFromChildren();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("黒でプレビュー"))
            {
                foreach (Object t in targets) ((eterpix_monitor_theme)t).EditorApplyPreset(eterpix_theme.ThemeBlack);
            }
            if (GUILayout.Button("白でプレビュー"))
            {
                foreach (Object t in targets) ((eterpix_monitor_theme)t).EditorApplyPreset(eterpix_theme.ThemeWhite);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "子オブジェクト名の接頭辞で役割が決まります: bg_ 背景 / tx_ 文字 / btn_ ボタン / acc_ アクセント / acctx_ アクセント上の文字。\n" +
                "要素を追加・改名したら「子から自動収集」を押してください。\n" +
                "実行中の色はシーンの EterpixTheme が決めます。",
                MessageType.None);
        }
    }
}
#endif
```

- [ ] **Step 3: コンパイル確認**

「コンパイル確認」を行う。期待: エラー0件。

- [ ] **Step 4: コミット**

```bash
git add Assets/EterPix/Editor/eterpix_themeEditor.cs Assets/EterPix/Editor/eterpix_themeEditor.cs.meta Assets/EterPix/Editor/eterpix_monitor_themeEditor.cs Assets/EterPix/Editor/eterpix_monitor_themeEditor.cs.meta
git status --short
git commit -m "feat(eterpix-v2): Japanese inspectors for theme and monitor theme

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```

---

### Task 6: prefab ビルダー(Stack / Split / EterpixTheme / プレビューシーン)

**Files:**
- Create: `Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs`
- Generated: `Assets/EterPix/UI/LoadingPulse.mat`、`Assets/EterPix/Prefabs/EterpixMonitor_Stack.prefab`、`Assets/EterPix/Prefabs/EterpixMonitor_Split.prefab`、`Assets/EterPix/Prefabs/EterpixTheme.prefab`、`Assets/EterPix_dev/Preview/MonitorPreview.unity`

**Interfaces:**
- Consumes: Task 2〜4 のすべて(`eterpix_monitor` のフィールド名、`eterpix_monitor_theme.EditorCollectFromChildren/EditorApplyPreset`、`eterpix_theme.SingletonObjectName`、シェーダー名)
- Produces: メニュー
  - `ali/eterpix/dev/Build Monitor Prefabs` … Stack / Split / EterpixTheme の prefab を作る(既存なら同じパスに上書きし、GUIDは維持される)
  - `ali/eterpix/dev/Build Monitor Preview Scene` … プレビューシーンを作り、追加(Additive)で開いたままにする
  - `ali/eterpix/dev/Close Monitor Preview Scene` … プレビューシーンを閉じる

**寸法(単位 px、Canvas 左上原点・下向きが +y)**

| 要素 | Stack | Split |
|---|---|---|
| Canvas | 560 × 684 | 560 × 608 |
| Header | x16 y16 528×40 | 同左 |
| PhotoFrame | x16 y72 528×396 | 同左 |
| PrevHit | x0 y72 200.8×396 (余白16 + 写真の35% 184.8) | 同左 |
| NextHit | x359.2 y72 200.8×396 | 同左 |
| 投稿 | PostBlock x16 y484 528×88 (名前 h24 / 説明 y28 h60 = 3行) | BottomRow x16 y484 528×108 → PostColumn(flex 3): 名前 h24 / 説明 y28 h80 = 4行 |
| ワールド | WorldBlock x16 y588 528×80 (名前 y0 h36 幅372 / ポータル x388 140×36 / 説明 y40 h40 = 2行) | WorldColumn(flex 2): 名前 h24 / 説明 y28 h40 / ポータル y72 h36 幅いっぱい |
| InfoWindow | x0 y56 560×628 | x0 y56 560×552 |

- [ ] **Step 1: ビルダーを作成する**

```csharp
#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System.Collections.Generic;
using System.IO;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;
using ali.eterpix.v2;

namespace ali.eterpix.dev
{
    // EterPixモニターのprefab(Stack / Split)と EterpixTheme をコードから組み立てる開発用ツール。
    // 寸法は docs/superpowers/plans/2026-09-27-eterpix-monitor-ui-remake.md の表のとおり。
    // prefabを手で直さず、ここを直して作り直すこと(同じパスに上書きするのでGUIDは変わらない)。
    public static class eterpix_monitor_builder
    {
        public const string PrefabDir = "Assets/EterPix/Prefabs";
        public const string StackPath = PrefabDir + "/EterpixMonitor_Stack.prefab";
        public const string SplitPath = PrefabDir + "/EterpixMonitor_Split.prefab";
        public const string ThemePath = PrefabDir + "/EterpixTheme.prefab";
        public const string PreviewScenePath = "Assets/EterPix_dev/Preview/MonitorPreview.unity";

        private const string FontPath = "Assets/EterPix/Fonts/NotoSansJP-Bold SDF.asset";
        private const string LoadingShaderName = "EterPix/UI/LoadingPulse";
        private const string LoadingMaterialPath = "Assets/EterPix/UI/LoadingPulse.mat";

        private const float W = 560f;
        private const float P = 16f;
        private const float HeaderY = 16f;
        private const float HeaderH = 40f;
        private const float PhotoY = 72f;
        private const float PhotoW = 528f;
        private const float PhotoH = 396f;
        private const float ContentY = 484f;
        private const float StackH = 684f;
        private const float SplitH = 608f;
        private const float HitRatio = 0.35f;
        private const float RootScale = 0.00125f;

        private const string InfoUrl = "https://api.eterpix.uk/app";
        private const string InfoHeading = "EterPixについて";
        private const string InfoBody =
            "EterPixは、VRChatで撮影した写真を共有できるSNSです。このモニターでは、EterPixに投稿された公開写真を閲覧できます。\n\n" +
            "<b>ページを切り替える</b>\n写真の左右を押すと、前後の投稿に切り替わります。表示中のページは、このインスタンスにいる全員で共有されます。\n\n" +
            "<b>ワールドへ行く</b>\n写真にワールド情報がある場合は「ポータルを開く」を押すと、撮影されたワールドへのポータルが開きます。\n\n" +
            "<b>表示色を変える</b>\n右上の切替ボタンで、黒・白などの表示色を切り替えられます。切り替えはあなたにだけ反映され、次に来たときも保持されます(ワールドによっては切り替えできません)。\n\n" +
            "<b>写真を投稿する</b>\n投稿はWebから行えます。下のURLをコピーして、ブラウザで開いてください。";
        private const string InfoFooter = "最新情報は X @_alicilia をご確認ください";

        private static TMP_FontAsset _font;

        [MenuItem("ali/eterpix/dev/Build Monitor Prefabs")]
        public static void BuildAll()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (_font == null) throw new System.Exception("Font not found: " + FontPath);
            Material loadingMat = EnsureLoadingMaterial();

            BuildMonitor(false, loadingMat);
            BuildMonitor(true, loadingMat);
            BuildThemePrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[eterpix_monitor_builder] Built Stack / Split / EterpixTheme prefabs.");
        }

        // ---------------- prefab ----------------

        private static Material EnsureLoadingMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(LoadingMaterialPath);
            if (mat != null) return mat;
            Shader shader = Shader.Find(LoadingShaderName);
            if (shader == null) throw new System.Exception("Shader not found: " + LoadingShaderName);
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, LoadingMaterialPath);
            return mat;
        }

        private static void BuildThemePrefab()
        {
            GameObject go = new GameObject(eterpix_theme.SingletonObjectName);
            try
            {
                UdonSharpUndo.AddComponent<eterpix_theme>(go);
                PrefabUtility.SaveAsPrefabAsset(go, ThemePath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void BuildMonitor(bool split, Material loadingMat)
        {
            float h = split ? SplitH : StackH;
            GameObject root = new GameObject(split ? "EterpixMonitor_Split" : "EterpixMonitor_Stack");
            try
            {
                root.transform.localScale = Vector3.one * RootScale;
                eterpix_monitor monitor = UdonSharpUndo.AddComponent<eterpix_monitor>(root);
                eterpix_monitor_theme monitorTheme = UdonSharpUndo.AddComponent<eterpix_monitor_theme>(root);
                UdonBehaviour monitorUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(monitor);
                UdonBehaviour themeUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(monitorTheme);

                // ---- Screen (Canvas)。pivotを上端中央にして、旧prefab(ルートがCanvas)と同じ位置に来るようにする
                GameObject screen = NewUI("Screen", root.transform);
                Canvas canvas = screen.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                screen.AddComponent<GraphicRaycaster>();
                screen.AddComponent<VRCUiShape>();
                RectTransform srt = (RectTransform)screen.transform;
                srt.anchorMin = new Vector2(0.5f, 0.5f);
                srt.anchorMax = new Vector2(0.5f, 0.5f);
                srt.pivot = new Vector2(0.5f, 1f);
                srt.sizeDelta = new Vector2(W, h);
                srt.localPosition = Vector3.zero;
                srt.localRotation = Quaternion.identity;
                srt.localScale = Vector3.one;

                GameObject bg = NewUI("bg_Background", screen.transform);
                Stretch(bg, 0, 0, 0, 0);
                AddImage(bg, Color.black, true);

                // ---- Header
                GameObject header = NewUI("Header", screen.transform);
                Place(header, P, HeaderY, PhotoW, HeaderH);

                GameObject logo = NewUI("tx_Logo", header.transform);
                Place(logo, 0, 0, 220, HeaderH);
                AddText(logo, "EterPix-Beta", 16, TextAlignmentOptions.MidlineLeft, false);

                GameObject page = NewUI("tx_PageLabel", header.transform);
                Place(page, PhotoW / 2f - 80f, 0, 160, HeaderH);
                TextMeshProUGUI pageText = AddText(page, "1 / 12", 16, TextAlignmentOptions.Midline, false);

                GameObject themeBtn = NewUI("btn_ThemeButton", header.transform);
                Place(themeBtn, PhotoW - 88f, 0, 40, 40);
                Button themeButton = AddButton(themeBtn, AddImage(themeBtn, Color.gray, true), true);
                BindEvent(themeButton, themeUdon, "CycleTheme");
                // 左半分が塗り、右半分が枠だけの20x20の正方形(長方形4枚)
                IconRect(themeBtn, "tx_ThemeIconFill", 10, 10, 10, 20);
                IconRect(themeBtn, "tx_ThemeIconTop", 20, 10, 10, 2);
                IconRect(themeBtn, "tx_ThemeIconBottom", 20, 28, 10, 2);
                IconRect(themeBtn, "tx_ThemeIconRight", 28, 10, 2, 20);

                GameObject infoBtn = NewUI("btn_InfoButton", header.transform);
                Place(infoBtn, PhotoW - 40f, 0, 40, 40);
                Button infoButton = AddButton(infoBtn, AddImage(infoBtn, Color.gray, true), true);
                BindEvent(infoButton, monitorUdon, "ToggleInformationWindow");
                GameObject iconOpen = NewUI("tx_InfoIconOpen", infoBtn.transform);
                Stretch(iconOpen, 0, 0, 0, 0);
                AddText(iconOpen, "i", 18, TextAlignmentOptions.Center, false).overflowMode = TextOverflowModes.Overflow;
                GameObject iconClose = NewUI("InfoIconClose", infoBtn.transform);
                Stretch(iconClose, 0, 0, 0, 0);
                CrossBar(iconClose, "tx_InfoIconCloseA", 45f);
                CrossBar(iconClose, "tx_InfoIconCloseB", -45f);
                iconClose.SetActive(false);

                // ---- Photo
                GameObject frame = NewUI("PhotoFrame", screen.transform);
                Place(frame, P, PhotoY, PhotoW, PhotoH);

                // AspectRatioFitterの親にLayoutGroupを付けないこと(CLAUDE.md aspect_ratio_fitter_under_layout_group)
                GameObject fitterGo = NewUI("ImageFitter", frame.transform);
                Stretch(fitterGo, 0, 0, 0, 0);
                AspectRatioFitter fitter = fitterGo.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = 16f / 9f;

                GameObject postImgGo = NewUI("PostImage", fitterGo.transform);
                RectTransform prt = (RectTransform)postImgGo.transform;
                prt.anchorMin = new Vector2(0.5f, 0.5f);
                prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(PhotoW, PhotoW * 9f / 16f);
                prt.anchoredPosition = Vector2.zero;
                RawImage raw = postImgGo.AddComponent<RawImage>();
                raw.texture = null;
                raw.material = loadingMat;
                raw.color = Color.gray;
                raw.raycastTarget = false;

                GameObject status = NewUI("StatusRoot", frame.transform);
                Stretch(status, 0, 0, 0, 0);
                GameObject statusTextGo = NewUI("tx_StatusText", status.transform);
                Stretch(statusTextGo, 24, 24, 24, 24);
                TextMeshProUGUI statusText = AddText(statusTextGo, "読み込み中…", 16, TextAlignmentOptions.Center, true);
                status.SetActive(false);

                // ---- ページ送り: 見た目は左右の余白の < >、判定は余白 + 写真の左右35%(写真の上は透明)
                float hitW = PhotoW * HitRatio;
                GameObject prev = NewUI("PrevHit", screen.transform);
                Place(prev, 0, PhotoY, P + hitW, PhotoH);
                Button prevButton = AddButton(prev, AddImage(prev, new Color(1f, 1f, 1f, 0f), true), false);
                BindEvent(prevButton, monitorUdon, "PagePrev");
                GameObject prevGlyph = NewUI("tx_PrevGlyph", prev.transform);
                Place(prevGlyph, 0, 0, P, PhotoH);
                AddText(prevGlyph, "<", 16, TextAlignmentOptions.Center, false).overflowMode = TextOverflowModes.Overflow;

                GameObject next = NewUI("NextHit", screen.transform);
                Place(next, W - P - hitW, PhotoY, P + hitW, PhotoH);
                Button nextButton = AddButton(next, AddImage(next, new Color(1f, 1f, 1f, 0f), true), false);
                BindEvent(nextButton, monitorUdon, "PageNext");
                GameObject nextGlyph = NewUI("tx_NextGlyph", next.transform);
                Place(nextGlyph, hitW, 0, P, PhotoH);
                AddText(nextGlyph, ">", 16, TextAlignmentOptions.Center, false).overflowMode = TextOverflowModes.Overflow;

                // ---- 投稿 / ワールド
                TextMeshProUGUI userName, description, worldName, worldDescription;
                GameObject worldRoot, postRoot;
                Button portalButton;
                if (split)
                {
                    GameObject row = NewUI("BottomRow", screen.transform);
                    Place(row, P, ContentY, PhotoW, 108);
                    HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
                    hlg.spacing = 16;
                    hlg.childAlignment = TextAnchor.UpperLeft;
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandWidth = false;
                    hlg.childForceExpandHeight = true;
                    hlg.padding = new RectOffset(0, 0, 0, 0);
                    postRoot = row;

                    GameObject postCol = NewUI("PostColumn", row.transform);
                    Column(postCol, 3);
                    userName = AddText(TopStretch(NewUI("tx_UserName", postCol.transform), 0, 24), "投稿者名", 16, TextAlignmentOptions.MidlineLeft, false);
                    description = AddText(TopStretch(NewUI("tx_PostDescription", postCol.transform), 28, 80), "投稿の説明文がここに入ります。#タグ", 13, TextAlignmentOptions.TopLeft, true);

                    worldRoot = NewUI("WorldColumn", row.transform);
                    Column(worldRoot, 2);
                    worldName = AddText(TopStretch(NewUI("tx_WorldName", worldRoot.transform), 0, 24), "ワールド名", 16, TextAlignmentOptions.MidlineLeft, false);
                    worldDescription = AddText(TopStretch(NewUI("tx_WorldDescription", worldRoot.transform), 28, 40), "ワールドの説明文がここに入ります。", 13, TextAlignmentOptions.TopLeft, true);
                    GameObject portalGo = TopStretch(NewUI("acc_PortalButton", worldRoot.transform), 72, 36);
                    portalButton = BuildPortalButton(portalGo, monitorUdon);
                }
                else
                {
                    postRoot = NewUI("PostBlock", screen.transform);
                    Place(postRoot, P, ContentY, PhotoW, 88);
                    GameObject un = NewUI("tx_UserName", postRoot.transform);
                    Place(un, 0, 0, PhotoW, 24);
                    userName = AddText(un, "投稿者名", 16, TextAlignmentOptions.MidlineLeft, false);
                    GameObject pd = NewUI("tx_PostDescription", postRoot.transform);
                    Place(pd, 0, 28, PhotoW, 60);
                    description = AddText(pd, "投稿の説明文がここに入ります。#タグ", 13, TextAlignmentOptions.TopLeft, true);

                    worldRoot = NewUI("WorldBlock", screen.transform);
                    Place(worldRoot, P, 588, PhotoW, 80);
                    GameObject wn = NewUI("tx_WorldName", worldRoot.transform);
                    Place(wn, 0, 0, PhotoW - 156f, 36);
                    worldName = AddText(wn, "ワールド名", 16, TextAlignmentOptions.MidlineLeft, false);
                    GameObject portalGo = NewUI("acc_PortalButton", worldRoot.transform);
                    Place(portalGo, PhotoW - 140f, 0, 140, 36);
                    portalButton = BuildPortalButton(portalGo, monitorUdon);
                    GameObject wd = NewUI("tx_WorldDescription", worldRoot.transform);
                    Place(wd, 0, 40, PhotoW, 40);
                    worldDescription = AddText(wd, "ワールドの説明文がここに入ります。", 13, TextAlignmentOptions.TopLeft, true);
                }

                // ---- 情報ウィンドウ(ヘッダーより下の全体を覆う。最後に置いて最前面に描く)
                GameObject info = NewUI("bg_InfoWindow", screen.transform);
                Place(info, 0, HeaderY + HeaderH, W, h - (HeaderY + HeaderH));
                AddImage(info, Color.black, true);
                BuildInfoContent(info);
                info.SetActive(false);

                // ---- ポータル出現位置 / 表示範囲トリガー(旧prefabと同じ値)
                GameObject spawn = new GameObject("PortalSpawnPoint");
                spawn.transform.SetParent(root.transform, false);
                spawn.transform.localPosition = new Vector3(-233f, -548f, 0f);

                GameObject range = new GameObject("ViewRange");
                range.transform.SetParent(root.transform, false);
                range.transform.localPosition = new Vector3(0f, -h / 2f, 0f);
                range.layer = 2; // Ignore Raycast
                range.tag = "EditorOnly";
                BoxCollider box = range.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(700f, 1000f, 1000f);
                box.center = new Vector3(0f, 0f, -500f);
                UdonSharpUndo.AddComponent<eterpix_monitor_trigger>(range);

                // ---- monitor の参照
                SerializedObject so = new SerializedObject(monitor);
                SetRef(so, "image", raw);
                SetRef(so, "figureAspectFitter", fitter);
                SetRef(so, "prevButton", prevButton);
                SetRef(so, "nextButton", nextButton);
                SetRef(so, "pageLabel", pageText);
                SetRef(so, "userNameText", userName);
                SetRef(so, "descriptionText", description);
                SetRef(so, "worldContextRoot", worldRoot);
                SetRef(so, "worldNameText", worldName);
                SetRef(so, "worldDescriptionText", worldDescription);
                SetRef(so, "openPortalButton", portalButton);
                SetRef(so, "portalSpawnPoint", spawn.transform);
                SetRef(so, "informationButton", infoButton);
                SetRef(so, "informationWindowRoot", info);
                SetRef(so, "infoIconOpen", iconOpen);
                SetRef(so, "infoIconClose", iconClose);
                SetRef(so, "statusRoot", status);
                SetRef(so, "statusText", statusText);
                SetRef(so, "loadingMaterial", loadingMat);
                SetRef(so, "monitorTheme", monitorTheme);
                SetArray(so, "normalOnlyObjects", new Object[] { page, prev, next, fitterGo, postRoot });
                so.FindProperty("debugIgnoreTriggerRange").boolValue = true; // 旧prefabと同じ(挙動を変えない)
                so.ApplyModifiedPropertiesWithoutUndo();

                // ---- 配色(黒プリセットで焼き込む)
                monitorTheme.EditorCollectFromChildren();
                monitorTheme.EditorApplyPreset(eterpix_theme.ThemeBlack);

                PrefabUtility.SaveAsPrefabAsset(root, split ? SplitPath : StackPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Button BuildPortalButton(GameObject go, UdonBehaviour monitorUdon)
        {
            Button b = AddButton(go, AddImage(go, Color.magenta, true), true);
            BindEvent(b, monitorUdon, "OpenPortal");
            GameObject label = NewUI("acctx_PortalLabel", go.transform);
            Stretch(label, 0, 0, 0, 0);
            AddText(label, "ポータルを開く", 16, TextAlignmentOptions.Center, false);
            return b;
        }

        private static void BuildInfoContent(GameObject info)
        {
            GameObject scroll = NewUI("InfoScroll", info.transform);
            Stretch(scroll, P, P, P, P);
            AddImage(scroll, new Color(1f, 1f, 1f, 0f), true);
            scroll.AddComponent<RectMask2D>();
            ScrollRect sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20f;
            sr.viewport = (RectTransform)scroll.transform;

            GameObject content = NewUI("InfoContent", scroll.transform);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;
            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sr.content = crt;

            AddText(NewUI("tx_InfoHeading", content.transform), InfoHeading, 18, TextAlignmentOptions.TopLeft, true).overflowMode = TextOverflowModes.Overflow;
            AddText(NewUI("tx_InfoBody", content.transform), InfoBody, 13, TextAlignmentOptions.TopLeft, true).overflowMode = TextOverflowModes.Overflow;
            BuildUrlField(content.transform);
            AddText(NewUI("tx_InfoFooter", content.transform), InfoFooter, 13, TextAlignmentOptions.TopLeft, true).overflowMode = TextOverflowModes.Overflow;
        }

        // 読み取り専用のURL欄(VRChatはワールドからブラウザを開けないため、コピーしてもらう)
        private static void BuildUrlField(Transform parent)
        {
            GameObject go = NewUI("btn_UrlField", parent);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 36;
            Image img = AddImage(go, Color.gray, true);

            GameObject area = NewUI("TextArea", go.transform);
            Stretch(area, 8, 0, 8, 0);
            area.AddComponent<RectMask2D>();

            GameObject textGo = NewUI("tx_UrlText", area.transform);
            Stretch(textGo, 0, 0, 0, 0);
            TextMeshProUGUI t = AddText(textGo, InfoUrl, 13, TextAlignmentOptions.MidlineLeft, false);
            t.overflowMode = TextOverflowModes.Overflow;

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport = (RectTransform)area.transform;
            field.textComponent = t;
            field.targetGraphic = img;
            field.fontAsset = _font;
            field.pointSize = 13;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.readOnly = true;
            field.transition = Selectable.Transition.None;
            Navigation nav = new Navigation();
            nav.mode = Navigation.Mode.None;
            field.navigation = nav;
            field.text = InfoUrl;
        }

        // ---------------- helpers ----------------

        private static GameObject NewUI(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 0;
            go.transform.SetParent(parent, false);
            return go;
        }

        // 親の左上を原点(下向き+y)にして配置する
        private static RectTransform Place(GameObject go, float x, float y, float w, float h)
        {
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static RectTransform Stretch(GameObject go, float left, float top, float right, float bottom)
        {
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        // 親の幅いっぱい・上端からyの位置に高さhで置く
        private static GameObject TopStretch(GameObject go, float y, float h)
        {
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -y);
            rt.sizeDelta = new Vector2(0f, h);
            return go;
        }

        private static void Column(GameObject go, float flexibleWidth)
        {
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = 0;
            le.preferredWidth = 0;
            le.flexibleWidth = flexibleWidth;
        }

        private static Image AddImage(GameObject go, Color color, bool raycast)
        {
            Image img = go.AddComponent<Image>();
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        private static void IconRect(GameObject parent, string name, float x, float y, float w, float h)
        {
            GameObject go = NewUI(name, parent.transform);
            Place(go, x, y, w, h);
            AddImage(go, Color.white, false);
        }

        private static void CrossBar(GameObject parent, string name, float angle)
        {
            GameObject go = NewUI(name, parent.transform);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(22f, 3f);
            rt.anchoredPosition = Vector2.zero;
            rt.localEulerAngles = new Vector3(0f, 0f, angle);
            AddImage(go, Color.white, false);
        }

        private static TextMeshProUGUI AddText(GameObject go, string text, float size, TextAlignmentOptions align, bool wrap)
        {
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.enableAutoSizing = false;
            t.fontStyle = FontStyles.Normal;
            t.color = Color.white;
            t.text = text;
            t.alignment = align;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.richText = true;
            t.margin = Vector4.zero;
            return t;
        }

        private static Button AddButton(GameObject go, Image target, bool tint)
        {
            Button b = go.AddComponent<Button>();
            b.targetGraphic = target;
            Navigation nav = new Navigation();
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            if (tint)
            {
                // 押下の色はテーマに依らず、塗りを暗くするだけ(U#から ColorBlock を書き換えないため)
                ColorBlock cb = ColorBlock.defaultColorBlock;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
                cb.selectedColor = Color.white;
                cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                cb.colorMultiplier = 1f;
                cb.fadeDuration = 0.1f;
                b.transition = Selectable.Transition.ColorTint;
                b.colors = cb;
            }
            else
            {
                b.transition = Selectable.Transition.None;
            }
            return b;
        }

        private static void BindEvent(Button button, UdonBehaviour udon, string eventName)
        {
            UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(udon.SendCustomEvent), eventName);
        }

        private static void SetRef(SerializedObject so, string name, Object value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) throw new System.Exception("eterpix_monitor has no field: " + name);
            p.objectReferenceValue = value;
        }

        private static void SetArray(SerializedObject so, string name, Object[] values)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) throw new System.Exception("eterpix_monitor has no field: " + name);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
#endif
```

- [ ] **Step 2: コンパイル確認**

「コンパイル確認」を行う。期待: エラー0件。

- [ ] **Step 3: prefab を生成する**

`mcp__UnityMCP__execute_menu_item(menu_path="ali/eterpix/dev/Build Monitor Prefabs")` を実行し、`read_console(types=["error","warning","log"], filter_text="eterpix", count="30")` を確認する。

期待:
- ログ `[eterpix_monitor_builder] Built Stack / Split / EterpixTheme prefabs.`
- エラー0件
- `Assets/EterPix/Prefabs/EterpixMonitor_Stack.prefab` / `EterpixMonitor_Split.prefab` / `EterpixTheme.prefab` / `Assets/EterPix/UI/LoadingPulse.mat` が存在する

- [ ] **Step 4: 生成物を検査する**

`mcp__UnityMCP__execute_code` で次を実行する:

```csharp
var sb = new System.Text.StringBuilder();
foreach (var path in new[] { "Assets/EterPix/Prefabs/EterpixMonitor_Stack.prefab", "Assets/EterPix/Prefabs/EterpixMonitor_Split.prefab" })
{
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    sb.AppendLine(path + " rootCanvas=" + (go.GetComponent<UnityEngine.Canvas>() != null) + " scale=" + go.transform.localScale.x);
    var m = go.GetComponent<ali.eterpix.v2.eterpix_monitor>();
    var so = new UnityEditor.SerializedObject(m);
    var it = so.GetIterator();
    it.NextVisible(true);
    while (it.NextVisible(false))
    {
        if (it.propertyType == UnityEditor.SerializedPropertyType.ObjectReference && it.objectReferenceValue == null)
            sb.AppendLine("  null: " + it.name);
    }
    foreach (var b in go.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        sb.AppendLine("  button " + b.name + " -> " + (b.onClick.GetPersistentEventCount() > 0 ? b.onClick.GetPersistentMethodName(0) : "NONE"));
    foreach (var img in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        if (img.sprite != null) sb.AppendLine("  SPRITE FOUND: " + img.name);
    var th = go.GetComponent<ali.eterpix.v2.eterpix_monitor_theme>();
    var tso = new UnityEditor.SerializedObject(th);
    foreach (var n in new[] { "backgroundImages", "texts", "textImages", "buttonImages", "accentImages", "accentTexts" })
        sb.AppendLine("  theme " + n + "=" + tso.FindProperty(n).arraySize);
}
return sb.ToString();
```

期待:
- 両方 `rootCanvas=False scale=0.00125`
- `null:` に出るのは `portal` と `debugLog` だけ(どちらも実行時に名前で探す)。`rotationTargetObjects` は配列なので出ない
- ボタンはすべて `SendCustomEvent`(NONE が無い): `btn_ThemeButton`, `btn_InfoButton`, `PrevHit`, `NextHit`, `acc_PortalButton`
- `SPRITE FOUND` が0件
- theme の配列: `backgroundImages=2`、`buttonImages=3`(Theme / Info / UrlField)、`accentImages=1`、`accentTexts=1`、`textImages=6`(テーマアイコン4 + ×の棒2)、`texts=14`(Logo, PageLabel, InfoIconOpen, StatusText, PrevGlyph, NextGlyph, UserName, PostDescription, WorldName, WorldDescription, InfoHeading, InfoBody, UrlText, InfoFooter)

食い違いがあれば原因を直して Step 3 からやり直す。

- [ ] **Step 5: プレビューシーンのメニューをビルダーに追加する**

`eterpix_monitor_builder` クラスの `// ---------------- helpers ----------------` の直前に追加する:

```csharp
        // ---------------- preview ----------------
        // 見た目確認用。Stack(上段)/ Split(下段)× 5状態を並べる。
        // 今開いているシーンを閉じないよう、Additiveで開く(保存はこのシーンだけ)。
        [MenuItem("ali/eterpix/dev/Build Monitor Preview Scene")]
        public static void BuildPreviewScene()
        {
            CloseMonitorPreviewScene();
            Directory.CreateDirectory(Path.GetDirectoryName(PreviewScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            GameObject rootGo = new GameObject("MonitorPreview");
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            rootGo.transform.position = new Vector3(1000f, 1000f, 0f);

            GameObject stack = AssetDatabase.LoadAssetAtPath<GameObject>(StackPath);
            GameObject split = AssetDatabase.LoadAssetAtPath<GameObject>(SplitPath);
            string[] labels = { "Normal-Black", "Normal-White", "Untrusted-Black", "Info-White", "NoWorld-Black" };
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < labels.Length; col++)
                {
                    GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(row == 0 ? stack : split, rootGo.transform);
                    inst.name = (row == 0 ? "Stack_" : "Split_") + labels[col];
                    inst.transform.localPosition = new Vector3(col * 0.9f, row == 0 ? 0f : -1.0f, 0f);
                    ApplyPreviewState(inst, col);
                }
            }

            GameObject camGo = new GameObject("PreviewCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.25f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            camGo.transform.position = new Vector3(1000f + 2.15f, 1000f - 0.93f, -2f);
            camGo.transform.rotation = Quaternion.identity;
            cam.enabled = false; // 他のシーンの表示を邪魔しない。撮影時は camera 指定で描画される

            EditorSceneManager.SaveScene(scene, PreviewScenePath);
            Debug.Log("[eterpix_monitor_builder] Preview scene built: " + PreviewScenePath);
        }

        [MenuItem("ali/eterpix/dev/Close Monitor Preview Scene")]
        public static void CloseMonitorPreviewScene()
        {
            Scene existing = SceneManager.GetSceneByPath(PreviewScenePath);
            if (existing.IsValid() && existing.isLoaded) EditorSceneManager.CloseScene(existing, true);
        }

        // 0: 通常(黒) 1: 通常(白) 2: 信頼されていないURL(黒) 3: 情報ウィンドウ(白) 4: ワールド無し(黒)
        private static void ApplyPreviewState(GameObject inst, int state)
        {
            eterpix_monitor_theme theme = inst.GetComponent<eterpix_monitor_theme>();
            eterpix_monitor monitor = inst.GetComponent<eterpix_monitor>();
            SerializedObject so = new SerializedObject(monitor);
            bool white = state == 1 || state == 3;
            theme.EditorApplyPreset(white ? eterpix_theme.ThemeWhite : eterpix_theme.ThemeBlack);

            // 写真の代わりに中間のグレーを置く(明滅マテリアルを外す)
            RawImage raw = (RawImage)so.FindProperty("image").objectReferenceValue;
            raw.material = null;
            raw.color = new Color(0.45f, 0.45f, 0.45f, 1f);

            if (state == 2)
            {
                GameObject status = (GameObject)so.FindProperty("statusRoot").objectReferenceValue;
                TMP_Text statusText = (TMP_Text)so.FindProperty("statusText").objectReferenceValue;
                status.SetActive(true);
                statusText.text = so.FindProperty("untrustedUrlMessage").stringValue;
                SerializedProperty normal = so.FindProperty("normalOnlyObjects");
                for (int i = 0; i < normal.arraySize; i++) ((GameObject)normal.GetArrayElementAtIndex(i).objectReferenceValue).SetActive(false);
                ((GameObject)so.FindProperty("worldContextRoot").objectReferenceValue).SetActive(false);
            }
            if (state == 3)
            {
                ((GameObject)so.FindProperty("informationWindowRoot").objectReferenceValue).SetActive(true);
                ((GameObject)so.FindProperty("infoIconOpen").objectReferenceValue).SetActive(false);
                ((GameObject)so.FindProperty("infoIconClose").objectReferenceValue).SetActive(true);
            }
            if (state == 4)
            {
                ((GameObject)so.FindProperty("worldContextRoot").objectReferenceValue).SetActive(false);
            }
            Canvas.ForceUpdateCanvases();
        }
```

- [ ] **Step 6: コンパイル確認とプレビューシーン生成**

「コンパイル確認」→ `execute_menu_item("ali/eterpix/dev/Build Monitor Preview Scene")` → コンソールにログ `Preview scene built` があり、エラー0件であること。

- [ ] **Step 7: スクリーンショットを撮る**

`mcp__UnityMCP__manage_camera(action="screenshot", camera="PreviewCamera", include_image=true, max_resolution=1600, screenshot_file_name="monitor_preview_all", output_folder="Assets/EterPix_dev/Preview/Screenshots")`

カメラが無効(`enabled=false`)のせいで撮影に失敗した場合は、`execute_code` で `PreviewCamera` の Camera を有効にしてから撮り直し、撮影後に無効へ戻す。

撮れた画像のファイルパスを**最終報告に含める**(controller が見た目を判定する。サブエージェントはデザインの良し悪しを判断しない。明らかな崩れ ―― 要素が枠外に出る、文字が表示されない、ピンク(シェーダーエラー)になる ―― だけ報告する)。

- [ ] **Step 8: プレビューシーンを閉じる**

`execute_menu_item("ali/eterpix/dev/Close Monitor Preview Scene")`。ユーザーの作業シーン(`Assets/セーブ/20260927-UI-REMAKE.unity`)は保存も変更もしないこと。ビルダーが一時的に作ったオブジェクトで作業シーンが dirty になっていても、**保存しない**。

- [ ] **Step 9: コミット**

```bash
git add Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs.meta Assets/EterPix/UI/LoadingPulse.mat Assets/EterPix/UI/LoadingPulse.mat.meta Assets/EterPix/Prefabs/EterpixMonitor_Stack.prefab Assets/EterPix/Prefabs/EterpixMonitor_Stack.prefab.meta Assets/EterPix/Prefabs/EterpixMonitor_Split.prefab Assets/EterPix/Prefabs/EterpixMonitor_Split.prefab.meta Assets/EterPix/Prefabs/EterpixTheme.prefab Assets/EterPix/Prefabs/EterpixTheme.prefab.meta Assets/EterPix_dev/Preview
git status --short
git commit -m "feat(eterpix-v2): code-built Stack/Split monitor prefabs, theme prefab, preview scene

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```

---

### Task 7: 見た目のレビューと調整(controller が行う)

**このタスクはサブエージェントに渡さない。** controller(メインのセッション)が Task 6 のスクリーンショットを見て判断する。

- [ ] **Step 1:** Task 6 Step 7 の画像を見て、仕様書「4. デザイン原則」「5. レイアウト」と照合する(余白16の揃い、写真を隠す要素が無いこと、長方形のみ、線が無いこと、文字の大きさの階層、白テーマの見え方、エラー文の収まり、ワールド無しで投稿が全幅になること、情報ウィンドウのX)
- [ ] **Step 2:** 直すべき点があれば、`eterpix_monitor_builder.cs` の定数・配置だけを直し、Build Monitor Prefabs → Build Monitor Preview Scene → スクリーンショット → Close を繰り返す。仕様を変える判断が必要な場合はユーザーに確認する
- [ ] **Step 3:** スクリーンショットをユーザーに見せて承認を得る
- [ ] **Step 4:** 変更があればコミット・プッシュする(Task 6 Step 9 と同じファイル群)

---

### Task 8: テンプレート移行・旧prefab退避・ドキュメント

**Files:**
- Move: `Assets/EterPix/Prefabs/EterpixMonitorUI.prefab` → `Assets/EterPix/Prefabs/Legacy/EterpixMonitorUI.prefab`
- Modify: `Assets/EterPix/Etp_テンプレート.prefab`
- Modify: `Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs`(移行メニューを追加)
- Modify: `Assets/EterPix/README.md`、`Assets/EterPix_dev/code/CLAUDE.md`

**前提:** このタスクの開始前に、controller がユーザーに `EterpixMonitorUI.prefab` / `Etp_テンプレート.prefab` の未コミット変更の扱いを確認済みであること(controller から指示が無ければ着手しない)。

- [ ] **Step 1: 旧prefabを Legacy へ移動する**

`mcp__UnityMCP__execute_code`:

```csharp
UnityEditor.AssetDatabase.CreateFolder("Assets/EterPix/Prefabs", "Legacy");
string err = UnityEditor.AssetDatabase.MoveAsset("Assets/EterPix/Prefabs/EterpixMonitorUI.prefab", "Assets/EterPix/Prefabs/Legacy/EterpixMonitorUI.prefab");
return string.IsNullOrEmpty(err) ? "moved" : err;
```

期待: `moved`(`Legacy` フォルダが既にある場合 `CreateFolder` は `Legacy 1` を作ってしまうので、先に `AssetDatabase.IsValidFolder("Assets/EterPix/Prefabs/Legacy")` を確認し、あれば作らない)。

- [ ] **Step 2: 移行メニューをビルダーに追加する**

`eterpix_monitor_builder` の `// ---------------- preview ----------------` の直前に追加する:

```csharp
        // ---------------- template migration ----------------
        private const string TemplatePath = "Assets/EterPix/Etp_テンプレート.prefab";
        private const string LegacyMonitorPath = "Assets/EterPix/Prefabs/Legacy/EterpixMonitorUI.prefab";

        // テンプレート内の旧モニター(EterpixMonitorUI)を Stack に差し替え、EterpixTheme を追加する。
        // 位置・回転・スケール・アクティブ状態・offset を引き継ぐ。
        [MenuItem("ali/eterpix/dev/Migrate Template To Stack")]
        public static void MigrateTemplate()
        {
            GameObject stack = AssetDatabase.LoadAssetAtPath<GameObject>(StackPath);
            GameObject themePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ThemePath);
            GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyMonitorPath);
            if (stack == null || themePrefab == null || legacy == null) throw new System.Exception("Build prefabs and move legacy prefab first.");

            GameObject contents = PrefabUtility.LoadPrefabContents(TemplatePath);
            try
            {
                List<GameObject> olds = new List<GameObject>();
                foreach (Transform t in contents.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                    if (source == legacy) olds.Add(go);
                }

                int replaced = 0;
                foreach (GameObject old in olds)
                {
                    Transform parent = old.transform.parent;
                    int sibling = old.transform.GetSiblingIndex();
                    GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(stack, parent);
                    inst.name = old.name.Replace("EterpixMonitorUI", "EterpixMonitor_Stack");
                    inst.transform.SetSiblingIndex(sibling);
                    inst.transform.localPosition = old.transform.localPosition;
                    inst.transform.localRotation = old.transform.localRotation;
                    inst.transform.localScale = old.transform.localScale;
                    inst.SetActive(old.activeSelf);

                    SerializedObject oldSo = new SerializedObject(old.GetComponent<eterpix_monitor>());
                    SerializedObject newSo = new SerializedObject(inst.GetComponent<eterpix_monitor>());
                    newSo.FindProperty("offset").intValue = oldSo.FindProperty("offset").intValue;
                    newSo.ApplyModifiedPropertiesWithoutUndo();

                    Object.DestroyImmediate(old);
                    replaced++;
                }

                if (contents.transform.Find(eterpix_theme.SingletonObjectName) == null)
                {
                    GameObject theme = (GameObject)PrefabUtility.InstantiatePrefab(themePrefab, contents.transform);
                    theme.name = eterpix_theme.SingletonObjectName;
                }

                PrefabUtility.SaveAsPrefabAsset(contents, TemplatePath);
                Debug.Log("[eterpix_monitor_builder] Template migrated. replaced=" + replaced);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
```

- [ ] **Step 3: コンパイル確認と移行の実行**

「コンパイル確認」→ `execute_menu_item("ali/eterpix/dev/Migrate Template To Stack")` → コンソールに `Template migrated. replaced=4` があり、エラー0件であること。

- [ ] **Step 4: 移行結果を検査する**

`mcp__UnityMCP__execute_code`:

```csharp
var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/EterPix/Etp_テンプレート.prefab");
var sb = new System.Text.StringBuilder();
foreach (var m in go.GetComponentsInChildren<ali.eterpix.v2.eterpix_monitor>(true))
    sb.AppendLine(m.name + " src=" + UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(m.gameObject)) + " pos=" + m.transform.localPosition + " scale=" + m.transform.localScale.x);
sb.AppendLine("theme=" + (go.transform.Find("EterpixTheme") != null));
foreach (var dep in UnityEditor.AssetDatabase.GetDependencies("Assets/EterPix/Etp_テンプレート.prefab", true))
    if (!dep.StartsWith("Assets/EterPix/") && !dep.StartsWith("Packages/") && !dep.StartsWith("Assets/SerializedUdonPrograms/") && !dep.StartsWith("Assets/TextMesh Pro/")) sb.AppendLine("EXTERNAL DEP: " + dep);
return sb.ToString();
```

期待: モニターが4台すべて `src=Assets/EterPix/Prefabs/EterpixMonitor_Stack.prefab`、`scale=0.00125`、`theme=True`、`EXTERNAL DEP` が0件(配布物が `Assets/EterPix` の外を参照しないこと)。

- [ ] **Step 5: README を更新する**

`Assets/EterPix/README.md` の「使い方」の 4. を次に置き換え、その後に「テーマ」節を追加する:

```markdown
4. モニターを増やす場合は `Prefabs/EterpixMonitor_Stack.prefab`(縦積み)または `Prefabs/EterpixMonitor_Split.prefab`(下段が横並び)を `EterpixRequester` の子に置く
   - 大きさはモニターのルートの Scale で変える(子の Scale は変えない)
   - 旧デザインの `Prefabs/Legacy/EterpixMonitorUI.prefab` も引き続き使える(エラー表示・テーマには非対応)

### テーマ(表示色)

- `EterpixTheme`(テンプレートに含まれる。`Prefabs/EterpixTheme.prefab`)の Inspector で設定する
  - 切替モード: `切替あり`(モニターの切替ボタンで 黒 → 白 →(カスタム)→ 黒)/ `固定: 黒` / `固定: 白` / `固定: カスタム`
  - 切り替えは押した本人にだけ反映され、ワールドごとに保存される
  - `カスタム` の色は自由に入力できる
- モニターの `eterpix_monitor_theme` で `直接編集` を ON にすると、そのモニターはテーマの対象外になる(各 Image / TMP の色を直接編集する)
```

「注意」の「次のオブジェクト名は変更しないこと」に `EterpixTheme` を追加し、「モニターの表示範囲は子の `collider`」を「モニターの表示範囲は子の `ViewRange`(旧prefabでは `collider`)」に直す。

「フォルダ構成」の `Prefabs/` の説明を `… 個別の prefab(Legacy/ に旧モニター)` に、`UI/` を `… 読み込み中の明滅シェーダー等` に直す。

- [ ] **Step 6: CLAUDE.md の組み立てメモを更新する**

`Assets/EterPix_dev/code/CLAUDE.md` の「## v2 (...) prefab組み立てメモ」の末尾に追加する:

```markdown
- **新モニター(Stack / Split)は手で組まない。** `Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs` のメニュー `ali/eterpix/dev/Build Monitor Prefabs` がコードから組み立てる(同じパスに上書きするのでGUIDは維持)。寸法やボタンの登録はビルダーを直して作り直す。見た目の確認は `Build Monitor Preview Scene` → PreviewCamera のスクリーンショット → `Close Monitor Preview Scene`。
- 新モニターのルートはCanvasではない(空のGameObject、scale 0.00125 = 1px 1.25mm)。Canvas・GraphicRaycaster・VRCUiShape は子の `Screen` にある。
- テーマの配色先は子オブジェクト名の接頭辞(`bg_` / `tx_` / `btn_` / `acc_` / `acctx_`)で決まる。要素を足したら `eterpix_monitor_theme` の「子から自動収集」を押す。
- ボタンの押下色(ColorBlock)はテーマで変えない(U#から ColorBlock を書き換えるのが未検証のため)。塗りを 0.75 倍に暗くするだけ。
```

「## BUGS (未修正)」に追加する:

```markdown
- [ ] 表示範囲トリガー(旧prefabの `collider`、新prefabの `ViewRange`)に `EditorOnly` タグが付いており、ビルド時に削除される。そのため実機では `OnViewRangeEnter` が呼ばれず、`debugIgnoreTriggerRange = true` で常に画像を要求する状態になっている。UIリメイクでは挙動を変えないため同じ値を再現した (発見日: 2026-09-27)
```

- [ ] **Step 7: コミット**

```bash
git add "Assets/EterPix/Prefabs/Legacy" "Assets/EterPix/Prefabs/Legacy.meta" Assets/EterPix/Prefabs/EterpixMonitorUI.prefab Assets/EterPix/Prefabs/EterpixMonitorUI.prefab.meta "Assets/EterPix/Etp_テンプレート.prefab" Assets/EterPix_dev/Editor/eterpix_monitor_builder.cs Assets/EterPix/README.md Assets/EterPix_dev/code/CLAUDE.md
git status --short
git commit -m "feat(eterpix-v2): switch template to Stack monitor, move old monitor to Legacy, docs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```
(`git add` の旧パスは削除として記録される。`git status --short` で `R`(rename)または `D`+`A` になっていることを確認する)

---

## 手動確認(実装後、ユーザーに依頼する項目)

自動では確認できないため、完了報告でユーザーに依頼する:
1. VRChat SDK の Network ID ユーティリティで、テンプレートを置いたシーンの Network ID 競合を解消する(UdonBehaviourが増減したため)
2. ClientSim / VRChat で: 読み込み中 → 通常表示、写真左右の判定でページ送り(端でループ)、ポータルボタン、ⓘ ⇔ X、テーマ切替と入り直し後の保持
3. VRChat で「信頼されていないURLを許可」OFF のときに URL 許可の案内が出ること
