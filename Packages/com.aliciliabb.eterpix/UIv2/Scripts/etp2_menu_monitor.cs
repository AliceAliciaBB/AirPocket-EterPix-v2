using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ali.eterpix.ui2
{
    // メニューモニターのページの中身のうち、設定・ワールド一覧・リンクのボタン(ソートは etp2_sort_page).
    // prefab のルートに付ける. ワールド作者がつなぐのは「データ」「ビューア」だけ(ほかはビルダーが入れる)
    // 決まり: 統合管理 docs/plans/unity-ui-v2-mock.md 4章
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_menu_monitor : UdonSharpBehaviour
    {
        public const string GroupId = "grp_b3f71193-2ed1-4de0-a5f1-3237609f4aeb";

        [Header("ワールド作者がつなぐ")]
        public etp2_mock_data data;
        public etp2_viewer viewer;

        [Header("ビルダーが入れる")]
        public etp2_theme_target theme;
        public etp2_sort_page sortPage;
        private etp2_theme themeSource;

        [Header("設定: テーマ [黒][白]")]
        public Button[] themeButtons;
        public TextMeshProUGUI[] themeLabels;
        [Header("設定: 前/次 [◀ 前 / 次 ▶][◀ 次 / 前 ▶]")]
        public Button[] swapButtons;
        public TextMeshProUGUI[] swapLabels;
        public Image[] swapIcons;   // ◀ / ▶ のアイコン. ボタン i の左 = i*2、右 = i*2+1

        [Header("ワールド一覧(使い回しの行)")]
        public GameObject[] worldRows;
        public TextMeshProUGUI[] worldNames;
        public TextMeshProUGUI[] worldAuthors;

        [HideInInspector] public int pressedIndex;

        private void Start()
        {
            GameObject go = GameObject.Find(etp2_theme.SingletonObjectName);
            if (go != null) themeSource = go.GetComponent<etp2_theme>();
            if (sortPage != null) sortPage.Init(data);
            FillWorlds();
            Paint();
        }

        public void OnTheme()
        {
            if (themeSource == null || !themeSource.IsSwitchable()) return;
            themeSource.SetTheme(pressedIndex);
        }

        public void OnSwap()
        {
            if (viewer != null) viewer.SetSwap(pressedIndex == 1);
            Paint();
        }

        public void OnPortal()
        {
            // ワールド一覧のポータル. 第1段階ではログだけ
            Debug.Log("[Etp2Trace] menu: portal " + pressedIndex);
        }

        public void OpenGroupPage()
        {
            VRC.Economy.Store.OpenGroupPage(GroupId);
        }

        public void OnThemeChanged()
        {
            Paint();
        }

        private void FillWorlds()
        {
            if (worldRows == null) return;
            int n = data == null ? 0 : data.WorldCount();
            for (int i = 0; i < worldRows.Length; i++)
            {
                if (worldRows[i] == null) continue;
                bool used = i < n;
                worldRows[i].SetActive(used);
                if (!used) continue;
                worldNames[i].text = data.worldName[i];
                worldAuthors[i].text = data.worldAuthor[i];
            }
        }

        private void Paint()
        {
            if (theme == null || !theme.ready) return;
            bool switchable = themeSource == null || themeSource.IsSwitchable();
            int currentTheme = themeSource == null ? etp2_theme.ThemeBlack : themeSource.GetCurrentTheme();
            PaintSegment(themeButtons, themeLabels, null, currentTheme, switchable);
            bool swap = viewer != null && viewer.IsSwapped();
            PaintSegment(swapButtons, swapLabels, swapIcons, swap ? 1 : 0, true);
        }

        private void PaintSegment(Button[] buttons, TextMeshProUGUI[] labels, Image[] icons, int selected, bool enabled)
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
            {
                int style = !enabled ? etp2_theme_target.StyleDisabled
                    : (i == selected ? etp2_theme_target.StyleAccent : etp2_theme_target.StyleNormal);
                theme.PaintButton(buttons[i], style);
                Color c = theme.LabelColor(style);
                if (labels != null && i < labels.Length && labels[i] != null) labels[i].color = c;
                if (icons != null)
                    for (int k = i * 2; k < i * 2 + 2 && k < icons.Length; k++)
                        if (icons[k] != null) icons[k].color = c;
            }
        }
    }
}
