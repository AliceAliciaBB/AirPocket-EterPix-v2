using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace ali.eterpix.v2
{
    // モニター1台ぶんの配色の適用先。子オブジェクト名の接頭辞で役割を決める:
    //   bg_ = 背景(Image) / tx_ = 文字(TMP、またはアイコンのImage) / btn_ = ボタン(Image)
    //   acc_ = アクセント(Image) / acctx_ = アクセント上の文字(TMP)
    // 配列はInspectorの「子から自動収集」で作る(Editor/eterpix_monitor_themeEditor.cs)。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_monitor_theme : UdonSharpBehaviour
    {
        [Header("直接編集: ONにするとテーマの対象外(色は各Image/TMPを直接編集する)")]
        [SerializeField] private bool directEdit = false;

        [Header("テーマ切替ボタン(切替できないときは非表示にする)")]
        [SerializeField] private GameObject themeSwitchButton;

        [Header("配色が変わったことを伝えるモニター(読み込み中の明滅色の更新)")]
        [SerializeField] private eterpix_monitor monitor;

        [Header("役割ごとの適用先(「子から自動収集」で作る)")]
        [SerializeField] private Image[] backgroundImages = new Image[0];
        [SerializeField] private TMP_Text[] texts = new TMP_Text[0];
        [SerializeField] private Image[] textImages = new Image[0];
        [SerializeField] private Image[] buttonImages = new Image[0];
        [SerializeField] private Image[] accentImages = new Image[0];
        [SerializeField] private TMP_Text[] accentTexts = new TMP_Text[0];

        private eterpix_theme _theme;
        private Color _buttonColor = new Color(42f / 255f, 42f / 255f, 42f / 255f, 1f);

        // 画像の読み込み中にPostImageを明滅させる色(=テーマのボタン色)
        public Color LoadingColor => _buttonColor;
        public bool IsDirectEdit => directEdit;

        private void Start()
        {
            if (buttonImages != null && buttonImages.Length > 0 && buttonImages[0] != null)
            {
                _buttonColor = buttonImages[0].color;
            }

            if (directEdit)
            {
                SetSwitchVisible(false);
                return;
            }

            GameObject themeObj = GameObject.Find(eterpix_theme.SingletonObjectName);
            if (themeObj != null) _theme = themeObj.GetComponent<eterpix_theme>();
            if (_theme == null)
            {
                // テーマのシングルトンが無いワールドでは、prefabに焼き込んだ色のまま切替ボタンを隠す
                SetSwitchVisible(false);
                return;
            }
            _theme.Register(this);
        }

        // テーマ切替ボタンのOnClick(SendCustomEvent "CycleTheme")
        public void CycleTheme()
        {
            if (_theme != null) _theme.CycleTheme();
        }

        public void ApplyColors(Color background, Color text, Color button, Color accent, Color accentText, bool switchVisible)
        {
            SetImageColors(backgroundImages, background);
            SetTextColors(texts, text);
            SetImageColors(textImages, text);
            SetImageColors(buttonImages, button);
            SetImageColors(accentImages, accent);
            SetTextColors(accentTexts, accentText);
            _buttonColor = button;
            SetSwitchVisible(switchVisible);
            if (monitor != null) monitor.OnThemeChanged();
        }

        private void SetSwitchVisible(bool visible)
        {
            if (themeSwitchButton != null) themeSwitchButton.SetActive(visible);
        }

        private void SetImageColors(Image[] targets, Color color)
        {
            if (targets == null) return;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targets[i].color = color;
            }
        }

        private void SetTextColors(TMP_Text[] targets, Color color)
        {
            if (targets == null) return;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targets[i].color = color;
            }
        }

        #region Editor
#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorCollectFromChildren()
        {
            List<Image> bg = new List<Image>();
            List<TMP_Text> tx = new List<TMP_Text>();
            List<Image> txImages = new List<Image>();
            List<Image> btn = new List<Image>();
            List<Image> acc = new List<Image>();
            List<TMP_Text> accTx = new List<TMP_Text>();
            GameObject switchButton = null;

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                Image img = t.GetComponent<Image>();
                TMP_Text text = t.GetComponent<TMP_Text>();
                if (n == "btn_ThemeButton") switchButton = t.gameObject;

                if (n.StartsWith("bg_")) { if (img != null) bg.Add(img); }
                else if (n.StartsWith("tx_")) { if (text != null) tx.Add(text); else if (img != null) txImages.Add(img); }
                else if (n.StartsWith("btn_")) { if (img != null) btn.Add(img); }
                else if (n.StartsWith("acctx_")) { if (text != null) accTx.Add(text); }
                else if (n.StartsWith("acc_")) { if (img != null) acc.Add(img); }
            }

            Undo.RecordObject(this, "Collect theme targets");
            backgroundImages = bg.ToArray();
            texts = tx.ToArray();
            textImages = txImages.ToArray();
            buttonImages = btn.ToArray();
            accentImages = acc.ToArray();
            accentTexts = accTx.ToArray();
            if (themeSwitchButton == null) themeSwitchButton = switchButton;
            if (monitor == null) monitor = GetComponent<eterpix_monitor>();
            EditorUtility.SetDirty(this);
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        public void EditorApplyPreset(int theme)
        {
            EditorApplyColors(
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleBackground),
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleText),
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleButton),
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleAccent),
                eterpix_theme.GetPresetColor(theme, eterpix_theme.RoleAccentText));
        }

        public void EditorApplyColors(Color background, Color text, Color button, Color accent, Color accentText)
        {
            EditorSetImages(backgroundImages, background);
            EditorSetTexts(texts, text);
            EditorSetImages(textImages, text);
            EditorSetImages(buttonImages, button);
            EditorSetImages(accentImages, accent);
            EditorSetTexts(accentTexts, accentText);
            if (monitor != null) monitor.EditorSetLoadingPreviewColor(button);
        }

        private static void EditorSetImages(Image[] targets, Color color)
        {
            if (targets == null) return;
            foreach (Image g in targets)
            {
                if (g == null || g.color == color) continue;
                Undo.RecordObject(g, "Apply theme");
                g.color = color;
                EditorUtility.SetDirty(g);
                PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            }
        }

        private static void EditorSetTexts(TMP_Text[] targets, Color color)
        {
            if (targets == null) return;
            foreach (TMP_Text g in targets)
            {
                if (g == null || g.color == color) continue;
                Undo.RecordObject(g, "Apply theme");
                g.color = color;
                EditorUtility.SetDirty(g);
                PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            }
        }
#endif
        #endregion
    }
}
