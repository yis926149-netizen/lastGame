Shader "Hidden/FogEnvironmentObjectMask"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _FogMapOrigin;
            float4 _FogMapSize;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
                float2 fogUV : TEXCOORD2;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.screenPosition = ComputeScreenPos(output.position);
                output.eyeDepth = -UnityObjectToViewPos(input.vertex).z;
                float3 worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.fogUV = (worldPosition.xz - _FogMapOrigin.xy) / _FogMapSize.xy;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(
                    _CameraDepthTexture,
                    UNITY_PROJ_COORD(input.screenPosition)));

                // 只标记与主相机深度一致的可见表面，避免单位或建筑前方的环境物体穿透遮罩。
                clip(sceneDepth - input.eyeDepth + 0.05);
                // RG 直接记录模型表面的地图坐标，B=1 表示有效环境像素。
                // 后处理不再依赖模型背后的地面深度重建世界位置，避免相机移动时产生视差跳变。
                return fixed4(input.fogUV, 1.0, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
