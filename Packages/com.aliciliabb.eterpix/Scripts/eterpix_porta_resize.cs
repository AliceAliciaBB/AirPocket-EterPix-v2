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
    // Y オフセット用の中間 Transform。localPosition.y に offset×siz_value を開く直前にセットする
    [SerializeField] private Transform offsetY;
    [SerializeField] private string world_id;
    [SerializeField] private float siz_value = 0.5f;
    // ポータルの上にあるUIと重ならないよう、指定座標から下へずらす量(ポータル1倍時の高さ基準)。
    // 実際の移動量は offset × siz_value(大きさに比例する)
    [Tooltip("指定座標から下へずらす量。ポータル1倍時の高さ基準で、実際の移動量は Offset × siz_value")]
    [SerializeField] private float offset = 2f;
    [SerializeField] private float portal_clause_distance = 3f;
    public float distance;


    // 現在このポータルを開いたv2モニター(旧系統はnull)
    private UdonSharpBehaviour _opener;

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
                NotifyOpenerAndClear();
            }
        }

    }

    private void NotifyOpenerAndClear()
    {
        if (_opener != null)
        {
            _opener.SendCustomEvent("OnPortalStateChanged");
            _opener = null;
        }
    }

    // 自分をparentの子にして、その場所へスナップする(旧系統用)
    public void SetParentObject(Transform parent, string roomId)
    {
        SetParentObjectWithOpener(parent, roomId, null);
    }

    // 自分をparentの子にして、その場所へスナップする(v2モニター用。openerに状態変化を通知する)
    public void SetParentObjectWithOpener(Transform parent, string roomId, UdonSharpBehaviour opener)
    {
        if (portal == null)
            return;

        // SetParent より先に通知すると、前のopenerがIsOpenAtで「まだ自分の下」と誤判定するため
        // 先にparentを移動してから通知する
        UdonSharpBehaviour prevOpener = _opener;
        _opener = null;

        if (roomId != "")
        {
            transform.SetParent(parent);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // SetActive より前に Y オフセットをセット(出現時から正しい位置にするため)
            if (offsetY != null) offsetY.localPosition = Vector3.down * (offset * siz_value);

            // 表示にする
            portal.gameObject.SetActive(true);

            // 親の移動が完了してから前のopenerに通知する
            // 同じモニターが自分のポータルを再表示する場合は通知しない(_portalShouldBeOpenを壊さないため)
            if (prevOpener != null && prevOpener != opener) prevOpener.SendCustomEvent("OnPortalStateChanged");

            _opener = opener;

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

    // ポータルを閉じる(v2モニターのトグルボタンから呼ぶ)
    public void Close()
    {
        if (portal != null) portal.gameObject.SetActive(false);
        NotifyOpenerAndClear();
    }

    // ポータルを一時的に非表示にする(_openerは維持。ワールド情報が戻ったら再表示するため)
    public void HideTemporarily()
    {
        if (portal != null) portal.gameObject.SetActive(false);
    }

    // このポータルが指定のspawnで開いているか
    public bool IsOpenAt(Transform spawn)
    {
        return portal != null && portal.gameObject.activeSelf && transform.parent == spawn;
    }

    // 位置・大きさはそのままでroomIdだけ差し替える(ページ送り時のワールド更新用)
    public void ChangeWorld(string roomId)
    {
        if (portal == null) return;
        world_id = roomId;
        portal.roomId = world_id;
        portal.RefreshPortal();
    }

    // public void but_Update()
    // {
    //     // siz_value = float.Parse(portal_siz.text);
    //     string world_id = world_id_input.text;

    //     portal.roomId = world_id;
    //     SendCustomEventDelayedSeconds(nameof(ApplyScale), 0.2f);
    // }

    // スケールのみ適用(RefreshPortal直後に動かすとポータルの挙動がおかしくなるため0.2秒遅延)
    // Y オフセットは offsetY.localPosition で開く直前にセット済みのため、ここでは位置を触らない
    public void ApplyScale()
    {
        portal.transform.localScale = new Vector3(siz_value, siz_value, siz_value);
    }
}