using UdonSharp;
using UnityEngine;
using TMPro;

namespace ali.eterpix
{
    // リスナー部品(共通、Aタイプ): キャプション表示。方針.md「キャプション表示用コード指定Aタイプ」参照。
    // 下位セルからUse時、または常時表示用として直接呼ばれる。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class eterpix_listener_caption : UdonSharpBehaviour
    {
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text captionText;
        [SerializeField] private TMP_Text worldNameText;
        [SerializeField] private TMP_Text worldCaptionText;

        private void Start()
        {
            // はみ出す場合は三点リーダー(…)で省略する
            SetEllipsisOverflow(userNameText);
            SetEllipsisOverflow(captionText);
            SetEllipsisOverflow(worldNameText);
            SetEllipsisOverflow(worldCaptionText);
        }

        private void SetEllipsisOverflow(TMP_Text text)
        {
            if (text == null) return;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        public void ApplyCaption(string userName, string description, string worldName, string worldDescription)
        {
            if (userNameText != null) userNameText.text = userName;
            if (captionText != null) captionText.text = description;
            if (worldNameText != null) worldNameText.text = worldName;
            if (worldCaptionText != null) worldCaptionText.text = worldDescription;
        }

        public void ClearCaption()
        {
            ApplyCaption("", "", "", "");
        }
    }
}
