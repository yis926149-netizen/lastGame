using System.Collections.Generic;
using UnityEngine;

//****************************************
// 功能说明：《多单位地块落点与移动编排计划》3.1~3.3 的槽位模型。
//   - 每格固定 5 个槽位（0=中心，1~4=四角），数量全图统一，只有位置随机。
//   - 位置在生成期按「地图种子 + 地块坐标 + 槽位索引」烘焙一次，运行时只读。
//   - StandingSlot（占用）与 ReservedSlot（预留）共享同一组槽位，
//     同一 (cell, slotId) 不能被两个单位同时持有。
//   - AttackerSlot 仍由 HexCellData 的 6 方向槽独立管理，不计入普通站位容量。
//****************************************

public sealed class UnitSlotData
{
    public readonly int SlotId;
    public Vector3 LocalOffset;        // 相对格心的 XZ 偏移（Y 恒 0），生成期烘焙、运行时只读
    public bool Disabled;              // 生成期碰撞校验失败，永久不可用
    public GameObject OccupiedBy;      // StandingSlot：当前站在此槽的单位（null = 空）
    public IUnitMovement ReservedBy;   // ReservedSlot：移动任务预留但尚未到达（null = 空）

    public UnitSlotData(int slotId)
    {
        SlotId = slotId;
    }
}

public sealed class UnitSlotProvider
{
    public const int SlotCount = 5;
    public const int CenterSlotId = 0;

    // 基础布局：中心 + 四角（相对格心 XZ 的归一化方向）。中心锚点固定 0 偏移、不抖动。
    private static readonly Vector2[] BaseAnchors = new Vector2[]
    {
        new Vector2( 0f,  0f),  // 中心（不抖动）
        new Vector2(-1f, -1f),  // 左前
        new Vector2( 1f, -1f),  // 右前
        new Vector2(-1f,  1f),  // 左后
        new Vector2( 1f,  1f),  // 右后
    };

    private readonly UnitSlotData[] _slots;

    public UnitSlotProvider()
    {
        _slots = new UnitSlotData[SlotCount];
        for (int i = 0; i < SlotCount; i++)
            _slots[i] = new UnitSlotData(i);
    }

    public IReadOnlyList<UnitSlotData> Slots => _slots;

    // ── 烘焙（生成期一次性）──────────────────────────────────────
    /// <summary>
    /// 按「地图种子 + 地块坐标」为每个槽位采样一次 XZ 偏移并固定。
    /// 四角锚点按地块最小尺寸的 32.5% 布置，抖动半径为锚点半径的 23%，
    /// 因此最终偏移长度约落在 25%~40% 范围内；中心锚点固定为零偏移、不抖动。
    /// </summary>
    public void Bake(int mapSeed, Vector3 hexCoordinate, float cellWidth, float cellDepth)
    {
        int q = (int)hexCoordinate.x;
        int r = (int)hexCoordinate.y;
        int s = (int)hexCoordinate.z;

        float unit = Mathf.Max(0.0001f, Mathf.Min(cellWidth, cellDepth));
        // 四角锚点 32.5% + 抖动 23%（=d*0.23）：最终偏移长度落在 [25%, 40%] 带内，
        // 满足“落点偏移 = 地块尺寸 25% ~ 40%”的观感要求（中心槽仍为 0 偏移）。
        float anchorRadius = unit * 0.325f;
        float jitterRadius = anchorRadius * 0.23f;

        for (int slotId = 0; slotId < SlotCount; slotId++)
        {
            UnitSlotData slot = _slots[slotId];
            slot.Disabled = false;

            if (slotId == CenterSlotId)
            {
                slot.LocalOffset = Vector3.zero;
                continue;
            }

            Vector2 dir = BaseAnchors[slotId].normalized;
            Vector2 basePos = dir * anchorRadius;

            int rnd1 = SeedService.DeriveCellSeed(SeedService.UnitSlotModule, mapSeed, q, r, s, slotId);
            int rnd2 = SeedService.DeriveCellSeed(SeedService.UnitSlotModule, mapSeed, r, s, q, slotId + SlotCount);

            float angle = (rnd1 / (float)int.MaxValue) * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(rnd2 / (float)int.MaxValue) * jitterRadius;
            Vector2 jitter = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            slot.LocalOffset = new Vector3(basePos.x + jitter.x, 0f, basePos.y + jitter.y);
        }
    }

