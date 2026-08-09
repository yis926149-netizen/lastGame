using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
// 单格高亮渲染器：Chunk 地图的统一高亮入口。
// 按需高亮的格生成小型动态 mesh（六边形轮廓线 + 半透明填充），多通道互不干扰。
// 玩家输入（拖拽高亮 PlayerInputHandler.cs:93-124）与 UI（可达高亮 UIController.cs:369-400）
// 调用方提交"高亮格集合"，不依赖逐格 GameObject。
//****************************************

public enum HexHighlightChannel
{
    CardPlacement = 0,
    Reachable = 1,
    AttackRange = 2,
    Selection = 3,
    DebugDirtyChunk = 4
}

public sealed class HexHighlightRenderer : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_Color");

    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private IMapDataService _mapDataService;

    private readonly Dictionary<HexHighlightChannel, ChannelState> _channels =
        new Dictionary<HexHighlightChannel, ChannelState>();

    private sealed class ChannelState
    {
        public GameObject Root;
        public Mesh Mesh;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public Material Material;
        public readonly HashSet<HexCellData> Cells = new HashSet<HexCellData>();
    }

    private IReadOnlyMapView _view;

    private void EnsureInitialized()
    {
        if (_view != null) return;
        _view = new MapDataReadOnlyView(_mapDataService);
    }

    /// <summary>
    /// 提交一个通道的高亮格集合（整体替换）。cells 为空 = 清空该通道。
    /// 【程序化山脉-阶段6.4】默认门禁：有效山格（IsEffectiveMountainCell）被过滤，
    /// 不会产出埋进山体的基础地表高亮（决策 ⑨）；DebugDirtyChunk 诊断通道豁免。
    /// 动态水→陆/清除恢复后，下一次刷新按当前格状态重新过滤（整体替换语义）。
    /// </summary>
    public void SetHighlightedCells(HexHighlightChannel channel, IReadOnlyCollection<HexCellData> cells, Color color)
    {
        SetHighlightedCellsInternal(channel, cells, color, filterMountain: true);
    }

    /// <summary>
    /// 显式诊断豁免入口（阶段6.4）：跳过山格门禁。
    /// 仅供开发调试工具（如 MapHeightEditTestController）使用；玩家可见通道禁止调用，
    /// 避免调试高亮被玩法门禁静默吞掉，也不允许调试豁免泄漏到玩家 UI。
    /// </summary>
    public void SetHighlightedCellsDiagnostic(HexHighlightChannel channel, IReadOnlyCollection<HexCellData> cells, Color color)
    {
        SetHighlightedCellsInternal(channel, cells, color, filterMountain: false);
    }

    /// <summary>
    /// 【程序化山脉-阶段6.4】山格高亮门禁纯函数：该格是否应被玩家可见通道过滤。
    /// 玩家可见通道（CardPlacement/Reachable/AttackRange/Selection）过滤有效山格（决策 ⑨），
    /// 诊断通道 DebugDirtyChunk 豁免。过滤依据 = MountainCellRule.IsEffectiveMountainCell
    /// （统一口径，不复制 landForm 判断）；水淹/清除后自动放行，恢复后重新拦截。
    /// </summary>
    public static bool IsBlockedByMountainGate(HexHighlightChannel channel, HexCellData cell)
    {
        return channel != HexHighlightChannel.DebugDirtyChunk
            && MountainCellRule.IsEffectiveMountainCell(cell);
    }

    private void SetHighlightedCellsInternal(HexHighlightChannel channel, IReadOnlyCollection<HexCellData> cells, Color color, bool filterMountain)
    {
        EnsureInitialized();
        ChannelState state = GetOrCreateChannel(channel);
        state.Cells.Clear();
        if (cells != null)
        {
            foreach (HexCellData cell in cells)
            {
                if (cell == null) continue;
                if (filterMountain && IsBlockedByMountainGate(channel, cell))
                    continue;
                state.Cells.Add(cell);
            }
        }
        state.Material.color = color;
        RebuildChannel(state);
    }

    /// <summary>清空指定通道。</summary>
    public void ClearChannel(HexHighlightChannel channel)
    {
        if (!_channels.TryGetValue(channel, out ChannelState state)) return;
        state.Cells.Clear();
        RebuildChannel(state);
    }

    /// <summary>清空全部通道。</summary>
    public void ClearAll()
    {
        foreach (HexHighlightChannel channel in System.Enum.GetValues(typeof(HexHighlightChannel)))
            ClearChannel(channel);
    }

    private ChannelState GetOrCreateChannel(HexHighlightChannel channel)
    {
        if (_channels.TryGetValue(channel, out ChannelState state)) return state;

        state = new ChannelState();
        state.Root = new GameObject($"HexHighlight_{channel}");
        state.Root.transform.SetParent(transform, false);
        state.Filter = state.Root.AddComponent<MeshFilter>();
        state.Renderer = state.Root.AddComponent<MeshRenderer>();
        state.Mesh = new Mesh { name = $"HexHighlight_{channel}_Mesh" };
        state.Filter.sharedMesh = state.Mesh;
        state.Material = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
        state.Material.color = Color.yellow;
        state.Renderer.sharedMaterial = state.Material;
        _channels[channel] = state;
        return state;
    }

    private void RebuildChannel(ChannelState state)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        int vertexOffset = 0;

        foreach (HexCellData cell in state.Cells)
        {
            CellBuildContext ctx = MakeBuildContext(cell);
            // 外圈 6 顶点（与网格线共用构建）：生成六边形轮廓 + 上表面填充
            List<Vector3> gridVerts = _meshGenerator.BuildGridVertices(ctx);
            if (gridVerts == null || gridVerts.Count < 12) continue;

            // 外圈 6 点（网格线前 6 个顶点即外圈）
            for (int i = 0; i < 6; i++)
            {
                verts.Add(gridVerts[i] + Vector3.up * 0.05f);
                uvs.Add(Vector2.zero);
            }
            // 六边形上表面：中心 + 外圈 → 6 个三角形
            Vector3 center = cell.RealCenterWorldCoordinate + Vector3.up * 0.05f;
            verts.Add(center);
            uvs.Add(Vector2.zero);
            int centerIndex = vertexOffset + 6;
            for (int i = 0; i < 6; i++)
            {
                tris.Add(centerIndex);
                tris.Add(vertexOffset + i);
                tris.Add(vertexOffset + (i + 1) % 6);
            }
            vertexOffset += 7;
        }

        state.Mesh.Clear();
        state.Mesh.vertices = verts.ToArray();
        state.Mesh.uv = uvs.ToArray();
        state.Mesh.triangles = tris.ToArray();
        state.Mesh.RecalculateNormals();
        state.Mesh.RecalculateBounds();
        state.Root.SetActive(verts.Count > 0);
    }

    private CellBuildContext MakeBuildContext(HexCellData cell)
    {
        SolidAreaMeshData solid = _meshGenerator.BuildSolidArea(cell, _view);
        var solids = new Dictionary<int, Vector3[]>
        {
            [cell.GenerateOrder] = solid.Vertices
        };
        return new CellBuildContext
        {
            Cell = cell,
            View = _view,
            Solid = solid.Vertices,
            Solids = solids,
            LakeOrSeas = new Dictionary<int, Vector3[]>(),
            RectVertices = new Dictionary<(int, Enums.HexDirection), List<Vector3>>(),
            InterpCount = cell.interpCount
        };
    }
}
