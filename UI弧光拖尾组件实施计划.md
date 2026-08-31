# UI 弧光拖尾组件实施计划

> 目标：实现一个**通用可挂载组件**，挂到任意 UI 元素上后，该 UI 在移动时自动拖出一条带贴图的弧光光带（参考图：物资飞入时的交错光弧）。组件与业务解耦，不绑定金币 / 卡牌等任何具体玩法。
>
> 状态：视觉方案已确认（**A = 贴图驱动辉光**）；D1 / D2 / D3 三项决策已确认（见 §8）。本文档只描述实现方案，不包含实际代码修改。

---

## 1. 背景与前置实测

### 1.1 工程环境（已实测，非假设）

| 项 | 实测值 | 出处 |
|---|---|---|
| 渲染管线 | 内建（`m_CustomRenderPipeline: {fileID: 0}`、`m_SRPDefaultSettings: {}`） | `ProjectSettings/GraphicsSettings.asset:58,82` |
| 后处理 | **无**：`Packages/manifest.json` 无 postprocessing 包，工程内无任何 VolumeProfile / Bloom asset | 全盘搜索无结果 |
| 场景 Canvas | **仅 1 个**，`m_RenderMode: 0` = Screen Space - Overlay，`m_SortingOrder: 0`，`m_OverrideSorting: 0` | `Assets/Scenes/GameScene.unity:9201` |
| 参考分辨率 | 1080×1920（竖屏），`m_UiScaleMode: 1`，`m_ScreenMatchMode: 0`，`m_MatchWidthOrHeight: 0` | 同上 CanvasScaler |
| 相机 HDR | `m_HDR: 1` | `Assets/Scenes/GameScene.unity:5979` |
| 目标平台 | 微信小游戏 WebGL（见 `CardDragGlow.shader` 头部约束） | `Assets/Shader/CardDragGlow.shader` |
| 现有 Graphic 子类 | **无**，本组件是工程内第一个 | 全盘搜索无结果 |

### 1.2 由实测事实推出的三条硬约束

**C1. 辉光必须画进贴图，不能靠 HDR + Bloom。**
相机 `m_HDR: 1` 是假信号——内建管线下无后处理链，超过 1 的亮度最终被 clamp，不会溢出成光晕。且 Overlay 模式的 Canvas **本身就不经过任何相机后处理**，即使后续装了 PostProcessing 包也无效。这是双重否定，没有绕过的余地。

因此走**方案 A**：把中心过曝白芯、外圈暖黄扩散、上下软边衰减，全部**直接画在 ribbon 贴图里**，shader 只做加色混合与强度调制。

**C2. 拖尾 Renderer 必须放独立 Canvas。**
全场景只有 1 个 Canvas，所有 UI 都在里面。Unity 的 Canvas 重建（rebatch）是**按 Canvas 粒度**的——往里塞一个每帧重建 mesh 的 `Graphic`，会导致整个主 Canvas 每帧重新合批。这是 UI 拖尾最大的性能陷阱，代价远高于 mesh 生成本身。

因此 Renderer 节点必须挂 `Canvas` 组件 + `overrideSorting = true`，把它从主 Canvas 的重建域隔离出去。**这是必需项，不是优化项。**

**C3. WebGL 预算。**
- shader 封顶 `#pragma target 3.0`，禁用 geometry / compute shader
- 单条尾巴 32 采样点 = 64 顶点，同屏条数需受控
- 走单材质共享 Renderer 合批（WebGL 的 drawcall 成本显著高于原生）
- 贴图打包成静态小图（建议 64×16），不运行时程序化生成 `Texture2D`

### 1.3 可复用的既有资产

- **`Assets/Shader/CardDragGlow.shader`**：同样是"无 bloom 环境下伪装辉光"的产物，其 `Blend SrcAlpha One` + `ZWrite Off` + `_Time.y` 驱动流动/呼吸（CPU 每帧零更新）的写法可整套迁移。其中"让光带动起来弥补亮度不足"这一条对本组件同样成立——静态加色条子视觉上会很平。
- **`Assets/Scripts/Utilities/PrefabPool.cs`**：按 prefab 分桶的通用池，`Get(prefab, parent)` / `Release(prefab, go)` / `Clear()`。本组件的 Renderer 节点复用不走它（Renderer 是常驻单例式节点，非瞬态），但**若后续需要池化 Emitter 宿主，沿用此 API 风格**。
- **`GameLoop.GameTime`**（`Assets/Scripts/Core/Services/GameLoop.cs:50,90`）：暂停时不累加的累计游戏时间。**本组件不使用它**——见 §8 D1，接入会引入 Zenject 依赖，破坏组件的通用性定位。此处记录是为了说明"已评估并主动放弃"，避免后续误接。

