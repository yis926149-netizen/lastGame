using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;

public class MeshGeneratorService : IMeshGenerator
{
    private readonly IMapDataService _mapDataService;
    private readonly MapGenerationConfigSO _config; 

    public MeshGeneratorService(IMapDataService mapDataService, MapGenerationConfigSO config)
    {
        _mapDataService = mapDataService;
        _config = config;
    }

    /////////////////////////////////////////////////////////////////////- 地块实心区域 -/////////////////////////////////////////////////////////////////////
    /*
    地块实心区域的种类：
    1、无河道地块
    2、河道始末地块
    3、河道中流地块 - 出入方向相邻、出入方向不相邻

    1、无河道地块有 1 种情况
    2、河道始末地块有 6 种情况
    3、河道中流地块
           |——出入方向相邻：12种情况 - 每个方向有正反(6 * 2)
           |——出入方向相差2：12种情况 - 每个方向有正反(6 * 2)
           |——贯穿：3种情况
    共有34种情况

    顶点组、uv组是共用的；
    区别在于如何绘制三角形；
    */
    /// <summary>
    /// 返回地块实心区域的坐标（包括河道顶点）
    /// </summary>
    public List<Vector3> GetSolidAreaVertices(ref HexCellData hexCellData)
    {
        //实心区域顶点坐标 
        //根据每个地块的中心点世界坐标生成 - 对应地块的其他6个坐标
        float x = hexCellData.CenterWorldCoordinate.x;
        // 唯一的地表 Height→世界Y 换算点：乘 elevationStep 把整数高度映射到世界坐标
        float y = hexCellData.CenterWorldCoordinate.y + hexCellData.Height * _config.elevationStep;
        //float y = hexCellData.Height;
        float z = hexCellData.CenterWorldCoordinate.z;
        float o = _config.OuterRadius;
        float i = _config.InnerRadius;
        float s = _config.SolidAreaRatio;
        //本体六边形的7个点 + 12个分割点(无扰动)
        //中心点(无扰动) 
        Vector3 zero = new Vector3(x, y, z);
        Vector3[] arrSolidAreaVerticesWithoutPerturb = new Vector3[]
        {
            //0点(地块中心点)
            zero,
            //1点(无扰动)
            new Vector3(x, y, z + o * s),
            //2点(无扰动)
            new Vector3(x + i * s, y, z + 0.5f * o * s),
            //3点(无扰动)
            new Vector3(x + i * s, y, z - 0.5f * o * s),
            //4点(无扰动)
            new Vector3(x, y, z - o * s),
            //5点(无扰动)
            new Vector3(x - i * s, y, z - 0.5f * o * s),
            //6点(无扰动)
            new Vector3(x - i * s, y, z + 0.5f * o * s),

            //分割边缘的12个新点（每条边多2个等分点）- r == o(更像圆)
            //分割边缘的12个新点（每条边多2个等分点）- r == i(更像六边形) - （选了这个）
            //7点(无扰动)
            zero + new Vector3(i * s * Mathf.Cos(Mathf.PI * 7 / 18), 0, i * s * Mathf.Sin(Mathf.PI * 7 / 18)),
            //8点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * 5 / 18), 0, i*s * Mathf.Sin(Mathf.PI * 5 / 18)),
            //9点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * 1 / 18), 0, i*s * Mathf.Sin(Mathf.PI * 1 / 18)),
            //10点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * -1 / 18), 0, i*s * Mathf.Sin(Mathf.PI * -1 / 18)),
            //11点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * -5 / 18), 0, i*s * Mathf.Sin(Mathf.PI * -5 / 18)),
            //12点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * -7 / 18), 0, i*s * Mathf.Sin(Mathf.PI * -7 / 18)),
            //13点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * -11 / 18), 0, i*s * Mathf.Sin(Mathf.PI * -11 / 18)),
            //14点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * -13 / 18), 0, i*s * Mathf.Sin(Mathf.PI * -13 / 18)),
            //15点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * -17 / 18), 0, i*s * Mathf.Sin(Mathf.PI * -17 / 18)),
            //16点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * 17 / 18), 0, i*s * Mathf.Sin(Mathf.PI * 17 / 18)),
            //17点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * 13 / 18), 0, i*s * Mathf.Sin(Mathf.PI * 13 / 18)),
            //18点(无扰动)
            zero + new Vector3(i*s * Mathf.Cos(Mathf.PI * 11 / 18), 0, i*s * Mathf.Sin(Mathf.PI * 11 / 18)),
        };
        hexCellData.SolidAreaVerticesWithoutPerturb.AddRange(arrSolidAreaVerticesWithoutPerturb);

        //比例值(不知道为什么算错了，但总而言之先让比例值减半吧) - (减半之后好像是对的，但原因先不管了)
        float ratio = ((hexCellData.SolidAreaVerticesWithoutPerturb[7] - hexCellData.SolidAreaVerticesWithoutPerturb[1]).magnitude / ((hexCellData.SolidAreaVerticesWithoutPerturb[2] - hexCellData.SolidAreaVerticesWithoutPerturb[1]) / 2).magnitude) / 2;
        //全扰动 + 地块整体高程扰动       
        Vector3 Y_Perturb = HexMetrics.PerturbY2(zero);
        //位移向量
        Vector3[] displacementVector = new Vector3[]
        {
            //位移向量1
            (HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[1]) - HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0])) * ratio,
            //位移向量2
            (HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[2]) - HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0])) * ratio,
            //位移向量3
            (HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[3]) - HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0])) * ratio,
            //位移向量4
            (HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[4]) - HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0])) * ratio,
            //位移向量5
            (HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[5]) - HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0])) * ratio,
            //位移向量6
            (HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[6]) - HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0])) * ratio,
        };
        //河道高度偏移
        Vector3 RiverOffset = new Vector3(0, hexCellData.RiverDepth, 0);

        //实心区域（包含河道）44个点
        Vector3[] arrVertices = new Vector3[]
        {
            ///*
            //本体六边形的7个点
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[1]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[2]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[3]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[4]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[5]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[6]) + Y_Perturb,
            //分割边缘的12个新点
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[7]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[8]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[9]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[10]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[11]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[12]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[13]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[14]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[15]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[16]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[17]) + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[18]) + Y_Perturb,
            //*/
            
            /*
            //本体六边形的7个点
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[0]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[1]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[2]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[3]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[4]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[5]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[6]),
            //分割边缘的12个新点
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[7]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[8]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[9]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[10]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[11]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[12]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[13]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[14]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[15]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[16]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[17]),
            HexMetrics.Perturb(SolidAreaVerticesWithoutPerturb[18]),
            */

            //河道的25个点：[同平面的6个河道点](顺时针排序) - [河道底部的7个点](含0'点顺时针排序) - [河道底部拆分的12个点](顺时针排序)
            //同平面除0点的6个河道点：地块中心点 + 位移向量
            //位移向量：外径向量 * 比例值
            //比例值：模(点7-点1)/(模(点2-点1)/2)
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0]) + displacementVector[0] + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0]) + displacementVector[1] + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0]) + displacementVector[2] + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0]) + displacementVector[3] + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0]) + displacementVector[4] + Y_Perturb,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0]) + displacementVector[5] + Y_Perturb,
            //河道底部的7个点(含0'点): 就前面的点 + 河道深度
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[0]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(zero + displacementVector[0]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(zero + displacementVector[1]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(zero + displacementVector[2]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(zero + displacementVector[3]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(zero + displacementVector[4]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(zero + displacementVector[5]) + Y_Perturb + RiverOffset,
            //河道底部拆分的12个点：就前面的点 + 河道深度
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[7]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[8]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[9]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[10]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[11]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[12]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[13]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[14]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[15]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[16]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[17]) + Y_Perturb + RiverOffset,
            HexMetrics.Perturb(hexCellData.SolidAreaVerticesWithoutPerturb[18]) + Y_Perturb + RiverOffset,

        };
        hexCellData.SolidAreaVertices.AddRange(arrVertices);
        hexCellData.RealCenterWorldCoordinate = arrVertices[0];

        return arrVertices.ToList();
    }

    /// <summary>
    /// 设置地块实心区域的UV（包括河道顶点）
    /// </summary>
    public List<Vector2> GetSolidAreaVerticesUV(ref HexCellData hexCellData)
    {
        //河道UV暂且设为(0.5f，0.5f)，其他正常
        //实心区域顶点UV
        Vector2 center = new Vector2(0.5f, 0.5f);
        float r = 0.5f * 0.866025404f;
        Vector2[] arrUV = new Vector2[]
        {
            //本体六边形的7个点
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 1.0f),
            new Vector2(1.0f, 0.75f),
            new Vector2(1.0f, 1.0f),
            new Vector2(0.5f, 0.0f),
            new Vector2(0.0f, 0.25f),
            new Vector2(0.0f, 0.75f),
            //分割边缘的12个新点（每条边多2个等分点）
            center + new Vector2(r * Mathf.Cos(Mathf.PI * 7 / 18), r * Mathf.Sin(Mathf.PI * 7 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * 5 / 18), r * Mathf.Sin(Mathf.PI * 5 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * 1 / 18), r * Mathf.Sin(Mathf.PI * 1 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * -1 / 18), r * Mathf.Sin(Mathf.PI * -1 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * -5 / 18), r * Mathf.Sin(Mathf.PI * -5 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * -7 / 18), r * Mathf.Sin(Mathf.PI * -7 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * -11 / 18), r * Mathf.Sin(Mathf.PI * -11 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * -13 / 18), r * Mathf.Sin(Mathf.PI * -13 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * -17 / 18), r * Mathf.Sin(Mathf.PI * -17 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * 17 / 18), r * Mathf.Sin(Mathf.PI * 17 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * 13 / 18), r * Mathf.Sin(Mathf.PI * 13 / 18)),
            center + new Vector2(r * Mathf.Cos(Mathf.PI * 11 / 18), r * Mathf.Sin(Mathf.PI * 11 / 18)),

            //河道的24个点
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
        };
        hexCellData.SolidAreaUV.AddRange(arrUV);

        return arrUV.ToList();
    }

    /// <summary>
    /// 获取正常平面面片
    /// </summary>
    /// <param name="direction">方向</param>
    /// <returns></returns>
    private List<int> GetPlaneFace(Enums.HexDirection direction)
    {
        switch (direction)
        {
            case Enums.HexDirection.NE:
                return new List<int>()
                {
                    0,1,7,
                    0,7,8,
                    0,8,2,
                };
            case Enums.HexDirection.E:
                return new List<int>()
                {
                    0,2,9,
                    0,9,10,
                    0,10,3,
                };
            case Enums.HexDirection.SE:
                return new List<int>()
                {
                    0,3,11,
                    0,11,12,
                    0,12,4,
                };
            case Enums.HexDirection.SW:
                return new List<int>()
                {
                    0,4,13,
                    0,13,14,
                    0,14,5,
                };
            case Enums.HexDirection.W:
                return new List<int>()
                {
                    0,5,15,
                    0,15,16,
                    0,16,6
                };
            case Enums.HexDirection.NW:
                return new List<int>()
                {
                    0,6,17,
                    0,17,18,
                    0,18,1,
                };
            default:
                return new List<int>() { default };
        }
    }

    /// <summary>
    /// 获取河道平面面片
    /// </summary>
    /// <param name="direction">方向</param>
    /// <returns></returns>
    private List<int> GetRiverPlaneFace(Enums.HexDirection direction)
    {
        switch (direction)
        {
            case Enums.HexDirection.NE:
                return new List<int>()
                {
                    1,7,19,
                    8,2,20,
                };
            case Enums.HexDirection.E:
                return new List<int>()
                {
                    2,9,20,
                    10,3,21,
                };
            case Enums.HexDirection.SE:
                return new List<int>()
                {
                    21,3,11,
                    22,12,4,
                };
            case Enums.HexDirection.SW:
                return new List<int>()
                {
                    22,4,13,
                    23,14,5,
                };
            case Enums.HexDirection.W:
                return new List<int>()
                {
                    23,5,15,
                    24,16,6,
                };
            case Enums.HexDirection.NW:
                return new List<int>()
                {
                    24,6,17,
                    19,18,1,
                };
            default:
                return new List<int>() { default };
        }
    }

    /// <summary>
    /// 获取河道2平面面片
    /// </summary>
    /// <param name="direction"></param>
    /// <returns></returns>
    private List<int> GetRiver2PlaneFace(Enums.HexDirection direction)
    {
        switch (direction)
        {
            case Enums.HexDirection.NE:
                return new List<int>()
                {
                    1,7,19,

                    7,8,19,
                    8,20,19,

                    8,2,20,
                };
            case Enums.HexDirection.E:
                return new List<int>()
                {
                    2,9,20,

                    9,10,20,
                    10,21,20,

                    10,3,21,
                };
            case Enums.HexDirection.SE:
                return new List<int>()
                {
                    21,3,11,

                    11,12,21,
                    12,22,21,

                    22,12,4,
                };
            case Enums.HexDirection.SW:
                return new List<int>()
                {
                    22,4,13,

                    13,14,22,
                    14,23,22,

                    23,14,5,
                };
            case Enums.HexDirection.W:
                return new List<int>()
                {
                    23,5,15,

                    15,16,23,
                    16,24,23,

                    24,16,6,
                };
            case Enums.HexDirection.NW:
                return new List<int>()
                {
                    24,6,17,

                    17,18,24,
                    18,19,24,

                    19,18,1,
                };
            default:
                return new List<int>() { default };
        }
    }

    /// <summary>
    /// 获取河道3平面面片
    /// </summary>
    /// <param name="incomingDirection"></param>
    /// <param name="outgoingDirection"></param>
    private List<int> GetRiver3PlaneFace(Enums.HexDirection incomingDirection, Enums.HexDirection outgoingDirection)
    {
        //(NE - SW)为类型1、(E - W)为类型2、(SE - NW)为类型3
        List<int> arr = new List<int>();
        switch (incomingDirection)
        {
            case Enums.HexDirection.NE:
            case Enums.HexDirection.SW:
                arr.AddRange(new int[] {
                    16,6,17,
                    16,17,15,
                    15,17,18,
                    15,18,5,
                    5,18,1,
                    5,1,7,
                    14,5,7,

                    13,8,4,
                    4,8,2,
                    4,2,9,
                    12,4,9,
                    12,9,10,
                    11,12,10,
                    11,10,3,
                });
                return arr;
            case Enums.HexDirection.E:
            case Enums.HexDirection.W:
                arr.AddRange(new int[] {
                    18,1,7,
                    18,7,17,
                    17,7,8,
                    17,8,6,
                    6,8,2,
                    6,2,16,
                    16,2,9,

                    15,10,3,
                    5,15,3,
                    5,3,11,
                    14,5,11,
                    14,11,12,
                    13,14,12,
                    13,12,4,
                });
                return arr;
            case Enums.HexDirection.SE:
            case Enums.HexDirection.NW:
                arr.AddRange(new int[] {
                    14,5,15,
                    14,15,16,
                    13,14,16,
                    13,16,6,
                    4,13,6,
                    4,6,17,
                    12,4,17,

                    11,18,3,
                    3,18,1,
                    3,1,7,
                    10,3,7,
                    10,7,8,
                    9,10,8,
                    9,8,2,
                });
                return arr;
            default:
                return new int[] { default }.ToList();
        }

    }

    /// <summary>
    /// 获取河道方向面片
    /// </summary>
    /// <param name="direction"></param>

    private List<int> GetRiverDirectionFace(Enums.HexDirection direction)
    {
        switch (direction)
        {
            case Enums.HexDirection.NE:
                return new List<int>()
                {
                    19,7,26,
                    7,32,26,

                    //7,8,32,
                    //8,33,32,

                    8,27,33,
                    20,27,8,

                    25,26,27,

                    26,32,33,
                    26,33,27,
                };
            case Enums.HexDirection.E:
                return new List<int>()
                {
                    20,9,27,
                    9,34,27,

                    //9,10,34,
                    //10,35,34,

                    10,21,28,
                    10,28,35,

                    25,27,28,

                    27,34,28,
                    34,35,28,
                };
            case Enums.HexDirection.SE:
                return new List<int>()
                {
                    21,11,36,
                    21,36,28,

                    //11,12,36,
                    //12,37,36,

                    12,22,29,
                    12,29,37,

                    25,28,29,

                    28,36,29,
                    36,37,29,
                };
            case Enums.HexDirection.SW:
                return new List<int>()
                {
                    22,13,29,
                    13,38,29,

                    //13,14,39,
                    //13,39,38,

                    14,23,30,
                    14,30,39,

                    25,29,30,

                    30,29,38,
                    30,38,39
                };
            case Enums.HexDirection.W:
                return new List<int>()
                {
                    23,15,30,
                    15,40,30,

                    //15,16,40,
                    //16,41,40,

                    16,24,31,
                    16,31,41,

                    25,30,31,

                    30,40,41,
                    41,31,30,
                };
            case Enums.HexDirection.NW:
                return new List<int>()
                {
                    24,17,31,
                    17,42,31,

                    //17,18,42,
                    //18,43,42,

                    18,19,43,
                    43,19,26,

                    25,31,26,

                    31,42,26,
                    42,43,26,
                };
            default:
                return new List<int>() { default };
        }
    }

    /// <summary>
    /// 获取河道3方向面片
    /// </summary>
    /// <param name="incomingDirection"></param>
    /// <param name="outgoingDirection"></param>
    private List<int> GetRiver3DirectionFace(Enums.HexDirection incomingDirection, Enums.HexDirection outgoingDirection)
    {
        List<int> arr = new List<int>();
        switch (incomingDirection)
        {
            case Enums.HexDirection.NE:
            case Enums.HexDirection.SW:
                arr.AddRange(new int[] {
                    //7,8,33,
                    //7,33,32,

                    8,13,38,
                    8,38,33,

                    //13,14,39,
                    //13,39,38,

                    14,7,32,
                    14,32,39,

                    32,33,38,
                    32,38,39,
                });
                return arr;
            case Enums.HexDirection.E:
            case Enums.HexDirection.W:
                arr.AddRange(new int[] {
                   //9,10,35,
                   //9,35,34,

                   10,15,40,
                   10,40,35,

                   //15,16,41,
                   //15,41,40,

                   16,9,34,
                   16,34,41,

                   34,35,40,
                   34,40,41,
                });
                return arr;
            case Enums.HexDirection.SE:
            case Enums.HexDirection.NW:
                arr.AddRange(new int[] {
                    //11,12,37,
                    //11,37,36,

                    12,17,42,
                    12,42,37,

                    //17,18,43,
                    //17,43,42,

                    18,11,36,
                    18,36,43,

                    42,43,36,
                    42,36,37,
                });
                return arr;
            default:
                return new int[] { default }.ToList();
        }
    }

    /// <summary>
    /// 获取河道1的链接面片
    /// </summary>
    /// <param name="direction">方向："01"、"02"、"03"、"04"、"05"、"06"</param>
    /// <param name="type">朝向：0向右、1向左</param>

    private List<int> GetRiverLinkFace(string direction, int type)
    {
        switch (direction)
        {
            case "01":
                if (type == 0)
                {
                    return new List<int>()
                    {
                        0,19,26,
                        0,26,25
                    };
                }
                else
                {
                    return new List<int>()
                    {
                        0,26,19,
                        0,25,26
                    };
                }
            case "02":
                if (type == 0)
                {
                    return new List<int>()
                    {
                        0,20,27,
                        0,27,25
                    };
                }
                else
                {
                    return new List<int>()
                    {
                        0,27,20,  0,25,27
                    };
                }
            case "03":
                if (type == 0)
                {
                    return new List<int>()
                    {
                        0,21,28,  0,28,25
                    };
                }
                else
                {
                    return new List<int>()
                    {
                        0,28,21,  0,25,28
                    };
                }
            case "04":
                if (type == 0)
                {
                    return new List<int>()
                    {
                        0,22,29,  0,29,25
                    };
                }
                else
                {
                    return new List<int>()
                    {
                        0,29,22,  0,25,29
                    };
                }
            case "05":
                if (type == 0)
                {
                    return new List<int>()
                    {
                        0,23,30,
                        0,30,25
                    };
                }
                else
                {
                    return new List<int>()
                    {
                        0,30,23,
                        0,25,30
                    };
                }
            case "06":
                if (type == 0)
                {
                    return new List<int>()
                    {
                        0,24,31,
                        0,31,25
                    };
                }
                else
                {
                    return new List<int>()
                    {
                        0,31,24,
                        0,25,31
                    };
                }
            default:
                return new List<int>() { default };
        }
    }

    /// <returns></returns>
    /// <summary>
    /// 设置地块实心区域的顶点绘制顺序（无河道地块）
    /// </summary>
    public List<int> GetSolidAreaVerticesDrawOrder1(ref HexCellData hexCellData)
    {
        //实心区域三角形的绘制顺序
        //每个地块的绘制顺序是 - 本体：012、023、034、045、056、061
        //                     - 本体7点 + 分割边缘的12个新点（每条边多2个等分点）
        //                     - 河道点不用
        hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NE));
        hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.E));
        hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SE));
        hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SW));
        hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.W));
        hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NW));

        return hexCellData.SolidAreaDrawOrder;
    }

    /// <summary>
    /// 设置地块实心区域的顶点绘制顺序（河道始末地块）
    /// </summary>
    public List<int> GetSolidAreaVerticesDrawOrder2(ref HexCellData hexCellData, Enums.HexDirection direction)
    {
        //实心区域三角形顶点的储存顺序是：中心点 - 本体6点 - 分割12点 - 同平面河道6点 - 下方中心点 - 下方河道6点 - 下方分割12点 == 44点
        //绘制顺序：平面 - 河道方向面 - 河道链接面
        switch (direction)
        {
            case Enums.HexDirection.NE:
                //平面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverPlaneFace(Enums.HexDirection.NE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.E));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SW));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.W));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NW));

                //方向面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(Enums.HexDirection.NE));

                //链接面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("01", 0));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("02", 1));
                break;
            case Enums.HexDirection.E:
                //平面
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverPlaneFace(Enums.HexDirection.E));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SW));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.W));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NW));

                //方向面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(Enums.HexDirection.E));

                //链接面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("02", 0));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("03", 1));
                break;
            case Enums.HexDirection.SE:
                //平面
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.E));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverPlaneFace(Enums.HexDirection.SE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SW));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.W));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NW));

                //方向面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(Enums.HexDirection.SE));

                //链接面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("03", 0));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("04", 1));
                break;
            case Enums.HexDirection.SW:
                //平面
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.E));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverPlaneFace(Enums.HexDirection.SW));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.W));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NW));

                //方向面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(Enums.HexDirection.SW));

                //链接面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("04", 0));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("05", 1));
                break;
            case Enums.HexDirection.W:
                //平面
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.E));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SW));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverPlaneFace(Enums.HexDirection.W));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NW));

                //方向面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(Enums.HexDirection.W));

                //链接面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("05", 0));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("06", 1));
                break;
            case Enums.HexDirection.NW:
                //平面
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.E));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SW));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.W));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverPlaneFace(Enums.HexDirection.NW));

                //方向面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(Enums.HexDirection.NW));

                //链接面
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("06", 0));
                hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace("01", 1));
                break;
            default:
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.E));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SE));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SW));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.W));
                hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NW));
                break;
        }

        return hexCellData.SolidAreaDrawOrder;
    }

    /// <summary>
    /// 设置地块实心区域的顶点绘制顺序（河道中流地块）
    /// </summary>
    public List<int> GetSolidAreaVerticesDrawOrder3(ref HexCellData hexCellData, Enums.HexDirection incomingDirection, Enums.HexDirection outgoingDirection)
    {
        //实心区域三角形顶点的储存顺序是：中心点 - 本体6点 - 分割12点 - 同平面河道6点 - 下方中心点 - 下方河道6点 - 下方分割12点 == 44点
        //绘制顺序：先平面，再河道
        //河道中流地块 - [出入方向相邻] - [出入方向相差2] - [贯穿]
        //三种地块的构成是：平面 + 方向面 + 链接面（方向面三种地块可复用，平面和链接面是各自特有的）
        //绘制顺序：平面 + 方向面 + 链接面
        int intervalCount = Mathf.Abs((int)outgoingDirection - (int)incomingDirection);
        //Debug.Log("intervalCount" +  intervalCount);
        //[出入方向相邻]
        if (intervalCount == 1 || intervalCount == 5)
        {
            //平面(按顺序绘制)
            for (int i = 0; i < 6; i++)
            {
                if ((Enums.HexDirection)i != incomingDirection && (Enums.HexDirection)i != outgoingDirection)
                {
                    //Debug.Log("111");
                    hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace((Enums.HexDirection)i));
                }
                else if ((Enums.HexDirection)i == incomingDirection || (Enums.HexDirection)i == outgoingDirection)
                {
                    //Debug.Log("222");
                    //Debug.Log((Map.HexDirection)i);
                    hexCellData.SolidAreaDrawOrder.AddRange(GetRiverPlaneFace((Enums.HexDirection)i));
                }
            }

            //进入方向面 + 离开方向面
            hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(incomingDirection));
            hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(outgoingDirection));

            //链接面
            string incomingLink;
            int incomingLinkType;
            string outgoingLink;
            int outgoingLinkType;
            int order = (int)outgoingDirection - (int)incomingDirection;
            switch (incomingDirection)
            {

                case Enums.HexDirection.NE:
                    if (order == 1)
                    {
                        incomingLink = "01";
                        incomingLinkType = 0;

                        outgoingLink = "03";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "02";
                        incomingLinkType = 1;

                        outgoingLink = "06";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.E:
                    if (order == 1)
                    {
                        incomingLink = "02";
                        incomingLinkType = 0;

                        outgoingLink = "04";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "03";
                        incomingLinkType = 1;

                        outgoingLink = "01";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.SE:
                    if (order == 1)
                    {
                        incomingLink = "03";
                        incomingLinkType = 0;

                        outgoingLink = "05";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "04";
                        incomingLinkType = 1;

                        outgoingLink = "02";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.SW:
                    if (order == 1)
                    {
                        incomingLink = "04";
                        incomingLinkType = 0;

                        outgoingLink = "06";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "05";
                        incomingLinkType = 1;

                        outgoingLink = "03";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.W:
                    if (order == 1)
                    {
                        incomingLink = "05";
                        incomingLinkType = 0;

                        outgoingLink = "01";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "06";
                        incomingLinkType = 1;

                        outgoingLink = "04";
                        outgoingLinkType = 0;
                    }
                    break;
                //-5是边缘情况
                case Enums.HexDirection.NW:
                    if (order == 1 || order == -5)
                    {
                        incomingLink = "06";
                        incomingLinkType = 0;

                        outgoingLink = "02";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "01";
                        incomingLinkType = 1;

                        outgoingLink = "05";
                        outgoingLinkType = 0;
                    }
                    break;
                default:
                    throw new Exception("出错");
            }

            hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace(incomingLink, incomingLinkType));
            hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace(outgoingLink, outgoingLinkType));
        }
        //[出入方向相差2]
        else if (intervalCount == 2 || intervalCount == 4)
        {
            //间隔平面方向
            int intervalIndex = ((int)outgoingDirection + (int)incomingDirection) / 2;
            if (((int)outgoingDirection == 0 || (int)outgoingDirection == 4) && ((int)incomingDirection == 0 || (int)incomingDirection == 4) && ((int)incomingDirection != (int)outgoingDirection))
            {
                intervalIndex = 5;
            }
            else if (((int)outgoingDirection == 1 || (int)outgoingDirection == 5) && ((int)incomingDirection == 1 || (int)incomingDirection == 5) && ((int)incomingDirection != (int)outgoingDirection))
            {
                intervalIndex = 0;
            }
            //平面(按顺序绘制)
            for (int i = 0; i < 6; i++)
            {
                //正常平面
                if ((Enums.HexDirection)i != incomingDirection && (Enums.HexDirection)i != outgoingDirection && i != intervalIndex)
                {
                    hexCellData.SolidAreaDrawOrder.AddRange(GetPlaneFace((Enums.HexDirection)i));
                }
                //河道所在平面
                else if (((Enums.HexDirection)i == incomingDirection || (Enums.HexDirection)i == outgoingDirection) && i != intervalIndex)
                {
                    hexCellData.SolidAreaDrawOrder.AddRange(GetRiverPlaneFace((Enums.HexDirection)i));
                }
                //间隔平面
                else
                {
                    hexCellData.SolidAreaDrawOrder.AddRange(GetRiver2PlaneFace((Enums.HexDirection)i));
                }
            }

            //进入方向面 + 离开方向面
            hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(incomingDirection));
            hexCellData.SolidAreaDrawOrder.AddRange(GetRiverDirectionFace(outgoingDirection));

            //链接面 - [两个河道侧面 + 间隔的面(一底一侧)]
            //两个河道侧面
            string incomingLink;
            int incomingLinkType;
            string outgoingLink;
            int outgoingLinkType;
            int order = (int)outgoingDirection - (int)incomingDirection;
            //Debug.Log("order：" + order);
            switch (incomingDirection)
            {

                case Enums.HexDirection.NE:
                    if (order == 2)
                    {
                        incomingLink = "01";
                        incomingLinkType = 0;

                        outgoingLink = "04";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "02";
                        incomingLinkType = 1;

                        outgoingLink = "05";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.E:
                    if (order == 2)
                    {
                        incomingLink = "02";
                        incomingLinkType = 0;

                        outgoingLink = "05";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "03";
                        incomingLinkType = 1;

                        outgoingLink = "06";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.SE:
                    if (order == 2)
                    {
                        incomingLink = "03";
                        incomingLinkType = 0;

                        outgoingLink = "06";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "04";
                        incomingLinkType = 1;

                        outgoingLink = "01";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.SW:
                    if (order == 2)
                    {
                        incomingLink = "04";
                        incomingLinkType = 0;

                        outgoingLink = "01";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "05";
                        incomingLinkType = 1;

                        outgoingLink = "02";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.W:
                    if (order == -4)
                    {
                        incomingLink = "05";
                        incomingLinkType = 0;

                        outgoingLink = "02";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "06";
                        incomingLinkType = 1;

                        outgoingLink = "03";
                        outgoingLinkType = 0;
                    }
                    break;
                case Enums.HexDirection.NW:
                    if (order == -4)
                    {
                        incomingLink = "06";
                        incomingLinkType = 0;

                        outgoingLink = "03";
                        outgoingLinkType = 1;
                    }
                    else
                    {
                        incomingLink = "01";
                        incomingLinkType = 1;

                        outgoingLink = "04";
                        outgoingLinkType = 0;
                    }
                    break;
                default:
                    throw new Exception("出错");
            }

            hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace(incomingLink, incomingLinkType));
            hexCellData.SolidAreaDrawOrder.AddRange(GetRiverLinkFace(outgoingLink, outgoingLinkType));

            //间隔的面(一底一侧)
            //底
            int[] arr = GetRiverDirectionFace((Enums.HexDirection)intervalIndex).ToArray(); ;
            hexCellData.SolidAreaDrawOrder.AddRange(new int[] { arr[12], arr[13], arr[14], });
            //侧
            int one = arr[13] - 7;
            int two = arr[14] - 7;
            int three = arr[13];
            int four = arr[14];
            hexCellData.SolidAreaDrawOrder.AddRange(new int[] {
                one, two, three,
                two, four,three,
            });

        }
        //[贯穿]
        //贯穿的话与上述两种不同，只需要考虑[平面] - [河道]即可
        //[平面]重绘整个平面，不再由6个等边三角形组成，而是两边各由一个等腰三角形，一个矩形(分解为两三角)组成，三角形数量同样为6个
        else
        {
            //平面(按顺序绘制)
            hexCellData.SolidAreaDrawOrder.AddRange(GetRiver3PlaneFace(incomingDirection, outgoingDirection));
            //河道
            hexCellData.SolidAreaDrawOrder.AddRange(GetRiver3DirectionFace(incomingDirection, outgoingDirection));
        }

        return hexCellData.SolidAreaDrawOrder;
    }

    /////////////////////////////////////////////////////////////////////- 矩形 -/////////////////////////////////////////////////////////////////////
    ///////////////////- 坡 -///////////////////
    /// <summary>
    /// 返回矩形过渡区域的顶点坐标
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    /// <returns></returns>
    public List<Vector3> GetRectVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        //坡河道的话就是在原本的基础上多4个点，4个侧矩形，1个底矩形
        //顶点组顺序是原本的坡顶点组 + 4个河道点(己方、邻居、邻居、己方) - 3 4 7 8
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            //需要自己的1、7、8、2点 + NE邻居的4、13、14、5点
            //排序应为1、NE_5、NE_14、NE_13、NE_4、2、8、7
            Vector3[] arrRectVertices = new Vector3[]
            {
                hexCellData.SolidAreaVertices[1],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[5],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[14],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[13],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[4],
                hexCellData.SolidAreaVertices[2],
                hexCellData.SolidAreaVertices[8],
                hexCellData.SolidAreaVertices[7],

                //河道
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[14] + new Vector3(0, hexCellData.RiverDepth, 0),
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[13] + new Vector3(0, hexCellData.RiverDepth, 0),
                hexCellData.SolidAreaVertices[8] + new Vector3(0, hexCellData.RiverDepth, 0),
                hexCellData.SolidAreaVertices[7] + new Vector3(0, hexCellData.RiverDepth, 0),

            };
            hexCellData.NERectVertices.AddRange(arrRectVertices);
            return hexCellData.NERectVertices;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2、9、10、3点 + E邻居的5、15、16、6点
            //排序应为2、E_6、E_16、E_15、E_5、3、10、9
            Vector3[] arrRectVertices = new Vector3[]
            {
                hexCellData.SolidAreaVertices[2],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[6],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[16],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[15],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[5],
                hexCellData.SolidAreaVertices[3],
                hexCellData.SolidAreaVertices[10],
                hexCellData.SolidAreaVertices[9],

                //河道
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[16] + new Vector3(0, hexCellData.RiverDepth, 0),
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[15] + new Vector3(0, hexCellData.RiverDepth, 0),
                hexCellData.SolidAreaVertices[10] + new Vector3(0, hexCellData.RiverDepth, 0),
                hexCellData.SolidAreaVertices[9] + new Vector3(0, hexCellData.RiverDepth, 0),

            };
            hexCellData.ERectVertices.AddRange(arrRectVertices);
            return hexCellData.ERectVertices;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3、11、12、4点 + SE邻居的6、17、18、1点
            //排序应为3、SE_1、SE_18、SE_17、SE_6、4、12、11
            Vector3[] arrRectVertices = new Vector3[]
            {
                hexCellData.SolidAreaVertices[3],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[1],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[18],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[17],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[6],
                hexCellData.SolidAreaVertices[4],
                hexCellData.SolidAreaVertices[12],
                hexCellData.SolidAreaVertices[11],

                //河道
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[18] + new Vector3(0, hexCellData.RiverDepth, 0),
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[17] + new Vector3(0, hexCellData.RiverDepth, 0),
                hexCellData.SolidAreaVertices[12] + new Vector3(0, hexCellData.RiverDepth, 0),
                hexCellData.SolidAreaVertices[11] + new Vector3(0, hexCellData.RiverDepth, 0),
            };
            hexCellData.SERectVertices.AddRange(arrRectVertices);
            return hexCellData.SERectVertices;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回矩形过渡区域的uv
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    public List<Vector2> GetRectUV(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        Vector2[] arrRectUV = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1f/3, 1),
            new Vector2(2f/3, 1),
            new Vector2(1, 1),
            new Vector2(1, 0),
            new Vector2(1f/3, 0),
            new Vector2(2f/3, 0),

            //河道（暂且全为(0.5f,0.5f)）
            new Vector2(0.5f,0.5f),
            new Vector2(0.5f,0.5f),
            new Vector2(0.5f,0.5f),
            new Vector2(0.5f,0.5f),
        };
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            hexCellData.NERectUV.AddRange(arrRectUV);
            return hexCellData.NERectUV;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            hexCellData.ERectUV.AddRange(arrRectUV);
            return hexCellData.ERectUV;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            hexCellData.SERectUV.AddRange(arrRectUV);
            return hexCellData.SERectUV;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回矩形过渡区域的矩形绘制顺序
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>

    public List<int> GetRectDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        int[] arrRectDrawOrder = new int[]
        {
            0,1,2,
            0,2,7,
            7,2,3,
            7,3,6,
            6,3,4,
            6,4,5,
        };
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            hexCellData.NERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.NERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            hexCellData.ERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.ERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            hexCellData.SERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.SERectDrawOrder;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回矩形坡河道过渡区域的矩形绘制顺序
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>

    public List<int> GetRectSlopeRiverDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        int[] arrRectDrawOrder = new int[]
        {
            //表面
            0,1,2,
            0,2,7,
            //7,2,3,
            //7,3,6,
            6,3,4,
            6,4,5,

            //河道
            /*
            //7,8,11,
            //7,11,6,
            */
            
            7,2,8,
            7,8,11,
            
            /*
            //2,3,10,
            //2,10,9,
            */
            
            3,6,10,
            3,10,9,

            8,9,10,
            8,10,11,
        };
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            hexCellData.NERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.NERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            hexCellData.ERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.ERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            hexCellData.SERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.SERectDrawOrder;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }


    ///////////////////- 阶梯 -///////////////////
    /// <summary>
    /// 两边的顶点不变
    /// 中间的点通过插值获取
    /// 插值规律 - 在竖直Y方向上插n个值，在水平X方向上插2n个值，插值均分起始点和终末点
    /// </summary>
    /// <param name="direction"></param>
    /// <returns>获取矩形阶梯的顶点</returns>
    public List<Vector3> GetRectStepVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        float heightDelta = Mathf.Abs(hexCellData.Height - _mapDataService.GetNeighbor(hexCellData, direction).Height);
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            //需要自己的1、7、8、2点 + NE邻居的4、13、14、5点
            //排序应为1、NE_5、NE_14、NE_13、NE_4、2、8、7
            //对应的点对为 (1、NE_5) - (7、NE_14) - (8、NE_13) - (2、NE_4)
            //同理点的排序为：(1、NE_5) - (7、NE_14) - (8、NE_13) - (2、NE_4)
            List<Vector3> one = SimpleInterpolate(hexCellData.SolidAreaVertices[1], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[5], hexCellData.interpCount);
            List<Vector3> two = SimpleInterpolate(hexCellData.SolidAreaVertices[7], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[14], hexCellData.interpCount);
            List<Vector3> three = SimpleInterpolate(hexCellData.SolidAreaVertices[8], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[13], hexCellData.interpCount);
            List<Vector3> four = SimpleInterpolate(hexCellData.SolidAreaVertices[2], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[4], hexCellData.interpCount);
            //河道
            List<Vector3> River_two = new List<Vector3>();
            List<Vector3> River_three = new List<Vector3>();
            foreach (Vector3 v in two)
            {
                River_two.Add(v + new Vector3(0, hexCellData.RiverDepth, 0));
            }
            foreach (Vector3 v in three)
            {
                River_three.Add(v + new Vector3(0, hexCellData.RiverDepth, 0));
            }


            if (heightDelta > 0f)
            {
                //添加全扰动
                for (int j = 0; j < 4; j++)
                {
                    List<Vector3> vector3s = new List<Vector3>();
                    if (j == 0)
                    {
                        vector3s = one;
                    }
                    else if (j == 1)
                    {
                        vector3s = two;
                    }
                    else if (j == 2)
                    {
                        vector3s = three;
                    }
                    else
                    {
                        vector3s = four;
                    }

                    for (int i = 1; i < vector3s.Count - 1; i++)
                    {
                        vector3s[i] = HexMetrics.Perturb(vector3s[i]);
                    }
                }
            }

            hexCellData.NERectVertices.AddRange(one);
            hexCellData.NERectVertices.AddRange(two);
            hexCellData.NERectVertices.AddRange(three);
            hexCellData.NERectVertices.AddRange(four);
            hexCellData.NERectVertices.AddRange(River_two);
            hexCellData.NERectVertices.AddRange(River_three);

            return hexCellData.NERectVertices;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2、9、10、3点 + E邻居的5、15、16、6点
            //排序应为2、E_6、E_16、E_15、E_5、3、10、9
            //对应的点对为 (2、E_6) - (9、NE_16) - (10、NE_15) - (3、NE_5)
            //同理点的排序为：(2、E_6) - (9、NE_16) - (10、NE_15) - (3、NE_5)
            //阶梯
            List<Vector3> one = SimpleInterpolate(hexCellData.SolidAreaVertices[2], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[6], hexCellData.interpCount);
            List<Vector3> two = SimpleInterpolate(hexCellData.SolidAreaVertices[9], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[16], hexCellData.interpCount);
            List<Vector3> three = SimpleInterpolate(hexCellData.SolidAreaVertices[10], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[15], hexCellData.interpCount);
            List<Vector3> four = SimpleInterpolate(hexCellData.SolidAreaVertices[3], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[5], hexCellData.interpCount);
            //河道
            List<Vector3> River_two = new List<Vector3>();
            List<Vector3> River_three = new List<Vector3>();
            foreach (Vector3 v in two)
            {
                River_two.Add(v + new Vector3(0, hexCellData.RiverDepth, 0));
            }
            foreach (Vector3 v in three)
            {
                River_three.Add(v + new Vector3(0, hexCellData.RiverDepth, 0));
            }

            if (heightDelta > 0f)
            {
                //添加全扰动
                for (int j = 0; j < 4; j++)
                {
                    List<Vector3> vector3s = new List<Vector3>();
                    if (j == 0)
                    {
                        vector3s = one;
                    }
                    else if (j == 1)
                    {
                        vector3s = two;
                    }
                    else if (j == 2)
                    {
                        vector3s = three;
                    }
                    else
                    {
                        vector3s = four;
                    }

                    for (int i = 1; i < vector3s.Count - 1; i++)
                    {
                        vector3s[i] = HexMetrics.Perturb(vector3s[i]);
                    }
                }
            }


            hexCellData.ERectVertices.AddRange(one);
            hexCellData.ERectVertices.AddRange(two);
            hexCellData.ERectVertices.AddRange(three);
            hexCellData.ERectVertices.AddRange(four);
            hexCellData.ERectVertices.AddRange(River_two);
            hexCellData.ERectVertices.AddRange(River_three);

            return hexCellData.ERectVertices;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3、11、12、4点 + SE邻居的6、17、18、1点
            //排序应为3、SE_1、SE_18、SE_17、SE_6、4、12、11
            //对应的点对为 (3、NE_1) - (11、NE_18) - (12、NE_17) - (4、NE_6)
            //同理点的排序为：(3、NE_1) - (11、NE_18) - (12、NE_17) - (4、NE_6)
            List<Vector3> one = SimpleInterpolate(hexCellData.SolidAreaVertices[3], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[1], hexCellData.interpCount);
            List<Vector3> two = SimpleInterpolate(hexCellData.SolidAreaVertices[11], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[18], hexCellData.interpCount);
            List<Vector3> three = SimpleInterpolate(hexCellData.SolidAreaVertices[12], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[17], hexCellData.interpCount);
            List<Vector3> four = SimpleInterpolate(hexCellData.SolidAreaVertices[4], _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[6], hexCellData.interpCount);
            //河道
            List<Vector3> River_two = new List<Vector3>();
            List<Vector3> River_three = new List<Vector3>();
            foreach (Vector3 v in two)
            {
                River_two.Add(v + new Vector3(0, hexCellData.RiverDepth, 0));
            }
            foreach (Vector3 v in three)
            {
                River_three.Add(v + new Vector3(0, hexCellData.RiverDepth, 0));
            }

            if (heightDelta > 0f)
            {
                //添加全扰动
                for (int j = 0; j < 4; j++)
                {
                    List<Vector3> vector3s = new List<Vector3>();
                    if (j == 0)
                    {
                        vector3s = one;
                    }
                    else if (j == 1)
                    {
                        vector3s = two;
                    }
                    else if (j == 2)
                    {
                        vector3s = three;
                    }
                    else
                    {
                        vector3s = four;
                    }

                    for (int i = 1; i < vector3s.Count - 1; i++)
                    {
                        vector3s[i] = HexMetrics.Perturb(vector3s[i]);
                    }
                }
            }

            hexCellData.SERectVertices.AddRange(one);
            hexCellData.SERectVertices.AddRange(two);
            hexCellData.SERectVertices.AddRange(three);
            hexCellData.SERectVertices.AddRange(four);
            hexCellData.SERectVertices.AddRange(River_two);
            hexCellData.SERectVertices.AddRange(River_three);

            return hexCellData.SERectVertices;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 获取矩形阶梯的uv(新的、简化的方法)
    /// </summary>
    /// <param name="direction">方向</param>

    public List<Vector2> GetRectStepUV(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        List<Vector2> arrRectUV = new List<Vector2>();

        List<Vector3> RectVertices = new List<Vector3>();
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            RectVertices = hexCellData.NERectVertices;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            RectVertices = hexCellData.ERectVertices;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            RectVertices = hexCellData.SERectVertices;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }

        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < hexCellData.interpCount * 2 + 2; j++)
            {
                float numerator = (i + 1) / 4.0f;
                float denominator = (float)(j + 1) / (hexCellData.interpCount * 2 + 2);
                arrRectUV.Add(new Vector2(numerator, denominator));
            }
        }

        //河道点的uv暂且全为(0.5f,0.5f)
        for (int i = 0; i < 2 * (2 + 2 * hexCellData.interpCount); i++)
        {
            arrRectUV.Add(new Vector2(0.5f, 0.5f));
        }

        switch (direction)
        {
            case Enums.HexDirection.NE:
                hexCellData.NERectUV.AddRange(arrRectUV);
                return hexCellData.NERectUV;
            case Enums.HexDirection.E:
                hexCellData.ERectUV.AddRange(arrRectUV);
                return hexCellData.ERectUV;
            case Enums.HexDirection.SE:
                hexCellData.SERectUV.AddRange(arrRectUV);
                return hexCellData.SERectUV;
            default:
                Debug.LogError("方向输入出错");
                return null;
        }
    }

    /// <summary>
    /// 获取矩形阶梯的绘制顺序
    /// </summary>
    /// <param name="direction">方向</param>
    public List<int> GetRectStepDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        List<int> arrRectDrawOrder = new List<int>();

        List<Vector3> RectVertices = new List<Vector3>();
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            RectVertices = hexCellData.NERectVertices;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            RectVertices = hexCellData.ERectVertices;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            RectVertices = hexCellData.SERectVertices;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }

        for (int i = 1; i < 4; i++)
        {
            int round = hexCellData.interpCount * 2 + 2;
            for (int j = 0; j < (hexCellData.interpCount * 2 + 2) - 1; j++)
            {
                if (i == 1)
                {
                    arrRectDrawOrder.Add(j);
                    arrRectDrawOrder.Add(j + 1 + round * i);
                    arrRectDrawOrder.Add(j + round * i);

                    arrRectDrawOrder.Add(j);
                    arrRectDrawOrder.Add(j + 1);
                    arrRectDrawOrder.Add(j + 1 + round * i);
                }
                else
                {
                    arrRectDrawOrder.Add(j + round * (i - 1));
                    arrRectDrawOrder.Add(j + 1 + round * i);
                    arrRectDrawOrder.Add(j + round * i);

                    arrRectDrawOrder.Add(j + round * (i - 1));
                    arrRectDrawOrder.Add(j + 1 + round * (i - 1));
                    arrRectDrawOrder.Add(j + 1 + round * i);
                }

            }
        }

        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            hexCellData.NERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.NERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            hexCellData.ERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.ERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            hexCellData.SERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.SERectDrawOrder;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回矩形矩形河道过渡区域的矩形绘制顺序
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    public List<int> GetRectStepRiverDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        List<int> arrRectDrawOrder = new List<int>();

        List<Vector3> RectVertices = new List<Vector3>();
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            RectVertices = hexCellData.NERectVertices;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            RectVertices = hexCellData.ERectVertices;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            RectVertices = hexCellData.SERectVertices;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }

        //阶梯面 + 河道底面
        for (int i = 1; i < 4; i++)
        {
            if (i == 2) { i = 5; }
            int round = hexCellData.interpCount * 2 + 2;
            for (int j = 0; j < (hexCellData.interpCount * 2 + 2) - 1; j++)
            {
                if (i == 1)
                {
                    arrRectDrawOrder.Add(j);
                    arrRectDrawOrder.Add(j + 1 + round * i);
                    arrRectDrawOrder.Add(j + round * i);

                    arrRectDrawOrder.Add(j);
                    arrRectDrawOrder.Add(j + 1);
                    arrRectDrawOrder.Add(j + 1 + round * i);
                }
                else
                {
                    arrRectDrawOrder.Add(j + round * (i - 1));
                    arrRectDrawOrder.Add(j + 1 + round * i);
                    arrRectDrawOrder.Add(j + round * i);

                    arrRectDrawOrder.Add(j + round * (i - 1));
                    arrRectDrawOrder.Add(j + 1 + round * (i - 1));
                    arrRectDrawOrder.Add(j + 1 + round * i);
                }

            }
            if (i == 5) { i = 2; }
        }

        //河道侧面     
        for (int i = 0; i < 2; i++)
        {
            int Offset;
            int RiverOffset;
            if (i == 0)
            {
                Offset = (hexCellData.interpCount * 2 + 2) * 1;
                RiverOffset = (hexCellData.interpCount * 2 + 2) * 4;
                for (int j = 0; j < (hexCellData.interpCount * 2 + 2) - 1; j++)
                {
                    arrRectDrawOrder.Add(j + Offset);
                    arrRectDrawOrder.Add(j + 1 + Offset);
                    arrRectDrawOrder.Add(j + 1 + RiverOffset);

                    arrRectDrawOrder.Add(j + Offset);
                    arrRectDrawOrder.Add(j + 1 + RiverOffset);
                    arrRectDrawOrder.Add(j + RiverOffset);
                }
            }
            else
            {
                Offset = (hexCellData.interpCount * 2 + 2) * 2;
                RiverOffset = (hexCellData.interpCount * 2 + 2) * 5;
                for (int j = 0; j < (hexCellData.interpCount * 2 + 2) - 1; j++)
                {
                    arrRectDrawOrder.Add(j + Offset);
                    arrRectDrawOrder.Add(j + 1 + RiverOffset);
                    arrRectDrawOrder.Add(j + 1 + Offset);

                    arrRectDrawOrder.Add(j + Offset);
                    arrRectDrawOrder.Add(j + RiverOffset);
                    arrRectDrawOrder.Add(j + 1 + RiverOffset);
                }
            }
        }


        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            hexCellData.NERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.NERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            hexCellData.ERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.ERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            hexCellData.SERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.SERectDrawOrder;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /////////////////////////////////////////////////////////////////////- 三角 -/////////////////////////////////////////////////////////////////////
    ///////////////////- 方法一 -///////////////////
    /// <summary>
    /// 返回三角过渡区域的顶点坐标
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>

    public List<Vector3> GetTriVertices(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        if (direction0 == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null && direction1 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2点 + NE邻居的4点 + E邻居的6点
            //排序应为2、NE_4、E_6
            hexCellData.NE_ETriVertices.Add(hexCellData.SolidAreaVertices[2]);
            hexCellData.NE_ETriVertices.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[4]);
            hexCellData.NE_ETriVertices.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[6]);
            return hexCellData.NE_ETriVertices;
        }
        else if (direction0 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null && direction1 == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3点 + E邻居的5点 + SE邻居的1点
            //排序应为3、E_5、SE_1
            hexCellData.E_SETriVertices.Add(hexCellData.SolidAreaVertices[3]);
            hexCellData.E_SETriVertices.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[5]);
            hexCellData.E_SETriVertices.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[1]);
            return hexCellData.E_SETriVertices;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回三角形过渡区域的uv
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<Vector2> GetTriUV(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        if (direction0 == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null && direction1 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2点 + NE邻居的4点 + E邻居的6点
            //排序应为2、NE_4、E_6
            hexCellData.NE_ETriUV.Add(new Vector2(0, 1));
            hexCellData.NE_ETriUV.Add(new Vector2(0.5f, 0.2f));
            hexCellData.NE_ETriUV.Add(new Vector2(1, 1));
            return hexCellData.NE_ETriUV;
        }
        else if (direction0 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null && direction1 == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3点 + E邻居的5点 + SE邻居的1点
            //排序应为3、E_5、SE_1
            hexCellData.E_SETriUV.Add(new Vector2(0, 1));
            hexCellData.E_SETriUV.Add(new Vector2(0.5f, 0.2f));
            hexCellData.E_SETriUV.Add(new Vector2(1, 1));
            return hexCellData.E_SETriUV;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回三角形过渡区域的绘制顺序
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<int> GetTriDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        if (direction0 == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null && direction1 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2点 + NE邻居的4点 + E邻居的6点
            //排序应为2、NE_4、E_6
            hexCellData.NE_ETriDrawOrder.Add(0);
            hexCellData.NE_ETriDrawOrder.Add(1);
            hexCellData.NE_ETriDrawOrder.Add(2);
            return hexCellData.NE_ETriDrawOrder;
        }
        else if (direction0 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null && direction1 == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3点 + E邻居的5点 + SE邻居的1点
            //排序应为3、E_5、SE_1
            hexCellData.E_SETriDrawOrder.Add(0);
            hexCellData.E_SETriDrawOrder.Add(1);
            hexCellData.E_SETriDrawOrder.Add(2);
            return hexCellData.E_SETriDrawOrder;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    ///////////////////- 方法三 -///////////////////
    /// <summary>
    /// 返回三角过渡区域 - 方法3 - 的顶点坐标
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<Vector3> GetTriStep3Vertices(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        //方法三：梯 - 坡(2) - 梯NE_ETriVertices
        //判断是哪个方向的三角过渡区域
        bool isNE_E = false;
        if (direction0 == Enums.HexDirection.NE && direction1 == Enums.HexDirection.E) { isNE_E = true; }
        //判断哪边是坡
        float height0 = hexCellData.Height;
        float height1 = _mapDataService.GetNeighbor(hexCellData, direction0).Height;
        float height2 = _mapDataService.GetNeighbor(hexCellData, direction1).Height;
        //返回的顶点组
        List<Vector3> vector3s = new List<Vector3>();

        //梯边的偶数顶点
        List<Vector3> v1 = new List<Vector3>();
        List<Vector3> v2 = new List<Vector3>();

        //0边是坡
        if (Mathf.Abs(height0 - height1) == 2)
        {
            if (isNE_E)
            {
                hexCellData.isSlope[0] = 0;
                //NE_E三角
                //1边：NE邻居的SE矩形后(1/4)顶点
                //2边：自己E矩形的前(1/4)顶点

                //坡_0边：自己的2点 + NE邻居的4点 - 排序应为2、NE_4

                //坡边要加个插值处理
                //对应梯边的偶数顶点（顶点从0开始计数，如：0、2、4、6），作与xz平面的平行线延申到坡边，两线相交的点，即为插值点。
                //一条梯边，会有(interpCount + 1)个插值点，两个0'会重叠。
                //插值点按梯边在三角过渡区域内的边顺序排（如：1边_0'、1边_2'、1边_4'、...、2边_0'、2边_2'、2边_4'、...）
                //
                //求插入点的方法：
                //求坡的直线参数方程，∵对应梯边的偶数顶点的y坐标与插入点的y坐标相同，所以代入y坐标求得对应t，进而得到插入点

                //1边
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SERectVertices.Count != 0)
                    {
                        v.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SERectVertices[i]);
                    }

                }
                //List <Vector3> v = Map.GetNeighbor(this, Map.HexDirection.NE).GetSERectVertices;

                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 1)
                    {
                        v1.Add(v[i]);
                    }
                }
                //2边
                v.Clear();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (hexCellData.ERectVertices.Count != 0)
                    {
                        v.Add(hexCellData.ERectVertices[i]);
                    }

                }
                //v = ERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 1)
                    {
                        v2.Add(v[i]);
                    }
                }

                Vector3 PointA = hexCellData.SolidAreaVertices[2];
                Vector3 PointB = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[4];
                //梯1的插入点
                for (int i = 0; i < v1.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v1[i]));
                }
                //梯2的插入点
                for (int i = 0; i < v2.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v2[i]));
                }
            }
            else
            {
                hexCellData.isSlope[1] = 0;
                //E_SE三角
                //1边：SE邻居的NE矩形前(1/4)顶点
                //2边：自己的SE矩形前(1/4)顶点
                //坡_0边：自己的3点 + E邻居的5点 - 排序应为3、E_5

                //1边
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).NERectVertices.Count != 0)
                    {
                        v.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).NERectVertices[i]);
                    }

                }
                //List<Vector3> v = Map.GetNeighbor(this, Map.HexDirection.SE).GetNERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 0)
                    {
                        v1.Add(v[i]);
                    }
                }
                //2边
                v.Clear();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (hexCellData.SERectVertices.Count != 0)
                    {
                        v.Add(hexCellData.SERectVertices[i]);
                    }

                }
                //v = SERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 1)
                    {
                        v2.Add(v[i]);
                    }
                }
                //v2.Reverse();

                Vector3 PointA = hexCellData.SolidAreaVertices[3];
                Vector3 PointB = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[5];
                //梯1的插入点
                for (int i = 0; i < v1.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v1[i]));
                }
                //梯2的插入点
                for (int i = 0; i < v2.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v2[i]));
                }
            }
        }
        //1边是坡
        else if (Mathf.Abs(height1 - height2) == 2)
        {
            if (isNE_E)
            {
                hexCellData.isSlope[0] = 1;
                //NE_E三角
                //0边：自己NE矩形的后(1/4)顶点
                //2边：自己E矩形的前(1/4)顶点
                //坡_1边：NE邻居的4点 + E邻居的6点 - 排序应为NE_4、E_6

                //1边                
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (hexCellData.NERectVertices.Count != 0)
                    {
                        v.Add(hexCellData.NERectVertices[i]);
                    }

                }
                //List<Vector3> v = NERectVertices;

                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 0)
                    {
                        v1.Add(v[i]);
                    }
                }
                //2边
                v.Clear();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (hexCellData.ERectVertices.Count != 0)
                    {
                        v.Add(hexCellData.ERectVertices[i]);
                    }

                }
                //v = ERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 0)
                    {
                        v2.Add(v[i]);
                    }
                }

                Vector3 PointA = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[4];
                Vector3 PointB = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[6];
                //梯1的插入点
                for (int i = 0; i < v1.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v1[i]));
                }
                //梯2的插入点
                for (int i = 0; i < v2.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v2[i]));
                }

            }
            else
            {
                hexCellData.isSlope[1] = 1;
                //E_SE三角

                //0边：自己E矩形的后(1/4)顶点
                //2边：自己SE矩形的前(1/4)顶点
                //坡_1边：E邻居的5点 + SE邻居的1点 - 排序应为E_5、SE_1

                //1边
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (hexCellData.ERectVertices.Count != 0)
                    {
                        v.Add(hexCellData.ERectVertices[i]);
                    }
                }
                //List<Vector3> v = ERectVertices;
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 0)
                    {
                        v1.Add(v[i]);
                    }
                }
                //2边
                v.Clear();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (hexCellData.SERectVertices.Count != 0)
                    {
                        v.Add(hexCellData.SERectVertices[i]);
                    }

                }
                //v = SERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 0)
                    {
                        v2.Add(v[i]);
                    }
                }

                Vector3 PointA = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[5];
                Vector3 PointB = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[1];
                //梯1的插入点
                for (int i = 0; i < v1.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v1[i]));
                }
                //梯2的插入点
                for (int i = 0; i < v2.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v2[i]));
                }
            }
        }
        //2边是坡
        else if (Mathf.Abs(height2 - height0) == 2)
        {
            if (isNE_E)
            {
                hexCellData.isSlope[0] = 2;
                //NE_E三角
                //0边：自己NE矩形的后(1/4)顶点
                //1边：NE邻居的SE矩形后(1/4)顶点
                //坡_2边：自己的2点 + E邻居的6点 - 排序应为2、E_6
                //0边
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (hexCellData.NERectVertices.Count != 0)
                    {
                        v.Add(hexCellData.NERectVertices[i]);
                    }

                }
                //List<Vector3> v = NERectVertices; 
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 1)
                    {
                        v1.Add(v[i]);
                    }
                }
                //1边
                v.Clear();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SERectVertices.Count != 0)
                    {
                        v.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SERectVertices[i]);
                    }

                }
                //v =  Map.GetNeighbor(this, Map.HexDirection.NE).GetSERectVertices;
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 0)
                    {
                        v2.Add(v[i]);
                    }
                }
                Vector3 PointA = hexCellData.SolidAreaVertices[2];
                Vector3 PointB = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[6];
                //梯1的插入点
                for (int i = 0; i < v1.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v1[i]));
                }
                //梯2的插入点
                for (int i = 0; i < v2.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v2[i]));
                }
            }
            else
            {
                hexCellData.isSlope[1] = 2;
                //E_SE三角
                //0边：自己E矩形的后(1/4)顶点
                //1边：SE邻居的NE矩形前(1/4)顶点
                //坡_2边：自己的3点 + SE邻居的1点 - 排序应3、SE_1

                //0边
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (hexCellData.ERectVertices.Count != 0)
                    {
                        v.Add(hexCellData.ERectVertices[i]);
                    }

                }
                //List<Vector3> v = ERectVertices; 
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 1)
                    {
                        v1.Add(v[i]);
                    }
                }
                //1边
                v.Clear();
                for (int i = 0; i < 4 * (hexCellData.interpCount * 2 + 2); i++)
                {
                    if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).NERectVertices.Count != 0)
                    {
                        v.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).NERectVertices[i]);
                    }

                }
                //v = Map.GetNeighbor(this, Map.HexDirection.SE).GetNERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                    if (i % 2 == 1)
                    {
                        v2.Add(v[i]);
                    }
                }
                Vector3 PointA = hexCellData.SolidAreaVertices[3];
                Vector3 PointB = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[1];
                //梯1的插入点
                for (int i = 0; i < v1.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v1[i]));
                }
                //梯2的插入点
                for (int i = 0; i < v2.Count; i++)
                {
                    vector3s.Add(TriStep3_GetInsertionPoint(PointA, PointB, v2[i]));
                }
            }
        }

        if (isNE_E) { hexCellData.NE_ETriVertices.AddRange(vector3s); return hexCellData.NE_ETriVertices; }
        else { hexCellData.E_SETriVertices.AddRange(vector3s); return hexCellData.E_SETriVertices; }

    }

    /// <summary>
    /// 返回三角过渡区域 - 方法3 - 的uv
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<Vector2> GetTriStep3UV(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        //判断是哪个方向的三角过渡区域
        bool isNE_E = false;
        List<Vector3> vector3s = new List<Vector3>();
        List<Vector2> vector2s = new List<Vector2>();
        if (direction0 == Enums.HexDirection.NE && direction1 == Enums.HexDirection.E)
        {
            isNE_E = true;
            vector3s = hexCellData.NE_ETriVertices;
        }
        else
        {
            vector3s = hexCellData.E_SETriVertices;
        }
        /*
        //需要将三角阶梯摊开，映射到正方形区域上
        //梯边有：(2*interpCount + 2)个点 - 有一个点重复
        //坡边有：2*(interpCount + 1)个点 - 有一个点重复
        //总共有：2梯 + 1坡 = (8*interpCount + 8)个点
        //顶点排序是梯1、梯2、坡1、坡2
        //梯点均分 y=1，x∈(0,1)，坡点均分 y=0，x∈(0,1)
        */

        //需要将三角阶梯摊开，映射到正方形区域上
        //梯边有：(2*interpCount + 2)个点 - 有一个点重复
        //坡边有：2*(interpCount + 1)个点 - 有一个点重复
        //总共有：2梯 + 1坡 = (8*interpCount + 8)个点
        //顶点排序是梯1、梯2、坡1、坡2
        //三个顶点从自己开始顺时针排序分别为：(0, 1)、(0.5f, 0.2f)、(1, 1)
        //梯边、坡边的其余点为其中的插值

        float ΔxT = 1 / 2 * (2 * hexCellData.interpCount + 2);
        float ΔxP = 1 / 2 * (1 * hexCellData.interpCount + 1);
        for (int i = 0; i < 2 * (2 * hexCellData.interpCount + 2); i++)
        {
            vector2s.Add(new Vector2(ΔxT * i, 1));
        }
        for (int i = 0; i < 2 * (hexCellData.interpCount + 1); i++)
        {
            vector2s.Add(new Vector2(ΔxP * i, 0));
        }

        if (isNE_E)
        {
            hexCellData.NE_ETriUV.AddRange(vector2s);
            return hexCellData.NE_ETriUV;
        }
        else
        {
            hexCellData.E_SETriUV.AddRange(vector2s);
            return hexCellData.E_SETriUV;
        }
    }

    /// <summary>
    /// 返回三角过渡区域 - 方法3 - 的绘制顺序
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<int> GetTriStep3DrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        //判断是哪个方向的三角过渡区域
        bool isNE_E = false;
        if (direction0 == Enums.HexDirection.NE && direction1 == Enums.HexDirection.E) { isNE_E = true; }
        //方法3会有：2 * (1 + 3*interpCount) 个三角形
        //          (两梯) * [尖端一个三角 + (一阶梯有三个三角)*(插入阶梯数)]
        //按照顶点数组的顺序：梯1、梯2、坡1、坡2
        //顶点数分别为：(2 + 2*interpCount)、(2 + 2*interpCount)、(1 + interpCount)、(1 + interpCount)
        //
        //以下是 [interpCount == 4] 绘制顺序（T是梯，P是坡）
        /*
T1_9、P1_4、T1_8✔

T1_8、P1_4、T1_7✔
T1_7、P1_4、T1_6✔
T1_6、P1_4、P1_3✔

T1_6、P1_3、T1_5✔
T1_5、P1_3、T1_4✔
T1_4、P1_3、P1_2✔

T1_4、P1_2、T1_3✔
T1_3、P1_2、T1_2✔
T1_2、P1_2、P1_1✔

T1_2、P1_1、T1_1✔
T1_1、P1_1、T1_0✔
T1_0、P1_1、P1_0✔
//
//
T2_0、P2_0、T2_1✔
T2_1、P2_0、P2_1✔

T2_1、P2_1、T2_2✔
T2_2、P2_1、T2_3✔
T2_3、P2_1、P2_2✔

T2_3、P2_2、T2_4✔
T2_4、P2_2、T2_5✔
T2_5、P2_2、P2_3✔

T2_5、P2_3、T2_6✔
T2_6、P2_3、T2_7✔
T2_7、P2_3、P2_4✔

T2_7、P2_4、T2_8✔
T2_8、P2_4、T2_9✔
        */
        //以下是各组的顶点数
        int T1_Count = (2 + 2 * hexCellData.interpCount);
        int T2_Count = (2 + 2 * hexCellData.interpCount);
        int P1_Count = (1 + hexCellData.interpCount);
        int P2_Count = (1 + hexCellData.interpCount);
        //以下是各组的偏移量
        int T1_Offset = 0;
        int T2_Offset = (2 + 2 * hexCellData.interpCount);
        int P1_Offset = (2 + 2 * hexCellData.interpCount) + (2 + 2 * hexCellData.interpCount);
        int P2_Offset = (1 + hexCellData.interpCount) + (2 + 2 * hexCellData.interpCount) + (2 + 2 * hexCellData.interpCount);

        List<int> drawOrder = new List<int>();

        if (isNE_E)
        {
            if (hexCellData.isSlope[0] == 0 || hexCellData.isSlope[0] == 1)
            {
                //第一个梯(顺时针)
                for (int i = P1_Count - 1, j = T1_Count - 1; i > 0; i--, j -= 2)
                {
                    if (i == P1_Count - 1)
                    {
                        drawOrder.Add(T1_Offset + j);
                        drawOrder.Add(P1_Offset + i);
                        drawOrder.Add(T1_Offset + --j);
                    }

                    drawOrder.Add(T1_Offset + j);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(T1_Offset + j - 1);

                    drawOrder.Add(T1_Offset + j - 1);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(T1_Offset + j - 2);

                    drawOrder.Add(T1_Offset + j - 2);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(P1_Offset + i - 1);
                }
                //第二个梯(顺时针)
                for (int i = 0, j = 0; i < P2_Count; i++, j += 2)
                {
                    if (i == 0)
                    {
                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(T2_Offset + ++j);

                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(P2_Offset + ++i);
                    }

                    drawOrder.Add(T2_Offset + j);
                    drawOrder.Add(P2_Offset + i);
                    drawOrder.Add(T2_Offset + j + 1);

                    drawOrder.Add(T2_Offset + j + 1);
                    drawOrder.Add(P2_Offset + i);
                    drawOrder.Add(T2_Offset + j + 2);

                    if (i != P2_Count - 1)
                    {
                        drawOrder.Add(T2_Offset + j + 2);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(P2_Offset + i + 1);
                    }
                }
            }
            else
            {
                //第一个梯(顺时针)
                for (int i = P1_Count - 1, j = T1_Count - 1; i > 0; i--, j -= 2)
                {
                    if (i == P1_Count - 1)
                    {
                        drawOrder.Add(T1_Offset + j);
                        drawOrder.Add(P1_Offset + i);
                        drawOrder.Add(T1_Offset + --j);
                    }

                    drawOrder.Add(T1_Offset + j);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(T1_Offset + j - 1);

                    drawOrder.Add(T1_Offset + j - 1);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(T1_Offset + j - 2);

                    drawOrder.Add(T1_Offset + j - 2);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(P1_Offset + i - 1);
                }
                //第二个梯(逆时针)
                for (int i = 0, j = 0; i < P2_Count; i++, j += 2)
                {
                    ///*
                    if (i == 0)
                    {
                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(T2_Offset + ++j);
                        drawOrder.Add(P2_Offset + i);


                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + ++i);
                        drawOrder.Add(P2_Offset + i - 1);

                    }

                    drawOrder.Add(T2_Offset + j);
                    drawOrder.Add(T2_Offset + j + 1);
                    drawOrder.Add(P2_Offset + i);


                    drawOrder.Add(T2_Offset + j + 1);
                    drawOrder.Add(T2_Offset + j + 2);
                    drawOrder.Add(P2_Offset + i);


                    if (i != P2_Count - 1)
                    {
                        drawOrder.Add(T2_Offset + j + 2);
                        drawOrder.Add(P2_Offset + i + 1);
                        drawOrder.Add(P2_Offset + i);

                    }
                }
            }
            hexCellData.NE_ETriDrawOrder.AddRange(drawOrder);
        }
        else
        {
            if (hexCellData.isSlope[1] == 0)
            {
                //第一个梯(逆时针)
                for (int i = P1_Count - 1, j = T1_Count - 1; i > 0; i--, j -= 2)
                {
                    if (i == P1_Count - 1)
                    {
                        drawOrder.Add(T1_Offset + j);
                        drawOrder.Add(T1_Offset + --j);
                        drawOrder.Add(P1_Offset + i);

                    }

                    drawOrder.Add(T1_Offset + j);
                    drawOrder.Add(T1_Offset + j - 1);
                    drawOrder.Add(P1_Offset + i);


                    drawOrder.Add(T1_Offset + j - 1);
                    drawOrder.Add(T1_Offset + j - 2);
                    drawOrder.Add(P1_Offset + i);


                    drawOrder.Add(T1_Offset + j - 2);
                    drawOrder.Add(P1_Offset + i - 1);
                    drawOrder.Add(P1_Offset + i);

                }
                //第二个梯(顺时针)
                for (int i = 0, j = 0; i < P2_Count; i++, j += 2)
                {
                    if (i == 0)
                    {
                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(T2_Offset + ++j);

                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(P2_Offset + ++i);
                    }
                    drawOrder.Add(T2_Offset + j);
                    drawOrder.Add(P2_Offset + i);
                    drawOrder.Add(T2_Offset + j + 1);

                    drawOrder.Add(T2_Offset + j + 1);
                    drawOrder.Add(P2_Offset + i);
                    drawOrder.Add(T2_Offset + j + 2);

                    if (i != P2_Count - 1)
                    {
                        drawOrder.Add(T2_Offset + j + 2);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(P2_Offset + i + 1);
                    }
                }
            }
            else if (hexCellData.isSlope[1] == 1)
            {
                //第一个梯(顺时针)
                for (int i = P1_Count - 1, j = T1_Count - 1; i > 0; i--, j -= 2)
                {
                    if (i == P1_Count - 1)
                    {
                        drawOrder.Add(T1_Offset + j);
                        drawOrder.Add(P1_Offset + i);
                        drawOrder.Add(T1_Offset + --j);
                    }

                    drawOrder.Add(T1_Offset + j);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(T1_Offset + j - 1);

                    drawOrder.Add(T1_Offset + j - 1);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(T1_Offset + j - 2);

                    drawOrder.Add(T1_Offset + j - 2);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(P1_Offset + i - 1);
                }
                //第二个梯(顺时针)
                for (int i = 0, j = 0; i < P2_Count; i++, j += 2)
                {
                    if (i == 0)
                    {
                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(T2_Offset + ++j);

                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(P2_Offset + ++i);
                    }
                    drawOrder.Add(T2_Offset + j);
                    drawOrder.Add(P2_Offset + i);
                    drawOrder.Add(T2_Offset + j + 1);

                    drawOrder.Add(T2_Offset + j + 1);
                    drawOrder.Add(P2_Offset + i);
                    drawOrder.Add(T2_Offset + j + 2);

                    if (i != P2_Count - 1)
                    {
                        drawOrder.Add(T2_Offset + j + 2);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(P2_Offset + i + 1);
                    }
                }
            }
            else
            {
                //第一个梯(顺时针)
                for (int i = P1_Count - 1, j = T1_Count - 1; i > 0; i--, j -= 2)
                {
                    if (i == P1_Count - 1)
                    {
                        drawOrder.Add(T1_Offset + j);
                        drawOrder.Add(P1_Offset + i);
                        drawOrder.Add(T1_Offset + --j);
                    }

                    drawOrder.Add(T1_Offset + j);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(T1_Offset + j - 1);

                    drawOrder.Add(T1_Offset + j - 1);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(T1_Offset + j - 2);

                    drawOrder.Add(T1_Offset + j - 2);
                    drawOrder.Add(P1_Offset + i);
                    drawOrder.Add(P1_Offset + i - 1);
                }
                //第二个梯(逆时针)
                for (int i = 0, j = 0; i < P2_Count; i++, j += 2)
                {
                    if (i == 0)
                    {
                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(T2_Offset + ++j);

                        drawOrder.Add(T2_Offset + j);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(P2_Offset + ++i);
                    }

                    drawOrder.Add(T2_Offset + j);
                    drawOrder.Add(P2_Offset + i);
                    drawOrder.Add(T2_Offset + j + 1);

                    drawOrder.Add(T2_Offset + j + 1);
                    drawOrder.Add(P2_Offset + i);
                    drawOrder.Add(T2_Offset + j + 2);

                    if (i != P2_Count - 1)
                    {
                        drawOrder.Add(T2_Offset + j + 2);
                        drawOrder.Add(P2_Offset + i);
                        drawOrder.Add(P2_Offset + i + 1);
                    }
                }
            }
            hexCellData.E_SETriDrawOrder.AddRange(drawOrder);
        }

        return drawOrder;
    }


    ///////////////////- 方法四 -///////////////////
    /// <summary>
    /// 返回三角过渡区域 - 方法4 - 的顶点坐标
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<Vector3> GetTriStep4Vertices(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        //方法四：两梯一平坡（要分清哪边是平坡）
        //只需要两梯边的顶点即可
        //返回的顶点组
        List<Vector3> vector3s = new List<Vector3>();
        //用于逆序
        List<Vector3> reverseVector3s = new List<Vector3>();
        //判断是哪个方向的三角过渡区域
        bool isNE_E = false;
        if (direction0 == Enums.HexDirection.NE && direction1 == Enums.HexDirection.E) { isNE_E = true; }
        //判断哪边是坡
        int isSlope;
        float height0 = hexCellData.Height;
        float height1 = _mapDataService.GetNeighbor(hexCellData, direction0).Height;
        float height2 = _mapDataService.GetNeighbor(hexCellData, direction1).Height;
        if (height0 == height1) { isSlope = 0; }
        else if (height1 == height2) { isSlope = 1; }
        else { isSlope = 2; }

        if (isNE_E)
        {
            //0边是坡
            //NE邻居的SE矩形后(1/4)顶点
            //自己的E矩形前(1/4)顶点
            if (isSlope == 0)
            {
                //1边 - NE邻居的SE矩形后(1/4)顶点
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SERectVertices[i]);
                }
                //List<Vector3> v = Map.GetNeighbor(this, Map.HexDirection.NE).GetSERectVertices;
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                }
                //2边 - 自己的E矩形前(1/4)顶点
                v.Clear();
                for (int i = 0; i < hexCellData.ERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(hexCellData.ERectVertices[i]);
                }
                //v = ERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                }
            }

            //1边是坡
            //自己的NE矩形后(1/4)顶点
            //自己的E矩形前(1/4)顶点
            else if (isSlope == 1)
            {
                //0边 - 自己的NE矩形后(1/4)顶点
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < hexCellData.NERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(hexCellData.NERectVertices[i]);
                }
                //List<Vector3> v = NERectVertices;
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    reverseVector3s.Add(v[i]);
                }
                reverseVector3s.Reverse();
                vector3s.AddRange(reverseVector3s);
                reverseVector3s.Clear();
                //2边 - 自己的E矩形前(1/4)顶点
                v.Clear();
                for (int i = 0; i < hexCellData.ERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(hexCellData.ERectVertices[i]);
                }
                //v = ERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    reverseVector3s.Add(v[i]);
                }
                reverseVector3s.Reverse();
                vector3s.AddRange(reverseVector3s);
                reverseVector3s.Clear();
            }

            //2边是坡
            //自己的NE矩形后(1/4)顶点
            //NE邻居的SE矩形后(1/4)顶点
            else
            {
                //0边 - 自己的NE矩形后(1/4)顶点
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < hexCellData.NERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(hexCellData.NERectVertices[i]);
                }
                //List<Vector3> v = NERectVertices;
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                }
                //1边 - NE邻居的SE矩形后(1/4)顶点
                v.Clear();
                for (int i = 0; i < _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SERectVertices[i]);
                }
                //v = Map.GetNeighbor(this, Map.HexDirection.NE).GetSERectVertices;
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    reverseVector3s.Add(v[i]);
                }
                reverseVector3s.Reverse();
                vector3s.AddRange(reverseVector3s);
                reverseVector3s.Clear();
            }
        }
        else
        {
            //0边是坡
            //SE邻居的NE矩形前(1/4)顶点
            //自己的SE矩形前(1/4)顶点
            if (isSlope == 0)
            {
                //1边 - SE邻居的NE矩形前(1/4)顶点
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).NERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).NERectVertices[i]);
                }
                //List<Vector3> v = Map.GetNeighbor(this, Map.HexDirection.SE).GetNERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    reverseVector3s.Add(v[i]);
                }
                reverseVector3s.Reverse();
                vector3s.AddRange(reverseVector3s);
                reverseVector3s.Clear();
                //2边 - 自己的E矩形后(1/4)顶点
                v.Clear();
                for (int i = 0; i < hexCellData.SERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(hexCellData.SERectVertices[i]);
                }
                //v = SERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                }
            }

            //1边是坡
            //自己的E矩形后(1/4)顶点
            //自己的SE矩形前(1/4)顶点
            else if (isSlope == 1)
            {
                //0边 - 自己的E矩形后(1/4)顶点
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < hexCellData.ERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(hexCellData.ERectVertices[i]);
                }
                //List<Vector3> v = ERectVertices;
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    reverseVector3s.Add(v[i]);
                }
                reverseVector3s.Reverse();
                vector3s.AddRange(reverseVector3s);
                reverseVector3s.Clear();
                //2边 - 自己的SE矩形前(1/4)顶点
                v.Clear();
                for (int i = 0; i < hexCellData.SERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(hexCellData.SERectVertices[i]);
                }
                //v = SERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    reverseVector3s.Add(v[i]);
                }
                reverseVector3s.Reverse();
                vector3s.AddRange(reverseVector3s);
                reverseVector3s.Clear();
            }

            //2边是坡
            //自己的E矩形后(1/4)顶点
            //SE邻居的NE矩形前(1/4)顶点
            else
            {
                //0边 - 自己的E矩形后(1/4)顶点
                List<Vector3> v = new List<Vector3>();
                for (int i = 0; i < hexCellData.ERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(hexCellData.ERectVertices[i]);
                }
                //List<Vector3> v = ERectVertices;
                for (int i = (v.Count * 3 / 4); i < v.Count; i++)
                {
                    vector3s.Add(v[i]);
                }
                //1边 - SE邻居的NE矩形前(1/4)顶点
                v.Clear();
                for (int i = 0; i < _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).NERectVertices.Count * 2 / 3; i++)
                {
                    v.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).NERectVertices[i]);
                }
                //v = Map.GetNeighbor(this, Map.HexDirection.SE).GetNERectVertices;
                for (int i = 0; i < (v.Count * 1 / 4); i++)
                {
                    vector3s.Add(v[i]);
                }
            }
        }

        if (isNE_E) { hexCellData.NE_ETriVertices.AddRange(vector3s); return hexCellData.NE_ETriVertices; }
        else { hexCellData.E_SETriVertices.AddRange(vector3s); return hexCellData.E_SETriVertices; }
    }

    /// <summary>
    /// 返回三角过渡区域 - 方法4 - 的uv - （旧的，复杂的方法）
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param> //顶点排序是梯1、梯2
    public List<Vector2> GetTriStep4UV_Old(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        //判断是哪个方向的三角过渡区域
        bool isNE_E = false;
        List<Vector3> vector3s = new List<Vector3>();
        List<Vector2> vector2s = new List<Vector2>();
        if (direction0 == Enums.HexDirection.NE && direction1 == Enums.HexDirection.E)
        {
            isNE_E = true;
            vector3s = hexCellData.NE_ETriVertices;
        }
        else
        {
            vector3s = hexCellData.E_SETriVertices;
        }

        //顶点排序是梯1、梯2


        //倾斜、垂直线段的长度不论什么方向，理应都是一样的，所有暂且取NE方向，即x[0,0]、x[0,1]
        //x只是比值，总二维向量的模是 (0.5)/cosθ
        float ΔY = hexCellData.x[0, 1] * (0.5f / Mathf.Cos(Mathf.Atan(2f)));
        float ΔX = hexCellData.x[0, 0] * (0.5f / Mathf.Cos(Mathf.Atan(2f)));

        //∵这是个三角形，所以ΔY、ΔX是二维向量，而非标量
        //∴通过三角函数求坐标的x,y增量
        float ΔY_x = ΔY * Mathf.Cos(Mathf.Atan(2f));
        float ΔY_y = ΔY * Mathf.Sin(Mathf.Atan(2f));

        float ΔX_x = ΔX * Mathf.Cos(Mathf.Atan(2f));
        float ΔX_y = ΔX * Mathf.Sin(Mathf.Atan(2f));

        //坐标x,y
        float Δx = 0, Δy = 0;
        //x增量一直是正的、y增量前梯是正的，后梯是负的
        //前梯
        for (int i = 0; i < vector3s.Count / 2; i++)
        {
            if (i % 2 == 0)
            {
                vector2s.Add(new Vector2(Δx, Δy));
                Δx += ΔY_x;
                Δy += ΔY_y;
            }
            else
            {
                vector2s.Add(new Vector2(Δx, Δy));
                Δx += ΔX_x;
                Δy += ΔX_y;
            }
        }
        //后梯
        for (int i = 0; i < vector3s.Count / 2; i++)
        {
            if (i % 2 == 0)
            {
                vector2s.Add(new Vector2(Δx, Δy));
                Δx += ΔY_x;
                Δy -= ΔY_y;
            }
            else
            {
                vector2s.Add(new Vector2(Δx, Δy));
                Δx += ΔX_x;
                Δy -= ΔX_y;
            }
        }

        if (isNE_E)
        {
            hexCellData.NE_ETriUV.AddRange(vector2s);
            return hexCellData.NE_ETriUV;
        }
        else
        {
            hexCellData.E_SETriUV.AddRange(vector2s);
            return hexCellData.E_SETriUV;
        }
    }

    /// <summary>
    /// 返回三角过渡区域 - 方法4 - 的uv - （新的，简单的方法）
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param> //顶点排序是梯1、梯2
    public List<Vector2> GetTriStep4UV(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        //判断是哪个方向的三角过渡区域
        bool isNE_E = false;
        List<Vector3> vector3s = new List<Vector3>();
        List<Vector2> vector2s = new List<Vector2>();
        if (direction0 == Enums.HexDirection.NE && direction1 == Enums.HexDirection.E)
        {
            isNE_E = true;
            vector3s = hexCellData.NE_ETriVertices;
        }
        else
        {
            vector3s = hexCellData.E_SETriVertices;
        }

        //顶点排序是梯1、梯2
        //三个顶点从自己开始顺时针排序分别为：(0, 1)、(0.5f, 0.2f)、(1, 1)
        //梯边其余点为其中的插值
        //设梯1的起点、梯2的终点点为(0.5f, 0.2f)、梯1终点为(0, 1)、梯2起点为(1, 1)
        Vector2 Δv1 = (new Vector2(0, 1) - new Vector2(0.5f, 0.2f)) / (vector3s.Count / 2);
        //Vector2 Δv2 = (new Vector2(1, 1) - new Vector2(0.5f, 0.2f)) / (vector3s.Count / 2);
        //Vector2 Δv1 = (new Vector2(0.5f, 0.2f) - new Vector2(0, 1)) / (vector3s.Count / 2);
        Vector2 Δv2 = (new Vector2(0.5f, 0.2f) - new Vector2(1, 1)) / (vector3s.Count / 2);
        for (int j = 0; j < 2; j++)
        {
            for (int i = 0; i < vector3s.Count / 2; i++)
            {
                if (i == 0)
                {
                    if (j == 0)
                    {
                        vector2s.Add(new Vector2(0.5f, 0.2f));
                    }
                    else
                    {
                        vector2s.Add(new Vector2(1, 1));
                    }
                }
                else if (i == (vector3s.Count / 2) - 1)
                {
                    if (j == 0)
                    {
                        vector2s.Add(new Vector2(0, 1));
                    }
                    else
                    {
                        vector2s.Add(new Vector2(0.5f, 0.2f));
                    }
                }
                else
                {
                    if (j == 0)
                    {
                        vector2s.Add(new Vector2(0.5f, 0.2f) + i * Δv1);
                    }
                    else
                    {
                        vector2s.Add(new Vector2(1, 1) + i * Δv2);
                    }
                }


            }
        }



        if (isNE_E)
        {
            hexCellData.NE_ETriUV.AddRange(vector2s);
            return hexCellData.NE_ETriUV;
        }
        else
        {
            hexCellData.E_SETriUV.AddRange(vector2s);
            return hexCellData.E_SETriUV;
        }
    }


    /// <summary>
    /// 返回三角过渡区域 - 方法4 - 的绘制顺序
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>

    public List<int> GetTriStep4DrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        //判断是哪个方向的三角过渡区域
        bool isNE_E = false;
        if (direction0 == Enums.HexDirection.NE && direction1 == Enums.HexDirection.E) { isNE_E = true; }
        List<int> drawOrder = new List<int>();
        //后梯边的偏移
        List<Vector3> vector3s = new List<Vector3>();
        if (direction0 == Enums.HexDirection.NE && direction1 == Enums.HexDirection.E)
        {
            isNE_E = true;
            vector3s = hexCellData.NE_ETriVertices;
        }
        else
        {
            vector3s = hexCellData.E_SETriVertices;
        }
        int offset = vector3s.Count / 2;
        //判断哪边是坡
        int isSlope;
        float height0 = hexCellData.Height;
        float height1 = _mapDataService.GetNeighbor(hexCellData, direction0).Height;
        float height2 = _mapDataService.GetNeighbor(hexCellData, direction1).Height;
        if (height0 == height1) { isSlope = 0; }
        else if (height1 == height2) { isSlope = 1; }
        else { isSlope = 2; }

        if (isSlope != 1)
        {
            for (int i = 0; i < hexCellData.interpCount * 2 + 1; i++)
            {
                drawOrder.Add(i);
                drawOrder.Add(i + 1);
                drawOrder.Add(i + offset);

                drawOrder.Add(i + 1);
                drawOrder.Add(i + 1 + offset);
                drawOrder.Add(i + offset);

            }
        }
        else
        {
            for (int i = 0; i < hexCellData.interpCount * 2 + 1; i++)
            {
                drawOrder.Add(i);
                drawOrder.Add(i + offset);
                drawOrder.Add(i + 1);

                drawOrder.Add(i + 1);
                drawOrder.Add(i + offset);
                drawOrder.Add(i + 1 + offset);
            }
        }

        if (isNE_E) { hexCellData.NE_ETriDrawOrder.AddRange(drawOrder); return drawOrder; }
        else { hexCellData.E_SETriDrawOrder.AddRange(drawOrder); return drawOrder; }
    }

    /// <summary>
    /// 简单插值法
    /// 在竖直Y方向上插n个值（y方向）
    /// 在水平X方向上插2n个值（x、z平面向量）
    /// 插值均分起始点和终末点
    /// 最终返回2n + 2个坐标（包含原坐标）
    /// </summary>
    /// <param name="startPoint">起始点</param>
    /// <param name="endPoint">终末点</param>
    /// <param name="interpCount">插值数量</param>
    private List<Vector3> SimpleInterpolate(Vector3 startPoint, Vector3 endPoint, int interpCount)
    {
        return RectangleTransitionMesh.CreateStepPoints(startPoint, endPoint, interpCount);
    }

    /// <summary>
    /// TriStep3求顶点坐标的中间方法
    /// 输入坡的起点、终点、以及梯的偶数点
    /// 返回坡对应梯偶数点的那一个插入点
    /// 注意！！！
    /// PointA.y 与 PointB.y 不能相等，否则返回Vector3(0, 0, 0)
    /// </summary>
    /// <param name="PointA">线段起点</param>
    /// <param name="PointB">线段终点</param>
    /// <param name="PointC">线段外的点</param>
    private Vector3 TriStep3_GetInsertionPoint(Vector3 PointA, Vector3 PointB, Vector3 PointC)
    {
        if (PointA.y == PointB.y) { return new Vector3(0, 0, 0); }
        //求直线参数方程，需要定点、方向向量
        //定点设置为点A
        //方向向量为(点B - 点A)
        Vector3 directionVector = PointB - PointA;
        //列直线参数方程
        //x = xA + t(xB - xA)
        //y = yA + t(yB - yA)
        //z = zA + t(zB - zA)
        //求参数t：
        //yC = yA + t(yB - yA)
        // t = (yC - yA)/(yB - yA)
        float t = (PointC.y - PointA.y) / (PointB.y - PointA.y);
        //求完整坐标
        float x = PointA.x + directionVector.x * t;
        float z = PointA.z + directionVector.z * t;

        return new Vector3(x, PointC.y, z);
    }


    /////////////////////////////////////////////////////////////////////- 河水 -/////////////////////////////////////////////////////////////////////
    //河水的地块管理：自己的实心区域 + 河流的下游过渡区域 
    ///////////////////- 地块实心区域 -///////////////////
    /// <summary>
    /// 返回地块实心区域的河水坐标
    /// </summary>
    public Vector3[] GetRiverVertices(ref HexCellData hexCellData)
    {
        List<Vector3> solidAreaVertices = hexCellData.SolidAreaVertices;
        //实心区域河水坐标 - ( 0" - 外围顺时针" - 内圈顺时针" ) - (1 - 12 - 6) - 19个点
        Vector3[] arrVertices = new Vector3[]
        {
            solidAreaVertices[0] + (solidAreaVertices[25] - solidAreaVertices[0]) * (1 - hexCellData.RiverWaterDepth),

            solidAreaVertices[7] + (solidAreaVertices[32] - solidAreaVertices[7]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[8] + (solidAreaVertices[33] - solidAreaVertices[8]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[9] + (solidAreaVertices[34] - solidAreaVertices[9]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[10] + (solidAreaVertices[35] - solidAreaVertices[10]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[11] + (solidAreaVertices[36] - solidAreaVertices[11]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[12] + (solidAreaVertices[37] - solidAreaVertices[12]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[13] + (solidAreaVertices[38] - solidAreaVertices[13]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[14] + (solidAreaVertices[39] - solidAreaVertices[14]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[15] + (solidAreaVertices[40] - solidAreaVertices[15]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[16] + (solidAreaVertices[41] - solidAreaVertices[16]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[17] + (solidAreaVertices[42] - solidAreaVertices[17]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[18] + (solidAreaVertices[43] - solidAreaVertices[18]) * (1 - hexCellData.RiverWaterDepth),

            solidAreaVertices[19] + (solidAreaVertices[26] - solidAreaVertices[19]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[20] + (solidAreaVertices[27] - solidAreaVertices[20]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[21] + (solidAreaVertices[28] - solidAreaVertices[21]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[22] + (solidAreaVertices[29] - solidAreaVertices[22]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[23] + (solidAreaVertices[30] - solidAreaVertices[23]) * (1 - hexCellData.RiverWaterDepth),
            solidAreaVertices[24] + (solidAreaVertices[31] - solidAreaVertices[24]) * (1 - hexCellData.RiverWaterDepth),
        };
        hexCellData.RiverVertices.AddRange(arrVertices);

        return hexCellData.RiverVertices.ToArray();
    }

    /// <summary>
    ///  返回地块实心区域的河水坐标UV
    /// </summary>
    public Vector2[] GetRiverUV(ref HexCellData hexCellData, List<int> l)
    {
        Enums.HexDirection incomingDirection = hexCellData.RiverIncomingDirection;
        Enums.HexDirection outgoingDirection = hexCellData.RiverOutgoingDirection;

        if ((hexCellData.HexType == Enums.HexType.RiverSource || hexCellData.HexType == Enums.HexType.RiverEnd) && l.Count > 6)
        {
            //所需点按河流方向排序 - 河道源头、终点都有5个点、3*3=9 个排序
            int[] points = new int[]
            {
                l[2],
                l[0],l[1],
                l[4],l[5],
            };

            for (int i = 0; i < hexCellData.RiverVertices.Count; i++)
            {
                if (i == points[0])
                {
                    hexCellData.RiverUV.Add(hexCellData.HexType == Enums.HexType.RiverSource ? new Vector2(0.5f, 0) : new Vector2(0.5f, 1));
                }
                else if (i == points[1])
                {
                    hexCellData.RiverUV.Add(hexCellData.HexType == Enums.HexType.RiverSource ? new Vector2(0, 0.4f) : new Vector2(0, 0.6f));
                }
                else if (i == points[2])
                {
                    hexCellData.RiverUV.Add(hexCellData.HexType == Enums.HexType.RiverSource ? new Vector2(1, 0.4f) : new Vector2(1, 0.6f));
                }
                else if (i == points[3])
                {
                    hexCellData.RiverUV.Add(hexCellData.HexType == Enums.HexType.RiverSource ? new Vector2(0, 1) : new Vector2(0, 0));
                }
                else if (i == points[4])
                {
                    hexCellData.RiverUV.Add(hexCellData.HexType == Enums.HexType.RiverSource ? new Vector2(1, 1) : new Vector2(1, 0));
                }
                else
                {
                    hexCellData.RiverUV.Add(new Vector2(0.5f, 0.5f));
                }
            }
        }
        else if (hexCellData.HexType == Enums.HexType.RiverMidstream && l.Count > 0)
        {
            int intervalCount = Mathf.Abs((int)outgoingDirection - (int)incomingDirection);
            //[出入方向相邻]
            if (intervalCount == 1 || intervalCount == 5)
            {
                //所需点按河流方向排序 - 河道源头、终点都有 5*2=10 个点 9*2=18 个排序
                int[] points = new int[]
                {
                    l[2],
                    l[0],l[1],
                    l[4],l[5],


                    l[9],l[10],
                    l[13],l[14],
                };

                for (int i = 0; i < hexCellData.RiverVertices.Count; i++)
                {
                    if (i == points[0])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0.45f, 0.45f));
                    }
                    else if (i == points[1])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 0.3f));
                    }
                    else if (i == points[2])
                    {
                        hexCellData.RiverUV.Add(new Vector2(1, 0.3f));
                    }
                    else if (i == points[3])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 0));
                    }
                    else if (i == points[4])
                    {
                        hexCellData.RiverUV.Add(new Vector2(1, 0));
                    }


                    else if (i == points[5])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 0.7f));
                    }
                    else if (i == points[6])
                    {
                        hexCellData.RiverUV.Add(new Vector2(1, 0.7f));
                    }
                    else if (i == points[7])
                    {
                        hexCellData.RiverUV.Add(new Vector2(1, 1));
                    }
                    else if (i == points[8])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 1));
                    }

                    else
                    {
                        hexCellData.RiverUV.Add(new Vector2(0.5f, 0.5f));
                    }
                }
            }
            //[出入方向相差2]
            else if (intervalCount == 2 || intervalCount == 4)
            {
                //间隔平面方向
                int intervalIndex = ((int)outgoingDirection + (int)incomingDirection) / 2;
                if (((int)outgoingDirection == 0 || (int)outgoingDirection == 4) && ((int)incomingDirection == 0 || (int)incomingDirection == 4) && ((int)incomingDirection != (int)outgoingDirection))
                {
                    intervalIndex = 5;
                }
                else if (((int)outgoingDirection == 1 || (int)outgoingDirection == 5) && ((int)incomingDirection == 1 || (int)incomingDirection == 5) && ((int)incomingDirection != (int)outgoingDirection))
                {
                    intervalIndex = 0;
                }

                //所需点按河流方向排序 - 河道源头、终点都有 5*2=10 个点 9*2=18 个排序
                int[] points = new int[]
                {
                    l[4],l[5],
                    l[0],l[1],

                    l[2],


                    l[10],l[11],
                    l[13],l[14],
                };

                for (int i = 0; i < hexCellData.RiverVertices.Count; i++)
                {
                    if (i == points[0])
                    {
                        hexCellData.RiverUV.Add(new Vector2(1, 0));
                    }
                    else if (i == points[1])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 0));
                    }
                    else if (i == points[2])
                    {
                        hexCellData.RiverUV.Add(new Vector2(1, 0.3f));
                    }
                    else if (i == points[3])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 0.3f));
                    }

                    else if (i == points[4])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 0.5f));
                    }

                    else if (i == points[5])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 0.7f));
                    }
                    else if (i == points[6])
                    {
                        hexCellData.RiverUV.Add(new Vector2(1, 0.7f));
                    }
                    else if (i == points[7])
                    {
                        hexCellData.RiverUV.Add(new Vector2(0, 1));
                    }
                    else if (i == points[8])
                    {
                        hexCellData.RiverUV.Add(new Vector2(1, 1));
                    }

                    else
                    {
                        hexCellData.RiverUV.Add(new Vector2(0.5f, 0.5f));
                    }
                }

            }
            //[贯穿]
            else
            {
                //(NE - SW)为类型1、(E - W)为类型2、(SE - NW)为类型3
                int[] points = new int[]
                {
                    l[0],l[1],
                    l[2],l[5],
                };
                for (int i = 0; i < hexCellData.RiverVertices.Count; i++)
                {
                    bool isAscending = (incomingDirection == Enums.HexDirection.NE || incomingDirection == Enums.HexDirection.E || incomingDirection == Enums.HexDirection.SE);
                    if (i == points[0])
                    {
                        hexCellData.RiverUV.Add(isAscending ? new Vector2(0, 0) : new Vector2(0, 1));
                    }
                    else if (i == points[1])
                    {
                        hexCellData.RiverUV.Add(isAscending ? new Vector2(1, 0) : new Vector2(1, 1));
                    }
                    else if (i == points[2])
                    {
                        hexCellData.RiverUV.Add(isAscending ? new Vector2(0, 1) : new Vector2(0, 0));
                    }
                    else if (i == points[3])
                    {
                        hexCellData.RiverUV.Add(isAscending ? new Vector2(1, 1) : new Vector2(1, 0));
                    }
                    else
                    {
                        hexCellData.RiverUV.Add(new Vector2(0.5f, 0.5f));
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < hexCellData.RiverVertices.Count; i++)
            {
                hexCellData.RiverUV.Add(new Vector2(0.5f, 0.5f));
            }
        }

        return hexCellData.RiverUV.ToArray();
    }

    /// <summary>
    /// 获取河水2实心区域的绘制顺序 - (河水始末地块)
    /// </summary>
    /// <param name="direction">方向</param>
    public List<int> GetRiverWater2DrawOrder(Enums.HexDirection direction)
    {
        switch (direction)
        {
            case Enums.HexDirection.NE:
                return new List<int>()
                {
                    13,14,0,

                    13,1,2,
                    13,2,14,
                };
            case Enums.HexDirection.E:
                return new List<int>()
                {
                    14,15,0,

                    14,3,4,
                    14,4,15,
                };
            case Enums.HexDirection.SE:
                return new List<int>()
                {
                    15,16,0,

                    15,5,6,
                    15,6,16,
                };
            case Enums.HexDirection.SW:
                return new List<int>()
                {
                    16,17,0,

                    16,7,8,
                    16,8,17,
                };
            case Enums.HexDirection.W:
                return new List<int>()
                {
                    17,18,0,

                    17,9,10,
                    17,10,18,
                };
            case Enums.HexDirection.NW:
                return new List<int>()
                {
                    18,13,0,

                    18,11,12,
                    18,12,13,
                };
            default:
                return new List<int>() { };
        }
    }

    /// <summary>
    /// 获取河水3实心区域的绘制顺序 - (河水中游地块)
    /// </summary>
    public List<int> GetRiverWater3DrawOrder(ref HexCellData hexCellData)
    {
        Enums.HexDirection incomingDirection = hexCellData.RiverIncomingDirection;
        Enums.HexDirection outgoingDirection = hexCellData.RiverOutgoingDirection;
        int intervalCount = Mathf.Abs((int)outgoingDirection - (int)incomingDirection);
        //Debug.Log("intervalCount" +  intervalCount);
        //[出入方向相邻] - 18个int
        if (intervalCount == 1 || intervalCount == 5)
        {
            List<int> l = GetRiverWater2DrawOrder(incomingDirection);
            l.AddRange(GetRiverWater2DrawOrder(outgoingDirection));
            return l;
        }
        //[出入方向相差2] - 21个int
        else if (intervalCount == 2 || intervalCount == 4)
        {
            //间隔平面方向
            int intervalIndex = ((int)outgoingDirection + (int)incomingDirection) / 2;
            if (((int)outgoingDirection == 0 || (int)outgoingDirection == 4) && ((int)incomingDirection == 0 || (int)incomingDirection == 4) && ((int)incomingDirection != (int)outgoingDirection))
            {
                intervalIndex = 5;
            }
            else if (((int)outgoingDirection == 1 || (int)outgoingDirection == 5) && ((int)incomingDirection == 1 || (int)incomingDirection == 5) && ((int)incomingDirection != (int)outgoingDirection))
            {
                intervalIndex = 0;
            }

            List<int> link = new List<int>();

            switch (intervalIndex)
            {
                case 0:
                    link.AddRange(new int[] { 13, 14, 0, });
                    break;
                case 1:
                    link.AddRange(new int[] { 14, 15, 0, });
                    break;
                case 2:
                    link.AddRange(new int[] { 15, 16, 0, });
                    break;
                case 3:
                    link.AddRange(new int[] { 16, 17, 0, });
                    break;
                case 4:
                    link.AddRange(new int[] { 17, 18, 0, });
                    break;
                case 5:
                    link.AddRange(new int[] { 18, 13, 0, });
                    break;
            }

            List<int> l = GetRiverWater2DrawOrder(incomingDirection);
            l.AddRange(GetRiverWater2DrawOrder(outgoingDirection));
            l.AddRange(link);
            return l;

        }
        //[贯穿] - 6个int
        else
        {
            //(NE - SW)为类型1、(E - W)为类型2、(SE - NW)为类型3
            List<int> l = new List<int>();
            switch (incomingDirection)
            {
                case Enums.HexDirection.NE:
                case Enums.HexDirection.SW:
                    l.AddRange(new int[] {
                        1,2,7,
                        1,7,8,
                     });
                    return l;
                case Enums.HexDirection.E:
                case Enums.HexDirection.W:
                    l.AddRange(new int[] {
                        3,4,9,
                        3,9,10,
                     });
                    return l;
                case Enums.HexDirection.SE:
                case Enums.HexDirection.NW:
                    l.AddRange(new int[] {
                        5,6,11,
                        5,11,12,
                     });
                    return l;
                default:
                    return new int[] { default }.ToList();
            }
        }
    }

    ///////////////////- 矩形区域 -///////////////////
    //河水不分坡和阶梯
    /// <summary>
    /// 返回地块下游过渡区域的河水坐标
    /// </summary>
    public List<Vector3> GetOutgoingRiverVertices(ref HexCellData hexCellData, IMapDataService _mapDataService)
    {
        //坡河水
        //坐标顺序是顺时针 - 邻居 - 自己
        List<int> points = new List<int>();
        switch (hexCellData.RiverOutgoingDirection)
        {
            case Enums.HexDirection.NE:
                points.AddRange(new int[] { 14, 13, 8, 7 });
                break;
            case Enums.HexDirection.E:
                points.AddRange(new int[] { 16, 15, 10, 9 });
                break;
            case Enums.HexDirection.SE:
                points.AddRange(new int[] { 18, 17, 12, 11 });
                break;
            case Enums.HexDirection.SW:
                points.AddRange(new int[] { 8, 7, 14, 13 });
                break;
            case Enums.HexDirection.W:
                points.AddRange(new int[] { 10, 9, 16, 15 });
                break;
            case Enums.HexDirection.NW:
                points.AddRange(new int[] { 12, 11, 18, 17 });
                break;
        }

        Vector3[] arrRectVertices = new Vector3[4];
        if (_mapDataService.GetNeighbor(hexCellData, hexCellData.RiverOutgoingDirection) != null)
        {
            arrRectVertices = new Vector3[]
            {
                _mapDataService.GetNeighbor(hexCellData, hexCellData.RiverOutgoingDirection).SolidAreaVertices[points[0]] + new Vector3(0, hexCellData.RiverDepth, 0) * ( 1 - hexCellData.RiverWaterDepth ),
                _mapDataService.GetNeighbor(hexCellData, hexCellData.RiverOutgoingDirection).SolidAreaVertices[points[1]] + new Vector3(0, hexCellData.RiverDepth, 0) * ( 1 - hexCellData.RiverWaterDepth ),
                hexCellData.SolidAreaVertices[points[2]] + new Vector3(0, hexCellData.RiverDepth, 0) * ( 1 - hexCellData.RiverWaterDepth ),
                hexCellData.SolidAreaVertices[points[3]] + new Vector3(0, hexCellData.RiverDepth, 0) * ( 1 - hexCellData.RiverWaterDepth ),
            };
        }

        hexCellData.OutgoingRiverVertices.AddRange(arrRectVertices);
        return hexCellData.OutgoingRiverVertices;
    }

    /// <summary>
    ///  返回地块下游过渡区域的河水坐标UV
    /// </summary>
    public Vector2[] GetOutgoingRiverSlopUV(ref HexCellData hexCellData)
    {
        hexCellData.OutgoingRiverUV.AddRange(new Vector2[]{
            new Vector2(0,1),
            new Vector2(1,1),
            new Vector2(0,0),
            new Vector2(1,0)

        });
        return hexCellData.OutgoingRiverUV.ToArray();
    }

    /// <summary>
    ///  返回地块下游过渡区域的河水绘制顺序
    /// </summary>
    public int[] GetOutgoingRiverSlopDrawOrder(ref HexCellData hexCellData)
    {
        hexCellData.OutgoingRiverDrawOrder.AddRange(new int[]{
            0,1,2,
            0,2,3,
        });
        return hexCellData.OutgoingRiverDrawOrder.ToArray();
    }


    /////////////////////////////////////////////////////////////////////- 湖或海 -/////////////////////////////////////////////////////////////////////
    /// <summary>
    /// 返回湖或海实心区域坐标
    /// </summary>
    public Vector3[] GetlakeOrSeaVertices(ref HexCellData hexCellData)
    {
        hexCellData.lakeOrSeaVertices = hexCellData.SolidAreaVertices.GetRange(0, 25);
        // 视觉水面世界Y = (判定水位 + 视觉偏移) * elevationStep。
        // 偏移由 waterSurfaceOffset 配置（默认2：水面顶到判定水位之上第2层的名义高度）。
        // 注意：视觉水面与判定水位(seaLevel)是有意解耦的——判定水位决定通行/材质/海岸，
        // 视觉水面只影响画出来的海面平面，两者可不同层（见 waterSurfaceOffset 说明）。
        float waterSurfaceY = hexCellData.CenterWorldCoordinate.y + (hexCellData.waterLevel + _config.waterSurfaceOffset) * _config.elevationStep;
        for (int i = 0; i < hexCellData.lakeOrSeaVertices.Count; i++)
        {
            hexCellData.lakeOrSeaVertices[i] = new Vector3(hexCellData.lakeOrSeaVertices[i].x, waterSurfaceY, hexCellData.lakeOrSeaVertices[i].z);
        }
        return hexCellData.lakeOrSeaVertices.ToArray();
    }

    /// <summary>
    /// 设置湖或海实心区域UV
    /// </summary>
    public Vector2[] GetlakeOrSeaUV(ref HexCellData hexCellData)
    {
        //实心区域顶点UV
        Vector2[] arrUV = new Vector2[]
        {            
            //本体六边形的7个点           
            new Vector2(0.5f, 0),
            new Vector2(0, 1.0f),
            new Vector2(1.0f, 1.0f),
            new Vector2(0, 1.0f),
            new Vector2(1.0f, 1.0f),
            new Vector2(0, 1.0f),
            new Vector2(1.0f, 1.0f),
            //分割边缘的12个新点（每条边多2个等分点）        
            new Vector2(1.0f/3, 1.0f),
            new Vector2(2.0f/3, 1.0f),

            new Vector2(2.0f/3, 1.0f),
            new Vector2(1.0f/3, 1.0f),

            new Vector2(1.0f/3, 1.0f),
            new Vector2(2.0f/3, 1.0f),

            new Vector2(2.0f/3, 1.0f),
            new Vector2(1.0f/3, 1.0f),

            new Vector2(1.0f/3, 1.0f),
            new Vector2(2.0f/3, 1.0f),

            new Vector2(2.0f/3, 1.0f),
            new Vector2(1.0f/3, 1.0f),
            //平面的内圈 - 即河道的前6个点
            new Vector2(1.0f/3, 1.0f/3),
            new Vector2(2.0f/3, 1.0f/3),
            new Vector2(1.0f/3, 1.0f/3),
            new Vector2(2.0f/3, 1.0f/3),
            new Vector2(1.0f/3, 1.0f/3),
            new Vector2(2.0f/3, 1.0f/3),
        };
        hexCellData.lakeOrSeaUV.AddRange(arrUV);

        return arrUV;
    }

    /// <returns></returns>
    /// <summary>
    /// 设置湖或海实心区域的顶点绘制顺序
    /// </summary>
    public int[] GetlakeOrSeaDrawOrder(ref HexCellData hexCellData)
    {
        List<int> arr = new List<int>();
        ///*
        hexCellData.lakeOrSeaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NE));
        hexCellData.lakeOrSeaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.E));
        hexCellData.lakeOrSeaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SE));
        hexCellData.lakeOrSeaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.SW));
        hexCellData.lakeOrSeaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.W));
        hexCellData.lakeOrSeaDrawOrder.AddRange(GetPlaneFace(Enums.HexDirection.NW));

        arr.AddRange(hexCellData.lakeOrSeaDrawOrder);
        return arr.ToArray();
    }

    /////////////////////////////////////////////////////////////////////- 矩形 -/////////////////////////////////////////////////////////////////////
    /// <summary>
    /// 返回湖或海矩形过渡区域的顶点坐标
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    public List<Vector3> GetlakeOrSeaRectVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        //顶点组顺序是原本的坡顶点组
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            //需要自己的1、2点 + NE邻居的4、5点
            //排序应为1、NE_5、NE_4、2
            Vector3[] arrRectVertices = new Vector3[]
            {
                hexCellData.lakeOrSeaVertices[1],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).lakeOrSeaVertices[5],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).lakeOrSeaVertices[14],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).lakeOrSeaVertices[13],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).lakeOrSeaVertices[4],
                hexCellData.lakeOrSeaVertices[2],
                hexCellData.lakeOrSeaVertices[8],
                hexCellData.lakeOrSeaVertices[7],
            };
            hexCellData.lakeOrSeaNERectVertices.AddRange(arrRectVertices);
            return hexCellData.lakeOrSeaNERectVertices;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2、3点 + E邻居的5、6点
            //排序应为2、E_6、E_5、3
            Vector3[] arrRectVertices = new Vector3[]
            {
                hexCellData.lakeOrSeaVertices[2],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).lakeOrSeaVertices[6],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).lakeOrSeaVertices[16],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).lakeOrSeaVertices[15],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).lakeOrSeaVertices[5],
                hexCellData.lakeOrSeaVertices[3],
                hexCellData.lakeOrSeaVertices[10],
                hexCellData.lakeOrSeaVertices[9],
            };
            hexCellData.lakeOrSeaERectVertices.AddRange(arrRectVertices);
            return hexCellData.lakeOrSeaERectVertices;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3、4点 + SE邻居的6、1点
            //排序应为3、SE_1、SE_6、4
            Vector3[] arrRectVertices = new Vector3[]
            {
                hexCellData.lakeOrSeaVertices[3],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).lakeOrSeaVertices[1],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).lakeOrSeaVertices[18],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).lakeOrSeaVertices[17],
                _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).lakeOrSeaVertices[6],
                hexCellData.lakeOrSeaVertices[4],
                hexCellData.lakeOrSeaVertices[12],
                hexCellData.lakeOrSeaVertices[11],
            };
            hexCellData.lakeOrSeaSERectVertices.AddRange(arrRectVertices);
            return hexCellData.lakeOrSeaSERectVertices;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回湖或海矩形过渡区域的uv
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    public List<Vector2> GetlakeOrSeaRectUV(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        Vector2[] arrRectUV = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1f/3, 1),
            new Vector2(2f/3, 1),
            new Vector2(1, 1),
            new Vector2(1, 0),
            new Vector2(1f/3, 0),
            new Vector2(2f/3, 0),
        };
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            hexCellData.lakeOrSeaNERectUV.AddRange(arrRectUV);
            return hexCellData.lakeOrSeaNERectUV;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            hexCellData.lakeOrSeaERectUV.AddRange(arrRectUV);
            return hexCellData.lakeOrSeaERectUV;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            hexCellData.lakeOrSeaSERectUV.AddRange(arrRectUV);
            return hexCellData.lakeOrSeaSERectUV;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回湖或海矩形过渡区域的矩形绘制顺序
    /// </summary>
    /// <param name="direction">哪个方向的矩形</param>
    public List<int> GetlakeOrSeaRectDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        int[] arrRectDrawOrder = new int[]
        {
            0,1,2,
            0,2,7,
            7,2,3,
            7,3,6,
            6,3,4,
            6,4,5,
        };
        if (direction == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null)
        {
            hexCellData.lakeOrSeaNERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.lakeOrSeaNERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            hexCellData.lakeOrSeaERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.lakeOrSeaERectDrawOrder;
        }
        else if (direction == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            hexCellData.lakeOrSeaSERectDrawOrder.AddRange(arrRectDrawOrder);
            return hexCellData.lakeOrSeaSERectDrawOrder;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /////////////////////////////////////////////////////////////////////- 三角 -/////////////////////////////////////////////////////////////////////
    /// <summary>
    /// 返回湖或海三角过渡区域的顶点坐标
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<Vector3> GetlakeOrSeaTriVertices(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        if (direction0 == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null && direction1 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2点 + NE邻居的4点 + E邻居的6点
            //排序应为2、NE_4、E_6
            hexCellData.lakeOrSeaNE_ETriVertices.Add(hexCellData.lakeOrSeaVertices[2]);
            hexCellData.lakeOrSeaNE_ETriVertices.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).lakeOrSeaVertices[4]);
            hexCellData.lakeOrSeaNE_ETriVertices.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).lakeOrSeaVertices[6]);
            return hexCellData.lakeOrSeaNE_ETriVertices;
        }
        else if (direction0 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null && direction1 == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3点 + E邻居的5点 + SE邻居的1点
            //排序应为3、E_5、SE_1
            hexCellData.lakeOrSeaE_SETriVertices.Add(hexCellData.lakeOrSeaVertices[3]);
            hexCellData.lakeOrSeaE_SETriVertices.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).lakeOrSeaVertices[5]);
            hexCellData.lakeOrSeaE_SETriVertices.Add(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).lakeOrSeaVertices[1]);
            return hexCellData.lakeOrSeaE_SETriVertices;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回湖或海三角形过渡区域的uv
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<Vector2> GetlakeOrSeaTriUV(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        if (direction0 == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null && direction1 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2点 + NE邻居的4点 + E邻居的6点
            //排序应为2、NE_4、E_6
            hexCellData.lakeOrSeaNE_ETriUV.Add(new Vector2(0, 0));
            hexCellData.lakeOrSeaNE_ETriUV.Add(new Vector2(0.5f, 1));
            hexCellData.lakeOrSeaNE_ETriUV.Add(new Vector2(0, 1));
            return hexCellData.lakeOrSeaNE_ETriUV;
        }
        else if (direction0 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null && direction1 == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3点 + E邻居的5点 + SE邻居的1点
            //排序应为3、E_5、SE_1
            hexCellData.lakeOrSeaE_SETriUV.Add(new Vector2(0, 0));
            hexCellData.lakeOrSeaE_SETriUV.Add(new Vector2(0.5f, 1));
            hexCellData.lakeOrSeaE_SETriUV.Add(new Vector2(0, 1));
            return hexCellData.lakeOrSeaE_SETriUV;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /// <summary>
    /// 返回湖或海三角形过渡区域的绘制顺序
    /// </summary>
    /// <param name="direction0">顺时针方向第一个夹角</param>
    /// <param name="direction1">顺时针方向第二个夹角</param>
    public List<int> GetlakeOrSeaTriDrawOrder(ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1, IMapDataService _mapDataService)
    {
        if (direction0 == Enums.HexDirection.NE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null && direction1 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            //需要自己的2点 + NE邻居的4点 + E邻居的6点
            //排序应为2、NE_4、E_6
            hexCellData.lakeOrSeaNE_ETriDrawOrder.Add(0);
            hexCellData.lakeOrSeaNE_ETriDrawOrder.Add(1);
            hexCellData.lakeOrSeaNE_ETriDrawOrder.Add(2);
            return hexCellData.lakeOrSeaNE_ETriDrawOrder;
        }
        else if (direction0 == Enums.HexDirection.E && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null && direction1 == Enums.HexDirection.SE && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            //需要自己的3点 + E邻居的5点 + SE邻居的1点
            //排序应为3、E_5、SE_1
            hexCellData.lakeOrSeaE_SETriDrawOrder.Add(0);
            hexCellData.lakeOrSeaE_SETriDrawOrder.Add(1);
            hexCellData.lakeOrSeaE_SETriDrawOrder.Add(2);
            return hexCellData.lakeOrSeaE_SETriDrawOrder;
        }
        else
        {
            Debug.LogError("方向输入出错");
            return null;
        }
    }

    /////////////////////////////////////////////////////////////////////- 海岸 -/////////////////////////////////////////////////////////////////////
    ////////////////////////- 矩形 -////////////////////////
    /// <summary>
    /// 返回海岸矩形过渡区域某个方向的顶点坐标
    /// </summary>
    public List<Vector3> GetOneDirectionCoastRectVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        //实心区域顶点沿着方向延申，形成直线，直线与地图网格的交点即为顶点。顶点按顺时针排序
        int numA, numB, numC, numD;
        int neighborNumA, neighborNumB, neighborNumC, neighborNumD;
        switch (direction)
        {
            case Enums.HexDirection.NE:
                numA = 1; numB = 2; numC = 7; numD = 8;
                neighborNumA = 4; neighborNumB = 5; neighborNumC = 13; neighborNumD = 14;
                break;
            case Enums.HexDirection.E:
                numA = 2; numB = 3; numC = 9; numD = 10;
                neighborNumA = 5; neighborNumB = 6; neighborNumC = 15; neighborNumD = 16;
                break;
            case Enums.HexDirection.SE:
                numA = 3; numB = 4; numC = 11; numD = 12;
                neighborNumA = 6; neighborNumB = 1; neighborNumC = 17; neighborNumD = 18;
                break;
            case Enums.HexDirection.SW:
                numA = 4; numB = 5; numC = 13; numD = 14;
                neighborNumA = 1; neighborNumB = 2; neighborNumC = 7; neighborNumD = 8;
                break;
            case Enums.HexDirection.W:
                numA = 5; numB = 6; numC = 15; numD = 16;
                neighborNumA = 2; neighborNumB = 3; neighborNumC = 9; neighborNumD = 10;
                break;
            case Enums.HexDirection.NW:
                numA = 6; numB = 1; numC = 17; numD = 18;
                neighborNumA = 3; neighborNumB = 4; neighborNumC = 11; neighborNumD = 12;
                break;
            default:
                numA = 0; numB = 0; numC = 0; numD = 0;
                neighborNumA = 0; neighborNumB = 0; neighborNumC = 0; neighborNumD = 0;
                break;
        }
        Vector3 p1 = hexCellData.lakeOrSeaVertices[numA];
        Vector3 p2 = hexCellData.lakeOrSeaVertices[numB];
        Vector3 p3 = hexCellData.lakeOrSeaVertices[numC];
        Vector3 p4 = hexCellData.lakeOrSeaVertices[numD];
        Vector3 v1 = GetDirectionVector3(ref hexCellData, p1, direction);
        Vector3 v2 = GetDirectionVector3(ref hexCellData, p2, direction);
        Vector3 v3 = GetDirectionVector3(ref hexCellData, p3, direction);
        Vector3 v4 = GetDirectionVector3(ref hexCellData, p4, direction);
        //float maxDistance = 100f; // 最大检测距离（避免无限延伸）

        // 陆地侧顶点：保留 XZ（伸到相邻陆地海岸边的水平位置），但把 Y 压平到水面高度，
        // 使海岸边缘不再斜着爬到陆地海岸线，而是与实心水面同高（水陆之间的竖直落差不再由此填充）。
        float wy = p1.y; // 水面高度（lakeOrSeaVertices 各点 Y 相同）
        HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, direction);
        Vector3 nA = neighbor.SolidAreaVertices[neighborNumA];
        Vector3 nB = neighbor.SolidAreaVertices[neighborNumB];
        Vector3 nC = neighbor.SolidAreaVertices[neighborNumC];
        Vector3 nD = neighbor.SolidAreaVertices[neighborNumD];

        //返回的点排序是：自己1、对方1'、对方2'、对方3'、对方4'、自己4、自己3、自己2
        return new List<Vector3>()
        {
            p1,
            new Vector3(nB.x, wy, nB.z),
            new Vector3(nD.x, wy, nD.z),
            new Vector3(nC.x, wy, nC.z),
            new Vector3(nA.x, wy, nA.z),
            p2,
            p4,
            p3,

        };
    }

    /// <summary>
    /// 获取某个点，某方向的单位向量
    /// </summary>
    private Vector3 GetDirectionVector3(ref HexCellData hexCellData, Vector3 point, Enums.HexDirection direction)
    {
        int numA, numB;
        switch (direction)
        {
            case Enums.HexDirection.NE:
                numA = 1; numB = 2;
                break;
            case Enums.HexDirection.E:
                numA = 2; numB = 3;
                break;
            case Enums.HexDirection.SE:
                numA = 3; numB = 4;
                break;
            case Enums.HexDirection.SW:
                numA = 4; numB = 5;
                break;
            case Enums.HexDirection.W:
                numA = 5; numB = 6;
                break;
            case Enums.HexDirection.NW:
                numA = 6; numB = 1;
                break;
            default:
                numA = 0; numB = 0;
                break;
        }
        if (hexCellData.lakeOrSeaVertices.Count != 0)
        {
            Vector3 zero = new Vector3(hexCellData.lakeOrSeaVertices[0].x, 0, hexCellData.lakeOrSeaVertices[0].z);
            Vector3 A = new Vector3(hexCellData.lakeOrSeaVertices[numA].x, 0, hexCellData.lakeOrSeaVertices[numA].z);
            Vector3 B = new Vector3(hexCellData.lakeOrSeaVertices[numB].x, 0, hexCellData.lakeOrSeaVertices[numB].z);
            Vector3 v = (B - A) / 2 - zero;
            return new Vector3(v.x, point.y, v.z).normalized;
        }
        else
        {
            Debug.LogError("用错方法了吧");
            return new Vector3(0, 0, 0);
        }
    }

    /// <summary>
    /// 返回射线交点
    /// </summary>
    private Vector3 GetRayPoint(Vector3 point, Vector3 direction, float maxDistance, GameObject mapMeshObj)
    {
        Vector3 vector3 = Vector3.zero;
        // 获取地图的MeshCollider
        MeshCollider mapCollider = mapMeshObj.GetComponent<MeshCollider>();
        if (mapCollider == null || !mapCollider.enabled)
        {
            Debug.LogError("地图对象缺少启用的MeshCollider！");
            return vector3;
        }

        // 构造射线（确保方向归一化，hit.distance才是真实距离）
        Ray ray = new Ray(point, direction.normalized);
        RaycastHit hit;

        // 执行射线检测
        if (Physics.Raycast(ray, out hit, maxDistance) && hit.collider == mapCollider)
        {
            Debug.Log($"找到交点：{hit.point}");
            vector3 = hit.point;
        }
        else
        {
            Debug.LogWarning($"射线未与地图网格相交！起点：{point}，方向：{direction}");
        }
        return vector3;
    }

    /// <summary>
    /// 返回海岸矩形过渡区域的uv
    /// </summary>
    public List<Vector2> GetCoastRectUV(ref HexCellData hexCellData, Vector3[] vertices)
    {
        List<Vector2> arrRectUV = new List<Vector2>();
        for (int i = 0; i < vertices.Length; i += 8)
        {
            arrRectUV.AddRange(new Vector2[]
            {
                new Vector2(0, 0.5f),
                new Vector2(0, 1),
                new Vector2(0.333f, 1),
                new Vector2(0.666f, 1),
                new Vector2(1, 1),
                new Vector2(1, 0.5f),
                new Vector2(0.666f, 0.5f),
                new Vector2(0.333f, 0.5f),
            });
        }

        hexCellData.CoastRectUV.AddRange(arrRectUV);
        return hexCellData.CoastRectUV;
    }

    /// <summary>
    /// 返回海岸矩形过渡区域的矩形绘制顺序
    /// </summary>
    public List<int> GetCoastRectDrawOrder(ref HexCellData hexCellData, Vector3[] vertices)
    {
        List<int> arrRectDrawOrder = new List<int>();
        for (int i = 0; i < vertices.Length; i += 8)
        {
            arrRectDrawOrder.AddRange(new int[]
            {
                0 + i,1 + i,2 + i,
                0 + i,2 + i,7 + i,
                7 + i,2 + i,3 + i,
                7 + i,3 + i,6 + i,
                6 + i,3 + i,4 + i,
                6 + i,4 + i,5 + i,
            });
        }

        hexCellData.CoastRectDrawOrder.AddRange(arrRectDrawOrder);
        return hexCellData.CoastRectDrawOrder;
    }

    ////////////////////////- 三角 -////////////////////////
    /// <summary>
    /// 返回海岸三角过渡区域某个方向的顶点坐标
    /// </summary>
    public List<Vector3> GetOneDirectionCoastTriVertices(ref HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        //实心区域顶点沿着方向延申，形成直线，直线与地图网格的交点即为顶点。顶点按顺时针排序
        int numA;
        int neighborNumA, neighborNumB;
        Enums.HexDirection directionA = direction, directionB;

        switch (direction)
        {
            case Enums.HexDirection.NE:
                numA = 2;
                neighborNumA = 4; neighborNumB = 6;
                directionB = Enums.HexDirection.E;
                break;
            case Enums.HexDirection.E:
                numA = 3;
                neighborNumA = 5; neighborNumB = 1;
                directionB = Enums.HexDirection.SE;
                break;
            case Enums.HexDirection.SE:
                numA = 4;
                neighborNumA = 6; neighborNumB = 2;
                directionB = Enums.HexDirection.SW;
                break;
            case Enums.HexDirection.SW:
                numA = 5;
                neighborNumA = 1; neighborNumB = 3;
                directionB = Enums.HexDirection.W;
                break;
            case Enums.HexDirection.W:
                numA = 6;
                neighborNumA = 2; neighborNumB = 4;
                directionB = Enums.HexDirection.NW;
                break;
            case Enums.HexDirection.NW:
                numA = 1;
                neighborNumA = 3; neighborNumB = 5;
                directionB = Enums.HexDirection.NE;
                break;
            default:
                numA = 0;
                neighborNumA = 0; neighborNumB = 0;
                directionB = direction;
                break;
        }

        HexCellData neighborA = _mapDataService.GetNeighbor(hexCellData, directionA);
        HexCellData neighborB = _mapDataService.GetNeighbor(hexCellData, directionB);
        if (neighborA == null || neighborB == null) { return new List<Vector3>() { }; }

        List<Vector3> vector3sA = neighborA.isCoast ? neighborA.lakeOrSeaVertices : neighborA.SolidAreaVertices;
        List<Vector3> vector3sB = neighborB.isCoast ? neighborB.lakeOrSeaVertices : neighborB.SolidAreaVertices;

        // 与矩形海岸一致：另两个角保留 XZ，Y 压平到本格水面高度，使整条海岸边缘与实心水面同高
        float wy = hexCellData.lakeOrSeaVertices[numA].y;
        Vector3 cA = vector3sA[neighborNumA];
        Vector3 cB = vector3sB[neighborNumB];

        //返回的点排序是：自己1、对方1'、对方2'
        return new List<Vector3>()
        {
            hexCellData.lakeOrSeaVertices[numA],
            new Vector3(cA.x, wy, cA.z),
            new Vector3(cB.x, wy, cB.z),
        };
    }

    /// <summary>
    /// 返回海岸三角过渡区域的uv
    /// </summary>
    public List<Vector2> GetCoastTriUV(ref HexCellData hexCellData, Vector3[] vertices)
    {
        List<Vector2> arrTriUV = new List<Vector2>();
        for (int i = 0; i < vertices.Length; i += 3)
        {
            arrTriUV.AddRange(new Vector2[]
            {
                new Vector2(0.5f, 0.5f),
                new Vector2(0, 0.7f),
                new Vector2(1, 1f),
            });
        }

        hexCellData.CoastTriUV.AddRange(arrTriUV);
        return hexCellData.CoastTriUV;
    }

    /// <summary>
    /// 返回海岸三角过渡区域的矩形绘制顺序
    /// </summary>
    public List<int> GetCoastTriDrawOrder(ref HexCellData hexCellData, Vector3[] vertices)
    {
        List<int> arrTriDrawOrder = new List<int>();
        for (int i = 0; i < vertices.Length; i += 3)
        {
            arrTriDrawOrder.AddRange(new int[]
            {
                0 + i,1 + i,2 + i,
            });
        }

        hexCellData.CoastTriDrawOrder.AddRange(arrTriDrawOrder);
        return hexCellData.CoastTriDrawOrder;
    }

    /////////////////////////////////////////////////////////////////////- 六边形网格线 -/////////////////////////////////////////////////////////////////////
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
    public List<Vector3> GetGridVertices(ref HexCellData hexCellData)
    {
        //一共有 12 个点 - 内圈6个、外圈6个
        float h = hexCellData.SolidAreaVertices[0].y;
        Vector3 center = hexCellData.SolidAreaVertices[0];
        //外圈6点
        Vector3[] outer = new Vector3[]
        {
            new Vector3(hexCellData.SolidAreaVertices[1].x,h,hexCellData.SolidAreaVertices[1].z),
            new Vector3(hexCellData.SolidAreaVertices[2].x,h,hexCellData.SolidAreaVertices[2].z),
            new Vector3(hexCellData.SolidAreaVertices[3].x,h,hexCellData.SolidAreaVertices[3].z),
            new Vector3(hexCellData.SolidAreaVertices[4].x,h,hexCellData.SolidAreaVertices[4].z),
            new Vector3(hexCellData.SolidAreaVertices[5].x,h,hexCellData.SolidAreaVertices[5].z),
            new Vector3(hexCellData.SolidAreaVertices[6].x,h,hexCellData.SolidAreaVertices[6].z),
        };
        hexCellData.GridVertices.AddRange(outer);

        //内圈6点
        //这个宽度比率是指（六边形实心区域外径 / 宽度）的比率 - 1时宽度=外径、0时宽度=0
        float widthRatio = 0.1f;
        Vector3[] innerVector = new Vector3[]
        {
            outer[0] - center,
            outer[1] - center,
            outer[2] - center,
            outer[3] - center,
            outer[4] - center,
            outer[5] - center,
        };
        Vector3[] inner = new Vector3[]
        {
            center + innerVector[0] * (1-widthRatio),
            center + innerVector[1] * (1-widthRatio),
            center + innerVector[2] * (1-widthRatio),
            center + innerVector[3] * (1-widthRatio),
            center + innerVector[4] * (1-widthRatio),
            center + innerVector[5] * (1-widthRatio),
        };
        hexCellData.GridVertices.AddRange(inner);

        return hexCellData.GridVertices;
    }

    /// <summary>
    /// 返回网格线的uv
    /// ∵顶点是固定的，所以uv也可以硬编程
    /// </summary>
    public List<Vector2> GetGridUV(ref HexCellData hexCellData)
    {
        List<Vector2> arrGridUV = new List<Vector2>();
        arrGridUV.AddRange(new Vector2[]
        {
            new Vector2(1/6.0f, 1),
            new Vector2(2/6.0f, 1),
            new Vector2(3/6.0f, 1),
            new Vector2(4/6.0f, 1),
            new Vector2(5/6.0f, 1),
            new Vector2(1, 1),

            new Vector2(1/6.0f, 0),
            new Vector2(2/6.0f, 0),
            new Vector2(3/6.0f, 0),
            new Vector2(4/6.0f, 0),
            new Vector2(5/6.0f, 0),
            new Vector2(1, 0),
        });

        hexCellData.GridUV.AddRange(arrGridUV);
        return hexCellData.GridUV;
    }

    /// <summary>
    /// 返回网格线的绘制顺序
    /// </summary>
    public List<int> GetGridDrawOrder(ref HexCellData hexCellData)
    {
        List<int> arrGridDrawOrder = new List<int>();

        arrGridDrawOrder.AddRange(new int[]
        {
            0,1,7,
            0,7,6,

            1,2,8,
            1,8,7,

            2,3,9,
            2,9,8,

            3,4,10,
            3,10,9,

            4,5,11,
            4,11,10,

            5,0,6,
            5,6,11,
        });

        hexCellData.GridDrawOrder.AddRange(arrGridDrawOrder);
        return hexCellData.GridDrawOrder;
    }


    /////////////////////////////////////////////////////////////////////- 移动显示器的路径连线 -/////////////////////////////////////////////////////////////////////
    /// <summary>
    /// 获取两个相邻的地块间连线的顶点
    /// </summary>
    public List<Vector3> GetAdjacentHexLineVertices(HexCellData StartHexCellData, HexCellData EndHexCellData)
    {
        //一共有 4 个点，2 个三角形
        Vector3 startPoint = StartHexCellData.RealCenterWorldCoordinate;
        Vector3 endPoint = EndHexCellData.RealCenterWorldCoordinate;

        //连线方向
        Vector3 lineDirection = (endPoint - startPoint);

        //两个非零向量 v₁ 和 v₂ 垂直的充要条件是：它们的 点积（内积）为 0。
        // 原向量 (a, b)，则垂直向量为：(-b, a)、(b, -a) 
        // a*(-b) + b*a = -ab + ab = 0
        // a*b + b*(-a) =  ab - ab = 0
        //∴先将连线投影到xz平面，再求两垂直方向
        Vector3 xzlineDirection = new Vector3(lineDirection.x, 0, lineDirection.z);
        xzlineDirection.Normalize();
        Vector3 DirectionA = new Vector3(-xzlineDirection.z, 0, xzlineDirection.x);
        Vector3 DirectionB = new Vector3(xzlineDirection.z, 0, -xzlineDirection.x);

        //起点方
        Vector3 PointOne = startPoint + 0.1f * DirectionA;
        Vector3 PointTwo = startPoint + 0.1f * DirectionB;
        //起点方
        Vector3 PointThree = endPoint + 0.1f * DirectionA;
        Vector3 PointFour = endPoint + 0.1f * DirectionB;

        return new List<Vector3> { PointOne, PointThree, PointFour, PointTwo };
    }

    /// <summary>
    /// 返回连线的uv
    /// ∵顶点是固定的，所以uv也可以硬编程
    /// </summary>
    public List<Vector2> GetAdjacentHexLineUV()
    {
        List<Vector2> arrAdjacentHexLineUV = new List<Vector2>();
        arrAdjacentHexLineUV.AddRange(new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0),
        });

        return arrAdjacentHexLineUV;
    }

    /// <summary>
    /// 返回连线的绘制顺序
    /// </summary>
    public List<int> GetAdjacentHexLineDrawOrder()
    {
        List<int> AdjacentHexLineDrawOrder = new List<int>();

        AdjacentHexLineDrawOrder.AddRange(new int[]
        {
            0,1,2,
            0,2,3,
        });

        return AdjacentHexLineDrawOrder;
    }


    /////////////////////////////////////////////////////////////////////- 势力范围 -/////////////////////////////////////////////////////////////////////

    /// <summary>
    /// 获取一个团块势力范围的顶点
    /// </summary>
    public List<List<Vector3>> GetOneSphereOfInfluenceVertices(List<HexCellData> hexCells, out int edgeCount, IMapDataService _mapDataService)
    {
        //旧签名：归属集合即描边集合本身（行为与原来完全一致）。
        return GetOneSphereOfInfluenceVertices(hexCells, hexCells, out edgeCount, _mapDataService);
    }

    public List<List<Vector3>> GetOneSphereOfInfluenceVertices(List<HexCellData> hexCells, ICollection<HexCellData> membershipCells, out int edgeCount, IMapDataService _mapDataService)
    {
        //membershipCells：判定"边是否为势力边界"的完整归属集合。
        //邻居只要属于 membershipCells 就算内部边不描——因此被迷雾切断的一侧（邻居属该势力但当前不可见、
        //不在描边集合 hexCells 里）不会画出"假边界"，形成开口图形。
        if (membershipCells == null) { membershipCells = hexCells; }

        edgeCount = 0;
        //一、先判断哪些地块是边缘地块 —— 边缘地块中哪些点是边缘点
        List<HexCellData> edgeHexCells = new List<HexCellData>();
        //字典中向量的顺序与edgeHexCells中的顺序相同
        List<Dictionary<int, Vector3>> edgeHexPoints = new List<Dictionary<int, Vector3>>();
        List<Dictionary<int, Vector3>> edgeHexWidthPoints = new List<Dictionary<int, Vector3>>();
        //每个边缘地块的"边界边"（角点对），顺序与 edgeHexCells 一致。
        //注意：不能靠"两端角点是否都被收集"来推断某条边是不是边界——角点可能是因它另一条边才被收集的，
        //那样会把内部共享边（相邻同势力地块之间的边）误判为边界并画出多余的线。必须按边本身记录。
        List<List<(int a, int b)>> edgeHexBoundaryPairs = new List<List<(int a, int b)>>();

        for (int i = 0; i < hexCells.Count; i++)
        {
            //是否为边界：邻居不在"归属集合"里才算边界边（真实势力边缘），
            //邻居属该势力但当前不可见时不算边界——不沿迷雾线描"假边界"。
            bool isEdgeAtNE = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.NE));
            bool isEdgeAtE = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.E));
            bool isEdgeAtSE = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.SE));
            bool isEdgeAtSW = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.SW));
            bool isEdgeAtW = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.W));
            bool isEdgeAtNW = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.NW));
            //若全部邻居都在势力范围内，该地块就不是边缘地块（只要有一个邻居不在，就是边缘地块）
            bool isEdge = (isEdgeAtNE || isEdgeAtE || isEdgeAtSE || isEdgeAtSW || isEdgeAtW || isEdgeAtNW);

            //不是边缘地块就跳过
            if (!isEdge) { continue; }
            else
            {
                //收集边缘地块
                edgeHexCells.Add(hexCells[i]);
                //收集边缘点 - 去除重复点
                List<Vector3> v = hexCells[i].SolidAreaVertices;
                List<int> index = new List<int>();
                //NE边对应1、2点
                if (isEdgeAtNE) { index.AddRange(new int[] { 1, 2 }); }
                //E边对应2、3点
                if (isEdgeAtE) { index.AddRange(new int[] { 2, 3 }); }
                //SE边对应3、4点
                if (isEdgeAtSE) { index.AddRange(new int[] { 3, 4 }); }
                //SW边对应4、5点
                if (isEdgeAtSW) { index.AddRange(new int[] { 4, 5 }); }
                //W边对应5、6点
                if (isEdgeAtW) { index.AddRange(new int[] { 5, 6 }); }
                //NW边对应6、1点
                if (isEdgeAtNW) { index.AddRange(new int[] { 6, 1 }); }
                //0点用于确定边缘线的宽度方向
                index.Add(0);
                //去重 + 升序
                List<int> unique_ascendingOrder_Index = new HashSet<int>(index).ToList();
                unique_ascendingOrder_Index.Sort();

                //将点写入列表
                Dictionary<int, Vector3> vector3s = new Dictionary<int, Vector3>();
                for (int j = 0; j < unique_ascendingOrder_Index.Count; j++)
                {
                    vector3s.Add(unique_ascendingOrder_Index[j], v[unique_ascendingOrder_Index[j]]);
                }
                edgeHexPoints.Add(vector3s);

                //记录该地块的边界边（角点对），三-1 直接据此画线
                List<(int a, int b)> boundaryPairs = new List<(int a, int b)>();
                if (isEdgeAtNE) { boundaryPairs.Add((1, 2)); }
                if (isEdgeAtE)  { boundaryPairs.Add((2, 3)); }
                if (isEdgeAtSE) { boundaryPairs.Add((3, 4)); }
                if (isEdgeAtSW) { boundaryPairs.Add((4, 5)); }
                if (isEdgeAtW)  { boundaryPairs.Add((5, 6)); }
                if (isEdgeAtNW) { boundaryPairs.Add((6, 1)); }
                edgeHexBoundaryPairs.Add(boundaryPairs);

            }
        }

        //二、求宽度点（数量与边缘点数量一致）
        for (int i = 0; i < edgeHexPoints.Count; i++)
        {
            //宽度点列表
            Dictionary<int, Vector3> widthVector3s = new Dictionary<int, Vector3>();
            //先找出0点
            Vector3 center = edgeHexPoints[i][0];
            widthVector3s.Add(0, center);

            //再逐个求出
            List<int> index = edgeHexPoints[i].Keys.ToList();
            index.Remove(0);
            for (int j = 0; j < index.Count; j++)
            {
                //宽度比例
                float widthRatio = 0.15f;
                //向量
                Vector3 v = (edgeHexPoints[i][index[j]] - center) * widthRatio;
                //新点 = 旧点 + 向量
                widthVector3s.Add(index[j], edgeHexPoints[i][index[j]] + v);
            }

            //归档 - 得到绘制边缘线的全部点
            edgeHexWidthPoints.Add(widthVector3s);
        }

        //三、将点有序排列 - 将点两两（四四）组合
        //点的有序列表 - 每一个子列表包含4个点，可构造一条边
        List<List<Vector3>> OrderedPointList = new List<List<Vector3>>();
        //1、同地块间组合
        //2、相邻地块组合

        //三-1、同地块间组合
        //按"边界边"逐条画线：一条边界边对应它的两个角点（a,b），
        //只画真正的边界边，绝不画内部共享边（相邻同势力地块之间的边）。
        for (int i = 0; i < edgeHexPoints.Count; i++)
        {
            List<Vector3> v = new List<Vector3>();

            foreach (var pair in edgeHexBoundaryPairs[i])
            {
                v.Add(edgeHexPoints[i][pair.a]);
                v.Add(edgeHexPoints[i][pair.b]);
                v.Add(edgeHexWidthPoints[i][pair.a]);
                v.Add(edgeHexWidthPoints[i][pair.b]);
                edgeCount++;
            }

            OrderedPointList.Add(v);
        }

        //三-2、相邻地块组合
        //A、确定相邻地块 - 存在相邻0、1、2、3、4、5个地块的情况
        List<List<HexCellData>> neighbors = new List<List<HexCellData>>();
        for (int i = 0; i < edgeHexCells.Count; i++)
        {
            //总有6个子元素
            HexCellData NeighberAtNE = edgeHexCells.Contains(_mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.NE)) ? _mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.NE) : null;
            HexCellData NeighberAtE = edgeHexCells.Contains(_mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.E)) ? _mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.E) : null;
            HexCellData NeighberAtSE = edgeHexCells.Contains(_mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.SE)) ? _mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.SE) : null;
            HexCellData NeighberAtSW = edgeHexCells.Contains(_mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.SW)) ? _mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.SW) : null;
            HexCellData NeighberAtW = edgeHexCells.Contains(_mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.W)) ? _mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.W) : null;
            HexCellData NeighberAtNW = edgeHexCells.Contains(_mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.NW)) ? _mapDataService.GetNeighbor(edgeHexCells[i], Enums.HexDirection.NW) : null;

            neighbors.Add(new List<HexCellData> { NeighberAtNE, NeighberAtE, NeighberAtSE, NeighberAtSW, NeighberAtW, NeighberAtNW });
        }

        for (int i = 0; i < neighbors.Count; i++)
        {
            int count = 0;
            for (int j = 0; j < neighbors[i].Count; j++)
            {
                if (neighbors[i][j] != null) { count++; }
            }
            //Debug.Log("边缘地块的邻居数：" +  count);
        }

        //Debug.Log("边缘地块数量：" + edgeHexCells.Count);

        //B、添加点对
        for (int i = 0; i < neighbors.Count; i++)
        {
            //某个边缘地块
            for (int j = 0; j < neighbors[i].Count; j++)
            {
                List<Vector3> v = new List<Vector3>();
                //该边缘地块的邻居
                if (neighbors[i][j] == null) { continue; }
                edgeCount++;
                //若该方向存在邻居
                //邻居的索引
                int index = 0;
                for (int k = 0; k < edgeHexCells.Count; k++)
                {
                    if (edgeHexCells[k] == neighbors[i][j])
                    {
                        index = k;
                        break;
                    }
                }

                //NE方向1 - n5、2 - n4
                if (j == 0)
                {
                    if (edgeHexPoints[i].ContainsKey(1) && edgeHexPoints[index].ContainsKey(5))
                    {
                        v.Add(edgeHexPoints[i][1]);
                        v.Add(edgeHexPoints[index][5]);
                        v.Add(edgeHexWidthPoints[i][1]);
                        v.Add(edgeHexWidthPoints[index][5]);
                    }

                    if (edgeHexPoints[i].ContainsKey(2) && edgeHexPoints[index].ContainsKey(4))
                    {
                        v.Add(edgeHexPoints[i][2]);
                        v.Add(edgeHexPoints[index][4]);
                        v.Add(edgeHexWidthPoints[i][2]);
                        v.Add(edgeHexWidthPoints[index][4]);
                    }
                }
                //E方向2 - n6、3 - n5
                else if (j == 1)
                {
                    if (edgeHexPoints[i].ContainsKey(2) && edgeHexPoints[index].ContainsKey(6))
                    {
                        v.Add(edgeHexPoints[i][2]);
                        v.Add(edgeHexPoints[index][6]);
                        v.Add(edgeHexWidthPoints[i][2]);
                        v.Add(edgeHexWidthPoints[index][6]);
                    }

                    if (edgeHexPoints[i].ContainsKey(3) && edgeHexPoints[index].ContainsKey(5))
                    {
                        v.Add(edgeHexPoints[i][3]);
                        v.Add(edgeHexPoints[index][5]);
                        v.Add(edgeHexWidthPoints[i][3]);
                        v.Add(edgeHexWidthPoints[index][5]);
                    }
                }
                //SE方向3 - n1、4 - n6
                else if (j == 2)
                {
                    if (edgeHexPoints[i].ContainsKey(3) && edgeHexPoints[index].ContainsKey(1))
                    {
                        v.Add(edgeHexPoints[i][3]);
                        v.Add(edgeHexPoints[index][1]);
                        v.Add(edgeHexWidthPoints[i][3]);
                        v.Add(edgeHexWidthPoints[index][1]);
                    }

                    if (edgeHexPoints[i].ContainsKey(4) && edgeHexPoints[index].ContainsKey(6))
                    {
                        v.Add(edgeHexPoints[i][4]);
                        v.Add(edgeHexPoints[index][6]);
                        v.Add(edgeHexWidthPoints[i][4]);
                        v.Add(edgeHexWidthPoints[index][6]);
                    }
                }
                //SW方向4 - n2、5 - n1
                else if (j == 3)
                {
                    if (edgeHexPoints[i].ContainsKey(4) && edgeHexPoints[index].ContainsKey(2))
                    {
                        v.Add(edgeHexPoints[i][4]);
                        v.Add(edgeHexPoints[index][2]);
                        v.Add(edgeHexWidthPoints[i][4]);
                        v.Add(edgeHexWidthPoints[index][2]);
                    }

                    if (edgeHexPoints[i].ContainsKey(5) && edgeHexPoints[index].ContainsKey(1))
                    {
                        v.Add(edgeHexPoints[i][5]);
                        v.Add(edgeHexPoints[index][1]);
                        v.Add(edgeHexWidthPoints[i][5]);
                        v.Add(edgeHexWidthPoints[index][1]);
                    }
                }
                //W方向5 - n3、6 - n2
                else if (j == 4)
                {
                    if (edgeHexPoints[i].ContainsKey(5) && edgeHexPoints[index].ContainsKey(3))
                    {
                        v.Add(edgeHexPoints[i][5]);
                        v.Add(edgeHexPoints[index][3]);
                        v.Add(edgeHexWidthPoints[i][5]);
                        v.Add(edgeHexWidthPoints[index][3]);
                    }

                    if (edgeHexPoints[i].ContainsKey(6) && edgeHexPoints[index].ContainsKey(2))
                    {
                        v.Add(edgeHexPoints[i][6]);
                        v.Add(edgeHexPoints[index][2]);
                        v.Add(edgeHexWidthPoints[i][6]);
                        v.Add(edgeHexWidthPoints[index][2]);
                    }
                }
                //NW方向6 - n4、1 - n3
                else if (j == 5)
                {
                    if (edgeHexPoints[i].ContainsKey(6) && edgeHexPoints[index].ContainsKey(4))
                    {
                        v.Add(edgeHexPoints[i][6]);
                        v.Add(edgeHexPoints[index][4]);
                        v.Add(edgeHexWidthPoints[i][6]);
                        v.Add(edgeHexWidthPoints[index][4]);
                    }

                    if (edgeHexPoints[i].ContainsKey(1) && edgeHexPoints[index].ContainsKey(3))
                    {
                        v.Add(edgeHexPoints[i][1]);
                        v.Add(edgeHexPoints[index][3]);
                        v.Add(edgeHexWidthPoints[i][1]);
                        v.Add(edgeHexWidthPoints[index][3]);
                    }
                }

                OrderedPointList.Add(v);
            }

        }
        //*/
        //edgeCount = OrderedPointList.Count;
        return OrderedPointList;
    }

    /// <summary>
    /// 提取势力范围边界线段与角点（实体城墙/城墩用）。
    /// 复用与 GetOneSphereOfInfluenceVertices 完全相同的边界判定逻辑：
    ///   - 三-1（HexEdge）：每条边界边取该地块的两个真实角点（SolidAreaVertices 的 1~6 点）；
    ///   - 三-2（Transition）：相邻边缘地块之间的过渡边，取两个不同地块的对应角点；
    ///   - 角点集合：所有边界线段端点去重（空间量化）。
    /// </summary>
    public void ExtractSphereOfInfluenceBoundary(
        List<HexCellData> hexCells,
        ICollection<HexCellData> membershipCells,
        IMapDataService _mapDataService,
        List<BoundarySegment> segments,
        List<Vector3> cornerPoints)
    {
        if (membershipCells == null) { membershipCells = hexCells; }
        segments.Clear();
        cornerPoints.Clear();

        // 角点去重：空间量化（0.01 单位精度）
        var cornerKeys = new HashSet<long>();
        void AddCorner(Vector3 p)
        {
            long key = QuantizeKey(p);
            if (cornerKeys.Add(key)) cornerPoints.Add(p);
        }

        // 一、收集边缘地块及其边界边（与 GetOneSphereOfInfluenceVertices 同款判定）
        List<HexCellData> edgeHexCells = new List<HexCellData>();
        // 每个边缘地块的角点字典：索引(1~6, 0=中心) → 世界坐标
        List<Dictionary<int, Vector3>> edgeHexPoints = new List<Dictionary<int, Vector3>>();
        // 每个边缘地块的边界边（角点对）
        List<List<(int a, int b)>> edgeHexBoundaryPairs = new List<List<(int a, int b)>>();

        for (int i = 0; i < hexCells.Count; i++)
        {
            bool isEdgeAtNE = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.NE));
            bool isEdgeAtE  = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.E));
            bool isEdgeAtSE = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.SE));
            bool isEdgeAtSW = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.SW));
            bool isEdgeAtW  = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.W));
            bool isEdgeAtNW = !membershipCells.Contains(_mapDataService.GetNeighbor(hexCells[i], Enums.HexDirection.NW));
            bool isEdge = (isEdgeAtNE || isEdgeAtE || isEdgeAtSE || isEdgeAtSW || isEdgeAtW || isEdgeAtNW);

            if (!isEdge) { continue; }

            edgeHexCells.Add(hexCells[i]);

            List<Vector3> v = hexCells[i].SolidAreaVertices;
            List<int> index = new List<int>();
            if (isEdgeAtNE) { index.AddRange(new int[] { 1, 2 }); }
            if (isEdgeAtE)  { index.AddRange(new int[] { 2, 3 }); }
            if (isEdgeAtSE) { index.AddRange(new int[] { 3, 4 }); }
            if (isEdgeAtSW) { index.AddRange(new int[] { 4, 5 }); }
            if (isEdgeAtW)  { index.AddRange(new int[] { 5, 6 }); }
            if (isEdgeAtNW) { index.AddRange(new int[] { 6, 1 }); }
            index.Add(0);
            List<int> uniqueIndex = new HashSet<int>(index).ToList();
            uniqueIndex.Sort();

            Dictionary<int, Vector3> vector3s = new Dictionary<int, Vector3>();
            for (int j = 0; j < uniqueIndex.Count; j++)
                vector3s.Add(uniqueIndex[j], v[uniqueIndex[j]]);
            edgeHexPoints.Add(vector3s);

            List<(int a, int b)> boundaryPairs = new List<(int a, int b)>();
            if (isEdgeAtNE) { boundaryPairs.Add((1, 2)); }
            if (isEdgeAtE)  { boundaryPairs.Add((2, 3)); }
            if (isEdgeAtSE) { boundaryPairs.Add((3, 4)); }
            if (isEdgeAtSW) { boundaryPairs.Add((4, 5)); }
            if (isEdgeAtW)  { boundaryPairs.Add((5, 6)); }
            if (isEdgeAtNW) { boundaryPairs.Add((6, 1)); }
            edgeHexBoundaryPairs.Add(boundaryPairs);
        }

        // 二、三-1：地块自身的边界边 → HexEdge 线段
        for (int i = 0; i < edgeHexPoints.Count; i++)
        {
            foreach (var pair in edgeHexBoundaryPairs[i])
            {
                Vector3 a = edgeHexPoints[i][pair.a];
                Vector3 b = edgeHexPoints[i][pair.b];
                segments.Add(new BoundarySegment(a, b, BoundarySegmentType.HexEdge));
                AddCorner(a);
                AddCorner(b);
            }
        }

        // 三、三-2：相邻边缘地块之间的过渡边 → Transition 线段
        // 各方向对应的角点对（与 GetOneSphereOfInfluenceVertices 三-2 完全一致）
        // j: 0=NE,1=E,2=SE,3=SW,4=W,5=NW
        (int self, int neighbor)[][] transitionPairs = new (int, int)[][]
        {
            new (int,int)[] { (1, 5), (2, 4) }, // NE
            new (int,int)[] { (2, 6), (3, 5) }, // E
            new (int,int)[] { (3, 1), (4, 6) }, // SE
            new (int,int)[] { (4, 2), (5, 1) }, // SW
            new (int,int)[] { (5, 3), (6, 2) }, // W
            new (int,int)[] { (6, 4), (1, 3) }, // NW
        };
        Enums.HexDirection[] dirs =
        {
            Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE,
            Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW
        };

        for (int i = 0; i < edgeHexCells.Count; i++)
        {
            for (int j = 0; j < 6; j++)
            {
                HexCellData neighborCell = _mapDataService.GetNeighbor(edgeHexCells[i], dirs[j]);
                if (neighborCell == null || !edgeHexCells.Contains(neighborCell)) continue;

                int index = edgeHexCells.IndexOf(neighborCell);
                if (index < 0) continue;

                foreach (var (self, nb) in transitionPairs[j])
                {
                    if (edgeHexPoints[i].ContainsKey(self) && edgeHexPoints[index].ContainsKey(nb))
                    {
                        Vector3 a = edgeHexPoints[i][self];
                        Vector3 b = edgeHexPoints[index][nb];
                        segments.Add(new BoundarySegment(a, b, BoundarySegmentType.Transition));
                        AddCorner(a);
                        AddCorner(b);
                    }
                }
            }
        }
    }

    // 空间量化键（0.01 单位精度），用于角点去重
    private static long QuantizeKey(Vector3 p)
    {
        long qx = (long)Mathf.Round(p.x * 100f);
        long qy = (long)Mathf.Round(p.y * 100f);
        long qz = (long)Mathf.Round(p.z * 100f);
        // 打包成单一 long（各占约 21 位，范围足够地图尺度）
        return (qx & 0x1FFFFF) | ((qy & 0x1FFFFF) << 21) | ((qz & 0x1FFFFF) << 42);
    }

    /// <summary>
    /// 获取一段边缘线的绘制UV
    /// ∵顶点是固定的，所以uv也可以硬编程
    /// </summary>
    public List<Vector2> GetOneSphereOfInfluenceUV()
    {
        List<Vector2> UV = new List<Vector2>();
        UV.AddRange(new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0),
        });

        return UV;
    }

    /// <summary>
    /// 返回一段边缘线的绘制顺序
    /// </summary>
    public List<int> GetOneSphereOfInfluenceDrawOrder()
    {
        List<int> DrawOrder = new List<int>();

        DrawOrder.AddRange(new int[]
        {
            0,2,1,
            2,3,1,
        });

        return DrawOrder;
    }


    /////////////////////////////////////////////////////////////////////- 迷雾 -/////////////////////////////////////////////////////////////////////

    /// <summary>
    /// 迷雾连接面片边界：输出封皮内边矩形（与 GetFogVertices 的 outerBoundary 相同）
    /// 和地图真实不规则轮廓（边缘地块开放边的有序真实顶点，含扰动与高度）。
    /// 连接面片 = 矩形（外）与真实轮廓（洞）之间的锯齿环带，用于闭合
    /// “不规则地图边缘 ↔ 矩形封皮内边”之间的缝隙。
    /// </summary>
    public void GetFogConnectorBoundaries(out List<Vector3> rectBoundary, out List<Vector3> realOutline,
        out List<Vector3> slopeOuterBoundary, IMapDataService _mapDataService)
    {
        realOutline = BuildMapRealOutline(_mapDataService);
        rectBoundary = BuildRectBoundary(realOutline);
        slopeOuterBoundary = BuildSlopeOuterBoundary(realOutline, rectBoundary, _config.fogConnectorSlopeWidth);
    }

    private static void AddOpenEdgePoints(List<Vector3> outline, HexCellData cell, int direction)
    {
        switch (direction)
        {
            case 0: outline.AddRange(new[] { cell.SolidAreaVertices[1], cell.SolidAreaVertices[7], cell.SolidAreaVertices[8], cell.SolidAreaVertices[2] }); break;
            case 1: outline.AddRange(new[] { cell.SolidAreaVertices[2], cell.SolidAreaVertices[9], cell.SolidAreaVertices[10], cell.SolidAreaVertices[3] }); break;
            case 2: outline.AddRange(new[] { cell.SolidAreaVertices[3], cell.SolidAreaVertices[11], cell.SolidAreaVertices[12], cell.SolidAreaVertices[4] }); break;
            case 3: outline.AddRange(new[] { cell.SolidAreaVertices[4], cell.SolidAreaVertices[13], cell.SolidAreaVertices[14], cell.SolidAreaVertices[5] }); break;
            case 4: outline.AddRange(new[] { cell.SolidAreaVertices[5], cell.SolidAreaVertices[15], cell.SolidAreaVertices[16], cell.SolidAreaVertices[6] }); break;
            default: outline.AddRange(new[] { cell.SolidAreaVertices[6], cell.SolidAreaVertices[17], cell.SolidAreaVertices[18], cell.SolidAreaVertices[1] }); break;
        }
    }

    // SolidAreaRatio 会让相邻地块的实体边之间保留过渡区域，因此跨地块端点并不重合。
    // 按地图外围地块的确定顺序收集开放边，并由相邻点显式跨过这些过渡区域形成闭环。
    private List<Vector3> BuildMapRealOutline(IMapDataService _mapDataService)
    {
        int xNum = _config.xNumber;
        int zNum = _config.zNumber;
        Dictionary<int, HexCellData> cellsByOrder = _mapDataService.GetOrderToCell();
        List<HexCellData> boundaryCells = new List<HexCellData>();
        for (int i = 1; i < zNum; i++) boundaryCells.Add(cellsByOrder[xNum * i]);
        for (int i = cellsByOrder.Count - (xNum - 1); i < cellsByOrder.Count; i++)
            boundaryCells.Add(cellsByOrder[i]);
        for (int i = zNum - 1; i > 0; i--) boundaryCells.Add(cellsByOrder[xNum * i - 1]);
        for (int i = xNum - 2; i >= 0; i--) boundaryCells.Add(cellsByOrder[i]);

        List<Vector3> outline = new List<Vector3>();
        foreach (HexCellData cell in boundaryCells)
        {
            List<int> openDirections = new List<int>();
            for (int direction = 0; direction < 6; direction++)
            {
                if (_mapDataService.GetNeighbor(cell, (Enums.HexDirection)direction) == null)
                    openDirections.Add(direction);
            }
            openDirections.Sort();
            int split = -1;
            for (int i = 0; i < openDirections.Count - 1; i++)
            {
                if (openDirections[i + 1] - openDirections[i] > 1)
                {
                    split = i + 1;
                    break;
                }
            }
            if (split > 0)
            {
                List<int> reordered = openDirections.GetRange(split, openDirections.Count - split);
                reordered.AddRange(openDirections.GetRange(0, split));
                openDirections = reordered;
            }

            foreach (int direction in openDirections) AddOpenEdgePoints(outline, cell, direction);
        }

        for (int i = outline.Count - 1; i > 0; i--)
        {
            if ((outline[i] - outline[i - 1]).sqrMagnitude < 1e-6f) outline.RemoveAt(i);
        }
        return outline;
    }

    private static List<Vector3> BuildSlopeOuterBoundary(List<Vector3> inner, List<Vector3> rectangle, float slopeWidth)
    {
        List<Vector3> outer = new List<Vector3>();
        if (inner == null || inner.Count < 3 || rectangle == null || rectangle.Count < 4) return outer;

        float minX = rectangle.Min(v => v.x);
        float maxX = rectangle.Max(v => v.x);
        float minZ = rectangle.Min(v => v.z);
        float maxZ = rectangle.Max(v => v.z);
        float minY = rectangle[0].y;
        Vector2 center = Vector2.zero;
        foreach (Vector3 point in inner) center += new Vector2(point.x, point.z);
        center /= inner.Count;

        foreach (Vector3 point in inner)
        {
            Vector2 start = new Vector2(point.x, point.z);
            Vector2 direction = start - center;
            float tx = direction.x > 0f ? (maxX - center.x) / direction.x
                : direction.x < 0f ? (minX - center.x) / direction.x : float.PositiveInfinity;
            float tz = direction.y > 0f ? (maxZ - center.y) / direction.y
                : direction.y < 0f ? (minZ - center.y) / direction.y : float.PositiveInfinity;
            float t = Mathf.Min(tx, tz);
            Vector2 rectanglePoint = center + direction * t;
            float distance = Vector2.Distance(start, rectanglePoint);
            float amount = distance > 0.0001f ? Mathf.Min(1f, slopeWidth / distance) : 1f;
            Vector2 xz = Vector2.Lerp(start, rectanglePoint, amount);
            outer.Add(new Vector3(xz.x, minY, xz.y));
        }

        return outer;
    }
    // 真实轮廓的包围盒矩形（与 GetFogVertices 输出的 RectPoints 同一算法，保证与封皮内边重合）
    private static List<Vector3> BuildRectBoundary(List<Vector3> outline)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        float MinY = float.MaxValue;
        const float additionalValue_X = 1f;
        const float additionalValue_Z = 1f;

        foreach (Vector3 v in outline)
        {
            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.z < minZ) minZ = v.z;
            if (v.z > maxZ) maxZ = v.z;
            if (v.y < MinY) MinY = v.y;
        }

        return new List<Vector3>
        {
            new Vector3(minX - additionalValue_X, MinY, maxZ + additionalValue_Z), // 左上
            new Vector3(maxX + additionalValue_X, MinY, maxZ + additionalValue_Z), // 右上
            new Vector3(maxX + additionalValue_X, MinY, minZ - additionalValue_Z), // 右下
            new Vector3(minX - additionalValue_X, MinY, minZ - additionalValue_Z), // 左下
        };
    }

    public void GetFogVertices(out List<Vector3> outerBoundary, out List<List<Vector3>> holesVector3, IMapDataService _mapDataService)
    {
        outerBoundary = new List<Vector3>();
        holesVector3 = new List<List<Vector3>>();

        //一、整个地图就是一个未探索区域的团块，外轮廓的边缘就是地图边缘
        List<HexCellData> outerBoundaryHex = new List<HexCellData>();
        //1.如何获取地图边缘地块
        //解：通过xNum、zNum、地块绘制顺序（从 0 开始计数），计算得出，需排序
        int xNum = _config.xNumber;
        int zNum = _config.zNumber;
        Dictionary<int, HexCellData> d = _mapDataService.GetOrderToCell();
        //(xNum * n) (从小到大 -【n∈[1，zNum], n∈N*】)
        for (int i = 1; i < zNum; i++)
        {
            outerBoundaryHex.Add(d[xNum * i]);
        }
        //后 xNum-1 个地块 (从小到大)
        for (int i = d.Values.Count - (xNum - 1); i < d.Values.Count; i++)
        {
            outerBoundaryHex.Add(d[i]);
        }
        //(xNum * n' - 1) (从大到小 -【n'∈[0，zNum-1], n'∈N*】)
        for (int i = zNum - 1; i > 0; i--)
        {
            outerBoundaryHex.Add(d[xNum * i - 1]);
        }
        //前 xNum-1 个地块 (从大到小)
        for (int i = (xNum - 1) - 1; i >= 0; i--)
        {
            outerBoundaryHex.Add(d[i]);
        }


        //2.得到边缘地块后，如何获取顶点
        Dictionary<HexCellData, List<int>> Hex_Boundary = new Dictionary<HexCellData, List<int>>();
        List<Vector3> outerBoundaryVector3 = new List<Vector3>();
        //解：从边缘地块组，按顺序 - 
        //    邻居为空的方向为边缘边，边缘地块可得出1、2、3、4条边缘边
        //    这些边是“连续”的
        //    先从小到大排列，若能排完全部边，则该排序为正确排序
        //    若出现突变序号，则从突变序号重新开始（eg. 1 2 5 6 - 5 6 1 2）
        //    
        //    从而得到每个边缘地块的对应顺序边
        for (int i = 0; i < outerBoundaryHex.Count; i++)
        {
            List<int> ints = new List<int>();
            for (int j = 0; j < 6; j++)
            {
                HexCellData h = _mapDataService.GetNeighbor(outerBoundaryHex[i], (Enums.HexDirection)j);
                if (h != null) { continue; }
                ints.Add(j);
            }

            ints.Sort();

            int outlierIndex = -1;
            for (int j = 0; j < ints.Count - 1; j++)
            {
                if (ints[j + 1] - ints[j] != 1)
                {
                    outlierIndex = j + 1;
                    break;
                }
            }

            if (outlierIndex != -1 && outlierIndex < ints.Count)
            {
                // 拆分：突变点到末尾的子列表 + 0到突变点前的子列表
                List<int> newInts = ints.GetRange(outlierIndex, ints.Count - outlierIndex);
                newInts.AddRange(ints.GetRange(0, outlierIndex));
                // 替换为重组后的新列表
                ints = newInts;
            }

            Hex_Boundary.Add(outerBoundaryHex[i], ints);
        }


        //    然后根据这些边，获取边对应的点
        //    0边 - 1、2点
        //    1边 - 2、3点
        //    2边 - 3、4点
        //    3边 - 4、5点
        //    4边 - 5、6点
        //    5边 - 6、1点
        //    最后Points去除重复点(即第一次出现外的点)，不干扰排序
        for (int i = 0; i < outerBoundaryHex.Count; i++)
        {
            List<int> ints = Hex_Boundary[outerBoundaryHex[i]];
            List<int> Points = new List<int>();

            for (int j = 0; j < ints.Count; j++)
            {
                int index = ints[j];
                switch (index)
                {
                    case 0: Points.AddRange(new[] { 1, 2 }); break;
                    case 1: Points.AddRange(new[] { 2, 3 }); break;
                    case 2: Points.AddRange(new[] { 3, 4 }); break;
                    case 3: Points.AddRange(new[] { 4, 5 }); break;
                    case 4: Points.AddRange(new[] { 5, 6 }); break;
                    case 5: Points.AddRange(new[] { 6, 1 }); break;
                }
            }

            List<int> uniquePoints = new List<int>();
            foreach (int point in Points)
            {
                // 仅当点未在结果列表中出现过，才添加 → 剔除后续重复项，不干扰排序
                if (!uniquePoints.Contains(point))
                {
                    uniquePoints.Add(point);
                }
            }

            Points = uniquePoints;

            foreach (int point in Points)
            {
                outerBoundaryVector3.Add(outerBoundaryHex[i].SolidAreaVertices[point]);
            }
        }

        //外轮廓应该是一个矩形（包围outerBoundaryVector3）
        // 1. 初始化极值
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float MinY = float.MaxValue;

        //float additionalValue_X = 12;
        //float additionalValue_Z = 12;
        float additionalValue_X = 1f;
        float additionalValue_Z = 1f;

        // 2. 遍历绿色点集获取边界
        foreach (Vector3 v in outerBoundaryVector3)
        {
            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.z < minZ) minZ = v.z;
            if (v.z > maxZ) maxZ = v.z;
            if (v.y < MinY) MinY = v.y; // 获取最小的 Y 值
        }
        // 3. 构建矩形顶点
        // 假设顺序为：左上 -> 右上 -> 右下 -> 左下 (顺时针)
        List<Vector3> RectPoints = new List<Vector3>();
        RectPoints.Add(new Vector3(minX - additionalValue_X, MinY, maxZ + additionalValue_Z)); // 左上
        RectPoints.Add(new Vector3(maxX + additionalValue_X, MinY, maxZ + additionalValue_Z)); // 右上
        RectPoints.Add(new Vector3(maxX + additionalValue_X, MinY, minZ - additionalValue_Z)); // 右下
        RectPoints.Add(new Vector3(minX - additionalValue_X, MinY, minZ - additionalValue_Z)); // 左下


        outerBoundary = RectPoints;
        //outerBoundary = outerBoundaryVector3;

        //二、区分迷雾团块中的已探索地块团块
        List<List<HexCellData>> holeHexsList = new List<List<HexCellData>>();
        List<HexCellData> holeHexs = new List<HexCellData>();
        List<HexCellData> hexs = _mapDataService.GetAllCells();

        foreach (HexCellData hex in hexs)
        {
            if (hex.IsExplored)
            {
                holeHexs.Add(hex);
            }
        }

        //Debug.Log($"已探索地块数量：{holeHexs.Count}");
        //1.如何从已探索地块中区分不同的团块？
        //解：深度优先搜索
        // - 先随机选择一个已探索地块，对其深度优先搜索，其得到的全部地块为一个团块
        // - 再从其他已探索地块中，重复上述步骤，直至区分所有已探索地块
        while (holeHexs.Count > 0)
        {
            //Debug.Log($"还剩 - {holeHexs.Count} - 个已探索地块");
            //一个已探索区域团块
            List<HexCellData> hexCellDatas = new List<HexCellData>();
            //栈 - 储存已访问节点
            Stack<HexCellData> stack = new Stack<HexCellData>();
            //就拿第一个
            HexCellData holeHex = holeHexs[0];
            //先将该地块入栈
            stack.Push(holeHex);

            while (stack.Count > 0)
            {
                //Debug.Log($"该地块的生成序号是：{holeHex.GenerateOrder}");
                //将该地块加入团块
                if (!hexCellDatas.Contains(holeHex))
                    hexCellDatas.Add(holeHex);

                //将该地块从“全部已探索地块”中去除
                if (holeHexs.Contains(holeHex))
                    holeHexs.Remove(holeHex);

                //该地块的可延伸方向
                List<Enums.HexDirection> directions = new List<Enums.HexDirection>();
                for (int i = 0; i < 6; i++)
                {
                    HexCellData neighbor = _mapDataService.GetNeighbor(holeHex, (Enums.HexDirection)i);
                    //该邻居存在
                    if (neighbor == null) { continue; }
                    //该邻居已探索
                    if (!neighbor.IsExplored) { continue; }
                    //该邻居未被深度搜索访问过
                    if (!holeHexs.Contains(neighbor)) { continue; }

                    directions.Add((Enums.HexDirection)i);
                }

                //选择下一个节点
                Enums.HexDirection direction = new Enums.HexDirection();
                if (directions.Count > 0)
                {
                    //先将该地块入栈
                    if (!stack.Contains(holeHex)) { stack.Push(holeHex); }
                    //按顺序选择延申方向
                    direction = directions[0];
                    //下一个节点
                    holeHex = _mapDataService.GetNeighbor(holeHex, direction);
                }
                else
                {
                    //从栈中弹出节点
                    holeHex = stack.Pop();
                }
            }
            //Debug.Log($"一个团块中有 - {hexCellDatas.Count} - 个地块");
            holeHexsList.Add(hexCellDatas);
        }

        //Debug.Log($"有 - {holeHexsList.Count} - 个团块");

        //2.如何在一个团块中找出“边缘边”（以“边”为单位，而非“地块”为单位）
        //解：深度搜索
        /*
        // - 先剔除非边缘地块（六个邻居皆探索就是非边缘地块）
        // - 选一个边缘地块，按顺时针选一条边缘边的第一个点

        ///开始迭代
        // - 一个点涉及两个方向，即对应两条边
        //   1点 - 5边、0边
        //   2点 - 0边、1边
        //   3点 - 1边、2边
        //   4点 - 2边、3边
        //   5点 - 3边、4边
        //   6点 - 4边、5边
        //
        // - 一条边对应两种邻居（已探索边缘地块、未探索地块）
        //
        // - 每种邻居有其对应方案
        //    已探索边缘地块 - 过渡至该边缘地块，即生成一条过渡边缘边
        //    未探索地块 - 连接该边的另一点，即生成一条地块边缘边
        //
        // - 总结：三个变量，各自组合，以生成一条边（获取该边的另一个点），然后以该新点继续迭代，直至再次出现原点
        ///迭代结束

        //获取点的方式 - 开始点：随机选择、迭代点：按逻辑得出
        //获取边的方式 - 起始点按顺时针选，后续的新点就只有“进入”边外的另一边 
        //获取邻居方式 - 通过函数获取

        // - 一共有 6 * 2 * 2 = 24 种组合(就24种可以硬编码，方便检查)
        //1点 5边 已探索：NW 邻居的 3 点 ✔
        //1点 0边 已探索：NE 邻居的 5 点 ✔
        //
        //1点 5边 未探索：自己的 6 点 ✔
        //1点 0边 未探索：自己的 2 点 ✔
        //
        //2点 0边 已探索：NE 邻居的 4 点 ✔
        //2点 1边 已探索：E  邻居的 6 点 ✔
        //
        //2点 0边 未探索：自己的 1 点 ✔
        //2点 1边 未探索：自己的 3 点 ✔
        //
        //3点 1边 已探索：E  邻居的 5 点 ✔
        //3点 2边 已探索：SE 邻居的 1 点 ✔
        //
        //3点 1边 未探索：自己的 2 点 ✔
        //3点 2边 未探索：自己的 4 点 ✔
        //
        //4点 2边 已探索：SE 邻居的 6 点 ✔
        //4点 3边 已探索：SW 邻居的 2 点 ✔
        //
        //4点 2边 未探索：自己的 3 点 ✔
        //4点 3边 未探索：自己的 5 点 ✔
        //
        //5点 3边 已探索：SW 邻居的 1 点 ✔
        //5点 4边 已探索：W  邻居的 3 点 ✔
        //
        //5点 3边 未探索：自己的 4 点 ✔
        //5点 4边 未探索：自己的 6 点 ✔
        //
        //6点 4边 已探索：W  邻居的 2 点 ✔
        //6点 5边 已探索：NW 邻居的 4 点 ✔
        //
        //6点 4边 未探索：自己的 5 点 ✔
        //6点 5边 未探索：自己的 1 点 ✔
        */

        for (int i = 0; i < holeHexsList.Count; i++)
        {
            //Debug.Log($"团块个数：{holeHexsList.Count}");

            //一个团块的边缘点
            List<Vector3> vector3s = new List<Vector3>();

            //剔除非边缘地块
            for (int j = holeHexsList[i].Count - 1; j >= 0; j--)
            {
                HexCellData h = holeHexsList[i][j];

                // 检查六个方向是否都已探索
                bool allNeighborsExplored = true;
                for (int dir = 0; dir < 6; dir++)
                {
                    HexCellData nei = _mapDataService.GetNeighbor(h, (Enums.HexDirection)dir);
                    if (nei == null || !nei.IsExplored)
                    {
                        allNeighborsExplored = false;
                        break;
                    }
                }

                if (allNeighborsExplored)
                {
                    holeHexsList[i].RemoveAt(j);
                }
            }


            //选一个边缘地块，按顺时针选一条边缘边的第一个点
            HexCellData hexCellData = holeHexsList[i][0];
            if (hexCellData == null)
            {
                Debug.Log("你好，你好");
            }

            Enums.HexDirection direction = Enums.HexDirection.None;
            //选边
            for (int j = 0; j < 6; j++)
            {

                //邻居是未探索 || 邻居不存在 的边才是“边缘边”
                if ((_mapDataService.GetNeighbor(hexCellData, (Enums.HexDirection)j) == null) ||
                    (_mapDataService.GetNeighbor(hexCellData, (Enums.HexDirection)j) != null && !_mapDataService.GetNeighbor(hexCellData, (Enums.HexDirection)j).IsExplored))
                {
                    direction = (Enums.HexDirection)j;
                    //Debug.Log("direction：" + direction);
                    break;
                }
            }
            //Debug.Log("测试，测试，测试");
            //Debug.Log("direction：" + direction);
            if (direction == Enums.HexDirection.None) { Debug.Log("BUG"); break; }

            //选起始点
            int index = -1;
            switch (direction)
            {
                case Enums.HexDirection.NE:
                    index = 1;
                    break;
                case Enums.HexDirection.E:
                    index = 2;
                    break;
                case Enums.HexDirection.SE:
                    index = 3;
                    break;
                case Enums.HexDirection.SW:
                    index = 4;
                    break;
                case Enums.HexDirection.W:
                    index = 5;
                    break;
                case Enums.HexDirection.NW:
                    index = 6;
                    break;
            }
            if (index == -1) { Debug.Log("BUG"); }
            Vector3 Point = hexCellData.SolidAreaVertices[index];
            Vector3 StartPoint = Point;
            //起始边
            int edgeIndex = -1;
            switch (index)
            {
                case 1:
                    edgeIndex = 5;
                    break;
                case 2:
                    edgeIndex = 0;
                    break;
                case 3:
                    edgeIndex = 1;
                    break;
                case 4:
                    edgeIndex = 2;
                    break;
                case 5:
                    edgeIndex = 3;
                    break;
                case 6:
                    edgeIndex = 4;
                    break;
            }
            if (edgeIndex == -1) { Debug.Log("BUG"); }
            //起始邻居状态
            HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, (Enums.HexDirection)edgeIndex);
            bool isExplored = neighbor == null ? false : neighbor.IsExplored;
            //添加起始点坐标
            vector3s.Add(Point);

            //开始迭代
            while (true)
            {
                //获取新点、新HexCellData
                Vector3 newPoint = new Vector3();
                int newIndex = -1;
                int newEdgeIndex = -1;
                if (index == 1)
                {
                    if (isExplored)
                    {
                        if (edgeIndex == 5)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NW).SolidAreaVertices[3];
                            newIndex = 3;
                            newEdgeIndex = (int)Enums.HexDirection.E;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NW);
                        }
                        else if (edgeIndex == 0)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[5];
                            newIndex = 5;
                            newEdgeIndex = (int)Enums.HexDirection.W;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE);
                        }
                        else { Debug.Log("BUG"); }
                    }
                    else
                    {
                        if (edgeIndex == 5)
                        {
                            newPoint = hexCellData.SolidAreaVertices[6];
                            newIndex = 6;
                            newEdgeIndex = (int)Enums.HexDirection.W;
                            //hexCellData = hexCellData;
                        }
                        else if (edgeIndex == 0)
                        {
                            newPoint = hexCellData.SolidAreaVertices[2];
                            newIndex = 2;
                            newEdgeIndex = (int)Enums.HexDirection.E;
                            //hexCellData = hexCellData;
                        }
                        else { Debug.Log("BUG"); }
                    }
                }
                else if (index == 2)
                {
                    if (isExplored)
                    {
                        if (edgeIndex == 0)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).SolidAreaVertices[4];
                            newIndex = 4;
                            newEdgeIndex = (int)Enums.HexDirection.SE;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE);
                        }
                        else if (edgeIndex == 1)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[6];
                            newIndex = 6;
                            newEdgeIndex = (int)Enums.HexDirection.NW;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E);
                        }
                        else { Debug.Log("BUG"); }
                    }
                    else
                    {
                        if (edgeIndex == 0)
                        {
                            newPoint = hexCellData.SolidAreaVertices[1];
                            newIndex = 1;
                            newEdgeIndex = (int)Enums.HexDirection.NW;
                            //hexCellData = hexCellData;
                        }
                        else if (edgeIndex == 1)
                        {
                            newPoint = hexCellData.SolidAreaVertices[3];
                            newIndex = 3;
                            newEdgeIndex = (int)Enums.HexDirection.SE;
                            //hexCellData = hexCellData;
                        }
                        else { Debug.Log("BUG"); }
                    }
                }
                else if (index == 3)
                {
                    if (isExplored)
                    {
                        if (edgeIndex == 1)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).SolidAreaVertices[5];
                            newIndex = 5;
                            newEdgeIndex = (int)Enums.HexDirection.SW;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E);
                        }
                        else if (edgeIndex == 2)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[1];
                            newIndex = 1;
                            newEdgeIndex = (int)Enums.HexDirection.NE;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE);
                        }
                        else { Debug.Log("BUG"); }
                    }
                    else
                    {
                        if (edgeIndex == 1)
                        {
                            newPoint = hexCellData.SolidAreaVertices[2];
                            newIndex = 2;
                            newEdgeIndex = (int)Enums.HexDirection.NE;
                            //hexCellData = hexCellData;
                        }
                        else if (edgeIndex == 2)
                        {
                            newPoint = hexCellData.SolidAreaVertices[4];
                            newIndex = 4;
                            newEdgeIndex = (int)Enums.HexDirection.SW;
                            //hexCellData = hexCellData;
                        }
                        else { Debug.Log("BUG"); }
                    }
                }
                else if (index == 4)
                {
                    if (isExplored)
                    {
                        if (edgeIndex == 2)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).SolidAreaVertices[6];
                            newIndex = 6;
                            newEdgeIndex = (int)Enums.HexDirection.W;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE);
                        }
                        else if (edgeIndex == 3)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SW).SolidAreaVertices[2];
                            newIndex = 2;
                            newEdgeIndex = (int)Enums.HexDirection.E;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SW);
                        }
                        else { Debug.Log("BUG"); }
                    }
                    else
                    {
                        if (edgeIndex == 2)
                        {
                            newPoint = hexCellData.SolidAreaVertices[3];
                            newIndex = 3;
                            newEdgeIndex = (int)Enums.HexDirection.E;
                            //hexCellData = hexCellData;
                        }
                        else if (edgeIndex == 3)
                        {
                            newPoint = hexCellData.SolidAreaVertices[5];
                            newIndex = 5;
                            newEdgeIndex = (int)Enums.HexDirection.W;
                            //hexCellData = hexCellData;
                        }
                        else { Debug.Log("BUG"); }
                    }
                }
                else if (index == 5)
                {
                    if (isExplored)
                    {
                        if (edgeIndex == 3)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SW).SolidAreaVertices[1];
                            newIndex = 1;
                            newEdgeIndex = (int)Enums.HexDirection.NW;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SW);
                        }
                        else if (edgeIndex == 4)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.W).SolidAreaVertices[3];
                            newIndex = 3;
                            newEdgeIndex = (int)Enums.HexDirection.SE;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.W);
                        }
                        else { Debug.Log("BUG"); }
                    }
                    else
                    {
                        if (edgeIndex == 3)
                        {
                            newPoint = hexCellData.SolidAreaVertices[4];
                            newIndex = 4;
                            newEdgeIndex = (int)Enums.HexDirection.SE;
                            //hexCellData = hexCellData;
                        }
                        else if (edgeIndex == 4)
                        {
                            newPoint = hexCellData.SolidAreaVertices[6];
                            newIndex = 6;
                            newEdgeIndex = (int)Enums.HexDirection.NW;
                            //hexCellData = hexCellData;
                        }
                        else { Debug.Log("BUG"); }
                    }
                }
                else if (index == 6)
                {
                    if (isExplored)
                    {
                        if (edgeIndex == 4)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.W).SolidAreaVertices[2];
                            newIndex = 2;
                            newEdgeIndex = (int)Enums.HexDirection.NE;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.W);
                        }
                        else if (edgeIndex == 5)
                        {
                            newPoint = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NW).SolidAreaVertices[4];
                            newIndex = 4;
                            newEdgeIndex = (int)Enums.HexDirection.SW;
                            hexCellData = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NW);
                        }
                        else { Debug.Log("BUG"); }
                    }
                    else
                    {
                        if (edgeIndex == 4)
                        {
                            newPoint = hexCellData.SolidAreaVertices[5];
                            newIndex = 5;
                            newEdgeIndex = (int)Enums.HexDirection.SW;
                            //hexCellData = hexCellData;
                        }
                        else if (edgeIndex == 5)
                        {
                            newEdgeIndex = (int)Enums.HexDirection.NE;
                            newPoint = hexCellData.SolidAreaVertices[1];
                            newIndex = 1;
                            //hexCellData = hexCellData;
                        }
                        else { Debug.Log("BUG"); }
                    }
                }
                Point = newPoint;

                //迭代结束条件
                if (Point == StartPoint) { break; }

                //添加新点坐标
                vector3s.Add(Point);

                //点 - 新index
                index = newIndex;

                //边 - 新edgeIndex
                edgeIndex = newEdgeIndex;

                //邻居 - 新isExplored
                neighbor = _mapDataService.GetNeighbor(hexCellData, (Enums.HexDirection)edgeIndex);
                isExplored = neighbor == null ? false : neighbor.IsExplored;
            }

            //一个团块的点
            holesVector3.Add(vector3s);
        }

    }

    /////////////////////////////////////////////////////////////////////- 迷雾的封皮 -/////////////////////////////////////////////////////////////////////
    public List<Vector3> GetFogCoverVertices(List<Vector3> vector3s, float incrementX, float incrementZ, float uniformHeight)
    {
        if (vector3s == null || vector3s.Count < 3)
        {
            Debug.LogError("输入顶点数量不足。");
            return vector3s;
        }

        // 1. 计算中心点（仅用于扩展方向）
        Vector3 center = Vector3.zero;
        foreach (var v in vector3s) center += v;
        center /= vector3s.Count;

        // 2. 扩充顶点，X 和 Z 方向使用不同的 increment，Y 统一使用传入的高度
        List<Vector3> expandedPoints = new List<Vector3>();
        foreach (var v in vector3s)
        {
            Vector3 dir = v - center;
            Vector3 offset = new Vector3(
                Mathf.Sign(dir.x) * incrementX,
                0,
                Mathf.Sign(dir.z) * incrementZ
            );
            Vector3 expanded = v + offset;
            expanded.y = uniformHeight;
            expandedPoints.Add(expanded);
        }

        // 3. 顺时针排序 (基于 XZ 平面的极角)
        return expandedPoints
            .OrderByDescending(v => Mathf.Atan2(v.z - center.z, v.x - center.x))
            .ToList();
    }

}
