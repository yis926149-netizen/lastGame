Shader "Custom/FogConnector"
{
    // 迷雾连接面片（地图边缘斜坡 + MinY 平面填充）专用 Shader。
    // 固定执行"未探索"视觉：基础贴图去饱和 + 冷色调 + 叠加滚动迷雾。
    // 不采样探索遮罩、不接入探索状态，因此 Connector 永远保持迷雾外观。
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
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "FogBlend.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 baseColor = tex2D(_MainTex, i.uv).rgb * _Color.rgb;

                // 光照：漫反射 + 环境光
                float3 worldNormal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = max(0.0, dot(worldNormal, lightDir));
                fixed3 ambient = ShadeSH9(half4(worldNormal, 1.0));
                fixed3 litColor = baseColor * (ambient + _LightColor0.rgb * NdotL);

                float2 fogUV = (i.worldPos.xz - _FogMapOrigin.xy) / _FogMapSize.xy;
                float2 uvW = FogBlend_warpUV(fogUV);
                fixed3 finalColor = FogBlend_applyUnexplored(litColor, uvW);
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
