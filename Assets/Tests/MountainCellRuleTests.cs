using NUnit.Framework;
using UnityEngine;

// 【程序化山脉】山脉地块玩法规则纯函数测试（决策 ①/⑦/㉕）。
/// <summary>
/// 覆盖：占用标记判定、有效山体判定（水淹/永久清除）、移动力派生、通行/部署/建造资格。
/// </summary>
public class MountainCellRuleTests
{
    private MapLandFormSO _mountainForm;
    private MapLandFormSO _normalForm;
    private MapLandFormSO _blockBuildForm;

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;

        _mountainForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        _mountainForm.mountainForm = true;
        _mountainForm.blockBuildingSpawn = true;

        _normalForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        _blockBuildForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        _blockBuildForm.blockBuildingSpawn = true;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mountainForm);
        Object.DestroyImmediate(_normalForm);
        Object.DestroyImmediate(_blockBuildForm);
    }

    private static HexCellData CreateCell(float height, MapLandFormSO landForm = null)
    {
        var cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, height);
        cell.landForm = landForm;
        return cell;
    }

    [Test]
    public void IsMountainForm_OnlyMountainMarkedForm()
    {
        Assert.IsTrue(MountainCellRule.IsMountainForm(_mountainForm));
        Assert.IsFalse(MountainCellRule.IsMountainForm(_normalForm));
        Assert.IsFalse(MountainCellRule.IsMountainForm(null));
    }

    [Test]
    public void IsMountainCell_ChecksLandFormMarker()
    {
        Assert.IsTrue(MountainCellRule.IsMountainCell(CreateCell(2f, _mountainForm)));
        Assert.IsFalse(MountainCellRule.IsMountainCell(CreateCell(2f, _normalForm)));
        Assert.IsFalse(MountainCellRule.IsMountainCell(CreateCell(2f, null)));
    }

    [Test]
    public void IsEffectiveMountainCell_ExcludesWaterAndCleared()
    {
        HexCellData mountain = CreateCell(2f, _mountainForm);
        Assert.IsTrue(MountainCellRule.IsEffectiveMountainCell(mountain), "陆地山格为有效山体");

        HexCellData flooded = CreateCell(0.5f, _mountainForm);
        Assert.IsFalse(MountainCellRule.IsEffectiveMountainCell(flooded), "水淹山格贡献移除，保留基础海床（决策 ⑦）");

        HexCellData cleared = CreateCell(2f, _mountainForm);
        cleared.mountainCleared = true;
        Assert.IsFalse(MountainCellRule.IsEffectiveMountainCell(cleared), "永久清除山格不恢复（决策 ㉕）");
    }

    [Test]
    public void DeriveMovementCost_FollowsWaterThenMountainThenLand()
    {
        Assert.AreEqual(1f, MountainCellRule.DeriveMovementCost(CreateCell(2f)), "普通陆地 = 1");
        Assert.AreEqual(float.MaxValue, MountainCellRule.DeriveMovementCost(CreateCell(0.5f)), "水域 = MaxValue");
        Assert.AreEqual(float.MaxValue, MountainCellRule.DeriveMovementCost(CreateCell(2f, _mountainForm)), "山格 = MaxValue（决策 ①）");
        HexCellData clearedLand = CreateCell(2f, _mountainForm);
        clearedLand.mountainCleared = true;
        Assert.AreEqual(1f, MountainCellRule.DeriveMovementCost(clearedLand), "清除山格恢复 = 1");
        Assert.AreEqual(float.MaxValue, MountainCellRule.DeriveMovementCost(CreateCell(0.5f, _mountainForm)), "水淹山格仍按水 = MaxValue");
    }

    [Test]
    public void CanEnterCell_And_CanSpawnUnitOnCell_RejectMountain()
    {
        Assert.IsTrue(MountainCellRule.CanEnterCell(CreateCell(2f)));
        Assert.IsFalse(MountainCellRule.CanEnterCell(CreateCell(2f, _mountainForm)), "山格不可通行（决策 ①）");
        Assert.IsFalse(MountainCellRule.CanSpawnUnitOnCell(CreateCell(2f, _mountainForm)), "山格不可部署单位（决策 ①）");
        Assert.IsFalse(MountainCellRule.CanSpawnUnitOnCell(CreateCell(0.5f)), "水域不可部署单位");
    }

    [Test]
    public void CanBuildOnCell_RejectsMountainWaterAndBlockBuildForm()
    {
        Assert.IsTrue(MountainCellRule.CanBuildOnCell(CreateCell(2f)), "空地可建造");
        Assert.IsFalse(MountainCellRule.CanBuildOnCell(CreateCell(2f, _mountainForm)), "山格不可建造（决策 ①）");
        Assert.IsFalse(MountainCellRule.CanBuildOnCell(CreateCell(0.5f)), "水域不可建造");
        Assert.IsFalse(MountainCellRule.CanBuildOnCell(CreateCell(2f, _blockBuildForm)), "blockBuildingSpawn 地貌不可建造");
        Assert.IsTrue(MountainCellRule.CanBuildOnCell(CreateCell(2f, _normalForm)), "纯视觉地貌可建造");
    }
}
