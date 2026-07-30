#ifndef FOG_BLEND_INCLUDED
#define FOG_BLEND_INCLUDED

sampler2D _FogTex;
fixed4 _FogColor;
float  _FogTexScale;
float  _FogEmission;
float  _FogTexAmount;   // 迷雾贴图叠加强度：0=纯色，1=完全用贴图
float4 _FogMapOrigin;   // 地图世界 XZ 包围盒起点 (x=minX, y=minZ)
float4 _FogMapSize;     // 地图世界 XZ 尺寸 (x=sizeX, y=sizeZ)
float  _FogPixelSize;   // 0=平滑曲线；>0=像素阶梯块（值=方块世界边长）
float  _FogJaggedAmount;// 边界起伏【幅度】（世界单位）：边界在原线两侧摆动多远
float  _FogNoiseWavelength; // 锯齿起伏的【波长】（世界单位）：越大凸起/凹口越大越舒展，越小越细碎
sampler2D _FogMaskTex;  // 世界对齐的探索遮罩（R 通道 0/1）。

float _FogEdgeStyle;     // 0=Original, 1=BlurMask9, 2=WideSmooth, 3=DitheredEdge, 4=SoftPlusFogBand
float _FogEdgeSoftness;  // 边缘柔化宽度（世界单位）
float _FogEdgeAnimSpeed; // 曲线边界流动速度（仅 WideSmooth），0=静态

// 【探索重构-方案三】未探索区视觉参数（去饱和+半透明雾，不再遮挡地形）
float  _FogUnexploredDesaturate; // 未探索区去饱和强度 [0,1]，建议 0.5
float  _FogUnexploredBlend;      // 迷雾色叠加强度 [0,1]，建议 0.5
float2 _FogScrollSpeed;          // 雾纹理 UV 滚动速度（世界单位/秒），建议 (0.02, 0.01)

struct FogInput
{
    float2 uv_FogTex;
    float  exploration;
};

void FogBlend_vert(float4 vertex, out float2 uv_FogTex)
{
    float3 worldPos = mul(unity_ObjectToWorld, vertex).xyz;
    // 方案A：整图唯一映射——把世界 XZ 归一化到地图包围盒 [0,1]，一张贴图铺满整个地图
    // 正好一次、不重复平铺，因此不会再出现“每格描边”。图案钉在世界坐标上，探索切换时不动。
    uv_FogTex = (worldPos.xz - _FogMapOrigin.xy) / _FogMapSize.xy;
}

float FogBlend_hash2(float2 p)
{
    float h = dot(p, float2(127.1, 311.7));
    return frac(sin(h) * 43758.5453);
}

