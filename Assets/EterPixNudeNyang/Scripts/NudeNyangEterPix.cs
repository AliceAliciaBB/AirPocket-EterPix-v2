using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using UnityEngine.UI;
using TMPro;

namespace NudeNyang
{
    // NudeNyangタブレットの12番目のアプリ「EterPix-app」.
    // 計画: docs/superpowers/plans/2026-10-31-eterpix-app.md
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class NudeNyangEterPix : UdonSharpBehaviour
    {
        // ---- 画面の同期(他のプレイヤーにも自分の画面が見える) ----
        // NudeNyang 本体の XFeed と同じ作法: 操作した人がオーナーになり RequestSerialization、他の人は OnDeserialization で追従.
        // 投稿は添字でなく poid(投稿ID)で指す(フィードの順が人によって違い得るため)
        [UdonSynced] private int sRev;
        [UdonSynced] private int sScreen, sReturn, sBack, sZoomMode;
        [UdonSynced] private bool sStarted, sDark;
        [UdonSynced] private string sCursor = "", sZoomPoid = "", sLiked = "";
        private bool syncDirty, applyingRemote, pendingRemote;
        private float nextSend, retryAt;
        private const float SyncInterval = 0.2f;
        // NudeNyangTabletShell.AppCount の12番目. パッチ(P1〜P9)で追加される
        public const int AppId = 12;

        // 画面
        public const int ScreenStart = 0;
        public const int ScreenSwipe = 1;
        public const int ScreenList = 2;
        public const int ScreenInfo = 3;
        public const int ScreenGroup = 4;
        public const int ScreenSettings = 5;

        public NudeNyangTablet pad;
        public RectTransform panel;
        public CanvasGroup group;
        public TMP_Text title;
        public EterPixNudeNyang.EterPixAppFeed feed;
        public EterPixNudeNyang.EterPixAppImages images;

        // 画面の根(Builderが作る)
        public GameObject startScreen;
        public GameObject swipeScreen;
        public GameObject infoScreen;
        public GameObject groupScreen;
        public GameObject settingsScreen;
        public Image[] settingImages;   // 設定の分割ボタン 0:黒 1:白 2:しない 3:する
        public TMP_Text[] settingLabels;
        public Image[] settingIcons;    // テーマの2つは文字の代わりにアイコン(null=文字)
        [HideInInspector] public bool swapNav; // 進む・戻るの入れ替え(自分の好み. 同期しない. セッション内だけ)

        // ヘッダー
        public TMP_Text infoButtonLabel;
        public TMP_Text groupButtonLabel;
        public TMP_Text settingsButtonLabel;
        public GameObject settingsIcon; // 歯車の画像. 設定を開いている間は隠して×の文字を出す
        public GameObject counterRoot;     // 現在値 / 全体数(それぞれ3桁ぶんの幅)
        public TMP_Text counterCurrent;
        public TMP_Text counterTotal;

        // スワイプ画面
        public EterPixNudeNyang.EterPixAppGeometry geometry;
        public RectTransform cardFrame;
        // 実行時の RectTransform.rect は、タブレットがスリープ中だと0になる場合があるため、ビルド時の大きさを持つ
        public Vector2 panelSize = new Vector2(1173f, 660f);
        public Vector2 cardSize = new Vector2(774f, 432f);
        public RectTransform photoRect;
        public RawImage photo;
        public TMP_Text userLabel;
        public TMP_Text worldLabel;
        public TMP_Text swipeDes;          // 投稿の説明(pode). スワイプの情報帯
        public RectTransform swipeBandRect;
        public TMP_Text zoomDes;           // 拡大の情報帯
        public TMP_Text[] rowDes;          // 一覧の各行
        public TMP_Text cardNotice;
        public GameObject buttonRow;
        // ドラッグでのスワイプ(Card の ScrollRect を横に引く. 離すと元に戻る. 閾値を越えたら次へ)
        public ScrollRect swipeScroll;
        public RectTransform swipeBody;
        public Image leftHot;
        public Image rightHot;
        // ボタン列 [戻る][リロード][ポータル][☆/★][進む](入れ替え時は左右の文字と動作だけ替わる)
        public TMP_Text navLeftLabel;
        public TMP_Text navRightLabel;
        public TMP_Text navPortalLabel;
        public CanvasGroup navLeftGroup;
        public CanvasGroup navRightGroup;
        public CanvasGroup navReloadGroup;
        public CanvasGroup navPortalGroup;
        public CanvasGroup navStarGroup;
        public GameObject starOutline;
        public GameObject starFilled;
        public float swipeHotStart = 0.04f;   // カード幅に対する割合: ここからボタンが明るくなる
        public float swipeCommit = 0.12f;     // カード幅に対する割合: ここを越えたら確定(NudeNyang の感度をかけた量で比べる)
        public float swipeAxisLock = 0.02f;   // この割合を越えて動いた時点で、横(1)か縦(2)に軸を固定する
        public Image starHot;
        public TMP_Text noticeLabel;
        public GameObject noWorldNotice;     // 下スワイプでワールド情報が無いときの一瞬の表示
        private float noWorldUntil;
        private int swipeAxis;                // 0=未確定 1=横 2=縦
        private bool swipeDragging, swipeLeftUse, swipeRightUse;
        private float suppressTapUntil;
        private float lastPhotoTap = -10f;
        public GameObject endScreen;
        public TMP_Text endSummary;
        public GameObject confirmModal;
        // 現在のカードから先に取っておく投稿数
        public int lookAhead = 6;

        // 気になる投稿一覧(スクロールする行を使い回す. 見えている分だけ実体を持つ)
        public const int RowPool = 8;
        public const float RowHeight = 211f;      // 行の間隔 = 画像 192 + 間隔 19(モックの 160 + 16 を 1.2 倍)
        [HideInInspector] public float rowHeight = 211f; // いまの行の間隔(向きによらず同じ)
        public GameObject heartButton;
        public GameObject groupIcon; // グループのアイコン(画像). 開いている間は隠して×の文字を出す
        public TMP_Text heartLabel;
        public GameObject heartIcon;  // 気になる一覧ボタンの画像. 一覧を開いている間は隠して×の文字を出す
        public GameObject listScreen;
        public RectTransform listContent;
        public RectTransform listViewport;
        public TMP_Text listEmpty;
        public TMP_Text listHeading;
        public InputField infoUrlField;
        public const string InfoUrl = "https://eterpix.com/";
        public GameObject[] rowRoots;
        public RawImage[] rowThumbs;
        public TMP_Text[] rowNames;
        public TMP_Text[] rowWorlds;
        public InputField[] rowWebFields;
        public InputField[] rowHqFields;
        public GameObject[] rowPortalButtons;
        public TMP_Text[] rowNoWorld;
        [HideInInspector] public int[] likedIndices = new int[0];
        private int listFirst = -1;

        // 拡大(全面): 写真を押す → 拡大(情報帯あり) → 押す → UIなし → 押す → 閉じる
        public GameObject zoomRoot;
        public RectTransform zoomPhotoRect;
        public RawImage zoomPhoto;
        public GameObject zoomBand;
        public TMP_Text zoomUser;
        public TMP_Text zoomWorld;
        [HideInInspector] public int zoomMode; // 0=閉じている 1=情報帯あり 2=UIなし
        [HideInInspector] public int zoomIndex = -1;

        // 起動画面
        public GameObject loadingRow;
        public TMP_Text loadingLabel;
        public TMP_Text percentLabel;
        public RectTransform barFill;
        public GameObject startButton;
        public TMP_Text statusLabel;

        // テーマ(NudeNyang本体のテーマとは独立. 合意書に連動の記載なし)
        public Graphic[] bgGraphics;
        public Graphic[] panelGraphics;
        public Graphic[] btnGraphics;
        public Color darkBtn = new Color32(0x2A, 0x2A, 0x2A, 0xFF);
        public Color lightBtn = new Color32(0xE6, 0xE6, 0xE6, 0xFF);
        public Graphic[] textGraphics;
        public Graphic[] subGraphics;
        public Color darkBg = new Color(0.078f, 0.078f, 0.078f, 1f);
        public Color darkPanel = new Color(0.12f, 0.12f, 0.12f, 1f);
        public Color darkText = Color.white;
        public Color darkSub = new Color(0.63f, 0.63f, 0.63f, 1f);
        public Color lightBg = new Color(0.95f, 0.95f, 0.95f, 1f);
        public Color lightPanel = new Color(0.88f, 0.88f, 0.88f, 1f);
        public Color lightText = new Color(0.078f, 0.078f, 0.078f, 1f);
        public Color lightSub = new Color(0.38f, 0.38f, 0.38f, 1f);

        // 起動時に先に取っておくコラージュの枚数. これが取れたら「push to start」を出す.
        // 5秒間隔で1枚ずつなので、全部(約26枚)を待つと約130秒かかる. 残りは開始後に裏で取る.
        // 仮の値(合意書「ロードの定義」が未決. 決まり次第この値を調整する)
        public int startupPreload = 3;

        [HideInInspector] public int screen = ScreenStart;
        [HideInInspector] public int returnScreen = ScreenStart;
        [HideInInspector] public int overlayBack = ScreenStart;
        [HideInInspector] public bool isDark = true;
        [HideInInspector] public bool ready;

        // 現在のカード(feed の添字)と、気になる印
        [HideInInspector] public int cursor;
        [HideInInspector] public bool[] liked = new bool[0];
        [HideInInspector] public int likedCount;

        private bool visible;
        private bool fetchStarted;
        private bool themeApplied;
        private bool started;

        // NudeNyangTabletShell から毎フレーム呼ばれる(パッチP6)
        public void Present()
        {
            if (pad == null || pad.shell == null || panel == null) return;
            bool show = pad.shell.AppWindowVisible(AppId) && !pad.storageSleeping;
            if (visible != show)
            {
                visible = show;
                panel.gameObject.SetActive(show);
                if (!show) CancelPortal();
                Debug.Log("[EtpTrace] eterpix-app visible=" + show);
            }
            // 初めて開いたときに投稿を取りに行く(VRChatのダウンロードは5秒間隔なので、開くまで取らない)
            if (show && !fetchStarted && feed != null)
            {
                fetchStarted = true;
                if (!themeApplied) { themeApplied = true; ApplyTheme(); }
                ShowScreen(ScreenStart);
                RefreshStart();
                feed.Fetch();
            }
            if (!show) return;
            // 他のプレイヤーの操作に追従している間・ジェスチャ中は入力を受けない
            if (screen == ScreenList) UpdateListRows(false);
            bool follow = pad.FollowingUI();
            group.interactable = group.blocksRaycasts = pad.shell.view == 2 && !pad.shell.GestureActive() && !follow;
        }

        // 毎フレーム(表示中のスワイプ画面だけ). 矢印キーと明滅はシェルの Present に依存させない
        private void Update()
        {
            SyncTick();
            if (!visible || group == null) return;
            UpdateOrientation();
            // 一覧のスクロールに合わせた行の付け替えも、シェルの Present に頼らず毎フレーム見る
            if (screen == ScreenList) UpdateListRows(false);
            if (screen == ScreenSwipe && started) UpdateSwipeInput();
        }

        // スワイプ画面の矢印キー(← 気にならない / → 気になる)と、画像待ちの明滅
        private void UpdateSwipeInput()
        {
            bool modal = ModalOpen();
            if (group.interactable && !modal)
            {
                // ←/→ は画面上のスワイプ方向と同じ(入れ替え設定に従う). ↑ は ☆/★
                if (Input.GetKeyDown(KeyCode.LeftArrow)) OnNavLeft();
                else if (Input.GetKeyDown(KeyCode.RightArrow)) OnNavRight();
                else if (Input.GetKeyDown(KeyCode.UpArrow)) OnStarToggle();
                else if (Input.GetKeyDown(KeyCode.DownArrow)) OnWorldDetail();
            }
            UpdateSwipeDrag(modal);
            if (cardNotice != null && cardNotice.gameObject.activeSelf)
            {
                // 読み込み中の文字をゆっくり明滅させる(失敗の表示は止める)
                bool failed = cursor < feed.Count && images != null && images.IsFailed(feed.Coid[cursor]);
                Color c = cardNotice.color;
                c.a = failed ? 1f : 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
                cardNotice.color = c;
            }
        }

        // ---- 同期の実装 ----

        // 自分が操作したときに呼ぶ(他の人の状態を適用している間は呼ばない)
        private void MarkDirty()
        {
            if (applyingRemote) return;
            syncDirty = true;
        }

        private void SyncTick()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            if (syncDirty && Time.unscaledTime >= nextSend && !Networking.IsClogged)
            {
                syncDirty = false;
                nextSend = Time.unscaledTime + SyncInterval;
                SendSync();
            }
            // 受けた状態は、自分のフィード・画像が揃ってから適用する
            if (pendingRemote && Time.unscaledTime >= retryAt && visible && feed != null && feed.Status == EterPixNudeNyang.EterPixAppFeed.StatusOk) ApplyRemote();
        }

