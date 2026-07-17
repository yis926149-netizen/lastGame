# 模块 01 检查报告：工程、编译与构建基线

## 结论

- 状态：不通过
- 检查日期：2026-07-16
- 检查者：OpenCode（gpt-5.6-sol）
- 基准提交：`22a207c91eb47a3ff27b1808815265a70bd229c6`
- 工作区状态：非干净。审查开始前已有业务脚本、Prefab、RenderTexture、`Packages/packages-lock.json`、`ProjectSettings/PackageManagerSettings.asset`、`ProjectSettings/ProjectVersion.txt` 等未提交修改；本次未修改这些既有内容。

当前 Editor 脚本编译成功，但运行时程序集包含无条件 `UnityEditor` 引用，Windows Player 编译不满足通过条件。EditMode 测试和 Windows Development Build 因同一项目正由 Unity Editor 占用而未并发执行。

## 检查范围

- 已检查文件/资产：`Packages/manifest.json`、`Packages/packages-lock.json`、`ProjectSettings/ProjectVersion.txt`、`ProjectSettings/EditorBuildSettings.asset`、`ProjectSettings/PackageManagerSettings.asset`、`ProjectSettings/ProjectSettings.asset`、`Assets/Scripts/MainGame.asmdef`、`Assets/Tests/Tests.asmdef`、`Assets/Tests/*.cs`、业务脚本中的 `UnityEditor` 引用、两个构建场景及其 `.meta`、DOTween、UniRx、Zenject、LibTessDotNet 和 NSubstitute 的版本/许可证/使用证据。
- 相关调用链：`EditorBuildSettings -> StartScene -> GameScene`；`MainGame.asmdef -> Assets/Scripts/**/*.cs`；`Tests.asmdef -> MainGame + NSubstitute + Zenject`；`manifest.json -> packages-lock.json -> packages.unity.cn/OpenUPM`。
- 运行环境：`C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe`，与 `ProjectSettings/ProjectVersion.txt:1` 一致。
- 排除范围及原因：未逐行审查第三方源码；未执行干净缓存包恢复；未关闭用户正在运行的 Unity Editor；未执行 Player Build、Build Report 或包体统计；未检查第三方导入资源对最终包体的贡献，该项留给模块 15。

## 发现

### [P0] 运行时程序集无条件引用 UnityEditor，阻断 Player 编译

- 位置：`Assets/Scripts/Scenes/StartScene/gameOptionController.cs:6`
- 现象：Editor 编译可以成功，但目标平台 Player 编译无法解析仅存在于 Editor 的 `UnityEditor` 命名空间，Windows Development Build 会在脚本编译阶段停止，无法生成可运行产物。
- 触发条件：以当前源码构建任意 Player 平台；该文件没有 `UNITY_EDITOR` 条件编译，所属程序集也没有平台排除。
- 根因：`gameOptionController.cs:6` 无条件声明 `using UnityEditor;`。该文件实际没有使用任何 UnityEditor API，却由 `Assets/Scripts/MainGame.asmdef:12-13` 定义的全平台运行时程序集编译。生成工程也确认 `MainGame.csproj:110` 包含该源文件，并在 Editor 编译上下文通过 `MainGame.csproj:433` 引用了 `UnityEditor.dll`。
- 影响：所有 Player 平台的构建基线；当前只能在 Editor 中编译和运行，无法满足模块 01 的目标平台构建通过条件。
- 证据：静态调用链为 `MainGame.asmdef -> gameOptionController.cs -> UnityEditor`；全项目业务脚本扫描只发现这一处无条件业务引用。当前 `Editor.log:507-510` 显示 Editor 上下文成功编译 `MainGame.dll`，这不能证明 Player 编译通过，因为 Editor 上下文提供了 `UnityEditor.dll`。实际 Player Build 因项目正被 PID 2736 的 Unity Editor 占用而未并发执行，故没有本轮 Build Report 或最终错误日志。
- 修复方向：删除未使用的 `using UnityEditor;`。不要为此把整个开始场景控制器改为 Editor-only；它是运行时行为。
- 测试建议：修复后在 `2022.3.62f3c1` 执行一次 Windows Standalone x64 Development Build，并增加 Editor 资产测试或 CI 静态检查，禁止非 `Editor` 目录、非 Editor-only asmdef 的业务源码引用 `UnityEditor`。

### [P3] 多项直接包和已导入插件缺少业务使用证据

