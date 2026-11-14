using UdonSharp;
using UnityEngine;

namespace ali.eterpix.ui2
{
    // U# のボタンは押したときに引数を渡せないので、行ごとに番号を持たせる.
    // 押されたら送り先の public int pressedIndex に番号を入れてから、eventName を送る
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_index_button : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;
        public string eventName;
        public int index;

        public void Press()
        {
            if (target == null) return;
            target.SetProgramVariable("pressedIndex", index);
            target.SendCustomEvent(eventName);
        }
    }
}
