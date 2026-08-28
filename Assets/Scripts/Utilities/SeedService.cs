using System;

public static class SeedService
{
    private const int Terrain  = 101;
    private const int River    = 201;
    private const int LandForm = 301;
    private const int LandFormCluster = 311;
    private const int Mountain = 321;
    private const int Resource = 401;
    private const int Player   = 501;
    private const int AI       = 601;
    private const int Card     = 701;
    private const int TransitionTest = 801;
    private const int PublicBuilding = 901;
    private const int PublicBuildingMarker = 902;
    private const int ExplorationReward = 1001;

    // 【多单位落点】槽位烘焙独立随机流（整数模块常量，避免复用 Terrain/LandForm/AI 随机流）。
    public const int UnitSlotModule = 10001;

    // 【多单位落点】槽位烘焙算法版本。锚点公式 / 混合算法变化导致旧地图槽位变化时，
    // 存档与回放必须按此版本处理（与 GeneratorVersion 解耦，避免影响地形等既有随机流）。
    // v2：四角锚点偏移带由「地块尺寸 5%~10%」调整为「25%~40%」（观感调参，锚点 32.5% + 抖动 23%）。
    public const int UnitSlotGeneratorVersion = 2;

    private static bool _initialized = false;
    private static int _rootSeed;

    public static void Initialize(int rootSeed)
    {
        _rootSeed = rootSeed;
        _initialized = true;
    }

    public static System.Random GetRandom(string moduleId)
    {
        if (!_initialized)
            throw new InvalidOperationException("SeedService 未初始化，请先调用 SeedService.Initialize(rootSeed)");

        int moduleConst = moduleId switch
        {
            "Terrain"  => Terrain,
            "River"    => River,
            "LandForm" => LandForm,
            "LandFormCluster" => LandFormCluster,
            "Mountain" => Mountain,
            "Resource" => Resource,
            "Player"   => Player,
            "AI"       => AI,
            "Card"     => Card,
            "TransitionTest" => TransitionTest,
            "PublicBuilding" => PublicBuilding,
            "PublicBuildingMarker" => PublicBuildingMarker,
            "ExplorationReward" => ExplorationReward,
            _          => throw new ArgumentException($"未知模块 ID: {moduleId}")
        };

        int derivedSeed = (_rootSeed * 31 + moduleConst) & int.MaxValue;
        return new System.Random(derivedSeed);
    }

    public static int CurrentSeed => _rootSeed;

    /// <summary>
    /// 从「地图种子 + 整数六边形坐标 + 槽位索引」派生稳定整数种子（32 位混合，不依赖运行时实现的 System.HashCode）。
    /// 同一 (mapSeed, q, r, s, slotIndex) 恒得到同一结果，跨平台/跨进程可复现。
    /// </summary>
    public static int DeriveCellSeed(int moduleId, int mapSeed, int q, int r, int s, int slotIndex)
    {
        unchecked
        {
            uint h = (uint)mapSeed;
            h = Mix(h, (uint)moduleId);
            h = Mix(h, (uint)q);
            h = Mix(h, (uint)r);
            h = Mix(h, (uint)s);
            h = Mix(h, (uint)slotIndex);
            // 收尾雪崩，摊平低 bit 相关性
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (int)(h & 0x7FFFFFFF); // 保证非负
        }
    }

    private static uint Mix(uint h, uint k)
    {
        h ^= k + 0x9E3779B9u + (h << 6) + (h >> 2);
        return h;
    }

    public const int GeneratorVersion = 2;
}
