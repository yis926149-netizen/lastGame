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
    private ITechCultureService _mockTechCulture;
    private IGameStateMachine _mockGameState;
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

        _mockTechCulture = Substitute.For<ITechCultureService>();
        _mockGameState = Substitute.For<IGameStateMachine>();
        _mockUnlockRules = Substitute.For<ICardUnlockRuleProvider>();
        _mockUnlockRules.GetUnlockedCardIds(Arg.Any<int>(), Arg.Any<int>())
            .Returns(new List<int> { 0, 1, 15 });

        _container.Bind<IUnitDataProvider>().FromInstance(_mockUnitData);
        _container.Bind<IBuildingDataProvider>().FromInstance(_mockBuildingData);
        _container.Bind<ITechCultureService>().FromInstance(_mockTechCulture);
        _container.Bind<IGameStateMachine>().FromInstance(_mockGameState);
        _container.Bind<IUIConfigProvider>().FromInstance(Substitute.For<IUIConfigProvider>());
        _container.Bind<ICardUnlockRuleProvider>().FromInstance(_mockUnlockRules);
        _container.Bind<ICardService>().To<CardService>().AsSingle();

        _service = _container.Resolve<ICardService>() as CardService;
    }

    [Test]
    public void GenerateNextCardID_FirstTurn_ReturnsSettlerID()
    {
        _mockGameState.CurrentTurn.Returns(1);
        // ˽���ֶ� _hasGivenFirstTurnSettler ��ʼΪ false
        int id = _service.GenerateNextCardID();
        Assert.AreEqual(0, id); // ����IDΪ0
    }

    [Test]
    public void GenerateNextCardID_AfterFirstTurn_ReturnsOnlyUnlockedCard()
    {
        _mockGameState.CurrentTurn.Returns(2);
        _mockTechCulture.TechLevel.Returns(9);
        _mockTechCulture.CultureLevel.Returns(9);
        _mockUnlockRules.GetUnlockedCardIds(9, 9).Returns(new List<int> { 15 });

        int id = _service.GenerateNextCardID();

        Assert.AreEqual(15, id);
        _mockUnlockRules.Received(1).GetUnlockedCardIds(9, 9);
    }

    [Test]
    public void RegisterCardView_OccupiesSlot()
    {
        for (int slot = 0; slot < 5; slot++)
        {
            _service.RegisterCardView(slot, Substitute.For<ICardView>());
        }
        Assert.AreEqual(-1, _service.GetFirstEmptySlot()); // �޿ղ�
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
