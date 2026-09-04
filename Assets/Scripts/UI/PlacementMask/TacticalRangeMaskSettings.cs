using System;

namespace UI.PlacementMask
{
    //****************************************
    // 战术卡影响范围遮罩（拖拽态·触点指向地块 + 其 n 环）的参数组。
    //
    // 参数全部继承自 CardRangeMaskSettings（配色 / 描边形状 / 阴影 / 重建阈值），
    // 本类只保留「战术卡」这一身份：默认沿用基类的平滑形状（SimplifyEpsilonInR=0.6R、
    // CornerRadiusInR=0.55R，即圆角大六边形），因为战术卡范围通常要表达「覆盖到哪」的区域读法。
    //
    // 载体是 GameInstaller 上的序列化字段（遮罩 UI 由 Zenject 运行时新建，自身 Inspector
    // 不随场景保存、无法在 Play 之前调）。
    //****************************************
    [Serializable]
    public sealed class TacticalRangeMaskSettings : CardRangeMaskSettings
    {
        // 无额外字段：显示开关与全部表现参数见基类 CardRangeMaskSettings。
    }
}
