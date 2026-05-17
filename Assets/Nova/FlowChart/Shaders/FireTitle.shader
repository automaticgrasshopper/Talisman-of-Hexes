Shader "UI/FireTitle"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("基础色调", Color) = (1,1,1,1)

        // 火焰颜色 (从外焰到内焰)
        _FireColorOuter ("外焰色 (红橙)", Color) = (1, 0.4, 0.1, 1)
        _FireColorMid   ("中焰色 (橙黄)", Color) = (1, 0.7, 0.2, 1)
        _FireColorInner ("内焰色 (亮黄)", Color) = (1, 0.9, 0.3, 1)

        _FireSpeed      ("火焰流动速度", Range(0.5, 5)) = 1.8
        _FireIntensity  ("火焰强度", Range(0.5, 2)) = 1.2
        _NoiseScale     ("火焰噪声尺度", Range(0.5, 8)) = 3.0

        // 蒸汽/热浪扭曲参数
        _HeatDistortStrength ("热浪扭曲强度", Range(0, 0.05)) = 0.02
        _HeatDistortSpeed    ("热浪扭曲速度", Range(0.5, 3)) = 1.2

        // 火星/粒子飘浮
        _SparkAmount    ("火星数量", Range(0, 1)) = 0.4
        _SparkSize      ("火星尺寸", Range(1, 8)) = 2
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
            fixed4 _FireColorOuter;
            fixed4 _FireColorMid;
            fixed4 _FireColorInner;

            float _FireSpeed;
            float _FireIntensity;
            float _NoiseScale;
            float _HeatDistortStrength;
            float _HeatDistortSpeed;
            float _SparkAmount;
            float _SparkSize;

            // 伪随机函数 (用于火星)
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // 简单噪声 (用于火焰形态)
            float noise(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
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
                // 原始采样 (得到文字形状，假设文字部分是白色，背景透明)
                fixed4 originalTex = tex2D(_MainTex, i.texcoord);
                float alpha = originalTex.a * i.color.a;
                if (alpha < 0.01) discard;  // 完全透明区域直接丢弃

                // ========== 热浪扭曲 (使坐标偏移，产生蒸汽效果) ==========
                float2 heatUV = i.texcoord;
                float timeHeat = _Time.y * _HeatDistortSpeed;
                float heatOffsetX = sin(heatUV.y * 50 + timeHeat) * _HeatDistortStrength;
                float heatOffsetY = cos(heatUV.x * 40 + timeHeat * 1.3) * _HeatDistortStrength;
                heatUV += float2(heatOffsetX, heatOffsetY);
                // 再次采样得到被扭曲的文字形状（用于影响颜色区域）
                fixed4 distortedTex = tex2D(_MainTex, heatUV);
                float distortedAlpha = distortedTex.a;

                // 如果扭曲坐标超出文字范围（即热浪区域在文字外部），我们也让它产生微弱火焰光晕
                // 用于文字边缘“蒸汽发光”效果
                float isInsideOriginal = alpha;
                float isInsideDistorted = distortedAlpha;

                // 最终火焰强度基础：原始文字内部取1，外部用扭曲alpha * 0.6，使边缘也有火焰光
                float fireStrength = clamp(isInsideOriginal + isInsideDistorted * 0.6, 0.2, 1.0);

                // ========== 火焰颜色动态 (基于UV流动 + 噪声) ==========
                float2 fireUV = i.texcoord * _NoiseScale;
                fireUV.y -= _Time.y * _FireSpeed;   // 向上流动
                fireUV.x += _Time.y * 0.5;          // 轻微水平漂移

                // 生成火焰噪声值 (0~1) 决定颜色混合
                float fireNoise = noise(floor(fireUV * 10)) * 0.5 + 0.5;
                fireNoise += sin(fireUV.x * 30 + _Time.y * 10) * 0.2;
                fireNoise += cos(fireUV.y * 25 - _Time.y * 8) * 0.2;
                fireNoise = clamp(fireNoise, 0, 1);

                // 根据噪声值混合三种火焰色
                fixed4 fireColor;
                if (fireNoise < 0.33)
                    fireColor = lerp(_FireColorOuter, _FireColorMid, fireNoise / 0.33);
                else if (fireNoise < 0.66)
                    fireColor = lerp(_FireColorMid, _FireColorInner, (fireNoise - 0.33) / 0.33);
                else
                    fireColor = lerp(_FireColorInner, fixed4(1,1,0.8,1), (fireNoise - 0.66) / 0.34);

                // 火焰强度乘以内部/边缘衰减 + 全局强度系数
                float finalIntensity = fireStrength * _FireIntensity;
                fireColor.rgb *= finalIntensity;

                // ========== 火星/粒子效果 (在文字周围随机闪烁) ==========
                float2 sparkUV = i.texcoord * (1.0 / _SparkSize);
                float2 sparkBlock = floor(sparkUV);
                float rand = hash(sparkBlock + floor(_Time.y * 15));
                float spark = 0.0;
                if (rand < _SparkAmount && alpha < 0.5 && distortedAlpha > 0.1) {
                    // 只在文字边缘外（alpha低但有热浪区域）产生火星
                    spark = step(0.8, hash(sparkBlock + 100)) * 0.8;
                }
                // 火星颜色为亮橙红
                fixed4 sparkColor = fixed4(1, 0.5, 0.2, 1) * spark;

                // ========== 最终合成 ==========
                // 火焰颜色作为主体，火星附加
                fixed4 final = fireColor + sparkColor;
                final.a = alpha * (0.8 + fireStrength * 0.5); // 半透明，热浪感
                return final;
            }
            ENDCG
        }
    }
}
