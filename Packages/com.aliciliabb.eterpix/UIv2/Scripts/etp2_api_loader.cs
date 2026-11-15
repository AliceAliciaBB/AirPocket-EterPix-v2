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
    // ページ画像は v1(eterpix_downloader)と同じく、画面が頼んだものだけを優先度の高い順に1枚ずつ取り、
    // 持つ枚数に上限を置く(計画書 13章). 画面は RequestPage で頼み、使い終わったら ReleasePage で手放す
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class etp2_api_loader : UdonSharpBehaviour
    {
        public const int PageCount = 256;
        // JSON の 423(リング更新中)は 5 秒待って最大 2 回取り直す(v1 と同じ)
        public const int BusyStatusCode = 423;
        public const float BusyRetrySeconds = 5f;
        public const int BusyMaxRetries = 2;
        public const float RequestTimeoutSeconds = 15f;
        // VRChat の「信頼されていない URL を許可」が OFF のときの result.Error(v1 と同じ判定)
        private const string UntrustedUrlErrorMarker = "Not trusted url hit";

        // 優先度(大きいほど先). 同じなら新しいページ(rine に近い)から
        public const int PriorityViewer = 100;
        public const int PriorityFrame = 50;

        private const int StateNone = 0, StateQueued = 1, StateDownloading = 2, StateLoaded = 3;

        [Header("ワールド作者がつなぐ")]
        public etp2_mock_data data;
        [Tooltip("VRC-APIv2 の /public の URL. 空ならダミーのまま")]
        public VRCUrl jsonUrl;
        [Tooltip("ページ画像の URL(/public/00 〜 /public/FF の 256 個). エディタのメニューで jsonUrl から作る")]
        public VRCUrl[] pageUrls;

        [Header("画像を持つ枚数の上限(v1 と同じ既定 8 枚 ≒ 150MB)")]
        public int maxLoadedTextures = 8;
        [Header("使わなくなってから捨てるまでの秒数(前/次で戻ったときに取り直さない)")]
        public float releaseGraceSeconds = 10f;

        private VRCImageDownloader _images;
        private int _busyRetry;
        private bool _jsonPending;
        private int _rine;

        private int[] _state;
        private int[] _ref;          // 頼んでいる画面の数
        private int[] _prio;         // 頼まれた中でいちばん高い優先度
        private float[] _releasedAt; // 参照が 0 になった時刻
        private int[] _stamp;        // 取った画像の riim(最終更新)
        private int[] _riim;         // 今の JSON の riim
        private int _downloading = -1;
        private int _loadedCount;

        private void Start()
        {
            _state = new int[PageCount];
            _ref = new int[PageCount];
            _prio = new int[PageCount];
            _releasedAt = new float[PageCount];
            _stamp = new int[PageCount];
            _riim = new int[PageCount];
            if (data != null) data.loader = this;
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
            _jsonPending = true;
            VRCStringDownloader.LoadUrl(jsonUrl, (IUdonEventReceiver)this);
            SendCustomEventDelayedSeconds(nameof(_CheckTimeout), RequestTimeoutSeconds);
        }

        // 応答が来なくてもログに出すだけ(v1 と同じ. 次へ進めると、後から来た応答を取り違える)
        public void _CheckTimeout()
        {
            if (_jsonPending) Debug.LogWarning("[Etp2Trace] api: json no response after " + RequestTimeoutSeconds + "s");
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            _jsonPending = false;
            string error = result.Error;
            Debug.LogWarning("[Etp2Trace] api: json error " + result.ErrorCode + " " + error);
            if (result.ErrorCode == BusyStatusCode && _busyRetry < BusyMaxRetries)
            {
                _busyRetry++;
                Debug.Log("[Etp2Trace] api: json busy(423) retry " + _busyRetry + "/" + BusyMaxRetries);
                SendCustomEventDelayedSeconds(nameof(Load), BusyRetrySeconds);
                return;
            }
            _busyRetry = 0;
            if (!string.IsNullOrEmpty(error) && error.Contains(UntrustedUrlErrorMarker))
                Debug.LogWarning("[Etp2Trace] api: 信頼されていない URL の許可が OFF");
            // 前に取れたデータはそのまま出し続ける
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            _jsonPending = false;
            _busyRetry = 0;
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
            string[] ids = new string[n];
            string[] user = new string[n], title = new string[n], date = new string[n], desc = new string[n];
            string[] world = new string[n], prefix = new string[n], tags = new string[n];
            int[] views = new int[n], stars = new int[n], likes = new int[n];
            Color[] color = new Color[n];
            int[] page = new int[n], rot = new int[n], iconPage = new int[n];
            Vector4[] uv = new Vector4[n], iconUv = new Vector4[n];
            Vector2[] size = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                DataDictionary p = posts[i].TokenType == TokenType.DataDictionary ? posts[i].DataDictionary : new DataDictionary();
                ids[i] = Str(p, "poid");
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
                    }
                    string woid = Str(ph, "woid");
                    if (woid.Length > 0 && woda != null && woda.TryGetValue(woid, TokenType.DataDictionary, out DataToken wt))
                        world[i] = Str(wt.DataDictionary, "wona");
                    DataList wopf = List(ph, "wopf");
                    if (wopf != null && wopt != null)
                        for (int k = 0; k < wopf.Count; k++) prefix[i] += Str(wopt, Key(wopf[k]));
                }
            }

            data.postId = ids;
            data.postUser = user; data.postTitle = title; data.postDate = date; data.postDesc = desc;
            data.postWorld = world; data.postWorldPrefix = prefix; data.postTags = tags;
            data.postViews = views; data.postStars = stars; data.postLikes = likes; data.postColor = color;
            data.postPage = page; data.postUv = uv; data.postPhotoSize = size; data.postRot = rot;
            data.postIconPage = iconPage; data.postIconUv = iconUv;
            data.worldName = wName; data.worldAuthor = wAuthor; data.worldDesc = wDesc;
            if (data.pageTex == null || data.pageTex.Length != PageCount) data.pageTex = new Texture[PageCount];
            _rine = (int)Num(root, "rine", 0);
            Debug.Log("[Etp2Trace] api: json ok posts=" + n + " worlds=" + wn + " rine=" + _rine);
            ApplyStamps(riim);
            data.version++;
            data.NotifyChanged();   // 画面は出す投稿のページを RequestPage で頼み直す
            StartNextImage();
        }

        // JSON の riim を覚え、取った後で更新されたページは取り直す(v1 の「並びが変わったスロットの取り直し」の代わり)
        private void ApplyStamps(DataDictionary riim)
        {
            for (int c = 0; c < PageCount; c++)
            {
                _riim[c] = riim == null ? 0 : (int)Num(riim, c.ToString(), 0);
                if (_state[c] != StateLoaded || _stamp[c] == _riim[c]) continue;
                if (_ref[c] > 0)
                {
                    _state[c] = StateQueued;   // 届くまでは古い画像のまま
                    Debug.Log("[Etp2Trace] api: page " + c.ToString("X2") + " updated, refetch");
                }
                else Discard(c, "updated");
            }
        }

        // ---- 画面からの頼み ----

        public void RequestPage(int page, int priority)
        {
            if (_state == null || page < 0 || page >= PageCount) return;
            _ref[page]++;
            if (priority > _prio[page]) _prio[page] = priority;
            if (_state[page] == StateNone) _state[page] = StateQueued;
            StartNextImage();
        }

        public void ReleasePage(int page)
        {
            if (_state == null || page < 0 || page >= PageCount || _ref[page] <= 0) return;
            _ref[page]--;
            if (_ref[page] > 0) return;
            _prio[page] = 0;
            _releasedAt[page] = Time.time;
            SendCustomEventDelayedSeconds(nameof(_TryDiscard), releaseGraceSeconds);
        }

        // 使われなくなって releaseGraceSeconds たったものを捨てる(引数を運べないので全部を見る)
        public void _TryDiscard()
        {
            float soonest = -1f;
            for (int c = 0; c < PageCount; c++)
            {
                if (_ref[c] > 0 || _state[c] == StateNone || _state[c] == StateDownloading) continue;
                if (_state[c] == StateQueued && data.pageTex[c] == null) { _state[c] = StateNone; continue; }   // 取る前に要らなくなった
                float remaining = releaseGraceSeconds - (Time.time - _releasedAt[c]);
                if (remaining <= 0.05f) Discard(c, "released");
                else if (soonest < 0f || remaining < soonest) soonest = remaining;
            }
            if (soonest > 0f) SendCustomEventDelayedSeconds(nameof(_TryDiscard), soonest);
        }

        private void Discard(int c, string reason)
        {
            if (data != null && data.pageTex != null && data.pageTex[c] != null)
            {
                Texture t = data.pageTex[c];
                data.pageTex[c] = null;
                Destroy(t);
                _loadedCount--;
            }
            _state[c] = _ref[c] > 0 ? StateQueued : StateNone;
            Debug.Log("[Etp2Trace] api: discard " + c.ToString("X2") + " (" + reason + ") loaded=" + _loadedCount);
        }

        // 上限に届いているなら、使われていないものの中で優先度の低い・古いものから捨てる
        private void EvictIfOverBudget()
        {
            while (_loadedCount >= maxLoadedTextures)
            {
                int worst = -1;
                for (int c = 0; c < PageCount; c++)
                {
                    if (_state[c] != StateLoaded || _ref[c] > 0) continue;
                    if (worst < 0 || _prio[c] < _prio[worst] || (_prio[c] == _prio[worst] && Age(c) > Age(worst))) worst = c;
                }
                if (worst < 0) return;   // 全部使われている(上限を超えて持つ)
                Discard(worst, "over budget");
            }
        }

        // rine からどれだけ古いか(0 がいちばん新しい)
        private int Age(int c)
        {
            return (_rine - c + PageCount) % PageCount;
        }

        // ---- ページ画像(1枚ずつ. 届いたらすぐ次. 待ちは VRChat 側の 5 秒に任せる) ----

        public void StartNextImage()
        {
            if (_downloading >= 0 || _state == null) return;
            int best = -1;
            for (int c = 0; c < PageCount; c++)
            {
                if (_state[c] != StateQueued || _ref[c] <= 0) continue;
                if (best < 0 || _prio[c] > _prio[best] || (_prio[c] == _prio[best] && Age(c) < Age(best))) best = c;
            }
            if (best < 0) return;
            if (pageUrls == null || best >= pageUrls.Length || pageUrls[best] == null)
            {
                Debug.LogWarning("[Etp2Trace] api: pageUrls[" + best + "] が無い(メニューで作る)");
                _state[best] = StateNone;
                StartNextImage();
                return;
            }
            if (data.pageTex[best] == null) EvictIfOverBudget();   // 取り直し(更新)は数が増えない
            if (_images == null) _images = new VRCImageDownloader();
            TextureInfo info = new TextureInfo();
            info.GenerateMipMaps = true;
            info.WrapModeU = TextureWrapMode.Clamp;
            info.WrapModeV = TextureWrapMode.Clamp;
            info.AnisoLevel = 4;
            _state[best] = StateDownloading;
            _downloading = best;
            Debug.Log("[Etp2Trace] api: image " + best.ToString("X2") + " priority=" + _prio[best] + " loaded=" + _loadedCount);
            _images.DownloadImage(pageUrls[best], null, (IUdonEventReceiver)this, info);
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            int c = _downloading;
            _downloading = -1;
            if (c >= 0)
            {
                if (data.pageTex[c] != null) { Destroy(data.pageTex[c]); _loadedCount--; }   // 更新で取り直した
                data.pageTex[c] = result.Result;
                _loadedCount++;
                _state[c] = StateLoaded;
                _stamp[c] = _riim[c];
                Debug.Log("[Etp2Trace] api: image ok " + c.ToString("X2") + " ref=" + _ref[c] + " loaded=" + _loadedCount);
                if (_ref[c] <= 0) { _releasedAt[c] = Time.time; SendCustomEventDelayedSeconds(nameof(_TryDiscard), releaseGraceSeconds); }
                data.NotifyChanged();
            }
            StartNextImage();
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            int c = _downloading;
            _downloading = -1;
            Debug.LogWarning("[Etp2Trace] api: image error " + c + " " + result.Error + " " + result.ErrorMessage);
            // まだ使う画面があれば並べ直す(v1 と同じ)
            if (c >= 0) _state[c] = _ref[c] > 0 ? StateQueued : (data.pageTex[c] != null ? StateLoaded : StateNone);
            StartNextImage();
        }

        // 調べる用
        public int LoadedCount() { return _loadedCount; }

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
