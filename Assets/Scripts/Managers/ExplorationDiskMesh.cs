using UnityEngine;

/// <summary>
/// 运行时生成扁平六边形盘体 Mesh（方案二飞盘砸落特效用）。
/// 与 ExplorationPillarMesh 逻辑相同，默认高度极小（0.2），外观为硬币/石板。
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ExplorationDiskMesh : MonoBehaviour
{
	[SerializeField] private float _outerRadius = 2.1f;
	[SerializeField] private float _height = 0.2f;

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
		mesh.name = "ExplorationDisk";

		// 顶面 7 顶点（中心 + 6 角）+ 底面 7 顶点 = 14
		Vector3[] vertices = new Vector3[14];
		int[] triangles = new int[72]; // 顶面 6 三角 + 底面 6 三角 + 侧面 6×2 三角
		Vector2[] uv = new Vector2[14];

		// Pointy-top：第一个顶点在 +Z 方向，与地图格子方向一致
		Vector3[] topVerts = new Vector3[7];
		topVerts[0] = new Vector3(0,      h, 0);
		topVerts[1] = new Vector3(0,      h,  r);
		topVerts[2] = new Vector3( innerR, h,  0.5f * r);
		topVerts[3] = new Vector3( innerR, h, -0.5f * r);
		topVerts[4] = new Vector3(0,      h, -r);
		topVerts[5] = new Vector3(-innerR, h, -0.5f * r);
		topVerts[6] = new Vector3(-innerR, h,  0.5f * r);

		Vector3[] botVerts = new Vector3[7];
		botVerts[0] = new Vector3(0,      0, 0);
		botVerts[1] = new Vector3(0,      0,  r);
		botVerts[2] = new Vector3( innerR, 0,  0.5f * r);
		botVerts[3] = new Vector3( innerR, 0, -0.5f * r);
		botVerts[4] = new Vector3(0,      0, -r);
		botVerts[5] = new Vector3(-innerR, 0, -0.5f * r);
		botVerts[6] = new Vector3(-innerR, 0,  0.5f * r);

		for (int i = 0; i < 7; i++)
		{
			vertices[i]     = topVerts[i];
			vertices[i + 7] = botVerts[i];
		}

		int triIndex = 0;

		// 顶面扇形：6 个三角形
		for (int i = 1; i <= 6; i++)
		{
			int next = (i % 6) + 1;
			triangles[triIndex++] = 0;
			triangles[triIndex++] = i;
			triangles[triIndex++] = next;
		}

		// 侧面：6 个四边形 = 12 个三角形
		for (int i = 1; i <= 6; i++)
		{
			int next = (i % 6) + 1;
			int b1 = i + 7, b2 = next + 7;

			triangles[triIndex++] = i;
			triangles[triIndex++] = b1;
			triangles[triIndex++] = next;

			triangles[triIndex++] = next;
			triangles[triIndex++] = b1;
			triangles[triIndex++] = b2;
		}

		// 底面扇形：6 个三角形（与顶面相反绕序，从下方看为顺时针正面）
		for (int i = 1; i <= 6; i++)
		{
			int next = (i % 6) + 1;
			triangles[triIndex++] = 7;         // 底面中心
			triangles[triIndex++] = next + 7; // 反转顶点顺序
			triangles[triIndex++] = i + 7;
		}

		// UV：侧面按周长展开，顶/底面中心取 (0.5, 0.5)
		float dx = innerR;
		float dz = 0.5f * r;
		float sideLength = Mathf.Sqrt(dx * dx + dz * dz);
		float totalPerimeter = sideLength * 6f;

		float[] perimU = new float[7];
		perimU[1] = 0f;
		for (int i = 2; i <= 6; i++)
			perimU[i] = perimU[i - 1] + sideLength / totalPerimeter;

		for (int i = 1; i <= 6; i++)
		{
			uv[i]     = new Vector2(perimU[i], 1f);
			uv[i + 7] = new Vector2(perimU[i], 0f);
		}
		uv[0] = new Vector2(0.5f, 0.5f);
		uv[7] = new Vector2(0.5f, 0.5f);

		mesh.vertices  = vertices;
		mesh.triangles = triangles;
		mesh.uv        = uv;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();

		GetComponent<MeshFilter>().mesh = mesh;
	}
}
