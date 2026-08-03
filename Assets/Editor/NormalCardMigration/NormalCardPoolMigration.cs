using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

//****************************************
//功能说明：普通卡池对象化——一次性资产迁移工具。
//         把 UnitDatabase / BuildingDatabase 的旧平行列表迁移为 UnitConfigSO / BuildingConfigSO，
//         组装 NormalCardPool，迁移探索奖励引用，并写入 GameScene 的 GameInstaller 新卡池引用。
//         要求迁移前旧字段与新字段并存（数据库 SO 中两者都存在）。
//         幂等：重跑时更新已有 config 资产，不重建 GUID。
//****************************************
public static class NormalCardPoolMigration
{
    private const string UnitDatabasePath = "Assets/Scripts/ScriptableObjects/UnitDatabase.asset";
    private const string BuildingDatabasePath = "Assets/Scripts/ScriptableObjects/BuildingDatabase.asset";
    private const string UnitConfigDir = "Assets/Scripts/ScriptableObjects/UnitConfigs";
    private const string BuildingConfigDir = "Assets/Scripts/ScriptableObjects/BuildingConfigs";
    private const string PoolPath = "Assets/Scripts/ScriptableObjects/NormalCardPool.asset";
    private const string ExplorationRewardPath = "Assets/Scripts/ScriptableObjects/ExplorationRewardConfigSO.asset";
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";

    // buildingId -> BulidingType 显式映射（不依赖枚举声明顺序）
    private static readonly Enums.BulidingType[] BuildingTypeMap =
    {
        Enums.BulidingType.AttackStatue,          // 0
        Enums.BulidingType.DefenseStatue,         // 1
        Enums.BulidingType.Altar,                 // 2
        Enums.BulidingType.TechnologyAndCultural, // 3
        Enums.BulidingType.Barracks,              // 4
        Enums.BulidingType.ArrowTower,            // 5
    };

    [MenuItem("Tools/Normal Card Pool/Migrate")]
    public static void Migrate()
    {
        var report = new StringBuilder();
        report.AppendLine("=== Normal Card Pool Migration ===");

        // ---------- 预检 ----------
        var unitDb = AssetDatabase.LoadAssetAtPath<UnitDatabaseSO>(UnitDatabasePath);
        var buildingDb = AssetDatabase.LoadAssetAtPath<BuildingDatabaseSO>(BuildingDatabasePath);
        if (unitDb == null || buildingDb == null)
        {
            Debug.LogError($"[Migration] 数据库资产加载失败: {UnitDatabasePath} / {BuildingDatabasePath}");
            return;
        }

        Precheck(unitDb, buildingDb, report);

        // ---------- 单位迁移 ----------
        var unitConfigs = MigrateUnits(unitDb, report);

        // ---------- 建筑迁移 ----------
        var buildingConfigs = MigrateBuildings(buildingDb, unitConfigs, report);

        // ---------- 数据库原地更新（保留 GUID） ----------
        unitDb.units = unitConfigs;
        buildingDb.buildings = buildingConfigs;
        if (buildingDb.CityModel != null && buildingDb.CityModel.Count > 0)
            buildingDb.cityModel = buildingDb.CityModel[0];
        EditorUtility.SetDirty(unitDb);
        EditorUtility.SetDirty(buildingDb);

        // ---------- 普通卡池 ----------
        var pool = MigratePool(unitConfigs, buildingConfigs, report);

        // ---------- 探索奖励 ----------
        MigrateExplorationReward(unitConfigs, report);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ---------- 场景绑定 ----------
        BindScene(pool, report);

        report.AppendLine("=== Migration Finished ===");
        Debug.Log(report.ToString());
    }

    // ===================== 预检 =====================

    private static void Precheck(UnitDatabaseSO unitDb, BuildingDatabaseSO buildingDb, StringBuilder report)
    {
        int units = unitDb.unitModels?.Count ?? 0;
        int unitDatas = unitDb.unitDatas?.Count ?? 0;
        int cards = unitDb.Cards?.Count ?? 0;
        int icons = unitDb.unitIcons?.Count ?? 0;
        int skillIcons = unitDb.skillIcons?.Count ?? 0;
        report.AppendLine($"[Precheck] Unit lists: models={units}, datas={unitDatas}, cards={cards}, icons={icons}, skillIcons={skillIcons}");

        int models = buildingDb.buildingModels?.Count ?? 0;
        int hp = buildingDb.buildingBaseHP?.Count ?? 0;
        int buildingCards = buildingDb.buildingCards?.Count ?? 0;
        report.AppendLine($"[Precheck] Building lists: models={models}, baseHP={hp}, cards={buildingCards}");

        if (units != 12 || unitDatas != 12 || cards != 12 || icons != 12 || skillIcons != 12)
            throw new System.InvalidOperationException($"[Migration] 单位平行列表长度不一致（期望全部为 12）：{units}/{unitDatas}/{cards}/{icons}/{skillIcons}，迁移中止。");

        // 决策 1：建筑 HP 12 项，后 6 项为垃圾数据，只取前 6 项。
        if (models != 6 || buildingCards != 6)
            throw new System.InvalidOperationException($"[Migration] 建筑模型/卡面长度异常（期望 6）：models={models}, cards={buildingCards}，迁移中止。");
        if (hp < 6)
            throw new System.InvalidOperationException($"[Migration] 建筑 HP 不足 6 项（当前 {hp}），迁移中止。");
        if (hp > 6)
            report.AppendLine($"[Precheck] 建筑 baseHP 共 {hp} 项，按决策 1 只取前 6 项，后 {hp - 6} 项作为垃圾数据丢弃。");

        var seen = new HashSet<int>();
        foreach (var d in unitDb.unitDatas)
        {
            if (d == null) throw new System.InvalidOperationException("[Migration] unitDatas 中存在 null 条目，迁移中止。");
            if (d.id < 0 || !seen.Add(d.id))
                throw new System.InvalidOperationException($"[Migration] 单位 ID 非法或重复：{d.id}，迁移中止。");
        }
        report.AppendLine("[Precheck] 单位 ID 非负且唯一，通过。");

        report.AppendLine("[Precheck] AttackInterval 按决策 5 保持原序列化值（当前全部为 0），不做修改。");
    }

