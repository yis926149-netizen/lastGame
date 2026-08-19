using System.IO;
using NUnit.Framework;

/// <summary>
/// 【程序化山脉】阶段 7.4：拓扑验收相关源码契约测试。
/// 防止替换式拓扑/双轨碰撞/MountainVertexRanges 校验被后续重构回滚：
///  - ChunkMapRenderer：山体面只进 MountainIndices、被替换原始面只进 CollisionIndices（决策 ⑤ 替换式拓扑）
///  - ChunkMapRenderer：MountainVertexRanges 校验（collision 索引不得引用山体顶点，阶段 5.8）
///  - ChunkMapRenderer：槽布局 = 3 基础 + rect + tri + 山脚融合 + 可选末尾主山体槽（决策 ⑤）
///  - ChunkMapRenderer/MountainGeometryBuilder：山-普通 rect 格界劈半——普通半边 PlainRect 回地形槽（续22，决策 ④ 细化）
///  - MountainGeometryBuilder：拓扑诊断工具（退化三角/非流形边/几何 hash，决策 ㉛）
///  - TriangleTransitionMesh：tri 与 rect 角 profile 端点闭合契约（< 1e-4，决策 ㉛）
/// </summary>
public class MountainStage7SourceContractTests
{
    private const string ScriptsRoot = "Assets/Scripts";

    private static string ReadScript(string relativePath)
    {
        return File.ReadAllText(Path.Combine(ScriptsRoot, relativePath));
    }

