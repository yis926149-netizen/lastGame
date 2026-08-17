using System;

namespace GameConfig
{
    /// <summary>
    /// 天赋抽卡偏好规则（由 game-config.json 导入的只读数据）。
    /// 消除“由天赋 ID 隐式推断抽卡偏好”的串线：偏好由 triggerTalentId → targetCardType 显式声明。
    /// </summary>
    [Serializable]
    public sealed class TalentDrawRuleData
    {
        public string ruleId;
        public string triggerTalentId;
        public string targetCardType;
        public int weightMultiplier;
        public bool enabled;
    }
}
