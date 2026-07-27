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

// 把采样到的遮罩通道值二值化：像素模式硬 step（干脆阶梯），曲线模式 fwidth 抗锯齿（干净曲线）。
float FogBlend_resolveEdge(float v)
{
    if (_FogPixelSize > 0.0001)
        return step(0.5, v);
    float aa = max(fwidth(v), 1e-4);
    return smoothstep(0.5 - aa, 0.5 + aa, v);
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

// 【探索重构-方案三】在 finalcolor 修改器中调用：光照算完后处理未探索区视觉。
//  - 已探索(exploredJagged=1)：正常光照颜色，不变。
//  - 未探索(exploredJagged=0)：地形去饱和 + 叠加半透明滚动迷雾（不遮挡地形信息）。
void FogBlend_final(float exploration, float visibility, half3 fogEmission, inout fixed4 color, float2 uv_FogTex)
{
    float2 uvW = FogBlend_warpUV(uv_FogTex);
    float r = tex2Dlod(_FogMaskTex, float4(uvW, 0, 0)).r;
    float exploredJagged = FogBlend_resolveEdge(r);

    // 未探索区颜色处理
    fixed3 unexploredColor = color.rgb;
    // 1. 去饱和（转灰度 + 冷色调偏移），保留地形明暗和轮廓
    float gray = dot(unexploredColor, fixed3(0.299, 0.587, 0.114));
    unexploredColor = lerp(unexploredColor, gray * fixed3(0.85, 0.90, 1.0), _FogUnexploredDesaturate);
    // 2. 叠加半透明雾（UV 随时间缓慢滚动，雾纹漂移，零额外开销）
    float2 scrollUV = uvW + _Time.y * _FogScrollSpeed;
    fixed3 fogTex = tex2Dlod(_FogTex, float4(scrollUV, 0, 0)).rgb;
    fixed3 fogLayer = _FogColor.rgb * lerp(fixed3(1,1,1), fogTex, _FogTexAmount) * _FogEmission;
    unexploredColor = lerp(unexploredColor, fogLayer, _FogUnexploredBlend);

    // 按锯齿边界在"未探索处理色"和"正常色"间过渡
    color.rgb = lerp(unexploredColor, color.rgb, exploredJagged);
}

#endif
