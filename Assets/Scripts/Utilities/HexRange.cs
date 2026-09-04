using System.Collections.Generic;
using UnityEngine;

//****************************************
// 战术卡影响范围 · n 环格枚举工具（先简单实现，未来重构再统一）。
//
// ⚠️ 本类是「先简单实现、未来重构再统一」的产物（见 战术卡影响范围遮罩-分步实施计划.md 事实 C）。
// 仓库内已知的同类重复实现，将来统一时一并处理：
//   n 环收集：ArrowTowerShooter.cs:248 CollectCellsInRange
//   距离公式（(|dx|+|dy|+|dz|)*0.5f）至少 10 处 private static：
//     UnitBrainBase.cs:615 / UIController.cs:389 / ArrowTowerShooter.cs:273 /
//     RangedStrategy.cs:192 / MeleeStrategy.cs:186 / UnitMovementSystem.cs:610 /
//     PublicBuildingGenerator.cs:429 / ArenaEventManager.cs:609 /
//     MapVisualTransitionService.cs:500 / RidgeGenerator.cs:570
// 本次刻意不动它们：无关改动会无谓扩大回归面。
//
// CollectInRange 用 BFS 逐环扩张（参照 ArrowTowerShooter.cs:250-270 的成熟结构），
// 而非「遍历全图 + 距离判定」——后者在触点移格时每次全扫全图，浪费。
// 内部用静态缓冲避免触点移格高频调用时的每帧 GC；代价是单线程非重入
//（Unity 主线程单点调用，满足约束；勿在并行/嵌套场景复用本类）。
//****************************************
public static class HexRange
{
    // BFS 复用缓冲（静态）：遮罩触点移格时高频调用，避免每帧产生 GC。
    // Frontier/Next 不能 readonly：逐环扩张时二者通过引用交换实现「上一环 → 下一环」的复用。
    private static List<HexCellData> Frontier = new List<HexCellData>();
    private static List<HexCellData> Next = new List<HexCellData>();
    private static readonly HashSet<HexCellData> Visited = new HashSet<HexCellData>();

    /// <summary>
    /// 立方距离（整数）。输入为立方坐标（HexCellData.HexCoordinate，Vector3，x+y+z=0）。
    /// 与仓库内至少 10 处 (|dx|+|dy|+|dz|)*0.5f 副本同式。
    /// </summary>
    public static int Distance(Vector3 a, Vector3 b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);
        float dz = Mathf.Abs(a.z - b.z);
        return Mathf.RoundToInt((dx + dy + dz) * 0.5f);
    }

    /// <summary>
    /// 中心格 + 半径 n 内全部格（含中心），纯坐标、无地形过滤；图外格由 mapData 自然裁掉。
    /// 结果写进 result（复用缓冲，不分配）。radius = 0 返回仅中心格；
    /// 调用方自行决定 n=0 不画遮罩（决策 8）。
    /// </summary>
    public static void CollectInRange(
        IMapDataService mapData, HexCellData center, int radius, List<HexCellData> result)
    {
        result.Clear();
        if (center == null) return;
        result.Add(center);
        if (radius <= 0 || mapData == null) return;

        Visited.Clear();
        Frontier.Clear();
        Next.Clear();
        Visited.Add(center);
        Frontier.Add(center);

        for (int ring = 1; ring <= radius; ring++)
        {
            Next.Clear();
            for (int f = 0; f < Frontier.Count; f++)
            {
                HexCellData cell = Frontier[f];
                for (int d = 0; d < 6; d++)
                {
                    HexCellData neighbor = mapData.GetNeighbor(cell, (Enums.HexDirection)d);
                    if (neighbor != null && Visited.Add(neighbor))
                    {
                        result.Add(neighbor);
                        Next.Add(neighbor);
                    }
                }
            }
            List<HexCellData> tmp = Frontier;
            Frontier = Next;
            Next = tmp;
        }
    }
}
