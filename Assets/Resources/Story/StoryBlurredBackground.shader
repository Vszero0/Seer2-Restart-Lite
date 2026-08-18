Shader "UI/Story Blurred Background"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BlurTex ("Blurred Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SpriteUvRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _BlurStrength ("Blur Strength", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _BlurTex;
            fixed4 _Color;
            float4 _SpriteUvRect;
            float _BlurStrength;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 localUv = (input.texcoord - _SpriteUvRect.xy) / max(_SpriteUvRect.zw, 0.00001);
                fixed4 source = tex2D(_MainTex, input.texcoord);
                fixed4 blurred = tex2D(_BlurTex, saturate(localUv));
                return lerp(source, blurred, _BlurStrength) * input.color;
            }
            ENDCG
        }
    }
}
