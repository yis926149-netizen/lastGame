Shader "Custom/VertexColorShader"
{
    Properties
    {
    // 1. Debug Mode：默认设为0（对应Normal Light，图中数值）
    [Enum(Normal Light,0,Only Vertex Color,1,Normal Visualization,2)] _DebugMode ("Debug Mode", Float) = 0 
    // 2. 环境光强度：默认设为0.602（图中数值，原默认0.3）
    _AmbientIntensity ("环境光强度", Range(0.1, 1.0)) = 0.75 
    // 3. Shadow Color：保持默认(0.1,0.1,0.1,1)，无需修改
    _ShadowColor ("Shadow Color", Color) = (0.3,0.3,0.3,1)
    }
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "Queue"="Geometry" 
            "LightMode"="ForwardBase"
        }
        LOD 100
        ZWrite On

        // ---------------------- 1. 主光照通道（原有逻辑，新增阴影接收） ----------------------
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 新增：启用阴影接收（告诉Unity采样阴影贴图）
            #pragma multi_compile_fwdbase_shadows
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            // 新增：阴影贴图采样工具库
            #include "AutoLight.cginc"

            uniform float _DebugMode;
            uniform float _AmbientIntensity;
            uniform float4 _ShadowColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float3 normal : NORMAL;
                // 新增：接收顶点的世界坐标（用于阴影计算）
                float4 worldPos : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                // 新增：阴影坐标（用于采样阴影贴图）
                SHADOW_COORDS(2) // 占用TEXCOORD2通道
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                // 新增：计算阴影坐标（Unity自动处理阴影贴图采样准备）
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 调试模式逻辑（原有）
                if (_DebugMode == 1) return i.color;
                if (_DebugMode == 2)
                {
                    float3 normal = normalize(i.worldNormal);
                    return fixed4(normal * 0.5 + 0.5, 1.0);
                }

                // 原有光照计算
                float3 worldNormal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float diffuse = max(dot(worldNormal, lightDir), 0.0);
                fixed3 diffuseColor = _LightColor0.rgb * (diffuse + _AmbientIntensity);

                // 新增：采样阴影贴图，获取阴影强度（0=完全阴影，1=无阴影）
                fixed shadow = SHADOW_ATTENUATION(i);
                // 混合阴影颜色（阴影部分 = 阴影色，非阴影部分 = 漫反射色）
                fixed3 finalColor = lerp(_ShadowColor.rgb, diffuseColor, shadow) * i.color.rgb;

                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }

        // ---------------------- 2. 新增：阴影投射通道（关键！让物体能投射阴影） ----------------------
        Pass
        {
            Tags { "LightMode"="ShadowCaster" } // 声明为阴影投射通道

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 启用阴影投射编译宏
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER; // Unity定义的阴影投射所需数据
            };

            v2f vert (appdata v)
            {
                v2f o;
                // 计算阴影投射所需的裁剪空间位置和深度
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 阴影投射通道无需复杂颜色计算，仅需返回深度值（Unity自动处理）
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    // 降级策略：确保低版本Unity或Shader编译失败时，仍能使用内置阴影逻辑
    FallBack "Diffuse"
}