# 03. 领域模型、ScriptableObject 与配置资产审计报告

## 结论

- 审查日期：2026-07-17
- 审查对象：当前未提交工作区中的 `Assets/Scripts/Core/Models/`、`Assets/Scripts/Data/`、`Assets/Scripts/ScriptableObjects/`、`Assets/Scripts/Core/Services/DataProviders/`、`Enums.cs`、`HexMetrics.cs` 及相关调用方。
- 结果：原始审查发现 6 项 P1、3 项 P2；截至 2026-07-17 复核，全部 9 项均已修复。
- 编译验证：`dotnet build "My project - new.sln" --no-restore` 成功，0 error（仅遗留与本次修复无关的 CS0649/CS0108 等既有告警）。
- 运行验证限制：本机 `2022.3.62f3c1` Editor 因许可握手失败（Failed to handshake / Access token is unavailable）无法在 batchmode 执行测试，EditMode/PlayMode 测试与 Inspector Missing Reference 检查按用户指示跳过；已通过完整 `dotnet` 编译与静态审查确认旧问题模式全部清除。

## 发现

### ✔ [P1][已修复] 文化 8/9 级把普通数据对象当作 Unity Component 获取

- 位置：`Assets/Scripts/Controllers/Tech_CultureTreeController.cs:411`、`:422`、`:423`、`:428`、`:429`；`Assets/Scripts/Core/Models/BuildingData.cs:4`
- 现象：文化达到 8 或 9 级且场上有目标建筑后，文化效果无法读取建筑数据；根据目标运行时具体行为，会在 `GetComponent<BuildingData>()` 处报告非法组件类型，或在随后访问字段时产生空引用。该逻辑在每帧执行，会持续报错。
- 触发条件：文化等级为 8 且存在回血阵，或等级为 9 且存在进攻建筑/回血阵。
- 根因：`BuildingData` 是普通 `[Serializable]` 类，不继承 `Component`；运行时数据实际位于 `BuildingController.buildingData`。项目其他路径已经使用正确访问方式，见 `SettlementPhase.cs:69`。
- 影响：后期文化效果失效，并可能持续污染 Console、打断该帧后续逻辑。
- 证据：`MainGame.csproj` 同时包含两个文件且构建成功，因此这不是编译阻断，而是运行时 API 使用错误。
- 修复方向：先获取 `BuildingController`，再访问其 `buildingData`；文化效果改为仅在等级变化时执行一次。
- 测试建议：创建带 `BuildingController.buildingData` 的三类建筑，分别进入文化 8、9 级并断言无异常且只更新目标字段一次。
- 修复验证：文化效果已移到成功的等级切换点，只执行一次；建筑数据改由 `BuildingController.buildingData` 获取，并新增 7→8、8→9 及同回合重复调用的回归测试。`MainGame.csproj` 与 `Tests.csproj` 均构建成功。

### ✔ [P1][已修复] 玩家文化升级直接修改共享 ScriptableObject 模板

