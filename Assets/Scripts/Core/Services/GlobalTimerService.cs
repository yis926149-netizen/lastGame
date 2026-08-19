using System;
using UnityEngine;

/// <summary>
/// 全局倒计时服务。不依赖 GameLoop（由外层在非暂停时调用 Tick）。
/// 超时结束通过 Event 通知，不包含自动判胜负逻辑。
/// 【Excel 数值化】总时长仅读 GameFlowConfigProvider（阶段6 唯一主源）。
/// </summary>
public class GlobalTimerService
{
    private readonly GameFlowConfigProvider _gameFlow;

    // 初始化为满时长：未启动时 HUD 显示总时长而非 0；StartTimer 会覆盖此值。
    private float _remaining;
    private bool _running;

    public GlobalTimerService(GameFlowConfigProvider gameFlow = null)
    {
        _gameFlow = gameFlow;
        _remaining = DefaultDuration;
    }

    /// <summary>默认倒计时总时长（秒）。启动前 Remaining 即为此值，使 HUD 显示满时长。Excel 唯一主源。</summary>
    public float DefaultDuration => _gameFlow.GameDurationSeconds;

    public float Remaining => _remaining;
    public bool IsRunning => _running;
    public bool IsExpired => _running && _remaining <= 0f;

    /// <summary>超时事件。为 EndGame 或其他消费者预留接口。</summary>
    public event Action OnTimeout;

    /// <summary>启动倒计时，若已在运行则静默忽略。</summary>
    public void StartTimer(float seconds)
    {
        if (_running) return;

        _remaining = Mathf.Max(0f, seconds);
        _running = true;
        Debug.Log($"[GlobalTimer] Started: {_remaining:F0}s");
    }

    /// <summary>每帧调用（调用方需保证仅在非暂停时调用）。</summary>
    public void Tick(float deltaTime)
    {
        if (!_running) return;

        _remaining -= deltaTime;
        if (_remaining <= 0f)
        {
            _remaining = 0f;
            _running = false;
            Debug.Log("[GlobalTimer] Time expired");
            OnTimeout?.Invoke();
        }
    }
}
