using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

public class SphereOfInfluenceRenderer : MonoBehaviour
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private EnemyModelManager _enemyModelManager;

    // 存储生成的网格对象，方便销毁
    private GameObject _playerSphere;
    private List<GameObject> _enemySpheres = new List<GameObject>();

    private void Start()
    {
        // 初始生成
        RefreshAllSpheres();
    }

    private void Awake()
    {
        if (_mapVisualEvent != null)
            _mapVisualEvent.OnMapVisualChanged.AddListener(RefreshAllSpheres);
    }

    private void OnDisable()
    {
        if (_mapVisualEvent != null)
            _mapVisualEvent.OnMapVisualChanged.RemoveListener(RefreshAllSpheres);
    }

    private void RefreshAllSpheres()
    {
        // 销毁旧的
        if (_playerSphere != null) Destroy(_playerSphere);
        foreach (var go in _enemySpheres)
            if (go != null) Destroy(go);
        _enemySpheres.Clear();

        // 生成玩家势力范围
        if (_playerModelManager.SphereOfInfluence_HexC_HexCellData.Count > 0)
        {
            var cells = _playerModelManager.SphereOfInfluence_HexC_HexCellData.Values.ToList();
            _playerSphere = CreateSphereMesh(cells, Color.blue, "PlayerSphereOfInfluence");
        }

        // 生成每个敌方的势力范围
        foreach (var kv in _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData)
        {
            var cells = kv.Value.Values.ToList();
            if (cells.Count == 0) continue;
            Color color = GetEnemyColor(kv.Key);
            GameObject go = CreateSphereMesh(cells, color, $"EnemySphereOfInfluence_{kv.Key}");
            _enemySpheres.Add(go);
        }
    }

    private GameObject CreateSphereMesh(List<HexCellData> hexCells, Color color, string objectName)
    {
        int edgeCount;
        var verticeList = _meshGenerator.GetOneSphereOfInfluenceVertices(hexCells, out edgeCount, _mapDataService);
        List<Vector3> vertices = new List<Vector3>();
        foreach (var list in verticeList) vertices.AddRange(list);

        List<Vector2> uv = new List<Vector2>();
        List<int> drawOrder = new List<int>();

        for (int i = 0; i < vertices.Count / 4; i++)
        {
            var order = _meshGenerator.GetOneSphereOfInfluenceDrawOrder();
            for (int j = 0; j < order.Count; j++) order[j] += i * 4;
            drawOrder.AddRange(order);
            uv.AddRange(_meshGenerator.GetOneSphereOfInfluenceUV());
        }

        Shader shader = Shader.Find("Custom/SphereOfInfluence") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Hidden/InternalErrorShader");
        Material mat = new Material(shader);
        mat.SetColor("_Color", color);

        GameObject obj = new GameObject(objectName);
        MapController.CreatMesh(vertices.ToArray(), uv.ToArray(), drawOrder.ToArray(), obj, mat);
        return obj;
    }

    private Color GetEnemyColor(int enemyIndex)
    {
        // 可根据需要定制不同敌人的颜色，现在统一灰色
        return Color.gray;
    }
}