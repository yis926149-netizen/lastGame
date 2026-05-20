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
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

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

        // 输入结构体：采样三种材质UV + 遮罩UV
        struct Input
        {
            float2 uv_MainTexA;
            float2 uv_MainTexB;
            float2 uv_MainTexC;
            float2 uv_MaskTex;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
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
            // 归一化：确保三种材质权重总和为1
            half totalWeight = weightA + weightB + weightC;
            totalWeight = max(totalWeight, 0.001); // 避免除以0
            weightA /= totalWeight;
            weightB /= totalWeight;
            weightC /= totalWeight;

            // 3. 采样三种材质属性（强制非金属+低光滑度）
            // 材质A
            fixed4 albedoA = tex2D(_MainTexA, IN.uv_MainTexA);
            fixed3 normalA = UnpackNormal(tex2D(_NormalMapA, IN.uv_MainTexA));
            half metallicA = 0.0; // 土地强制非金属
            half smoothnessA = _SmoothnessA;

            // 材质B
            fixed4 albedoB = tex2D(_MainTexB, IN.uv_MainTexB);
            fixed3 normalB = UnpackNormal(tex2D(_NormalMapB, IN.uv_MainTexB));
            half metallicB = 0.0; // 土地强制非金属
            half smoothnessB = _SmoothnessB;

            // 材质C（新增）
            fixed4 albedoC = tex2D(_MainTexC, IN.uv_MainTexC);
            fixed3 normalC = UnpackNormal(tex2D(_NormalMapC, IN.uv_MainTexC));
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
        ENDCG
    }
    FallBack "Standard"
}