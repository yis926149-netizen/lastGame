using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
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
    //地貌类型
    public Enums.LandFormType landFormType = Enums.LandFormType.None;
    public GameObject landFormModel;
    //资源类型
    public Enums.ResourceType resourceType = Enums.ResourceType.None;
    public GameObject resourceModel;
    //对应的六边形网格线
    public GameObject GridMesh;

    //地块的移动力消耗(暂且都设置为1)
    public float movementCost = 1;


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

    //实心顶点组（全部44个点）
    public List<Vector3> SolidAreaVertices = new List<Vector3>();
    //实心7顶点 + 12个分割点组(无扰动)
    public List<Vector3> SolidAreaVerticesWithoutPerturb = new List<Vector3>();
    //实心44绘制顺序
    public List<int> SolidAreaDrawOrder = new List<int>();
    //实心44UV
    public List<Vector2> SolidAreaUV = new List<Vector2>();
    //实心44颜色顶点数组
    public List<Color> SolidAreaColors = new List<Color>();
    //该地块在地形合并Mesh中的实心区域首顶点索引
    public int MeshSolidAreaVertexStartIndex = -1;
    //该地块在地形合并Mesh中的过渡区域顶点范围列表（起始索引，顶点数）
    public List<(int start, int count)> MeshTransitionVertexRanges = new List<(int start, int count)>();
    //该地块在海洋/湖泊合并Mesh中的顶点范围列表（起始索引，顶点数）——用于运行时迷雾更新
    public List<(int start, int count)> MeshWaterVertexRanges = new List<(int start, int count)>();
    //该地块在河流合并Mesh中的顶点范围列表（起始索引，顶点数）——用于运行时迷雾更新
    public List<(int start, int count)> MeshRiverVertexRanges = new List<(int start, int count)>();

    //NE矩形顶点组
    public List<Vector3> NERectVertices = new List<Vector3>();
    //NE矩形绘制顺序
    public List<int> NERectDrawOrder = new List<int>();
    //NE矩形UV
    public List<Vector2> NERectUV = new List<Vector2>();
    //NE矩形颜色顶点数组
    public List<Color> NERectColors = new List<Color>();

    //NE_E三角顶点组
    public List<Vector3> NE_ETriVertices = new List<Vector3>();
    //NE_E三角绘制顺序
    public List<int> NE_ETriDrawOrder = new List<int>();
    //NE_E三角UV
    public List<Vector2> NE_ETriUV = new List<Vector2>();
    //NE_E三角颜色顶点数组
    public List<Color> NE_ETriColors = new List<Color>();

    //E矩形顶点组
    public List<Vector3> ERectVertices = new List<Vector3>();
    //E矩形绘制顺序
    public List<int> ERectDrawOrder = new List<int>();
    //E矩形UV
    public List<Vector2> ERectUV = new List<Vector2>();
    //E矩形颜色顶点数组
    public List<Color> ERectColors = new List<Color>();

    //E_SE三角顶点组
    public List<Vector3> E_SETriVertices = new List<Vector3>();
    //E_SE三角绘制顺序
    public List<int> E_SETriDrawOrder = new List<int>();
    //E_SE三角UV
    public List<Vector2> E_SETriUV = new List<Vector2>();
    //E_SE三角颜色顶点数组
    public List<Color> E_SETriColors = new List<Color>();

    //SE矩形顶点组
    public List<Vector3> SERectVertices = new List<Vector3>();
    //SE矩形绘制顺序
    public List<int> SERectDrawOrder = new List<int>();
    //SE矩形UV
    public List<Vector2> SERectUV = new List<Vector2>();
    //SE矩形颜色顶点数组
    public List<Color> SERectColors = new List<Color>();

    //矩形、三角形过渡区域阶梯的分段数
    public int interpCount = 1;
    //矩形阶梯的uv那个Δx, 元素顺序对应不同方向 - NE{倾斜，水平}、E{倾斜，水平}、SE{倾斜，水平}
    public float[,] x = new float[3, 2] { { 0, 0 }, { 0, 0 }, { 0, 0 } };
    //三角形过渡区域，方法三，那条边是坡(NE_E、E_SE)
    public int[] isSlope = new int[2] { -1, -1 };

    //河水深度 ∈ (0，1]，1为与河岸齐平，0为河道干涸
    public float RiverWaterDepth = 0.7f;
    //实心区域河流顶点组
    public List<Vector3> RiverVertices = new List<Vector3>();
    //实心区域河流绘制顺序
    public List<int> RiverDrawOrder = new List<int>();
    //实心区域河流UV
    public List<Vector2> RiverUV = new List<Vector2>();

    //过渡区域河流顶点组
    public List<Vector3> OutgoingRiverVertices = new List<Vector3>();
    //过渡区域河流绘制顺序
    public List<int> OutgoingRiverDrawOrder = new List<int>();
    //过渡区域河流UV
    public List<Vector2> OutgoingRiverUV = new List<Vector2>();

    //水位高度 - 若水位大于海拔，则形成湖或海
    public float lakeOrSeaWaterLevel = 2;
    // 该格所属水体的水面高度（Height 单位），由 MapRenderer 从 seaLevel 配置填入
    public float waterLevel;
    // 是否为海岸地块
    public bool isCoast;
    //湖或海实心区域顶点组
    public List<Vector3> lakeOrSeaVertices = new List<Vector3>();
    //湖或海实心区域绘制顺序
    public List<int> lakeOrSeaDrawOrder = new List<int>();
    //湖或海实心区域UV
    public List<Vector2> lakeOrSeaUV = new List<Vector2>();

    //湖或海NE矩形过渡区域顶点组
    public List<Vector3> lakeOrSeaNERectVertices = new List<Vector3>();
    //湖或海NE矩形过渡区域绘制顺序
    public List<int> lakeOrSeaNERectDrawOrder = new List<int>();
    //湖或海NE矩形过渡区域UV
    public List<Vector2> lakeOrSeaNERectUV = new List<Vector2>();

    //湖或海E矩形过渡区域顶点组
    public List<Vector3> lakeOrSeaERectVertices = new List<Vector3>();
    //湖或海E矩形过渡区域绘制顺序
    public List<int> lakeOrSeaERectDrawOrder = new List<int>();
    //湖或海E矩形过渡区域UV
    public List<Vector2> lakeOrSeaERectUV = new List<Vector2>();

    //湖或海SE矩形过渡区域顶点组
    public List<Vector3> lakeOrSeaSERectVertices = new List<Vector3>();
    //湖或海SE矩形过渡区域绘制顺序
    public List<int> lakeOrSeaSERectDrawOrder = new List<int>();
    //湖或海SE矩形过渡区域UV
    public List<Vector2> lakeOrSeaSERectUV = new List<Vector2>();

    //湖或海NE_E三角过渡区域顶点组
    public List<Vector3> lakeOrSeaNE_ETriVertices = new List<Vector3>();
    //湖或海NE_E三角过渡区域绘制顺序
    public List<int> lakeOrSeaNE_ETriDrawOrder = new List<int>();
    //湖或海NE_E三角形过渡区域UV
    public List<Vector2> lakeOrSeaNE_ETriUV = new List<Vector2>();

    //湖或海E_SE三角过渡区域顶点组
    public List<Vector3> lakeOrSeaE_SETriVertices = new List<Vector3>();
    //湖或海E_SE三角过渡区域绘制顺序
    public List<int> lakeOrSeaE_SETriDrawOrder = new List<int>();
    //湖或海E_SE三角过渡区域UV
    public List<Vector2> lakeOrSeaE_SETriUV = new List<Vector2>();

    //海岸矩形过渡区域顶点组
    public List<Vector3> CoastRectVertices = new List<Vector3>();
    //海岸矩形过渡区域绘制顺序
    public List<int> CoastRectDrawOrder = new List<int>();
    //海岸矩形过渡区域UV
    public List<Vector2> CoastRectUV = new List<Vector2>();

    //海岸三角过渡区域顶点组
    public List<Vector3> CoastTriVertices = new List<Vector3>();
    //海岸三角过渡区域绘制顺序
    public List<int> CoastTriDrawOrder = new List<int>();
    //海岸三角过渡区域UV
    public List<Vector2> CoastTriUV = new List<Vector2>();

    //网格线顶点组
    public List<Vector3> GridVertices = new List<Vector3>();
    //网格线绘制顺序
    public List<int> GridDrawOrder = new List<int>();
    //网格线UV
    public List<Vector2> GridUV = new List<Vector2>();

    //该地块归属
    public KeyValuePair<int, int> Player_City_Index = new KeyValuePair<int, int>(-1, -1);

    //该地块是否被玩家探索
    public bool IsExplored { get; private set; }

    //该地块当前是否在己方视野内（三态记忆迷雾：每回合/每次行动重算，反复 true↔false）。
    // 未探索=!IsExplored；记忆区=IsExplored&&!IsVisible；可见=IsVisible。
    public bool IsVisible;

    //该地块的建筑类型
    public KeyValuePair<Enums.BulidingType, GameObject> BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(Enums.BulidingType.NoBuilding, null);

    //该地块上是否存在单位
    private KeyValuePair<bool, GameObject> HaveUnit = new KeyValuePair<bool, GameObject>(false, null);

    public HexCellData(Enums.HexType HexType, int GenerateOrder, Vector3 HexCoordinate, Vector3 CenterWorldCoordinate, float Height)
    {
        this.HexType = HexType;
        this.GenerateOrder = GenerateOrder;
        this.HexCoordinate = HexCoordinate;
        this.CenterWorldCoordinate = CenterWorldCoordinate;
        this.Height = Height;
        //测试迷雾
        IsExplored = false;

        //Debug.Log("IsExplored：" + IsExplored);
        //测试用
        //单位不能下海
        if (WaterLevelConfig.IsWater(this))
        {          
            movementCost = float.MaxValue;
            
        }
        //单位不能走未探索的路
        if (!IsExplored)
        {
            movementCost = float.MaxValue;
        }       
    }

    //设置该地块已探索
    public void ExploreThisHexCell()
    {
        IsExplored = true;

        //普通地块
        movementCost = 1;
        //地貌有树林：移动力 + 1
        if (landFormType == Enums.LandFormType.Forest) { movementCost += 1; }

        //湖不能进去
        if (HexType == Enums.HexType.LakeOrSea) { movementCost = float.MaxValue; }
        //进攻、防御建筑不能路过，不能停留
        if (BulidingTypeOnHex_Building.Key == Enums.BulidingType.AttackStatue || BulidingTypeOnHex_Building.Key == Enums.BulidingType.DefenseStatue) { movementCost = float.MaxValue; }

        //物体显隐不再在此处零散处理：统一由 MapRenderer.SyncCellObjectVisibility
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

    //获取地块上的资源
    public Enums.ResourceType GetResource()
    {
        return resourceType;
    }

    //收割地块上的资源
    public void ReapResource()
    {
        resourceType = Enums.ResourceType.None;
    }

    //获取地块上的地貌
    public Enums.LandFormType GetLandForm()
    {
        return landFormType;
    }
}