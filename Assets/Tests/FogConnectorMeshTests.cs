using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class FogConnectorMeshTests
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
    public void GenerateSlopeMesh_CreatesTwoTrianglesPerOutlineSegment()
    {
        _gameObject = new GameObject("FogConnectorSlope");
        MeshGenerator generator = _gameObject.AddComponent<MeshGenerator>();
        _material = new Material(Shader.Find("Standard"));
        var inner = new List<Vector3>
        {
            new Vector3(-1f, 2f, 1f),
            new Vector3(1f, 3f, 1f),
            new Vector3(1f, 4f, -1f),
            new Vector3(-1f, 2.5f, -1f),
        };
        var outer = new List<Vector3>
        {
            new Vector3(-2f, 0f, 2f),
            new Vector3(2f, 0f, 2f),
            new Vector3(2f, 0f, -2f),
            new Vector3(-2f, 0f, -2f),
        };

        generator.GenerateSlopeMesh(inner, outer, _material);

        Mesh mesh = _gameObject.GetComponent<MeshFilter>().sharedMesh;
        Assert.AreEqual(8, mesh.vertexCount);
        Assert.AreEqual(4 * 2 * 3, mesh.triangles.Length);
        CollectionAssert.AreEqual(inner, new List<Vector3>(mesh.vertices).GetRange(0, inner.Count));
        CollectionAssert.AreEqual(outer, new List<Vector3>(mesh.vertices).GetRange(inner.Count, outer.Count));

        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 normal = Vector3.Cross(
                vertices[triangles[i + 1]] - vertices[triangles[i]],
                vertices[triangles[i + 2]] - vertices[triangles[i]]);
            Assert.GreaterOrEqual(normal.y, 0f);
            Assert.Greater(normal.sqrMagnitude, 1e-8f, $"Triangle {i / 3} is degenerate.");
        }
    }
}
