Shader "UI/GlowingStar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("主色调", Color) = (0.8, 0.9, 0.2, 1)
        _GlowIntensity ("发光强度", Range(0.5, 10)) = 3.0   // 上限提高到10
        _RotateSpeed ("旋转速度 (度/秒)", Range(-360, 360)) = 45
        _PulseSpeed ("脉动速度", Range(0.2, 10)) = 1.2
        _PulseRange ("脉动幅度", Range(0.0, 1.5)) = 0.5    // 幅度可更大

        // 向外光晕参数 - 大幅提高上限
        _HaloSpread ("光晕扩散半径 (像素)", Range(1, 120)) = 30   // 最大120像素
        _HaloIntensity ("光晕强度系数", Range(0, 8)) = 2.0        // 最大8倍
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
            float _GlowIntensity;
            float _RotateSpeed;
            float _PulseSpeed;
            float _PulseRange;
            float _HaloSpread;
            float _HaloIntensity;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 旋转 UV
                float2 uv = i.texcoord - 0.5;
                float angle = _Time.y * _RotateSpeed * 3.14159 / 180.0;
                float cosAngle = cos(angle);
                float sinAngle = sin(angle);
                float2 uvRot;
                uvRot.x = uv.x * cosAngle - uv.y * sinAngle;
                uvRot.y = uv.x * sinAngle + uv.y * cosAngle;
                uvRot += 0.5;

                fixed4 tex = tex2D(_MainTex, uvRot);
                float originalAlpha = tex.a;

                // 向外光晕: 采样四周最大alpha，产生外扩效果
                float2 offsetPx = _HaloSpread * _MainTex_TexelSize.xy;
                float2 offsets[4] = {
                    float2( offsetPx.x, 0),
                    float2(-offsetPx.x, 0),
                    float2(0,  offsetPx.y),
                    float2(0, -offsetPx.y)
                };
                float maxNeighborAlpha = originalAlpha;
                for (int j = 0; j < 4; j++) {
                    float neighborAlpha = tex2D(_MainTex, uvRot + offsets[j]).a;
                    if (neighborAlpha > maxNeighborAlpha) maxNeighborAlpha = neighborAlpha;
                }
                // 光晕强度 = 外部增加的alpha值
                float haloStrength = max(0, maxNeighborAlpha - originalAlpha) * _HaloIntensity;

                // 脉动
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed * 6.28318) * _PulseRange;
                float glowStrength = _GlowIntensity * pulse;

                // 最终颜色: 原图主体 + 外部光晕
                fixed3 finalRGB = tex.rgb * _Color.rgb * glowStrength;
                finalRGB += _Color.rgb * haloStrength * pulse;

                fixed alpha = originalAlpha * i.color.a;
                return fixed4(finalRGB, alpha);
            }
            ENDCG
        }
    }
}
