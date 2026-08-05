Shader "Custom/TerrainBase_Fog"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _BumpMap ("Normal Map", 2D) = "bump" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        // 地形含竖面/过渡面，保持双面渲染。
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow finalcolor:fogFinal
        #pragma target 3.0

        #include "FogBlend.cginc"

        // 注意：Input 只保留必需的插值量。
        // 原先额外声明的 uv_BumpMap / uv_OcclusionMap / uv_EmissionMap（基础地形材质里
        // 这些贴图全为空）会占用多套 TEXCOORD 插值器，叠加 Standard + fullforwardshadows
        // 后在 target 3.0 下超出插值器上限，导致前向 Pass 编译失败、回退到 FallBack "Diffuse"。
        // 而 Legacy Diffuse 会把 _MainTex 直接乘以顶点色 → 已探索(顶点色白)=贴图、
        // 未探索(顶点色黑)=纯黑，且 surf/迷雾混合根本不执行。精简到 3 个插值量后可正常编译。
        sampler2D _MainTex;
        sampler2D _BumpMap;

        struct Input
        {
            float2 uv_MainTex;
            float2 fogCoord;       // 迷雾整图归一化 UV（vert 里由世界 XZ 计算）。
                                   // 注意：不能叫 uv_FogTex —— surface shader 会把 uv_<纹理名> 字段
                                   // 自动填成该纹理的 mesh UV，覆盖 vert 赋的世界 UV，导致每面片各铺一张。
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            FogBlend_vert(v.vertex, o.fogCoord);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
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
    }
    // 回退到 Standard 而非 Legacy Diffuse：万一未来仍编译失败，也只是丢失迷雾、
    // 显示正常地形，绝不会因顶点色相乘而把未探索地块渲染成纯黑。
    FallBack "Standard"
}
