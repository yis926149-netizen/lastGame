using System;

namespace GameConfig
{
    /// <summary>
    /// 表现配置（单行，由 game-config.json 导入的只读数据）。
    /// 相机震动、迷雾刷新、卡牌暗淡、天赋选择震屏与卡牌拖拽模型预览等表现手感参数。
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

        // 卡牌拖拽模型预览特效：两阶段进度阈值以 Canvas 参考高度为基准（见实施计划 §3）。
        public float cardDragStage1Ratio;
        public float cardDragStage2Ratio;
        public float cardDragCardMinScale;
        public float cardDragCardFadeStart;
        public float cardDragModelMinScale;
        public float cardDragModelFadeIn;
        public int cardDragPreviewRTSize;
        public float cardDragPreviewWindowSize;
        public float cardDragPreviewCameraDistance;
        public float cardDragPreviewPadding;
    }
}
