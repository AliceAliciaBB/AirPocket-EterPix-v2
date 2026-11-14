using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ali.eterpix.ui2
{
    // メニューモニターの「ソート」ページ(モックは暫定). 編集は「適用」を押したときに効く.
    // 第1段階は編集と押せる/押せないまで. 並べ替え(繰り上げ)は第3段階、鍵の対象にするのは第2段階.
    // 決まり: 統合管理 docs/plans/unity-ui-v2-mock.md 4.3、unity-ui-v2-sort.md 1章
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_sort_page : UdonSharpBehaviour
    {
        public const int CatWorld = 0;
        public const int CatTag = 1;
        public const int CatUser = 2;
        public const int CatCount = 3;
        public const int MaxCand = 16;   // 1つの欄の候補の最大数(行の数)
        public const int MaxSel = 16;    // 1つの欄に追加できる最大数
        public const float RowHeight = 40f;
        public const float MaxRows = 5.5f;

        public etp2_mock_data data;
        public etp2_theme_target theme;

        [Header("初期設定に戻す / 適用")]
        public Button applyButton;
        public TextMeshProUGUI applyLabel;

        [Header("並び順 [最新順][いいね数順]")]
        public Button[] orderButtons;
        public TextMeshProUGUI[] orderLabels;

        [Header("ドロップダウン(欄ごと)")]
        public TextMeshProUGUI[] arrowTexts;     // ▲ / ▼
        public GameObject[] listRoots;           // 上へ開く一覧
        public RectTransform[] listRects;
        public GameObject[] emptyRows;           // 「追加できるものはありません」

        [Header("候補の行(欄 × MaxCand)")]
        public GameObject[] candRows;
        public TextMeshProUGUI[] candNames;
        public Button[] candAddButtons;
        public TextMeshProUGUI[] candAddLabels;

        [Header("追加したものの行(欄 × MaxSel)")]
        public GameObject[] selRows;
        public TextMeshProUGUI[] selNames;

        [HideInInspector] public int pressedIndex;

        // 候補(データから集める)
        private string[] _cand = new string[CatCount * MaxCand];
        private int[] _candCount = new int[CatCount];
        // 編集中と適用済み
        private int _order;
        private string[] _sel = new string[CatCount * MaxSel];
        private int[] _selCount = new int[CatCount];
        private int _appliedOrder;
        private string[] _appliedSel = new string[CatCount * MaxSel];
        private int[] _appliedCount = new int[CatCount];
        private int _open = -1;
        // 候補の行 → 候補の番号
        private int[] _rowToCand = new int[CatCount * MaxCand];

        // 作者の既定(第1段階は「最新順・条件なし」)
        public int defaultOrder;

        // メニューモニター(ルートの部品)がデータを渡して呼ぶ
        public void Init(etp2_mock_data source)
        {
            data = source;
            CollectCandidates();
            ResetToDefault();
            CopyToApplied();
            Render();
        }

        // ---- ボタン ----

        public void OnOrder()
        {
            _order = pressedIndex;
            Render();
        }

        public void OnToggleDropdown()
        {
            _open = _open == pressedIndex ? -1 : pressedIndex;
            Render();
        }

        public void OnAdd()
        {
            int cat = pressedIndex / MaxCand;
            int c = _rowToCand[pressedIndex];
            if (c < 0 || _selCount[cat] >= MaxSel) return;
            _sel[cat * MaxSel + _selCount[cat]] = _cand[cat * MaxCand + c];
            _selCount[cat]++;
            Render();   // 続けて足せるよう、一覧は開いたまま
        }

        public void OnRemove()
        {
            int cat = pressedIndex / MaxSel;
            int i = pressedIndex % MaxSel;
            if (i >= _selCount[cat]) return;
            for (int k = i; k < _selCount[cat] - 1; k++) _sel[cat * MaxSel + k] = _sel[cat * MaxSel + k + 1];
            _selCount[cat]--;
            _sel[cat * MaxSel + _selCount[cat]] = null;
            Render();
        }

        public void OnReset()
        {
            ResetToDefault();
            Render();
        }

        public void OnApply()
        {
            if (!IsDirty()) return;
            CopyToApplied();
            _open = -1;
            Debug.Log("[Etp2Trace] sort: apply order=" + (_order == 0 ? "最新順" : "いいね数順")
                + " world=" + Join(CatWorld) + " tag=" + Join(CatTag) + " user=" + Join(CatUser));
            Render();
        }

        public void OnThemeChanged()
        {
            Render();
        }

        // ---- 状態 ----

        private void ResetToDefault()
        {
            _order = defaultOrder;
            for (int c = 0; c < CatCount; c++) _selCount[c] = 0;
            for (int i = 0; i < _sel.Length; i++) _sel[i] = null;
        }

        private void CopyToApplied()
        {
            _appliedOrder = _order;
            for (int c = 0; c < CatCount; c++) _appliedCount[c] = _selCount[c];
            for (int i = 0; i < _sel.Length; i++) _appliedSel[i] = _sel[i];
        }

        private bool IsDirty()
        {
            if (_order != _appliedOrder) return true;
            for (int c = 0; c < CatCount; c++)
            {
                if (_selCount[c] != _appliedCount[c]) return true;
                for (int i = 0; i < _selCount[c]; i++) if (_sel[c * MaxSel + i] != _appliedSel[c * MaxSel + i]) return true;
            }
            return false;
        }

        private bool IsSelected(int cat, string v)
        {
            for (int i = 0; i < _selCount[cat]; i++) if (_sel[cat * MaxSel + i] == v) return true;
            return false;
        }

        private string Join(int cat)
        {
            string s = "[";
            for (int i = 0; i < _selCount[cat]; i++) s += (i > 0 ? "," : "") + _sel[cat * MaxSel + i];
            return s + "]";
        }

        // 候補は投稿の情報から集める. タグは写真説明の中の「#…」
        private void CollectCandidates()
        {
            for (int c = 0; c < CatCount; c++) _candCount[c] = 0;
            if (data == null) return;
            int n = data.PostCount();
            for (int p = 0; p < n; p++)
            {
                AddCand(CatWorld, data.postWorld[p]);
                AddCand(CatUser, data.postUser[p]);
                string d = data.postDesc[p];
                if (d == null) continue;
                int at = d.IndexOf('#');
                while (at >= 0)
                {
                    int end = at + 1;
                    while (end < d.Length && d[end] != ' ' && d[end] != '\n' && d[end] != '#' && d[end] != '　') end++;
                    if (end > at + 1) AddCand(CatTag, d.Substring(at, end - at));
                    at = end < d.Length ? d.IndexOf('#', end) : -1;
                }
            }
        }

        private void AddCand(int cat, string v)
        {
            if (string.IsNullOrEmpty(v) || _candCount[cat] >= MaxCand) return;
            for (int i = 0; i < _candCount[cat]; i++) if (_cand[cat * MaxCand + i] == v) return;
            _cand[cat * MaxCand + _candCount[cat]] = v;
            _candCount[cat]++;
        }

        // ---- 描画 ----

        private void Render()
        {
            for (int cat = 0; cat < CatCount; cat++)
            {
                bool open = _open == cat;
                if (arrowTexts != null && arrowTexts[cat] != null) arrowTexts[cat].text = open ? "▼" : "▲";
                if (listRoots != null && listRoots[cat] != null) listRoots[cat].SetActive(open);

                int rows = 0;
                for (int c = 0; c < _candCount[cat]; c++)
                {
                    string v = _cand[cat * MaxCand + c];
                    if (IsSelected(cat, v)) continue;
                    int r = cat * MaxCand + rows;
                    _rowToCand[r] = c;
                    candRows[r].SetActive(true);
                    candNames[r].text = v;
                    rows++;
                }
                for (int r = rows; r < MaxCand; r++)
                {
                    _rowToCand[cat * MaxCand + r] = -1;
                    candRows[cat * MaxCand + r].SetActive(false);
                }
                if (emptyRows != null && emptyRows[cat] != null) emptyRows[cat].SetActive(rows == 0);
                if (listRects != null && listRects[cat] != null)
                {
                    float shown = Mathf.Min(Mathf.Max(rows, 1), MaxRows);
                    listRects[cat].sizeDelta = new Vector2(listRects[cat].sizeDelta.x, shown * RowHeight);
                }

                for (int i = 0; i < MaxSel; i++)
                {
                    int k = cat * MaxSel + i;
                    bool used = i < _selCount[cat];
                    selRows[k].SetActive(used);
                    if (used) selNames[k].text = _sel[k];
                }
            }
            Paint();
        }

        private void Paint()
        {
            if (theme == null || !theme.ready) return;
            int applyStyle = IsDirty() ? etp2_theme_target.StyleAccent : etp2_theme_target.StyleDisabled;
            theme.PaintButton(applyButton, applyStyle);
            if (applyLabel != null) applyLabel.color = theme.LabelColor(applyStyle);
            if (orderButtons != null)
                for (int i = 0; i < orderButtons.Length; i++)
                {
                    int style = i == _order ? etp2_theme_target.StyleAccent : etp2_theme_target.StyleNormal;
                    theme.PaintButton(orderButtons[i], style);
                    if (orderLabels != null && orderLabels[i] != null) orderLabels[i].color = theme.LabelColor(style);
                }
            if (candAddButtons != null)
                for (int i = 0; i < candAddButtons.Length; i++)
                {
                    theme.PaintButton(candAddButtons[i], etp2_theme_target.StyleAdd);
                    if (candAddLabels != null && candAddLabels[i] != null) candAddLabels[i].color = theme.cText;
                }
        }
    }
}
