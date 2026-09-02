using System;
using UnityEngine;

namespace UI.PlacementMask
{
    //****************************************
    // 一层「立体感阴影线」的参数（方案 A：把描边路径整体平移一段，用深色再画一遍压在主线下）。
    //
    // 独立成类是为了让红（不可放置）与绿（可放置）各持一份：两者的区域形态差别很大——
    // 红是几乎铺满全屏的连片区域，厚重的墙感是想要的；绿多是零散小岛，同样的偏移量
    // 在小块上会把整块压暗、读作「脏」。故偏移/方向/颜色都必须能分开调。
    //
    // 其余描边参数（半宽、芯比例、简化容差、圆角……）仍是红绿共用的单份：它们作用在
    // PrepareLoops 这条共享的拟合流水线上，拆开会让两层几何不同源。
    //****************************************
    [Serializable]
    public sealed class PlacementMaskShadowSettings
    {
        [Tooltip("阴影线偏移距离（Canvas 单位）。0 = 关闭，本层不出任何额外几何。\n"
                 + "做法：把同一条描边路径整体平移一段距离，用深色再画一遍、压在主线下面。\n\n"
                 + "⚠️ 已知代价（这是「平移复制」这一做法的固有属性，不是 bug）：\n"
                 + "偏移是**整体**的，故朝下的边界外侧探出深带（想要的「厚度」），\n"
                 + "而朝上的边界会在**区域内侧**同样探出一条深带（物理上说不通的「重影」）。\n"
                 + "另外平移方向与线平行的那些段（DirDeg=270 时即竖直边界）阴影几乎被主线完全盖住，\n"
                 + "故立体感只在近水平的边界段出现、两侧渐隐。\n"
                 + "偏移小（2~3）时重影很轻，读作厚度；给大值必然穿帮，那时应改用「按朝向调制的墙带」方案。")]
        [Range(0f, 12f)] public float Offset;

        [Tooltip("阴影线的平移方向（度）。屏幕空间角度：0 = 右，90 = 上，270 = 下。\n"
                 + "俯视斜角下「朝向玩家的一侧」恒为屏幕下方，故默认 270；\n"
                 + "屏幕空间取向的好处是相机转 yaw 时光照方向不会跟着乱转。")]
        [Range(0f, 360f)] public float DirDeg;

        [Tooltip("阴影线颜色。建议取**本层描边同色系**的暗版、alpha 略低，\n"
                 + "读作「线自己的背光面」而非一条独立的线（红绿各自取自己的色系，别共用一个深色）。")]
        public Color Tint;
    }

    //****************************************
    // 提起态放置范围遮罩（红＝不可放置 / 绿＝可放置）的全部对外可调参数。
    //
    // 载体是 GameInstaller 上的序列化字段：PlacementRangeMaskUI 本身由 Zenject
    // FromNewComponentOnNewGameObject 运行时新建（GameInstaller.cs），其 Inspector 勾选
    // 不随场景保存、也无法在 Play 之前调；把参数收进本类挂到 Installer 上，即可进 Play 前标定并存盘。
    //
    // 默认值 = 收进本类之前散落在 PlacementRangeMaskUI 里的那批 const/static readonly 原值，
    // 全部保持不动（红值已在真机标定过）。
    //****************************************
    [Serializable]
    public sealed class PlacementRangeMaskSettings
    {
        [Header("显示开关")]
        [Tooltip("是否显示【红色】不可放置区域遮罩（填充 + 描边）。关闭后该层不出 mesh，拓扑与投屏也整段跳过。")]
        public bool ShowUnplaceableMask = true;

        [Tooltip("是否显示【绿色】可放置区域遮罩（填充 + 描边）。关闭后该层不出 mesh，拓扑与投屏也整段跳过。")]
        public bool ShowPlaceableMask = true;

        [Header("红·不可放置 配色")]
        [Tooltip("填充：低 alpha 淡红。近乎铺满全屏，故只作「氛围底」，不承担辨识度；alpha 高了会压抑。")]
        public Color UnplaceableFillColor = new Color(1.0f, 0.18f, 0.15f, 0.55f);

