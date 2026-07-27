using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ExplorationPillarMesh : MonoBehaviour
{
	[SerializeField] private float _outerRadius = 2.5f;
	[SerializeField] private float _height = 3.5f;

	public float OuterRadius => _outerRadius;
	public float Height => _height;

	private void Awake()
	{
		GenerateMesh();
	}

	private void GenerateMesh()
	{
		float r = _outerRadius;
		float innerR = r * 0.866025404f;
		float h = _height;

		Mesh mesh = new Mesh();
		mesh.name = "ExplorationPillar";

		Vector3[] vertices = new Vector3[14];
		int[] triangles = new int[54];
		Vector2[] uv = new Vector2[14];

		Vector3[] topVerts = new Vector3[7];
		topVerts[0] = new Vector3(0, h, 0);
		topVerts[1] = new Vector3(0, h, r);
		topVerts[2] = new Vector3(innerR, h, 0.5f * r);
		topVerts[3] = new Vector3(innerR, h, -0.5f * r);
		topVerts[4] = new Vector3(0, h, -r);
		topVerts[5] = new Vector3(-innerR, h, -0.5f * r);
		topVerts[6] = new Vector3(-innerR, h, 0.5f * r);

		Vector3[] botVerts = new Vector3[7];
		botVerts[0] = new Vector3(0, 0, 0);
		botVerts[1] = new Vector3(0, 0, r);
		botVerts[2] = new Vector3(innerR, 0, 0.5f * r);
		botVerts[3] = new Vector3(innerR, 0, -0.5f * r);
		botVerts[4] = new Vector3(0, 0, -r);
		botVerts[5] = new Vector3(-innerR, 0, -0.5f * r);
		botVerts[6] = new Vector3(-innerR, 0, 0.5f * r);

		for (int i = 0; i < 7; i++)
		{
			vertices[i] = topVerts[i];
			vertices[i + 7] = botVerts[i];
		}

		int triIndex = 0;

		// 顶面扇形：6 个三角形（从上往下看逆时针）
		for (int i = 1; i <= 6; i++)
		{
			int next = (i % 6) + 1;
			triangles[triIndex++] = 0;
			triangles[triIndex++] = i;
			triangles[triIndex++] = next;
		}

		// 侧面：6 个四边形
		for (int i = 1; i <= 6; i++)
		{
			int next = (i % 6) + 1;
			int t1 = i, t2 = next;
			int b1 = i + 7, b2 = next + 7;

			triangles[triIndex++] = t1;
			triangles[triIndex++] = b1;
			triangles[triIndex++] = t2;

			triangles[triIndex++] = t2;
			triangles[triIndex++] = b1;
			triangles[triIndex++] = b2;
		}

		// UV：侧面按周长均匀展开，顶面/底面中心取中点
		float dx = innerR;
		float dz = 0.5f * r;
		float sideLength = Mathf.Sqrt(dx * dx + dz * dz);
		float totalPerimeter = sideLength * 6f;

		float[] perimU = new float[7];
		perimU[1] = 0f;
		for (int i = 2; i <= 6; i++)
		{
			perimU[i] = perimU[i - 1] + sideLength / totalPerimeter;
		}

		// 侧面：U=周长位置, V=高度比
		for (int i = 1; i <= 6; i++)
		{
			float u = perimU[i];
			uv[i] = new Vector2(u, 1f);
			uv[i + 7] = new Vector2(u, 0f);
		}

		// 顶面/底面中心
		uv[0] = new Vector2(0.5f, 0.5f);
		uv[7] = new Vector2(0.5f, 0.5f);

		mesh.vertices = vertices;
		mesh.triangles = triangles;
		mesh.uv = uv;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();

		GetComponent<MeshFilter>().mesh = mesh;
	}
}
