Shader "UI/slot"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _JitterAmount ("抖动强度", Range(0, 0.1)) = 0.01
        _JitterSpeed  ("抖动速度", Range(1, 20)) = 8
        _ScanLineCount("扫描线数量", Range(1, 30)) = 10
        _ScanLineDark ("扫描线暗度", Range(0, 1)) = 0.25
        _SnowAmount   ("雪花强度", Range(0, 1)) = 0.15
        _SnowSize     ("雪花尺寸(像素)", Range(1, 16)) = 4
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;

            float _JitterAmount;
            float _JitterSpeed;
            float _ScanLineCount;
            float _ScanLineDark;
            float _SnowAmount;
            float _SnowSize;

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.texcoord;

                // 1. 水平抖动，模拟信号不稳
                float jitterWave = sin(uv.y * 30.0 + _Time.y * _JitterSpeed)
                                 + sin(uv.y * 79.0 - _Time.y * 4.7) * 0.6;
                float offset = jitterWave * _JitterAmount;
                uv.x += offset;

                // 2. 采样原图并转换为黑白
                fixed4 texColor = tex2D(_MainTex, uv);
                float gray = dot(texColor.rgb, float3(0.299, 0.587, 0.114));
                fixed3 bw = fixed3(gray, gray, gray);

                // 3. 粗扫描线（暗条间隔）—— 修改变量名避免冲突
                float scanLinePos = frac(uv.y * _ScanLineCount);
                float scanMask = lerp(_ScanLineDark, 1.0, step(0.5, scanLinePos));
                bw *= scanMask;

                // 4. 雪花噪声（根据实际像素尺寸分块）
                float2 texSize = _MainTex_TexelSize.zw;
                float2 snowUV = floor(uv * texSize / _SnowSize);
                float snowRand = hash(snowUV + floor(_Time.y * 30));
                float snow = step(snowRand, _SnowAmount * 0.5);
                bw = lerp(bw, fixed3(1,1,1), snow * 0.7);

                fixed4 finalColor = fixed4(bw, texColor.a * i.color.a);
                return finalColor;
            }
            ENDCG
        }
    }
}
