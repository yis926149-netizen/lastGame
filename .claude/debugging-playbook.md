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
