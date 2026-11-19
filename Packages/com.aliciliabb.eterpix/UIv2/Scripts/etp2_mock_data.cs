using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ali.eterpix.ui2
{
    // 画面が読むデータ. 初めの値はダミー(ビルダーが Etp2Spec から入れる).
    // etp2_api_loader があれば、VRC-APIv2 の中身で置き換え、NotifyChanged で画面に知らせる(計画書 11章)
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
        public string[] postWorldPrefix;  // ワールド名の前の印(wopt). 無ければ空
        public string[] postTags;
        public string[] postId;       // poid(API のとき. チェキの同期に使う)     // 「#夕暮れ #集合写真」. 無い(ダミー)なら説明の中の # を使う

        [Header("写真(API のときだけ. phot[0])")]
        public int[] postPage;        // ページ画像の番号. -1 は無し
        public Vector4[] postUv;      // x, y, 幅, 高さ(0〜1)
        public Vector2[] postPhotoSize; // phwi, phhe(セルの中の向きのまま)
        public int[] postRot;         // coro(時計回りに 90° × この数)
        public int[] postIconPage;    // 投稿者のアイコン. -1 は無し
        public Vector4[] postIconUv;
        public Texture[] pageTex;     // ページ画像(256)

        [Header("ワールド")]
        public string[] worldName;
        public string[] worldAuthor;
        public string[] worldDesc;

        [HideInInspector] public int version;
        [HideInInspector] public etp2_api_loader loader;   // 読み込み係が Start で入れる. 無ければ画像は無し

        // 画面が出す投稿のページ画像を頼む / 手放す(計画書 13章). 前に頼んだページ(held)を返す値で置き換える
        public int HoldPage(int held, int page, int priority)
        {
            if (held == page || loader == null) return held;   // 読み込み係がまだ無ければ頼まない(JSON が届いたら頼み直す)
            if (page >= 0) loader.RequestPage(page, priority);
            if (held >= 0) loader.ReleasePage(held);
            return page;
        }

        public string PostIdOf(int p)
        {
            if (postId != null && p >= 0 && p < postId.Length) return postId[p];
            return p >= 0 ? "dummy-" + p : "";   // ダミーのとき
        }

        public int FindPost(string poid)
        {
            if (string.IsNullOrEmpty(poid)) return -1;
            if (postId != null) { for (int i = 0; i < postId.Length; i++) if (postId[i] == poid) return i; return -1; }
            if (poid.StartsWith("dummy-")) { int n; if (int.TryParse(poid.Substring(6), out n) && n < PostCount()) return n; }
            return -1;
        }

        public int PhotoPageOf(int p)
        {
            return postPage != null && p >= 0 && p < postPage.Length ? postPage[p] : -1;
        }

        public int IconPageOf(int p)
        {
            return postIconPage != null && p >= 0 && p < postIconPage.Length ? postIconPage[p] : -1;
        }   // JSON を読み直すたびに 1 増える(画像が届いただけでは増えない)

        private UdonSharpBehaviour[] _listeners = new UdonSharpBehaviour[0];
        private etp2_photo_frame[] _frames = new etp2_photo_frame[0];

        // フォトフレームが Start で登録する. チェキに同期した位置から、一番近いフォトフレームを探す(計画書 15章)
        public void AddFrame(etp2_photo_frame f)
        {
            if (f == null) return;
            for (int i = 0; i < _frames.Length; i++) if (_frames[i] == f) return;
            etp2_photo_frame[] next = new etp2_photo_frame[_frames.Length + 1];
            for (int i = 0; i < _frames.Length; i++) next[i] = _frames[i];
            next[_frames.Length] = f;
            _frames = next;
        }

        public etp2_photo_frame FindFrameNear(Vector3 pos)
        {
            etp2_photo_frame best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _frames.Length; i++)
            {
                if (_frames[i] == null) continue;
                float d = (_frames[i].transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = _frames[i]; }
            }
            return best;
        }

        // 画面が Start で登録する. 中身が変わると OnDataChanged を送る
        public void Listen(UdonSharpBehaviour b)
        {
            if (b == null) return;
            for (int i = 0; i < _listeners.Length; i++) if (_listeners[i] == b) return;
            UdonSharpBehaviour[] next = new UdonSharpBehaviour[_listeners.Length + 1];
            for (int i = 0; i < _listeners.Length; i++) next[i] = _listeners[i];
            next[_listeners.Length] = b;
            _listeners = next;
        }

        public void NotifyChanged()
        {
            for (int i = 0; i < _listeners.Length; i++) if (_listeners[i] != null) _listeners[i].SendCustomEvent("OnDataChanged");
        }

        public string TagsOf(int p)
        {
            if (postTags != null && p < postTags.Length) return postTags[p];
            return postDesc != null && p < postDesc.Length ? postDesc[p] : "";
        }

        public string WorldLabel(int p)
        {
            string pre = postWorldPrefix != null && p < postWorldPrefix.Length ? postWorldPrefix[p] : "";
            return pre + postWorld[p];
        }

        private Texture PhotoTex(int p)
        {
            if (postPage == null || pageTex == null || p < 0 || p >= postPage.Length) return null;
            int c = postPage[p];
            return c >= 0 && c < pageTex.Length ? pageTex[c] : null;
        }

        // 写真を出す. box: 枠(写真が無いときは代わりの色). img: 枠の子の写真(中央に置く).
        // cover: 枠いっぱいに切り抜く(フォトフレーム) / false: 縦横比を保って収める(ビューア)
        public void ShowPhoto(int p, RawImage box, RawImage img, bool cover)
        {
            if (box == null) return;
            Texture tex = PhotoTex(p);
            if (tex == null || img == null)
            {
                if (postColor != null && p >= 0 && p < postColor.Length) box.color = postColor[p];
                if (img != null) img.gameObject.SetActive(false);
                return;
            }
            box.color = new Color(0f, 0f, 0f, 0f);
            img.gameObject.SetActive(true);
            Rect r = box.GetComponent<RectTransform>().rect;
            Vector4 uv = postUv[p];
            float w = postPhotoSize[p].x, h = postPhotoSize[p].y;
            int rot = postRot[p] & 3;
            bool odd = (rot & 1) == 1;
            if (cover && r.width > 0f && r.height > 0f)
            {
                float target = r.width / r.height;
                float dw = odd ? h : w, dh = odd ? w : h;   // 回したあとの見た目の大きさ
                if (dw / dh > target)
                {
                    float k = dh * target / dw;              // 見た目の横を削る
                    if (!odd) { uv.x += uv.z * (1f - k) * 0.5f; uv.z *= k; w *= k; }
                    else { uv.y += uv.w * (1f - k) * 0.5f; uv.w *= k; h *= k; }
                }
                else
                {
                    float k = dw / target / dh;              // 見た目の縦を削る
                    if (!odd) { uv.y += uv.w * (1f - k) * 0.5f; uv.w *= k; h *= k; }
                    else { uv.x += uv.z * (1f - k) * 0.5f; uv.z *= k; w *= k; }
                }
            }
            float sw = odd ? h : w, sh = odd ? w : h;
            float s = Mathf.Min(r.width / sw, r.height / sh);
            RectTransform irt = img.GetComponent<RectTransform>();
            irt.sizeDelta = new Vector2(w * s, h * s);
            irt.localEulerAngles = new Vector3(0f, 0f, -90f * rot);
            img.texture = tex;
            img.uvRect = new Rect(uv.x, uv.y, uv.z, uv.w);
            img.color = Color.white;
        }

        // 投稿者のアイコン(丸の中). 無ければ隠す
        public void ShowIcon(int p, RawImage icon)
        {
            if (icon == null) return;
            Texture tex = null;
            if (postIconPage != null && pageTex != null && p >= 0 && p < postIconPage.Length)
            {
                int c = postIconPage[p];
                if (c >= 0 && c < pageTex.Length) tex = pageTex[c];
            }
            icon.gameObject.SetActive(tex != null);
            if (tex == null) return;
            Vector4 uv = postIconUv[p];
            icon.texture = tex;
            icon.uvRect = new Rect(uv.x, uv.y, uv.z, uv.w);
        }

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