- 位置：`Assets/Scripts/Controllers/Tech_CultureTreeController.cs:327`、`:353`、`:385`、`:417`；`Assets/Scripts/Core/Services/DataProviders/UnitDataProvider.cs:24`；`Assets/Scripts/Core/Services/DataProviders/BuildingDataProvider.cs:28`
- 现象：玩家文化达到 5、6、7 级后，`UnitDatabaseSO.unitDatas` 中的模板被原地修改；9 级会把 `BuildingDatabaseSO.buildingBaseHP` 全部改成 40。AI 后续生成同 ID 对象时读取相同模板，同一 Editor 会话重新开局也可能继续沿用被修改的数据。
- 触发条件：玩家完成对应文化升级，随后 AI 或玩家生成受影响对象，或不重启 Editor 重新加载游戏场景。
- 根因：Provider 返回资产中的可变对象原引用，升级状态没有按玩家/阵营或单局隔离。
- 影响：玩家升级越权增强 AI，模板数据与单局状态混合，重开局基线不可靠。
- 证据：玩家生成路径 `CardPresenter.cs:259-264` 和 AI 生成路径均读取同一个 `IUnitDataProvider.GetUnitData()`；`SetBuildingBaseHP` 循环覆盖资产内整个列表。
- 修复方向：SO 只保存基础模板；创建运行时 `CharacterData`/`BuildingData` 时复制基础值，并从玩家独立的升级状态应用修正。
- 测试建议：升级玩家后生成玩家和 AI 的同 ID 单位，断言只有玩家获得增益；重新加载场景后断言模板值未变化。
- 修复验证：`CharacterData` 现在复制基础 `UnitData`，文化升级不再写入 Unit/Building SO，`SetBuildingBaseHP` 写接口已移除。玩家创建路径按当前文化等级修改独立运行时实例，AI 路径只保留基础值。新增测试验证玩家/AI 单位不共享模板、玩家建筑升级不改变 Provider 基础 HP；`MainGame.csproj` 与 `Tests.csproj` 均构建成功。

### ✔ [P1][已修复] 单位死亡只销毁 GameObject，未清理地块占用和仓库

- 位置：`Assets/Scripts/Controllers/UnitMovementController.cs:238`、`:248`；`Assets/Scripts/Core/Models/HexCellData.cs:294`；`Assets/Scripts/Core/Services/CardPresenter.cs:244`
- 现象：死亡单位消失后，原地块的 `HaveUnit.Key` 仍为 `true`，仓库也保留死亡单位数据。部署规则继续把该地块视为被占用。
- 触发条件：任意单位生命降到 0，并等待 2.2 秒销毁动画结束。
- 根因：死亡路径只调用 `Destroy(gameObject)`，没有调用 `SetHaveUnit(false, null)`，也没有按阵营从 `IUnitRepository` 移除。
- 影响：空地块无法再部署，仓库含悬空模型，地块、领域仓库和场景对象失去一致性。
- 证据：全项目清理占用的业务路径只有移动和 AI 建城；`UnitDeath()` 没有调用任何仓库移除接口。
- 修复方向：建立唯一的单位移除入口，按“核对当前占用者、清地块、移仓库、清选择/命令、销毁对象”的顺序执行，并防止死亡逻辑每帧重复调度 `Invoke`。
- 测试建议：分别击杀玩家与 AI 单位，断言地块、仓库、选择状态和场景对象同步清空，死亡结算只发生一次。
- 修复验证：新增 `UnitRemovalService` 作为唯一移除入口，按占用者核对、清地块、取消移动、重置攻击/选择、按阵营移仓库、销毁对象的顺序执行，并对同一对象幂等。`UnitDeath()` 增加一次性 `_isDeathScheduled` 守卫，死亡表现与 `Invoke` 只调度一次；`UIManagerPresenter` 与 `PlayerInputHandler` 监听仓库移除事件同步清选择。新增玩家、AI、重复移除、占用者核对、冲刺位置及移动取消测试。`MainGame.csproj` 与 `Tests.csproj` 均构建成功。

### ✔ [P1][已修复] 玩家移民建城后保留单位占用和仓库记录

