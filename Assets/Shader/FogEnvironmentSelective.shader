Shader "Hidden/FogEnvironmentSelective"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "FogBlend.cginc"

            sampler2D _FogSceneColorTex;
            sampler2D _FogAffectedObjectMask;

            fixed4 frag(v2f_img input) : SV_Target
            {
                // 场景颜色和对象数据缓冲直接使用同一屏幕 UV。
                float2 colorUV = input.uv;

                fixed4 source = tex2D(_FogSceneColorTex, colorUV);
                fixed4 objectData = tex2D(_FogAffectedObjectMask, colorUV);
                float objectMask = objectData.b;

                if (objectMask < 0.5)
                    return source;

                float2 fogUV = objectData.rg;
                if (any(fogUV < 0.0) || any(fogUV > 1.0))
                    return source;

                // 与 FogBlend_final 完全一致的边界解析（保证资源/地貌雾化边界与地形对齐）
                float2 uvW = FogBlend_warpUV(fogUV);
                float exploredJagged;
                float edgeBandFog = 0.0;

                if (_FogEdgeStyle < 0.5)
                {
                    float r = tex2Dlod(_FogMaskTex, float4(uvW, 0, 0)).r;
                    exploredJagged = FogBlend_resolveEdge(r);
                }
                else
                {
                    float soft = FogBlend_sampleSoftMask(uvW);

                    if (_FogEdgeStyle < 1.5)
                    {
                        exploredJagged = smoothstep(0.25, 0.75, soft);
                    }
                    else if (_FogEdgeStyle < 2.5)
                    {
                        float w = saturate(_FogEdgeSoftness * 0.2);
                        w = max(w, 0.05);
                        float2 uvE = (_FogEdgeAnimSpeed > 0.0001)
                            ? FogBlend_warpUV_animated(fogUV, _FogEdgeAnimSpeed)
                            : uvW;
                        float r = tex2Dlod(_FogMaskTex, float4(uvE, 0, 0)).r;
                        exploredJagged = smoothstep(0.5 - w, 0.5 + w, r);
                    }
                    else if (_FogEdgeStyle < 3.5)
                    {
                        float n = FogBlend_valueNoise(uvW * 12.0);
                        exploredJagged = smoothstep(0.32 - n * 0.18, 0.68 + n * 0.18, soft);
                    }
                    else
                    {
                        exploredJagged = smoothstep(0.25, 0.75, soft);
                        edgeBandFog = saturate(soft * (1.0 - soft) * 4.0);
                    }
                }

                if (exploredJagged >= 0.999)
                    return source;

                fixed3 unexploredColor = FogBlend_applyUnexplored(source.rgb, uvW);

                if (edgeBandFog > 0.001)
                {
                    fixed3 bandLayer = FogBlend_sampleFogLayer(uvW, 1.7, 0.31) * 0.7;
                    unexploredColor = lerp(unexploredColor, bandLayer, edgeBandFog * 0.45);
                }

                // 按锯齿边界在"未探索处理色"和"正常色"间过渡
                source.rgb = lerp(unexploredColor, source.rgb, exploredJagged);
                return source;
            }
            ENDCG
        }
    }
    FallBack Off
}
