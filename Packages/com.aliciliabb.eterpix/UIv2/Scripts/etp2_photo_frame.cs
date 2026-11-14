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
        public RawImage photo;
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
            RefreshPhoto();
        }

        public int[] GetPostIndices()
        {
            if (postIndices != null && postIndices.Length > 0) return postIndices;
            int n = data == null ? 0 : data.PostCount();
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
            if (data.postColor != null && p < data.postColor.Length) photo.color = data.postColor[p];
        }

        public void OnClick()
        {
            if (viewer == null) return;
            Debug.Log("[Etp2Trace] photo_frame: open " + gameObject.name);
            viewer.OpenFrame(this);
        }
    }
}
