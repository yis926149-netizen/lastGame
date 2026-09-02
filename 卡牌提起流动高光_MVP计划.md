# 卡牌「提起」边缘流动高光 — MVP 验证计划

> 目标：**先确认一条高光能否沿着 UGUI Image 的真实透明轮廓连续流动**，再谈接入卡牌「提起」动效。
> 本版只做最小可行验证，不追求一步到位。

---

## 0. 一句话目标

```text
普通图片透明轮廓 ────▶ 一条亮光沿边缘连续流动
```

先不处理：复杂粒子、多颜色渐变、复杂噪声、性能优化、多图批量应用。

---

## 1. 项目现状（本计划的事实依据）

| 项 | 现状 |
|---|---|
| Unity 版本 | 2022.3.62f3c1（LTS） |
| 渲染管线 | URP 14.0.12（可用 Global Volume + Bloom 后处理） |
| 卡牌渲染 | UGUI `Image` + `Sprite`，代码里 `_image.sprite = data.CardSprite` |
| 卡牌预制体 | `Assets\UI\UnitCards\Card.prefab`（普通卡）、`TacticalCard.prefab`（战术卡）、`NextCard.prefab`（预告卡） |
| 卡牌代码 | `Assets\Scripts\UI\CardController.cs`，`SetData()` 里 `_image.sprite = data.CardSprite` |
| 提起动效入口 | `CardController.RaiseCard()`（当前已实现：上移 + 上升途中放缩） |
| 自定义 Shader 位置 | `Assets\Shader\`（现有 `VertexColor.shader`，流光 Shader 新建于此） |
| 参数配置 | `Assets\Scripts\Core\Services\FeelConfigProvider.cs`（后续可把流光参数迁入） |

---

## 2. 技术路线

```text
UGUI Image
+ 自定义 UI Shader（Alpha 轮廓检测 + UV 流动）
+ （URP Bloom 后处理做光晕）
```

- 不使用 Unity 自带 `Outline`（只适合静态描边，无法得到连续流动的真实轮廓）。
- 项目是 URP，光晕优先走 **Global Volume + Bloom**。

---

## 3. 测试资源准备

准备一张透明背景 PNG，例如：

- 一个圆形图标
- 一个不规则道具图标（五边形/异形）

要求：

- 图片必须有透明背景；
- 外轮廓与透明区域对比明显；
- 先不用特别复杂的图片；
- 边缘避免半透明阴影/羽化（会干扰轮廓检测）。

导入设置：

- `Texture Type = Sprite (2D and UI)`
- `Alpha Is Transparency = true`
- 关闭压缩或保证透明通道不失真

---

## 4. 第一阶段：静态轮廓 Mask（最关键）

> 目的：确认 Shader 能找到图片的透明边缘。

判定逻辑：

```text
当前像素透明
且周围像素（上下左右）至少一个不透明
=> 认为是轮廓
```

先只输出纯红色：

```text
轮廓区域 = 红色
其他区域 = 透明
```

预期结果：

```text
图片本体不显示，只有图片外轮廓显示红色
```

### 验收标准

- 轮廓跟随图片形状；
- 不是简单的矩形边框；
- 四个角没有明显断裂；
- 轮廓宽度基本一致。

> 若这一步不对，先不要做流动效果。

---

## 5. 第二阶段：加入移动高光

轮廓确认后，叠加最简单的移动渐变。先不引入纹理，用斜向正弦波：

```text
Flow = sin(UV.x + UV.y + Time × Speed)
Glow = OutlineMask × Flow
颜色输出 = 白色或青色
```

预期效果：

```text
轮廓整体存在，其中一段比较亮，亮点沿斜向移动
```

这一阶段只验证**运动感**，不追求严格沿轮廓走一圈。

---

## 6. 第三阶段：让高光更像“沿边缘流动”

若第二阶段只是“斜向扫过图片”，则引入一张渐变纹理。推荐先做一张横向渐变：

```text
黑 → 蓝 → 白 → 蓝 → 黑
```

Shader 中滚动采样 UV：

```text
FlowUV.x = UV.x + Time × Speed
Glow = OutlineMask × FlowTexture
```

可选叠加两层让效果更自然：

```text
Glow = OutlineMask × MovingGradient × Noise
```

但第一版只用一张黑白渐变纹理即可。

---

## 7. 第四阶段：加入柔和光晕

### 方式 A：URP Bloom（推荐）

项目已用 URP，直接：

```text
Global Volume └── Bloom
```

Shader 中提高高光亮度：

```text
GlowIntensity = 3 ~ 8
```

### 方式 B：Shader 内部模拟

复制一层较宽、较透明的轮廓：

```text
OuterGlow = WiderOutline × 低透明度
Final = 原图 + 外部柔光 + 流动亮边
```

不如 Bloom 真实，但不依赖后处理。

---

## 8. 推荐参数（第一版）

```text
Glow Color     = #7DEBFF（青蓝）
Glow Width     = 1~2 像素
Glow Intensity = 2~4
Flow Speed     = 0.3~0.8
Gradient Width = 图片宽度的 20%~30%
```

颜色方向：

- 青蓝：科技、能量
- 金黄：稀有、奖励
- 紫色：魔法、史诗
- 白色：通用高光

---

## 9. Shader 实现要点（伪代码结构）

```hlsl
Properties
{
    _MainTex        ("Sprite", 2D) = "white" {}
    _GlowColor      ("GlowColor", Color) = (0.49, 0.92, 1.0, 1.0)  // #7DEBFF
    _OutlineWidth   ("OutlineWidth", Range(0, 8)) = 2     // 像素
    _GlowIntensity  ("GlowIntensity", Range(0, 10)) = 3
    _FlowSpeed      ("FlowSpeed", Range(-2, 2)) = 0.5
    _FlowTex        ("FlowTex", 2D) = "white" {}
}

