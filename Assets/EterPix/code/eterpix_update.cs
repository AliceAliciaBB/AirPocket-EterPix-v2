using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ali.eterpix
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_update : UdonSharpBehaviour
    {
        [SerializeField] private eterpix_get_api eterpix_get_api;
        [SerializeField] private int intervalMinutes = 10;
        [SerializeField] private int offsetMinutes = 1;

        private void Start()
        {
            eterpix_get_api.UpdateItems();
            ScheduleNextUpdate();
        }

        private void ScheduleNextUpdate()
        {
            System.DateTime now = System.DateTime.Now;
            System.DateTime candidate = new System.DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            while (candidate <= now || ((candidate.Minute - offsetMinutes) % intervalMinutes + intervalMinutes) % intervalMinutes != 0)
            {
                candidate = candidate.AddMinutes(1);
            }

            float delay = (float)(candidate - now).TotalSeconds;
            SendCustomEventDelayedSeconds(nameof(TriggerUpdate), delay);
        }

        public void TriggerUpdate()
        {
            if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(UpdateContainers));
            }

            ScheduleNextUpdate();
        }

        public void UpdateContainers()
        {
            eterpix_get_api.UpdateItems();
        }

        public void ForceUpdateContainers()
        {
            eterpix_get_api.ForceUpdateItems();
        }
    }
}
