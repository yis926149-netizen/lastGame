using System.Collections.Generic;

//****************************************
// 【动态地图-阶段二】地图交互闸门实现（MapInteractionGate）
// 独立叶子类（无反向依赖，避免 MapMutationService ↔ UnitMovementSystem 循环构造依赖）。
// MapMutationService 在事务期间 LockCells / 提交结束 UnlockAll；
// 移动/探索/部署入口统一查询 IMapInteractionGate.IsLocked（§12.6）。
// 阶段二 Duration=0：锁只在同步 Commit 期间持有；阶段四动画期间扩展为持续锁定。
//****************************************

public class MapInteractionGate : IMapInteractionGate
{
    private readonly HashSet<HexCellData> _lockedCells = new HashSet<HexCellData>();

    public bool IsLocked(HexCellData cell, MapInteractionType type)
    {
        return cell != null && _lockedCells.Contains(cell);
    }

    /// <summary>锁定一批受影响格（事务开始/动画开始）。</summary>
    public void LockCells(IEnumerable<HexCellData> cells)
    {
        if (cells == null) return;
        foreach (HexCellData cell in cells)
        {
            if (cell != null) _lockedCells.Add(cell);
        }
    }

    /// <summary>解锁全部（提交结束/动画结束/取消路径）。幂等。</summary>
    public void UnlockAll()
    {
        _lockedCells.Clear();
    }

    /// <summary>只解锁指定格集合（并行动画下各动画只解锁自己的格，§阶段五-并行动画）。幂等。</summary>
    public void UnlockCells(IEnumerable<HexCellData> cells)
    {
        if (cells == null) return;
        foreach (HexCellData cell in cells)
        {
            if (cell != null) _lockedCells.Remove(cell);
        }
    }

    public bool HasLocks => _lockedCells.Count > 0;
}
