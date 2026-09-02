using System;

namespace GameConfig
{
    /// <summary>
    /// 表现配置（单行，由 game-config.json 导入的只读数据）。
    /// 相机震动、迷雾刷新、卡牌暗淡、天赋选择震屏与卡牌拖拽世界空间预览等表现手感参数。
    /// </summary>
    [Serializable]
    public sealed class FeelConfigData
    {
        public string configId;
        public float fogRefreshInterval;
        public float cameraShakeFrequency;
        public float unaffordableCardDim;
        public float talentScreenShakeStrength;
        public float talentScreenShakeDuration;

        // 卡牌拖拽：单阶段卡牌手感。阈值以 Canvas 参考高度为基准（见改造计划 §六）。
        public float cardDragStage1Ratio;
        public float cardDragCardMinScale;
        public float cardDragCardFadeStart;

        // ── 以下 7 个字段为两阶段/RT 预览方案的遗留（deprecated）────────────────
        // 配置链（Excel 列 / schema 列定义 / Importer 赋值）一律保留以维持导出稳定，
        // 代码侧不再读取：FeelConfigProvider 中对应属性已删除。
        // ──────────────────────────────────────────────────────────────
        // [deprecated] 拖拽阶段二满程距离 / Canvas参考高度（D2，必须大于D1）
        public float cardDragStage2Ratio;
        // [deprecated] 阶段二起始模型预览缩放（0~1）
        public float cardDragModelMinScale;
        // [deprecated] 模型淡入占 modelProgress 的比例区间（0~1）
        public float cardDragModelFadeIn;
        // [deprecated] 模型预览 RenderTexture 边长（像素，正方形）
        public int cardDragPreviewRTSize;
        // [deprecated] 模型预览窗口边长（Canvas 参考单位）
        public float cardDragPreviewWindowSize;
        // [deprecated] 预览正交相机与模型的距离（世界单位）
        public float cardDragPreviewCameraDistance;
        // [deprecated] 预览取景留白系数（正交尺寸 = 模型半径 × 本系数）
        public float cardDragPreviewPadding;

        // 卡牌拖拽世界空间预览（改造计划 §7.1）。
        public float cardDragPreviewHoverHeight;
        public float cardDragPreviewSnapDuration;
        public float cardDragPreviewAppearDuration;

        // 落位拉伸（Squash & Stretch）：拉长峰值幅度（Y 拉长比例），0 = 关闭拉伸。
        public float cardDragPreviewLandingStretch;

        // 落位拉伸：拉长峰值位置（progress 0~1，峰值时刻）。
        public float cardDragPreviewLandingStretchPeak;

        // 落地压扁幅度（落地瞬间 Y 下压比例），0 = 关闭压扁。
        public float cardDragPreviewLandingSquash;

        // 额外下落高度（落位视觉起点整体抬高的世界 Y 量，延长落差），0 = 关闭。
        public float cardDragPreviewLandingDropHeight;

        // 卡牌拖拽落点提示（落点图标与连线计划 §5.5）。
        public float cardDragTargetIconHeight;
        public float cardDragTargetIconScale;
        public float cardDragLinkWidth;
    }
}
