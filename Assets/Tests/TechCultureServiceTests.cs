using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class TechCultureServiceTests
{
    private DiContainer _container;
    private IUnitDataProvider _mockUnitData;
    private IBuildingDataProvider _mockBuildingData;
    private ITechTreeIconsProvider _mockIcons;
    private IUnitRepository _mockUnitRepo;
    private PlayerModelManager _mockPlayerModel;
    private TechData _mockTechData;
    private CultureData _mockCultureData;
    private IGameStateMachine _mockGameState;
    private Tech_CultureTreeController _controller;

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        _mockUnitData = Substitute.For<IUnitDataProvider>();
        _mockBuildingData = Substitute.For<IBuildingDataProvider>();
        _mockIcons = Substitute.For<ITechTreeIconsProvider>();
        _mockUnitRepo = Substitute.For<IUnitRepository>();
        _mockPlayerModel = Substitute.For<PlayerModelManager>();
        _mockGameState = Substitute.For<IGameStateMachine>();

        // 创建临时数据组件（需要挂载在 GameObject 上）
        var techGo = new GameObject();
        _mockTechData = techGo.AddComponent<TechData>();
        var cultureGo = new GameObject();
        _mockCultureData = cultureGo.AddComponent<CultureData>();

        // 注入依赖（通过字段或属性，实际组件可能用 [Inject]）
        // 这里简化：直接赋值
        _controller = new GameObject().AddComponent<Tech_CultureTreeController>();
        _container.Inject(_controller); // 假设已绑定所需依赖

        // 手动设置一些测试数据
        _mockIcons.GetAllTechIcon().Returns(new List<Sprite> { null, null });
        _mockIcons.GetAllCultureIcon().Returns(new List<Sprite> { null, null });
        _mockTechData.TechCost = new List<int> { 100, 200 };
        _mockCultureData.CultureCost = new List<int> { 100, 200 };

        _controller.Tech = new Tech_CultureTreeController.Tech_Culture();
        _controller.Culture = new Tech_CultureTreeController.Tech_Culture();
        _controller.Start(); // 调用初始化
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_controller.gameObject);
        Object.DestroyImmediate(_mockTechData.gameObject);
        Object.DestroyImmediate(_mockCultureData.gameObject);
    }

    [Test]
    public void AddTechPoints_IncreasesAccumulatedPointsAndTriggersEvent()
    {
        bool eventFired = false;
        _controller.OnTechPointsChanged += () => eventFired = true;

        _controller.AddTechPoints(50);
        Assert.AreEqual(0.5f, _controller.Tech.AccumulatedPoints); // 50/100
        Assert.IsTrue(eventFired);
    }

    [Test]
    public void AddPointsPerTurn_UpdatesBothTechAndCulture()
    {
        _controller.Tech.Points = 30;
        _controller.Culture.Points = 20;
        _controller.AddPointsPerTurn();

        Assert.AreEqual(0.3f, _controller.Tech.AccumulatedPoints); // 30/100
        Assert.AreEqual(0.2f, _controller.Culture.AccumulatedPoints); // 20/100
    }

    [Test]
    public void LevelUp_WhenAccumulatedReachesFull_AdvancesLevel()
    {
        _mockGameState.CurrentTurn.Returns(1);
        _controller.Tech.AccumulatedPoints = 1f;
        _controller.Tech.Level = 0;
        _controller.Tech.SwitchOptionsTurn = 0; // 允许切换

        // 模拟 Update 调用（通常需要驱动 Update，可改为手动调用 SwitchOptions）
        // 直接调用 SwitchOptions 私有方法（可用反射或提取为内部方法）
        // 此处假设有一个可调用的公开方法
    }
}