---

## 2. 核心设计：为什么必须拆成两个组件

直觉写法是把拖尾逻辑直接挂在要拖尾的 UI 上、自己画自己的尾巴。**这个写法是错的。**

UI 的 mesh 在**自身局部坐标系**中生成，物体一移动，整条尾巴跟着一起移动——结果是一根固定长度的棍子黏在图标屁股后面，而不是留在运动路径上的痕迹。

因此拆分为：

| 角色 | 类型 | 挂载位置 | 职责 |
|---|---|---|---|
| **Emitter** | `UITrail` : `MonoBehaviour` | 目标 UI 上（用户挂载） | 每帧采样自身位置 → 转换到 Renderer 坐标空间 → 提交采样点；管理生命周期与开关 |
| **Renderer** | `UITrailRenderer` : `MaskableGraphic` | Canvas 下**不移动**的独立节点（自动创建） | 持有全部活跃 Emitter 的采样点，生成 ribbon mesh |
| **Profile** | `UITrailProfile` : `ScriptableObject` | Assets 资源 | 材质、贴图、宽度曲线、颜色渐变、采样与老化参数 |

Renderer 全程不动 → mesh 顶点是**绝对坐标** → 尾巴留在原地。这是整个方案的立足点。

对外体验仍是"挂一个组件就行"：`UITrail` 在 `OnEnable` 时按 profile 自动查找或创建对应的 Renderer 节点。

---

## 3. 分层与合批策略

### 3.1 按 Profile 分组共享 Renderer

两种极端：

| 方案 | drawcall | 灵活性 |
|---|---|---|
| 全局单 Renderer | 1 | 所有拖尾必须同材质同贴图 |
| 每 Emitter 一个 Renderer | N | 完全独立 |

**采用折中：按 `UITrailProfile` 实例分组。** 同一 profile 的所有 Emitter 共享一个 Renderer 节点（一次 drawcall 画完），不同 profile 自然分开。颜色差异走**顶点色**，因此同 profile 下每条尾巴仍可有不同色调。

Renderer 节点命名 `__UITrailLayer_{profileName}_{layer}`，`HideFlags` 不隐藏（便于调试观察）。

### 3.2 层级：Below / Above 二选一

Overlay Canvas 严格按 hierarchy 顺序渲染，共享 Renderer 相对所有 UI 的前后关系是**固定的**。参考图中拖尾在飞行物之后（被遮挡）。

组件暴露枚举：

- `Below`：Renderer 节点 `SetAsFirstSibling()`，位于业务 UI 之下
- `Above`：Renderer 节点 `SetAsLastSibling()`，覆盖全部 UI

**不做**"精确插入到某个具体 UI 之间"——那会让组件依赖场景结构，通用性归零。

两个 layer 各自独立分组（即 `profile × layer` 才是分组键）。**首版只实现 `Below`**，见 §8 D3。

### 3.3 独立 Canvas 隔离（对应 C2）

Renderer 节点上同时挂：

```
Canvas            (overrideSorting = true, sortingOrder = Below:-1 / Above:+1)
GraphicRaycaster  // ← 不挂，拖尾不接收射线
UITrailRenderer   (MaskableGraphic)
```

`raycastTarget = false` 必须设，否则光带会吞掉点击。

---

## 4. 采样与老化

### 4.1 采样策略：距离 + 时间双阈值

| 策略 | 问题 |
|---|---|
| 纯按时间 | 慢速移动时点堆积（浪费顶点、转角 mesh 自交）；快速移动时点稀疏（弧线变折线） |
| 纯按距离 | 分布均匀，但停止移动时尾巴"冻住"不消散 |
| **距离触发 + 逐点时间戳老化** | **采用** |

- 位移超过 `minSampleDistance` 才追加新采样点 → 空间分布均匀
- 每个采样点携带独立生成时间戳（`Time.unscaledTime`，见 §8 D1），超过 `lifetime` 自动移除 → 停止移动时尾巴正常淡出
- 点数上限 `maxPoints`（默认 32），**环形缓冲**复用，禁止 `List.RemoveAt(0)`

### 4.2 坐标空间转换

Emitter 与 Renderer 可能处于不同层级。采样时统一转换到 **Renderer 的 RectTransform 局部坐标**：

```
世界坐标 → Renderer.rectTransform.InverseTransformPoint()
```

Overlay 模式下 Canvas 的世界坐标即屏幕空间尺度，此转换稳定。若后续 Canvas 改为 Screen Space - Camera，需改走 `RectTransformUtility.WorldToScreenPoint` + `ScreenPointToLocalPointInRectangle` 两段式——**此处需留注释标注该假设**。

