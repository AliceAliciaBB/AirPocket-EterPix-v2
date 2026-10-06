using UdonSharp;
using UnityEngine;

namespace ali.eterpix
{
    // 共通デバッグフラグ。上位prefab内に1つだけ配置するシングルトン。
    // 各コードはGameObject.Find(SingletonObjectName)経由でこれを参照し、
    // Debug.Logの出力をon/offする。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_debug : UdonSharpBehaviour
    {
        public const string SingletonObjectName = "EterpixDebug";

        [Header("Debug.LogをUnity/MCPのログへ出すかどうか")]
        [SerializeField] private bool debugEnabled = false;

        // UdonSharpはstaticフィールドをサポートしないため、GameObject.Findで
        // 「SingletonObjectNameを名乗る先着オブジェクト」を判定に使う
        // (2つ目以降、自分がその先着オブジェクトでなければ無効化する)。
        private void Start()
        {
            GameObject canonical = GameObject.Find(SingletonObjectName);
            if (canonical != null && canonical != this.gameObject)
            {
                Debug.LogWarning("[eterpix_debug] Duplicate instance detected. Disabling self.");
                gameObject.SetActive(false);
                return;
            }
        }

        public bool IsDebugEnabled()
        {
            return debugEnabled;
        }

        public void Log(string message)
        {
            if (debugEnabled)
            {
                Debug.Log(message);
            }
        }

        public void LogWarning(string message)
        {
            if (debugEnabled)
            {
                Debug.LogWarning(message);
            }
        }

        public void LogError(string message)
        {
            if (debugEnabled)
            {
                Debug.LogError(message);
            }
        }
    }
}
