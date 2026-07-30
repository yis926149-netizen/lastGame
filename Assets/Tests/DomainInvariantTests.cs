using NUnit.Framework;
using NSubstitute;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class DomainInvariantTests
{
    private MapVisualEventSO _explorationMapVisualEvent;

    [TearDown]
    public void TearDown()
    {
        if (_explorationMapVisualEvent != null)
        {
            Object.DestroyImmediate(_explorationMapVisualEvent);
            _explorationMapVisualEvent = null;
        }
    }

    [Test]
    public void CompleteExploration_DuplicateCallback_SettlesEconomyOnce()
    {
        var wallet = new GoldWallet();
        wallet.InitPlayer(0);

        var costProvider = Substitute.For<IExplorationCostProvider>();
        costProvider.GetCost(Arg.Any<HexCellData>()).Returns(new ExplorationCost("Gold", 50));
        var rule = Substitute.For<IExplorationRule>();
        rule.IsValid(Arg.Any<HexCellData>()).Returns(true);
        var territory = Substitute.For<ITerritoryService>();
        var logistics = Substitute.For<ILogisticsService>();
        _explorationMapVisualEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();

        var service = new ExplorationService(
            costProvider,
            wallet,
            rule,
            _explorationMapVisualEvent,
            territory,
            wallet,
            logistics);
        var cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);
        int rewardEventCount = 0;
        service.ExplorationRewardTriggered += _ => rewardEventCount++;

        Assert.AreEqual(ExploreResult.Success, service.TryExplore(cell));
        service.CompleteExploration(cell);
        service.CompleteExploration(cell);

        Assert.AreEqual(55, wallet.Gold);
        Assert.AreEqual(1, rewardEventCount);
        territory.Received(1).Claim(cell);
        logistics.Received(1).RecalculateAll();
    }

    [TestCase(0, Enums.ResourceType.Animals)]
    [TestCase(1, Enums.ResourceType.Plants)]
    [TestCase(2, Enums.ResourceType.Minerals)]
    [TestCase(3, Enums.ResourceType.Chest)]
    [TestCase(4, Enums.ResourceType.None)]
    [TestCase(5, Enums.ResourceType.None)]
    [TestCase(17, Enums.ResourceType.None)]
    public void MapRandomResourceRoll_ExcludesHealthPack(int roll, Enums.ResourceType expected)
    {
        Assert.AreEqual(expected, MapGenerator.MapRandomResourceRoll(roll));
    }

    [Test]
    public void GenerateTerrainHeight_SameSeed_IsReproducible()
    {
        int[,] first = TerrainGenerator.GenerateTerrainHeight(7, 5, new System.Random(1234));
        int[,] second = TerrainGenerator.GenerateTerrainHeight(7, 5, new System.Random(1234));

        CollectionAssert.AreEqual(first, second);
    }

    [Test]
    public void OptimizeTerrain_UsesSixHexNeighborsAndPreservesTies()
    {
        int[,] map =
        {
            { 0, 2, 0 },
            { 1, 2, 1 },
            { 0, 2, 0 }
        };

        int[,] result = TerrainGenerator.OptimizeTerrain(3, 3, map);

        Assert.AreEqual(2, result[1, 1]);
    }

    [Test]
    public void Heal_FullHealth_DoesNotExceedMaximum()
    {
        CharacterData unit = CreateUnit();

        float healed = unit.Heal(25);

        Assert.AreEqual(0, healed);
        Assert.AreEqual(100, unit.currentHp);
    }

    [Test]
    public void Heal_MissingOneHp_ClampsToMaximum()
    {
        CharacterData unit = CreateUnit();
        unit.currentHp = 99;

        float healed = unit.Heal(25);

        Assert.AreEqual(1, healed);
        Assert.AreEqual(100, unit.currentHp);
    }

    [Test]
    public void CityIndexes_AreNotReusedAfterCityCountDecreases()
    {
        var playerObject = new GameObject("PlayerModelManagerTest");
        var enemyObject = new GameObject("EnemyModelManagerTest");
        try
        {
            var player = playerObject.AddComponent<PlayerModelManager>();
            var enemy = enemyObject.AddComponent<EnemyModelManager>();

            Assert.AreEqual(1, player.AllocateCityIndex());
            player.CityCount = 0;
            Assert.AreEqual(2, player.AllocateCityIndex());

            Assert.AreEqual(0, enemy.AllocateCityIndex(1));
            enemy.CityCount[1] = 0;
            Assert.AreEqual(1, enemy.AllocateCityIndex(1));
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(enemyObject);
        }
    }

    [Test]
    public void RebuildPlayerSphere_PreservesCellsOwnedByRemainingCity()
    {
        var managerObject = new GameObject("PlayerModelManagerTest");
        try
        {
            var manager = managerObject.AddComponent<PlayerModelManager>();
            var removedOnly = CreateCell(new Vector3(0, 0, 0));
            var overlap = CreateCell(new Vector3(1, -1, 0));
            var remainingOnly = CreateCell(new Vector3(2, -2, 0));
            removedOnly.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(-1, -1);
            overlap.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 1);
            remainingOnly.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 1);

            var mapData = Substitute.For<IMapDataService>();
            mapData.GetAllCells().Returns(new System.Collections.Generic.List<HexCellData>
            {
                removedOnly,
                overlap,
                remainingOnly
            });
            var container = new Zenject.DiContainer();
            container.Bind<IMapDataService>().FromInstance(mapData);
            container.Bind<IMeshGenerator>().FromInstance(Substitute.For<IMeshGenerator>());
            container.Inject(manager);

            manager.RebuildSphereOfInfluence();

            Assert.IsFalse(manager.SphereOfInfluence_HexC_HexCellData.ContainsKey(removedOnly.HexCoordinate));
            Assert.IsTrue(manager.SphereOfInfluence_HexC_HexCellData.ContainsKey(overlap.HexCoordinate));
            Assert.AreEqual(new System.Collections.Generic.KeyValuePair<int, int>(0, 1), overlap.Player_City_Index);
            Assert.IsTrue(manager.SphereOfInfluence_HexC_HexCellData.ContainsKey(remainingOnly.HexCoordinate));
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void CityDestroyed_MissingAttacker_AllowsAnotherCaptureAttempt()
    {
        var city = new GameObject("City");
        try
        {
            var controller = city.AddComponent<BuildingController>();
            controller.isCityChangeOwner = true;

            LogAssert.Expect(LogType.Warning, "[BuildingController] CityDestroyed aborted: attacker is missing.");
            controller.CityDestroyed();

            Assert.IsFalse(controller.isCityChangeOwner);
        }
        finally
        {
            Object.DestroyImmediate(city);
        }
    }

    [Test]
    public void SliderFillColor_UsesConfiguredFillRectInsteadOfChildOrder()
    {
        var root = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        var unrelated = new GameObject("Unrelated", typeof(RectTransform), typeof(Image));
        var fill = new GameObject("ConfiguredFill", typeof(RectTransform), typeof(Image));
        unrelated.transform.SetParent(root.transform);
        fill.transform.SetParent(root.transform);

        try
        {
            Slider slider = root.GetComponent<Slider>();
            slider.fillRect = fill.GetComponent<RectTransform>();

            Assert.IsTrue(UITool.TrySetSliderFillColor(slider, Color.red));
            Assert.AreEqual(Color.red, fill.GetComponent<Image>().color);
            Assert.AreNotEqual(Color.red, unrelated.GetComponent<Image>().color);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SphereExpansion_AppliesOwnerAndDoesNotClaimEnemyNeighbor()
    {
        var service = new HexMapService();
        var center = CreateCell(Vector3.zero);
        var friendlyNeighbor = CreateCell(new Vector3(0, -1, 1));
        var enemyNeighbor = CreateCell(new Vector3(1, -1, 0));
        enemyNeighbor.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(2, 0);
        var cells = new System.Collections.Generic.Dictionary<Vector3, HexCellData>
        {
            [center.HexCoordinate] = center,
            [friendlyNeighbor.HexCoordinate] = friendlyNeighbor,
            [enemyNeighbor.HexCoordinate] = enemyNeighbor
        };
        service.Initialize(
            cells,
            new System.Collections.Generic.Dictionary<int, HexCellData>(),
            new System.Collections.Generic.List<Vector3>(),
            new System.Collections.Generic.Dictionary<Vector3, Vector3>(),
            null,
            new Vector3[0],
            null,
            null,
            null);
        var sphere = new System.Collections.Generic.Dictionary<Vector3, HexCellData>();
        var owner = new System.Collections.Generic.KeyValuePair<int, int>(1, 4);

        SphereOfInfluenceRules.Expand(service, center.HexCoordinate, sphere, owner);

        Assert.AreEqual(owner, center.Player_City_Index);
        Assert.AreEqual(owner, friendlyNeighbor.Player_City_Index);
        Assert.AreEqual(new System.Collections.Generic.KeyValuePair<int, int>(2, 0), enemyNeighbor.Player_City_Index);
        Assert.IsFalse(sphere.ContainsKey(enemyNeighbor.HexCoordinate));
    }

    [TestCase(false, false, 0, 0, EndGameResult.None)]
    [TestCase(true, false, 0, 1, EndGameResult.None)]
    [TestCase(true, false, 0, 0, EndGameResult.Victory)]
    [TestCase(true, true, 1, 0, EndGameResult.Victory)]
    [TestCase(true, true, 0, 1, EndGameResult.Defeat)]
    [TestCase(true, true, 0, 0, EndGameResult.Draw)]
    public void EndGameResult_HandlesInitializationAndCityBoundaries(
        bool initialized,
        bool playerHasOwnedCity,
        int playerCities,
        int aiCities,
        EndGameResult expected)
    {
        Assert.AreEqual(expected, EndGame.EvaluateResult(initialized, playerHasOwnedCity, playerCities, aiCities));
    }

    [TestCase(100, 100, EndGameResult.None)]
    [TestCase(0, 100, EndGameResult.Defeat)]
    [TestCase(100, 0, EndGameResult.Victory)]
    [TestCase(0, 0, EndGameResult.Draw)]
    public void EndGameResult_UsesMainCityHealth(float playerHp, float aiHp, EndGameResult expected)
    {
        Assert.AreEqual(expected, EndGame.EvaluateMainCityHealth(playerHp, aiHp));
    }

    private static CharacterData CreateUnit()
    {
        var unitData = new UnitData(0, "Test Unit", 3, 100, 1, 10, 3, 2);
        return new CharacterData(0, null, null, unitData);
    }

    private static HexCellData CreateCell(Vector3 coordinate)
    {
        return new HexCellData(Enums.HexType.NoRiver, 0, coordinate, Vector3.zero, 1);
    }
}

public class LogisticsServiceTests
{
    [Test]
    public void RecalculateAll_ConnectsOnlyContinuousOwnedCells()
    {
        var map = new HexMapService();
        HexCellData root = CreateLogisticsCell(Vector3.zero, 0);
        HexCellData connected = CreateLogisticsCell(new Vector3(0, -1, 1), 0);
        HexCellData blocked = CreateLogisticsCell(new Vector3(0, -2, 2), 1);
        HexCellData isolated = CreateLogisticsCell(new Vector3(0, -3, 3), 0);
        InitializeLogisticsMap(map, root, connected, blocked, isolated);
        var service = new LogisticsService(map);
        service.RegisterMainCity(0, root);

        service.RecalculateAll();

        Assert.IsTrue(service.IsLogisticsConnected(root, 0));
        Assert.IsTrue(service.IsLogisticsConnected(connected, 0));
        Assert.IsFalse(service.IsLogisticsConnected(blocked, 0));
        Assert.IsFalse(service.IsLogisticsConnected(isolated, 0));
    }

    [Test]
    public void TransferOwner_RecalculatesBothFactions()
    {
        var map = new HexMapService();
        HexCellData playerRoot = CreateLogisticsCell(Vector3.zero, 0);
        HexCellData bridge = CreateLogisticsCell(new Vector3(0, -1, 1), 1);
        HexCellData playerRear = CreateLogisticsCell(new Vector3(0, -2, 2), 0);
        HexCellData aiRoot = CreateLogisticsCell(new Vector3(0, -3, 3), 1);
        InitializeLogisticsMap(map, playerRoot, bridge, playerRear, aiRoot);
        var service = new LogisticsService(map);
        service.RegisterMainCity(0, playerRoot);
        service.RegisterMainCity(1, aiRoot);
        service.RecalculateAll();

        service.TransferOwner(bridge, 0);

        Assert.IsTrue(service.IsLogisticsConnected(playerRear, 0));
        Assert.IsFalse(service.IsLogisticsConnected(bridge, 1));
    }

    [Test]
    public void IsVisibleToFaction_OwnedCellUsesOwnerSupplyForEveryViewer()
    {
        var map = new HexMapService();
        HexCellData root = CreateLogisticsCell(Vector3.zero, 1);
        InitializeLogisticsMap(map, root);
        var service = new LogisticsService(map);
        service.RegisterMainCity(1, root);
        service.RecalculateAll();

        Assert.IsFalse(service.IsVisibleToFaction(root, 0));

        root.ExploreBy(1);

        Assert.IsTrue(service.IsVisibleToFaction(root, 0));
        Assert.IsTrue(service.IsVisibleToFaction(root, 1));
    }

    [Test]
    public void IsVisibleToFaction_AiTerritoryBecomesHiddenForAllViewersWhenCutOff()
    {
        var map = new HexMapService();
        HexCellData aiRoot = CreateLogisticsCell(Vector3.zero, 1);
        HexCellData bridge = CreateLogisticsCell(new Vector3(0, -1, 1), 1);
        HexCellData rear = CreateLogisticsCell(new Vector3(0, -2, 2), 1);
        aiRoot.ExploreBy(1);
        bridge.ExploreBy(1);
        rear.ExploreBy(1);
        InitializeLogisticsMap(map, aiRoot, bridge, rear);
        var service = new LogisticsService(map);
        service.RegisterMainCity(1, aiRoot);
        service.RecalculateAll();
        Assert.IsTrue(service.IsVisibleToFaction(rear, 0));
        Assert.IsTrue(service.IsVisibleToFaction(rear, 1));

        bridge.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);
        service.RecalculateAll();

        Assert.IsFalse(service.IsVisibleToFaction(rear, 0));
        Assert.IsFalse(service.IsVisibleToFaction(rear, 1));
    }

    [Test]
    public void IsVisibleToFaction_NeutralCellUsesViewerDiscoveryOnly()
    {
        var map = new HexMapService();
        HexCellData neutral = CreateLogisticsCell(Vector3.zero, -1);
        neutral.ExploreBy(1);
        InitializeLogisticsMap(map, neutral);
        var service = new LogisticsService(map);

        Assert.IsFalse(service.IsVisibleToFaction(neutral, 0));
        Assert.IsTrue(service.IsVisibleToFaction(neutral, 1));
    }

    [Test]
    public void RecalculateAll_InvalidRootClearsPreviousCache()
    {
        var map = new HexMapService();
        HexCellData root = CreateLogisticsCell(Vector3.zero, 0);
        InitializeLogisticsMap(map, root);
        var service = new LogisticsService(map);
        service.RegisterMainCity(0, root);
        service.RecalculateAll();
        Assert.IsTrue(service.IsLogisticsConnected(root, 0));

        root.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(1, 0);
        service.RecalculateAll();

        Assert.IsFalse(service.IsLogisticsConnected(root, 0));
    }

    private static HexCellData CreateLogisticsCell(Vector3 coordinate, int owner)
    {
        var cell = new HexCellData(Enums.HexType.NoRiver, 0, coordinate, Vector3.zero, 1f);
        cell.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(owner, 0);
        return cell;
    }

    private static void InitializeLogisticsMap(HexMapService map, params HexCellData[] cells)
    {
        var byCoordinate = new System.Collections.Generic.Dictionary<Vector3, HexCellData>();
        var byOrder = new System.Collections.Generic.Dictionary<int, HexCellData>();
        for (int index = 0; index < cells.Length; index++)
        {
            byCoordinate[cells[index].HexCoordinate] = cells[index];
            byOrder[index] = cells[index];
        }

        map.Initialize(
            byCoordinate,
            byOrder,
            new System.Collections.Generic.List<Vector3>(),
            new System.Collections.Generic.Dictionary<Vector3, Vector3>(),
            null,
            new Vector3[0],
            null,
            null,
            null);
    }
}
