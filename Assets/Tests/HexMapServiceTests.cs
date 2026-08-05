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

        // ����һ����ʱ����SO
        _config = ScriptableObject.CreateInstance<MapGenerationConfigSO>();
        _config.xNumber = 5;
        _config.zNumber = 5;
        _config.OuterRadius = 3f;
        _container.BindInstance(_config);

        // �󶨷�������
        _container.Bind<IMapDataService>().To<HexMapService>().AsSingle();

        // ��ʼ������
        InitializeMapData();
    }

    private void InitializeMapData()
    {
        // ����򵥵� 5x5 ��ͼ����
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
                float innerRadius = _config.OuterRadius * 0.866025404f;
                Vector3 worldPos = new Vector3(hexCoord.x * 2f * innerRadius + hexCoord.z * innerRadius, 0, hexCoord.z * 1.5f * _config.OuterRadius);
                var cell = new HexCellData(Enums.HexType.NoRiver, order, hexCoord, worldPos, 1f);
                hexToCell[hexCoord] = cell;
                orderToCell[order] = cell;
                centerWorld.Add(worldPos);
                worldToHex[worldPos] = hexCoord;
                order++;
            }
        }

        var hexVertices = new Vector3[0]; // �����в���Ҫ

        _service = _container.Resolve<IMapDataService>() as HexMapService;
        _service.Initialize(hexToCell, orderToCell, centerWorld, worldToHex, new GameObject(), hexVertices);
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
        var center = _service.GetCell(new Vector3(1, -1, 0)); // ȡһ�����ĵ�
        var neighbor = _service.GetNeighbor(center, Enums.HexDirection.NE);
        Assert.IsNotNull(neighbor);
        Assert.AreEqual(new Vector3(1, -2, 1), neighbor.HexCoordinate);
    }

    [Test]
    public void WorldToHexCoordinate_FindsClosestHex()
    {
        HexCellData expected = _service.GetCell(new Vector3(1, -2, 1));

        Assert.IsTrue(_service.TryWorldToHexCoordinate(expected.CenterWorldCoordinate + new Vector3(0.1f, 5f, -0.1f), out Vector3 hex));
        Assert.AreEqual(expected.HexCoordinate, hex);
    }

    [Test]
    public void TryWorldToHexCoordinate_OutsideMap_ReturnsFalse()
    {
        Assert.IsFalse(_service.TryWorldToHexCoordinate(new Vector3(1000f, 0f, 1000f), out _));
        Assert.IsNull(_service.GetCellByWorldPosition(new Vector3(1000f, 0f, 1000f)));
    }

    [Test]
    public void GetNeighbors_CenterAndCorner_ReturnSixAndTwoUniqueCells()
    {
        HexCellData center = _service.GetCell(new Vector3(1, -3, 2));
        HexCellData corner = _service.GetCell(Vector3.zero);

        CollectionAssert.AllItemsAreUnique(_service.GetNeighbors(center));
        Assert.AreEqual(6, _service.GetNeighbors(center).Count);
        CollectionAssert.AllItemsAreUnique(_service.GetNeighbors(corner));
        Assert.AreEqual(2, _service.GetNeighbors(corner).Count);
    }
}
