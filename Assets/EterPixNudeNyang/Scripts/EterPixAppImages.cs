using UdonSharp;
using UnityEngine;
using VRC.SDK3.Image;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace EterPixNudeNyang
{
    // コラージュ画像(1枚に6投稿)の取得とキャッシュ。VRChat の画像ダウンロードは5秒間隔のため、1枚ずつ直列に取る.
    // 取得URL: https://eterpix.com/api/vrc/v1/public/<coid を16進2桁>(ビルド時に256件を焼く)
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class EterPixAppImages : UdonSharpBehaviour
    {
        public const int Slots = 256;
        private const int StateNone = 0;
        private const int StateWanted = 1;
        private const int StateDownloading = 2;
        private const int StateLoaded = 3;
        private const int StateFailed = 4;
        private const int MaxErrors = 3;
        private const int Unneeded = 1000000;

        // coid 0〜255 の取得URL(ビルド時に焼く)
        public VRCUrl[] imageUrls;
        // 保持するテクスチャの上限(枚数)。仮の値。実機のメモリで決める(合意書「投稿の件数」)
        public int maxCached = 24;
        // 画像が届いたら "OnImageLoaded" を送る相手
        public UdonSharpBehaviour owner;

        private Texture2D[] _tex = new Texture2D[Slots];
        private int[] _state = new int[Slots];
        private int[] _priority = new int[Slots];
        private int[] _errors = new int[Slots];
        private VRCImageDownloader _downloader;
        private bool _busy;
        private bool _discardCurrent;
        private int _current = -1;
        private int _loadedCount;

        // ---- 高画質(pad の写真 1 枚. ユーザーが貼った VRCUrl で取る) ----
        public const int HqNone = 0;
        public const int HqLoading = 1;
        public const int HqLoaded = 2;
        public const int HqFailed = 3;
        private const int HqSlot = -2; // _current がこの値なら、取得中は高画質
        private int _hqState = HqNone;
        private VRCUrl _hqUrl;
        private VRCUrl _keptUrl;      // 直近に貼られた高画質の URL(1件)
        private int _keptPost = -1;   // その投稿(feed の添字)
        private bool _hqPending;      // 取得待ち(コラージュの取得中の1枚を待っている)
        private int _hqPost = -1;     // どの投稿の写真か(feed の添字)
        private Texture2D _hqTex;
        private string _hqError = "";

        // 高画質を取る. 取得中の1枚(5秒制限)が終わり次第、コラージュより先に取る. 前の高画質は捨てる
        public bool RequestHq(VRCUrl url, int post)
        {
            if (url == null || VRCUrl.IsNullOrEmpty(url)) return false;
            ClearHq();
            _hqUrl = url;
            _keptUrl = url; _keptPost = post; // 直近の1件だけ保持(次に同じ投稿の「高画質」を押したとき、貼り直しなしで取り直す)
            _hqPost = post;
            _hqPending = true;
            _hqState = HqLoading;
            _hqError = "";
            Debug.Log("[EtpTrace] hq request post=" + post + " busy=" + _busy);
            StartNext();
            return true;
        }

        // 保持した URL(破棄=ClearHq では消えない. 画像を解放・取得の失敗・別の投稿で取り直したときに入れ替わる/消える)
        public bool HasKept(int post) { return _keptPost == post && _keptUrl != null && !VRCUrl.IsNullOrEmpty(_keptUrl); }
        public VRCUrl KeptUrl() { return _keptUrl; }
        public void DropKept() { _keptUrl = VRCUrl.Empty; _keptPost = -1; }

        public int HqStatus() { return _hqState; }
        public int HqPostIndex() { return _hqPost; }
        public string HqErrorText() { return _hqError; }
        public Texture2D GetHqTexture() { return _hqState == HqLoaded ? _hqTex : null; }

        // 高画質を捨てる(Zoom を閉じたとき・取り直すとき・画像の解放). 取得中なら、届いたものを捨てる
        public void ClearHq()
        {
            if (_hqTex != null) { Destroy(_hqTex); _hqTex = null; }
            if (_hqPending) _hqPending = false;
            else if (_busy && _current == HqSlot) _discardCurrent = true;
            _hqState = HqNone;
            _hqPost = -1;
            _hqError = "";
        }

        private void Start()
        {
            for (int i = 0; i < Slots; i++) _priority[i] = Unneeded;
        }

        private void EnsureDownloader()
        {
            if (_downloader == null) _downloader = new VRCImageDownloader();
        }

        public Texture2D GetTexture(int coid)
        {
            if (coid < 0 || coid >= Slots) return null;
            return _state[coid] == StateLoaded ? _tex[coid] : null;
        }

        public bool IsFailed(int coid)
        {
            return coid >= 0 && coid < Slots && _state[coid] == StateFailed;
        }

        public int LoadedCount()
        {
            return _loadedCount;
        }

        // これから欲しい範囲を組み直す前に呼ぶ。以降 Want で入れ直した画像だけが「必要」になる
        public void BeginWindow()
        {
            for (int i = 0; i < Slots; i++) _priority[i] = Unneeded;
        }

        // priority が小さいほど先に取る(現在のカードに近い順)。取得済みなら何もしない
        public void Want(int coid, int priority)
        {
            if (coid < 0 || coid >= Slots) return;
            if (priority < _priority[coid]) _priority[coid] = priority;
            if (_state[coid] == StateNone || _state[coid] == StateFailed)
            {
                _errors[coid] = 0;
                _state[coid] = StateWanted;
            }
        }

        // Want を呼び終えたら呼ぶ(上限を超えた分の追い出しと、取得の開始)
        public void EndWindow()
        {
            Evict();
            StartNext();
        }

        // 取得済みの画像を全部捨てて、取得待ちも空にする(設定の「画像を解放」). 取得中の1枚は止められないので、届いたら捨てる.
        // 捨てたあと必要なら BeginWindow / Want / EndWindow で取り直す
        public void ReleaseAll()
        {
            int freed = 0;
            for (int i = 0; i < Slots; i++)
            {
                if (_tex[i] != null) { Destroy(_tex[i]); _tex[i] = null; freed++; }
                _state[i] = StateNone;
                _errors[i] = 0;
                _priority[i] = Unneeded;
            }
            _loadedCount = 0;
            ClearHq(); // 高画質も捨てる
            DropKept(); // 保持した URL も捨てる
            if (_busy) _discardCurrent = true;
            Debug.Log("[EtpTrace] img release all freed=" + freed + " discardCurrent=" + _discardCurrent);
        }

        // 必要な画像のうち、取得済みの数と全体の数(起動画面の進行度に使う)
        public int NeededCount()
        {
            int n = 0;
            for (int i = 0; i < Slots; i++) if (_priority[i] < Unneeded) n++;
            return n;
        }

        public int NeededLoadedCount()
        {
            int n = 0;
            for (int i = 0; i < Slots; i++) if (_priority[i] < Unneeded && _state[i] == StateLoaded) n++;
            return n;
        }

        // 必要な画像のうち、取得済み+あきらめた数(起動画面の進行度で「もう待たない」数)
        public int NeededSettledCount()
        {
            int n = 0;
            for (int i = 0; i < Slots; i++)
                if (_priority[i] < Unneeded && (_state[i] == StateLoaded || _state[i] == StateFailed)) n++;
            return n;
        }

        private void StartNext()
        {
            if (_busy) return;
            if (_hqPending)
            {
                _hqPending = false;
                EnsureDownloader();
                _busy = true;
                _current = HqSlot;
                _downloader.DownloadImage(_hqUrl, null, (IUdonEventReceiver)this, null);
                return;
            }
            int best = -1;
            int bestPriority = Unneeded;
            for (int i = 0; i < Slots; i++)
            {
                if (_state[i] != StateWanted) continue;
                if (_priority[i] >= Unneeded) continue;
                if (_priority[i] < bestPriority) { bestPriority = _priority[i]; best = i; }
            }
            if (best < 0) return;
            if (imageUrls == null || best >= imageUrls.Length || imageUrls[best] == null)
            {
                Debug.Log("[EtpTrace] img coid=" + best + " URLが未設定です");
                _state[best] = StateFailed;
                return;
            }
            EnsureDownloader();
            _busy = true;
            _current = best;
            _state[best] = StateDownloading;
            _downloader.DownloadImage(imageUrls[best], null, (IUdonEventReceiver)this, null);
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            _busy = false;
            int coid = _current;
            _current = -1;
            if (coid == HqSlot)
            {
                if (_discardCurrent)
                {
                    _discardCurrent = false;
                    if (result.Result != null) Destroy(result.Result);
                    Debug.Log("[EtpTrace] hq discarded (cleared while downloading)");
                }
                else
                {
                    if (_hqTex != null) Destroy(_hqTex);
                    _hqTex = result.Result;
                    _hqState = HqLoaded;
                    Debug.Log("[EtpTrace] hq loaded size=" + _hqTex.width + "x" + _hqTex.height + " post=" + _hqPost);
                    if (owner != null) owner.SendCustomEvent("OnHqLoaded");
                }
                StartNext();
                return;
            }
            if (_discardCurrent)
            {
                // 解放(ReleaseAll)の前に始まった取得: 捨てる
                _discardCurrent = false;
                if (result.Result != null) Destroy(result.Result);
                Debug.Log("[EtpTrace] img coid=" + coid + " discarded (released while downloading)");
                StartNext();
                return;
            }
            if (coid < 0 || coid >= Slots)
            {
                StartNext();
                return;
            }
            if (_tex[coid] != null) Destroy(_tex[coid]);
            _tex[coid] = result.Result;
            _state[coid] = StateLoaded;
            _loadedCount++;
            Debug.Log("[EtpTrace] img coid=" + coid + " loaded size=" + result.Result.width + "x" + result.Result.height
                + " loadedCount=" + _loadedCount + " needed=" + NeededLoadedCount() + "/" + NeededCount());
            Evict();
            if (owner != null) owner.SendCustomEvent("OnImageLoaded");
            StartNext();
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            _busy = false;
            int coid = _current;
            _current = -1;
            if (coid == HqSlot)
            {
                if (_discardCurrent) _discardCurrent = false;
                else
                {
                    _hqState = HqFailed;
                    _hqError = result.ErrorMessage;
                    Debug.Log("[EtpTrace] hq error=" + result.ErrorMessage + " post=" + _hqPost);
                    if (owner != null) owner.SendCustomEvent("OnHqFailed");
                }
                StartNext();
                return;
            }
            if (_discardCurrent)
            {
                _discardCurrent = false;
                StartNext();
                return;
            }
            if (coid >= 0 && coid < Slots)
            {
                _errors[coid]++;
                _state[coid] = _errors[coid] < MaxErrors ? StateWanted : StateFailed;
                Debug.Log("[EtpTrace] img coid=" + coid + " error=" + result.ErrorMessage + " errors=" + _errors[coid]
                    + (_state[coid] == StateFailed ? " (あきらめました)" : " (取り直します)"));
                if (_state[coid] == StateFailed && owner != null) owner.SendCustomEvent("OnImageLoaded");
            }
            StartNext();
        }

        // 保持上限を超えたら、必要度の低い(priority が大きい)取得済みから捨てる
        private void Evict()
        {
            while (_loadedCount > maxCached)
            {
                int worst = -1;
                int worstPriority = -1;
                for (int i = 0; i < Slots; i++)
                {
                    if (_state[i] != StateLoaded) continue;
                    if (_priority[i] > worstPriority) { worstPriority = _priority[i]; worst = i; }
                }
                if (worst < 0) return;
                // 必要な画像だけで上限を超えているなら、これ以上は捨てない(見ている画像を消さない)
                if (worstPriority < Unneeded) return;
                Destroy(_tex[worst]);
                _tex[worst] = null;
                _state[worst] = StateNone;
                _loadedCount--;
                Debug.Log("[EtpTrace] img coid=" + worst + " evicted loadedCount=" + _loadedCount);
            }
        }
    }
}
