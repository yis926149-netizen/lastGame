
---

### IUnitMovement.md

# IUnitMovement

## 职责描述
控制单位的移动行为，包括移动到指定六边形、取消移动、重置移动力、获取可达范围等。此接口由 `UnitMovementController` 实现。

## 依赖项
- `IMapDataService`
- `MapVisualEventSO`
- `UnitMovementSystem`
- `UIManagerPresenter`
- `AudioManager`

实现类 `UnitMovementController` 通过 Zenject 注入上述依赖。

## 公共方法
请参考 [API 文档](../api/IUnitMovement.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class UnitCommandHandler
{
    public void MoveUnit(IUnitMovement unit, Vector3 targetHex)
    {
        if (unit.RemainingMovement > 0)
        {
            unit.MoveTo(targetHex, Enums.MovementPurpose.MoveToDestination);
        }
    }

    public void ShowReachable(IUnitMovement unit)
    {
        List<Vector3> reachable = unit.GetReachableHexes();
        foreach (var hex in reachable)
        {
            // 高亮显示
        }
    }
}
```
