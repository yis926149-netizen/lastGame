using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：储存与地图生成相隔的地块数据
//****************************************

public class HexCellData
{
    //地块类型
    public Enums.HexType HexType;
    //地形类型
    public Enums.TerrainType terrainType;
    //【地图地貌配置化】地块上的地貌配置（SO 引用，单一事实源；null = 无地貌）
    public MapLandFormSO landForm;
    public GameObject landFormModel;
    // 【地图资源配置化】地块上的资源配置（SO 引用，单一事实源）
    public MapResourceSO resource;
    public GameObject resourceModel;
    //地块的移动力消耗(暂且都设置为1)
    public float movementCost = 1;

    // ── 【程序化山脉】山脉地块数据（决策 ①/②/⑯/㉕）──────────────
    // 山脉占用标记 = landForm == MountainConfigSO.mountainLandForm（modelPrefab/markerPrefab 留空，
    // effectType=None、blockBuildingSpawn=true）；mountainRidge 为生成时固化的脊线参数快照（共享引用）。
    /// <summary>所属脊线的固化参数快照；null = 本格不是山脉地块。</summary>
    public MountainRidgeData mountainRidge;
    /// <summary>山脉地块角色：脊线格 / 宽度化坡面格。</summary>
    public Enums.MountainRidgeStatus mountainRidgeStatus = Enums.MountainRidgeStatus.None;
    /// <summary>脊线格两端通过方向（无向，决策 ⑯；端点只有一端有效）。</summary>
    public Enums.HexDirection RidgeDirectionA = Enums.HexDirection.None;
    /// <summary>脊线格另一端通过方向（无向；端点 = None）。</summary>
    public Enums.HexDirection RidgeDirectionB = Enums.HexDirection.None;
    /// <summary>格中心到脊线最近投影的 XZ 距离（格距；脊线格 = 0，决策 ㉑）。高度场派生输入。</summary>
    public float mountainDistToRidge;
    /// <summary>沿脊线位置 s（格距，0 = 脊线起点）。沿脊线起伏的派生输入。</summary>
    public float mountainPosAlongRidge;
    /// <summary>该山脉地块是否已被永久清除（决策 ㉕：清除后不恢复，重建时跳过）。</summary>
    public bool mountainCleared;


    //地块的六边形坐标
    public Vector3 HexCoordinate;

    //地块中心的世界坐标
    public Vector3 CenterWorldCoordinate;
    //地块中心的真实世界坐标
    public Vector3 RealCenterWorldCoordinate;

    //地块生成顺序(序号)
    public int GenerateOrder;

    //地块高度
    public float Height;

    //河道深度
    public float RiverDepth = -0.75f;
    // 是否有河流进入
    public bool hasRiverIncoming = false;
    // 是否有河流出去
    public bool hasRiverOutgoing = false;

    // 河流进入方向
    public Enums.HexDirection RiverIncomingDirection = Enums.HexDirection.None;

    // 河流出去方向
    public Enums.HexDirection RiverOutgoingDirection = Enums.HexDirection.None;

    //有无河流流经
    public bool hasRiver = false;

    //矩形、三角形过渡区域阶梯的分段数
    public int interpCount = 1;
    //矩形阶梯的uv那个Δx, 元素顺序对应不同方向 - NE{倾斜，水平}、E{倾斜，水平}、SE{倾斜，水平}
    public float[,] x = new float[3, 2] { { 0, 0 }, { 0, 0 }, { 0, 0 } };
    //三角形过渡区域，方法三，那条边是坡(NE_E、E_SE)
    public int[] isSlope = new int[2] { -1, -1 };

    //河水深度 ∈ (0，1]，1为与河岸齐平，0为河道干涸
    public float RiverWaterDepth = 0.7f;

    //水位高度 - 若水位大于海拔，则形成湖或海
    public float lakeOrSeaWaterLevel = 2;
    // 该格所属水体的水面高度（Height 单位），由 ChunkMapRenderer 从 seaLevel 配置填入
    public float waterLevel;
    // 是否为海岸地块
    public bool isCoast;

    //该地块归属
    public KeyValuePair<int, int> Player_City_Index = new KeyValuePair<int, int>(-1, -1);

    //该地块是否被探索（位掩码：bit 0=玩家, bit 1=AI）
    //保留旧属性兼容现有调用方，默认查询阵营0（玩家）
    private int _exploredMask;
    public bool IsExplored => IsExploredBy(0);
    public bool IsExploredBy(int factionId) => (_exploredMask & (1 << factionId)) != 0;
    public void ExploreBy(int factionId) => _exploredMask |= (1 << factionId);

    // 【探索奖励预生成】地图生成时固化；null 表示该格没有待结算奖励。
    private ExplorationRewardData _explorationReward;
    public ExplorationRewardData ExplorationReward => _explorationReward;
    public void SetExplorationReward(ExplorationRewardData reward) => _explorationReward = reward;
    public ExplorationRewardData TakeExplorationReward()
    {
        ExplorationRewardData reward = _explorationReward;
        _explorationReward = null;
        return reward;
    }

    // 【探索重构-阶段6】IsVisible 已移除。地图全可见，只保留 IsExplored（是否已探索/占领）。

    // 【迷雾过渡】迷雾透明度：0=完全迷雾遮挡，1=完全清晰显示。逐帧过渡到目标值。
    public float FogAlpha { get; set; }
    public float FogAlphaTarget { get; set; }

    // 【公共建筑系统-决策#42】该地块是否不可探索（公共建筑占位格+周围一环）
    // 这些地块只能通过占领公共建筑获得，不能通过探索系统主动探索
    public bool IsUnexplorable { get; set; }

    //该地块的建筑类型
    public KeyValuePair<Enums.BulidingType, GameObject> BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(Enums.BulidingType.NoBuilding, null);

