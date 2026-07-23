using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapGenerationConfig", menuName = "Game/Map Generation Config")]
public class MapGenerationConfigSO : ScriptableObject
{
    [Header("地块尺寸")]
    public float OuterRadius = 3f;
    public float InnerRadius => OuterRadius * 0.866025404f;

    [Header("地块实心区域占比")]
    public float SolidAreaRatio = 0.7f;

    [Header("地图尺寸")]
    public int xNumber;
    public int zNumber;

    [Header("随机生成")]
    public bool useFixedSeed = true;
    public int randomSeed;
    public int generatorVersion = 2;

    [Header("噪声纹理")]
    public Texture2D noiseSource;

    [Header("地图材质")]
    public Material[] mapMaterial;

    [Header("混合纹理")]
    public Texture2D blendMask;

    [Header("混合参数")]
    public float blendSmooth = 1.2f;
    public float blendContrast = 0.4f;
    public float globalSmoothness = 0.15f;

    [Header("河流材质")]
    public Material[] riverMaterial;

    [Header("河流参数")]
    public int minLongestLength = 1;
    public int maxLongestLength = 3;
    public float numerator = 1.0f;
    public float denominator = 90.0f;
    public float RiverSourceGenerationProbability => numerator / denominator;

    [Header("湖海材质")]
    public Material[] lakeOrSeaMaterial;

    [Header("高度范围")]
    [Min(0)] public int minHeight = 0;
    [Min(0)] public int maxHeight = 5;
    [Tooltip("海平面高度阈值：Height <= seaLevel 为水域（整数级差单位，不是世界Y）")]
    public float seaLevel = 1f;
    [Tooltip("视觉水面相对判定水位的层数偏移：水面世界Y = (seaLevel + waterSurfaceOffset) * elevationStep")]
    public float waterSurfaceOffset = 2f;
    [Tooltip("每级高度对应的世界单位（Height→世界Y 的缩放系数）")]
    [Min(0.0001f)] public float elevationStep = 3f;
    [Tooltip("每格竖直扰动占步长的比例，必须 < 0.5 才能保证水下不捅穿、陆地不沉水")]
    [Range(0f, 0.49f)] public float verticalPerturbRatio = 0.4f;

    [Header("台阶参数")]
    [Tooltip("开启后，梯边类型和细分数由实际高度差决定")]
    public bool useHeightBasedSubdivision = true;
    [Tooltip("每多少高度单位算一级台阶")]
    [Min(0.0001f)] public float stepHeight = 1f;
    [Tooltip("单条边台阶细分数上限")]
    [Min(0)] public int maxStepSubdivision = 8;

    [Header("过渡算法")]
    public Enums.TransitionGenerationMode transitionGenerationMode = Enums.TransitionGenerationMode.GenericFan;

    [Header("迷雾材质")]
    public Material fogMaterial;
    [Tooltip("记忆区（探索过·当前无视野）叠加颜色（乘法叠加），默认白色=不染色")]
    public Color fogMemoryColor = Color.white;

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
}
