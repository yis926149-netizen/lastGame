# Excel配置表混合架构修改计划

## 1. 改造目标

将当前以零散 ScriptableObject、Inspector 字段和 C# 默认值共同决定运行数据的方式，改造为：

```text
策划维护 Excel（唯一数值源）
        ↓ 独立导出工具：解析、规范化、校验
中间数据（UTF-8 CSV + JSON，可审查、不可手改）
        ↓ Unity Editor 导入器：交叉引用、资源校验、生成
运行时数据库 SO（自动生成、只读）
        +
Unity 资源 SO（Prefab / Sprite / 音效等人工维护）
        ↓ 按稳定 ID 合并
Provider / Service / 游戏运行逻辑
```

核心原则：

1. Excel 是纯数值配置的唯一事实来源。
2. SO 继续作为 Unity 资源引用和最终运行数据载体，不再作为策划直接维护数值的主要入口。
3. 代码默认值只允许用于新建模板或明确的容错，不得静默覆盖缺失配置。
4. 所有实体使用稳定字符串 ID 关联，禁止使用列表索引、显示名称或天赋 ID 承担隐藏业务语义。
5. 导入必须可重复：相同 Excel 输入生成完全一致的中间数据和运行 SO。
6. 配置错误必须在编辑期或构建前失败，不能等到运行时才以空数组、0值或空引用暴露。
7. 自动生成文件禁止人工修改；每次生成应留下源版本、内容哈希和生成时间。

## 2. 当前问题与改造约束

本计划针对当前项目已经确认的问题设计：

- 单位数值分散在14个 `UnitConfigSO`，全部 `AttackInterval=0`，难以横向发现。
- 普通卡池、公共建筑库、资源库、地貌库依靠手工拖引用，已出现空数据库仍可运行的情况。
- 探索奖励的 C# 默认值与实际 SO 值明显不同，文档曾误把默认值当作运行值。
- 天赋 ID 同时被效果系统和抽卡偏好解释，形成隐式耦合。
- 天赋文案“+25%”与运行倍率 `4` 不一致。
- 战术卡配置了数值，但部分效果未实现。
- SO 同时承载数值、Prefab、Sprite、音效、描述和玩法语义，策划改数值时必须操作复杂资源资产。
- 数组在 Unity YAML 中可能序列化为十六进制字节串，不利于代码审查。
- 项目存在异常 `.meta` GUID；在迁移配置前必须先确保核心脚本和资产引用可稳定解析。

改造期间不得一次性删除旧 SO。采用双轨迁移和逐表切换，保证每一阶段都可以运行、验证和回滚。

## 3. 技术选型

### 3.1 Excel 文件

根目录新增：

```text
Config/
  Excel/
    游戏数值配置.xlsx
  Schema/
    配置表结构.json
  Generated/
    Csv/
    game-config.json
    game-config.manifest.json
    README.md
```

约定：

- `游戏数值配置.xlsx` 是唯一允许策划编辑的数值源。
- 一个工作簿包含多张业务表，不使用公式作为最终运行值。
- 枚举值写稳定英文代码，例如 `Melee`、`StatMultiplier`，不写枚举整数。
- ID 使用稳定英文小写标识，例如 `unit.archer`、`building.gold_mine`。
- 中文名称和描述作为普通字段保存，不作为关联键。
- 空白、0、false必须有不同语义，导出器不得混为一谈。
- 数组和多对多关系使用独立子表，不在单元格中拼接逗号字符串。

### 3.2 Excel 导出工具

新增独立工具：

```text
Tools/ConfigExporter/
  ConfigExporter.sln
  src/ConfigExporter/
  tests/ConfigExporter.Tests/
```

建议使用独立 .NET 8 命令行工具和 ClosedXML 读取 `.xlsx`。理由：

- Excel 解析依赖不进入 Unity 运行程序集和最终包体。
- 可以在命令行和 CI 中无界面执行。
- 单元测试不依赖 Unity Editor。
- 比直接在 Unity 中引用 Excel DLL 更容易控制版本和报错信息。

标准命令：

```powershell
dotnet run --project Tools/ConfigExporter/src/ConfigExporter -- `
  --input Config/Excel/游戏数值配置.xlsx `
  --schema Config/Schema/配置表结构.json `
  --output Config/Generated `
  --strict
```

