# 06. 单位仓库、移动、寻路与战斗审计报告

## 结论

- 状态：核心缺陷已修复，Unity 场景回归待验证
- 检查日期：2026-07-17
- 检查者：OpenCode（当前会话）
- 基准提交：`ccd0407`；工作区非干净，本报告基于当前未提交实现，只新增审计文档，不修改业务代码。
- 发现统计：P0 0 项、P1 6 项、P2 4 项、P3 1 项。

## 修复结果（2026-07-17）

- 已修复 6 项 P1 和 4 项 P2 的代码根因；P3 历史对象强引用作为性能/生命周期优化延期处理。
- 移动现在统一过滤负数、NaN、Infinity 和 `float.MaxValue` 成本，排除单位占用，并使用目标预留防止并发移动或部署抢格。
- 普通移动保持起点占用直到完成，完成时一次切换到终点；取消移动恢复起点、位置和提交前移动力。
- `MoveToAttack` 参数统一为敌方目标格，由移动系统选择合法相邻攻击位，不再由输入层提前截断。
- 远程攻击统一校验行动力、单位射程、目标存活和玩家阶段；AI 读取单位射程并等待完整 `IsBusy` 生命周期。
- 仓库注册改为跨玩家/AI 阵营和 AI 组唯一；单位死亡立即清除逻辑状态，视觉对象仍可延迟销毁；普通建筑死亡清理全部玩家建筑索引。
- 新增占用目标、取消回滚、`float.MaxValue` 不可通行和仓库跨阵营/跨组注册测试。
- 延期性能与算法工作记录在 `MODULE_06_DEFERRED_IMPROVEMENTS.md`。

### 修复验证

- `dotnet build Tests.csproj --no-restore`：成功，0 error；既有 Unity/Zenject 注入字段告警仍存在。
- `git diff --check`：通过，仅有工作区换行符提示。
- Unity EditMode 筛选测试：未执行，项目仍被另一个 Unity 实例打开；关闭该实例后需运行 `UnitRepositoryTests`、`UnitMovementSystemTests`、`UnitRemovalServiceTests`。
- PlayMode：未验证移动动画、攻击动画、AI 回合边界和死亡动画表现，因此当前不标记为完整运行通过。

## 检查范围

- 核心实现：`IUnitRepository`、`UnitRepository`、`IUnitService`、`UnitService`、`UnitRemovalService`、`IUnitMovement`、`UnitMovementSystem`、`MinPriorityQueue`。
- 控制与调用方：`UnitMovementController`、`PlayerInputHandler`、`UIController`、`IAIManager`、`BuildingController`、`ModelController` 及阶段切换调用。
- 数据契约：`CharacterData`、`UnitData`、`HexCellData`、`Enums` 和单位数据库。
- 现有测试：`UnitRepositoryTests`、`UnitMovementSystemTests`、`UnitRemovalServiceTests`、`DomainInvariantTests`、`GameStateMachineTests`。
- 主要调用链：玩家左/右键或信息面板 -> 单位控制器 -> 移动系统 -> 地块占用/攻击；AI 单位回合 -> 寻路 -> 移动/攻击 -> 阶段结束；死亡检查 -> `UnitRemovalService` -> 仓库/地块/GameObject。
- 排除范围：未逐项验证 Animator、Collider、Tag、Layer、Prefab 子节点和视觉距离；这些项目需要 PlayMode 场景验证。

## 发现

### [P1] 移动不检查单位占用，完成时会覆盖目标格占用者

