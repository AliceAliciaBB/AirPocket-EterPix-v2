using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace ali.eterpix.v2
{
    // モニターの配色テーマ(ワールドに1つだけ置くシングルトン。固定名 SingletonObjectName で解決する)。
    // 切替はローカルのみ(同期しない)。切替ありモードでは、選んだテーマを PlayerData に保存する。
    // 色の役割: 背景 / 文字 / ボタン / アクセント(ポータルボタン) / アクセント上の文字
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_theme : UdonSharpBehaviour
    {
        public const string SingletonObjectName = "EterpixTheme";
        public const string PersistenceKey = "eterpix.theme";

        public const int ThemeBlack = 0;
        public const int ThemeWhite = 1;
        public const int ThemeCustom = 2;

        public const int ModeSwitchable = 0;
        public const int ModeFixedBlack = 1;
        public const int ModeFixedWhite = 2;
        public const int ModeFixedCustom = 3;

        public const int RoleBackground = 0;
        public const int RoleText = 1;
        public const int RoleButton = 2;
        public const int RoleAccent = 3;
        public const int RoleAccentText = 4;

        // 値の意味は上の Mode* / Theme* 定数(Inspectorは Editor/eterpix_themeEditor.cs が日本語で表示する)
        [SerializeField] private int switchMode = ModeSwitchable;
        [SerializeField] private bool includeCustomInCycle = false;
        [SerializeField] private int initialTheme = ThemeBlack;

        [SerializeField] private Color customBackground = new Color(20f / 255f, 20f / 255f, 20f / 255f, 1f);
        [SerializeField] private Color customText = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color customButton = new Color(42f / 255f, 42f / 255f, 42f / 255f, 1f);
        [SerializeField] private Color customAccent = new Color(157f / 255f, 111f / 255f, 1f, 1f);
        [SerializeField] private Color customAccentText = new Color(20f / 255f, 20f / 255f, 20f / 255f, 1f);

        private eterpix_monitor_theme[] _monitors = new eterpix_monitor_theme[0];
        private int _monitorCount = 0;
        private int _currentTheme = ThemeBlack;
        private bool _initialized = false;

        public bool IsSwitchable => switchMode == ModeSwitchable;

        private void Start()
        {
            GameObject canonical = GameObject.Find(SingletonObjectName);
            if (canonical != null && canonical != this.gameObject)
            {
                Debug.LogWarning("[eterpix_theme] Duplicate instance detected. Disabling self.");
                gameObject.SetActive(false);
                return;
            }
            EnsureInitialized();
            ApplyAll();
        }

        // モニター側のStartが先に走ることがあるため、Register/Startのどちらからでも初期化する
        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _currentTheme = GetStartTheme();
        }

        public int GetStartTheme()
        {
            if (switchMode == ModeFixedBlack) return ThemeBlack;
            if (switchMode == ModeFixedWhite) return ThemeWhite;
            if (switchMode == ModeFixedCustom) return ThemeCustom;
            return IsSelectable(initialTheme) ? initialTheme : ThemeBlack;
        }

        public void Register(eterpix_monitor_theme monitorTheme)
        {
            if (monitorTheme == null) return;
            EnsureInitialized();

            if (_monitorCount >= _monitors.Length)
            {
                eterpix_monitor_theme[] bigger = new eterpix_monitor_theme[_monitors.Length + 8];
                for (int i = 0; i < _monitors.Length; i++) bigger[i] = _monitors[i];
                _monitors = bigger;
            }
            _monitors[_monitorCount] = monitorTheme;
            _monitorCount++;

            ApplyTo(monitorTheme);
        }

        // モニターの切替ボタンから(eterpix_monitor_theme.CycleTheme経由で)呼ばれる。ローカルのみ
        public void CycleTheme()
        {
            if (!IsSwitchable) return;
            EnsureInitialized();
            _currentTheme = GetNextTheme(_currentTheme);
            ApplyAll();
            PlayerData.SetInt(PersistenceKey, _currentTheme);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || !IsSwitchable) return;
            if (PlayerData.TryGetInt(player, PersistenceKey, out int saved) && IsSelectable(saved))
            {
                EnsureInitialized();
                _currentTheme = saved;
                ApplyAll();
            }
        }

        private int GetNextTheme(int theme)
        {
            if (theme == ThemeBlack) return ThemeWhite;
            if (theme == ThemeWhite) return includeCustomInCycle ? ThemeCustom : ThemeBlack;
            return ThemeBlack;
        }

        private bool IsSelectable(int theme)
        {
            return theme == ThemeBlack || theme == ThemeWhite || (theme == ThemeCustom && includeCustomInCycle);
        }

        private void ApplyAll()
        {
            for (int i = 0; i < _monitorCount; i++) ApplyTo(_monitors[i]);
        }

        private void ApplyTo(eterpix_monitor_theme monitorTheme)
        {
            if (monitorTheme == null) return;
            monitorTheme.ApplyColors(
                GetColor(_currentTheme, RoleBackground),
                GetColor(_currentTheme, RoleText),
                GetColor(_currentTheme, RoleButton),
                GetColor(_currentTheme, RoleAccent),
                GetColor(_currentTheme, RoleAccentText),
                IsSwitchable);
        }

        public Color GetColor(int theme, int role)
        {
            if (theme != ThemeCustom) return GetPresetColor(theme, role);
            if (role == RoleBackground) return customBackground;
            if (role == RoleText) return customText;
            if (role == RoleButton) return customButton;
            if (role == RoleAccent) return customAccent;
            return customAccentText;
        }

        // 黒/白プリセット(仕様書「8. テーマ」の表)。色はすべて不透明
        public static Color GetPresetColor(int theme, int role)
        {
            bool white = theme == ThemeWhite;
            if (role == RoleBackground) return white ? Rgb(250, 250, 250) : Rgb(20, 20, 20);
            if (role == RoleText) return white ? Rgb(20, 20, 20) : Rgb(255, 255, 255);
            if (role == RoleButton) return white ? Rgb(230, 230, 230) : Rgb(42, 42, 42);
            if (role == RoleAccent) return Rgb(157, 111, 255);
            return Rgb(20, 20, 20);
        }

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        #region Editor
#if !COMPILER_UDONSHARP && UNITY_EDITOR
        // Inspector(eterpix_themeEditor)から呼ぶ。シーン内の全モニターへ開始時のテーマを塗る
        public void EditorApplyToScene()
        {
            int theme = GetStartTheme();
            eterpix_monitor_theme[] all = Object.FindObjectsOfType<eterpix_monitor_theme>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].IsDirectEdit) continue;
                all[i].EditorApplyColors(
                    GetColor(theme, RoleBackground),
                    GetColor(theme, RoleText),
                    GetColor(theme, RoleButton),
                    GetColor(theme, RoleAccent),
                    GetColor(theme, RoleAccentText));
            }
        }
#endif
        #endregion
    }
}
