Shader "Custom/RealMaterialMaskBlend_Transition"
{
    //【动态地图-阶段四修订】RealMaterialMaskBlend 的动画专用变体（§十九-21/§20-10）。
    //【同步契约-2026-08-05 修订】顶点动画已改为 C# 侧逐帧写 mesh.vertices（vert 不读 UV2/UV3，
    //   禁止把顶点变形加回 vert，否则与 C# 路径双重位移）。与稳定版的全部差异：
    //     ① Properties 增加 _ChunkProgress / _ChunkAnimBaseY / _ChunkAnimRiseHeight；
    //     ② Input 增加 worldPos；③ surf 末尾 keep-below clip；④ 末尾手动 ShadowCaster pass。
    //   除上述四点外，混合逻辑主体与 fogFinal 必须与稳定版 RealMaterialMaskBlend 逐字保持一致。
    // - 仅在动画期间由 ChunkMapRenderer 按 Chunk 切换使用；稳定渲染永不加载本 Shader。
    // - mesh 上的动画数据通道（仅供 C# 侧 ChunkMapRenderer.SetChunkAnimationProgress 读取，§20-10）：
    //     UV2 (texcoord1): x=startVertexY、y=targetVertexY
    //     UV3 (texcoord2): x=staggerDelay [0,1]、y=participatesInTransition (1=参与, 0=不参与)
    // - 顶出方案-修订：surf 与末尾手动 ShadowCaster pass 执行同一 keep-below clip 平面
    //   （_ChunkAnimBaseY + _ChunkProgress*_ChunkAnimRiseHeight），阴影几何与可见几何一致（§13.2/§13.3）。
    Properties
    {
        // 材质 A（土地材质1：如草地、泥土）
        _MainTexA ("Albedo (土地纹理A)", 2D) = "white" {}
        _NormalMapA ("Normal (土地法线A)", 2D) = "bump" {}
        _MetallicA ("Metallic (金属度，土地强制0)", Range(0,1)) = 0.0
        _SmoothnessA ("Smoothness (光滑度，土地0.15)", Range(0,1)) = 0.15
        
        // 材质 B（土地材质2：如泥土、岩石）
        _MainTexB ("Albedo (土地纹理B)", 2D) = "white" {}
        _NormalMapB ("Normal (土地法线B)", 2D) = "bump" {}
        _MetallicB ("Metallic (金属度，土地强制0)", Range(0,1)) = 0.0
        _SmoothnessB ("Smoothness (光滑度，土地0.15)", Range(0,1)) = 0.15
        
        // 混合控制
        _MaskTex ("Blend Mask (混合遮罩)", 2D) = "white" {}
        _BlendSmooth ("Blend Width (过渡宽度)", Range(0.05, 1.0)) = 0.3
        _BlendContrast ("Blend Contrast (混合对比度)", Range(1.0, 5.0)) = 2.0

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
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 200
        // 矩形过渡含竖面/踢面，保持双面渲染。
        Cull Off

        CGPROGRAM
        // 注意：不写 addshadow——SubShader 末尾的手动 ShadowCaster pass 执行同一 clip 平面，
        // 保证阴影几何与可见几何一致（addshadow 自动 pass 不执行 surf 的 clip，曾致"隐形地形黑块"）。
        #pragma surface surf Standard fullforwardshadows vertex:vert finalcolor:fogFinal
        #pragma target 3.0

        #include "FogBlend.cginc"

        // 材质 A 纹理与属性
        sampler2D _MainTexA;
        sampler2D _NormalMapA;
        half _MetallicA;
        half _SmoothnessA;

        // 材质 B 纹理与属性
        sampler2D _MainTexB;
        sampler2D _NormalMapB;
        half _MetallicB;
        half _SmoothnessB;

        // 混合控制属性
        sampler2D _MaskTex;
        half _BlendSmooth;
        half _BlendContrast;

        float _ChunkProgress;
        float _ChunkAnimBaseY;
        float _ChunkAnimRiseHeight;

        struct Input
        {
            float2 uv_MainTexA;
            float2 uv_MainTexB;
            float2 uv_MaskTex;
            float2 fogCoord;   // 迷雾整图归一化 UV（不能叫 uv_FogTex，见 TerrainBase_Fog 注释）
            float3 worldPos;       // 【顶出方案】片元世界坐标（clip 平面用，vert 变形后自动计算）
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            // 【CPU动画-2026-08-05】顶点动画改为 C# 侧逐帧写 mesh.vertices（同 TerrainBase_Fog_Transition）。
            // 顶点变形后计算迷雾世界坐标，保证迷雾跟随动画高度（§13.3）
            FogBlend_vert(v.vertex, o.fogCoord);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // 【顶出方案-修订】clip 平面（keep-below）：丢弃高于当前动画高度线的片元。
            // 动画起点（progress=0）新平台整体隐藏，旧地形快照（TerrainGhost）完整可见；
            // 随进度升高，新平台从旧地表下逐层"顶出"，消除"先变平再升起"的拓扑突变观感。
            float animClipY = _ChunkAnimBaseY + _ChunkProgress * _ChunkAnimRiseHeight;
            clip(animClipY - IN.worldPos.y + 0.02);

            // 1. 计算混合权重（保留对比度增强逻辑）
            half mask = tex2D(_MaskTex, IN.uv_MaskTex).r;
            mask = pow(mask, _BlendContrast);
            half blendMin = 0.5 - _BlendSmooth * 0.5;
            half blendMax = 0.5 + _BlendSmooth * 0.5;
            half blendWeight = smoothstep(blendMin, blendMax, mask);

            // 2. 采样材质 A（强制非金属+低光滑度）
            fixed4 albedoA = tex2D(_MainTexA, IN.uv_MainTexA);
            fixed3 normalA = UnpackNormal(tex2D(_NormalMapA, IN.uv_MainTexA));
            half metallicA = 0.0; // 土地强制非金属，覆盖外部参数
            half smoothnessA = _SmoothnessA;

            // 3. 采样材质 B（同上，强制非金属）
            fixed4 albedoB = tex2D(_MainTexB, IN.uv_MainTexB);
            fixed3 normalB = UnpackNormal(tex2D(_NormalMapB, IN.uv_MainTexB));
            half metallicB = 0.0; // 土地强制非金属，覆盖外部参数
            half smoothnessB = _SmoothnessB;

            // 4. 混合核心属性（消除塑料反光的关键）
            o.Albedo = lerp(albedoB.rgb, albedoA.rgb, blendWeight);
            o.Normal = lerp(normalB, normalA, blendWeight);
            o.Metallic = lerp(metallicB, metallicA, blendWeight); // 最终仍为0（非金属）
            o.Smoothness = lerp(smoothnessB, smoothnessA, blendWeight); // 低光滑度=粗糙表面

            // 5. 弱化环境反射（土地不需要强环境反射，此处直接移除复杂逻辑）
            o.Emission = 0; // 无自发光
            o.Alpha = lerp(albedoB.a, albedoA.a, blendWeight);
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
            // 矩形过渡含竖面/踢面，双面阴影与双面渲染一致。
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

                // 【CPU动画-2026-08-05】顶点已由 C# 逐帧写入 mesh.vertices，此处不再变形。
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
    FallBack "Standard"
}
