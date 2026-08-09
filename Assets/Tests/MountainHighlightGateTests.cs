using NUnit.Framework;
using UnityEngine;

// 【程序化山脉】阶段 6.4：高亮山格门禁纯函数测试（决策 ⑨）。
/// <summary>
/// 覆盖：玩家可见通道过滤有效山格、DebugDirtyChunk 诊断豁免、水淹/清除放行、
/// 水→陆恢复重新拦截、混合集合语义、多通道互不污染（纯函数按通道判定）。
/// 门禁入口 = HexHighlightRenderer.IsBlockedByMountainGate（SetHighlightedCells 实际使用同一函数）。
/// </summary>
public class MountainHighlightGateTests
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

    private static HexCellData CreateCell(float height, MapLandFormSO landForm = null)
    {
        var cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, height);
        cell.landForm = landForm;
        return cell;
    }

    [Test]
    public void PlayerVisibleChannels_BlockEffectiveMountainCells()
    {
        HexCellData mountain = CreateCell(2f, _mountainForm);

        HexHighlightChannel[] playerChannels =
        {
            HexHighlightChannel.CardPlacement,
            HexHighlightChannel.Reachable,
            HexHighlightChannel.AttackRange,
            HexHighlightChannel.Selection,
        };
        foreach (HexHighlightChannel channel in playerChannels)
        {
            Assert.IsTrue(HexHighlightRenderer.IsBlockedByMountainGate(channel, mountain),
                $"{channel} 通道应过滤有效山格（决策 ⑨）");
        }
    }

    [Test]
    public void DebugDirtyChunkChannel_IsExemptFromGate()
    {
        HexCellData mountain = CreateCell(2f, _mountainForm);

        Assert.IsFalse(
            HexHighlightRenderer.IsBlockedByMountainGate(HexHighlightChannel.DebugDirtyChunk, mountain),
            "诊断通道豁免，调试脏格高亮不被玩法门禁吞掉（阶段 6.4）");
    }

    [Test]
    public void PlainCells_AreNeverBlocked()
    {
        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(
            HexHighlightChannel.CardPlacement, CreateCell(2f)));
        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(
            HexHighlightChannel.Reachable, CreateCell(2f)));
        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(
            HexHighlightChannel.DebugDirtyChunk, CreateCell(2f)));
    }

    [Test]
    public void WaterFloodedAndClearedMountains_AreNotBlocked()
    {
        HexCellData flooded = CreateCell(0.5f, _mountainForm);
        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(
            HexHighlightChannel.CardPlacement, flooded), "水淹山格按普通格高亮（决策 ⑦）");

        HexCellData cleared = CreateCell(2f, _mountainForm);
        cleared.mountainCleared = true;
        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(
            HexHighlightChannel.CardPlacement, cleared), "清除山格重新可高亮（决策 ㉕）");
    }

    [Test]
    public void WaterToLandRestore_BlocksAgain()
    {
        HexCellData cell = CreateCell(0.5f, _mountainForm);
        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(HexHighlightChannel.Reachable, cell));

        cell.Height = 2f;
        Assert.IsTrue(HexHighlightRenderer.IsBlockedByMountainGate(HexHighlightChannel.Reachable, cell),
            "水→陆恢复后下一次刷新移除旧高亮（阶段 6.4 动态恢复语义）");
    }

    [Test]
    public void MixedSet_OnlyMountainsFiltered_AndChannelsDoNotPollute()
    {
        HexCellData plain = CreateCell(2f);
        HexCellData mountain = CreateCell(2f, _mountainForm);

        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(HexHighlightChannel.Reachable, plain));
        Assert.IsTrue(HexHighlightRenderer.IsBlockedByMountainGate(HexHighlightChannel.Reachable, mountain));

        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(HexHighlightChannel.DebugDirtyChunk, mountain),
            "同一格在诊断通道不受影响（多通道互不污染）");
        Assert.IsTrue(HexHighlightRenderer.IsBlockedByMountainGate(HexHighlightChannel.AttackRange, mountain),
            "同一格在玩家通道仍被拦截");
    }

    [Test]
    public void NullCell_IsNeverBlocked()
    {
        Assert.IsFalse(HexHighlightRenderer.IsBlockedByMountainGate(HexHighlightChannel.CardPlacement, null));
    }
}
