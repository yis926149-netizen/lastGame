using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using Zenject;

public class CardUnlockRuleProviderTests
{
    private CardUnlockRuleProvider _provider;

    [SetUp]
    public void SetUp()
    {
        var unitData = Substitute.For<IUnitDataProvider>();
        unitData.GetUnitIconCount().Returns(12);
        var buildingData = Substitute.For<IBuildingDataProvider>();
        buildingData.GetBuildingCardsCount().Returns(4);

        var container = new DiContainer();
        container.Bind<IUnitDataProvider>().FromInstance(unitData);
        container.Bind<IBuildingDataProvider>().FromInstance(buildingData);
        container.Bind<CardUnlockRuleProvider>().AsSingle();
        _provider = container.Resolve<CardUnlockRuleProvider>();
    }

    [TestCase(0, 0, new[] { 0, 1, 2, 14, 15 })]
    [TestCase(1, 0, new[] { 0, 1, 2, 3, 14, 15 })]
    [TestCase(2, 1, new[] { 0, 1, 2, 3, 4, 12, 14, 15 })]
    [TestCase(5, 2, new[] { 0, 1, 2, 3, 4, 9, 10, 11, 12, 13, 14, 15 })]
    [TestCase(9, 9, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 })]
    public void GetUnlockedCardIds_ReturnsExplicitCumulativeMapping(
        int techLevel,
        int cultureLevel,
        int[] expected)
    {
        List<int> actual = _provider.GetUnlockedCardIds(techLevel, cultureLevel);

        CollectionAssert.AreEquivalent(expected, actual);
        Assert.AreEqual(actual.Count, new HashSet<int>(actual).Count);
    }
}
