using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.Image;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ali.eterpix.ui2
{
    // VRC-APIv2 の /public を読み、etp2_mock_data の中身を置き換える(ワールドに1つ. 手元だけ. 同期はしない).
    // 形: 統合管理 docs/contracts/vrc-api-v2-schema.md. 知らないキーは無視する.
    // ページ画像は、使われている番号だけを1枚ずつ取る(VRChat の画像の取得は 5 秒に1回まで)
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_api_loader : UdonSharpBehaviour
    {
        public const int PageCount = 256;
        public const float RetrySeconds = 5.5f;
        public const int MaxRetry = 3;
        public const float ImageGapSeconds = 5.5f;

        [Header("ワールド作者がつなぐ")]
        public etp2_mock_data data;
        [Tooltip("VRC-APIv2 の /public の URL. 空ならダミーのまま")]
        public VRCUrl jsonUrl;
        [Tooltip("ページ画像の URL(/public/00 〜 /public/FF の 256 個). エディタのメニューで jsonUrl から作る")]
        public VRCUrl[] pageUrls;

        private VRCImageDownloader _images;
        private int _retry;
        private bool[] _want;      // 取る必要があるページ
        private bool[] _asked;     // 取りに行ったページ
        private int[] _stamp;      // riim(取った時の最終更新)
        private int[] _wantStamp;
        private bool _imageBusy;

        private void Start()
        {
            _want = new bool[PageCount];
            _asked = new bool[PageCount];
            _stamp = new int[PageCount];
            _wantStamp = new int[PageCount];
            if (jsonUrl == null || string.IsNullOrEmpty(jsonUrl.Get()))
            {
                Debug.Log("[Etp2Trace] api: jsonUrl が空なのでダミーのまま");
                return;
            }
            Load();
        }

        public void Load()
        {
            Debug.Log("[Etp2Trace] api: load " + jsonUrl.Get());
            VRCStringDownloader.LoadUrl(jsonUrl, (IUdonEventReceiver)this);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            Debug.LogWarning("[Etp2Trace] api: json error " + result.ErrorCode + " " + result.Error);
            if (_retry >= MaxRetry) return;
            _retry++;
            SendCustomEventDelayedSeconds(nameof(Load), RetrySeconds);   // 423(リング更新中)もここ
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            _retry = 0;
            if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken root) || root.TokenType != TokenType.DataDictionary)
            {
                Debug.LogWarning("[Etp2Trace] api: json parse error");
                return;
            }
            Parse(root.DataDictionary);
        }

        // ---- JSON → etp2_mock_data ----

        private void Parse(DataDictionary root)
        {
            DataDictionary woda = Dict(root, "woda");
            DataDictionary wopt = Dict(root, "wopt");
            DataDictionary usst = Dict(root, "usst");
            DataDictionary tast = Dict(root, "tast");
            DataDictionary riim = Dict(root, "riim");
            DataList posts = (root.TryGetValue("post", TokenType.DataList, out DataToken pt)) ? pt.DataList : new DataList();

            // ワールド(woda の順)
            int wn = woda == null ? 0 : woda.Count;
            string[] wName = new string[wn], wAuthor = new string[wn], wDesc = new string[wn], wId = new string[wn];
            if (woda != null)
            {
                DataList keys = woda.GetKeys();
                for (int i = 0; i < wn; i++)
                {
                    wId[i] = keys[i].String;
                    DataDictionary w = woda[keys[i]].TokenType == TokenType.DataDictionary ? woda[keys[i]].DataDictionary : null;
                    wName[i] = Str(w, "wona");
                    wAuthor[i] = Str(w, "woau");
                    wDesc[i] = Str(w, "wode");
                }
            }

            int n = posts.Count;
            string[] user = new string[n], title = new string[n], date = new string[n], desc = new string[n];
            string[] world = new string[n], prefix = new string[n], tags = new string[n];
            int[] views = new int[n], stars = new int[n], likes = new int[n];
            Color[] color = new Color[n];
            int[] page = new int[n], rot = new int[n], iconPage = new int[n];
            Vector4[] uv = new Vector4[n], iconUv = new Vector4[n];
            Vector2[] size = new Vector2[n];

            for (int c = 0; c < PageCount; c++) _want[c] = false;

            for (int i = 0; i < n; i++)
            {
                DataDictionary p = posts[i].TokenType == TokenType.DataDictionary ? posts[i].DataDictionary : new DataDictionary();
                user[i] = Str(p, "usna");
                desc[i] = Str(p, "pode");
                date[i] = FormatDate(Num(p, "poca", 0));
                views[i] = (int)Num(p, "povc", 0);
                stars[i] = (int)Num(p, "posl", 0);
                likes[i] = (int)Num(p, "poli", 0);
                color[i] = new Color(0.533f, 0.533f, 0.533f);   // ページ画像の背景と同じ灰色(読み込み中)

                title[i] = "";
                DataList ussf = List(p, "ussf");
                if (ussf != null && ussf.Count > 0 && usst != null) title[i] = Str(usst, Key(ussf[0]));

                string t = "";
                DataList potg = List(p, "potg");
                if (potg != null && tast != null)
                    for (int k = 0; k < potg.Count; k++)
                    {
                        string name = Str(tast, Key(potg[k]));
                        if (name.Length > 0) t += (t.Length > 0 ? " " : "") + "#" + name;
                    }
                tags[i] = t;

                // アイコン(無い人はキーが無い)
                iconPage[i] = -1;
                if (p.ContainsKey("icid") && p.ContainsKey("icpo"))
                {
                    int ic = (int)Num(p, "icid", -1), ipo = (int)Num(p, "icpo", 0);
                    if (ic >= 0 && ic < PageCount && ipo >= 1 && ipo <= 6)
                    {
                        iconPage[i] = ic;
                        iconUv[i] = new Vector4((ipo - 1) * 320f / 2048f, 0f, 320f / 2048f, 320f / 2048f);
                        Want(ic, riim);
                    }
                }

                // 写真は先頭(トップ画像)だけ. 2枚目以降は後の段階
                page[i] = -1;
                world[i] = "";
                prefix[i] = "";
                DataList phot = List(p, "phot");
                if (phot != null && phot.Count > 0 && phot[0].TokenType == TokenType.DataDictionary)
                {
                    DataDictionary ph = phot[0].DataDictionary;
                    int coid = (int)Num(ph, "coid", -1), copo = (int)Num(ph, "copo", 0);
                    float pw = Mathf.Clamp((float)Num(ph, "phwi", 1024), 1f, 1024f);
                    float phh = Mathf.Clamp((float)Num(ph, "phhe", 576), 1f, 576f);
                    if (coid >= 0 && coid < PageCount && copo >= 1 && copo <= 6)
                    {
                        int col = (copo - 1) % 2, row = (copo - 1) / 2;
                        page[i] = coid;
                        uv[i] = new Vector4(col * 1024f / 2048f, 1f - (row * 576f + phh) / 2048f, pw / 2048f, phh / 2048f);
                        size[i] = new Vector2(pw, phh);
                        rot[i] = ((int)Num(ph, "coro", 0)) & 3;
                        Want(coid, riim);
                    }
                    string woid = Str(ph, "woid");
                    if (woid.Length > 0 && woda != null && woda.TryGetValue(woid, TokenType.DataDictionary, out DataToken wt))
                        world[i] = Str(wt.DataDictionary, "wona");
                    DataList wopf = List(ph, "wopf");
                    if (wopf != null && wopt != null)
                        for (int k = 0; k < wopf.Count; k++) prefix[i] += Str(wopt, Key(wopf[k]));
                }
            }

            data.postUser = user; data.postTitle = title; data.postDate = date; data.postDesc = desc;
            data.postWorld = world; data.postWorldPrefix = prefix; data.postTags = tags;
            data.postViews = views; data.postStars = stars; data.postLikes = likes; data.postColor = color;
            data.postPage = page; data.postUv = uv; data.postPhotoSize = size; data.postRot = rot;
            data.postIconPage = iconPage; data.postIconUv = iconUv;
            data.worldName = wName; data.worldAuthor = wAuthor; data.worldDesc = wDesc;
            if (data.pageTex == null || data.pageTex.Length != PageCount) data.pageTex = new Texture[PageCount];
            Debug.Log("[Etp2Trace] api: json ok posts=" + n + " worlds=" + wn);
            data.version++;
            data.NotifyChanged();
            NextImage();
        }

        private void Want(int c, DataDictionary riim)
        {
            _want[c] = true;
            _wantStamp[c] = riim == null ? 0 : (int)Num(riim, c.ToString(), 0);
        }

        // ---- ページ画像(1枚ずつ) ----

        public void NextImage()
        {
            if (_imageBusy) return;
            for (int c = 0; c < PageCount; c++)
            {
                if (!_want[c]) continue;
                if (_asked[c] && _stamp[c] == _wantStamp[c]) continue;
                if (pageUrls == null || c >= pageUrls.Length || pageUrls[c] == null)
                {
                    Debug.LogWarning("[Etp2Trace] api: pageUrls[" + c + "] が無い(メニューで作る)");
                    _asked[c] = true; _stamp[c] = _wantStamp[c];
                    continue;
                }
                if (_images == null) _images = new VRCImageDownloader();
                TextureInfo info = new TextureInfo();
                info.GenerateMipMaps = true;
                info.WrapModeU = TextureWrapMode.Clamp;
                info.WrapModeV = TextureWrapMode.Clamp;
                info.AnisoLevel = 4;
                _asked[c] = true;
                _stamp[c] = _wantStamp[c];
                _imageBusy = true;
                Debug.Log("[Etp2Trace] api: image " + c.ToString("X2"));
                _images.DownloadImage(pageUrls[c], null, (IUdonEventReceiver)this, info);
                return;
            }
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            int c = PageOf(result.Url);
            if (c >= 0)
            {
                if (data.pageTex[c] != null && data.pageTex[c] != result.Result) Destroy(data.pageTex[c]);
                data.pageTex[c] = result.Result;
                Debug.Log("[Etp2Trace] api: image ok " + c.ToString("X2"));
                data.NotifyChanged();
            }
            ImageDone();
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            int c = PageOf(result.Url);
            Debug.LogWarning("[Etp2Trace] api: image error " + c + " " + result.Error + " " + result.ErrorMessage);
            if (c >= 0) _asked[c] = false;   // 次の機会にもう一度
            ImageDone();
        }

        private void ImageDone()
        {
            SendCustomEventDelayedSeconds(nameof(_ImageGap), ImageGapSeconds);
        }

        public void _ImageGap()
        {
            _imageBusy = false;
            NextImage();
        }

        private int PageOf(VRCUrl url)
        {
            if (url == null || pageUrls == null) return -1;
            string s = url.Get();
            for (int c = 0; c < pageUrls.Length; c++) if (pageUrls[c] != null && pageUrls[c].Get() == s) return c;
            return -1;
        }

        // ---- 小さい道具 ----

        private static DataDictionary Dict(DataDictionary d, string key)
        {
            if (d != null && d.TryGetValue(key, TokenType.DataDictionary, out DataToken t)) return t.DataDictionary;
            return null;
        }

        private static DataList List(DataDictionary d, string key)
        {
            if (d != null && d.TryGetValue(key, TokenType.DataList, out DataToken t)) return t.DataList;
            return null;
        }

        private static string Str(DataDictionary d, string key)
        {
            if (d != null && d.TryGetValue(key, TokenType.String, out DataToken t)) return t.String;
            return "";   // null・無い・形が違う
        }

        private static double Num(DataDictionary d, string key, double fallback)
        {
            if (d != null && d.TryGetValue(key, out DataToken t) && t.TokenType == TokenType.Double) return t.Double;
            return fallback;
        }

        // 数の配列の要素(VRCJson は数を double で返す)を、辞書のキー("3")にする
        private static string Key(DataToken t)
        {
            if (t.TokenType == TokenType.Double) return ((int)t.Double).ToString();
            if (t.TokenType == TokenType.String) return t.String;
            return "";
        }

        // UNIX 秒 → 見る人の端末の時刻で 2026/11/13 07:21
        private static string FormatDate(double sec)
        {
            if (sec <= 0) return "";
            System.DateTime d = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(sec).ToLocalTime();
            return d.ToString("yyyy/MM/dd HH:mm");
        }
    }
}
