using System;

public static class SeedService
{
    private const int Terrain  = 101;
    private const int River    = 201;
    private const int LandForm = 301;
    private const int Resource = 401;
    private const int Player   = 501;
    private const int AI       = 601;
    private const int Card     = 701;
    private const int TransitionTest = 801;

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
            "Resource" => Resource,
            "Player"   => Player,
            "AI"       => AI,
            "Card"     => Card,
            "TransitionTest" => TransitionTest,
            _          => throw new ArgumentException($"未知模块 ID: {moduleId}")
        };

        int derivedSeed = (_rootSeed * 31 + moduleConst) & int.MaxValue;
        return new System.Random(derivedSeed);
    }

    public static int CurrentSeed => _rootSeed;

    public const int GeneratorVersion = 1;
}
