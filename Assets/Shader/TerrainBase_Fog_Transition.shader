Shader "Custom/TerrainBase_Fog_Transition"
{
    //【动态地图-阶段四修订】TerrainBase_Fog 的动画专用变体（§十九-21/§20-10）。
    //【同步契约-2026-08-05 修订】顶点动画已改为 C# 侧逐帧写 mesh.vertices（vert 不读 UV2/UV3，
    //   禁止把顶点变形加回 vert，否则与 C# 路径双重位移）。与稳定版的全部差异：
    //     ① Properties 增加 _ChunkProgress / _ChunkAnimBaseY / _ChunkAnimRiseHeight；
    //     ② Input 增加 worldPos；③ surf 末尾 keep-below clip；④ 末尾手动 ShadowCaster pass。
    //   除上述四点外，混合逻辑主体与 fogFinal 必须与稳定版 TerrainBase_Fog 逐字保持一致。
    // - 仅在动画期间由 ChunkMapRenderer 按 Chunk 切换使用；稳定渲染永不加载本 Shader，
    //   因此不会像阶段四首次实施那样破坏稳定地图渲染（§十九-21 根因=直接修改稳定 Shader）。
    // - mesh 上的动画数据通道（仅供 C# 侧 ChunkMapRenderer.SetChunkAnimationProgress 读取，§20-10）：
    //     UV2 (texcoord1): x=startVertexY、y=targetVertexY
    //     UV3 (texcoord2): x=staggerDelay [0,1]、y=participatesInTransition (1=参与, 0=不参与)
    // - 顶出方案-修订：surf 与末尾手动 ShadowCaster pass 执行同一 keep-below clip 平面
    //   （_ChunkAnimBaseY + _ChunkProgress*_ChunkAnimRiseHeight），阴影几何与可见几何一致（§13.2/§13.3）。
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _ChunkProgress ("Chunk Animation Progress", Range(0,1)) = 0.0
        // 【顶出方案-修订】clip 平面参数（每 Chunk 经 MaterialPropertyBlock 设置，动画期间恒定）：
        // _ChunkAnimBaseY = 本 Chunk 动画顶点最低 startY；_ChunkAnimRiseHeight = 最高 targetY - _ChunkAnimBaseY。
        // surf 与 ShadowCaster 按 _ChunkAnimBaseY + _ChunkProgress*_ChunkAnimRiseHeight 做 keep-below clip：
        // 动画起点新平台整体隐藏（只露旧地形快照 TerrainGhost），随进度从旧地表下逐层"顶出"。
        _ChunkAnimBaseY ("Chunk Anim Clip Base Y", Float) = 0.0
        _ChunkAnimRiseHeight ("Chunk Anim Clip Rise Height", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        // 地形含竖面/过渡面，保持双面渲染。
        Cull Off

        CGPROGRAM
        // 注意：不写 addshadow——SubShader 末尾的手动 ShadowCaster pass 执行同一 clip 平面，
        // 保证阴影几何与可见几何一致（addshadow 自动 pass 不执行 surf 的 clip，曾致"隐形地形黑块"）。
        #pragma surface surf Standard fullforwardshadows vertex:vert finalcolor:fogFinal
        #pragma target 3.0

        #include "FogBlend.cginc"

        // 注意：Input 只保留必需的插值量（与稳定版一致，动画数据只在 vert 内读取，不占插值器）。
        sampler2D _MainTex;
        sampler2D _BumpMap;

        struct Input
        {
            float2 uv_MainTex;
            float2 fogCoord;       // 迷雾整图归一化 UV（vert 里由世界 XZ 计算）
            float3 worldPos;       // 【顶出方案】片元世界坐标（clip 平面用，vert 变形后自动计算）
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;
        float _ChunkProgress;
        float _ChunkAnimBaseY;
        float _ChunkAnimRiseHeight;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            // 【CPU动画-2026-08-05】顶点动画改为 C# 侧逐帧写 mesh.vertices（SetChunkAnimationProgress），
            // 不再在 vert 内读取 UV2/UV3 插值（surface shader 编译对未声明 UV 通道读取不可靠，
            // 已由三次实验确认：无条件插值无效、无条件 +5 有效 → vert 执行但 texcoord1/2 数据不可信）。
            // 顶点变形后计算迷雾世界坐标，保证迷雾跟随动画高度（§13.3）
            FogBlend_vert(v.vertex, o.fogCoord);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // 【顶出方案-修订】clip 平面（keep-below）：丢弃高于当前动画高度线的片元。
            // 动画起点（progress=0）新平台整体隐藏，旧地形快照（TerrainGhost）完整可见；
            // 随进度升高，新平台从旧地表下逐层"顶出"，消除"先变平再升起"的拓扑突变观感。
            float animClipY = _ChunkAnimBaseY + _ChunkProgress * _ChunkAnimRiseHeight;
            clip(animClipY - IN.worldPos.y + 0.02);

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            o.Albedo = c.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = _Metallic;
            o.Alpha = c.a;

            // 基础地形材质无独立法线贴图，复用 uv_MainTex 采样，省一套插值器；
            // 贴图为空时默认为 "bump"(平面法线)，不影响光照。
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
        }

        // 未探索片元最终颜色 = 纯迷雾自发光，消除面片交界的光照接缝
        void fogFinal(Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            FogBlend_final(color, IN.fogCoord);
        }
        ENDCG

        // 【顶出方案-修订】手动 ShadowCaster pass：与 surf 同一 clip 平面 + 同一顶点动画，
        // 保证阴影几何与可见几何一致（addshadow 自动 pass 不执行 surf 的 clip，曾致"隐形地形黑块"）。
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            // 地形含竖面/过渡面，双面阴影与双面渲染一致。
            Cull Off

            CGPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_shadowcaster
            #pragma target 3.0

            #include "UnityCG.cginc"

            float _ChunkProgress;
            float _ChunkAnimBaseY;
            float _ChunkAnimRiseHeight;

            struct appdata_anim_shadow
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv2    : TEXCOORD1; // 动画通道：x=startVertexY、y=targetVertexY
                float2 uv3    : TEXCOORD2; // 动画通道：x=staggerDelay、y=participates
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_anim_shadow
            {
                V2F_SHADOW_CASTER;
                float worldPosY : TEXCOORD1;
            };

            v2f_anim_shadow shadowVert(appdata_anim_shadow v)
            {
                v2f_anim_shadow o;

                // 【CPU动画-2026-08-05】顶点已由 C# 逐帧写入 mesh.vertices（SetChunkAnimationProgress），
                // 此处不再做 UV 插值变形；worldPosY 用于与 surf 一致的 keep-below clip。
                o.worldPosY = mul(unity_ObjectToWorld, v.vertex).y;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 shadowFrag(v2f_anim_shadow i) : SV_Target
            {
                float animClipY = _ChunkAnimBaseY + _ChunkProgress * _ChunkAnimRiseHeight;
                clip(animClipY - i.worldPosY + 0.02);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    // 与稳定版一致：回退到 Standard 而非 Legacy Diffuse
    FallBack "Standard"
}
