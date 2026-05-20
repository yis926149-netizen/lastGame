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


    void Awake()
    {
        if (generator == null) generator = GetComponent<MeshGenerator>();
        _mapVisualEvent.OnMapVisualChanged.AddListener(OnMapVisualChanged);
        _mapVisualEvent.fogInit.AddListener(OnFogInit);

    }

    private void OnDestroy()
    {
        _mapVisualEvent.OnMapVisualChanged.RemoveListener(OnMapVisualChanged);
        _mapVisualEvent.fogInit.RemoveListener(OnFogInit);
    }

    private void OnMapVisualChanged()
    {
        // 重新生成迷雾
        if(gameObject.name == "Fog")
        {
            GenerateFog();
            return;
        }                      
    }

    private void OnFogInit()
    {
        if (gameObject.name == "FogCover")
        {
            GenerateFogCover(20);
            return;
        }

        if (gameObject.name == "FogCover_two")
        {
            GenerateFogCover_two(1);
            transform.position += new Vector3(0, 0.05f, 0);
            return;
        }

        if(gameObject.name == "Fog")
        {
            GenerateFog();
            return;
        }
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
        outerBoundary = _meshGenerator.GetFogCoverVertices(innerBoundary, increment);
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
        outerBoundary = _meshGenerator.GetFogCoverVertices(innerBoundary, increment);
        holes.Clear();
        holes.Add(innerBoundary);

        //执行生成
        generator.GenerateMesh(outerBoundary, holes, fogCoverMaterial_two);
    }
}