Shader "UI/LighteningTitle"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 呼吸缩放（从脚底锚定，垂直微动）
        _BreathAmount ("呼吸幅度", Range(0, 0.02)) = 0.005
        _BreathSpeed  ("呼吸速度", Range(0.2, 3)) = 1.2

        // 边缘烟丝（fbm 噪声 UV warp，向上飘）
        _WispStrength ("烟丝强度", Range(0, 0.05)) = 0.015
        _WispScale    ("烟丝噪声尺度", Range(1, 20)) = 8.0
        _WispSpeed    ("烟丝飘动速度", Range(0.05, 2)) = 0.35
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
            fixed4 _Color;
            float _BreathAmount;
            float _BreathSpeed;
            float _WispStrength;
            float _WispScale;
            float _WispSpeed;

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // 值噪声（带双线性插值），比纯 hash 更柔和
            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash(i);
                float b = hash(i + float2(1, 0));
                float c = hash(i + float2(0, 1));
                float d = hash(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                for (int k = 0; k < 3; k++)
                {
                    v += amp * vnoise(p);
                    p *= 2.0;
                    amp *= 0.5;
                }
                return v;
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

                // ===== 1. 呼吸缩放（从脚底 uv.y=0 锚定）=====
                // breath > 1：略拉高；< 1：略压缩。脚底锁住，头顶/发梢动得最多。
                float breath = 1.0 + sin(_Time.y * _BreathSpeed) * _BreathAmount;
                uv.y = uv.y / breath;

                // ===== 2. 边缘烟丝（噪声向上飘）=====
                // 噪声坐标随时间向 -y 方向移动 → 视觉上图案"向上漂"
                float2 noiseUV = uv * _WispScale + float2(_Time.y * _WispSpeed * 0.2,
                                                          -_Time.y * _WispSpeed);
                float n = fbm(noiseUV);

                // 仅向下取样（n >= 0），制造"轮廓向上延伸"的烟丝感
                // 头顶 / 肩膀 / 裙摆下沿等边缘处，透明像素会偶尔采到下方实体 → 像烟
                // 实体内部因为颜色均匀，几乎察觉不到位移
                float2 warpUV = uv;
                warpUV.y -= n * _WispStrength;
                // 横向给极弱的扰动，让烟丝不至于呆板地纯垂直
                warpUV.x += (n - 0.5) * _WispStrength * 0.3;

                // ===== 3. 采样（颜色完全来自原图，没有任何色调注入）=====
                fixed4 col = tex2D(_MainTex, warpUV) * i.color;

                // 极低 alpha 直接 discard，保边缘干净
                if (col.a < 0.01) discard;

                return col;
            }
            ENDCG
        }
    }
}