### 4.3 `Clear()` 必须暴露

UI 元素经常被瞬间重定位：切页签、对象池取出复用、布局重排。不清空采样点的话，会从旧位置拉出一条**横穿屏幕的光带**。

这是此类组件最高频的 bug 报告来源。因此：
- 公开 `Clear()` 方法
- `OnEnable` 时自动 `Clear()`（覆盖对象池复用场景）
- `OnDisable` 时从 Renderer 注销并清点

---

## 5. Mesh 生成细节

### 5.1 Ribbon 构建

每个采样点生成 2 个顶点（左 / 右），相邻点间构成 2 个三角形（quad strip）。

**宽度方向** = 该点切线的垂直向量：
- 中间点：中心差分 `normalize(P[i+1] - P[i-1])`
- 首尾点：单边差分

垂直向量 = `(-tangent.y, tangent.x)`。

### 5.2 UV（方案 A 的关键）

- **V 方向（横向，跨宽度）**：铺 `0 → 1`，直接采样贴图的软边渐变——这是辉光的来源
- **U 方向（纵向，沿长度）**：累积**实际距离**并归一化，**不能用点序号**。点疏密不均时用序号会导致贴图拉伸不匀

### 5.3 急转弯自交

转角内侧两顶点会交叉，产生视觉上的"打结"。缓解：
- 按相邻段夹角衰减该点宽度（夹角越锐宽度越窄）
- 参考图中的大弧线不会触发，但组件通用化后必然有人拿它做急转，**必须处理**

### 5.4 顶点色

`widthCurve` (`AnimationCurve`) 与 `colorGradient` (`Gradient`) 按"该点在尾巴中的归一化位置"（0 = 尾端最旧，1 = 头端最新）采样，颜色写入顶点色。这样共享 Renderer 下每条尾巴仍可独立着色（对应 §3.1）。

### 5.5 提交时机

`UITrailRenderer` 在 `LateUpdate` 中收集所有注册 Emitter 的点、老化、然后 `SetVerticesDirty()`，由 `OnPopulateMesh(VertexHelper)` 统一填充。

**注意**：所有 Emitter 的所有尾巴填进**同一个 `VertexHelper`**（多段互不相连的 strip），这正是合批的实现方式。

---

## 6. Shader 与贴图（方案 A）

### 6.1 贴图规格

- 尺寸建议 **64×16**（U 沿长度，V 跨宽度），或纯横向渐变 8×64
- 内容：中心过曝白芯 → 暖黄扩散 → 边缘 alpha 归零的**软边衰减**
- 格式：带 alpha，WebGL 下建议 ETC2 / ASTC，关闭 mipmap，`Wrap Mode = Clamp`（V 方向必须 Clamp，否则边缘会串色）

### 6.2 Shader 要点

新建 `Assets/Shader/UITrailGlow.shader`，以 `CardDragGlow.shader` 为蓝本：

```
Blend SrcAlpha One      // 加色，alpha 当强度用（比纯 One One 好控）
ZWrite Off
ZTest Always            // UI 层，不参与深度
Cull Off
#pragma target 3.0
```

**必须包含的 UI 专用属性**（`CardDragGlow` 是世界空间 shader，没有这些）：

- `_Stencil` / `_StencilComp` / `_StencilOp` / `_StencilReadMask` / `_StencilWriteMask` / `_ColorMask` —— `Mask` 组件依赖
- `UNITY_UI_CLIP_RECT` + `_ClipRect` —— 否则放进 `ScrollView` 会漏到 `RectMask2D` 框外
- `UNITY_UI_ALPHACLIP`

不加这些也能跑，但一旦有人把带拖尾的 UI 放进滚动列表就会穿帮。

### 6.3 动态感（弥补无 bloom）

沿用 `CardDragGlow` 的思路，`_Time.y` 驱动、CPU 每帧零开销：
- **流动**：U 方向 scroll 一层噪声或第二层贴图，调制亮度
- **呼吸**：整体强度缓慢起伏

参数暴露到 Profile，可整体关闭（静态拖尾也应可用）。

---

## 7. 对外接口形态

### 7.1 `UITrailProfile` (ScriptableObject)

```
material / texture
widthCurve      : AnimationCurve
colorGradient   : Gradient
lifetime        : float
minSampleDistance : float
maxPoints       : int (默认 32)
flowSpeed / breathAmount 等 shader 参数
```

用 SO 装参数的双重收益：策划可存预设复用；同时它天然就是 §3.1 的合批分组键。

### 7.2 `UITrail` (Emitter，用户挂载)

