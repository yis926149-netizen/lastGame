
---

### IUnitService.md

```markdown
# IUnitService

## 职责描述
提供对玩家和敌方单位的访问与管理，包括获取单位列表、添加/移除敌方单位、查询AI城市数量和势力范围等。简化对 `IUnitRepository` 和 `EnemyModelManager` 的操作。

## 依赖项
- `IUnitRepository`（单位数据仓库）
- `EnemyModelManager`（敌方势力范围、城市计数等数据）

实现类 `UnitService` 通过 Zenject 注入上述依赖。

## 公共方法
请参考 [API 文档](../api/IUnitService.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class AITurnHandler
{
    private IUnitService _unitService;

    [Inject]
    public AITurnHandler(IUnitService unitService)
    {
        _unitService = unitService;
    }

    public void OnAITurn()
    {
        var enemyUnits = _unitService.GetAllEnemyUnits();
        foreach (var unit in enemyUnits)
        {
            // 处理AI单位逻辑
        }

        int cityCount = _unitService.GetAICityCount(1);
        Debug.Log($"AI 1 的城市数量: {cityCount}");
    }
}