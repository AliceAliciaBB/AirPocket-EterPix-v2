using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ali.eterpix.ui2
{
    // サブチェキ(ほかの人のチェキ)の周りのオーブ. 自分の画面だけに出る(同期しない).
    // VR: 手を入れて掴む = 自分のチェキで見る(仮のピックアップを掴み、自分のチェキがついて来る) / レーザーで選ぶ = 一緒に見る
    // デスクトップ: 左クリックで「一緒に見る」「自分のチェキで見る」を選ぶ(どちらも左クリックで区別できないため)
    // 決まり: 計画書 15章. このスクリプトは仮のピックアップ(Grab)に付ける. 見た目と UI は兄弟
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_cheki_orb : UdonSharpBehaviour
    {
        public const float HideNearDistance = 0.5f;   // 自分のチェキがこれより近ければ出さない(取り違えを防ぐ. 案 A)
        public const float OwnDistance = 0.25f;       // デスクトップの「自分のチェキで見る」: サブチェキから見る人の方へ出す距離
        public const float ChoiceSeconds = 5f;        // デスクトップの選択肢を出しておく時間

        [Header("ビルダーが入れる")]
        public etp2_cheki cheki;          // このオーブのサブチェキ(親)
        public GameObject visual;         // 半透明の球
        public Transform ui;              // 選ぶための Canvas(見る人の方へ向ける)
        public GameObject vrCaption;      // VR: 「一緒に見る」
        public GameObject choices;        // デスクトップ: 「一緒に見る」「自分のチェキで見る」
        public VRC_Pickup pickup;         // 仮のピックアップ(VR だけ)
        public Collider grabCollider;

        private bool _shown = true;
        private bool _holding;
        private float _choiceUntil;
        private Vector3 _homePos;
        private Quaternion _homeRot;

        private void Start()
        {
            _homePos = transform.localPosition;
            _homeRot = transform.localRotation;
            Apply(false);
        }

        private bool IsVR()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) && lp.IsUserInVR();
        }

        private etp2_cheki Mine()
        {
            return cheki != null && cheki.viewer != null ? cheki.viewer.Mine() : null;
        }

        private bool ShouldShow()
        {
            if (_holding) return true;
            if (cheki == null || !cheki.IsShown() || cheki.IsMine()) return false;
            etp2_cheki mine = Mine();
            if (mine != null && mine.IsShown() && (mine.transform.position - cheki.transform.position).magnitude < HideNearDistance) return false;
            return true;
        }

        private void Apply(bool show)
        {
            _shown = show;
            bool vr = IsVR();
            if (visual != null) visual.SetActive(show);
            if (ui != null) ui.gameObject.SetActive(show);
            if (vrCaption != null) vrCaption.SetActive(vr);
            if (choices != null) choices.SetActive(false);
            if (pickup != null) pickup.pickupable = show && vr;
            if (grabCollider != null) grabCollider.enabled = show && vr;
        }

        private void Update()
        {
            bool show = ShouldShow();
            if (show != _shown) Apply(show);
            if (!show) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (ui != null && Utilities.IsValid(lp))
            {
                Vector3 d = ui.position - lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                if (d.sqrMagnitude > 0.0001f) ui.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
            if (_holding)
            {
                etp2_cheki mine = Mine();
                if (mine != null) mine.FollowTo(transform.position, transform.rotation);
            }
            if (choices != null && choices.activeSelf && Time.time > _choiceUntil) choices.SetActive(false);
        }

        // ---- VR: 手を入れて掴む = 自分のチェキで見る ----

        public override void OnPickup()
        {
            etp2_cheki mine = Mine();
            if (mine == null) return;
            _holding = true;
            mine.TakeFrom(cheki, transform.position, transform.rotation);
            Debug.Log("[Etp2Trace] orb: grab (own cheki)");
        }

        public override void OnDrop()
        {
            if (!_holding) return;
            _holding = false;
            etp2_cheki mine = Mine();
            if (mine != null) { mine.FollowTo(transform.position, transform.rotation); mine.OnDrop(); }
            transform.localPosition = _homePos;
            transform.localRotation = _homeRot;
        }

        public override void OnPickupUseDown()
        {
            etp2_cheki mine = Mine();
            if (mine != null && cheki.viewer != null) cheki.viewer.OpenFromCheki(mine);
        }

        // ---- UI(VR はレーザー、デスクトップはクリック) ----

        // オーブ全体の見えないボタン
        public void OnOrb()
        {
            if (IsVR()) { OnTogether(); return; }
            if (choices == null) return;
            choices.SetActive(true);
            _choiceUntil = Time.time + ChoiceSeconds;
        }

        public void OnTogether()
        {
            if (choices != null) choices.SetActive(false);
            if (cheki == null || cheki.viewer == null) return;
            Debug.Log("[Etp2Trace] orb: together with player " + cheki.OwnerId());
            cheki.viewer.JoinFrom(cheki);
        }

        // デスクトップ: 自分のチェキをサブチェキの手前(見る人の方)に出す. 次の左クリックで掴める
        public void OnOwn()
        {
            if (choices != null) choices.SetActive(false);
            etp2_cheki mine = Mine();
            if (mine == null || cheki == null) return;
            Vector3 c = cheki.transform.position;
            Vector3 dir = Vector3.back;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp)) dir = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position - c;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.back;
            dir = dir.normalized;   // Udon では Normalize() が元の値を変えない
            // チェキの表はフォトフレームと同じく -forward 側. 見る人の方へ表を向ける
            mine.TakeFrom(cheki, c + dir * OwnDistance, Quaternion.LookRotation(-dir, Vector3.up));
            Debug.Log("[Etp2Trace] orb: own cheki (desktop)");
        }
    }
}
