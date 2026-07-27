using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

//****************************************
// 功能说明：势力范围渲染器。
//   将边界渲染为实体城墙 + 城墩模型（替代旧的描边面片）。
//   - 边界线段（BoundarySegment）用"城墙"预制体；
//   - 边界折线节点（角点）用"城墩"预制体；
//   - HexEdge 段：标准水平城墙；
//   - Transition 段：高度差小→水平墙；高度差大→运行时 Mesh 变形墙。
//
// 预制体未指定时，回退到旧的描边面片渲染（保证不配置也能跑）。
//
// 使用方式：在场景中建空物体，挂本组件，Inspector 指定城墙/城墩预制体。
//   Zenject 绑定：FromComponentInHierarchy。
//
// 详见《势力范围实体城墙改造方案.md》。
//****************************************

public class SphereOfInfluenceRenderer : MonoBehaviour
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private EnemyModelManager _enemyModelManager;

    [Header("实体模型预制体（不指定则回退面片渲染）")]
    [Tooltip("城墙预制体：Pivot 底部中心，Z 轴为长度方向，原始长度=六边形边长")]
    [SerializeField] private GameObject _wallPrefab;
    [Tooltip("城墩预制体：Pivot 底部中心，放在边界折线节点上")]
    [SerializeField] private GameObject _towerPrefab;

    [Header("过渡墙坡度处理")]
    [Tooltip("高度差小于此值视为水平墙（忽略随机扰动）")]
    [SerializeField] private float _heightTolerance = 0.15f;

    // ── 对象池 ────────────────────────────────────────────
    private readonly List<GameObject> _wallPool = new List<GameObject>();
    private readonly List<GameObject> _towerPool = new List<GameObject>();
    private int _activeWallCount;
    private int _activeTowerCount;
    private Transform _poolRoot;

    // ── 旧面片渲染回退用 ──────────────────────────────────
    private GameObject _playerSphere;
    private List<GameObject> _enemySpheres = new List<GameObject>();

    // ── 复用缓冲，避免每次刷新分配 ────────────────────────
    private readonly List<BoundarySegment> _segBuffer = new List<BoundarySegment>();
    private readonly List<Vector3> _cornerBuffer = new List<Vector3>();

    private bool UseModels => _wallPrefab != null && _towerPrefab != null;

    // 预制体基准缩放（保留美术在预制体上设定的 scale，不强制归一）
    private Vector3 WallBaseScale => _wallPrefab != null ? _wallPrefab.transform.localScale : Vector3.one;
    private Vector3 TowerBaseScale => _towerPrefab != null ? _towerPrefab.transform.localScale : Vector3.one;

    private void Start()
    {
        _poolRoot = new GameObject("SphereOfInfluence_Models").transform;
        _poolRoot.SetParent(transform, false);

        _mapVisualEvent.OnMapVisualChanged.AddListener(RefreshAllSpheres);
        RefreshAllSpheres();
    }

    private void OnDestroy()
    {
        if (_mapVisualEvent != null)
            _mapVisualEvent.OnMapVisualChanged.RemoveListener(RefreshAllSpheres);

        DestroySphere(ref _playerSphere);
        foreach (var sphere in _enemySpheres)
            DestroySphere(sphere);
        _enemySpheres.Clear();
    }

    private void RefreshAllSpheres()
    {
        if (UseModels)
            RefreshWithModels();
        else
            RefreshWithMesh();
    }

    // ══════════════════════════════════════════════════════
    //  实体模型渲染
    // ══════════════════════════════════════════════════════

    private void RefreshWithModels()
    {
        // 回收全部激活实例
        _activeWallCount = 0;
        _activeTowerCount = 0;

        // 玩家势力：完整显示
        if (_playerModelManager.SphereOfInfluence_HexC_HexCellData.Count > 0)
        {
            var cells = _playerModelManager.SphereOfInfluence_HexC_HexCellData.Values.ToList();
            BuildModelsForSphere(cells, cells);
        }

        // 敌方势力：【探索重构-阶段1】始终显示完整势力范围，不再按 IsVisible 裁切
        foreach (var kv in _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData)
        {
            var fullSphere = kv.Value.Values.Where(c => c != null).ToList();
            if (fullSphere.Count == 0) continue;
            BuildModelsForSphere(fullSphere, fullSphere);
        }

        // 停用多余实例
        DeactivateExtra(_wallPool, _activeWallCount);
        DeactivateExtra(_towerPool, _activeTowerCount);
    }

    private void BuildModelsForSphere(List<HexCellData> hexCells, ICollection<HexCellData> membershipCells)
    {
        _meshGenerator.ExtractSphereOfInfluenceBoundary(
            hexCells, membershipCells, _mapDataService, _segBuffer, _cornerBuffer);

        // 城墙
        foreach (var seg in _segBuffer)
            PlaceWall(seg);

        // 城墩
        foreach (var corner in _cornerBuffer)
            PlaceTower(corner);
    }

    private void PlaceWall(BoundarySegment seg)
    {
        GameObject wall = GetPooled(_wallPool, _wallPrefab, ref _activeWallCount);

        Vector3 start = seg.Start;
        Vector3 end = seg.End;
        float heightDelta = seg.HeightDelta;

        var filter = wall.GetComponent<MeshFilter>();

        bool needDeform = seg.Type == BoundarySegmentType.Transition
                          && Mathf.Abs(heightDelta) > _heightTolerance
                          && filter != null;

        if (needDeform)
        {
            // 变形墙：pivot 在底部中心，放线段中点；Mesh 内部从 -heightDelta/2 渐变到 +heightDelta/2，
            // 使两端底部分别对齐 start.y 和 end.y，墙体竖直方向保持不倾斜。
            Vector3 deformMid = seg.Midpoint;
            deformMid.y = (start.y + end.y) * 0.5f;
            wall.transform.position = deformMid;
            Vector3 horizontalDir = new Vector3(end.x - start.x, 0f, end.z - start.z);
            if (horizontalDir.sqrMagnitude > 0.0001f)
                wall.transform.rotation = Quaternion.LookRotation(horizontalDir.normalized);
            wall.transform.localScale = WallBaseScale;

            // 用原始预制体 Mesh 作为源
            Mesh sourceMesh = _wallPrefab.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (sourceMesh != null)
                WallMeshDeformer.ApplyDeformedMesh(filter, sourceMesh, heightDelta);
        }
        else
        {
            // 标准水平墙：还原可能残留的变形 Mesh，放中点、水平朝向、平均高度
            if (filter != null) RestoreOriginalMesh(wall, filter);

            Vector3 mid = seg.Midpoint;
            // 水平墙：Y 取两端平均，忽略微小扰动
            mid.y = (start.y + end.y) * 0.5f;
            wall.transform.position = mid;

            Vector3 horizontalDir = new Vector3(end.x - start.x, 0f, end.z - start.z);
            if (horizontalDir.sqrMagnitude > 0.0001f)
                wall.transform.rotation = Quaternion.LookRotation(horizontalDir.normalized);
            wall.transform.localScale = WallBaseScale;
        }
    }

    private void PlaceTower(Vector3 corner)
    {
        GameObject tower = GetPooled(_towerPool, _towerPrefab, ref _activeTowerCount);
        tower.transform.position = corner;
        tower.transform.rotation = Quaternion.identity;
        tower.transform.localScale = TowerBaseScale;
    }

    // ── 对象池辅助 ────────────────────────────────────────

    private GameObject GetPooled(List<GameObject> pool, GameObject prefab, ref int activeCount)
    {
        GameObject obj;
        if (activeCount < pool.Count)
        {
            obj = pool[activeCount];
        }
        else
        {
            obj = Instantiate(prefab, _poolRoot);
            pool.Add(obj);
        }
        obj.SetActive(true);
        activeCount++;
        return obj;
    }

    private void DeactivateExtra(List<GameObject> pool, int activeCount)
    {
        for (int i = activeCount; i < pool.Count; i++)
        {
            if (pool[i] == null) continue;
            // 释放墙的动态变形 Mesh
            var filter = pool[i].GetComponent<MeshFilter>();
            if (filter != null) WallMeshDeformer.ReleaseDeformedMesh(filter);
            pool[i].SetActive(false);
        }
    }

    // 还原预制体原始 Mesh（清除上次的变形 Mesh）
    private void RestoreOriginalMesh(GameObject wall, MeshFilter filter)
    {
        if (filter.sharedMesh != null && filter.sharedMesh.name == "WallDeformed")
        {
            WallMeshDeformer.ReleaseDeformedMesh(filter);
            Mesh sourceMesh = _wallPrefab.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (sourceMesh != null) filter.sharedMesh = sourceMesh;
        }
    }

    // ══════════════════════════════════════════════════════
    //  旧面片渲染（预制体未指定时回退）
    // ══════════════════════════════════════════════════════

    private void RefreshWithMesh()
    {
        DestroySphere(ref _playerSphere);
        foreach (var go in _enemySpheres)
            DestroySphere(go);
        _enemySpheres.Clear();

        if (_playerModelManager.SphereOfInfluence_HexC_HexCellData.Count > 0)
        {
            var cells = _playerModelManager.SphereOfInfluence_HexC_HexCellData.Values.ToList();
            _playerSphere = CreateSphereMesh(cells, cells, Color.blue, "PlayerSphereOfInfluence");
        }

        foreach (var kv in _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData)
        {
            var fullSphere = kv.Value.Values.Where(c => c != null).ToList();
            // 【探索重构-阶段1】始终显示完整势力范围
            if (fullSphere.Count == 0) continue;
            Color color = GetEnemyColor(kv.Key);
            GameObject go = CreateSphereMesh(fullSphere, fullSphere, color, $"EnemySphereOfInfluence_{kv.Key}");
            _enemySpheres.Add(go);
        }
    }

    private GameObject CreateSphereMesh(List<HexCellData> hexCells, ICollection<HexCellData> membershipCells, Color color, string objectName)
    {
        int edgeCount;
        var verticeList = _meshGenerator.GetOneSphereOfInfluenceVertices(hexCells, membershipCells, out edgeCount, _mapDataService);
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
        MapController.CreatMesh(vertices.ToArray(), uv.ToArray(), drawOrder.ToArray(), obj, mat, addCollider: false);
        return obj;
    }

    private static void DestroySphere(ref GameObject sphere)
    {
        DestroySphere(sphere);
        sphere = null;
    }

    private static void DestroySphere(GameObject sphere)
    {
        if (sphere == null) return;

        var renderer = sphere.GetComponent<MeshRenderer>();
        var filter = sphere.GetComponent<MeshFilter>();
        if (renderer != null && renderer.sharedMaterial != null)
            Destroy(renderer.sharedMaterial);
        if (filter != null && filter.sharedMesh != null)
            Destroy(filter.sharedMesh);
        Destroy(sphere);
    }

    private Color GetEnemyColor(int enemyIndex)
    {
        return Color.gray;
    }
}
