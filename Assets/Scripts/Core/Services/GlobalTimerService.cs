using System;
using UnityEngine;

/// <summary>
/// 全局倒计时服务。不依赖 GameLoop（由外层在非暂停时调用 Tick）。
/// 超时结束通过 Event 通知，不包含自动判胜负逻辑。
/// </summary>
public class GlobalTimerService
{
    private float _remaining;
    private bool _running;

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
