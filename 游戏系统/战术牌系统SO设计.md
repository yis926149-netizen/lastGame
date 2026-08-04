# 战术牌系统 SO 设计

## 一、概述

战术牌（维修、战斗号令）为玩家主动拖拽释放的即时效果卡牌，区别于常规卡牌（部署单位/建筑）和天赋卡牌（永久Buff）。

设计原则：与天赋卡牌保持一致的三层SO架构，数据层各自独立。

---

## 二、SO 层级结构

```
Assets/
├── Scripts/
│   └── TacticalCard/
│       ├── Data/
│       │   ├── TacticalEffectType.cs        ← 效果类型枚举
│       │   ├── TacticalCardEffect.cs        ← 效果参数结构体
│       │   ├── TacticalCardSO.cs            ← 单张卡 SO 脚本
│       │   └── TacticalCardDatabaseSO.cs    ← 数据库 SO 脚本
│       ├── TacticalCardInstance.cs          ← 运行时实例（含数量）
│       └── TacticalCardPresenter.cs         ← 管理器（后续实现）
│
├── ScriptableObjects/
│   └── TacticalCard/
│       ├── TacticalCardDatabase.asset        ← 数据库实例
│       ├── TacticalCard-Repair.asset         ← 维修
│       └── TacticalCard-BattleOrder.asset    ← 战斗号令
```

---

## 三、数据定义

### 3.1 效果类型枚举（`TacticalEffectType.cs`）

```csharp
public enum TacticalEffectType
{
    Repair       = 0,  // 维修：恢复建筑生命值
    BattleOrder  = 1,  // 战斗号令：临时提升步兵攻速和移速
}
```

### 3.2 效果参数结构体（`TacticalCardEffect.cs`）

```csharp
[System.Serializable]
public struct TacticalCardEffect
{
    [Tooltip("回复比例（维修用）：0.3 = 恢复 30% 最大 HP")]
    public float healRatio;

    [Tooltip("攻击力提升乘数（战斗号令用）：1.3 = +30%")]
    public float attackMultiplier;

    [Tooltip("移速提升乘数（战斗号令用）：1.2 = +20%")]
    public float speedMultiplier;

    [Tooltip("持续时间（战斗号令用），秒")]
    public float duration;
}
```

不同 `effectType` 使用结构体内不同字段：

| effectType | 使用字段 |
|-----------|---------|
| Repair (0) | `healRatio` |
| BattleOrder (1) | `attackMultiplier`, `speedMultiplier`, `duration` |

### 3.3 单张卡 SO（`TacticalCardSO.cs`）

```csharp
[CreateAssetMenu(fileName = "TacticalCard", menuName = "Game Data/Tactical Cards/Tactical Card")]
public class TacticalCardSO : ScriptableObject
{
    [Header("显示")]
    [Tooltip("唯一 ID")]
    public string cardId;            // 如 "repair" / "battle_command"

    [Tooltip("名称")]
    public string cardName;          // 如 "维修" / "战斗号令"

    [Tooltip("描述")]
    [TextArea(3, 6)]
    public string description;

    [Tooltip("卡面图")]
    public SpritecardSprite;

    [Header("效果")]
    [Tooltip("效果类型")]
    public TacticalEffectType effectType;

    [Tooltip("效果参数")]
    public TacticalCardEffect effect;
}
```

### 3.4 数据库 SO（`TacticalCardDatabaseSO.cs`）

```csharp
[CreateAssetMenu(fileName = "TacticalCardDatabase", menuName = "Game Data/Tactical Cards/Tactical Card Database")]
public class TacticalCardDatabaseSO : ScriptableObject
{
    [Tooltip("所有战术卡牌定义")]
    public List<TacticalCardSO> cards = new();
}
```

---

## 四、运行时数据

### 4.1 `TacticalCardInstance`

SO 是只读模板。运行时需要记录叠放数量：

```csharp
public class TacticalCardInstance
{
    public TacticalCardSO Config;   // 引用 SO 模板
    public int Quantity;             // 当前叠放数量。0 表示已耗尽。

    public bool IsEmpty => Quantity <= 0;
}
```

`TacticalCardPresenter` 内部维护 `List<TacticalCardInstance>`，最多 2 种卡（维修 + 战斗号令），每种初始数量由策划配置（开局各 ×1）。

---

## 五、运行时配置（策划案 §E）

| 卡ID | 名称 | 效果 | 参数 |
|------|------|------|------|
| `repair` | 维修 | 恢复目标建筑 30% 最大 HP | `healRatio: 0.3` |
| `battle_command` | 战斗号令 | +30% 攻击力、+20% 移速，持续 8 秒 | `attackMultiplier: 1.3`, `speedMultiplier: 1.2`, `duration: 8` |

---

## 六、与现有系统对比

| 对比项 | 天赋卡 | 普通卡 | 战术卡 |
|--------|--------|--------|--------|
| 单卡定义 | `TalentCardConfigSO` | `UnitDatabaseSO` 里的条目 | `TacticalCardSO` |
| 数据库 | `TalentCardPoolSO` | `UnitDatabaseSO` | `TacticalCardDatabaseSO` |
| 运行时状态 | 无（选完即永久生效） | `CardData`（ID + Sprite） | `TacticalCardInstance`（Config + Quantity） |
| 效果参数 | `TalentCardEffect`（statId + 乘数） | 无（ID → 直接 Spawn） | `TacticalCardEffect`（healRatio + 攻速 + 持续时间） |
| 发放方式 | 3选1弹窗 | 从卡池随机抽 | 开局固定发放 |
| 消耗 | 无 | 金币 | 数量 -1（不消耗金币） |
| 补充 | 无 | 使用后立即补 | 不自动补充 |
| 叠放 | 不可重复 | 可同名 | 同名牌叠放显示数量 |
| 使用方式 | 点选即生效 | 拖拽到 hex | 拖拽到目标单位/建筑 |

---

## 七、UI 表现思路（后续实现）

1. 复用普通卡牌 `Card.prefab` 的外观样式（卡面图 + 边框），直接在 TacticalCardSO 里设定 `cardSprite` 作为 `Card.prefab` 的 `Image` 素材。
2. 屏幕上两个固定位置（锚点），由 TacticalCardPresenter 在实例化时将卡牌 Parents 到对应锚点，`anchoredPosition = Vector2.zero`。
3. 复用 `CardController` 的拖拽逻辑（先坠落到己方单位/建筑上），不做 hex 高亮。
4. 释放后执行 TacticalCardEffect 中的效果（暂时通过 Debug.Log 打印，后续再实现具体逻辑）。