- 位置：`Assets/Scripts/UI/UIController.cs:273`；对照 `Assets/Scripts/Core/Interfaces/IAIManager.cs:680`
- 现象：玩家城市生成后只销毁移民 GameObject；城市格仍记录已销毁的单位，玩家仓库也仍持有该移民。AI 建城路径则正确清理二者。
- 触发条件：玩家使用 ID 0 的移民建立城市。
- 根因：玩家和 AI 使用两套不对称的移民消费逻辑；玩家路径缺少 `cell.SetHaveUnit(false, null)` 和 `_unitRepository.RemovePlayerUnit(unit)`。
- 影响：城市格同时具有建筑占用和逻辑单位占用，仓库含无效对象，后续部署和结算行为不一致。
- 证据：AI 路径在生成城市后依次清地块、移仓库、销毁对象；玩家路径只有 `Destroy(unit)`。
- 修复方向：玩家和 AI 都调用统一的单位移除服务消费移民。
- 测试建议：玩家与 AI 分别建城，断言移民对象、仓库和 `HexCellData.HaveUnit` 最终状态一致。
- 修复验证：玩家路径 `UIController.PlayerBuildCity` 与 AI 路径 `TryFoundCityWithSettler` 现在都调用 `UnitRemovalService.RemoveUnit` 消费移民，两条路径对称清理地块占用、玩家/敌方仓库并销毁对象。`MainGame.csproj` 构建成功。

### ✔ [P1][已修复] 玩家卡池永久排除第 16 张卡并绕过科技文化解锁

- 位置：`Assets/Scripts/Core/Services/CardService.cs:43`、`:54`、`:65`；`Assets/Scripts/ScriptableObjects/UnitDatabase.asset:15`；`Assets/Scripts/ScriptableObjects/BuildingDatabase.asset:35`
- 现象：当前资产包含 12 张单位卡和 4 张建筑卡，全局 ID 为 0-15；随机范围 `_random.Next(0, 15)` 只能生成 0-14，因此 ID 15 的科技文化建筑永远不会进入玩家手牌。玩家卡池也完全不受科技文化等级限制。
- 触发条件：第一回合固定移民卡之后的任意玩家抽卡。
- 根因：总卡数硬编码为 15，已有解锁 Provider 调用被整段注释。
- 影响：玩家无法获得唯一增加每回合科技文化产出的建筑；玩家和 AI 使用不同解锁规则。
- 证据：真实资产数量为 12+4；现有测试使用虚构的 10+5 配置且只断言 0-14，无法发现缺失 ID 15。
- 修复方向：从 Provider 读取真实卡数，并恢复经过校正的显式解锁集合。
- 测试建议：使用真实 12+4 配置和可控随机源，断言 ID 15 可生成、所有 ID 均合法，并逐级验证玩家卡池。
- 修复验证：`CardService.GenerateNextCardID` 恢复调用 `ICardUnlockRuleProvider`，玩家与 AI 共用同一解锁集合并从 Provider 读取真实卡数，硬编码 `_random.Next(0,15)` 已移除。新增 `CardServiceTests` 断言解锁池含 ID 15 且只从解锁集合抽取。`MainGame.csproj` 与 `Tests.csproj` 均构建成功。

### ✔ [P1][已修复] AI 解锁规则错误地把科技文化等级等同于数据库连续下标

- 位置：`Assets/Scripts/Core/Services/CardUnlockRuleProvider.cs:18`；`Assets/Scripts/Data/TechData.cs:30`；`Assets/Scripts/Data/CultureData.cs:29`
- 现象：AI 解锁的单位和建筑与科技文化界面描述不一致。例如科技节点描述与 `UnitDatabase.asset` 中单位业务顺序不同，文化首项描述回血建筑，但连续下标规则先解锁进攻建筑；最高科技等级仍无法解锁单位 ID 11。
- 触发条件：AI 依据科技文化等级生成卡牌。
- 根因：规则用 `i <= level + 1` 和 `i <= level` 推导连续 ID，没有显式的节点到卡牌 ID 映射。
- 影响：AI 提前获得错误卡牌、延迟或永久缺失应解锁卡牌，玩法规则与 UI 文本不一致。
- 证据：单位资产顺序为移民、蘑菇、仙人掌、巫妖等，建筑资产顺序为进攻、防御、回血、科技文化；这两个顺序都不是数据类描述的科技文化节点顺序。
- 修复方向：每个科技/文化节点配置明确的全局卡 ID 列表，并在资产导入或启动时验证 ID 存在且唯一。
- 测试建议：建立等级 0-9 的期望累计解锁快照，逐级断言新增集合，并验证玩家与 AI 共用同一规则。
- 修复验证：`CardUnlockRuleProvider` 改用显式节点→全局卡 ID 映射（基础单位 0/1；科技 0-9 依次 2,3,4,9,10,11,6,5,7,8；基础建筑含科技文化建筑；文化 0-2 依次回血、进攻、防御），并对映射越界或重复 ID 快速失败。新增 `CardUnlockRuleProviderTests` 逐级断言累计解锁快照且无重复。`MainGame.csproj` 与 `Tests.csproj` 均构建成功。

