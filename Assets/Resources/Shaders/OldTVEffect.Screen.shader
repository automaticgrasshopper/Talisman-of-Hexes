// This file is generated. Do not edit it manually. Please edit .shaderproto files.

Shader "Nova/VFX Screen/OldTVEffect"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "gray" {}
        _ScanlineDensity ("Scanline Density", Range(50,200)) = 100
        _StaticIntensity ("Static Intensity", Range(0,1)) = 0.3
        _ColorFlicker ("Color Flicker", Range(0,0.1)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        CGINCLUDE
        #include "UnityCG.cginc"

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float2 uv : TEXCOORD0;
            float4 pos : SV_POSITION;
        };

        sampler2D _MainTex;
        sampler2D _NoiseTex;
        float _ScanlineDensity;
        float _StaticIntensity;
        float _ColorFlicker;

        // 生成随机噪声
        float random (float2 st) 
        {
            return frac(sin(dot(st.xy, float2(12.9898,78.233)))*43758.5453123);
        }

        v2f vert (appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }

        fixed4 frag (v2f i) : SV_Target
        {
            // 基础颜色
            fixed4 col = tex2D(_MainTex, i.uv);

            // 添加扫描线
            float scanline = sin(i.uv.y * _ScanlineDensity * 3.1415);
            col.rgb *= 0.8 + 0.2 * smoothstep(0.4, 0.6, scanline);

            // 添加雪花噪点
            float staticNoise = random(i.uv + _Time.x); 
            col.rgb += staticNoise * _StaticIntensity;

            // 颜色抖动
            col.r += random(i.uv + float2(0.34, 0.66)) * _ColorFlicker;
            col.g += random(i.uv + float2(0.12, 0.88)) * _ColorFlicker;
            col.b += random(i.uv + float2(0.55, 0.45)) * _ColorFlicker;

            // 降低饱和度
            float gray = dot(col.rgb, float3(0.299, 0.587, 0.114));
            col.rgb = lerp(col.rgb, gray.xxx, 0.2);

            // 轻微模糊
            col = (col + tex2D(_MainTex, i.uv + float2(0.001,0.001))) / 2;

            return saturate(col);
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}