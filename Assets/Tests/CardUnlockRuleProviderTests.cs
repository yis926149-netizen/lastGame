using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Zenject;

public class CardUnlockRuleProviderTests
{
    private ICardUnlockRuleProvider _provider;
    private NormalCardPoolSO _pool;

    [SetUp]
    public void SetUp()
    {
        _pool = ScriptableObject.CreateInstance<NormalCardPoolSO>();
        var unit0 = ScriptableObject.CreateInstance<UnitConfigSO>();
        unit0.unitData = new UnitData(0, "Settler", 1, 20, 1, 0, 1, 2);
        var unit1 = ScriptableObject.CreateInstance<UnitConfigSO>();
        unit1.unitData = new UnitData(1, "Warrior", 1, 20, 1, 5, 1, 2);
        var building0 = ScriptableObject.CreateInstance<BuildingConfigSO>();
        building0.buildingId = 0;
        building0.buildingType = Enums.BulidingType.AttackStatue;
        var building1 = ScriptableObject.CreateInstance<BuildingConfigSO>();
        building1.buildingId = 1;
        building1.buildingType = Enums.BulidingType.DefenseStatue;

        _pool.cards = new List<NormalCardConfigSO> { unit0, unit1, building0, building1 };
        _pool.guaranteedFirstCard = unit0;

        var container = new DiContainer();
        container.Bind<NormalCardPoolSO>().FromInstance(_pool);
        container.Bind<ICardUnlockRuleProvider>().To<CardUnlockRuleProvider>().AsSingle();
        _provider = container.Resolve<ICardUnlockRuleProvider>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_pool);
    }

    [Test]
    public void GetUnlockedCards_ReturnsAllPoolCards()
    {
        var cards = _provider.GetUnlockedCards();
        Assert.AreEqual(4, cards.Count);
        Assert.AreEqual(_pool.cards[0], cards[0]);
        Assert.AreEqual(_pool.cards[3], cards[3]);
    }

    [Test]
    public void GetGuaranteedFirstCard_ReturnsPoolGuaranteed()
    {
        Assert.AreEqual(_pool.guaranteedFirstCard, _provider.GetGuaranteedFirstCard());
    }
}
