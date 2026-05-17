Shader "UI/LighteningTitle"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 蒸汽扭曲参数（全局热浪）
        _SteamStrength ("蒸汽扭曲强度", Range(0, 0.05)) = 0.02
        _SteamSpeed ("蒸汽扭曲速度", Range(0.5, 3)) = 1.2

        // 白烟参数（右上角弥漫）
        _SmokeIntensity ("白烟强度", Range(0, 0.8)) = 0.4
        _SmokeScale ("白烟噪声尺度", Range(1, 10)) = 3.0
        _SmokeSpeed ("白烟流动速度", Range(0.2, 2)) = 0.8
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
            float _SteamStrength;
            float _SteamSpeed;
            float _SmokeIntensity;
            float _SmokeScale;
            float _SmokeSpeed;

            // 简单的噪声函数
            float noise(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float fbm(float2 p, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * noise(p * frequency);
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                return value;
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
                // 原始采样
                fixed4 original = tex2D(_MainTex, i.texcoord);
                fixed4 texColor = original * i.color;
                float alpha = texColor.a;
                if (alpha < 0.01) discard;

                // ========== 蒸汽氤氲效果 (全局扭曲) ==========
                float2 steamUV = i.texcoord;
                float timeSteam = _Time.y * _SteamSpeed;
                steamUV.x += sin(i.texcoord.y * 40 + timeSteam) * _SteamStrength;
                steamUV.y += cos(i.texcoord.x * 35 + timeSteam * 1.2) * _SteamStrength;
                fixed4 steamedTex = tex2D(_MainTex, steamUV) * i.color;
                texColor = lerp(texColor, steamedTex, 0.35);

                // ========== 紫色雾气 (全局氛围) ==========
                float fogFactor = 0.2 * (sin(i.texcoord.x * 20 + _Time.y * 0.8) * 0.5 + 0.5)
                                * (cos(i.texcoord.y * 25 + _Time.y * 0.6) * 0.5 + 0.5);
                fixed4 fogColor = fixed4(0.6, 0.4, 0.8, 0.15);
                texColor = lerp(texColor, fogColor, fogFactor * 0.6);

                // ========== 右上角白烟弥漫 ==========
                // 只影响右上角区域 (x>0.55, y>0.55)，向四周渐隐
                float2 uv = i.texcoord;
                float cornerMask = 1.0;
                if (uv.x < 0.55 || uv.y < 0.55)
                {
                    // 离右上角越远，白烟越弱
                    float dist = max(0.55 - uv.x, 0.55 - uv.y);
                    cornerMask = 1.0 - smoothstep(0.0, 0.3, dist);
                }
                if (cornerMask > 0.01)
                {
                    // 动态白烟：使用分形噪声生成飘移纹理
                    float2 smokeUV = uv * _SmokeScale;
                    smokeUV.x += _Time.y * _SmokeSpeed;
                    smokeUV.y -= _Time.y * _SmokeSpeed * 0.7;
                    
                    float smoke1 = fbm(smokeUV, 3);
                    float smoke2 = fbm(smokeUV * 2.5 + float2(0.5, 0.2), 2);
                    float smoke = (smoke1 * 0.7 + smoke2 * 0.3) * cornerMask;
                    // 让烟更柔和，呈白色
                    smoke = pow(smoke, 1.5) * _SmokeIntensity;
                    
                    fixed4 smokeColor = fixed4(0.95, 0.95, 1.0, smoke);
                    texColor = texColor + smokeColor * smoke;
                }

                fixed4 final = texColor;
                final.a = alpha;
                return final;
            }
            ENDCG
        }
    }
}
