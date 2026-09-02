# Debugging Playbook

## Zenject startup ordering

- `BindInitializableExecutionOrder` only orders `IInitializable.Initialize()` calls. It cannot order a MonoBehaviour `Start()` against container initialization.
- Put dependent startup work under the same Zenject lifecycle, then declare the complete order in the installer. For this project: game flow prerequisites, card initialization, UI initialization, then game state entry.
- Do not hide a missing prerequisite with an infinite coroutine wait or a fabricated default Unity object. Keep the value null until its owner initializes it, then fail immediately with a diagnostic exception if execution order or scene configuration is wrong.

## Installer validation

- Validate every required serialized reference and required scene component before the first `Container.Bind` call. Returning after some bindings creates a misleading half-installed container.
- Aggregate all missing names into one exception so the scene can be repaired in one pass. Do not log and continue into `FromInstance(null)` or make required consumer dependencies conditional.
- Scope scene-component validation to the installer's own scene, including inactive children. A matching component in an additive or persistent scene must not accidentally validate the wrong composition root.

## Runtime data and level effects

- Serializable domain data stored by a MonoBehaviour is not a Unity component. Resolve the owning component first, then access its runtime data field; do not call `GetComponent<T>()` for a plain `[Serializable]` class.
- Apply one-time unlock and upgrade effects at the successful level transition, after clamping confirms the level increased. Running them from `Update()` repeatedly rewrites state, masks invalid ownership, and turns one bad reference into a per-frame error.
- Treat ScriptableObject records as templates when player and AI creation paths share the same provider. Copy mutable records into each runtime entity, keep faction progression outside the template, and apply it only at the owning faction's creation boundary.

## Entity removal consistency

- Removing a scene entity must be a domain operation, not a direct `Destroy`. Clear its current cell only if that cell still points to the same object, cancel pending movement or commands, remove it from the owning repository, notify selection/UI subscribers, then destroy it.
- Any aggregate contribution registered when an entity is created, such as per-turn technology or culture production, must be removed through every destruction and ownership-cleanup path. Make the unregister operation idempotent and clamp aggregate counters so repeated cleanup cannot create negative production.
- Route every removal reason through the same idempotent service, including death and settler consumption. Otherwise each path gradually diverges as new runtime state is added.
- Guard delayed death scheduling before playing effects or calling `Invoke`; an `Update`-driven health check otherwise queues the same settlement every frame.
- Treat grid movement as an occupancy transaction. Reserve the destination before releasing the origin, store the last committed cell, and make completion or cancellation compare the expected occupant before changing either cell; otherwise concurrent moves and mid-move cancellation create stacked or untracked entities.
- Give attack movement one coordinate contract: either accept the target entity cell and compute an attack position once, or accept an already legal attack position. Truncating once in input code and again in path execution silently extends melee reach and bypasses terrain costs.
- Keep logical death separate from visual death. Remove the entity from movement, occupancy, repositories, selection, and collision immediately when HP reaches zero, then delay only the visual GameObject destruction for the death animation.

## Domain rules driven by asset order

- Never derive card/unlock IDs from a contiguous `i <= level` loop over a database. Progression node order (tech/culture UI text) is independent of asset array order, so map each node to an explicit global card ID list and validate the IDs exist and are unique at startup.
- Keep the random draw range tied to real provider counts, not a hardcoded literal. A stale `Next(0, 15)` silently drops the last card when the database grows to 16 entries.
- Normalize random resource/terrain rolls through one function that maps only the intended values and sends everything else to `None`. A kill-only drop type added to an enum must be excluded from the map generator explicitly, not left to `Clamp`.
- Funnel every heal through one domain method that clamps to `[0, maxHp]` and refreshes UI from the clamped value. Scattered `currentHp += ...` sites let domain HP exceed the maximum even when the slider looks clamped.
- Keep card database IDs separate from display enums. If deployable building cards use IDs `0..N-1` while `BulidingType` reserves `0` for City, passing the enum value into parallel asset arrays shifts every lookup by one and can stay hidden while test data happens to be identical.

## Card deployment transactions

- Validate the target, prefab, parent, and required prefab hierarchy before consuming a card or mutating map ownership. Commit card removal only after entity creation succeeds; otherwise one missing Canvas or parent leaves a visible card detached from its hand slot or a half-deployed map entity.
- A permanently unplayable card must not remain eligible for random draws. One-use flags combined with an unchanged draw pool eventually fill a bounded hand with cards that can never be consumed.

## Unity batchmode test execution

- `dotnet build` failing with NETSDK1004 (missing `project.assets.json`) usually means a running Unity refreshed `Temp/obj`; run `dotnet restore` before rebuilding instead of treating it as a code error.
- A Unity CLI test run that logs `Failed to handshake` / `Access token is unavailable` and exits after script compile without writing the results XML is a licensing/environment failure, not a test failure. It can also leave a stuck process holding the single-instance project lock.

## Input modes and phase authority

- Gate commands at both gesture start and the domain commit boundary. Disabling a per-frame input handler does not disable EventSystem callbacks or already-visible UI buttons, so phase changes can otherwise still deploy cards or execute selected-unit commands.
- Before entering a mutually exclusive input mode, explicitly exit the previous mode and clear its state. If movement, attack, and card placement all toggle the same grid GameObjects directly, stale mode state can hide or recolor another mode's feedback.
- Give frame-driven services one update owner. Binding a component through `BindInterfacesAndSelfTo` already registers its `ITickable`; manually calling `Tick()` from another tickable doubles movement and smoothing without an obvious stack trace.
- Give each physical input gesture one command owner. Two update loops reading the same mouse-down frame can both mutate combat state; visual modes may observe the gesture, but only one handler should commit the action, and selection/phase changes must cancel the visual mode.
- Temporary UI highlights must restore the state they replaced, not assume the prior state was hidden or a default color. Shared grid objects can simultaneously represent a global toggle, movement, attack, and drag feedback.

## Procedural map generation

- When a two-dimensional array is flattened, its traversal order must match the consumer's generation order. This project creates cells with `z` outermost and `x` innermost, so flattening heights with `x` outermost silently transposes square maps and misassigns non-square maps.
- Create one seeded random source at the generation boundary and pass it through terrain, rivers, landforms, and resources. Multiple time-seeded `Random` instances make maps impossible to replay and can produce correlated sequences when constructed close together.
- Preselected procedural feature sources are only candidates. Revalidate each source immediately before use because an earlier feature may already have occupied it; otherwise later generation can overwrite bidirectional connection state.

## Runtime map visuals

- Purely visual meshes such as grid highlights and influence borders must not receive `MeshCollider`; otherwise toggling visibility changes raycast targets and interaction behavior. Give each per-cell mesh only its local vertices, or memory grows quadratically when every cell duplicates the full-map vertex buffer.
- Treat runtime `Mesh` and `Material` instances as owned native resources. Use shared renderer properties when sharing assets, and explicitly destroy generated meshes/materials when replacing their GameObjects; destroying the GameObject alone does not release externally created material instances immediately.
- A disabled MonoBehaviour cannot receive the ScriptableObject event intended to initialize it. Subscribe only after injection is available, unsubscribe symmetrically, and have the composition root enable required visual components before the initialization event is raised.
- Unity `.meta` GUIDs must be 32 hexadecimal characters. A Base64-like or otherwise malformed GUID can leave a script compilable in the IDE while making it impossible for scenes/prefabs to serialize a valid component reference.
- After migrating a scene from Tuanjie to Unity, verify each critical asset by matching the scene's serialized GUID to the asset `.meta`; a converted scene can retain a 32-character Unity GUID while the asset keeps a Tuanjie-style GUID, leaving only that reference broken even when batch import and compilation succeed.

## Overlapping territory ownership

- Do not use an active entity count as its next ID. Destruction lowers the count and makes a later city reuse an ID that may still belong to another live city; keep a monotonic allocator separate from active counts.
- A faction's total territory is a union of its per-city territories. When a city is removed, clear its direct ownership and rebuild the union from remaining cities before deciding which cells transfer; subtracting the removed city's whole set incorrectly removes or captures overlap still supported by another city.
## 文档/笔记里记录的"表现常量"可能是陈旧值——真机标定过的常量以当前文件为准

- **现象**：往 `PlacementRangeMaskUI.cs` 里加绿色遮罩层时直接 `Write` 整文件，被拒绝（"File has been modified since read"）。重新 Read 发现红区 `FillColor` alpha = **0.55**、`StrokeHalfWidth` = **12f**，而设计文档 `不可放置区域红色遮罩实现方案.md` §5 与实施记录里写的还是 alpha **0.16**、halfwidth **2.6f**——差了 3 倍以上。
- **根因**：那套表现参数（填充 alpha、描边半宽）是**用户亲手在真机上反复标定过的**，标定结果随手改进了代码文件，但**没人回写文档**。文档记录的是"初版建议值"，代码里的是"最终标定值"。旧值已被证明观感不对（0.16 太淡、2.6f 太细），若我信文档把红值"改回去"，等于把已标定的视觉回退。
- **做法**：
  1. 写这种"包含表现常量"的文件前，**先 Read 当前磁盘内容**再改，别用旧上下文里的快照（快照可能早于别人/用户在编辑器里的手工调整）。
  2. **文件里现有的常量 = 当前事实**，文档/笔记里的值只是历史记录；两者冲突时以文件为准，并保留文件里的真机标定值不动。
  3. 把"是否已真机标定"的状态写进文件注释（本文档 `PlacementRangeMaskUI.cs` 顶部就注了"红值已标定保持不动"），避免后人对着一组看似"过头"的数字犹豫。
- **可迁移判据**：凡是要重写/编辑一个含**视觉/手感参数**（alpha、宽度、容差、时长）的文件，先 `git diff` 或 Read 确认当前值，再判断要不要动；**改动此类参数前先问一句"这套数值是不是已经在真机上调过了"**。文档里的参数表永远只是候选值，不是待恢复的基准。

# Unity YAML 补丁必须绑定组件上下文

- **现象**：只按 `m_Enabled: 0` 等通用字段修改场景 YAML 时，补丁成功但目标组件没有变化。
- **根因**：Unity 场景中同名序列化字段大量重复，缺少组件 header/fileID 上下文的文本补丁会命中首个相同片段。
- **做法**：补丁必须包含 `--- !u!<type> &<fileID>`、组件类型和附近唯一字段；修改后立刻按目标 fileID 回读验证。
## Triangle transition strips can overlap inward boundary edges

- Symptom: synchronized step profiles render twisted or overlapping triangles even though every triangle has valid indices and upward winding.
- Root cause: strip quads filled the triangle up to the far endpoints' chord, then a fan filled the actual far-edge polyline. If that polyline bent inward, the fan covered an area already occupied by the strips.
- Fix pattern: stop the regular strips one level before the far edge and tessellate the single remaining polygon bounded by that rung, both rail tails, and the actual far-edge polyline.
- Regression test: compare the sum of absolute generated triangle areas with the XZ area of a deliberately concave boundary. Index and winding checks alone do not detect overlap.

## Triangle strip transitions: no step appearance + shadow seams

- Symptom: when two rectangular edges are Step type, the triangle strip transition lacks visible horizontal terraces, and large dark regions appear around transition boundaries.
- Step root cause: `BuildStrips` stopped one level short and used generic XZ tessellation on the final polygon, producing slanted surfaces with linearly interpolated Y across step boundaries.
- Shadow root cause: globally welding coincident vertices before `RecalculateNormals()` merges unrelated smoothing groups, including tread/riser hard edges, material boundaries, and UV seams. The discarded UV and invalid epsilon dictionary comparer can add further shading instability.
- Fix: preserve distinct indices and UVs, recalculate normals without global welding, and recalculate tangents for normal-mapped transition shaders. Cross-surface smoothing must be angle/crease aware; position-only index welding is not a valid normal fix.

## 过渡面片“异常暗/死黑”排查：先分层排除，别急着改法线

一次完整定位过程（三角过渡比矩形明显偏暗、部分墙面死黑），按此顺序分层排除，每步都是一个能一刀切开假设的廉价实验：

- **把法线焊接夹角设成 179°（无条件合并所有重合顶点法线）仍不变** → 排除“接缝/法线按索引平均”这一类。是接缝问题的话，全合并必然改变结果。
- **抬“很亮的常量环境光”仍不变** → 排除“光照不足/凹槽自阴影/投影”。常量环境光逐片元叠加、与法线和阴影无关；照不亮说明不是缺光。**投影阴影同理**：阴影只削减直射光、不动环境光，所以“亮环境光救不回”就等于“不是阴影”，不必再去试关 receive shadows。
- **消毒 NaN/零长法线与切线（保证 TBN 有效）仍不变** → 排除“切线 NaN 让法线贴图输出 NaN 黑”。
- 走到这里，Standard 光照下 `final ≈ albedo × ambient`：亮常量环境光都点不亮，只剩一个可能——**albedo ≈ 0（黑）**。问题在着色器算出的颜色，不在几何/法线/光照。

