# 项目 UI 动画检查结果

> 检索范围：`Assets/Scripts/**/*.cs`
> 底层动画引擎：**DOTween**（`Assets/DOTween_1_2_765`），辅以协程 / Update 状态机 / 手写缓动。
> 生成说明：本文档由代码检索整理，路径与行号对应检索时的源文件。

---

## 一、卡牌与手牌（核心，最密集）

| 位置 | 动画 | 机制 |
|---|---|---|
| `Assets/Scripts/UI/CardController.cs` `PlayDealAnimation` (226) | 发牌 / 预告卡入场：手牌 `DOAnchorPos` + `DOScale` 0.4s；预告卡 0.6s `Ease.OutBack` 回弹弹出 | DOTween |
| `CardController.cs` 322 / 330 | 鼠标悬停上浮 / 离开回落 `DOAnchorPos` 0.2s（上浮量为参考高度比例） | DOTween |
| `CardController.cs` `ResetToOrigin` (290) | 拖拽取消 / 失败复位：`DOAnchorPos` + `DOScale` 0.2s | DOTween |
| `CardController.cs` `OnDragUpdate` (250) | 拖拽中实时缩放 + 淡出（卡牌→模型两阶段预览的卡牌侧） | 逐帧计算 |
| `Assets/Scripts/Core/Services/CardPresenter.cs` `PromoteNextCardToHand` (225) | 次卡滑入手牌：`DOAnchorPos` + `DOScale` 0.3s `Ease.OutQuad` | DOTween |
| `CardPresenter.cs` `TryInsertCardAtFront` (720) | 手牌整体右移：每张 `DOAnchorPos` 0.2s `Ease.OutQuad` | DOTween |
| `CardPresenter.cs` `InsertCardAtFrontWithFly` (769) | 奖励卡两段式飞入：上升放大 → 俯冲入位（0.75s，`GameTime` 驱动，手写 `EaseOutQuad`/`EaseOutBack`/`EaseInCubic` + 贝塞尔） | DOTween OnUpdate |
| `Assets/Scripts/TacticalCard/TacticalCardPresenter.cs` `PlayFlySequence` (173) | 战术卡两段式飞入（参考实现）+ 落地 `DOPunchScale` 弹跳槽位卡 / 数量徽标 (353/360) | DOTween |
| `Assets/Scripts/UI/CardDragPreview/CardDragPreviewController.cs` (96/175) | 拖拽模型预览：`RawImage` alpha + `localScale` 逐帧跟随指针，`Animator.speed` 暂停冻结 | Update 逐帧 |

---

## 二、天赋卡（TalentCard）

| 位置 | 动画 | 机制 |
|---|---|---|
| `Assets/Scripts/TalentCard/TalentCardSelectionUI.cs` `PlayEntranceAnimation` (306) | 暗幕 `DOFade` 0.7 + 三张卡错开弹入（`DOScale` `Ease.OutBack` + `DOFade` `Ease.OutQuad`） | `DOTween.Sequence` |
| `Assets/Scripts/TalentCard/TalentCardSlotVisual.cs` `PlaySelectAnimation` (46) | 选中：放大 `DOScale` + 整卡 `DOFade` 淡出 + 闪光 `DOFade` + 随机抖动复位 | `DOTween.Sequence` |
| `TalentCardSelectionUI.cs` 367 | 选中后屏幕震动 `CameraController.Shake` | 相机抖动 |

---

## 三、通用入场动画工具

- `Assets/Scripts/Utilities/ScaleInEffectPlayer.cs`：可复用**入场动画播放器**，集中列表配置，三种类型：
  - **Scale 缩放入场**：先放大 n 倍 → `DOScale` 回缩（默认 `Ease.OutBack` 过冲）
  - **Fade 淡入恢复**：整棵子树置透明 → 逐组件 `DOFade` 恢复，自动识别 `CanvasGroup` / `Graphic` / `SpriteRenderer` / `Renderer`
  - **Position 位移恢复**：起始位置 + 偏移 → `DOLocalMove` 复位
  - 支持延迟 `DOVirtual.DelayedCall`、`useUnscaledTime`、`onComplete`。

---

## 四、HUD / 面板 / 浮标

