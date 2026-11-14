using TMPro;
using UdonSharp;
using UnityEngine;

namespace ali.eterpix.ui2
{
    // 画面1つに1枚のホバーの札. 当ててから 0.3 秒で出し、離す・押すですぐ消す.
    // 位置はボタンの下 8(ナビボタンは右 8). はみ出すなら反対側、それでもはみ出す分は内側へずらす.
    // 札は画面の一番上(最後の子)に置き、当たり判定を持たない(ビルダーが raycastTarget = false にする)
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_tooltip : UdonSharpBehaviour
    {
        public const float Delay = 0.3f;
        public const float Gap = 8f;
        public const float Height = 32f;
        public const float PadX = 8f;
        public const float Border = 1f;

        public RectTransform screenRect;   // 画面の Canvas の RectTransform
        public RectTransform tipRect;      // 札(左上基準. 親は screenRect)
        public TextMeshProUGUI tipText;

        private etp2_hover _pending;
        private etp2_hover _shown;
        private int _scheduled;
        private Vector3[] _corners = new Vector3[4];

        private void Start()
        {
            if (tipRect != null) tipRect.gameObject.SetActive(false);
        }

        public void Enter(etp2_hover hover)
        {
            if (hover == null || hover.suppressed || string.IsNullOrEmpty(hover.label)) { Hide(); return; }
            if (_shown == hover) return;
            Hide();
            _pending = hover;
            _scheduled++;
            SendCustomEventDelayedSeconds(nameof(_ShowPending), Delay);
        }

        public void Exit(etp2_hover hover)
        {
            if (hover == _pending || hover == _shown) Hide();
        }

        public void Hide()
        {
            _pending = null;
            _shown = null;
            if (tipRect != null) tipRect.gameObject.SetActive(false);
        }

        public void Refresh(etp2_hover hover)
        {
            if (hover != null && hover == _shown) Place(hover);
        }

        public void _ShowPending()
        {
            _scheduled--;
            if (_scheduled > 0 || _pending == null) return;
            if (!_pending.gameObject.activeInHierarchy || _pending.suppressed) { Hide(); return; }
            _shown = _pending;
            _pending = null;
            Place(_shown);
        }

        // 札の幅は ContentSizeFitter が決める(U# では文字の幅を測れない). 決まるのを1フレーム待ってから置く.
        // 待つ間は縮尺 0 にして、前の位置に見えないようにする
        private void Place(etp2_hover hover)
        {
            if (screenRect == null || tipRect == null || tipText == null) return;
            tipText.text = hover.label;
            tipRect.localScale = Vector3.zero;
            tipRect.gameObject.SetActive(true);
            tipRect.SetAsLastSibling();
            SendCustomEventDelayedFrames(nameof(_Layout), 1);
        }

        public void _Layout()
        {
            etp2_hover hover = _shown;
            if (hover == null || tipRect == null || !tipRect.gameObject.activeSelf) return;
            float w = tipRect.rect.width;

            // ボタンの四隅を、画面の左上を原点(下が +)とする座標に直す
            RectTransform target = (RectTransform)hover.transform;
            target.GetWorldCorners(_corners);
            Rect r = screenRect.rect;
            Vector3 tl = screenRect.InverseTransformPoint(_corners[1]);
            Vector3 br = screenRect.InverseTransformPoint(_corners[3]);
            float bx = tl.x - r.xMin;
            float by = r.yMax - tl.y;
            float bw = br.x - tl.x;
            float bh = tl.y - br.y;
            float mw = r.width;
            float mh = r.height;

            float x;
            float y;
            if (hover.side == etp2_hover.SideRight)
            {
                x = bx + bw + Gap;
                y = by + (bh - Height) / 2f;
                if (x + w > mw) x = bx - Gap - w;
            }
            else
            {
                x = bx + (bw - w) / 2f;
                y = by + bh + Gap;
                if (y + Height > mh) y = by - Gap - Height;
            }
            x = Mathf.Max(0f, Mathf.Min(x, mw - w));
            y = Mathf.Max(0f, Mathf.Min(y, mh - Height));
            tipRect.anchoredPosition = new Vector2(x, -y);
            tipRect.localScale = Vector3.one;
        }
    }
}
