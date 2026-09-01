using DG.Tweening;
using UnityEngine;

/// <summary>
/// 目标 UI 缩放 punch 共享工具（每枚重启语义）：
/// 每一枚飞行元素落地都独立触发一次完整的缩放反馈——
///   · 每枚落地调用一次 RequestPunch()；
///   · 若上一条 punch 仍在播放，先 Kill 掉并**显式归位到基准缩放**，再从头开始新的一轮；
///   · 因此连续落地时目标 UI 表现为"一次次从原大小重新弹"，而不是叠加或合并。
///
/// 关于归位：DOPunchScale 被 Kill 时不会自动回退到起始值（只有正常 Complete 才回退），
/// 必须手动把 localScale 写回 _baseScale，否则连续打断会让缩放逐次漂移、越缩越小或越弹越大。
///
/// 只 Kill 自己这条 Sequence（_seq.Kill()），不用 _target.DOKill()——后者会把目标 UI 上
/// 无关的 tween 一并杀掉。
///
/// 本类为无状态持有者（非 MonoBehaviour），由使用方在 Awake/Start 创建并负责 Dispose。
/// </summary>
public sealed class FlyerPunchTarget
{
	private readonly RectTransform _target;
	private readonly Vector3 _baseScale;      // punch 开始/结束/被打断时的基准缩放
	private readonly float _punchScale;       // 每枚落地的振幅（每次都相同，不随枚数放大）
	private readonly float _punchDuration;
	private readonly int _vibrato;
	private readonly float _elasticity;

	private Sequence _seq;

	/// <param name="target">被 punch 的目标 UI。</param>
	/// <param name="baseScale">目标 UI 未受 punch 影响时的基准缩放（Vector3，保留非等比缩放）。</param>
	/// <param name="punchScale">每枚落地的振幅。</param>
	/// <param name="punchDuration">单轮 punch 持续时长。</param>
	/// <param name="vibrato">punch 振动次数。</param>
	/// <param name="elasticity">punch 弹性。</param>
	public FlyerPunchTarget(
		RectTransform target,
		Vector3 baseScale,
		float punchScale,
		float punchDuration,
		int vibrato,
		float elasticity)
	{
		_target = target;
		_baseScale = baseScale;
		_punchScale = punchScale;
		_punchDuration = punchDuration;
		_vibrato = vibrato;
		_elasticity = elasticity;
	}

	/// <summary>
	/// 一次落地到达：强制重启缩放动画。
	/// 缩放期间再有金币进入，会打断当前动画、恢复原大小、重新开始。
	/// </summary>
	public void RequestPunch()
	{
		if (_target == null) return;

		// 强制重启：先停掉在播的那条并归位，避免打断处的缩放被当成新起点而逐次漂移。
		KillAndReset();

		_seq = DOTween.Sequence();
		_seq.Append(_target.DOPunchScale(Vector3.one * _punchScale, _punchDuration, _vibrato, _elasticity));
		_seq.OnComplete(() =>
		{
			_seq = null;
			if (_target != null) _target.localScale = _baseScale;
		});
	}

	/// <summary>使用方销毁时调用：停掉自己这条动画并归位基准缩放。</summary>
	public void Dispose()
	{
		KillAndReset();
	}

	private void KillAndReset()
	{
		if (_seq != null)
		{
			_seq.Kill();
			_seq = null;
		}
		if (_target != null)
		{
			// DOPunchScale 被 Kill 不会自动回退，必须显式归位（见类注释）。
			_target.localScale = _baseScale;
		}
	}
}
