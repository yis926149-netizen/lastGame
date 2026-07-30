# 模块 15 检查报告：资源、包体、性能与发布配置

## 结论

- 状态：有条件通过
- 检查日期：2026-07-17
- 基准提交：`ccd0407`；工作区原本非干净，本次仅修改结束视频引用并新增本报告与延期清单
- 发现统计：P0 0，P1 1，P2 3，P3 0

## 已修复

### P1 结束视频引用失效且存在重复播放器

- 证据：`Assets/Scenes/GameScene.unity` 的两个 `VideoPlayer` 均引用 GUID `4101f070aa837864a8c64a382080c4bb`，但视频 `.meta` 原先使用非 Unity 格式的 Base64 字符串，导致引用无法解析。
- 影响：胜负流程启用 `EndGameVideo` 后，视频无法可靠播放；两个启用的播放器还会同时向同一 RenderTexture 输出。
- 修复：恢复视频 `.meta` 为场景所引用的合法 32 位 GUID，并禁用重复的子级播放器，保留 `EndGameVideo` 上的播放器。
- 验证建议：在 Unity 中重新导入视频，分别触发胜利和失败流程，确认视频画面、声音、跳过和重播行为。

## 发布配置待办

### P2 发布身份仍为模板值

- `ProjectSettings/ProjectSettings.asset` 仍为 `DefaultCompany`、`My project`、`com.DefaultCompany.My-project` 和版本 `0.1`。
- 这些值需要正式产品名、公司名和包名才能正确修改，因此本次不猜测替换。

### P2 Android 仅配置 ARMv7 且平台图标不完整

- `AndroidTargetArchitectures: 1` 仅启用 ARMv7，不符合主流 Android 商店的 64 位发布要求。
- Android adaptive、round、legacy 图标槽位为空。
- 当前已验证构建目标是 Windows；若发布 Android，需启用 ARM64、配置各类图标并生成 AAB 检查 ABI、targetSdk、权限和签名。

### P2 Analytics 自动初始化但缺少发布侧隐私闭环

- `ProjectSettings/UnityConnectSettings.asset` 启用了 Analytics 并设置启动初始化，但项目未发现用户同意、退出或数据删除入口。
- 发布前应选择关闭未使用的 Analytics，或补齐隐私政策、同意和退出流程，并验证实际联网行为。

## 已确认配置

- Windows Development 构建成功：`AuditReports/windows-development-build.log` 第 15968 行记录 `Build Finished, Result: Success`，最终返回码为 0。
- 修复后使用 Unity 2022.3.62f3c1 执行批处理重新导入和脚本编译，进程返回码为 0；日志见 `AuditReports/module-15-import-compile.log`。
- 构建场景为有效的 `StartScene` 和 `GameScene`。
- 项目使用 Built-in Render Pipeline；未发现发布场景引用 `Resources/Import` 下 URP 管线资产的证据。
- `Packages/manifest.json` 与 `packages-lock.json` 中 Shader Graph 均为 `14.1.0`，未发现版本不一致。
- Ads 和 Purchasing 包已安装但服务关闭，业务代码未发现明确调用。

## 包体证据

- 现有 Windows Development Build 完整体积为 1.7 GB，用户资产 1.6 GB。
- 未压缩用户资产中纹理 1.4 GB（85.8%），网格 190.1 MB（11.5%）。
- Build Report 前三项均为 `Assets/Resources/Import/.../Free 2D Impact FX` 下的 64 MB PSD。
- 详细的包体、内存和性能处理建议见根目录 `MODULE_15_DEFERRED_IMPROVEMENTS.md`。

## 未执行验证

- 未运行默认地图和 20 回合 Profiler 流程，因此 FPS、主线程尖峰、GC Alloc、内存和 Draw Call 尚无量化结果。
- 修复后尚未重新生成完整 Player；已通过 Unity 批处理导入和编译。现有 1.7 GB 构建报告来自修复前，但足以证明 `Resources/Import` 被打包。
- 未执行 Android/iOS 构建、商店校验、权限检查、签名检查或联网抓包。
- 未逐一进行第三方许可证法律审查；发布前需生成 Third-Party Notices 并核实实际保留资产的许可义务。

## 建议后续模块

- 模块 17：在端到端胜负流程中验证结束视频，并执行 20 回合性能采样。