**根因**：三材质混合 Shader（`ThreeMaterialBlend_Land`）`o.Albedo = wA·A+wB·B+wC·C`，权重取自 `mask.rgb` 归一化。当遮罩在该 UV 处 RGB≈0（遮罩黑区，或竖面 XZ 投影 UV 退化采到黑点）→ 三权重都≈0，归一化后仍≈0 → **albedo 塌成纯黑**，任何光照都点不亮。而矩形用 `RealMaterialMaskBlend` 的 `lerp(B,A,w)`，遮罩为 0/1 时只是整块取某个真实材质，**永不为黑**——这正是“三角黑、矩形不黑”的非对称来源。

**修复**：归一化前判断 `wA+wB+wC < ε` 就退回等权（各 1/3），任何多权重（mask 驱动）混合都要加这个兜底；`max(total, 0.001)` 不够，因为分子也≈0。二路 `lerp` 没有这个失效模式。

**可迁移判据**：**“死黑且亮常量环境光都照不亮”≈ 零 albedo 或 NaN 输出，不是光照/阴影/法线问题**——这是最快的分流器。

## 过渡/切面网格用逐面平坦着色，且相邻面片着色模型要一致

- center-fan 这类“中心枢纽顶点被所有扇形面共享”的结构，`RecalculateNormals` 会把一圈朝向发散的面法线平均成一个被冲淡/偏斜的法线 → 中心发暗发糊。**渲染期展开成逐面独立顶点（deindex，每个三角形 3 个独立顶点）**，让法线逐面平坦，中心不再被平均，天然是干净硬切面；顺带让阶梯踏面/踢面各自平坦、边界清晰。
- 展开只在渲染期做，`Build*` 仍返回带索引几何，保证拓扑/绕序/剖分等 EditMode 测试语义不变。
- **相邻过渡面片的着色模型必须一致**：一侧条带平滑渐变、另一侧逐面硬切面，交界会出现突兀明暗跳变。要硬切面就整片都硬切面。
- 反过来记住上一条的“死黑”经验：改这些法线相关的东西之前，先用“179°/亮环境光”两步确认问题是否真的出在法线上，避免在着色器 albedo 问题上白改一通几何。

## Unity 顶点法线/UV 的两个坑（本项目过渡面已踩）

- `Mesh.RecalculateNormals()` 只按**顶点索引**平均，从不按位置合并；不同面片各自持有的重合边界顶点（位置相同、索引不同）法线互不相干。
- `UVGenerator.GeneratePlanarUV` 是 XZ 平面投影，**近垂直面在 XZ 上退化**：轻则贴图竖向拉伸成条纹，重则 UV 三角形零面积 → `RecalculateTangents` 出 Inf/NaN 切线。想根治竖面贴图与切线，应按面法线选投影轴做三平面（triplanar）UV（水平面 XZ、竖面 XY/ZY）；逐面独立顶点后正好可逐面选轴。

- **竞技场墙体不能复用平台的通行代价**：第二阶段突起事务会对带 `ArenaMountainPlacement` 的外环墙体写入 `HasMountain=true`。若同时显式写 `MovementCost=1`，`UnitMovementSystem` 只看移动代价就会允许单位进入有效山体；墙体必须写 `float.MaxValue`，而可通行的 `1` 只应保留给内环平台和两个入口。`HasMountain` 的派生规则也不能依赖调用方误设的代价。
- **寻路不能只信缓存的 movementCost**：任何逻辑若能显式覆盖移动代价，仍可能留下“有效山体 + 1”的矛盾状态。进入资格应在 `CanEnterCell` 中再次调用 `MountainCellRule.IsEffectiveMountainCell`，这样寻路邻居、攻击落点和最终移动都会统一拒绝有效山格。

## Transition blend runs along the wrong edge pair

- Symptom: rectangle transition should blend edge1(material1) -> edge3(material2) along the self->neighbor axis, but the blend ran perpendicular (edge2->edge4), rotated 90 degrees; triangles were worse.
- Root cause: UVGenerator.GeneratePlanarUV derived UVs from world XZ bounds with no relation to the profile (self->neighbor) direction, while the active blend mask ramps along V. Only the axis-aligned E direction presented a clean 90-degree rotation; diagonal NE/SE directions scrambled arbitrarily.
- Secondary: MapController passed ConfigureBlendMaterial(baseMaterialBs, baseMaterialAs) reversed, so _MainTexA became neighbor and _MainTexB became self, inverting which side each material occupies.
- Fix (rectangle): BuildProfileUV parameterizes UV along the profile - V = self->neighbor progress (self V=1, neighbor V=0, matching the mask bright->dark ramp and shader lerp(B,A,weight)), U = profile index. ToFlatShaded now carries source UVs instead of regenerating planar XZ. MapController arg order restored to (self A, neighbor B).
- Still open: triangle transition UVs remain planar XZ; needs barycentric/edge-distance UVs plus an RGB three-corner mask for correct per-edge blending. The ThreeMaterialBlend shader also expects RGB while mask 1.png is grayscale, and MapController sets _BlendContrast/_GlobalSmoothness which do not exist on that shader (only _BlendSmooth).

## Rectangle transition blend direction reversed (V-polarity mismatch)

- Symptom: rectangle transition shows self material on neighbor side and neighbor material on self side (expected: self→blend→neighbor).
- Root cause: BuildProfileUV set self V=1, neighbor V=0, matching the comment "遮罩实际暗(V=1)亮(V=0)" (mask dark at top/bright at bottom). But the actual mask texture `mask 1.png` has the opposite gradient (standard: dark at bottom/V=0, bright at top/V=1). So self(V=1)→bright mask→weight=1→_MainTexA(neighbor material), and neighbor(V=0)→dark mask→weight=0→_MainTexB(self material) — exactly reversed.
- Fix: flip V direction in BuildProfileUV from `1f - j/vDen` to `j/vDen` so self=V=0, neighbor=V=1, matching the actual mask dark→bright gradient. Material arg order in ConfigureBlendMaterial (neighbor→_MainTexA at bright side, self→_MainTexB at dark side) remains correct and unchanged.

## Triangle transition material blending was scrambled

- Symptom: after fixing rectangle blend direction, triangle transition material A/B/C assignment was still incorrect - all three corners blended chaotically instead of each corner being its own hex cell's material.
- Root cause 1: triangle UVs were generated by world-XZ planar projection, which has no relationship to the three triangle corners. The ThreeMaterialBlend shader expects R/G/B weights corresponding to the three corners, sampled via a mask texture at vertex UVs.
- Root cause 2: the mask texture was a grayscale 2D gradient (mask 1.png), not an RGB three-corner mask. The shader normalized gray weights (R=G=B at every sample point) to equal 1/3 each, producing a uniform blend.
- Root cause 3: MapController set _BlendContrast and _GlobalSmoothness on the ThreeMaterialBlend material, but the shader only declares _BlendSmooth; both were no-ops. ToFlatShaded for triangles also regenerated planar-XZ UVs, discarding any upstream UV fix.
- Fix: TriangleTransitionMesh.BuildFan now wraps all three build methods (BuildSimpleTriangle/TryBuildStriped/BuildTerrace) and replaces their UVs with barycentric UVs computed by ComputeBarycentricUVs. Barycentric UV = (weight of corner A, weight of corner B) relative to the three triangle corners (Self, A, B).
- Mask generation: GetOrCreateBarycentricMask in MapController generates a 32x32 runtime Texture2D where R=1-u-v, G=u, B=v (normalized), cached statically. When the shader samples this at vertex UV (u=w_A, v=w_B), it gets R=w_Self material, G=w_A material, B=w_B material.
- ToFlatShaded for triangles now carries the source barycentric UVs instead of regenerating planar XZ.
- MapController's three-material ConfigureBlendMaterial now sets _BlendSmooth (the shader's actual parameter) instead of the non-existent _BlendContrast/_GlobalSmoothness.
- Triangle material order in BuildNEE/BuildESE: materialAsTri=self, materialBsTri=neighbor[h[0]], materialCsTri=neighbor[h[1]] — this matches the shader A/B/C mapping (R=self, G=A edge, B=B edge).

## River channel missing in rectangular transition between two river cells

- Symptom: when two adjacent hex cells share a river edge (one's outgoing direction = the other's incoming direction), the rectangular transition mesh fills the entire gap with terrain, blocking the river channel between cells.
- Root cause: the generic transition refactor replaced the old 4-way branch (slope/step × river/non-river) in `MainMapMeshCreat`'s rectangle loop with a single `GetGenericRectangleMesh` path. This path is completely river-agnostic — it stitches outer surface rim vertices with no knowledge of river channels. The `MainMeshRectFunction` (which detects `isRiver`) was left uncalled, and the existing river-specific methods (`GetRectVertices` + `GetRectSlopeRiverDrawOrder`, `GetRectStepVertices` + `GetRectStepRiverDrawOrder`) in `MeshDataGenerator.cs` were orphaned.
- Fix: restore the `isRiver` branch in the rectangle transition loop. Call `MainMeshRectFunction` to determine `isSlope`/`isRiver`; when `isRiver==true`, use the old river-specific vertex/drawOrder methods; otherwise continue using the generic path. The old methods (`GetRectVertices` returns 8 surface + 4 river-bed vertices; `GetRectSlopeRiverDrawOrder` adds surface + river side-wall + bottom triangles) still exist in MeshDataGenerator and work correctly.

## Lake/sea water surface height hardcoded to 4.7, disconnected from WaterLevel

- Symptom: water surface floats far above terrain (Y=4.7 vs terrain Y=0~1 for water cells). Coast transitions span absurd vertical gaps (4.7→2.0 = 2.7-unit cliff). Water appears to hang in midair.
- Root cause: `GetlakeOrSeaVertices` (MeshDataGenerator.cs:3976) overwrites all lake/sea vertex Y to `4.7f` — a hardcoded constant that has no relationship to `WaterLevelConfig.WaterLevel` (=1) or the terrain's world-space Y (= CenterWorldCoordinate.y + Height, where CenterWorldCoordinate.y=0).
- Fix (Plan A — configurable sea level): 
  1. `MapGenerationConfigSO` added `seaLevel` field (default 1f).
  2. `HexCellData` added `waterLevel` field — set per cell by MapRenderer from config.
  3. `WaterLevelConfig.WaterLevel` changed from `const` to static field; synced from `_config.seaLevel` in `MapGenerator.MapDataGeneration` before cell construction.
  4. `MapRenderer.LakeOrSeaMeshCreat` sets `cell.waterLevel = _config.seaLevel` on each water cell.
  5. `MeshDataGenerator.GetlakeOrSeaVertices` computes water surface Y as `cell.CenterWorldCoordinate.y + cell.waterLevel + 0.1f`.
  6. All water cells share the same `waterLevel` (global sea), forming a single flat water plane. Coast/lake-lake transitions remain correct.

## mesh.Optimize() 会使外部记录的顶点索引失效（迷雾顶点色棋盘格乱纹）

- **现象**：顶点色迷雾方案上线后，地图渲染成黑白棋盘格乱纹——每个三角面片随机黑或白，与探索状态完全无关。
- **根因**：`MapController.CreatMesh` 在构建末尾调用 `mesh.Optimize()`。它会**重排顶点缓冲区**（vertex cache 优化），顶点/颜色/索引整体一致重排，所以初始渲染是对的；但 `HexCellData.MeshSolidAreaVertexStartIndex` / `MeshTransitionVertexRanges` 记录的是**重排前**的索引。运行时 `UpdateExplorationVisuals` 按旧索引往重排后的 `mesh.colors` 回写黑/白，写到完全无关的顶点上 → 逐面片随机黑白。
- **修复**：去掉 `mesh.Optimize()`（或改为在记录索引之前完成一切重排）。凡是"构建期记录顶点区间、运行时按区间回写属性"的方案，构建管线里**任何会重排顶点的调用**（`Optimize`、`OptimizeReorderVertexBuffer`、烘焙/合批工具）都必须禁用或在记录前执行。
- **可迁移判据**：初始帧正确、首次按索引回写后立刻变成逐面片随机乱纹 ≈ 索引与实际缓冲区顺序错位，先查构建后有没有重排顶点的步骤。

## Surface Shader 插值器超限 → 回退 legacy → 顶点色乘主纹理（伪装成"数据 bug"的黑白）

