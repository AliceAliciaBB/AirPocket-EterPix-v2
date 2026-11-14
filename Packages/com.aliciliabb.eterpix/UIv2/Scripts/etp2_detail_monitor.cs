using TMPro;
using UdonSharp;
using UnityEngine;

namespace ali.eterpix.ui2
{
    // 詳細モニター. ビューアで今開いている投稿に合わせて、画像説明と同じワールド/ユーザーの投稿を出す.
    // ビューアを閉じても最後の投稿のまま. 決まり: 統合管理 docs/plans/unity-ui-v2-mock.md 5章
    // prefab のルートに付ける. ワールド作者がつなぐのは「データ」「ビューア」だけ
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_detail_monitor : UdonSharpBehaviour
    {
        [Header("ワールド作者がつなぐ")]
        public etp2_mock_data data;
        public etp2_viewer viewer;

        [Header("ビルダーが入れる")]
        public etp2_theme_target theme;
        public GameObject screen;   // ビューアを開いている間だけ出す

        [Header("画像説明")]
        public TextMeshProUGUI dateText;
        public TextMeshProUGUI userText;
        public TextMeshProUGUI titleText;
        public GameObject titleRoot;
        public TextMeshProUGUI viewsText;
        public TextMeshProUGUI starsText;
        public TextMeshProUGUI likesText;
        public TextMeshProUGUI descText;
        public TextMeshProUGUI worldText;
        public TextMeshProUGUI authorText;
        public TextMeshProUGUI worldDescText;

        [Header("同じワールドの投稿")]
        public TextMeshProUGUI worldHeadText;
        public etp2_post_row[] worldRows;

        [Header("同じユーザーの投稿")]
        public TextMeshProUGUI userHeadText;
        public etp2_post_row[] userRows;

        [HideInInspector] public int pressedIndex;

        private int _post;

        private void Start()
        {
            Render();
            // ふだんは出さない(ワールドに1つ. ビューアを開いている間だけ右隣に出る)
            if (screen != null && !_shown) screen.SetActive(false);
        }

        private bool _shown;

        // ビューアが開いたときに呼ぶ. ルートは有効のまま、Screen だけを出す
        public void ShowBeside(Vector3 position, Quaternion rotation)
        {
            _shown = true;
            transform.SetPositionAndRotation(position, rotation);
            if (screen != null) screen.SetActive(true);
            Debug.Log("[Etp2Trace] detail: show beside viewer");
        }

        public void Hide()
        {
            _shown = false;
            if (screen != null) screen.SetActive(false);
            Debug.Log("[Etp2Trace] detail: hide");
        }

        public void OnViewerChanged()
        {
            Render();
        }

        public void OnThemeChanged()
        {
            PaintRows(worldRows);
            PaintRows(userRows);
        }

        // 一覧の行を押した. 押した投稿をビューアに出す(第2段階で鍵の対象にする)
        public void OnRow()
        {
            Debug.Log("[Etp2Trace] detail: select post " + pressedIndex);
            if (viewer != null) viewer.SelectPost(pressedIndex);
        }

        // 見出しの右の [Pad用URL] [WebページのURL]、ワールドのポータル. 第1段階ではログだけ
        public void OnPadUrl() { Debug.Log("[Etp2Trace] detail: Pad用URL post " + _post); }
        public void OnWebUrl() { Debug.Log("[Etp2Trace] detail: WebページのURL post " + _post); }
        public void OnPortal() { Debug.Log("[Etp2Trace] detail: portal post " + _post); }

        private void Render()
        {
            if (data == null || data.PostCount() == 0) return;
            _post = viewer != null ? viewer.CurrentPost() : 0;
            if (_post < 0 || _post >= data.PostCount()) _post = 0;
            int p = _post;

            dateText.text = data.postDate[p];
            userText.text = data.postUser[p];
            string title = data.postTitle[p];
            titleText.text = title;
            if (titleRoot != null) titleRoot.SetActive(!string.IsNullOrEmpty(title));
            viewsText.text = etp2_mock_data.FormatCount(data.postViews[p]);
            starsText.text = etp2_mock_data.FormatCount(data.postStars[p]);
            likesText.text = etp2_mock_data.FormatCount(data.postLikes[p]);
            descText.text = data.postDesc[p];
            string world = data.postWorld[p];
            worldText.text = world;
            int w = data.FindWorld(world);
            authorText.text = w >= 0 ? data.worldAuthor[w] : "";
            worldDescText.text = w >= 0 ? data.worldDesc[w] : "";

            worldHeadText.text = "ワールド: " + world;
            userHeadText.text = "ユーザー: " + data.postUser[p];
            FillRows(worldRows, true, world);
            FillRows(userRows, false, data.postUser[p]);
        }

        private void FillRows(etp2_post_row[] rows, bool byWorld, string key)
        {
            if (rows == null) return;
            int[] list = viewer != null ? viewer.CurrentList() : null;
            int n = list != null ? list.Length : data.PostCount();
            int used = 0;
            for (int i = 0; i < n && used < rows.Length; i++)
            {
                int q = list != null ? list[i] : i;
                if ((byWorld ? data.postWorld[q] : data.postUser[q]) != key) continue;
                // 「同じユーザーの投稿」はユーザー名の代わりにワールド名を出し、称号は出さない(暫定)
                rows[used].Set(q, byWorld ? data.postUser[q] : data.postWorld[q], byWorld ? data.postTitle[q] : "",
                    data.postDate[q], data.postDesc[q], data.postViews[q], data.postStars[q], data.postLikes[q]);
                used++;
            }
            for (int i = used; i < rows.Length; i++) if (rows[i] != null) rows[i].gameObject.SetActive(false);
            PaintRows(rows);
        }

        private void PaintRows(etp2_post_row[] rows)
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null && rows[i].gameObject.activeSelf) rows[i].Paint(theme, rows[i].post == _post);
        }
    }
}
