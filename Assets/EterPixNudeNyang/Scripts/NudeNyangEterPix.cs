using UdonSharp;
using UnityEngine;
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

        public NudeNyangTablet pad;
        public RectTransform panel;
        public CanvasGroup group;
        public TMP_Text title;
        public EterPixNudeNyang.EterPixAppFeed feed;

        private bool visible;
        private bool fetchStarted;

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
