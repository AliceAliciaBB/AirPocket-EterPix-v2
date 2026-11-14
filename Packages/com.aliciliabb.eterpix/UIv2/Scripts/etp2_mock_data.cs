using UdonSharp;
using UnityEngine;

namespace ali.eterpix.ui2
{
    // 第1段階のダミーデータ. 第3段階で VRC-APIv2 の JSON 読み込みに差し替える.
    // 画面側はこの部品から読むだけにする(初めの値はビルダーが Etp2Spec から入れる)
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_mock_data : UdonSharpBehaviour
    {
        [Header("投稿")]
        public string[] postUser;
        public string[] postTitle;    // 称号. 無い人は空
        public string[] postDate;
        public string[] postDesc;
        public string[] postWorld;
        public int[] postViews;
        public int[] postStars;
        public int[] postLikes;
        public Color[] postColor;     // 写真の代わりの色

        [Header("ワールド")]
        public string[] worldName;
        public string[] worldAuthor;
        public string[] worldDesc;

        public int PostCount() { return postUser == null ? 0 : postUser.Length; }
        public int WorldCount() { return worldName == null ? 0 : worldName.Length; }

        public int FindWorld(string name)
        {
            if (worldName == null) return -1;
            for (int i = 0; i < worldName.Length; i++) if (worldName[i] == name) return i;
            return -1;
        }

        // 1,234 のように3桁ごとに区切る
        public static string FormatCount(int n)
        {
            return n.ToString("N0");
        }
    }
}
