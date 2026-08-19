using GameConfig;

//****************************************
//功能说明：山体配置规则（阶段6：Excel 唯一主源）。
//         程序化山脉生成的几何/材质数值仅由 Excel 读取；Excel 未加载时抛异常，暴露配置缺失。
//         资源引用（mountainLandForm/stableMaterial/rockTexture）与材质色阶仍在手工 SO 上，不在此表。
//         消费点 RidgeGenerator 为静态工具类，采用静态 Configure 模式（同 BattleFormulaRule）。
//****************************************
public static class MountainConfigProvider
{
    private static MountainConfigDatabaseSO _excel;

    /// <summary>由 GameInstaller 在绑定阶段配置 Excel 数值库。</summary>
    public static void Configure(MountainConfigDatabaseSO excel)
    {
        _excel = excel;
    }

    private static MountainConfigData Config
    {
        get
        {
            if (_excel?.Config == null)
                throw new System.InvalidOperationException(
                    "[Mountain] Excel 山体配置未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 MountainConfigDatabaseSO。");
            return _excel.Config;
        }
    }

    public static int RidgeCount => Config.ridgeCount;
    public static int MinRidgeLength => Config.minRidgeLength;
    public static int MaxRidgeLength => Config.maxRidgeLength;
    public static float WidthRadius => Config.widthRadius;
    public static int RidgeMinSpacing => Config.ridgeMinSpacing;
    public static float ScoreHeightWeight => Config.scoreHeightWeight;
    public static float ScoreDropWeight => Config.scoreDropWeight;
    public static float ScoreTurnPenalty => Config.scoreTurnPenalty;
    public static float FlatHeightThreshold => Config.flatHeightThreshold;
    public static float BaseHeight => Config.baseHeight;
    public static float MinHeight => Config.minHeight;
    public static float MaxHeight => Config.maxHeight;
    public static float HeightPerLength => Config.heightPerLength;
    public static float Gamma => Config.gamma;
    public static float RidgeNoiseAmplitude => Config.ridgeNoiseAmplitude;
    public static float CellNoiseScale => Config.cellNoiseScale;
    public static float MinVisibleHeight => Config.minVisibleHeight;
    public static float MaxSlopeRatio => Config.maxSlopeRatio;
    public static float XzPerturbRatio => Config.xzPerturbRatio;
    public static float PeakEccentricMinRatio => Config.peakEccentricMinRatio;
    public static float PeakEccentricMaxRatio => Config.peakEccentricMaxRatio;
    public static bool DebugSingleCellAndStraightRidge => Config.debugSingleCellAndStraightRidge;
    public static int DebugStraightRidgeLength => Config.debugStraightRidgeLength;
    public static float TriplanarWorldScale => Config.triplanarWorldScale;
    public static float TriplanarBlendSharpness => Config.triplanarBlendSharpness;
    public static float Roughness => Config.roughness;
    public static float Metallic => Config.metallic;
    public static float ShadowStrength => Config.shadowStrength;
}
