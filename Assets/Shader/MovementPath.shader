Shader "Custom/MovementPath"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.84, 0, 0.7)  // 金色，透明度0.5
        _Thickness ("Thickness", Range(0.1, 2.0)) = 1.0
        _DepthOffset ("Depth Offset", Range(0, 0.1)) = 0.01
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent+500"  // 增加队列值，确保最后渲染
        }
        
        LOD 100

        Pass
        {
            Name "GridLine"
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always  // 改为总是通过深度测试
            Offset 0, [_DepthOffset]
            Cull Off  // 改为双面显示
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            fixed4 _Color;
            half _Thickness;
            half _DepthOffset;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = _Color;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    
    FallBack "Transparent/VertexLit"
}