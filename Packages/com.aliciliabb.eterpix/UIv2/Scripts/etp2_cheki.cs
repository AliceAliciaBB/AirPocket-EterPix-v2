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
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class etp2_cheki : UdonSharpBehaviour
    {
        public const float HoverHeight = 1.2f;      // 一度持たれたあと、離すと床からこの高さで止まる(仮)
        public const float RayLength = 50f;
        public const int PriorityOwner = 100;       // 持ち主の画面の写真(ビューアと同じ)
        public const int PriorityOthers = 10;       // ほかの人の画面の写真

        [Header("ワールド作者がつなぐ")]
        public etp2_mock_data data;
        public etp2_viewer viewer;
        [Tooltip("ほかの人のチェキにも写真を出す(その人のページ画像を取るので重くなる. 重さを見て決める)")]
        public bool showPhotoToOthers = false;
        [Tooltip("床を探す Raycast の対象")]
        public LayerMask floorLayers = 1;   // Default

        [Header("ビルダーが入れる")]
        public RawImage photo;      // 枠(写真が無いときは代わりの色)
        public RawImage photoImg;   // 枠の子の写真
        public GameObject body;     // 出るまでは隠す
        public VRCObjectSync objectSync;
        public VRC_Pickup pickup;   // 出るまでは持てない
        public Rigidbody body3d;

        [UdonSynced] private string _poid = "";
        [UdonSynced] private bool _shown;

        private etp2_photo_frame _frame;   // 持ち主だけが知っている(押したフォトフレーム)
        private int _post = -1;
        private int _heldPage = -1;
        private bool _touched;             // 出てから一度でも持たれたか
        private bool _falling;             // 離して落ちている(持ち主だけ)

        private void Start()
        {
            if (data != null) data.Listen(this);
            ApplyVisible();
        }

        // ---- 出す(持ち主がフォトフレームから呼ぶ) ----

        public void Spawn(etp2_photo_frame frame, int post, Vector3 position, Quaternion rotation)
        {
            if (!Networking.IsOwner(gameObject)) return;
            _frame = frame;
            _touched = false;
            _falling = false;
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
            if (Networking.IsOwner(gameObject)) RequestSerialization();
            Render();
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
            bool mine = Networking.IsOwner(gameObject);
            bool show = _shown && _post >= 0 && (mine || showPhotoToOthers);
            int page = show ? data.PhotoPageOf(_post) : -1;
            _heldPage = data.HoldPage(_heldPage, page, mine ? PriorityOwner : PriorityOthers);
            if (show) data.ShowPhoto(_post, photo, photoImg, false);
            else { photo.color = new Color(0.533f, 0.533f, 0.533f); if (photoImg != null) photoImg.gameObject.SetActive(false); }
        }

        // ---- 持つ・離す・Use ----

        public override void OnPickup()
        {
            _touched = true;
            _falling = false;
        }

        public override void OnDrop()
        {
            if (!_touched || !Networking.IsOwner(gameObject)) return;
            // 重力で落とし、床から HoverHeight で止める(Update で見る)
            if (objectSync != null) { objectSync.SetKinematic(false); objectSync.SetGravity(true); }
            _falling = true;
        }

        public override void OnPickupUseDown()
        {
            if (viewer == null || _post < 0) return;
            Debug.Log("[Etp2Trace] cheki: open viewer post " + _post);
            viewer.OpenFromCheki(this);
        }

        private void Update()
        {
            if (!_falling) return;
            Vector3 p = transform.position;
            if (!Physics.Raycast(p + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, RayLength, floorLayers, QueryTriggerInteraction.Ignore)) return;
            float stopY = hit.point.y + HoverHeight;
            if (p.y > stopY) return;
            _falling = false;
            if (objectSync != null) { objectSync.SetGravity(false); objectSync.SetKinematic(true); }
            if (body3d != null) body3d.velocity = Vector3.zero;
            transform.position = new Vector3(p.x, stopY, p.z);
            Debug.Log("[Etp2Trace] cheki: stop at " + stopY.ToString("F2"));
        }
    }
}
