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

    // 【卡牌拖拽放置高亮-方案甲】绿/红语义色常量：可放置 = 亮绿，不可放置 = 柔和红。
    // 由 PlayerInputHandler 在拖牌时按 CanHighlightCellForCard 决定，供 CardPlacement 通道换色复用。
    // 可放置已由金色改亮绿（G 超 1 提亮、R/B 压沉成纯绿，去黄/青调）；红色退去粉调、更红更亮（G/B 压沉、R 微超 1 提亮）。
    // 第 4 个分量 a = 整体不透明度（shader 用 fill*_Color.a）：1=不透明（现状），<1 整层更透更弱、不改变色相与明度结构。
    public static readonly Color PlaceableGlowColor = new Color(0.2f, 1.15f, 0.16f, 1f);
    public static readonly Color UnplaceableGlowColor = new Color(1.1f, 0.12f, 0.10f, 1f); // 明度已调回原值（更红更亮）、不透明度 a=1（不透明）

    // 【方案乙】CardPlacement 能量围墙高度（世界单位）。六边形格 OuterRadius≈3.0（宽约 5.2），
    // 参考图为低矮发光结界而非全高箱体，故压到 ~0.7（方案乙 §4.2 意图 0.5~1.0）；真机再标定。
    private const float CardPlacementWallHeight = 0.7f;

    // 【方案乙】角柱光柱高度（世界单位）：高于围墙、向上渐隐，贴参考图"四角上升光柱"。
    // 高度与相机俯角强相关，真机定（见参考效果文档 §6.3）。
    private const float CardPlacementPillarHeight = 4.8f;

    // 【方案乙】角柱光柱单边半宽（世界单位）：软束的核心半宽。竖面已被"3 列中心亮、两侧羽化"
    // 细分（见 AppendGlowBeamQuad），此值只是核心亮柱的半径，两侧靠 shader 横向羽化，不再有硬边"纸片"感。
    private const float CardPlacementPillarHalfWidth = 0.36f;

    // 【方案乙】格心中央光柱（图三式"中心光束"）：比角柱更高更粗，向上渐隐。
    // 顶点色 a=128 触发 shader 的中央光柱分支（a=255 是角柱、a=0 是顶面/墙面）。
    private const float CardPlacementBeamHeight = 6.6f;
    private const float CardPlacementBeamHalfWidth = 0.54f;

    // 顶点色 a 通道掩码语义（shader 以 a>0.4 / a>0.9 分支）：角柱=255，中央光柱=128。
    private const byte PillarMaskByte = 255;
    private const byte BeamMaskByte = 128;

    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private IMapDataService _mapDataService;

    private readonly Dictionary<HexHighlightChannel, ChannelState> _channels =
        new Dictionary<HexHighlightChannel, ChannelState>();

    private sealed class ChannelState
    {
        public HexHighlightChannel Channel;
        public GameObject Root;
        public Mesh Mesh;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public Material Material;
        public ParticleSystem Sparks;
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
        UpdateSparksColor(state.Sparks, color);
        RebuildChannel(state);
        SyncSparks(state);
    }

    /// <summary>清空指定通道。</summary>
    public void ClearChannel(HexHighlightChannel channel)
    {
        if (!_channels.TryGetValue(channel, out ChannelState state)) return;
        state.Cells.Clear();
        RebuildChannel(state);
        SyncSparks(state);
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
        state.Channel = channel;
        state.Root = new GameObject($"HexHighlight_{channel}");
        state.Root.transform.SetParent(transform, false);
        state.Filter = state.Root.AddComponent<MeshFilter>();
        state.Renderer = state.Root.AddComponent<MeshRenderer>();
        state.Mesh = new Mesh { name = $"HexHighlight_{channel}_Mesh" };
        state.Filter.sharedMesh = state.Mesh;
        // 【方案乙】仅 CardPlacement 通道换发光材质；其余通道维持 Unlit/Color 纯色，
        // shader 找不到时安全回退到现有纯色路径，不炸。
        Shader glowShader = Shader.Find("Custom/CardDragPillarGlow");
        if (channel == HexHighlightChannel.CardPlacement && glowShader == null)
            Debug.LogWarning("[HexHighlightRenderer] Custom/CardDragPillarGlow 未找到，CardPlacement 回退 Unlit/Color（检查 shader 是否已导入 / 名称是否一致）。");
        bool useGlow = channel == HexHighlightChannel.CardPlacement && glowShader != null;
        state.Material = useGlow
            ? new Material(glowShader)
            : new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
        state.Material.color = Color.yellow; // 通道初始占位色；随后的 SetHighlightedCells 会用真实语义色覆写（可放置=亮绿）
        state.Renderer.sharedMaterial = state.Material;
        // 【方案乙】CardPlacement 额外挂单个 additive 粒子火花（参考图漂浮光点）；其余通道不挂。
        if (channel == HexHighlightChannel.CardPlacement)
            state.Sparks = CreateCardSparks(state.Root.transform);
        _channels[channel] = state;
        return state;
    }

    private void RebuildChannel(ChannelState state)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color32>();
        var tris = new List<int>();
        int vertexOffset = 0;

        // 仅 CardPlacement 通道写顶点色（其余通道不写，避免 Sprites/Default 回退路径被顶点色污染）。
        // 顶点色语义（供 Custom/CardDragPillarGlow 读取）：
        //   r = 顶面几何描边带（0=中心 → 1=边界）；角柱/光柱竖面 = 横向剖面（0=两侧缘 → 1=中线，见 AppendGlowBeamQuad）
        //   g = 墙面掩码（0=顶面/光柱（走顶面或光柱路径），1=墙面竖面）
        //   b = 竖直梯度（墙面竖面/光柱 0=底 → 1=顶；其余恒 0）
        //   a = 光柱掩码（0=顶面/墙面，1=角柱光柱 = PillarMaskByte，0.5=中央光柱 = BeamMaskByte）
        bool isCardPlacement = state.Channel == HexHighlightChannel.CardPlacement;
        Color32 topEdgeColor = new Color32(255, 0, 0, 0);
        Color32 topCenterColor = new Color32(0, 0, 0, 0);
        Color32 wallBottomColor = new Color32(255, 255, 0, 0);
        Color32 wallTopColor = new Color32(255, 255, 255, 0);

        foreach (HexCellData cell in state.Cells)
        {
            CellBuildContext ctx = MakeBuildContext(cell);
            // 外圈 6 顶点（与网格线共用构建）：前 6 点即外圈
            List<Vector3> gridVerts = _meshGenerator.BuildGridVertices(ctx);
            if (gridVerts == null || gridVerts.Count < 12) continue;

            // ---- 顶面（地面）填充：外圈 6 点 + 中心点 → 6 三角形 ----
            Vector3[] ring = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                ring[i] = gridVerts[i] + Vector3.up * 0.05f;
                verts.Add(ring[i]);
                uvs.Add(Vector2.zero);
                colors.Add(topEdgeColor);
            }
            Vector3 center = cell.RealCenterWorldCoordinate + Vector3.up * 0.05f;
            verts.Add(center);
            uvs.Add(Vector2.zero);
            colors.Add(topCenterColor);
            int centerIndex = vertexOffset + 6;
            for (int i = 0; i < 6; i++)
            {
                tris.Add(centerIndex);
                tris.Add(vertexOffset + i);
                tris.Add(vertexOffset + (i + 1) % 6);
            }
            vertexOffset += 7;

            // ---- 方案乙：CardPlacement 追加竖直能量围墙（低矮外立面 + 四角光柱）----
            if (isCardPlacement)
            {
                Vector3 centerXZ = new Vector3(center.x, 0f, center.z);
                Vector3 up = Vector3.up;

                // 6 面低矮能量墙：仅外立面竖面，底部亮 → 顶部渐隐（g=1，b=0底→1顶）。
                // 墙体顶部描边环带已撤：参考图无硬顶"缸沿"，光向上消散（见方案 zeta）。
                for (int i = 0; i < 6; i++)
                {
                    int j = (i + 1) % 6;
                    Vector3 bottomA = ring[i];
                    Vector3 bottomB = ring[j];
                    Vector3 topA = ring[i] + up * CardPlacementWallHeight;
                    Vector3 topB = ring[j] + up * CardPlacementWallHeight;

                    // 外立面竖面 quad：垂直上升，底部亮 → 顶部渐隐
                    int b0 = vertexOffset;
                    verts.Add(bottomA); uvs.Add(Vector2.zero); colors.Add(wallBottomColor);
                    verts.Add(bottomB); uvs.Add(Vector2.zero); colors.Add(wallBottomColor);
                    verts.Add(topA);    uvs.Add(Vector2.zero); colors.Add(wallTopColor);
                    verts.Add(topB);    uvs.Add(Vector2.zero); colors.Add(wallTopColor);
                    tris.Add(b0); tris.Add(b0 + 2); tris.Add(b0 + 1);
                    tris.Add(b0 + 1); tris.Add(b0 + 2); tris.Add(b0 + 3);
                    vertexOffset += 4;
                }

                // 6 根角柱"光柱"：在每根墙柱交点向上拔高、顶部渐隐（pillar=1，b=0底→1顶）。
                // 各做一对交叉"软束"竖面（俯视横截面 "+"），任一 yaw 下都可见；竖面已细分 3 列
                // 中心亮、两侧羽化 → 读作体积光束而非扁平卡片。
                for (int i = 0; i < 6; i++)
                {
                    Vector3 outward = new Vector3(ring[i].x - centerXZ.x, 0f, ring[i].z - centerXZ.z);
                    if (outward.sqrMagnitude < 1e-6f) continue;
                    outward.Normalize();
                    Vector3 tangent = new Vector3(outward.z, 0f, -outward.x);

                    AppendGlowBeamQuad(verts, uvs, colors, tris, ref vertexOffset,
                        ring[i], tangent, CardPlacementPillarHalfWidth, CardPlacementPillarHeight,
                        PillarMaskByte);
                    AppendGlowBeamQuad(verts, uvs, colors, tris, ref vertexOffset,
                        ring[i], outward, CardPlacementPillarHalfWidth, CardPlacementPillarHeight,
                        PillarMaskByte);
                }

                // 格心中央光柱（图三式"中心光束"）：交叉软束，位于格心、向上拔高、顶部渐隐。
                // 顶点色 a=128 触发 shader 中央光柱分支；比角柱更粗更高，形成"中心光柱"。
                AppendGlowBeamQuad(verts, uvs, colors, tris, ref vertexOffset,
                    center, Vector3.right, CardPlacementBeamHalfWidth, CardPlacementBeamHeight,
                    BeamMaskByte);
                AppendGlowBeamQuad(verts, uvs, colors, tris, ref vertexOffset,
                    center, Vector3.forward, CardPlacementBeamHalfWidth, CardPlacementBeamHeight,
                    BeamMaskByte);
            }
        }

        state.Mesh.Clear();
        state.Mesh.vertices = verts.ToArray();
        state.Mesh.uv = uvs.ToArray();
        if (isCardPlacement)
            state.Mesh.colors32 = colors.ToArray();
        state.Mesh.triangles = tris.ToArray();
        state.Mesh.RecalculateNormals();
        state.Mesh.RecalculateBounds();
        state.Root.SetActive(verts.Count > 0);
    }

    /// <summary>
    /// 【方案乙】在 <paramref name="basePos"/> 处追加一根"软束"竖面，以水平轴 <paramref name="axis"/> 为
    /// 宽度方向、向上拔高 <paramref name="height"/>。竖面被细分为 3 列（左缘 / 中线 / 右缘）：
    ///   - 顶点色 r 通道作横向剖面：两侧 r=0、中线 r=1 → shader 用 pow 羽化成"中心亮、两侧透"的体积光束，
    ///     去掉"扁平纸片"的硬边（旧 2 列 quad 横跨宽度全是均匀亮色，是纸片感的根因）。
    ///   - g 恒 0（不误入墙面幕布分支）；b 通道 0底→1顶（供 shader 渐隐）；a = <paramref name="maskByte"/>
    ///     （PillarMaskByte=255 角柱 / BeamMaskByte=128 中央光柱）。
    /// 对同一角点分别沿切线 / 外向调用即得俯视 "+" 截面（任一 yaw 可见）。
    /// </summary>
    private static void AppendGlowBeamQuad(
        List<Vector3> verts, List<Vector2> uvs, List<Color32> colors, List<int> tris,
        ref int vertexOffset,
        Vector3 basePos, Vector3 axis, float halfWidth, float height,
        byte maskByte)
    {
        Vector3 up = Vector3.up;
        int b0 = vertexOffset;
        verts.Add(basePos - axis * halfWidth);                  // 0 左缘/底
        verts.Add(basePos);                                     // 1 中线/底
        verts.Add(basePos + axis * halfWidth);                  // 2 右缘/底
        verts.Add(basePos - axis * halfWidth + up * height);    // 3 左缘/顶
        verts.Add(basePos + up * height);                       // 4 中线/顶
        verts.Add(basePos + axis * halfWidth + up * height);    // 5 右缘/顶

        // 横向剖面在 r（左0 → 中1 → 右0），竖直渐隐在 b（0底 → 255顶）。
        colors.Add(new Color32(0, 0, 0, maskByte));
        colors.Add(new Color32(255, 0, 0, maskByte));
        colors.Add(new Color32(0, 0, 0, maskByte));
        colors.Add(new Color32(0, 0, 255, maskByte));
        colors.Add(new Color32(255, 0, 255, maskByte));
        colors.Add(new Color32(0, 0, 255, maskByte));
        for (int k = 0; k < 6; k++) uvs.Add(Vector2.zero);

        tris.Add(b0);     tris.Add(b0 + 3); tris.Add(b0 + 4);
        tris.Add(b0);     tris.Add(b0 + 4); tris.Add(b0 + 1);
        tris.Add(b0 + 1); tris.Add(b0 + 4); tris.Add(b0 + 5);
        tris.Add(b0 + 1); tris.Add(b0 + 5); tris.Add(b0 + 2);
        vertexOffset += 6;
    }

    /// <summary>
    /// 【方案乙·粒子 garnish】为 CardPlacement 通道创建一个 additive 粒子火花系统：
    /// 格心一个球形发射区、向上飘、短寿命、低强度。默认弱到只补氛围，不影响读格；真机若掉帧可整体撤掉。
    /// </summary>
    private ParticleSystem CreateCardSparks(Transform parent)
    {
        var go = new GameObject("CardPlacement_Sparks");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f); // 少横向喷射，以整体上飘为主（更柔的漂浮感）
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
        // 临时启动色（随后 UpdateSparksColor 会用发光色覆写 rgb）；对齐可放置亮绿，避免冷启动瞬间闪黄。
        main.startColor = new Color(0.2f, 1.15f, 0.16f, 1f);
        main.gravityModifier = 0f;
        main.maxParticles = 96;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 30f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.9f; // 收拢发射区，火花更聚在格心一簇，更显眼

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        // Unity 要求 Velocity over Lifetime 的 X/Y/Z 三轴必须是同一种曲线模式，
        // 只赋 y 会导致 y 为 TwoConstants、x/z 为 Constant，运行时每帧报
        // "Particle Velocity curves must all be in the same mode"。
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.8f, 1.5f); // 整体向上飘，更明显
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var colorLifetime = ps.colorOverLifetime;
        colorLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 0f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }); // 生成即最亮，向上渐隐
        colorLifetime.color = grad;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = CreateSparkMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Stop(true);
        ps.Clear();
        return ps;
    }

    private Material CreateSparkMaterial()
    {
        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.mainTexture = CreateSoftDotTexture();
        return mat;
    }

    /// <summary>生成 32×32 软圆点，additive 粒子的柔和光斑，避免硬边方块。</summary>
    private static Texture2D CreateSoftDotTexture()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0 中心 → 1 角
                float a = Mathf.SmoothStep(1f, 0f, dist);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a)); // 单次衰减：实心软光斑，去叠淡
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags = HideFlags.DontSave;
        return tex;
    }

    private static void SyncSparks(ChannelState state)
    {
        if (state.Sparks == null) return;
        if (state.Cells.Count > 0)
        {
            // 火花系统是 Root 的子物体（位于地图原点），但高亮格在世界任意位置：
            // 世界模拟下发射器需跟随当前高亮格中心（CardPlacement 每帧只有 1 格）。
            HexCellData cell = null;
            foreach (HexCellData c in state.Cells) { cell = c; break; }
            if (cell != null)
                state.Sparks.transform.position = cell.RealCenterWorldCoordinate + Vector3.up * 0.3f;
            if (!state.Sparks.isPlaying) state.Sparks.Play();
        }
        else
        {
            state.Sparks.Stop(true);
            state.Sparks.Clear();
        }
    }

    private static void UpdateSparksColor(ParticleSystem ps, Color color)
    {
        if (ps == null) return;
        var main = ps.main;
        // respect 语义色 alpha（整体不透明度）：让粒子与高亮层同步变透/变弱，保持整层观感一致。
        main.startColor = new Color(color.r, color.g, color.b, color.a);
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
