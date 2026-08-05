using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
// FogManager：迷雾盖面（FogCover）与地图边缘连接面片（FogConnector）管理。
//
// 【外围网格跟随约束-2026-08-05（波浪测试反哺，详见 动态地图/动态地图变化与分块重建方案.md 末章）】
// FogConnector 是贴地图边缘的静态网格，不参与 Chunk 重建。地图动画期间其内圈必须跟随
// 地形高度，否则与地图断开（WaveCaptures/170848 实机）。约束：
// ① 内圈顶点逐点映射最近边缘格，LateUpdate 读 MapVisualTransitionService.GetAnimatedWorldY
//    计算高度偏移；外圈保持 FogCover 原高度，连接坡面随波峰动态伸缩。
// ② 禁止直接读 cell.RealCenterWorldCoordinate 当视觉高度（动画中它是逻辑值，非显示值）。
// ③ 后续同类外围网格（势力范围城墙、覆盖层等）应复用同一模式：follower 或 GetAnimatedWorldY。
//****************************************
public class FogManager : MonoBehaviour
{
    //注入
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private MapVisualTransitionService _visualTransition;

    public MeshGenerator generator;
    public Material fogCoverMaterial;
    public Material fogCoverMaterial_two;
    public Material connectorMaterial;
    private bool _isSubscribed;
    private bool _secondaryCoverOffsetApplied;
    //迷雾连接面片：贴地斜坡 + MinY 平面填充，共用一个 Mesh 以保持 UV 连续
    private GameObject _connectorGO;
    private Material _connectorMaterial;
    private MeshGenerator _connectorGenerator;
    private readonly List<HexCellData> _connectorBoundaryCells = new List<HexCellData>();
    private readonly List<float> _connectorHeightOffsets = new List<float>();
    private bool _connectorWasAnimating;