- **现象**：某个用 `#pragma surface surf Standard fullforwardshadows` 的地形/过渡 Shader，整片渲染成"顶点色白=白、顶点色黑=黑"的纯黑白，看起来像顶点色数据错了；但同项目里 UV 套数更少的同类 Shader 完全正常。本项目里迷雾集成后：核心区(TerrainBase_Fog)与三角过渡(ThreeMaterialBlend_Land)黑白，矩形过渡(RealMaterialMaskBlend)正常。
- **根因**：surface shader 的 `Input` 里每多一套 `uv_*` 就多占一个 TEXCOORD 插值器。`Standard + fullforwardshadows` 自身还要占世界坐标/切线基/阴影坐标等。**当 `Input` 里有 5 套 `uv_*` 时，在 `#pragma target 3.0` 下超出插值器上限，前向 Pass 编译失败 → 用 `FallBack` 里的 legacy shader 渲染**。而 legacy VertexLit/Diffuse 链会把（材质里往往未绑定、默认为白的）主纹理**直接乘以 mesh 顶点色** → 顶点色白处=白、黑处=黑，surf 里的混合/迷雾逻辑根本不执行。这非常像"顶点色/贴图没绑上"，极易误诊。
- **关键判据（一刀切开假设）**：确认全局/材质贴图确实已绑定（打日志验证），若此时未探索仍是纯黑而非应有的迷雾色 → **surf 没在跑**，是回退，不是绑定问题。对照"UV 套数少的同类 Shader 是否正常"即可锁定插值器超限。
- **修复**：合并共用同一套 `mesh.uv0` 的采样坐标。本项目三材质混合的 `_MainTexA/B/C`+`_MaskTex` 都是运行时 `SetTexture` 绑定（ST 默认 1,0）、采样同一套 uv0，故把 `uv_MainTexB/C`、`uv_MaskTex` 全改用 `IN.uv_MainTexA`，`Input` 从 5 套 UV 降到 2 套（`uv_MainTexA`+`uv_FogTex`），即可正常编译、surf 正常执行。删掉基础地形里全为空的 `_BumpMap/_OcclusionMap/_EmissionMap` 各自的 uv 套同理。
- **附带防御**：`FallBack` 用 `"Standard"` 而非 `"Diffuse"`，即便将来仍回退也只是丢失自定义效果、显示正常地表，不会因顶点色相乘而渲染成纯黑。
- **可迁移判据**：给一个已满载的 Standard surface shader **新增一套 `uv_*`（如集成全局迷雾/额外遮罩）后整片变黑白/异常**，第一嫌疑就是插值器超限触发 legacy 回退——先数 `Input` 里的 `uv_*` 套数，而不是去查顶点色数据。

## Surface Shader 的 `uv_<纹理名>` 字段会被自动填成 mesh UV，覆盖 vertex 函数的赋值

- **现象**：想让某张全局贴图按**世界坐标连续**铺满整图（战争迷雾/全局投影），在 surface shader 的 vertex 修饰函数里算好世界 UV 写进 `Input` 的 `uv_FogTex` 字段，surf 里 `tex2D(_FogTex, IN.uv_FogTex)`。结果贴图**每个面片各自铺一张完整图（0~1）**，而不是整图连续一张。改 vert 里的 UV 公式（世界投影、归一化、锁 mip）全都无效。
- **决定性线索（不对称）**：同项目里一个**纯 vert/frag shader**（本项目水面 LakeorSea）在 frag 里直接用 `i.worldPos.xz` 算 UV，表现完全正确、整图连续；只有 **surface shader** 的地形错。“同样的世界 UV，纯 shader 对、surface shader 错”直接指向 surface shader 特有机制。
- **根因**：Unity surface shader 会把 `Input` 里任何形如 `uv_<纹理名>`（或 `uv2_<纹理名>`）的字段**自动填充为该纹理的 mesh UV**（`mesh.uv * _Tex_ST`），**覆盖**你在 `vertex:vert` 函数里对该字段赋的值。`_FogTex` 是 sampler，`uv_FogTex` 正好匹配这个保留命名模式，于是 surf 实际拿到的是 mesh 每面片本地 UV（本项目每个 hex 面片是 0~1）→ 每面片一张完整贴图。vert 里算的世界 UV 从未被采用。
- **这解释了为什么"世界 UV / finalcolor / 锁 mip"多次修改无效**：改的那个值根本没进 surf；期间"改纯色能消掉花纹"只是因为纯色不采样贴图，进一步掩盖了真因。
- **修复**：把该 `Input` 字段改成**不匹配 `uv` 前缀模式**的名字（本项目用 `fogCoord`），Unity 便不再自动填充，保留 vertex 函数赋的世界 UV。插值器数量不变。备选：改用内置 `float3 worldPos;`（Unity 自动填世界坐标）在 surf 里现算，但多占一个 float3 插值器，已满载的 shader 慎用。
- **可迁移判据**：surface shader 里“vertex 函数给 `Input.uv_XXX` 赋的自定义值在 surf 里不生效 / 表现得像用了 mesh 原始 UV”，先看字段名是不是 `uv_<某纹理名>` 撞上了自动 UV 规则——改名即可，别再去调 vert 里的算法。

## 用噪声抖动做"锯齿边界"时，阈值必须钳在 (0,1) 内，否则翻的是整片内部而非边界

- **现象**：给战争迷雾的探索边界（地形 shader 逐顶点 `exploration` 0/1，`FogBlend.cginc`）加"世界方格量化 + 噪声扰阈值"做像素锯齿后，**满地图未探索区冒出大量零散黑块**（本该纯迷雾的地方露出了地形），已探索区里也可能被抠出空洞。
- **根因**：remap 写成 `threshold = 0.5 + (n-0.5)*_FogJaggedAmount; step(threshold, exploration)`，且**对每个像素都生效**。当 `_FogJaggedAmount ≥ 1`，`threshold` 跑出 `(0,1)`：
  - 深处未探索 `exploration=0`，遇到 `threshold ≤ 0` 的格子 → `step` 返回 1 → 露出地形（黑块）。
  - 深处已探索 `exploration=1`，遇到 `threshold > 1` 的格子 → 翻成迷雾（空洞）。
  - 噪声本意只在过渡带 `0<exploration<1` 微调边界，却因阈值越界把**纯 0 / 纯 1 的大片内部**也随机翻了面。`fogJaggedAmount` 默认 1.0、`Range` 上限还给到 2 → 直接踩雷。
- **修复**：`threshold = clamp(0.5 + (n-0.5)*amount, 0.04, 0.96)`。钳进 (0,1) 后 `exploration==0 → 恒 0`、`exploration==1 → 恒 1`，只有过渡带抖动。并把默认幅度降到 0.6、`Range` 上限收到 1。
- **可迁移判据**：凡是"平滑场 vs 逐像素噪声阈值"二值化做边缘效果（迷雾/溶解/像素海岸线/dissolve），**噪声只能推动边界，不能把阈值推出场值的两端**。判断标准：阈值的最坏取值代入"场=最小值/最大值"，`step`/`clip` 结果是否仍等于该端应有的值；不满足就 `clamp` 阈值或只在带内应用。黑块/空洞出现在**远离边界的纯色内部**，就是阈值越界的签名，别去查数据（顶点色）对不对。

## 做"程序化锯齿/像素边界"：白噪声阈值出的是椒盐方块，要连贯阶梯边必须用低频相干噪声做域扭曲

- **现象**：想把战争迷雾探索边界做成"像素海岸线"（连续的、由横竖短线段组成的阶梯状凸起/凹口）。用"世界像素格 + 每格 hash 白噪声扰动阈值 `step(0.5+(n-0.5)*amt, e)`"实现后，出来的是**一堆分散的独立小方块（椒盐/棋盘感）**，不是连贯的边界线。
- **根因**：`hash(cell)` 是**白噪声**——相邻像素格的噪声值毫不相关，于是边界渐变带里每个格子**独立地**翻面 → 空间上不连续 → 椒盐方块。连贯的边界要求"相邻格的判定相关"。
- **修复（域扭曲 domain warp + 量化）**：
  1. 用**低频相干噪声**（value/Perlin：对格点白噪声做 smoothstep 双线性插值）算一个平滑的 2D 偏移向量 `off`，波长 >> 像素格。
  2. 在【被 off 扭曲后的像素格中心】采样一个**单一连续场**（这里是探索遮罩的双线性值），再 `step(0.5, e)`。单一连续场的等值线天然连通不断裂 → 边界是一条连贯折线在原线两侧摆动。
  3. 在【格中心】统一采样、整格取同一值 → 量化成横平竖直的阶梯段（干脆方块），而不是格内斜线。
  - 参数直觉：噪声**波长**=凸起/凹口的大小；**幅度**（`off` 上限，可用"像素格数"表达）=边界摆动深度；**像素格**=阶梯粗细。
- **额外好处**：远离边界处场恒 0/1，偏移只在同色区内游走 → 内部零翻面（顺带避免了椒盐/黑块）。但要求那个"场"在同色区内**真的恒定无洞**：本例遮罩若用内切圆盖章会在三格交汇角点留缝，`step(0.5)` 下变成内部 fog 斑点 → 改用**外接圆**盖章保证无缝。
- **可迁移判据**：凡是"噪声驱动的边缘装饰"（迷雾/溶解 dissolve/描边/海岸线），若结果是**分散的点/块**而非**连贯的线**，第一反应就是"用了白噪声"——换成低频相干噪声（或对白噪声插值）并改成"域扭曲一个连续场再阈值"，而不是"逐像素独立掷骰子"。

## 六边形势力范围描边：不能靠"两端角点是否都被收集"来推断某条边是边界，会画出内部共享边

- **现象**：势力范围（`SphereOfInfluenceRenderer` → `MeshDataGenerator.GetOneSphereOfInfluenceVertices`）本应只描**外围轮廓**，但两个同属一个势力、彼此相邻的地块**之间**多出一条本不该有的六边形边线（内部共享边被画了出来）。
- **根因**：描边分两步，Bug 在"三-1 同地块内组合"。第一步先收集"边缘角点"——**一个角点只要它相邻的任一条边是边界就会被收集**（六边形角点 2 同属 NE 边与 E 边，角点 3 同属 E 边与 SE 边……）。三-1 却用"角点 n 和 n+1 都被收集 ⇒ 在 n→n+1 之间画边"（`ContainsKey(index[j]+1)`）来判定边界边。当某地块除 E 边（与东邻同势力地块共享、是内部边）外其余边都是边界时，角点 2、3 都因各自的**另一条**边（NE、SE）而被收集 → 三-1 误以为 2-3（E 边）也是边界 → 把内部共享边画出来。两侧地块各画一次，就成了两块之间那条多余的线。
- **决定性判据**：多余的线**恰好落在两个同势力相邻地块的公共边**上，而非区域最外缘 → 指向"内部边被误判为边界"，别去查邻居检测/引用相等（本项目 cell 都是 `GetCell` 规范实例，`List.Contains` 引用相等是可靠的，那条路没错）。
- **修复**：判断边界要**按"边"本身**，不能靠"两端角点是否都在"反推。第一步计算 `isEdgeAtNE/E/SE/...` 时就把该地块的边界边（角点对 `(1,2)/(2,3)/(3,4)/(4,5)/(5,6)/(6,1)`）记进 `edgeHexBoundaryPairs`，三-1 直接遍历这些角点对逐条画线。每条边界边仍产出 4 顶点一个四边形，下游 `vertices.Count/4` 的绘制/UV 不变。"三-2 相邻地块组合"（凸角处两地块内缩描边之间的小桥接）本就只在真凸角触发，正确，无需改。
- **可迁移判据**：凡是"由格子集合生成外轮廓/描边"（势力范围、区域高亮、选区边框、tile region outline），**边界的判定单元必须是"边"而不是"顶点"**。共享顶点会同时属于多条边，用"顶点是否入选"去推断"边是否为界"必然把内部边误纳。症状是**多余线落在同区两相邻格的公共边上**——直接改成按方向标记边界边逐条描，而不是扫描相邻顶点。

## Unity 特效节点不能依赖子节点顺序

- **现象**：单位攻击时模型显示，攻击结束进入移动逻辑后整个模型被 `SetActive(false)`，下一次攻击时又显示。
- **根因**：攻击特效通过 `transform.GetChild(transform.childCount - 1)` 获取。Prefab 子节点顺序变化后，最后一个节点可能是模型本体；攻击开始把它激活，攻击结束又把它关闭。
- **修复**：通过序列化字段或稳定名称定位并缓存特效节点；找不到特效时不操作其他子对象。不要把“最后一个子节点”当作稳定的组件契约。

## 派生经济数值必须由结算与 HUD 共用同一查询

- **现象**：新经济效果已经接入钱包结算，但 HUD 的“每秒收入”始终显示旧基础值，玩家会判断效果没有生效。
- **根因**：结算服务计算了“基础收入 + 地块/建筑加成 + Buff”，UI 却独立复制了“基础收入 × Buff”的旧公式；缺少覆盖真实结算服务的测试时，两条公式很容易永久漂移。
- **修复**：由结算服务暴露只读查询（如 `GetIncomePerTick(factionId)`），Tick 和 UI 必须调用同一方法；相关占领状态改变后，用现有经济事件或专门的领地事件刷新 HUD。测试同时覆盖规则统计和服务最终结果。

## C# 字符串插值里的条件表达式必须加括号（Unity 编译全红的隐蔽原因）

- **现象**：`$"...{mat.shader != null ? mat.shader.name : "null"}..."` 这类插值导致 Unity 脚本编译全红（CS8076/CS8361/CS1003 一堆），且错误行号出现在冒号附近，看起来像格式串写错。
- **根因**：任何 C# 版本的插值表达式里都不允许裸条件表达式——`:` 会被解析为格式说明符分隔符（`{expr:format}`），必须 `{(cond ? a : b)}` 加括号。Unity 的 Roslyn 与 dotnet 行为一致。
- **修复**：把插值内的条件表达式整体用括号包住。**排查法**：对 `$"..."` 内的 `{...}` 逐个目检有没有裸 `? :`。
- **附带坑（本案例联动）**：脚本编译一直失败 ⇒ `[InitializeOnLoadMethod]` 从未执行 ⇒ 依赖它自动补齐的序列化字段（如 `MountainConfig.stableMaterial`）一直缺失，而资产 YAML 里看不到新加的字段。**编译修复 + 域重载成功后字段才会自动补齐**——"资产缺字段"要先问"编译成功过吗"。