导出结果：

- 每张表一份规范化 UTF-8 CSV，便于 Git 比较。
- 一份完整 `game-config.json`，供 Unity 导入。
- 一份 manifest，记录 schema版本、工作簿SHA-256、导出工具版本、各表行数和输出哈希。
- 输出使用固定字段顺序、固定小数格式、固定换行，保证确定性。

`Config/Generated` 只能由工具覆盖，文件头和 README 明确禁止手改。

### 3.3 Unity 导入器

新增 Editor 程序集：

```text
Assets/GameConfig/Editor/GameConfig.Editor.asmdef
Assets/GameConfig/Editor/Import/
Assets/GameConfig/Editor/Validation/
Assets/GameConfig/Editor/Windows/
```

导入器职责：

1. 读取 `Config/Generated/game-config.json`。
2. 校验 schema 版本和源文件哈希。
3. 解析为与 Unity 无关的 DTO。
4. 按稳定 ID 检查跨表引用。
5. 将数据写入自动生成的运行数据库 SO。
6. 检查人工维护的资源 SO 是否能按 ID 完整匹配。
7. 生成验证报告，存在错误时拒绝覆盖上一次有效运行配置。

导入入口：

- 菜单：`Tools/游戏配置/导入并校验`。
- 菜单：`Tools/游戏配置/仅校验`。
- 可选自动导入：检测 `game-config.json` 内容哈希变化后执行。
- 构建前强制执行严格校验。

禁止在运行时读取 Excel、CSV或JSON。Player只读取已经生成并由场景绑定的 SO。

## 4. 数据职责边界

### 4.1 Excel 管理的内容

以下字段属于平衡数值和玩法规则，应进入 Excel：

- 单位：ID、名称、HP、攻击、防御、射程、移动力、视野、攻击间隔、策略类型、卡费、启用状态。
- 建筑：ID、名称、类型、HP、是否阻挡移动、卡费、产出单位ID、出兵间隔、收入速率、攻击参数。
- 普通卡池：卡ID、类型、抽取权重、启用状态、是否首张保底。
- 战术卡：ID、名称、描述、效果类型、治疗比例、攻击倍率、速度倍率、持续时间、初始数量。
- 天赋卡：ID、名称、描述模板、效果类型、目标属性、数值、抽取权重、可否重复。
- 天赋抽卡偏好：独立规则ID、触发条件、目标卡类型、权重倍率；不得再由天赋ID隐式推断。
- 探索奖励：奖励类型权重、金币档位、单位数量档位、探索费用。
- 探索奖励池：奖励类型、目标实体ID、权重、启用状态。
- 地图资源：ID、名称、描述、拾取效果类型和参数、收割金币、生成权重。
- 地貌：ID、名称、描述、效果类型和参数、禁建规则、生成权重、簇生成参数。
- 公共建筑：ID、名称、夺取HP、防守HP、占格形状、生成权重、每局数量上限。
- 地图生成、山脉、竞技场、迷雾参数。
- 经济、游戏流程、AI节奏等目前散落在 const 和 `[SerializeField]` 中的平衡参数。

### 4.2 Unity资源 SO 管理的内容

人工维护但不由 Excel 写入：

- Prefab和敌我双方Prefab。
- Sprite、卡面、类型图标、技能图标。
- 音效资源或现有音效键。
- 粒子特效、材质、动画资源。
- World Space 标记预制体。
- 仅与Unity表现相关且无需批量平衡的曲线和资源引用。

建议拆分为：

```text
UnitResourceSO
BuildingResourceSO
TacticalCardResourceSO
TalentCardResourceSO
MapResourcePresentationSO
MapLandFormPresentationSO
PublicBuildingResourceSO
GameResourceCatalogSO
```

每个资源 SO 必须包含对应的稳定 `configId`，由 `GameResourceCatalogSO` 汇总。资源目录不再保存 HP、攻击、卡费、权重等纯数值。

### 4.3 自动生成的运行 SO

新增生成目录：

