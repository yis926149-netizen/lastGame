using System;

namespace GameConfig
{
    /// <summary>
    /// 全局游戏流程配置（单行，由 game-config.json 导入的只读数据）。
    /// 收口 GlobalTimer / SunCycle / GlobalTimerUI / EndGame / Logistics / FogTransition 硬编码。
    /// 昼夜周期与游戏时长已解耦为独立字段。
    /// </summary>
    [Serializable]
    public sealed class GameFlowConfigData
    {
        public string configId;
        public float gameDurationSeconds;
        public float dayNightCycleSeconds;
        public float noonLightIntensity;
        public float sunsetLightIntensity;
        public float countdownUrgentThreshold;
        public float settlementDelaySeconds;
        public float endGameUiDelaySeconds;
        public int annexationRecalcDepth;
        public float fogTransitionSpeed;
    }
}
