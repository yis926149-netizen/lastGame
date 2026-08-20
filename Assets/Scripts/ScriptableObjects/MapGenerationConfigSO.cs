using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapGenerationConfig", menuName = "Game/Map Generation Config")]
public class MapGenerationConfigSO : ScriptableObject
{
    [System.Serializable]
    public struct ZoneRange
    {
        [Tooltip("起始 z 行号（含）")]
        public int zMin;
        [Tooltip("结束 z 行号（含）")]
        public int zMax;

        public bool Contains(float z) => z >= zMin && z <= zMax;
    }

    [Header("区域划分")]
    [Tooltip("Player 主城可放置的区域（z 坐标范围）")]
    public ZoneRange playerZone = new ZoneRange { zMin = 2, zMax = 9 };

    [Tooltip("中立区域：公共建筑可放置的区域（z 坐标范围）")]
    public ZoneRange neutralZone = new ZoneRange { zMin = 10, zMax = 19 };

    [Tooltip("AI 主城可放置的区域（z 坐标范围）")]
    public ZoneRange aiZone = new ZoneRange { zMin = 20, zMax = 27 };

    [Header("地块尺寸")]
    [Tooltip("六边形地块外接圆半径（世界单位）")]
    public float OuterRadius = 3f;
    public float InnerRadius => OuterRadius * 0.866025404f;

    [Header("地块实心区域占比")]
    [Tooltip("实心部分占外接圆半径的比例，0~1，值越大六边形的填充面越大")]
    [Range(0.2f, 1f)]
    public float SolidAreaRatio = 0.7f;

    [Header("地块顶面轮廓")]
    [Tooltip("实心顶面的边界轮廓：EighteenGon = 每条边 2 个等分点落在内切圆上（圆滑十八边形，默认）；Hexagon = 等分点落在角点连线上（直边六边形）。两种模式顶点数/拓扑一致，仅轮廓不同")]
    public Enums.SolidAreaTopology solidAreaTopology = Enums.SolidAreaTopology.EighteenGon;

    [Header("地图尺寸")]
    [Tooltip("水平方向（X轴）格子数量")]
    public int xNumber;
    [Tooltip("竖直方向（Z轴）格子数量")]
    public int zNumber;

    [Header("随机生成")]
    [Tooltip("使用固定种子以复现相同地图（关闭则每次随机）")]
    public bool useFixedSeed = true;
    [Tooltip("固定随机种子值")]
    public int randomSeed;
    [Tooltip("地图生成器版本号")]
    public int generatorVersion = 2;

    [Header("噪声纹理")]
    [Tooltip("竖直扰动噪声纹理贴图")]
    public Texture2D noiseSource;
    [Tooltip("同一逻辑高度下视觉高程扰动的采样频率。值越大，相邻地块起伏变化越明显")]
    [Range(0.001f, 0.1f)] public float visualPerturbFrequency = 0.003f;

    [Header("地图材质")]
    [Tooltip("地形材质数组，索引顺序：[0]=高地, [1]=平地, [2]=水域")]
    public Material[] mapMaterial;

    [Header("六边形网格覆盖层")]
    [Tooltip("是否显示规则六边形网格覆盖层")]
    public bool showHexGrid = true;
    [Tooltip("可选的网格覆盖材质；为空时运行时使用 Custom/HexGridOverlay")]
    public Material gridMaterial;
    [Tooltip("网格线颜色；最终透明度还会乘 gridAlpha")]
    public Color gridColor = new Color(0.08f, 0.1f, 0.12f, 1f);
    [Range(0f, 1f)]
    [Tooltip("网格线整体透明度")]
    public float gridAlpha = 0.32f;
    [Min(0.001f)]
    [Tooltip("网格线世界空间宽度")]
    public float gridLineWidth = 0.06f;
    [Min(0f)]
    [Tooltip("网格线相对采样地表沿世界 Y 的抬升量")]
    public float gridSurfaceOffset = 0.05f;

    [Header("混合纹理")]
    [Tooltip("地形混合遮罩纹理")]
    public Texture2D blendMask;

    [Header("混合参数")]
    [Tooltip("地形混合平滑度，值越大过渡越柔和")]
    [Range(0.1f, 5f)]
    public float blendSmooth = 1.2f;
    [Tooltip("地形混合对比度")]
    [Range(0f, 1f)]
    public float blendContrast = 0.4f;
    [Tooltip("全局表面光滑度")]
    [Range(0f, 1f)]
    public float globalSmoothness = 0.15f;

    [Header("河流材质")]
    [Tooltip("河流材质数组")]
    public Material[] riverMaterial;

    [Header("河流参数")]
    [Tooltip("河流最短长度（格子数），小于此长度不生成河流")]
    [Min(0)] public int minLongestLength = 1;
    [Tooltip("河流最长长度（格子数）")]
    [Min(0)] public int maxLongestLength = 3;
    [Tooltip("河流源生成概率：分子（详见分母描述）")]
    public float numerator = 1.0f;
    [Tooltip("河流源生成概率：分母。概率 = 分子 / 分母")]
    public float denominator = 90.0f;
    public float RiverSourceGenerationProbability => numerator / denominator;

    [Header("湖海材质")]
    [Tooltip("湖泊/海洋材质数组")]
    public Material[] lakeOrSeaMaterial;

