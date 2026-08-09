using UnityEngine;

/// <summary>
/// 程序化山脉生成配置（决策 ㉔：山专属参数块）。
/// 山脉不是"高度很高的地块"：叠加在地块之上的跨格连续隆起高度场，
/// 不改变格级 Height；本配置负责生成规律（阶段 2）、几何参数（阶段 3）与表现参数（阶段 4）。
/// 本资产只存数据，严禁把运行时创建的材质实例序列化回本资产。
/// </summary>
[CreateAssetMenu(fileName = "MountainConfig", menuName = "Game Data/Map Land Forms/Mountain Config")]
public class MountainConfigSO : ScriptableObject
{
    [Header("地貌引用")]
    [Tooltip("山脉地貌 SO（landForm 占用标记）：modelPrefab/markerPrefab 必须留空、effectType=None、blockBuildingSpawn=true；不入地貌数据库（不参与散落权重池）")]
    public MapLandFormSO mountainLandForm;

    [Header("生成（决策 ⑯/⑱）")]
    [Tooltip("期望山脉数量（每条 = 一条脊线 + 宽度化坡面格）；0 = 不生成山脉")]
    [Min(0)] public int ridgeCount = 3;
    [Tooltip("脊线最短长度（格）")]
    [Min(2)] public int minRidgeLength = 5;
    [Tooltip("脊线最长长度（格）")]
    [Min(2)] public int maxRidgeLength = 12;
    [Tooltip("R：宽度化半径（六边形格距）：t = 1 - d/R；≈1.5 覆盖两侧 1 格")]
    [Min(0.5f)] public float widthRadius = 1.5f;
    [Tooltip("新脊线起点与已有山格的最小六边形距离（格），防止山脉粘连")]
    [Min(0)] public int ridgeMinSpacing = 3;

    [Header("走向评分（决策 ⑰）")]
    [Tooltip("w1：候选邻居高度排名权重（越高分越高）")]
    public float scoreHeightWeight = 1f;
    [Tooltip("w2：两侧落差权重（沿山脊性：候选比走廊两侧高多少）")]
    public float scoreDropWeight = 1f;
    [Tooltip("w3：转向惩罚权重（转弯越大分越低）")]
    public float scoreTurnPenalty = 1f;
    [Tooltip("平坦区高度差阈值（Height 级，决策 ⑱ 陡坡阈值 = 1 级）：候选间最大高差低于此值视为平坦区，噪声随机游走")]
    [Min(0f)] public float flatHeightThreshold = 1f;

    [Header("高度（决策 ⑳/㉔）")]
    [Tooltip("H_base：脊线基准高度（世界单位）")]
    public float baseHeight = 1.2f;
    [Tooltip("H_min：脊线高度下限（世界单位）")]
    public float minHeight = 0.8f;
    [Tooltip("H_max：脊线高度上限（世界单位）")]
    public float maxHeight = 2.5f;
    [Tooltip("k：H_max 随脊线长度增量（每格，世界单位）")]
    public float heightPerLength = 0.12f;
    [Tooltip("γ：垂直脊线衰减指数")]
    public float gamma = 1.2f;
    [Tooltip("沿脊线起伏幅度：H_ridge(s) = H_max * (0.6 + 0.4 * ridgeNoise(s)) 中的 0.4")]
    [Range(0f, 1f)] public float ridgeNoiseAmplitude = 0.4f;
    [Tooltip("每格噪声幅度：noise = hash(格坐标, seed) → ±cellNoiseScale * H_max")]
    [Range(0f, 1f)] public float cellNoiseScale = 0.3f;
    [Tooltip("最小可见隆起（世界单位）：mountainHeight 低于此值不生成山体几何，防微隆起噪点")]
    public float minVisibleHeight = 0.15f;
    [Tooltip("最大坡度：相邻控制点高差上限 = ratio × 格距（防陡薄片）")]
    [Range(0.1f, 2f)] public float maxSlopeRatio = 0.8f;

    [Header("Low-poly（决策 ㉘）")]
    [Tooltip("控制点 XZ 扰动幅度 = ratio × 格内切圆半径")]
    [Range(0f, 0.5f)] public float xzPerturbRatio = 0.15f;
    [Tooltip("主峰偏心量下限 = ratio × 格内切圆半径")]
    [Range(0f, 0.5f)] public float peakEccentricMinRatio = 0.05f;
    [Tooltip("主峰偏心量上限 = ratio × 格内切圆半径")]
    [Range(0f, 0.5f)] public float peakEccentricMaxRatio = 0.2f;

    [Header("调试（绕过正常生成规律，仅对照用；正常代码保留不删）")]
    [Tooltip("勾选后绕过正常生成（ridgeCount/间距/评分行走全部不生效），同图生成两座对照山体：1 个孤立山脉地块 + 1 条严格只占指定格数的直脊线（两者都不宽度化），两者相距较远便于对比；取消勾选即恢复正常生成")]
    public bool debugSingleCellAndStraightRidge = false;
    [Tooltip("调试直脊线长度（格）= 实际山格占地数；调试模式不附加宽度化坡面格")]
    [Min(2)] public int debugStraightRidgeLength = 8;

    [Header("表现（阶段 4：稳定材质契约）")]
    [Tooltip("稳定山体材质资产（Assets/Materials/MountainLowPoly_Fog.mat，阶段 4.2）：运行时克隆实例使用；本资产只保存共享资产引用，严禁把运行时创建的材质实例序列化回任何配置资产；留空 = Shader.Find(Custom/MountainLowPoly_Fog) + Shader 默认值")]
    public Material stableMaterial;
    [Tooltip("色阶 0（岩褐）：最低一档颜色；无纹理时即纯色显示（决策 ㉘ 色阶 3 段起步）")]
    public Color tierColorLow = new Color(0.42f, 0.34f, 0.28f, 1f);
    [Tooltip("色阶 1（灰岩）：中间档颜色")]
    public Color tierColorMid = new Color(0.56f, 0.54f, 0.50f, 1f);
    [Tooltip("色阶 2（浅灰）：最高档颜色")]
    public Color tierColorHigh = new Color(0.72f, 0.71f, 0.68f, 1f);
    [Tooltip("可选岩石纹理（1 张岩石纹理 × 色阶染色；3 纹理方案暂缓）；留空 = 纯色稳定显示")]
    public Texture2D rockTexture;
    [Tooltip("Triplanar 世界空间采样缩放（世界单位/贴图周期）；必须 > 0（决策 ㉗）")]
    [Min(0.01f)] public float triplanarWorldScale = 1f;
    [Tooltip("Triplanar 三轴权重锐度：pow(|worldNormal|, sharpness) 后归一化；必须 ≥ 1")]
    [Min(1f)] public float triplanarBlendSharpness = 4f;
    [Tooltip("表面粗糙度 [0,1]（1 = 最粗糙）")]
    [Range(0f, 1f)] public float roughness = 0.9f;
    [Tooltip("金属度 [0,1]")]
    [Range(0f, 1f)] public float metallic = 0f;
    [Tooltip("阴影强度 [0,1]（1 = 正常阴影）")]
    [Range(0f, 1f)] public float shadowStrength = 1f;
}
