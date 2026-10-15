using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Image;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

namespace ali.eterpix.v2
{
    // EtrPix-Pad の統合要求者(計画書 K-61)。1ワールドに1つだけ置く(シングルトン。2つ目以降は自分を止める)。
    // 既存モニターの「downloader ← requester ← monitor」と同じく、「downloader ← pad_requester ← pad」の中位を受け持つ。
    //   - URL単位の相乗り: 同じURLの文字列取得は1回にまとめ、待っているPad全員に結果を配る(結果はキャッシュしない)
    //   - 写真の共有: URLごとにテクスチャを1枚だけ持ち、使っているPadの数で管理する。0になったら破棄する
    //   - 取得の通り道: ワールドにdownloaderがあればその優先キューに積み(K-12)、無ければ自分で直接取得する(K-48)
    // Padは1度に1つのURLしか持たないため、URLを変えた時・範囲を出た時はReleaseAllでまとめて手放す。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_pad_requester : UdonSharpBehaviour
    {
        public const string SingletonObjectName = "EterpixPadRequester";

        private const int ImageNone = 0;
        private const int ImageLoading = 1;
        private const int ImageLoaded = 2;

        [Header("デバッグログ")]
        [SerializeField] private ali.eterpix.eterpix_debug debugLog;

        // URLごとの記録(可変長を模した固定長+件数管理。使い終わった枠は空けて再利用する)
        private string[] _keys = new string[0];
        private VRCUrl[] _urls = new VRCUrl[0];
        private bool[] _stringLoading = new bool[0];
        private eterpix_pad[][] _stringWaiters = new eterpix_pad[0][];
        private int[] _stringWaiterCounts = new int[0];
        private int[] _imageState = new int[0];
        private Texture2D[] _textures = new Texture2D[0];
        private eterpix_pad[][] _imageHolders = new eterpix_pad[0][]; // 写真を待っている/表示しているPad
        private int[] _imageHolderCounts = new int[0];

        private eterpix_downloader _downloader;
        private bool _downloaderResolved = false;
        private VRCImageDownloader _ownImageDownloader; // downloaderが無いワールド用

        private void Start()
        {
            GameObject canonical = GameObject.Find(SingletonObjectName);
            if (canonical != null && canonical != this.gameObject)
            {
                Debug.LogWarning("[eterpix_pad_requester] Duplicate instance detected. Disabling self.");
                gameObject.SetActive(false);
                return;
            }

            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(ali.eterpix.eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<ali.eterpix.eterpix_debug>();
            }
        }

        private void OnDestroy()
        {
            if (_ownImageDownloader != null) _ownImageDownloader.Dispose();
        }

        // downloaderのStartより先に呼ばれても困らないよう、最初の要求の時に探す
        private void ResolveDownloader()
        {
            if (_downloaderResolved) return;
            _downloaderResolved = true;
            GameObject downloaderObj = GameObject.Find(eterpix_downloader.SingletonObjectName);
            if (downloaderObj != null) _downloader = downloaderObj.GetComponent<eterpix_downloader>();
            if (_downloader == null) _ownImageDownloader = new VRCImageDownloader();
        }

        // ---- Padからの要求 ----
        // 結果はPadのOnPadStringSuccess / OnPadStringErrorで返す
        public void RequestString(VRCUrl url, eterpix_pad pad)
        {
            if (url == null || pad == null) return;
            int index = GetOrAddEntry(url);
            if (index < 0) return;

            AddPad(_stringWaiters, _stringWaiterCounts, index, pad);
            if (_stringLoading[index]) return; // 取得中の要求に相乗りする

            _stringLoading[index] = true;
            ResolveDownloader();
            if (_downloader != null) _downloader.RequestPadString(url, this);
            else VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
        }

        // 写真はOnPadImageSuccessで渡す。共有のテクスチャなので、Padは破棄せずReleaseAllで手放す
        public void RequestImage(VRCUrl url, eterpix_pad pad)
        {
            if (url == null || pad == null) return;
            int index = GetOrAddEntry(url);
            if (index < 0) return;

            AddPad(_imageHolders, _imageHolderCounts, index, pad);
            if (_imageState[index] == ImageLoaded)
            {
                pad.OnPadImageSuccess(_keys[index], _textures[index]);
                return;
            }
            if (_imageState[index] == ImageLoading) return; // 取得中の要求に相乗りする

            _imageState[index] = ImageLoading;
            ResolveDownloader();
            if (_downloader != null) _downloader.RequestPadImage(url, this);
            else _ownImageDownloader.DownloadImage(url, null, (IUdonEventReceiver)this, null);
        }

        // Padが持っている要求と写真をすべて手放す(URLを変えた時・範囲を出た時)
        public void ReleaseAll(eterpix_pad pad)
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                if (_keys[i] == null) continue;
                RemovePad(_stringWaiters, _stringWaiterCounts, i, pad);
                RemovePad(_imageHolders, _imageHolderCounts, i, pad);

                // 誰も待たなくなった要求は、downloaderでまだ未送信なら取り消す(送信済みなら結果が届いてから片付ける)
                if (_downloader != null)
                {
                    if (_stringLoading[i] && _stringWaiterCounts[i] == 0 && _downloader.CancelPadString(_keys[i])) _stringLoading[i] = false;
                    if (_imageState[i] == ImageLoading && _imageHolderCounts[i] == 0 && _downloader.CancelPadImage(_keys[i])) _imageState[i] = ImageNone;
                }
                CleanupEntry(i);
            }
        }

        // ---- downloaderからの結果(downloaderが無い時は下の直接取得のコールバックから) ----
        public void OnTransportStringSuccess(string url, string text)
        {
            int index = FindEntry(url);
            if (index < 0) return;
            _stringLoading[index] = false;

            eterpix_pad[] waiters = TakePads(_stringWaiters, _stringWaiterCounts, index);
            CleanupEntry(index);
            for (int i = 0; i < waiters.Length; i++)
            {
                if (waiters[i] != null) waiters[i].OnPadStringSuccess(url, text);
            }
        }

        public void OnTransportStringError(string url, int errorCode, string error)
        {
            int index = FindEntry(url);
            if (index < 0) return;
            _stringLoading[index] = false;
            if (debugLog != null) debugLog.LogWarning($"[eterpix_pad_requester] String load error ({errorCode}): {error}");

            eterpix_pad[] waiters = TakePads(_stringWaiters, _stringWaiterCounts, index);
            CleanupEntry(index);
            for (int i = 0; i < waiters.Length; i++)
            {
                if (waiters[i] != null) waiters[i].OnPadStringError(url, errorCode, error);
            }
        }

        public void OnTransportImageSuccess(string url, Texture2D texture)
        {
            int index = FindEntry(url);
            if (index < 0 || _imageHolderCounts[index] == 0)
            {
                // 届く前に全員が手放した
                if (texture != null) Destroy(texture);
                if (index >= 0)
                {
                    _imageState[index] = ImageNone;
                    CleanupEntry(index);
                }
                return;
            }

            _textures[index] = texture;
            _imageState[index] = ImageLoaded;
            eterpix_pad[] holders = CopyPads(_imageHolders, _imageHolderCounts, index);
            for (int i = 0; i < holders.Length; i++)
            {
                if (holders[i] != null) holders[i].OnPadImageSuccess(url, texture);
            }
        }

        public void OnTransportImageError(string url, string error)
        {
            int index = FindEntry(url);
            if (index < 0) return;
            _imageState[index] = ImageNone;
            if (debugLog != null) debugLog.LogWarning($"[eterpix_pad_requester] Image load error: {error}");

            // 待っていたPadは手放さない(エラー表示のまま残り、URLの入れ直しで取り直す)
            eterpix_pad[] holders = CopyPads(_imageHolders, _imageHolderCounts, index);
            CleanupEntry(index);
            for (int i = 0; i < holders.Length; i++)
            {
                if (holders[i] != null) holders[i].OnPadImageError(url, error);
            }
        }

        // ---- downloaderが無いときの直接取得のコールバック ----
        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            OnTransportStringSuccess(result.Url.Get(), result.Result);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            OnTransportStringError(result.Url.Get(), result.ErrorCode, result.Error);
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            OnTransportImageSuccess(result.Url.Get(), result.Result);
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            OnTransportImageError(result.Url.Get(), result.ErrorMessage);
        }

        // ---- URLごとの記録 ----
        private int FindEntry(string key)
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                if (_keys[i] != null && _keys[i] == key) return i;
            }
            return -1;
        }

        private int GetOrAddEntry(VRCUrl url)
        {
            string key = url.Get();
            if (string.IsNullOrEmpty(key)) return -1;
            int index = FindEntry(key);
            if (index >= 0) return index;

            for (int i = 0; i < _keys.Length; i++)
            {
                if (_keys[i] == null)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                index = _keys.Length;
                GrowEntries(_keys.Length + 4);
            }

            _keys[index] = key;
            _urls[index] = url;
            _stringLoading[index] = false;
            _stringWaiterCounts[index] = 0;
            _imageState[index] = ImageNone;
            _textures[index] = null;
            _imageHolderCounts[index] = 0;
            return index;
        }

        // 誰も待っておらず使ってもいない記録を空ける(写真は破棄する)。取得中のものは結果が届くまで残す
        private void CleanupEntry(int index)
        {
            if (_imageHolderCounts[index] == 0 && _imageState[index] == ImageLoaded)
            {
                if (_textures[index] != null) Destroy(_textures[index]);
                _textures[index] = null;
                _imageState[index] = ImageNone;
                if (debugLog != null) debugLog.Log($"[eterpix_pad_requester] Released texture: {_keys[index]}");
            }

            if (_stringLoading[index] || _imageState[index] == ImageLoading) return;
            if (_stringWaiterCounts[index] > 0 || _imageHolderCounts[index] > 0) return;

            _keys[index] = null;
            _urls[index] = null;
        }

        private void GrowEntries(int size)
        {
            string[] keys = new string[size];
            VRCUrl[] urls = new VRCUrl[size];
            bool[] stringLoading = new bool[size];
            eterpix_pad[][] stringWaiters = new eterpix_pad[size][];
            int[] stringWaiterCounts = new int[size];
            int[] imageState = new int[size];
            Texture2D[] textures = new Texture2D[size];
            eterpix_pad[][] imageHolders = new eterpix_pad[size][];
            int[] imageHolderCounts = new int[size];

            for (int i = 0; i < size; i++)
            {
                if (i < _keys.Length)
                {
                    keys[i] = _keys[i];
                    urls[i] = _urls[i];
                    stringLoading[i] = _stringLoading[i];
                    stringWaiters[i] = _stringWaiters[i];
                    stringWaiterCounts[i] = _stringWaiterCounts[i];
                    imageState[i] = _imageState[i];
                    textures[i] = _textures[i];
                    imageHolders[i] = _imageHolders[i];
                    imageHolderCounts[i] = _imageHolderCounts[i];
                }
                else
                {
                    stringWaiters[i] = new eterpix_pad[4];
                    imageHolders[i] = new eterpix_pad[4];
                }
            }

            _keys = keys;
            _urls = urls;
            _stringLoading = stringLoading;
            _stringWaiters = stringWaiters;
            _stringWaiterCounts = stringWaiterCounts;
            _imageState = imageState;
            _textures = textures;
            _imageHolders = imageHolders;
            _imageHolderCounts = imageHolderCounts;
        }

        // ---- Padの一覧(URLごと。可変長を模した固定長+件数管理) ----
        private void AddPad(eterpix_pad[][] lists, int[] counts, int index, eterpix_pad pad)
        {
            eterpix_pad[] list = lists[index];
            int count = counts[index];
            for (int i = 0; i < count; i++)
            {
                if (list[i] == pad) return;
            }
            if (count >= list.Length)
            {
                eterpix_pad[] bigger = new eterpix_pad[list.Length + 4];
                for (int i = 0; i < list.Length; i++) bigger[i] = list[i];
                list = bigger;
                lists[index] = list;
            }
            list[count] = pad;
            counts[index] = count + 1;
        }

        private void RemovePad(eterpix_pad[][] lists, int[] counts, int index, eterpix_pad pad)
        {
            eterpix_pad[] list = lists[index];
            int count = counts[index];
            for (int i = 0; i < count; i++)
            {
                if (list[i] != pad) continue;
                for (int j = i + 1; j < count; j++) list[j - 1] = list[j];
                list[count - 1] = null;
                counts[index] = count - 1;
                return;
            }
        }

        // 通知の途中でPadが一覧を書き換えても困らないよう、写しを返す
        private eterpix_pad[] CopyPads(eterpix_pad[][] lists, int[] counts, int index)
        {
            int count = counts[index];
            eterpix_pad[] copy = new eterpix_pad[count];
            for (int i = 0; i < count; i++) copy[i] = lists[index][i];
            return copy;
        }

        private eterpix_pad[] TakePads(eterpix_pad[][] lists, int[] counts, int index)
        {
            eterpix_pad[] copy = CopyPads(lists, counts, index);
            eterpix_pad[] list = lists[index];
            for (int i = 0; i < counts[index]; i++) list[i] = null;
            counts[index] = 0;
            return copy;
        }
    }
}
