#ifndef FOG_BLEND_INCLUDED
#define FOG_BLEND_INCLUDED

sampler2D _FogTex;
fixed4 _FogColor;
float  _FogTexScale;
float  _FogEmission;
float  _FogTexAmount;   // 迷雾贴图叠加强度：0=纯色，1=完全用贴图
float4 _FogMapOrigin;   // 地图世界 XZ 包围盒起点 (x=minX, y=minZ)
float4 _FogMapSize;     // 地图世界 XZ 尺寸 (x=sizeX, y=sizeZ)
float  _FogMemoryDim;   // 记忆区（探索过·当前无视野）亮度系数：0=全黑，1=不压暗。建议 0.45
fixed4 _FogMemoryColor; // 记忆区叠加颜色（RGB），默认白色(1,1,1)=不染色
float  _FogPixelSize;   // 0=平滑曲线；>0=像素阶梯块（值=方块世界边长）
float  _FogJaggedAmount;// 边界起伏【幅度】（世界单位）：边界在原线两侧摆动多远
float  _FogNoiseWavelength; // 锯齿起伏的【波长】（世界单位）：越大凸起/凹口越大越舒展，越小越细碎
sampler2D _FogMaskTex;  // 方案B：世界对齐的探索遮罩（R 通道 0/1）。

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

// 探索/未探索曲线边（供 surf 使用）：在被 warp 的位置采遮罩 R 通道。
float FogBlend_jaggedExploration(float2 uv_FogTex, float exploration)
{
    float2 uvW = FogBlend_warpUV(uv_FogTex);
    float r = tex2Dlod(_FogMaskTex, float4(uvW, 0, 0)).r;
    return FogBlend_resolveEdge(r);
}

fixed3 FogBlend_surf(float2 uv_FogTex, float exploration, fixed3 terrainAlbedo, inout fixed3 o_Emission)
{
    float exploredJagged = FogBlend_jaggedExploration(uv_FogTex, exploration);

    // 在纯色迷雾基础上，用"大尺度(_FogTexScale 小)+低强度(_FogTexAmount 小)"叠加 fog2 贴图，
    // 做一点云雾质感而不再连成面片轮廓：
    //  - 尺度：uv = worldPos.xz * _FogTexScale，_FogTexScale 越小贴图铺得越大、纹路越稀疏舒展；
    //  - 强度：lerp(纯色, 纯色×贴图, _FogTexAmount)，_FogTexAmount 很小时只做轻微明暗起伏。
    //  - 锁 LOD 0：避免斜面/平面 mip 不一致产生的接缝。
    fixed3 fogTex = tex2Dlod(_FogTex, float4(uv_FogTex, 0, 0)).rgb;
    fixed3 fogColor = _FogColor.rgb * lerp(fixed3(1,1,1), fogTex, _FogTexAmount);

    fixed3 finalAlbedo = terrainAlbedo * exploredJagged;
    o_Emission = lerp(fogColor * _FogEmission, o_Emission, exploredJagged);

    return finalAlbedo;
}

fixed FogBlend_alpha(float exploration, float param)
{
    return param * exploration;
}

// 在 surf 中调用：仅在 shadow caster pass 里把未探索片元 clip 掉，使未探索地块不投射阴影。
// UNITY_PASS_SHADOWCASTER 守卫保证只影响阴影 Pass；前向 Pass 不受影响，未探索仍显示迷雾。
// 需要 Shader 的 #pragma surface 带 addshadow，才会生成执行 surf 的自有阴影 Pass。
void FogBlend_shadowClip(float exploration)
{
#if defined(UNITY_PASS_SHADOWCASTER)
    clip(exploration - 0.5);
#endif
}

// 在 finalcolor 修改器中调用：光照全部算完后处理两态。
//  - 未探索(exploration=0)：最终颜色强制覆盖为纯迷雾自发光。
//  - 已探索(exploration=1)：正常光照颜色，全亮。
void FogBlend_final(float exploration, float visibility, half3 fogEmission, inout fixed4 color, float2 uv_FogTex)
{
    float2 uvW = FogBlend_warpUV(uv_FogTex);
    float r = tex2Dlod(_FogMaskTex, float4(uvW, 0, 0)).r;
    float exploredJagged = FogBlend_resolveEdge(r);

    color.rgb = lerp(fogEmission, color.rgb, exploredJagged);
}

#endif