        [Tooltip("描边：高饱和亮红、接近不透明，是本方案的视觉主体（对比度全押在边界上）。")]
        public Color UnplaceableStrokeColor = new Color(1.0f, 0.32f, 0.26f, 0.95f);

        [Header("绿·可放置 配色")]
        [Tooltip("填充：低 alpha 淡绿。与红同口径，只作氛围底。")]
        public Color PlaceableFillColor = new Color(0.18f, 1.0f, 0.30f, 0.40f);

        [Tooltip("描边：高饱和亮绿、接近不透明，承担边界强调。")]
        public Color PlaceableStrokeColor = new Color(0.25f, 1.0f, 0.32f, 0.95f);

        [Header("描边形状")]
        [Tooltip("描边半宽（Canvas 单位）。屏幕空间恒定粗细，不随相机推拉变化。红/绿共用。\n"
                 + "总宽 = 2×本值。控制「线有多粗」。")]
        [Range(1f, 40f)] public float StrokeHalfWidth = 12f;

        [Tooltip("实心芯占半宽的比例，控制「边缘多锐」（与粗细解耦）：\n"
                 + "0   = 全程羽化，从两侧全透明渐变到中线（旧行为，线看着糊）\n"
                 + "0.5 = 中间一半不透明、两侧各 25% 渐变（清晰且不锯齿，推荐）\n"
                 + "1   = 全实心硬边（屏幕空间无 MSAA，会有锯齿）")]
        [Range(0f, 1f)] public float StrokeCoreRatio = 0.5f;

        [Tooltip("轮廓简化容差，单位 = 六边形 OuterRadius 的倍数（Douglas-Peucker）。\n"
                 + "几何硬约束：锯齿振幅 = 0.5R，孤立单格的角点凸起也是 0.5R，两者垂距不可区分。\n"
                 + "< 0.5R → 锯齿留着（等于没简化）；≥ 0.8R → 连「单格凹口」这类真实特征一起丢。\n"
                 + "0.6R 居中：代价是孤立单格塌成圆润小块。")]
        [Range(0f, 1.2f)] public float SimplifyEpsilonInR = 0.6f;

        [Tooltip("圆角半径，同样以 OuterRadius 为单位。切点按相邻边半长夹取，故永不外溢，可放心给大值。")]
        [Range(0f, 1.5f)] public float CornerRadiusInR = 0.55f;

        [Tooltip("每个圆角的贝塞尔细分段数。折线已被简化到几十个点，这里可以给足。")]
        [Range(1, 16)] public int CornerSegments = 5;

        [Tooltip("相邻点合并阈值（Canvas 单位）：投屏后过近的点会让缎带在拐角处退化出尖刺，必须去重。")]
        [Range(0f, 4f)] public float MergeEpsilonLocal = 0.5f;

        [Header("描边立体感（方案 A：整体平移的深色副本）· 红绿各一份")]
        [Tooltip("【红·不可放置】的阴影线参数。红是全图口径的大片区域，通常要它有厚重感。")]
        public PlacementMaskShadowSettings UnplaceableShadow = new PlacementMaskShadowSettings
        {
            Offset = 0f,
            DirDeg = 270f,
            Tint = new Color(0.35f, 0.05f, 0.04f, 0.75f),
        };

        [Tooltip("【绿·可放置】的阴影线参数，与红完全独立。\n"
                 + "绿多是零散小岛，同样的 Offset 在小块上会显得过重，一般要给得比红小、或干脆置 0 关掉。")]
        public PlacementMaskShadowSettings PlaceableShadow = new PlacementMaskShadowSettings
        {
            Offset = 0f,
            DirDeg = 270f,
            Tint = new Color(0.05f, 0.30f, 0.08f, 0.75f),
        };

        [Header("重建触发阈值")]
        [Tooltip("相机位移平方阈值：超过才重建遮罩。调大省性能、但相机微动时轮廓会滞后。")]
        public float CamMoveSqrThreshold = 0.01f;

        [Tooltip("相机旋转阈值（度）：超过才重建遮罩。")]
        public float CamRotThresholdDeg = 0.1f;
    }
}
