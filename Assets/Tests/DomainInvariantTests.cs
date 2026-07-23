using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class DomainInvariantTests
{
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

            Assert.AreEqual(0, player.AllocateCityIndex());
            player.CityCount = 0;
            Assert.AreEqual(1, player.AllocateCityIndex());

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

            manager.SingleCity_SphereOfInfluence_HexC_HexCellData[0] = new System.Collections.Generic.Dictionary<Vector3, HexCellData>
            {
                [removedOnly.HexCoordinate] = removedOnly,
                [overlap.HexCoordinate] = overlap
            };
            manager.SingleCity_SphereOfInfluence_HexC_HexCellData[1] = new System.Collections.Generic.Dictionary<Vector3, HexCellData>
            {
                [overlap.HexCoordinate] = overlap,
                [remainingOnly.HexCoordinate] = remainingOnly
            };

            manager.SingleCity_SphereOfInfluence_HexC_HexCellData.Remove(0);
            removedOnly.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(-1, -1);
            overlap.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(-1, -1);
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
