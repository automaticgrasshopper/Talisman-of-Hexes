Shader "Custom/NorcoCRTFilter"
{
    Properties
    {
        [PerRendererData] _MainTex("Base Texture", 2D) = "white" {}
        _ScanlineIntensity("扫描线强度", Range(0, 1)) = 0.7
        _ScanlineDensity("扫描线密度", Range(0.5, 3)) = 1.3
        _Distortion("畸变强度", Range(0, 0.2)) = 0.08
        _Vignette("暗角强度", Range(0, 1)) = 0.5
        _ChromaticAberration("色散强度", Range(0, 0.03)) = 0.012
        _Curvature("曲率", Range(0, 0.1)) = 0.025
        _NoiseIntensity("噪点强度", Range(0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags { 
            "Queue"="Transparent" 
            "IgnoreProjector"="True" 
            "RenderType"="Opaque"
        }
        
        Cull Off
        Lighting Off
        ZWrite Off
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
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _ScanlineIntensity;
            float _ScanlineDensity;
            float _Distortion;
            float _Vignette;
            float _ChromaticAberration;
            float _Curvature;
            float _NoiseIntensity;

            // 桶形畸变 + 曲面效果 :cite[5]
            float2 WarpUV(float2 uv)
            {
                // 中心点偏移
                float2 center = uv - 0.5;
                // 距离计算
                float dist = dot(center, center);
                // 桶形畸变
                uv = center * (1.0 + dist * _Distortion) + 0.5;
                
                // 曲面效果 :cite[7]
                uv = (uv - 0.5) * 2.0;
                uv.x *= 1.0 + pow(abs(uv.y) / 8.0, 2.0) * _Curvature;
                uv.y *= 1.0 + pow(abs(uv.x) / 8.0, 2.0) * _Curvature;
                return saturate(uv * 0.5 + 0.5);
            }

            // 扫描线效果 :cite[1]
            float Scanlines(float2 uv)
            {
                float scanline = sin(uv.y * _ScreenParams.y * _ScanlineDensity * UNITY_PI);
                return 1.0 - abs(scanline) * _ScanlineIntensity * 0.4;
            }

            // 暗角效果 :cite[7]
            float Vignette(float2 uv)
            {
                float2 d = abs(uv - 0.5) * 2.0;
                d = pow(d, 4);
                return 1.0 - saturate((d.x + d.y) * _Vignette);
            }

            // 随机噪点 :cite[1]
            float Noise(float2 seed)
            {
                return frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // UV畸变处理
                float2 uv = WarpUV(i.uv);
                
                // 色散偏移 (RGB分离) :cite[5]
                float2 offset = float2(_ChromaticAberration, -_ChromaticAberration);
                float r = tex2D(_MainTex, saturate(uv + offset * 0.4)).r;
                float g = tex2D(_MainTex, saturate(uv + offset * 0.1)).g;
                float b = tex2D(_MainTex, saturate(uv - offset * 0.3)).b;
                
                fixed4 col = fixed4(r, g, b, tex2D(_MainTex, uv).a);
                
                // 扫描线叠加
                col.rgb *= Scanlines(uv);
                
                // 暗角叠加
                col.rgb *= Vignette(uv);
                
                // 噪点干扰 :cite[1]
                float noiseVal = Noise(i.uv + _Time.x) * _NoiseIntensity;
                col.rgb += noiseVal;
                
                // 边缘辉光 (亮色增强)
                float luminance = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb += col.rgb * smoothstep(0.3, 0.8, luminance) * 0.25;
                
                return col * i.color;
            }
            ENDCG
        }
    }
    FallBack "UI/Default"
}
