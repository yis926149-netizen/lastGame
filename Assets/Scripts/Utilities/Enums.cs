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

    //地貌类型
    public enum LandFormType
    {
        //森林 - 移动力 - 1
        Forest,
        //石头 - 地形高度 + 1
        Stone,
        //大骨阵 - 防御力增加 
        BigBones,
        //农田 - 每回合自动回血
        FromLand,
        
        None
    }

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

    //资源类型
    public enum ResourceType
    {
        //动物 - 提升下一次攻击力
        Animals,
        //植物 - 立即回血
        Plants,
        //矿物 - 提升下一次防御力
        Minerals,
        //科技文化值宝箱 - 提升科文
        Chest,
        //击杀敌人掉落的回血包
        HealthPack,

        None
    }

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
