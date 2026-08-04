using System.Collections.Generic;
using UnityEngine;

public interface IMeshGenerator
{
    // ==============================
    // 地块实心区域（无状态，阶段一）
    // 输出不再写回 HexCellData 渲染缓存；RealCenterWorldCoordinate 由调用方同步。
    // ==============================

    /// <summary>
    /// 无状态构建地块实心区域：44 顶点 + 中心点（不写回 HexCellData）。
    /// </summary>
    SolidAreaMeshData BuildSolidArea(HexCellData hexCellData, IReadOnlyMapView view);

    /// <summary>
    /// 实心区域 UV（44 个，含河道）。
    /// </summary>
    List<Vector2> BuildSolidAreaUV(HexCellData hexCellData);

    /// <summary>
    /// 实心区域顶点绘制顺序（无河道地块）。
    /// </summary>
    List<int> BuildSolidAreaDrawOrder1(HexCellData hexCellData);

    /// <summary>
    /// 实心区域顶点绘制顺序（河道始末地块）。
    /// </summary>
    List<int> BuildSolidAreaDrawOrder2(HexCellData hexCellData, Enums.HexDirection direction);

    /// <summary>
    /// 实心区域顶点绘制顺序（河道中流地块）。
    /// </summary>
    List<int> BuildSolidAreaDrawOrder3(HexCellData hexCellData, Enums.HexDirection incomingDirection, Enums.HexDirection outgoingDirection);

    // ==============================
    // 矩形过渡区域（无状态，阶段一）
    // 跨格依赖（邻居 44 点）经 CellBuildContext.Solids / View 读取。
    // ==============================

    /// <summary>
    /// 矩形过渡区域（坡）顶点坐标。
    /// </summary>
    List<Vector3> BuildRectVertices(CellBuildContext ctx, Enums.HexDirection direction);

    /// <summary>
    /// 矩形过渡区域（坡）uv。
    /// </summary>
    List<Vector2> BuildRectUV(CellBuildContext ctx, Enums.HexDirection direction);

    /// <summary>
    /// 矩形过渡区域（坡）绘制顺序。
    /// </summary>
    List<int> BuildRectDrawOrder(CellBuildContext ctx, Enums.HexDirection direction);

    /// <summary>
    /// 矩形坡河道过渡区域的绘制顺序。
    /// </summary>
    List<int> BuildRectSlopeRiverDrawOrder(CellBuildContext ctx, Enums.HexDirection direction);

    /// <summary>
    /// 矩形过渡区域（阶梯）顶点坐标。
    /// </summary>
    List<Vector3> BuildRectStepVertices(CellBuildContext ctx, Enums.HexDirection direction);

    /// <summary>
    /// 矩形阶梯 uv。
    /// </summary>
    List<Vector2> BuildRectStepUV(CellBuildContext ctx, IReadOnlyList<Vector3> rectVertices);

    /// <summary>
    /// 矩形阶梯绘制顺序。
    /// </summary>
    List<int> BuildRectStepDrawOrder(CellBuildContext ctx, IReadOnlyList<Vector3> rectVertices);

    /// <summary>
    /// 矩形阶梯河道过渡区域的绘制顺序。
    /// </summary>
    List<int> BuildRectStepRiverDrawOrder(CellBuildContext ctx, IReadOnlyList<Vector3> rectVertices);

    // ==============================
    // 三角过渡区域（无状态，阶段一）
    // ==============================

