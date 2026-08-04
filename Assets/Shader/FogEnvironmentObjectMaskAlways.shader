Shader "Hidden/FogEnvironmentObjectMaskAlways"
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

            float4 _FogMapOrigin;
            float4 _FogMapSize;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 fogUV : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                float3 worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.fogUV = (worldPosition.xz - _FogMapOrigin.xy) / _FogMapSize.xy;
                return output;
            }

            // 【地貌/资源常驻遮罩】不采样相机深度、不做深度裁剪：
            // 贴地/半埋模型（金矿等）的像素会随相机角度被深度测试裁出遮罩，
            // 导致"拉近时从迷雾中显露"。地貌/资源是环境物体，雾化只应取决于
            // 地块探索状态，与相机视角无关；被建筑遮挡的像素由后绘制的
            // 建筑遮罩覆盖，被单位遮挡的像素由单位擦除层清除。
            fixed4 frag(v2f input) : SV_Target
            {
                return fixed4(input.fogUV, 1.0, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
