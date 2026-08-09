using System.Collections.Generic;

//****************************************
// 【动态地图-阶段二】临时可见性服务（TemporaryVisibilityService）
// 来源式 VisibilityLease：竞技场/技能/教程/调试各自持有 lease，
// 只释放自己的 lease 不影响其他来源（§四约束、§二十-7）。
// 可见性不写入 HexCellData / HexCellPatch。
//****************************************

/// <summary>
/// 可见性租约：AcquireLease 返回，释放后不可重复使用（幂等）。
/// 对局结束/异常取消时由持有方在 finally 中释放。
/// </summary>
public sealed class VisibilityLease
{
    private readonly TemporaryVisibilityService _service;
    private bool _released;

    public string Source { get; }
    public IReadOnlyList<HexCellData> Cells { get; }
    public bool IsActive => !_released;

    internal VisibilityLease(TemporaryVisibilityService service, string source, IReadOnlyList<HexCellData> cells)
    {
        _service = service;
        Source = source;
        Cells = cells;
    }

    /// <summary>释放本 lease（只影响本来源的可见性）。幂等。</summary>
    public void Release()
    {
        if (_released) return;
        _released = true;
        _service.ReleaseLease(this);
    }
}

public class TemporaryVisibilityService : IMapVisibilityResolver
{
    private readonly ILogisticsService _logisticsService;
    private readonly List<VisibilityLease> _leases = new List<VisibilityLease>();
    private readonly HashSet<HexCellData> _temporaryVisible = new HashSet<HexCellData>();

    public TemporaryVisibilityService([Zenject.InjectOptional] ILogisticsService logisticsService = null)
    {
        _logisticsService = logisticsService;
    }

    /// <summary>以指定来源申请覆盖一组格的临时可见性。同一来源可多次申请（各持一份 lease）。</summary>
    public VisibilityLease AcquireLease(string source, IEnumerable<HexCellData> cells)
    {
        var list = new List<HexCellData>();
        foreach (HexCellData cell in cells)
        {
            if (cell != null && !list.Contains(cell)) list.Add(cell);
        }

        var lease = new VisibilityLease(this, source, list);
        _leases.Add(lease);
        foreach (HexCellData cell in list) _temporaryVisible.Add(cell);
        return lease;
    }

    /// <summary>当前是否有任意来源正在临时点亮该格。</summary>
    public bool IsTemporarilyVisible(HexCellData cell)
    {
        return cell != null && _temporaryVisible.Contains(cell);
    }

    public bool HasActiveLeases => _leases.Count > 0;

    /// <summary>强制释放全部 lease（对局结束兜底）。</summary>
    public void ReleaseAll()
    {
        for (int i = _leases.Count - 1; i >= 0; i--)
            _leases[i].Release();
    }

    internal void ReleaseLease(VisibilityLease lease)
    {
        _leases.Remove(lease);
        RebuildCache();
    }

    private void RebuildCache()
    {
        _temporaryVisible.Clear();
        foreach (VisibilityLease lease in _leases)
        {
            foreach (HexCellData cell in lease.Cells)
                _temporaryVisible.Add(cell);
        }
    }

    public bool IsVisibleToFaction(HexCellData cell, int factionId)
    {
        if (cell == null) return false;

        // 【程序化山脉-阶段6.2】有效山格永久视觉可见（决策 ⑪）：
        // 只参与视觉可见性合成，不写 IsExplored/归属/探索费用，也不创建 lease；
        // 优先级高于普通未探索雾与后勤可见性。水淹/永久清除/低于最小可见高度后
        // MountainVisibilityRule 自动回落 false，重新走下方普通可见性链（决策 ⑦/㉕）。
        // 结果与阵营无关：玩家/AI/中立归属的山格视觉均可见（MountainVisibilityRuleTests 覆盖）。
        if (MountainVisibilityRule.IsPermanentlyVisible(cell)) return true;

        if (_temporaryVisible.Contains(cell)) return true;

        // 永久可见性：与 LogisticsService 语义一致（归属方已探索且后勤连通 / 中立格按观察方探索位）
        if (_logisticsService != null)
            return _logisticsService.IsVisibleToFaction(cell, factionId);
        return cell.IsExplored;
    }
}
