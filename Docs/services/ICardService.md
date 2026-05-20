
---

### ICardService.md

```markdown
# ICardService

## 职责描述
管理卡牌系统的核心逻辑，包括卡槽、卡牌ID生成、抽卡/发卡机会控制等。与 `CardPresenter` 配合实现完整的卡牌流程。

## 依赖项
- `IUnitDataProvider`
- `IBuildingDataProvider`
- `IUIConfigProvider`
- `ITechCultureService`
- `IGameStateMachine`

实现类 `CardService` 通过 Zenject 注入上述依赖。

## 公共方法
请参考 [API 文档](../api/ICardService.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class CardPresenter
{
    private ICardService _cardService;

    [Inject]
    public CardPresenter(ICardService cardService)
    {
        _cardService = cardService;
    }

    public void TryDealNextCard()
    {
        if (_cardService.CanDealThisTurn())
        {
            int emptySlot = _cardService.GetFirstEmptySlot();
            if (emptySlot != -1)
            {
                int cardID = _cardService.GenerateNextCardID();
                // 创建卡牌视图...
            }
        }
    }
}