## Unity 编辑器自动重编译有滞后：验证编译结果以 DLL 时间戳为准

- **现象**：修改脚本后 Editor.log 长时间（几分钟）不更新，以为编辑器没反应/自动刷新被关。实际它稍后会编译。
- **做法**：判断"Unity 是否已重新编译"直接看 `Library/ScriptAssemblies/*.dll` 的 LastWriteTime（比 Editor.log 更及时可靠），再回读日志确认 0 error。Windows 上 EditorPrefs 的 `kAutoRefreshMode`（`HKCU\Software\Unity Technologies\Unity Editor 5.x`，Disabled=0/Enabled=1/EnabledOutsidePlaymode=2）可确认刷新模式。
- **可迁移判据**：`dotnet build` 全绿 ≠ Unity 侧全绿——Unity 的 csproj 是它自己生成的，内容与手写工程可能不同（文件清单、LangVersion、facade 引用）；以 Unity 实际编译为准，反之亦然（本案例：dotnet 抓到 `Tests.csproj` 里 `IEnumerable<ICall>.Count()` 缺 `using System.Linq`，其他测试文件都有该 using，只有新文件漏了）。

## ���Լоߵ�"���� Y ��׼"������ע��/���Կھ�һ�£����ε����δ�� Unity ����ʱ��Ǳ��ʧ�ܣ�

- **����**��MountainGeometryTests �ļо� CreateFixtureCell �� CenterWorldCoordinate = new Vector3(wx, 0f, wz)��Y=0����������ע������Ի�׼��"�������� Y=2"��Height=2����ɽ�幹����ֱ���� solid ��������� Y������ Height�������ʵ�ʶ������� Y = 0 + ¡�𣬶��������� 2 + ¡�� �� �״��� Unity ���б�Ȼ�졣ͬ�о�����һ������ȴ�� Y=0 ���ԣ�ɽ-��ͨ rect ���ê�� == 0�������о��ڲ�����ì�ܡ�
- **����**�����׼��μо��Խ׶� 3.3 ��ֻ�� dotnet ����ͨ����"MainGame/Tests ���� 0 ����"������δ������ Unity �ܹ���"����ͨ��"���󵱳ɱ���ͨ����"Height"��"�������� Y"�����������ֶΣ�builder ֻ����ǰ�ߴ����� solid ���� Y���������߰� Height �����˻������� Y��
- **�޸�**���Ѽоߵ� CenterWorldCoordinate.y �ĳ� 2���� Height һ�£������� 3 ���� 0 ��׼���Ե������Ļ� 2 ��׼����������� = 2+hA��ɽ-��ͨ���ê������ Y=2��3 ɽ�� tri ����ǵ����� Y = 2+max(...) = 4����
- **��Ǩ�ƾ���**���� �����൥���"���� Y"������������������� Y �������������� solid �������飬���� Height �ֶΣ����� ����ֵ��ע��ʱ��ע��������ѡһΪ׼�������������޶��ԣ���Ҫ���������Ը�����һ�ֻ�׼���� "���� 0 ����"������"����ͨ��"������ע"�� Unity ����"�Ĳ����׼����������ȷ��������ǰ��Ӧ��Ϊδ��֤���� �оߵ�"�������� Y"Ҫôͳһ��һ������������Ҫôÿ�����Զ��� ��׼Y + ¡�� ����ʽ����Ӳ�������֡�
## �������������񽻽� tri ��"�м�߹���"��BuildESE �м� rect = (apex+SE, NE)������ (apex+E, NE)

- **����**���׶� 7.4 ȫͼװ������У�(E,SE) ·�ɵ����񽻽� tri �� "Disconnected triangle boundary at edge0 -> edge1"��ValidateConnectedEdges �˵�պ�ʧ�ܣ���������εء����ô� rect ʱ�� BuildSimpleTriangle ֻȡ e0.first/e0.last/e1.last �����ǵ㣬��"ƴ��"һ�����ǵ������ڸý��硣
- **����**������ {O, O+E, O+SE} ���������У����������ھ� (O+E)?(O+SE) �ıߣ��� (O+SE) �ӽǿ��� NE ����(O+SE)+NE = (O+E)������ (O+E) �ӽǿ��� W ���򣨲��� NE/E/SE ֮һ��? �ñ�ֻ���ܹ� (O+SE) �� NE rect�������ܹ� (O+E) �� NE rect���� (NE,E) ·�ɵ��м� rect = (O+NE, SE)��(O+NE)+SE = O+E�������� apex ·�ɵ��м� rect ���򲻶Գƣ����� = �м���������ھӣ�����������"���ھ���ʹ�ñ����� NE/E/SE ��λ����һ��"��
- **�޸�**��BuildPlainTri/(E,SE) ��֧�м� rect �� = (neighborB=c+SE, NE)��(NE,E) ��֧ = (neighborA=c+NE, SE)��BuildTriangleMountain �ڲ��Ѱ���ʵ�֣�neighborA = neighborOf(owner, pair0==NE?NE:SE)�������Ը���ʱ��Ҫƾ"����Գ�"ֱ����
- **��Ǩ�ƾ���**���� ����������"�ھӼ��"�Ĺ��������ܿ�����Գ������������� (X+dir) ��㣬��֤�ñߴ���һ���ӽ����� NE/E/SE �ۣ��� ���񽻽� tri �Ľǵ� = {e0.first, e0.last, e1.last}���м� profile �ô�ʱ BuildFan ���ܲ��ף����˵�ǡ�ñպϣ����׳������˵��λ����������·�ɵĲ���Ӧͬʱ����"�ǵ� = Ԥ�� solid �ǵ�"������ֻ��"�ܹ���"���� ɳ�� harness �� Dictionary ������ UnityEngine.Vector3.GetHashCode()����ϣ��ײ��Ѷ�� rect ����ͬһ�ݣ����ø���/hex �ַ�����

## ���Լо�/װ������е�"��"����ȷ���ԣ�Vector3.GetHashCode �������ֵ��

- **����**���׶� 7.4 ɳ�����У���У�ȫͼװ��ȫ�� rect ����"����ͬһ�� rect"����ͬ���������ͬ�ǵ㣩���� NUnit ��ȴ������
- **����**��ɳ������� c.Hex.GetHashCode() ���ֵ������UnityEngine.Vector3 �� GetHashCode ����ײ���ҽ����ڲ��ȶ����������� hex ӳ�䵽ͬһ�� ? ���һ��മλ��
- **�޸�**�����ø�� GenerateOrder/�������������
- **��Ǩ�ƾ���**���κ�"�������"���ֵ����Ӧ�ø����/�����ַ����������� UnityEngine.Vector3.GetHashCode()������װ��������Ʒ������ͬһ��Լ������Ʒ�� GenerateOrder����ɳ�临��ʱ����һ�¡�
## "ͳһ�ʸ����"�տڱ�������÷���ƣ�Ԥ��բ �� ȷ��բ�����բ �� AI/��Ӫ/����բ

- **����**���׶� 6.5 �Ѱ���ҷ���Ԥ������ CanSpawnUnitOnCell��PlayerInputHandler��������Ʒ��� 5 ������Կ���ɽ���ϲ���λ��CardPresenter.IsReleaseValid����ҿ����Ϸ�ȷ��·������Ԥ��բ֮��ĵڶ���բȱʧ�����"�޸�������ִ��"���ڣ���AICardBrain.IsValidSpawnCellForUnit��AI ���Ƶ�λ��ֻ֧��ˮ/����/��λ/����/���ڣ�����ɽ�񣩡�BarracksSpawner.FindAdjacentEmptyHex����Ӫ�������ڸ񣩡�AIAutoExplorer.SpawnRewardUnits + FindOverflowCell��AI ̽����������ExplorationRewardSystem.SpawnUnitsWithOverflow + FindOverflowCell�����̽����������
- **����**��һ��"ͳһ����"���� �� ������ڽ��ϣ�ÿ������/����·������д��һ�ױ����ʸ��飨ˮ/����/��λ/���ڣ������µ�ò���壨ɽ��ʱֻ����Ԥ��·��������·������©�ġ�lockBuildingSpawn ֻ������������λ��movementCost ֻ��Ѱ·�������𡪡������������һ�Σ��κ�һ��©�Ӷ���ɿɲ��𴰿ڡ�
- **�޸�**������ڲ�ͳһբ����λ�� MountainCellRule.CanSpawnUnitOnCell�������� CanBuildOnCell����ˮ��/ɽ��/blockBuildingSpawn������ 5 �ļ� 7 ����������Դ����Լ���ԣ�StringAssert.Contains ���ÿ������ļ���ͳһբ���ã����ع�����
- **��Ǩ�ƾ���**���� �����µĸ��ʸ�����ʱ����ö��ȫ��"�������/����/�ƶ�"���÷��������Ϸ�ȷ�ϡ�AI ���ơ���Ӫ��̽����������ʼ���ɡ�AI ��������Ԥ��/���� UI ���տڲ��ܴ���ȷ��·������ �ʸ�У���ɢ�ڸ����ʱ����Դ����Լ�������ļ�����ͳһբ���ã��� �������壨blockBuildingSpawn/movementCost/ͳһ�ʸ��������и���������嵥Ҫ��"��� �� ����"����˶ԡ�
## װ��˳�����"��ƫ�Ƹ���"�Ƿ�ȫ����׷��ɽ����׷�� plain ʱ��plain �����þ� IndexOffset ����ɽ������

- **����**��PlayMode ���ܣ���ʼ�����뾺��������ؽ����� "Terrain collision index �����˽�ɽ����Ⱦ���㣨���� xxx..xxx��"����MountainVertexRanges validator �ܾ��ύ������ Chunk(0,0)/(0,1)/(0,2) û�е������񣨲��ɼ� + ����ײ����
- **����**��BuildChunkTerrain �� 3 ɽ�� tri ��֧�У�plain ��ڣ�collision-only������׷����**ɽ�� tri ֮��**��AddRange ˳��ɽ�� �� plain������ collision �����õ���**ɽ�� tri ׷��ǰ**����� IndexOffset�������������ɽ�嶥�����䡣rect ��֧ǡ�����ȼ� plain ���ɽ�壬ͬһģʽȴ��ȷ����"ƫ���Ƿ�ȫ"ȡ����׷��˳��
- **Ϊʲô EditMode/7.4 ���̲���û��ס**��7.4 ȫͼ���̵�װ��˳����"�� plain ��ɽ��"���� rect ��֧ͬ�򣩣�����ʵ tri ��֧��ɽ���ȡ�plain �󣩲�ͬ�������̲���˳�� �� ��ʵ����˳���ڸ��� bug������ʱ validator �������������ҹ���������
- **�޸�**��plain ׷�Ӻ���ȡ int plainOffset = verticesList.Count ��ƫ�ƣ��� 7.4 ���̸�Ϊ��ʵ˳��ɽ�� tri �ȡ�plain �󣩣�����Դ����Լ���� collisionIndices.Add(i + plainOffset) �� int plainOffset = verticesList.Count; ͬʱ���ڡ�
- **��Ǩ�ƾ���**���� ����װ��/·�ɵĲ��Ա�������ʵ����**���ͬ��**��˭��׷�ӡ�ƫ�ƺ�ʱ���񣩣�˳��ͬ����"��ƫ�Ƹ���"�� bug ��Ĭͨ������ ������������ + ��������˫���װ�䣬ƫ��һ����"�öζ���׷����ɺ�"�ٲ��񣬽�ֹ����ѭ���������գ��� ����ʱУ������MountainVertexRanges���� EditMode ���ǲ�������ʵװ���ϳе�����ְ�𡪡���װ��·�����ߺ��������һ�� PlayMode ȫ���������� 7.x ���ա�
## "磁盘上有资产、Project 窗口不显示"——先确认 Unity 是否处于 Play 模式（AssetDatabase 被冻结）

- **现象**：某些文件夹/资产（本例 TalentCard、MapLandForm、MapResource）明明在磁盘上、文件完整（`.meta` 合法、`m_Script` 能对上、无重复 GUID），Unity 的 Project 窗口里却不显示；按 `Ctrl+R`（Refresh）也刷不出来。
- **根因**：Unity 在 **Play 模式**下会冻结 AssetDatabase——不导入新增/改动的资产，`Ctrl+R`/`Reimport` 也被屏蔽。资产本身没坏（运行时甚至能正常加载它们）。
- **决定性判据（不必猜）**：读 `Editor.log`（Windows：`%LOCALAPPDATA%/Unity/Editor/Editor.log`）末尾——若全是运行时输出（Bootstrap/Tick/业务日志）且出现 `Reloading assemblies for play mode`，就是正在 Play。日志里**没有** `same guid`/`conflict`/`could not be imported` 才能排除真正的导入错误。
- **修复**：退出 Play 模式（▶ 停止 / Ctrl+P）后再 `Ctrl+R`，文件夹立刻回来。仍不显示才升级：关 Unity → 删 `Library/` → 重开做全量重导入（磁盘资产是好的，不会丢）。
- **可迁移判据**：Ctrl+R 刷不出磁盘上确实存在的资产时，**先看是不是在 Play**，而不是去查 `.meta`/GUID。查 meta 是"磁盘上文件损坏/引用悬空"的排查方向，跟"编辑器不刷新"是两码事——先用日志把这两类分开。

