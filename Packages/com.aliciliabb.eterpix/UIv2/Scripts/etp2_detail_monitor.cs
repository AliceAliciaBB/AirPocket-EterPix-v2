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
        public UnityEngine.UI.RawImage userIcon;   // 丸の中の投稿者のアイコン(API のとき)
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
        public GameObject worldMore;   // 「上位16件のみ表示しています」

        [Header("同じユーザーの投稿")]
        public TextMeshProUGUI userHeadText;
        public etp2_post_row[] userRows;
        public GameObject userMore;

        [HideInInspector] public int pressedIndex;

        private int _post;

        private void Start()
        {
            if (data != null) data.Listen(this);
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

        public void OnDataChanged()
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
            data.ShowIcon(p, userIcon);
            string title = data.postTitle[p];
            titleText.text = title;
            if (titleRoot != null) titleRoot.SetActive(!string.IsNullOrEmpty(title));
            viewsText.text = etp2_mock_data.FormatCount(data.postViews[p]);
            starsText.text = etp2_mock_data.FormatCount(data.postStars[p]);
            likesText.text = etp2_mock_data.FormatCount(data.postLikes[p]);
            descText.text = data.postDesc[p];
            string world = data.postWorld[p];
            worldText.text = data.WorldLabel(p);
            int w = data.FindWorld(world);
            authorText.text = w >= 0 ? data.worldAuthor[w] : "";
            worldDescText.text = w >= 0 ? data.worldDesc[w] : "";

            worldHeadText.text = "ワールド: " + world;
            userHeadText.text = "ユーザー: " + data.postUser[p];
            FillRows(worldRows, worldMore, true, world);
            FillRows(userRows, userMore, false, data.postUser[p]);
        }

        private void FillRows(etp2_post_row[] rows, GameObject more, bool byWorld, string key)
        {
            if (rows == null) return;
            if (more != null) more.SetActive(false);
            if (string.IsNullOrEmpty(key))
            {
                // ワールドの情報が無い投稿は、同じワールドでまとめない
                for (int i = 0; i < rows.Length; i++) if (rows[i] != null) rows[i].gameObject.SetActive(false);
                return;
            }
            int[] list = viewer != null ? viewer.CurrentList() : null;
            int n = list != null ? list.Length : data.PostCount();
            int used = 0;
            // 並びはビューアの並び(ソート順)のまま. 行より多いときは上から行の数だけ出し、文で知らせる
            int matched = 0;
            for (int i = 0; i < n; i++)
            {
                int q = list != null ? list[i] : i;
                if ((byWorld ? data.postWorld[q] : data.postUser[q]) != key) continue;
                matched++;
                if (used >= rows.Length) continue;
                // 「同じユーザーの投稿」はユーザー名の代わりにワールド名を出し、称号は出さない(暫定)
                rows[used].Set(q, byWorld ? data.postUser[q] : data.postWorld[q], byWorld ? data.postTitle[q] : "",
                    data.postDate[q], data.postDesc[q], data.postViews[q], data.postStars[q], data.postLikes[q]);
                used++;
            }
            for (int i = used; i < rows.Length; i++) if (rows[i] != null) rows[i].gameObject.SetActive(false);
            if (more != null) more.SetActive(matched > rows.Length);
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
