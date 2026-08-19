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
        public float frequency;
        public int octaves;
        public float persistence;
        public int minHeight;
        public int maxHeight;

        public TerrainHeights(float frequency, int octaves, float persistence, int minHeight, int maxHeight)
        {
            this.frequency = frequency;
            this.octaves = octaves;
            this.persistence = persistence;
            this.minHeight = minHeight;
            this.maxHeight = maxHeight;
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
    /// <param name="minHeight">最低高度</param>
    /// <param name="maxHeight">最高高度</param>
    public static int[,] GenerateTerrainHeight(int xNumber, int zNumber, System.Random random, float frequency = 0.05f, int octaves = 3, float persistence = 0.6f, int minHeight = 0, int maxHeight = 2)
    {
        int[,] terrainMap = new int[xNumber, zNumber];
        float offsetX = random.Next(-100000, 100001);
        float offsetZ = random.Next(-100000, 100001);

        float[,] noiseMap = new float[xNumber, zNumber];
        int totalCells = xNumber * zNumber;
        float[] flat = new float[totalCells];
        int idx = 0;
        for (int x = 0; x < xNumber; x++)
        {
            for (int z = 0; z < zNumber; z++)
            {
                float noiseValue = 0;
                float amp = 1;
                float freq = frequency;
                float totalAmp = 0;

                for (int o = 0; o < octaves; o++)
                {
                    noiseValue += Mathf.PerlinNoise((x + offsetX) * freq, (z + offsetZ) * freq) * amp;
                    totalAmp += amp;
                    amp *= persistence;
                    freq *= 2;
                }
                float val = noiseValue / totalAmp;
                noiseMap[x, z] = val;
                flat[idx++] = val;
            }
        }

        // 直方图均衡：排序后等分，保证每种高度各占 1/layerCount
        System.Array.Sort(flat);
        int layerCount = maxHeight - minHeight + 1;
        float[] thresholds = new float[layerCount];
        for (int h = 1; h < layerCount; h++)
        {
            int cutoffIdx = h * totalCells / layerCount;
            thresholds[h] = cutoffIdx < totalCells ? flat[cutoffIdx] : 1f;
        }

        for (int x = 0; x < xNumber; x++)
        {
            for (int z = 0; z < zNumber; z++)
            {
                float val = noiseMap[x, z];
                int height = maxHeight;
                for (int h = 1; h < layerCount; h++)
                {
                    if (val < thresholds[h])
                    {
                        height = minHeight + h - 1;
                        break;
                    }
                }
                terrainMap[x, z] = height;
            }
        }

        return OptimizeTerrain(xNumber, zNumber, terrainMap);
    }

    /// <summary>
    /// 连贯性优化函数（支持 N 层高度）
    /// </summary>
    public static int[,] OptimizeTerrain(int xNumber, int zNumber, int[,] terrainMap)
    {
        var counts = new Dictionary<int, int>();
        int[,] tempMap = (int[,])terrainMap.Clone();
        for (int x = 0; x < xNumber; x++)
        {
            for (int z = 0; z < zNumber; z++)
            {
                counts.Clear();
                int offset = z % 2 == 0 ? -1 : 1;
                int[,] neighbors =
                {
                    { x + 1, z }, { x - 1, z },
                    { x, z + 1 }, { x, z - 1 },
                    { x + offset, z + 1 }, { x + offset, z - 1 }
                };
                for (int i = 0; i < neighbors.GetLength(0); i++)
                {
                    int neighborX = neighbors[i, 0];
                    int neighborZ = neighbors[i, 1];
                    if (neighborX < 0 || neighborX >= xNumber || neighborZ < 0 || neighborZ >= zNumber) continue;

                    int val = tempMap[neighborX, neighborZ];
                    counts.TryGetValue(val, out int c);
                    counts[val] = c + 1;
                }

                int bestHeight = terrainMap[x, z];
                int bestCount = 0;
                int winnerCount = 0;
                foreach (var kv in counts)
                {
                    if (kv.Value > bestCount)
                    {
                        bestCount = kv.Value;
                        bestHeight = kv.Key;
                        winnerCount = 1;
                    }
                    else if (kv.Value == bestCount)
                    {
                        winnerCount++;
                    }
                }
        if (winnerCount == 1)
                    terrainMap[x, z] = bestHeight;
            }
        }
        return terrainMap;
    }

    /// <summary>
    /// 颜色图高度生成：用策划绘制的颜色图控制宏观地形分布
    /// </summary>
    /// <param name="paletteMap">颜色图纹理（蓝=水域、绿=平地、橙=高地）</param>
    /// <param name="worldCenters">按 generateOrder 排列的世界坐标列表</param>
    public static int[,] GenerateTerrainHeightFromPalette(
        int xNumber, int zNumber,
        Texture2D paletteMap,
        float minHeight, float maxHeight, float seaLevel,
        float noiseAmplitude, float noiseFrequency,
        List<Vector3> worldCenters,
        System.Random random)
    {
        if (paletteMap == null || !paletteMap.isReadable || worldCenters == null || worldCenters.Count != xNumber * zNumber)
        {
            Debug.LogWarning("颜色图为空/不可读或世界坐标数量不匹配，回退到 Perlin 噪声生成");
            return GenerateTerrainHeight(xNumber, zNumber, random, 0.05f, 3, 0.6f, (int)minHeight, (int)maxHeight);
        }

        int[,] terrainMap = new int[xNumber, zNumber];

        float midPoint = seaLevel + (maxHeight - seaLevel + 1f) * 0.5f;
        float waterMid = (minHeight + seaLevel) * 0.5f;
        float flatMid = (seaLevel + midPoint) * 0.5f;
        float highMid = (midPoint + maxHeight) * 0.5f;

        Color refBlue = new Color(0f, 0f, 1f);
        Color refGreen = new Color(0f, 1f, 0f);
        Color refOrange = new Color(1f, 140f / 255f, 0f);

        float minWorldX = worldCenters[0].x;
        float maxWorldX = worldCenters[0].x;
        float minWorldZ = worldCenters[0].z;
        float maxWorldZ = worldCenters[0].z;
        for (int i = 1; i < worldCenters.Count; i++)
        {
            Vector3 wc = worldCenters[i];
            if (wc.x < minWorldX) minWorldX = wc.x;
            if (wc.x > maxWorldX) maxWorldX = wc.x;
            if (wc.z < minWorldZ) minWorldZ = wc.z;
            if (wc.z > maxWorldZ) maxWorldZ = wc.z;
        }

        float worldWidth = maxWorldX - minWorldX;
        float worldDepth = maxWorldZ - minWorldZ;
        if (worldWidth <= 0.0001f) worldWidth = 1f;
        if (worldDepth <= 0.0001f) worldDepth = 1f;

        float noiseOffsetX = random.Next(-100000, 100001);
        float noiseOffsetZ = random.Next(-100000, 100001);

        for (int z = 0; z < zNumber; z++)
        {
            for (int x = 0; x < xNumber; x++)
            {
                Vector3 wc = worldCenters[z * xNumber + x];
                float u = (wc.x - minWorldX) / worldWidth;
                float v = (wc.z - minWorldZ) / worldDepth;

                Color sampled = paletteMap.GetPixelBilinear(u, v);

                float dr = sampled.r - refBlue.r;
                float dg = sampled.g - refBlue.g;
                float db = sampled.b - refBlue.b;
                float distBlue = Mathf.Sqrt(dr * dr + dg * dg + db * db);

                dr = sampled.r - refGreen.r;
                dg = sampled.g - refGreen.g;
                db = sampled.b - refGreen.b;
                float distGreen = Mathf.Sqrt(dr * dr + dg * dg + db * db);

                dr = sampled.r - refOrange.r;
                dg = sampled.g - refOrange.g;
                db = sampled.b - refOrange.b;
                float distOrange = Mathf.Sqrt(dr * dr + dg * dg + db * db);

                float epsilon = 0.001f;
                float wBlue = 1f / (distBlue + epsilon);
                float wGreen = 1f / (distGreen + epsilon);
                float wOrange = 1f / (distOrange + epsilon);
                float totalW = wBlue + wGreen + wOrange;
                wBlue /= totalW;
                wGreen /= totalW;
                wOrange /= totalW;

                float baseHeight = wBlue * waterMid + wGreen * flatMid + wOrange * highMid;

                int bucket;
                if (wBlue >= wGreen && wBlue >= wOrange)
                    bucket = 0;
                else if (wGreen >= wBlue && wGreen >= wOrange)
                    bucket = 1;
                else
                    bucket = 2;

                float noiseX = (x + noiseOffsetX) * noiseFrequency;
                float noiseZ = (z + noiseOffsetZ) * noiseFrequency;
                float noise = (Mathf.PerlinNoise(noiseX, noiseZ) * 2f - 1f) * noiseAmplitude;
                float finalHeight = baseHeight + noise;

                switch (bucket)
                {
                    case 0:
                        finalHeight = Mathf.Clamp(finalHeight, minHeight, seaLevel);
                        break;
                    case 1:
                        finalHeight = Mathf.Clamp(finalHeight, seaLevel + 0.01f, midPoint - 0.01f);
                        break;
                    case 2:
                        finalHeight = Mathf.Clamp(finalHeight, midPoint, maxHeight);
                        break;
                }

                terrainMap[x, z] = Mathf.RoundToInt(finalHeight);
            }
        }

        return terrainMap;
    }

}