- 位置：`Assets/Scripts/Core/Services/UnitMovementSystem.cs:524`、`Assets/Scripts/Controllers/UnitMovementController.cs:203`、`Assets/Scripts/Core/Interfaces/IAIManager.cs:436`
- 现象：单位可以把已有单位的地块作为可达点并移动过去；后到单位会覆盖地块中原占用者引用。
- 触发条件：玩家点击友军所在格，AI 随机选中已占用格，或两个单位在前一个移动尚未结束时选择同一目标。
- 根因：邻接过滤只检查 `movementCost == -1`，未检查 `IsHaveUnit()`；`OnMoveFinished` 又无条件执行 `SetHaveUnit(true, gameObject)`。
- 影响：多个 GameObject 可叠在一格，但地块只能记录一个单位。部署、选择、死亡清理和后续移动都会基于错误占用状态。
- 证据：`GetAllNeighborsAndCosts` 第 531-547 行没有占用分支；AI 第 436-441 行直接从可达集合随机选点；完成回调第 203-204 行覆盖引用。
- 修复方向：由统一的通行规则同时处理友军、敌军和目标例外；提交前预留目标格，完成时以比较并交换方式更新占用，拒绝已被其他任务预留的目标。
- 测试建议：覆盖友军/敌军作为中间格和终点、两个移动任务争抢同一格、占用者死亡后的引用一致性。

### [P1] 移动提交和取消不是原子操作，可能产生不属于任何地块的单位

- 位置：`Assets/Scripts/Core/Services/UnitMovementSystem.cs:89`、`Assets/Scripts/Core/Services/UnitMovementSystem.cs:105`、`Assets/Scripts/Controllers/UnitMovementController.cs:166`、`Assets/Scripts/UI/UIController.cs:187`
- 现象：移动一开始起点占用即被清空；取消只删除任务，公开的控制器取消方法为空。结束回合也不等待或取消在途移动。
- 触发条件：移动过程中调用取消、点击下一回合、切场景或对象被移除。
- 根因：起点在任务提交时被释放，但任务没有保存和恢复占用事务；`CancelMove` 不做位置吸附、地块恢复或完成回调。
- 影响：单位可停在格间且没有地块引用，移动还可能跨越 AI/结算阶段继续执行，其他单位可同时占据其起点或终点。
- 证据：第 90-100 行先清格再入队；第 105-110 行仅 `RemoveAll`；控制器第 166-169 行没有实现；`NextTurn` 第 187-192 行直接结束回合。
- 修复方向：定义移动任务的提交、完成和回滚状态，取消时吸附到最后一个已结算格并恢复唯一占用；阶段切换必须等待或统一取消所有任务。
- 测试建议：在零步、半步、刚扣费、目标到达和对象销毁时分别取消，断言位置、移动力和唯一占用一致。

### [P1] 信息面板远程攻击可无视射程和行动力重复执行

- 位置：`Assets/Scripts/UI/UIController.cs:195`、`Assets/Scripts/UI/UIController.cs:319`、`Assets/Scripts/UI/UIController.cs:345`、`Assets/Scripts/Controllers/UnitMovementController.cs:47`
- 现象：技能按钮显示固定成本 2 的高亮，但右键提交任意敌人时不校验距离、高亮集合、阶段或剩余行动力；攻击结束后零移动力单位仍可重新选择。
- 触发条件：选择 ID 3、5 或 9 的玩家单位，进入技能模式后右击地图任意距离敌人；动画结束后重新选择并重复。
- 根因：`PerformRangedAttack` 只写目标和攻击状态；`CanBeSelected` 不检查移动力；信息面板模式在自身 `Update` 中独立处理输入。
- 影响：玩家可跨地图攻击，并在同一回合无限重复攻击；模式若跨阶段保留，还可能在非玩家阶段提交攻击。
- 证据：高亮第 322-326 行硬编码 `2`；提交第 346-355 行无任何规则校验；可选择条件第 47-56 行只检查动画状态。
- 修复方向：把射程、阶段、阵营、目标存活和行动消耗收敛到一个攻击命令校验入口，UI 只展示该入口返回的合法目标。
- 测试建议：覆盖射程边界、超射程、零行动力、动画后重复攻击、切阶段和目标死亡。

### [P1] 玩家近战选择合法邻格后，攻击移动会再次截断路径

