using UnityEditor;
using UnityEngine;

// 【波浪动画调试】运行时检查工具：
// 1. 静态检查：场景中 Chunk 的 mesh.uv2/uv3 数据、材质 shader、MPB _ChunkProgress
// 2. 动画期间（按 V 后）每帧采样：确认 _ChunkProgress 是否在 0→1 驱动、uv 数据是否被 shader 读取
public static class WaveAnimDebugCheck
{
    static float _lastSampledProgress = -1f;

    [MenuItem("Tools/Debug/波浪动画通道检查")]
    public static void Execute()
    {
        var chunks = Object.FindObjectsOfType<MapChunkView>();
        Debug.Log($"[WaveAnimDebug] 场景中 Chunk 数量: {chunks?.Length ?? 0}");
        if (chunks == null || chunks.Length == 0) return;

        int shown = 0;
        foreach (MapChunkView view in chunks)
        {
            var filter = view.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Mesh m = filter.sharedMesh;
            Vector2[] uv2 = m.uv2;
            Vector2[] uv3 = m.uv3;
            string uv2Info = uv2 == null || uv2.Length == 0 ? "空(0)" : $"{uv2.Length}";
            string uv3Info = uv3 == null || uv3.Length == 0 ? "空(0)" : $"{uv3.Length}";
            Vector2 first2 = uv2 != null && uv2.Length > 0 ? uv2[0] : new Vector2(float.NaN, float.NaN);
            Vector2 first3 = uv3 != null && uv3.Length > 0 ? uv3[0] : new Vector2(float.NaN, float.NaN);
            Vector2 last3 = uv3 != null && uv3.Length > 0 ? uv3[uv3.Length - 1] : new Vector2(float.NaN, float.NaN);
            var r = filter.GetComponent<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            r?.GetPropertyBlock(block);
            int progressId = Shader.PropertyToID("_ChunkProgress");
            float progress = block.HasProperty(progressId) ? block.GetFloat(progressId) : float.NaN;
            string matInfo = "null";
            if (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null)
                matInfo = r.sharedMaterials[0].shader.name;
            Debug.Log($"[WaveAnimDebug] Chunk {view.Index.X},{view.Index.Z}: verts={m.vertexCount} " +
                      $"uv2={uv2Info} 首值=({first2.x:F2},{first2.y:F2}) " +
                      $"uv3={uv3Info} 首值=({first3.x:F2},{first3.y:F2}) 末值=({last3.x:F2},{last3.y:F2}) " +
                      $"_ChunkProgress={progress} 材质0={matInfo}");
            if (++shown >= 5) break;
        }
    }

    [MenuItem("Tools/Debug/波浪动画期间逐帧采样(开)")]
    public static void StartSampling()
    {
        EditorApplication.update -= Sample;
        EditorApplication.update += Sample;
        _lastSampledProgress = -1f;
        Debug.Log("[WaveAnimDebug] 已开启逐帧采样：进入 Play 按 V，观察 _ChunkProgress 与 uv 数据变化。");
    }

    [MenuItem("Tools/Debug/波浪动画期间逐帧采样(关)")]
    public static void StopSampling()
    {
        EditorApplication.update -= Sample;
        Debug.Log("[WaveAnimDebug] 已关闭逐帧采样。");
    }

    static void Sample()
    {
        var chunks = Object.FindObjectsOfType<MapChunkView>();
        if (chunks == null || chunks.Length == 0) return;
        MapChunkView view = chunks[0];
        var filter = view.GetComponentInChildren<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        var r = filter.GetComponent<MeshRenderer>();
        var block = new MaterialPropertyBlock();
        r?.GetPropertyBlock(block);
        float progress = block.HasProperty(Shader.PropertyToID("_ChunkProgress"))
            ? block.GetFloat(Shader.PropertyToID("_ChunkProgress"))
            : float.NaN;
        if (Mathf.Abs(progress - _lastSampledProgress) > 0.01f)
        {
            _lastSampledProgress = progress;
            Mesh m = filter.sharedMesh;
            Vector2[] uv2 = m.uv2;
            Vector2[] uv3 = m.uv3;
            string mat = r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 0
                ? r.sharedMaterials[0].shader.name : "null";
            Debug.Log($"[WaveAnimDebug] 采样: _ChunkProgress={progress:F3} uv2#={uv2?.Length ?? 0} " +
                      $"uv2首=({uv2?[0].x:F1},{uv2?[0].y:F1}) uv3首=({uv3?[0].x:F2},{uv3?[0].y:F1}) 材质={mat}");
        }
    }
}
