using System;

namespace GameConfig
{
    /// <summary>
    /// 山体配置（单行，由 game-config.json 导入的只读数据）。
    /// 程序化山脉生成的几何/材质数值；资源引用与材质色阶保留在 MountainConfigSO 手工资产。
    /// </summary>
    [Serializable]
    public sealed class MountainConfigData
    {
        public string configId;
        public int ridgeCount;
        public int minRidgeLength;
        public int maxRidgeLength;
        public float widthRadius;
        public int ridgeMinSpacing;
        public float scoreHeightWeight;
        public float scoreDropWeight;
        public float scoreTurnPenalty;
        public float flatHeightThreshold;
        public float baseHeight;
        public float minHeight;
        public float maxHeight;
        public float heightPerLength;
        public float gamma;
        public float ridgeNoiseAmplitude;
        public float cellNoiseScale;
        public float minVisibleHeight;
        public float maxSlopeRatio;
        public float xzPerturbRatio;
        public float peakEccentricMinRatio;
        public float peakEccentricMaxRatio;
        public bool debugSingleCellAndStraightRidge;
        public int debugStraightRidgeLength;
        public float triplanarWorldScale;
        public float triplanarBlendSharpness;
        public float roughness;
        public float metallic;
        public float shadowStrength;
    }
}
