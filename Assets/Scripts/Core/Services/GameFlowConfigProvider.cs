using GameConfig;

//****************************************
//功能说明：游戏流程配置提供者（阶段6：Excel 唯一主源）。
//         游戏时长、昼夜周期、光照强度、倒计时阈值、结算延迟、吞并深度、迷雾过渡速度
//         仅由 Excel 读取；Excel 未生成/未绑定时抛异常，暴露配置缺失。
//****************************************
public class GameFlowConfigProvider
{
    private readonly GameFlowConfigDatabaseSO _database;

    public GameFlowConfigProvider(GameFlowConfigDatabaseSO database = null)
    {
        _database = database;
    }

    public GameFlowConfigData Config
    {
        get
        {
            if (_database?.Config == null)
                throw new System.InvalidOperationException(
                    "[GameFlow] Excel 游戏流程配置未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 GameFlowConfigDatabaseSO。");
            return _database.Config;
        }
    }

    public float GameDurationSeconds => Config.gameDurationSeconds;
    public float DayNightCycleSeconds => Config.dayNightCycleSeconds;
    public float NoonLightIntensity => Config.noonLightIntensity;
    public float SunsetLightIntensity => Config.sunsetLightIntensity;
    public float CountdownUrgentThreshold => Config.countdownUrgentThreshold;
    public float SettlementDelaySeconds => Config.settlementDelaySeconds;
    public float EndGameUiDelaySeconds => Config.endGameUiDelaySeconds;
    public int AnnexationRecalcDepth => Config.annexationRecalcDepth;
    public float FogTransitionSpeed => Config.fogTransitionSpeed;
}