        private void SendSync()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            bool feedOk = feed != null && feed.Status == EterPixNudeNyang.EterPixAppFeed.StatusOk;
            sScreen = screen; sReturn = returnScreen; sBack = overlayBack;
            sStarted = started; sDark = isDark; sZoomMode = zoomMode;
            sCursor = (started && feedOk) ? (cursor >= feed.Count ? "#end" : feed.Poid[cursor]) : "";
            sZoomPoid = (feedOk && zoomMode != 0 && zoomIndex >= 0 && zoomIndex < feed.Count) ? feed.Poid[zoomIndex] : "";
            string l = "";
            if (feedOk)
            {
                for (int i = 0; i < liked.Length && i < feed.Count; i++)
                {
                    if (liked[i]) l += (l.Length > 0 ? "," : "") + feed.Poid[i];
                }
            }
            sLiked = l;
            sRev++;
            RequestSerialization();
            Debug.Log("[EtpTrace] eterpix-app sync send rev=" + sRev + " screen=" + sScreen + " started=" + sStarted
                + " cursor=" + sCursor + " zoom=" + sZoomMode + " liked=" + likedCount);
        }

        public override void OnDeserialization()
        {
            if (sRev == 0) return; // まだ誰も送っていない
            pendingRemote = true;
            Debug.Log("[EtpTrace] eterpix-app sync recv rev=" + sRev + " screen=" + sScreen + " started=" + sStarted
                + " cursor=" + sCursor + " zoom=" + sZoomMode);
        }

        private int IndexOfPoid(string poid)
        {
            if (feed == null || string.IsNullOrEmpty(poid)) return -1;
            for (int i = 0; i < feed.Count; i++)
            {
                if (feed.Poid[i] == poid) return i;
            }
            return -1;
        }

        // 他のプレイヤーの状態を、自分の画面へ映す
        private void ApplyRemote()
        {
            pendingRemote = false;
            applyingRemote = true;
            if (sDark != isDark) { isDark = sDark; ApplyTheme(); }
            bool needRetry = false;
            if (sStarted)
            {
                started = true;
                if (liked == null || liked.Length != feed.Count) liked = new bool[feed.Count];
                for (int i = 0; i < liked.Length; i++) liked[i] = false;
                likedCount = 0;
                string[] ids = sLiked.Split(',');
                for (int k = 0; k < ids.Length; k++)
                {
                    int li = IndexOfPoid(ids[k]);
                    if (li >= 0 && !liked[li]) { liked[li] = true; likedCount++; }
                }
                if (sCursor == "#end") cursor = feed.Count;
                else
                {
                    int ci = IndexOfPoid(sCursor);
                    if (ci >= 0) cursor = ci; else needRetry = sCursor != "";
                }
                int zi = sZoomMode != 0 ? IndexOfPoid(sZoomPoid) : -1;
                if (zi >= 0)
                {
                    if (images != null && images.GetTexture(feed.Coid[zi]) != null)
                    {
                        zoomIndex = zi; zoomMode = sZoomMode;
                        zoomRoot.SetActive(true);
                        RefreshZoom();
                    }
                    else { images.Want(feed.Coid[zi], 0); needRetry = true; }
                }
                else if (zoomMode != 0) { zoomMode = 0; zoomIndex = -1; zoomRoot.SetActive(false); }
            }
            returnScreen = sReturn; overlayBack = sBack;
            if (screen != sScreen) ShowScreen(sScreen);
            else if (screen == ScreenSwipe && started) ShowCard();
            else if (screen == ScreenList) OpenList();
            applyingRemote = false;
            if (needRetry) { pendingRemote = true; retryAt = Time.unscaledTime + 0.5f; } // 画像・投稿が届くまで、0.5秒おきに取り直す
            Debug.Log("[EtpTrace] eterpix-app sync applied rev=" + sRev + " screen=" + screen + " cursor=" + cursor + " zoom=" + zoomMode + " retry=" + needRetry);
        }

        // 実際の枠の大きさ. タブレットを縦向きにすると変わる(スリープ中は rect が0になるので、ビルド時の大きさで代用)
        private Vector2 PanelFrame()
        {
            if (panel != null) { Vector2 s = panel.rect.size; if (s.x > 1f && s.y > 1f) return s; }
            return panelSize;
        }

        private Vector2 CardFrame()
        {
            if (cardFrame != null) { Vector2 s = cardFrame.rect.size; if (s.x > 1f && s.y > 1f) return s; }
            return cardSize;
        }

        // 縦向きのときは、カードとボタンを横いっぱいに広げる(横向きは左右に17%の余白)
        private Vector2 lastPanelFrame;
        private void UpdateOrientation()
        {
            if (panel == null || cardFrame == null) return;
            Vector2 p = panel.rect.size;
            if (p.x <= 1f || p.y <= 1f || (p - lastPanelFrame).sqrMagnitude < 0.25f) return;
            lastPanelFrame = p;
            bool portrait = p.y > p.x;
            float lo = portrait ? 0.03f : 0.17f, hi = 1f - lo;
            cardFrame.anchorMin = new Vector2(lo, cardFrame.anchorMin.y); cardFrame.anchorMax = new Vector2(hi, cardFrame.anchorMax.y);
            if (buttonRow != null)
            {
                RectTransform br = (RectTransform)buttonRow.transform;
                br.anchorMin = new Vector2(lo, br.anchorMin.y); br.anchorMax = new Vector2(hi, br.anchorMax.y);
            }
            ApplyRowLayout(portrait);
            Debug.Log("[EtpTrace] eterpix-app layout panel=" + p + " card=" + CardFrame() + " portrait=" + portrait + " rowHeight=" + rowHeight);
            if (screen == ScreenSwipe && started) ShowCard();
            RefreshZoom();
            if (screen == ScreenList)
            {
                listContent.sizeDelta = new Vector2(0f, Mathf.Max(likedIndices.Length * rowHeight, listViewport.rect.height));
                UpdateListRows(true);
            }
        }

        // カードのドラッグ量を見る. 方向のボタンを明るくし、閾値を越えたら次へ送る.
        // 離したことは直接わからないので、確定後は ScrollRect を1フレーム無効にしてドラッグを打ち切る
        // (そうしないと、指を離すまで次のカードも続けて送られてしまう)
        private void UpdateSwipeDrag(bool modal)
        {
            if (swipeScroll == null || swipeBody == null) return;
            if (modal || cursor >= feed.Count)
            {
                SetHot(0f, 0f, 0f);
                return;
            }
            Vector2 cf = CardFrame();
            Vector2 p = swipeBody.anchoredPosition;
            // 感度は NudeNyang のタブレット共通設定(swipeSensitivity)に従う
            float sens = SwipeSensitivity();
            // 軸ロック: 最初に大きく動いた軸に固定し、もう一方の軸の動きは捨てる(引き終わるまで)
            if (swipeAxis == 0)
            {
                float mx = Mathf.Abs(p.x) * sens / cf.x;
                float my = Mathf.Abs(p.y) * sens / cf.y;
                if (Mathf.Max(mx, my) >= swipeAxisLock) swipeAxis = mx >= my ? 1 : 2;
            }
            if (swipeAxis == 1 && p.y != 0f) { p.y = 0f; swipeBody.anchoredPosition = p; }
            else if (swipeAxis == 2 && p.x != 0f) { p.x = 0f; swipeBody.anchoredPosition = p; }
            float span = Mathf.Max(0.001f, swipeCommit - swipeHotStart);
            float hotH = swipeAxis == 1 ? Mathf.Clamp01((Mathf.Abs(p.x) * sens / cf.x - swipeHotStart) / span) : 0f;
            float hotV = swipeAxis == 2 ? Mathf.Clamp01((Mathf.Abs(p.y) * sens / cf.y - swipeHotStart) / span) : 0f;
            // 押せない向き(先頭で戻る など)は光らせない. 上=☆/★(下=ワールド詳細は対応するボタンが無い)
            SetHot(p.x < 0f && SlotEnabled(false) ? hotH : 0f, p.x > 0f && SlotEnabled(true) ? hotH : 0f, p.y > 0f ? hotV : 0f);
            if (p.sqrMagnitude > 0f) swipeDragging = true;
        }

        // ---- NudeNyang 本体のジェスチャと同じ作法 ----
        // 感度: タブレット共通設定(全ナビゲーションのスワイプで共通)
        private float SwipeSensitivity()
        {
            if (pad != null && pad.shell != null) return pad.shell.GestureSensitivity();
            return 1.2f;
        }

        // VR: レーザーが外れて EndDrag が来なくても、トリガーを両手とも離したら確定する(本体の InputUse → ReleaseGesture と同じ)
        public override void InputUse(bool value, VRC.Udon.Common.UdonInputEventArgs args)
        {
            if (args.handType == VRC.Udon.Common.HandType.LEFT) swipeLeftUse = value; else swipeRightUse = value;
            if (!swipeLeftUse && !swipeRightUse && swipeDragging) SendCustomEventDelayedFrames(nameof(OnSwipeReleaseFromTrigger), 1);
        }

        public void OnSwipeReleaseFromTrigger()
        {
            if (!swipeLeftUse && !swipeRightUse && swipeDragging) OnSwipeRelease();
        }

        // 無効になる・向きが変わるなどで、引いている途中を取り消す(本体の CancelGesture と同じ)
        private void CancelSwipe()
        {
            swipeDragging = false;
            swipeAxis = 0;
            if (swipeScroll != null) swipeScroll.StopMovement();
            if (swipeBody != null) swipeBody.anchoredPosition = Vector2.zero;
            SetHot(0f, 0f, 0f);
        }

        private void OnDisable() { CancelSwipe(); }

        // 指(レーザー)を離した(EventTrigger の EndDrag から呼ばれる). 閾値を越えていれば確定、そうでなければ戻るだけ
        public void OnSwipeRelease()
        {
            if (swipeBody == null || swipeScroll == null) return;
            Vector2 p = swipeBody.anchoredPosition;
            Vector2 cf = CardFrame();
            float sens = SwipeSensitivity();
            float fh = Mathf.Abs(p.x) * sens / cf.x;   // 横: カード幅に対する割合
            float fv = Mathf.Abs(p.y) * sens / cf.y;   // 縦: カード高さに対する割合
            bool horizontal = swipeAxis == 1 || (swipeAxis == 0 && fh >= fv);
            bool onCard = screen == ScreenSwipe && started && cursor < feed.Count && !ModalOpen();
            bool commitH = onCard && horizontal && fh >= swipeCommit;
            bool commitV = onCard && !horizontal && fv >= swipeCommit;
            Debug.Log("[EtpTrace] eterpix-app swipe release dx=" + p.x.ToString("F1") + " dy=" + p.y.ToString("F1") + " sens=" + sens.ToString("F2")
                + " axis=" + swipeAxis + " commitH=" + commitH + " commitV=" + commitV + " cursor=" + cursor);
            // 拡大を優先: どちらの引き量も 4% 未満なら、ドラッグではなくタップとして拡大する
            bool tapLike = onCard && !commitH && !commitV && fh < swipeHotStart && fv < swipeHotStart;
            CancelSwipe();
            // スワイプが確定したときだけ、離した拍の写真タップ(拡大)を少し抑える
            if (commitH || commitV) suppressTapUntil = Time.unscaledTime + 0.25f;
            if (commitH) { if (p.x > 0f) OnNavRight(); else OnNavLeft(); }
            else if (commitV) { if (p.y > 0f) OnStarToggle(); else OnWorldDetail(); }
            else if (tapLike) RegisterPhotoTap();
        }

        private void SetHot(float left, float right, float star)
        {
            if (leftHot != null) { Color c = leftHot.color; c.a = 0.3f * left; leftHot.color = c; }
            if (rightHot != null) { Color c = rightHot.color; c.a = 0.3f * right; rightHot.color = c; }
            if (starHot != null) { Color c = starHot.color; c.a = 0.3f * star; starHot.color = c; }
        }

        // 下スワイプ・↓キー: ワールドの詳細(モーダルはステップ5). ワールド情報が無い投稿は開かず、一瞬だけ知らせる
        public void OnWorldDetail()
        {
            if (!CardLive() || cursor >= feed.Count) return;
            if (string.IsNullOrEmpty(feed.WorldId[cursor]))
            {
                ShowNotice("ワールド情報なし");
                return;
            }
            OpenWorldModal(cursor);
        }

        // ---- ワールドの詳細モーダル ----
        public GameObject worldModal;            // 全面の暗幕(押すと閉じる. 縦に引ける)
        public RectTransform worldModalBody;     // 暗幕の ScrollRect の content(中のカード)
        public TMP_Text worldNameLabel;
        public TMP_Text worldAuthorLabel;
        public TMP_Text worldDescLabel;
        public RectTransform worldDescContent;   // 説明のスクロールの content
        public CanvasGroup worldPortalGroup;

        private bool WorldModalOpen() { return worldModal != null && worldModal.activeSelf; }

        private void OpenWorldModal(int post)
        {
            if (worldModal == null) return;
            worldNameLabel.text = feed.WorldLabel[post];
            string author = feed.WorldAuthor[post];
            worldAuthorLabel.text = string.IsNullOrEmpty(author) ? "" : "作者: " + author;
            worldAuthorLabel.gameObject.SetActive(!string.IsNullOrEmpty(author));
            worldDescLabel.text = feed.WorldDescription[post];
            worldDescContent.anchoredPosition = Vector2.zero;
            worldModalBody.anchoredPosition = Vector2.zero;
            SetNav(worldPortalGroup, portalEnabled && portal != null);
            worldModal.SetActive(true);
            Debug.Log("[EtpTrace] eterpix-app world modal open cursor=" + post);
            MarkDirty();
        }

        public void OnWorldClose()
        {
            if (!WorldModalOpen()) return;
            worldModal.SetActive(false);
            worldModalBody.anchoredPosition = Vector2.zero;
            Debug.Log("[EtpTrace] eterpix-app world modal close");
            MarkDirty();
        }

        // 「ポータルを開く」: モーダルを閉じて、いまのカードのワールドへのポータルを置く流れへ
        public void OnWorldPortal()
        {
            if (!WorldModalOpen()) return;
            OnWorldClose();
            OnSwipePortal();
        }

        // 暗幕を引いて離した: 上へ十分引いていたら閉じる(そうでなければ戻るだけ)
        public void OnWorldModalRelease()
        {
            if (!WorldModalOpen()) return;
            float h = Mathf.Max(1f, worldModalBody.rect.height);
            bool up = worldModalBody.anchoredPosition.y * SwipeSensitivity() / h >= swipeCommit;
            if (up) OnWorldClose(); else worldModalBody.anchoredPosition = Vector2.zero;
        }

        public void HideNoWorldNotice()
        {
            if (noWorldNotice != null && Time.unscaledTime >= noWorldUntil) noWorldNotice.SetActive(false);
        }

        // EterPixAppFeed から呼ばれる(取得完了・失敗)
        public void OnFeedChanged()
        {
            Debug.Log("[EtpTrace] eterpix-app feed status=" + feed.Status + " count=" + feed.Count);
            if (reloading) { FinishReload(); return; }
            if (feed.Status == EterPixNudeNyang.EterPixAppFeed.StatusOk) PlanStartup();
            RefreshStart();
        }

        // EterPixAppImages から呼ばれる(画像が届いた・あきらめた)
        public void OnImageLoaded()
        {
            if (started)
            {
                // 見ているカードの画像が届いたら描き直す
                if (screen == ScreenSwipe || returnScreen == ScreenSwipe) ShowCard();
                RefreshZoom();
                if (screen == ScreenList) UpdateListRows(true);
                return;
            }
            RefreshStart();
        }

        // ---- スワイプ ----

        // 現在のカードを表示する(画像が未着なら読み込み中の表示)。すべて見終わったら終了画面
        private void ShowCard()
        {
            if (feed == null || feed.Status != EterPixNudeNyang.EterPixAppFeed.StatusOk || photo == null) return;
            int count = feed.Count;
            bool finished = cursor >= count;
            cardFrame.gameObject.SetActive(!finished);
            buttonRow.SetActive(true); // 終了画面でも「戻る」「リロード」を使う
            endScreen.SetActive(finished);
            if (finished)
            {
                counterCurrent.text = count.ToString(); counterTotal.text = count.ToString();
                endSummary.text = "すべての投稿を見ました\n気になる: " + likedCount + " 件";
                RefreshNav();
                return;
            }
            RefreshNav();
            counterCurrent.text = (cursor + 1).ToString(); counterTotal.text = count.ToString();
            userLabel.text = feed.UserLabel[cursor];
            worldLabel.text = feed.WorldLabel[cursor];
            SetDescription(swipeDes, swipeBandRect, DescText(cursor), 76f, 130f);
            int coid = feed.Coid[cursor];
            Texture2D tex = images != null ? images.GetTexture(coid) : null;
            if (tex != null)
            {
                // スワイプ画面: 縦写真は縦のまま(正立)枠に収める(左右に余白). 倒さない. 拡大画面は従来どおり長辺合わせ
                int turns = ((feed.Coro[cursor] % 4) + 4) % 4;
                Vector2 frame = CardFrame();
                photo.texture = tex;
                photo.uvRect = geometry.CellUv(feed.Copo[cursor]);
                photoRect.sizeDelta = geometry.FitSize(frame.x, frame.y, turns);
                photoRect.localRotation = Quaternion.Euler(0f, 0f, turns * -90f);
                photo.gameObject.SetActive(true);
                cardNotice.gameObject.SetActive(false);
            }
            else
            {
                photo.gameObject.SetActive(false);
                cardNotice.gameObject.SetActive(true);
                cardNotice.text = images != null && images.IsFailed(coid) ? "読み込みに失敗しました" : "読み込み中…";
            }
            WantWindow();
            Debug.Log("[EtpTrace] eterpix-app card cursor=" + cursor + "/" + count + " coid=" + coid + " copo=" + feed.Copo[cursor]
                + " coro=" + feed.Coro[cursor] + " loaded=" + (tex != null) + " liked=" + likedCount);
        }

        // ---- 気になる一覧 ----

        public void OnHeart() { if (reloading) return; ToggleOverlay(ScreenList); }

        // 一覧を開くたびに、気になる印の付いた投稿を feed の順で集め直す
        private void OpenList()
        {
            int total = likedCount;
            likedIndices = new int[total];
            int n = 0;
            for (int i = 0; i < liked.Length && n < total; i++)
            {
                if (liked[i]) { likedIndices[n] = i; n++; }
            }
            listContent.sizeDelta = new Vector2(0f, Mathf.Max(total * rowHeight, listViewport.rect.height));
            listContent.anchoredPosition = Vector2.zero;
            listEmpty.gameObject.SetActive(total == 0);
            if (listHeading != null) listHeading.text = "気になる投稿 " + total + " 件";
            listFirst = -1;
            UpdateListRows(true);
            Debug.Log("[EtpTrace] eterpix-app list open liked=" + total + " viewport=" + listViewport.rect.size
                + " content=" + listContent.sizeDelta + " rows=" + RowPool + " portal=" + portalEnabled);
        }

        // 投稿の説明(pode). 改行は空白に直して(モックの2行省略と同じ)、前後の空白を取る. 無ければ空
        private string DescText(int post)
        {
            if (feed == null || feed.Pode == null || post < 0 || post >= feed.Pode.Length) return "";
            string s = feed.Pode[post];
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s;
        }

        // 説明の欄を出し入れし、情報帯の高さを合わせる(説明が無い投稿は低いまま)
        private void SetDescription(TMP_Text label, RectTransform band, string text, float lowH, float highH)
        {
            if (label == null) return;
            bool has = text.Length > 0;
            label.text = text;
            label.gameObject.SetActive(has);
            if (band != null) band.offsetMax = new Vector2(band.offsetMax.x, has ? highH : lowH);
        }

        // 一覧の行はモックの .li と同じ配置(Builder がアンカーで作る). 向きで変わるのは右の列の幅だけで、
        // ボタンは縦向きでも1行に収まる(モックの縦向きと同じ). ここでは行の高さを決めるだけ
        private void ApplyRowLayout(bool portrait)
        {
            rowHeight = RowHeight;
        }

        // スクロール位置から、先頭の行を決めて行を付け替える
        private void UpdateListRows(bool force)
        {
            if (listContent == null || rowRoots == null) return;
            int total = likedIndices.Length;
            int maxFirst = Mathf.Max(0, total - RowPool);
            int first = Mathf.Clamp(Mathf.FloorToInt(listContent.anchoredPosition.y / rowHeight), 0, maxFirst);
            if (!force && first == listFirst) return;
            listFirst = first;
            if (images != null) images.BeginWindow();
            for (int k = 0; k < RowPool; k++)
            {
                int item = first + k;
                bool active = item < total;
                rowRoots[k].SetActive(active);
                if (!active) continue;
                int post = likedIndices[item];
                RectTransform rt = (RectTransform)rowRoots[k].transform;
                rt.anchoredPosition = new Vector2(0f, -item * rowHeight);
                rowNames[k].text = feed.UserLabel[post];
                bool hasWorld = !string.IsNullOrEmpty(feed.WorldId[post]);
                rowWorlds[k].text = hasWorld ? feed.WorldLabel[post] : "";
                if (rowDes != null && rowDes.Length > k) rowDes[k].text = DescText(post);
                rowNoWorld[k].gameObject.SetActive(false);
                // モックはワールドが無いとその行を出さないので、説明を1行ぶん上へ詰める
                RectTransform des = (RectTransform)rowDes[k].gameObject.transform;
                float dy = hasWorld ? 63f : 34f;
                des.offsetMin = new Vector2(211f, -(dy + 48f)); des.offsetMax = new Vector2(0f, -dy);
                rowPortalButtons[k].SetActive(hasWorld && portalEnabled);
                if (rowPortalLabels != null && rowPortalLabels.Length > k) rowPortalLabels[k].text = PortalLabel(post);
                SetCopyText(rowWebFields[k], "https://eterpix.com/web/post/" + feed.Poid[post]);
                SetCopyText(rowHqFields[k], "https://eterpix.com/api/vrc/v1/pad/" + feed.Poid[post]);
                int coid = feed.Coid[post];
                if (images != null) images.Want(coid, k);
                BindThumb(k, post);
            }
            if (images != null) images.EndWindow();
        }

        private void BindThumb(int k, int post)
        {
            Texture2D tex = images != null ? images.GetTexture(feed.Coid[post]) : null;
            rowThumbs[k].gameObject.SetActive(tex != null);
            if (tex == null) return;
            rowThumbs[k].texture = tex;
            rowThumbs[k].uvRect = geometry.SquareCropUv(feed.Copo[post]);
            // 縦写真は、セル中央の正方形を coro 回して正立させる
            int turns = ((feed.Coro[post] % 4) + 4) % 4;
            ((RectTransform)rowThumbs[k].transform).localRotation = Quaternion.Euler(0f, 0f, turns * -90f);
        }

        // コピー用の欄(押すとVRChatの入力UIが開く)。編集されても、終了時に元の値へ戻す
        private void SetCopyText(InputField field, string value)
        {
            if (field.text != value) field.text = value;
            field.ForceLabelUpdate();
            field.textComponent.RecalculateMasking();
        }

        // 入力を終えたら(書き換えられていても)値を戻す。ラベルが空のまま残ることがあるので2フレーム後にも直す
        public void OnCopyEnd()
        {
            if (infoUrlField != null) SetCopyText(infoUrlField, InfoUrl); // 情報ページのURL欄も元の値へ戻す
            RebindList();
            SendCustomEventDelayedFrames(nameof(RebindList), 2);
        }

        public void RebindList()
        {
            if (screen == ScreenList) UpdateListRows(true);
        }

        public void OnRowPhoto0() { RowPhoto(0); }
        public void OnRowPhoto1() { RowPhoto(1); }
        public void OnRowPhoto2() { RowPhoto(2); }
        public void OnRowPhoto3() { RowPhoto(3); }
        public void OnRowPhoto4() { RowPhoto(4); }
        public void OnRowPhoto5() { RowPhoto(5); }
        public void OnRowPhoto6() { RowPhoto(6); }
        public void OnRowPhoto7() { RowPhoto(7); }

        private void RowPhoto(int k)
        {
            int item = listFirst + k;
            if (screen != ScreenList || item < 0 || item >= likedIndices.Length) return;
            if (!IsDoubleTap(k)) return;
            OpenZoom(likedIndices[item]);
        }

        // ---- ポータル(方式A: 行のボタンを押す → 床を指して置く) ----
        // Builder がシーン内の既存ポータルを見つけたときだけ true にする
        public bool portalEnabled;
        // NudeNyangTravelPortal(パッチP10済み). パッチ前でもこのファイルがコンパイルできるよう、型は持たず
        // SetProgramVariable / SendCustomEvent で呼ぶ(コンパイルが通らないとパッチを当てるメニュー自体が出ないため)
        public UdonSharpBehaviour portal;
        public TMP_Text[] rowPortalLabels;
        private int portalPost = -1;    // いま置こうとしている投稿(feed の添字)
        private int portalRequest = -1; // 押された投稿(Begin の通知で portalPost に移す)
        private string portalState = "";

        public void OnRowPortal0() { RowPortal(0); }
        public void OnRowPortal1() { RowPortal(1); }
        public void OnRowPortal2() { RowPortal(2); }
        public void OnRowPortal3() { RowPortal(3); }
        public void OnRowPortal4() { RowPortal(4); }
        public void OnRowPortal5() { RowPortal(5); }
        public void OnRowPortal6() { RowPortal(6); }
        public void OnRowPortal7() { RowPortal(7); }

        private void RowPortal(int k)
        {
            int item = listFirst + k;
            if (!portalEnabled || portal == null || screen != ScreenList || item < 0 || item >= likedIndices.Length) return;
            PortalToggle(likedIndices[item]);
        }

        private void PortalToggle(int post)
        {
            // 置いている最中にもう一度押すと取り消す
            if (portalPost == post && portalState == "placing") { CancelPortal(); return; }
            if (!PlacementAllowed() || !pad.TryUI()) return;
            portalRequest = post;
            portal.SetProgramVariable("eterApp", this);
            portal.SetProgramVariable("eterWorld", feed.WorldId[post]);
            portal.SendCustomEvent("EterPixBegin");
            if (!(bool)portal.GetProgramVariable("eterResult"))
            {
                portalPost = post;
                PlacementNotice("failed");
            }
        }

        // ポータル側(NudeNyangTravelPortal)からの通知: placing / opened / cancelled
        public void PlacementNotice(string value)
        {
            Debug.Log("[EtpTrace] eterpix-app placement notice=" + value);
            if (value == "placing") portalPost = portalRequest;
            portalState = value;
            if (value == "cancelled") { ClearPortalState(); return; }
            if (value == "opened" || value == "failed") SendCustomEventDelayedSeconds(nameof(ClearPortalState), 3f);
            RebindList();
            RefreshNav();
        }

        public void ClearPortalState()
        {
            portalPost = -1;
            portalState = "";
            RebindList();
            RefreshNav();
        }

        private string PortalLabel(int post)
        {
            if (post != portalPost) return "ポータルを開く";
            if (portalState == "placing") return "ポータルを置く";
            if (portalState == "opened") return "ポータル開いた";
            if (portalState == "failed") return "置けません";
            return "ポータルを開く";
        }

        // 置きかけのポータルが残らないよう、一覧を離れる・アプリを閉じるときに取り消す
        private void CancelPortal()
        {
            if (portal == null || !portalEnabled) return;
            portal.SetProgramVariable("eterApp", this);
            portal.SendCustomEvent("EterPixCancel");
        }

        // ---- 拡大 ----

        public void OnPhoto() { RegisterPhotoTap(); }

        // 拡大はダブルタップ(0.35 秒以内の同じ写真への 2 回)のときだけ. 1 回押しでは開かない.
        // 同じフレームに来る「離し」と「クリック」は 1 回として数える. ダブルタップは直前のスワイプ抑止を無視して拡大する
        private int lastTapKey = -1, lastTapFrame = -1;
        private bool IsDoubleTap(int key)
        {
            if (Time.frameCount == lastTapFrame) return false;
            lastTapFrame = Time.frameCount;
            float now = Time.unscaledTime;
            bool d = key == lastTapKey && now - lastPhotoTap <= 0.35f;
            lastPhotoTap = now;
            lastTapKey = d ? -1 : key;
            return d;
        }

        private void RegisterPhotoTap()
        {
            if (!started || screen != ScreenSwipe || feed == null || cursor >= feed.Count || reloading) return;
            if (!IsDoubleTap(-2)) return;
            if (zoomMode != 0) return;
            OpenZoom(cursor);
        }

        // 画像が届いていない投稿は開かない
        public void OpenZoom(int index)
        {
            if (images == null || index < 0 || index >= feed.Count) return;
            if (images.GetTexture(feed.Coid[index]) == null) return;
            zoomIndex = index;
            zoomMode = 1;
            zoomRoot.SetActive(true);
            RefreshZoom();
            MarkDirty();
        }

        public void OnZoomTap()
        {
            if (zoomMode == 1)
            {
                zoomMode = 2;
                zoomBand.SetActive(false);
            }
            else
            {
                zoomMode = 0;
                zoomIndex = -1;
                zoomRoot.SetActive(false);
            }
            Debug.Log("[EtpTrace] eterpix-app zoom mode=" + zoomMode);
            MarkDirty();
        }

        private void RefreshZoom()
        {
            if (zoomMode == 0 || zoomIndex < 0) return;
            Texture2D tex = images.GetTexture(feed.Coid[zoomIndex]);
            if (tex == null) { zoomMode = 0; zoomRoot.SetActive(false); return; }
            Vector2 pf = PanelFrame();
            int turns = geometry.DisplayTurns(feed.Coro[zoomIndex], pf.x, pf.y);
            zoomPhoto.texture = tex;
            zoomPhoto.uvRect = geometry.CellUv(feed.Copo[zoomIndex]);
            zoomPhotoRect.sizeDelta = geometry.FitSize(pf.x, pf.y, turns);
            zoomPhotoRect.localRotation = Quaternion.Euler(0f, 0f, turns * -90f);
            zoomUser.text = feed.UserLabel[zoomIndex];
            zoomWorld.text = feed.WorldLabel[zoomIndex];
            SetDescription(zoomDes, (RectTransform)zoomBand.transform, DescText(zoomIndex), 84f, 142f);
            zoomBand.SetActive(zoomMode == 1);
            Debug.Log("[EtpTrace] eterpix-app zoom index=" + zoomIndex + " mode=" + zoomMode + " turns=" + turns);
        }

        // 現在のカードから lookAhead 件ぶんのコラージュを、近い順に取る
        private void WantWindow()
        {
            if (images == null) return;
            int last = Mathf.Min(feed.Count, cursor + lookAhead + 1);
            images.BeginWindow();
            for (int i = cursor; i < last; i++) images.Want(feed.Coid[i], i - cursor);
            images.EndWindow();
        }

        // ---- ボタン列 [戻る][リロード][ポータル][☆/★][進む] ----

        private bool ModalOpen() { return zoomMode != 0 || (confirmModal != null && confirmModal.activeSelf) || WorldModalOpen() || reloading; }

        private bool CardLive() { return started && screen == ScreenSwipe && feed != null && !ModalOpen(); }

        // 左・右のボタン(入れ替え設定で 戻る/進む が替わる). 画面のスワイプ方向・矢印キーも同じ
        public void OnNavLeft() { if (swapNav) GoForward(); else GoBack(); }
        public void OnNavRight() { if (swapNav) GoBack(); else GoForward(); }

        // 左右どちらの側の動作が、いま使えるか
        private bool SlotEnabled(bool right)
        {
            if (feed == null) return false;
            bool forward = right != swapNav;
            return forward ? cursor < feed.Count : cursor > 0;
        }

        private void GoForward()
        {
            if (!CardLive() || cursor >= feed.Count) return;
            cursor++;
            ShowCard();
            MarkDirty();
        }

        private void GoBack()
        {
            if (!CardLive() || cursor <= 0) return;
            cursor--;
            ShowCard();
            MarkDirty();
        }

        // ☆/★: いまのカードの気になる状態を切り替える(カードは進まない)
        public void OnStarToggle()
        {
            if (!CardLive() || cursor >= feed.Count || cursor >= liked.Length) return;
            if (liked[cursor]) { liked[cursor] = false; likedCount--; }
            else { liked[cursor] = true; likedCount++; }
            RefreshNav();
            MarkDirty();
        }

        // ---- リロード(ボタンのみ) ----
        // feed だけ読み直し、見ていた位置と気になるを poid で引き継ぐ(無くなった投稿は外れる). 画像は持っているぶんをそのまま使う.
        // 読み込み中は全操作を押せなくし、失敗したら元のデータのまま「読み込みに失敗しました」を出す
        private bool reloading;
        private string reloadCurPoid = "";
        private bool reloadAtEnd;
        private string[] reloadLikedPoids = new string[0];

        public void OnReload()
        {
            if (reloading || !started || screen != ScreenSwipe || feed == null || feed.Status != EterPixNudeNyang.EterPixAppFeed.StatusOk) return;
            if (ModalOpen()) return;
            reloading = true;
            reloadAtEnd = cursor >= feed.Count;
            reloadCurPoid = reloadAtEnd ? "" : feed.Poid[cursor];
            string[] lp = new string[likedCount];
            int n = 0;
            for (int i = 0; i < liked.Length && n < lp.Length; i++)
            {
                if (liked[i]) { lp[n] = feed.Poid[i]; n++; }
            }
            reloadLikedPoids = lp;
            counterTotal.text = "…";
            RefreshNav();
            Debug.Log("[EtpTrace] eterpix-app reload start cursor=" + cursor + " liked=" + likedCount);
            feed.Reload();
        }

        private void FinishReload()
        {
            reloading = false;
            if (feed.Status != EterPixNudeNyang.EterPixAppFeed.StatusOk)
            {
                // 失敗(通信エラー・0件): 元の配列は残っているので、そのまま続ける
                feed.Status = EterPixNudeNyang.EterPixAppFeed.StatusOk;
                ShowNotice("読み込みに失敗しました");
                Debug.Log("[EtpTrace] eterpix-app reload failed (keep old data)");
                ShowCard();
                return;
            }
            int count = feed.Count;
            bool[] nl = new bool[count];
            int nc = 0;
            for (int k = 0; k < reloadLikedPoids.Length; k++)
            {
                for (int j = 0; j < count; j++)
                {
                    if (feed.Poid[j] == reloadLikedPoids[k]) { nl[j] = true; nc++; break; }
                }
            }
            liked = nl;
            likedCount = nc;
            int idx = -1;
            if (!reloadAtEnd)
            {
                for (int j = 0; j < count; j++)
                {
                    if (feed.Poid[j] == reloadCurPoid) { idx = j; break; }
                }
            }
            if (reloadAtEnd) cursor = count;
            else if (idx >= 0) cursor = idx;
            else cursor = Mathf.Min(cursor, count - 1); // 無くなった投稿: 同じ番号(末尾を超えるなら最後)へ
            Debug.Log("[EtpTrace] eterpix-app reload done count=" + count + " cursor=" + cursor + " liked=" + likedCount);
            ShowCard();
            MarkDirty();
        }

        // カードの下端に一瞬だけ出す知らせ(「ワールド情報なし」「読み込みに失敗しました」)
        private void ShowNotice(string text)
        {
            if (noWorldNotice == null) return;
            if (noticeLabel != null) noticeLabel.text = text;
            noWorldNotice.SetActive(true);
            noWorldUntil = Time.unscaledTime + 1.2f;
            SendCustomEventDelayedSeconds(nameof(HideNoWorldNotice), 1.3f);
        }

        // いまのカードのワールドへのポータル(一覧と同じ流れ)
        public void OnSwipePortal()
        {
            if (!CardLive() || cursor >= feed.Count || !portalEnabled || portal == null) return;
            if (string.IsNullOrEmpty(feed.WorldId[cursor])) return;
            PortalToggle(cursor);
        }

        private void SetNav(CanvasGroup g, bool on)
        {
            if (g == null) return;
            g.alpha = on ? 1f : 0.35f;
            g.interactable = on;
            g.blocksRaycasts = on;
        }

        // ボタン列の文字・薄さ・☆/★ をいまの状態に合わせる
        private void RefreshNav()
        {
            if (navLeftLabel == null) return;
            bool live = started && feed != null && feed.Status == EterPixNudeNyang.EterPixAppFeed.StatusOk && !reloading;
            bool onCard = live && cursor < feed.Count;
            navLeftLabel.text = swapNav ? "進む" : "戻る";
            navRightLabel.text = swapNav ? "戻る" : "進む";
            SetNav(navLeftGroup, live && SlotEnabled(false));
            SetNav(navRightGroup, live && SlotEnabled(true));
            SetNav(navReloadGroup, !reloading); // 読み込み中は押せない(終了画面では押せる)
            bool hasWorld = onCard && !string.IsNullOrEmpty(feed.WorldId[cursor]);
            SetNav(navPortalGroup, hasWorld && portalEnabled && portal != null);
            SetNav(navStarGroup, onCard);
            bool lk = onCard && cursor < liked.Length && liked[cursor];
            if (starOutline != null) starOutline.SetActive(!lk);
            if (starFilled != null) starFilled.SetActive(lk);
            if (navPortalLabel != null) navPortalLabel.text = onCard && portalPost == cursor && portalState == "placing" ? "×" : "◎";
        }

        // もう一度見る: 確認モーダル → はい で気になる選択を消して先頭へ
        public void OnRestartAsk()
        {
            if (confirmModal != null) confirmModal.SetActive(true);
        }

        public void OnRestartNo()
        {
            if (confirmModal != null) confirmModal.SetActive(false);
        }

        public void OnRestartYes()
        {
            if (confirmModal != null) confirmModal.SetActive(false);
            for (int i = 0; i < liked.Length; i++) liked[i] = false;
            likedCount = 0;
            cursor = 0;
            ShowCard();
            MarkDirty();
        }

        // 先頭から startupPreload 枚ぶんの(重複しない)コラージュを先に取る
        private void PlanStartup()
        {
            if (images == null) return;
            bool[] seen = new bool[256];
            int wanted = 0;
            images.BeginWindow();
            for (int i = 0; i < feed.Count && wanted < startupPreload; i++)
            {
                int coid = feed.Coid[i];
                if (coid < 0 || coid > 255 || seen[coid]) continue;
                seen[coid] = true;
                images.Want(coid, wanted);
                wanted++;
            }
            images.EndWindow();
            liked = new bool[feed.Count];
            likedCount = 0;
            Debug.Log("[EtpTrace] eterpix-app startup preload=" + wanted);
        }

        // 起動画面の表示を、取得の状況に合わせる
        private void RefreshStart()
        {
            if (loadingRow == null) return;
            int status = feed != null ? feed.Status : EterPixNudeNyang.EterPixAppFeed.StatusLoading;
            int needed = 0;
            int settled = 0;
            if (images != null && status == EterPixNudeNyang.EterPixAppFeed.StatusOk)
            {
                needed = images.NeededCount();
                settled = images.NeededSettledCount();
            }
            bool failed = status == EterPixNudeNyang.EterPixAppFeed.StatusUntrusted
                || status == EterPixNudeNyang.EterPixAppFeed.StatusServerError
                || status == EterPixNudeNyang.EterPixAppFeed.StatusEmpty;
            bool done = status == EterPixNudeNyang.EterPixAppFeed.StatusOk && settled >= needed;
            ready = done;

            // 進行度: 投稿データの取得を1段、画像を needed 段として数える
            int total = 1 + needed;
            int current = (status == EterPixNudeNyang.EterPixAppFeed.StatusOk ? 1 : 0) + settled;
            float p = failed ? 0f : (done ? 1f : (float)current / (float)total);
            if (percentLabel != null) percentLabel.text = Mathf.RoundToInt(p * 100f) + "%";
            if (barFill != null) barFill.anchorMax = new Vector2(p, 1f);

            loadingRow.SetActive(!failed && !done);
            if (startButton != null) startButton.SetActive(done);
            if (statusLabel != null)
            {
                statusLabel.gameObject.SetActive(failed);
                if (failed) statusLabel.text = StatusMessage(status);
            }
            Debug.Log("[EtpTrace] eterpix-app start-refresh status=" + status + " settled=" + settled + "/" + needed + " ready=" + ready);
        }

        // 取得に失敗したときの表示(合意書「状態の表示」)
        private string StatusMessage(int status)
        {
            if (status == EterPixNudeNyang.EterPixAppFeed.StatusUntrusted)
                return "「Allow Untrusted URLs」をONにしてください";
            if (status == EterPixNudeNyang.EterPixAppFeed.StatusServerError)
                return "読み込みに失敗しました";
            return "まだ投稿がありません";
        }

        // 画面の切り替え
        public void ShowScreen(int next)
        {
            screen = next;
            if (startScreen != null) startScreen.SetActive(next == ScreenStart);
            if (swipeScreen != null) swipeScreen.SetActive(next == ScreenSwipe);
            if (infoScreen != null) infoScreen.SetActive(next == ScreenInfo);
            if (groupScreen != null) groupScreen.SetActive(next == ScreenGroup);
            // 押すと開いた画面を閉じて元へ戻れるよう、ボタンの表示を×にする
            if (infoButtonLabel != null) infoButtonLabel.text = next == ScreenInfo ? "×" : "i";
            if (groupButtonLabel != null) groupButtonLabel.text = next == ScreenGroup ? "×" : "";
            if (groupIcon != null) groupIcon.SetActive(next != ScreenGroup);
            if (settingsScreen != null) settingsScreen.SetActive(next == ScreenSettings);
            if (next == ScreenSettings) RefreshSettings();
            if (settingsButtonLabel != null) settingsButtonLabel.text = next == ScreenSettings ? "×" : "";
            if (settingsIcon != null) settingsIcon.SetActive(next != ScreenSettings);
            if (counterRoot != null) counterRoot.SetActive(next == ScreenSwipe);
            if (listScreen != null) listScreen.SetActive(next == ScreenList);
            if (heartButton != null) heartButton.SetActive(started);
            if (heartLabel != null) heartLabel.text = next == ScreenList ? "×" : "";
            if (heartIcon != null) heartIcon.SetActive(next != ScreenList);
            if (next != ScreenSwipe && WorldModalOpen()) OnWorldClose();
            if (next != ScreenList) CancelPortal();
            if (next == ScreenList) OpenList();
            if (next == ScreenInfo && infoUrlField != null) SetCopyText(infoUrlField, InfoUrl);
            if (next == ScreenSwipe && started) ShowCard(); // 一覧などで入れ替わった先読みを、現在のカードに合わせて取り直す
            Debug.Log("[EtpTrace] eterpix-app screen=" + next);
            if (fetchStarted) MarkDirty();
        }

        // i・グループは同じボタンで開閉する(開く前の画面へ戻る)
        public void ToggleOverlay(int target)
        {
            // returnScreen = 基準の画面(起動/スワイプ). 一覧は i・グループと重なる画面なので基準にしない
            // (一覧→i→一覧のあと×を押しても、一覧自身へ戻ってスワイプへ戻れなくなる不具合の修正)
            // overlayBack = i・グループを閉じたときの戻り先(基準の画面か一覧)
            if (screen == target)
            {
                if (target == ScreenList) ShowScreen(returnScreen);
                else ShowScreen(overlayBack);
                return;
            }
            if (screen == ScreenStart || screen == ScreenSwipe) returnScreen = screen;
            if ((target == ScreenInfo || target == ScreenGroup || target == ScreenSettings) && !IsTextOverlay(screen)) overlayBack = screen;
            ShowScreen(target);
        }

        // i・グループ・設定は、開く前の画面へ戻る重なる画面
        private bool IsTextOverlay(int sc) { return sc == ScreenInfo || sc == ScreenGroup || sc == ScreenSettings; }

        public void OnSettings() { ToggleOverlay(ScreenSettings); }
        public void OnInfo() { ToggleOverlay(ScreenInfo); }
        public void OnGroup() { ToggleOverlay(ScreenGroup); }

        public void OnStart()
        {
            if (!ready) return;
            started = true;
            cursor = 0;
            ShowScreen(ScreenSwipe);
            ShowCard();
            MarkDirty();
        }

        public void OnTheme() { SetTheme(!isDark); }
        public void OnThemeDark() { SetTheme(true); }
        public void OnThemeLight() { SetTheme(false); }
        public void OnSwapOff() { SetSwap(false); }
        public void OnSwapOn() { SetSwap(true); }

        private void SetTheme(bool dark)
        {
            if (isDark == dark) return;
            isDark = dark;
            ApplyTheme();
            Debug.Log("[EtpTrace] eterpix-app theme dark=" + isDark);
            MarkDirty();
        }

        private void SetSwap(bool swap)
        {
            if (swapNav == swap) return;
            swapNav = swap;
            RefreshSettings();
            RefreshNav();
            Debug.Log("[EtpTrace] eterpix-app swapNav=" + swapNav);
        }

        // 設定画面の分割ボタン: 選択中は accent、そうでなければボタン色
        private void RefreshSettings()
        {
            if (settingImages == null || settingLabels == null || settingImages.Length < 4) return;
            bool[] on = { isDark, !isDark, !swapNav, swapNav };
            Color accent = new Color(0.616f, 0.435f, 1f, 1f);   // #9D6FFF
            Color onText = new Color(0.078f, 0.078f, 0.078f, 1f); // #141414
            Color btnColor = isDark ? darkBtn : lightBtn;
            Color textColor = isDark ? darkText : lightText;
            for (int i = 0; i < 4; i++)
            {
                if (settingImages[i] != null) settingImages[i].color = on[i] ? accent : btnColor;
                Color fg = on[i] ? onText : textColor;
                if (settingLabels[i] != null) settingLabels[i].color = fg;
                if (settingIcons != null && i < settingIcons.Length && settingIcons[i] != null) settingIcons[i].color = fg;
            }
        }

        private void ApplyTheme()
        {
            Paint(bgGraphics, isDark ? darkBg : lightBg);
            Paint(panelGraphics, isDark ? darkPanel : lightPanel);
            Paint(btnGraphics, isDark ? darkBtn : lightBtn);
            Paint(textGraphics, isDark ? darkText : lightText);
            Paint(subGraphics, isDark ? darkSub : lightSub);
            RefreshSettings();
        }

        private void Paint(Graphic[] targets, Color c)
        {
            if (targets == null) return;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targets[i].color = c;
            }
        }

        // ポータルの配置通知先(Task 8 で使う)
        public bool PlacementAllowed()
        {
            return visible && pad != null && !pad.storageSleeping && pad.shell.view == 2
                && pad.shell.app == AppId && !pad.shell.GestureActive();
        }

    }
}