// 关键：用 _MainTex_TexelSize 以纹理像素为单位控制描边宽度，
// 避免 Image 缩放时轮廓宽度跟着变化。
half4 outline = 0;
outline = max(outline, tex2D(_MainTex, uv + float2( texel.x, 0) * _OutlineWidth));
outline = max(outline, tex2D(_MainTex, uv + float2(-texel.x, 0) * _OutlineWidth));
outline = max(outline, tex2D(_MainTex, uv + float2(0,  texel.y) * _OutlineWidth));
outline = max(outline, tex2D(_MainTex, uv + float2(0, -texel.y) * _OutlineWidth));

half centerAlpha = tex2D(_MainTex, uv).a;
half edge = saturate((outline.a - centerAlpha) * _OutlineWidth);  // 轮廓 Mask
// edge 做 smoothstep 抗锯齿

half2 flowUV = uv;
flowUV.x += _Time.y * _FlowSpeed;   // 或斜向：uv + _Time.y * _FlowSpeed
half flow = tex2D(_FlowTex, flowUV).r;
half glow = edge * flow * _GlowIntensity;

half4 c = tex2D(_MainTex, uv);              // 原图
half3 col = c.rgb + _GlowColor.rgb * glow;  // 叠加亮边
return half4(col, c.a);                     // 保持原图 alpha
```

要点：

- 轮廓检测用四方向采样（后续可加八方向提高圆滑度）；
- 轮廓宽度以 `_MainTex_TexelSize` 像素为单位，不随 Image 尺寸变化；
- 对 `edge` 用 `smoothstep` 抗锯齿；
- 最终输出保留原图 alpha，不影响卡面原本显示与射线（`alphaHitTestMinimumThreshold`）。

---

## 10. 接入卡牌「提起」的挂载方式（计划）

> MVP 阶段先用一个独立测试场景/测试 Image 验证 Shader 本身；验证通过后再接入卡牌。

接入思路（待 Shader 稳定后实施）：

1. **材质实例化**：`CardController.Construct/SetData` 里克隆材质实例（`_image.material = new Material(flowShader)` 或用 `MaterialPropertyBlock`），避免多张卡共享同一材质互相污染。
2. **驱动开关**：`RaiseCard()` 时开启流光（或驱动 `_Time`/进度），`LowerCard()` / 拖拽 / 买不起 / 回收时复位并关闭。
3. **参数来源**：第一版先硬编码/常量；稳定后再迁入 `FeelConfigProvider`（与现有数值化风格一致）。
4. **兼容性确认**：
   - 流光 Shader 的输出必须仍包含原图，否则卡面会“只剩轮廓”；
   - 保持 `Image.raycastTarget` 与 `alphaHitTestMinimumThreshold = 0.01` 不受影响；
   - 注意卡牌压暗（买不起）走的是 `ApplyGraphicAlpha()`（改 Graphic.color），流光 Shader 用材质参数控制亮度，两者互不干扰。

---

## 11. 建议验证顺序

按序做，出问题好定位：

```text
 1. Image 正常显示（原图 OK）
 2. 只显示 Alpha 轮廓（纯红）
 3. 轮廓颜色可调
 4. 高光亮度可调
 5. 高光可以移动
 6. 高光移动速度可调
 7. 添加渐变纹理
 8. 添加 Bloom 或外部光晕
 9. 测试不同形状图片
