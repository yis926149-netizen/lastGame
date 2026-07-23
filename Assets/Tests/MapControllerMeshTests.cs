using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

public class MapControllerMeshTests
{
    private GameObject _gameObject;
    private Material _material;

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_gameObject);
        Object.DestroyImmediate(_material);
    }

    [Test]
    public void CreatMesh_ForVisualOnlyMesh_SkipsColliderAndCalculatesGeometryData()
    {
        _gameObject = new GameObject("VisualMesh");
        _material = new Material(Shader.Find("Standard"));

        Mesh mesh = MapController.CreatMesh(
            new[] { Vector3.zero, Vector3.forward, Vector3.right },
            new[] { Vector2.zero, Vector2.up, Vector2.right },
            new[] { 0, 1, 2 },
            _gameObject,
            _material,
            addCollider: false);

        Assert.IsNull(_gameObject.GetComponent<MeshCollider>());
        Assert.AreSame(_material, _gameObject.GetComponent<MeshRenderer>().sharedMaterial);
        Assert.Greater(mesh.bounds.size.sqrMagnitude, 0f);
        Assert.AreEqual(mesh.vertexCount, mesh.normals.Length);
    }

    [Test]
    public void CreatMesh_WithLargeVertexBuffer_UsesUInt32IndicesAutomatically()
    {
        _gameObject = new GameObject("LargeMesh");
        _material = new Material(Shader.Find("Standard"));
        var vertices = new Vector3[ushort.MaxValue + 1];
        vertices[1] = Vector3.forward;
        vertices[2] = Vector3.right;

        Mesh mesh = MapController.CreatMesh(
            vertices,
            new Vector2[vertices.Length],
            new[] { 0, 1, 2 },
            _gameObject,
            _material,
            addCollider: false);

        Assert.AreEqual(IndexFormat.UInt32, mesh.indexFormat);
    }
}
