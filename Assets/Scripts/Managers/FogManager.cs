using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class FogManager : MonoBehaviour
{
    //注入
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private MapGenerationConfigSO _config;

    public MeshGenerator generator;
    public Material myMaterial;
    public Material fogCoverMaterial;
    public Material fogCoverMaterial_two;
    public Material connectorMaterial;
    private bool _isSubscribed;
    private bool _secondaryCoverOffsetApplied;
    //迷雾连接面片：贴地斜坡 + MinY 平面填充，共用一个 Mesh 以保持 UV 连续
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

        _connectorGO.GetComponent<MeshGenerator>()
            .GenerateConnectorMesh(rectBoundary, realOutline, slopeOuterBoundary, _connectorMaterial);
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
