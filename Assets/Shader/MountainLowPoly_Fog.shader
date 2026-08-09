Shader "Custom/MountainLowPoly_Fog"
{
    // 【程序化山脉-阶段 4.3~4.6】山体专属稳定 Shader（路线 A 山体槽，决策 ⑥）。
    // 稳定态完整契约：
    //  4.3 Triplanar 世界空间采样（决策 ㉗）：worldPos 三轴采样 _RockTexture，权重
    //     pow(|faceNormal|, _TriplanarBlendSharpness) 归一化（分母兜底防 NaN）；
    //     负轴取 abs() 防相邻面纹理方向突然反转；UV 纯世界坐标，与 Chunk transform/生成顺序无关，
    //     禁用局部坐标与 ridgeKey01 作平移量；无纹理（_ROCK_TEXTURE 关闭）= 纯色模式，零采样成本。
    //  4.4 离散 face tier（决策 ⑥/㉘）：UV0.y 经 round/saturate 解码 tier 0/1/2（与
    //     MountainMaterialContract.DecodeFaceTier 等价），同面同 tier（阶段 3 flat 拆分保证），
    //     禁止按 worldY 连续阈值重算色阶、无面内渐变；UV0.x=ridgeKey01 只做极轻逐面恒定亮度偏移
    //     （0.97~1.03），不改变三档顺序、无渐变；无雪顶（决策 ㉒），无第 4 档（正式资产不引入）。
    //  4.5 片元导数平面法线（决策 ㉖）：cross(ddy(worldPos),ddx(worldPos)) 经插值几何法线校准朝向
    //     （退化面兜底回退几何法线，禁止 NaN/黑闪）；用于 Triplanar 权重。
    //     【落地细化】Surface shader 光照的 o.Normal 是切空间，无法注入世界空间导数法线：
    //     光照使用插值顶点法线（几何已 flat 拆分 + RecalculateNormals ⇒ 逐面平面法线，
    //     与导数法线同向等值；默认 FlatAll 风格不执行 MergeChunkBoundaryNormals，无接缝）。
    //     Cull Off 背面片元得到同一朝外法线，不发黑、不反向，无需 VFACE 翻转。
    //     normal map 首版禁用（o.Normal = 切空间恒等）。
    //  4.6 阴影与雾化：手动 ShadowCaster pass（Cull Off、无 clip、与 surf 同顶点位置，稳定态
    //     无需阶段 5 的 keep-below clip；双面投射）；FogBlend_vert/fogFinal 与 TerrainBase_Fog
    //     同一 FogBlend.cginc 世界 XZ 掩码契约；山体只走 Terrain FogBlend，禁止注册
    //     FogAffectedEnvironment 选择性对象雾化（防双重雾化，决策 ⑪）；高度感知雾 v1 不启用
    //     （预留关闭，不改变现有雾边界）；_ShadowStrength 为预留参数（Standard 光照模型
    //     不支持逐面阴影强度，需要时引入自定义光照函数）。
    //  Transition 变体（keep-below clip / CPU 顶点动画 UV2/UV3）属阶段 5，本 Shader 不消费
    //  _ChunkProgress 等 Renderer 级 MPB 属性，但必须容忍同一 Renderer 上的属性块（多余属性忽略）。
    //
    // UV0 数据契约（权威定义见 MountainMaterialContract）：
    //  UV0.x = ridgeKey01 ∈ [0,1)（禁止作为纹理平移量）；UV0.y = faceTier 编码 (tier+0.5)/3。
    // 本 Shader 只把 UV0 当数据通道读取，不做任何"贴图展开"式重解释。
    Properties
    {
        _ColorLow ("Tier 0 - Rock Brown", Color) = (0.42, 0.34, 0.28, 1)
        _ColorMid ("Tier 1 - Gray Rock", Color) = (0.56, 0.54, 0.50, 1)
        _ColorHigh ("Tier 2 - Light Gray", Color) = (0.72, 0.71, 0.68, 1)
        [Toggle(_ROCK_TEXTURE)] _RockTextureEnabled ("Rock Texture (Triplanar)", Float) = 0
        _RockTexture ("Rock Texture (Optional, tinted by tier)", 2D) = "white" {}
        _TriplanarWorldScale ("Triplanar World Scale", Float) = 1.0
        _TriplanarBlendSharpness ("Triplanar Blend Sharpness", Float) = 4.0
        _Roughness ("Roughness", Range(0,1)) = 0.9
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _ShadowStrength ("Shadow Strength (reserved)", Range(0,1)) = 1.0
        _TerrainTex ("Boundary Terrain Albedo", 2D) = "white" {}
        _TerrainNormal ("Boundary Terrain Normal", 2D) = "bump" {}
        _TerrainColor ("Boundary Terrain Color", Color) = (1,1,1,1)
        _TerrainSmoothness ("Boundary Terrain Smoothness", Range(0,1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        // 山体含背面/近竖直面，与地形一致保持双面渲染。
        Cull Off

        CGPROGRAM
        // 不写 addshadow：末尾手动 ShadowCaster pass 与 surf 同顶点位置（稳定态无 clip）；
        // 与 TerrainBase_Fog_Transition 的先例一致，避免重复生成 ShadowCaster。
        #pragma surface surf Standard fullforwardshadows vertex:vert finalcolor:fogFinal
        // _ROCK_TEXTURE 由 ChunkMapRenderer 按 MountainConfig.rockTexture 开关；
        // 没有 shader_feature 时，surf 中的 #if defined(_ROCK_TEXTURE) 分支不会生成可用变体。
        #pragma shader_feature _ROCK_TEXTURE
        #pragma shader_feature _MOUNTAIN_TERRAIN_BLEND
        #pragma target 3.0

        #include "FogBlend.cginc"

        struct Input
        {
            float2 tierUV;       // 山体槽 UV0 数据通道：x=ridgeKey01、y=faceTier 编码（契约见 MountainMaterialContract）
            float3 worldPos;     // Triplanar 采样 + 片元导数平面法线
            float3 worldNormal;  // 仅用于叉乘法线朝向校准与退化面兜底（不直接参与光照/权重）
            // Surface Shader 同时读取 worldNormal 且写入 o.Normal 时，Unity 生成的
            // WorldNormalVector 需要这些内部切线空间数据；缺失会在 _ROCK_TEXTURE 变体报错。
            INTERNAL_DATA
            float3 terrainBlend; // xy=terrain UV，z=岩石权重（仅山脚融合槽使用）
            float2 fogCoord;     // 迷雾整图归一化 UV（vert 里由世界 XZ 计算；命名避开 uv_<纹理名> 前缀）
        };

        sampler2D _RockTexture;
        fixed4 _ColorLow;
        fixed4 _ColorMid;
        fixed4 _ColorHigh;
        float _TriplanarWorldScale;
        float _TriplanarBlendSharpness;
        half _Roughness;
        half _Metallic;
        half _ShadowStrength;
        sampler2D _TerrainTex;
        float4 _TerrainTex_ST;
        sampler2D _TerrainNormal;
        fixed4 _TerrainColor;
        half _TerrainSmoothness;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            // 山体槽 UV0 是编码数据通道，不是贴图 UV：直接透传（v.texcoord = UV0）。
            o.tierUV = v.texcoord.xy;
            o.terrainBlend = float3(TRANSFORM_TEX(v.texcoord3.xy, _TerrainTex), v.texcoord3.z);
            FogBlend_vert(v.vertex, o.fogCoord);
        }

        // 4.5：片元导数平面法线。叉乘方向由插值几何法线校准（Cull Off 背面天然得到朝外法线）；
        // 退化面（cross≈0）兜底回退几何法线，禁止 NaN/黑闪。
        float3 ComputeFaceNormal(float3 worldPos, float3 worldNormal)
        {
            float3 geoNormal = normalize(worldNormal);
            float3 deriv = cross(ddy(worldPos), ddx(worldPos));
            if (dot(deriv, geoNormal) < 0.0)
                deriv = -deriv;
            return dot(deriv, deriv) < 1e-8 ? geoNormal : normalize(deriv);
        }

        // 4.3：世界空间三轴 Triplanar 采样（决策 ㉗）。UV 用世界坐标，与 Chunk transform/生成顺序无关；
        // 负轴 abs() 防镜像轴与正轴方向反转；world scale 防 0/负值（配置 IsValid 已保证，双保险）。
        fixed3 SampleRockTriplanar(float3 worldPos, float3 faceNormal)
        {
            float scale = max(_TriplanarWorldScale, 1e-4);
            float3 w = pow(saturate(abs(faceNormal)), saturate(max(_TriplanarBlendSharpness, 1.0)));
            float total = w.x + w.y + w.z;
            w = total > 1e-6 ? w / total : float3(1.0, 0.0, 0.0); // 分母兜底，禁止 NaN

            fixed3 col = 0.0;
            col += tex2D(_RockTexture, abs(worldPos.zy) * scale) * w.x; // +X/-X 轴：YZ 面
            col += tex2D(_RockTexture, abs(worldPos.xz) * scale) * w.y; // +Y/-Y 轴：XZ 面
            col += tex2D(_RockTexture, abs(worldPos.xy) * scale) * w.z; // +Z/-Z 轴：XY 面
            return col;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // 4.4：UV0.y 解码 tier（round/saturate 等价 MountainMaterialContract.DecodeFaceTier）；
            // 同面三顶点同 tier（阶段 3 flat 拆分写齐 UV0），无面内渐变；禁止按 worldY 重算色阶。
            float tier = saturate(round(IN.tierUV.y * 3.0 - 0.5)); // 0/1/2
            fixed3 tierColor = lerp(lerp(_ColorLow, _ColorMid, saturate(tier)), _ColorHigh, saturate(tier - 1.0));
            // UV0.x = ridgeKey01：极轻、离散（逐面恒定）亮度偏移，不改变三档顺序、无渐变（决策 ㉘/4.4）
            fixed3 albedo = tierColor * (0.97 + 0.06 * IN.tierUV.x);

            #if defined(_ROCK_TEXTURE)
            // 4.3：Triplanar 采样 × 色阶染色；权重使用片元平面法线（与光照同向，见 4.5 落地细化）
            albedo *= SampleRockTriplanar(IN.worldPos, ComputeFaceNormal(IN.worldPos, IN.worldNormal));
            #endif

            #if defined(_MOUNTAIN_TERRAIN_BLEND)
            fixed4 terrain = tex2D(_TerrainTex, IN.terrainBlend.xy) * _TerrainColor;
            float mountainWeight = smoothstep(0.0, 1.0, saturate(IN.terrainBlend.z));
            albedo = lerp(terrain.rgb, albedo, mountainWeight);
            #endif

            o.Albedo = albedo;
            #if defined(_MOUNTAIN_TERRAIN_BLEND)
            o.Smoothness = lerp(_TerrainSmoothness, saturate(1.0 - _Roughness),
                smoothstep(0.0, 1.0, saturate(IN.terrainBlend.z)));
            #else
            o.Smoothness = saturate(1.0 - _Roughness);
            #endif
            o.Metallic = _Metallic;
            o.Alpha = 1.0;
            // 4.5：normal map 首版禁用 —— 切空间恒等，光照使用插值顶点法线（逐面平面法线）
            o.Normal = float3(0.0, 0.0, 1.0);
        }

        // 与 TerrainBase_Fog 同一 FogBlend 契约：未探索区去饱和 + 滚动雾；山体只被这条雾化路径处理。
        void fogFinal(Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            FogBlend_final(color, IN.fogCoord);
        }
        ENDCG

        // 4.6：手动 ShadowCaster（与 surf 同顶点位置；Cull Off 双面；稳定态无 clip，
        // 阶段 5 的 Transition 变体才需要 keep-below clip）。
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            // 双面阴影与双面渲染一致（同 TerrainBase_Fog_Transition 先例）。
            Cull Off

            CGPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_shadowcaster
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata_shadow
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_shadow
            {
                V2F_SHADOW_CASTER;
            };

            v2f_shadow shadowVert(appdata_shadow v)
            {
                v2f_shadow o;
                UNITY_SETUP_INSTANCE_ID(v);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 shadowFrag(v2f_shadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    // 与地形一致：回退 Standard 而非 Legacy Diffuse
    FallBack "Standard"
}
