using System;

namespace GameConfig
{
    /// <summary>
    /// 地图资源全局生成参数（单行，由 game-config.json 导入的只读数据）。
    /// </summary>
    [Serializable]
    public sealed class ResourceGlobalConfigData
    {
        public string configId;
        public int emptySpawnWeight;
        public int baseExplorationGold;
    }
}
