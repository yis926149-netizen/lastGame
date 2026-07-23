
---

### IGameStateMachine.md

# IGameStateMachine

## 职责描述
管理游戏回合状态机，控制当前回合和阶段。协调玩家阶段、AI阶段和结算阶段的流转，并提供当前回合数访问。

## 依赖项
- `PlayerPhase`
- `AIPhase`
- `SettlementPhase`
- `ICardService`
- `CardPresenter`
- `IUnitRepository`
- `ITechCultureService`

实现类 `GameStateMachine` 通过 Zenject 注入上述依赖。

## 公共方法
请参考 [API 文档](../api/IGameStateMachine.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class EndTurnButton
{
    private IGameStateMachine _gameState;

    [Inject]
    public EndTurnButton(IGameStateMachine gameState)
    {
        _gameState = gameState;
    }

    public void OnClick()
    {
        if (_gameState.CurrentPhase is PlayerPhase)
        {
            _gameState.EndTurn();
        }
    }
}
```
