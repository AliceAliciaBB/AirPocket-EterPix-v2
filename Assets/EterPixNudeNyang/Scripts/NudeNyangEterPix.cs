using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NudeNyang
{
    // NudeNyangタブレットの12番目のアプリ「EterPix-app」.
    // 計画: docs/superpowers/plans/2026-10-31-eterpix-app.md
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NudeNyangEterPix : UdonSharpBehaviour
    {
        // NudeNyangTabletShell.AppCount の12番目. パッチ(P1〜P9)で追加される
        public const int AppId = 12;

        // 画面
        public const int ScreenStart = 0;
        public const int ScreenSwipe = 1;
        public const int ScreenList = 2;
        public const int ScreenInfo = 3;
        public const int ScreenGroup = 4;

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

        // ヘッダー
        public TMP_Text infoButtonLabel;
        public TMP_Text groupButtonLabel;
        public TMP_Text counterLabel;

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
        public TMP_Text cardNotice;
        public GameObject buttonRow;
        public GameObject endScreen;
        public TMP_Text endSummary;
        public GameObject confirmModal;
        // 現在のカードから先に取っておく投稿数
        public int lookAhead = 6;

        // 気になる投稿一覧(スクロールする行を使い回す. 見えている分だけ実体を持つ)
        public const int RowPool = 8;
        public const float RowHeight = 140f;
        public GameObject heartButton;
        public TMP_Text heartLabel;
        public GameObject listScreen;
        public RectTransform listContent;
        public RectTransform listViewport;
        public TMP_Text listEmpty;
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
            if (!visible || group == null || screen != ScreenSwipe || !started) return;
            UpdateSwipeInput();
        }

        // スワイプ画面の矢印キー(← 気にならない / → 気になる)と、画像待ちの明滅
        private void UpdateSwipeInput()
        {
            bool modal = zoomMode != 0 || (confirmModal != null && confirmModal.activeSelf);
            if (group.interactable && !modal)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) Advance(false);
                else if (Input.GetKeyDown(KeyCode.RightArrow)) Advance(true);
            }
            if (cardNotice != null && cardNotice.gameObject.activeSelf)
            {
                // 読み込み中の文字をゆっくり明滅させる(失敗の表示は止める)
                bool failed = cursor < feed.Count && images != null && images.IsFailed(feed.Coid[cursor]);
                Color c = cardNotice.color;
                c.a = failed ? 1f : 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
                cardNotice.color = c;
            }
        }

        // EterPixAppFeed から呼ばれる(取得完了・失敗)
        public void OnFeedChanged()
        {
            Debug.Log("[EtpTrace] eterpix-app feed status=" + feed.Status + " count=" + feed.Count);
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
            buttonRow.SetActive(!finished);
            endScreen.SetActive(finished);
            if (finished)
            {
                counterLabel.text = count + " / " + count;
                endSummary.text = "すべての投稿を見ました\n気になる: " + likedCount + " 件";
                return;
            }
            counterLabel.text = (cursor + 1) + " / " + count;
            userLabel.text = feed.UserLabel[cursor];
            worldLabel.text = feed.WorldLabel[cursor];
            int coid = feed.Coid[cursor];
            Texture2D tex = images != null ? images.GetTexture(coid) : null;
            if (tex != null)
            {
                // 写真の長辺を枠の長辺に合わせる(Pad の K-4. 縦写真は枠に合わせて反時計回りに倒す)
                int turns = geometry.DisplayTurns(feed.Coro[cursor], cardSize.x, cardSize.y);
                photo.texture = tex;
                photo.uvRect = geometry.CellUv(feed.Copo[cursor]);
                photoRect.sizeDelta = geometry.FitSize(cardSize.x, cardSize.y, turns);
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

        public void OnHeart() { ToggleOverlay(ScreenList); }

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
            listContent.sizeDelta = new Vector2(0f, Mathf.Max(total * RowHeight, listViewport.rect.height));
            listContent.anchoredPosition = Vector2.zero;
            listEmpty.gameObject.SetActive(total == 0);
            listFirst = -1;
            UpdateListRows(true);
            Debug.Log("[EtpTrace] eterpix-app list open liked=" + total);
        }

        // スクロール位置から、先頭の行を決めて行を付け替える
        private void UpdateListRows(bool force)
        {
            if (listContent == null || rowRoots == null) return;
            int total = likedIndices.Length;
            int maxFirst = Mathf.Max(0, total - RowPool);
            int first = Mathf.Clamp(Mathf.FloorToInt(listContent.anchoredPosition.y / RowHeight), 0, maxFirst);
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
                rt.anchoredPosition = new Vector2(0f, -item * RowHeight);
                rowNames[k].text = feed.UserLabel[post];
                bool hasWorld = !string.IsNullOrEmpty(feed.WorldId[post]);
                rowWorlds[k].text = hasWorld ? feed.WorldLabel[post] : "";
                rowNoWorld[k].gameObject.SetActive(!hasWorld);
                rowPortalButtons[k].SetActive(hasWorld && portalEnabled);
                if (rowPortalLabels != null && rowPortalLabels.Length > k) rowPortalLabels[k].text = PortalLabel(post);
                SetCopyText(rowWebFields[k], "https://eterpix.com/web/post/" + feed.Poid[post]);
                SetCopyText(rowHqFields[k], "https://eterpix.com/api/images/" + feed.Poid[post]);
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
            int post = likedIndices[item];
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
        }

        public void ClearPortalState()
        {
            portalPost = -1;
            portalState = "";
            RebindList();
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

        public void OnPhoto()
        {
            if (!started || screen != ScreenSwipe || feed == null || cursor >= feed.Count) return;
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
        }

        private void RefreshZoom()
        {
            if (zoomMode == 0 || zoomIndex < 0) return;
            Texture2D tex = images.GetTexture(feed.Coid[zoomIndex]);
            if (tex == null) { zoomMode = 0; zoomRoot.SetActive(false); return; }
            int turns = geometry.DisplayTurns(feed.Coro[zoomIndex], panelSize.x, panelSize.y);
            zoomPhoto.texture = tex;
            zoomPhoto.uvRect = geometry.CellUv(feed.Copo[zoomIndex]);
            zoomPhotoRect.sizeDelta = geometry.FitSize(panelSize.x, panelSize.y, turns);
            zoomPhotoRect.localRotation = Quaternion.Euler(0f, 0f, turns * -90f);
            zoomUser.text = feed.UserLabel[zoomIndex];
            zoomWorld.text = feed.WorldLabel[zoomIndex];
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

        // 左(気にならない)・右(気になる)
        public void OnPass() { Advance(false); }
        public void OnLike() { Advance(true); }

        private void Advance(bool like)
        {
            if (!started || screen != ScreenSwipe || feed == null || cursor >= feed.Count) return;
            if (like && !liked[cursor]) { liked[cursor] = true; likedCount++; }
            cursor++;
            ShowCard();
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
            if (groupButtonLabel != null) groupButtonLabel.text = next == ScreenGroup ? "×" : "G";
            if (counterLabel != null) counterLabel.gameObject.SetActive(next == ScreenSwipe);
            if (listScreen != null) listScreen.SetActive(next == ScreenList);
            if (heartButton != null) heartButton.SetActive(started);
            if (heartLabel != null) heartLabel.text = next == ScreenList ? "×" : "☆";
            if (next != ScreenList) CancelPortal();
            if (next == ScreenList) OpenList();
            if (next == ScreenSwipe && started) ShowCard(); // 一覧などで入れ替わった先読みを、現在のカードに合わせて取り直す
            Debug.Log("[EtpTrace] eterpix-app screen=" + next);
        }

        // i・グループは同じボタンで開閉する(開く前の画面へ戻る)
        public void ToggleOverlay(int target)
        {
            if (screen == target)
            {
                ShowScreen(returnScreen);
                return;
            }
            if (screen != ScreenInfo && screen != ScreenGroup) returnScreen = screen;
            ShowScreen(target);
        }

        public void OnInfo() { ToggleOverlay(ScreenInfo); }
        public void OnGroup() { ToggleOverlay(ScreenGroup); }

        public void OnStart()
        {
            if (!ready) return;
            started = true;
            cursor = 0;
            ShowScreen(ScreenSwipe);
            ShowCard();
        }

        public void OnTheme()
        {
            isDark = !isDark;
            ApplyTheme();
            Debug.Log("[EtpTrace] eterpix-app theme dark=" + isDark);
        }

        private void ApplyTheme()
        {
            Paint(bgGraphics, isDark ? darkBg : lightBg);
            Paint(panelGraphics, isDark ? darkPanel : lightPanel);
            Paint(textGraphics, isDark ? darkText : lightText);
            Paint(subGraphics, isDark ? darkSub : lightSub);
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
