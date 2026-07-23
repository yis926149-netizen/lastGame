
---

### IUnitRepository.md

# IUnitRepository

## 职责描述
单位数据仓库，统一管理玩家和敌方单位的实例与数据，并提供事件通知。所有单位生成、销毁时都应通过仓库记录。

## 依赖项
无（独立数据容器，可被任何需要访问单位数据的服务注入）。

## 公共方法
请参考 [API 文档](../api/IUnitRepository.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class UnitService : IUnitService
{
    private IUnitRepository _repository;

    [Inject]
    public UnitService(IUnitRepository repository)
    {
        _repository = repository;
    }

    public List<CharacterData> GetAllPlayerUnits()
    {
        return _repository.AllPlayerUnits.Values.ToList();
    }

    public void AddEnemyUnit(int aiIndex, GameObject unit, CharacterData data)
    {
        _repository.AddEnemyUnit(aiIndex, unit, data);
    }
}
```
