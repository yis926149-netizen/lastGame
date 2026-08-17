using GameConfig;

//****************************************
//功能说明：游戏流程配置提供者。
//         游戏时长、昼夜周期、光照强度、倒计时阈值、结算延迟、吞并深度、迷雾过渡速度
//         优先由 Excel 读取，Excel 未生成时回退 Legacy 默认值（双轨迁移期，阶段6 删除回退）。
//****************************************
public class GameFlowConfigProvider
{
    private readonly GameFlowConfigDatabaseSO _database;  // Excel 数值（可选）

    public GameFlowConfigProvider(GameFlowConfigDatabaseSO database = null)
    {
        _database = database;
    }

    public GameFlowConfigData Config => _database?.Config;

    public float GameDurationSeconds => Config?.gameDurationSeconds ?? 300f;

    public float DayNightCycleSeconds => Config?.dayNightCycleSeconds ?? 300f;

    public float NoonLightIntensity => Config?.noonLightIntensity ?? 1.2f;

    public float SunsetLightIntensity => Config?.sunsetLightIntensity ?? 0.4f;

    public float CountdownUrgentThreshold => Config?.countdownUrgentThreshold ?? 60f;

    public float SettlementDelaySeconds => Config?.settlementDelaySeconds ?? 1.5f;

    public float EndGameUiDelaySeconds => Config?.endGameUiDelaySeconds ?? 6.5f;

    public int AnnexationRecalcDepth => Config?.annexationRecalcDepth ?? 3;

    public float FogTransitionSpeed => Config?.fogTransitionSpeed ?? 0.5f;
}
