Shader "Custom/RealMaterialMaskBlend"
{
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
        _WorldTexScale ("World Texture Scale", Float) = 0.238095
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 200
        // 矩形过渡含竖面/踢面，保持双面渲染。
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow finalcolor:fogFinal
        #pragma target 3.0

        #include "FogBlend.cginc"

        // 材质 A 纹理与属性
        sampler2D _MainTexA;
        sampler2D _NormalMapA;
        float4 _MainTexA_ST;
        half _MetallicA;
        half _SmoothnessA;

        // 材质 B 纹理与属性
        sampler2D _MainTexB;
        sampler2D _NormalMapB;
        float4 _MainTexB_ST;
        half _MetallicB;
        half _SmoothnessB;

        // 混合控制属性
        sampler2D _MaskTex;
        half _BlendSmooth;
        half _BlendContrast;
        float _WorldTexScale;

        struct Input
        {
            float2 uv_MaskTex;
            float2 fogCoord;   // 迷雾整图归一化 UV（不能叫 uv_FogTex，见 TerrainBase_Fog 注释）
            float3 worldPos;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            FogBlend_vert(v.vertex, o.fogCoord);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // 1. 计算混合权重（保留对比度增强逻辑）
            half mask = tex2D(_MaskTex, IN.uv_MaskTex).r;
            mask = pow(mask, _BlendContrast);
            half blendMin = 0.5 - _BlendSmooth * 0.5;
            half blendMax = 0.5 + _BlendSmooth * 0.5;
            half blendWeight = smoothstep(blendMin, blendMax, mask);

            // 2. 采样材质 A（强制非金属+低光滑度）
            float2 worldUV = IN.worldPos.xz * _WorldTexScale;
            float2 terrainUVA = worldUV * _MainTexA_ST.xy + _MainTexA_ST.zw;
            float2 terrainUVB = worldUV * _MainTexB_ST.xy + _MainTexB_ST.zw;
            fixed4 albedoA = tex2D(_MainTexA, terrainUVA);
            fixed3 normalA = UnpackNormal(tex2D(_NormalMapA, terrainUVA));
            half metallicA = 0.0; // 土地强制非金属，覆盖外部参数
            half smoothnessA = _SmoothnessA;

            // 3. 采样材质 B（同上，强制非金属）
            fixed4 albedoB = tex2D(_MainTexB, terrainUVB);
            fixed3 normalB = UnpackNormal(tex2D(_NormalMapB, terrainUVB));
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
    }
    FallBack "Standard"
}
