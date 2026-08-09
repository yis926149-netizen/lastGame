using NUnit.Framework;
using UnityEngine;

/// <summary>阶段 4.1：材质输入契约（faceTier UV0 编码 / 配置默认值 / 边界 / 回退路径）。</summary>
public class MountainMaterialContractTests
{
    [Test]
    public void FaceTier_EncodeDecodeRoundtrip_Only012()
    {
        for (int tier = 0; tier < MountainMaterialContract.FaceTierCount; tier++)
        {
            float uvY = MountainMaterialContract.EncodeFaceTier(tier);
            Assert.AreEqual(tier, MountainMaterialContract.DecodeFaceTier(uvY), $"tier {tier} 往返解码");
            Assert.AreEqual(0, Mathf.FloorToInt(uvY * MountainMaterialContract.FaceTierCount - 0.5f) - tier, "编码点必须是整数档位");
        }
    }

    [Test]
    public void FaceTier_DecodeNeverLeaves012_ForArbitraryUvY()
    {
        foreach (float uvY in new[] { 0f, 0.1f, 0.3f, 0.4f, 0.5f, 0.6f, 0.8f, 0.9f, 1f })
        {
            int tier = MountainMaterialContract.DecodeFaceTier(uvY);
            Assert.That(tier, Is.InRange(0, MountainMaterialContract.FaceTierCount - 1), $"uvY={uvY}");
        }
    }

    [Test]
    public void FaceTier_EncodeClampsOutOfRangeTiers()
    {
        Assert.AreEqual(
            MountainMaterialContract.EncodeFaceTier(0),
            MountainMaterialContract.EncodeFaceTier(-3),
            "负档位钳到 0");
        Assert.AreEqual(
            MountainMaterialContract.EncodeFaceTier(MountainMaterialContract.FaceTierCount - 1),
            MountainMaterialContract.EncodeFaceTier(99),
            "超档钳到最高档");
    }

    [Test]
    public void RidgeKey01_IsStable_In01Range_AndNullSafe()
    {
        var ridge = new MountainRidgeData { ridgeId = 7, seed = 12345 };

        Assert.AreEqual(MountainMaterialContract.RidgeKey01(ridge), MountainMaterialContract.RidgeKey01(ridge));
        Assert.That(MountainMaterialContract.RidgeKey01(ridge), Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
        Assert.AreEqual(0f, MountainMaterialContract.RidgeKey01(null), "无脊线快照 = 0（不应产生 NaN/越界）");
    }

    [Test]
    public void ConfigDefaults_AreValid()
    {
        var config = ScriptableObject.CreateInstance<MountainConfigSO>();
        try
        {
            Assert.Greater(config.triplanarWorldScale, 0f, "world scale 必须 > 0");
            Assert.GreaterOrEqual(config.triplanarBlendSharpness, 1f, "blend sharpness 必须 ≥ 1");
            Assert.AreEqual(1f, config.tierColorLow.a, 1e-4f, "色阶 0 不透明");
            Assert.AreEqual(1f, config.tierColorMid.a, 1e-4f, "色阶 1 不透明");
            Assert.AreEqual(1f, config.tierColorHigh.a, 1e-4f, "色阶 2 不透明");
            Assert.That(config.roughness, Is.InRange(0f, 1f));
            Assert.That(config.metallic, Is.InRange(0f, 1f));
            Assert.That(config.shadowStrength, Is.InRange(0f, 1f));
            Assert.IsTrue(MountainMaterialContract.IsValid(config, out string error), error);
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }

    [Test]
    public void Config_NoTexture_PureColorModeStillValid()
    {
        var config = ScriptableObject.CreateInstance<MountainConfigSO>();
        try
        {
            Assert.IsNull(config.rockTexture, "默认无纹理 = 纯色模式");
            Assert.IsTrue(MountainMaterialContract.IsValid(config, out string error), error);
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }

    [Test]
    public void Config_InvalidBounds_AreReported()
    {
        var config = ScriptableObject.CreateInstance<MountainConfigSO>();
        try
        {
            config.triplanarWorldScale = 0f;
            Assert.IsFalse(MountainMaterialContract.IsValid(config, out string error));
            StringAssert.Contains("triplanarWorldScale", error);

            config.triplanarWorldScale = 1f;
            config.triplanarBlendSharpness = 0.5f;
            Assert.IsFalse(MountainMaterialContract.IsValid(config, out error));
            StringAssert.Contains("triplanarBlendSharpness", error);

            config.triplanarBlendSharpness = 4f;
            config.roughness = 2f;
            Assert.IsFalse(MountainMaterialContract.IsValid(config, out error));
            StringAssert.Contains("roughness", error);

            config.roughness = 0.9f;
            config.metallic = -1f;
            Assert.IsFalse(MountainMaterialContract.IsValid(config, out error));
            StringAssert.Contains("metallic", error);

            config.metallic = 0f;
            config.shadowStrength = 1.5f;
            Assert.IsFalse(MountainMaterialContract.IsValid(config, out error));
            StringAssert.Contains("shadowStrength", error);
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }

    [Test]
    public void FallbackContract_ShaderNameNonEmpty_AndIndexInsideBaseSlots()
    {
        Assert.IsFalse(string.IsNullOrEmpty(MountainMaterialContract.StableShaderName), "阶段 4.2 需要 Shader 名称作为查找键");
        Assert.That(
            MountainMaterialContract.FallbackMaterialIndex,
            Is.InRange(0, MountainMaterialContract.BaseSlotCount - 1),
            "回退材质必须在基础 3 槽内（_terrainBaseMaterial0/1/2）");
    }
}
