using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【程序化山脉】阶段 7.8：性能与资源生命周期基线采集工具。
/// 菜单 Tools/程序化山脉/性能基线（自动进入 PlayMode）：
///  - 自动进入 PlayMode（已处于 PlayMode 时直接采集），等待初始地图生成完成；
///  - 采集 Chunk 构建耗时（含山/无山分类，ChunkMapRenderer 静态钩子，默认关闭零开销）、
///    collision cooking（提交）次数、CPU 顶点动画单帧写入耗时（本次未触发时提示手动采样）、
///    全图渲染/碰撞 mesh 顶点三角总量、山体稳定/Transition 材质实例数；
///  - 写报告到 Temp/mountain_perf_report.txt 并输出 Console 摘要，随后自动退出 PlayMode。
/// 单格 solid 扇顶点预算断言（=54）由 MountainStage7PerformanceContractTests 锁住。
/// 动画成本项需要运行期触发：进入 PlayMode 后按 R/F 抬升地形，动画结束后再次点击本菜单采集。
/// </summary>
public static class MountainPerformanceBaseline
{
    private const string ReportPath = "Temp/mountain_perf_report.txt";

    private const double StableFramesBeforeCollect = 90;   // 计数连续稳定帧数（≈1.5s）
    private const double TimeoutSeconds = 120;             // 硬超时，防止编辑器卡死在等待态

    private static int _stableFrames;
    private static long _lastBuildCount;
    private static double _startedAt;