### ✔ [P2][已修复] `HealthPack` 可被随机生成，但没有地图显示和收割效果

- 位置：`Assets/Scripts/Utilities/Enums.cs:58`；`Assets/Scripts/Managers/MapGenerator.cs:215`；`Assets/Scripts/Managers/MapRenderer.cs:866`；`Assets/Scripts/UI/UIController.cs:469`；`Assets/Scripts/ScriptableObjects/EnvironmentModels.asset:20`
- 现象：地块可获得 `ResourceType.HealthPack`，但渲染器跳过所有索引大于等于 4 的资源，资产只有 4 个资源模型，收割 switch 也没有 `HealthPack` 分支。该资源不可见，收割后直接消失且无效果。
- 触发条件：符合资源生成条件的地块随机得到枚举值 4。
- 根因：`HealthPack` 作为“击杀掉落”加入枚举后，随机地图候选池没有排除它，显示和交互契约也未同步。
- 影响：地图包含不可见、无收益的逻辑资源，固定种子下可稳定造成体验和规则错误。
- 证据：生成器可 Clamp 到 4；渲染器明确 `>= 4` 跳过；收割逻辑只处理 0-3。
- 修复方向：若它仅由击杀产生，从地图随机池排除；否则补齐 Prefab、显示和回血处理。
- 测试建议：遍历每个非 `None` 资源，断言生成资格、模型和收割效果形成闭环。
- 修复验证：地图随机资源改由 `MapGenerator.MapRandomResourceRoll` 归一化，仅 `0-3`（Animals/Plants/Minerals/Chest）可落地，其余全部映射 `None`，`HealthPack` 不再进入随机地图池。新增 `DomainInvariantTests` 参数化断言该契约。`MainGame.csproj` 构建成功。

### ✔ [P2][已修复] 文化 7/9 级的目标 ID 和属性映射存在复制错误

- 位置：`Assets/Scripts/Controllers/Tech_CultureTreeController.cs:385`、`:417`、`:420`、`:426`
- 现象：7 级重复两次设置 ID 5 攻击和 ID 6 防御，新生成的 ID 5 缺少防御升级、ID 6 缺少攻击升级，而已有单位同时升级两项。9 级描述升级进攻/防御建筑，却覆盖全部 12 个基础 HP 槽，并更新已有进攻建筑和回血阵，漏掉防御建筑。
- 触发条件：文化达到 7 或 9 级，比较升级前已有对象与升级后新生成对象。
- 根因：重复分支中的 ID/字段复制错误，并用无类型的并行 HP 列表承载建筑规则。
- 影响：同阵营同类型对象的属性取决于生成时间；非目标建筑被增强，目标建筑反而可能未增强。
- 证据：7 级已有对象分支会同时设置攻击和防御，但模板分支不会；9 级 `SetBuildingBaseHP` 无条件遍历整个列表。
- 修复方向：用显式、带建筑/单位 ID 的升级规则表统一计算已有和新对象属性。
- 测试建议：逐级对每个目标 ID 做升级前、已有对象、升级后新对象三组属性断言。
- 修复验证：文化 7 级单位升级统一走 `ApplyUpgradedUnitStats`；文化 9 级目标改为进攻+防御建筑（不再误伤回血阵），已有与新建对象共用 `ApplyUpgradedBuildingStats`。`TechCultureServiceTests` 补断言防御建筑升级、回血阵 HP 不变，且 `ApplyPlayerCultureBonus` 9 级只作用进攻/防御。`MainGame.csproj` 与 `Tests.csproj` 均构建成功。

