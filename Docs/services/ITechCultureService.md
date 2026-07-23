
---

### ITechCultureService.md

# ITechCultureService

## 职责描述
管理科技和文化点数、等级，提供每回合产量，并触发 UI 更新。科技和文化树通过此接口与游戏逻辑交互。

## 依赖项
- `IUnitDataProvider`
- `IBuildingDataProvider`
- `ITechTreeIconsProvider`
- `IUnitRepository`
- `PlayerModelManager`
- `TechData`
- `CultureData`

实现类 `Tech_CultureTreeController` 通过 Zenject 注入上述依赖。

## 公共方法
请参考 [API 文档](../api/ITechCultureService.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class UIManagerPresenter
{
    private ITechCultureService _techCultureService;

    [Inject]
    public UIManagerPresenter(ITechCultureService techCultureService)
    {
        _techCultureService = techCultureService;
        _techCultureService.OnTechPointsChanged += UpdateTechUI;
    }

    private void UpdateTechUI()
    {
        Debug.Log($"科技点数: {_techCultureService.TechPoints}");
    }
}
```
