using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
// 【动态地图-阶段三】单格高亮渲染器（§二十-4：HexHighlightRenderer 替代 cell.GridMesh）。
// 按需高亮的格生成小型动态 mesh（六边形轮廓线 + 半透明填充），多通道互不干扰。
// 玩家输入（拖拽高亮 PlayerInputHandler.cs:93-124）与 UI（可达高亮 UIController.cs:369-400）
// 一律改为提交"高亮格集合"，不再直接操作 cell.GridMesh。
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

    /// <summary>提交一个通道的高亮格集合（整体替换）。cells 为空 = 清空该通道。</summary>
    public void SetHighlightedCells(HexHighlightChannel channel, IReadOnlyCollection<HexCellData> cells, Color color)
    {
        EnsureInitialized();
        ChannelState state = GetOrCreateChannel(channel);
        state.Cells.Clear();
        if (cells != null)
        {
            foreach (HexCellData cell in cells)
            {
                if (cell != null) state.Cells.Add(cell);
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
        return new CellBuildContext
        {
            Cell = cell,
            View = _view,
            Solids = new Dictionary<int, Vector3[]>(),
            LakeOrSeas = new Dictionary<int, Vector3[]>(),
            RectVertices = new Dictionary<(int, Enums.HexDirection), List<Vector3>>(),
            InterpCount = cell.interpCount
        };
    }
}
