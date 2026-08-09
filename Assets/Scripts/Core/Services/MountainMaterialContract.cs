using UnityEngine;

/// <summary>
/// 阶段 4.1：山体稳定材质的输入契约（决策 ⑥/㉗/㉘）。
/// 只描述数据契约，不持有运行时材质实例：运行时创建/克隆的 Material 实例
/// 严禁序列化回共享配置资产（MountainConfigSO / MountainConfig.asset）。
/// 本契约是 阶段 3 几何 UV0 写入与 阶段 4 shader 解码之间的唯一权威定义，
/// 后续普通地形 UV 逻辑禁止重解释山体槽 UV。
/// </summary>
public static class MountainMaterialContract
{
    /// <summary>离散色阶档数（决策 ㉘：3 段起步，不设雪顶；第 4 档实验参数保持关闭）。</summary>
    public const int FaceTierCount = 3;

    /// <summary>脊线关键域（"RIDG"）：ridgeKey01 = Hash01(seed, 该域, ridgeId)，与遍历顺序无关（决策 ㉓）。</summary>
    public const uint RidgeKeyDomain = 0x52494447u;

    /// <summary>稳定山体 Shader 名称（阶段 4.2 创建资产 Custom/MountainLowPoly_Fog）。</summary>
    public const string StableShaderName = "Custom/MountainLowPoly_Fog";

    /// <summary>山体 Transition Shader 名称（阶段 5.4 创建资产 Custom/MountainLowPoly_Fog_Transition）。
    /// 完整复制稳定版契约（Triplanar/face tier/ridgeKey01/导数法线/FogBlend/双面），
    /// 仅新增 keep-below clip 平面参数；顶点动画在 C# 侧，vert 禁止读取 UV2/UV3。</summary>
    public const string TransitionShaderName = "Custom/MountainLowPoly_Fog_Transition";

    /// <summary>
    /// Shader 查找失败的回退材质索引：基础地形槽 0（_terrainBaseMaterial0，阶段 3 临时回退）。
    /// 回退只记录一次错误，禁止每 Chunk/每帧重复 Shader.Find 或刷日志（阶段 4.2 落地）。
    /// </summary>
    public const int FallbackMaterialIndex = 0;

    /// <summary>基础地形槽数量（3 基础 + N rect + M tri），山体槽恒在其后。</summary>
    public const int BaseSlotCount = 3;

    /// <summary>UV0.x：ridgeKey01 ∈ [0,1)。首版只用于极轻、离散且确定性的色相/亮度偏移，禁止作为纹理平移量。</summary>
    public static float RidgeKey01(MountainRidgeData ridge)
    {
        return ridge != null ? MountainHash.Hash01(ridge.seed, (int)RidgeKeyDomain, ridge.ridgeId) : 0f;
    }

    /// <summary>tier → UV0.y 编码：(tier + 0.5) / FaceTierCount；tier 固定钳制到 [0, FaceTierCount-1]（决策 ㉘）。</summary>
    public static float EncodeFaceTier(int tier)
    {
        return (Mathf.Clamp(tier, 0, FaceTierCount - 1) + 0.5f) / FaceTierCount;
    }

    /// <summary>UV0.y → tier 解码（shader 端用安全 round/saturate 等价形式；同面三顶点同值，无面内渐变）。</summary>
    public static int DecodeFaceTier(float uvY)
    {
        return Mathf.Clamp(Mathf.RoundToInt(uvY * FaceTierCount - 0.5f), 0, FaceTierCount - 1);
    }

    /// <summary>表现参数边界校验（阶段 4.1）：world scale &gt; 0、blend sharpness ≥ 1、roughness/metallic/shadowStrength ∈ [0,1]。</summary>
    public static bool IsValid(MountainConfigSO config, out string error)
    {
        error = null;
        if (config == null)
        {
            error = "MountainConfigSO 为空";
            return false;
        }
        if (config.triplanarWorldScale <= 0f)
        {
            error = $"triplanarWorldScale 必须 > 0（当前 {config.triplanarWorldScale}）";
            return false;
        }
        if (config.triplanarBlendSharpness < 1f)
        {
            error = $"triplanarBlendSharpness 必须 ≥ 1（当前 {config.triplanarBlendSharpness}）";
            return false;
        }
        if (config.roughness < 0f || config.roughness > 1f)
        {
            error = $"roughness 必须在 [0,1]（当前 {config.roughness}）";
            return false;
        }
        if (config.metallic < 0f || config.metallic > 1f)
        {
            error = $"metallic 必须在 [0,1]（当前 {config.metallic}）";
            return false;
        }
        if (config.shadowStrength < 0f || config.shadowStrength > 1f)
        {
            error = $"shadowStrength 必须在 [0,1]（当前 {config.shadowStrength}）";
            return false;
        }
        return true;
    }
}
