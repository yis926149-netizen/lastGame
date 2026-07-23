using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
// 功能说明：实时化游戏主循环（替代 GameStateMachine）。
//   每帧 Tick 顺序驱动所有单位 Brain 与结算逻辑；持有全局暂停标志。
//
// 决策顺序化（见 5.6）：所有单位决策在同一 Tick 内顺序执行（不并行），
//   使实时并发的"同时冲突"自动消解为"先后"，现有预占机制可直接复用。
//
// 暂停语义（见第二步）：
//   IsPaused == true 时，单位行为/移动动画/结算定时器全部停止；
//   摄像机控制与卡牌拖拽（UI 交互）不受暂停影响。
//
// 【检查点 2：搭架子】空壳，不注册为 ITickable，不驱动任何逻辑，
//   不参与 DI 绑定，游戏仍由 GameStateMachine 回合制驱动。
//   将在检查点 4 接管驱动。
//****************************************

public class GameLoop
{
    /// <summary>全局暂停标志（对应暂停按钮）。</summary>
    public bool IsPaused { get; private set; }

    /// <summary>累计游戏时间（秒），暂停时不累加。</summary>
    public float GameTime { get; private set; }

    // 注册到循环的所有单位 Brain（检查点 4 接入）
    private readonly List<UnitBrainBase> _brains = new List<UnitBrainBase>();

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        // TODO（检查点 4）：广播暂停状态到各 Brain 与移动系统。
    }

    /// <summary>
    /// 每帧驱动。
    /// 【检查点 2】空实现，未接入。
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (IsPaused) return;
        GameTime += deltaTime;

        // TODO（检查点 4 接入）：
        // foreach (var brain in _brains) brain.OnStepFinished();
        // 驱动结算定时器等。
    }

    public void Register(UnitBrainBase brain)
    {
        if (brain != null && !_brains.Contains(brain)) _brains.Add(brain);
    }

    public void Unregister(UnitBrainBase brain)
    {
        _brains.Remove(brain);
    }
}
