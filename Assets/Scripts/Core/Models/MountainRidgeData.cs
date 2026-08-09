using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一条山脉脊线的固化参数快照（决策 ②：山形参数生成时固化）。
/// 生成时创建一次，被该脊线的所有山脉地块（脊线格 + 宽度化格）共享引用；
/// Chunk 重建按本快照派生山体几何，不随当前连通簇实时重算（决策 ㉕ 清除单格不重算整条山）。
/// </summary>
public class MountainRidgeData
{
    /// <summary>脊线唯一 ID（地图内自增）。</summary>
    public int ridgeId;

    /// <summary>该脊线专属确定性种子；所有随机量由 hash(规范化边键 + seed) 派生（决策 ㉓）。</summary>
    public int seed;

    /// <summary>脊线长度（格数）。</summary>
    public int length;

    /// <summary>R：宽度化半径（六边形格距，≈1.5）。高度场 t = 1 - d/R。</summary>
    public float widthRadius;

    /// <summary>γ：垂直脊线衰减指数。</summary>
    public float gamma;

    /// <summary>H_max：已按脊线长度缩放的脊线基准高度（世界单位，决策 ㉔）。</summary>
    public float hMax;

    /// <summary>沿脊线一维值噪声振幅。固化后高度场不再依赖生成配置。</summary>
    public float ridgeNoiseAmplitude;

    /// <summary>格级确定性噪声相对 H_max 的振幅。固化后高度场不再依赖生成配置。</summary>
    public float cellNoiseScale;

    /// <summary>最小可见隆起（世界单位，决策 ⑳）：低于此不生成山体几何，防微隆起噪点。</summary>
    public float minVisibleHeight;

    /// <summary>最大坡度（世界单位，决策 ⑳）：相邻控制点高差上限 = maxSlopeRatio × 格距。</summary>
    public float maxSlope;

    /// <summary>控制点 XZ 扰动幅度（世界单位，决策 ㉘）。</summary>
    public float xzPerturb;

    /// <summary>主峰偏心量下限（世界单位，决策 ㉘）。</summary>
    public float peakEccentricMin;

    /// <summary>主峰偏心量上限（世界单位，决策 ㉘）。</summary>
    public float peakEccentricMax;

    /// <summary>脊线格六边形坐标序列（起点 → 终点；诊断/测试用）。</summary>
    public List<Vector3> ridgeHexes = new List<Vector3>();

    /// <summary>该脊线的山脉地块总数（脊线格 + 宽度化格；诊断/测试用）。</summary>
    public int mountainCellCount;
}
