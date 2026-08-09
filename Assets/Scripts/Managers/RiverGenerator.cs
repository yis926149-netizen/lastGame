using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

//****************************************
//创建人：易生
//功能说明：河流生成规则
//****************************************

public class RiverGenerator : MonoBehaviour
{
    /*伪代码
    生成源头 - 根据源头生成完整河流

    生成源头：
    int[] 生成源头地块(){
        ∵最开始就可以知道生成(x*z)个地块
        ∴生成⌊(x*z)*generateProbability⌋个源头
        ∴在（0，x*z）中生成不重复的随机数，对应生成序号的地块即源头
    }

    int[] 获取河流长度(){
        //根据规则为每个源头生成一个河流长度
        规则：在[最短长度，最长长度]间随机
    }

    根据源头生成完整河流：
    for(i<源头数){
        迭代{
            切换主体();
            剔除不可流往的方向();
            if(剩余方向数量为0)停止迭代;
            随机下一个流往方向(河流长度++)
            if(河流长度达标)停止迭代
        } 
    }

    切换主体(){
        现有长度为0？主体切换为下一个源头：主体切换为下游地块()
    }

    剔除不可流往的方向(){
        for(6个方向){
            是否符合条件A；
            是否符合条件B；
            ...
            若有一项不符合便标记该方向
        }
        返回没有被标记的方向
    }

    随机下一个流往方向(河流长度++){

    }

    主体切换为下游地块(){
        地块有河流流入()
    }

    地块有河流流入(){
        设置流入方向；
        有河流流入；
    }

    地块有河流流出(){
        设置流出方向；
        有河流流出；
    }
    */

    public static int[] RiverGeneration(int x,int z, int minLongestLength,int maxLongestLength, float riverSourceGenerationProbability, IMapDataService _mapDataService, System.Random random)
    {
        //一条河流的迭代流程就是：切换主体 |—— 剔除不可流往的方向 | (若剩余方向数量不为零)—— 随机下一个流往方向 | (若河流长度未达标)—— 进行下一次迭代
        //                                                          | (若剩余方向数量为零)—— 停止迭代             | (若河流长度达标)—— 停止迭代

        //生成源头地块
        int[] riverSources = GenerateRiverSource(x, z, riverSourceGenerationProbability, random);
        if (riverSources == null || riverSources.Length == 0)
        {
            //Debug.Log("河流源头数量为0");
            return null;
        }
        //获取河流长度
        int[] riverLength = GenerateRiverLength(minLongestLength, maxLongestLength, riverSources.Length, random);

        //根据源头生成完整河流
        for(int i = 0; i < riverSources.Length; i++)
        {
            
            Enums.HexDirection OutgoingDirection = Enums.HexDirection.None;
            //该源头河流的现有长度
            int currentLength = 0;
            //主体
            if (_mapDataService.GetCell(riverSources[i]) == null)
            {
                Debug.LogError($"找不到地块序号: {riverSources[i]}");
                continue;
            }
            HexCellData hexCellData = _mapDataService.GetCell(riverSources[i]);
            // 【程序化山脉】决策 ③：山与河不共格——源头不得为山格
            if (hexCellData.hasRiver || WaterLevelConfig.IsWater(hexCellData) || MountainCellRule.IsMountainCell(hexCellData))
            {
                continue;
            }

            //一条河流迭代
            while (true)
            {
                //切换主体
                SwitchHexCellData(ref hexCellData, currentLength, riverSources[i], OutgoingDirection, _mapDataService);               
                //剔除不可流往的方向
                List<Enums.HexDirection> validRiverFlowDirections = FilterOutInvalidRiverFlowDirections(hexCellData, _mapDataService);

                //if(剩余方向数量为0)停止迭代;
                if(validRiverFlowDirections.Count == 0)
                {
                    if (currentLength == 0)
                    {
                        //源头外就没有可延伸的方向
                        hexCellData.HexType = Enums.HexType.NoRiver;
                        break;
                    }
                    else
                    {
                        //没有可延伸的方向
                        hexCellData.HexType = Enums.HexType.RiverEnd;
                        break;
                    }
                }

                //(若剩余方向数量不为零)—— 随机下一个流往方向
                currentLength++;

                //(若河流长度达标)—— 停止迭代
                if (currentLength == riverLength[i])
                {
                    if (currentLength == 0)
                    {
                        //河流长度为1，源头即终点 - 此时不生成河流
                        hexCellData.HexType = Enums.HexType.NoRiver;
                        break;
                    }
                    else
                    {
                        //河流长度达标;
                        hexCellData.HexType = Enums.HexType.RiverEnd;
                        break;
                    }
                }

                //(若河流长度未达标)—— 进行下一次迭代
                //随机流向
                OutgoingDirection = validRiverFlowDirections[random.Next(0, validRiverFlowDirections.Count)];
                //设置本地块类型
                //Debug.Log("HexCell.GenerateOrder：" + hexCellData.GenerateOrder);
                hexCellData.HexType = currentLength == 1 ? Enums.HexType.RiverSource : Enums.HexType.RiverMidstream;

                hasRiverOutging(ref hexCellData, OutgoingDirection);
            }
        }

        return riverSources;
    }


