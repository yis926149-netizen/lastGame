using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class CardServiceTests
{
    private DiContainer _container;
    private IUIConfigProvider _mockUiConfig;
    private ICardUnlockRuleProvider _mockUnlockRules;
    private CardService _service;
    private NormalCardPoolSO _pool;

    [SetUp]
    public void SetUp()
    {
        SeedService.Initialize(12345);
        _pool = ScriptableObject.CreateInstance<NormalCardPoolSO>();
        var unit0 = ScriptableObject.CreateInstance<UnitConfigSO>();
        unit0.unitData = new UnitData(0, "Settler", 1, 20, 1, 0, 1, 2);
        var unit1 = ScriptableObject.CreateInstance<UnitConfigSO>();
        unit1.unitData = new UnitData(1, "Warrior", 1, 20, 1, 5, 1, 2);
        _pool.cards = new List<NormalCardConfigSO> { unit0, unit1 };
        _pool.guaranteedFirstCard = unit0;

        _container = new DiContainer();

        _mockUiConfig = Substitute.For<IUIConfigProvider>();

        _mockUnlockRules = Substitute.For<ICardUnlockRuleProvider>();
        _mockUnlockRules.GetUnlockedCards().Returns(new List<NormalCardConfigSO> { unit0, unit1 });
        _mockUnlockRules.GetGuaranteedFirstCard().Returns(unit0);

        _container.Bind<IUIConfigProvider>().FromInstance(_mockUiConfig);
        _container.Bind<ICardUnlockRuleProvider>().FromInstance(_mockUnlockRules);
        _container.Bind<ICardService>().To<CardService>().AsSingle();

        _service = _container.Resolve<ICardService>() as CardService;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_pool);
    }

    [Test]
    public void GenerateNextCard_FirstCallReturnsGuaranteedSettler()
    {
        NormalCardConfigSO card = _service.GenerateNextCard();
        Assert.IsNotNull(card);
        Assert.IsTrue(card is UnitConfigSO);
        Assert.AreEqual(0, ((UnitConfigSO)card).Id);
    }

    [Test]
    public void GenerateNextCard_SubsequentCallsBelongToPool()
    {
        _service.GenerateNextCard(); // 消耗保底
        for (int i = 0; i < 10; i++)
        {
            NormalCardConfigSO card = _service.GenerateNextCard();
            Assert.IsNotNull(card);
            CollectionAssert.Contains(new List<NormalCardConfigSO>(_pool.cards), card);
        }
    }

    [Test]
    public void RegisterCardView_OccupiesSlot()
    {
        for (int slot = 0; slot < 5; slot++)
        {
            _service.RegisterCardView(slot, Substitute.For<ICardView>());
        }
        Assert.AreEqual(-1, _service.GetFirstEmptySlot());
    }

    [Test]
    public void RemoveCard_FreesSlot()
    {
        var view = Substitute.For<ICardView>();
        _service.RegisterCardView(0, view);
        _service.RemoveCard(0);
        Assert.AreEqual(0, _service.GetFirstEmptySlot());
    }
}
