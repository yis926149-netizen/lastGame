using System;

namespace GameConfig
{
    /// <summary>
    /// 地图生成 Perlin 噪声与竞技场参数（单行，由 game-config.json 导入的只读数据）。
    /// </summary>
    [Serializable]
    public sealed class MapGenConfigData
    {
        public string configId;
        public float perlinFrequency;
        public int perlinOctaves;
        public float perlinPersistence;
        public int arenaRadius;
        public float arenaRiseDurationSeconds;
    }
}
