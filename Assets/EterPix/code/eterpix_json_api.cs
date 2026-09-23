using UdonSharp;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ali.eterpix
{
    /// <summary>
    /// 商品情報を取得するリクエスト
    /// </summary>
    public class eterpix_json_api : UdonSharpBehaviour
    {
        // 取得したデータのリスト
        private DataList _list;


        /// <summary>
        /// リクエストを送信します。
        /// </summary>
        /// <param name="receiver">送信結果を返すIUdonEventReceiver</param>
        /// <param name="URL">リクエストURL</param>
        public void SendRequest(IUdonEventReceiver receiver, VRCUrl URL)
        {
            if (receiver == null || URL == null)
            {

                return;
            }

            // 送信
            VRCStringDownloader.LoadUrl(URL, receiver);
        }

        /// <summary>
        /// 成功時に呼び出す処理
        /// </summary>
        /// <param name="result">結果</param>
        /// <returns>Jsonの解析に成功したらtrueを返します。</returns>
        public bool OnSuccess(IVRCStringDownload result)
        {
            if (result == null)
            {

                return false;
            }

            // Jsonの解析
            if (!VRCJson.TryDeserializeFromJson(result.Result, out var token))
            {

                return false;
            }

            // データがDictionaryかを確認（サーバーAPI形式: {"new":..,"old":..,"collage_img":{...},"posts":[...]}）
            if (token.TokenType != TokenType.DataDictionary)
            {

                return false;
            }

            // "posts" キーから DataList を取得
            if (!token.DataDictionary.TryGetValue("posts", out DataToken postToken) ||
                postToken.TokenType != TokenType.DataList)
            {

                return false;
            }

            _list = postToken.DataList;
            return true;
        }

        /// <summary>
        /// 結果の中から指定したインデックスのデータを取得します。
        /// </summary>
        /// <param name="index">取得するデータのインデックス</param>
        /// <param name="item">取得結果のデータを渡す</param>
        /// <returns>データの取得に成功したらtrueを返す</returns>
        public bool TryGetItem(int index, out DataDictionary item)
        {
            item = null;

            if (_list == null)
            {

                return false;
            }

            // データの取得
            if (!_list.TryGetValue(index, out var token))
            {
                return false;
            }

            // データがDictionaryかを確認
            if (token.TokenType != TokenType.DataDictionary)
            {

                return false;
            }

            item = token.DataDictionary;
            return true;
        }

        /// <summary>
        /// 失敗時に呼び出す処理
        /// </summary>
        /// <param name="result">結果</param>
        public void OnFailed(IVRCStringDownload result)
        {
            if (result != null)
            {

            }
            else
            {

            }
        }
    }
}