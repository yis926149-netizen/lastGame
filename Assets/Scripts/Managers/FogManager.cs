using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class FogManager : MonoBehaviour
{
    //注入
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;

    public MeshGenerator generator;
    public Material myMaterial;
    public Material fogCoverMaterial;
    public Material fogCoverMaterial_two;
    private bool _isSubscribed;
    private bool _secondaryCoverOffsetApplied;
    //迷雾连接面片（矩形封皮内边 ↔ 地图真实轮廓 之间的锯齿环带）
    private GameObject _connectorGO;
    private Material _connectorMaterial;


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

        _mapVisualEvent.OnMapVisualChanged.AddListener(OnMapVisualChanged);
        _mapVisualEvent.fogInit.AddListener(OnFogInit);
        _isSubscribed = true;
    }

    private void OnDisable()
    {
        if (!_isSubscribed || _mapVisualEvent == null) return;

        _mapVisualEvent.OnMapVisualChanged.RemoveListener(OnMapVisualChanged);
        _mapVisualEvent.fogInit.RemoveListener(OnFogInit);
        _isSubscribed = false;
    }

    private void OnMapVisualChanged()
    {
    }

    private void OnFogInit()
    {
        if (gameObject.name == "FogCover")
        {
            GenerateFogCover(20);
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
    /// 迷雾连接面片：闭合“不规则地图边缘 ↔ 矩形封皮内边”之间的缝隙。
    /// 外边界 = 封皮内边矩形；洞 = 地图真实不规则轮廓（边缘地块真实顶点，逐点贴合地形网格）。
    /// 材质用 Custom/FogCover 恒迷雾 Shader（与地形迷雾同一套全局参数与整图 UV），
    /// 图案从未探索地形 → 连接面片 → 封皮无缝延续。
    /// </summary>
    public void GenerateFogConnector()
    {
        List<Vector3> rectBoundary, realOutline;
        _meshGenerator.GetFogConnectorBoundaries(out rectBoundary, out realOutline, _mapDataService);
        if (realOutline == null || realOutline.Count < 3)
        {
            Debug.LogWarning("FogManager: 地图真实轮廓顶点不足，跳过迷雾连接面片生成。");
            return;
        }

        if (_connectorGO == null)
        {
            _connectorGO = new GameObject("FogConnector");
            _connectorGO.transform.SetParent(transform.parent, false);
            //MeshGenerator 带 [RequireComponent(MeshFilter, MeshRenderer)]，AddComponent 时自动补齐；
            //且 GameObject 处于激活态，Awake 会在 AddComponent 时同步执行，m_mesh 随即就绪。
            _connectorGO.AddComponent<MeshGenerator>();
        }

        if (_connectorMaterial == null)
        {
            Shader s = Shader.Find("Custom/FogCover");
            if (s == null)
            {
                Debug.LogError("FogManager: 找不到 Custom/FogCover Shader，迷雾连接面片使用封皮材质回退。");
                _connectorMaterial = fogCoverMaterial;
            }
            else
            {
                _connectorMaterial = new Material(s);
            }
        }

        var connectorGen = _connectorGO.GetComponent<MeshGenerator>();
        connectorGen.GenerateMesh(rectBoundary, new List<List<Vector3>> { realOutline }, _connectorMaterial);

        //连接面片内缘已内缩到地形边缘之下（重叠），再整体下压一小段，
        //保证重叠区地形始终赢得深度测试，彻底消除交界处 Z-fighting 细缝/闪烁。
        _connectorGO.transform.localPosition = new Vector3(0f, -0.05f, 0f);
    }

    //迷雾
    public void GenerateFog()
    {
        List<Vector3> outerBoundary = new List<Vector3>();
        List<List<Vector3>> holes = new List<List<Vector3>>();
        _meshGenerator.GetFogVertices(out outerBoundary, out holes, _mapDataService);

        //执行生成
        generator.GenerateMesh(outerBoundary, holes, myMaterial);
    }

    //迷雾封皮
    public void GenerateFogCover(float increment)
    {
        List<Vector3> outerBoundary = new List<Vector3>();
        List<Vector3> innerBoundary = new List<Vector3>();
        List<List<Vector3>> holes = new List<List<Vector3>>();
        _meshGenerator.GetFogVertices(out innerBoundary, out holes, _mapDataService);

        // 计算所有边缘地块的平均高度
        float avgHeight = 0f;
        if (innerBoundary.Count > 0)
        {
            foreach (var v in innerBoundary) avgHeight += v.y;
            avgHeight /= innerBoundary.Count;
        }

        outerBoundary = _meshGenerator.GetFogCoverVertices(innerBoundary, increment, avgHeight);
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

        // 计算所有边缘地块的平均高度
        float avgHeight = 0f;
        if (innerBoundary.Count > 0)
        {
            foreach (var v in innerBoundary) avgHeight += v.y;
            avgHeight /= innerBoundary.Count;
        }

        outerBoundary = _meshGenerator.GetFogCoverVertices(innerBoundary, increment, avgHeight);
        holes.Clear();
        holes.Add(innerBoundary);

        //执行生成
        generator.GenerateMesh(outerBoundary, holes, fogCoverMaterial_two);
    }
}
