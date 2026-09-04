using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameConfig.Editor
{
    /// <summary>
    /// 从 game-config.json 生成运行期 SO（单位 / 建筑 / 普通卡池）。
    /// 解析为与 Unity 无关的 DTO，再映射到类型化的数值数据类。
    /// </summary>
    public static class GameConfigImporter
    {
        public const string JsonPath = "Config/Generated/game-config.json";
        public const string GeneratedDir = "Assets/GameConfig/Generated";
        public const string UnitDbAssetPath = GeneratedDir + "/UnitBalanceDatabase.asset";
        public const string BuildingDbAssetPath = GeneratedDir + "/BuildingBalanceDatabase.asset";
        public const string CardPoolAssetPath = GeneratedDir + "/NormalCardPoolDatabase.asset";
        public const string TacticalCardDbAssetPath = GeneratedDir + "/TacticalCardBalanceDatabase.asset";
        public const string TalentCardDbAssetPath = GeneratedDir + "/TalentCardBalanceDatabase.asset";
        public const string TalentDrawRuleDbAssetPath = GeneratedDir + "/TalentDrawRuleDatabase.asset";
        public const string ExplorationRewardConfigAssetPath = GeneratedDir + "/ExplorationRewardConfigDatabase.asset";
        public const string ExplorationRewardPoolAssetPath = GeneratedDir + "/ExplorationRewardPoolDatabase.asset";
        public const string MapResourceDbAssetPath = GeneratedDir + "/MapResourceBalanceDatabase.asset";
        public const string ResourceGlobalConfigAssetPath = GeneratedDir + "/ResourceGlobalConfigDatabase.asset";
        public const string MapLandFormDbAssetPath = GeneratedDir + "/MapLandFormBalanceDatabase.asset";
        public const string LandFormGlobalConfigAssetPath = GeneratedDir + "/LandFormGlobalConfigDatabase.asset";
        public const string PublicBuildingDbAssetPath = GeneratedDir + "/PublicBuildingBalanceDatabase.asset";
        public const string EconomyConfigAssetPath = GeneratedDir + "/EconomyConfigDatabase.asset";
        public const string GameFlowConfigAssetPath = GeneratedDir + "/GameFlowConfigDatabase.asset";
        public const string AIConfigAssetPath = GeneratedDir + "/AIConfigDatabase.asset";
        public const string BattleFormulaConfigAssetPath = GeneratedDir + "/BattleFormulaConfigDatabase.asset";
        public const string MapGenConfigAssetPath = GeneratedDir + "/MapGenConfigDatabase.asset";
        public const string CoreGameplayConfigAssetPath = GeneratedDir + "/CoreGameplayConfigDatabase.asset";
        public const string FeelConfigAssetPath = GeneratedDir + "/FeelConfigDatabase.asset";
        public const string MountainConfigAssetPath = GeneratedDir + "/MountainConfigDatabase.asset";

        public static ImportResult ImportAll(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return ImportResult.Fail($"找不到 {jsonPath}");

            GameConfigFile file;
            try
            {
                file = JsonUtility.FromJson<GameConfigFile>(File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                return ImportResult.Fail("JSON 解析失败: " + ex.Message);
            }

            if (file is null || file.tables is null)
                return ImportResult.Fail("game-config.json 结构为空或非法");

            var errors = new List<string>();
            var units = BuildUnits(file, errors);
            var buildings = BuildBuildings(file, errors);
            var cards = BuildCardPool(file, errors);
            var tacticalCards = BuildTacticalCards(file, errors);
            var talentCards = BuildTalentCards(file, errors);
            var drawRules = BuildDrawRules(file, errors);
            var rewardConfig = BuildExplorationRewardConfig(file, errors);
            var rewardPool = BuildExplorationRewardPool(file, errors, units, buildings, tacticalCards);
            var resources = BuildMapResources(file, errors);
            var resourceGlobal = BuildResourceGlobalConfig(file, errors);
            var landForms = BuildLandForms(file, errors);
            var landFormGlobal = BuildLandFormGlobalConfig(file, errors);
            var publicBuildings = BuildPublicBuildings(file, errors);
            var economy = BuildEconomyConfig(file, errors);
            var gameFlow = BuildGameFlowConfig(file, errors);
            var aiConfig = BuildAIConfig(file, errors);
            var battleFormula = BuildBattleFormulaConfig(file, errors);
            var mapGen = BuildMapGenConfig(file, errors);
            var gameplay = BuildCoreGameplayConfig(file, errors);
            var feel = BuildFeelConfig(file, errors);
            var mountain = BuildMountainConfig(file, errors);
            if (errors.Count > 0)
                return ImportResult.Fail(string.Join("\n", errors));

            Directory.CreateDirectory(GeneratedDir);

            CreateOrReplace<UnitBalanceDatabaseSO>(UnitDbAssetPath, so => so.ReplaceAll(units.ToArray()));
            CreateOrReplace<BuildingBalanceDatabaseSO>(BuildingDbAssetPath, so => so.ReplaceAll(buildings.ToArray()));
            var cardPool = CreateOrReplace<NormalCardPoolDatabaseSO>(CardPoolAssetPath, so => so.ReplaceAll(cards.ToArray()));
            CreateOrReplace<TacticalCardBalanceDatabaseSO>(TacticalCardDbAssetPath, so => so.ReplaceAll(tacticalCards.ToArray()));
            CreateOrReplace<TalentCardBalanceDatabaseSO>(TalentCardDbAssetPath, so => so.ReplaceAll(talentCards.ToArray()));
            CreateOrReplace<TalentDrawRuleDatabaseSO>(TalentDrawRuleDbAssetPath, so => so.ReplaceAll(drawRules.ToArray()));
            CreateOrReplace<ExplorationRewardConfigDatabaseSO>(ExplorationRewardConfigAssetPath, so => so.ReplaceAll(rewardConfig.ToArray()));
            CreateOrReplace<ExplorationRewardPoolDatabaseSO>(ExplorationRewardPoolAssetPath, so => so.ReplaceAll(rewardPool.ToArray()));
            CreateOrReplace<MapResourceBalanceDatabaseSO>(MapResourceDbAssetPath, so => so.ReplaceAll(resources.ToArray()));
            CreateOrReplace<ResourceGlobalConfigDatabaseSO>(ResourceGlobalConfigAssetPath, so => so.ReplaceAll(resourceGlobal.ToArray()));
            CreateOrReplace<MapLandFormBalanceDatabaseSO>(MapLandFormDbAssetPath, so => so.ReplaceAll(landForms.ToArray()));
            CreateOrReplace<LandFormGlobalConfigDatabaseSO>(LandFormGlobalConfigAssetPath, so => so.ReplaceAll(landFormGlobal.ToArray()));
            CreateOrReplace<PublicBuildingBalanceDatabaseSO>(PublicBuildingDbAssetPath, so => so.ReplaceAll(publicBuildings.ToArray()));
            CreateOrReplace<EconomyConfigDatabaseSO>(EconomyConfigAssetPath, so => so.ReplaceAll(economy.ToArray()));
            CreateOrReplace<GameFlowConfigDatabaseSO>(GameFlowConfigAssetPath, so => so.ReplaceAll(gameFlow.ToArray()));
            CreateOrReplace<AIConfigDatabaseSO>(AIConfigAssetPath, so => so.ReplaceAll(aiConfig.ToArray()));
            CreateOrReplace<BattleFormulaConfigDatabaseSO>(BattleFormulaConfigAssetPath, so => so.ReplaceAll(battleFormula.ToArray()));
            CreateOrReplace<MapGenConfigDatabaseSO>(MapGenConfigAssetPath, so => so.ReplaceAll(mapGen.ToArray()));
            CreateOrReplace<CoreGameplayConfigDatabaseSO>(CoreGameplayConfigAssetPath, so => so.ReplaceAll(gameplay.ToArray()));
            CreateOrReplace<FeelConfigDatabaseSO>(FeelConfigAssetPath, so => so.ReplaceAll(feel.ToArray()));
            CreateOrReplace<MountainConfigDatabaseSO>(MountainConfigAssetPath, so => so.ReplaceAll(mountain.ToArray()));

            AssetDatabase.SaveAssets();
            return ImportResult.Ok(
                $"生成 {units.Count} 个单位、{buildings.Count} 个建筑、{cards.Count} 张普通卡、" +
                $"{tacticalCards.Count} 张战术卡、{talentCards.Count} 张天赋卡、{drawRules.Count} 条抽卡规则、" +
                $"{resources.Count} 个资源、{landForms.Count} 个地貌、{publicBuildings.Count} 个公共建筑、{rewardPool.Count} 条奖励池。\n" +
                $"保底卡: {(string.IsNullOrEmpty(cardPool.GuaranteedFirstCardId) ? "(无)" : cardPool.GuaranteedFirstCardId)}");
        }

        private static T CreateOrReplace<T>(string path, Action<T> populate) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            var asset = existing != null ? existing : ScriptableObject.CreateInstance<T>();
            populate(asset);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
            return asset;
        }

        private static List<UnitBalanceData> BuildUnits(GameConfigFile file, List<string> errors)
        {
            var result = new List<UnitBalanceData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "单位");
            if (table is null)
            {
                errors.Add("缺少 [单位] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new UnitBalanceData
                {
                    unitId = Get(v, col, "unitId"),
                    legacyId = GetInt(v, col, "legacyId", 0),
                    displayName = Get(v, col, "displayName"),
                    enabled = GetBool(v, col, "enabled", false),
                    strategyType = Get(v, col, "strategyType"),
                    hp = GetFloat(v, col, "hp", 0f),
                    attack = GetFloat(v, col, "attack", 0f),
                    defense = GetFloat(v, col, "defense", 0f),
                    attackRange = GetInt(v, col, "attackRange", 0),
                    movementPoints = GetFloat(v, col, "movementPoints", 0f),
                    viewPoints = GetInt(v, col, "viewPoints", 0),
                    attackIntervalSeconds = GetFloat(v, col, "attackIntervalSeconds", 0f),
                    cardCost = GetInt(v, col, "cardCost", 0),
                });
            }

            return result;
        }

        private static List<BuildingBalanceData> BuildBuildings(GameConfigFile file, List<string> errors)
        {
            var result = new List<BuildingBalanceData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "建筑");
            if (table is null)
            {
                errors.Add("缺少 [建筑] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                var produced = Get(v, col, "producedUnitId");
                result.Add(new BuildingBalanceData
                {
                    buildingId = Get(v, col, "buildingId"),
                    legacyId = GetInt(v, col, "legacyId", 0),
                    displayName = Get(v, col, "displayName"),
                    buildingType = Get(v, col, "buildingType"),
                    hp = GetFloat(v, col, "hp", 0f),
                    blocksMovement = GetBool(v, col, "blocksMovement", false),
                    cardCost = GetInt(v, col, "cardCost", 0),
                    producedUnitId = string.IsNullOrEmpty(produced) ? null : produced,
                    goldIncomePerSecond = GetFloat(v, col, "goldIncomePerSecond", 0f),
                });
            }

            return result;
        }

        private static List<NormalCardPoolEntry> BuildCardPool(GameConfigFile file, List<string> errors)
        {
            var result = new List<NormalCardPoolEntry>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "普通卡池");
            if (table is null)
            {
                errors.Add("缺少 [普通卡池] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new NormalCardPoolEntry
                {
                    cardId = Get(v, col, "cardId"),
                    cardType = Get(v, col, "cardType"),
                    weight = GetInt(v, col, "weight", 1),
                    enabled = GetBool(v, col, "enabled", true),
                    guaranteedFirst = GetBool(v, col, "guaranteedFirst", false),
                });
            }

            return result;
        }

        private static List<TacticalCardBalanceData> BuildTacticalCards(GameConfigFile file, List<string> errors)
        {
            var result = new List<TacticalCardBalanceData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "战术卡");
            if (table is null)
            {
                errors.Add("缺少 [战术卡] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new TacticalCardBalanceData
                {
                    cardId = Get(v, col, "cardId"),
                    legacyId = GetInt(v, col, "legacyId", 0),
                    displayName = Get(v, col, "displayName"),
                    description = Get(v, col, "description"),
                    enabled = GetBool(v, col, "enabled", false),
                    effectType = Get(v, col, "effectType"),
                    healRatio = GetFloat(v, col, "healRatio", 0f),
                    unitHealRatio = GetFloat(v, col, "unitHealRatio", 0f),
                    attackMultiplier = GetFloat(v, col, "attackMultiplier", 0f),
                    speedMultiplier = GetFloat(v, col, "speedMultiplier", 0f),
                    duration = GetFloat(v, col, "duration", 0f),
                    effectRadius = GetInt(v, col, "effectRadius", 1),
                    initialQuantity = GetInt(v, col, "initialQuantity", 0),
                });
            }

            return result;
        }

        private static List<TalentCardBalanceData> BuildTalentCards(GameConfigFile file, List<string> errors)
        {
            var result = new List<TalentCardBalanceData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "天赋卡");
            if (table is null)
            {
                errors.Add("缺少 [天赋卡] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new TalentCardBalanceData
                {
                    talentId = Get(v, col, "talentId"),
                    legacyId = GetInt(v, col, "legacyId", 0),
                    talentName = Get(v, col, "talentName"),
                    description = Get(v, col, "description"),
                    enabled = GetBool(v, col, "enabled", false),
                    effectType = Get(v, col, "effectType"),
                    statId = Get(v, col, "statId"),
                    value = GetFloat(v, col, "value", 0f),
                    weight = GetInt(v, col, "weight", 1),
                    repeatable = GetBool(v, col, "repeatable", false),
                });
            }

            return result;
        }

        private static List<TalentDrawRuleData> BuildDrawRules(GameConfigFile file, List<string> errors)
        {
            var result = new List<TalentDrawRuleData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "天赋抽卡规则");
            if (table is null)
            {
                errors.Add("缺少 [天赋抽卡规则] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new TalentDrawRuleData
                {
                    ruleId = Get(v, col, "ruleId"),
                    triggerTalentId = Get(v, col, "triggerTalentId"),
                    targetCardType = Get(v, col, "targetCardType"),
                    weightMultiplier = GetInt(v, col, "weightMultiplier", 1),
                    enabled = GetBool(v, col, "enabled", true),
                });
            }

            return result;
        }

        private static List<ExplorationRewardConfigData> BuildExplorationRewardConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<ExplorationRewardConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "探索奖励配置");
            if (table is null)
            {
                errors.Add("缺少 [探索奖励配置] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new ExplorationRewardConfigData
                {
                    configId = Get(v, col, "configId"),
                    noneWeight = GetInt(v, col, "noneWeight", 0),
                    goldWeight = GetInt(v, col, "goldWeight", 0),
                    militaryWeight = GetInt(v, col, "militaryWeight", 0),
                    tacticalWeight = GetInt(v, col, "tacticalWeight", 0),
                    buildingWeight = GetInt(v, col, "buildingWeight", 0),
                    goldTiers = Get(v, col, "goldTiers"),
                    unitCountTiers = Get(v, col, "unitCountTiers"),
                    costNone = GetInt(v, col, "costNone", 0),
                    costGold = GetInt(v, col, "costGold", 0),
                    costMilitary = GetInt(v, col, "costMilitary", 0),
                    costTactical = GetInt(v, col, "costTactical", 0),
                    costBuilding = GetInt(v, col, "costBuilding", 0),
                });
            }

            return result;
        }

        private static List<ExplorationRewardPoolEntry> BuildExplorationRewardPool(
            GameConfigFile file, List<string> errors,
            List<UnitBalanceData> units, List<BuildingBalanceData> buildings, List<TacticalCardBalanceData> tacticalCards)
        {
            var result = new List<ExplorationRewardPoolEntry>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "探索奖励池");
            if (table is null)
            {
                errors.Add("缺少 [探索奖励池] 表");
                return result;
            }

            // 跨表校验：只保留"目标启用"的条目，避免发放未启用/未实现的卡。
            var enabledUnits = new HashSet<string>(units.Where(u => u.enabled).Select(u => u.unitId));
            // 建筑表无 enabled 列（不做启用门禁）：表内建筑一律视为可作为奖励目标。
            var enabledBuildings = new HashSet<string>(buildings.Select(b => b.buildingId));
            var enabledTactical = new HashSet<string>(tacticalCards.Where(t => t.enabled).Select(t => t.cardId));

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                var poolId = Get(v, col, "poolId");
                var rewardType = Get(v, col, "rewardType");
                var configId = Get(v, col, "configId");

                bool targetEnabled = rewardType switch
                {
                    "MilitaryUnit" => enabledUnits.Contains(configId),
                    "TacticalCard" => enabledTactical.Contains(configId),
                    "Building" => enabledBuildings.Contains(configId),
                    _ => false,
                };
                if (!targetEnabled)
                {
                    Debug.LogWarning($"[游戏配置导入] 探索奖励池条目 {poolId} ({rewardType}:{configId}) 引用了未启用的目标，已跳过。");
                    continue;
                }

                result.Add(new ExplorationRewardPoolEntry
                {
                    poolId = poolId,
                    rewardType = rewardType,
                    configId = configId,
                    weight = GetInt(v, col, "weight", 1),
                    enabled = GetBool(v, col, "enabled", true),
                });
            }

            return result;
        }

        private static List<MapResourceBalanceData> BuildMapResources(GameConfigFile file, List<string> errors)
        {
            var result = new List<MapResourceBalanceData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "地图资源");
            if (table is null)
            {
                errors.Add("缺少 [地图资源] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new MapResourceBalanceData
                {
                    resourceId = Get(v, col, "resourceId"),
                    resourceName = Get(v, col, "resourceName"),
                    description = Get(v, col, "description"),
                    enabled = GetBool(v, col, "enabled", false),
                    pickupEffectType = Get(v, col, "pickupEffectType"),
                    attackBonus = GetFloat(v, col, "attackBonus", 0f),
                    healRatio = GetFloat(v, col, "healRatio", 0f),
                    defenseBonus = GetFloat(v, col, "defenseBonus", 0f),
                    goldAmount = GetInt(v, col, "goldAmount", 0),
                    explorationGoldBonus = GetInt(v, col, "explorationGoldBonus", 0),
                    spawnWeight = GetInt(v, col, "spawnWeight", 0),
                });
            }

            return result;
        }

        private static List<ResourceGlobalConfigData> BuildResourceGlobalConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<ResourceGlobalConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "地图资源全局");
            if (table is null)
            {
                errors.Add("缺少 [地图资源全局] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new ResourceGlobalConfigData
                {
                    configId = Get(v, col, "configId"),
                    emptySpawnWeight = GetInt(v, col, "emptySpawnWeight", 0),
                    baseExplorationGold = GetInt(v, col, "baseExplorationGold", 0),
                });
            }

            return result;
        }

        private static List<MapLandFormBalanceData> BuildLandForms(GameConfigFile file, List<string> errors)
        {
            var result = new List<MapLandFormBalanceData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "地图地貌");
            if (table is null)
            {
                errors.Add("缺少 [地图地貌] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new MapLandFormBalanceData
                {
                    landFormId = Get(v, col, "landFormId"),
                    landFormName = Get(v, col, "landFormName"),
                    description = Get(v, col, "description"),
                    enabled = GetBool(v, col, "enabled", false),
                    effectType = Get(v, col, "effectType"),
                    defenseBonus = GetFloat(v, col, "defenseBonus", 0f),
                    healRatio = GetFloat(v, col, "healRatio", 0f),
                    healInterval = GetFloat(v, col, "healInterval", 0f),
                    goldIncomePerSecond = GetFloat(v, col, "goldIncomePerSecond", 0f),
                    blockBuildingSpawn = GetBool(v, col, "blockBuildingSpawn", false),
                    spawnWeight = GetInt(v, col, "spawnWeight", 0),
                    clusterSpawn = GetBool(v, col, "clusterSpawn", false),
                    clusterCount = GetInt(v, col, "clusterCount", 1),
                    clusterTargetSize = GetInt(v, col, "clusterTargetSize", 8),
                    clusterFillProbability = GetFloat(v, col, "clusterFillProbability", 0.8f),
                    clusterMinSpacing = GetInt(v, col, "clusterMinSpacing", 4),
                    clusterMaxRadius = GetInt(v, col, "clusterMaxRadius", 4),
                });
            }

            return result;
        }

        private static List<LandFormGlobalConfigData> BuildLandFormGlobalConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<LandFormGlobalConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "地图地貌全局");
            if (table is null)
            {
                errors.Add("缺少 [地图地貌全局] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new LandFormGlobalConfigData
                {
                    configId = Get(v, col, "configId"),
                    emptySpawnWeight = GetInt(v, col, "emptySpawnWeight", 0),
                });
            }

            return result;
        }

        private static List<PublicBuildingBalanceData> BuildPublicBuildings(GameConfigFile file, List<string> errors)
        {
            var result = new List<PublicBuildingBalanceData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "公共建筑");
            if (table is null)
            {
                errors.Add("缺少 [公共建筑] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new PublicBuildingBalanceData
                {
                    buildingId = Get(v, col, "buildingId"),
                    legacyId = GetInt(v, col, "legacyId", 0),
                    buildingName = Get(v, col, "buildingName"),
                    enabled = GetBool(v, col, "enabled", false),
                    captureHp = GetFloat(v, col, "captureHp", 0f),
                    defenseHp = GetFloat(v, col, "defenseHp", 0f),
                    subHexDirections = Get(v, col, "subHexDirections"),
                });
            }

            if (result.Count == 0)
                errors.Add("[公共建筑] 表为空：公共建筑是核心玩法，禁止空配置");

            return result;
        }

        private static List<EconomyConfigData> BuildEconomyConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<EconomyConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "经济配置");
            if (table is null)
            {
                errors.Add("缺少 [经济配置] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new EconomyConfigData
                {
                    configId = Get(v, col, "configId"),
                    startingGold = GetInt(v, col, "startingGold", 100),
                    baseIncomePerTick = GetInt(v, col, "baseIncomePerTick", 2),
                    aiIncomeBonusPerTick = GetInt(v, col, "aiIncomeBonusPerTick", 6),
                    incomeTickInterval = GetFloat(v, col, "incomeTickInterval", 1f),
                    explorationCostFallback = GetInt(v, col, "explorationCostFallback", 50),
                    cardCostFallback = GetInt(v, col, "cardCostFallback", 10),
                });
            }

            if (result.Count == 0)
                errors.Add("[经济配置] 表为空：经济是核心玩法，禁止空配置");

            return result;
        }

        private static List<GameFlowConfigData> BuildGameFlowConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<GameFlowConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "游戏流程配置");
            if (table is null)
            {
                errors.Add("缺少 [游戏流程配置] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new GameFlowConfigData
                {
                    configId = Get(v, col, "configId"),
                    gameDurationSeconds = GetFloat(v, col, "gameDurationSeconds", 300f),
                    dayNightCycleSeconds = GetFloat(v, col, "dayNightCycleSeconds", 300f),
                    noonLightIntensity = GetFloat(v, col, "noonLightIntensity", 1.2f),
                    sunsetLightIntensity = GetFloat(v, col, "sunsetLightIntensity", 0.4f),
                    countdownUrgentThreshold = GetFloat(v, col, "countdownUrgentThreshold", 60f),
                    settlementDelaySeconds = GetFloat(v, col, "settlementDelaySeconds", 1.5f),
                    endGameUiDelaySeconds = GetFloat(v, col, "endGameUiDelaySeconds", 6.5f),
                    annexationRecalcDepth = GetInt(v, col, "annexationRecalcDepth", 3),
                    fogTransitionSpeed = GetFloat(v, col, "fogTransitionSpeed", 0.5f),
                });
            }

            if (result.Count == 0)
                errors.Add("[游戏流程配置] 表为空：流程时间是核心玩法，禁止空配置");

            return result;
        }

        private static List<AIConfigData> BuildAIConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<AIConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "AI配置");
            if (table is null)
            {
                errors.Add("缺少 [AI配置] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new AIConfigData
                {
                    configId = Get(v, col, "configId"),
                    cardPlayInterval = GetFloat(v, col, "cardPlayInterval", 1.5f),
                    exploreInterval = GetFloat(v, col, "exploreInterval", 1.5f),
                    globalActionMinInterval = GetFloat(v, col, "globalActionMinInterval", 1f),
                    settlerCardPriority = GetInt(v, col, "settlerCardPriority", 100),
                    technologyCardPriority = GetInt(v, col, "technologyCardPriority", 90),
                    unitCardPriority = GetInt(v, col, "unitCardPriority", 70),
                    buildingCardPriority = GetInt(v, col, "buildingCardPriority", 60),
                    militaryRewardOverflowRings = GetInt(v, col, "militaryRewardOverflowRings", 5),
                });
            }

            if (result.Count == 0)
                errors.Add("[AI配置] 表为空：AI 是核心玩法，禁止空配置");

            return result;
        }

        private static List<BattleFormulaConfigData> BuildBattleFormulaConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<BattleFormulaConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "战斗公式");
            if (table is null)
            {
                errors.Add("缺少 [战斗公式] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new BattleFormulaConfigData
                {
                    configId = Get(v, col, "configId"),
                    riverDefensePenalty = GetFloat(v, col, "riverDefensePenalty", -0.5f),
                    attackStatueBonus = GetFloat(v, col, "attackStatueBonus", 0.7f),
                    highGroundAttackBonus = GetFloat(v, col, "highGroundAttackBonus", 0.5f),
                    highGroundRangeBonus = GetInt(v, col, "highGroundRangeBonus", 1),
                    meleeAlertRange = GetInt(v, col, "meleeAlertRange", 3),
                });
            }

            if (result.Count == 0)
                errors.Add("[战斗公式] 表为空：战斗公式是核心玩法，禁止空配置");

            return result;
        }

        private static List<MapGenConfigData> BuildMapGenConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<MapGenConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "地图生成参数");
            if (table is null)
            {
                errors.Add("缺少 [地图生成参数] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new MapGenConfigData
                {
                    configId = Get(v, col, "configId"),
                    perlinFrequency = GetFloat(v, col, "perlinFrequency", 0.05f),
                    perlinOctaves = GetInt(v, col, "perlinOctaves", 3),
                    perlinPersistence = GetFloat(v, col, "perlinPersistence", 0.6f),
                    arenaRadius = GetInt(v, col, "arenaRadius", 3),
                    arenaRiseDurationSeconds = GetFloat(v, col, "arenaRiseDurationSeconds", 1.2f),
                });
            }

            if (result.Count == 0)
                errors.Add("[地图生成参数] 表为空：地图生成参数是核心玩法，禁止空配置");

            return result;
        }

        private static List<CoreGameplayConfigData> BuildCoreGameplayConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<CoreGameplayConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "核心玩法配置");
            if (table is null)
            {
                errors.Add("缺少 [核心玩法配置] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new CoreGameplayConfigData
                {
                    configId = Get(v, col, "configId"),
                    movementSpeedPerPoint = GetFloat(v, col, "movementSpeedPerPoint", 10f),
                    attackDashSpeed = GetFloat(v, col, "attackDashSpeed", 20f),
                    unitRotationSpeed = GetFloat(v, col, "unitRotationSpeed", 5f),
                    attackArrivalThreshold = GetFloat(v, col, "attackArrivalThreshold", 1.5f),
                    attackReturnThreshold = GetFloat(v, col, "attackReturnThreshold", 0.2f),
                    attackAnimationDuration = GetFloat(v, col, "attackAnimationDuration", 1.5f),
                    unitDeathDestroyDelay = GetFloat(v, col, "unitDeathDestroyDelay", 2.2f),
                    buildingHealIntervalFallback = GetFloat(v, col, "buildingHealIntervalFallback", 5f),
                    unitPathSearchThrottle = GetInt(v, col, "unitPathSearchThrottle", 20),
                    barracksSpawnInterval = GetFloat(v, col, "barracksSpawnInterval", 15f),
                    barracksFallbackUnitLegacyId = GetInt(v, col, "barracksFallbackUnitLegacyId", 1),
                    arrowTowerRange = GetInt(v, col, "arrowTowerRange", 2),
                    arrowTowerAttackInterval = GetFloat(v, col, "arrowTowerAttackInterval", 1f),
                    arrowTowerDamage = GetFloat(v, col, "arrowTowerDamage", 15f),
                    arrowTowerArcHeight = GetFloat(v, col, "arrowTowerArcHeight", 2f),
                    arrowTowerFlightDuration = GetFloat(v, col, "arrowTowerFlightDuration", 0.3f),
                    centralChestHp = GetFloat(v, col, "centralChestHp", 500f),
                    handCardLimit = GetInt(v, col, "handCardLimit", 5),
                    initialHandCardCount = GetInt(v, col, "initialHandCardCount", 5),
                    tacticalCardSlotCount = GetInt(v, col, "tacticalCardSlotCount", 2),
                    talentOfferCount = GetInt(v, col, "talentOfferCount", 3),
                    publicBuildingMaxCount = GetInt(v, col, "publicBuildingMaxCount", 3),
                    publicBuildingMinLandNeighbors = GetInt(v, col, "publicBuildingMinLandNeighbors", 2),
                    publicBuildingArenaReserveExtraRings = GetInt(v, col, "publicBuildingArenaReserveExtraRings", 1),
                });
            }

            if (result.Count == 0)
                errors.Add("[核心玩法配置] 表为空：核心玩法参数禁止空配置");

            return result;
        }

        private static List<FeelConfigData> BuildFeelConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<FeelConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "表现配置");
            if (table is null)
            {
                errors.Add("缺少 [表现配置] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new FeelConfigData
                {
                    configId = Get(v, col, "configId"),
                    fogRefreshInterval = GetFloat(v, col, "fogRefreshInterval", 0.05f),
                    cameraShakeFrequency = GetFloat(v, col, "cameraShakeFrequency", 50f),
                    unaffordableCardDim = GetFloat(v, col, "unaffordableCardDim", 0.45f),
                    talentScreenShakeStrength = GetFloat(v, col, "talentScreenShakeStrength", 0.15f),
                    talentScreenShakeDuration = GetFloat(v, col, "talentScreenShakeDuration", 0.3f),
                    cardDragStage1Ratio = GetFloat(v, col, "cardDragStage1Ratio", 0.13f),
                    cardDragCardMinScale = GetFloat(v, col, "cardDragCardMinScale", 0.1f),
                    cardDragCardFadeStart = GetFloat(v, col, "cardDragCardFadeStart", 0.6f),
                    // ── deprecated：两阶段/RT 预览遗留列，仅保留配置链稳定，代码侧不再读取 ──
                    cardDragStage2Ratio = GetFloat(v, col, "cardDragStage2Ratio", 0.25f),
                    cardDragModelMinScale = GetFloat(v, col, "cardDragModelMinScale", 0.1f),
                    cardDragModelFadeIn = GetFloat(v, col, "cardDragModelFadeIn", 0.2f),
                    cardDragPreviewRTSize = GetInt(v, col, "cardDragPreviewRTSize", 512),
                    cardDragPreviewWindowSize = GetFloat(v, col, "cardDragPreviewWindowSize", 512f),
                    cardDragPreviewCameraDistance = GetFloat(v, col, "cardDragPreviewCameraDistance", 10f),
                    cardDragPreviewPadding = GetFloat(v, col, "cardDragPreviewPadding", 1.25f),
                    // ── 卡牌拖拽世界空间预览（改造计划 §7.1）──
                    cardDragPreviewHoverHeight = GetFloat(v, col, "cardDragPreviewHoverHeight", 1f),
                    cardDragPreviewSnapDuration = GetFloat(v, col, "cardDragPreviewSnapDuration", 1f),
                    cardDragPreviewAppearDuration = GetFloat(v, col, "cardDragPreviewAppearDuration", 0.08f),
                    cardDragPreviewLandingStretch = GetFloat(v, col, "cardDragPreviewLandingStretch", 0.3f),
                    cardDragPreviewLandingStretchPeak = GetFloat(v, col, "cardDragPreviewLandingStretchPeak", 0.4f),
                    cardDragPreviewLandingSquash = GetFloat(v, col, "cardDragPreviewLandingSquash", 0.22f),
                    cardDragPreviewLandingDropHeight = GetFloat(v, col, "cardDragPreviewLandingDropHeight", 0.4f),
                    // ── 卡牌拖拽落点提示（落点图标与连线计划 §5.5）──
                    cardDragTargetIconHeight = GetFloat(v, col, "cardDragTargetIconHeight", 3.5f),
                    cardDragTargetIconScale = GetFloat(v, col, "cardDragTargetIconScale", 1f),
                    cardDragLinkWidth = GetFloat(v, col, "cardDragLinkWidth", 6f),
                });
            }

            if (result.Count == 0)
                errors.Add("[表现配置] 表为空：表现配置参数禁止空配置");

            return result;
        }

        private static List<MountainConfigData> BuildMountainConfig(GameConfigFile file, List<string> errors)
        {
            var result = new List<MountainConfigData>();
            var table = Array.Find(file.tables, t => t is not null && t.sheetName == "山体配置");
            if (table is null)
            {
                errors.Add("缺少 [山体配置] 表");
                return result;
            }

            var col = ColumnIndex(table.columns);

            foreach (var row in table.rows ?? Array.Empty<RowEntry>())
            {
                var v = row?.values ?? Array.Empty<string>();
                result.Add(new MountainConfigData
                {
                    configId = Get(v, col, "configId"),
                    ridgeCount = GetInt(v, col, "ridgeCount", 0),
                    minRidgeLength = GetInt(v, col, "minRidgeLength", 5),
                    maxRidgeLength = GetInt(v, col, "maxRidgeLength", 12),
                    widthRadius = GetFloat(v, col, "widthRadius", 1f),
                    ridgeMinSpacing = GetInt(v, col, "ridgeMinSpacing", 3),
                    scoreHeightWeight = GetFloat(v, col, "scoreHeightWeight", 1f),
                    scoreDropWeight = GetFloat(v, col, "scoreDropWeight", 1f),
                    scoreTurnPenalty = GetFloat(v, col, "scoreTurnPenalty", 1f),
                    flatHeightThreshold = GetFloat(v, col, "flatHeightThreshold", 1f),
                    baseHeight = GetFloat(v, col, "baseHeight", 1.2f),
                    minHeight = GetFloat(v, col, "minHeight", 0.8f),
                    maxHeight = GetFloat(v, col, "maxHeight", 2.5f),
                    heightPerLength = GetFloat(v, col, "heightPerLength", 0.12f),
                    gamma = GetFloat(v, col, "gamma", 1.2f),
                    ridgeNoiseAmplitude = GetFloat(v, col, "ridgeNoiseAmplitude", 0.4f),
                    cellNoiseScale = GetFloat(v, col, "cellNoiseScale", 0.3f),
                    minVisibleHeight = GetFloat(v, col, "minVisibleHeight", 0.15f),
                    maxSlopeRatio = GetFloat(v, col, "maxSlopeRatio", 0.8f),
                    xzPerturbRatio = GetFloat(v, col, "xzPerturbRatio", 0.15f),
                    peakEccentricMinRatio = GetFloat(v, col, "peakEccentricMinRatio", 0.05f),
                    peakEccentricMaxRatio = GetFloat(v, col, "peakEccentricMaxRatio", 0.2f),
                    debugSingleCellAndStraightRidge = GetBool(v, col, "debugSingleCellAndStraightRidge", false),
                    debugStraightRidgeLength = GetInt(v, col, "debugStraightRidgeLength", 5),
                    triplanarWorldScale = GetFloat(v, col, "triplanarWorldScale", 1f),
                    triplanarBlendSharpness = GetFloat(v, col, "triplanarBlendSharpness", 4f),
                    roughness = GetFloat(v, col, "roughness", 0.9f),
                    metallic = GetFloat(v, col, "metallic", 0f),
                    shadowStrength = GetFloat(v, col, "shadowStrength", 1f),
                });
            }

            if (result.Count == 0)
                errors.Add("[山体配置] 表为空：山体配置参数禁止空配置");

            return result;
        }

        private static Dictionary<string, int> ColumnIndex(string[] columns)
        {
            var col = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Length; i++)
                col[columns[i]] = i;
            return col;
        }

        private static string Get(string[] v, Dictionary<string, int> col, string name)
            => col.TryGetValue(name, out var i) && i < v.Length ? v[i] : "";

        private static int GetInt(string[] v, Dictionary<string, int> col, string name, int fallback)
            => col.TryGetValue(name, out var i) && i < v.Length
               && int.TryParse(v[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n : fallback;

        private static float GetFloat(string[] v, Dictionary<string, int> col, string name, float fallback)
            => col.TryGetValue(name, out var i) && i < v.Length
               && float.TryParse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f : fallback;

        private static bool GetBool(string[] v, Dictionary<string, int> col, string name, bool fallback)
            => col.TryGetValue(name, out var i) && i < v.Length && bool.TryParse(v[i], out var b)
                ? b : fallback;
    }

    public sealed class ImportResult
    {
        public bool Success;
        public string Message;

        public static ImportResult Ok(string message) => new() { Success = true, Message = message };
        public static ImportResult Fail(string message) => new() { Success = false, Message = message };
    }
}
