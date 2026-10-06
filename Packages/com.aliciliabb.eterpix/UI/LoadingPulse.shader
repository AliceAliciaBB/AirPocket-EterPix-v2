// 画像の読み込み中にRawImageを明滅させる極小UIシェーダー。
// 色はRawImage.color(頂点カラー)をそのまま使い、αだけを時間で揺らす。
// 背景の上に描かれるため、見た目は「ボタン色 ⇔ 背景色」の明滅になる。
Shader "EterPix/UI/LoadingPulse"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture (unused)", 2D) = "white" {}
        _Speed ("Speed", Float) = 2.5
        _MinAlpha ("Min Alpha", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _Speed;
            float _MinAlpha;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = 0.5 + 0.5 * sin(_Time.y * _Speed);
                fixed4 c = i.color;
                c.a *= lerp(_MinAlpha, 1.0, t);
                return c;
            }
            ENDCG
        }
    }
}
