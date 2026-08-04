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

    //地貌类型（【地图地貌配置化】已移除：改由 MapLandFormSO 配置，见 ScriptableObjects/MapLandForm）

    public enum TransitionEdgeType
    {
        Slope,
        Step
    }

    public enum TransitionGenerationMode
    {
        Legacy,
        GenericFan
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

    // 【动态地图-阶段三】渲染后端模式：WholeMap = 整图合并 mesh（阶段二现状），Chunked = 8×8 分块（阶段三）
    public enum MapRenderMode
    {
        WholeMap,
        Chunked
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
        City, AttackStatue, DefenseStatue, Altar, TechnologyAndCultural, Barracks, ArrowTower,
        // 【公共建筑系统】公共建筑（多格、可争夺、伪AI阵营）
        PublicBuilding,
        NoBuilding
    }
}
