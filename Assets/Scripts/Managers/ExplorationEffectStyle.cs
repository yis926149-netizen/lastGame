/// <summary>
/// 地块探索特效风格。挂在 ExplorationPillarPool 上通过 Inspector 下拉框切换。
/// 两种方案共存，互不覆盖。
/// </summary>
public enum ExplorationEffectStyle
{
	/// <summary>方案一：六边形石柱从地下升起 → 顶部溶解消散。</summary>
	PillarRise,

	/// <summary>方案二：六边形飞盘从地下弹出 → 上抛翻转 → 下坠撞击 → 势力范围扩散环。</summary>
	DiskSmash
}
