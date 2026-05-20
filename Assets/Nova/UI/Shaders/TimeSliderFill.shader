Shader "UI/TimeSliderFill"
{
    /*
        TimeSlider 倒计时条 fill 层 shader
        ─────────────────────────────────────────────
        和 FlowLine / ChoiceButtonFX 同一套美术语：暖金主色 + 流光带 + 边缘柔化。
        额外特性：
          1. 沿 uv.x 持续流动的光带（_FlowSpeed/_FlowWidth/_FlowIntensity）
          2. 右端"燃烧"前缘 leading edge（_LeadGlow/_LeadWidth）—— bar 缩短时永远跟在最右
          3. Urgency 危险脉冲：剩余时间越少 _Urgency 越大
             - 颜色向 _DangerColor 偏移
             - 整体亮度按 sin(time * speed) 脉冲，speed 也随 urgency 加快
          4. 顶/底边缘柔化做软抗锯齿

        UV：
          uv.x = 0(左) → 1(右) 沿条方向
          uv.y = 0(底) → 1(顶) 垂直方向
    */

    Properties
    {
        _MainTex      ("Texture",     2D)    = "white" {}
        _Color        ("Base Color",  Color) = (0.95, 0.78, 0.32, 1)
        _GlowColor    ("Glow / Edge", Color) = (1.0, 0.92, 0.55, 1)
        _DangerColor  ("Danger Color", Color) = (1.0, 0.42, 0.22, 1)

        // 流光带
        _FlowSpeed     ("Flow Speed",     Range(0,5))    = 1.4
        _FlowWidth     ("Flow Band Width", Range(0.05,0.6)) = 0.28
        _FlowIntensity ("Flow Intensity", Range(0,2))    = 0.7

        // 右端 leading edge
        _LeadGlow  ("Leading Edge Glow",  Range(0,3))    = 1.4
        _LeadWidth ("Leading Edge Width", Range(0.005,0.25)) = 0.06

        // 危险脉冲
        _Urgency        ("Urgency 0-1",     Range(0,1))  = 0
        _PulseAmp       ("Pulse Amplitude", Range(0,1))  = 0.45
        _PulseSpeedBase ("Pulse Speed Base", Range(0,15)) = 2

        // 边缘柔化
        _EdgeSoftness ("Edge Softness", Range(0,0.5)) = 0.18

        // 横向暗扫描线（科幻/恐怖味）
        _ScanFreq    ("Scanline Freq",  Range(10,200)) = 90
        _ScanDarken  ("Scanline Darken", Range(0,0.3)) = 0.06

        [HideInInspector] _StencilComp      ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil          ("Stencil ID",         Float) = 0
        [HideInInspector] _StencilOp        ("Stencil Operation",  Float) = 0
        [HideInInspector] _StencilReadMask  ("Stencil Read Mask",  Float) = 255
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _ColorMask        ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull     Off
        Lighting Off
        ZWrite   Off
        ZTest    [unity_GUIZTestMode]
        Blend    SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "TimeSliderFill"

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            fixed4    _GlowColor;
            fixed4    _DangerColor;

            float _FlowSpeed, _FlowWidth, _FlowIntensity;
            float _LeadGlow, _LeadWidth;
            float _Urgency, _PulseAmp, _PulseSpeedBase;
            float _EdgeSoftness;
            float _ScanFreq, _ScanDarken;

            float4 _ClipRect;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPos = IN.vertex;
                OUT.vertex   = UnityObjectToClipPos(IN.vertex);
                OUT.uv       = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color    = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.uv;
                fixed4 baseTex = tex2D(_MainTex, uv);

                // 基础色：金 → 危险色随 urgency 偏移
                fixed3 baseRGB = lerp(_Color.rgb, _DangerColor.rgb, saturate(_Urgency * 0.85));
                fixed4 col;
                col.rgb = baseRGB;
                col.a   = IN.color.a * baseTex.a;

                // 边缘柔化
                float eDist  = abs(uv.y - 0.5) * 2.0;
                float eMask  = 1.0 - smoothstep(1.0 - _EdgeSoftness * 2.0, 1.0, eDist);
                col.a *= eMask;

                // 流光带（沿 uv.x 滚）
                float t        = frac(_Time.y * _FlowSpeed);
                float glowU    = frac(uv.x - t);
                float bandMask = 1.0 - smoothstep(0.0, _FlowWidth, abs(glowU - 0.5) * 2.0);
                col.rgb += _GlowColor.rgb * bandMask * _FlowIntensity * col.a;

                // 右端 leading edge 燃烧高光（uv.x 越接近 1 越亮）
                float lead = smoothstep(1.0 - _LeadWidth, 1.0, uv.x);
                col.rgb += _GlowColor.rgb * lead * _LeadGlow * col.a;

                // 危险脉冲：urgency 越大 → 整体颜色越亮 + 频率越高
                float pulseSpeed = _PulseSpeedBase + _Urgency * 9.0;
                float pulse      = 0.5 + 0.5 * sin(_Time.y * pulseSpeed);
                col.rgb += baseRGB * pulse * _PulseAmp * _Urgency * col.a;

                // 暗扫描线
                float scan = 0.5 + 0.5 * sin(uv.y * _ScanFreq);
                col.rgb -= scan * _ScanDarken * col.a;

                // UI 裁剪
                col.a *= UnityGet2DClipping(IN.worldPos.xy, _ClipRect);
                clip(col.a - 0.001);
                return col;
            }
            ENDCG
        }
    }
}
