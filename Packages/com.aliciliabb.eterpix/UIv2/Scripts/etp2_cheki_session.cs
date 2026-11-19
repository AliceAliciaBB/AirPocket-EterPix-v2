using UdonSharp;
using UnityEngine;

namespace ali.eterpix.ui2
{
    // チェキの投稿とセッションの値を同期する(チェキの子. 持ち主はチェキと同じ人. VRCPlayerObject の子).
    // チェキ本体は VRCObjectSync があるので Continuous しか使えず、置いたままだと値が送られない.
    // ここは Manual にして、変えたときに必ず送る(計画書 15章 2026-11-19 の直し)
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class etp2_cheki_session : UdonSharpBehaviour
    {
        [Header("ビルダーが入れる")]
        public etp2_cheki cheki;

        [UdonSynced] public string poid = "";
        [UdonSynced] public bool shown;
        [UdonSynced] public Vector3 framePos;   // 押したフォトフレームの位置(ほかの人は一番近いフォトフレームを探す)
        [UdonSynced] public int session = -1;   // 入っているセッションの番号. -1 は入っていない
        [UdonSynced] public int joinedAt;       // セッションに入ったサーバー時刻(いちばん早い人がホスト)
        [UdonSynced] public int poidAt;         // 投稿を変えたサーバー時刻(いちばん新しい人の投稿を出す)
        [UdonSynced] public int lockAt;         // 鍵をかけたサーバー時刻. 0 はかけていない
        [UdonSynced] public int hint = -1;      // セッションに入っていない人のチェキに入るとき、その人の playerId

        public override void OnDeserialization()
        {
            if (cheki != null) cheki.OnSessionData();
        }
    }
}
