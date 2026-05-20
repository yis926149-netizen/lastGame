# IMapDataService

## 职责描述
提供地图核心数据的访问，包括六边形坐标与地块数据的映射、邻居查询、世界坐标转换等。通过该接口可以获取地图中所有地块的详细信息，如地块类型、坐标、高度、建筑、单位等。

## 依赖项
- `MapGenerationConfigSO`（配置地图生成参数）
- 无其他运行时依赖（初始化时通过 `Initialize` 方法注入数据，由地图生成系统提供）

实现类 `HexMapService` 通过 Zenject 绑定为单例，可在任何需要的地方注入。

## 公共方法
请参考 [API 文档](../api/IMapDataService.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class ExampleUser : MonoBehaviour
{
    private IMapDataService _mapDataService;

    [Inject]
    public void Construct(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    void Start()
    {
        // 通过世界坐标获取地块
        HexCellData cell = _mapDataService.GetCellByWorldPosition(transform.position);
        Debug.Log($"当前地块坐标: {cell.HexCoordinate}");

        // 获取邻居
        HexCellData neighbor = _mapDataService.GetNeighbor(cell, Enums.HexDirection.NE);
        if (neighbor != null)
        {
            Debug.Log($"NE邻居高度: {neighbor.Height}");
        }
    }
}