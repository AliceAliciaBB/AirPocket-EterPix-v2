using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Data;

namespace ali.eterpix
{
    // U#はネストされた型宣言をサポートしないため、名前空間直下に定義する
    // (eterpix_middle_nav/eterpix_middle_ringで共用)
    public enum eterpix_selection_order { Arrival, Ascending, Descending, FixedSeedShuffle }

    // 中位: 一枚表示(ナビゲーション型)。方針.md「一枚表示」「選択軸」「送り操作」を参照。
    // 下位セルは常に1つで、左右ボタンでインデックスを前後に移動する。
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_middle_nav : UdonSharpBehaviour
    {
        [Header("下位セル (常に1つ)")]
        [SerializeField] private eterpix_cell cell;

        [Header("URL設定 (collage_id -> URL。上位imageLoaderへ登録する)")]
        [SerializeField] private vrcurllist urlList;

        [Header("json_URL (上位jsonLoaderへ登録する)")]
        [SerializeField] private VRC.SDKBase.VRCUrl requestUrl;

        [Header("表示アイテムの並べ方(選択軸)")]
        [SerializeField] private eterpix_selection_order selectionOrder = eterpix_selection_order.Arrival;

        [Header("固定シードシャッフル用シード(オーナーが決定し同期)")]
        [UdonSynced] public int sharedSeed = 0;

        [Header("投稿数オフセット")]
        [SerializeField] private int startIndex = 0;

        [Header("デバッグログ")]
        [SerializeField] private eterpix_debug debugLog;

        private eterpix_json_loader _jsonLoader;
        private eterpix_image_loader _imageLoader;

        private int[] _order; // jsonArray内インデックスを並べ替えた配列
        private int _cursor = 0; // _order内の現在位置

        private void Start()
        {
            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<eterpix_debug>();
            }

            if (selectionOrder == eterpix_selection_order.FixedSeedShuffle && Networking.IsOwner(Networking.LocalPlayer, gameObject) && sharedSeed == 0)
            {
                sharedSeed = Random.Range(1, int.MaxValue);
                RequestSerialization();
            }

            // 上位はStart()時点で初期化済みとは限らないため、1フレーム遅延させて取得する
            SendCustomEventDelayedFrames(nameof(FetchLoaders), 1);
        }

        public void FetchLoaders()
        {
            GameObject jsonObj = GameObject.Find(eterpix_json_loader.SingletonObjectName);
            if (jsonObj != null) _jsonLoader = jsonObj.GetComponent<eterpix_json_loader>();

            GameObject imgObj = GameObject.Find(eterpix_image_loader.SingletonObjectName);
            if (imgObj != null) _imageLoader = imgObj.GetComponent<eterpix_image_loader>();

            if (_imageLoader != null && urlList != null) _imageLoader.SetUrlList(urlList);

            if (_jsonLoader != null)
            {
                if (requestUrl != null) _jsonLoader.SetRequestUrl(requestUrl);
                _jsonLoader.RegisterMiddle(this);
            }

            OnJsonUpdated();
        }

        // ---- 上位から呼ばれる更新通知 ----
        public void OnJsonUpdated()
        {
            RebuildOrder();
            DisplayAtCursor();
        }

        // ---- 手動更新(ボタンから呼ぶ) ----
        public void ManualRefresh()
        {
            if (_jsonLoader != null) _jsonLoader.RequestManualRefresh();
        }

        // ---- 送り操作 ----
        public void NavNext()
        {
            MoveCursor(1);
        }

        public void NavPrev()
        {
            MoveCursor(-1);
        }

        private void MoveCursor(int step)
        {
            if (_order == null || _order.Length == 0) return;

            for (int attempts = 0; attempts < _order.Length; attempts++)
            {
                _cursor = ((_cursor + step) % _order.Length + _order.Length) % _order.Length;
                if (TryDisplayAtCursor()) return;
            }

            if (debugLog != null) debugLog.LogWarning("[eterpix_middle_nav] No post matches this cell's accepted orientation.");
        }

        private void DisplayAtCursor()
        {
            if (_order == null || _order.Length == 0) return;
            if (!TryDisplayAtCursor())
            {
                // 現在位置が対応外の向きなら、次を探す
                MoveCursor(1);
            }
        }

