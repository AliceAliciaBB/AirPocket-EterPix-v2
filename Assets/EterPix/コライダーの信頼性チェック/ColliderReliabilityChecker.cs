using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AliciliaEterPix.Udon
{
    /// <summary>
    /// コライダーの信頼性チェック用。
    /// このスクリプトを付けたGameObjectのトリガーコライダー(isTrigger = ON)に
    /// プレイヤーが入った瞬間・出た瞬間を時刻付きでテキストに表示する。
    /// 入った回数・出た回数・現在中にいる人数も表示し、取りこぼしを検出しやすくする。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ColliderReliabilityChecker : UdonSharpBehaviour
    {
        [Tooltip("ログを表示するテキスト")]
        [SerializeField]
        private TextMeshProUGUI logText;

        [Tooltip("表示する直近ログの行数")]
        [SerializeField]
        private int maxLines = 20;

        [Tooltip("ONにするとプレイヤー以外の物体(Rigidbody付き等)の出入りも記録する")]
        [SerializeField]
        private bool logObjects = false;

        private string[] lines;
        private int lineCount;
        private int enterCount;
        private int exitCount;

        private void Start()
        {
            if (maxLines < 1)
            {
                maxLines = 1;
            }
            lines = new string[maxLines];
            Refresh();
        }

        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            enterCount++;
            AddLine("ENTER", PlayerLabel(player));
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            exitCount++;
            AddLine("EXIT ", PlayerLabel(player));
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!logObjects || other == null)
            {
                return;
            }
            AddLine("obj ENTER", other.name);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!logObjects || other == null)
            {
                return;
            }
            AddLine("obj EXIT ", other.name);
        }

        /// <summary>ログとカウンタを消去する(ボタン等からSendCustomEventで呼ぶ)</summary>
        public void ClearLog()
        {
            lineCount = 0;
            enterCount = 0;
            exitCount = 0;
            Refresh();
        }

        private string PlayerLabel(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
            {
                return "(invalid player)";
            }
            string label = player.displayName + " [" + player.playerId + "]";
            if (player.isLocal)
            {
                label += " (自分)";
            }
            return label;
        }

        private void AddLine(string kind, string who)
        {
            if (lines == null)
            {
                return;
            }

            // 古い行を押し出して末尾に追加する
            if (lineCount >= lines.Length)
            {
                for (int i = 1; i < lines.Length; i++)
                {
                    lines[i - 1] = lines[i];
                }
                lineCount = lines.Length - 1;
            }

            lines[lineCount] = DateTime.Now.ToString("HH:mm:ss.fff")
                + " f" + Time.frameCount
                + "  " + kind + "  " + who;
            lineCount++;

            Refresh();
        }

        private void Refresh()
        {
            if (logText == null)
            {
                return;
            }

            int inside = enterCount - exitCount;
            string header = "ENTER " + enterCount + " / EXIT " + exitCount + " / 中にいる " + inside;
            if (inside < 0)
            {
                header += "  <color=red>(ENTERの取りこぼし?)</color>";
            }

            string body = "";
            for (int i = lineCount - 1; i >= 0; i--)
            {
                body += "\n" + lines[i];
            }

            logText.text = header + "\n-----" + body;
        }
    }
}