## 新增 .cs 文件后 dotnet build 是"假绿"——Unity 生成的 csproj 只含生成时已存在的文件

- **现象**：新建 `Assets/Editor/MountainPerformanceBaseline.cs` 与两个新测试文件后，`dotnet build Assembly-CSharp-Editor.csproj / Tests.csproj` 全绿（0 error），但新文件的语法错误根本不会被编译——后续单独验证才发现报错。
- **根因**：Unity 生成的 `.csproj` 用**显式 `<Compile Include="...">` 逐文件清单**（非通配符 glob），只在 Unity 域重载/刷新时重新生成。新建的 .cs 在编辑器重新生成 csproj 之前**不在任何 csproj 里**，dotnet build 自然跳过它。
- **做法**：新增文件后先确认它进了 csproj（`Select-String '<Compile Include="路径"' *.csproj`），没进就**临时手工插入 Compile 项**再 build 验证（csproj 被 gitignore，Unity 下次域重载会覆盖，临时改动无害）；不要依赖"build 全绿"作为新增文件的编译证据。
- **可迁移判据**：`dotnet build` 通过但改的是"刚新建的文件" → 先查 csproj 清单；"我以为编译过了"的空欢喜通常来自这类生成物缓存。

## 分帧化渲染前，先查"哪些逻辑字段是被 mesh 构建顺手写出来的"

- **现象**：把 `MapPresentationBootstrap` 的地图初始化从单帧改成逐 Chunk 分帧（P0-1，消除 1588ms 长帧）后，开局的玩家主城/AI 主城/公共建筑/地貌浮标会落在错误位置（(0,0,0) 或上一局的旧值）——但地形本身看起来完全正常。
- **根因**：`cell.RealCenterWorldCoordinate` 是**逻辑字段**，却只在 `ChunkMapRenderer.PreBuildRectProfiles`（逐 Chunk 的 mesh 构建步骤）里被写入：`cell.RealCenterWorldCoordinate = _meshGenerator.BuildSolidArea(cell, _view).Center;`。同步路径下"全图 mesh 构建"与"骨架就绪"是同一时刻，这个隐式依赖看不出来；一旦按帧切分，`GameFlowManager.Initialize()` 后半段（`GeneratePlayerMainCity` / `AIInit` / `TrySpawnPublicBuilding` / `CreateAllMarkers`）在同帧继续跑，而绝大多数 Chunk 还没构建，读到的就是默认值。
- **做法**：给渲染器加一个只算中心点的轻量纯函数（`IMeshGenerator.ComputeSolidAreaCenter`，2 次噪声采样、不分配 44 点数组），在 `PrepareChunkHosts`（骨架帧）里对**全图**跑一遍预置；逐 Chunk 构建时的覆写值与它逐位相同，两条路径结果一致。
- **坑中坑**：别照直觉写"中心 = `CenterWorldCoordinate` + `Height * elevationStep`"。44 点里的 0 号点其实是 `HexMetrics.Perturb(zero) + HexMetrics.PerturbY2(zero)`——**三个轴都被扰动过**。少算这两次扰动，实体会与地表差出可见的高度/水平偏移。必须读 `MeshDataGenerator` 的顶点数组确认真实表达式，而不是按"逻辑高度"推。
- **防漂移**：两处公式并存必然随各自演进分叉。把未扰动中心抽成 `SolidAreaCenterWithoutPerturb`，再让 44 点数组的 0 号点**直接调用** `ComputeSolidAreaCenter`——变成结构上的同一份代码，另配一条 `ComputeSolidAreaCenter == BuildSolidArea().Center` 的数值测试守住这枚钉子（`Assets/Tests/MapPresentationSlicedInitTests.cs`）。
- **可迁移判据**：把任何"构建/生成"步骤分帧或异步化之前，先 grep 这个步骤里所有对 `cell.` / 领域对象字段的**赋值**。凡是"顺手写出来的逻辑字段"，都是同帧下游消费者的隐式依赖；分帧会让它们静默变成默认值——症状出现在离改动很远的地方（实体位置），而不是被改的那个系统（地形）。

## 布尔标记不等于"已执行过的副作用"——可见性要靠记录意图重放，不能靠标记反推

- **现象**：同上次分帧改造。资源模型改成分帧实例化后，`PublicBuildingGenerator.MarkUnexplorableArea()` 在骨架帧执行 `hex.resourceModel.SetActive(false)`，但那些 `resourceModel` 还没被创建（`null`），于是不可探索区的资源全部照常显示。
- **第一反应是错的**：在实例化时统一按 `SetActive(!cell.IsUnexplorable)` 处理——看着等价，实际会**多藏一批**。`ArenaEventManager.OnMapInitialized()` 也对 37 个竞技场预留格置 `IsUnexplorable = true`，但它**故意不隐藏**资源模型。两个生产者写同一个标记、期望的可见性却不同，所以标记根本不足以反推可见性。
- **做法**：让施加副作用的那一方记录自己的意图（`HashSet<HexCellData> _resourceHiddenHexes`），并暴露一个幂等的重放入口（`ApplyResourceVisibility()`），由分帧完成回调（`GameFlowManager.OnMapPresentationReady`）调用一次。谁隐藏、谁记账、谁重放。
- **可迁移判据**：见到"标记位 + 对应副作用"这种组合，先数**有几个生产者写这个标记**。只要多于一个，标记就只是标记，不是副作用的可靠代理；需要延迟/重放副作用时，重放的必须是**记录下来的意图集合**，绝不能拿标记现场重算。

## Animator 参数默认值与 C# 边沿同步不一致 → "原地不动 + 播放跑步动画"

- **现象**：单位被海洋完全隔绝目标时，站在原处不动，但一直播放跑步动画（`isMoving` 在 C# 层看起来没问题，`GameLoop` 里 `if (brain.IsBusy) continue;` 也判不出异常——`isMoving` 是 `false`，单位确实没入队移动）。
- **根因**：两个 Animator 控制器（`swordsman.controller`, `archer.controller`）文件里参数 `isMoving` 的 **`m_DefaultBool: 1`（默认 true）**；默认状态是 `IdleBattle`，`Run 0` 的进入条件就是 `isMoving == true`（`m_HasExitTime: 0`）。而 `UnitMovementController.Update()` 的动画同步是**边沿检测**——只在 `isMoving != lastIsMoving` 时才写 `animator.SetBool("isMoving", ...)`。单位一出生 `isMoving` 声明为 `false`，从未变过 `true` → 无任何边沿 → `SetBool` **从未被调用** → Animator 参数一直用控制器默认的 `true` → `Run 0` 从出生就常驻。只有真正移动过一次、触发过 `false→true` 边沿的单位，才会在停下时被 `SetBool(false)` 拉回 IdleBattle，所以"无目标"时看起来正常，而新生成、从未移动的单位必定卡跑步。
- **修复（B 方案）**：`UnitMovementController.Start()` 里，在 animator 非空分支末尾**无条件初始化一次** `animator.SetBool("isMoving", isMoving);`（不依赖边沿，把参数拉回代码语义）。更治本是改控制器文件的 `m_DefaultBool: 1 → 0`。
- **可迁移判据**：**Animator 布尔参数用边沿检测同步、且控制器文件默认值 ≠ 代码期望值时**，"从未变化"这个合法状态会让参数永远停在错误默认值。凡是"能预置 true 的进入条件状态"（跑步/攻击/受击），要么在 `Start()` 无条件 `SetBool` 一次，要么把 `.controller` 的默认值改对——两条等价，缺一不可防。
- **排查顺序经验**：先 grep 出**所有** `isMoving`/`SetBool("isMoving",...)` 的写点，再打开 `.controller` 看 `m_DefaultBool` 与默认状态。别一开始就在 C# 标志生命周期里找"哪条路径漏复位"（这次全查过：只有 4 处清 `isMoving`，都各归其位）。

## 哨兵值当预算传进"≤ 预算"的筛选里 → 筛出全集（`GetAllReachableHexesFromStartHex(…, float.MaxValue)` 返回含水域的全图）

- **现象**：想用"无限预算"取整块连通可达区域（隔海趋近要在可达区里找最靠近目标的岸格），顺手传 `float.MaxValue` 作 `totalCost`。结果返回的不是连通区，而是**全图**——包括水域和被海隔开的对岸，趋近逻辑直接把单位往海里指。
- **根因**：`UnitMovementSystem.GetAllReachableHexesFromStartHex` 的收尾判据是 `Point_minCostValuesList[i] <= totalCost`，而 Dijkstra 初始化时**不可达格的 minCost 恰好就是 `float.MaxValue`**（哨兵值）。`MaxValue <= MaxValue` 成立 → 每个不可达格都通过筛选。哨兵值和预算撞成同一个数，"过滤条件"退化成恒真。
- **修复**：传**有限**预算。本项目每条边 cost 恒为 1（`HexCellData.movementCost` 只有 `1` 与 `float.MaxValue` 两种取值，且 `CanEnterCell` 在建边阶段就把 `MaxValue` 格剔除），故任何可达格代价必 `<= allPoints.Count` —— 用 `allPoints.Count` 作预算既覆盖全连通区又不触碰哨兵。
- **可迁移判据**：**任何"用 `<=`/`<` 对比预算来筛选"的 API，都不能把该数据结构的『不可达/未初始化』哨兵值当预算传进去**。传之前先问：哨兵值代进这个比较式，结果是 true 吗？是就必须换成有限上界。症状是"放宽限制后结果反而从子集变成全集"——不是算法错，是哨兵与阈值撞号。

## 带可达性过滤的索敌查询会让"被隔绝的目标"完全隐形，使多个不同场景塌缩成同一个兜底

- **现象**：单位在**无目标**和**目标被海洋隔绝**两种场景下表现完全一样（都原地站桩）。设计上二者应当不同：无目标该随机游走，目标被隔开该走到最靠近目标的岸格（远程隔海射击、近战海边驻扎）。
- **根因**：`FindNearestEnemy` / `FindNearestChest` / `FindNearestEnemyBuilding` 三个查询**都内建可达性过滤**（`CalculateMinMovementCostBetweenTwoHexes(...) && cost < bestCost`）。被海隔绝的目标 Dijkstra 无解 → 被跳过 → 三查询齐返回 `null`。于是"真的没有目标"和"目标存在但过不去"产生**完全相同的返回值**，下游策略无从分辨，只能落到同一个 `return null` 兜底。信息在查询层就被抹掉了。
- **修复**：给索敌链加一组**忽略可达性**的镜像查询（`FindNearest*IgnoringReachability`，过滤条件完全一致，只把"可达且代价最小"换成"六边形距离最近"），兜底时先用它们判断"目标是否存在"，再单独校验可达性来区分两种场景。
- **关键细节（否则会引入新 bug）**：判定"被隔绝"**必须真的跑一次可达性校验**，不能只看"忽略可达性的查询有结果、而普通查询没结果"。因为近战 step 2 有**警戒范围（3格）门槛**——一个**可达但较远**的敌人同样会落到兜底。若不校验就直接趋近，会变成无限追击，既越过 `AlertRange` 的设计意图，也抢掉"无目标 → 随机游走"。本项目 `CalculateMinMovementCostBetweenTwoHexes` 对不可达返回 `false`（且 `totalCost = -1`），可达返回 `true`，据此可精确区分。
- **可迁移判据**：**当两个本该不同的场景表现塌缩成同一种时，先查它们的输入查询是不是共用了一个"把失败折叠成 `null`"的过滤器**。带过滤的查询天然会丢失"目标不存在"与"目标存在但不满足条件"的区别；需要下游分辨时，就得提供一个不带该过滤的镜像查询，把判定权交还给调用方——而不是在兜底里靠猜。

## RenderTexture 预览体取景三连坑：SkinnedMeshRenderer.bounds 的绑定姿势 / 离屏缓存 / 世界-局部混算

做"卡牌拖拽 → 3D 模型预览"（独立 Layer + 正交预览相机 + RT + RawImage）时按 `Renderer.bounds` 自动取景，连踩三个独立坑，症状层层伪装：

