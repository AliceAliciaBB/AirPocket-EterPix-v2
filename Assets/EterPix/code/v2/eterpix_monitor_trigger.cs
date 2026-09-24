using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ali.eterpix.v2
{
    // v2下位: モニターの表示範囲トリガー(中継)。
    // OnPlayerTriggerEnter/ExitはColliderと同じGameObjectにしか届かない。
    // モニターのルートはVRCUiShapeがBoxColliderをUI操作用に管理するため、
    // 表示範囲のトリガーColliderは子に分け、そこにこのスクリプトを付けて
    // 親のeterpix_monitorへローカルプレイヤーの出入りを中継する。
    // 参照はeterpix_monitorがGetComponentsInChildren(true)で集めてSetMonitor()で渡す。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class eterpix_monitor_trigger : UdonSharpBehaviour
    {
        private eterpix_monitor _monitor;

        public void SetMonitor(eterpix_monitor monitor)
        {
            _monitor = monitor;
        }

        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (_monitor == null || !Utilities.IsValid(player) || !player.isLocal) return;
            _monitor.OnViewRangeEnter();
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (_monitor == null || !Utilities.IsValid(player) || !player.isLocal) return;
            _monitor.OnViewRangeExit();
        }
    }
}