```text
Assets/GameConfig/Generated/
  GameBalanceDatabase.asset
  UnitBalanceDatabase.asset
  BuildingBalanceDatabase.asset
  CardBalanceDatabase.asset
  ExplorationBalanceDatabase.asset
  MapBalanceDatabase.asset
  GameConfigBuildInfo.asset
```

这些资产：

- 由导入器全量重建或稳定覆盖。
- Inspector 显示“自动生成，禁止手改”。
- 不允许在其上保存 Prefab、Sprite 等人工资源引用。
- 数据条目按稳定 ID 排序，避免 Excel 行顺序导致无意义 diff。
- 对外暴露只读接口或只读集合。

运行时 Provider 使用：

```text
BalanceDatabase：提供纯数据
ResourceCatalog：提供Unity对象引用
Provider：按configId合并并向旧业务接口提供数据
```

## 5. 稳定ID规范

统一采用带命名空间的字符串ID：

```text
unit.settler
unit.archer
unit.swordsman
building.barracks
building.arrow_tower
building.gold_mine
tactical.repair
tactical.battle_order
talent.damage
talent.building_hp
talent.gold
resource.animals
landform.gold_mine
public_building.fort
```

规则：

- 仅允许小写英文字母、数字、下划线和单个点分段。
- ID创建后原则上不可改名；显示名称可随时修改。
- 删除数据使用 `enabled=false` 过渡，不立即复用ID。
- 存档、日志、跨表引用全部使用稳定ID。
- 现有整数 `unitData.id` 和 `buildingId` 在迁移期保留为 `legacyId`，只用于兼容旧代码，不再创建新业务依赖。
- 所有ID必须全表唯一；数据库内建立字典，禁止按列表索引查找。

## 6. Excel工作表设计

首期工作簿建议包含以下工作表：

| 工作表 | 主键 | 主要用途 |
|---|---|---|
| `说明` | 无 | 维护规则、枚举说明、版本和负责人 |
| `单位` | `unitId` | 单位战斗数值、策略、卡费 |
| `建筑` | `buildingId` | 建筑数值、产出和经济参数 |
| `普通卡池` | `entryId` | 普通卡启用、权重、保底 |
| `战术卡` | `cardId` | 战术效果参数和初始数量 |
| `天赋卡` | `talentId` | 天赋效果参数、权重、可重复规则 |
| `天赋抽卡规则` | `ruleId` | 消除天赋ID与抽卡偏好的硬编码 |
| `探索奖励类型` | `rewardType` | 类型权重和探索费用 |
| `探索金币档位` | `tierId` | 金币档位及权重 |
| `探索单位数量档位` | `tierId` | 单位数量及权重 |
| `探索奖励池` | `entryId` | 单位、战术卡、建筑奖励池 |
| `地图资源` | `resourceId` | 拾取效果、收割奖励、生成权重 |
| `地图地貌` | `landFormId` | 地貌效果和生成规则 |
| `公共建筑` | `publicBuildingId` | HP、形状、生成参数 |
| `地图生成` | `configKey` | 地图、河流、高度、迷雾、竞技场参数 |
| `山脉生成` | `configKey` | 山脉生成参数 |
| `经济` | `configKey` | 起始金币、基础收入、AI补贴等 |
| `游戏流程` | `configKey` | 游戏时长、结算延迟等 |
| `AI参数` | `configKey` | 出牌、探索、优先级和难度档 |

不建议使用一张超宽“总表”。实体表、关系表和全局参数表应分开。

### 6.1 单位表示例字段

| 字段 | 类型 | 约束 |
|---|---|---|
| `unitId` | string | 必填、唯一、匹配ID规范 |
| `legacyId` | int | 迁移期唯一，完成迁移后可移除 |
| `displayName` | string | 必填 |
| `enabled` | bool | 必填 |
| `strategyType` | enum | `Melee/Ranged/Settler` |
| `hp` | float | `> 0` |
| `attack` | float | `>= 0` |
| `defense` | float | `>= 0` |
| `attackRange` | int | `>= 0` |
| `movementPoints` | float | `> 0` |
| `viewPoints` | int | `>= 0` |
| `attackIntervalSeconds` | float | 非移民必须 `> 0` |
| `cardCost` | int | `>= 0` |

### 6.2 关系表原则

以下关系必须拆成逐行记录：

