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
    private CardService _service;

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        _mockUnitData = Substitute.For<IUnitDataProvider>();
        _mockUnitData.GetUnitIconCount().Returns(10);
        _mockUnitData.GetCard(Arg.Any<int>()).Returns((Sprite)null);

        _mockBuildingData = Substitute.For<IBuildingDataProvider>();
        _mockBuildingData.GetBuildingCardsCount().Returns(5);

        _mockTechCulture = Substitute.For<ITechCultureService>();
        _mockGameState = Substitute.For<IGameStateMachine>();

        _container.Bind<IUnitDataProvider>().FromInstance(_mockUnitData);
        _container.Bind<IBuildingDataProvider>().FromInstance(_mockBuildingData);
        _container.Bind<ITechCultureService>().FromInstance(_mockTechCulture);
        _container.Bind<IGameStateMachine>().FromInstance(_mockGameState);
        _container.Bind<IUIConfigProvider>().FromInstance(Substitute.For<IUIConfigProvider>());
        _container.Bind<ICardService>().To<CardService>().AsSingle();

        _service = _container.Resolve<ICardService>() as CardService;
    }

    [Test]
    public void GenerateNextCardID_FirstTurn_ReturnsSettlerID()
    {
        _mockGameState.CurrentTurn.Returns(1);
        // 私有字段 _hasGivenFirstTurnSettler 初始为 false
        int id = _service.GenerateNextCardID();
        Assert.AreEqual(0, id); // 移民卡ID为0
    }

    [Test]
    public void GenerateNextCardID_UnlockedByTechLevel_ReturnsUnitWithinTechLevelPlusOne()
    {
        _mockGameState.CurrentTurn.Returns(2);
        _mockTechCulture.TechLevel.Returns(3); // 解锁0~4号单位
        _mockTechCulture.CultureLevel.Returns(0);

        // 假设单位ID 0~9，建筑ID 10~14
        _mockUnitData.GetUnitIconCount().Returns(10);
        _mockBuildingData.GetBuildingCardsCount().Returns(5);

        int id = _service.GenerateNextCardID();
        Assert.IsTrue(id >= 0 && id <= 4);
    }

    [Test]
    public void RegisterCardView_OccupiesSlot()
    {
        var view = Substitute.For<ICardView>();
        int slot = 2;
        _service.RegisterCardView(slot, view);
        Assert.AreEqual(-1, _service.GetFirstEmptySlot()); // 无空槽
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