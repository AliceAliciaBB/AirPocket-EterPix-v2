using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ali.eterpix.ui2
{
    // メニューモニター・詳細モニターの左のナビとページの切り替え(モニターごと. ローカル).
    // 決まり: 統合管理 docs/plans/unity-ui-v2-mock.md 3章
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_nav : UdonSharpBehaviour
    {
        public const float CollapsedWidth = 40f;
        public const float ExpandedWidth = 200f;

        public string monitorName;
        public etp2_theme_target theme;
        public etp2_tooltip tooltip;

        [Header("ナビボタン(上から)")]
        public Button[] navButtons;
        public RectTransform[] navRects;
        public Image[] navIcons;
        public TextMeshProUGUI[] navLabels;
        public etp2_hover[] navHovers;
        public GameObject[] pages;

        [Header("展開ボタン")]
        public Button toggleButton;
        public RectTransform toggleRect;
        public Image toggleIcon;
        public TextMeshProUGUI toggleLabel;
        public etp2_hover toggleHover;
        public Sprite expandSprite;
        public Sprite collapseSprite;

        [Header("広げたとき")]
        public GameObject band;   // 幅 216 の帯(背景色)
        public GameObject dim;    // ページ側の暗幕. 押すと畳む

        [Header("×で隠す中身")]
        public GameObject body;

        public int initialPage;
        [Tooltip("ページが変わったら OnPageChanged を送る(無くてよい)")]
        public UdonSharpBehaviour pageListener;

        [HideInInspector] public int pressedIndex;

        private int _current = -1;
        private bool _expanded;

        private void Start()
        {
            SetExpanded(false);
            ShowPage(initialPage);
        }

        public int CurrentPage() { return _current; }

        public void OnNav()
        {
            ShowPage(pressedIndex);
        }

        public void ShowPage(int index)
        {
            if (pages == null || index < 0 || index >= pages.Length) return;
            bool changed = _current != index;
            _current = index;
            for (int i = 0; i < pages.Length; i++) if (pages[i] != null) pages[i].SetActive(i == index);
            Paint();
            if (changed)
            {
                Debug.Log("[Etp2Trace] " + monitorName + ": page " + index + (navLabels != null && index < navLabels.Length && navLabels[index] != null ? " " + navLabels[index].text : ""));
                if (pageListener != null) pageListener.SendCustomEvent("OnPageChanged");
            }
        }

        public void OnToggle()
        {
            SetExpanded(!_expanded);
        }

        public void OnDim()
        {
            SetExpanded(false);
        }

        // ヘッダーの ×. 第1段階では中身を隠す/戻すだけ(動きはモックでも未定)
        public void OnClose()
        {
            if (body == null) return;
            body.SetActive(!body.activeSelf);
            if (tooltip != null) tooltip.Hide();
            Debug.Log("[Etp2Trace] " + monitorName + ": close " + (body.activeSelf ? "戻す" : "隠す"));
        }

        private void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            float w = expanded ? ExpandedWidth : CollapsedWidth;
            if (navRects != null)
                for (int i = 0; i < navRects.Length; i++)
                    if (navRects[i] != null) navRects[i].sizeDelta = new Vector2(w, navRects[i].sizeDelta.y);
            if (toggleRect != null) toggleRect.sizeDelta = new Vector2(w, toggleRect.sizeDelta.y);
            if (navLabels != null)
                for (int i = 0; i < navLabels.Length; i++)
                    if (navLabels[i] != null) navLabels[i].gameObject.SetActive(expanded);
            if (navHovers != null)
                for (int i = 0; i < navHovers.Length; i++)
                    if (navHovers[i] != null) navHovers[i].suppressed = expanded;
            if (toggleLabel != null) toggleLabel.gameObject.SetActive(expanded);
            if (toggleIcon != null) toggleIcon.sprite = expanded ? collapseSprite : expandSprite;
            if (toggleHover != null)
            {
                // 広げているときは「たたむ」が見えるので札は出さない
                toggleHover.suppressed = expanded;
                toggleHover.SetLabel(expanded ? "たたむ" : "ひろげる");
            }
            if (band != null) band.SetActive(expanded);
            if (dim != null) dim.SetActive(expanded);
            if (tooltip != null) tooltip.Hide();
        }

        public void OnThemeChanged()
        {
            Paint();
        }

        private void Paint()
        {
            if (theme == null || !theme.ready || navButtons == null) return;
            for (int i = 0; i < navButtons.Length; i++)
            {
                int style = i == _current ? etp2_theme_target.StyleAccent : etp2_theme_target.StyleNormal;
                theme.PaintButton(navButtons[i], style);
                Color c = theme.LabelColor(style);
                if (navIcons != null && i < navIcons.Length && navIcons[i] != null) navIcons[i].color = c;
                if (navLabels != null && i < navLabels.Length && navLabels[i] != null) navLabels[i].color = c;
            }
        }
    }
}
