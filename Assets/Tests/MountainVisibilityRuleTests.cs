using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 【程序化山脉】阶段 6.1：山格可见性纯规则测试（决策 ⑪）。
/// <summary>
/// 覆盖：普通格 / 有效山格 / 水淹 / 永久清除 / 恢复 / 无 ridge 数据 / 最小可见高度阈值边界；
/// 中立、玩家归属、AI 归属三类山格视觉均可见但数据层不变；
/// 规则纯函数无副作用（不写 IsExplored / 归属 / FogAlphaTarget，不伪造 TemporaryVisibilityService lease）。
/// </summary>
public class MountainVisibilityRuleTests
{
    private MapLandFormSO _mountainForm;

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;
        WaterLevelConfig.MaxHeight = 5f;
        _mountainForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        _mountainForm.mountainForm = true;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mountainForm);
    }

    private HexCellData CreateMountainCell(float height, float distance, float minVisibleHeight = 0.15f)
    {
        var cell = new HexCellData(
            Enums.HexType.NoRiver,
            7,
            new Vector3(7, -7, 0f),
            new Vector3(35f, 0f, 0f),
            height);
        cell.landForm = _mountainForm;
        cell.mountainRidge = new MountainRidgeData
        {
            ridgeId = 3,
            seed = 123456,
            length = 8,
            widthRadius = 1.5f,
            gamma = 1.2f,
            hMax = 2f,
            ridgeNoiseAmplitude = 0f,
            cellNoiseScale = 0f,
            minVisibleHeight = minVisibleHeight,
            maxSlope = 4f,
        };
        cell.mountainRidgeStatus = distance <= 0f
            ? Enums.MountainRidgeStatus.RidgeCell
            : Enums.MountainRidgeStatus.SlopeCell;
        cell.mountainDistToRidge = distance;
        cell.mountainPosAlongRidge = 1f;
        return cell;
    }

    [Test]
    public void IsPermanentlyVisible_NormalCellIsNotVisible()
    {
        var plain = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 2f);
        Assert.IsFalse(MountainVisibilityRule.IsPermanentlyVisible(plain), "普通格无永久视觉可见");

        MapLandFormSO otherForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        try
        {
            var otherCell = new HexCellData(Enums.HexType.NoRiver, 1, Vector3.zero, Vector3.zero, 2f);
            otherCell.landForm = otherForm;
            Assert.IsFalse(MountainVisibilityRule.IsPermanentlyVisible(otherCell), "非山体地貌不豁免");
        }
        finally
        {
            Object.DestroyImmediate(otherForm);
        }
    }

    [Test]
    public void IsPermanentlyVisible_EffectiveMountainIsVisible()
    {
        HexCellData mountain = CreateMountainCell(2f, 0f);
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(mountain), "陆地有效山格视觉可见");
    }

    [Test]
    public void IsPermanentlyVisible_WaterSubmergedMountainIsNotVisible()
    {
        HexCellData flooded = CreateMountainCell(0.5f, 0f);
        Assert.IsFalse(MountainVisibilityRule.IsPermanentlyVisible(flooded), "水淹山格撤销免雾（决策 ⑦）");
    }

    [Test]
    public void IsPermanentlyVisible_ClearedMountainIsNotVisible_AndRestoredAfterRestore()
    {
        HexCellData cleared = CreateMountainCell(2f, 0f);
        cleared.mountainCleared = true;
        Assert.IsFalse(MountainVisibilityRule.IsPermanentlyVisible(cleared), "永久清除山格不豁免（决策 ㉕）");

        cleared.mountainCleared = false;
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(cleared), "清除状态恢复后重新生效");
    }

    [Test]
    public void IsPermanentlyVisible_WaterToLandRestoreTakesEffect()
    {
        HexCellData cell = CreateMountainCell(0.5f, 0f);
        Assert.IsFalse(MountainVisibilityRule.IsPermanentlyVisible(cell));

        cell.Height = 2f;
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(cell), "水→陆恢复山体时重新生效（阶段 6.2 回落口径）");
    }

    [Test]
    public void IsPermanentlyVisible_NoRidgeDataIsNotVisible()
    {
        HexCellData mountain = CreateMountainCell(2f, 0f);
        mountain.mountainRidge = null;
        Assert.IsFalse(MountainVisibilityRule.IsPermanentlyVisible(mountain), "无 ridge 快照不豁免");
    }

    [Test]
    public void IsPermanentlyVisible_MinVisibleHeightThresholdBoundary()
    {
        HexCellData cell = CreateMountainCell(2f, 0f, minVisibleHeight: 0f);
        float exactHeight = MountainGeometryBuilder.ComputeMountainHeight(cell);
        Assert.That(exactHeight, Is.GreaterThan(0f));

        cell.mountainRidge.minVisibleHeight = exactHeight;
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(cell), "恰好等于阈值应可见（>= 判定，决策 ⑳）");

        cell.mountainRidge.minVisibleHeight = exactHeight * 1.001f;
        Assert.IsFalse(MountainVisibilityRule.IsPermanentlyVisible(cell), "低于阈值不可见（防微隆起噪点）");

        cell.mountainRidge.minVisibleHeight = exactHeight * 0.999f;
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(cell), "高于阈值可见");
    }

    [Test]
    public void IsPermanentlyVisible_MatchesHasVisibleMountainOnEveryState()
    {
        HexCellData[] cells =
        {
            CreateMountainCell(2f, 0f),          // 有效山格
            CreateMountainCell(2f, 0f),          // 永久清除
            CreateMountainCell(0.5f, 0f),        // 水淹
            CreateMountainCell(2f, 0.8f),        // 低隆起（可能低于阈值）
        };
        cells[1].mountainCleared = true;

        foreach (HexCellData cell in cells)
        {
            Assert.AreEqual(
                MountainGeometryBuilder.HasVisibleMountain(cell),
                MountainVisibilityRule.IsPermanentlyVisible(cell),
                "可见性规则必须与几何贡献使用同一有效性口径");
        }
    }

    [Test]
    public void IsPermanentlyVisible_AllOwnershipClassesVisible_AndDataLayerUnchanged()
    {
        HexCellData neutral = CreateMountainCell(2f, 0f);
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(neutral), "中立山格视觉可见");

        HexCellData playerOwned = CreateMountainCell(2f, 0f);
        playerOwned.ExploreBy(0);
        playerOwned.Player_City_Index = new KeyValuePair<int, int>(0, 3);
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(playerOwned), "玩家归属山格视觉可见");

        HexCellData aiOwned = CreateMountainCell(2f, 0f);
        aiOwned.ExploreBy(1);
        aiOwned.Player_City_Index = new KeyValuePair<int, int>(1, 5);
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(aiOwned), "AI 归属山格视觉可见");

        Assert.IsTrue(playerOwned.IsExploredBy(0), "玩家探索位保持原值");
        Assert.IsFalse(playerOwned.IsExploredBy(1));
        Assert.AreEqual(new KeyValuePair<int, int>(0, 3), playerOwned.Player_City_Index, "玩家归属保持原值");

        Assert.IsTrue(aiOwned.IsExploredBy(1), "AI 探索位保持原值");
        Assert.IsFalse(aiOwned.IsExploredBy(0));
        Assert.AreEqual(new KeyValuePair<int, int>(1, 5), aiOwned.Player_City_Index, "AI 归属保持原值");
    }

    [Test]
    public void IsPermanentlyVisible_DoesNotChangeGameplayQualification()
    {
        HexCellData mountain = CreateMountainCell(2f, 0f);
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(mountain));

        Assert.IsFalse(MountainCellRule.CanEnterCell(mountain), "视觉可见不改变不可通行（决策 ①）");
        Assert.IsFalse(MountainCellRule.CanSpawnUnitOnCell(mountain), "视觉可见不改变不可部署（决策 ①）");
        Assert.IsFalse(MountainCellRule.CanBuildOnCell(mountain), "视觉可见不改变不可建造（决策 ①）");
    }

    [Test]
    public void IsPermanentlyVisible_IsPure_NoMutationAndNoLeaseFake()
    {
        HexCellData cell = CreateMountainCell(2f, 0f);
        cell.ExploreBy(0);
        cell.ExploreBy(1);
        cell.Player_City_Index = new KeyValuePair<int, int>(0, 2);
        cell.FogAlpha = 0.3f;
        cell.FogAlphaTarget = 0.7f;

        bool beforeExplored0 = cell.IsExploredBy(0);
        bool beforeExplored1 = cell.IsExploredBy(1);
        KeyValuePair<int, int> beforeOwnership = cell.Player_City_Index;
        float beforeFogAlpha = cell.FogAlpha;
        float beforeFogAlphaTarget = cell.FogAlphaTarget;

        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(cell));

        Assert.AreEqual(beforeExplored0, cell.IsExploredBy(0), "不得写探索位（决策 ⑪）");
        Assert.AreEqual(beforeExplored1, cell.IsExploredBy(1), "不得写 AI 探索位（决策 ⑪）");
        Assert.AreEqual(beforeOwnership, cell.Player_City_Index, "不得改归属");
        Assert.AreEqual(beforeFogAlpha, cell.FogAlpha, "规则本身不得直接改雾透明度");
        Assert.AreEqual(beforeFogAlphaTarget, cell.FogAlphaTarget, "不得散写 FogAlphaTarget（阶段 6.2 才接入合成链）");
    }

    [Test]
    public void IsPermanentlyVisible_DoesNotFakeVisibilityLease()
    {
        var service = new TemporaryVisibilityService();
        Assert.IsFalse(service.HasActiveLeases);

        HexCellData cell = CreateMountainCell(2f, 0f);
        Assert.IsTrue(MountainVisibilityRule.IsPermanentlyVisible(cell));

        Assert.IsFalse(service.HasActiveLeases, "规则不得伪造 visibility lease");
        Assert.IsFalse(service.IsTemporarilyVisible(cell));
        Assert.IsTrue(service.IsVisibleToFaction(cell, 0),
            "阶段 6.2 resolver 通过永久可见规则点亮山格，但不得创建临时 lease");
    }
}
