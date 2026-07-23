
---

### IUIManagerView.md

# IUIManagerView

## 职责描述
定义 UI 管理器的视图接口，用于更新科技文化点数、显示/隐藏单位信息面板、刷新面板数据等。与 `UIManagerPresenter` 配合实现 UI 与逻辑的分离。

## 依赖项
- `IUnitDataProvider`
- `UIConfigSO`

实现类 `UIManager` 通过 Zenject 注入上述依赖。

## 公共方法
请参考 [API 文档](../api/IUIManagerView.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class UIManagerPresenter
{
    private IUIManagerView _view;

    [Inject]
    public UIManagerPresenter(IUIManagerView view)
    {
        _view = view;
    }

    public void SelectUnit(CharacterData unit)
    {
        _view.ShowUnitInfoPanel(unit);
    }

    public void UpdateTechCulture(int tech, int culture)
    {
        _view.SetTechPoints(tech);
        _view.SetCulturePoints(culture);
    }
}
```