    /// <summary>
    /// 三角过渡区域（方法一）顶点坐标。
    /// </summary>
    List<Vector3> BuildTriVertices(CellBuildContext ctx, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 三角过渡区域（方法一）uv。
    /// </summary>
    List<Vector2> BuildTriUV(CellBuildContext ctx, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 三角过渡区域（方法一）绘制顺序。
    /// </summary>
    List<int> BuildTriDrawOrder(CellBuildContext ctx, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 三角过渡区域（方法三）顶点坐标（依赖本格/邻居矩形过渡顶点组，见 CellBuildContext.RectVertices）。
    /// isSlope 输出方法三的坡边判定（供 BuildTriStep3DrawOrder 使用）。
    /// </summary>
    List<Vector3> BuildTriStep3Vertices(CellBuildContext ctx, Enums.HexDirection direction0, Enums.HexDirection direction1, out int[] isSlope);

    /// <summary>
    /// 三角过渡区域（方法三）uv。
    /// </summary>
    List<Vector2> BuildTriStep3UV(CellBuildContext ctx);

    /// <summary>
    /// 三角过渡区域（方法三）绘制顺序。
    /// </summary>
    List<int> BuildTriStep3DrawOrder(CellBuildContext ctx, int[] isSlope, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 三角过渡区域（方法四）顶点坐标（依赖矩形过渡顶点组）。
    /// </summary>
    List<Vector3> BuildTriStep4Vertices(CellBuildContext ctx, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 三角过渡区域（方法四）uv（新的、简单的方法）。
    /// </summary>
    List<Vector2> BuildTriStep4UV(IReadOnlyList<Vector3> triVertices);

    /// <summary>
    /// 三角过渡区域（方法四）绘制顺序。
    /// </summary>
    List<int> BuildTriStep4DrawOrder(CellBuildContext ctx, IReadOnlyList<Vector3> triVertices, Enums.HexDirection direction0, Enums.HexDirection direction1);

    // ==============================
    // 河流（无状态，阶段一）
    // ==============================

    /// <summary>
    /// 地块实心区域的河水坐标（19 点）。
    /// </summary>
    Vector3[] BuildRiverVertices(CellBuildContext ctx);

    /// <summary>
    /// 地块实心区域的河水坐标 UV。drawOrder 为河水绘制顺序（BuildRiverWater2/3DrawOrder 输出）。
    /// riverVertexCount 为 BuildRiverVertices 输出的顶点数。
    /// </summary>
    Vector2[] BuildRiverUV(CellBuildContext ctx, List<int> drawOrder, int riverVertexCount);

    /// <summary>
    /// 河水实心区域绘制顺序（河水始末地块）。
    /// </summary>
    List<int> BuildRiverWater2DrawOrder(Enums.HexDirection direction);

    /// <summary>
    /// 河水实心区域绘制顺序（河水中游地块）。
    /// </summary>
    List<int> BuildRiverWater3DrawOrder(HexCellData hexCellData);

    /// <summary>
    /// 地块下游过渡区域的河水坐标（4 点；无河流方向时返回 4 个零向量，与旧行为一致）。
    /// </summary>
    List<Vector3> BuildOutgoingRiverVertices(CellBuildContext ctx);

    /// <summary>
    /// 地块下游过渡区域的河水 UV。
    /// </summary>
    Vector2[] BuildOutgoingRiverSlopUV();

    /// <summary>
    /// 地块下游过渡区域的河水绘制顺序。
    /// </summary>
    int[] BuildOutgoingRiverSlopDrawOrder();

    // ==============================
    // 湖海（无状态，阶段一）
    // lake：本格湖海实心 25 点（水面高度）；neighborLake：方向邻居湖海 25 点。
    // ==============================

    /// <summary>
    /// 湖或海实心区域坐标（25 点，水面高度）。
    /// </summary>
    Vector3[] BuildLakeOrSeaVertices(CellBuildContext ctx);

    /// <summary>
    /// 湖或海实心区域 UV。
    /// </summary>
    Vector2[] BuildLakeOrSeaUV();

    /// <summary>
    /// 湖或海实心区域绘制顺序。
    /// </summary>
    int[] BuildLakeOrSeaDrawOrder();

    /// <summary>
    /// 湖或海矩形过渡区域顶点坐标。
    /// </summary>
    List<Vector3> BuildLakeOrSeaRectVertices(CellBuildContext ctx, Enums.HexDirection direction);

    /// <summary>
    /// 湖或海矩形过渡区域 uv。
    /// </summary>
    List<Vector2> BuildLakeOrSeaRectUV(Enums.HexDirection direction);

    /// <summary>
    /// 湖或海矩形过渡区域绘制顺序。
    /// </summary>
    List<int> BuildLakeOrSeaRectDrawOrder(Enums.HexDirection direction);

    /// <summary>
    /// 湖或海三角过渡区域顶点坐标。
    /// </summary>
    List<Vector3> BuildLakeOrSeaTriVertices(CellBuildContext ctx, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 湖或海三角过渡区域 uv。
    /// </summary>
    List<Vector2> BuildLakeOrSeaTriUV(Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 湖或海三角过渡区域绘制顺序。
    /// </summary>
    List<int> BuildLakeOrSeaTriDrawOrder(Enums.HexDirection direction0, Enums.HexDirection direction1);

    // ==============================
    // 湖海海岸（无状态，阶段一）
    // ==============================

    /// <summary>
    /// 海岸矩形过渡区域某个方向的顶点坐标。
    /// </summary>
    List<Vector3> BuildCoastRectVertices(CellBuildContext ctx, Enums.HexDirection direction);

    /// <summary>
    /// 海岸矩形过渡区域 uv。
    /// </summary>
    List<Vector2> BuildCoastRectUV(Vector3[] vertices);

    /// <summary>
    /// 海岸矩形过渡区域绘制顺序。
    /// </summary>
    List<int> BuildCoastRectDrawOrder(Vector3[] vertices);

    /// <summary>
    /// 海岸三角过渡区域某个方向的顶点坐标。
    /// 邻居若为水（isCoast）取 lake 数组，否则取 solid 数组（与旧行为一致）。
    /// </summary>
    List<Vector3> BuildCoastTriVertices(CellBuildContext ctx, Enums.HexDirection direction);

    /// <summary>
    /// 海岸三角过渡区域 uv。
    /// </summary>
    List<Vector2> BuildCoastTriUV(Vector3[] vertices);

    /// <summary>
    /// 海岸三角过渡区域绘制顺序。
    /// </summary>
    List<int> BuildCoastTriDrawOrder(Vector3[] vertices);

    // ==============================
    // 网格线（无状态，阶段一）
    // ==============================

    /// <summary>
    /// 网格线顶点（12 点：外圈 6 + 内圈 6）。
    /// </summary>
    List<Vector3> BuildGridVertices(CellBuildContext ctx);

    /// <summary>
    /// 网格线 uv。
    /// </summary>
    List<Vector2> BuildGridUV();

    /// <summary>
    /// 网格线绘制顺序。
    /// </summary>
    List<int> BuildGridDrawOrder();

    // ==============================
    // 移动显示器的路径连线
    // ==============================

    /// <summary>
    /// 获取两个相邻的地块间连线的顶点
    /// </summary>
    List<Vector3> GetAdjacentHexLineVertices(HexCellData StartHexCellData, HexCellData EndHexCellData);

    /// <summary>
    /// 返回连线的uv
    /// ∵顶点是固定的，所以uv也可以硬编程
    /// </summary>
    List<Vector2> GetAdjacentHexLineUV();

    /// <summary>
    /// 返回连线的绘制顺序
    /// </summary>
    List<int> GetAdjacentHexLineDrawOrder();

    // ==============================
    // 势力范围
    // ==============================

    /// <summary>
    /// 获取一个团块势力范围的顶点
    /// </summary>
    List<List<Vector3>> GetOneSphereOfInfluenceVertices(List<HexCellData> hexCells, out int edgeCount, IMapDataService _mapDataService);

    /// <summary>
    /// 获取一个团块势力范围的顶点（区分"描边地块"与"归属地块"）。
    /// hexCells：真正参与描边的地块（如敌方仅当前可见的部分）；
    /// membershipCells：判定"边是否为势力边界"所用的完整归属集合（如敌方完整势力范围）。
    /// 邻居只要属于 membershipCells，那条边即视为内部边不描——
    /// 从而被迷雾切断的一侧不会画出"假边界"，形成开口图形。
    /// </summary>
    List<List<Vector3>> GetOneSphereOfInfluenceVertices(List<HexCellData> hexCells, ICollection<HexCellData> membershipCells, out int edgeCount, IMapDataService _mapDataService);

    /// <summary>
    /// 提取势力范围边界线段与角点（供实体城墙/城墩渲染使用）。
    /// 复用与 GetOneSphereOfInfluenceVertices 相同的边界判定逻辑，但输出
    /// 结构化的 BoundarySegment（区分 HexEdge / Transition）与去重后的唯一角点集合。
    /// </summary>
    void ExtractSphereOfInfluenceBoundary(
        List<HexCellData> hexCells,
        ICollection<HexCellData> membershipCells,
        IMapDataService _mapDataService,
        List<BoundarySegment> segments,
        List<Vector3> cornerPoints);

    /// <summary>
    /// 获取一段边缘线的绘制UV
    /// ∵顶点是固定的，所以uv也可以硬编程
    /// </summary>
    List<Vector2> GetOneSphereOfInfluenceUV();

    /// <summary>
    /// 返回一段边缘线的绘制顺序
    /// </summary>
    List<int> GetOneSphereOfInfluenceDrawOrder();

    // ==============================
    // 迷雾
    // ==============================

    void GetFogVertices(out List<Vector3> outerBoundary, out List<List<Vector3>> holesVector3, IMapDataService _mapDataService);

    ///////////////////- 迷雾的封边 -///////////////////
    List<Vector3> GetFogCoverVertices(List<Vector3> vector3s, float incrementX, float incrementZ, float uniformHeight);

    ///////////////////- 迷雾连接面片（矩形封皮内边 ↔ 地图真实轮廓 之间的环带）-///////////////////
    void GetFogConnectorBoundaries(out List<Vector3> rectBoundary, out List<Vector3> realOutline,
        out List<Vector3> slopeOuterBoundary, IMapDataService _mapDataService);
}
