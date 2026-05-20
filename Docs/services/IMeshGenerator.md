# 服务接口：`IMeshGenerator`

## 职责描述
`IMeshGenerator` 负责生成六边形网格地图的所有视觉相关网格数据，包括：
- 地块实心区域（含河流、湖海）
- 矩形与三角形过渡区域（用于处理地形高度差）
- 河流水体网格
- 湖海与海岸网格
- 六边形网格线
- 移动路径连线
- 势力范围边缘线
- 迷雾轮廓

该服务将地形、河流、建筑等逻辑数据转换为具体的网格顶点、UV 和三角形索引，供 `MapRenderer` 构建最终的 Mesh。

## 依赖项
- `IMapDataService`：用于获取地块数据（`HexCellData`）及邻居关系。
- `MapGenerationConfigSO`：提供地图生成配置参数（半径、材质、混合参数等）。

上述依赖通过构造函数注入，由 Zenject 容器自动提供。

## 公共方法
由于接口方法数量庞大，此处按功能分类列出主要方法组，具体方法签名请参阅代码注释或自动生成的 API 文档。

### 地块实心区域
- `GetSolidAreaVertices`：返回地块实心区域的顶点坐标（含河道顶点）。
- `GetSolidAreaVerticesUV`：返回对应的 UV 坐标。
- `GetSolidAreaVerticesDrawOrder1/2/3`：返回不同河道情况下的三角形绘制顺序。

### 矩形过渡区域（坡/阶梯）
- `GetRectVertices` / `GetRectUV` / `GetRectDrawOrder`：处理斜坡矩形过渡。
- `GetRectStepVertices` / `GetRectStepUV` / `GetRectStepDrawOrder`：处理阶梯矩形过渡。
- 带 `River` 后缀的方法用于处理含河道的过渡区域。

### 三角过渡区域（方法一/三/四）
- `GetTriVertices` / `GetTriUV` / `GetTriDrawOrder`：基本三角过渡（三坡）。
- `GetTriStep3Vertices` / `GetTriStep3UV` / `GetTriStep3DrawOrder`：梯-坡-梯 类型。
- `GetTriStep4Vertices` / `GetTriStep4UV` / `GetTriStep4DrawOrder`：两梯一平坡 类型。

### 河流与水体
- `GetRiverVertices` / `GetRiverUV` / `GetRiverWater2DrawOrder` / `GetRiverWater3DrawOrder`：生成河道内部水体网格。
- `GetOutgoingRiverVertices` / `GetOutgoingRiverSlopUV` / `GetOutgoingRiverSlopDrawOrder`：生成河流下游过渡区域水体。

### 湖海与海岸
- `GetlakeOrSeaVertices` / `GetlakeOrSeaUV` / `GetlakeOrSeaDrawOrder`：湖海实心区域。
- `GetlakeOrSeaRectVertices` / `GetlakeOrSeaRectUV` / `GetlakeOrSeaRectDrawOrder`：湖海矩形过渡。
- `GetlakeOrSeaTriVertices` / `GetlakeOrSeaTriUV` / `GetlakeOrSeaTriDrawOrder`：湖海三角过渡。
- `GetOneDirectionCoastRectVertices` / `GetCoastRectUV` / `GetCoastRectDrawOrder`：海岸矩形过渡。
- `GetOneDirectionCoastTriVertices` / `GetCoastTriUV` / `GetCoastTriDrawOrder`：海岸三角过渡。

### 网格线与辅助线
- `GetGridVertices` / `GetGridUV` / `GetGridDrawOrder`：生成地块网格线。
- `GetAdjacentHexLineVertices` / `GetAdjacentHexLineUV` / `GetAdjacentHexLineDrawOrder`：生成移动路径连线。

### 势力范围与迷雾
- `GetOneSphereOfInfluenceVertices`：获取势力范围边缘的顶点组（用于绘制彩色边界线）。
- `GetOneSphereOfInfluenceUV` / `GetOneSphereOfInfluenceDrawOrder`：边缘线的 UV 和绘制顺序。
- `GetFogVertices`：计算迷雾区域的外边界和孔洞（已探索区域）顶点。
- `GetFogCoverVertices`：生成迷雾封边（外扩）的顶点。

## 使用示例
`IMeshGenerator` 通常在 `MapRenderer` 中被调用，用于逐地块收集网格数据。以下是一个简化示例：

```csharp
public class MapRenderer : MonoBehaviour
{
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private IMapDataService _mapData;

    public void RenderMap()
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> triangles = new List<int>();

        foreach (var cell in _mapData.GetAllCells())
        {
            int offset = vertices.Count;

            // 添加实心区域顶点
            vertices.AddRange(_meshGenerator.GetSolidAreaVertices(ref cell));
            uv.AddRange(_meshGenerator.GetSolidAreaVerticesUV(ref cell));

            // 根据河流类型获取绘制顺序
            List<int> drawOrder;
            if (cell.HexType == Enums.HexType.NoRiver)
                drawOrder = _meshGenerator.GetSolidAreaVerticesDrawOrder1(ref cell);
            else if (cell.HexType == Enums.HexType.RiverSource)
                drawOrder = _meshGenerator.GetSolidAreaVerticesDrawOrder2(ref cell, cell.RiverOutgoingDirection);
            else
                drawOrder = _meshGenerator.GetSolidAreaVerticesDrawOrder3(ref cell, cell.RiverIncomingDirection, cell.RiverOutgoingDirection);

            // 将局部索引转为全局索引
            foreach (int index in drawOrder)
                triangles.Add(index + offset);
        }

        // 创建 Mesh（使用 MapController 工具类）
        Mesh mesh = MapController.CreatMesh(vertices.ToArray(), uv.ToArray(), triangles.ToArray(), ...);
    }
}
```

实际使用时，还需处理过渡区域、河流、湖海等多个子网格，可参考 `MapRenderer` 中的完整实现。

## 注意事项
- 大部分方法接受 `ref HexCellData` 参数，并会修改地块内的缓存数据（如顶点列表），因此调用顺序需保证地块数据已正确初始化。
- 绘制顺序方法返回的索引基于当前地块内的局部顶点，调用方需根据整体顶点列表偏移量进行转换。
- 过渡区域的生成依赖地形高度差判断（通过 `TerrainGenerator.IsType`），确保在生成网格前已正确设置地块高度。