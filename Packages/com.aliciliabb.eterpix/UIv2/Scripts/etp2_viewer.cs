using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace ali.eterpix.ui2
{
    // ビューア(ワールドに1つ). 開いたフォトフレームの写真を大きく見せ、前/次・鍵を操作する.
    // 第1段階: 開く・閉じる・前/次・入れ替えは手元だけ. 鍵と受け渡しのパネルは見た目だけ.
    // 決まり: 統合管理 docs/plans/unity-ui-v2-mock.md 6章
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_viewer : UdonSharpBehaviour
    {
        public const float HandoverSeconds = 5f;

        [Header("ワールド作者がつなぐ")]
        public etp2_mock_data data;
        public etp2_detail_monitor detail;
        [Tooltip("開いている間はビューアの左隣へ移し、閉じたら元の場所へ戻す(無くてよい)")]
        public etp2_menu_monitor menu;

        [Header("ビルダーが入れる")]
        [Tooltip("隣のモニターまでの横のずれ(ビューアのルートの座標. 幅 560 + 間 32)")]
        public float sideOffset = 592f;
        public etp2_theme_target theme;
        public etp2_tooltip tooltip;
        public GameObject screen;          // 閉じると隠す

        [Header("画像と前/次")]
        public RawImage photo;
        public TextMeshProUGUI pageText;
        public TextMeshProUGUI leftLabel;
        public TextMeshProUGUI rightLabel;

        [Header("鍵")]
        public Button lockButton;
        public Image lockIcon;
        public etp2_hover lockHover;
        public Sprite lockOpenSprite;
        public Sprite lockClosedSprite;
        public TextMeshProUGUI ownerText;  // ヘッダー中央「XXさんが操作中」

        [Header("受け渡しのパネル")]
        public GameObject handoverRoot;
        public TextMeshProUGUI handoverText;
        public Button primaryButton;
        public TextMeshProUGUI primaryLabel;
        public Button secondaryButton;
        public TextMeshProUGUI secondaryLabel;
        public GameObject progressRoot;
        public RectTransform progressFill;
        public float progressWidth = 368f;

        private etp2_photo_frame _frame;
        private int[] _list;
        private int _pos;
        private bool _locked;
        private bool _swap;
        private int _scene;   // 0: なし / 1: 確認 / 2: 返事待ち / 3: 要求を受けた
        private float _sceneStart;
        private int _lastPost;

        private void Start()
        {
            if (handoverRoot != null) handoverRoot.SetActive(false);
            if (screen != null) screen.SetActive(false);
            ApplyLock();
            ApplySwap();
        }

        // ---- 開く・閉じる ----

        public void OpenFrame(etp2_photo_frame frame)
        {
            if (frame == null) return;
            _frame = frame;
            _list = frame.GetPostIndices();
            _pos = Mathf.Clamp(frame.localPos, 0, Mathf.Max(0, _list.Length - 1));
            if (screen != null) screen.SetActive(true);
            Show();
            // 開いている間: メニューモニターは左隣、詳細モニターは右隣(押した人の画面だけ)
            Transform t = transform;
            if (menu != null) menu.MoveBeside(t.TransformPoint(new Vector3(-sideOffset, 0f, 0f)), t.rotation);
            if (detail != null) detail.ShowBeside(t.TransformPoint(new Vector3(sideOffset, 0f, 0f)), t.rotation);
        }

        public void OnClose()
        {
            if (screen != null) screen.SetActive(false);
            if (tooltip != null) tooltip.Hide();
            SetScene(0);
            if (menu != null) menu.ReturnHome();
            if (detail != null) detail.Hide();
            Debug.Log("[Etp2Trace] viewer: close");
        }

        // ---- 前/次 ----

        public void OnLeft() { if (_swap) Step(1); else Step(-1); }
        public void OnRight() { if (_swap) Step(-1); else Step(1); }

        private void Step(int d)
        {
            if (_list == null || _list.Length == 0) return;
            _pos = (_pos + d + _list.Length) % _list.Length;
            Show();
        }

        // 詳細モニターの一覧から選んだ投稿を出す
        public void SelectPost(int post)
        {
            if (_list == null) return;
            for (int i = 0; i < _list.Length; i++)
            {
                if (_list[i] != post) continue;
                _pos = i;
                Show();
                return;
            }
        }

        private void Show()
        {
            if (_list == null || _list.Length == 0) return;
            if (_frame != null) { _frame.localPos = _pos; _frame.RefreshPhoto(); }
            int p = _list[_pos];
            _lastPost = p;
            if (photo != null && data != null && data.postColor != null && p < data.postColor.Length) photo.color = data.postColor[p];
            if (pageText != null) pageText.text = (_pos + 1) + " / " + _list.Length;
            Debug.Log("[Etp2Trace] viewer: post " + p + " (" + (_pos + 1) + "/" + _list.Length + ")");
            if (detail != null) detail.OnViewerChanged();
        }

        public int CurrentPost() { return _lastPost; }
        public int[] CurrentList() { return _list; }

        // 設定ページの「前の画像・次の画像のボタン」
        public void SetSwap(bool swap)
        {
            _swap = swap;
            ApplySwap();
            Debug.Log("[Etp2Trace] viewer: swap " + swap);
        }

        public bool IsSwapped() { return _swap; }

        private void ApplySwap()
        {
            // 矢印は置き場所の向き(左は ◀、右は ▶). 矢印はアイコンで、文字だけ入れ替える
            if (leftLabel != null) leftLabel.text = _swap ? "次の画像" : "前の画像";
            if (rightLabel != null) rightLabel.text = _swap ? "前の画像" : "次の画像";
        }

        // ---- 鍵(第1段階は見た目だけ) ----

        public void OnLock()
        {
            _locked = !_locked;
            ApplyLock();
            Debug.Log("[Etp2Trace] viewer: lock " + _locked);
        }

        private void ApplyLock()
        {
            if (lockIcon != null) lockIcon.sprite = _locked ? lockClosedSprite : lockOpenSprite;
            if (lockHover != null) lockHover.SetLabel(_locked ? "鍵を外す" : "鍵をかける");
            if (ownerText != null)
            {
                ownerText.gameObject.SetActive(_locked);
                VRCPlayerApi lp = Networking.LocalPlayer;
                string name = Utilities.IsValid(lp) ? lp.displayName : "XX";
                ownerText.text = name + "さんが操作中";
            }
            PaintLock();
        }

        public void OnThemeChanged()
        {
            PaintLock();
            SetScene(_scene);
        }

        private void PaintLock()
        {
            if (theme == null || !theme.ready) return;
            int style = _locked ? etp2_theme_target.StyleAccent : etp2_theme_target.StyleNormal;
            theme.PaintButton(lockButton, style);
            if (lockIcon != null) lockIcon.color = theme.LabelColor(style);
        }

        // ---- 受け渡しのパネル(第1段階はテストシーンのデバッグボタンから出す) ----

        public void DebugScene1() { if (screen != null) screen.SetActive(true); SetScene(1); }
        public void DebugScene2() { if (screen != null) screen.SetActive(true); SetScene(2); }
        public void DebugScene3() { if (screen != null) screen.SetActive(true); SetScene(3); }

        public void OnPrimary()
        {
            if (_scene == 1) SetScene(2);      // はい → 返事待ち
            else SetScene(0);                  // 譲る
        }

        public void OnSecondary()
        {
            SetScene(0);                       // やめる / 取り消す / 断る
        }

        private void SetScene(int scene)
        {
            _scene = scene;
            if (handoverRoot == null) return;
            handoverRoot.SetActive(scene != 0);
            if (scene == 0) return;
            _sceneStart = Time.time;
            bool timer = scene != 1;
            if (progressRoot != null) progressRoot.SetActive(timer);
            SetFill(0f);
            if (primaryButton != null) primaryButton.gameObject.SetActive(scene != 2);
            if (scene == 1)
            {
                handoverText.text = "XXさんが操作中です。\n操作を代わってもらいますか?";
                secondaryLabel.text = "やめる";
                primaryLabel.text = "はい";
            }
            else if (scene == 2)
            {
                handoverText.text = "XXさんの返事を待っています…";
                secondaryLabel.text = "取り消す";
            }
            else
            {
                handoverText.text = "YYさんが操作したいそうです。\n断らなければ交代します";
                secondaryLabel.text = "断る";
                primaryLabel.text = "譲る";
            }
            if (theme != null && theme.ready)
            {
                theme.PaintButton(primaryButton, etp2_theme_target.StyleAccent);
                theme.PaintButton(secondaryButton, etp2_theme_target.StyleNormal);
                primaryLabel.color = theme.cAccentText;
                secondaryLabel.color = theme.cText;
            }
            Debug.Log("[Etp2Trace] viewer: handover scene " + scene);
        }

        private void SetFill(float t)
        {
            if (progressFill != null) progressFill.sizeDelta = new Vector2(progressWidth * Mathf.Clamp01(t), progressFill.sizeDelta.y);
        }

        private void Update()
        {
            if (_scene != 2 && _scene != 3) return;
            float t = (Time.time - _sceneStart) / HandoverSeconds;
            SetFill(t);
            if (t >= 1f) SetScene(0);
        }
    }
}