- 位置：`Packages/manifest.json:3-16`
- 现象：包管理器在打开项目时注册并编译 Ads、Analytics、Purchasing、Cinemachine、Visual Scripting、Shader Graph 等直接依赖及其传递程序集；仓库内还包含 UniRx 及其 Examples，但多项依赖未找到主业务使用证据。
- 触发条件：打开项目、恢复包或编译程序集；这些依赖即使未被玩法使用也参与解析，部分程序集出现在当前编译日志中。
- 根因：`manifest.json` 保留了开发模板或历史导入的直接依赖，未建立“直接依赖 -> 业务引用/资产”清单。静态扫描未能证明 `com.unity.ads`、`com.unity.analytics`、`com.unity.purchasing`、`com.unity.cinemachine`、`com.unity.mathematics`、`com.unity.collab-proxy` 被主业务使用；`Assets/Plugins/UniRx/ReadMe.txt:1` 表明 UniRx 6.2.2 已导入，但未找到 `Assets/Scripts` 对 UniRx 的调用。
- 影响：增加包恢复的外部依赖、编辑器导入和编译面，可能增加发布合规与包体审查工作；本轮没有 Build Report，不能断言它们一定增加最终 Player 包体。
- 证据：`Packages/manifest.json:3-16` 是直接依赖清单；`Editor.log:470-475`、`Editor.log:513-518`、`Editor.log:529-555` 显示 Purchasing、Analytics、Collab、NSubstitute、Cinemachine 等程序集被处理；已确认 TextMeshPro、UGUI、NSubstitute、Zenject、DOTween、LibTessDotNet 等有实际代码或测试引用，因此未列入本发现。
- 修复方向：在模块 15 中结合 Build Report 和引用扫描逐项确认用途；仅删除已证明无调用、无序列化资产引用且完成回归构建的直接依赖和示例程序集。
- 测试建议：建立依赖清单，记录包用途、负责人、许可证和 Player 构建贡献；每次移除一项后执行 Editor 编译、EditMode 测试和 Windows Player Build。

### [P3] 部分第三方库缺少可归档的精确版本或本地许可证文件

- 位置：`Assets/DOTween_1_2_765/DOTween/readme.txt:1`、`Assets/Plugins/LibTessDotNet/Properties/AssemblyInfo.cs:8-14`
- 现象：可以识别第三方来源和部分许可声明，但无法仅凭仓库内容为 DOTween 和 LibTessDotNet 建立完整、可靠的“上游版本 + 本地许可证文件”交付记录。
- 触发条件：毕业项目归档、第三方许可证清单生成、依赖升级或来源追溯。
- 根因：DOTween 目录名提示 `1.2.765`，其 readme 指向在线许可证页面，但目录中没有完整本地许可证文本；LibTessDotNet 源文件头包含 SGI Free Software License B 2.0 条款，`AssemblyInfo.cs` 又指向 `LICENSE.txt`，但该目录不存在此文件，程序集版本 `1.0.0.0` 也不能证明上游发布版本。
- 影响：不阻断当前编译，但发布归档、许可证披露和未来依赖升级缺少稳定依据。
- 证据：Zenject 的 `Assets/Plugins/Zenject/Version.txt:1` 与 `LICENSE.txt:1-23` 可完整识别为 9.2.0/MIT；UniRx 的 `Assets/Plugins/UniRx/ReadMe.txt:1,25-29` 可识别为 6.2.2/MIT；NSubstitute 由 `Packages/manifest.json:16` 和包缓存许可证锁定。相比之下，DOTween 与 LibTessDotNet 缺少同等级本地证据。
- 修复方向：从实际采用版本的官方发行包补充许可证原文和来源/版本记录，不修改第三方源码内容；若资产商店条款限制再分发许可证文件，则在项目第三方清单中记录官方 URL、获取日期和适用版本。
- 测试建议：增加发布前依赖清单检查，要求每个第三方运行时库都有来源、版本、许可证标识和许可证文本或合规链接。

## 已执行验证

| 验证 | 环境/输入 | 结果 | 证据位置 |
| --- | --- | --- | --- |
| Unity 版本核对 | 安装路径与项目版本文件 | 通过：均为 `2022.3.62f3c1 (1623fc0bbb97)` | `ProjectSettings/ProjectVersion.txt:1-2`；`Editor.log:172` |
| 当前 Editor 脚本编译 | 已打开项目的当前导入/编译会话 | 通过：生成 `MainGame.dll` 和 `Tests.dll`，未检出 C# error；12 条唯一 warning | `Editor.log:478-510`、`Editor.log:573-620`；`Library/ScriptAssemblies/MainGame.dll`；`Library/ScriptAssemblies/Tests.dll` |
| 编译警告基线 | 当前 `Editor.log` 首轮完整编译 | 12 条唯一警告：UnityChan 9、CFXR 1、业务代码 2 | `Editor.log:478-510` |
| EditMode 测试静态发现 | `Assets/Tests/*.cs` 与 `Tests.asmdef` | 发现 6 个 fixture、20 个 `[Test]`；程序集限制为 Editor 且启用 `UNITY_INCLUDE_TESTS` | `Assets/Tests/Tests.asmdef:4-23`；六个测试源码 |
| Player 程序集 Editor API 扫描 | `Assets/Scripts/**/*.cs` | 失败：发现 1 处无条件 `using UnityEditor` | `Assets/Scripts/Scenes/StartScene/gameOptionController.cs:6` |
| 构建场景顺序与 GUID | `EditorBuildSettings.asset` 对照两个场景 `.meta` | 通过：索引 0 为 StartScene，索引 1 为 GameScene，路径存在且 GUID 一致 | `ProjectSettings/EditorBuildSettings.asset:7-13`；两个 `.unity.meta:2` |
| 包锁与 Registry 一致性 | manifest、lock、PackageManagerSettings | 通过（当前缓存）：Unity 包使用 `packages.unity.cn`，NSubstitute 5.1.0 使用 OpenUPM；当前 Editor 注册 60 个包 | `Packages/manifest.json:2-57`；`Packages/packages-lock.json:251-256`；`ProjectSettings/PackageManagerSettings.asset:21-36`；`Editor.log:94-168` |
| 第三方版本/许可证静态核对 | DOTween、UniRx、Zenject、LibTessDotNet、NSubstitute | 部分通过：Zenject、UniRx、NSubstitute 证据明确；DOTween、LibTessDotNet 记录不完整 | 对应第三方目录的 Version/ReadMe/LICENSE/AssemblyInfo 文件 |