        private bool TryDisplayAtCursor()
        {
            if (_jsonLoader == null || _jsonLoader.jsonArray == null) return false;
            if (_cursor < 0 || _cursor >= _order.Length) return false;

            int postIndex = _order[_cursor];
            if (!_jsonLoader.jsonArray.TryGetValue(postIndex, out DataToken postToken) ||
                postToken.TokenType != TokenType.DataDictionary) return false;

            DataDictionary data = postToken.DataDictionary;
            int imgRotation = ReadInt(data, "img_rotation", 0);

            if (cell != null && !cell.AcceptsOrientation(imgRotation)) return false;

            ApplyToCell(postIndex, data);
            return true;
        }

        private void ApplyToCell(int postIndex, DataDictionary data)
        {
            if (cell == null) return;

            int collageId = ReadInt(data, "collage_id", -1);
            int imgPos = ReadInt(data, "img_pos", -1);
            int imgRotation = ReadInt(data, "img_rotation", 0);
            string description = ReadString(data, "description", "");
            string userName = ReadString(data, "user_name", "");
            string worldId = ReadString(data, "world_vrc_id", "");
            string worldName = "";
            string worldDescription = "";

            if (!string.IsNullOrEmpty(worldId) && _jsonLoader != null && _jsonLoader.worldData != null &&
                _jsonLoader.worldData.TryGetValue(worldId, out DataToken worldEntryToken) &&
                worldEntryToken.TokenType == TokenType.DataDictionary)
            {
                DataDictionary worldEntry = worldEntryToken.DataDictionary;
                worldName = ReadString(worldEntry, "world_name", "");
                worldDescription = ReadString(worldEntry, "confirmed_description", "");
            }

            cell.UpdateItemInfo(postIndex, collageId, imgPos, imgRotation, description, userName, worldName, worldDescription, worldId);
        }

        // ---- 選択軸(順序)の構築 ----
        private void RebuildOrder()
        {
            if (_jsonLoader == null || _jsonLoader.jsonArray == null)
            {
                _order = new int[0];
                return;
            }

            int total = _jsonLoader.jsonArray.Count;
            int count = Mathf.Max(0, total - startIndex);
            _order = new int[count];
            for (int i = 0; i < count; i++) _order[i] = startIndex + i;

            if (selectionOrder == eterpix_selection_order.Ascending || selectionOrder == eterpix_selection_order.Descending)
            {
                SortByLikes(selectionOrder == eterpix_selection_order.Descending);
            }
            else if (selectionOrder == eterpix_selection_order.FixedSeedShuffle)
            {
                ShuffleWithSeed(sharedSeed);
            }
            // Arrivalはそのまま(到着順)

            _cursor = Mathf.Clamp(_cursor, 0, Mathf.Max(0, _order.Length - 1));
        }

        private void SortByLikes(bool descending)
        {
            // 単純な挿入ソート(件数がそれほど多くない前提)
            for (int i = 1; i < _order.Length; i++)
            {
                int key = _order[i];
                int keyLikes = ReadLikes(key);
                int j = i - 1;
                while (j >= 0 && (descending ? ReadLikes(_order[j]) < keyLikes : ReadLikes(_order[j]) > keyLikes))
                {
                    _order[j + 1] = _order[j];
                    j--;
                }
                _order[j + 1] = key;
            }
        }

        private int ReadLikes(int postIndex)
        {
            if (_jsonLoader.jsonArray.TryGetValue(postIndex, out DataToken token) && token.TokenType == TokenType.DataDictionary)
            {
                return ReadInt(token.DataDictionary, "likes", 0);
            }
            return 0;
        }

        private void ShuffleWithSeed(int seed)
        {
            System.Random rng = new System.Random(seed);
            for (int i = _order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                int tmp = _order[i];
                _order[i] = _order[j];
                _order[j] = tmp;
            }
        }

        private int ReadInt(DataDictionary data, string key, int fallback)
        {
            if (!data.TryGetValue(key, out DataToken token)) return fallback;
            if (token.TokenType == TokenType.Double) return (int)token.Double;
            if (token.TokenType == TokenType.String && int.TryParse(token.String, out int parsed)) return parsed;
            return fallback;
        }

        private string ReadString(DataDictionary data, string key, string fallback)
        {
            if (data.TryGetValue(key, out DataToken token) && token.TokenType == TokenType.String) return token.String;
            return fallback;
        }
    }
}