    /// <summary>生成期碰撞校验失败时标记该槽永久不可用（有效容量 -1）。</summary>
    public void DisableSlot(int slotId)
    {
        if (slotId >= 0 && slotId < SlotCount)
            _slots[slotId].Disabled = true;
    }

    /// <summary>整格不可站立（水域/有效山体等不可通行格）。</summary>
    public void DisableAll()
    {
        for (int i = 0; i < SlotCount; i++)
            _slots[i].Disabled = true;
    }

    /// <summary>恢复整格站位容量（动态地形清除后使用）。</summary>
    public void EnableAll()
    {
        for (int i = 0; i < SlotCount; i++)
            _slots[i].Disabled = false;
    }

    // ── 容量 ────────────────────────────────────────────────────
    public int EffectiveCapacity
    {
        get
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++)
                if (!_slots[i].Disabled) n++;
            return n;
        }
    }

    public int StandingCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].OccupiedBy != null) n++;
            return n;
        }
    }

    public int ReservedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].ReservedBy != null) n++;
            return n;
        }
    }

    public int FreeStandingCount => EffectiveCapacity - StandingCount;
    public bool HasFreeStandingSlot() => FreeStandingCount > 0;
    public bool IsFullyOccupied() => FreeStandingCount <= 0;

    /// <summary>是否存在空闲「站位+预留」槽（供移动任务预留判断，排除 disabled/占用/预留）。</summary>
    public bool HasFreeSlotForReservation() => FreeReservationSlotCount > 0;

    public int FreeReservationSlotCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                UnitSlotData slot = _slots[i];
                if (!slot.Disabled && slot.OccupiedBy == null && slot.ReservedBy == null) n++;
            }
            return n;
        }
    }

    public bool HasAnyReservation()
    {
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i].ReservedBy != null) return true;
        return false;
    }

    public bool IsSlotReserved(int slotId) =>
        slotId >= 0 && slotId < SlotCount && _slots[slotId].ReservedBy != null;

    // ── 站位（StandingSlot）─────────────────────────────────────
    /// <summary>为单位取得一个站位槽（不按几何，取首个空槽，用于部署/出生）。</summary>
    public bool TryAcquireStandingSlot(GameObject unit, Vector3 centerWorld, out int slotId, out Vector3 worldPos)
    {
        return TryAcquireStandingSlot(unit, centerWorld, centerWorld, out slotId, out worldPos, centerWorld, preferLine: false);
    }

    /// <summary>
    /// 为单位取得站位槽。preferLine=true 时按 4.1 几何规则：取「fromWorld→toWorld」连线（XZ）最近的空槽。
    /// 已站在本格某槽则幂等返回同一槽。
    /// </summary>
    public bool TryAcquireStandingSlot(
        GameObject unit,
        Vector3 fromWorld,
        Vector3 toWorld,
        out int slotId,
        out Vector3 worldPos,
        Vector3 centerWorld,
        bool preferLine)
    {
        slotId = -1;
        worldPos = centerWorld;
        if (unit == null) return false;

        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i].OccupiedBy == unit)
            {
                slotId = i;
                worldPos = GetWorldPosition(i, centerWorld);
                return true;
            }
        }

        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < SlotCount; i++)
        {
            UnitSlotData slot = _slots[i];
            if (slot.Disabled || slot.OccupiedBy != null) continue;

            if (!preferLine)
            {
                best = i;
                break; // 部署/出生：首个空槽即可
            }

            float score = DistanceToLineXZ(GetWorldPosition(i, centerWorld), fromWorld, toWorld);
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best == -1) return false;

        _slots[best].OccupiedBy = unit;
        slotId = best;
        worldPos = GetWorldPosition(best, centerWorld);
        return true;
    }

    /// <summary>占据指定槽位（取消移动恢复到起点槽时使用）。槽被其他单位占用或 disabled 则失败。</summary>
    public bool TryAcquireStandingSlotAt(GameObject unit, int slotId, Vector3 centerWorld, out Vector3 worldPos)
    {
        worldPos = centerWorld;
        if (unit == null || slotId < 0 || slotId >= SlotCount) return false;
        UnitSlotData slot = _slots[slotId];
        if (slot.Disabled) return false;
        if (slot.OccupiedBy != null && slot.OccupiedBy != unit) return false;
        slot.OccupiedBy = unit;
        worldPos = GetWorldPosition(slotId, centerWorld);
        return true;
    }

    public void ReleaseStandingSlot(GameObject unit)
    {
        if (unit == null) return;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i].OccupiedBy == unit)
            {
                _slots[i].OccupiedBy = null;
                return;
            }
        }
    }

    // ── 预留（ReservedSlot）─────────────────────────────────────
    /// <summary>为移动任务预留一个「站位+预留」均空闲的槽，按几何选点。幂等：同一任务重复预留返回同一槽。</summary>
    public bool TryReserveSlot(IUnitMovement unit, Vector3 fromWorld, Vector3 toWorld, Vector3 centerWorld, out int slotId, out Vector3 worldPos)
    {
        slotId = -1;
        worldPos = centerWorld;
        if (unit == null) return false;

        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i].ReservedBy == unit)
            {
                slotId = i;
                worldPos = GetWorldPosition(i, centerWorld);
                return true;
            }
        }

        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < SlotCount; i++)
        {
            UnitSlotData slot = _slots[i];
            if (slot.Disabled || slot.OccupiedBy != null || slot.ReservedBy != null) continue;

            float score = DistanceToLineXZ(GetWorldPosition(i, centerWorld), fromWorld, toWorld);
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best == -1) return false;

        _slots[best].ReservedBy = unit;
        slotId = best;
        worldPos = GetWorldPosition(best, centerWorld);
        return true;
    }

    public void ReleaseReservation(IUnitMovement unit)
    {
        if (unit == null) return;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i].ReservedBy == unit)
                _slots[i].ReservedBy = null;
        }
    }

    public void ReleaseReservation(int slotId)
    {
        if (slotId >= 0 && slotId < SlotCount)
            _slots[slotId].ReservedBy = null;
    }

    /// <summary>把预留槽转为站位槽（单位抵达该格时调用）。</summary>
    public bool PromoteReservationToStanding(IUnitMovement unit, int slotId, GameObject unitObject, Vector3 centerWorld, out Vector3 worldPos)
    {
        worldPos = centerWorld;
        if (slotId < 0 || slotId >= SlotCount) return false;
        UnitSlotData slot = _slots[slotId];
        if (slot.Disabled || (slot.OccupiedBy != null && slot.OccupiedBy != unitObject)) return false;

        slot.OccupiedBy = unitObject;
        slot.ReservedBy = null;
        worldPos = GetWorldPosition(slotId, centerWorld);
        return true;
    }

    // ── 查询 ────────────────────────────────────────────────────
    public Vector3 GetWorldPosition(int slotId, Vector3 centerWorld)
    {
        if (slotId < 0 || slotId >= SlotCount) return centerWorld;
        return centerWorld + _slots[slotId].LocalOffset;
    }

    public int GetStandingSlot(GameObject unit)
    {
        if (unit == null) return -1;
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i].OccupiedBy == unit) return i;
        return -1;
    }

    public Vector3? GetStandingWorldPosition(GameObject unit, Vector3 centerWorld)
    {
        int slotId = GetStandingSlot(unit);
        if (slotId < 0) return null;
        return GetWorldPosition(slotId, centerWorld);
    }

    public void GetStandingUnits(List<GameObject> result)
    {
        if (result == null) return;
        for (int i = 0; i < SlotCount; i++)
        {
            GameObject u = _slots[i].OccupiedBy;
            if (u != null && !result.Contains(u))
                result.Add(u);
        }
    }

    // ── 几何 ────────────────────────────────────────────────────
    private static float DistanceToLineXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        ab.y = 0f;
        Vector3 ap = p - a;
        ap.y = 0f;

        if (ab.sqrMagnitude < 1e-6f)
            return ap.magnitude;

        float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
        Vector3 closest = a + ab * t;
        Vector3 d = p - closest;
        d.y = 0f;
        return d.magnitude;
    }
}
