Shader "Custom/CardEdgeFlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // ---- 阶段一：静态轮廓 Mask ----
        _OutlineColor ("Outline Color", Color) = (1,0,0,1)
        _OutlineWidth ("Outline Width", Range(0, 64)) = 3
        [Toggle(_WIDTH_IN_SCREEN_PX)] _WidthInScreenPx ("宽度按屏幕像素（推荐，跨贴图一致）", Float) = 1
        _OutlineSoftness ("Outline Softness", Range(0, 1)) = 0.05

        // ---- 阶段二/三：移动高光 ----
        _FlowTex ("Flow Gradient (循环渐变)", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (0.49, 0.92, 1.0, 1.0)   // #7DEBFF
        _GlowIntensity ("Glow Intensity", Range(0, 10)) = 3
        _FlowSpeed ("Flow Speed", Range(-2, 2)) = 0.5
        _FlowTiling ("Flow Tiling (波数)", Range(0.25, 8)) = 1
        _FlowBase ("Flow Base (底光)", Range(0, 1)) = 0.15
        [Toggle(_USE_FLOWTEX)] _UseFlowTex ("阶段三：用渐变纹理替代正弦波", Float) = 1
        [Toggle(_FLOW_TINT_BY_TEX)] _FlowTintByTex ("渐变纹理带色（否则只取亮度）", Float) = 1

        // ---- 绕圈模式：极角驱动，高光沿轮廓循环 ----
        [Toggle(_FLOW_ANGULAR)] _FlowAngular ("绕圈流动（极角驱动）", Float) = 1
        _FlowCenter ("Flow Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _FlowAspect ("Flow Aspect (宽高比校正)", Range(0.25, 4)) = 1

        // ---- 阶段四 方式B：Shader 内部外发光（Overlay Canvas 下 Bloom 无效时使用）----
        [Toggle(_INNER_GLOW)] _UseInnerGlow ("内部柔光（不依赖后处理）", Float) = 1
        _OuterGlowWidth ("Outer Glow Width (px)", Range(0, 96)) = 40
        _OuterGlowAlpha ("Outer Glow Alpha", Range(0, 3)) = 1.2
        _OuterGlowFalloff ("Outer Glow Falloff", Range(0.25, 4)) = 0.8

        [Toggle(_DRAW_SPRITE)] _DrawSprite ("绘制原图（接卡牌时必开，阶段一诊断时关）", Float) = 1
        _GlowMaster ("Glow Master (总开关 0~1)", Range(0, 1)) = 1

        // ---- 金币不足：自上而下解锁的压暗覆盖（叠加在灰版卡面之上）----
        // _DimFill = 1 - 金币/卡费 = 底部暗区占卡面高度的比例。
        // 0 表示攒满（不压暗），1 表示整张压暗；顶部先恢复，暗区随金币增加从底部向上收缩。
        _DimFill ("压暗覆盖比例 0~1（自上而下解锁）", Range(0, 1)) = 0
        _DimColor ("压暗颜色（a=压暗强度）", Color) = (0, 0, 0, 0.55)
        _DimEdge ("压暗边界过渡带宽度", Range(0, 0.25)) = 0.02

        // ---- UGUI 标准模板/剪裁参数 ----
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CardEdgeFlow"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma shader_feature_local _USE_FLOWTEX
            #pragma shader_feature_local _FLOW_TINT_BY_TEX
            #pragma shader_feature_local _FLOW_ANGULAR
            #pragma shader_feature_local _INNER_GLOW
            #pragma shader_feature_local _DRAW_SPRITE
            #pragma shader_feature_local _WIDTH_IN_SCREEN_PX

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            sampler2D _FlowTex;
            float4 _FlowTex_ST;
            fixed4 _Color;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineSoftness;
            fixed4 _GlowColor;
            float _GlowIntensity;
            float _FlowSpeed;
            float _FlowTiling;
            float _FlowBase;
            float4 _FlowCenter;
            float _FlowAspect;
            float _OuterGlowWidth;
            float _OuterGlowAlpha;
            float _OuterGlowFalloff;
            float _GlowMaster;
            float _DimFill;
            fixed4 _DimColor;
            float _DimEdge;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 中心像素的 alpha（只看形状，不含 tint）
                half centerAlpha = tex2D(_MainTex, IN.texcoord).a;

                // 采样步长（UV 单位）。
                // 屏幕像素模式：fwidth 给出「1 屏幕像素对应多少 UV」，于是同一个宽度值
                // 在任何贴图分辨率 / Image 尺寸下都得到一致的视觉粗细。
                // 纹素模式：沿用 _MainTex_TexelSize，Image 缩放时粗细跟随贴图（旧行为）。
                #ifdef _WIDTH_IN_SCREEN_PX
                    float2 uvPerPixel = fwidth(IN.texcoord);
                    float2 unit = uvPerPixel;
                #else
                    float2 unit = _MainTex_TexelSize.xy;
                #endif

                // 以 unit 为步长做轮廓检测。
                // 单圈 8 方向在宽度大时采样点间隙盖不住，边缘会出现星芒状毛刺；
                // 改为 16 方向 × 2 圈（半径 50% / 100%），把间隙填上。
                float2 texel = unit * _OutlineWidth;
                half n = 0;
                UNITY_UNROLL
                for (int i = 0; i < 16; i++)
                {
                    // 16 个均匀角度，预先算出 sin/cos 方向向量
                    float ang = (i / 16.0) * UNITY_TWO_PI;
                    float2 dir = float2(cos(ang), sin(ang));
                    n = max(n, tex2D(_MainTex, IN.texcoord + dir * texel).a);
                    n = max(n, tex2D(_MainTex, IN.texcoord + dir * texel * 0.5).a);
                }

                // 轮廓判定：周围不透明、中心透明 => 邻居 alpha 高于中心 alpha
                half diff = n - centerAlpha;
                half edge = saturate(diff);
                edge = smoothstep(0.0, _OutlineSoftness, edge);

                // ---- 流动相位 ----
                #ifdef _FLOW_ANGULAR
                    // 绕圈模式：用相对中心的极角作为相位，高光沿轮廓循环一周
                    // atan2 返回 [-PI, PI]，归一化到 [0,1] 后天然首尾相接
                    float2 d = IN.texcoord - _FlowCenter;
                    d.x *= _FlowAspect;                       // 校正非正方形贴图/UV 的角度畸变
                    float angle = atan2(d.y, d.x) * (1.0 / UNITY_TWO_PI) + 0.5;
                    float phase = angle * _FlowTiling + _Time.y * _FlowSpeed;
                #else
                    // 斜向相位：(u+v) 得到 45° 条纹，随时间滚动
                    float phase = (IN.texcoord.x + IN.texcoord.y) * _FlowTiling - _Time.y * _FlowSpeed;
                #endif

                #ifdef _USE_FLOWTEX
                    // 阶段三：把相位当作渐变纹理的 U 坐标滚动采样（纹理需 Repeat 且首尾同值）
                    half4 flowTex = tex2D(_FlowTex, float2(phase, 0.5));
                    half wave = max(max(flowTex.r, flowTex.g), flowTex.b);
                #else
                    // 阶段二：纯正弦波
                    half wave = sin(phase * UNITY_TWO_PI) * 0.5 + 0.5;
                #endif

                half flow = _FlowBase + (1.0 - _FlowBase) * wave;   // 保底亮度，避免暗段完全消失

                // 亮边（预乘 alpha 输出，匹配 Blend One OneMinusSrcAlpha）
                // alpha 只由轮廓决定，亮度由 flow/intensity 决定 —— 这样 >1 的 HDR 亮度能溢出给 Bloom
                half3 glowRGB = _GlowColor.rgb;
                #if defined(_USE_FLOWTEX) && defined(_FLOW_TINT_BY_TEX)
                    // 让渐变纹理自带的 黑→蓝→白 色相参与着色，而不只当亮度遮罩
                    glowRGB *= flowTex.rgb / max(wave, 1e-4);
                #endif

                half a = edge * _GlowColor.a;
                half3 rgb = glowRGB * (flow * _GlowIntensity) * a;

                #ifdef _INNER_GLOW
                    // 方式B：外发光。关键是多层半径累加 —— 单一半径只能得到一圈细环，
                    // 必须由内向外多圈采样并按距离加权，才有真正的衰减渐变。
                    // 4 圈 × 8 方向 = 32 次采样，内圈权重高、外圈权重低。
                    half halo = 0;
                    half wsum = 0;
                    UNITY_UNROLL
                    for (int ring = 1; ring <= 4; ring++)
                    {
                        float r = _OuterGlowWidth * (ring / 4.0);
                        float2 rt = unit * r;
                        half s = 0;
                        s += tex2D(_MainTex, IN.texcoord + float2( rt.x, 0.0)).a;
                        s += tex2D(_MainTex, IN.texcoord + float2(-rt.x, 0.0)).a;
                        s += tex2D(_MainTex, IN.texcoord + float2(0.0,  rt.y)).a;
                        s += tex2D(_MainTex, IN.texcoord + float2(0.0, -rt.y)).a;
                        // 对角线距离是 sqrt(2) 倍，乘 0.707 保持各向同性
                        float2 dg = rt * 0.7071;
                        s += tex2D(_MainTex, IN.texcoord + float2( dg.x,  dg.y)).a;
                        s += tex2D(_MainTex, IN.texcoord + float2( dg.x, -dg.y)).a;
                        s += tex2D(_MainTex, IN.texcoord + float2(-dg.x,  dg.y)).a;
                        s += tex2D(_MainTex, IN.texcoord + float2(-dg.x, -dg.y)).a;
                        half w = 1.0 / ring;               // 越远权重越低 => 向外衰减
                        halo += (s * 0.125) * w;
                        wsum += w;
                    }
                    halo /= wsum;

                    halo = saturate(halo * (1.0 - centerAlpha));     // 只留在形状外侧
                    halo = pow(halo, _OuterGlowFalloff);
                    halo *= _OuterGlowAlpha * _GlowColor.a * flow;

                    // 光晕走加法混合：Blend One OneMinusSrcAlpha 下，rgb 加光而 alpha 少加，
                    // 才是"发光"而不是"贴一层半透明色块"。
                    rgb += glowRGB * _GlowIntensity * halo * (1.0 - a);
                    a   += halo * 0.5 * (1.0 - a);
                #endif

                fixed4 col = fixed4(rgb, a);

                #ifdef _DRAW_SPRITE
                    // 接入卡牌：原图在下，流光在上。原图走 Image.color（含 tint 与拖拽淡出的 alpha），
                    // 转预乘后与流光做 over 合成 —— 流光此时已按 _GlowMaster 缩放。
                    col *= _GlowMaster;
                    fixed4 src = tex2D(_MainTex, IN.texcoord) * IN.color;

                    // 金币不足压暗：自顶向下解锁 —— 顶部先恢复，暗区留在底部并随金币增加而收缩。
                    // IN.texcoord.y 自下而上 0→1；_DimFill = 1 - 金币/卡费 = 底部暗区占卡面高度的比例。
                    // 即 y < _DimFill（靠近底边）压暗，y >= _DimFill（靠近顶边）保持原色。
                    // 只改 rgb 不动 a：alpha 归拖拽淡出独占，两者互不干扰。
                    // 压在 IN.color 之后 => 灰版卡面与压暗自然叠加（先灰后暗）。
                    half dimMask;
                    if (_DimFill <= 0.0001)
                    {
                        // 攒够金币：强制整卡不压暗。避免 smoothstep 过渡带越过 y=0 边界，
                        // 在底边留下一条挥之不去的半透明假暗线。
                        dimMask = 0;
                    }
                    else if (_DimFill >= 0.9999)
                    {
                        // 完全买不起：强制整卡压暗到底，同理避免 y=1 顶边的过渡带假象。
                        dimMask = 1;
                    }
                    else
                    {
                        dimMask = 1.0 - smoothstep(_DimFill - _DimEdge, _DimFill + _DimEdge, IN.texcoord.y);
                    }
                    src.rgb = lerp(src.rgb, _DimColor.rgb, dimMask * _DimColor.a);

                    fixed4 baseCol = fixed4(src.rgb * src.a, src.a);
                    col = baseCol * (1.0 - col.a) + col;
                #endif

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
