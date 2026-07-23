using UnityEngine;
using Zenject;

//****************************************
// 功能说明：单位行为基类（MonoBehaviour，挂在每个单位 GameObject 上）。
//
// 职责分工：
//   - 玩家/AI 差异 → 继承（PlayerUnitBrain / AIUnitBrain 各自提供迷雾/目标查询）
//   - 兵种差异     → 组合（activeStrategy 按 UnitID 装配兵种策略）
//   - buff 改行为  → 装饰器（buff 生效时往 activeStrategy 外包装饰器）
//
// 核心骨架（OnStepFinished）固定在此，子类不可覆写：
//   先判断作战 → 否则移动。
//
// 决策触发时机：
//   1. 每走一格到达后 → UnitMovementController.OnMoveFinished() 调用 OnStepFinished()
//   2. 每个攻速周期结束 → 检查目标是否还活/在范围内
//   3. 目标死亡事件    → 立即重新决策
//
// 【检查点 2：搭架子】当前仅声明结构，OnStepFinished 内部为空占位，
//   不接入任何现有移动/攻击逻辑，不影响游戏运行。
//****************************************

public abstract class UnitBrainBase : MonoBehaviour
{
    // ── 关联数据 ──────────────────────────────────────────
    /// <summary>与本 Brain 关联的运行时角色数据。</summary>
    public CharacterData Owner { get; set; }

    // ── 当前策略（兵种基础策略 or 装饰器链头部）────────────
    protected IUnitStrategy activeStrategy;

    // ── 暂停标志（由 GameLoop 控制）────────────────────────
    public bool IsPaused { get; set; }

    // ── 共有骨架（不可覆写）─────────────────────────────────
    /// <summary>
    /// 每走一格到达后触发的决策入口。
    /// 固定优先级：先判断作战，否则移动。
    /// 【检查点 2】当前为空占位，尚未接入移动/攻击逻辑。
    /// </summary>
    public void OnStepFinished()
    {
        if (IsPaused || activeStrategy == null || Owner == null) return;

        // TODO（检查点 3/4 接入）：
        // if (activeStrategy.CanAttack(this))
        //     activeStrategy.DoCombat(this);
        // else
        //     MoveTo(activeStrategy.ChooseNextStep(this));
    }

    // ── 子类必须实现：提供目标查询（区分玩家迷雾/AI迷雾）──────
    /// <summary>在当前视野内，找最近的敌方单位坐标（找不到返回 null）。</summary>
    public abstract Vector3? FindNearestEnemy();

    /// <summary>在当前视野内，找最近的敌方建筑坐标（找不到返回 null）。</summary>
    public abstract Vector3? FindNearestEnemyBuilding();

    // ── 策略装配（由工厂/Installer 在单位创建时调用）──────────
    public void SetStrategy(IUnitStrategy strategy)
    {
        activeStrategy = strategy;
    }
}
