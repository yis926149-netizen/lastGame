using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public interface IMeshGenerator
{
    // ==============================
    // 地块实心区域
    // ==============================

    /// <summary>
    /// 返回地块实心区域的坐标（包括河道顶点）
    /// </summary>
    List<Vector3> GetSolidAreaVertices(ref HexCellData hexCellData);

    /// <summary>
    /// 设置地块实心区域的UV（包括河道顶点）
    /// </summary>
    List<Vector2> GetSolidAreaVerticesUV(ref HexCellData hexCellData);

    /// <returns></returns>
    /// <summary>
    /// 设置地块实心区域的顶点绘制顺序（无河道地块）
    /// </summary>
    List<int> GetSolidAreaVerticesDrawOrder1(ref HexCellData hexCellData);

    /// <summary>
    /// 设置地块实心区域的顶点绘制顺序（河道始末地块）
    /// </summary>
    List<int> GetSolidAreaVerticesDrawOrder2(ref HexCellData hexCellData, Enums.HexDirection direction);

    /// <summary>
    /// 设置地块实心区域的顶点绘制顺序（河道中流地块）
    /// </summary>
    List<int> GetSolidAreaVerticesDrawOrder3(ref HexCellData hexCellData, Enums.HexDirection incomingDirection, Enums.HexDirection outgoingDirection);

    // ==============================
    // 矩形过渡区域
    // ==============================
    //
    ///////////////////- 坡 -///////////////////

    /// <summary>
    /// 返回矩形过渡区域的顶点坐标
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    /// <returns></returns>
    List<Vector3> GetRectVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 返回矩形过渡区域的uv
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    /// <returns></returns>
    List<Vector2> GetRectUV(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 返回矩形过渡区域的矩形绘制顺序
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    /// <returns></returns>
    List<int> GetRectDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 返回矩形坡河道过渡区域的矩形绘制顺序
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    /// <returns></returns>
    List<int> GetRectSlopeRiverDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    ///////////////////- 阶梯 -///////////////////
    
    /// <summary>
    /// 两边的顶点不变
    /// 中间的点通过插值获取
    /// 插值规律 - 在竖直Y方向上插n个值，在水平X方向上插2n个值，插值均分起始点和终末点
    /// </summary>
    /// <param name="direction"></param>
    /// <returns>获取矩形阶梯的顶点</returns>
    List<Vector3> GetRectStepVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 获取矩形阶梯的uv(新的、简化的方法)
    /// </summary>
    /// <param name="direction">方向</param>
    /// <returns></returns>
    List<Vector2> GetRectStepUV(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 获取矩形阶梯的绘制顺序
    /// </summary>
    /// <param name="direction">方向</param>
    /// <returns></returns>
    List<int> GetRectStepDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 返回矩形矩形河道过渡区域的矩形绘制顺序
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    /// <returns></returns>
    List<int> GetRectStepRiverDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    // ==============================
    // 三角过渡区域（方法三、四等）
    // ==============================
    //
    ///////////////////- 方法一 -///////////////////
    
    /// <summary>
    /// 返回三角过渡区域的顶点坐标
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    /// <returns></returns>
    List<Vector3> GetTriVertices(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    /// <summary>
    /// 返回三角形过渡区域的uv
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    /// <returns></returns>
    List<Vector2> GetTriUV(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    /// <summary>
    /// 返回三角形过渡区域的绘制顺序
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    /// <returns></returns>
    List<int> GetTriDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    ///////////////////- 方法三 -///////////////////

    /// <summary>
    /// 返回三角过渡区域 - 方法3 - 的顶点坐标
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    /// <returns></returns>
    List<Vector3> GetTriStep3Vertices(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    /// <summary>
    /// 返回三角过渡区域 - 方法3 - 的uv
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    /// <returns></returns>
    List<Vector2> GetTriStep3UV(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 返回三角过渡区域 - 方法3 - 的绘制顺序
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    /// <returns></returns>
    List<int> GetTriStep3DrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1);

    ///////////////////- 方法四 -///////////////////

    /// <summary>
    /// 返回三角过渡区域 - 方法4 - 的顶点坐标
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    /// <returns></returns>
    List<Vector3> GetTriStep4Vertices(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    /// <summary>
    /// 返回三角过渡区域 - 方法4 - 的uv - （旧的，复杂的方法）
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param> //顶点排序是梯1、梯2
    List<Vector2> GetTriStep4UV_Old(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 返回三角过渡区域 - 方法4 - 的uv - （新的，简单的方法）
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param> //顶点排序是梯1、梯2
    List<Vector2> GetTriStep4UV(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1);

    /// <summary>
    /// 返回三角过渡区域 - 方法4 - 的绘制顺序
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    /// <returns></returns>
    List<int> GetTriStep4DrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    // ==============================
    // 河流
    // ==============================
    //
    //河水的地块管理：自己的实心区域 + 河流的下游过渡区域 
    //
    ///////////////////- 地块实心区域 -///////////////////

    /// <summary>
    /// 返回地块实心区域的河水坐标
    /// </summary>
    Vector3[] GetRiverVertices(ref HexCellData hexCellData);

    /// <summary>
    ///  返回地块实心区域的河水坐标UV
    /// </summary>
    Vector2[] GetRiverUV(ref HexCellData hexCellData, List<int> l);

    /// <summary>
    /// 获取河水2实心区域的绘制顺序 - (河水始末地块)
    /// </summary>
    /// <param name="direction">方向</param>
    List<int> GetRiverWater2DrawOrder(Enums.HexDirection direction);

    /// <summary>
    /// 获取河水3实心区域的绘制顺序 - (河水中游地块)
    /// </summary>
    List<int> GetRiverWater3DrawOrder(ref HexCellData hexCellData);

    ///////////////////- 矩形区域 -///////////////////
    //河水不分坡和阶梯

    /// <summary>
    /// 返回地块下游过渡区域的河水坐标
    /// </summary>
    List<Vector3> GetOutgoingRiverVertices(ref HexCellData hexCellData, IMapDataService _mapDataService);

    /// <summary>
    ///  返回地块下游过渡区域的河水坐标UV
    /// </summary>
    Vector2[] GetOutgoingRiverSlopUV(ref HexCellData hexCellData);

    /// <summary>
    ///  返回地块下游过渡区域的河水绘制顺序
    /// </summary>
    int[] GetOutgoingRiverSlopDrawOrder(ref HexCellData hexCellData);

    // ==============================
    // 湖海
    // ==============================
    //
    ///////////////////- 实心区域 -///////////////////
    
    /// <summary>
    /// 返回湖或海实心区域坐标
    /// </summary>
    /// <returns></returns>
    Vector3[] GetlakeOrSeaVertices(ref HexCellData hexCellData);

    /// <summary>
    /// 设置湖或海实心区域UV
    /// </summary>
    Vector2[] GetlakeOrSeaUV(ref HexCellData hexCellData);

    /// <returns></returns>
    /// <summary>
    /// 设置湖或海实心区域的顶点绘制顺序
    /// </summary>
    int[] GetlakeOrSeaDrawOrder(ref HexCellData hexCellData);

    ///////////////////- 矩形区域 -///////////////////

    /// <summary>
    /// 返回湖或海矩形过渡区域的顶点坐标
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    List<Vector3> GetlakeOrSeaRectVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 返回湖或海矩形过渡区域的uv
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    List<Vector2> GetlakeOrSeaRectUV(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 返回湖或海矩形过渡区域的矩形绘制顺序
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    List<int> GetlakeOrSeaRectDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    ///////////////////- 三角 -///////////////////

    /// <summary>
    /// 返回湖或海三角过渡区域的顶点坐标
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    List<Vector3> GetlakeOrSeaTriVertices(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    /// <summary>
    /// 返回湖或海三角形过渡区域的uv
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    List<Vector2> GetlakeOrSeaTriUV(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    /// <summary>
    /// 返回湖或海三角形过渡区域的绘制顺序
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    List<int> GetlakeOrSeaTriDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService);

    // ==============================
    // 湖海海岸
    // ==============================
    ///
    ///////////////////- 矩形 -///////////////////

    /// <summary>
    /// 返回海岸矩形过渡区域某个方向的顶点坐标
    /// </summary>
    List<Vector3> GetOneDirectionCoastRectVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 返回海岸矩形过渡区域的uv
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    List<Vector2> GetCoastRectUV(ref HexCellData hexCellData, Vector3[] vertices);

    /// <summary>
    /// 返回海岸矩形过渡区域的矩形绘制顺序
    /// </summary>
    List<int> GetCoastRectDrawOrder(ref HexCellData hexCellData, Vector3[] vertices);

    ///////////////////- 三角 -///////////////////

    ///
    /// <summary>
    /// 返回海岸三角过渡区域某个方向的顶点坐标
    /// </summary>
    List<Vector3> GetOneDirectionCoastTriVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService);

    /// <summary>
    /// 返回海岸三角过渡区域的uv
    /// </summary>
    List<Vector2> GetCoastTriUV(ref HexCellData hexCellData, Vector3[] vertices);


    /// <summary>
    /// 返回海岸三角过渡区域的矩形绘制顺序
    /// </summary>
    List<int> GetCoastTriDrawOrder(ref HexCellData hexCellData, Vector3[] vertices);


    // ==============================
    // 网格线
    // ==============================
    /*
    //这里的六边形网格线只包裹实心区域
    //网格线高度 = 地块中心点高度
    //然后通过渲染队列，使其显示在最上层
    //然后通过透明度控制，使其半透明
    //网格线是一个独立的网格
    */

    /// <summary>
    /// 获取网格线的顶点
    /// </summary>
    List<Vector3> GetGridVertices(ref HexCellData hexCellData);

    /// <summary>
    /// 返回网格线的uv
    /// ∵顶点是固定的，所以uv也可以硬编程
    /// </summary>
    List<Vector2> GetGridUV(ref HexCellData hexCellData);

    /// <summary>
    /// 返回网格线的绘制顺序
    /// </summary>
    List<int> GetGridDrawOrder(ref HexCellData hexCellData);

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
    List<Vector3> GetFogCoverVertices(List<Vector3> vector3s, float increment);
}