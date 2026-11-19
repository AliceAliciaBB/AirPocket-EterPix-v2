using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace ali.eterpix.ui2
{
    // チェキ(仮). フォトフレームを押すと自分のチェキがその前に出て、その投稿の写真になる.
    // 持って Use するとビューアがチェキの前に開く. 1 人 1 枚(VRCPlayerObject). 位置と投稿(poid)はみんなに同期する.
    // セッション(何人かで同じ投稿を見る)の値も自分のチェキに載せる. 計算はビューアがする(計画書 15章)
    // 決まり: 計画書 14章・15章. 見た目は仮(写真だけ)
    // 位置は VRCObjectSync. 投稿とセッションの値は子の etp2_cheki_session(Manual)で送る.
    // ここ(VRCObjectSync と同じ GameObject)は Manual にできず、Continuous は置いたままだと送られないため(2026-11-19 の直し)
    [UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
    public class etp2_cheki : UdonSharpBehaviour
    {
        public const int PriorityOwner = 100;       // 持ち主の画面の写真(ビューアと同じ)
        public const int PriorityOthers = 10;       // ほかの人の画面の写真

        [Header("ワールド作者がつなぐ")]
        public etp2_mock_data data;
        public etp2_viewer viewer;
        [Tooltip("ほかの人のチェキにも写真を出す(その人のページ画像を取るので重くなる. 重さを見て決める)")]
        public bool showPhotoToOthers = false;

        [Header("ビルダーが入れる")]
        public RawImage photo;      // 枠(写真が無いときは代わりの色)
        public RawImage photoImg;   // 枠の子の写真
        public GameObject body;     // 出るまでは隠す
        public VRCObjectSync objectSync;
        public VRC_Pickup pickup;   // 出るまでは持てない. 持てるのは持ち主だけ
        public etp2_cheki_session sync;   // 投稿とセッションの値を送る(子. Manual)

        // 同期する値の手元の写し. 書くのは持ち主だけで、書いたら Push() で送る. 受け取りは OnSessionData()
        private string _poid = "";
        private bool _shown;
        private Vector3 _framePos;     // 押したフォトフレームの位置(ほかの人は一番近いフォトフレームを探す)
        private int _session = -1;     // 入っているセッションの番号. -1 は入っていない
        private int _joinedAt;         // セッションに入ったサーバー時刻(いちばん早い人がホスト)
        private int _poidAt;           // 投稿を変えたサーバー時刻(いちばん新しい人の投稿を出す)
        private int _lockAt;           // 鍵をかけたサーバー時刻. 0 はかけていない
        private int _hint = -1;        // セッションに入っていない人のチェキに入るとき、その人の playerId

        private etp2_photo_frame _frame;
        private int _post = -1;
        private int _heldPage = -1;
        // 前に受け取った値(変わったときだけビューアに知らせる)
        private string _lastPoid = "";
        private int _lastSession = -1, _lastPoidAt, _lastLockAt, _lastHint = -1;

        private void Start()
        {
            if (data != null) data.Listen(this);
            if (viewer != null) viewer.RegisterCheki(this);
            ApplyVisible();
        }

        // ---- 出す(持ち主がフォトフレームから呼ぶ) ----

        public void Spawn(etp2_photo_frame frame, int post, Vector3 position, Quaternion rotation)
        {
            if (!IsMine()) return;
            LeaveSession();   // 別の写真に使った = セッションを抜ける(15章)
            _frame = frame;
            _framePos = frame != null ? frame.transform.position : Vector3.zero;
            PlaceAt(position, rotation);
            _shown = true;
            SetPost(post);   // SetPost が送る
            Debug.Log("[Etp2Trace] cheki: spawn post " + post + " at " + position);
        }

        // サブチェキのオーブの「自分のチェキで見る」. その投稿を自分のチェキに出す. セッションは共有しない(15章)
        public void TakeFrom(etp2_cheki other, Vector3 position, Quaternion rotation)
        {
            if (!IsMine() || other == null) return;
            LeaveSession();
            _framePos = other.FramePos();
            _frame = data != null ? data.FindFrameNear(_framePos) : null;
            PlaceAt(position, rotation);
            _shown = true;
            SetPostId(other.Poid());
            Debug.Log("[Etp2Trace] cheki: take post " + _post + " from player " + other.OwnerId());
        }

        // オーブの仮のピックアップを持っている間、ついて行く
        public void FollowTo(Vector3 position, Quaternion rotation)
        {
            if (!IsMine()) return;
            transform.SetPositionAndRotation(position, rotation);
        }

        private void PlaceAt(Vector3 position, Quaternion rotation)
        {
            if (objectSync != null) { objectSync.SetKinematic(true); objectSync.SetGravity(false); }
            transform.SetPositionAndRotation(position, rotation);
            if (objectSync != null) objectSync.FlagDiscontinuity();
        }

        // ビューアの前/次. 写真と同期する投稿を変える(フォトフレームは変えない)
        public void SetPost(int post)
        {
            if (!IsMine()) return;
            string poid = data != null ? data.PostIdOf(post) : "";
            if (poid != _poid) _poidAt = Now();   // 変わったときだけ時刻を進める(15章)
            _poid = poid;
            _post = post;
            Push();
            Render();
        }

        private void SetPostId(string poid)
        {
            if (poid != _poid) _poidAt = Now();
            _poid = poid;
            _post = data != null ? data.FindPost(poid) : -1;
            Push();
            Render();
        }

        // ---- セッション(15章. 書くのは自分のチェキだけ) ----

        // other のセッションに入る. other がセッションに入っていなければ新しく作り、other の持ち主に頼む(_hint)
        public void JoinSessionOf(etp2_cheki other)
        {
            if (!IsMine() || other == null) return;
            int key = other.SessionKey();
            if (key < 0)
            {
                key = NewKey();
                _hint = other.OwnerId();
            }
            else _hint = -1;
            _session = key;
            _joinedAt = Now();
            _lockAt = 0;
            _poid = other.Poid();
            _poidAt = other.PoidAt();
            _post = data != null ? data.FindPost(_poid) : -1;
            _framePos = other.FramePos();
            _frame = data != null ? data.FindFrameNear(_framePos) : null;
            Push();
            Render();
            Debug.Log("[Etp2Trace] cheki: join session " + key + " of player " + other.OwnerId() + (_hint >= 0 ? " (new)" : ""));
        }

        // 誰かが自分のチェキに入った(その人の _hint が自分). まだセッションに入っていなければホストになる
        public void Adopt(int key, int joinerAt)
        {
            if (!IsMine() || _session >= 0) return;
            _session = key;
            _joinedAt = joinerAt - 1;   // 入ってきた人より早く入ったことにする
            _hint = -1;
            Push();
            Debug.Log("[Etp2Trace] cheki: host of session " + key);
        }

        public void LeaveSession()
        {
            if (!IsMine()) return;
            if (_session >= 0) Debug.Log("[Etp2Trace] cheki: leave session " + _session);
            bool changed = _session != -1 || _hint != -1 || _lockAt != 0;
            _session = -1;
            _hint = -1;
            _lockAt = 0;
            if (changed) Push();
        }

        public void ClearHint() { if (IsMine() && _hint != -1) { _hint = -1; Push(); } }

        // 同じセッションの人がもっと新しい投稿を出した. 自分のチェキもそれに合わせる(時刻も同じにする)
        public void FollowPost(string poid, int at)
        {
            if (!IsMine() || poid == _poid) return;
            _poid = poid;
            _poidAt = at;
            _post = data != null ? data.FindPost(poid) : -1;
            Push();
            Render();
        }

        public void SetLock(bool on)
        {
            if (!IsMine()) return;
            _lockAt = on ? Mathf.Max(1, Now()) : 0;
            Push();
        }

        // 手元の値を子の同期に写して送る(持ち主だけ)
        private void Push()
        {
            if (!IsMine() || sync == null) return;
            sync.poid = _poid;
            sync.shown = _shown;
            sync.framePos = _framePos;
            sync.session = _session;
            sync.joinedAt = _joinedAt;
            sync.poidAt = _poidAt;
            sync.lockAt = _lockAt;
            sync.hint = _hint;
            sync.RequestSerialization();
        }

        private int Now() { return Networking.GetServerTimeInMilliseconds(); }

        private int NewKey()
        {
            int k = (Now() & 0x00FFFFFF) * 64 + (OwnerId() & 63);
            return k < 0 ? -k : k;
        }

        // 部屋を出る途中などで自分のプレイヤーが無いときに IsOwner を呼ぶと止まるので確かめる
        public bool IsMine()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) && Utilities.IsValid(gameObject) && lp.IsOwner(gameObject);
        }

        public int OwnerId()
        {
            VRCPlayerApi o = Networking.GetOwner(gameObject);
            return Utilities.IsValid(o) ? o.playerId : -1;
        }

        public string OwnerName()
        {
            VRCPlayerApi o = Networking.GetOwner(gameObject);
            return Utilities.IsValid(o) ? o.displayName : "";
        }

        public int CurrentPost() { return _post; }
        public string Poid() { return _poid; }
        public int PoidAt() { return _poidAt; }
        public int SessionKey() { return _session; }
        public int JoinedAt() { return _joinedAt; }
        public int LockAt() { return _lockAt; }
        public int Hint() { return _hint; }
        public bool IsShown() { return _shown; }
        public Vector3 FramePos() { return _framePos; }

        // 押したフォトフレーム. ほかの人のチェキは、同期した位置から一番近いものを探す
        public etp2_photo_frame Frame()
        {
            if (_frame == null && data != null && !string.IsNullOrEmpty(_poid)) _frame = data.FindFrameNear(_framePos);
            return _frame;
        }

        // 子の同期が値を受け取った(ほかの人のチェキ)
        public void OnSessionData()
        {
            if (sync == null || IsMine()) return;
            _poid = sync.poid;
            _shown = sync.shown;
            _framePos = sync.framePos;
            _session = sync.session;
            _joinedAt = sync.joinedAt;
            _poidAt = sync.poidAt;
            _lockAt = sync.lockAt;
            _hint = sync.hint;
            if (data != null) _post = data.FindPost(_poid);
            ApplyVisible();
            Render();
            bool changed = _poid != _lastPoid || _session != _lastSession || _poidAt != _lastPoidAt || _lockAt != _lastLockAt || _hint != _lastHint;
            if (!changed) return;
            _lastPoid = _poid; _lastSession = _session; _lastPoidAt = _poidAt; _lastLockAt = _lockAt; _lastHint = _hint;
            _frame = null;   // フォトフレームを探し直す
            if (viewer == null) return;
            // 自分のチェキに入ってきた人がいる(15章 頼みの印)
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (_hint >= 0 && _session >= 0 && Utilities.IsValid(lp) && lp.playerId == _hint) viewer.AdoptRequest(_session, _joinedAt);
            viewer.OnSessionChanged();
        }

        public void OnDataChanged()
        {
            if (data != null && !string.IsNullOrEmpty(_poid)) _post = data.FindPost(_poid);
            Render();
        }

        private void ApplyVisible()
        {
            if (body != null) body.SetActive(_shown);
            if (pickup != null) pickup.pickupable = _shown && IsMine();   // ほかの人のチェキは持てない(15章)
        }

        private void Render()
        {
            ApplyVisible();
            if (data == null || photo == null) return;
            bool mine = IsMine();
            bool show = _shown && _post >= 0 && (mine || showPhotoToOthers);
            int page = show ? data.PhotoPageOf(_post) : -1;
            _heldPage = data.HoldPage(_heldPage, page, mine ? PriorityOwner : PriorityOthers);
            if (show) data.ShowPhoto(_post, photo, photoImg, false);
            else { photo.color = new Color(0.533f, 0.533f, 0.533f); if (photoImg != null) photoImg.gameObject.SetActive(false); }
        }

        // ---- 持つ・離す・Use ----

        // 離したところにそのまま残る(落ちない. 2026-11-15 ユーザー指示で 1.2m の停止はやめた)
        public override void OnDrop()
        {
            if (!IsMine() || objectSync == null) return;
            objectSync.SetGravity(false);
            objectSync.SetKinematic(true);
        }

        public override void OnPickupUseDown()
        {
            if (viewer == null || _post < 0) return;
            Debug.Log("[Etp2Trace] cheki: open viewer post " + _post);
            viewer.OpenFromCheki(this);
        }
    }
}
