using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class eterpix_porta_resize : UdonSharpBehaviour
{
    // VRC_PortalMarker(ポータル)
    [SerializeField] private VRC_PortalMarker portal;
    // [SerializeField] private TMP_InputField world_id_input;
    // [SerializeField] private TMP_InputField portal_siz;
    [SerializeField] private string world_id;
    [SerializeField] private float siz_value = 0.5f;
    [SerializeField] private float portal_clause_distance = 3f;
    public float distance;


    // ポータルが表示中のみの処理
    void Update()
    {
        if (portal.gameObject.activeSelf == true)
        {
            // ローカルユーザーとポータルの距離をDistanceに保存
            VRCPlayerApi player = Networking.LocalPlayer;
            if (player == null)
                return;

            distance = Vector3.Distance(transform.position, player.GetPosition());

            if (distance >= portal_clause_distance)
            {
                portal.gameObject.SetActive(false);
            }
        }

    }


    // 自分をparentの子にして、その場所へスナップする(外部用)
    public void SetParentObject(Transform parent, string roomId)
    {
        if (roomId != "")
        {
            // 表示にする
            portal.gameObject.SetActive(true);

            transform.SetParent(parent);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;



            world_id = roomId;


            portal.roomId = world_id;

            SendCustomEventDelayedSeconds(nameof(ApplyScale), 0.2f);
        }
        else
        {
            portal.gameObject.SetActive(false);
        }
    }

    // public void but_Update()
    // {
    //     // siz_value = float.Parse(portal_siz.text);
    //     string world_id = world_id_input.text;

    //     portal.roomId = world_id;
    //     SendCustomEventDelayedSeconds(nameof(ApplyScale), 0.2f);
    // }

    public void ApplyScale()
    {
        portal.transform.localScale = new Vector3(siz_value, siz_value, siz_value);
    }
}