// 低频【相干】值噪声：对格点白噪声做双线性+smoothstep 插值，相邻位置结果接近，
// 因此用它扰动边界会得到【连贯起伏】而不是每格独立翻面的椒盐方块。返回 [0,1]。
float FogBlend_valueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = FogBlend_hash2(i);
    float b = FogBlend_hash2(i + float2(1.0, 0.0));
    float c = FogBlend_hash2(i + float2(0.0, 1.0));
    float d = FogBlend_hash2(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// 曲线机制核心：把片元世界位置按“像素量化(可选)+低频相干噪声域扭曲”得到用于采样遮罩的 UV。
// explored 与 visible 两条边共用这同一个 warp（同一 off）→ 风格一致，且 visible 恒落在 explored 内侧。
float2 FogBlend_warpUV(float2 uv_FogTex)
{
    float2 worldXZ = uv_FogTex * _FogMapSize.xy + _FogMapOrigin.xy;

    // _FogPixelSize>0 → 量化到像素格中心（干脆的阶梯像素块）；=0 → 连续世界坐标（平滑曲线）。
    float2 samplePos = worldXZ;
    if (_FogPixelSize > 0.0001)
    {
        float2 cell = floor(worldXZ / _FogPixelSize);
        samplePos = (cell + 0.5) * _FogPixelSize;
    }

    // 低频相干噪声 → 平滑偏移向量，让边界连贯起伏成不规则形状（波长=起伏大小，幅度=起伏深度）。
    float freq = 1.0 / max(0.5, _FogNoiseWavelength);
    float nx = FogBlend_valueNoise(samplePos * freq);
    float ny = FogBlend_valueNoise(samplePos * freq + 41.3);
    float2 off = (float2(nx, ny) - 0.5) * (2.0 * _FogJaggedAmount);

    return (samplePos + off - _FogMapOrigin.xy) / _FogMapSize.xy;
}

// 动画版边界扭曲：噪声采样坐标随时间漂移 → 曲线边界连续流动（仅 WideSmooth 用）
float2 FogBlend_warpUV_animated(float2 uv_FogTex, float animSpeed)
{
    float2 worldXZ = uv_FogTex * _FogMapSize.xy + _FogMapOrigin.xy;

    float2 samplePos = worldXZ;
    if (_FogPixelSize > 0.0001)
    {
        float2 cell = floor(worldXZ / _FogPixelSize);
        samplePos = (cell + 0.5) * _FogPixelSize;
    }

    // 噪声采样坐标加上时间漂移向量 → 噪声域整体平移，边界随之流动
    float freq = 1.0 / max(0.5, _FogNoiseWavelength);
    float2 drift = _Time.y * animSpeed * float2(0.3, 0.7); // 斜向漂移，避免平行坐标轴
    float nx = FogBlend_valueNoise((samplePos + drift) * freq);
    float ny = FogBlend_valueNoise((samplePos + drift + 41.3) * freq);
    float2 off = (float2(nx, ny) - 0.5) * (2.0 * _FogJaggedAmount);

    return (samplePos + off - _FogMapOrigin.xy) / _FogMapSize.xy;
}

// 把采样到的遮罩通道值二值化：像素模式硬 step（干脆阶梯），曲线模式 fwidth 抗锯齿（干净曲线）。
float FogBlend_resolveEdge(float v)
{
    if (_FogPixelSize > 0.0001)
        return step(0.5, v);
    float aa = max(fwidth(v), 1e-4);
    return smoothstep(0.5 - aa, 0.5 + aa, v);
}

// 在世界空间对周围 3x3=9 点采样 _FogMaskTex，返回 0~1 的软遮罩。
// 采样半径 = _FogEdgeSoftness（世界单位），不依赖遮罩分辨率。
float FogBlend_sampleSoftMask(float2 uvCenter)
{
    float2 stepUV = (_FogEdgeSoftness / _FogMapSize.xy);
    float sum = 0.0;
    for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            sum += tex2Dlod(_FogMaskTex, float4(uvCenter + stepUV * float2(dx, dy), 0, 0)).r;
    return sum / 9.0;
}

// 【探索重构-阶段6】FogBlend_jaggedExploration 已删除（surf 改造后无调用者）。

// 【探索重构-方案三】surf 阶段不再让未探索地形变黑，正常返回地形 Albedo。
// 去饱和+半透明雾的处理全部移到 FogBlend_final（光照算完后统一处理）。
fixed3 FogBlend_surf(float2 uv_FogTex, float exploration, fixed3 terrainAlbedo, inout fixed3 o_Emission)
{
    return terrainAlbedo;
}

// 【探索重构-方案三】Smoothness/Metallic 不再随探索状态归零，始终返回原值。
fixed FogBlend_alpha(float exploration, float param)
{
    return param;
}

// 【探索重构-方案三】未探索格现在正常投射阴影（不再 clip），保留空函数以兼容 Shader 调用点。
void FogBlend_shadowClip(float exploration)
{
}

// 地图、环境对象与永久未探索面片共用的滚动雾纹采样。
// 相位先取 frac，避免长时间运行后浮点精度下降；最终 UV 再循环到 [0,1)。
fixed3 FogBlend_sampleFogLayer(float2 uvW, float speedMultiplier, float uvOffset)
{
    float2 scrollPhase = frac(_Time.y * _FogScrollSpeed * speedMultiplier);
    float2 scrollUV = frac(uvW + scrollPhase + uvOffset);
    fixed3 fogTex = tex2Dlod(_FogTex, float4(scrollUV, 0, 0)).rgb;
    return _FogColor.rgb * lerp(fixed3(1,1,1), fogTex, _FogTexAmount) * _FogEmission;
}

// 永久未探索视觉核心。调用者只提供自身基础色与统一世界雾 UV。
fixed3 FogBlend_applyUnexplored(float3 baseColor, float2 uvW)
{
    float gray = dot(baseColor, fixed3(0.299, 0.587, 0.114));
    fixed3 unexploredColor = lerp(
        baseColor,
        gray * fixed3(0.85, 0.90, 1.0),
        _FogUnexploredDesaturate);
    return lerp(unexploredColor, FogBlend_sampleFogLayer(uvW, 1.0, 0.0), _FogUnexploredBlend);
}

// 【探索重构-方案三】在 finalcolor 修改器中调用：光照算完后处理未探索区视觉。
//  - 已探索(exploredJagged=1)：正常光照颜色，不变。
//  - 未探索(exploredJagged=0)：地形去饱和 + 叠加半透明滚动迷雾（不遮挡地形信息）。
void FogBlend_final(float exploration, float visibility, half3 fogEmission, inout fixed4 color, float2 uv_FogTex)
{
    float2 uvW = FogBlend_warpUV(uv_FogTex);

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
            // animSpeed>0 时用动画版 warp（曲线实时流动）；=0 时退回静态 uvW
            float2 uvE = (_FogEdgeAnimSpeed > 0.0001)
                ? FogBlend_warpUV_animated(uv_FogTex, _FogEdgeAnimSpeed)
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

    fixed3 unexploredColor = FogBlend_applyUnexplored(color.rgb, uvW);

    if (edgeBandFog > 0.001)
    {
        fixed3 bandLayer = FogBlend_sampleFogLayer(uvW, 1.7, 0.31) * 0.7;
        unexploredColor = lerp(unexploredColor, bandLayer, edgeBandFog * 0.45);
    }

    // 按锯齿边界在"未探索处理色"和"正常色"间过渡
    color.rgb = lerp(unexploredColor, color.rgb, exploredJagged);
}

#endif