    /// <summary>
    /// 生成源头
    /// </summary>
    /// <param name="x,z">地图的行、列</param>
    /// <param name="riverSourceGenerationProbability">河流生成概率</param>
    /// <returns>返回源头地块的生成序号</returns>
    private static int[] GenerateRiverSource(int x,int z,float riverSourceGenerationProbability, System.Random random)
    {
        /*
        ∵最开始就可以知道生成(x*z)个地块
        ∴生成⌊(x*z)*generateProbability⌋个源头
        ∴在（0，x*z）中生成不重复的随机数，对应生成序号的地块即源头
        */
        //地块数量
        int HexNumber = x * z;
        //源头数量
        int riverSourceNumber = (int)(HexNumber * riverSourceGenerationProbability);
        if (riverSourceNumber == 0) 
        { 
            //Debug.Log("源头生成概率为0");
            return null; 
        }
        //边界检查：确保源头数量不超过地块总数
        if (riverSourceNumber > HexNumber) { throw new ArgumentException("源头数量(riverSourceNumber)不能大于地块总数(HexNumber)，否则无法生成不重复的源头序号"); }
        //所有可能的地块序号（0 ~ HexNumber-1）
        List<int> allHexIndices = Enumerable.Range(0, HexNumber).ToList();
        // Fisher-Yates算法：打乱序号顺序
        for (int i = allHexIndices.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1); // 生成[0, i]的随机索引
            // 交换元素，实现打乱
            int temp = allHexIndices[i];
            allHexIndices[i] = allHexIndices[j];
            allHexIndices[j] = temp;
        }
        // 取前riverSourceNumber个元素，作为不重复的源头序号
        int[] HexRiverSourceIndex = allHexIndices.Take(riverSourceNumber).ToArray();
        return HexRiverSourceIndex;
    }

    /// <summary>
    /// 生成河流长度
    /// </summary>
    /// <param name="minLongestLength">最小长度</param>
    /// <param name="maxLongestLength">最大长度</param>
    /// <returns>根据规则为每个源头生成一个河流长度</returns>
    private static int[] GenerateRiverLength(int minLongestLength, int maxLongestLength, int riverSourceCount, System.Random random)
    {
        //河流最小长度为2
        minLongestLength = minLongestLength < 2 ? 2 : minLongestLength;
        maxLongestLength = maxLongestLength < minLongestLength ? minLongestLength + 1 : maxLongestLength;
        // 生成最终长度 length
        int[] length = new int[riverSourceCount];
        for(int i = 0; i < riverSourceCount; i++)
        {
            length[i] = random.Next(minLongestLength, maxLongestLength + 1);
        }
        return length;
    }

    /// <summary>
    /// 切换主体
    /// </summary>
    /// <param name="hexCellData">当前的主体</param>
    /// <param name="currentLength">当前河流长度</param>
    /// <param name="riverSourceIndex">当前河流源头地块的序号</param>
    /// <param name="outgoingDirection">上一个河流流出方向</param>
    /// <returns></returns>
    private static void SwitchHexCellData(ref HexCellData hexCellData, int currentLength,int riverSourceIndex, Enums.HexDirection outgoingDirection, IMapDataService _mapDataService)
    {
        //切换主体
        if (currentLength == 0)
        {
            hexCellData = _mapDataService.GetCell(riverSourceIndex);
        }
        else
        {
            hexCellData = _mapDataService.GetNeighbor(hexCellData, outgoingDirection);
            hasRiverIncoming(ref hexCellData, (Enums.HexDirection)((int)(outgoingDirection + 3) % 6));
        }
    }

    private static void hasRiverOutging(ref HexCellData hexCellData, Enums.HexDirection outgoingDirection)
    {
        hexCellData.RiverOutgoingDirection = outgoingDirection;
        hexCellData.hasRiverOutgoing = true;
        hexCellData.hasRiver = true;
    }

    private static void hasRiverIncoming(ref HexCellData hexCellData, Enums.HexDirection incomingDirection)
    {
        hexCellData.RiverIncomingDirection = incomingDirection;
        hexCellData.hasRiverIncoming = true;
        hexCellData.hasRiver = true;
    }

    /// <summary>
    /// 剔除河流不可流经的方向，返回可流经的方向
    /// </summary>
    /// <param name="hexCellData"></param>
    private static List<Enums.HexDirection> FilterOutInvalidRiverFlowDirections(HexCellData hexCellData, IMapDataService _mapDataService)
    {
        //邻居列表
        List<Enums.HexDirection> Neighbor = new List<Enums.HexDirection>()
        {
            Enums.HexDirection.NE,
            Enums.HexDirection.E,
            Enums.HexDirection.SE,
            Enums.HexDirection.SW,
            Enums.HexDirection.W,
            Enums.HexDirection.NW,
        };
        //需要剔除的方向           
        List<int> InvalidDirectionIndex = new List<int>();
        
        //逐个方向检查是否符合条件
        for (int j = 0; j < Neighbor.Count; j++)
        {
            //高度条件
            if (!CheckHeight(hexCellData, Neighbor[j], _mapDataService)) { InvalidDirectionIndex.Add(j); }
            //不交叉条件
            else if (!CheckCross(hexCellData, Neighbor[j], _mapDataService)) { InvalidDirectionIndex.Add(j); }
            //山体条件（决策 ③：山与河不共格，流向与交叉均排除山格）
            else if (!CheckMountain(hexCellData, Neighbor[j], _mapDataService)) { InvalidDirectionIndex.Add(j); }
        }
        //开始剔除
        for (int j = InvalidDirectionIndex.Count - 1; j >= 0; j--)
        {
            Neighbor.RemoveAt(InvalidDirectionIndex[j]);
        }

        //返回剩下的、可流经的方向
        return Neighbor;
    }

    //剔除不可流往的方向 - 高度条件
    private static bool CheckHeight(HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        //获取邻居高度
        float NeighborHeight = _mapDataService.GetNeighbor(hexCellData, direction)?.Height ?? 10000000;
        //邻居不高于自己时，才能通过
        return (NeighborHeight <= hexCellData.Height);
    }

    //剔除不可流往的方向 - 交叉条件
    private static bool CheckCross(HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        //邻居是否有河流
        bool NeighborHasRiver = _mapDataService.GetNeighbor(hexCellData, direction) != null ? _mapDataService.GetNeighbor(hexCellData, direction).hasRiver : true;
        //邻居没河流时，才能流经
        return !NeighborHasRiver;
    }

    //剔除不可流往的方向 - 山体条件（决策 ③：山与河不共格；山格不可流经）
    private static bool CheckMountain(HexCellData hexCellData, Enums.HexDirection direction, IMapDataService _mapDataService)
    {
        HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, direction);
        return neighbor != null && !MountainCellRule.IsMountainCell(neighbor);
    }
}
