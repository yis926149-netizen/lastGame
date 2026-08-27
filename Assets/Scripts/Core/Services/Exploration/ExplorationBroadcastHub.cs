using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>探索广播源：普通接收者只注入本接口，只能订阅，不能发布。</summary>
public interface IExplorationBroadcastSource
{
    event Action<ExplorationAcquisition> Broadcast;
}

/// <summary>探索广播发布接口：仅领域服务（探索/奖励结算）可发布后续阶段。</summary>
public interface IExplorationBroadcastPublisher
{
    void Publish(ExplorationAcquisition acquisition);
}

/// <summary>
/// 探索统一广播中心：单一广播流、FIFO 排队、逐订阅者异常隔离。
/// 不包含奖励规则、阵营规则或动画规则，只负责可靠分发。
/// </summary>
public class ExplorationBroadcastHub : IExplorationBroadcastSource, IExplorationBroadcastPublisher
{
    private readonly Queue<ExplorationAcquisition> _queue = new Queue<ExplorationAcquisition>();
    private bool _isDispatching;

    public event Action<ExplorationAcquisition> Broadcast;

    public void Publish(ExplorationAcquisition acquisition)
    {
        if (acquisition == null)
        {
            Debug.LogWarning("[ExplorationBroadcast] Publish(null) 被忽略。");
            return;
        }

        _queue.Enqueue(acquisition);
        if (_isDispatching)
            return;

        _isDispatching = true;
        try
        {
            while (_queue.Count > 0)
            {
                Dispatch(_queue.Dequeue());
            }
        }
        finally
        {
            _isDispatching = false;
        }
    }

    private void Dispatch(ExplorationAcquisition acquisition)
    {
        if (Broadcast == null)
            return;

        foreach (Delegate handler in Broadcast.GetInvocationList())
        {
            try
            {
                ((Action<ExplorationAcquisition>)handler)(acquisition);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
