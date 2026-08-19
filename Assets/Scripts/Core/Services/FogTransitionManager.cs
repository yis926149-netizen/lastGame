using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 迷雾过渡动画管理器：驱动 HexCellData.FogAlpha 从当前值平滑过渡到目标值。
/// 替代原先的瞬间 0/1 切换，实现逐渐消散/重聚效果。
/// </summary>
public class FogTransitionManager
{
    private static float _configuredSpeed = float.NaN;

    /// <summary>由 GameInstaller 在绑定阶段配置过渡速度（阶段6：Excel 唯一主源，未配置即抛异常）。</summary>
    public static void Configure(float speed)
    {
        _configuredSpeed = speed;
    }

    /// <summary>过渡速度（每秒改变量，0.5 = 2秒完成 0→1）。</summary>
    public float TransitionSpeed
    {
        get
        {
            if (float.IsNaN(_configuredSpeed))
                throw new System.InvalidOperationException(
                    "[FogTransition] 迷雾过渡速度未配置：请在 GameInstaller 调用 FogTransitionManager.Configure(...)。");
            return _configuredSpeed;
        }
    }

    // 当本帧有 alpha 值实际变化时设为 true，由 ChunkMapRenderer 在刷新视觉后清除。
    // 注意：Tick 内部只"置位"，从不"清位"——清位由 ClearDirty 在外部完成。
    // 这样保证最后一帧（过渡完成帧）的最终值也能被正确刷新到 GPU。
    private bool _dirty;

    /// <summary>本帧是否有 FogAlpha 发生变化，需要刷新视觉。</summary>
    public bool IsDirty => _dirty;

    private readonly HashSet<HexCellData> _transitioningCells = new HashSet<HexCellData>();

    /// <summary>
    /// 将一个 cell 加入过渡队列，设置其目标值。
    /// </summary>
    /// <param name="cell">要过渡的格子</param>
    /// <param name="targetAlpha">目标透明度（0=完全迷雾，1=完全清晰）</param>
    public void RequestTransition(HexCellData cell, float targetAlpha)
    {
        if (cell == null) return;

        float clamped = Mathf.Clamp01(targetAlpha);
        cell.FogAlphaTarget = clamped;

        // 已经在目标值，不需要过渡
        if (Mathf.Approximately(cell.FogAlpha, clamped))
        {
            cell.FogAlpha = clamped;
            return;
        }

        _transitioningCells.Add(cell);
    }

    /// <summary>
    /// 立即将 cell 的 FogAlpha 设为目标值（无过渡，用于开局初始化）。
    /// </summary>
    public void SnapTransition(HexCellData cell, float targetAlpha)
    {
        if (cell == null) return;

        float clamped = Mathf.Clamp01(targetAlpha);
        cell.FogAlphaTarget = clamped;
        cell.FogAlpha = clamped;
        _transitioningCells.Remove(cell);
        _dirty = true; // snap 也需要刷新
    }

    /// <summary>
    /// 每帧更新所有过渡中的 cell，推进 FogAlpha 向目标值靠近。
    /// 只要本帧有任何值变化就置 IsDirty=true（包括最后完成帧）。
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (_transitioningCells.Count == 0) return;

        float step = TransitionSpeed * deltaTime;
        var completed = new List<HexCellData>();

        foreach (var cell in _transitioningCells)
        {
            if (cell == null)
            {
                completed.Add(cell);
                continue;
            }

            float newValue = Mathf.MoveTowards(cell.FogAlpha, cell.FogAlphaTarget, step);
            cell.FogAlpha = newValue;
            _dirty = true; // 本帧有值变化，需要刷新

            if (Mathf.Approximately(newValue, cell.FogAlphaTarget))
            {
                cell.FogAlpha = cell.FogAlphaTarget; // 精确钳到目标值
                completed.Add(cell);
            }
        }

        foreach (var cell in completed)
            _transitioningCells.Remove(cell);
    }

    /// <summary>
    /// 清除脏标记（ChunkMapRenderer 在刷新完视觉后调用）。
    /// </summary>
    public void ClearDirty() => _dirty = false;

    /// <summary>当前仍在过渡中的 cell 数量（调试用）。</summary>
    public int TransitioningCount => _transitioningCells.Count;
}
