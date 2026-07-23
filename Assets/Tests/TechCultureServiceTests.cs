using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
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
    private GameObject _techTreeObject;
    private readonly List<GameObject> _buildingObjects = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        _mockUnitData = Substitute.For<IUnitDataProvider>();
        _mockBuildingData = Substitute.For<IBuildingDataProvider>();
        _mockIcons = Substitute.For<ITechTreeIconsProvider>();
        _mockUnitRepo = Substitute.For<IUnitRepository>();
        _mockGameState = Substitute.For<IGameStateMachine>();

        var playerModelGo = new GameObject("PlayerModelManager");
        _mockPlayerModel = playerModelGo.AddComponent<PlayerModelManager>();

        var techGo = new GameObject("TechData");
        techGo.SetActive(false);
        _mockTechData = techGo.AddComponent<TechData>();
        var cultureGo = new GameObject("CultureData");
        cultureGo.SetActive(false);
        _mockCultureData = cultureGo.AddComponent<CultureData>();

        _mockIcons.GetAllTechIcon().Returns(new List<Sprite> { null, null });
        _mockIcons.GetAllCultureIcon().Returns(new List<Sprite> { null, null });
        _mockTechData.TechCost = new List<int> { 100, 200 };
        _mockCultureData.CultureCost = new List<int> { 100, 200 };

        _container.Bind<IUnitDataProvider>().FromInstance(_mockUnitData);
        _container.Bind<IBuildingDataProvider>().FromInstance(_mockBuildingData);
        _container.Bind<ITechTreeIconsProvider>().FromInstance(_mockIcons);
        _container.Bind<IUnitRepository>().FromInstance(_mockUnitRepo);
        _container.Bind<PlayerModelManager>().FromInstance(_mockPlayerModel);
        _container.Bind<TechData>().FromInstance(_mockTechData);
        _container.Bind<CultureData>().FromInstance(_mockCultureData);
        _container.Bind<IGameStateMachine>().FromInstance(_mockGameState);

        _controller = new GameObject("TechCultureController").AddComponent<Tech_CultureTreeController>();
        _controller.Tech = new Tech_CultureTreeController.Tech_Culture();
        _controller.Culture = new Tech_CultureTreeController.Tech_Culture();
        _container.Inject(_controller);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_controller.gameObject);
        Object.DestroyImmediate(_mockTechData.gameObject);
        Object.DestroyImmediate(_mockCultureData.gameObject);
        Object.DestroyImmediate(_mockPlayerModel.gameObject);
        if (_techTreeObject != null)
        {
            Object.DestroyImmediate(_techTreeObject);
        }
        foreach (GameObject buildingObject in _buildingObjects)
        {
            Object.DestroyImmediate(buildingObject);
        }
        _buildingObjects.Clear();
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
    public void AddPointsPerTurn_AtMaxLevel_DoesNotRestartProgress()
    {
        _controller.Tech.Level = 1;
        _controller.Tech.Points = 100;
        _controller.Tech.AccumulatedPoints = 0.25f;

        _controller.AddPointsPerTurn();

        Assert.AreEqual(0.25f, _controller.Tech.AccumulatedPoints);
    }

    [Test]
    public void AddTechPoints_WithMissingCost_DoesNotThrowOrChangeProgress()
    {
        _mockTechData.TechCost.Clear();

        Assert.DoesNotThrow(() => _controller.AddTechPoints(50));
        Assert.AreEqual(0f, _controller.Tech.AccumulatedPoints);
    }

    [Test]
    public void LevelUp_WhenAccumulatedReachesFull_AdvancesLevel()
    {
        _mockGameState.CurrentTurn.Returns(1);
        _techTreeObject = new GameObject("TechTree", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _controller.Tech.Tree = _techTreeObject.GetComponent<Image>();
        _controller.Tech.Tree.fillAmount = 1f;
        _controller.Tech.Level = 0;
        _controller.Tech.SwitchOptionsTurn = 0;

        var switchOptions = typeof(Tech_CultureTreeController).GetMethod(
            "SwitchOptions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        switchOptions.Invoke(_controller, new object[] { _controller.Tech });

        Assert.AreEqual(1, _controller.Tech.Level);
        Assert.AreEqual(0f, _controller.Tech.AccumulatedPoints);
    }

    [Test]
    public void CultureLevelUp_AppliesBuildingEffectsOnlyWhenLevelAdvances()
    {
        _mockIcons.GetAllCultureIcon().Returns(new List<Sprite>(new Sprite[10]));

        GameObject altar = CreateBuilding(Enums.BulidingType.Altar);
        GameObject attackBuilding = CreateBuilding(Enums.BulidingType.AttackStatue);
        GameObject defenseBuilding = CreateBuilding(Enums.BulidingType.DefenseStatue);
        _mockPlayerModel.Index_AltarBuilding.Add(0, altar);
        _mockPlayerModel.Index_AttackBuilding.Add(0, attackBuilding);
        _mockPlayerModel.Index_DefenseBuilding.Add(0, defenseBuilding);

        _techTreeObject = new GameObject("CultureTree", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _controller.Culture.Tree = _techTreeObject.GetComponent<Image>();
        _controller.Culture.Level = 7;
        _controller.Culture.SwitchOptionsTurn = 0;

        _mockGameState.CurrentTurn.Returns(1);
        _controller.Culture.Tree.fillAmount = 1f;
        InvokeSwitchOptions(_controller.Culture);

        Assert.AreEqual(8, _controller.Culture.Level);
        Assert.AreEqual(0.7f, altar.GetComponent<BuildingController>().buildingData.AltarValue);

        altar.GetComponent<BuildingController>().buildingData.AltarValue = 0.5f;
        _controller.Culture.Tree.fillAmount = 1f;
        InvokeSwitchOptions(_controller.Culture);
        Assert.AreEqual(0.5f, altar.GetComponent<BuildingController>().buildingData.AltarValue);

        _mockGameState.CurrentTurn.Returns(2);
        _controller.Culture.Tree.fillAmount = 1f;
        InvokeSwitchOptions(_controller.Culture);

        Assert.AreEqual(9, _controller.Culture.Level);
        Assert.AreEqual(40f, attackBuilding.GetComponent<BuildingController>().buildingData.hp);
        Assert.AreEqual(40f, attackBuilding.GetComponent<BuildingController>().buildingData.currentHp);
        Assert.AreEqual(40f, defenseBuilding.GetComponent<BuildingController>().buildingData.hp);
        Assert.AreEqual(40f, defenseBuilding.GetComponent<BuildingController>().buildingData.currentHp);
        Assert.AreEqual(10f, altar.GetComponent<BuildingController>().buildingData.hp);
    }

    [Test]
    public void ApplyPlayerCultureBonus_ChangesRuntimeUnitWithoutChangingTemplate()
    {
        UnitData template = new UnitData(3, "Unit", 3, 20, 2, 15, 5, 2);
        CharacterData playerUnit = new CharacterData(3, null, null, template);
        CharacterData aiUnit = new CharacterData(3, null, null, template);
        _controller.Culture.Level = 5;

        _controller.ApplyPlayerCultureBonus(playerUnit);

        Assert.AreEqual(30, playerUnit.currentAttackValue);
        Assert.AreEqual(10, playerUnit.Defense);
        Assert.AreEqual(30, playerUnit.unitData.BasicAttackValue);
        Assert.AreEqual(10, playerUnit.unitData.Defense);
        Assert.AreEqual(15, template.BasicAttackValue);
        Assert.AreEqual(2, template.Defense);
        Assert.AreEqual(15, aiUnit.currentAttackValue);
        Assert.AreEqual(2, aiUnit.Defense);
        Assert.AreNotSame(template, playerUnit.unitData);
        Assert.AreNotSame(playerUnit.unitData, aiUnit.unitData);
    }

    [Test]
    public void ApplyPlayerCultureBonus_ChangesRuntimeBuildingWithoutChangingProviderBaseHp()
    {
        _mockBuildingData.GetBuildingBaseHP((int)Enums.BulidingType.AttackStatue).Returns(10f);
        BuildingData playerBuilding = new BuildingData(Enums.BulidingType.AttackStatue, _mockBuildingData);
        BuildingData aiBuilding = new BuildingData(Enums.BulidingType.AttackStatue, _mockBuildingData);
        _controller.Culture.Level = 9;

        _controller.ApplyPlayerCultureBonus(playerBuilding);

        Assert.AreEqual(40f, playerBuilding.hp);
        Assert.AreEqual(40f, playerBuilding.currentHp);
        Assert.AreEqual(10f, aiBuilding.hp);
        Assert.AreEqual(10f, _mockBuildingData.GetBuildingBaseHP((int)Enums.BulidingType.AttackStatue));
    }

    [Test]
    public void ApplyPlayerCultureBonus_LevelNineTargetsAttackAndDefenseOnly()
    {
        _mockBuildingData.GetBuildingBaseHP(Arg.Any<int>()).Returns(10f);
        BuildingData attack = new BuildingData(Enums.BulidingType.AttackStatue, _mockBuildingData);
        BuildingData defense = new BuildingData(Enums.BulidingType.DefenseStatue, _mockBuildingData);
        BuildingData altar = new BuildingData(Enums.BulidingType.Altar, _mockBuildingData);
        _controller.Culture.Level = 9;

        _controller.ApplyPlayerCultureBonus(attack);
        _controller.ApplyPlayerCultureBonus(defense);
        _controller.ApplyPlayerCultureBonus(altar);

        Assert.AreEqual(40f, attack.hp);
        Assert.AreEqual(40f, defense.hp);
        Assert.AreEqual(10f, altar.hp);
        Assert.AreEqual(0.7f, altar.AltarValue);
    }

    [Test]
    public void BuildingData_UsesExplicitCardDatabaseIdForBaseHp()
    {
        _mockBuildingData.GetBuildingBaseHP(0).Returns(25f);

        BuildingData building = new BuildingData(
            Enums.BulidingType.AttackStatue,
            _mockBuildingData,
            0);

        Assert.AreEqual(25f, building.hp);
        _mockBuildingData.Received(1).GetBuildingBaseHP(0);
        _mockBuildingData.DidNotReceive().GetBuildingBaseHP((int)Enums.BulidingType.AttackStatue);
    }

    private GameObject CreateBuilding(Enums.BulidingType type)
    {
        GameObject buildingObject = new GameObject(type.ToString());
        BuildingController controller = buildingObject.AddComponent<BuildingController>();
        controller.buildingData = new BuildingData(type, _mockBuildingData);
        _buildingObjects.Add(buildingObject);
        return buildingObject;
    }

    private void InvokeSwitchOptions(Tech_CultureTreeController.Tech_Culture item)
    {
        var switchOptions = typeof(Tech_CultureTreeController).GetMethod(
            "SwitchOptions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        switchOptions.Invoke(_controller, new object[] { item });
    }
}