- **坑 1（世界差值赋给 localPosition）**：`bounds.center` 是**世界坐标**。`model.transform.localPosition = -(bounds.center - anchor.position)` 看着像"把模型挪到 anchor 中心"，实际把 anchor 自身的世界偏移也算进去了——预览工作室故意放在 `y=-5000`，模型于是被推到 `localPosition.y≈-5000`（世界 `y≈-10000`）飞出视锥，RT 全透明。**必须 `anchor.InverseTransformPoint(bounds.center)` 换算回局部空间再取反。**
- **坑 2（蒙皮网格的顶点不在渲染器空间）**：`SkinnedMeshRenderer.sharedMesh.bounds` 的顶点在**绑定姿势空间**、由骨骼 `bindposes` 驱动，与渲染器自身 `transform` **无关**（渲染器常挂在与实际网格位置毫无关系的节点上）。拿 8 个角点过 `renderer.transform.TransformPoint` 换算 → 整体偏移。而 `MeshRenderer` 的 `mesh.bounds` 恰好就定义在自己 transform 的局部空间里，同样的代码完全正确。**决定性签名：「单位（蒙皮）偏移、建筑（MeshRenderer）正常」= 你在用 sharedMesh + renderer.transform 处理蒙皮网格。**
- **坑 3（离屏返回绑定姿势缓存盒）**：改回直接读 `renderer.bounds` 还不够，它的可信度有两个前提：① 实例化当帧 Animator 尚未求值，得先 `animator.Update(0f)`（不推进时间，只求值一次骨骼矩阵）；② `updateWhenOffscreen` 默认 `false` 时 Unity 对**离屏**蒙皮网格返回**绑定姿势缓存盒**——预览体常驻主相机视锥外的独立 Layer，正是这条路径，必须置 `true`。
- **诊断顺序**：先按"蒙皮 vs 非蒙皮"分流（坑 2 的签名），再查坐标空间（坑 1：把模型 `localPosition` 打出来，看是不是 ±几千），最后才是姿势/缓存（坑 3）。三者症状都表现为"有的卡能显示、有的不能，五五开"，不分流会一直在错误层面改。
- **附带**：45° 斜俯视拍摄时正交尺寸要用 `bounds.extents.magnitude`（对角半径），用 `max(extents.x, extents.y)` 会在模型绕 Y 旋转后被水平裁切。`enabled` 不能用来过滤 Renderer——预览体已统一禁用所有 MonoBehaviour，某些 Prefab 的渲染器本就默认关闭，按 `enabled` 过滤会漏掉真实网格。

## "位置即指示"的跟随物不能做边缘 Clamp，尤其不能用随缩放变化的边距

- **现象**：拖拽预览窗口（512×512 RawImage）跟随指针；小模型阶段能贴到屏幕左右边，模型放大到满尺寸后却在离边很远处就停住。
- **根因**：为"防止窗口出屏被裁剪"加了 `Clamp(localPoint.x, xMin + halfW, xMax - halfW)`，而 `halfW = rect.width * 0.5f * scale` **随缩放变化**——`scale` 从 0.1 涨到 1，边距从 ~26 涨到 256 参考单位。两阶段手感割裂只是表象，真问题是：这个窗口的位置本身就是**落点指示**，一旦被 Clamp 就与指针脱钩，玩家看到的落点是错的。
- **修复**：去掉 Clamp，精确跟随。ScreenSpaceOverlay 下越界部分自然落到屏幕外，不产生渲染问题（除非父节点上有 `RectMask2D`/`Mask`）。
- **可迁移判据**：给跟随指针的 UI 加边界约束前先问一句——**它的位置是"装饰"还是"信息"**。是信息（落点/瞄准/拾取目标）就不能 Clamp，宁可半个出屏；只有纯装饰性的浮层才适合限位。若确实要限位，边距也必须与视觉缩放解耦，否则会凭空造出"不同阶段行为不一致"的诡异手感。

## 世界空间单位血条在「Prefab 视图对齐」与「Play 姿态」对不上：绑定姿势坐标系 + Rebuild 坐标系漂移

给 `archer_blue.prefab` 里世界空间 Canvas 下的血条（Slider）调位置时，Prefab 场景视图看着贴住了单位胸口，进 Play 却悬空/偏移一大截（比如血条高高飘在上方）。顺着 Bip/网格节点的绑定姿势调，越调越偏。

- **根因 1（主导）——Prefab 视图显示的是 FBX 绑定姿势，Play 里 Animator(Humanoid) 每帧重写骨骼变换**。`archer_blue` 的 `Bip001` 绑定局部位置 `(-3.25, -8.0, -15.32)`（且 WK_ 网格节点在 `(-3.25, -10.9, -15.12)`，尺度 4）——这套数只是素材制作时留下的绑定姿势，运行时被 Animator retarget 成另一套（往往把脚放到 Animator 根、体高拉回正常量级）。**在 Prefab 视图里"把血条拖到模型胸口"这一操作本身就在错误参考系里**：你看得见的模型不是运行时那个模型的形状/位置。
- **根因 2（放大偏移）——血条 RectTransform 的 `m_AnchoredPosition` 是**真实数值**，而它 `m_LocalPosition.x/y` 在 YAML 里是 0（不一致）。Prefab 视图能容忍这组不一致数，Play 里 RectTransform 按 "anchoredPosition + 父 Rect" **重算** localPosition，把 `anch=(3.6,-36.2)`（Canvas 已乘 `0.015×4`）这道纵向偏移重新变现，血条相对模型又动一格。
- **根因 3（与朝向耦合）——血条离 Canvas 原点很远时，朝向变化会被放大成水平漂移**。Canvas 本身用 `LookAt(相机)` 每帧 billboard（`UIController.Update` 的 `unitCanvas/buildingCanvas` 分支），它认的是**游戏相机**的朝向；Prefab 视图里 Canvas 的 `eulerX=-39.312` 只是场景相机/静态视角。血条锚点离原点越远，朝向差 28° 产生的位移就越大；而同工程里 `swordsman_blue`/`archer_red` 的血条锚点就在 Canvas 原点附近，朝向差几乎没影响——**同一套 billboard 逻辑，只有"血条放远"的文件才会被坑**。
- **修复（以 `archer_blue` 为 0 号参照，照抄**正确**文件）**：血条锚点改回接近 Canvas 原点的做法——`archer_red`/`swordsman_blue` 的滑块 `anch=(x:20,y:0)`（即 `localPosition (1.2, 0, 0)`×0.06 世界单位）且 `localPosition.z=0`；竖向偏移放在**外层 Canvas 节点**的 `anchoredPosition=(0, 2.5)`。别再把血条 z 拉成 `-357.8`、也别把锚点挪到 `(3.6,-36.2)`。
- **可迁移判据**：**带 Animator 的 Prefab，凡是在 Prefab 视图里能看到"模型位置与节点不一致、且绑定姿势数值巨大（±几十、尺度 4）"的，千万别拿 Prefab 视图当运行时参考**。判定"改对了没"要进 Play 看；或者用一个运行时不重绑骨骼的参考物（如 Cube）做对齐参照。**要复用别的 Prefab 的布局，直接对比这几项**：血条 `anchoredPosition`（是否 ≈原点）、血条 `localPosition.z`（是否为 0）、外层 `Canvas.anchoredPosition`（竖向偏移是否放在这）、`Canvas` 的 `scale`（是否 `0.015`）——4 项都和正确文件一致，Play 姿态自然对得上。
- **附带**：两个文件都改了导致回归。`archer_blue.prefab` 未提交改动（`model` 指到 `y=2.23`、血条锚点 `(3.6,-36.2)`、`localPosition.z=-357.8`）与 `archer_red`（`anch=(x:20,y:0)`、`z=0`）互为反例——**改血条前先 `git diff` 看两侧是否都被动过**。

## `void` 的下游拒绝会让上游的退避/节流永远不生效（20+ 单位卡顿的直接触发器）

- **现象**：单位数少时流畅，到 20+ 附近**突然**卡顿（而非平滑劣化）。`UnitBrainBase` 明明有 `_pathfindFailed` + `SearchInterval` 节流，Profiler 里却看到大量单位每帧都在跑完整决策链（索敌 + Dijkstra）。
- **根因**：节流位只在 `ChooseNextPath` **返回 null**（找不到路）时置位。但拥挤时真正的失败形态是「找到了路、但抢不到槽位」：`RequestMove → ReservePathSlots` 整路径原子预留失败 → `RequestMove` 返回 false → `UnitMovementController.MoveTo` 是 `void`，把这个 false 就地吞掉直接 `return` → `isMoving` 保持 false → brain 下一帧仍空闲 → 无节流重跑全链。于是形成正反馈：**单位越多 → 格子越满 → 预留失败越多 → 越多单位每帧全速重算 → 更卡**，阈值感就是这么来的。
- **修复**：`IUnitMovement.MoveTo` / `UnitMovementController.MoveTo` 由 `void` 改 `bool`，brain 收到 false 时 `InvalidatePath()`（目标格已被别人占住，沿旧路径继续只会一步步撞同一堵墙）+ 置 `_pathfindFailed`，让既有节流介入。
- **可迁移判据**：**凡是「上游有退避/节流机制、下游有可能拒绝请求」的组合，先检查这条拒绝信号有没有真的传回上游**。`void` 的提交接口 + 内部 `if (!success) return;` 是最典型的信号黑洞——静默失败不会报错，只会让重试变成满速空转。症状签名：**有节流却观测不到节流生效，且失败率随负载上升**。写这类接口时，「请求被拒」必须是返回值/回调，不能只是一个提前 return。

## 兜底分支往往是最热的路径：可达性判定要与"取可达域"共用同一次洪泛

- **现象**：同上卡顿分析。`ChooseFallbackPath`（无目标/隔海时的兜底）一次要跑「最多 3 次 `MoveToAttack` 全图 Dijkstra（判目标是否被隔绝）+ 1 次全图洪泛（选岸格）+ 1 次求路 Dijkstra」= **单个空闲单位单帧 5 次以上全图搜索**。而「没目标 / 挤不进去 / 隔海相望」恰恰是单位多时**最常见**的状态——最贵的分支被走得最勤。
- **修复**：在兜底入口只跑**一次** `GetAllReachableHexesFromStartHex`，结果一鱼两吃：灌进复用的 `HashSet` 让可达性判定退化成 O(1) 查表；同一份 `List` 直接用来选最近岸格。
- **等价性陷阱**：旧判定用 `MovementPurpose.MoveToAttack`，该模式**允许终点被占据**（走到邻格即可开打）；而洪泛的 `allowedBlockedTarget` 为 null，站着敌人的格子不在可达域里。直接换成查表会把近身敌人误判成「被隔绝」，错误触发隔海趋近。补法：目标格本身在集合内 **或其 6 个邻格任一在集合内** 即算可达。
- **可迁移判据**：优化前先问「**这条分支是异常路径还是常态路径**」——名字叫 fallback/兜底/异常处理的代码，在高负载下常常反而是主路径。另外，**把 N 次「单点可达性查询」换成 1 次「全可达域计算 + 查表」时，必须逐一核对两者的可进入性语义**（是否允许终点被占据/阻挡、是否允许起点、代价预算），语义差一点就会在边界场景静默改变行为。

## 逻辑计时加速会被表现层动画窗口掩盖：攻速缩放"看起来没生效"

- **现象**：速度系统（x2/x3）接入 `ScaledDeltaTime` 后，攻速冷却已同步加速，但实战中攻速完全没变化。
- **根因**：攻速冷却只是节奏链条的一环。攻击动画用 `Invoke(nameof(StopAttackAnimation), animDuration)` 走真实时间，窗口期间 `IsBusy = true`（isAttack / isAttackingInProgress），决策轮转跳过该单位——每两次攻击的最小间隔被钉死在动画时长上，冷却加速再快也被掩盖。`Invoke` 走 timeScale 时间（本项目不改 `Time.timeScale`），天然不受任何内部倍率影响。
- **修复**：动画窗口改由 Update 按 `ScaledDeltaTime` 手动倒计时（替换 Invoke），并把 `Animator.speed` 设为档位倍率；两者同倍率 → 动画完整快进播完、窗口到期恰好结束，无截断。死亡动画例外（依赖帧事件 + 兜底延时，保持 speed=1）。跨基准换时间戳（`Time.time` → `GameLoop.GameTime`）前先确认该状态无存档序列化。
- **可迁移判据**：给任何游戏逻辑计时器（冷却/生产/收入）接入加速或缩放后，**先画出该节奏的完整门控链**：计时器到期后还要经过哪些标志位（IsBusy/动画状态/Invoke 窗口/动作间隔门控）才能真正执行下一次动作？链上只要有一个真实时间驱动的环节，加速就会被它钳住。症状签名：**倍率加大到极端（10 倍）节奏仍纹丝不动 → 不是计时器没改到，是门控链上有别的锁**。

## 运行时新建 UGUI Graphic：`new GameObject(name, types)` + 后置 `SetParent` 会让 mesh 永不重建