    [Test]
    public void ChunkMapRenderer_ReplacementTopology_DualTrackSeparation()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("MountainIndices", renderer, "山体槽索引必须存在（决策 ⑤）");
        StringAssert.Contains("CollisionIndices", renderer, "独立碰撞索引必须存在（决策 ㉚）");
        StringAssert.Contains("MountainIndices = mountainIndices.Count > 0 ? mountainIndices.ToArray() : null", renderer,
            "无山 Chunk 两数组均为 null（碰撞回落渲染 mesh，阶段 3.2）");
        StringAssert.Contains("mountainIndices.Count > 0 || mergedMountainBoundaryIndices.Count > 0", renderer,
            "主山体或山脚融合任一存在时必须使用独立 collision；真正无山 Chunk 才零额外内存");
    }

    [Test]
    public void ChunkMapRenderer_MountainVertexRangesValidatorPresent()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("MountainVertexRanges", renderer,
            "山体顶点区间必须随装配记录（阶段 5.8）");
        StringAssert.Contains("collision index", renderer,
            "MountainVertexRanges 校验必须存在（collision 索引不得引用山体顶点，决策 ㉛）");
        StringAssert.Contains("mountainRanges.Add", renderer,
            "山体几何追加时必须记录顶点区间");
    }

    [Test]
    public void ChunkMapRenderer_MountainSlotAppendedAtEnd()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("+ mergedMountainBoundaryIndices.Count + (mountainIndices.Count > 0 ? 1 : 0)",
            renderer, "subMesh 布局必须包含山脚融合槽，并保留可选末尾主山体槽（决策 ⑤）");
        StringAssert.Contains("arrArawOrder[offset] = mountainIndices.ToArray()", renderer,
            "山体槽必须是末尾追加槽");
    }

    [Test]
    public void ChunkMapRenderer_MountainRoutingConditions()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("HasVisibleMountain(hexCellData)", renderer,
            "solid 替换式路由：有效山格顶面不进地形槽（阶段 3.6）");
        StringAssert.Contains("edgeMountain", renderer,
            "rect 替换式路由：贴山边走 mountain rect（阶段 3.4）");
        StringAssert.Contains("allMountain", renderer,
            "tri 替换式路由：仅 3 山格进山体槽（阶段 3.5）");
    }

    [Test]
    public void ChunkMapRenderer_MountainPlainRectSplit_TerrainHalfRouting()
    {
        // 2026-08-07 格界劈半（续22，决策 ④ 细化）：山-普通 rect 在格界劈成两件——
        // 山侧半边进山体槽、普通半边（PlainRect）回地形槽（rectGroups 地形材质分组），
        // 山体视觉边界收回到格界线。防回滚：builder 必须产出 PlainRect、renderer 必须路由回地形槽。
        string builder = ReadScript("Core/Services/MountainGeometryBuilder.cs");
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("public RectangleTransitionMeshData PlainRect", builder,
            "MountainRectBuild 必须携带普通半边 PlainRect（续22）");
        StringAssert.Contains("BuildRectFromProfiles(ownerHalfProfiles, 0f, 0.5f)", builder,
            "owner 半边 UV.y ∈ [0,0.5]（续22 动画/混合坐标与整面 rect 半段一致）");
        StringAssert.Contains("BuildRectFromProfiles(neighborHalfProfiles, 0.5f, 1f)", builder,
            "neighbor 半边 UV.y ∈ [0.5,1]（续22）");
        StringAssert.Contains("mountainBuild.PlainRect != null", renderer,
            "山-普通 rect 普通半边必须路由回地形槽（续22）");
        StringAssert.Contains("rectGroups[halfKey].AddRange(plainHalfInts)", renderer,
            "普通半边必须归入 (matA,matB) 地形材质分组（续22）");
    }

    [Test]
    public void ChunkMapRenderer_MountainBoundaryMaterialBlend_RoutesByTerrainAndWritesUv4()
    {
        string builder = ReadScript("Core/Services/MountainGeometryBuilder.cs");
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("RectToTerrainBlendRender", builder,
            "山-普通山侧半 rect 必须生成材质融合数据");
        StringAssert.Contains("geometry.BlendData = blendData", builder,
            "山侧1→格界0的融合权重必须随 flat 顶点输出");
        StringAssert.Contains("mountainBoundaryGroups", renderer,
            "山脚必须按普通侧地形材质分组，不能混入单一主山体槽");
        StringAssert.Contains("mesh.SetUVs(3", renderer,
            "山脚 terrain UV/融合权重必须写入 UV4/TEXCOORD3");
        StringAssert.Contains("EnableKeyword(\"_MOUNTAIN_TERRAIN_BLEND\")", renderer,
            "山脚材质实例必须启用不透明地形融合变体");
        StringAssert.Contains("SetTexture(\"_TerrainTex\"", renderer,
            "山脚材质必须接入普通侧地形 Albedo");
    }

    [Test]
    public void ChunkMapRenderer_RidgeEdgeTriangleShoulder_VisualOnlyRouting()
    {
        // 连续脊边相邻 tri：原 terrain tri 继续进地形槽/collision；肩部仅进 mountainIndices，
        // 第三格不写 mountain 数据。防回滚为“整块 tri 山体化”或把肩部加入 collision。
        string builder = ReadScript("Core/Services/MountainGeometryBuilder.cs");
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("BuildRidgeEdgeTriangleShoulder", builder,
            "必须保留脊边 tri 肩部纯几何入口");
        StringAssert.Contains("IsRidgeConsecutive(owner, neighborA)", builder,
            "肩部触发必须由连续脊边决定，而非按山格数量泛化");
        StringAssert.Contains("!HasVisibleMountain(neighborB)", builder,
            "第三格必须是普通格；3 山格 tri 仍走既有路由");
        StringAssert.Contains("BuildRidgeEdgeTriangleShoulder(", renderer,
            "ChunkMapRenderer 非 allMountain tri 分支必须尝试生成肩部");
        StringAssert.Contains("mountainIndices.Add(i + shoulderOffset)", renderer,
            "肩部只进山体渲染槽");
        StringAssert.DoesNotContain("collisionIndices.Add(i + shoulderOffset)", renderer,
            "肩部不得进入 collision；点击/镜头仍命中原 terrain tri");
    }

    [Test]
    public void MountainGeometryBuilder_TopologyDiagnosticsRetained()
    {
        string builder = ReadScript("Core/Services/MountainGeometryBuilder.cs");

        StringAssert.Contains("CountDegenerateTriangles", builder, "退化三角诊断必须保留（决策 ㉛）");
        StringAssert.Contains("FindNonManifoldEdges", builder, "非流形边诊断必须保留（决策 ㉛）");
        StringAssert.Contains("GeometryHash", builder, "几何确定性 hash 必须保留（决策 ㉛）");
    }

    [Test]
    public void ChunkMapRenderer_CollisionPlainTri_UsesFreshOffset()
    {
        // 阶段 7.4 修复回归：3 山格 tri 分支的 plain 封口（collision-only）追加在山体 tri 之后，
        // 索引偏移必须取 plain 实际追加位置（plainOffset）；复用山体 tri 之前的旧 IndexOffset
        // 会把 collision 索引打进山体顶点区间（MountainVertexRanges 校验拒绝 ⇒ 初始地图缺地形/无碰撞）。
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("collisionIndices.Add(i + plainOffset)", renderer,
            "3 山格 tri 的 plain 封口索引必须取 plain 实际追加偏移（阶段 7.4 修复）");
        StringAssert.Contains("int plainOffset = verticesList.Count;", renderer,
            "plainOffset 必须在山体 tri 追加完成后捕获");
    }

    [Test]
    public void TriangleTransitionMesh_EdgeClosureContractRetained()
    {
        string mesh = ReadScript("Utilities/TriangleTransitionMesh.cs");

        StringAssert.Contains("ValidateConnectedEdges", mesh, "tri 与 rect 角 profile 端点闭合校验必须保留（阶段 3.5）");
        StringAssert.Contains("EndpointTolerance", mesh, "闭合容差常量必须保留（< 1e-4，决策 ㉛）");
    }

    // ── 阶段 7.6：玩法规则与交互回归——部署入口统一资格（决策 ①）────────

    [Test]
    public void CardPresenter_ReleaseValid_UsesUnifiedDeploymentGates()
    {
        string presenter = ReadScript("Core/Services/CardPresenter.cs");

        StringAssert.Contains("MountainCellRule.CanSpawnUnitOnCell(cell)", presenter,
            "玩家单位卡确认路径必须校验山格/水域（决策 ①，阶段 7.6；放置预览之外的第二道闸）");
        StringAssert.Contains("MountainCellRule.CanBuildOnCell(cell)", presenter,
            "玩家建筑卡确认路径必须校验山格/水域（阶段 7.6）");
    }

    [Test]
    public void CardPresenter_CommittedDeployment_AlwaysConsumesCard()
    {
        string presenter = ReadScript("Core/Services/CardPresenter.cs");

        StringAssert.Contains("spawned = IsDeploymentCommitted(config, targetCell)", presenter,
            "生成后置初始化抛异常时，必须按目标格已提交状态决定是否消耗卡牌");
        StringAssert.Contains("if (viewBehaviour != null) viewBehaviour.gameObject.SetActive(false);", presenter,
            "成功部署后必须先隐藏卡牌，再触发扣费和补牌回调");

        int hideIndex = presenter.IndexOf("if (viewBehaviour != null) viewBehaviour.gameObject.SetActive(false);");
        int removeIndex = presenter.IndexOf("_cardService.RemoveCard(view.PlacementID);");
        Assert.That(hideIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(removeIndex, Is.GreaterThan(hideIndex), "卡牌槽更新必须发生在卡面隐藏之后");

        int buildingCommitIndex = presenter.IndexOf("h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(buildingType, g);");
        int visualRaiseIndex = presenter.IndexOf("_mapVisualEvent.Raise();", buildingCommitIndex);
        Assert.That(buildingCommitIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(visualRaiseIndex, Is.GreaterThan(buildingCommitIndex), "建筑必须先提交到目标格，再触发可能抛异常的视觉回调");
    }

    [Test]
    public void AICardBrain_SpawnCellChecks_UseUnifiedGates()
    {
        string brain = ReadScript("AI/AICardBrain.cs");

        StringAssert.Contains("MountainCellRule.CanSpawnUnitOnCell(cell)", brain,
            "AI 卡牌单位入口必须校验山格（决策 ①，阶段 7.6）");
        StringAssert.Contains("MountainCellRule.CanBuildOnCell(cell)", brain,
            "AI 卡牌建筑入口必须校验山格（阶段 7.6）");
    }

    [Test]
    public void BarracksSpawner_AdjacentSpawnCell_UsesUnifiedGate()
    {
        string spawner = ReadScript("Controllers/BarracksSpawner.cs");

        StringAssert.Contains("MountainCellRule.CanSpawnUnitOnCell(neighbor)", spawner,
            "兵营生产（玩家 + AI）出口格必须校验山格（决策 ①，阶段 7.6）");
    }

    [Test]
    public void AIAutoExplorer_RewardSpawn_UsesUnifiedGate()
    {
        string explorer = ReadScript("AI/AIAutoExplorer.cs");

        StringAssert.Contains("MountainCellRule.CanSpawnUnitOnCell(", explorer,
            "AI 探索奖励单位入口必须校验山格（决策 ①，阶段 7.6）");
    }

    [Test]
    public void ExplorationRewardSystem_Spawn_UsesUnifiedGate()
    {
        string system = ReadScript("Core/Services/Exploration/ExplorationRewardSystem.cs");

        StringAssert.Contains("MountainCellRule.CanSpawnUnitOnCell(", system,
            "玩家探索奖励单位入口必须校验山格（决策 ①，阶段 7.6）");
    }
}