    private void Awake()
    {
        if (generator == null) generator = GetComponent<MeshGenerator>();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    [Inject]
    private void Initialize(MapVisualEventSO mapVisualEvent)
    {
        _mapVisualEvent = mapVisualEvent;
        Subscribe();
    }

    private void Subscribe()
    {
        if (_isSubscribed || _mapVisualEvent == null) return;

        _mapVisualEvent.fogInit.AddListener(OnFogInit);
        _isSubscribed = true;
    }

    private void OnDisable()
    {
        if (!_isSubscribed || _mapVisualEvent == null) return;

        _mapVisualEvent.fogInit.RemoveListener(OnFogInit);
        _isSubscribed = false;
    }

    private void LateUpdate()
    {
        if (_connectorGenerator == null || _visualTransition == null) return;
        if (!_visualTransition.IsAnimating)
        {
            if (_connectorWasAnimating)
            {
                _connectorGenerator.ResetConnectorInnerHeightOffsets();
                _connectorWasAnimating = false;
            }
            return;
        }

        _connectorHeightOffsets.Clear();
        foreach (HexCellData cell in _connectorBoundaryCells)
        {
            float offset = cell != null
                ? _visualTransition.GetAnimatedWorldY(cell) - cell.RealCenterWorldCoordinate.y
                : 0f;
            _connectorHeightOffsets.Add(offset);
        }
        _connectorGenerator.SetConnectorInnerHeightOffsets(_connectorHeightOffsets);
        _connectorWasAnimating = true;
    }

    private void OnFogInit()
    {
        if (gameObject.name == "FogCover")
        {
            GenerateFogCover(_config.fogCoverWidth);
            //连接面片挂在 FogCover 的初始化下一并生成（一次性静态网格，探索变化无需重建）
            GenerateFogConnector();
            return;
        }

        if (gameObject.name == "FogCover_two")
        {
            GenerateFogCover_two(1);
            if (!_secondaryCoverOffsetApplied)
            {
                transform.position += new Vector3(0, 0.05f, 0);
                _secondaryCoverOffsetApplied = true;
            }
            return;
        }
    }

    /// <summary>
    /// 迷雾连接面片：闭合"不规则地图边缘 ↔ 矩形封皮内边"之间的缝隙。
    /// 材质复制主封皮的 Custom/FogCover 参数；斜坡和平面填充合并为一个 Mesh，
    /// 使用同一套 Planar UV，避免两部分各自归一化造成纹理接缝。
    /// </summary>
    public void GenerateFogConnector()
    {
        List<Vector3> rectBoundary, realOutline, slopeOuterBoundary;
        _meshGenerator.GetFogConnectorBoundaries(
            out rectBoundary, out realOutline, out slopeOuterBoundary, _mapDataService);
        if (realOutline == null || realOutline.Count < 3 ||
            slopeOuterBoundary == null || slopeOuterBoundary.Count != realOutline.Count)
        {
            Debug.LogWarning("FogManager: Connector 轮廓无效，跳过迷雾连接面片生成。");
            return;
        }

        if (_connectorGO == null)
        {
            _connectorGO = new GameObject("FogConnector");
            _connectorGO.transform.SetParent(transform.parent, false);
            _connectorGO.AddComponent<MeshGenerator>();
        }

        if (_connectorMaterial == null)
        {
            if (connectorMaterial != null)
            {
                _connectorMaterial = new Material(connectorMaterial);
            }
            else if (fogCoverMaterial == null)
            {
                Debug.LogError("FogManager: connectorMaterial 和 fogCoverMaterial 均为空。");
                return;
            }
            else
            {
                Shader connectorShader = Shader.Find("Custom/FogConnector");
                if (connectorShader == null)
                {
                    Debug.LogError("FogManager: Custom/FogConnector Shader 缺失。");
                    return;
                }

                _connectorMaterial = new Material(connectorShader);
                if (fogCoverMaterial.HasProperty("_MainTex"))
                    _connectorMaterial.SetTexture("_MainTex", fogCoverMaterial.GetTexture("_MainTex"));
                if (fogCoverMaterial.HasProperty("_Color"))
                    _connectorMaterial.SetColor("_Color", fogCoverMaterial.GetColor("_Color"));
            }

        }

        _connectorGenerator = _connectorGO.GetComponent<MeshGenerator>();
        _connectorGenerator.GenerateConnectorMesh(rectBoundary, realOutline, slopeOuterBoundary, _connectorMaterial);
        BindConnectorBoundaryCells(realOutline);
    }

    private void BindConnectorBoundaryCells(IReadOnlyList<Vector3> realOutline)
    {
        _connectorBoundaryCells.Clear();
        if (realOutline == null) return;
        IReadOnlyList<HexCellData> cells = _mapDataService.GetAllCells();
        foreach (Vector3 point in realOutline)
        {
            HexCellData nearest = null;
            float best = float.MaxValue;
            if (cells != null)
            {
                foreach (HexCellData cell in cells)
                {
                    if (cell == null) continue;
                    Vector3 center = cell.RealCenterWorldCoordinate;
                    float dx = point.x - center.x;
                    float dz = point.z - center.z;
                    float distance = dx * dx + dz * dz;
                    if (distance >= best) continue;
                    best = distance;
                    nearest = cell;
                }
            }
            _connectorBoundaryCells.Add(nearest);
        }
    }

    //迷雾封皮
    public void GenerateFogCover(float increment)
    {
        List<Vector3> outerBoundary = new List<Vector3>();
        List<Vector3> innerBoundary = new List<Vector3>();
        List<List<Vector3>> holes = new List<List<Vector3>>();
        _meshGenerator.GetFogVertices(out innerBoundary, out holes, _mapDataService);

        // innerBoundary 的 Y 已是边缘地块的 MinY（GetFogVertices 内计算）
        float coverHeight = innerBoundary.Count > 0 ? innerBoundary[0].y : 0f;

        outerBoundary = _meshGenerator.GetFogCoverVertices(innerBoundary, increment, increment * 0.5f, coverHeight);
        holes.Clear();
        holes.Add(innerBoundary);

        //执行生成
        generator.GenerateMesh(outerBoundary, holes, fogCoverMaterial);
    }

    public void GenerateFogCover_two(float increment)
    {
        List<Vector3> outerBoundary = new List<Vector3>();
        List<Vector3> innerBoundary = new List<Vector3>();
        List<List<Vector3>> holes = new List<List<Vector3>>();
        _meshGenerator.GetFogVertices(out innerBoundary, out holes, _mapDataService);

        // innerBoundary 的 Y 已是边缘地块的 MinY（GetFogVertices 内计算）
        float coverHeight = innerBoundary.Count > 0 ? innerBoundary[0].y : 0f;

        outerBoundary = _meshGenerator.GetFogCoverVertices(innerBoundary, increment, increment * 0.5f, coverHeight);
        holes.Clear();
        holes.Add(innerBoundary);

        //执行生成
        generator.GenerateMesh(outerBoundary, holes, fogCoverMaterial_two);
    }

}
