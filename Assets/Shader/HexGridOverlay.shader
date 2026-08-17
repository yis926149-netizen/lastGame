Shader "Custom/HexGridOverlay"
{
    Properties
    {
        _Color ("Color (alpha 控制透明度)", Color) = (1, 1, 1, 0.35)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
        }

        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // 被单位/建筑/山体正确遮挡：不透明物已写入深度，网格线只在其前方片元可见。
        ZTest LEqual
        // 配合几何抬升，消除与地形共面的 Z-fighting。
        Offset -1, -1
        Cull Off

        Pass
        {
            Name "HexGridOverlay"

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
