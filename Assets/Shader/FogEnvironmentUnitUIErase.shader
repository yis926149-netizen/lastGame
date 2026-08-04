Shader "Hidden/FogEnvironmentUnitUIErase"
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                // CreateFullScreenQuad 已提供 clip-space [-1,1] 顶点，不能再乘相机 MVP。
                output.position = input.vertex;
                output.uv = input.uv;
                return output;
            }

            // 单位 UI（世界空间 Canvas）的屏幕矩形，归一化坐标（x,y=min, z,w=max）
            float4 _UnitUIRects[32];
            int _UnitUICount;

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.uv;
                for (int i = 0; i < 32; i++)
                {
                    if (i >= _UnitUICount) break;
                    float4 r = _UnitUIRects[i];
                    if (uv.x >= r.x && uv.x <= r.z && uv.y >= r.y && uv.y <= r.w)
                        return fixed4(0, 0, 0, 1);
                }
                clip(-1.0);
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
