using UdonSharp;
using UnityEngine;

namespace ali.eterpix.ui2
{
    // ホバーの札を出すボタンに付ける. EventTrigger(PointerEnter / PointerExit / PointerDown)から呼ぶ.
    // 決まり: 統合管理 docs/plans/unity-ui-v2-mock.md 1.5
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_hover : UdonSharpBehaviour
    {
        public const int SideBelow = 0;
        public const int SideRight = 1;

        public etp2_tooltip tooltip;
        public string label;
        [Tooltip("0: ボタンの下に出す / 1: 右に出す(ナビボタン)")]
        public int side = SideBelow;
        [Tooltip("true の間は札を出さない(ナビを広げているときなど)")]
        public bool suppressed;

        public void OnPointerEnterEvent() { if (tooltip != null) tooltip.Enter(this); }
        public void OnPointerExitEvent() { if (tooltip != null) tooltip.Exit(this); }
        public void OnPointerDownEvent() { if (tooltip != null) tooltip.Hide(); }

        // 鍵・展開ボタンのように、状態で文言が変わるもの
        public void SetLabel(string text)
        {
            label = text;
            if (tooltip != null) tooltip.Refresh(this);
        }
    }
}
