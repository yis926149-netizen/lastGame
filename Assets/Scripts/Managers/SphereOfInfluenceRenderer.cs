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
// 城墙/城墩各有两套预制体：玩家一套、AI 一套。
// 城墙、城墩各自独立判定：某部件的预制体未指定时，只跳过该部件（玩家与 AI 互不影响）。
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

    [Header("玩家势力实体模型预制体（不指定则不生成）")]
    [Tooltip("玩家城墙预制体：Pivot 底部中心，Z 轴为长度方向，原始长度=六边形边长")]
    [SerializeField] private GameObject _wallPrefab;
    [Tooltip("玩家城墩预制体：Pivot 底部中心，放在边界折线节点上")]
    [SerializeField] private GameObject _towerPrefab;

    [Header("AI 势力实体模型预制体（不指定则不生成）")]
    [Tooltip("AI 城墙预制体，未指定时 AI 势力不生成模型")]
    [SerializeField] private GameObject _enemyWallPrefab;
    [Tooltip("AI 城墩预制体，未指定时 AI 势力不生成模型")]
    [SerializeField] private GameObject _enemyTowerPrefab;

    [Header("过渡墙坡度处理")]
    [Tooltip("高度差小于此值视为水平墙（忽略随机扰动）")]
    [SerializeField] private float _heightTolerance = 0.15f;

    // ── 对象池 ────────────────────────────────────────────
    private readonly List<GameObject> _playerWallPool = new List<GameObject>();
    private readonly List<GameObject> _playerTowerPool = new List<GameObject>();
    private readonly List<GameObject> _enemyWallPool = new List<GameObject>();
    private readonly List<GameObject> _enemyTowerPool = new List<GameObject>();
    private int _activePlayerWallCount;
    private int _activePlayerTowerCount;
    private int _activeEnemyWallCount;
    private int _activeEnemyTowerCount;
    private Transform _poolRoot;

    // ── 势力颜色 ──────────────────────────────────────────
    private static readonly Color PlayerColor = new Color(1f, 0.84f, 0f);     // 金黄色
    private static readonly Color EnemyColor   = new Color(0.5f, 0f, 0.5f);    // 紫色
    private readonly List<BoundarySegment> _segBuffer = new List<BoundarySegment>();
    private readonly List<Vector3> _cornerBuffer = new List<Vector3>();

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
    }

    private void RefreshAllSpheres()
    {
        RefreshWithModels();
    }

    // ══════════════════════════════════════════════════════
    //  实体模型渲染
    // ══════════════════════════════════════════════════════

    private void RefreshWithModels()
    {
        // 回收全部激活实例
        _activePlayerWallCount = 0;
        _activePlayerTowerCount = 0;
        _activeEnemyWallCount = 0;
        _activeEnemyTowerCount = 0;

        // 玩家势力：完整显示（城墙/城墩按各自预制体是否指定独立生成）
        if (_playerModelManager.SphereOfInfluence_HexC_HexCellData.Count > 0)
        {
            var cells = _playerModelManager.SphereOfInfluence_HexC_HexCellData.Values.ToList();
            BuildModelsForSphere(cells, cells, PlayerColor, _wallPrefab, _towerPrefab,
                _playerWallPool, _playerTowerPool, ref _activePlayerWallCount, ref _activePlayerTowerCount);
        }

        // 敌方势力：【探索重构-阶段1】始终显示完整势力范围，不再按 IsVisible 裁切（城墙/城墩按各自预制体是否指定独立生成）
        foreach (var kv in _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData)
        {
            var fullSphere = kv.Value.Values.Where(c => c != null).ToList();
            if (fullSphere.Count == 0) continue;
            BuildModelsForSphere(fullSphere, fullSphere, EnemyColor, _enemyWallPrefab, _enemyTowerPrefab,
                _enemyWallPool, _enemyTowerPool, ref _activeEnemyWallCount, ref _activeEnemyTowerCount);
        }

        // 停用多余实例
        DeactivateExtra(_playerWallPool, _activePlayerWallCount);
        DeactivateExtra(_playerTowerPool, _activePlayerTowerCount);
        DeactivateExtra(_enemyWallPool, _activeEnemyWallCount);
        DeactivateExtra(_enemyTowerPool, _activeEnemyTowerCount);
    }

    private void BuildModelsForSphere(
        List<HexCellData> hexCells, ICollection<HexCellData> membershipCells, Color factionColor,
        GameObject wallPrefab, GameObject towerPrefab,
        List<GameObject> wallPool, List<GameObject> towerPool,
        ref int activeWallCount, ref int activeTowerCount)
    {
        _meshGenerator.ExtractSphereOfInfluenceBoundary(
            hexCells, membershipCells, _mapDataService, _segBuffer, _cornerBuffer);

        // 城墙（城墙预制体未指定则跳过）
        if (wallPrefab != null)
        {
            foreach (var seg in _segBuffer)
                PlaceWall(seg, factionColor, wallPrefab, wallPool, ref activeWallCount);
        }

        // 城墩（城墩预制体未指定则跳过）
        if (towerPrefab != null)
        {
            foreach (var corner in _cornerBuffer)
                PlaceTower(corner, factionColor, towerPrefab, towerPool, ref activeTowerCount);
        }
    }

    private void PlaceWall(BoundarySegment seg, Color factionColor, GameObject prefab, List<GameObject> pool, ref int activeCount)
    {
        GameObject wall = GetPooled(pool, prefab, ref activeCount);
        ApplyModelColor(wall, factionColor);

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
            wall.transform.localScale = prefab.transform.localScale;

            // 用原始预制体 Mesh 作为源
            Mesh sourceMesh = prefab.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (sourceMesh != null)
                WallMeshDeformer.ApplyDeformedMesh(filter, sourceMesh, heightDelta);
        }
        else
        {
            // 标准水平墙：还原可能残留的变形 Mesh，放中点、水平朝向、平均高度
            if (filter != null) RestoreOriginalMesh(wall, filter, prefab);

            Vector3 mid = seg.Midpoint;
            // 水平墙：Y 取两端平均，忽略微小扰动
            mid.y = (start.y + end.y) * 0.5f;
            wall.transform.position = mid;

            Vector3 horizontalDir = new Vector3(end.x - start.x, 0f, end.z - start.z);
            if (horizontalDir.sqrMagnitude > 0.0001f)
                wall.transform.rotation = Quaternion.LookRotation(horizontalDir.normalized);
            wall.transform.localScale = prefab.transform.localScale;
        }
    }

    private void PlaceTower(Vector3 corner, Color factionColor, GameObject prefab, List<GameObject> pool, ref int activeCount)
    {
        GameObject tower = GetPooled(pool, prefab, ref activeCount);
        ApplyModelColor(tower, factionColor);
        tower.transform.position = corner;
        tower.transform.rotation = Quaternion.identity;
        tower.transform.localScale = prefab.transform.localScale;
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
    private void RestoreOriginalMesh(GameObject wall, MeshFilter filter, GameObject prefab)
    {
        if (filter.sharedMesh != null && filter.sharedMesh.name == "WallDeformed")
        {
            WallMeshDeformer.ReleaseDeformedMesh(filter);
            Mesh sourceMesh = prefab.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (sourceMesh != null) filter.sharedMesh = sourceMesh;
        }
    }

    private static void ApplyModelColor(GameObject model, Color color)
    {
        if (model == null) return;
        var renderer = model.GetComponentInChildren<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            renderer.material.SetColor("_Color", color);
        }
    }
}
