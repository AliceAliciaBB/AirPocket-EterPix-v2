using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Data;

namespace ali.eterpix
{
    // 中位: N枚リングバッファ型(4枚/36枚など。Nは子要素数で決まる)。方針.md「36枚表示(N枚)」
    // 「選択軸」「Colliderの仕様」を参照。新規投稿到着分だけ押し出し、
    // 押し出されたセルはeterpix_cell.UpdateItemInfo内で自動的にReleaseTextureされる。
    // (eterpix_selection_order列挙体はeterpix_middle_nav.csで定義、共用)
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class eterpix_middle_ring : UdonSharpBehaviour
    {
        [Header("下位セル一覧 (未設定ならStart()で子階層から自動収集)")]
        [SerializeField] private eterpix_cell[] cells;

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

        [Header("R18表示可否フラグ (上位側フィルタと重複可。中位個別に絞る場合のみ使用)")]
        [SerializeField] private bool showR18Posts = true;

        [Header("読み込み開始距離用BoxCollider (0なら常時ロード対象=優先度に従って読み込む)")]
        [SerializeField] private BoxCollider loadTriggerCollider;
        [SerializeField] private float loadStartDistance = 0f;

        [Header("デバッグログ")]
        [SerializeField] private eterpix_debug debugLog;

        private eterpix_json_loader _jsonLoader;
        private int[] _assignedPostIndex; // cells[i]に現在割り当てられているpostIndex(-1=未割当)
        private bool _loadEnabled = true; // BoxColliderで管理する「現在ロード対象として必要か」

        private void Start()
        {
            if (debugLog == null)
            {
                GameObject debugObj = GameObject.Find(eterpix_debug.SingletonObjectName);
                if (debugObj != null) debugLog = debugObj.GetComponent<eterpix_debug>();
            }

            if (cells == null || cells.Length == 0)
            {
                cells = GetComponentsInChildren<eterpix_cell>(true);
            }

            _assignedPostIndex = new int[cells.Length];
            for (int i = 0; i < _assignedPostIndex.Length; i++) _assignedPostIndex[i] = -1;

            if (loadTriggerCollider != null) loadTriggerCollider.isTrigger = true;
            _loadEnabled = loadStartDistance <= 0f; // 0 = 自動ロードなし判定は上位優先度に一任(常にロード対象)

            if (selectionOrder == eterpix_selection_order.FixedSeedShuffle && Networking.IsOwner(Networking.LocalPlayer, gameObject) && sharedSeed == 0)
            {
                sharedSeed = Random.Range(1, int.MaxValue);
                RequestSerialization();
            }

            SendCustomEventDelayedFrames(nameof(FetchLoaders), 1);
        }

        public void FetchLoaders()
        {
            GameObject jsonObj = GameObject.Find(eterpix_json_loader.SingletonObjectName);
            if (jsonObj != null) _jsonLoader = jsonObj.GetComponent<eterpix_json_loader>();

            GameObject imgObj = GameObject.Find(eterpix_image_loader.SingletonObjectName);
            if (imgObj != null)
            {
                eterpix_image_loader imageLoader = imgObj.GetComponent<eterpix_image_loader>();
                if (imageLoader != null && urlList != null) imageLoader.SetUrlList(urlList);
            }

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
            if (!_loadEnabled) return;
            AssignCells();
        }

        // ---- 手動更新(ボタンから呼ぶ) ----
        public void ManualRefresh()
        {
            if (_jsonLoader != null) _jsonLoader.RequestManualRefresh();
        }

        // ---- 距離判定(BoxColliderトリガー。ローカルプレイヤーのみ判定) ----
        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (!player.isLocal) return;
            _loadEnabled = true;
            AssignCells();
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (!player.isLocal) return;
            if (loadStartDistance <= 0f) return; // 0=常時ロード対象なので退出しても解除しない
            _loadEnabled = false;
            ClearAllCells();
        }

