Shader "Custom/FogCover"
{
    // 恒迷雾 Shader：用于地图边缘迷雾连接面片与封皮。
    // 保留封皮材质原有的 MainTex、Color 和网格 UV 映射，同时参与深度测试。
    // 纯自发光观感：不受光照、不接受阴影、不投射阴影（无 ShadowCaster Pass）。
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" "IgnoreProjector"="True" }
        LOD 100

        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv) * _Color;
                return fixed4(color.rgb, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
