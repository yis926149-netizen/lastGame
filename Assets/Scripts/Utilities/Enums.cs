using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：枚举类，项目中所有的枚举
//****************************************

public class Enums
{
    // 地块的6个方向
    public enum HexDirection
    {
        NE, E, SE, SW, W, NW, None
    }

    // 矩形过渡区域的绘制种类
    public enum RectType
    {
        slope, step, none
    }

    // 三角形过渡区域的绘制种类
    public enum TriType
    {
        one, two, three, four, zero
    }

    //地块实心区域类型
    public enum HexType
    {
        NoRiver, RiverSource, RiverMidstream, RiverEnd, LakeOrSea
    }

    // 地貌类型（目前只有三类 - 海底、平地、高地）- 现在只是简单根据地块高低分类
    public enum TerrainType
    {
        SeaFloor, FlatLand, HighLand
    }

    // 【程序化山脉】山脉地块角色（决策 ⑯：山脉 = 一条脊线 + 宽度化坡面格）
    public enum MountainRidgeStatus
    {
        None,
        // 脊线格：山脊轴心，决定山体走向与 RidgesDirectionA/B
        RidgeCell,
        // 宽度化坡面格：脊线两侧低矮坡面，沿垂直脊线方向衰减
        SlopeCell
    }

    //地貌类型（【地图地貌配置化】已移除：改由 MapLandFormSO 配置，见 ScriptableObjects/MapLandForm）

    public enum TransitionEdgeType
    {
        Slope,
        Step
    }

    public enum ShadingStyle
    {
        FlatAll,
        SmoothAll,
        FlatRect_SmoothTri,
        SmoothRect_FlatTri,
        ForceUpNormals,
        ExaggeratedNormals
    }

    public enum FogEdgeStyle
    {
        Original,
        BlurMask9,
        WideSmooth,
        DitheredEdge,
        SoftPlusFogBand
    }

    // 高度生成模式
    public enum HeightGenerationMode
    {
        PerlinNoise,
        PaletteMap
    }

    // 地块实心顶面轮廓（网格渲染细分）：
    // EighteenGon = 每条边 2 个等分点落在内切圆上（圆滑十八边形，默认/现状）；
    // Hexagon     = 等分点落在角点连线上（直边六边形）。
    // 两种模式顶点数/索引顺序/三角扇拓扑一致，仅轮廓不同，下游契约不受影响。
    public enum SolidAreaTopology
    {
        EighteenGon,
        Hexagon
    }

    //资源类型（【地图资源配置化】已移除：改由 MapResourceSO 配置，见 ScriptableObjects/MapResource）

    //移动目的
    public enum MovementPurpose
    {
        MoveToDestination, 
        MoveToAttack,
        None
    }

    //队列类型
    public enum CommandQueueType
    {
        Unit, Settlement
    }

    //建筑类型
    public enum BulidingType
    {
        City = 0,
        AttackStatue = 1,
        DefenseStatue = 2,
        Altar = 3,
        TechnologyAndCultural = 4,
        Barracks = 5,
        ArrowTower = 6,
        // 【公共建筑系统】公共建筑（多格、可争夺、伪AI阵营）
        PublicBuilding = 7,
        NoBuilding = 8,
        GoldMine = 9
    }
}