- 普通卡池包含哪些卡。
- 探索奖励池包含哪些单位、战术卡和建筑。
- 公共建筑占用哪些相对格。
- 数值档位及其权重。

禁止使用 `unit.archer,unit.swordsman` 形式的单元格列表，因为它不利于逐项校验、权重扩展和Git审查。

## 7. Schema与校验体系

### 7.1 导出阶段校验

独立导出工具负责：

- 工作表存在且表头完全匹配 schema。
- 必填单元格不为空。
- 单元格类型、数字范围、枚举值合法。
- 主键唯一且符合ID规范。
- 小数不含百分号或本地化逗号等歧义格式。
- 禁止公式错误、合并单元格和隐藏的业务数据行。
- 跨表引用目标存在且启用状态合理。
- 关系表无重复行。
- 权重非负且需要抽取的集合总权重大于0。
- 错误报告精确到“工作表、行、列、字段、原值、规则”。

### 7.2 Unity导入阶段校验

Unity Editor负责：

- 每条数值记录能找到且只找到一个资源 SO。
- Prefab、Sprite和必需音效引用不为空。
- `NormalCardPool` 非空，且恰好一个首张保底配置；保底卡必须在池中。
- 公共建筑、资源、地貌等要求启用的数据库不为空。
- 非移民单位 `attackIntervalSeconds > 0`。
- 兵营 `producedUnitId` 指向有效单位。
- 箭塔等需要攻击参数的建筑配置完整。
- 天赋描述与效果由统一格式化器生成或校验，不允许“+25%”与倍率4并存。
- 配置了但代码尚未支持的效果类型直接报错，不允许仅打印日志后继续。
- 探索奖励池不得包含规则禁止的实体；若允许金矿，应删除旧Tooltip限制并将规则写入表。
- 场景中的 `GameInstaller` 引用均能解析。
- 所有项目 `.meta` GUID 符合 `^[0-9a-f]{32}$`，核心配置引用不存在悬空。

### 7.3 构建门禁

实现 `IPreprocessBuildWithReport`：

- Excel哈希必须与 manifest 和生成SO记录一致。
- 不允许存在未导入的Excel修改。
- 严格校验有任何错误即终止构建。
- 警告按允许列表管理，不能无限积累。
- CI执行导出后检查工作区是否产生未提交差异，防止漏提交生成物。

## 8. 运行时代码改造

### 8.1 新接口

新增只读接口，示意：

```csharp
public interface IGameBalanceDatabase
{
    UnitBalanceData GetUnit(string unitId);
    BuildingBalanceData GetBuilding(string buildingId);
    IReadOnlyList<NormalCardPoolEntry> GetNormalCardPool();
}

public interface IGameResourceCatalog
{
    UnitResourceSO GetUnit(string unitId);
    BuildingResourceSO GetBuilding(string buildingId);
}
```

Provider将两者合并为运行视图。业务代码不直接访问 Excel DTO，也不直接遍历SO列表。

### 8.2 兼容层

迁移期保留现有接口：

- `IUnitDataProvider.GetUnitData(int unitId)`。
- `IBuildingDataProvider` 的整数ID查询。
- 当前卡牌和生成服务所需的 `UnitConfigSO` / `BuildingConfigSO` 入口。

兼容层通过 `legacyId → configId` 映射转发到新数据库。新增代码一律使用字符串 `configId`，旧整数接口标记为待废弃。

### 8.3 默认值策略

- DTO字段不设置会改变玩法的隐式默认值。
- 必填值缺失应在导入时失败。
- 可选字段必须在 schema 中声明默认值及适用条件。
- 运行时找不到ID时抛出带上下文异常，不回退到列表第一项、魔法ID或固定数值。
- `?? 1.5f`、固定卡费10、探索费50等容错应逐项移除或改为明确的开发错误。

## 9. 分阶段实施计划

### 阶段0：修复工程元数据和建立基线

目标：确保迁移前的Unity资源引用可信。

工作项：

1. 备份当前工程和 `Library`，记录当前可运行场景。
2. 清点并修复异常 `.meta` GUID，优先处理核心脚本、核心SO、场景引用和卡面图标。
3. 在Unity中重新绑定 `GameScene` 的 Installer 字段。
4. 修复 Missing Script、空图标和悬空资源引用。
5. 增加 `.meta` GUID格式及引用完整性检查。
6. 运行现有测试并记录基线运行结果。

