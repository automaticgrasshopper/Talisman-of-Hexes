Shader "Custom/OldTVNoise"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "black" {}
        _GreenTint ("Green Tint", Range(0,2)) = 1.0
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.1
        _NoiseSpeed ("Noise Speed", Range(0,10)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            float _GreenTint;
            float _NoiseAmount;
            float _NoiseSpeed;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample the main texture
                fixed4 col = tex2D(_MainTex, i.uv);

                // Add green tint
                col.rgb = lerp(col.rgb, float3(col.r, col.g * _GreenTint, col.b), _GreenTint);

                // Create a time-dependent random offset for the noise texture
                float2 noiseUV = i.uv + float2(frac(_Time.y * _NoiseSpeed) - 0.5, frac(_Time.y * _NoiseSpeed) - 0.5);
                fixed4 noise = tex2D(_NoiseTex, noiseUV);

                // Add noise to the color
                col.rgb += noise.rgb * _NoiseAmount;

                // Clamp the color to avoid over-saturation
                col.rgb = clamp(col.rgb, 0, 1);

                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}