using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的探索奖励配置数据库 SO（只读，单行）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class ExplorationRewardConfigDatabaseSO : ScriptableObject
    {
        [SerializeField] private ExplorationRewardConfigData config;

        public ExplorationRewardConfigData Config => config;

        public void ReplaceAll(ExplorationRewardConfigData[] data)
        {
            config = (data != null && data.Length > 0) ? data[0] : null;
        }
    }
}