- 位置：`Assets/Scripts/Core/Services/PlayerInputHandler.cs:223`、`Assets/Scripts/Core/Services/UnitMovementSystem.cs:49`、`Assets/Scripts/Controllers/UnitMovementController.cs:525`
- 现象：输入层已把敌人邻格作为目的地，移动系统仍把最后一格当成敌人格删除，单位会停在预定攻击位前一格，再用世界坐标冲刺攻击。
- 触发条件：敌人格不在可达集合，但至少一个敌人邻格可达。
- 根因：`MoveToAttack` 参数同时被两层解释为“敌人格”和“合法攻击位置”，契约不唯一。
- 影响：近战可从距离二发起不受地块规则约束的冲刺，穿过占用、建筑或不可通行地块，且实际移动力扣除少一格。
- 证据：输入层第 235-249 行选择 `validNeighbor` 并提交；系统第 49-55 行无条件删除路径末格；控制器第 553-572 行按世界坐标直线移动。
- 修复方向：明确攻击命令只接收敌方目标，由领域层计算合法攻击位；或明确只接收攻击位并禁止再次截断，两种语义不能并存。
- 测试建议：覆盖已相邻、差一格、多个候选邻格、邻格被占用以及目标格不可通行。

### [P1] AI 在近战冲刺开始前停止等待，攻击可跨到后续单位或玩家阶段

- 位置：`Assets/Scripts/Core/Interfaces/IAIManager.cs:426`、`Assets/Scripts/Core/Interfaces/IAIManager.cs:429`、`Assets/Scripts/Controllers/UnitMovementController.cs:206`
- 现象：路径完成后 `isMoving` 已为 false，而近战先进入 `GoToAttackPosition`，此时 `isAttack` 尚未置 true；AI 等待循环立即结束。
- 触发条件：AI 单位需要先移动再近战攻击，尤其是该回合最后一个 AI 单位。
- 根因：AI 只等待两个瞬时布尔状态，没有等待完整命令生命周期；控制器内部攻击进行状态没有对外形成稳定完成信号。
- 影响：多个 AI 单位动作重叠；最后一次伤害和动画可能延续到玩家阶段，回合边界失去确定性。
- 证据：AI 第 429-430 行仅等待 `isMoving || isAttack`；控制器第 207-209、525-545 行先启动冲刺，直到第 384-385 行才设置 `isAttack`。
- 修复方向：让移动/攻击命令返回可等待的完成句柄，或公开覆盖移动、冲刺、攻击、返回全过程的只读忙碌状态。
- 测试建议：PlayMode 中逐帧记录 AI 阶段、移动、冲刺、伤害和返回顺序，断言阶段只在完整结束后切换。

### [P1] 不可通行成本约定冲突，AI 无法攻击部分建筑且玩家与 AI 远程规则不同

- 位置：`Assets/Scripts/Core/Models/HexCellData.cs:231`、`Assets/Scripts/Core/Models/HexCellData.cs:253`、`Assets/Scripts/Core/Services/UnitMovementSystem.cs:536`、`Assets/Scripts/Core/Interfaces/IAIManager.cs:369`
- 现象：海洋、未探索以及攻防建筑使用 `float.MaxValue`，寻路却只过滤 `-1`，目标例外也只对 `-1` 生效；AI 所有战斗统一按近战移动，不读取 `BasicAttackRange`。
- 触发条件：路径邻接海洋/未探索/攻防建筑，AI 以不可通行建筑为目标，或 AI 控制 ID 3、5、9 的远程单位。
- 根因：不可通行状态同时被编码为成本值，生产者和消费者约定不一致；AI 绕过玩家使用的射程判断。
- 影响：不可通行节点会以极大成本进入队列，建筑目标无法获得攻击目标例外；AI 远程单位失去射程能力，双方对同类单位得出不同结果。
- 证据：地块第 233、239、254、256 行写入 `float.MaxValue`；寻路第 537 行只判断 `-1`；AI 第 413-429 行仅在目标总成本不超过移动力时执行 `MoveToAttack`。
- 修复方向：使用显式 `IsPassable`/通行结果而非特殊浮点值，并让玩家与 AI 共用同一攻击范围和攻击位计算服务。
- 测试建议：参数化覆盖所有地形、探索、建筑和目标例外；对同一局面断言玩家与 AI 返回相同合法命令集合。

### [P2] 仓库允许同一对象跨阵营和跨 AI 组重复注册

