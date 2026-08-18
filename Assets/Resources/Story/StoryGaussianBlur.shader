Shader "Hidden/Story Gaussian Blur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurDirection ("Blur Direction", Vector) = (1,0,0,0)
        _SourceUvRect ("Source UV Rect", Vector) = (0,0,1,1)
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _BlurDirection;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 direction = _MainTex_TexelSize.xy * _BlurDirection.xy;
                fixed4 color = tex2D(_MainTex, input.uv) * 0.227027;
                color += tex2D(_MainTex, input.uv + direction * 1.384615) * 0.316216;
                color += tex2D(_MainTex, input.uv - direction * 1.384615) * 0.316216;
                color += tex2D(_MainTex, input.uv + direction * 3.230769) * 0.070270;
                color += tex2D(_MainTex, input.uv - direction * 3.230769) * 0.070270;
                return color;
            }
            ENDCG
        }

        // Copy a sprite rectangle into the working buffer while reconstructing
        // bilinear filtering independently of the source texture's FilterMode.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragCopy
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _SourceUvRect;

            fixed4 SampleBilinear(float2 uv)
            {
                float2 textureSize = 1.0 / _MainTex_TexelSize.xy;
                float2 texelPosition = uv * textureSize - 0.5;
                float2 baseTexel = floor(texelPosition);
                float2 blend = frac(texelPosition);
                float2 baseUv = (baseTexel + 0.5) * _MainTex_TexelSize.xy;
                fixed4 bottomLeft = tex2D(_MainTex, baseUv);
                fixed4 bottomRight = tex2D(_MainTex, baseUv + float2(_MainTex_TexelSize.x, 0));
                fixed4 topLeft = tex2D(_MainTex, baseUv + float2(0, _MainTex_TexelSize.y));
                fixed4 topRight = tex2D(_MainTex, baseUv + _MainTex_TexelSize.xy);
                return lerp(
                    lerp(bottomLeft, bottomRight, blend.x),
                    lerp(topLeft, topRight, blend.x),
                    blend.y);
            }

            fixed4 fragCopy(v2f_img input) : SV_Target
            {
                float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
                float2 minimumUv = _SourceUvRect.xy + halfTexel;
                float2 maximumUv = _SourceUvRect.xy + _SourceUvRect.zw - halfTexel;
                float2 sourceUv = _SourceUvRect.xy + input.uv * _SourceUvRect.zw;
                return SampleBilinear(clamp(sourceUv, minimumUv, maximumUv));
            }
            ENDCG
        }
    }
}
