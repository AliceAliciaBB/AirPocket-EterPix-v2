using UdonSharp;
using UnityEngine;

namespace ali.eterpix.ui2
{
    // フォトフレームを開いたときに、ビューアを置く位置と向き(フォトフレームの子 ViewerAnchor に付ける).
    // 中身は無い. [SelectionBase] で、シーンで凡例の黒い板をクリックしたときにこのオブジェクトが選ばれるようにする
    [SelectionBase]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_viewer_anchor : UdonSharpBehaviour
    {
    }
}