```
profile   : UITrailProfile
layer     : Below | Above    // 首版仅 Below 生效，见 §8 D3
emitting  : bool        // 运行时可开关；关闭后停止新采样，已有点仍正常老化消散
tint      : Color       // 叠加到 colorGradient 上，走顶点色
Clear()                 // 瞬移 / 池复用时调用；亦是"立即冻结并清空"的唯一手段
```

---

## 8. 已确认决策

### D1. 拖尾时钟源 = `Time.unscaledDeltaTime`（确认）

采样节流与逐点老化**一律使用 `Time.unscaledDeltaTime`**，不接入 `GameLoop.GameTime`。

理由是依赖层面的而非表现层面的：接入 `GameLoop` 意味着组件必须走 Zenject 注入，定位就从"挂上就能用的通用 UI 组件"退化成"必须在本工程 DI 容器内才能工作"，通用性归零。

代价与补偿：游戏暂停（`GameLoop.IsPaused`）时拖尾**仍会老化消散**，与 `TacticalCardPresenter` 等既有表现的暂停语义不一致。若某业务确需暂停冻结，由业务方在暂停时置 `emitting = false` 自行控制——**注意这只停止新采样，已有点仍会淡出**。需要完全冻结的话调 `Clear()`。

此取舍需在 `UITrail` 类注释中显式写明，避免后续误接 `GameTime`。

### D2. 贴图先用程序化占位图（确认）

先由脚本按 §6.1 规格生成一张占位 PNG（中心白芯 → 暖黄扩散 → 边缘 alpha 归零的横向软边渐变），用于打通 mesh / UV / shader 全链路；美术正式图到位后直接替换同路径文件即可，无需改代码。

占位图生成走**编辑器脚本一次性产出静态 PNG 资源**，不是运行时 `Texture2D` 动态生成——后者违反 C3（WebGL 预算）。生成脚本置于 `Assets/Editor/`，不进包体。

### D3. 首版只实现 `Below` 层（确认）

参考图效果只需要拖尾位于飞行物之后。首版：

- `UITrailLayer` 枚举**保留 `Below` / `Above` 两个值**（避免后续加值时破坏已序列化的 Inspector 数据）
- 仅实现 `Below` 分支（`SetAsFirstSibling()`）
- 选中 `Above` 时 `Debug.LogWarning` 提示未实现并回退到 `Below`

§3.1 的分组键仍按 `profile × layer` 设计，`Above` 后续补齐时无需改结构。

---

## 9. 交付物清单

| 文件 | 类型 | 说明 |
|---|---|---|
| `Assets/Scripts/UI/Trail/UITrail.cs` | 新增 | Emitter，用户挂载入口 |
| `Assets/Scripts/UI/Trail/UITrailRenderer.cs` | 新增 | `MaskableGraphic` 子类，ribbon mesh 生成（工程内首个 Graphic 子类） |
| `Assets/Scripts/UI/Trail/UITrailProfile.cs` | 新增 | ScriptableObject 参数容器 |
| `Assets/Shader/UITrailGlow.shader` | 新增 | UI 加色发光，含 Stencil / ClipRect 支持 |
| `Assets/Editor/UITrailTextureGenerator.cs` | 新增 | 编辑器菜单项，一次性产出占位贴图（见 D2），不进包体 |
| `Assets/UI/Textures/TrailGlow.png` | 新增 | 软边光带贴图，首版为占位图，美术图到位后**原路径替换** |
| `Assets/UI/Trail/DefaultTrailProfile.asset` | 新增 | 默认 profile 预设 |

均为**新增文件，不修改任何既有脚本**。

---

## 10. 验证方式

1. 空场景挂一个 `Image` + `UITrail`，用简单脚本让它走贝塞尔曲线，观察光带是否留在路径上（验证 §2 坐标空间）
2. 让它停下不动，观察尾巴是否正常淡出（验证 §4.1 时间戳老化）
3. 运行时 `transform.position` 瞬移，确认不调 `Clear()` 会拉出长直线、调了则不会（验证 §4.3）
4. 同 profile 挂 3 个 Emitter，Frame Debugger 确认合并为 **1 个 drawcall**（验证 §3.1）
5. Profiler 对比开启前后主 Canvas 的 `Canvas.BuildBatch` 耗时，确认独立 Canvas 隔离生效（验证 C2）
6. 放进 `ScrollView` 滚动，确认光带被正确裁剪（验证 §6.2）
7. 游戏暂停（`GameLoop.IsPaused`）时移动挂载对象，确认拖尾仍正常生成与消散（验证 §8 D1 的取舍确实生效，而非意外接入了 `GameTime`）
