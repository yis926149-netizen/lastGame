using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
// 【动态地图-阶段五】管线通用性演示命令（MapMutationDemoCommands）
// 验证目标（§阶段五-验证）：任意新地图变化事件接入同一条管线即达标。
// 本类提供两个"新事件"示例（洪水淹没 / 地震抬升），全部经 MapMutationService
// （BeginTransaction → Apply(HexCellPatch) → Commit）走同一条通用管线，
// 与竞技场共用 MapMutationService / HexCellPatch / 事务协议 / 分帧提交 / 动画能力。
// 供 execute_script / 调试调用：参数传 DiContainer（如 ProjectContext.Instance.Container）。
//****************************************

public static class MapMutationDemoCommands
{
    /// <summary>
    /// 演示事件①：洪水——把中心格周围 radius 环内全部压到海平面以下（Height ≤ seaLevel），
    /// 清河流/地貌/资源；水域判定由 WaterLevelConfig 自动处理（§8 双向重置）。
    /// </summary>
    public static void RunFloodDemo(DiContainer container, int radius = 2, float duration = 0f)
    {
        var mapData = container.Resolve<IMapDataService>();
        var mutation = container.Resolve<MapMutationService>();
        var config = container.Resolve<MapGenerationConfigSO>();

        HexCellData center = FindCenterCell(mapData, config);
        if (center == null)
        {
            Debug.LogError("[MapMutationDemoCommands] 找不到地图中心格，洪水演示终止。");
            return;
        }

        var targets = new List<HexCellData>();
        foreach (HexCellData cell in mapData.GetAllCells())
        {
            if (cell != null && CubeDistance(cell.HexCoordinate, center.HexCoordinate) <= radius)
                targets.Add(cell);
        }

        mutation.BeginTransaction();
        foreach (HexCellData cell in targets)
        {
            mutation.Apply(cell, new HexCellPatch
            {
                HasHeight = true,
                Height = config.seaLevel,          // ≤ seaLevel → 水（§8 水陆双向重置）
                ClearRiver = true,
                ClearLandForm = true,
                ClearResource = true
            });
        }
        mutation.Commit(new MapTransitionOptions { Duration = duration });

        Debug.Log($"[MapMutationDemoCommands] 洪水演示完成：{targets.Count} 格沉入水下（半径 {radius}，中心 {center.HexCoordinate}）。");
    }

    /// <summary>
    /// 演示事件②：地震抬升——把中心格周围 radius 环内整体抬升 liftLevels 层（保留陆/水判定），
    /// 清河流/地貌/资源，边界环可设为不可通行（模拟塌陷断壁）。
    /// </summary>
    public static void RunEarthquakeDemo(DiContainer container, int radius = 2, int liftLevels = 2, bool wallImpassable = false, float duration = 0f)
    {
        var mapData = container.Resolve<IMapDataService>();
        var mutation = container.Resolve<MapMutationService>();
        var config = container.Resolve<MapGenerationConfigSO>();

        HexCellData center = FindCenterCell(mapData, config);
        if (center == null)
        {
            Debug.LogError("[MapMutationDemoCommands] 找不到地图中心格，地震演示终止。");
            return;
        }

        mutation.BeginTransaction();
        int count = 0;
        foreach (HexCellData cell in mapData.GetAllCells())
        {
            if (cell == null) continue;
            int distance = CubeDistance(cell.HexCoordinate, center.HexCoordinate);
            if (distance > radius) continue;

            bool isOuterRing = distance == radius;
            var patch = new HexCellPatch
            {
                HasHeight = true,
                Height = cell.Height + liftLevels,
                ClearRiver = true,
                ClearLandForm = true,
                ClearResource = true
            };
            if (isOuterRing && wallImpassable)
            {
                patch.HasMovementCost = true;
                patch.MovementCost = float.MaxValue;
            }
            mutation.Apply(cell, patch);
            count++;
        }
        mutation.Commit(new MapTransitionOptions { Duration = duration });

        Debug.Log($"[MapMutationDemoCommands] 地震演示完成：{count} 格抬升 {liftLevels} 层（半径 {radius}，中心 {center.HexCoordinate}）。");
    }

    /// <summary>
    /// 演示事件③：分帧洪水——同上但经 CommitSliced 分帧提交（每帧 maxChunksPerFrame 个 Chunk）。
    /// 验证分帧提交管线（阶段五-分帧提交）。
    /// </summary>
    public static void RunFloodDemoSliced(DiContainer container, int radius = 2, int maxChunksPerFrame = 2)
    {
        var mapData = container.Resolve<IMapDataService>();
        var mutation = container.Resolve<MapMutationService>();
        var config = container.Resolve<MapGenerationConfigSO>();

        HexCellData center = FindCenterCell(mapData, config);
        if (center == null)
        {
            Debug.LogError("[MapMutationDemoCommands] 找不到地图中心格，分帧洪水演示终止。");
            return;
        }

        mutation.BeginTransaction();
        int count = 0;
        foreach (HexCellData cell in mapData.GetAllCells())
        {
            if (cell != null && CubeDistance(cell.HexCoordinate, center.HexCoordinate) <= radius)
            {
                mutation.Apply(cell, new HexCellPatch
                {
                    HasHeight = true,
                    Height = config.seaLevel,
                    ClearRiver = true,
                    ClearLandForm = true,
                    ClearResource = true
                });
                count++;
            }
        }
        var result = mutation.CommitSliced(new MapTransitionOptions { Duration = 0f }, maxChunksPerFrame);
        Debug.Log($"[MapMutationDemoCommands] 分帧洪水已启动：{count} 格沉入水下，CommitId={result?.CommitId}（几何由 MapSlicedCommitExecutor 逐帧构建）。");
    }

    private static HexCellData FindCenterCell(IMapDataService mapData, MapGenerationConfigSO config)
    {
        int centerZ = config.zNumber / 2;
        int centerX = config.xNumber / 2;
        HexCellData cell = mapData.GetCell(centerZ * config.xNumber + centerX);
        if (cell == null)
        {
            var all = mapData.GetAllCells();
            if (all != null && all.Count > 0)
                return all[all.Count / 2];
        }
        return cell;
    }

    private static int CubeDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = a - b;
        return (int)((Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z)) * 0.5f);
    }
}