    [Header("高度范围")]
    [Tooltip("最低高度级别（整数），通常为0")]
    [Min(0)] public int minHeight = 0;
    [Tooltip("最高高度级别（整数），值越大地形层数越多")]
    [Min(0)] public int maxHeight = 5;
    [Tooltip("海平面高度阈值：Height <= seaLevel 为水域（整数级差单位，不是世界Y）")]
    public float seaLevel = 1f;
    [Tooltip("视觉水面相对判定水位的层数偏移：水面世界Y = (seaLevel + waterSurfaceOffset) * elevationStep")]
    public float waterSurfaceOffset = 2f;
    [Tooltip("每级高度对应的世界单位（Height→世界Y 的缩放系数）")]
    [Min(0.0001f)] public float elevationStep = 3f;
    [Tooltip("每格竖直扰动占步长的比例，必须 < 0.5 才能保证水下不捅穿、陆地不沉水")]
    [Range(0f, 0.49f)] public float verticalPerturbRatio = 0.4f;

    [Header("高度生成模式")]
    [Tooltip("选择高度生成方式：PerlinNoise = 随机噪声（现有方式），PaletteMap = 颜色图控制")]
    public Enums.HeightGenerationMode heightGenerationMode = Enums.HeightGenerationMode.PerlinNoise;

    [Header("颜色图高度控制（仅 PaletteMap 模式生效）")]
    [Tooltip("颜色→高度映射：\n蓝色 —— 水域（Height ≤ seaLevel）\n绿色 —— 平地（seaLevel < Height < 高低分界）\n橙色 —— 高地（Height ≥ 高低分界）\n\n软边缘处的渐变色自动产生坡面过渡，纯色区块内加随机微扰动")]
    public Texture2D heightPaletteMap;

    [Tooltip("区间内高度随机扰动振幅（整数级差单位），0=无扰动")]
    [Range(0f, 2f)] public float heightNoiseAmplitude = 0.5f;

    [Tooltip("扰动噪声频率，越小越连贯，越大越碎")]
    [Range(0.01f, 0.5f)] public float heightNoiseFrequency = 0.1f;

    [Header("台阶参数")]
    [Tooltip("开启后，梯边类型和细分数由实际高度差决定")]
    public bool useHeightBasedSubdivision = true;
    [Tooltip("每多少高度单位算一级台阶")]
    [Min(0.0001f)] public float stepHeight = 1f;
    [Tooltip("单条边台阶细分数上限")]
    [Min(0)] public int maxStepSubdivision = 8;

    [Header("迷雾材质")]
    [Tooltip("战争迷雾材质")]
    public Material fogMaterial;
    [Tooltip("地图边缘迷雾封皮向外延伸的世界单位宽度")]
    [Min(0f)] public float fogCoverWidth = 27.5f;
    [Tooltip("地图真实边缘下降到迷雾封皮 MinY 平面的斜坡宽度")]
    [Min(0.01f)] public float fogConnectorSlopeWidth = 1f;

    [Header("法线/着色风格对比")]
    [Tooltip("切换后重新进入 Play 模式即可对比不同渲染风格")]
    public Enums.ShadingStyle shadingStyle = Enums.ShadingStyle.FlatAll;

    [Header("迷雾锯齿边界（方案B：世界探索遮罩贴图）")]
    [Tooltip("0=平滑不规则曲线（推荐）；>0=像素阶梯块，值=方块世界边长（越小越细）")]
    public float fogPixelSize = 0f;
    [Tooltip("边界起伏【幅度】(世界单位)：边界在原线两侧摆动多远。越小越贴边、越大越不规则；0=贴合遮罩形状")]
    [Range(0f, 6f)]
    public float fogJaggedAmount = 1.0f;
    [Tooltip("边界起伏【波长】(世界单位)：越小越【碎】(密集小弯)，越大越舒展。想更碎就调小")]
    public float fogNoiseWavelength = 2.0f;
    [Tooltip("探索遮罩每 texel 的世界尺寸——越小越【贴合六边形棱】、越大越平滑圆润。想更贴边就调小")]
    public float fogMaskTexelSize = 0.8f;

    [Header("迷雾边缘风格（下拉框对比）")]
    [Tooltip("切换后重新进入 Play 模式对比不同边界柔化方案。Original=现状")]
    public Enums.FogEdgeStyle fogEdgeStyle = Enums.FogEdgeStyle.Original;
    [Tooltip("边缘柔化宽度（世界单位），仅非 Original 风格生效。建议 0.6~1.2")]
    [Range(0f, 3f)]
    public float fogEdgeSoftness = 0.8f;
    [Tooltip("曲线边界流动速度，仅 WideSmooth 生效。0=静态；越大边界摆动越快。建议 0.1~0.4，过大会让边缘格子明显脉动")]
    [Range(0f, 2f)]
    public float fogEdgeAnimSpeed = 0.25f;

    [Header("程序化山脉")]
    [Tooltip("山脉生成配置（决策 ㉔ 山专属参数块）；留空 = 不生成山脉")]
    public MountainConfigSO mountainConfig;
    [Tooltip("山脉高度全局缩放：乘在 MountainConfig 计算出的 H_max 上（1 = 原始高度，调大更高、调小更矮）。只影响视觉隆起，不改变格级 Height 与玩法规则；有效 H_max 上限 = MountainConfig.maxHeight × 本值")]
    [Min(0.01f)] public float mountainHeightScale = 1f;

    [Header("竞技场（动态地图-阶段二）")]
    [Tooltip("竞技场激活时机（GameTime 秒，暂停冻结）；0 = 开局即突起")]
    public float ArenaActivateTime = 90f;
    [Tooltip("竞技场内 2 环平台高度（Height 级差，必须 > seaLevel 才能脱离水域判定）")]
    [Min(0f)] public float ArenaFloorHeight = 3f;
    [Tooltip("竞技场边界高度兼容字段；当前边界与平台统一使用 ArenaFloorHeight，阻挡由程序化山脉提供")]
    [Min(0f)] public float ArenaWallHeight = 4f;
    [Tooltip("中央宝箱模型（出现即激活，含可被攻击的视觉本体；血条画布运行时构造）")]
    public GameObject centralChestModel;
}
