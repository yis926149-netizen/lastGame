using System.Linq;
using UnityEngine;
using Zenject;

//****************************************
// 【动态地图-阶段五】调试热键组件（MapMutationDebugHotkeys）
// 开发辅助（§18.5-2 调试命令的运行时替代）：当前环境无 MCP 桥接（execute_script 不可用），
// 用热键在 Play 模式直接驱动诊断开关与竞技场触发，验证"地图动态重建"三项指标：
//  1. 局部重建范围（脏 Chunk 数 vs 全图 Chunk 数，F8 提交日志）
//  2. 无泄漏（连续 Activate→Destroy→Activate 后 Mesh/材质数不增长，F11 统计）
//  3. 迷雾/单位/标签联动（F10 立即突起 + F9 脏 Chunk 高亮目视）
// 仅开发期存在；对局逻辑零侵入（只切静态诊断开关、调用既有 ActivateNow）。
//****************************************

public class MapMutationDebugHotkeys : MonoBehaviour, ITickable
{
    [InjectOptional] private ArenaEventManager _arena;
    [InjectOptional] private MapMutationService _mutationService;

    public void Tick()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            MapMutationDiagnostics.EnableCommitLogging = !MapMutationDiagnostics.EnableCommitLogging;
            Debug.Log($"[MapMutationDebugHotkeys] EnableCommitLogging = {MapMutationDiagnostics.EnableCommitLogging}" +
                      "（提交后打印补丁/脏格/脏 Chunk 数/耗时）");
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            MapMutationDiagnostics.EnableDirtyChunkHighlight = !MapMutationDiagnostics.EnableDirtyChunkHighlight;
            if (!MapMutationDiagnostics.EnableDirtyChunkHighlight)
                _mutationService?.ClearDirtyChunkHighlight();
            Debug.Log($"[MapMutationDebugHotkeys] EnableDirtyChunkHighlight = {MapMutationDiagnostics.EnableDirtyChunkHighlight}");
        }

        if (Input.GetKeyDown(KeyCode.F10))
        {
            if (_arena == null)
            {
                Debug.LogWarning("[MapMutationDebugHotkeys] 未注入 ArenaEventManager，F10 无效。");
            }
            else if (_arena.State == ArenaEventManager.ArenaState.Reserved)
            {
                _arena.ActivateNow();
            }
            else
            {
                Debug.LogWarning($"[MapMutationDebugHotkeys] 竞技场当前状态 {_arena.State}，F10 仅在 Reserved 状态生效。");
            }
        }

        if (Input.GetKeyDown(KeyCode.F11))
        {
            LogRuntimeResourceCounts();
        }
    }

    /// <summary>打印运行时 Mesh/材质/Renderer 计数（泄漏检查：记录基线后连续 Activate→Destroy→Activate，数不应增长）。</summary>
    private void LogRuntimeResourceCounts()
    {
        var meshes = Resources.FindObjectsOfTypeAll<Mesh>();
        var materials = Resources.FindObjectsOfTypeAll<Material>();
        int activeMeshRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        int activeColliders = Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        Debug.Log($"[MapMutationDebugHotkeys] 资源统计：Mesh 总数 {meshes.Length}" +
                  $"（运行时实例 {meshes.Count(m => !AssetDatabaseHelper.IsAsset(m))}）、" +
                  $"Material 总数 {materials.Length}、活动 MeshRenderer {activeMeshRenderers}、活动 MeshCollider {activeColliders}");
    }
}

/// <summary>帮助判断对象是否为资产（Resources.FindObjectsOfTypeAll 混入工程资产，泄漏统计只关心运行时实例）。</summary>
internal static class AssetDatabaseHelper
{
    internal static bool IsAsset(Object obj)
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.Contains(obj);
#else
        return false;
#endif
    }
}
