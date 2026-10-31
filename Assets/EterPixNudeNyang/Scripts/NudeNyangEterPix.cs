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

        private bool visible;
        private bool fetchStarted;
        private bool themeApplied;

        // NudeNyangTabletShell から毎フレーム呼ばれる(パッチP6)
        public void Present()
        {
            if (pad == null || pad.shell == null || panel == null) return;
            bool show = pad.shell.AppWindowVisible(AppId) && !pad.storageSleeping;
            if (visible != show)
            {
                visible = show;
                panel.gameObject.SetActive(show);
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
            bool follow = pad.FollowingUI();
            group.interactable = group.blocksRaycasts = pad.shell.view == 2 && !pad.shell.GestureActive() && !follow;
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
            RefreshStart();
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
                return "URLが許可されていません\nVRChatの設定で「信頼されていないURL」を許可してください";
            if (status == EterPixNudeNyang.EterPixAppFeed.StatusServerError)
                return "サーバーに接続できません\nしばらくしてからもう一度お試しください";
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
            ShowScreen(ScreenSwipe);
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

        public void PlacementNotice(string value)
        {
            Debug.Log("[EtpTrace] eterpix-app placement notice=" + value);
        }
    }
}
