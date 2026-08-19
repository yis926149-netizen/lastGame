using System;

namespace GameConfig
{
    /// <summary>
    /// 表现配置（单行，由 game-config.json 导入的只读数据）。
    /// 相机震动、迷雾刷新、卡牌暗淡与天赋选择震屏等表现手感参数。
    /// </summary>
    [Serializable]
    public sealed class FeelConfigData
    {
        public string configId;
        public float fogRefreshInterval;
        public float cameraShakeFrequency;
        public float unaffordableCardDim;
        public float talentScreenShakeStrength;
        public float talentScreenShakeDuration;
    }
}
