using System;
using UnityEngine;

namespace UI.PlacementMask
{
    //****************************************
    // 卡牌拖拽范围遮罩的共享参数基类（战术卡影响范围 / 箭塔攻击范围 共用同一组字段）。
    //
    // 为什么抽基类：两套范围遮罩（战术卡 effectRadius n 环、箭塔有效射程 n 环）走的是
    // 同一份「拓扑 → 拟合 → 投屏 → 填充 + 描边」渲染管线，唯一区别是范围来源与默认形状。
    // 把配色 / 描边形状 / 阴影 / 重建阈值收进基类，两个具体子类只补各自的显示开关与形状默认值，
    // 避免把 13 个参数复制两份、日后改一处漏一处。
    //
    // 配色初值取红绿都撞不上的亮青（cyan）：红/绿是放置遮罩的相邻语义，金黄/柔红又已被
    // 拖拽落点单格高亮（HexHighlightRenderer.PlaceableGlowColor / UnplaceableGlowColor）占用。
    // 非法态 = 同色相压暗 + 降饱和（不要换红，否则与单格高亮的柔红混淆）。
    //
    // 载体是 GameInstaller 上的序列化字段（遮罩 UI 由 Zenject 运行时新建，自身 Inspector
    // 不随场景保存、无法在 Play 之前调）。
    //****************************************
    [Serializable]
    public class CardRangeMaskSettings
    {
        [Header("显示开关")]
        [Tooltip("是否显示该范围遮罩（填充 + 描边）。关闭后该层不出 mesh，拓扑与投屏整段跳过。")]
        public bool ShowRangeMask = true;

        [Header("配色·合法")]
        [Tooltip("填充：低 alpha 淡青。只作「氛围底」，不承担辨识度；范围是局部小区域，alpha 不必太高。")]
        public Color FillColor = new Color(0.35f, 0.95f, 1.0f, 0.30f);

        [Tooltip("描边：高饱和亮青、接近不透明，是本方案的视觉主体（对比度押在边界上）。")]
        public Color StrokeColor = new Color(0.25f, 0.95f, 1.0f, 0.95f);

        [Header("配色·非法（中心格不可部署）")]
        [Tooltip("填充：非法态压暗 + 降饱和的淡青。表达「位置无效，但你能看到打出来会覆盖哪」。")]
        public Color InvalidFillColor = new Color(0.18f, 0.32f, 0.36f, 0.26f);

        [Tooltip("描边：非法态压暗 + 降饱和的暗青。勿换红色（与单格高亮柔红混淆）。")]
        public Color InvalidStrokeColor = new Color(0.20f, 0.40f, 0.45f, 0.90f);

        [Header("描边形状")]
        [Tooltip("描边半宽（Canvas 单位）。屏幕空间恒定粗细。范围是局部小区域，比红/绿（12f）略粗以在地形底上跳出。\n"
                 + "总宽 = 2×本值。")]
        [Range(1f, 40f)] public float StrokeHalfWidth = 16f;

        [Tooltip("实心芯占半宽的比例，控制「边缘多锐」（与粗细解耦）：\n"
                 + "0   = 全程羽化；0.5 = 中间一半不透明、两侧渐变（推荐）；1 = 全实心硬边。")]
        [Range(0f, 1f)] public float StrokeCoreRatio = 0.5f;

        [Tooltip("轮廓简化容差，单位 = 六边形 OuterRadius 的倍数（Douglas-Peucker）。\n"
                 + "0 = 完全贴合六边形地块（保留最外圈真实格边的锯齿）；\n"
                 + "0.6R = 把 6 条直边的锯齿拟合成圆角大六边形（平滑区域读法）。")]
        [Range(0f, 1.2f)] public float SimplifyEpsilonInR = 0.6f;

        [Tooltip("圆角半径，同样以 OuterRadius 为单位。切点按相邻边半长夹取，永不外溢。0 = 保留六边形尖角。")]
        [Range(0f, 1.5f)] public float CornerRadiusInR = 0.55f;

        [Tooltip("每个圆角的贝塞尔细分段数。")]
        [Range(1, 16)] public int CornerSegments = 5;

        [Tooltip("相邻点合并阈值（Canvas 单位）：投屏后过近的点会让缎带在拐角处退化出尖刺，必须去重。")]
        [Range(0f, 4f)] public float MergeEpsilonLocal = 0.5f;

        [Header("描边立体感（方案 A：整体平移的深色副本）")]
        [Tooltip("阴影线参数。小范围区域建议 Offset 先给 0 或很小值，真机再标定。")]
        public PlacementMaskShadowSettings Shadow = new PlacementMaskShadowSettings
        {
            Offset = 0f,
            DirDeg = 270f,
            Tint = new Color(0.05f, 0.25f, 0.30f, 0.75f),
        };

        [Header("重建触发阈值")]
        [Tooltip("相机位移平方阈值：超过才重建遮罩。")]
        public float CamMoveSqrThreshold = 0.01f;

        [Tooltip("相机旋转阈值（度）：超过才重建遮罩。")]
        public float CamRotThresholdDeg = 0.1f;
    }
}