- 位置：`Assets/Scripts/Core/Services/UnitRepository.cs:30`、`Assets/Scripts/Core/Services/UnitRepository.cs:66`、`Assets/Scripts/Core/Services/UnitRepository.cs:73`、`Assets/Scripts/Core/Services/UnitService.cs:105`
- 现象：同一 GameObject 可同时存在于玩家仓库和多个 AI 组；重复覆盖仍发送 Added 事件；敌方删除只清理第一个命中组。
- 触发条件：生成流程重复注册、阵营转换未先移除，或错误地使用不同 AI 索引注册同一对象。
- 根因：添加没有全局唯一性约束，删除遇到首个组即返回，移除服务使用 `if/else if`。
- 影响：单位可被重复结算或行动，死亡后仍残留仓库记录。
- 证据：玩家和敌方添加均直接索引赋值；敌方删除第 77-80 行首个命中后返回。
- 修复方向：在仓库层维护对象到所有者的唯一索引，明确重复添加是幂等、更新还是异常；删除必须清除全部非法残留。
- 测试建议：覆盖同组重复、跨组、跨阵营、阵营转换、空组和销毁对象查询。

### [P2] 移动预览会展示实际行动力无法执行的完整路径

- 位置：`Assets/Scripts/Core/Services/PlayerInputHandler.cs:387`、`Assets/Scripts/Core/Services/UnitMovementSystem.cs:83`
- 现象：悬停远处时显示完整路径和目标指示，点击后移动请求因费用超出剩余行动力而拒绝。
- 触发条件：目标拓扑可达，但最短路成本大于当前移动力。
- 根因：预览计算出 `totalCost` 后未与单位剩余移动力比较，也未截断；执行入口会比较并拒绝。
- 影响：移动范围、预览和实际执行反馈不一致。
- 证据：预览第 395-428 行忽略 `totalCost`；请求第 83-86 行拒绝超预算路径。
- 修复方向：预览复用执行命令的路径和预算结果，不单独解释寻路输出。
- 测试建议：对相同起终点断言范围、高亮、预览路径、请求结果和扣费完全一致。

### [P2] 死亡动画期间单位仍在仓库、地块和目标集合中

- 位置：`Assets/Scripts/Controllers/UnitMovementController.cs:240`、`Assets/Scripts/Controllers/UnitMovementController.cs:248`、`Assets/Scripts/Core/Interfaces/IAIManager.cs:389`
- 现象：生命降至零后等待 2.2 秒才执行领域清理，期间 Collider、地块占用和仓库仍保留；AI 目标搜索不排除死亡玩家单位。
- 触发条件：一个单位被击杀后，在死亡动画延迟内继续选择或执行下一 AI 行动。
- 根因：逻辑死亡和视觉销毁没有分离，全部清理都延迟到 `Invoke(RemoveUnit)`。
- 影响：可重复选择或攻击尸体，单位计数和胜负判断在延迟窗口内滞后。
- 证据：控制器第 243-248 行仅设置动画并延迟；AI 第 389-398 行不检查玩家目标 `currentHp`。
- 修复方向：生命归零时立即从逻辑集合、占用、选择和碰撞中移除，仅延迟销毁视觉对象。
- 测试建议：断言归零当帧不可选、不可寻址、无占用且不会二次结算，视觉对象可在动画后销毁。

### [P2] 非城市建筑销毁没有正确清理管理器索引

- 位置：`Assets/Scripts/Controllers/BuildingController.cs:392`
- 现象：销毁逻辑只扫描科文建筑索引，并把字典中的 `GameObject` 与 `Transform` 比较；攻击、防御和祭坛索引未处理。
- 触发条件：玩家任一非城市建筑生命归零。
- 根因：不同建筑类型的注册与移除未走统一仓库；当前比较第 409 行类型/引用不一致，通常找不到目标，随后还会尝试删除键 `-1`。
- 影响：管理器保留已销毁 Unity 对象，后续升级、加成或遍历可能访问伪空引用。
- 证据：第 404-415 行只处理 `Index_TechnologyAndCulturalBuilding`，没有其他索引清理分支。
- 修复方向：按建筑类型通过统一移除服务清理对应索引、地块和 GameObject，并使重复调用幂等。
- 测试建议：逐类建筑执行两次销毁，断言地块成本、索引、加成和场景对象全部只清理一次。

