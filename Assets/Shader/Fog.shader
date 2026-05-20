Shader "Custom/Fog_NoReflection_NoWrinkle"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1) 
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { 
            "RenderType"="Transparent" 
            "Queue"="Transparent+200" 
            "IgnoreProjector"="True" }
        LOD 100 
        
        ZWrite Off
        ZTest Always 
        Blend SrcAlpha OneMinusSrcAlpha 

        CGPROGRAM
        // 注意：pragma 必须写在同一行且不能有额外标签
        #pragma surface surf Lambert noforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };

        fixed4 _Color;

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Emission = c.rgb; // 使用自发光确保不受光照影响
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Unlit/Transparent"
}