| 位置 | 动画 | 机制 |
|---|---|---|
| `Assets/Scripts/UI/GlobalTimerUI.cs` (58) | 倒计时紧急态呼吸脉冲：`DOScale` 1.15 倍 `SetLoops(-1, Yoyo)` | DOTween |
| `Assets/Scripts/UI/UIController.cs` (194) | `UnitInfoPanel` 信息面板滑入 / 滑出：`DOAnchorPos` 0.5s | DOTween |
| `Assets/Scripts/UI/PublicBuildingMarkerView.cs` (54) | 公共建筑浮标呼吸动画：`Mathf.Sin` + `localScale` / `CanvasGroup.alpha` 脉动 | LateUpdate |
| `Assets/Scripts/UI/ProductionProgressImages.cs` | 兵营生产进度帧动画（按进度切 `Sprite` 帧，非补间） | 帧切换 |
| `Assets/Scripts/Managers/CostLabelRenderer.cs` (102/219) | 探索费用标签可负担态 alpha 切换（1.0 / 0.35，非连续动画） | 直接赋值 |

---

## 五、开始场景菜单（StartScene）

| 位置 | 动画 | 机制 |
|---|---|---|
| `Assets/Scripts/Scenes/StartScene/openController.cs` `OpenAni` (229) | 开场侧栏 `DOMove` 滑入 0.5s，首级按钮逐个滑入 0.2s（递归 `OnComplete`） | DOTween |
| `Assets/Scripts/Scenes/StartScene/UIControl.cs` (146–234) | 侧栏 / 按钮 `DOMove` 平移、右栏 `DOScaleY` 伸缩展开、按钮 `DOColor` 淡出、切页滑动 | DOTween |
| `Assets/Scripts/Scenes/StartScene/SimpleStartButton.cs` (86) | 开局按钮呼吸缩放：`Mathf.Sin` 周期 1.6s、幅度 0.06，支持相位错开 | Update |
| `gameOptionController.cs` / `exceptionController.cs` | 主要做按钮点击绑定 / 弹窗，无独立补间（复用 UIControl） | — |

---

## 六、探索奖励特效（UI 漂字 + 世界空间）

| 位置 | 动画 | 机制 |
|---|---|---|
| `Assets/Scripts/Managers/ExplorationAddCoinsUIEffect.cs` | +N 金币漂字：世界→屏幕映射，漂升 → 滞空 → `CanvasGroup` 淡出消散 | Update 状态机 |
| `Assets/Scripts/Managers/ExplorationCoinEffect.cs` | 3D 金币弹出（OutBack 缩放）→ 上升翻转 → 滞空 → 下坠 → 缩小消失 | 协程 + `AnimationCurve` |
| `Assets/Scripts/Managers/ExplorationPillarEffect.cs` / `ExplorationDiskEffect.cs` | 3D 光柱 / 飞盘揭示砸地（世界空间，非 UI） | 协程 |

---

## 七、底层动画引擎

- DOTween 主库 + 各模块（`Assets/DOTween_1_2_765/DOTween/Modules/`）：
  - `DOTweenModuleUI`：UI 位移 / 缩放 / 淡入 / 颜色 / 填充 / 抖动 / 跳字等
  - `DOTweenModuleSprite`、`DOTweenModuleAudio`、`DOTweenModulePhysics` 等
  - 项目内实际用到的 `DOAnchorPos` / `DOScale` / `DOFade` / `DOColor` / `DOPunchScale` / `DOPath` 均来自此。

---

## 小结

- **动画引擎**：统一走 DOTween（含 `Sequence` / `OnComplete` / `SetLoops` / `SetUpdate`），少数用协程 + `AnimationCurve`（探索金币 / 柱子 / 飞盘）和 Update / LateUpdate + `Mathf.Sin` / `Lerp`（呼吸类）。
- **两种「飞卡」**：`TacticalCardPresenter.PlayFlySequence`（战术卡）与 `CardPresenter.InsertCardAtFrontWithFly`（普通奖励卡）；后者刻意不用固定时长 driver，改用 `GameTime` 驱动以兼容暂停冻结。
- **可复用的通用入场动画**：`ScaleInEffectPlayer`（Scale / Fade / Position 三合一）是项目里唯一集中管理的通用 UI 入场工具。
