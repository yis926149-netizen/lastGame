Shader "Hidden/FogEnvironmentUnitErase"
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

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.screenPosition = ComputeScreenPos(output.position);
                output.eyeDepth = -UnityObjectToViewPos(input.vertex).z;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(
                    _CameraDepthTexture,
                    UNITY_PROJ_COORD(input.screenPosition)));

                // 【单位擦除层】单位材质是透明队列（如 UnityChan/Skin - Transparent，
                // Queue=Overlay+104），不出现在相机深度纹理中，导致对象遮罩的深度裁剪
                // （FogEnvironmentObjectMask）看不到"单位挡在金矿/建筑前"，
                // 雾化会连带盖住单位。此处用单位自身深度与场景深度比较：
                // 单位可见（前方无非透明物体）才擦除该像素；
                // 单位被金矿/地形等不透明物体遮挡时（clip 为负）不擦除，保持原雾化。
                clip(sceneDepth - input.eyeDepth + 0.05);

                // B=0：把该像素从雾化对象遮罩中清除 → 单位永不雾化（决策 8）
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
