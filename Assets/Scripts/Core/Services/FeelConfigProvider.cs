using GameConfig;

//****************************************
//功能说明：表现配置规则（阶段6：Excel 唯一主源）。
//         相机震动、迷雾刷新、卡牌暗淡与天赋震屏参数仅由 Excel 读取；
//         Excel 未加载时抛异常，暴露配置缺失。消费点分散（CameraController/ChunkMapRenderer/
//         CardController/TalentCardSelectionUI/CardDragPreviewController），采用静态 Configure 模式（同 BattleFormulaRule）。
//****************************************
public static class FeelConfigProvider
{
    private static FeelConfigDatabaseSO _database;

    /// <summary>由 GameInstaller 在绑定阶段配置 Excel 数值库。</summary>
    public static void Configure(FeelConfigDatabaseSO database)
    {
        _database = database;
    }

    public static FeelConfigData Config
    {
        get
        {
            if (_database?.Config == null)
                throw new System.InvalidOperationException(
                    "[Feel] Excel 表现配置未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 FeelConfigDatabaseSO。");
            return _database.Config;
        }
    }

    public static float FogRefreshInterval => Config.fogRefreshInterval;
    public static float CameraShakeFrequency => Config.cameraShakeFrequency;
    public static float UnaffordableCardDim => Config.unaffordableCardDim;
    public static float TalentScreenShakeStrength => Config.talentScreenShakeStrength;
    public static float TalentScreenShakeDuration => Config.talentScreenShakeDuration;

    // 卡牌拖拽模型预览特效（实施计划 §3）。阈值以 Canvas 参考高度为基准。
    public static float CardDragStage1Ratio => Config.cardDragStage1Ratio;
    public static float CardDragStage2Ratio => Config.cardDragStage2Ratio;
    public static float CardDragCardMinScale => Config.cardDragCardMinScale;
    public static float CardDragCardFadeStart => Config.cardDragCardFadeStart;
    public static float CardDragModelMinScale => Config.cardDragModelMinScale;
    public static float CardDragModelFadeIn => Config.cardDragModelFadeIn;
    public static int CardDragPreviewRTSize => Config.cardDragPreviewRTSize;
    public static float CardDragPreviewWindowSize => Config.cardDragPreviewWindowSize;
    public static float CardDragPreviewCameraDistance => Config.cardDragPreviewCameraDistance;
    public static float CardDragPreviewPadding => Config.cardDragPreviewPadding;
}
