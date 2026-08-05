using System.Collections.Generic;
using UnityEngine;

internal sealed class TerrainGeometry
{
    public Vector3[] Vertices;
    public Vector2[] UVs;
    public int[][] SubMeshIndices;
    public Material[] BaseMaterials;
    public Material[] RectAs;
    public Material[] RectBs;
    public Material[] TriAs;
    public Material[] TriBs;
    public Material[] TriCs;
    public Vector2[] UV2s;
    public Vector2[] UV3s;
}

internal sealed class RiverGeometry
{
    public Vector3[] Vertices;
    public Vector2[] UVs;
    public int[] Indices;
}

internal sealed class WaterGeometry
{
    public Vector3[] Vertices;
    public Vector2[] UVs;
    public int[][] Indices;
}
