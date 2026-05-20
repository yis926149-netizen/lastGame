using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：地形生成规则
//****************************************

public class TerrainGenerator : MonoBehaviour
{
    //生成地图地块高度的柏林噪声数据
    public struct TerrainHeights
    {
        //频率：控制地形区块大小（值越小，区块越大越连贯）-0.03~0.08（网格越大，值越小）
        public float frequency;
        //八度：分层叠加细节（值越多，细节越丰富但不碎片化）-  2~4（3 个高度无需过多细节）
        public int octaves;
        //持续性：控制高频细节的贡献度（值越小，地形越平滑）- 0.4~0.6
        public float persistence;
        //阈值1 - （0，T1）
        public float T1;
        //阈值2 - （T1,T2）
        public float T2;

        public TerrainHeights(float frequency, int octaves, float persistence, float T1, float T2)
        {
            this.frequency = frequency;
            this.octaves = octaves;
            this.persistence = persistence;
            this.T1 = T1;
            this.T2 = T2;
        }
    }

    /// <summary>
    /// 地形生成规则
    ///1、当相邻地格高度相同时，设置为平地
    ///2、当相邻地格高度差为1时，设置为斜坡
    ///3、当相邻地格高度差超过1时，设置为悬崖
    ///矩形面片高度判断 - 对比(height) (相对高度) 就行（绝对高度包含了扰动）
    ///则三角面片的类型包括：
    ///一、三者同高(三边同高)
    ///没有高度差
    ///1、平面（三坡）(000 - nnn)
    ///二、两者同高(两边同高)
    ///有两个相同的高度差
    ///1、高度差==1（一平两梯）(110- 11n、101 - 1n1、011 - n11)
    ///2、高度差>1 （三坡）（nn0、n0n、0nn） - nnn
    ///三、没有同高(无边同高)
    ///有三个高度差 - （1代表高度差为一、n代表高度差大于一）
    ///nnn（三坡 - 无数种 nnn）
    ///nn1（一梯两坡 - 无数种 nn1、n1n、1nn）
    ///n11（两梯一坡 - 仅有一种情况211 - n11、121 - 1n1、211 - n11）
    ///总结（没有0）(数字含义：边0长度、边1长度、边2长度)
    ///nnn
    ///11n、1n1、n11
    ///nn1、n1n、1nn
    ///规律：
    ///一长边、两短边；
    ///两短边相加等于长边；
    ///短边可以与长边等长；
    ///俩短边之间不可相等；
    ///nn - nnn、n1n - nnn|n1n - 三坡|长坡梯短坡
    ///1n、n1 - 11n、n11、nn1、1nn - nn1、1nn|11n、n11 - 长坡梯短坡|110、112、011、211
    ///11 - 1n1 - 101
    ///所以绘制方式只分四种
    ///1、三坡
    ///2、长坡 - 梯 - 短坡（要分清哪边是长坡、梯、短坡）【暂时还出现不了这种情况】
    ///3、梯 - 长坡 - 梯 (要分清哪边是长坡）
    ///4、两梯一平坡（要分清哪边是平坡）
    ///先比较己方情况，再比较邻居情况，决定使用哪种绘制方式，最后确定绘制方式的具体参数
    /// </summary>
    /// <param name="hexCell"></param>
    /// <returns></returns>
    public static void IsType(HexCellData hexCellData, out Enums.RectType[] rectTypes, out Enums.TriType[] triTypes, IMapDataService _mapDataService)
    {
        //矩形的绘制方式
        Enums.RectType NEType, EType, SEType;
        if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) == null)
        {
            NEType = Enums.RectType.none;
        }
        else if (Mathf.Abs(hexCellData.Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).Height) == 1)
        {
            NEType = Enums.RectType.step;
        }
        else
        {
            NEType = Enums.RectType.slope;
        }

        if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) == null)
        {
            EType = Enums.RectType.none;
        }
        else if (Mathf.Abs(hexCellData.Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).Height) == 1)
        {
            EType = Enums.RectType.step;
        }
        else
        {
            EType = Enums.RectType.slope;
        }

        if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) == null)
        {
            SEType = Enums.RectType.none;
        }
        else if (Mathf.Abs(hexCellData.Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).Height) == 1)
        {
            SEType = Enums.RectType.step;
        }
        else
        {
            SEType = Enums.RectType.slope;
        }

        //三角形的绘制方式
        Enums.TriType NE_EType = Enums.TriType.zero, E_SEType = Enums.TriType.zero;
        float NE_E_distance, E_SE_distance;
        //NE_E三角
        if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null)
        {
            NE_E_distance = Mathf.Abs(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).Height);
            //nn(先看自家)
            if (NEType == EType && NEType == Enums.RectType.slope)
            {

                //n1n - 长坡.梯.短坡
                if (NE_E_distance == 1)
                {
                    NE_EType = Enums.TriType.two;
                }
                //nnn - 三坡(再看邻居)
                else
                {
                    NE_EType = Enums.TriType.one;
                }

            }
            //1n、n1
            else if (NEType != EType)
            {
                //nn1、1nn - 长坡.梯.短坡
                if (NE_E_distance > 1)
                {
                    NE_EType = Enums.TriType.two;
                }
                //11n、n11 - 110、011 或 112、211
                else
                {
                    //110、011
                    if (hexCellData.Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).Height == 0 || hexCellData.Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).Height == 0)
                    {
                        NE_EType = Enums.TriType.four;
                    }
                    //112、211
                    else
                    {
                        NE_EType = Enums.TriType.three;
                    }
                }

            }
            //11
            else if (NEType == EType && NEType == Enums.RectType.step)
            {
                //101
                if (NE_E_distance == 0)
                {
                    NE_EType = Enums.TriType.four;
                }
                //121
                else
                {
                    NE_EType = Enums.TriType.three;
                }

            }
        }
        //E_SE三角
        if (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null && _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null)
        {
            E_SE_distance = Mathf.Abs(_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).Height);
            //nn(先看自家)
            if (EType == SEType && EType == Enums.RectType.slope)
            {
                //n1n - 长坡.梯.短坡(再看邻居)
                if (E_SE_distance == 1)
                {
                    E_SEType = Enums.TriType.two;
                }
                //nnn - 三坡
                else
                {
                    E_SEType = Enums.TriType.one;
                }
            }
            //1n、n1
            else if (EType != SEType)
            {
                //nn1、1nn - 长坡.梯.短坡
                if (E_SE_distance > 1)
                {
                    E_SEType = Enums.TriType.two;
                }
                //11n、n11 - 110、011 或 112、211
                else
                {
                    //110、011
                    if (hexCellData.Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).Height == 0 || hexCellData.Height - _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).Height == 0)
                    {
                        E_SEType = Enums.TriType.four;
                    }
                    //112、211
                    else
                    {
                        E_SEType = Enums.TriType.three;
                    }
                }

            }
            //11
            else if (EType == SEType && EType == Enums.RectType.step)
            {
                //101
                if (E_SE_distance == 0)
                {
                    E_SEType = Enums.TriType.four;
                }
                //121
                else
                {
                    E_SEType = Enums.TriType.three;
                }
            }
        }


        rectTypes = new Enums.RectType[] { NEType, EType, SEType };
        triTypes = new Enums.TriType[] { NE_EType, E_SEType };
    }

    /// <summary>
    /// 地块高度生成规则
    /// </summary>
    /// <param name="frequency">频率：控制地形区块大小（值越小，区块越大越连贯）- 0.03~0.08（网格越大，值越小） </param>
    /// <param name="octaves">八度：分层叠加细节（值越多，细节越丰富但不碎片化）-  2~4（3 个高度无需过多细节）</param>
    /// <param name="persistence">持续性：控制高频细节的贡献度（值越小，地形越平滑）- 0.4~0.6 </param>
    /// <param name="T1">阈值1 - （0，T1）</param>
    /// <param name="T2">阈值2 - （T1,T2）</param>
    /// <returns></returns>
    public static int[,] GenerateTerrainHeight(int xNumber, int zNumber, float frequency = 0.05f, int octaves = 3, float persistence = 0.5f, float T1 = 0.25f, float T2 = 0.75f)
    {
        int[,] terrainMap = new int[xNumber, zNumber];

        // 1. 生成柏林噪声图
        float[,] noiseMap = new float[xNumber, zNumber];
        for (int x = 0; x < xNumber; x++)
        {
            for (int z = 0; z < zNumber; z++)
            {
                float noiseValue = 0;
                float amp = 1;
                float freq = frequency;
                float totalAmp = 0;

                // 多八度叠加
                for (int o = 0; o < octaves; o++)
                {
                    noiseValue += Mathf.PerlinNoise(x * freq, z * freq) * amp;
                    totalAmp += amp;
                    amp *= persistence;
                    freq *= 2; // 频率翻倍（标准FBM）
                }
                // 归一化到[0,1]
                noiseMap[x, z] = noiseValue / totalAmp;
            }
        }

        // 2. 阈值映射
        for (int x = 0; x < xNumber; x++)
        {
            for (int z = 0; z < zNumber; z++)
            {
                float val = noiseMap[x, z];
                if (val < T1) terrainMap[x, z] = 0;
                else if (val < T2) terrainMap[x, z] = 1;
                else terrainMap[x, z] = 2;
            }
        }

        // 3. 连贯性优化
        return OptimizeTerrain(xNumber, zNumber, terrainMap);
    }

    /// <summary>
    /// 连贯性优化函数
    /// </summary>
    /// <param name="terrainMap">高度数组</param>
    /// <returns></returns>
    private static int[,] OptimizeTerrain(int xNumber, int zNumber, int[,] terrainMap)
    {
        int[,] tempMap = (int[,])terrainMap.Clone();
        for (int x = 1; x < xNumber - 1; x++)
        {
            for (int z = 1; z < zNumber - 1; z++)
            {
                // 统计周围8个单元格
                int count0 = 0, count1 = 0, count2 = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int val = tempMap[x + dx, z + dz];
                        if (val == 0) count0++;
                        else if (val == 1) count1++;
                        else count2++;
                    }
                }
                // 修正孤立点
                int maxCount = Mathf.Max(count0, count1, count2);
                if (maxCount == count0) terrainMap[x, z] = 0;
                else if (maxCount == count1) terrainMap[x, z] = 1;
                else terrainMap[x, z] = 2;
            }
        }
        return terrainMap;
    }

}