- **现象**：UI 弧光拖尾（`UITrailRenderer : MaskableGraphic`）在运行时按需创建共享 Renderer 节点，代码路径全部走到、Emitter 也注册上了，屏幕上却什么都没有——**且 `OnPopulateMesh` 里的诊断日志一条都没打**（既没有"首次生成 ribbon"，也没有创建失败的 LogError）。
- **根因**：`new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UITrailRenderer))` 建出的对象**是激活的**，`Graphic.OnEnable()` 在这一行就跑完了——此时对象还挂在场景根上，`CacheCanvas()` 缓存到 null。随后的 `SetParent(parent, false)` 触发 `OnTransformParentChanged`，而 UGUI 该函数是先 `m_Canvas = null` 再 `if (!IsActive()) return;`，偏偏 `Graphic.IsActive()` 的条件里就要求 `m_Canvas != null` → **直接早退，既不重新 CacheCanvas 也不 SetAllDirty**。从此这个 Graphic 卡在"非激活渲染态"：之后每一次 `SetVerticesDirty()` 同样被 `IsActive()` 挡掉，静默丢弃，`OnPopulateMesh` 永不执行。没有异常、没有日志，只是安静地不画。
- **修复**：改成 **先建成 inactive → AddComponent → 挂父子/设层级 → 配置 profile 等参数 → 最后 `SetActive(true)`**。让 `OnEnable` 在最终层级下运行一次，缓存到正确的 canvas，`ApplyMaterial()` 也拿得到已赋值的 profile（原写法要在外面补调一次 `ApplyMaterial()` 正是这个坑的另一面症状）。
- **然而"先 inactive 再激活"会引出第二个坑（同一次排查里踩到的）**：`Canvas` 是**原生组件**，`overrideSorting` / `sortingOrder` 在 GameObject **inactive 时写入不会落地**——Canvas 要到 `OnEnable` 才建立自己的排序状态，之前的赋值被静默丢弃。症状是日志打出 `sortingOrder=0`（而非你设的 -1/30000），节点退化成**按 hierarchy 顺序渲染**；如果它又恰好是第一个子节点，就被所有业务 UI 完整盖住，看起来仍然"什么都没有"。**排序属性必须在 `SetActive(true)` 之后再设**（本例抽成 `ApplySorting()`，`SetAsFirstSibling/LastSibling` 也一并挪过去）。
- **可迁移判据**：**运行时动态创建的 UGUI 元素，凡是"先建后挂"的，一律改成"先 SetActive(false) → 挂父子 → 配托管字段 → SetActive(true) → 再设原生组件的运行时状态"**。托管字段（自己脚本的 public/SerializeField）inactive 时写入没问题；**原生组件的运行时状态（Canvas 排序、Animator 参数等）必须等激活后再写**，这两类要分开处理。这条对所有 `Graphic` 子类（Image/Text/自定义）都成立，不止本例。诊断签名极好认——**代码路径明明走到了，但 `OnPopulateMesh` / `OnEnable` 里的日志一条不打**，就说明这个 Graphic 从未进入激活渲染态，去查它的创建顺序，而不是去查 shader、材质、sortingOrder。**在创建完成处把关键运行时状态整条打出来（canvasCached / isActive / overrideSorting / sortingOrder / 实际 shader 名），比逐个猜快得多**——本例第二个坑就是靠日志里那句刺眼的 `sortingOrder=0` 一眼定位的，设的明明是 -1。反过来说：排查"UI 不可见"时，**先确认 mesh 到底有没有生成**（在 `OnPopulateMesh` 里打一次顶点数），再谈层级和渲染；顺序反了会在 shader/层级上白耗很久。
- **附带（同一次排查里的第三个坑）**：`UITrailLayer.Above` 当时只是个占位枚举值，`GetOrCreate` 里遇到它会 LogWarning 后**静默回退成 Below**（`SetAsFirstSibling` + `sortingOrder=-1`，压在所有业务 UI 之下）。预制体上配了 `layer: 1` 却看不到拖尾，第一层原因就在这。**未实现的枚举值不要静默降级到"视觉上完全相反"的行为**——要么实现，要么让它明确失败。

## `AddComponent<某Graphic子类>()` 不会带来 `CanvasRenderer`，缺了它 `Graphic.Rebuild()` 每帧无条件早退

- **现象**：接着上一条。创建顺序、canvas 缓存、Canvas 排序全部修好之后，`UITrailRenderer` 的状态看起来完全健康——`IsActive=True`、`activeInHierarchy=True`、`enabled=True`、canvas 缓存正确、`rect` 是满屏 1080×1920、材质是编译通过的 `Custom/UITrailGlow`、Emitter 已注册且采样正常跑满 32/32 点。**但 `OnPopulateMesh` 依然一次都没执行**，屏幕上依然什么都没有。
- **根因**：那个节点上**根本没有 `CanvasRenderer` 组件**。`Graphic` 类声明了 `[RequireComponent(typeof(CanvasRenderer))]`，但该特性只在 **Inspector 里手工 Add Component** 时由编辑器补齐；**运行时 `gameObject.AddComponent<T>()` 不触发它**（本项目 Unity 2022.3 实测）。而 `Graphic.Rebuild(CanvasUpdate)` 的第一行就是：
  ```csharp
  if (canvasRenderer == null || canvasRenderer.cull) return;
  ```
  组件缺失 → 每帧无条件早退 → `OnPopulateMesh` 永不执行 → 零 mesh、零日志、零像素，**且不抛任何异常**。
- **为什么极难定位**：症状与上一条的"`SetVerticesDirty()` 被 `IsActive()` 静默丢弃"**完全一致**——都是"代码路径全走到、`OnPopulateMesh` 一条日志不打"。修好了创建顺序之后 `IsActive()` 已经返回 true，于是所有常规怀疑（激活态、canvas 缓存、排序、材质、shader、顶点数）全部显示正常，唯独结果不对。**这两个坑必须靠打印 `canvasRenderer` 是否为 null 才能区分**。
- **修复**：`GetOrCreate` 里显式 `go.AddComponent<CanvasRenderer>()`（在 `AddComponent<UITrailRenderer>()` 之前），并顺手 `cr.cullTransparentMesh = false`——初始 mesh 为空，留 `true` 会被原生 Canvas 标记 `cull`，而 `cull` 一旦置上，`Rebuild` 同样在第一行早退，`OnPopulateMesh` 再没机会把 mesh 填进去清掉它：**自锁**。另在 `OnEnable`/`LateUpdate` 加自愈兜底（缺组件就补上并告警、`cull` 被置上就清掉），覆盖手工挂载与预制体等其他来源。
- **可迁移判据**：**`[RequireComponent]` 只是编辑器的便利，不是运行时契约**。凡是运行时 `AddComponent` 一个带 `[RequireComponent]` 的类型（`Graphic` 子类要 `CanvasRenderer`，`Rigidbody` 依赖、`AudioSource` 依赖同理），**依赖组件必须自己显式加**，不能假定框架代劳。诊断上给"UI 不可见"这条排查线加一个**最先检查的项**：`canvasRenderer == null` 吗？它比 `IsActive`、比 shader、比 sortingOrder 都更靠前——`Rebuild` 的第一行就卡在这里，后面所有状态再健康也没用。**心跳日志要把它打出来**（本例正是靠心跳里那句 `canvasRenderer=无` 一眼定位的，此前三轮排查全在更下游的层面打转）。

## Unity asmdef 边界：新代码引不到已存在类型（CS0246/CS0234）

- **症状**：一批"类型找不到"报错（`AudioManager`、`RadialCounter`、`Volume`、`PSDImporter` 等），各自分布在**不同文件**，看似零散，实则由少数几个根因引发。
- **根因分类（逐个映射，不要逐个改）**：
  1. **命名空间变动**——`AudioManager.cs` 被移进 `namespace UIToolkitDemo`，而调用方在全局命名空间且无 `using`。修复：给调用方加 `using UIToolkitDemo;`。**判据**：报错文件是否已在自己 `namespace` 内则免加（同命名空间可直接解析）。
  2. **asmdef 未引用第三方程序集**——`UnityEngine.Rendering.Universal`/`Volume` 报错：`MainGame.asmdef` 未引用 URP 两个 Runtime 程序集。修复：往 references 加 GUID（`Unity.RenderPipelines.Core.Runtime`、`Unity.RenderPipelines.Universal.Runtime`）。
  3. **asmdef 边界**——`RadialCounter`/`ChartLibrary` 所在文件夹无 asmdef，编进 `Assembly-CSharp`；而带 asmdef 的 `MainGame` **不能引用 Assembly-CSharp**。修复：给那个文件夹新建 asmdef（并引其依赖如 Unity.Collections），把 GUID 加进 `MainGame.asmdef` references。
  4. **Editor-only 程序集被运行程序集引用**——`PSDImporter`/`PSDImporterEditor` 在 `Unity.2D.Psdimporter.Editor`（Editor-only），而文件位于运行程序集 `MainGame` 的目录下，`#if UNITY_EDITOR` **挡不住**（问题不在编译目标而在程序集边界）。修复：给该目录单独建一个 `includePlatforms:["Editor"]` 的 asmdef，并引用 `Unity.2D.Psdimporter.Editor` 与 `PsdPlugin`。
- **可迁移判据**：
  - **报错不是"类型不存在"，而是"当前程序集看不见"**。先看报错类型所在文件是否在某个 asmdef 作用域里，再想主调用程序集能否引用它。
  - **`#if UNITY_EDITOR` ≠ Editor-only 程序集**。平台宏只处理编译目标，不做程序集隔离；Editor API 类型（如 `ScriptedImporter` 派生类）必须放进 Editor-only asmdef 才能引用 Editor 程序集。
  - **新建 asmdef 要顺带写 `.meta`**（含 GUID）。Unity 用 `.asmdef.meta` 的 GUID 作为 references 里的引用 ID，`GUID:xxxx` 拼的就是它。可直接用 `[guid]::NewGuid().ToString('N')` 生成。
  - 加 `using` 前先确认目标类型无重名歧义；加 asmdef 前先用 `grep` 确认该目录**只有**要隔离的文件（否则会把别的运行代码也拉出 MainGame）。

## MonoBehaviour 的 `.meta` GUID 漂移会"静默"绑出 null 单例，把 NRE 伪装成 DI 没装

- **现象**：`[Inject] private AudioManager _audioManager;` 在 `GameFlowManager.Initialize()` 里判空触发 `NullReferenceException`。第一直觉是"AudioManager 没绑/忘注入"，于是翻 `GameInstaller`、翻 `GlobalServicesInstaller.InstallBindings`，都"看起来没毛病"。
- **根因**：`AudioManager.cs.meta` 的 guid 从 HEAD 的 `8220ab4a80d04b846861d8afb215c4bd` 漂移到了 `56662d7438cc11b42bebf96c5230efa4`。而 `ProjectContext.prefab`（项目级容器）里 AudioManager 组件是**按旧 guid 引用**的：guid 一变，那个组件引用就悬空，`.prefab` 反序列化时该组件解析失败 → `GlobalServicesInstaller._audioManager` 序列化字段值为 `null` → `Container.Bind<AudioManager>().FromInstance(_audioManager).AsSingle()` 把一个 **null 单例**注册到整个项目容器 → 所有 `[Inject] AudioManager` 都解到 null → 首个使用点（`GameFlowManager:44`）抛 NRE。
- **为什么难排查**：链条每一步都"成立了"——`GlobalServicesInstaller` 确实在 `InstallBindings` 里绑了、绑的还是 `AsSingle`、`GameFlowManager` 也确实 `[Inject]` 了。代码全对，唯独 `ProjectContext.prefab` 里那根线断在 **asset 层面**，而 C# 层面毫无痕迹。
- **决定性判据（不必猜绑没绑）**：用 `git diff -- <脚本>.cs.meta` 看 guid 是否变了；再 `grep` 那个 guid 看谁在引用它、谁在拥有它。**`<脚本>.cs.meta` 的 guid 必须是"唯一持有者"，且被 `ProjectContext.prefab` 等消费方按同一个 guid 引用**。guid 一漂移，消费方要么悬空、要么指向别人。
- **迁移判据**：`[Inject]` 解到 null / `FromInstance(null)` 注册了 null 单例 → 先怀疑**DI 没装**，但排掉代码后（绑定存在、`AsSingle`、字段非空）若仍 null，**下一步查 `.meta` guid 漂移**，不要继续在 C# 里找。反序列化失败的序列化字段顶到 DI 注册链，会把 asset 层的问题伪装成代码层的问题。
- **验证法**：`git diff -- Assets/Scripts/<MonoBehaviour>.cs.meta` 应为空（guid 未漂移）；`git grep -l <guid>` 应且只应命中 `.meta` 自身 + 消费方 prefab；漂移后的旧 guid 若残留在别的未跟踪 prefab 里（如测试件）无害，只要不在 DI 路径上即可。

## Unity 序列化陷阱：Component 类型的 prefab 字段必须用「组件 fileID」，不能用根 GameObject 的 fileID

- **现象**：`[CardDragTargetMarker] 未在 GameInstaller 绑定落点图标 prefab`——注入的 prefab 是 `null`，功能降级为空操作；`Scene` 里 `_cardDragTargetIconPrefab` / `_cardDragLinkPrefab` 明明"看起来绑了"。第一直觉是"绑错 prefab 了"，于是去核对 guid、核对是不是拿成别的 prefab。
- **根因**：`GameInstaller.cs` 里这两个字段的静态类型是 **Component**（`CardDragTargetMarkerView` / `CardDragLinkView`），不是 `GameObject`。Unity 对**组件类型**的序列化 prefab 引用，存的是该组件在 prefab 里的 `--- !u!114 &...` **组件 fileID**；把字段指向根 GameObject 的 `--- !u!1 &...` fileID，反序列化时类型不匹配 → 解析为 `null`。我上一轮把 fileID 错写成了根 GameObject 的 `4628901259271367074` / `3502627650508113528`（`!u!1`），所以还是 null。
- **正确值**：组件 fileID 是 `--- !u!114` 对象上的 `&` 号：
  - 图标 `CardDragTargetMarkerView` → `5937579817778867480`（在 `UITest.prefab`）
  - 连线 `CardDragLinkView` → `710961085589720210`（在 `UILinkTest.prefab`）
  - guid 不变（`e569...` / `944f...`），只改 fileID。
