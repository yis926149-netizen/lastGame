//****************************************
// 卡牌拖拽放置高亮 · 方案乙（能量围墙 + 顶部渐隐光柱）
// 定位：在方案甲（贴地发光描边）基础上，把 CardPlacement 通道升级为"立体六边形结界"：
//   - 顶面（地面）半透明填充，边缘亮、中心透（同方案甲 fresnel + 几何描边带）
//   - 6 面低矮能量围墙自外圈边缘垂直立起：摇曳光幕——噪声切出垂直光丝（丝间透明间隙），
//     光丝随时间横向摇曳（越靠顶部甩动越大，像垂挂幕布），底部亮、向上渐隐，能量沿光丝上行；
//     墙顶不再有硬顶环带（参考图无"缸沿"，光向上消散；见方案 zeta 去牢笼改造）。
//   - 6 根角柱"光柱"：在角点交叉竖面、高于围墙向上拔高、底部亮顶渐隐（顶点色 a=1 掩码）
//   - 格心中央光柱（图三式）：交叉竖面更高更粗，亮段保持、顶端渐隐（顶点色 a=0.5 掩码）
// 注：顶面中心填充压到很低（_CenterAlpha≈0.02）+ 顶亮边带加幂收紧（_EdgePower 上调），
//     让格心透明、只见六边形亮框 + 低墙 + 光柱，外发光集中而不是"无结构的整片光团"。
// 约束：内建渲染管线 + 无 bloom（additive 伪装辉光）+ 微信小游戏 WebGL（单 mesh + 单 shader）。
// 顶点色语义（由 HexHighlightRenderer.RebuildChannel 写入）：
//   r = 顶面几何描边带（0=中心 → 1=边界）；角柱/光柱竖面 = 横向剖面（0=两侧缘 → 1=中线，
//       由 AppendGlowBeamQuad 把竖面细分 3 列写入，着色器用 pow 羽化成"中心亮、两侧透"的体积光束）
//   g = 墙面掩码（0=顶面/光柱，1=墙面竖面）
//   b = 竖直梯度（墙面竖面/光柱 0=底 → 1=顶；其余恒 0）
//   a = 光柱掩码（0=顶面/墙面，1=角柱光柱 = PillarMaskByte，0.5=中央光柱 = BeamMaskByte）
// 颜色由 _Color 驱动（绿/红复用同一材质），绿↔红切换只改 Material.color。
// 墙高/柱高由 C# 常量（CardPlacementWallHeight / CardPlacementPillarHeight）写进顶点，
// 本 shader 无需 _BaseY/_WallHeight：竖直渐变用顶点色 b 通道插值，天然适配各格不同地形高度。
//****************************************

