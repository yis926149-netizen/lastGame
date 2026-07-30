# 05. 地图渲染、迷雾、网格与势力边界审计报告

## 结论

- 审查日期：2026-07-17
- 审查对象：`MapRenderer`、`FogManager`、`SphereOfInfluenceRenderer`、Mesh 生成工具、视觉事件、材质与 Shader。
- 结果：确认并修复 7 类渲染正确性、交互和资源生命周期缺陷；源码与测试程序集编译通过。
- 运行验证限制：Unity EditMode 测试因同一项目已被另一个 Unity 实例打开而未执行；未获取 Game 视图、Profiler、Draw Call、帧尖峰和 GC 实测数据，因此本模块为“代码审计通过，运行性能待测”。

## 已修复

### [P1] 地图过渡子网格数组长度使用了错误计数

- 根因：主地图子网格数组将矩形过渡数量使用了两次；三角过渡多于矩形过渡时会越界，少于时会留下无材质对应的空子网格。
- 修复：数组长度改为基础 3 层加矩形过渡数和三角过渡数。

### [P1] 每个地块网格复制整张地图顶点

- 根因：每个 `SubGridLine` 使用全图顶点和 UV，仅索引当前地块，内存随地块数平方增长。
- 修复：每个网格对象只保存本地 12 个顶点、12 个 UV 和 36 个索引，并共享一个网格材质。

### [P1] 势力边界渲染器未进入运行场景

- 根因：脚本 `.meta` 使用非法 GUID，场景中也没有该组件，视觉事件没有实际消费者绘制势力边界。
- 修复：恢复合法 GUID，并由 `GameInstaller` 创建和注入唯一的 `SphereOfInfluenceRenderer`。

### [P1] 禁用的迷雾封皮永远收不到初始化事件

- 根因：`GameScene` 中 `FogCover` 的 `FogManager` 被禁用，`FogInit` 事件无法唤醒禁用组件。
- 修复：安装器在地图初始化前启用所有场景迷雾组件；组件在注入完成后幂等订阅，禁用时成对退订。

### [P2] 网格与势力边界改变地图射线命中

- 根因：通用 Mesh 创建函数无条件添加 `MeshCollider`，视觉层启用后成为新的交互表面。
- 修复：新增纯视觉 Mesh 模式；网格和势力边界不再创建碰撞体，主地图仍保留碰撞体。

### [P2] 动态 Mesh 缺少法线、Bounds 和大顶点索引保护

- 根因：部分 `CreatMesh` 重载只写顶点与索引，未重算法线和 Bounds；顶点超过 65,535 时仍默认使用 16 位索引。
- 修复：所有重载重算法线和 Bounds，并按顶点数自动切换 `UInt32` 索引。

### [P2] 动态材质与 Mesh 持续泄漏

- 根因：势力边界每次视觉事件都新建材质和 Mesh，只销毁 GameObject；迷雾生成器创建的 Mesh 也没有释放。
- 修复：刷新或销毁时显式释放势力边界材质和 Mesh；`MeshGenerator` 销毁时释放其运行时 Mesh；共享材质通过 `sharedMaterial(s)` 赋值避免隐式实例化。

## 配置核对

- `GraphicsSettings` 的自定义渲染管线为空，当前使用 Built-in Render Pipeline。
- `GridLine`、`SphereOfInfluence`、`Fog`、双材质混合和三材质混合 Shader 均存在于 `Assets/Shader`。
- 三个迷雾材质均引用 `Fog.shader` 的有效 GUID，未发现主地图材质指向第三方 URP Shader 的静态证据。
- `OnMapVisualChanged` 仅由移动、探索、建城和归属变化等业务路径触发，未发现 `Update` 中每帧全图重建。

## 测试与验证

- 新增 `MapControllerMeshTests`：覆盖纯视觉 Mesh 无碰撞体、共享材质、法线/Bounds 和大顶点 `UInt32` 索引。
- `dotnet restore MainGame.csproj`、`dotnet restore Tests.csproj`：成功。
- `dotnet build MainGame.csproj --no-restore`：成功，0 error；仅有既有 Unity/Zenject 字段告警。
- `dotnet build Tests.csproj --no-restore`：成功，0 error。
- `git diff --check`：无空白错误，仅工作区既有 CRLF 提示。
- Unity EditMode：未运行，同一项目被已打开的 Unity 实例占用。

## 待运行验证

- 在 Unity 中执行 EditMode/PlayMode 测试并确认 Game 视图无粉色材质、迷雾三层位置正确、势力边界随建城和易主刷新。
- 使用默认地图和较大地图记录首次生成耗时、Draw Call、顶点数、主线程尖峰和 GC Alloc。
- 连续触发探索、单位移动和城市归属变化，使用 Memory Profiler 确认 Mesh/Material 数量回落且无持续增长。
