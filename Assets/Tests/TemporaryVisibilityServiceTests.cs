using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using UnityEngine;

//****************************************
// 【动态地图-阶段二】TemporaryVisibilityService 单元测试
// 覆盖：来源式 lease 多来源并存、释放一个不影响另一个、重复释放幂等、永久可见性回落。
//****************************************

public class TemporaryVisibilityServiceTests
{
    private TemporaryVisibilityService _service;
    private ILogisticsService _logistics;
    private HexCellData _cell;
    private HexCellData _otherCell;

    [SetUp]
    public void SetUp()
    {
        _logistics = Substitute.For<ILogisticsService>();
        _service = new TemporaryVisibilityService(_logistics);

        _cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 2f);
        _otherCell = new HexCellData(Enums.HexType.NoRiver, 1, new Vector3(1, -1, 0), Vector3.zero, 2f);
    }

    [Test]
    public void AcquireLease_MakesCellTemporarilyVisible()
    {
        var lease = _service.AcquireLease("Arena", new[] { _cell });

        Assert.IsTrue(_service.IsTemporarilyVisible(_cell));
        Assert.IsTrue(lease.IsActive);
    }

    [Test]
    public void IsVisibleToFaction_TemporaryLeaseOverridesPermanentVisibility()
    {
        _logistics.IsVisibleToFaction(_cell, 0).Returns(false);

        var lease = _service.AcquireLease("Arena", new[] { _cell });

        Assert.IsTrue(_service.IsVisibleToFaction(_cell, 0), "临时 lease 应覆盖永久不可见");
    }

    [Test]
    public void ReleaseLease_RestoresPermanentVisibility()
    {
        _logistics.IsVisibleToFaction(_cell, 0).Returns(false);

        var lease = _service.AcquireLease("Arena", new[] { _cell });
        lease.Release();

        Assert.IsFalse(lease.IsActive);
        Assert.IsFalse(_service.IsTemporarilyVisible(_cell));
        Assert.IsFalse(_service.IsVisibleToFaction(_cell, 0), "释放后应回落永久可见性");
    }

    [Test]
    public void MultipleSources_ReleasingOneKeepsOther()
    {
        var arena = _service.AcquireLease("Arena", new[] { _cell });
        var spell = _service.AcquireLease("Spell", new[] { _cell });

        arena.Release();

        Assert.IsTrue(_service.IsTemporarilyVisible(_cell), "释放 Arena 不应影响 Spell 的点亮");
        Assert.IsTrue(spell.IsActive);

        spell.Release();
        Assert.IsFalse(_service.IsTemporarilyVisible(_cell));
    }

    [Test]
    public void Release_IsIdempotent()
    {
        var lease = _service.AcquireLease("Arena", new[] { _cell });
        lease.Release();
        lease.Release(); // 重复释放不抛异常

        Assert.IsFalse(lease.IsActive);
        Assert.IsFalse(_service.IsTemporarilyVisible(_cell));
    }

    [Test]
    public void DifferentCells_Independent()
    {
        var lease = _service.AcquireLease("Arena", new[] { _cell });
        _logistics.IsVisibleToFaction(_otherCell, 0).Returns(false);

        Assert.IsTrue(_service.IsTemporarilyVisible(_cell));
        Assert.IsFalse(_service.IsTemporarilyVisible(_otherCell));
    }

    [Test]
    public void ReleaseAll_ClearsEverything()
    {
        _service.AcquireLease("Arena", new[] { _cell });
        _service.AcquireLease("Spell", new[] { _cell });

        _service.ReleaseAll();

        Assert.IsFalse(_service.HasActiveLeases);
        Assert.IsFalse(_service.IsTemporarilyVisible(_cell));
    }

    [Test]
    public void NoLease_FallsBackToLogisticsPermanentVisibility()
    {
        _logistics.IsVisibleToFaction(_cell, 0).Returns(true);
        Assert.IsTrue(_service.IsVisibleToFaction(_cell, 0));

        _logistics.IsVisibleToFaction(_cell, 0).Returns(false);
        Assert.IsFalse(_service.IsVisibleToFaction(_cell, 0));
    }

    [Test]
    public void NullLogistics_FallsBackToExplored()
    {
        var bare = new TemporaryVisibilityService();
        _cell.ExploreBy(0);
        Assert.IsTrue(bare.IsVisibleToFaction(_cell, 0));
    }
}
