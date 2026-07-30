# 模块 16 检查报告：文档、架构边界与遗留代码

## 结论

- 状态：有条件通过
- 检查日期：2026-07-17
- 基准提交：`ccd0407`；工作区原本非干净，本次只修改 DocFX 文档配置、导航和 Markdown 渲染问题，并新增本报告与延期清单
- 发现统计：P0 0，P1 1，P2 2，P3 3

## 已修复

### P1 DocFX API 输入配置失效

- `Docs/docfx.json` 原先从 `Docs/` 使用 `../../Assets/Scripts`，实际指向仓库外部；同时引用了仓库中不存在的 `filterConfig.yml`。
- 已将输入改为仓库内的 `../Assets/Scripts/Scripts.csproj`，移除无效 filter，并把站点占位名称 `FirstSite` 改为项目名称。

### P2 服务页面被渲染成整页代码块

- 8 个服务文档存在多余的 ` ```markdown ` 外层围栏，9 个服务文档的示例代码围栏没有闭合，导致正文、标题和链接无法正常渲染。
- 已移除外层围栏并闭合 C# 示例代码块。

### P2 服务文档无法从站点导航到达

- 根 `toc.yml` 原先只链接两个内容近乎为空的入门页和 API 目录，`Docs/index.md` 与服务说明不在导航中。
- 已加入首页和主要服务入口；完整服务列表仍由 `Docs/index.md` 提供。

## 遗留代码与架构边界

### P3 旧回合流程无有效引用

- `PhaseMachine` 没有业务代码调用，也没有主场景或 Prefab 的脚本 GUID 引用。
- `MoveCommand` 没有实例化位置，`CommandQueue.Submit` 没有调用；当前只有 `PlayerPhase.Exit()` 执行必为空的单位队列。
- 当前实际回合流程由 `GameStateMachine`、`PlayerPhase`、`AIPhase` 和 `SettlementPhase` 驱动。
- 建议：确认不需要回放/命令历史后，一并删除 `PhaseMachine`、`MoveCommand`、`CommandQueue` 以及空队列调用。本轮不删除，避免在模块审计中扩大代码变更。

### P3 `IAIManager` 名称和目录与职责不符

- `Core/Interfaces/IAIManager.cs` 实际是挂载在 `GameScene` 的具体 `MonoBehaviour`，包含完整 AI 实现，不是接口。
- `AIPhase` 和 Zenject Installer 直接依赖该具体组件，名称会误导测试替身和所有权判断。
- 建议：先重命名/移动为职责匹配的具体 Manager；仅在出现真实替换需求时提取窄接口。

### P3 单程序集存在层间双向依赖，但未发现 Editor API 混入

- `MainGame.asmdef` 覆盖全部业务代码；Core 中存在对 UI/Controller 具体类型的依赖，Controller 又依赖 Core 实现，未来直接拆程序集会形成循环依赖。
- 未发现 `Assets/Scripts` 引用 `UnityEditor`，当前没有 Player 构建被 Editor API 阻断的证据。
- 建议：不为形式分层强拆程序集；新增代码先停止 Core 反向依赖具体表现组件，再按实际边界逐步收敛。

## 文档剩余差异

- README 的启动场景、构建场景、基础操作和三阶段回合顺序与当前实现一致。
- README 对卡牌解锁、资源收割、城市易主、音频和胜负规则仅有概览或没有说明；测试覆盖清单也未包含近期新增测试。这些不阻断运行，但后续应按用户可复现流程补充。
- 部分服务文档的依赖、示例和接口所有权说明已落后于实现，尤其是 `ICardService`、`IMapDataService` 和 `ITechCultureService`。本轮只修复确定的构建与渲染错误，契约重写需结合设计意图单独处理。

## 已执行验证

- 静态核对 README、Docs、DocFX 配置、Core/Interfaces、Core/Services、Turn、Controllers、asmdef、场景/Prefab GUID 引用和 C# 调用引用。
- 核实 `PhaseMachine`、`MoveCommand`、`CommandQueue.Submit` 无有效运行入口。
- 核实业务程序集未引用 `UnityEditor`。
- 性能和算法建议已单独写入根目录 `MODULE_16_DEFERRED_IMPROVEMENTS.md`，本轮未实施。

## 未执行验证

- 本机未安装 `docfx`，因此未执行最终站点构建；已验证 `docfx.json` 可解析、项目输入存在、服务 Markdown 围栏成对且 TOC 的源文件链接存在。
- `dotnet build MainGame.csproj --no-restore` 因 Unity 临时目录缺少 `Temp/obj/MainGame/project.assets.json` 而失败；本轮没有修改 C#，该结果不表示新增编译错误。
- 未启动 Unity 场景验证文档中的全部操作、城市易主、资源收割、音频和胜负流程；这些应在模块 17 端到端检查中完成。

## 建议后续模块

- 模块 17：按 README 从 `StartScene` 完成开局及完整回合流程，并验证文档未覆盖的关键玩法。
