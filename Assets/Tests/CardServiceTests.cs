using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class CardServiceTests
{
    private DiContainer _container;
    private IUnitDataProvider _mockUnitData;
    private IBuildingDataProvider _mockBuildingData;
    private IUIConfigProvider _mockUiConfig;
    private ICardUnlockRuleProvider _mockUnlockRules;
    private CardService _service;

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        _mockUnitData = Substitute.For<IUnitDataProvider>();
        _mockUnitData.GetUnitIconCount().Returns(12);
        _mockUnitData.GetCard(Arg.Any<int>()).Returns((Sprite)null);

        _mockBuildingData = Substitute.For<IBuildingDataProvider>();
        _mockBuildingData.GetBuildingCardsCount().Returns(4);

        _mockUnlockRules = Substitute.For<ICardUnlockRuleProvider>();
        _mockUnlockRules.GetUnlockedCardIds(Arg.Any<int>(), Arg.Any<int>())
            .Returns(new List<int> { 0, 1, 15 });

        _container.Bind<IUnitDataProvider>().FromInstance(_mockUnitData);
        _container.Bind<IBuildingDataProvider>().FromInstance(_mockBuildingData);
        _container.Bind<IUIConfigProvider>().FromInstance(Substitute.For<IUIConfigProvider>());
        _container.Bind<ICardUnlockRuleProvider>().FromInstance(_mockUnlockRules);
        _container.Bind<ICardService>().To<CardService>().AsSingle();

        _service = _container.Resolve<ICardService>() as CardService;
    }

    [Test]
    public void GenerateNextCardID_ReturnsValidCard()
    {
        int id = _service.GenerateNextCardID();
        Assert.GreaterOrEqual(id, 0);
        int totalCardCount = (int)_mockUnitData.GetUnitIconCount() + _mockBuildingData.GetBuildingCardsCount();
        Assert.Less(id, totalCardCount);
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