    [MenuItem("Tools/程序化山脉/性能基线（自动进入 PlayMode）")]
    public static void RunBaseline()
    {
        if (EditorApplication.isPlaying)
        {
            CollectAndReport();
            return;
        }
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        _startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.isPlaying = true;
        Debug.Log("[性能基线] 已请求进入 PlayMode，等待地图生成完成后自动采集并写 Temp/mountain_perf_report.txt。");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            ResetCounters();
            ChunkMapRenderer.EnableChunkBuildTiming = true;
            _lastBuildCount = 0;
            _stableFrames = 0;
            _startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }
    }

    private static void ResetCounters()
    {
        ChunkMapRenderer.ChunkBuildCount = 0;
        ChunkMapRenderer.MountainChunkBuildCount = 0;
        ChunkMapRenderer.ChunkBuildMsTotal = 0d;
        ChunkMapRenderer.MountainChunkBuildMsTotal = 0d;
        ChunkMapRenderer.CollisionCommitCount = 0;
        ChunkMapRenderer.AnimProgressFrameCount = 0;
        ChunkMapRenderer.AnimProgressFrameMsTotal = 0d;
        ChunkMapRenderer.AnimProgressFrameMsMax = 0d;
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - _startedAt > TimeoutSeconds)
        {
            Debug.LogWarning("[性能基线] 等待地图生成超时，按当前状态输出报告。");
            CollectAndReport();
            return;
        }

        long count = ChunkMapRenderer.ChunkBuildCount;
        if (count == 0) return;                       // 地图尚未开始构建

        if (count != _lastBuildCount)
        {
            _lastBuildCount = count;
            _stableFrames = 0;
            return;
        }
        if (++_stableFrames < StableFramesBeforeCollect) return;
        CollectAndReport();
    }

    private static void CollectAndReport()
    {
        EditorApplication.update -= Tick;

        var sb = new StringBuilder();
        sb.AppendLine("==== 程序化山脉性能基线（固定 seed 场景）====");
        sb.AppendLine($"采集时间：{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // ── Chunk 构建耗时（阶段 7.8 第 1 项）───────────────
        long totalCount = ChunkMapRenderer.ChunkBuildCount;
        long mountainCount = ChunkMapRenderer.MountainChunkBuildCount;
        long plainCount = totalCount - mountainCount;
        double totalMs = ChunkMapRenderer.ChunkBuildMsTotal + ChunkMapRenderer.MountainChunkBuildMsTotal;
        sb.AppendLine("[Chunk 构建耗时]");
        sb.AppendLine($"  总 Chunk 构建：{totalCount}（含山 {mountainCount} / 无山 {plainCount}）");
        if (totalCount > 0)
        {
            sb.AppendLine($"  总耗时 {totalMs:F2} ms，平均 {(totalMs / totalCount):F3} ms/Chunk");
            if (mountainCount > 0)
                sb.AppendLine($"  含山 Chunk 平均 {(ChunkMapRenderer.MountainChunkBuildMsTotal / mountainCount):F3} ms/Chunk");
            if (plainCount > 0)
                sb.AppendLine($"  无山 Chunk 平均 {(ChunkMapRenderer.ChunkBuildMsTotal / plainCount):F3} ms/Chunk");
        }
        sb.AppendLine($"  山体替换式构建拖慢评估：含山平均 / 无山平均 比值见上（构建路径含替换式拓扑 + 独立碰撞）。");

        // ── 碰撞 cooking（阶段 7.8 第 4 项）──────────────────
        sb.AppendLine("[碰撞 cooking]");
        sb.AppendLine($"  collision mesh 提交（cooking）次数 = {ChunkMapRenderer.CollisionCommitCount}（应 = 含山 Chunk 提交次数，不逐帧 cooking）");

        // ── CPU 顶点动画帧耗时（阶段 7.8 第 5 项）────────────
        sb.AppendLine("[CPU 顶点动画]");
        if (ChunkMapRenderer.AnimProgressFrameCount > 0)
        {
            double avg = ChunkMapRenderer.AnimProgressFrameMsTotal / ChunkMapRenderer.AnimProgressFrameCount;
            sb.AppendLine($"  采样 {ChunkMapRenderer.AnimProgressFrameCount} 帧，平均 {avg:F3} ms/帧，最大 {ChunkMapRenderer.AnimProgressFrameMsMax:F3} ms/帧");
        }
        else
        {
            sb.AppendLine("  本次基线未触发动画（计数 = 0）。手动采样：进入 PlayMode 后按 R/F 抬升地形，动画结束后再次点击本菜单。");
        }

        // ── 全图 mesh 面数与材质实例（阶段 7.8 第 2/3 项）─────
        CollectMeshInventory(sb);

        // ── 顶点预算（阶段 7.8 第 2 项）─────────────────────
        sb.AppendLine("[顶点预算]");
        sb.AppendLine($"  单格山体 solid 扇 flat 拆分顶点预算常量 = {MountainGeometryBuilder.SolidMountainFanVertexCount}（18 面 × 3，决策 ㉛ 断言）；576 格地图全图总量见上。");

        sb.AppendLine("[备注]");
        sb.AppendLine("  · 材质实例稳定性（重复重建/切场景不增长）与 GC 粗查需人工复核（7.8 完成标准）。");
        sb.AppendLine("  · 阴影/雾化/镜头预算等视觉项见阶段 7.7 截图验收。");

        File.WriteAllText(ReportPath, sb.ToString());
        Debug.Log($"[性能基线] 报告已写入 {ReportPath}\n" + sb);

        EditorApplication.isPlaying = false;
    }

    private static void CollectMeshInventory(StringBuilder sb)
    {
        ChunkMapRenderer[] renderers = Object.FindObjectsOfType<ChunkMapRenderer>();
        sb.AppendLine("[全图 mesh 与材质实例]");
        if (renderers.Length == 0)
        {
            sb.AppendLine("  场景中未找到 ChunkMapRenderer（可能未加载地图场景，统计为空）。");
            return;
        }

        long renderVerts = 0, renderTris = 0, collisionVerts = 0, collisionTris = 0;
        int chunkTotal = 0, mountainChunks = 0, collisionChunks = 0;
        int mountainStableInstances = 0, mountainTransitionInstances = 0;
        int renderersWithMountainMaterial = 0;

        foreach (ChunkMapRenderer renderer in renderers)
        {
            bool rendererHasMountainMaterial = false;
            foreach (ChunkRenderData chunk in renderer.DebugChunks)
            {
                chunkTotal++;
                if (chunk.LastMountainTopology.HasMountain) mountainChunks++;

                Mesh renderMesh = chunk.TerrainFilter != null ? chunk.TerrainFilter.sharedMesh : null;
                if (renderMesh != null)
                {
                    renderVerts += renderMesh.vertexCount;
                    renderTris += renderMesh.triangles.Length / 3;
                }

                Mesh collisionMesh = chunk.TerrainCollider != null ? chunk.TerrainCollider.sharedMesh : null;
                if (collisionMesh != null)
                {
                    collisionChunks++;
                    collisionVerts += collisionMesh.vertexCount;
                    collisionTris += collisionMesh.triangles.Length / 3;
                }

                if (chunk.TerrainRenderer != null && chunk.TerrainRenderer.sharedMaterials != null)
                {
                    foreach (Material mat in chunk.TerrainRenderer.sharedMaterials)
                    {
                        if (mat == null || mat.shader == null) continue;
                        if (mat.shader.name == MountainMaterialContract.StableShaderName)
                        {
                            mountainStableInstances++;
                            rendererHasMountainMaterial = true;
                        }
                        else if (mat.shader.name == MountainMaterialContract.TransitionShaderName)
                        {
                            mountainTransitionInstances++;
                        }
                    }
                }
            }
            if (rendererHasMountainMaterial) renderersWithMountainMaterial++;
        }

        sb.AppendLine($"  Chunk 总数：{chunkTotal}；含山 Chunk：{mountainChunks}（按已提交拓扑签名）；挂独立碰撞 mesh：{collisionChunks}");
        sb.AppendLine($"  渲染 mesh 合计：{renderVerts} 顶点 / {renderTris} 三角；碰撞 mesh 合计：{collisionVerts} 顶点 / {collisionTris} 三角");
        sb.AppendLine($"  山体稳定材质实例：{mountainStableInstances}（期望 = 含山 Renderer 数 {renderersWithMountainMaterial}，每 Renderer 1 份）；Transition 实例：{mountainTransitionInstances}（动画期间才创建）");
        sb.AppendLine($"  每 Chunk mesh 预算：渲染 1 + 碰撞 Active/Staging 双缓冲 ≤ 3（无山 Chunk 碰撞回落渲染 mesh，无额外分配）");
    }
}
