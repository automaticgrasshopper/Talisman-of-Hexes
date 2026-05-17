Shader "UI/FlowLine"
{
    /*
        UI 流程图连线 Shader
        ─────────────────────────────────────────────
        支持三种效果，在 Inspector 里用 Material 控制：

        1. 普通实线（已解锁）
           - _Color 设成亮色，_GlowIntensity = 0

        2. 流光实线（已解锁 + 动态效果）
           - _GlowIntensity > 0，光带会沿线条方向移动
           - _GlowSpeed 控制速度，_GlowWidth 控制光带宽度

        3. 虚线（未解锁，C# 层已处理虚线几何，
           这里只负责颜色和透明度淡化）

        UV 说明：
          uv.x = 沿线方向 0→1（C# 层每小段独立 0→1，供 shader 采样）
          uv.y = 垂直方向 0→1（0=一侧边缘，0.5=中心，1=另一侧边缘）
    */

    Properties
    {
        // ── 基础 ──
        _MainTex      ("Texture",        2D)    = "white" {}
        _Color        ("Tint",           Color) = (1,1,1,1)

        // ── 流光效果（0=关闭）──
        _GlowIntensity ("Glow Intensity", Range(0,2))    = 0.8
        _GlowSpeed     ("Glow Speed",     Range(0,5))    = 1.5
        _GlowWidth     ("Glow Width",     Range(0.01,1)) = 0.25
        _GlowColor     ("Glow Color",     Color)         = (1,1,0.6,1)

        // ── 边缘柔化（让线条边缘有抗锯齿感）──
        _EdgeSoftness ("Edge Softness",  Range(0,0.5)) = 0.15

        // ── Unity UI 内部用，不要手动改 ──
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
            Name "FlowLine"

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            // ── 属性 ──
            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;

            float  _GlowIntensity;
            float  _GlowSpeed;
            float  _GlowWidth;
            fixed4 _GlowColor;

            float  _EdgeSoftness;

            // Unity UI 裁剪
            float4 _ClipRect;

            // ── 顶点结构 ──
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

            // ── 顶点着色器 ──
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

            // ── 片元着色器 ──
            fixed4 frag(v2f IN) : SV_Target
            {
                // 基础颜色
                fixed4 col = IN.color;
                col *= tex2D(_MainTex, IN.uv);

                // ── 边缘柔化 ──
                float edgeDist = abs(IN.uv.y - 0.5) * 2.0;
                float edgeMask = 1.0 - smoothstep(1.0 - _EdgeSoftness * 2.0, 1.0, edgeDist);
                col.a *= edgeMask;

                // ── 流光效果 ──
                if (_GlowIntensity > 0.001)
                {
                    float t        = frac(_Time.y * _GlowSpeed);
                    float glowU    = frac(IN.uv.x - t);
                    float glowMask = 1.0 - smoothstep(0.0, _GlowWidth, abs(glowU - 0.5) * 2.0);
                    glowMask = saturate(glowMask);

                    fixed4 glowContrib = _GlowColor * glowMask * _GlowIntensity;
                    col.rgb += glowContrib.rgb * col.a;
                }

                // Unity UI 矩形裁剪
                col.a *= UnityGet2DClipping(IN.worldPos.xy, _ClipRect);

                clip(col.a - 0.001);

                return col;
            }
            ENDCG
        }
    }
}