Shader "Custom/CardDragPillarGlow"
{
    Properties
    {
        _Color ("Glow Color", Color) = (0.2, 1.15, 0.16, 1)
        _CenterAlpha ("Top Center Fill Alpha", Range(0, 2)) = 0.02
        _EdgeStrength ("Top Edge Glow Strength", Range(0, 3)) = 1.7
        _EdgePower ("Top Edge Band Power", Range(0.5, 8)) = 3.5
        _RimPower ("Fresnel Rim Power", Range(0.5, 8)) = 3.0
        _FresnelBoost ("Fresnel Boost", Range(0, 2)) = 0.6
        _WallAlpha ("Wall Base Alpha", Range(0, 3)) = 0.65
        _WallTopResidual ("Wall Top Residual Glow", Range(0, 1)) = 0.03
        _CurtainDensity ("Curtain Strand Density", Range(0.5, 8)) = 1.6
        _CurtainGap ("Curtain Gap Threshold", Range(0, 1)) = 0.65
        _CurtainSoft ("Curtain Strand Softness", Range(0.01, 0.5)) = 0.22
        _SwaySpeed ("Sway Speed", Range(0, 4)) = 0.9
        _SwayAmount ("Sway Amount", Range(0, 2)) = 0.4
        _SwayFrequency ("Sway Frequency", Range(0, 4)) = 1.1
        _BreathSpeed ("Breath Speed", Range(0, 8)) = 2.0
        _BreathAmount ("Breath Amount", Range(0, 1)) = 0.2
        _FlowSpeed ("Flow Speed", Range(0, 8)) = 1.8
        _FlowScale ("Flow Scale", Range(0.01, 2)) = 0.5
        _FlowStrength ("Flow Strength", Range(0, 2)) = 0.6
        _PillarAlpha ("Corner Pillar Alpha", Range(0, 2)) = 0.7
        _PillarFalloff ("Corner Pillar Height Falloff", Range(0.5, 6)) = 2.6
        _BeamAlpha ("Center Beam Alpha", Range(0, 3)) = 1.2
        _BeamFalloff ("Center Beam Taper", Range(0.5, 6)) = 1.4
        _BeamResidual ("Center Beam Top Residual", Range(0, 1)) = 0.3
        _BeamProfilePower ("Beam Cross-Section Power", Range(0.5, 6)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
        }

        LOD 100

        // additive 伪辉光（无 bloom 的唯一可行解）
        Blend SrcAlpha One
        ZWrite Off
        // 被山体/单位正确遮挡（§七 取舍：需无视遮挡时再改 Always）
        ZTest LEqual
        // 配合几何抬升，消除与地形共面的 z-fighting
        Offset -1, -1
        Cull Off

        Pass
        {
            Name "CardDragPillarGlow"

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float  edge     : TEXCOORD1;
                float  wallMask : TEXCOORD2;
                float  wallV    : TEXCOORD3;
                float  pillar   : TEXCOORD4;
            };

            fixed4 _Color;
            float _CenterAlpha;
            float _EdgeStrength;
            float _EdgePower;
            float _RimPower;
            float _FresnelBoost;
            float _WallAlpha;
            float _WallTopResidual;
            float _CurtainDensity;
            float _CurtainGap;
            float _CurtainSoft;
            float _SwaySpeed;
            float _SwayAmount;
            float _SwayFrequency;
            float _BreathSpeed;
            float _BreathAmount;
            float _FlowSpeed;
            float _FlowScale;
            float _FlowStrength;
            float _PillarAlpha;
            float _PillarFalloff;
            float _BeamAlpha;
            float _BeamFalloff;
            float _BeamResidual;
            float _BeamProfilePower;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // 网格以世界坐标构建（Root 位于世界原点），仍走标准变换保证正确性
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.edge = v.color.r;
                o.wallMask = v.color.g;
                o.wallV = v.color.b;
                o.pillar = v.color.a;
                return o;
            }

            // 2D 值噪声（无贴图依赖，世界坐标采样）
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
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 worldUp = float3(0.0, 1.0, 0.0);

                // ---- 顶面（地面）填充：与方案甲一致 ----
                float edgeBand = pow(i.edge, _EdgePower);
                float ndotv = saturate(dot(viewDir, worldUp));
                float fresnel = pow(1.0 - ndotv, _RimPower);

                float2 flowUV = i.worldPos.xz * _FlowScale;
                float n1 = vnoise(flowUV + _Time.y * _FlowSpeed * float2(1.0, 0.6));
                float n2 = vnoise(flowUV - _Time.y * _FlowSpeed * float2(0.7, 0.3));
                float flow = (n1 + n2) * 0.5;

                float edgeGlow = edgeBand * _EdgeStrength;
                edgeGlow *= (0.8 + 0.4 * flow) * (1.0 + fresnel * _FresnelBoost);
                float topFill = _CenterAlpha + edgeGlow;

                // ---- 围墙竖面：摇曳光幕（垂直光丝 + 摇曳 + 底部亮→顶部渐隐 + 上升流动）----
                float wallFade = smoothstep(0.0, 1.0, i.wallV);
                float wallGradient = lerp(1.0, _WallTopResidual, wallFade);   // 1 底 → 残光 顶

                // 沿墙周长方向的坐标；摇曳量随高度放大（幕布下摆近固定、上端甩动大）
                float u = (i.worldPos.x + i.worldPos.z) * _FlowScale;
                float sway = sin(_Time.y * _SwaySpeed + u * _SwayFrequency) * _SwayAmount;
                float swayedU = u + sway * i.wallV;

                // 光丝：两层噪声叠出垂直丝带，smoothstep 切出丝与丝之间的透明间隙（光幕感）
                float strandA = vnoise(float2(swayedU * _CurtainDensity, i.wallV * 0.7));
                float strandB = vnoise(float2(swayedU * _CurtainDensity * 2.9 + 13.71, i.wallV * 1.3));
                float strand = strandA * 0.65 + strandB * 0.35;
                float strandMask = smoothstep(_CurtainGap, _CurtainGap + _CurtainSoft, strand);

                // 上升流动（能量沿光丝上行）；减 _Time 使噪声向上流动
                float flowNoise = vnoise(float2(swayedU * _CurtainDensity, i.wallV * 2.0) - float2(0.0, _Time.y * _FlowSpeed));
                float wallFlow = 1.0 - _FlowStrength * 0.5 + _FlowStrength * flowNoise;

                float wallFill = wallGradient * _WallAlpha * strandMask * wallFlow;

                // ---- 顶面 / 墙面合成（wallMask 0=顶面 1=墙面）----
                float fill = lerp(topFill, wallFill, i.wallMask);

                // ---- 光柱/中央光柱（a 通道：0=无 0.5≈中央光柱 1≈角柱）：底部亮→顶渐隐 ----
                if (i.pillar > 0.4)
                {
                    float vv = smoothstep(0.0, 1.0, i.wallV);
                    if (i.pillar > 0.9)
                    {
                        // 角柱：收敛较快，聚焦成细柱（图二式四角升腾光丝）
                        float pFade = pow(saturate(1.0 - vv), _PillarFalloff);
                        fill = _PillarAlpha * pFade * (0.85 + 0.3 * flow);
                    }
                    else
                    {
                        // 中央光柱：亮段保持更长、顶端才渐隐，叠加向上流动的竖向光纹
                        float bFade = lerp(1.0, _BeamResidual, pow(vv, _BeamFalloff));
                        float bWisp = vnoise(float2(i.wallV * 6.0 - _Time.y * _FlowSpeed * 0.8, i.wallV * 1.3));
                        fill = _BeamAlpha * bFade * (0.9 + 0.2 * flow + 0.18 * bWisp);
                    }
                    // 横向剖面羽化：r=1 中线亮 → r=0 两侧缘透（AppendGlowBeamQuad 细分 3 列写入）。
                    // 幂次把光聚到中央细核、两侧快速消隐 → 读作体积光束，而非均匀亮色的扁平"纸片"。
                    fill *= pow(saturate(i.edge), _BeamProfilePower);
                }

                // 呼吸：整体亮度/透明度缓慢起伏
                float breath = 1.0 - _BreathAmount * (0.5 + 0.5 * sin(_Time.y * _BreathSpeed));
                fill = saturate(fill * breath);

                fixed4 c;
                c.rgb = _Color.rgb;   // additive 下明暗即"辉光"
                // _Color.a 兼作整体不透明度：Blend SrcAlpha One 下 srcAlpha 缩放 additive 贡献，
                // 故 a<1 整层更透更弱（不改变 RGB 色相/明度结构，只降"透度"）。a=1 保持现状。
                c.a = fill * _Color.a;
                return c;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
