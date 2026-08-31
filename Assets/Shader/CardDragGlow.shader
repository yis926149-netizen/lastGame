//****************************************
// 卡牌拖拽放置高亮 · 方案甲（轻量发光描边）
// 定位：把 CardPlacement 通道的纯色六边形升级为"自发光能量描边 + 半透明填充"。
// 约束：
//   - 内建渲染管线（CGPROGRAM，无 URP/HDRP）
//   - 无 PostProcessing / bloom → additive 混合 + 明暗渐变伪装辉光
//   - 微信小游戏 WebGL：单 mesh + 单 shader，CPU 每帧零更新（_Time.y 驱动）
// 机制：
//   1) 几何描边带：顶点色 0(中心) → 1(六边形边界)，幂次把亮带压向边界（真正"描边"）
//   2) Fresnel：俯视带透视下，掠射角处自然泛光（边缘亮、中心透）
//   3) 流动：世界坐标 2D 值噪声双向 scroll，调制边界亮度（能量感）
//   4) 呼吸：整体亮度/透明度随 _Time.y 缓慢起伏（"活的能量"而非死贴片）
// 颜色由 _Color 驱动（金/红复用同一材质），金↔红切换只改 Material.color，零额外成本。
//****************************************

Shader "Custom/CardDragGlow"
{
    Properties
    {
        _Color ("Glow Color", Color) = (1, 0.84, 0.28, 1)
        _CenterAlpha ("Center Fill Alpha", Range(0, 2)) = 0.28
        _EdgeStrength ("Edge Glow Strength", Range(0, 3)) = 1.2
        _EdgePower ("Edge Band Power", Range(0.5, 8)) = 2.5
        _RimPower ("Fresnel Rim Power", Range(0.5, 8)) = 3.0
        _FresnelBoost ("Fresnel Boost", Range(0, 2)) = 0.6
        _BreathSpeed ("Breath Speed", Range(0, 8)) = 2.0
        _BreathAmount ("Breath Amount", Range(0, 1)) = 0.22
        _FlowSpeed ("Flow Speed", Range(0, 8)) = 1.6
        _FlowScale ("Flow Scale", Range(0.01, 2)) = 0.5
        _FlowStrength ("Flow Strength", Range(0, 2)) = 0.4
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
        // 被山体/单位正确遮挡
        ZTest LEqual
        // 配合几何抬升，消除与地形共面的 z-fighting
        Offset -1, -1
        Cull Off

        Pass
        {
            Name "CardDragGlow"

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
            };

            fixed4 _Color;
            float _CenterAlpha;
            float _EdgeStrength;
            float _EdgePower;
            float _RimPower;
            float _FresnelBoost;
            float _BreathSpeed;
            float _BreathAmount;
            float _FlowSpeed;
            float _FlowScale;
            float _FlowStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // 网格以世界坐标构建（Root 位于世界原点），仍走标准变换保证正确性
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.edge = v.color.r;
                return o;
            }

            // 2D 值噪声（无贴图依赖，世界坐标采样；避免 Resources/纹理绑定）
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

                // 1) 几何描边带：顶点色 0(中心)→1(边界)，幂次把亮带压向边界
                float edgeBand = pow(i.edge, _EdgePower);

                // 2) Fresnel 掠射泛光：正对相机≈0，掠射角≈1
                float ndotv = saturate(dot(viewDir, worldUp));
                float fresnel = pow(1.0 - ndotv, _RimPower);

                // 3) 流动噪声（两方向 scroll）调制边界亮度 → 能量感
                float2 flowUV = i.worldPos.xz * _FlowScale;
                float n1 = vnoise(flowUV + _Time.y * _FlowSpeed * float2(1.0, 0.6));
                float n2 = vnoise(flowUV - _Time.y * _FlowSpeed * float2(0.7, 0.3));
                float flow = (n1 + n2) * 0.5;

                float edgeGlow = edgeBand * _EdgeStrength;
                edgeGlow *= (0.8 + 0.4 * flow) * (1.0 + fresnel * _FresnelBoost);

                // 4) 呼吸：整体亮度/透明度缓慢起伏
                float breath = 1.0 - _BreathAmount * (0.5 + 0.5 * sin(_Time.y * _BreathSpeed));

                float fill = saturate((_CenterAlpha + edgeGlow) * breath);

                fixed4 c;
                c.rgb = _Color.rgb;   // additive 下明暗即"辉光"
                c.a = fill;
                return c;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
