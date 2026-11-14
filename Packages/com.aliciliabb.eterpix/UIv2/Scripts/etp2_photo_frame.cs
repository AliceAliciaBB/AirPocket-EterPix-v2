using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ali.eterpix.ui2
{
    // フォトフレーム(ワールドに複数). 押すとビューアでこのフォトフレームを開く(ローカル).
    // 並び順・ページ・鍵はフォトフレームごとに持つ(第2段階で同期する. 第1段階は手元だけ)
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_photo_frame : UdonSharpBehaviour
    {
        [Header("ワールド作者がつなぐ")]
        public etp2_viewer viewer;
        public etp2_mock_data data;
        [Tooltip("このフォトフレームで見せる投稿の番号. 空なら全部")]
        public int[] postIndices;

        [Header("ビルダーが入れる")]
        public RawImage photo;                 // 枠(押せる. 写真が無いときは代わりの色)
        public RawImage photoImg;              // 枠の子の写真(API のとき. 縦横比を保って全体を収める)
        public TextMeshProUGUI watchersText;   // 「X人が見ています」. 第2段階で使う
        [Tooltip("開いたときにビューアを置く位置と向き(子の ViewerAnchor. 凡例の黒い板を動かして決める)")]
        public Transform viewerAnchor;
        [Tooltip("凡例(EditorOnly. VRChat では外れる). エディタの Play 中は隠す")]
        public GameObject legend;

        [HideInInspector] public int localPos;

        private void Start()
        {
            if (watchersText != null) watchersText.gameObject.SetActive(false);
            if (legend != null) legend.SetActive(false);
            if (data != null) data.Listen(this);
            RefreshPhoto();
        }

        public void OnDataChanged()
        {
            int n = GetPostIndices().Length;
            if (localPos >= n) localPos = 0;
            RefreshPhoto();
        }

        public int[] GetPostIndices()
        {
            int n = data == null ? 0 : data.PostCount();
            if (postIndices != null && postIndices.Length > 0)
            {
                // データ(API)の件数を超える番号は外す
                int k = 0;
                for (int i = 0; i < postIndices.Length; i++) if (postIndices[i] >= 0 && postIndices[i] < n) k++;
                int[] list = new int[k];
                k = 0;
                for (int i = 0; i < postIndices.Length; i++) if (postIndices[i] >= 0 && postIndices[i] < n) list[k++] = postIndices[i];
                return list;
            }
            int[] all = new int[n];
            for (int i = 0; i < n; i++) all[i] = i;
            return all;
        }

        public void RefreshPhoto()
        {
            if (photo == null || data == null) return;
            int[] list = GetPostIndices();
            if (list.Length == 0) return;
            int p = list[Mathf.Clamp(localPos, 0, list.Length - 1)];
            data.ShowPhoto(p, photo, photoImg, false);   // 全体を見せる(切り抜かない. 2026-11-14 ユーザー指摘)
        }

        public void OnClick()
        {
            if (viewer == null) return;
            Debug.Log("[Etp2Trace] photo_frame: open " + gameObject.name);
            viewer.OpenFrame(this);
        }
    }
}
