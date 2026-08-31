//****************************************
// UI 弧光拖尾 · 方案 A（贴图驱动辉光）
// 定位：把"中心过曝白芯 + 外圈暖黄扩散 + 上下软边衰减"全部画进 ribbon 贴图，
//   shader 只做加色混合与强度调制。
// 约束（对应实施计划 C1/C3）：
//   - 内建渲染管线（CGPROGRAM，无 URP/HDRP）
//   - 无 PostProcessing / bloom → additive 混合 + 贴图软边伪装辉光
//   - 微信小游戏 WebGL：#pragma target 3.0，禁用 geometry/compute
//   - UI 专属：Stencil（Mask 依赖）+ UNITY_UI_CLIP_RECT（RectMask2D/ScrollView 裁剪）
// 机制：
//   1) 贴图 V 方向（跨宽度）采样软边渐变 → 辉光形状
//   2) 顶点色 = colorGradient(0=尾端 → 1=头端) × tint → 长度方向淡出 + 独立着色
//   3) 流动：_Time.y 驱动 U 方向值噪声 scroll，调制亮度（能量感）
//   4) 呼吸：整体亮度随 _Time.y 缓慢起伏（"活的能量"而非死贴片）
//   CPU 每帧零更新，_Time.y 驱动。
// 颜色由 _Color + 顶点色驱动，同一材质可被多条尾巴共享（合批）。
//****************************************

Shader "Custom/UITrailGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Glow Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        // ── UI 必需属性（Mask / RectMask2D 依赖）──
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        // ── 动态感（_Time.y 驱动）──
        _BreathSpeed ("Breath Speed", Range(0, 8)) = 2.0
        _BreathAmount ("Breath Amount", Range(0, 1)) = 0.22
        _FlowSpeed ("Flow Speed", Range(0, 8)) = 1.6
        _FlowScale ("Flow Scale", Range(0.5, 32)) = 8.0
        _FlowStrength ("Flow Strength", Range(0, 2)) = 0.35
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        // UI 层：不参与深度
        ZTest Always
        // additive 伪辉光（无 bloom 的唯一可行解）：alpha 当强度用
        Blend SrcAlpha One
        ColorMask [_ColorMask]

        Pass
        {
            Name "UITrailGlow"

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1; // 实际为局部坐标，供 RectMask2D 裁剪
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _BreathSpeed;
            float _BreathAmount;
            float _FlowSpeed;
            float _FlowScale;
            float _FlowStrength;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            // 2D 值噪声（与 CardDragGlow.shader 同源，无贴图依赖）
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half4 tex = tex2D(_MainTex, i.texcoord) + _TextureSampleAdd;

                // 方案 A：软边衰减已编码在贴图 alpha 里，这里只做加色混合 + 强度调制。
                half4 col = tex;
                col.rgb *= _Color.rgb;
                col.a *= _Color.a;

                // 流动：U 方向（沿尾巴）scroll 噪声，调制亮度 → 能量沿尾巴奔跑
                float flow = 1.0;
                if (_FlowStrength > 0.001)
                {
                    float2 flowUV = float2(i.texcoord.x * _FlowScale + _Time.y * _FlowSpeed, i.texcoord.y);
                    float n = vnoise(flowUV);
                    flow = 1.0 + _FlowStrength * (n * 2.0 - 1.0);
                }

                // 呼吸：整体亮度缓慢起伏
                float breath = 1.0 - _BreathAmount * (0.5 + 0.5 * sin(_Time.y * _BreathSpeed));

                col.rgb *= flow * breath;

                // 顶点色：colorGradient × tint（C# 已写入）
                col *= i.color;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
