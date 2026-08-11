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
        rule.IsValid(Arg.Any<HexCellData>(), Arg.Any<int>()).Returns(true);
        var territory = Substitute.For<ITerritoryService>();
        var logistics = Substitute.For<ILogisticsService>();
        _explorationMapVisualEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();

        // 【地图资源配置化】基础奖励来自数据库配置（此处 5）
        var database = ScriptableObject.CreateInstance<MapResourceDatabaseSO>();
        database.baseExplorationGold = 5;
        var collection = new MapResourceCollectionService(wallet, null, database);

        var service = new ExplorationService(
            costProvider,
            wallet,
            rule,
            _explorationMapVisualEvent,
            territory,
            logistics,
            collection);
        var cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);
        int rewardEventCount = 0;
        service.ExplorationRewardTriggered += (_, _) => rewardEventCount++;

        Assert.AreEqual(ExploreResult.Success, service.TryExplore(cell, 0));
        service.CompleteExploration(cell);
        service.CompleteExploration(cell);

        Assert.AreEqual(55, wallet.Gold);
        Assert.AreEqual(1, rewardEventCount);
        territory.Received(1).Claim(cell);
        logistics.Received(1).RecalculateAll();

        Object.DestroyImmediate(database);
    }

    // ── 【地图资源配置化】生成权重与奖励规则 ──────────────
    [Test]
    public void ResourceSpawnRule_WeightedRoll_MatchesLegacyDistribution()
    {
        var database = CreateResourceDatabase(emptyWeight: 14);
        var animals = AddResource(database, "Animals", 1);
        var plants = AddResource(database, "Plants", 1);
        var minerals = AddResource(database, "Minerals", 1);
        var chest = AddResource(database, "Chest", 1);

        try
        {
            // 掷点 0~13 → 空白；14~17 → 四种资源（复现改造前 Next(0,18)/roll<4 的 4/18 分布）
            for (int roll = 0; roll < 14; roll++)
                Assert.IsNull(ResourceSpawnRule.RollResource(database, roll), $"roll={roll} 应为空白");
            Assert.AreSame(animals, ResourceSpawnRule.RollResource(database, 14));
            Assert.AreSame(plants, ResourceSpawnRule.RollResource(database, 15));
            Assert.AreSame(minerals, ResourceSpawnRule.RollResource(database, 16));
            Assert.AreSame(chest, ResourceSpawnRule.RollResource(database, 17));
            Assert.AreEqual(18, ResourceSpawnRule.TotalWeight(database));
        }
        finally
        {
            DestroyResourceDatabase(database);
        }
    }

    [Test]
    public void ResourceSpawnRule_ZeroTotalWeight_ReturnsNull()
    {
        var database = CreateResourceDatabase(emptyWeight: 0);

        try
        {
            Assert.AreEqual(0, ResourceSpawnRule.TotalWeight(database));
            Assert.IsNull(ResourceSpawnRule.RollResource(database, new System.Random(1)));
        }
        finally
        {
            Object.DestroyImmediate(database);
        }
    }

    [Test]
    public void ResourceRewardRule_ComputesBaseAndBonus()
    {
        var resource = ScriptableObject.CreateInstance<MapResourceSO>();
        resource.explorationGoldBonus = 20;

        try
        {
            Assert.AreEqual(5, ResourceRewardRule.ComputeExplorationReward(5, null));
            Assert.AreEqual(25, ResourceRewardRule.ComputeExplorationReward(5, resource));
            Assert.AreEqual(0, ResourceRewardRule.ComputeExplorationReward(0, null));
        }
        finally
        {
            Object.DestroyImmediate(resource);
        }
    }

    [Test]
    public void HarvestForGold_ConsumesResourceOnce()
    {
        var wallet = new GoldWallet();
        wallet.InitPlayer(0);
        var database = CreateResourceDatabase(emptyWeight: 14);
        database.baseExplorationGold = 5;
        var chest = AddResource(database, "Chest", 1);
        chest.explorationGoldBonus = 30;
        var collection = new MapResourceCollectionService(wallet, null, database);

        var cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);
        cell.resource = chest;

        try
        {
            int first = collection.HarvestForGold(cell, 0);
            int second = collection.HarvestForGold(cell, 0);

            Assert.AreEqual(35, first); // 基础 5 + 宝箱 30
            Assert.AreEqual(5, second); // 资源已原子消费，第二次只有基础奖励
            Assert.AreEqual(40, wallet.Gold); // 100 + 35 + 5
        }
        finally
        {
            DestroyResourceDatabase(database);
        }
    }

    [Test]
    public void TryCollectForUnit_AppliesConfiguredEffects()
    {
        var wallet = new GoldWallet();
        wallet.InitPlayer(0);
        var database = CreateResourceDatabase(emptyWeight: 14);
        var collection = new MapResourceCollectionService(wallet, null, database);

        var animals = AddResource(database, "Animals", 1);
        animals.pickupEffectType = ResourcePickupEffectType.AttackBoost;
        animals.pickupEffect.attackBonus = 0.7f;

        var minerals = AddResource(database, "Minerals", 1);
        minerals.pickupEffectType = ResourcePickupEffectType.DefenseBoost;
        minerals.pickupEffect.defenseBonus = 0.25f;

        var chest = AddResource(database, "Chest", 1);
        chest.pickupEffectType = ResourcePickupEffectType.Gold;
        chest.pickupEffect.goldAmount = 50;

        var unitData = new UnitData(0, "Test", 3, 100, 1, 10, 3, 2);
        var character = new CharacterData(0, null, null, unitData);

        try
        {
            var animalCell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);
            animalCell.resource = animals;
            Assert.IsTrue(collection.TryCollectForUnit(animalCell, character, 0));
            Assert.AreEqual(0.7f, character.Resource_Animals);
            Assert.IsNull(animalCell.resource); // 原子消费
            Assert.IsFalse(collection.TryCollectForUnit(animalCell, character, 0)); // 第二次无资源

            var mineralCell = new HexCellData(Enums.HexType.NoRiver, 1, Vector3.one, Vector3.one, 1f);
            mineralCell.resource = minerals;
            collection.TryCollectForUnit(mineralCell, character, 0);
            Assert.AreEqual(0.25f, character.Resource_Minerals);

            // 宝箱：玩家 +50；AI（阵营 1）无效（与改造前 PlayerIndex==0 行为一致）
            character.currentHp = 50;
            var plantCell = new HexCellData(Enums.HexType.NoRiver, 2, Vector3.one * 2, Vector3.one * 2, 1f);
            var plants = AddResource(database, "Plants", 1);
            plants.pickupEffectType = ResourcePickupEffectType.Heal;
            plants.pickupEffect.healRatio = 0.25f;
            plantCell.resource = plants;
            collection.TryCollectForUnit(plantCell, character, 0);
            Assert.AreEqual(75, character.currentHp); // 50 + 25% * 100

            var chestCell = new HexCellData(Enums.HexType.NoRiver, 3, Vector3.one * 3, Vector3.one * 3, 1f);
            chestCell.resource = chest;
            collection.TryCollectForUnit(chestCell, character, 0);
            Assert.AreEqual(50, wallet.Gold);

            var aiChestCell = new HexCellData(Enums.HexType.NoRiver, 4, Vector3.one * 4, Vector3.one * 4, 1f);
            aiChestCell.resource = chest;
            collection.TryCollectForUnit(aiChestCell, character, 1);
            Assert.AreEqual(50, wallet.Gold); // AI 拾取宝箱不加金币
        }
        finally
        {
            DestroyResourceDatabase(database);
        }
    }

    private static MapResourceDatabaseSO CreateResourceDatabase(int emptyWeight)
    {
        var database = ScriptableObject.CreateInstance<MapResourceDatabaseSO>();
        database.emptySpawnWeight = emptyWeight;
        return database;
    }

    private static MapResourceSO AddResource(MapResourceDatabaseSO database, string id, int weight)
    {
        var resource = ScriptableObject.CreateInstance<MapResourceSO>();
        resource.resourceId = id;
        resource.spawnWeight = weight;
        database.resources.Add(resource);
        return resource;
    }

    private static void DestroyResourceDatabase(MapResourceDatabaseSO database)
    {
        if (database != null)
        {
            if (database.resources != null)
            {
                foreach (var r in database.resources)
                {
                    if (r != null) Object.DestroyImmediate(r);
                }
            }
            Object.DestroyImmediate(database);
        }
    }

    // ── 【地图地貌配置化】生成权重与效果规则 ──────────────
    [Test]
    public void LandFormSpawnRule_WeightedRoll_MatchesLegacyDistribution()
    {
        var database = CreateLandFormDatabase(emptyWeight: 10);
        var forest = AddLandForm(database, "Forest", 1);
        var stone = AddLandForm(database, "Stone", 1);
        var bigBones = AddLandForm(database, "BigBones", 1);
        var fromLand = AddLandForm(database, "FromLand", 1);

        try
        {
            // 锁定旧固定种子映射：0~3 依次为四种地貌，4~13 为空白（旧代码 Next(0,14)+Clamp）
            Assert.AreSame(forest, LandFormSpawnRule.RollLandForm(database, 0));
            Assert.AreSame(stone, LandFormSpawnRule.RollLandForm(database, 1));
            Assert.AreSame(bigBones, LandFormSpawnRule.RollLandForm(database, 2));
            Assert.AreSame(fromLand, LandFormSpawnRule.RollLandForm(database, 3));
            for (int roll = 4; roll < 14; roll++)
                Assert.IsNull(LandFormSpawnRule.RollLandForm(database, roll), $"roll={roll} 应为空白");
            Assert.AreEqual(14, LandFormSpawnRule.TotalWeight(database));
        }
        finally
        {
            DestroyLandFormDatabase(database);
        }
    }

    [Test]
    public void LandFormEffectRule_None_HasNoEffect()
    {
        var forest = ScriptableObject.CreateInstance<MapLandFormSO>();
        forest.effectType = LandFormEffectType.None;
        forest.effect.defenseBonus = 99f; // 残留数值不应产生效果

        try
        {
            Assert.AreEqual(0f, LandFormEffectRule.GetDefenseBonus(forest));
            Assert.IsFalse(LandFormEffectRule.TryGetPeriodicHeal(forest, out _, out _));
            Assert.AreEqual(0f, LandFormEffectRule.GetDefenseBonus(null));
        }
        finally
        {
            Object.DestroyImmediate(forest);
        }
    }

    [Test]
    public void LandFormEffectRule_DefenseBonus_UsesConfiguredValue()
    {
        var bigBones = ScriptableObject.CreateInstance<MapLandFormSO>();
        bigBones.effectType = LandFormEffectType.DefenseBonus;
        bigBones.effect.defenseBonus = 0.3f;

        try
        {
            Assert.AreEqual(0.3f, LandFormEffectRule.GetDefenseBonus(bigBones));
            Assert.IsFalse(LandFormEffectRule.TryGetPeriodicHeal(bigBones, out _, out _));
        }
        finally
        {
            Object.DestroyImmediate(bigBones);
        }
    }

    [Test]
    public void LandFormEffectRule_PeriodicHeal_UsesConfiguredValues()
    {
        var fromLand = ScriptableObject.CreateInstance<MapLandFormSO>();
        fromLand.effectType = LandFormEffectType.PeriodicHeal;
        fromLand.effect.healRatio = 0.1f;
        fromLand.effect.healInterval = 5f;

        try
        {
            Assert.AreEqual(0f, LandFormEffectRule.GetDefenseBonus(fromLand));
            Assert.IsTrue(LandFormEffectRule.TryGetPeriodicHeal(fromLand, out float ratio, out float interval));
            Assert.AreEqual(0.1f, ratio);
            Assert.AreEqual(5f, interval);
        }
        finally
        {
            Object.DestroyImmediate(fromLand);
        }
    }

    [Test]
    public void LandFormEffectRule_GoldIncomeBoost_UsesConfiguredValue()
    {
        var goldMine = ScriptableObject.CreateInstance<MapLandFormSO>();
        goldMine.effectType = LandFormEffectType.GoldIncomeBoost;
        goldMine.effect.goldIncomePerSecond = 2f;

        try
        {
            Assert.IsTrue(LandFormEffectRule.TryGetGoldIncomeBonus(goldMine, out float bonus));
            Assert.AreEqual(2f, bonus);
            Assert.AreEqual(0f, LandFormEffectRule.GetDefenseBonus(goldMine));

            // 非金矿地貌无加成
            var forest = ScriptableObject.CreateInstance<MapLandFormSO>();
            forest.effectType = LandFormEffectType.None;
            Assert.IsFalse(LandFormEffectRule.TryGetGoldIncomeBonus(forest, out _));
            Object.DestroyImmediate(forest);
        }
        finally
        {
            Object.DestroyImmediate(goldMine);
        }
    }

    [Test]
    public void LandFormEffectRule_SumGoldIncomeBonus_CountsOwnedMinesOnly()
    {
        var goldMine = ScriptableObject.CreateInstance<MapLandFormSO>();
        goldMine.effectType = LandFormEffectType.GoldIncomeBoost;
        goldMine.effect.goldIncomePerSecond = 2f;

        try
        {
            // 玩家占领 2 个金矿格 + 1 个普通格；AI 占领 1 个金矿格
            var playerMineA = CreateCell(new Vector3(0, 0, 0));
            playerMineA.landForm = goldMine;
            playerMineA.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);

            var playerMineB = CreateCell(new Vector3(1, -1, 0));
            playerMineB.landForm = goldMine;
            playerMineB.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);

            var plain = CreateCell(new Vector3(2, -2, 0));
            plain.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);

            var aiMine = CreateCell(new Vector3(3, -3, 0));
            aiMine.landForm = goldMine;
            aiMine.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(1, 0);

            // 金矿但未占领（中立）不计入
            var neutralMine = CreateCell(new Vector3(4, -4, 0));
            neutralMine.landForm = goldMine;

            var cells = new System.Collections.Generic.List<HexCellData>
            {
                playerMineA, playerMineB, plain, aiMine, neutralMine
            };

            Assert.AreEqual(4f, LandFormEffectRule.SumGoldIncomeBonus(cells, 0)); // 2 格 × 2
            Assert.AreEqual(2f, LandFormEffectRule.SumGoldIncomeBonus(cells, 1)); // 1 格 × 2
        }
        finally
        {
            Object.DestroyImmediate(goldMine);
        }
    }

    [Test]
    public void GoldIncomeService_GetIncomePerTick_IncludesOwnedGoldMine()
    {
        var goldMine = ScriptableObject.CreateInstance<MapLandFormSO>();
        goldMine.effectType = LandFormEffectType.GoldIncomeBoost;
        goldMine.effect.goldIncomePerSecond = 2f;

        try
        {
            var playerMine = CreateCell(Vector3.zero);
            playerMine.landForm = goldMine;
            playerMine.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);

            var map = Substitute.For<IMapDataService>();
            map.GetAllCells().Returns(new System.Collections.Generic.List<HexCellData> { playerMine });

            var buffs = Substitute.For<IFactionBuffService>();
            buffs.GetStatMultiplier(Arg.Any<int>(), "gold").Returns(1f);

            var wallet = new GoldWallet { PassiveIncomePerTick = 2 };
            // 【断供方案-阶段6.5】金矿加成需"归属 + 后勤畅通"：把金矿格注册为玩家主城 → 连通 → 计加成
            var logistics = new LogisticsService(map);
            logistics.RegisterMainCity(0, playerMine);
            var income = new GoldIncomeService(wallet, buffs, null, map, logistics);

            Assert.AreEqual(4, income.GetIncomePerTick(0)); // 基础 2 + 金矿 2
            Assert.AreEqual(2, income.GetIncomePerTick(1)); // AI 未占领金矿
        }
        finally
        {
            Object.DestroyImmediate(goldMine);
        }
    }

    [Test]
    public void GoldIncomeService_GetIncomePerTick_CutOffGoldMinePauses()
    {
        var goldMine = ScriptableObject.CreateInstance<MapLandFormSO>();
        goldMine.effectType = LandFormEffectType.GoldIncomeBoost;
        goldMine.effect.goldIncomePerSecond = 2f;

        try
        {
            var playerMine = CreateCell(Vector3.zero);
            playerMine.landForm = goldMine;
            playerMine.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);

            // 主城与金矿格不相邻 → 金矿断供 → 暂停产金
            var mainCity = CreateCell(new Vector3(10, -10, 0));
            mainCity.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);

            var map = Substitute.For<IMapDataService>();
            map.GetAllCells().Returns(new System.Collections.Generic.List<HexCellData> { playerMine, mainCity });

            var buffs = Substitute.For<IFactionBuffService>();
            buffs.GetStatMultiplier(Arg.Any<int>(), "gold").Returns(1f);

            var wallet = new GoldWallet { PassiveIncomePerTick = 2 };
            var logistics = new LogisticsService(map);
            logistics.RegisterMainCity(0, mainCity);
            var income = new GoldIncomeService(wallet, buffs, null, map, logistics);

            // 断供金矿不计加成；恢复连通后重新计入（连通语义由后勤 BFS 保证，此处直接断言断供态）
            Assert.AreEqual(2, income.GetIncomePerTick(0));
        }
        finally
        {
            Object.DestroyImmediate(goldMine);
        }
    }

    [Test]
    public void BuildingIncomeRule_SumGoldMineIncome_CountsOwnedLivingMinesOnly()
    {
        var database = ScriptableObject.CreateInstance<BuildingDatabaseSO>();
        var config = ScriptableObject.CreateInstance<BuildingConfigSO>();
        var playerMineObject = new GameObject("Player Gold Mine");
        var aiMineObject = new GameObject("AI Gold Mine");

        try
        {
            config.buildingType = Enums.BulidingType.GoldMine;
            config.goldIncomePerSecond = 1f;
            database.buildings.Add(config);

            var playerMine = CreateCell(Vector3.zero);
            playerMine.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);
            playerMine.BulidingTypeOnHex_Building =
                new System.Collections.Generic.KeyValuePair<Enums.BulidingType, GameObject>(
                    Enums.BulidingType.GoldMine, playerMineObject);

            var aiMine = CreateCell(new Vector3(1, -1, 0));
            aiMine.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(1, 0);
            aiMine.BulidingTypeOnHex_Building =
                new System.Collections.Generic.KeyValuePair<Enums.BulidingType, GameObject>(
                    Enums.BulidingType.GoldMine, aiMineObject);

            var destroyedMine = CreateCell(new Vector3(2, -2, 0));
            destroyedMine.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);
            destroyedMine.BulidingTypeOnHex_Building =
                new System.Collections.Generic.KeyValuePair<Enums.BulidingType, GameObject>(
                    Enums.BulidingType.GoldMine, null);

            var cells = new System.Collections.Generic.List<HexCellData>
            {
                playerMine, aiMine, destroyedMine
            };

            Assert.AreEqual(1f, BuildingIncomeRule.SumGoldMineIncome(cells, 0, database));
            Assert.AreEqual(1f, BuildingIncomeRule.SumGoldMineIncome(cells, 1, database));
        }
        finally
        {
            Object.DestroyImmediate(playerMineObject);
            Object.DestroyImmediate(aiMineObject);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(database);
        }
    }

    [Test]
    public void GoldIncomeService_GetIncomePerTick_CombinesLandformAndBuildingMines()
    {
        var landform = ScriptableObject.CreateInstance<MapLandFormSO>();
        var database = ScriptableObject.CreateInstance<BuildingDatabaseSO>();
        var config = ScriptableObject.CreateInstance<BuildingConfigSO>();
        var buildingObject = new GameObject("Gold Mine");

        try
        {
            landform.effectType = LandFormEffectType.GoldIncomeBoost;
            landform.effect.goldIncomePerSecond = 2f;
            config.buildingType = Enums.BulidingType.GoldMine;
            config.goldIncomePerSecond = 1f;
            database.buildings.Add(config);

            var mine = CreateCell(Vector3.zero);
            mine.landForm = landform;
            mine.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);
            mine.BulidingTypeOnHex_Building =
                new System.Collections.Generic.KeyValuePair<Enums.BulidingType, GameObject>(
                    Enums.BulidingType.GoldMine, buildingObject);

            var map = Substitute.For<IMapDataService>();
            map.GetAllCells().Returns(new System.Collections.Generic.List<HexCellData> { mine });

            var buffs = Substitute.For<IFactionBuffService>();
            buffs.GetStatMultiplier(0, "gold").Returns(2f);

            var wallet = new GoldWallet { PassiveIncomePerTick = 2 };
            var logistics = new LogisticsService(map);
            logistics.RegisterMainCity(0, mine);
            var income = new GoldIncomeService(wallet, buffs, null, map, logistics, database);

            Assert.AreEqual(10, income.GetIncomePerTick(0));
        }
        finally
        {
            Object.DestroyImmediate(buildingObject);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(database);
            Object.DestroyImmediate(landform);
        }
    }

    [Test]
    public void BuildingIncomeRule_SumGoldMineIncome_CutOffMinePauses()
    {
        var database = ScriptableObject.CreateInstance<BuildingDatabaseSO>();
        var config = ScriptableObject.CreateInstance<BuildingConfigSO>();
        var buildingObject = new GameObject("Cut Off Gold Mine");

        try
        {
            config.buildingType = Enums.BulidingType.GoldMine;
            config.goldIncomePerSecond = 1f;
            database.buildings.Add(config);

            var mine = CreateCell(Vector3.zero);
            mine.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);
            mine.BulidingTypeOnHex_Building =
                new System.Collections.Generic.KeyValuePair<Enums.BulidingType, GameObject>(
                    Enums.BulidingType.GoldMine, buildingObject);

            var mainCity = CreateCell(new Vector3(10, -10, 0));
            mainCity.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);

            var cells = new System.Collections.Generic.List<HexCellData> { mine, mainCity };
            var map = Substitute.For<IMapDataService>();
            map.GetAllCells().Returns(cells);

            var logistics = new LogisticsService(map);
            logistics.RegisterMainCity(0, mainCity);

            Assert.AreEqual(0f, BuildingIncomeRule.SumGoldMineIncome(cells, 0, database, logistics));
        }
        finally
        {
            Object.DestroyImmediate(buildingObject);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(database);
        }
    }

    private static MapLandFormDatabaseSO CreateLandFormDatabase(int emptyWeight)
    {
        var database = ScriptableObject.CreateInstance<MapLandFormDatabaseSO>();
        database.emptySpawnWeight = emptyWeight;
        return database;
    }

    private static MapLandFormSO AddLandForm(MapLandFormDatabaseSO database, string id, int weight)
    {
        var landForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        landForm.landFormId = id;
        landForm.spawnWeight = weight;
        database.landForms.Add(landForm);
        return landForm;
    }

    private static void DestroyLandFormDatabase(MapLandFormDatabaseSO database)
    {
        if (database != null)
        {
            if (database.landForms != null)
            {
                foreach (var f in database.landForms)
                {
                    if (f != null) Object.DestroyImmediate(f);
                }
            }
            Object.DestroyImmediate(database);
        }
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
            new Vector3[0]);
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
        // 【断供方案】断供区与玩家网络相邻会被立即吞并，故用中立格隔开以测试"断供隐藏"
        HexCellData neutral = CreateLogisticsCell(new Vector3(0, -2, 2), -1);
        HexCellData rear = CreateLogisticsCell(new Vector3(0, -3, 3), 1);
        aiRoot.ExploreBy(1);
        bridge.ExploreBy(1);
        neutral.ExploreBy(1);
        rear.ExploreBy(1);
        InitializeLogisticsMap(map, aiRoot, bridge, neutral, rear);
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

    // ══════════════ 断供方案-阶段4：区域吞并 ══════════════

    [Test]
    public void RecalculateAll_AnnexesUnsuppliedRegionAdjacentToOtherNetwork()
    {
        var map = new HexMapService();
        HexCellData playerRoot = CreateLogisticsCell(Vector3.zero, 0);
        HexCellData bridge = CreateLogisticsCell(new Vector3(0, -1, 1), 1);
        HexCellData aiRear = CreateLogisticsCell(new Vector3(0, -2, 2), 1);
        HexCellData aiFarRear = CreateLogisticsCell(new Vector3(0, -3, 3), 1);
        InitializeLogisticsMap(map, playerRoot, bridge, aiRear, aiFarRear);
        var service = new LogisticsService(map);
        service.RegisterMainCity(0, playerRoot);
        service.RegisterMainCity(1, bridge);
        service.RecalculateAll();

        // 切桥：bridge 被玩家占领，aiRear/aiFarRear 断供且与玩家网络相邻 → 整区域吞并
        bridge.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);
        service.RecalculateAll();

        Assert.IsTrue(service.IsOwnedBy(aiRear, 0));
        Assert.IsTrue(service.IsOwnedBy(aiFarRear, 0));
        Assert.IsTrue(service.IsLogisticsConnected(aiRear, 0));
        // 吞并自动写入探索 → 对新主双方可见
        Assert.IsTrue(service.IsVisibleToFaction(aiRear, 0));
        Assert.IsTrue(service.IsVisibleToFaction(aiRear, 1));
    }

    [Test]
    public void RecalculateAll_DoesNotAnnexRegionWithoutAdjacencyToNetwork()
    {
        var map = new HexMapService();
        HexCellData playerRoot = CreateLogisticsCell(Vector3.zero, 0);
        HexCellData neutral = CreateLogisticsCell(new Vector3(0, -1, 1), -1);
        HexCellData aiRear = CreateLogisticsCell(new Vector3(0, -2, 2), 1);
        HexCellData aiRoot = CreateLogisticsCell(new Vector3(0, -4, 4), 1);
        InitializeLogisticsMap(map, playerRoot, neutral, aiRear, aiRoot);
        var service = new LogisticsService(map);
        service.RegisterMainCity(0, playerRoot);
        service.RegisterMainCity(1, aiRoot);
        service.RecalculateAll();

        // aiRear 断供（与 aiRoot 不相邻、与玩家网络间隔中立格）→ 不吞并
        Assert.IsFalse(service.IsOwnedBy(aiRear, 0));
        Assert.IsTrue(service.IsOwnedBy(aiRear, 1));
        Assert.IsFalse(service.IsLogisticsConnected(aiRear, 1));
    }

    [Test]
    public void RecalculateAll_ExemptsPseudoFactionCellsFromAnnexation()
    {
        var map = new HexMapService();
        HexCellData playerRoot = CreateLogisticsCell(Vector3.zero, 0);
        // 中立公共建筑伪阵营（Key = 2），紧邻玩家网络
        HexCellData publicBuildingCell = CreateLogisticsCell(new Vector3(0, -1, 1), 2);
        InitializeLogisticsMap(map, playerRoot, publicBuildingCell);
        var service = new LogisticsService(map);
        service.RegisterMainCity(0, playerRoot);
        service.RecalculateAll();

        // Key >= 2 不参与断供检测/吞并（决策 10）
        Assert.IsTrue(service.IsOwnedBy(publicBuildingCell, 2));
    }

    [Test]
    public void IsVisibleToFaction_PseudoFactionCellUsesViewerDiscovery()
    {
        var map = new HexMapService();
        // 中立公共建筑伪阵营（Key = 2）：按观察方永久发现状态判断（A7 修复）
        HexCellData publicBuildingCell = CreateLogisticsCell(Vector3.zero, 2);
        publicBuildingCell.ExploreBy(1);
        InitializeLogisticsMap(map, publicBuildingCell);
        var service = new LogisticsService(map);

        Assert.IsFalse(service.IsVisibleToFaction(publicBuildingCell, 0));
        Assert.IsTrue(service.IsVisibleToFaction(publicBuildingCell, 1));
    }

    [Test]
    public void RecalculateAll_AnnexationFiresLogisticsChangedOnce()
    {
        var map = new HexMapService();
        HexCellData playerRoot = CreateLogisticsCell(Vector3.zero, 0);
        HexCellData bridge = CreateLogisticsCell(new Vector3(0, -1, 1), 1);
        HexCellData aiRear = CreateLogisticsCell(new Vector3(0, -2, 2), 1);
        InitializeLogisticsMap(map, playerRoot, bridge, aiRear);
        var service = new LogisticsService(map);
        service.RegisterMainCity(0, playerRoot);
        service.RegisterMainCity(1, bridge);
        service.RecalculateAll();

        int events = 0;
        service.LogisticsChanged += () => events++;

        // 切桥 → 吞并 aiRear（递归重算）→ 整个流程只触发一次事件
        bridge.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(0, 0);
        service.RecalculateAll();

        Assert.AreEqual(1, events);
        Assert.IsTrue(service.IsOwnedBy(aiRear, 0));
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
            new Vector3[0]);
    }
}
