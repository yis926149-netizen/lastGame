using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 【探索结果纯广播】广播中心与不可变载荷的单元测试。
/// </summary>
public class ExplorationBroadcastTests
{
    [Test]
    public void Publish_NotifiesAllSubscribers()
    {
        var hub = new ExplorationBroadcastHub();
        var acquisition = ExploredAcquisition();
        int a = 0, b = 0;
        hub.Broadcast += _ => a++;
        hub.Broadcast += _ => b++;

        hub.Publish(acquisition);

        Assert.AreEqual(1, a);
        Assert.AreEqual(1, b);
    }

    [Test]
    public void Publish_IsolatesThrowingSubscriber()
    {
        var hub = new ExplorationBroadcastHub();
        var acquisition = ExploredAcquisition();
        int received = 0;
        hub.Broadcast += _ => throw new InvalidOperationException("boom");
        hub.Broadcast += _ => received++;

        LogAssert.Expect(LogType.Exception, "boom");
        hub.Publish(acquisition);

        Assert.AreEqual(1, received);
    }

    [Test]
    public void Publish_ReentrantSettled_DispatchesAfterAllExploredSubscribers()
    {
        var hub = new ExplorationBroadcastHub();
        var explored = ExploredAcquisition();
        var events = new List<string>();

        hub.Broadcast += acquisition =>
        {
            if (acquisition.Phase == ExplorationBroadcastPhase.Explored)
            {
                events.Add("explored-1");
                // 重入发布：Settled 必须等 Explored 全部分发完后才分发
                hub.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, 25));
            }
            else if (acquisition.Phase == ExplorationBroadcastPhase.Settled)
            {
                events.Add("settled");
            }
        };
        hub.Broadcast += acquisition =>
        {
            if (acquisition.Phase == ExplorationBroadcastPhase.Explored)
                events.Add("explored-2");
        };

        hub.Publish(explored);

        CollectionAssert.AreEqual(new[] { "explored-1", "explored-2", "settled" }, events);
    }

    [Test]
    public void Publish_Null_IsIgnored()
    {
        var hub = new ExplorationBroadcastHub();
        int received = 0;
        hub.Broadcast += _ => received++;

        hub.Publish(null);

        Assert.AreEqual(0, received);
    }

    [Test]
    public void Acquisition_SettledAs_PreservesOriginalAndSwitchesPhase()
    {
        var cell = CreateCell();
        var reward = new ExplorationRewardData
        {
            RewardType = ExplorationRewardConfigSO.ExplorationRewardType.Building,
            GoldAmount = 40,
        };
        var explored = ExplorationAcquisition.Explored(cell, 0, reward);

        // 建筑降级为金币：Original 仍是 Building，Settled 是 Gold
        var settled = explored.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, 40);

        Assert.AreEqual(ExplorationBroadcastPhase.Settled, settled.Phase);
        Assert.AreEqual(ExplorationRewardConfigSO.ExplorationRewardType.Building, settled.OriginalRewardType);
        Assert.AreEqual(ExplorationRewardConfigSO.ExplorationRewardType.Gold, settled.SettledRewardType);
        Assert.AreEqual(40, settled.OriginalGoldAmount);
        Assert.AreEqual(40, settled.SettledGoldAmount);
        Assert.AreSame(cell, settled.Cell);
        Assert.IsTrue(settled.HasRewardSnapshot);

        var rewardPoint = settled.AtRewardPoint();
        Assert.AreEqual(ExplorationBroadcastPhase.RewardPoint, rewardPoint.Phase);
        Assert.AreEqual(ExplorationRewardConfigSO.ExplorationRewardType.Gold, rewardPoint.SettledRewardType);
        Assert.AreEqual(40, rewardPoint.SettledGoldAmount);
        Assert.AreEqual(ExplorationRewardConfigSO.ExplorationRewardType.Building, rewardPoint.OriginalRewardType);
    }

    [Test]
    public void Acquisition_UnitConfigs_AreCopiedAtConstruction()
    {
        var cell = CreateCell();
        var unit = ScriptableObject.CreateInstance<UnitConfigSO>();
        var reward = new ExplorationRewardData
        {
            RewardType = ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit,
            UnitConfigs = new[] { unit },
        };

        var acquisition = ExplorationAcquisition.Explored(cell, 0, reward);

        // 构造后修改原始数组不影响载荷（数组只在 Explored 时复制一次）
        reward.UnitConfigs[0] = null;
        Assert.AreSame(unit, acquisition.UnitConfigs[0]);
        Assert.AreEqual(1, acquisition.UnitConfigs.Count);

        UnityEngine.Object.DestroyImmediate(unit);
    }

    [Test]
    public void Acquisition_MissingSnapshot_DistinguishesFromExplicitNone()
    {
        var cell = CreateCell();

        var missing = ExplorationAcquisition.Explored(cell, 0, null);
        Assert.IsFalse(missing.HasRewardSnapshot);
        Assert.AreEqual(ExplorationRewardConfigSO.ExplorationRewardType.None, missing.OriginalRewardType);
        Assert.AreEqual(0, missing.OriginalGoldAmount);

        var none = ExplorationAcquisition.Explored(cell, 0, new ExplorationRewardData
        {
            RewardType = ExplorationRewardConfigSO.ExplorationRewardType.None,
        });
        Assert.IsTrue(none.HasRewardSnapshot);
    }

    [Test]
    public void Acquisition_InvalidPhaseTransitions_Throw()
    {
        var explored = ExploredAcquisition();
        var settled = explored.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, 25);
        var rewardPoint = settled.AtRewardPoint();

        // Settled 不能再次 SettledAs
        Assert.Throws<InvalidOperationException>(() => settled.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, 25));
        // Explored 不能直接 AtRewardPoint
        Assert.Throws<InvalidOperationException>(() => explored.AtRewardPoint());
        // RewardPoint 不能再推进
        Assert.Throws<InvalidOperationException>(() => rewardPoint.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, 25));
        Assert.Throws<InvalidOperationException>(() => rewardPoint.AtRewardPoint());
    }

    private static ExplorationAcquisition ExploredAcquisition()
    {
        return ExplorationAcquisition.Explored(
            CreateCell(),
            0,
            new ExplorationRewardData
            {
                RewardType = ExplorationRewardConfigSO.ExplorationRewardType.Gold,
                GoldAmount = 25,
            });
    }

    private static HexCellData CreateCell()
    {
        return new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);
    }
}