        // ---- 選択軸で並べたpost一覧を作り、先頭N件をセルへ割り当てる ----
        private void AssignCells()
        {
            if (_jsonLoader == null || _jsonLoader.jsonArray == null || cells == null) return;

            int[] order = BuildOrder();

            // 各セルの対応向きに合う投稿を、先頭から順に消費していく
            bool[] used = new bool[order.Length];
            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                eterpix_cell cell = cells[cellIndex];
                if (cell == null) continue;

                int foundPostIndex = -1;
                for (int i = 0; i < order.Length; i++)
                {
                    if (used[i]) continue;
                    int postIndex = order[i];
                    if (!TryGetPost(postIndex, out DataDictionary data)) continue;
                    int imgRotation = ReadInt(data, "img_rotation", 0);
                    if (!cell.AcceptsOrientation(imgRotation)) continue;

                    used[i] = true;
                    foundPostIndex = postIndex;
                    break;
                }

                if (foundPostIndex < 0)
                {
                    if (_assignedPostIndex[cellIndex] != -1)
                    {
                        cell.ResetItem();
                        _assignedPostIndex[cellIndex] = -1;
                    }
                    continue;
                }

                if (_assignedPostIndex[cellIndex] == foundPostIndex) continue; // 変化なし

                _assignedPostIndex[cellIndex] = foundPostIndex;
                ApplyToCell(cell, foundPostIndex);
            }
        }

        private void ClearAllCells()
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != null) cells[i].ResetItem();
                _assignedPostIndex[i] = -1;
            }
        }

        private void ApplyToCell(eterpix_cell cell, int postIndex)
        {
            if (!TryGetPost(postIndex, out DataDictionary data)) return;

            int collageId = ReadInt(data, "collage_id", -1);
            int imgPos = ReadInt(data, "img_pos", -1);
            int imgRotation = ReadInt(data, "img_rotation", 0);
            string description = ReadString(data, "description", "");
            string userName = ReadString(data, "user_name", "");
            string worldId = ReadString(data, "world_vrc_id", "");
            string worldName = "";
            string worldDescription = "";

            if (!string.IsNullOrEmpty(worldId) && _jsonLoader.worldData != null &&
                _jsonLoader.worldData.TryGetValue(worldId, out DataToken worldEntryToken) &&
                worldEntryToken.TokenType == TokenType.DataDictionary)
            {
                DataDictionary worldEntry = worldEntryToken.DataDictionary;
                worldName = ReadString(worldEntry, "world_name", "");
                worldDescription = ReadString(worldEntry, "confirmed_description", "");
            }

            cell.UpdateItemInfo(postIndex, collageId, imgPos, imgRotation, description, userName, worldName, worldDescription, worldId);
        }

        private bool TryGetPost(int postIndex, out DataDictionary data)
        {
            data = null;
            if (!_jsonLoader.jsonArray.TryGetValue(postIndex, out DataToken token) ||
                token.TokenType != TokenType.DataDictionary) return false;

            data = token.DataDictionary;

            if (!showR18Posts)
            {
                bool isR18 = data.TryGetValue("is_r18", out DataToken r18Token) &&
                    r18Token.TokenType == TokenType.Boolean && r18Token.Boolean;
                if (isR18) return false;
            }

            return true;
        }

        // ---- 選択軸の構築(先頭N件を「新しい/優先される」順で並べる) ----
        private int[] BuildOrder()
        {
            int total = _jsonLoader.jsonArray.Count;
            int count = Mathf.Max(0, total - startIndex);
            int[] order = new int[count];
            for (int i = 0; i < count; i++) order[i] = startIndex + i;

            if (selectionOrder == eterpix_selection_order.Arrival)
            {
                // jsonArrayはAPI側でcreated_at DESC(先頭が最新)なので、そのままで「新しい順」になっている
            }
            else if (selectionOrder == eterpix_selection_order.Ascending || selectionOrder == eterpix_selection_order.Descending)
            {
                SortByLikes(order, selectionOrder == eterpix_selection_order.Descending);
            }
            else if (selectionOrder == eterpix_selection_order.FixedSeedShuffle)
            {
                ShuffleWithSeed(order, sharedSeed);
            }

            return order;
        }

        private void SortByLikes(int[] order, bool descending)
        {
            for (int i = 1; i < order.Length; i++)
            {
                int key = order[i];
                int keyLikes = ReadLikes(key);
                int j = i - 1;
                while (j >= 0 && (descending ? ReadLikes(order[j]) < keyLikes : ReadLikes(order[j]) > keyLikes))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }
        }

        private int ReadLikes(int postIndex)
        {
            if (TryGetPost(postIndex, out DataDictionary data)) return ReadInt(data, "likes", 0);
            return 0;
        }

        private void ShuffleWithSeed(int[] order, int seed)
        {
            System.Random rng = new System.Random(seed);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                int tmp = order[i];
                order[i] = order[j];
                order[j] = tmp;
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
