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
        private int _current = -1;
        private int _loadedCount;

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

        private void StartNext()
        {
            if (_busy) return;
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
