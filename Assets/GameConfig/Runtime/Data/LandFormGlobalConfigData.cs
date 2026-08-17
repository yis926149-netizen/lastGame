using System;

namespace GameConfig
{
    /// <summary>
    /// 地图地貌全局生成参数（单行，由 game-config.json 导入的只读数据）。
    /// </summary>
    [Serializable]
    public sealed class LandFormGlobalConfigData
    {
        public string configId;
        public int emptySpawnWeight;
    }
}
