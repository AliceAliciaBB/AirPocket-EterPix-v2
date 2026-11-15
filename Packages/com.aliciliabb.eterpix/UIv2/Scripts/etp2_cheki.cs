using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace ali.eterpix.ui2
{
    // チェキ(仮). フォトフレームを押すと自分のチェキがその前に出て、その投稿の写真になる.
    // 持って Use するとビューアがチェキの前に開く. 1 人 1 枚(VRCPlayerObject). 位置と投稿(poid)はみんなに同期する.
    // 決まり: 計画書 14章(2026-11-15 ユーザー依頼). 見た目は仮(写真だけ)
    // 同期は Continuous: VRCObjectSync と同じ GameObject の Udon は Manual にできない(VRChat のビルドの決まり)
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
        public VRC_Pickup pickup;   // 出るまでは持てない

        [UdonSynced] private string _poid = "";
        [UdonSynced] private bool _shown;

        private etp2_photo_frame _frame;   // 持ち主だけが知っている(押したフォトフレーム)
        private int _post = -1;
        private int _heldPage = -1;

        private void Start()
        {
            if (data != null) data.Listen(this);
            ApplyVisible();
        }

        // ---- 出す(持ち主がフォトフレームから呼ぶ) ----

        public void Spawn(etp2_photo_frame frame, int post, Vector3 position, Quaternion rotation)
        {
            if (!IsMine()) return;
            _frame = frame;
            if (objectSync != null) { objectSync.SetKinematic(true); objectSync.SetGravity(false); }
            transform.SetPositionAndRotation(position, rotation);
            if (objectSync != null) objectSync.FlagDiscontinuity();
            _shown = true;
            SetPost(post);
            Debug.Log("[Etp2Trace] cheki: spawn post " + post + " at " + position);
        }

        // ビューアの前/次. 写真と同期する投稿を変える(フォトフレームは変えない)
        public void SetPost(int post)
        {
            _post = post;
            _poid = data != null ? data.PostIdOf(post) : "";
            Render();   // Continuous なので送るのは自動
        }

        // 部屋を出る途中などで自分のプレイヤーが無いときに IsOwner を呼ぶと止まるので確かめる
        private bool IsMine()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) && Utilities.IsValid(gameObject) && lp.IsOwner(gameObject);
        }

        public int CurrentPost() { return _post; }
        public etp2_photo_frame Frame() { return _frame; }

        public override void OnDeserialization()
        {
            if (data != null) _post = data.FindPost(_poid);
            ApplyVisible();
            Render();
        }

        public void OnDataChanged()
        {
            if (data != null && !string.IsNullOrEmpty(_poid)) _post = data.FindPost(_poid);
            Render();
        }

        private void ApplyVisible()
        {
            if (body != null) body.SetActive(_shown);
            if (pickup != null) pickup.pickupable = _shown;
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
