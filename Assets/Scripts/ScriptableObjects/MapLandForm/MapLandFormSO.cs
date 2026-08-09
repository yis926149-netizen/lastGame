using UnityEngine;

/// <summary>
/// 地图地貌（地形）单条配置。
/// 【地图地貌配置化】对齐 MapResourceSO 模式：模型/效果/生成权重全部在此配置。
/// 森林与石头第一版为纯视觉地貌（effectType = None），不提供移动成本/高度字段。
/// </summary>
[CreateAssetMenu(fileName = "MapLandForm", menuName = "Game Data/Map Land Forms/Map Land Form")]
public class MapLandFormSO : ScriptableObject
{
    [Header("显示")]
    [Tooltip("稳定唯一 ID；仅用于日志、编辑器校验和未来存档，不作为本局运行时查找键")]
    public string landFormId;

    [Tooltip("名称")]
    public string landFormName;

    [Tooltip("描述")]
    [TextArea(2, 4)]
    public string description;

    [Header("地图表现")]
    [Tooltip("地图模型预制体；留空 = 不生成模型")]
    public GameObject modelPrefab;

    [Header("效果")]
    [Tooltip("地貌效果类型")]
    public LandFormEffectType effectType;

    [Tooltip("效果参数（按 effectType 取用）")]
    public LandFormEffect effect;

    [Header("规则")]
    [Tooltip("该地貌所在格不可部署建筑（如金矿）；单位部署不受限制")]
    public bool blockBuildingSpawn;

    [Header("程序化山脉")]
    [Tooltip("山脉地貌标记：由 RidgeGenerator 专属 pass 生成，不参与散落/簇生成；必须 modelPrefab/markerPrefab 留空、effectType=None、blockBuildingSpawn=true")]
    public bool mountainForm;

    [Header("生成")]
    [Tooltip("生成权重；0 = 本地貌不生成。簇生成地貌必须保留原权重以锁定散落随机流（同种子下其他地貌位置不变），实际分布由簇逻辑决定")]
    public int spawnWeight = 1;

    [Header("提示图标（可选）")]
    [Tooltip("地貌上方提示图标预制体（World Space Canvas，可复用公共建筑浮标预制体）；留空 = 该地貌不生成提示。仅配置了预制体的地貌（如金矿）会显示")]
    public GameObject markerPrefab;

    [Tooltip("提示图标上显示的图标（markerPrefab 非空时生效）")]
    public Sprite markerIcon;

    [Header("簇生成（可选，仅扎堆类地貌开启，如金矿）")]
    [Tooltip("开启后该地貌不再散落出现，改为固定 clusterCount 堆扎堆生成；其余地貌保持 false")]
    public bool clusterSpawn;

    [Tooltip("堆总数（固定成功数，掷点失败自动重试；地图过小时降级为已选中的堆）")]
    [Min(1)] public int clusterCount = 1;

    [Tooltip("每堆目标格数上限（预算），达到即停止生长；实际大小受填充概率与水域/河流影响")]
    [Min(1)] public int clusterTargetSize = 8;

    [Tooltip("堆生长时每格被填充的概率（0~1）：越大堆越实，越小越碎（形状不规则）")]
    [Range(0.1f, 1f)] public float clusterFillProbability = 0.8f;

    [Tooltip("堆心之间的最小六边形距离（格），防止两堆粘连")]
    [Min(1)] public int clusterMinSpacing = 4;

    [Tooltip("堆生长最大半径（格），防止拉出长条")]
    [Min(1)] public int clusterMaxRadius = 4;
}