完成标准：删除并重建 `Library` 后项目仍能打开、编译、进入 `GameScene`，核心Installer字段均非空。

### 阶段1：搭建导出、Schema和验证骨架

目标：建立不接入业务逻辑的完整数据通路。

工作项：

1. 创建 `游戏数值配置.xlsx`、schema和目录规范。
2. 创建 `.NET 8 + ClosedXML` 导出工具。
3. 支持Excel→CSV/JSON/manifest确定性导出。
4. 创建 Unity Editor 导入器和独立Editor程序集。
5. 创建配置验证窗口和文本/JSON报告。
6. 增加导出工具单元测试与Unity EditMode测试。

完成标准：示例单位表可从Excel导出并生成测试SO；连续执行两次输出无差异。

### 阶段2：迁移单位、建筑和普通卡池

目标：先迁移问题最明显、依赖最核心的三张表。

工作项：

1. 将14个单位和7个建筑的实际值录入Excel。
2. 明确并修正当前异常值，尤其是所有单位攻击间隔0。
3. 将普通卡池四张卡和保底规则录入关系表。
4. 拆分 `UnitResourceSO` 和 `BuildingResourceSO`。
5. 生成单位、建筑和卡池运行数据库。
6. 改造 `UnitDataProvider`、`BuildingDataProvider`、`CardUnlockRuleProvider`。
7. 保留旧整数ID兼容映射。
8. 对比迁移前后除已批准修正项外的运行数据快照。

完成标准：单位生成、建筑生成、抽卡、玩家扣费和AI扣费全部从新数据库取值；普通卡池和保底卡不再依赖手工列表。

### 阶段3：迁移战术卡、天赋卡和抽卡规则

目标：消除ID串线和文案/数值不一致。

工作项：

1. 战术卡和天赋卡数值进入Excel。
2. 图标和卡面迁入资源SO。
3. 将天赋抽卡偏好改为独立规则表。
4. 使用效果枚举和显式目标类型，不使用天赋ID分支。
5. 建立效果描述格式化器，由数值生成“+30%”“×4”等文案，或严格校验人工文案。
6. 未实现的战斗号令效果不得标记为可用；选择实现后再在表中启用。
7. 添加重复天赋、叠加方式和上限规则字段。

完成标准：选择任意天赋不会产生未配置的抽卡偏好；显示文本与实际效果一致；所有启用战术卡均有真实执行路径。

### 阶段4：迁移探索奖励、资源、地貌和公共建筑

目标：清除空数据库和默认值混淆。

工作项：

1. 迁移探索类型权重、费用、金币档位、单位数量档位和奖励池关系。
2. 明确单位数量0是否允许；若不允许，schema设置最小值1。
3. 确认金矿是否允许作为探索建筑奖励，并统一表规则、Tooltip和代码。
4. 迁移资源和地貌数值，补齐数据库启用列表。
5. 地貌普通散落与山脉独立配置，避免用空地貌库解释山脉开关。
6. 迁移公共建筑数值及占格关系，配置为空时严格报错。
7. 将数据库列表改为导入器按 `enabled` 自动生成。

完成标准：固定种子地图的奖励、资源、地貌和公共建筑生成结果可复现；不存在意外空池。

### 阶段5：迁移地图、经济、流程和AI硬编码

目标：逐步收口 `需配表数值统计.md` 中的高优先级硬编码和重复定义。

建议顺序：

1. 经济和探索费用。
2. 游戏流程时间参数。
3. AI节奏、补贴和优先级。
4. 地图生成与竞技场参数。
5. 战斗公式共享系数。
6. 动画和手感参数，仅迁移确实需要策划调整的部分。

同一数值切换到新表后，必须删除旧 `const`、Inspector字段或静默兜底，避免形成新旧双事实。

### 阶段6：移除旧数据路径

目标：完成单一事实来源收口。

工作项：

1. 全项目搜索旧SO数值字段读取点。
2. 移除不再使用的字段和数据库手工列表。
3. 移除整数ID业务分支和魔法数。
4. 将旧配置资产移入迁移归档目录，确认一个版本后再删除。
5. 更新开发文档、策划操作手册和构建说明。

