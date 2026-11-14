using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ali.eterpix.ui2
{
    // 画面1つぶんの色の塗り先. 配列はビルダーが入れる.
    // ボタンの押下色は Button の ColorBlock で出す(Image は白、Transition = Color Tint).
    // 状態で色が変わる部品(ナビの選択中・切り替え・一覧の行など)は listeners に OnThemeChanged を送り、各自が塗り直す
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_theme_target : UdonSharpBehaviour
    {
        public const int StyleNormal = 0;
        public const int StyleAccent = 1;
        public const int StyleDisabled = 2;
        public const int StyleAdd = 3;   // ドロップダウンの行の「追加」: 地は押下色、押している間はアクセント

        public Image[] bgImages;          // bg_
        public TextMeshProUGUI[] txTexts; // tx_
        public Image[] txImages;          // tx_(アイコン・×の線)
        public Button[] btnButtons;       // btn_
        public Image[] btnImages;         // btn_(ボタンでない面: 区切り線・丸アイコン・押せない帯・溝)
        public Scrollbar[] scrollbars;    // btn_(つまみはボタン色、押すと押下色)
        public Scrollbar[] listScrollbars;   // ドロップダウンの一覧の中(溝は一覧の面=ボタン色、つまみは押下色)
        public Button[] accButtons;       // acc_(ボタン)
        public Image[] accImages;         // acc_(ボタンでない面)
        public TextMeshProUGUI[] accTexts;   // acctx_
        public Image[] accTextImages;     // acctx_(アクセントの上のアイコン)
        public TextMeshProUGUI[] disTexts;   // dis_
        public UdonSharpBehaviour[] listeners;

        [HideInInspector] public Color cBackground;
        [HideInInspector] public Color cText;
        [HideInInspector] public Color cButton;
        [HideInInspector] public Color cPressed;
        [HideInInspector] public Color cAccent;
        [HideInInspector] public Color cAccentText;
        [HideInInspector] public Color cDisabledText;
        [HideInInspector] public bool ready;

        private bool _registered;

        private void Start()
        {
            if (!ready) ApplyTheme(etp2_theme.ThemeBlack);
            RegisterToTheme();
        }

        public void RegisterToTheme()
        {
            if (_registered) return;
            GameObject go = GameObject.Find(etp2_theme.SingletonObjectName);
            if (go == null) { Debug.LogWarning("[Etp2Trace] theme_target: " + etp2_theme.SingletonObjectName + " がシーンにありません. 黒で表示します"); return; }
            etp2_theme theme = go.GetComponent<etp2_theme>();
            if (theme == null) return;
            _registered = true;
            theme.Register(this);
        }

        public void ApplyTheme(int theme)
        {
            cBackground = etp2_theme.GetPresetColor(theme, etp2_theme.RoleBackground);
            cText = etp2_theme.GetPresetColor(theme, etp2_theme.RoleText);
            cButton = etp2_theme.GetPresetColor(theme, etp2_theme.RoleButton);
            cPressed = etp2_theme.GetPresetColor(theme, etp2_theme.RolePressed);
            cAccent = etp2_theme.GetPresetColor(theme, etp2_theme.RoleAccent);
            cAccentText = etp2_theme.GetPresetColor(theme, etp2_theme.RoleAccentText);
            cDisabledText = etp2_theme.GetPresetColor(theme, etp2_theme.RoleDisabledText);
            ready = true;

            if (bgImages != null) for (int i = 0; i < bgImages.Length; i++) if (bgImages[i] != null) bgImages[i].color = cBackground;
            if (txTexts != null) for (int i = 0; i < txTexts.Length; i++) if (txTexts[i] != null) txTexts[i].color = cText;
            if (txImages != null) for (int i = 0; i < txImages.Length; i++) if (txImages[i] != null) txImages[i].color = cText;
            if (btnButtons != null) for (int i = 0; i < btnButtons.Length; i++) PaintButton(btnButtons[i], StyleNormal);
            if (btnImages != null) for (int i = 0; i < btnImages.Length; i++) if (btnImages[i] != null) btnImages[i].color = cButton;
            if (scrollbars != null)
                for (int i = 0; i < scrollbars.Length; i++)
                {
                    if (scrollbars[i] == null) continue;
                    ColorBlock sb = scrollbars[i].colors;
                    sb.normalColor = cButton; sb.highlightedColor = cButton; sb.selectedColor = cButton;
                    sb.pressedColor = cPressed; sb.disabledColor = cButton;
                    sb.colorMultiplier = 1f; sb.fadeDuration = 0f;
                    scrollbars[i].colors = sb;
                }
            if (listScrollbars != null)
                for (int i = 0; i < listScrollbars.Length; i++)
                {
                    if (listScrollbars[i] == null) continue;
                    ColorBlock lb = listScrollbars[i].colors;
                    lb.normalColor = cPressed; lb.highlightedColor = cPressed; lb.selectedColor = cPressed;
                    lb.pressedColor = cPressed; lb.disabledColor = cPressed;
                    lb.colorMultiplier = 1f; lb.fadeDuration = 0f;
                    listScrollbars[i].colors = lb;
                }
            if (accButtons != null) for (int i = 0; i < accButtons.Length; i++) PaintButton(accButtons[i], StyleAccent);
            if (accImages != null) for (int i = 0; i < accImages.Length; i++) if (accImages[i] != null) accImages[i].color = cAccent;
            if (accTexts != null) for (int i = 0; i < accTexts.Length; i++) if (accTexts[i] != null) accTexts[i].color = cAccentText;
            if (accTextImages != null) for (int i = 0; i < accTextImages.Length; i++) if (accTextImages[i] != null) accTextImages[i].color = cAccentText;
            if (disTexts != null) for (int i = 0; i < disTexts.Length; i++) if (disTexts[i] != null) disTexts[i].color = cDisabledText;

            if (listeners != null)
                for (int i = 0; i < listeners.Length; i++)
                    if (listeners[i] != null) listeners[i].SendCustomEvent("OnThemeChanged");
        }

        // ボタンの面の色. 押している間は押下色(アクセントのボタンは押してもアクセントのまま)
        public void PaintButton(Button button, int style)
        {
            if (button == null) return;
            ColorBlock cb = button.colors;
            Color face = style == StyleAccent ? cAccent : (style == StyleAdd ? cPressed : cButton);
            cb.normalColor = face;
            cb.highlightedColor = face;
            cb.selectedColor = face;
            cb.pressedColor = style == StyleNormal || style == StyleDisabled ? cPressed : cAccent;
            cb.disabledColor = face;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0f;
            button.colors = cb;
            button.interactable = style != StyleDisabled;
        }

        // ボタンの上の文字・アイコンの色
        public Color LabelColor(int style)
        {
            if (style == StyleAccent) return cAccentText;
            if (style == StyleDisabled) return cDisabledText;
            return cText;
        }
    }
}
