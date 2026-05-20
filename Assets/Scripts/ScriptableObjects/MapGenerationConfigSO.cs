using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapGenerationConfig", menuName = "Game/Map Generation Config")]
public class MapGenerationConfigSO : ScriptableObject
{
    [Header("地块尺寸")]
    public float OuterRadius = 3f;
    // InnerRadius 可以根据 OuterRadius 计算得出，不暴露为字段，提供只读属性
    public float InnerRadius => OuterRadius * 0.866025404f;

    [Header("地块实心区域占比")]
    public float SolidAreaRatio = 0.7f;

    [Header("地图尺寸")]
    public int xNumber;
    public int zNumber;

    [Header("顶点扰动用噪声图")]
    public Texture2D noiseSource;

    [Header("地图材质组")]
    public Material[] mapMaterial;

    [Header("过渡区域遮罩图")]
    public Texture2D blendMask;

    [Header("过渡区域混合效果配置")]
    public float blendSmooth = 1.2f;
    public float blendContrast = 0.4f;
    public float globalSmoothness = 0.15f;

    [Header("河流材质")]
    public Material[] riverMaterial;

    [Header("一条河流配置")]
    public int minLongestLength = 1;
    public int maxLongestLength = 3;
    public float numerator = 1.0f;
    public float denominator = 90.0f;

    // 河流生成概率自动计算，保留为只读属性
    public float RiverSourceGenerationProbability => numerator / denominator;

    [Header("湖或海材质")]
    public Material[] lakeOrSeaMaterial;

    // 如果还有其它运行时数据（如 MapMesh），建议移到专门的管理类中
}