using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AliciliaEterPix.Udon
{
    /// <summary>
    /// 共有FlekSitステーション(座席_A〜F、鉄骨シート等すべて)から離席した際、
    /// 一瞬だけトラッキングリセット用Stationを使用・解除し、
    /// アバターのモーショントラッキング復帰を促す。
    /// Pole Dance(PoleDanceRespawnExit.cs)と同じ方式。
    /// 共有FlekSitステーションと同じGameObjectに追加すること。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class FlekSitTrackingResetOnExit : UdonSharpBehaviour
    {
        [Tooltip("離席時にトラッキング回復のため一瞬だけ使用するStation。animatorControllerに トラッキング_reset.controller を設定しておくこと。Pole Danceと共用でよい。")]
        [SerializeField]
        private VRCStation trackingResetStation;

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (!player.isLocal)
            {
                return;
            }

            if (trackingResetStation == null)
            {
                return;
            }

            // プレイヤーが今いる場所にリセット用Stationを移動してから一瞬だけ使用し、
            // すぐに解除する。トラッキング_reset.controller が一瞬だけ
            // Baseレイヤーへ適用されることでトラッキングが回復する。
            // FlekSit本体(SitLine/Move)には一切触れないため、
            // 次回の着席位置計算には影響しない。
            trackingResetStation.transform.SetPositionAndRotation(player.GetPosition(), player.GetRotation());
            trackingResetStation.UseStation(player);
            SendCustomEventDelayedFrames(nameof(ReleaseTrackingReset), 1);
        }

        public void ReleaseTrackingReset()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (trackingResetStation != null && localPlayer != null)
            {
                trackingResetStation.ExitStation(localPlayer);
            }
        }
    }
}