### [P3] 单位移除幂等集合永久保留历史对象引用

- 位置：`Assets/Scripts/Core/Services/UnitService.cs:71`
- 现象：`_removedUnits` 在服务生命周期内只增不减。
- 触发条件：长局持续生成和移除单位。
- 根因：幂等性通过永久保存每个历史 GameObject 实现。
- 影响：内存随累计生成单位数增长；短局影响较低，但该集合没有上界。
- 证据：唯一写入位于第 85 行，代码中没有删除或按实例 ID 淘汰逻辑。
- 修复方向：将幂等状态放在实体生命周期组件中，或在销毁完成后使用不会强引用对象的标识策略。
- 测试建议：批量生成和移除单位后检查服务持有引用数量及 Memory Profiler 快照。

## 已执行验证

| 验证 | 环境/输入 | 结果 | 证据位置 |
| --- | --- | --- | --- |
| 静态调用链审查 | 当前工作区源码 | 确认上述 11 项发现 | 本报告“发现”章节 |
| `dotnet restore Tests.csproj` + `dotnet build Tests.csproj --no-restore` | .NET SDK 9.0.313 | 成功，0 error；同时生成 `MainGame.dll` 与 `Tests.dll`，有既有告警 | 终端输出 |
| `dotnet restore MainGame.csproj` + 独立 build | 与测试 build 并行 | 因共享 `Temp/obj` 文件锁失败；同轮 Tests 构建随后成功编译 MainGame | 终端输出 |
| Unity EditMode 筛选测试 | Unity 2022.3.62f3c1；三个单位测试类 | 未执行：另一 Unity 实例正在打开本项目 | `C:\Users\ADMINI~1\AppData\Local\Temp\opencode\module06-unity.log` |

## 检查清单结果

| 检查项 | 结果 | 说明 |
| --- | --- | --- |
| 仓库增删查、阵营过滤和空集合 | 失败 | 基本路径有测试，跨阵营/跨组重复不唯一 |
| 寻路障碍、占用、建筑、海洋和边缘 | 失败 | 不检查占用，成本哨兵冲突 |
| 不可通行成本统一 | 失败 | 生产端用 `float.MaxValue`，过滤端用 `-1` |
| 范围、预览、实际路径和扣费一致 | 失败 | 预览忽略预算，攻击路径语义冲突 |
| 移动全生命周期占用一致 | 失败 | 提交先清起点，取消无回滚 |
| 近战、远程、射程、伤害和行动消耗 | 失败 | 远程入口可绕过规则，玩家/AI 不一致 |
| 单位/建筑死亡只结算一次并完整清理 | 部分通过 | `UnitRemovalService` 幂等测试存在；逻辑清理延迟且建筑索引不完整 |
| 协程/Tween 在阶段和场景切换时终止 | 失败 | AI 等待条件存在状态空窗；未见统一阶段取消 |

## 测试缺口

- P1：占用阻挡、并发目标预留、取消回滚、跨阶段移动。
- P1：近战攻击位、远程射程/行动力、伤害一次性、反击规则和完整攻击生命周期。
- P1：`float.MaxValue`/`-1`、未探索、海洋、建筑目标和地图边缘的参数化寻路。
- P1：同一局面下玩家与 AI 合法移动/攻击结果一致性。
- P2：仓库重复注册、死亡动画窗口、UI 选择清理和各类建筑移除。

## 剩余风险

- Unity Test Runner 因项目锁未执行，现有 EditMode 测试是否全部通过仍未验证。
- 反击是否属于设计规则无法从当前代码和测试确认；当前未发现反击实现。
- Animator 参数、Prefab 子节点、Collider/Tag/Layer 和攻击视觉距离需要 PlayMode 验证。
- `Invoke`、对象禁用、切场景与 Zenject Tick 的逐帧顺序需要运行日志确认。
- Dijkstra 在大地图上的 CPU 和 GC 未用 Profiler 测量，不在本报告中定性为性能缺陷。

## 汇总

- P0：0
- P1：6
- P2：4
- P3：1
- 建议后续模块：07 玩家输入、10 AI、11 城市与胜负、14 测试体系、17 端到端一致性。
