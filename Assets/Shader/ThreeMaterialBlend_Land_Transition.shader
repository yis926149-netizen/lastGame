Shader "Custom/ThreeMaterialBlend_Land_Transition"
{
    //【动态地图-阶段四修订】ThreeMaterialBlend_Land 的动画专用变体（§十九-21/§20-10）。
    //【同步契约-2026-08-05 修订】顶点动画已改为 C# 侧逐帧写 mesh.vertices（vert 不读 UV2/UV3，
    //   禁止把顶点变形加回 vert，否则与 C# 路径双重位移）。与稳定版的全部差异：
    //     ① Properties 增加 _ChunkProgress / _ChunkAnimBaseY / _ChunkAnimRiseHeight；
    //     ② Input 增加 worldPos；③ surf 末尾 keep-below clip；④ 末尾手动 ShadowCaster pass。
    //   除上述四点外，混合逻辑主体与 fogFinal 必须与稳定版 ThreeMaterialBlend_Land 逐字保持一致。
    // - 仅在动画期间由 ChunkMapRenderer 按 Chunk 切换使用；稳定渲染永不加载本 Shader。
    // - mesh 上的动画数据通道（仅供 C# 侧 ChunkMapRenderer.SetChunkAnimationProgress 读取，§20-10）：
    //     UV2 (texcoord1): x=startVertexY、y=targetVertexY
    //     UV3 (texcoord2): x=staggerDelay [0,1]、y=participatesInTransition (1=参与, 0=不参与)
    // - 顶出方案-修订：surf 与末尾手动 ShadowCaster pass 执行同一 keep-below clip 平面
    //   （_ChunkAnimBaseY + _ChunkProgress*_ChunkAnimRiseHeight），阴影几何与可见几何一致（§13.2/§13.3）。
    Properties
    {
        // 材质 A（土地材质1：如草地）
        _MainTexA ("Albedo (草地纹理)", 2D) = "white" {}
        _NormalMapA ("Normal (草地法线)", 2D) = "bump" {}
        _MetallicA ("Metallic (强制0)", Range(0,1)) = 0.0
        _SmoothnessA ("Smoothness (0.15)", Range(0,1)) = 0.15
        
        // 材质 B（土地材质2：如泥土）
        _MainTexB ("Albedo (泥土纹理)", 2D) = "white" {}
        _NormalMapB ("Normal (泥土法线)", 2D) = "bump" {}
        _MetallicB ("Metallic (强制0)", Range(0,1)) = 0.0
        _SmoothnessB ("Smoothness (0.15)", Range(0,1)) = 0.15
        
        // 材质 C（土地材质3：如岩石）
        _MainTexC ("Albedo (岩石纹理)", 2D) = "white" {}
        _NormalMapC ("Normal (岩石法线)", 2D) = "bump" {}
        _MetallicC ("Metallic (强制0)", Range(0,1)) = 0.0
        _SmoothnessC ("Smoothness (0.15)", Range(0,1)) = 0.15
        
        // 混合控制：RGB三通道遮罩图（R=A权重，G=B权重，B=C权重）
        _MaskTex ("Blend Mask (RGB三通道遮罩)", 2D) = "white" {}
        _BlendSmooth ("Blend Smoothness (过渡柔和度)", Range(0.01, 0.5)) = 0.1
        _WorldTexScale ("World Texture Scale", Float) = 0.238095

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
        // 三角过渡含竖面，保持双面渲染。
        Cull Off

        CGPROGRAM
        // 注意：不写 addshadow——SubShader 末尾的手动 ShadowCaster pass 执行同一 clip 平面，
        // 保证阴影几何与可见几何一致（addshadow 自动 pass 不执行 surf 的 clip，曾致"隐形地形黑块"）。
        #pragma surface surf Standard fullforwardshadows vertex:vert finalcolor:fogFinal
        #pragma target 3.0

        #include "FogBlend.cginc"

        // 材质 A 变量声明
        sampler2D _MainTexA;
        sampler2D _NormalMapA;
        float4 _MainTexA_ST;
        half _MetallicA;
        half _SmoothnessA;

        // 材质 B 变量声明
        sampler2D _MainTexB;
        sampler2D _NormalMapB;
        float4 _MainTexB_ST;
        half _MetallicB;
        half _SmoothnessB;

        // 材质 C 变量声明（新增）
        sampler2D _MainTexC;
        sampler2D _NormalMapC;
        float4 _MainTexC_ST;
        half _MetallicC;
        half _SmoothnessC;

        // 混合控制变量
        sampler2D _MaskTex;
        half _BlendSmooth;
        float _WorldTexScale;

        float _ChunkProgress;
        float _ChunkAnimBaseY;
        float _ChunkAnimRiseHeight;

        // 输入结构体（与稳定版一致，动画数据只在 vert 内读取，不占插值器）
        struct Input
        {
            float2 uv_MaskTex;     // 重心坐标，仅用于三材质混合权重
            float2 fogCoord;       // 迷雾整图归一化 UV（不能叫 uv_FogTex，见 TerrainBase_Fog 注释）
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

            // 1. 采样RGB三通道遮罩，获取三种材质的基础权重
            fixed4 mask = tex2D(_MaskTex, IN.uv_MaskTex);
            half weightA = mask.r; // R通道 = 材质A权重
            half weightB = mask.g; // G通道 = 材质B权重
            half weightC = mask.b; // B通道 = 材质C权重

            // 2. 权重平滑与归一化（避免总和超过1导致颜色过亮）
            // 平滑权重：增强过渡柔和度
            weightA = smoothstep(0, 1, weightA);
            weightB = smoothstep(0, 1, weightB);
            weightC = smoothstep(0, 1, weightC);
            // 归一化：确保三种材质权重总和为1。
            // 关键兜底：当遮罩三通道在该 UV 处都≈0（例如竖面 UV 退化采到遮罩黑区）时，
            // 归一化后三个权重仍≈0，会让 o.Albedo 变纯黑 → 任何光照都点不亮的死黑。
            // 此时退回等权混合，保证 albedo 始终是有效的地表颜色而非黑。
            half totalWeight = weightA + weightB + weightC;
            if (totalWeight < 0.001)
            {
                weightA = 1.0 / 3.0;
                weightB = 1.0 / 3.0;
                weightC = 1.0 / 3.0;
            }
            else
            {
                weightA /= totalWeight;
                weightB /= totalWeight;
                weightC /= totalWeight;
            }

            // 3. 采样三种材质属性（强制非金属+低光滑度）。
            // 材质A
            float2 worldUV = IN.worldPos.xz * _WorldTexScale;
            float2 terrainUVA = worldUV * _MainTexA_ST.xy + _MainTexA_ST.zw;
            float2 terrainUVB = worldUV * _MainTexB_ST.xy + _MainTexB_ST.zw;
            float2 terrainUVC = worldUV * _MainTexC_ST.xy + _MainTexC_ST.zw;
            fixed4 albedoA = tex2D(_MainTexA, terrainUVA);
            fixed3 normalA = UnpackNormal(tex2D(_NormalMapA, terrainUVA));
            half metallicA = 0.0; // 土地强制非金属
            half smoothnessA = _SmoothnessA;

            // 材质B
            fixed4 albedoB = tex2D(_MainTexB, terrainUVB);
            fixed3 normalB = UnpackNormal(tex2D(_NormalMapB, terrainUVB));
            half metallicB = 0.0; // 土地强制非金属
            half smoothnessB = _SmoothnessB;

            // 材质C（新增）
            fixed4 albedoC = tex2D(_MainTexC, terrainUVC);
            fixed3 normalC = UnpackNormal(tex2D(_NormalMapC, terrainUVC));
            half metallicC = 0.0; // 土地强制非金属
            half smoothnessC = _SmoothnessC;

            // 4. 三种材质混合（核心逻辑）
            o.Albedo = weightA * albedoA.rgb + weightB * albedoB.rgb + weightC * albedoC.rgb;
            o.Normal = weightA * normalA + weightB * normalB + weightC * normalC;
            o.Metallic = weightA * metallicA + weightB * metallicB + weightC * metallicC; // 最终仍为0
            o.Smoothness = weightA * smoothnessA + weightB * smoothnessB + weightC * smoothnessC; // 混合后仍低光滑度

            // 5. 土地材质优化：无自发光+不透明
            o.Emission = 0;
            o.Alpha = 1.0; // 三角形Mesh无需透明，强制不透明
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
            // 三角过渡含竖面，双面阴影与双面渲染一致。
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
