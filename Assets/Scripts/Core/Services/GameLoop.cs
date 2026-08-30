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
    // ── 速度档位 ────────────────────────────────────────────────
    public enum GameSpeed { Paused, x1, x2, x3 }

    /// <summary>当前速度档位。</summary>
    public GameSpeed CurrentSpeed { get; private set; } = GameSpeed.x1;

    /// <summary>全局暂停标志（兼容旧代码，CurrentSpeed == Paused 时为 true）。</summary>
    public bool IsPaused => CurrentSpeed == GameSpeed.Paused;

    /// <summary>当前帧的缩放 deltaTime（暂停时为 0，供其他系统消费）。</summary>
    public float ScaledDeltaTime { get; private set; }

    private float SpeedMultiplier => CurrentSpeed switch
    {
        GameSpeed.x2 => 2f,
        GameSpeed.x3 => 3f,
        GameSpeed.Paused => 0f,
        _ => 1f
    };

    // 系统强制暂停（EndGame/天赋卡牌）前保存的速度，恢复时回到该档
    private GameSpeed _speedBeforeForcePause = GameSpeed.x1;

    /// <summary>累计游戏时间（秒），暂停时不累加。</summary>
    public float GameTime { get; private set; }

    // 注册到循环的所有单位 Brain
    private readonly List<UnitBrainBase> _brains = new List<UnitBrainBase>();

    // ── 每帧决策预算（轮转调度）────────────────────────────
    // 单次决策会触发完整寻路（Dijkstra），成本远高于一帧的其它工作。
    // 旧实现在同一帧内驱动全部空闲 brain，使「单位数」直接乘进每帧成本，
    // 20+ 单位时成为主要卡顿放大器。改为轮转：每帧最多驱动 BrainDecisionBudgetPerFrame
    // 个 brain，游标跨帧推进，保证公平且无饥饿（每个 brain 至多等 ceil(N/K) 帧）。
    private const int DefaultBrainDecisionsPerFrame = 8;

    /// <summary>每帧最多执行决策的 brain 数量。&lt;= 0 表示不限制（退回旧的全员驱动行为）。</summary>
    public int BrainDecisionBudgetPerFrame { get; set; } = DefaultBrainDecisionsPerFrame;

    // 轮转游标：指向下一个待检查的 brain 下标，跨帧保留。
    private int _brainCursor;

    // 【公共建筑系统-决策#26/#41】公共建筑列表（单独遍历，不改 UnitBrainBase）
    private readonly List<PublicBuildingBase> _publicBuildings = new List<PublicBuildingBase>();

    // 全局倒计时服务
    private readonly GlobalTimerService _globalTimer;

    public GameLoop(GlobalTimerService globalTimer)
    {
        _globalTimer = globalTimer;
    }

    public void Initialize()
    {
        // 批次 A：无额外初始化操作
    }

    /// <summary>Zenject 每帧调用。</summary>
    public void Tick()
    {
        // 先计算本帧缩放 delta（暂停时为 0），供本类与 UnitMovementSystem 等外部系统消费。
        ScaledDeltaTime = IsPaused ? 0f : Time.deltaTime * SpeedMultiplier;
        if (IsPaused) return;
        GameTime += ScaledDeltaTime;

        // 先清理已销毁的 brain，使下面的轮转可以按稳定下标推进
        for (int i = _brains.Count - 1; i >= 0; i--)
        {
            if (_brains[i] == null) _brains.RemoveAt(i);
        }

        // 轮转决策：从 _brainCursor 起最多扫一圈，累计执行 budget 次决策后停止。
        // 同一帧内仍按扫描顺序**顺序**决策，先到先得（见 5.6 决策顺序化）；
        // 只是把"全员/帧"改成了"K 个/帧"，把 N 的乘数从每帧成本里摘掉。
        int count = _brains.Count;
        if (count > 0)
        {
            int budget = BrainDecisionBudgetPerFrame > 0 ? BrainDecisionBudgetPerFrame : count;
            if (_brainCursor >= count) _brainCursor = 0;

            int executed = 0;
            for (int scanned = 0; scanned < count && executed < budget; scanned++)
            {
                int index = _brainCursor + scanned;
                if (index >= count) index -= count;

                var brain = _brains[index];
                // 决策过程中可能销毁单位（战斗结算），已销毁者下帧清理
                if (brain == null) continue;

                // 跳过暂停或忙碌的单位。忙碌不消耗预算：它本来就不会做决策，
                // 若计入预算会让"移动中的单位多"变相饿死真正需要决策的单位。
                if (brain.IsPaused) continue;
                if (brain.IsBusy) continue;

                brain.OnStepFinished();
                executed++;

                // 游标推进到刚执行者的下一位，保证下帧从未处理的 brain 继续
                _brainCursor = index + 1;
                if (_brainCursor >= count) _brainCursor = 0;
            }

            // 一圈扫完仍未用尽预算（全员忙碌/暂停）：游标整体前移一格，避免固定起点带来的偏置
            if (executed == 0)
            {
                _brainCursor++;
                if (_brainCursor >= count) _brainCursor = 0;
            }
        }

        // 【公共建筑系统】检测公共建筑死亡（易主），替代 Update() 轮询
        TickPublicBuildings();

        // 全局倒计时（暂停时跳过，已在上方 IsPaused 检查中一并跳过）
        _globalTimer.Tick(ScaledDeltaTime);
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

            pb.TickDiscovery();
        }
    }

    /// <summary>公共建筑显形后清除所有指向旧提示位置的缓存路径。</summary>
    public void InvalidateAllBrainPaths()
    {
        for (int i = _brains.Count - 1; i >= 0; i--)
        {
            var brain = _brains[i];
            if (brain == null)
            {
                _brains.RemoveAt(i);
                continue;
            }

            brain.InvalidatePath();
        }
    }

    public void SetPaused(bool paused)
    {
        if (paused)
        {
            // 记录强制暂停前的速度档，恢复时回到该档（EndGame/天赋卡牌等系统会直接调 SetPaused）
            if (CurrentSpeed != GameSpeed.Paused)
                _speedBeforeForcePause = CurrentSpeed;
            SetSpeed(GameSpeed.Paused);
        }
        else
        {
            SetSpeed(_speedBeforeForcePause);
        }
    }

    /// <summary>切换速度档位。</summary>
    public void SetSpeed(GameSpeed speed)
    {
        CurrentSpeed = speed;
        bool isPaused = speed == GameSpeed.Paused;

        // 广播暂停状态到所有已注册 Brain（Brain.IsPaused 控制单位决策）。
        // 移动动画暂停由 UnitMovementSystem.Tick 直接查询 GameLoop.IsPaused 实现。
        for (int i = _brains.Count - 1; i >= 0; i--)
        {
            var brain = _brains[i];
            if (brain == null) { _brains.RemoveAt(i); continue; }
            brain.IsPaused = isPaused;
        }
    }

    public void Register(UnitBrainBase brain)
    {
        if (brain == null || _brains.Contains(brain)) return;

        // 继承当前暂停状态：暂停时生成的单位（卡牌部署/兵营出兵/AI 增援）须立即冻结，
        // 否则其 MonoBehaviour.Update（回血/攻速冷却计时）会在暂停期间继续推进。
        brain.IsPaused = IsPaused;
        _brains.Add(brain);
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