## 检查清单结果

| 检查项 | 通过/失败/未验证/不适用 | 说明 |
| --- | --- | --- |
| 使用 `2022.3.62f3c1` 打开并导入 | 通过 | 当前 Editor 会话版本匹配，包注册与脚本编译完成。 |
| Editor 脚本编译无错误 | 通过 | 0 个 C# error，12 条唯一 warning。未将运行时 Console 异常计入模块 01 编译基线。 |
| EditMode 测试可发现并执行 | 未验证 | 静态发现 20 个测试且 `Tests.dll` 编译成功；未运行 Test Runner。 |
| Windows Standalone Development Build | 失败 | 静态确认 Player 程序集引用 `UnityEditor`；因项目正被 Editor 占用，未生成本轮实际 Build Report。 |
| Player 程序集不引用 UnityEditor | 失败 | `gameOptionController.cs:6` 违反约束。 |
| 构建场景顺序与 GUID | 通过 | StartScene、GameScene 顺序和 GUID 均一致。 |
| 包锁可在干净缓存恢复 | 未验证 | 当前缓存恢复成功；未清理用户缓存或复制项目，以保护当前工作环境。 |
| 直接依赖有实际用途 | 失败 | 多项直接包未找到业务使用证据，需模块 15 结合 Build Report 复核。 |
| 基线包体大小与 Build Report | 未验证 | 未完成 Player Build。 |

## 测试缺口

- P0：缺少在 CI 或独立构建机执行的 Windows Standalone x64 Development Build，无法持续防止 Editor API 泄漏。
- P1：现有 20 个 EditMode 测试本轮未执行，只有程序集成功编译证据，没有通过/失败数和 NUnit XML。
- P2：缺少资产完整性测试，无法自动检查构建场景、GUID、Missing Script、运行时程序集 Editor 引用和第三方依赖清单。
- P3：缺少干净 Unity/UPM 缓存恢复验证，不能证明 `packages.unity.cn` 与 OpenUPM 在目标构建网络中稳定可用。

## 剩余风险

- 当前 Unity Editor 正以 PID 2736 打开同一项目，另有两个 AssetImportWorker；为避免项目锁冲突和干扰用户会话，未启动第二个 Unity 批处理进程，也未关闭现有进程。
- 当前日志后段存在开始场景运行时异常调用栈，但属于模块 12 的 UI/场景行为，不在模块 01 中重复定性；后续应由模块 12 独立复核。
- `ProjectSettings/ProjectSettings.asset:824-853` 未显式记录 Standalone 架构、脚本后端和按平台 API Compatibility，实际采用编辑器默认值；在目标发布配置确定前只能列为待确认项。
- `ProjectSettings/ProjectSettings.asset:16-17,45` 仍使用 `DefaultCompany`、`My project` 和默认 bundle identifier；这不阻断基线构建，交由模块 15 核对正式发布身份。
- 干净缓存包恢复依赖 `https://packages.unity.cn` 和 `https://package.openupm.com`；本轮只证明当前缓存/网络会话完成注册，未证明离线或 CI 环境恢复能力。
- 当前工程使用团结引擎标识的 `2022.3.62f3c1` 和 `tag:yousandi.cn,2023` 序列化内容；未验证与国际版 Unity 的交叉兼容性，后续构建必须继续使用项目指定发行版，除非先在隔离副本迁移验证。

## 汇总

- 结论：不通过
- P0：1
- P1：0
- P2：0
- P3：2
- 已执行验证及结果：版本匹配；当前 Editor 编译通过，0 error/12 个唯一 warning；静态发现 20 个 EditMode 测试；场景顺序/GUID 通过；Player 程序集扫描失败；包与第三方证据部分通过。
- 未执行验证及原因：EditMode Test Runner、Windows Development Build、Build Report、包体统计和干净缓存恢复未执行，主要原因是同一项目正被 Unity Editor 占用，且本模块遵循保护现有工作区、不干扰用户会话的原则。
- 建议后续模块：02（启动与场景生命周期）、14（实际运行测试与补齐测试体系）、15（Windows 发布配置、依赖用途、Build Report 和包体）。
