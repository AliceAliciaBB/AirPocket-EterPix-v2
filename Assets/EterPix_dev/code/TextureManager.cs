using UdonSharp;
using UnityEngine;
using VRC.SDK3.Image;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ali.eterpix
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TextureManager : UdonSharpBehaviour
    {
        [Header("URL設定 (最大3つ切り替え対応)")]
        // urlList: 16個の合成画像URL（00〜0F）を設定
        [SerializeField] private vrcurllist urlList;
        [SerializeField] private vrcurllist urlList2;
        [SerializeField] private vrcurllist urlList3;

        [Header("↓ 通信ログ連携先（任意・eterpix_get_apiの通信ログに画像取得状況を表示する）")]
        [SerializeField] private eterpix_get_api logTarget;

        [HideInInspector] public int currentUrlListIndex = 0; // 0=urlList, 1=urlList2, 2=urlList3

        private vrcurllist GetCurrentUrlList()
        {
            if (currentUrlListIndex == 1 && urlList2 != null) return urlList2;
            if (currentUrlListIndex == 2 && urlList3 != null) return urlList3;
            return urlList;
        }

        /// <summary>
        /// タイムスタンプキャッシュをリセット（URL切り替え時に呼ぶ）
        /// </summary>
        public void ResetTimestampCache()
        {
            for (int i = 0; i < maxTextures; i++)
                cachedTimestamps[i] = "";
            Debug.Log("[TextureManager] Timestamp cache reset");
        }

        [Header("ダウンロード設定")]
        [SerializeField] private int maxTextures = 16;

        // テクスチャ配列（公開して直接アクセス可能）
        [HideInInspector] public Texture2D[] textures;

        // ダウンロード完了フラグ
        [HideInInspector] public bool[] downloadCompleted;

        // 待機中のアイテムリスト（set_index -> eterpix_itemの配列）
        private eterpix_item[][] waitingItems;

        // 待機中の eterpix_photo_vew リスト（set_index -> eterpix_photo_vewの配列）
        private eterpix_photo_vew[][] waitingPhotoViews;

        private VRCImageDownloader imageDownloader;
        private int currentDownloadIndex = 0;

        // ★ ダウンロード中フラグを追加
        private bool isDownloading = false;

        // ★ タイムスタンプキャッシュ（更新チェック用）
        private string[] cachedTimestamps;

        // ★ 更新が必要なインデックスのリスト
        private int[] pendingDownloadIndices;
        private int pendingDownloadCount = 0;
        private int currentPendingIndex = 0;

        // ダウンロード成功後に cachedTimestamps へ反映する新タイムスタンプの一時保存先
        private string[] _pendingNewTimestamps;

        private void Start()
        {
            // 初期化
            textures = new Texture2D[maxTextures];
            downloadCompleted = new bool[maxTextures];
            cachedTimestamps = new string[maxTextures];
            pendingDownloadIndices = new int[maxTextures];
            _pendingNewTimestamps = new string[maxTextures];

            // 各set_indexごとに最大50個のアイテムを想定
            waitingItems = new eterpix_item[maxTextures][];
            waitingPhotoViews = new eterpix_photo_vew[maxTextures][];
            for (int i = 0; i < maxTextures; i++)
            {
                waitingItems[i] = new eterpix_item[50];
                waitingPhotoViews[i] = new eterpix_photo_vew[50];
                cachedTimestamps[i] = "";
            }

            imageDownloader = new VRCImageDownloader();
        }


        /// <summary>
        /// すべての画像のダウンロードを開始（従来の全件ダウンロード）
        /// </summary>
        public void StartDownloadAll()
        {
            // タイムスタンプなしで呼ばれた場合は全件ダウンロード
            string[] emptyTimestamps = new string[maxTextures];
            for (int i = 0; i < maxTextures; i++)
            {
                emptyTimestamps[i] = ""; // 空文字列で強制更新
            }
            StartDownloadWithTimestamps(emptyTimestamps);
        }

        /// <summary>
        /// キャッシュを無視してすべての画像を強制再ダウンロード（タイムスタンプなし）
        /// </summary>
        public void ForceDownloadAll()
        {
            Debug.Log("[TextureManager] ForceDownloadAll: clearing timestamp cache");
            for (int i = 0; i < maxTextures; i++)
                cachedTimestamps[i] = null;

            StartDownloadAll();
        }

        /// <summary>
        /// キャッシュを無視してすべての画像を強制再ダウンロード（タイムスタンプ順）
        /// </summary>
        /// <param name="newTimestamps">サーバーから取得した最新タイムスタンプ配列（順序付けに使用）</param>
        public void ForceDownloadWithTimestamps(string[] newTimestamps)
        {
            Debug.Log("[TextureManager] ForceDownloadWithTimestamps: clearing timestamp cache");
            for (int i = 0; i < maxTextures; i++)
                cachedTimestamps[i] = null;

            StartDownloadWithTimestamps(newTimestamps);
        }

        /// <summary>
        /// タイムスタンプを比較して更新が必要な画像のみダウンロード
        /// </summary>
        /// <param name="newTimestamps">サーバーから取得した最新タイムスタンプ配列</param>
        public void StartDownloadWithTimestamps(string[] newTimestamps)
        {
            vrcurllist activeList = GetCurrentUrlList();
            if (activeList == null || activeList.urlArray == null)
            {
                Debug.LogWarning("[TextureManager] urlList or urlArray is null");
                return;
            }

            // 既にダウンロード中なら何もしない
            if (isDownloading)
            {
                Debug.LogWarning("[TextureManager] Already downloading, skipping...");
                return;
            }

            // 更新が必要なインデックスを特定
            pendingDownloadCount = 0;
            int skippedCount = 0;

            int checkCount = Mathf.Min(maxTextures, newTimestamps.Length);
            for (int i = 0; i < checkCount; i++)
            {
                string newTs = newTimestamps[i];
                string cachedTs = cachedTimestamps[i];

                // null = 画像なし（サーバー側にデータが存在しない）→ スキップ
                if (newTs == null)
                {
                    skippedCount++;
                    Debug.Log($"[TextureManager] Index {i} has null timestamp, skipping (no image)");
                    continue;
                }

                // タイムスタンプが異なる場合、または初回ダウンロードの場合
                if (string.IsNullOrEmpty(cachedTs) || cachedTs != newTs)
                {
                    pendingDownloadIndices[pendingDownloadCount] = i;
                    pendingDownloadCount++;
                    downloadCompleted[i] = false;  // 再ダウンロード中は「未完了」に戻す
                    Debug.Log($"[TextureManager] Index {i} needs update: cached='{cachedTs}' -> new='{newTs}'");
                }
                else
                {
                    skippedCount++;
                    // キャッシュ済みテクスチャがあれば、待機中アイテムに通知
                    NotifyWaitingItems(i);
                }
            }

            Debug.Log($"[TextureManager] Timestamp check: {pendingDownloadCount} to download, {skippedCount} skipped (cached)");

            if (pendingDownloadCount == 0)
            {
                Debug.Log("[TextureManager] All images are up-to-date, no download needed");
                return;
            }

            // タイムスタンプ降順（新しい順）でダウンロード順をソート
            for (int i = 0; i < pendingDownloadCount - 1; i++)
            {
                for (int j = 0; j < pendingDownloadCount - 1 - i; j++)
                {
                    int a = pendingDownloadIndices[j];
                    int b = pendingDownloadIndices[j + 1];
                    string tsA = (a < newTimestamps.Length && !string.IsNullOrEmpty(newTimestamps[a])) ? newTimestamps[a] : "";
                    string tsB = (b < newTimestamps.Length && !string.IsNullOrEmpty(newTimestamps[b])) ? newTimestamps[b] : "";
                    if (tsA.CompareTo(tsB) < 0) // tsAが古い → 後ろに回す（新しい順）
                    {
                        pendingDownloadIndices[j] = b;
                        pendingDownloadIndices[j + 1] = a;
                    }
                }
            }
            Debug.Log("[TextureManager] Download order sorted by newest timestamp first");

            // ダウンロード対象スロットの新タイムスタンプを一時保存し、
            // 成功時に OnImageLoadSuccess で cachedTimestamps へ反映する。
            // ここで先書きすると、サーバー側の画像生成が完了する前に
            // ダウンロードして白画像がキャッシュされた場合に再取得されなくなるため。
            for (int i = 0; i < maxTextures; i++) _pendingNewTimestamps[i] = null;
            for (int j = 0; j < pendingDownloadCount; j++)
            {
                int idx = pendingDownloadIndices[j];
                _pendingNewTimestamps[idx] = newTimestamps[idx];
            }

            currentPendingIndex = 0;
            isDownloading = true;

            Debug.Log($"[TextureManager] Starting download of {pendingDownloadCount} updated images");
            if (logTarget != null) logTarget.AppendLog($"画像ダウンロード開始（{pendingDownloadCount}件）");
            DownloadNextPending();
        }

        /// <summary>
        /// 待機中のアイテムにキャッシュ済みテクスチャを通知
        /// </summary>
        private void NotifyWaitingItems(int setIndex)
        {
            if (textures[setIndex] == null) return;

            if (waitingItems[setIndex] != null)
            {
                for (int i = 0; i < waitingItems[setIndex].Length; i++)
                {
                    if (waitingItems[setIndex][i] != null)
                    {
                        waitingItems[setIndex][i].ApplyTexture(textures[setIndex]);
                        waitingItems[setIndex][i] = null;
                    }
                }
            }

            if (waitingPhotoViews[setIndex] != null)
            {
                for (int i = 0; i < waitingPhotoViews[setIndex].Length; i++)
                {
                    if (waitingPhotoViews[setIndex][i] != null)
                    {
                        waitingPhotoViews[setIndex][i].ApplyTexture(textures[setIndex]);
                        waitingPhotoViews[setIndex][i] = null;
                    }
                }
            }
        }

        /// <summary>
        /// ダウンロード状態をリセット（待機リストのみ）
        /// </summary>
        private void ResetWaitingItems()
        {
            for (int i = 0; i < maxTextures; i++)
            {
                if (waitingItems[i] != null)
                {
                    for (int j = 0; j < waitingItems[i].Length; j++)
                    {
                        waitingItems[i][j] = null;
                    }
                }
            }
        }

        /// <summary>
        /// 次の更新対象画像をダウンロード
        /// </summary>
        private void DownloadNextPending()
        {
            if (currentPendingIndex >= pendingDownloadCount)
            {
                Debug.Log("[TextureManager] All pending downloads completed");
                if (logTarget != null) logTarget.AppendLog("画像ダウンロード完了");
                isDownloading = false;
                return;
            }

            int targetIndex = pendingDownloadIndices[currentPendingIndex];
            vrcurllist activeList = GetCurrentUrlList();

            if (activeList == null || targetIndex >= activeList.urlArray.Length)
            {
                Debug.LogWarning($"[TextureManager] Invalid target index {targetIndex}, skipping");
                currentPendingIndex++;
                DownloadNextPending();
                return;
            }

            VRCUrl url = activeList.urlArray[targetIndex];
            if (url != null)
            {
                // currentDownloadIndexを更新（コールバックで使用）
                currentDownloadIndex = targetIndex;
                Debug.Log($"[TextureManager] Downloading [{targetIndex:X2}]: {url}");
                imageDownloader.DownloadImage(url, null, (IUdonEventReceiver)this, null);
            }
            else
            {
                Debug.LogWarning($"[TextureManager] URL at index {targetIndex} is null, skipping");
                currentPendingIndex++;
                DownloadNextPending();
            }
        }

        /// <summary>
        /// 次の画像をダウンロード（従来の全件用 - 互換性のため残す）
        /// </summary>
        private void DownloadNext()
        {
            vrcurllist activeList = GetCurrentUrlList();
            if (activeList == null || currentDownloadIndex >= maxTextures || currentDownloadIndex >= activeList.urlArray.Length)
            {
                Debug.Log("[TextureManager] All downloads completed");
                isDownloading = false;
                return;
            }

            VRCUrl url = activeList.urlArray[currentDownloadIndex];
            if (url != null)
            {
                Debug.Log($"[TextureManager] Downloading [{currentDownloadIndex:X2}]: {url}");
                imageDownloader.DownloadImage(url, null, (IUdonEventReceiver)this, null);
            }
            else
            {
                Debug.LogWarning($"[TextureManager] URL at index {currentDownloadIndex} is null, skipping");
                currentDownloadIndex++;
                DownloadNext();
            }
        }

        /// <summary>
        /// 画像ダウンロード成功時のコールバック
        /// </summary>
        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            int downloadedIndex = currentDownloadIndex;

            // テクスチャを保存
            textures[downloadedIndex] = result.Result;
            downloadCompleted[downloadedIndex] = true;

            // 成功したスロットのタイムスタンプをここで確定（StartDownloadWithTimestamps では先書きしない）
            if (_pendingNewTimestamps != null && downloadedIndex < _pendingNewTimestamps.Length
                && _pendingNewTimestamps[downloadedIndex] != null)
            {
                cachedTimestamps[downloadedIndex] = _pendingNewTimestamps[downloadedIndex];
                _pendingNewTimestamps[downloadedIndex] = null;
            }

            Texture2D tex = result.Result;
            Debug.Log($"[TextureManager] Success [{downloadedIndex:X2}]: {tex.width}x{tex.height}");
            if (logTarget != null) logTarget.AppendLog($"画像[{downloadedIndex:X2}]取得成功");

            // 待機中のすべてのアイテムに通知
            if (waitingItems[downloadedIndex] != null)
            {
                int notifiedCount = 0;
                for (int i = 0; i < waitingItems[downloadedIndex].Length; i++)
                {
                    if (waitingItems[downloadedIndex][i] != null)
                    {
                        waitingItems[downloadedIndex][i].ApplyTexture(result.Result);
                        waitingItems[downloadedIndex][i] = null;
                        notifiedCount++;
                    }
                }
                if (notifiedCount > 0)
                {
                    Debug.Log($"[TextureManager] Notified {notifiedCount} waiting items for index {downloadedIndex}");
                }
            }

            // 待機中のすべての eterpix_photo_vew に通知
            if (waitingPhotoViews[downloadedIndex] != null)
            {
                for (int i = 0; i < waitingPhotoViews[downloadedIndex].Length; i++)
                {
                    if (waitingPhotoViews[downloadedIndex][i] != null)
                    {
                        waitingPhotoViews[downloadedIndex][i].ApplyTexture(result.Result);
                        waitingPhotoViews[downloadedIndex][i] = null;
                    }
                }
            }

            // 次のダウンロードを開始（pending方式）
            currentPendingIndex++;
            DownloadNextPending();
        }

        /// <summary>
        /// 画像ダウンロード失敗時のコールバック
        /// </summary>
        public override void OnImageLoadError(IVRCImageDownload result)
        {
            int failedIndex = currentDownloadIndex;

            Debug.LogError($"[TextureManager] Failed to download index {failedIndex}: {result.Error}");
            if (logTarget != null) logTarget.AppendLog($"画像[{failedIndex:X2}]取得失敗: {result.Error}");

            // 失敗時はキャッシュタイムスタンプをクリア（次回再試行のため）
            if (failedIndex < cachedTimestamps.Length)
            {
                cachedTimestamps[failedIndex] = "";
            }

            textures[failedIndex] = null;
            downloadCompleted[failedIndex] = true; // エラーでも「完了」扱い

            // 待機中のアイテムをクリア
            if (waitingItems[failedIndex] != null)
            {
                for (int i = 0; i < waitingItems[failedIndex].Length; i++)
                {
                    waitingItems[failedIndex][i] = null;
                }
            }

            if (waitingPhotoViews[failedIndex] != null)
            {
                for (int i = 0; i < waitingPhotoViews[failedIndex].Length; i++)
                {
                    waitingPhotoViews[failedIndex][i] = null;
                }
            }

            // 次のダウンロードを開始（pending方式）
            currentPendingIndex++;
            DownloadNextPending();
        }

        /// <summary>
        /// eterpix_itemが特定のset_indexの画像を要求
        /// </summary>
        public void RequestTexture(int setIndex, eterpix_item item)
        {
            if (setIndex < 0 || setIndex >= maxTextures)
            {
                Debug.LogWarning($"[TextureManager] Invalid setIndex: {setIndex}");
                return;
            }

            // すでにダウンロード完了していれば即座に適用
            if (downloadCompleted[setIndex] && textures[setIndex] != null)
            {
                Debug.Log($"[TextureManager] Texture [{setIndex:X2}] already downloaded, applying immediately");
                item.ApplyTexture(textures[setIndex]);
            }
            else
            {
                // 待機リストに登録
                for (int i = 0; i < waitingItems[setIndex].Length; i++)
                {
                    if (waitingItems[setIndex][i] == null)
                    {
                        waitingItems[setIndex][i] = item;
                        Debug.Log($"[TextureManager] Item added to waiting list for index {setIndex}");
                        return;
                    }
                }
                
                Debug.LogWarning($"[TextureManager] Waiting list full for index {setIndex}");
            }
        }

        /// <summary>
        /// eterpix_photo_vewが特定のset_indexの画像を要求
        /// </summary>
        public void RequestTexture(int setIndex, eterpix_photo_vew item)
        {
            if (setIndex < 0 || setIndex >= maxTextures)
            {
                Debug.LogWarning($"[TextureManager] Invalid setIndex: {setIndex}");
                return;
            }

            // すでにダウンロード完了していれば即座に適用
            if (downloadCompleted[setIndex] && textures[setIndex] != null)
            {
                Debug.Log($"[TextureManager] Texture [{setIndex:X2}] already downloaded, applying immediately");
                item.ApplyTexture(textures[setIndex]);
            }
            else
            {
                // 待機リストに登録
                for (int i = 0; i < waitingPhotoViews[setIndex].Length; i++)
                {
                    if (waitingPhotoViews[setIndex][i] == null)
                    {
                        waitingPhotoViews[setIndex][i] = item;
                        Debug.Log($"[TextureManager] PhotoView added to waiting list for index {setIndex}");
                        return;
                    }
                }

                Debug.LogWarning($"[TextureManager] PhotoView waiting list full for index {setIndex}");
            }
        }

        /// <summary>
        /// 特定のset_indexのテクスチャを直接取得
        /// </summary>
        public Texture2D GetTexture(int setIndex)
        {
            if (setIndex < 0 || setIndex >= maxTextures)
                return null;

            return downloadCompleted[setIndex] ? textures[setIndex] : null;
        }
        
        /// <summary>
        /// ★ ダウンロード中かどうかを確認
        /// </summary>
        public bool IsDownloading()
        {
            return isDownloading;
        }
    }
}