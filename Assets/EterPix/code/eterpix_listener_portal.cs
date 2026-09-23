using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;

namespace ali.eterpix
{
    // リスナー部品(共通、Aタイプ): ポータル発生。方針.md「ポータルコード指定Aタイプ」参照。
    // world_idを受けてVRCPortalMarker/PortalStand/ポータル本体(eterpix_porta_resize)に反映する
    // (既存eterpix_photo_get.des_portal_view相当)。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_listener_portal : UdonSharpBehaviour
    {
        [SerializeField] private VRCPortalMarker portalMarker;
        [SerializeField] private PortalStand portalStand;
        [SerializeField] private eterpix_porta_resize portalResize;

        public eterpix_porta_resize GetPortalResize()
        {
            return portalResize;
        }

        public void ApplyPortal(string worldId)
        {
            if (string.IsNullOrEmpty(worldId))
            {
                if (portalMarker != null) portalMarker.gameObject.SetActive(false);
                return;
            }

            if (portalMarker != null)
            {
                portalMarker.roomId = worldId;
                portalMarker.gameObject.SetActive(true);
                portalMarker.RefreshPortal();
            }

            if (portalStand != null)
            {
                portalStand.SetTargetRoomId(worldId);
            }
        }
    }
}