完成标准：修改Excel后执行一次导出/导入即可影响运行；不存在必须同步修改C#默认值或旧SO数值的情况。

## 10. 测试计划

### 10.1 导出工具测试

- 每种字段类型和枚举解析。
- 中文、空白、非法数字和公式错误。
- 重复ID、非法ID和跨表悬空引用。
- 权重为负、总权重为0。
- 确定性输出和哈希一致性。
- Schema升级与不兼容版本拒绝。

### 10.2 Unity EditMode测试

- JSON DTO到运行SO映射完整。
- 资源SO按ID一一匹配。
- 单位、建筑、卡牌、奖励池无重复或缺失。
- `AttackInterval=0` 等非法值阻止导入。
- 空资源、地貌、公共建筑数据库按设计报错。
- 保底卡必须属于启用卡池。
- `.meta` GUID格式和核心场景引用检查。

### 10.3 运行与回归测试

- 固定种子地图生成快照。
- 初始发牌和首张保底。
- 玩家/AI按各卡真实费用扣费。
- 单位攻击冷却、移动和战斗结算。
- 兵营产兵与金矿收入。
- 探索各奖励类型及降级路径。
- 战术卡和天赋实际效果。
- 资源、地貌、山脉和公共建筑生成。
- 新建工程缓存或删除 `Library` 后重新导入验证。

## 11. 版本控制和协作规则

应提交：

- `游戏数值配置.xlsx`。
- Schema。
- 规范化CSV、JSON和manifest。
- 自动生成运行SO及其合法 `.meta`。
- 导出器、导入器和测试。

不应提交：

- Excel临时锁文件 `~$*.xlsx`。
- 导出缓存、日志和个人验证报告。
- `Library`、`Temp` 等Unity缓存。

协作约定：

1. Excel一次改动尽量只涉及一个业务主题。
2. PR同时审查Excel导出的CSV差异和生成SO差异。
3. 不允许只提交 `.xlsx` 而漏提交生成物。
4. 合并冲突优先在Excel源中解决，再重新生成，禁止直接修改生成JSON或SO。
5. 配置schema变更必须带迁移说明和版本号升级。

## 12. 回滚方案

每张表独立设置迁移开关，仅用于迁移期：

```text
LegacySO
ExcelGenerated
```

规则：

- 切换以整张业务表为单位，不允许同一单位一半读Excel、一半读旧SO。
- 每个阶段保留迁移前数据快照和对比报告。
- 新配置导入失败时保留上一次通过校验的生成SO，不生成半成品。
- 阶段验收完成并稳定一个版本后，删除对应Legacy分支，防止长期双轨。

## 13. 验收标准

架构改造完成必须满足：

- 策划只编辑一个Excel工作簿即可调整所有已迁移平衡数值。
- Unity资源引用仍通过SO维护，Excel中不存GUID和Asset路径作为核心关联。
- 相同输入可重复生成字节级稳定的中间数据。
- 所有ID唯一，所有跨表引用可解析。
- 非法数值、空必需池和悬空资源会在构建前阻断。
- 删除 `Library` 后完整重导仍可运行。
- 运行时不读取Excel、CSV或JSON。
- 旧SO数值、C#默认值和新表之间不存在双事实。
- 当前已发现的零攻击间隔、空公共建筑库、空资源库、空地貌库、错误保底卡、天赋倍率文案冲突和天赋ID串线都有自动化规则防止复发。

## 14. 首轮实施范围建议

首轮不要同时迁移所有表。建议先完成一个可闭环的最小范围：

1. 修复核心 `.meta` 和场景引用。
2. 建立Excel导出、JSON中间层、Unity导入、验证和构建门禁。
3. 迁移单位表、建筑表和普通卡池。
4. 改造三个对应Provider并保留整数ID兼容层。
5. 用固定数据快照和实际运行验证闭环。

这三类数据覆盖当前最严重的零攻速、卡费、卡池和保底错误，也能验证“一份数值表 + 一套资源SO + 自动生成运行SO + Provider合并”的完整架构。闭环稳定后，再按阶段3至阶段5扩展，风险低于一次性重写全部配置系统。
