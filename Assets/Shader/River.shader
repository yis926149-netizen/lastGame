Shader "Custom/River" {
    Properties {
        _MainTex ("Noise Texture", 2D) = "white" {} 
        _Color ("Water Color (含透明度)", Color) = (0, 0.5, 1, 0.85) 
        _Metallic ("Metallic", Range(0,1)) = 0 
        _Glossiness ("Smoothness", Range(0,1)) = 0.8 
        _Speed ("水流速度 (Y轴)", Range(0.01, 2.0)) = 0.2 
        _XSpeed ("细节变化速度 (X轴)", Range(0.001, 2.0)) = 0.005 
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
        // 关键3：移除可能的阴影编译指令（仅保留alpha和基础光照）
        #pragma surface surf Standard alpha noforwardadd nolightmap nodirlightmap
        // noforwardadd：禁用附加光照（减少计算，避免阴影残留）
        // nolightmap/nodirlightmap：禁用光照贴图（透明物体无需光照贴图）
        #pragma target 3.0

        // 声明变量（不变）
        sampler2D _MainTex;
        float4 _Color;
        float _Metallic;
        float _Glossiness;
        float _Speed;
        float _XSpeed;

        struct Input {
            float2 uv_MainTex;
        };

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
            finalColor.a = _Color.a;  // 覆盖Alpha：噪声只影响颜色，不影响透明度
            // 当_Color.Alpha=0时，finalColor.a=0，实现完全透明

            // 表面属性赋值（不变）
            o.Albedo = finalColor.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = finalColor.a;
        }
        ENDCG
    }
    // 降级设置：禁用FallBack的阴影（关键！原Diffuse会带阴影，需替换）
    FallBack "Unlit/Transparent"  // 改用无光照透明Shader，避免降级后产生阴影
}