    // ===================== 单位迁移 =====================

    private static List<UnitConfigSO> MigrateUnits(UnitDatabaseSO unitDb, StringBuilder report)
    {
        if (!Directory.Exists(UnitConfigDir))
            AssetDatabase.CreateFolder("Assets/Scripts/ScriptableObjects", "UnitConfigs");

        var result = new List<UnitConfigSO>();
        for (int i = 0; i < unitDb.unitDatas.Count; i++)
        {
            string path = $"{UnitConfigDir}/UnitConfig-{i}.asset";
            var config = AssetDatabase.LoadAssetAtPath<UnitConfigSO>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<UnitConfigSO>();
                AssetDatabase.CreateAsset(config, path);
            }

            config.unitData = unitDb.unitDatas[i];
            config.unitModel = unitDb.unitModels[i];
            config.cardSprite = unitDb.Cards[i];
            config.unitIcon = unitDb.unitIcons[i];
            config.skillIcon = unitDb.skillIcons[i];
            config.strategyType = ResolveStrategyType(i);
            config.attackSfx = BuildAttackSfx(i);

            EditorUtility.SetDirty(config);
            result.Add(config);
            report.AppendLine($"[Unit] {i}: {config.unitData.unitName} strategy={config.strategyType} AttackInterval={config.unitData.AttackInterval}");
        }
        return result;
    }

    /// <summary>旧策略魔法数等价迁移：0→Settler；3/5/9→Ranged；其他→Melee。</summary>
    private static UnitStrategyType ResolveStrategyType(int unitId)
    {
        if (unitId == 0) return UnitStrategyType.Settler;
        if (unitId == 3 || unitId == 5 || unitId == 9) return UnitStrategyType.Ranged;
        return UnitStrategyType.Melee;
    }

    /// <summary>UnitMovementController 旧 switch（461-506 行）等价迁移：primarySfx 立即播放，delayedSfx 按延迟播放。</summary>
    private static AttackSfxConfig BuildAttackSfx(int unitId)
    {
        var config = new AttackSfxConfig();
        switch (unitId)
        {
            case 1:
            case 2:
                config.primarySfx = "Blunt5";
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Blunt5", delay = 0.5f });
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Blunt5", delay = 1.0f });
                break;
            case 3:
                config.primarySfx = "Indicator4";
                break;
            case 4:
                config.primarySfx = "Weapon_Whoosh 09";
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Short_Sword_Hit 04", delay = 0.6f });
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Short_Sword_Hit 04", delay = 1.0f });
                break;
            case 5:
                config.primarySfx = "Machine_Gun-008";
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Machine_Gun-008", delay = 0.6f });
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Machine_Gun-008", delay = 1.0f });
                break;
            case 6:
                config.primarySfx = "Weapon_Whoosh 09";
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Short_Sword_Hit 04", delay = 0.5f });
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Short_Sword_Hit 04", delay = 1.1f });
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Short_Sword_Hit 03", delay = 0f });
                break;
            case 7:
            case 8:
                config.primarySfx = "Creature_02_05";
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Creature_02_05", delay = 0.5f });
                break;
            case 9:
                config.primarySfx = "Toilet_Flush-006";
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Toilet_Flush-006", delay = 0.5f });
                break;
            case 10:
                config.primarySfx = "Big_Explosion-004";
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Big_Explosion-004", delay = 0.4f });
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Big_Explosion-004", delay = 0.9f });
                break;
            case 11:
                config.primarySfx = "Weapon_Whoosh 09";
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Short_Sword_Hit 04", delay = 0.5f });
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Short_Sword_Hit 04", delay = 1.0f });
                config.delayedSfx.Add(new AttackSfxEntry { sfxName = "Short_Sword_Hit 03", delay = 0f });
                break;
        }
        return config;
    }

    // ===================== 建筑迁移 =====================

    private static List<BuildingConfigSO> MigrateBuildings(
        BuildingDatabaseSO buildingDb,
        List<UnitConfigSO> unitConfigs,
        StringBuilder report)
    {
        if (!Directory.Exists(BuildingConfigDir))
            AssetDatabase.CreateFolder("Assets/Scripts/ScriptableObjects", "BuildingConfigs");

        var result = new List<BuildingConfigSO>();
        for (int i = 0; i < 6; i++)
        {
            string path = $"{BuildingConfigDir}/BuildingConfig-{i}.asset";
            var config = AssetDatabase.LoadAssetAtPath<BuildingConfigSO>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<BuildingConfigSO>();
                AssetDatabase.CreateAsset(config, path);
            }

            config.buildingId = i;
            config.buildingType = BuildingTypeMap[i];
            config.buildingModel = buildingDb.buildingModels[i];
            config.baseHP = buildingDb.buildingBaseHP[i];
            config.cardSprite = buildingDb.buildingCards[i];
            config.blocksMovement = (i == 0 || i == 1);

            // 兵营产出单位：与旧 BarracksSpawner._spawnUnitID = 1 保持一致（单位 1：愤怒蘑菇）。
            config.producedUnit = i == 4 && unitConfigs.Count > 1 ? unitConfigs[1] : null;

            EditorUtility.SetDirty(config);
            result.Add(config);
            report.AppendLine($"[Building] {i}: type={config.buildingType} hp={config.baseHP} blocks={config.blocksMovement} producedUnit={(config.producedUnit != null ? config.producedUnit.unitData.unitName : "null")}");
        }
        return result;
    }

    // ===================== 普通卡池 =====================

    private static NormalCardPoolSO MigratePool(
        List<UnitConfigSO> unitConfigs,
        List<BuildingConfigSO> buildingConfigs,
        StringBuilder report)
    {
        var pool = AssetDatabase.LoadAssetAtPath<NormalCardPoolSO>(PoolPath);
        if (pool == null)
        {
            pool = ScriptableObject.CreateInstance<NormalCardPoolSO>();
            AssetDatabase.CreateAsset(pool, PoolPath);
        }

        // 决策 2：随机池 = 12 单位卡 + 6 建筑卡；保底 = 单位 0（移民）。
        pool.cards.Clear();
        pool.cards.AddRange(unitConfigs);
        pool.cards.AddRange(buildingConfigs);
        pool.guaranteedFirstCard = unitConfigs[0];

        EditorUtility.SetDirty(pool);
        report.AppendLine($"[Pool] cards={pool.cards.Count}（12 单位 + 6 建筑），guaranteedFirstCard=UnitConfig-0");
        return pool;
    }

    // ===================== 探索奖励 =====================

    private static void MigrateExplorationReward(List<UnitConfigSO> unitConfigs, StringBuilder report)
    {
        var rewardConfig = AssetDatabase.LoadAssetAtPath<ExplorationRewardConfigSO>(ExplorationRewardPath);
        if (rewardConfig == null)
        {
            report.AppendLine("[ExplorationReward] 未找到资产，跳过。");
            return;
        }

        if (rewardConfig.rewardUnitIDs == null || rewardConfig.rewardUnitIDs.Length == 0)
        {
            rewardConfig.rewardUnits = new UnitConfigSO[0];
            report.AppendLine("[ExplorationReward] rewardUnitIDs 为空，rewardUnits 置空（不再回退魔法 ID 2）。");
        }
        else
        {
            var list = new List<UnitConfigSO>();
            foreach (int id in rewardConfig.rewardUnitIDs)
            {
                if (id < 0 || id >= unitConfigs.Count || unitConfigs[id] == null)
                {
                    throw new System.InvalidOperationException($"[Migration] 探索奖励单位 ID {id} 无法映射到 UnitConfig，迁移中止。");
                }
                list.Add(unitConfigs[id]);
            }
            rewardConfig.rewardUnits = list.ToArray();
            report.AppendLine($"[ExplorationReward] rewardUnitIDs=[{string.Join(",", rewardConfig.rewardUnitIDs)}] -> rewardUnits={list.Count} 个 config 引用。");
        }

        EditorUtility.SetDirty(rewardConfig);
    }

    // ===================== 场景绑定 =====================

    private static void BindScene(NormalCardPoolSO pool, StringBuilder report)
    {
        var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        var installer = Object.FindFirstObjectByType<GameInstaller>();
        if (installer == null)
        {
            report.AppendLine("[Scene] 未在 GameScene 中找到 GameInstaller，跳过场景绑定（需手动绑定）。");
            return;
        }

        var so = new SerializedObject(installer);
        SerializedProperty prop = so.FindProperty("_normalCardPoolSO");
        if (prop == null)
        {
            report.AppendLine("[Scene] GameInstaller 上未找到 _normalCardPoolSO 序列化字段，跳过场景绑定。");
            return;
        }

        prop.objectReferenceValue = pool;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene);
        report.AppendLine($"[Scene] GameInstaller._normalCardPoolSO 已写入 {PoolPath}，场景已保存。");
    }
}
