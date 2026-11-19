using UdonSharp;
using UnityEngine;

namespace ali.eterpix.ui2
{
    // オーブの球を押す(デスクトップ). 押すと球の上に「一緒に見る」「自分のチェキで見る」が出る.
    // 球の当たり判定に付ける. VR では使わない(オーブ側が当たり判定ごと止める). 決まり: 計画書 15章
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_cheki_orb_click : UdonSharpBehaviour
    {
        [Header("ビルダーが入れる")]
        public etp2_cheki_orb orb;

        public override void Interact()
        {
            if (orb != null) orb.OnOrb();
        }
    }
}
