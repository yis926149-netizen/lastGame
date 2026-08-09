using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;

// 【程序化山脉】阶段 6.2：visibility resolver 山格规则合成测试（决策 ⑪）。
/// <summary>
/// 覆盖：山格强制可见优先于普通未探索雾与后勤可见性、lease 获取/释放不影响山格、
/// 清除/水淹后回落与恢复后重新生效、重复刷新幂等、IsExplored 与 owner 从未被山格规则改写、
/// 玩家/AI 视角一致、无山路径零回归。
/// </summary>
public class MountainVisibilityResolverTests
{
    private MapLandFormSO _mountainForm;
    private TemporaryVisibilityService _service;
    private ILogisticsService _logistics;

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;
        WaterLevelConfig.MaxHeight = 5f;
        _mountainForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        _mountainForm.mountainForm = true;

        _logistics = Substitute.For<ILogisticsService>();
        _service = new TemporaryVisibilityService(_logistics);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mountainForm);
    }

    private HexCellData CreateMountainCell(float height, bool cleared = false)
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
            minVisibleHeight = 0.15f,
            maxSlope = 4f,
        };
        cell.mountainRidgeStatus = Enums.MountainRidgeStatus.RidgeCell;
        cell.mountainDistToRidge = 0f;
        cell.mountainPosAlongRidge = 1f;
        cell.mountainCleared = cleared;
        return cell;
    }

    private static HexCellData CreatePlainCell(int order = 0)
    {
        return new HexCellData(
            Enums.HexType.NoRiver,
            order,
            new Vector3(order, -order, 0f),
            new Vector3(order * 5f, 0f, 0f),
            2f);
    }

    [Test]
    public void IsVisibleToFaction_MountainVisibleDespiteUnexploredAndHiddenLogistics()
    {
        HexCellData mountain = CreateMountainCell(2f);
        _logistics.IsVisibleToFaction(mountain, 0).Returns(false);

        Assert.IsTrue(_service.IsVisibleToFaction(mountain, 0),
            "有效山格强制可见，优先于后勤不可见（决策 ⑪）");
        Assert.AreEqual(0, _logistics.ReceivedCalls().Count(), "山格规则短路后勤查询");
        Assert.IsFalse(_service.HasActiveLeases, "山格规则不伪造临时可见性");
    }

    [Test]
    public void IsVisibleToFaction_MountainVisibleForBothPlayerAndAiViewers()
    {
        HexCellData mountain = CreateMountainCell(2f);
        _logistics.IsVisibleToFaction(mountain, 0).Returns(false);
        _logistics.IsVisibleToFaction(mountain, 1).Returns(false);

        Assert.IsTrue(_service.IsVisibleToFaction(mountain, 0), "玩家视角可见");
        Assert.IsTrue(_service.IsVisibleToFaction(mountain, 1), "AI 视角可见（阵营无关，阶段 6.2）");
    }

    [Test]
    public void IsVisibleToFaction_LeaseAcquireAndReleaseDoNotAffectMountain()
    {
        HexCellData mountain = CreateMountainCell(2f);
        _logistics.IsVisibleToFaction(mountain, 0).Returns(false);

        var lease = _service.AcquireLease("Arena", new[] { mountain });
        Assert.IsTrue(_service.IsVisibleToFaction(mountain, 0), "lease 存在时山格仍可见");

        lease.Release();
        Assert.IsTrue(_service.IsVisibleToFaction(mountain, 0), "lease 释放后山格仍可见（规则与 lease 解耦）");
        Assert.IsFalse(_service.HasActiveLeases);
    }

    [Test]
    public void IsVisibleToFaction_NonMountainStillUsesLogisticsAndExplored()
    {
        HexCellData plain = CreatePlainCell(0);
        _logistics.IsVisibleToFaction(plain, 0).Returns(false);
        Assert.IsFalse(_service.IsVisibleToFaction(plain, 0), "普通格保持既有可见性链（零回归）");

        _logistics.IsVisibleToFaction(plain, 0).Returns(true);
        Assert.IsTrue(_service.IsVisibleToFaction(plain, 0), "普通格后勤可见保持既有行为");

        var bare = new TemporaryVisibilityService();
        HexCellData exploredCell = CreatePlainCell(1);
        exploredCell.ExploreBy(0);
        Assert.IsTrue(bare.IsVisibleToFaction(exploredCell, 0), "无后勤回落 IsExplored 保持既有行为");
    }

    [Test]
    public void IsVisibleToFaction_ClearedMountainFallsBackToLogistics()
    {
        HexCellData cleared = CreateMountainCell(2f, cleared: true);
        _logistics.IsVisibleToFaction(cleared, 0).Returns(false);
        Assert.IsFalse(_service.IsVisibleToFaction(cleared, 0), "清除山格撤销免雾，回落后勤（决策 ㉕）");

        _logistics.IsVisibleToFaction(cleared, 0).Returns(true);
        Assert.IsTrue(_service.IsVisibleToFaction(cleared, 0), "清除山格按普通可见性链判定");
    }

    [Test]
    public void IsVisibleToFaction_WaterFloodedMountainFallsBackToLogistics()
    {
        HexCellData flooded = CreateMountainCell(0.5f);
        _logistics.IsVisibleToFaction(flooded, 0).Returns(false);
        Assert.IsFalse(_service.IsVisibleToFaction(flooded, 0), "水淹山格撤销免雾（决策 ⑦）");
    }

    [Test]
    public void IsVisibleToFaction_WaterToLandRestoreTakesEffectOnNextRefresh()
    {
        HexCellData cell = CreateMountainCell(0.5f);
        _logistics.IsVisibleToFaction(cell, 0).Returns(false);
        Assert.IsFalse(_service.IsVisibleToFaction(cell, 0));

        cell.Height = 2f;
        Assert.IsTrue(_service.IsVisibleToFaction(cell, 0),
            "水→陆恢复山体后下一次刷新重新生效（雾目标刷新每次均经同一 resolver）");
    }

    [Test]
    public void IsVisibleToFaction_RepeatedRefreshIsIdempotent()
    {
        HexCellData mountain = CreateMountainCell(2f);
        _logistics.IsVisibleToFaction(mountain, 0).Returns(false);

        for (int i = 0; i < 5; i++)
            Assert.IsTrue(_service.IsVisibleToFaction(mountain, 0), "重复刷新结果稳定");
        Assert.IsFalse(_service.HasActiveLeases, "重复刷新不产生副作用");
    }

    [Test]
    public void IsVisibleToFaction_NeverWritesExploredOrOwner()
    {
        HexCellData mountain = CreateMountainCell(2f);
        mountain.ExploreBy(0);
        mountain.ExploreBy(1);
        mountain.Player_City_Index = new KeyValuePair<int, int>(1, 4);

        bool explored0 = mountain.IsExploredBy(0);
        bool explored1 = mountain.IsExploredBy(1);
        KeyValuePair<int, int> ownership = mountain.Player_City_Index;

        _service.IsVisibleToFaction(mountain, 0);
        _service.IsVisibleToFaction(mountain, 1);

        Assert.AreEqual(explored0, mountain.IsExploredBy(0), "山格规则不得写玩家探索位");
        Assert.AreEqual(explored1, mountain.IsExploredBy(1), "山格规则不得写 AI 探索位");
        Assert.AreEqual(ownership, mountain.Player_City_Index, "山格规则不得改归属");
    }

    [Test]
    public void IsVisibleToFaction_NeutralPlayerAndAiOwnedMountainsAllVisible()
    {
        HexCellData neutral = CreateMountainCell(2f);
        HexCellData playerOwned = CreateMountainCell(2f);
        playerOwned.ExploreBy(0);
        playerOwned.Player_City_Index = new KeyValuePair<int, int>(0, 3);
        HexCellData aiOwned = CreateMountainCell(2f);
        aiOwned.ExploreBy(1);
        aiOwned.Player_City_Index = new KeyValuePair<int, int>(1, 5);

        _logistics.IsVisibleToFaction(neutral, 0).Returns(false);
        _logistics.IsVisibleToFaction(playerOwned, 0).Returns(false);
        _logistics.IsVisibleToFaction(aiOwned, 0).Returns(false);

        Assert.IsTrue(_service.IsVisibleToFaction(neutral, 0), "中立山格可见");
        Assert.IsTrue(_service.IsVisibleToFaction(playerOwned, 0), "玩家归属山格可见");
        Assert.IsTrue(_service.IsVisibleToFaction(aiOwned, 0), "AI 归属山格可见");

        Assert.AreEqual(new KeyValuePair<int, int>(0, 3), playerOwned.Player_City_Index, "玩家归属保持原值");
        Assert.AreEqual(new KeyValuePair<int, int>(1, 5), aiOwned.Player_City_Index, "AI 归属保持原值");
    }
}
