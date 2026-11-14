using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ali.eterpix.ui2
{
    // 詳細モニターの「同じワールドの投稿」「同じユーザーの投稿」の1件(使い回しの行).
    // 1行目: 名前 | 閲覧数・すーぱーいいね・いいね / 2行目: 称号 | 投稿日時 / 3行目から写真説明(最大2行)
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_post_row : UdonSharpBehaviour
    {
        public Button button;
        public etp2_index_button indexButton;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI dateText;
        public TextMeshProUGUI descText;
        public TextMeshProUGUI viewsText;
        public TextMeshProUGUI starsText;
        public TextMeshProUGUI likesText;
        public Image[] statIcons;

        // 写真説明を最大2行で切る. U# では文字の高さを測れないので、見えない測り用の文字(ContentSizeFitter)の高さを
        // 1フレーム後に読み、表示側の箱の高さを min(測った高さ, 2行) にする. あふれは表示側の Ellipsis で「…」になる
        public TextMeshProUGUI descMeasure;
        public RectTransform descMeasureRect;
        public LayoutElement descLayout;
        public float maxDescHeight;

        [HideInInspector] public int post = -1;

        private void OnEnable()
        {
            SendCustomEventDelayedFrames(nameof(_FitDesc), 1);
        }

        public void _FitDesc()
        {
            if (descMeasureRect == null || descLayout == null || !descMeasureRect.gameObject.activeInHierarchy) return;
            float h = descMeasureRect.rect.height;
            descLayout.preferredHeight = Mathf.Min(h, maxDescHeight);
        }

        public void Set(int postIndex, string name, string title, string date, string desc, int views, int stars, int likes)
        {
            post = postIndex;
            if (indexButton != null) indexButton.index = postIndex;
            nameText.text = name;
            titleText.text = title;
            titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
            dateText.text = date;
            descText.text = desc;
            if (descMeasure != null) descMeasure.text = desc;
            viewsText.text = etp2_mock_data.FormatCount(views);
            starsText.text = etp2_mock_data.FormatCount(stars);
            likesText.text = etp2_mock_data.FormatCount(likes);
            gameObject.SetActive(true);
            SendCustomEventDelayedFrames(nameof(_FitDesc), 1);
        }

        public void Paint(etp2_theme_target theme, bool current)
        {
            if (theme == null || !theme.ready) return;
            int style = current ? etp2_theme_target.StyleAccent : etp2_theme_target.StyleNormal;
            theme.PaintButton(button, style);
            Color c = theme.LabelColor(style);
            nameText.color = c; titleText.color = c; dateText.color = c; descText.color = c;
            viewsText.color = c; starsText.color = c; likesText.color = c;
            if (statIcons != null) for (int i = 0; i < statIcons.Length; i++) if (statIcons[i] != null) statIcons[i].color = c;
        }
    }
}
