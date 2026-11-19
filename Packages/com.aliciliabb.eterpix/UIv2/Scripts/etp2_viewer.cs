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
        public RawImage photo;             // 枠(写真が無いときは代わりの色)
        public RawImage photoImg;          // 枠の子の写真(API のとき)
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
        private etp2_cheki _cheki;     // チェキから開いたとき(開いた場所の元). 前/次は自分のチェキの投稿を変える(フォトフレームは変えない)

        // ---- セッション(計画書 15章. 計算は手元. 値はそれぞれのチェキに同期してある) ----
        public const float HostLeaveDistance = 1f;   // ホストのチェキがセッションの場所からこれより離れたら、ホストは抜ける(承認 2026-11-19)
        public const float RecalcSeconds = 0.25f;
        private etp2_cheki[] _chekis = new etp2_cheki[0];
        private etp2_cheki _mine;
        private etp2_cheki _lockHolder;   // 鍵をかけている人のチェキ. 無ければ null
        private bool _isHost;
        private Vector3 _anchor;
        private float _nextRecalc;
        private int[] _list;
        private int _pos;
        private bool _locked;
        private bool _swap;
        private int _scene;   // 0: なし / 1: 確認 / 2: 返事待ち / 3: 要求を受けた
        private float _sceneStart;
        private int _lastPost;
        private int _heldPhoto = -1;   // 頼んでいるページ画像(写真・アイコン)
        private int _heldIcon = -1;

        private void Start()
        {
            if (handoverRoot != null) handoverRoot.SetActive(false);
            if (screen != null) screen.SetActive(false);
            ApplyLock();
            ApplySwap();
            if (data != null) data.Listen(this);
        }

        // データ(API)が読めた・ページ画像が届いた
        public void OnDataChanged()
        {
            if (_frame == null && _cheki == null) return;
            _list = ListOf(_frame);
            _pos = Mathf.Clamp(_pos, 0, Mathf.Max(0, _list.Length - 1));
            if (screen != null && screen.activeSelf) Show();
        }

        // ---- 開く・閉じる ----

        // ビューアのルートは画面の上端の中央. 画面の中心からルートまで(ルートの座標で 336 = 高さ 672 の半分)
        public float centerToRoot = 336f;
        // チェキの前に開くとき、チェキから奥へ離す距離(m)
        public float chekiDistance = 0.4f;

        public void OpenFrame(etp2_photo_frame frame)
        {
            if (frame == null) return;
            // フォトフレームの前(凡例で決めた位置)へ移る
            if (frame.viewerAnchor != null) transform.SetPositionAndRotation(frame.viewerAnchor.position, frame.viewerAnchor.rotation);
            Open(frame, null, frame.localPos);
        }

        // チェキを持って Use した. 見る人の頭からチェキへの向きで、チェキの奥に開く(計画書 14章)
        public void OpenFromCheki(etp2_cheki cheki)
        {
            if (cheki == null) return;
            Vector3 c = cheki.transform.position;
            Vector3 head = c - Vector3.forward;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp)) head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 dir = c - head;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir = dir.normalized;   // Udon では Normalize() が元の値を変えない
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 center = c + dir * chekiDistance;
            transform.SetPositionAndRotation(center + rot * new Vector3(0f, centerToRoot * transform.lossyScale.y, 0f), rot);
            etp2_photo_frame frame = cheki.Frame();
            int[] list = ListOf(frame);
            int pos = 0;
            for (int i = 0; i < list.Length; i++) if (list[i] == cheki.CurrentPost()) { pos = i; break; }
            Open(frame, cheki, pos);
        }

        // フォトフレームの並び. フォトフレームが分からないとき(探せなかった)は全部の投稿
        private int[] ListOf(etp2_photo_frame frame)
        {
            if (frame != null) return frame.GetPostIndices();
            int n = data == null ? 0 : data.PostCount();
            int[] all = new int[n];
            for (int i = 0; i < n; i++) all[i] = i;
            return all;
        }

        // 開いた直後の前/次は受け付けない. デスクトップでは開いた左クリック(チェキの Use)が、
        // そのまま目の前に出たビューアの「前へ」に当たり、1 つ戻ってしまう(最初の写真なら一番古い写真へ回る. 2026-11-19 ユーザー報告)
        public const float OpenGuardSeconds = 0.5f;
        private float _openedAt = -10f;

        private void Open(etp2_photo_frame frame, etp2_cheki cheki, int pos)
        {
            _openedAt = Time.time;
            _frame = frame;
            _cheki = cheki;
            _list = ListOf(frame);
            _pos = Mathf.Clamp(pos, 0, Mathf.Max(0, _list.Length - 1));
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
            // ビューアを閉じた = セッションを抜ける(15章)
            if (_cheki != null && Mine() != null && Mine().SessionKey() >= 0) { Mine().LeaveSession(); Recalc(); }
            _cheki = null;
            if (data != null)
            {
                _heldPhoto = data.HoldPage(_heldPhoto, -1, 0);
                _heldIcon = data.HoldPage(_heldIcon, -1, 0);
            }
            SetScene(0);
            if (menu != null) menu.ReturnHome();
            if (detail != null) detail.Hide();
            Debug.Log("[Etp2Trace] viewer: close");
        }

        // ---- 前/次 ----

        public void OnLeft() { if (JustOpened()) return; if (_swap) Step(1); else Step(-1); }
        public void OnRight() { if (JustOpened()) return; if (_swap) Step(-1); else Step(1); }

        private bool JustOpened()
        {
            if (Time.time - _openedAt >= OpenGuardSeconds) return false;
            Debug.Log("[Etp2Trace] viewer: ignore prev/next just after open");
            return true;
        }

        private void Step(int d)
        {
            if (_list == null || _list.Length == 0 || LockedByOther()) return;
            _pos = (_pos + d + _list.Length) % _list.Length;
            Show();
        }

        // 詳細モニターの一覧から選んだ投稿を出す
        public void SelectPost(int post)
        {
            if (_list == null || LockedByOther()) return;
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
            int p = _list[_pos];
            _lastPost = p;
            if (data != null)
            {
                // フォトフレームより先に頼む(ビューアの優先度で取らせる)
                _heldPhoto = data.HoldPage(_heldPhoto, data.PhotoPageOf(p), etp2_api_loader.PriorityViewer);
                _heldIcon = data.HoldPage(_heldIcon, data.IconPageOf(p), etp2_api_loader.PriorityViewer);   // 詳細モニターのアイコン
                data.ShowPhoto(p, photo, photoImg, false);
            }
            if (_cheki != null) { if (Mine() != null) Mine().SetPost(p); }   // フォトフレームは変えない(計画書 14章). 書くのは自分のチェキ(15章)
            else if (_frame != null) { _frame.localPos = _pos; _frame.RefreshPhoto(); }
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

        // ---- 鍵(15章: かけた人だけが前/次を押せる. かけていなければ全員. 誰でもかけられる) ----

        public void OnLock()
        {
            etp2_cheki mine = Mine();
            if (mine == null) return;
            if (_lockHolder == null) mine.SetLock(true);
            else if (_lockHolder == mine) mine.SetLock(false);
            else return;   // ほかの人がかけている
            Recalc();
            Debug.Log("[Etp2Trace] viewer: lock " + (_lockHolder != null));
        }

        private bool LockedByOther()
        {
            return _cheki != null && _lockHolder != null && _lockHolder != Mine();
        }

        private void ApplyLock()
        {
            _locked = _lockHolder != null;
            if (lockIcon != null) lockIcon.sprite = _locked ? lockClosedSprite : lockOpenSprite;
            if (lockHover != null) lockHover.SetLabel(!_locked ? "鍵をかける" : (_lockHolder == Mine() ? "鍵を外す" : "鍵がかかっています"));
            if (ownerText != null)
            {
                ownerText.gameObject.SetActive(_locked);
                if (_locked) ownerText.text = _lockHolder.OwnerName() + "さんが操作中";
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

        // ---- セッションの計算(15章) ----

        public void RegisterCheki(etp2_cheki c)
        {
            if (c == null) return;
            for (int i = 0; i < _chekis.Length; i++) if (_chekis[i] == c) return;
            int k = 0;
            for (int i = 0; i < _chekis.Length; i++) if (Utilities.IsValid(_chekis[i])) k++;
            etp2_cheki[] next = new etp2_cheki[k + 1];
            k = 0;
            for (int i = 0; i < _chekis.Length; i++) if (Utilities.IsValid(_chekis[i])) next[k++] = _chekis[i];   // 抜けた人の分は詰める
            next[k] = c;
            _chekis = next;
        }

        public etp2_cheki Mine()
        {
            if (Utilities.IsValid(_mine)) return _mine;
            _mine = null;
            for (int i = 0; i < _chekis.Length; i++) if (Utilities.IsValid(_chekis[i]) && _chekis[i].IsMine()) { _mine = _chekis[i]; break; }
            return _mine;
        }

        // サブチェキのオーブの「一緒に見る」
        public void JoinFrom(etp2_cheki other)
        {
            etp2_cheki mine = Mine();
            if (mine == null || other == null || other == mine) return;
            if (mine.SessionKey() < 0 || mine.SessionKey() != other.SessionKey())
            {
                mine.LeaveSession();
                mine.JoinSessionOf(other);
                Recalc();
            }
            OpenFromCheki(other);
        }

        // 自分のチェキに誰かが入ってきた(その人のチェキの _hint が自分)
        public void AdoptRequest(int key, int joinerAt)
        {
            etp2_cheki mine = Mine();
            if (mine == null || mine.SessionKey() >= 0) return;
            mine.Adopt(key, joinerAt);
            Recalc();
        }

        public void OnSessionChanged() { Recalc(); }

        public bool IsHost() { return _isHost; }

        // 同じセッションの人から、ホスト・出す投稿・鍵をかけた人を決める. 全員が同じ規則で手元で決める
        private void Recalc()
        {
            etp2_cheki mine = Mine();
            int key = mine != null ? mine.SessionKey() : -1;
            etp2_cheki host = null, latest = null, holder = null;
            if (key >= 0)
            {
                for (int i = 0; i < _chekis.Length; i++)
                {
                    etp2_cheki c = _chekis[i];
                    if (!Utilities.IsValid(c) || c.SessionKey() != key) continue;
                    if (host == null || c.JoinedAt() < host.JoinedAt()) host = c;
                    if (latest == null || c.PoidAt() > latest.PoidAt()) latest = c;
                    if (c.LockAt() > 0 && (holder == null || c.LockAt() < holder.LockAt())) holder = c;
                    if (mine.Hint() >= 0 && c != mine && c.OwnerId() == mine.Hint()) mine.ClearHint();   // 頼んだ人がホストになった
                }
            }
            else if (mine != null && mine.LockAt() > 0) holder = mine;   // 1 人でも鍵はかけておける
            bool wasHost = _isHost;
            _isHost = host != null && host == mine;
            if (_isHost && !wasHost) { _anchor = mine.transform.position; Debug.Log("[Etp2Trace] viewer: host of session " + key); }
            if (latest != null && latest != mine && latest.Poid() != mine.Poid()) mine.FollowPost(latest.Poid(), latest.PoidAt());
            bool lockChanged = holder != _lockHolder;
            _lockHolder = holder;
            if (lockChanged) ApplyLock();
            // 開いていれば、自分のチェキの投稿(セッションの投稿)に合わせる
            if (_cheki != null && mine != null && screen != null && screen.activeSelf && _list != null && _list.Length > 0)
            {
                int p = mine.CurrentPost();
                if (p >= 0 && _list[Mathf.Clamp(_pos, 0, _list.Length - 1)] != p)
                {
                    for (int i = 0; i < _list.Length; i++) if (_list[i] == p) { _pos = i; Show(); break; }
                }
            }
        }

        private void Update()
        {
            etp2_cheki mine = Mine();
            if (mine != null && (mine.SessionKey() >= 0 || _lockHolder != null) && Time.time >= _nextRecalc)
            {
                _nextRecalc = Time.time + RecalcSeconds;
                Recalc();   // 抜けた人の分も拾う
                // ホストのチェキがセッションの場所から離れたら、ホストは抜ける(次の人へ引き継がれる)
                if (_isHost && mine.SessionKey() >= 0 && (mine.transform.position - _anchor).magnitude > HostLeaveDistance)
                {
                    Debug.Log("[Etp2Trace] viewer: host moved away, leave session");
                    mine.LeaveSession();
                    Recalc();
                }
            }
            UpdateHandover();
        }

        private void UpdateHandover()
        {
            if (_scene != 2 && _scene != 3) return;
            float t = (Time.time - _sceneStart) / HandoverSeconds;
            SetFill(t);
            if (t >= 1f) SetScene(0);
        }
    }
}
