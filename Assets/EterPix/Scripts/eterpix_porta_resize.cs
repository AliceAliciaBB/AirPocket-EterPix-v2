using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class eterpix_porta_resize : UdonSharpBehaviour
{
    // VRC_PortalMarker(ポータル)
    [SerializeField] private VRCPortalMarker portal;
    // [SerializeField] private TMP_InputField world_id_input;
    // [SerializeField] private TMP_InputField portal_siz;
    [SerializeField] private string world_id;
    [SerializeField] private float siz_value = 0.5f;
    // ポータルの上にあるUIと重ならないよう、指定座標から下へずらす量(ポータル1倍時の高さ基準)。
    // 実際の移動量は offset × siz_value(大きさに比例する)
    [Tooltip("指定座標から下へずらす量。ポータル1倍時の高さ基準で、実際の移動量は Offset × siz_value")]
    [SerializeField] private float offset = 1.5f;
    [SerializeField] private float portal_clause_distance = 3f;
    public float distance;


    // ポータルが表示中のみの処理
    void Update()
    {
        if (portal == null)
            return;

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
        if (portal == null)
            return;

        if (roomId != "")
        {
            // 表示にする
            portal.gameObject.SetActive(true);

            transform.SetParent(parent);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;


            world_id = roomId;


            portal.roomId = world_id;
            portal.RefreshPortal();
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

    // 下へずらす処理も大きさと同じフレームで行う(RefreshPortal直後に動かすとポータルの挙動がおかしくなるため)
    public void ApplyScale()
    {
        portal.transform.localScale = new Vector3(siz_value, siz_value, siz_value);

        // 親(モニター)のスケールの影響を受けないよう、ワールド座標で真下へずらす
        Transform parent = transform.parent;
        if (parent != null) transform.position = parent.position + Vector3.down * (offset * siz_value);
    }
}