### ✔ [P2][已修复] 三条治疗路径允许领域生命值超过最大生命值

- 位置：`Assets/Scripts/UI/UIController.cs:496`；`Assets/Scripts/Turn/SettlementPhase.cs:47`、`:70`
- 现象：植物、农田和回血阵都直接累加 `currentHp`，没有限制到 `unitData.hp`。Slider 可能只在视觉上钳制，领域数据仍可超出最大值。
- 触发条件：满血单位接受治疗，或缺失生命小于单次治疗量。
- 根因：治疗规则分散在 UI 和结算代码中，没有统一的领域方法维护生命范围不变量。
- 影响：后续伤害、死亡判断和 UI 比例以非法生命值为输入，单位获得额外有效生命。
- 证据：三处均直接执行 `currentHp += ...`，未调用 `Mathf.Min` 或统一治疗接口。
- 修复方向：建立领域层治疗入口，将结果限制到 `[0, maxHp]`，再从领域值刷新 UI。
- 测试建议：对满血和仅缺 1 HP 的单位分别执行三种治疗，断言最终生命不超过最大值。
- 修复验证：新增 `CharacterData.Heal`，将 `currentHp` 钳制到 `[0, unitData.hp]` 并据此刷新血条；农田、回血阵、植物三条治疗路径统一改调该方法。新增 `DomainInvariantTests` 断言满血与仅缺 1 HP 场景均不超过上限。`MainGame.csproj` 与 `Tests.csproj` 均构建成功。

## 已核对通过

- `UnitDatabase.asset` 的 `unitModels`、`unitDatas`、`Cards`、`unitIcons`、`skillIcons` 均为 12 项；单位 ID 为连续且唯一的 0-11。
- 12 个单位当前生命、攻击、移动、射程、防御和视野均无负值。
- `BuildingDatabase.asset` 有 4 个可部署建筑 Prefab 和 4 张建筑卡，当前顺序均为进攻、防御、回血、科技文化。
- `TechData`、`CultureData` 的名称、描述、成本和 `TechTreeIcons.asset` 图标当前各为 10 项，成本为正且递增。
- `MapGenerationConfig.asset` 当前为 11x11、半径 3、实地区比例 0.7、河流长度 5-13、概率分母 30，未发现负值、零分母或反向区间；噪声、地形、河流和湖海材质字段均已配置。
- `LandFormType` 的 4 个实体值与 `EnvironmentModels.asset` 的 4 个地貌 Prefab 对齐，`None` 被渲染器排除。
- 六方向枚举 0-5 与邻接服务映射一致，`None=6` 不参与正常邻接查询。
- `HexCellData` 的网格列表均按实例初始化，未发现不同地块共享同一可变 List 的浅拷贝。

## 待验证风险

- 项目 YAML 使用 Tuanjie 的 `tag:yousandi.cn,2023`。部分 `.meta` GUID 表示与资产 YAML 中的 32 位 GUID 无法用普通文本匹配，必须在 `2022.3.62f3c1` 中重新导入后，通过 `AssetDatabase`/Inspector 检查所有 Sprite、Prefab、Material 和 Script 引用是否为空；静态审查不将其直接定性为断链。
- 未在 Editor 中验证 `GetComponent<BuildingData>()` 的具体异常文本，但普通数据类不是有效 Unity Component，且正确数据所有者已确认是 `BuildingController.buildingData`。
- 现有测试没有覆盖真实 12+4 卡池、资产完整性、显式解锁表、HealthPack 闭环、模板污染、单位死亡/建城后的地块一致性或生命上限。
