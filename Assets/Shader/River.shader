Shader "Custom/River" {
    Properties {
        _MainTex ("Noise Texture", 2D) = "white" {} 
        _Color ("Water Color (含透明度)", Color) = (0, 0.5, 1, 0.85) 
        _Metallic ("Metallic", Range(0,1)) = 0 
        _Glossiness ("Smoothness", Range(0,1)) = 0.8 
        _Speed ("水流速度 (Y轴)", Range(0.01, 2.0)) = 0.2 
        _XSpeed ("细节变化速度 (X轴)", Range(0.001, 2.0)) = 0.005 
        // 【阶段四】河流淡出（MaterialPropertyBlock 提供，§13.4 河流消失）
        _FadeAlpha ("Fade Alpha", Float) = 1.0
    }
    SubShader {
        // 关键1：添加光照模式标签，避免阴影计算干扰；保留透明队列
        Tags { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "LightMode"="ForwardBase"  // 明确光照模式，禁用多余阴影计算
        }
        LOD 200

        // 透明核心配置：混合规则+关闭深度写入（不变）
        Blend SrcAlpha OneMinusSrcAlpha  
        ZWrite Off  

        // 关键2：禁用阴影投射（Shader层面彻底关闭阴影生成）
        Cull Off  // 可选：若水体无背面，关闭剔除不影响；若有则保留Cull Back
        //Shadows Off  // 核心：Shader层面禁用阴影投射

        CGPROGRAM
        #pragma surface surf Standard alpha noforwardadd nolightmap nodirlightmap vertex:vert finalcolor:fogFinal
        #pragma target 3.0

        #include "FogBlend.cginc"

        sampler2D _MainTex;
        float4 _Color;
        float _Metallic;
        float _Glossiness;
        float _Speed;
        float _XSpeed;
        // 【阶段四】河流淡出（MaterialPropertyBlock 提供，§13.4）
        float _FadeAlpha;

        struct Input {
            float2 uv_MainTex;
            float2 fogCoord;
        };

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            FogBlend_vert(v.vertex, o.fogCoord);
        }

        void surf (Input IN, inout SurfaceOutputStandard o) {
            // UV动画（不变）
            float2 uv = IN.uv_MainTex;
            uv.x *= 0.0625;

            float2 uv1 = uv;
            uv1.x += _Time.y * _XSpeed;
            uv1.y -= _Time.y * _Speed;
            float4 noise1 = tex2D(_MainTex, uv1);

            float2 uv2 = uv;
            uv2.x -= _Time.y * (_XSpeed + 0.0002);
            uv2.y -= _Time.y * (_Speed - 0.02);
            float4 noise2 = tex2D(_MainTex, uv2);

            // 关键4：强制最终Alpha仅由_Color.Alpha控制，排除噪声干扰
            fixed4 finalColor = saturate(_Color + (noise1.r * noise2.a));
            // 覆盖Alpha：噪声只影响颜色，不影响透明度；【阶段四】乘 _FadeAlpha 实现河流淡出（§13.4）
            finalColor.a = _Color.a * _FadeAlpha;
            // 当_Color.Alpha=0时，finalColor.a=0，实现完全透明

            // 表面属性赋值
            o.Albedo = finalColor.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = finalColor.a;
        }

        void fogFinal(Input IN, SurfaceOutputStandard o, inout fixed4 color) {
            FogBlend_final(color, IN.fogCoord);
        }
        ENDCG
    }
    // 降级设置：禁用FallBack的阴影（关键！原Diffuse会带阴影，需替换）
    FallBack "Unlit/Transparent"  // 改用无光照透明Shader，避免降级后产生阴影
}