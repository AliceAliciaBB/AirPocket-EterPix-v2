using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace ali.eterpix.ui2
{
    // v2 UI のテーマ(ワールドに1つ). 画面ごとの etp2_theme_target へ7役の色を配る.
    // v1 の eterpix_theme と同じ作り(切り替え可/黒固定/白固定、PlayerData で保持)だが、保存キーは別.
    // 色の表: 統合管理 docs/plans/unity-ui-v2-mock.md 1.2
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_theme : UdonSharpBehaviour
    {
        public const string SingletonObjectName = "Etp2_Theme";
        public const string PersistenceKey = "eterpix.ui2.theme";

        public const int ThemeBlack = 0;
        public const int ThemeWhite = 1;

        public const int ModeSwitchable = 0;
        public const int ModeFixedBlack = 1;
        public const int ModeFixedWhite = 2;

        public const int RoleBackground = 0;
        public const int RoleText = 1;
        public const int RoleButton = 2;
        public const int RolePressed = 3;
        public const int RoleAccent = 4;
        public const int RoleAccentText = 5;
        public const int RoleDisabledText = 6;

        [Tooltip("0: 見る人が切り替えられる / 1: 黒で固定 / 2: 白で固定")]
        public int switchMode = ModeSwitchable;
        [Tooltip("切り替えられるときの、初めのテーマ(0: 黒 / 1: 白)")]
        public int defaultTheme = ThemeBlack;

        private etp2_theme_target[] _targets = new etp2_theme_target[16];
        private int _targetCount;
        private int _current = -1;
        private bool _restored;
        private bool _hasLocalChoice;

        public bool IsSwitchable() { return switchMode == ModeSwitchable; }

        public int GetStartTheme()
        {
            if (switchMode == ModeFixedBlack) return ThemeBlack;
            if (switchMode == ModeFixedWhite) return ThemeWhite;
            return defaultTheme == ThemeWhite ? ThemeWhite : ThemeBlack;
        }

        public int GetCurrentTheme()
        {
            if (_current < 0) _current = GetStartTheme();
            return _current;
        }

        public void Register(etp2_theme_target target)
        {
            if (target == null) return;
            for (int i = 0; i < _targetCount; i++) if (_targets[i] == target) { ApplyTo(target); return; }
            if (_targetCount >= _targets.Length)
            {
                etp2_theme_target[] grown = new etp2_theme_target[_targets.Length * 2];
                for (int i = 0; i < _targetCount; i++) grown[i] = _targets[i];
                _targets = grown;
            }
            _targets[_targetCount++] = target;
            ApplyTo(target);
        }

        // 設定ページの「黒/白」から呼ぶ. 押した人にだけ効く
        public void SetTheme(int theme)
        {
            if (!IsSwitchable()) return;
            if (theme != ThemeWhite) theme = ThemeBlack;
            _current = theme;
            _hasLocalChoice = true;
            if (_restored) PlayerData.SetInt(PersistenceKey, _current);
            Debug.Log("[Etp2Trace] theme: " + (_current == ThemeWhite ? "白" : "黒"));
            for (int i = 0; i < _targetCount; i++) ApplyTo(_targets[i]);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || !IsSwitchable()) return;
            _restored = true;
            // 復元前に押された切り替えを、復元で巻き戻さない
            if (_hasLocalChoice) { PlayerData.SetInt(PersistenceKey, _current); return; }
            int saved;
            if (PlayerData.TryGetInt(player, PersistenceKey, out saved) && (saved == ThemeBlack || saved == ThemeWhite))
            {
                _current = saved;
                for (int i = 0; i < _targetCount; i++) ApplyTo(_targets[i]);
            }
        }

        private void ApplyTo(etp2_theme_target target)
        {
            if (target == null) return;
            target.ApplyTheme(GetCurrentTheme());
        }

        public static Color GetPresetColor(int theme, int role)
        {
            bool white = theme == ThemeWhite;
            if (role == RoleBackground) return white ? Rgb(250, 250, 250) : Rgb(20, 20, 20);
            if (role == RoleText) return white ? Rgb(20, 20, 20) : Rgb(255, 255, 255);
            if (role == RoleButton) return white ? Rgb(230, 230, 230) : Rgb(42, 42, 42);
            if (role == RolePressed) return white ? Rgb(214, 214, 214) : Rgb(58, 58, 58);   // 白は仮
            if (role == RoleAccent) return Rgb(157, 111, 255);
            if (role == RoleAccentText) return Rgb(20, 20, 20);
            return white ? Rgb(154, 154, 154) : Rgb(106, 106, 106);   // 押せない文字. 白は仮
        }

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
