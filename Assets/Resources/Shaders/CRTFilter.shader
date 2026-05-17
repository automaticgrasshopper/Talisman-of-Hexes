// This file is generated. Do not edit it manually. Please edit .shaderproto files.

Shader "Nova/VFX/CRTFilter"
{
    Properties
    {
        [HideInInspector] _MainTex ("Main Texture", 2D) = "white" {}
        _T ("Time", Range(0.0, 1.0)) = 0.0
        _ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.7
        _ScanlineDensity ("Scanline Density", Range(0.5, 3)) = 1.3
        _Distortion ("Distortion", Range(0, 0.2)) = 0.08
        _Vignette ("Vignette", Range(0, 1)) = 0.5
        _Chroma ("Chromatic Aberration", Range(0, 0.03)) = 0.012
        _Curvature ("Curvature", Range(0, 0.1)) = 0.025
        _NoiseIntensity ("Noise Intensity", Range(0, 0.1)) = 0.02
        _BackColor ("Background Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Cull Off ZWrite Off Blend One OneMinusSrcAlpha
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
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
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float _T;
            float _ScanlineIntensity;
            float _ScanlineDensity;
            float _Distortion;
            float _Vignette;
            float _Chroma;
            float _Curvature;
            float _NoiseIntensity;
            float4 _BackColor;

            float2 WarpUV(float2 uv)
            {
                float2 center = uv - 0.5;
                float dist = dot(center, center);
                uv = center * (1.0 + dist * _Distortion) + 0.5;
                
                uv = (uv - 0.5) * 2.0;
                uv.x *= 1.0 + pow(abs(uv.y) / 8.0, 2.0) * _Curvature;
                uv.y *= 1.0 + pow(abs(uv.x) / 8.0, 2.0) * _Curvature;
                return saturate(uv * 0.5 + 0.5);
            }

            float Scanlines(float2 uv)
            {
                float scanline = sin(uv.y * _ScreenParams.y * _ScanlineDensity * UNITY_PI);
                return 1.0 - abs(scanline) * _ScanlineIntensity * 0.4;
            }

            float Vignette(float2 uv)
            {
                float2 d = abs(uv - 0.5) * 2.0;
                d = pow(d, 4);
                return 1.0 - saturate((d.x + d.y) * _Vignette);
            }

            float Noise(float2 seed)
            {
                return frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = WarpUV(i.uv);
                
                float2 offset = float2(_Chroma, -_Chroma);
                float r = tex2D(_MainTex, saturate(uv + offset * 0.4)).r;
                float g = tex2D(_MainTex, saturate(uv + offset * 0.1)).g;
                float b = tex2D(_MainTex, saturate(uv - offset * 0.3)).b;
                
                fixed4 col = fixed4(r, g, b, tex2D(_MainTex, uv).a);
                
                col.rgb *= Scanlines(uv);
                col.rgb *= Vignette(uv);
                
                float noiseVal = Noise(i.uv + _Time.x) * _NoiseIntensity;
                col.rgb += noiseVal;
                
                float luminance = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb += col.rgb * smoothstep(0.3, 0.8, luminance) * 0.25;
                
                col.rgb *= col.a;
                
                col *= i.color;
                return col;
            }
            ENDCG
        }
    }
}