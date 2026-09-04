using System;

namespace UI.PlacementMask
{
    //****************************************
    // 箭塔攻击范围遮罩（拖拽箭塔建造卡时·触点指向地块 + 其 n 环）的参数组。
    //
    // 参数全部继承自 CardRangeMaskSettings，本类只做一件事：把默认形状改成「完全贴合六边形地块」——
    // SimplifyEpsilonInR = 0、CornerRadiusInR = 0，即不简化、不圆角，边界就是外圈真实格边。
    // 箭塔范围的核心信息是「哪些格在射程内」，精确格边在语义上比圆角大六边形更诚实。
    //
    // 配色 / 描边 / 阴影等默认值与战术卡完全一致（继承基类），但二者是**独立实例**：
    // GameInstaller 上各挂一份序列化字段，可分别调整互不影响。
    //****************************************
    [Serializable]
    public sealed class ArrowTowerRangeMaskSettings : CardRangeMaskSettings
    {
        public ArrowTowerRangeMaskSettings()
        {
            // 默认精确贴合六边形地块：关掉 DP 简化与圆角，保留最外圈格边锯齿。
            SimplifyEpsilonInR = 0f;
            CornerRadiusInR = 0f;
        }
    }
}
