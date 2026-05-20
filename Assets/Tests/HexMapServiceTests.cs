using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class HexMapServiceTests
{
    private DiContainer _container;
    private HexMapService _service;
    private MapGenerationConfigSO _config;

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        // 创建一个临时配置SO
        _config = ScriptableObject.CreateInstance<MapGenerationConfigSO>();
        _config.xNumber = 5;
        _config.zNumber = 5;
        _config.OuterRadius = 3f;
        _container.BindInstance(_config);

        // 绑定服务自身
        _container.Bind<IMapDataService>().To<HexMapService>().AsSingle();

        // 初始化数据
        InitializeMapData();
    }

    private void InitializeMapData()
    {
        // 构造简单的 5x5 地图数据
        var hexToCell = new Dictionary<Vector3, HexCellData>();
        var orderToCell = new Dictionary<int, HexCellData>();
        var centerWorld = new List<Vector3>();
        var worldToHex = new Dictionary<Vector3, Vector3>();

        int order = 0;
        for (int z = 0; z < _config.zNumber; z++)
        {
            for (int x = 0; x < _config.xNumber; x++)
            {
                int offset = z / 2;
                Vector3 hexCoord = new Vector3(x - offset, -(x - offset) - z, z);
                Vector3 worldPos = new Vector3(x * 2, 0, z * 1.5f); // 简化计算
                var cell = new HexCellData(Enums.HexType.NoRiver, order, hexCoord, worldPos, 1f);
                hexToCell[hexCoord] = cell;
                orderToCell[order] = cell;
                centerWorld.Add(worldPos);
                worldToHex[worldPos] = hexCoord;
                order++;
            }
        }

        var hexVertices = new Vector3[0]; // 测试中不需要
        var verticesList = new List<Vector3>();
        var mesh = new Mesh();
        var gridGo = new GameObject();

        _service = _container.Resolve<IMapDataService>() as HexMapService;
        _service.Initialize(hexToCell, orderToCell, centerWorld, worldToHex, new GameObject(), hexVertices, verticesList, mesh, gridGo);
    }

    [Test]
    public void GetCell_ByHexCoordinate_ReturnsCorrectCell()
    {
        var coord = new Vector3(0, 0, 0);
        var cell = _service.GetCell(coord);
        Assert.IsNotNull(cell);
        Assert.AreEqual(coord, cell.HexCoordinate);
    }

    [Test]
    public void GetNeighbor_ReturnsCorrectNeighbor()
    {
        var center = _service.GetCell(new Vector3(1, -1, 0)); // 取一个中心点
        var neighbor = _service.GetNeighbor(center, Enums.HexDirection.NE);
        Assert.IsNotNull(neighbor);
        Assert.AreEqual(new Vector3(1, -2, 1), neighbor.HexCoordinate);
    }

    [Test]
    public void WorldToHexCoordinate_FindsClosestHex()
    {
        var worldPos = new Vector3(2.1f, 0, 1.4f);
        var hex = _service.WorldToHexCoordinate(worldPos);
        // 根据简化地图，预期应为 (1, -1, 0) 或附近
        Assert.IsTrue(_service.TryGetCell(hex, out _));
    }
}