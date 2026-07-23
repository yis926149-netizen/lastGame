Shader "Custom/FogCover"
{
    // 恒迷雾 Shader：用于地图边缘迷雾连接面片与封皮。
    // 与地形迷雾（FogBlend.cginc 未探索分支）同一套全局参数与整图 UV 公式，
    // 保证颜色/图案从未探索地形 → 连接面片 → 封皮无缝延续。
    // 纯自发光观感：不受光照、不接受阴影、不投射阴影（无 ShadowCaster Pass）。
    Properties { }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            // 全局迷雾参数（由 MapRenderer.SetupFogGlobalShaderProperties 设置）
            uniform sampler2D _FogTex;
            uniform fixed4 _FogColor;
            uniform float _FogEmission;
            uniform float _FogTexAmount;
            uniform float4 _FogMapOrigin;  // 地图世界 XZ 包围盒起点 (x=minX, y=minZ)
            uniform float4 _FogMapSize;    // 地图世界 XZ 尺寸 (x=sizeX, y=sizeZ)

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 整图唯一映射：与 FogBlend.cginc / LakeorSea 同一公式。
                // 面片超出地图包围盒的部分 UV 越界 [0,1]，迷雾贴图 wrap=Clamp，
                // 自动取边缘色向外延伸，不重复平铺。
                float2 uv = (i.worldPos.xz - _FogMapOrigin.xy) / _FogMapSize.xy;
                fixed3 fogTex = tex2Dlod(_FogTex, float4(uv, 0, 0)).rgb;
                fixed3 rgb = _FogColor.rgb * lerp(fixed3(1,1,1), fogTex, _FogTexAmount) * _FogEmission;
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
