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
// 【批次 A】已注册为 IInitializable + ITickable，每帧遍历空闲 Brain 触发
//   OnStepFinished()——此时 OnStepFinished 内部仍为空占位，游戏行为不变。
//   将在批次 B 接入实际决策逻辑。
//
// 【批次 C】SetPaused 将广播暂停状态到 UnitMovementSystem 等。
//****************************************

public class GameLoop : IInitializable, ITickable
{
    /// <summary>全局暂停标志（对应暂停按钮）。</summary>
    public bool IsPaused { get; private set; }

    /// <summary>累计游戏时间（秒），暂停时不累加。</summary>
    public float GameTime { get; private set; }

    // 注册到循环的所有单位 Brain
    private readonly List<UnitBrainBase> _brains = new List<UnitBrainBase>();

    // 【公共建筑系统-决策#26/#41】公共建筑列表（单独遍历，不改 UnitBrainBase）
    private readonly List<PublicBuildingBase> _publicBuildings = new List<PublicBuildingBase>();

    public void Initialize()
    {
        // 批次 A：无额外初始化操作
    }

    /// <summary>Zenject 每帧调用。</summary>
    public void Tick()
    {
        if (IsPaused) return;
        GameTime += Time.deltaTime;

        // 顺序遍历：同一帧内按遍历顺序决策，先到先得（见 5.6 决策顺序化）
        for (int i = _brains.Count - 1; i >= 0; i--)
        {
            var brain = _brains[i];
            if (brain == null)
            {
                _brains.RemoveAt(i);
                continue;
            }

            // 跳过已销毁、暂停或忙碌的单位
            if (brain.IsPaused) continue;
            if (brain.IsBusy) continue;

            brain.OnStepFinished();
        }

        // 【公共建筑系统】检测公共建筑死亡（易主），替代 Update() 轮询
        TickPublicBuildings();
    }

    // ── 公共建筑 Tick（决策#15/#26）─────────────────
    private void TickPublicBuildings()
    {
        for (int i = _publicBuildings.Count - 1; i >= 0; i--)
        {
            var pb = _publicBuildings[i];
            if (pb == null)
            {
                _publicBuildings.RemoveAt(i);
                continue;
            }

            if (pb.CheckDeath())
            {
                pb.OnDeath();
            }
        }
    }

    public void SetPaused(bool paused)
    {
        IsPaused = paused;

        // 广播暂停状态到所有已注册 Brain（Brain.IsPaused 控制单位决策）。
        // 移动动画暂停由 UnitMovementSystem.Tick 直接查询 GameLoop.IsPaused 实现。
        for (int i = _brains.Count - 1; i >= 0; i--)
        {
            var brain = _brains[i];
            if (brain == null) { _brains.RemoveAt(i); continue; }
            brain.IsPaused = paused;
        }
    }

    public void Register(UnitBrainBase brain)
    {
        if (brain != null && !_brains.Contains(brain)) _brains.Add(brain);
    }

    public void Unregister(UnitBrainBase brain)
    {
        _brains.Remove(brain);
    }

    // ── 公共建筑注册/注销（决策#26）─────────────────
    public void RegisterPublicBuilding(PublicBuildingBase pb)
    {
        if (pb != null && !_publicBuildings.Contains(pb)) _publicBuildings.Add(pb);
    }

    public void UnregisterPublicBuilding(PublicBuildingBase pb)
    {
        _publicBuildings.Remove(pb);
    }
}