- **可迁移判据（每次绑 prefab 都要先判断字段类型）**：
  - 字段是 `GameObject` → 用 `--- !u!1 &...`（根 GameObject）fileID。
  - 字段是 `MonoBehaviour` 子类 / 任意**组件类型** → 用 `--- !u!114 &...`（组件）fileID。**用组件类型去指根对象必成 null**，且 Unity 不报错、不告警，只在运行时表现出"没绑上"。
  - 判定方法：在 prefab 里 `grep -n "!u!114.*&"`（组件）和 `!u!1.*&`（根对象），看哪个 `&` 号对应的 `m_Script`/`m_GameObject` 符合目标。**一条铁律：字段的静态类型决定用哪一层 fileID。**
- **顺带**：日志里的 `Tag: EnemyBuilding / NeutralBuilding / PlayerBuilding is not defined` 是 `ProjectSettings/TagManager.asset` 的 `tags: []` 为空、而代码 `go.tag = "..."` 需要这些 tag 未定义。给 `tags:` 数组补齐脚本里出现的全部 5 个 tag（`EnemyBuilding`、`EnemyUnit`、`NeutralBuilding`、`PlayerBuilding`、`PlayerUnit`）即可，无需改代码。

## 噪声扰动会击穿"按浮点坐标焊接共享顶点"——跨格拼接几何必须用网格拓扑身份

- **现象**：不可放置区域红色遮罩逐格填充，渲染出"马赛克拼接图"的网格纹；同期两版"整体外轮廓追踪"也都失败（第 1 版出 35 个碎环，第 2 版 582 格只追踪出 6 个点 = 一个单格六边形）。当时把两者当成**两个独立问题**，分别去怪"T 型交叉度数 > 2"和"`PickNextByLeftHand` 转角公式方向不对"，两条判断**都是错的**。
- **根因（一个根因，两处症状）**：`HexMetrics.Perturb`（`HexMetrics.cs:42-43`）把每格的 `RealCenterWorldCoordinate` 在 XZ 上**逐格独立**推开 ±0.2。绕这个扰动后的格心画正六边形，相邻格"本该重合"的公共角点实际相差 **0.1~0.4**（六边形半径才 3）：
  - 焊接侧：旧 `Quantize` 按 0.01 精度量化坐标 → 永远焊不到一起 → 内部边一条也剔不掉 → 邻接表退化成 N 个孤立六环 → "只追踪出 6 个点"；
  - 填充侧：逐格填充之间既重叠又漏缝。Stencil 只能消重叠，**对缝隙无能为力**，漏出的背景亮线就是"马赛克"网格纹。
- **修复**：角点身份改用**立方坐标三元组**——每个角点恰由 3 个互为邻居的格共享，把这 3 个坐标排序后作为键，"是否同一角点"变成**整数相等判定**，与扰动彻底无关。顶点位置由**未扰动格心**（`CenterWorldCoordinate + Height * elevationStep`，同 `MeshDataGenerator.SolidAreaCenterWithoutPerturb`）推出，换来严格密铺；代价是遮罩边界与地形 silhouette 最多差 0.2 世界单位，半透明层上不可见。
- **顺带消掉的复杂度（度数不变量）**：任一角点由 3 格共享，按"在集合内的格数"统计边界边度数 = 0/2/2/0，**恒为 0 或 2** → T 型交叉**根本不可能存在** → 边界必然是若干条简单闭环 → 追踪只需"走向非来路的那个邻居"，**不需要左手法则、不需要转角比较**。前两版的脆弱性来源被整体消除，而不是被调参绕过。（原记录里"T 型交叉度数 > 2"与事实正好相反。）
- **可迁移判据**：
  - 本工程凡"**跨格拼接**"的几何（共享顶点、边界提取、区域并集），**一律不能**基于 `RealCenterWorldCoordinate` 做浮点坐标焊接，必须走网格拓扑身份。`MeshDataGenerator.ExtractSphereOfInfluenceBoundary` 的 `QuantizeKey` 有**同一个隐患**，若哪天用到跨格拼接需一并检查。
  - 更一般地：**量化精度必须大于数据本身的噪声幅度**，否则"焊接"静默失效——不报错、不抛异常，只是退化成"每个元素自成一体"，症状看起来像算法逻辑错误，很容易误导排查方向。
  - **半透明逐格填充出现深浅不均的网格纹时，先分清是"叠加"还是"漏缝"**：叠加可以用 Stencil 消，漏缝只能靠顶点共享消。看到网格纹就上 Stencil 是治标。

## 几何单元测试的夹具喂"理想值"是假阳性温床

- **现象**：`PlacementMaskGeometryTests` 全绿，但对应功能在真机上从没正确过；测试反过来给了"几何算法没问题"的错误信心，把排查方向推向了渲染层。
- **根因**：夹具写的是 `cell.RealCenterWorldCoordinate = new Vector3(wx, 0f, wz)` —— **精确格心、未扰动**。而生产数据被 `HexMetrics.Perturb` 推开 ±0.2。被测代码依赖"共享角点坐标完全相等"，这个前提在夹具里成立、在真机里必然不成立，bug 正好从测试缝里溜过去。
- **可迁移判据**：几何/空间类测试的夹具必须复现生产数据的**噪声特性**（扰动、浮点误差、非规则采样），而不是喂教科书式的理想坐标。做法：在夹具里注入**确定性伪随机**扰动（用坐标 hash 出偏移，保证可复现），幅度对齐真实值。新 `PlacementMaskTopologyTests` 即刻意注入 ±0.2——一旦实现回退到读 `RealCenterWorldCoordinate`，用例立刻失败。
- **顺带（编辑器占用时怎么跑测试）**：Unity 编辑器开着会占 `Temp/UnityLockfile`，CLI 测试跑不了。纯数学、不依赖 `MonoBehaviour` 的用例可以用独立 .NET 宿主 + 反射跑（`[SetUp]` + `[Test]` 手工调度，Unity 托管 DLL 用 `AssemblyLoadContext.Default.Resolving` 按文件名兜底解析）。**坑**：Unity 自带的 net35 版 `nunit.framework.dll` 依赖 `System.Runtime.Remoting.CallContext`，在 .NET 8 下加载即炸（表现为**所有**用例以同一条 `Could not load type ... CallContext` 失败——这是环境问题不是测试失败），换标准 NUnit 包即可。

## 轮廓拟合：插值样条只能「磨圆」顶点级噪声，去不了——顺序必须是先简化、再圆角

- **现象**：六边形格子集合的区域边界沿每格锯齿起伏（振幅 = 0.5R）。用 Catmull-Rom 重采样想拟合为曲线，结果只是把锯齿磨圆，边界仍是「沿地块的波浪边」，得不到要求的「直线 + 圆角折线」。
- **根因（顺序不可颠倒）**：Catmull-Rom 是**插值**样条，穿过每个输入点。锯齿**本身就是顶点**，插值只会柔化它们、不会移除它们。要得到直线段，必须先把锯齿当噪声**删掉**（用**逼近**类简化，如 Douglas-Peucker），再对留下的折角做定半径圆角——「先简化、再圆角」。任何平滑/插值都建立在「输入点都已删除到位」的前提下，顺序反了救不回来。
- **闭环 DP 的坑**：闭环没有天然端点，直接跑 DP 会把「起点附近」永久钉死在锯齿上。取离 `loop[0]` 最远的点作第二锚点，拆成两条开链分别简化——两个锚点都落在轮廓极值上，结果对起点选择不敏感。
- **容差的硬约束**：直边上的锯齿振幅 = `R − R·cos60°` = **0.5R**；孤立单格「角点相对邻角连线」的凸起 = **也是 0.5R**。二者在垂距意义上**不可区分**，所以没有任何容差能同时「抹平锯齿」和「保住孤立单格的六角形状」——必须自觉取舍（本方案取 eps=0.6R，代价：孤立单格必然塌成圆润小块）。
- **可迁移判据**：拟合「离散格子集合」的轮廓时，先问想去掉的噪声**是顶点本身、还是顶点之间**。是顶点本身 ⇒ 用逼近算法（DP / 最小包围）先删点；**任何插值样条（Catmull-Rom、插值型 Bezier）都只能磨圆、删不掉**。更一般：**当两种特征在你的简化判据下不可区分（本例振幅同为 0.5R）时，没有任何参数能同时满足两种诉求，只能明确选一个取舍、把它当已知代价写下来**——别再花时间找那个不存在的「两全参数」。

## 带洞多边形填充：桥接 + 耳切在这类输入上是脆的，扫描线偶奇才是对的默认选择

- **现象（两个，前一个是后一个的诱因）**：
  1. 高亮遮罩的填充没铺到描边边界线，描边内侧露白、凸角处填充又溢出到线外；
  2. 改用「共用闭环 + 带洞三角化」修复后，多洞场景下**偶发**填充面积虚高——随机压测稳定 **9/4000** 命中，最差偏差 24%，面积甚至比外环单独还大。
- **根因 1（几何不同源）**：填充走 `topo.CellCorners` 原始六边形角点逐格扇形，描边走 `SimplifyClosed`（DP）→ `RoundCorners` 后的平滑路径 —— **两套不同几何**。凹口处 DP 把路径切到填充之外 → 线内侧露白；凸角处圆角切角 → 填充溢出线外。偏移可达大半个格，远超 `StrokeHalfWidth`，**调粗线盖不住**。修复：填充改吃描边同一批处理后闭环（`PrepareLoops` 的 `_loops`）。这一条是对的，保留。
- **根因 2（耳切在弱简单多边形上静默丢面积）**：为挖洞而做的「嵌套判定 → 桥接 → 耳切」，桥接会把洞用退化双向边缝进外环，产生**弱简单多边形**：同一位置出现重复顶点、零宽通道。耳切在缝合口附近会走到**整圈顶点全为凹或共线**的中间状态 → 一个耳都找不到 → 兜底的 `ForceCut` 因 `bestCross` 从 `0f` 起判也选不出候选 → 返回 false → 循环**带着十几个没消费的顶点 break**，而末尾的 `if (_ring.Count == 3)` 此时不成立，**这些顶点连同它们围出的面积被静默丢弃**，同时已切出的三角互相重叠 → 面积反而偏大。决定性证据是结构不变量：`n=55 tris=41 want=53 leftover=14`（n 边形必须恰好出 n−2 个三角）。
- **修复**：整个换掉，改用**扫描线 + 偶奇规则**。取所有顶点 y 排序去重，相邻两 y 之间是一条「带」；带内无顶点 ⇒ 穿带边集合恒定 ⇒ 带中线求各边 x、排序后**两两配对**（0-1 实心、1-2 空、2-3 实心…），每对在带上下沿各取一次 = 一个梯形 = 2 个三角。于是**嵌套深度判定、绕向归一化、桥接可见性、耳切、凹凸判定全部不需要**，代码从 ~300 行降到 ~150 行且没有兜底分支。偶奇天然处理任意绕向 / 任意嵌套层数 / 多个不连通区域。
- **排查方法上的教训（这次绕了三圈弯路）**：连续三次「看现象猜根因 → 改代码 → 复跑」都失败（先怪 `ContainsOtherVertex` 的 reflex 判定、再怪共线退化、再怪 `Same()` 对重复顶点的豁免）。真正定位靠的是**把可疑策略参数化后做组合扫描**：把 3 个判定各加一个开关跑 8 种组合，结果 **7 种组合失败集完全相同（都是同样那 9 例）** —— 一眼看出「策略无关」，问题不在我猜的任何一处。**当多个互斥的假设改完症状不变时，立刻停止逐个试，改为参数化扫描 + 检查结构不变量**（这里是「n 边形应出 n−2 个三角」），比继续猜快一个数量级。
- **可迁移判据**：
  - **需要挖洞的多边形填充，默认选扫描线偶奇，不要默认选桥接 + 耳切**。耳切适用前提是「严格简单多边形」；一旦为了挖洞而桥接，输入就成了弱简单多边形，正好落在耳切最脆的地方。只有确实需要「三角数最少」或「保留原始顶点」时才值得付耳切的复杂度。
  - **面积和判据抓不住「静默丢弃」类 bug 的原因**：有向面积对任何splice 都恒等（桥的两条边正负抵消），所以「merged 面积 == 期望」看着全对，实则毫无信息量。必须同时查**计数类结构不变量**（三角数 = n−2、剩余顶点数 = 0）。
  - **凡是「找不到合法候选就 break」的循环，都要检查退出时的残留状态**：本例 `break` 之后剩余顶点被后续的 `== 3` 判定静默跳过，既不报错也不告警。这类兜底分支应当断言残留为空，或至少计数上报。
