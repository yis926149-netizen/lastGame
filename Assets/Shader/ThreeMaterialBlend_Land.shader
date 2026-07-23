Shader "Custom/ThreeMaterialBlend_Land"
{
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
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow finalcolor:fogFinal
        #pragma target 3.0

        #include "FogBlend.cginc"

        // 材质 A 变量声明
        sampler2D _MainTexA;
        sampler2D _NormalMapA;
        half _MetallicA;
        half _SmoothnessA;

        // 材质 B 变量声明
        sampler2D _MainTexB;
        sampler2D _NormalMapB;
        half _MetallicB;
        half _SmoothnessB;

        // 材质 C 变量声明（新增）
        sampler2D _MainTexC;
        sampler2D _NormalMapC;
        half _MetallicC;
        half _SmoothnessC;

        // 混合控制变量
        sampler2D _MaskTex;
        half _BlendSmooth;

        // 输入结构体。
        // 注意：只保留 2 套 UV 插值量。_MainTexA/B/C 与 _MaskTex 都由运行时混合材质用
        // SetTexture 绑定（tiling/offset 均为默认 1,0），采样的都是同一套 mesh.uv0（三角
        // 过渡的重心坐标 UV），因此可共用 uv_MainTexA 一套坐标。
        // 之前声明 uv_MainTexA/B/C + uv_MaskTex + uv_FogTex 共 5 套 UV，叠加 Standard +
        // fullforwardshadows 在 target 3.0 下超出插值器上限 → 前向 Pass 编译失败 → 回退到
        // legacy fallback，后者把（未绑定的）主纹理直接乘以顶点色：已探索(顶点色白)=白、
        // 未探索(顶点色黑)=黑，且三材质混合与迷雾混合根本不执行——这正是三角过渡黑白的成因。
        struct Input
        {
            float2 uv_MainTexA;    // 三材质 + 遮罩共用这一套 UV
            float2 fogCoord;       // 迷雾整图归一化 UV（不能叫 uv_FogTex，见 TerrainBase_Fog 注释）
            float  vertexColor_R;  // 顶点色 .r = 探索状态(0/1)
            float  vertexColor_G;  // 顶点色 .g = 当前视野(0/1)，记忆区压暗用
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertexColor_R = v.color.r;
            o.vertexColor_G = v.color.g;
            FogBlend_vert(v.vertex, o.fogCoord);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // 未探索地块不投射阴影（仅在 shadow caster pass 中 clip）
            FogBlend_shadowClip(IN.vertexColor_R);

            // 1. 采样RGB三通道遮罩，获取三种材质的基础权重
            fixed4 mask = tex2D(_MaskTex, IN.uv_MainTexA);
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

            // 3. 采样三种材质属性（强制非金属+低光滑度）。三套贴图共用 uv_MainTexA。
            // 材质A
            fixed4 albedoA = tex2D(_MainTexA, IN.uv_MainTexA);
            fixed3 normalA = UnpackNormal(tex2D(_NormalMapA, IN.uv_MainTexA));
            half metallicA = 0.0; // 土地强制非金属
            half smoothnessA = _SmoothnessA;

            // 材质B
            fixed4 albedoB = tex2D(_MainTexB, IN.uv_MainTexA);
            fixed3 normalB = UnpackNormal(tex2D(_NormalMapB, IN.uv_MainTexA));
            half metallicB = 0.0; // 土地强制非金属
            half smoothnessB = _SmoothnessB;

            // 材质C（新增）
            fixed4 albedoC = tex2D(_MainTexC, IN.uv_MainTexA);
            fixed3 normalC = UnpackNormal(tex2D(_NormalMapC, IN.uv_MainTexA));
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

            // 6. 迷雾混合
            o.Albedo = FogBlend_surf(IN.fogCoord, IN.vertexColor_R, o.Albedo, o.Emission);
            o.Smoothness = FogBlend_alpha(IN.vertexColor_R, o.Smoothness);
            o.Metallic = FogBlend_alpha(IN.vertexColor_R, o.Metallic);
        }

        // 未探索片元最终颜色 = 纯迷雾自发光，消除面片交界的光照接缝
        void fogFinal(Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            FogBlend_final(IN.vertexColor_R, IN.vertexColor_G, o.Emission, color, IN.fogCoord);
        }
        ENDCG
    }
    FallBack "Standard"
}