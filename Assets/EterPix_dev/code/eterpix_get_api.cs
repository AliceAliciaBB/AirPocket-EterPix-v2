using UdonSharp;
using UnityEngine;
using VRC.SDK3.Image;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Data;
using TMPro;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using System.Linq;
using UnityEditor;
#endif

namespace ali.eterpix
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_get_api : UdonSharpBehaviour
    {
        [Header("↓ ★json-api-同じモノリスのスプリクトを指定してください。")]
        [SerializeField] private eterpix_json_api jsonApiInstance;
        [Header("↓ ■img-api-同じモノリスのスプリクトを指定してください。")]
        [SerializeField] private TextureManager textureManager;
        [Header("↓ ★ログの内容を変更する json<->通信ログ")]
        [SerializeField] private bool enableJsonLogging = false;
        [Header("↓ ★R18投稿(is_r18=true)をワールドに表示するか。Inspectorのみで変更可能（ゲーム内からは変更不可）")]
        [SerializeField] private bool showR18Posts = false;
        [SerializeField] private eterpix_json_get[] eterpix_json_gets;
        [SerializeField] private eterpix_photo_get[] eterpix_photo_gets;

        [SerializeField] private TMP_Text itemNameText;

        [Header("↓ json_URL-jsonのAPIをURLでここで指定してください。(最大3つ)")]
        [SerializeField] private VRCUrl requestUrl1;
        [SerializeField] private VRCUrl requestUrl2;
        [SerializeField] private VRCUrl requestUrl3;

        [HideInInspector] public DataList jsonArray;
        [HideInInspector] public DataDictionary worldData;

        private bool isForceUpdate = false;
        private int currentUrlIndex = 0; // 0=URL1, 1=URL2, 2=URL3
        private string commLog = "";

        // リクエストが応答無しでハングしていないかを検知するためのウォッチドッグ
        private const float RequestTimeoutSeconds = 15f;
        private bool _isRequestPending = false;

        private void Start()
        {
            AppendLog("ワールド読み込み完了");
        }

        private VRCUrl GetCurrentUrl()
        {
            if (currentUrlIndex == 1) return requestUrl2;
            if (currentUrlIndex == 2) return requestUrl3;
            return requestUrl1;
        }

        /// <summary>
        /// 通信ログに1行追記し、enableJsonLoggingがfalseなら表示も更新する
        /// （TextureManager等、外部からの画像取得ログも受け付ける）
        /// </summary>
        public void AppendLog(string message)
        {
            string timeStamp = System.DateTime.Now.ToString("HH:mm:ss");
            commLog += $"[{timeStamp}] {message}\n";

            if (!enableJsonLogging && itemNameText != null)
            {
                itemNameText.text = commLog;
            }
        }

        public void SwitchToUrl1()
        {
            currentUrlIndex = 0;
            AppendLog("URL1に切り替えました");
            if (textureManager != null) { textureManager.currentUrlListIndex = 0; textureManager.ResetTimestampCache(); }
            UpdateItems();
        }

        public void SwitchToUrl2()
        {
            currentUrlIndex = 1;
            AppendLog("URL2に切り替えました");
            if (textureManager != null) { textureManager.currentUrlListIndex = 1; textureManager.ResetTimestampCache(); }
            UpdateItems();
        }

        public void SwitchToUrl3()
        {
            currentUrlIndex = 2;
            AppendLog("URL3に切り替えました");
            if (textureManager != null) { textureManager.currentUrlListIndex = 2; textureManager.ResetTimestampCache(); }
            UpdateItems();
        }

        public void collme(eterpix_json_get item)
        {
            int len = eterpix_json_gets != null ? eterpix_json_gets.Length : 0;
            var newArr = new eterpix_json_get[len + 1];

            for (int i = 0; i < len; i++) newArr[i] = eterpix_json_gets[i];

            newArr[len] = item;
            eterpix_json_gets = newArr;
        }

        public void collme(eterpix_photo_get item)
        {
            int len = eterpix_photo_gets != null ? eterpix_photo_gets.Length : 0;
            var newArr = new eterpix_photo_get[len + 1];

            for (int i = 0; i < len; i++) newArr[i] = eterpix_photo_gets[i];

            newArr[len] = item;
            eterpix_photo_gets = newArr;
        }

        public void UpdateItems()
        {
            if (jsonApiInstance != null)
            {
                if (enableJsonLogging) itemNameText.text = "Now loading...";
                isForceUpdate = false;
                AppendLog($"URL{currentUrlIndex + 1}を取得中...");
                Debug.Log("[eterpix_get_api] Fetching JSON...");
                BeginRequestWatchdog();
                jsonApiInstance.SendRequest((IUdonEventReceiver)this, GetCurrentUrl());
            }
            else
            {
                Debug.LogError("[eterpix_get_api] jsonApiInstance is null");
            }
        }

        /// <summary>
        /// キャッシュを無視してすべての画像を強制再取得する
        /// </summary>
        public void ForceUpdateItems()
        {
            if (jsonApiInstance != null)
            {
                if (enableJsonLogging) itemNameText.text = "Now loading...";
                isForceUpdate = true;
                AppendLog($"URL{currentUrlIndex + 1}を強制更新中...");
                Debug.Log("[eterpix_get_api] Force update: Fetching JSON...");
                BeginRequestWatchdog();
                jsonApiInstance.SendRequest((IUdonEventReceiver)this, GetCurrentUrl());
            }
            else
            {
                Debug.LogError("[eterpix_get_api] jsonApiInstance is null");
            }
        }

        /// <summary>
        /// リクエスト送信直前に呼ぶ。RequestTimeoutSeconds後に応答がまだ無ければ警告ログを出す。
        /// </summary>
        private void BeginRequestWatchdog()
        {
            _isRequestPending = true;
            SendCustomEventDelayedSeconds(nameof(CheckRequestTimeout), RequestTimeoutSeconds);
        }

        /// <summary>
        /// ウォッチドッグ本体。OnStringLoadSuccess/OnStringLoadErrorのどちらも
        /// 呼ばれないまま応答が無い（通信がハングしている）場合に警告を出す。
        /// </summary>
        public void CheckRequestTimeout()
        {
            if (_isRequestPending)
            {
                Debug.LogWarning($"[eterpix_get_api] No response (success/error) after {RequestTimeoutSeconds}s — request may be hanging");
                AppendLog($"{RequestTimeoutSeconds}秒応答がありません（通信がハングしている可能性）");
            }
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            _isRequestPending = false;

            if (enableJsonLogging) itemNameText.text = result.Result;
            AppendLog($"通信成功（URL{currentUrlIndex + 1}）");

            if (!jsonApiInstance.OnSuccess(result))
            {
                return;
            }

            if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken token) ||
                token.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError("[eterpix_get_api] Failed to parse JSON or unexpected format");
                AppendLog("JSON解析に失敗しました");
                return;
            }

            DataDictionary rootDict = token.DataDictionary;

            // "posts" から投稿リストを取得
            if (!rootDict.TryGetValue("posts", out DataToken postToken) ||
                postToken.TokenType != TokenType.DataList)
            {
                Debug.LogError("[eterpix_get_api] 'posts' key not found or not a list");
                AppendLog("'posts'キーが見つかりません");
                return;
            }
            jsonArray = showR18Posts ? postToken.DataList : FilterOutR18Posts(postToken.DataList);
            Debug.Log($"[eterpix_get_api] jsonArray populated with {jsonArray.Count} posts");

            // "world_data" からワールド情報辞書を取得(world_vrc_id -> {world_name, confirmed_description, is_provisional})
            if (rootDict.TryGetValue("world_data", out DataToken worldDataToken) &&
                worldDataToken.TokenType == TokenType.DataDictionary)
            {
                worldData = worldDataToken.DataDictionary;
            }
            else
            {
                worldData = null;
            }

            // "collage_img" からタイムスタンプを取得して画像ダウンロード開始
            if (textureManager != null)
            {
                string[] timestamps = ParseTimestamps(rootDict);
                if (timestamps != null)
                {
                    if (isForceUpdate)
                        textureManager.ForceDownloadWithTimestamps(timestamps);
                    else
                        textureManager.StartDownloadWithTimestamps(timestamps);
                }
            }
            isForceUpdate = false;

            // 各 eterpix_json_get に通知
            Debug.Log($"[API] eterpix_json_gets.Length = {eterpix_json_gets.Length}");
            for (int i = 0; i < eterpix_json_gets.Length; i++)
            {
                if (eterpix_json_gets[i] != null)
                {
                    Debug.Log($"[API] Calling GetItems() on index {i}");
                    eterpix_json_gets[i].GetItems();
                }
                else
                {
                    Debug.LogWarning($"[API] eterpix_json_gets[{i}] is NULL!");
                }
            }

            // 各 eterpix_photo_get に通知
            if (eterpix_photo_gets != null)
            {
                for (int i = 0; i < eterpix_photo_gets.Length; i++)
                {
                    if (eterpix_photo_gets[i] != null)
                    {
                        eterpix_photo_gets[i].DisplayItems();
                    }
                }
            }
        }

        /// <summary>
        /// posts配列から is_r18=true の投稿を除外した新しいリストを返す
        /// （showR18PostsはInspectorのみで変更可能な設定で、ゲーム内からは変更できない）
        /// </summary>
        private DataList FilterOutR18Posts(DataList posts)
        {
            DataList filtered = new DataList();

            for (int i = 0; i < posts.Count; i++)
            {
                if (!posts.TryGetValue(i, out DataToken postToken)) continue;

                bool isR18 = false;
                if (postToken.TokenType == TokenType.DataDictionary &&
                    postToken.DataDictionary.TryGetValue("is_r18", out DataToken isR18Token) &&
                    isR18Token.TokenType == TokenType.Boolean)
                {
                    isR18 = isR18Token.Boolean;
                }

                if (!isR18) filtered.Add(postToken);
            }

            return filtered;
        }

        /// <summary>
        /// メインJSONのルート辞書からタイムスタンプ配列を取得
        /// 形式: {"collage_img": {"0": "2026-03-04 12:34:56", "1": "...", ...}}
        /// キーはスロット番号（10進数文字列、存在するスロットのみ含まれる）
        /// </summary>
        private string[] ParseTimestamps(DataDictionary rootDict)
        {
            if (!rootDict.TryGetValue("collage_img", out DataToken collageImgToken) ||
                collageImgToken.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError("[eterpix_get_api] Could not find 'collage_img' object in JSON");
                AppendLog("'collage_img'キーが見つかりません");
                return null;
            }

            DataDictionary timestampDict = collageImgToken.DataDictionary;
            string[] timestamps = new string[16];

            for (int i = 0; i < 16; i++)
            {
                string key = i.ToString();
                if (timestampDict.TryGetValue(key, out DataToken tsToken) &&
                    tsToken.TokenType == TokenType.String)
                {
                    timestamps[i] = tsToken.String;
                }
                else
                {
                    timestamps[i] = null; // 該当スロットが未存在、スキップ対象
                }
            }

            int validCount = 0;
            for (int i = 0; i < 16; i++)
            {
                if (timestamps[i] != null) validCount++;
            }
            Debug.Log($"[eterpix_get_api] Parsed {validCount} valid timestamps");

            return timestamps;
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            _isRequestPending = false;

            Debug.LogError($"[eterpix_get_api] String load error: {result.Error}");

            if (result.Error != null && result.Error.ToLower().Contains("timeout"))
            {
                AppendLog("タイムアウトしました");
            }
            else
            {
                AppendLog($"通信エラー: {result.Error}");
            }

            if (enableJsonLogging) itemNameText.text = "Load error";
        }
    }
}
