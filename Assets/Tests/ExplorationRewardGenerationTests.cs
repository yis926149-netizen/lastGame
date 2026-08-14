using NUnit.Framework;
using UnityEngine;

public class ExplorationRewardGenerationTests
{
    private ExplorationRewardConfigSO _config;

    [SetUp]
    public void SetUp()
    {
        _config = ScriptableObject.CreateInstance<ExplorationRewardConfigSO>();
        _config.noneRewardWeight = 1;
        _config.goldRewardWeight = 2;
        _config.militaryRewardWeight = 3;
        _config.tacticalRewardWeight = 4;
        _config.buildingRewardWeight = 5;
        _config.goldTiers = new[] { 5, 10, 25 };
        _config.unitCountTiers = new[] { 0, 1, 2 };
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_config);
    }

    [Test]
    public void GenerateReward_SameSeed_ProducesSameCompleteSequence()
    {
        var firstRandom = new System.Random(12345);
        var secondRandom = new System.Random(12345);

        for (int i = 0; i < 100; i++)
        {
            ExplorationRewardData first = _config.GenerateReward(firstRandom);
            ExplorationRewardData second = _config.GenerateReward(secondRandom);

            Assert.AreEqual(first.RewardType, second.RewardType, $"index={i}");
            Assert.AreEqual(first.GoldAmount, second.GoldAmount, $"index={i}");
            Assert.AreEqual(first.UnitConfigs?.Length ?? 0, second.UnitConfigs?.Length ?? 0, $"index={i}");
            Assert.AreSame(first.TacticalCard, second.TacticalCard, $"index={i}");
            Assert.AreSame(first.BuildingConfig, second.BuildingConfig, $"index={i}");
        }
    }

    [Test]
    public void TakeExplorationReward_ConsumesSnapshotOnce()
    {
        var cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);
        var reward = new ExplorationRewardData
        {
            RewardType = ExplorationRewardConfigSO.ExplorationRewardType.Gold,
            GoldAmount = 25
        };

        cell.SetExplorationReward(reward);

        Assert.AreSame(reward, cell.ExplorationReward);
        Assert.AreSame(reward, cell.TakeExplorationReward());
        Assert.IsNull(cell.ExplorationReward);
        Assert.IsNull(cell.TakeExplorationReward());
    }
}