10. 测试不同 UI 尺寸 / 缩放
```

---

## 12. 验收标准（本版快速验证成功 = 全部满足）

- 可挂在普通 UGUI `Image` 上；
- 图片透明区域能生成真实轮廓；
- 有一段明显高光；
- 高光能持续移动；
- 可通过材质参数调整颜色与速度；
- 放大缩小 Image 后效果仍基本正常；
- 不影响图片原本的显示。

---

## 13. 可能遇到的问题与对策

1. **只出现矩形边框**
   - 原因：PNG 无透明通道 / Sprite 导入后 Alpha 被忽略 / Shader 用了矩形 UV 边界而非图片 Alpha。
   - 对策：检查 `Alpha Is Transparency`、纹理压缩、确认采样的是 `_MainTex.a`。

2. **高光斜向穿过图片而不是沿轮廓**
   - 这是第一版斜向 UV 流动的正常结果；要严格贴合轮廓需后续做 SDF/距离场或预烘焙边缘纹理（见升级路线）。

3. **边缘锯齿**
   - 对策：提高 Sprite 分辨率、开 Filter Mode、八方向采样、对 alpha 用 `smoothstep`。

4. **Image 改尺寸后描边宽度变化**
   - 对策：用 `_MainTex_TexelSize` 以纹理像素为单位控制宽度（本计划已采用）。

5. **多张卡共享材质互相污染**
   - 对策：每卡克隆材质实例，或用 MaterialPropertyBlock。

---

## 14. 后续升级路线

```text
MVP   ：Alpha 轮廓 + 移动渐变
增强   ：Alpha 轮廓 + 渐变纹理 + 噪声
高级   ：SDF 距离场 + 严格沿轮廓流动 + Bloom
最终   ：自定义边缘坐标图，让高光真正绕图片轮廓循环
```

最建议的第一版落点：

> **四方向 Alpha 采样生成轮廓，再用一张黑白渐变纹理横向滚动，最后叠加 URP Bloom。**

---

## 15. 本版明确不做

- 复杂粒子系统
- 多颜色渐变叠加
- 复杂噪声纹理
- 性能优化（合批、缓存、SRP Batcher 兼容性）
- 多张卡牌批量应用/资源管理

---

## 16. 产出物清单（MVP 完成后应有）

1. `Assets\Shader\CardEdgeFlow.shader`（自定义 UI 流光 Shader）
2. `Assets\Shader\CardEdgeFlow.mat` 或运行时材质（测试用）
3. 一张测试用的透明轮廓 PNG（圆形/五边形）
4. 一张黑白渐变纹理（第三阶段用）
5. 独立测试场景/测试 Image（验证 Shader）
6. （接入阶段）`CardController` 的材质实例化 + 提起/落下开关逻辑

> 本计划当前只到「计划」层级，未开始写 Shader / 代码。待确认后按阶段推进。