    // 【公共建筑系统-决策#29】若本格是公共建筑的占位格（根格或子格），存根格 Controller 引用；
    // 普通格保持 null。子格被攻击时通过此引用转发到根格，实现多格共享一份 HP。
    public PublicBuildingBase publicBuildingRoot = null;

    //该地块上是否存在单位（旧字段，与 Occupant 并存，不删）
    private KeyValuePair<bool, GameObject> HaveUnit = new KeyValuePair<bool, GameObject>(false, null);

    // ── 【批次 D】实时占用系统 ───────────────────────────────────
    // Occupant：格子主人（普通移动占用）。一格一主，主人为 null 表示空格。
    // AttackerSlots：6 方向进攻槽。近战单位进入目标格时占一个方向槽，允许同格共存（最多 6 个攻击者）。
    private GameObject _occupant;
    private readonly GameObject[] _attackerSlots = new GameObject[6];

    /// <summary>格子当前是否有移动主人（普通占用）。</summary>
    public bool HasOccupant() => _occupant != null;

    /// <summary>获取格子移动主人。</summary>
    public GameObject GetOccupant() => _occupant;

    /// <summary>设置格子移动主人。传 null 表示释放。</summary>
    public void SetOccupant(GameObject unit) => _occupant = unit;

    /// <summary>
    /// 尝试占用指定方向的进攻槽（0-5）。
    /// 槽为空则写入 unit 返回 true；已被其他单位占用则返回 false。
    /// </summary>
    public bool TryClaimAttackerSlot(int dir, GameObject unit)
    {
        if (dir < 0 || dir >= 6) return false;
        if (_attackerSlots[dir] != null && _attackerSlots[dir] != unit) return false;
        _attackerSlots[dir] = unit;
        return true;
    }

    /// <summary>释放指定方向的进攻槽。</summary>
    public void ReleaseAttackerSlot(int dir)
    {
        if (dir >= 0 && dir < 6) _attackerSlots[dir] = null;
    }

    /// <summary>【动态地图-阶段二】释放本格上属于指定单位的所有进攻槽（弹射迁移用）。</summary>
    public void ReleaseAttackerSlots(GameObject unit)
    {
        if (unit == null) return;
        for (int i = 0; i < _attackerSlots.Length; i++)
        {
            if (_attackerSlots[i] == unit)
                _attackerSlots[i] = null;
        }
    }

    /// <summary>
    /// 根据两个六边形坐标计算进攻方向槽（0-5，对应 NE/E/SE/SW/W/NW）。
    /// 从 fromHex 进入 toHex 时的方向。匹配失败返回 -1。
    /// </summary>
    public static int GetAttackerSlotDirection(Vector3 fromHex, Vector3 toHex)
    {
        Vector3 delta = toHex - fromHex;
        // 方向偏移与 HexMapService.GetNeighbor 中的偏移一致
        if (delta == new Vector3(0, -1, 1))  return 0; // NE
        if (delta == new Vector3(1, -1, 0))  return 1; // E
        if (delta == new Vector3(1, 0, -1))  return 2; // SE
        if (delta == new Vector3(0, 1, -1))  return 3; // SW
        if (delta == new Vector3(-1, 1, 0))  return 4; // W
        if (delta == new Vector3(-1, 0, 1))  return 5; // NW
        return -1;
    }

    public HexCellData(Enums.HexType HexType, int GenerateOrder, Vector3 HexCoordinate, Vector3 CenterWorldCoordinate, float Height)
    {
        this.HexType = HexType;
        this.GenerateOrder = GenerateOrder;
        this.HexCoordinate = HexCoordinate;
        this.CenterWorldCoordinate = CenterWorldCoordinate;
        this.Height = Height;
        //测试迷雾
        _exploredMask = 0;
        FogAlpha = 0f;
        FogAlphaTarget = 0f;

        //Debug.Log("IsExplored：" + IsExplored);
        //测试用
        //单位不能下海
        if (WaterLevelConfig.IsWater(this))
        {          
            movementCost = float.MaxValue;
        }
        // 【探索重构-阶段2】未探索格不再通过 movementCost 隐式阻挡寻路（方案C：探索不限制移动）
        // 【地图地貌配置化】森林移动惩罚已取消（原构造内判断因 landForm 尚未赋值而从未生效）
    }

    //设置该地块已探索
    // 【探索重构-阶段3】访问权改为 internal，强制通过 IExplorationService.TryExplore 调用
    internal void ExploreThisHexCell()
    {
        ExploreBy(0);

        // 【探索重构-阶段2】movementCost 不再随探索状态改变：
        //  - 构造时已按实际地形计算（普通1/森林2/水域MaxValue）
        //  - 建筑放置（AttackStatue/DefenseStatue）由 CardPresenter/AIEntityFactory 各自设置为 MaxValue
        //  - 探索不改变寻路权重，与单位移动完全解耦

        // 物体显隐不再在此处零散处理，由地图表现层统一维护。
        //（OnMapVisualChanged 事件驱动，按"归属×三态"规则集中同步）。
    }

    //设置该地块是否有单位
    public void SetHaveUnit(bool haveUnit, GameObject Unit)
    {
        HaveUnit = new KeyValuePair<bool, GameObject>(haveUnit, Unit);
    }

    //获取该地块是否有单位
    public bool IsHaveUnit()
    {
        return HaveUnit.Key;
    }

    //获取该地块上的单位
    public GameObject GetUnit()
    {
        return HaveUnit.Value;
    }

    //获取地块上的资源（【地图资源配置化】TakeResource 原子式取走并清空，防止重复结算）
    public MapResourceSO TakeResource()
    {
        MapResourceSO taken = resource;
        resource = null;
        return taken;
    }
}
