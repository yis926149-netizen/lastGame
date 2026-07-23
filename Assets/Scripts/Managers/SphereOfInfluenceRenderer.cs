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

    // �洢���ɵ�������󣬷�������
    private GameObject _playerSphere;
    private List<GameObject> _enemySpheres = new List<GameObject>();

    private void Start()
    {
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
        // ���پɵ�
        DestroySphere(ref _playerSphere);
        foreach (var go in _enemySpheres)
            DestroySphere(go);
        _enemySpheres.Clear();

        // 玩家自己的势力始终完整可见：描边集合与归属集合相同
        if (_playerModelManager.SphereOfInfluence_HexC_HexCellData.Count > 0)
        {
            var cells = _playerModelManager.SphereOfInfluence_HexC_HexCellData.Values.ToList();
            _playerSphere = CreateSphereMesh(cells, cells, Color.blue, "PlayerSphereOfInfluence");
        }

        // 绘制每个敌方的势力范围
        foreach (var kv in _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData)
        {
            // 归属集合：该敌方的完整势力范围（用于判定"边是否为真实势力边界"）
            var fullSphere = kv.Value.Values.Where(c => c != null).ToList();
            // 三态记忆迷雾：敌方势力范围只在当前视野内可见（记忆区/未探索均不显示）——仅这部分参与描边
            var cells = fullSphere.Where(c => c.IsVisible).ToList();
            if (cells.Count == 0) continue;
            Color color = GetEnemyColor(kv.Key);
            // 描边集合=可见子集，归属集合=完整势力范围：
            // 被迷雾切断的一侧邻居仍属该势力→算内部边不描，形成开口图形而非"假边界"闭合圈
            GameObject go = CreateSphereMesh(cells, fullSphere, color, $"EnemySphereOfInfluence_{kv.Key}");
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
        // �ɸ�����Ҫ���Ʋ�ͬ���˵���ɫ������ͳһ��ɫ
        return Color.gray;
    }
}
