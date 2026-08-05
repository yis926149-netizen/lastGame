using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Zenject;

// 【波浪动画取证-2026-08-05】一键自动取证：
// 自动进 Play → 等地图生成 → 反射调用 MapWaveTestController.BeginRise（等价按 V）→
// 动画全程每 0.15s 截 Game 视图 + 转储全部 Chunk 渲染状态（MPB _ChunkProgress/
// _ChunkAnimBaseY/_ChunkAnimRiseHeight、材质 shader、顶点 Y 范围、bounds、开关状态），
// 状态机回 Idle 后补最终帧并退出 Play。
// 输出：项目根/WaveCaptures/HHmmss/*.png + report.txt。
// 注意：进 Play 触发域重载会清空静态字段，状态经 SessionState 持久化并在重载后自动恢复。
public static class WaveAnimCapture
{
    private const string KeyActive = "WaveCap.Active";
    private const string KeyOutDir = "WaveCap.OutDir";
    private const string KeyStage = "WaveCap.Stage";
    private const string KeyDeadline = "WaveCap.Deadline";
    private const string KeyNextSample = "WaveCap.NextSample";
    private const string KeyIndex = "WaveCap.Index";

    private const double SampleInterval = 0.15;
    private const double MapWaitTimeout = 90.0;
    private const double AnimTimeout = 15.0;

    private static StreamWriter _report; // 重载后重建（追加）

    private enum Stage
    {
        WaitMap = 0,
        Trigger = 1,
        Capture = 2,
    }

    [MenuItem("Tools/Debug/波浪动画自动取证(自动Play+触发+截图+状态转储)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[WaveCap] 请先退出 Play 再运行取证。");
            return;
        }
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".",
            "WaveCaptures", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(outDir);

        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOutDir, outDir);
        SessionState.SetInt(KeyStage, (int)Stage.WaitMap);
        SessionState.SetFloat(KeyDeadline, (float)(EditorApplication.timeSinceStartup + MapWaitTimeout));
        SessionState.SetFloat(KeyNextSample, 0f);
        SessionState.SetInt(KeyIndex, 0);

        EditorApplication.update -= Pump;
        EditorApplication.update += Pump;
        EditorApplication.EnterPlaymode();
    }

    // 域重载后（进 Play 时）自动恢复 Pump
    [InitializeOnLoadMethod]
    private static void RestoreAfterReload()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update -= Pump;
        EditorApplication.update += Pump;
    }

    private static void Pump()
    {
        if (!SessionState.GetBool(KeyActive, false))
        {
            EditorApplication.update -= Pump;
            return;
        }
        if (!EditorApplication.isPlaying) return; // 等待进入 Play

        Stage stage = (Stage)SessionState.GetInt(KeyStage, 0);
        double now = EditorApplication.timeSinceStartup;
        double deadline = SessionState.GetFloat(KeyDeadline, 0f);
        if (now > deadline)
        {
            Abort("[WaveCap] 超时中止（stage=" + stage + "）。");
            return;
        }

        switch (stage)
        {
            case Stage.WaitMap:
                TryWaitMap(now);
                break;
            case Stage.Trigger:
                DoTrigger(now);
                break;
            case Stage.Capture:
                DoCapture(now);
                break;
        }
    }

    private static void TryWaitMap(double now)
    {
        DiContainer container = FindContainer();
        if (container == null) return;
        IMapDataService mapData = TryResolve<IMapDataService>(container);
        if (mapData == null) return;
        IReadOnlyList<HexCellData> cells = null;
        try { cells = mapData.GetAllCells(); } catch { /* 地图未就绪 */ }
        if (cells == null || cells.Count == 0) return;

        MapWaveTestController controller = TryResolve<MapWaveTestController>(container);
        if (controller == null)
        {
            Abort("[WaveCap] 无法解析 MapWaveTestController。");
            return;
        }

        Log($"[WaveCap] 地图就绪：{cells.Count} 格。初始截图 + 状态转储。");
        CaptureScreenshot("00_initial");
        DumpState("initial");
        SessionState.SetInt(KeyStage, (int)Stage.Trigger);
    }

    private static void DoTrigger(double now)
    {
        DiContainer container = FindContainer();
        MapWaveTestController controller = container != null ? TryResolve<MapWaveTestController>(container) : null;
        if (controller == null)
        {
            Abort("[WaveCap] Trigger 阶段解析控制器失败。");
            return;
        }
        MethodInfo begin = typeof(MapWaveTestController).GetMethod("BeginRise", BindingFlags.NonPublic | BindingFlags.Instance);
        if (begin == null)
        {
            Abort("[WaveCap] 找不到 BeginRise 方法。");
            return;
        }

        Log("[WaveCap] 触发 BeginRise（等价按 V）。");
        begin.Invoke(controller, null);
        SessionState.SetInt(KeyStage, (int)Stage.Capture);
        SessionState.SetFloat(KeyNextSample, (float)now); // 立即采第一帧
        SessionState.SetFloat(KeyDeadline, (float)(now + AnimTimeout));
    }

    private static void DoCapture(double now)
    {
        if (now < SessionState.GetFloat(KeyNextSample, 0f)) return;

        int idx = SessionState.GetInt(KeyIndex, 0) + 1;
        SessionState.SetInt(KeyIndex, idx);
        string label = idx.ToString("D2");
        CaptureScreenshot(label);
        DumpState(label);
        SessionState.SetFloat(KeyNextSample, (float)(now + SampleInterval));

        // 结束条件：状态机回 Idle（回落完成或异常复位）
        DiContainer container = FindContainer();
        MapWaveTestController controller = container != null ? TryResolve<MapWaveTestController>(container) : null;
        string state = controller != null ? controller.State.ToString() : "?";
        if (state == "Idle" && idx > 3)
        {
            Log("[WaveCap] 状态机回 Idle，补最终帧并退出 Play。");
            CaptureScreenshot("99_final");
            DumpState("final");
            Finish();
        }
    }

    // ── 取证动作 ─────────────────────────────────────────────

    private static void CaptureScreenshot(string label)
    {
        string path = Path.Combine(SessionState.GetString(KeyOutDir, "."), label + ".png");
        ScreenCapture.CaptureScreenshot(path);
    }

    private static void DumpState(string label)
    {
        try
        {
            Log($"[WaveCap] ── 状态转储 [{label}] t={EditorApplication.timeSinceStartup:F2} " +
                $"DisableKeepBelowClip={MapMutationDiagnostics.DisableKeepBelowClip}");

            Camera cam = Camera.main;
            if (cam != null)
                Log($"[WaveCap]   Camera pos={cam.transform.position} rot={cam.transform.rotation.eulerAngles} " +
                    $"far={cam.farClipPlane} near={cam.nearClipPlane} enabled={cam.enabled}");

            ChunkMapRenderer renderer = UnityEngine.Object.FindObjectOfType<ChunkMapRenderer>();
            if (renderer == null)
            {
                Log("[WaveCap]   找不到 ChunkMapRenderer！");
                return;
            }
            FieldInfo chunksField = typeof(ChunkMapRenderer).GetField("_chunks", BindingFlags.NonPublic | BindingFlags.Instance);
            var chunks = chunksField?.GetValue(renderer) as IDictionary;
            if (chunks == null || chunks.Count == 0)
            {
                Log("[WaveCap]   _chunks 为空。");
                return;
            }

            int progressId = Shader.PropertyToID("_ChunkProgress");
            int baseYId = Shader.PropertyToID("_ChunkAnimBaseY");
            int riseId = Shader.PropertyToID("_ChunkAnimRiseHeight");
            var block = new MaterialPropertyBlock();

            foreach (DictionaryEntry entry in chunks)
            {
                ChunkRenderData chunk = entry.Value as ChunkRenderData;
                if (chunk == null) continue;
                MeshRenderer r = chunk.TerrainRenderer;
                MeshFilter f = chunk.TerrainFilter;
                Mesh m = f != null ? f.sharedMesh : null;

                string progress = "–", baseY = "–", rise = "–";
                if (r != null)
                {
                    block.Clear();
                    r.GetPropertyBlock(block);
                    if (block.HasProperty(progressId)) progress = block.GetFloat(progressId).ToString("F3");
                    if (block.HasProperty(baseYId)) baseY = block.GetFloat(baseYId).ToString("F1");
                    if (block.HasProperty(riseId)) rise = block.GetFloat(riseId).ToString("F2");
                }

                string mats = "null";
                if (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 0)
                {
                    var names = new System.Text.StringBuilder();
                    for (int i = 0; i < r.sharedMaterials.Length; i++)
                    {
                        if (i > 0) names.Append('|');
                        names.Append(r.sharedMaterials[i] != null ? r.sharedMaterials[i].shader.name : "NULL");
                    }
                    mats = names.ToString();
                }

                float minY = float.NaN, maxY = float.NaN;
                int vcount = 0;
                string bounds = "–";
                if (m != null)
                {
                    vcount = m.vertexCount;
                    Vector3[] verts = m.vertices;
                    if (verts != null && verts.Length > 0)
                    {
                        minY = float.MaxValue; maxY = float.MinValue;
                        foreach (Vector3 v in verts)
                        {
                            if (v.y < minY) minY = v.y;
                            if (v.y > maxY) maxY = v.y;
                        }
                    }
                    bounds = $"min={m.bounds.min} max={m.bounds.max}";
                }

                Log($"[WaveCap]   Chunk({chunk.Index.X},{chunk.Index.Z}): enabled={(r != null && r.enabled)} hostActive={(chunk.TerrainHost != null && chunk.TerrainHost.activeSelf)} " +
                    $"progress={progress} baseY={baseY} riseH={rise} verts={vcount} yRange=[{minY:F2},{maxY:F2}] " +
                    $"animCaches={(chunk.AnimUV2Cache != null ? chunk.AnimUV2Cache.Length : -1)} bounds={bounds} mats={mats}");
            }
        }
        catch (Exception e)
        {
            Log("[WaveCap] DumpState 异常：" + e.Message);
        }
    }

    // ── 辅助 ─────────────────────────────────────────────────

    private static DiContainer FindContainer()
    {
        var sceneContext = UnityEngine.Object.FindObjectOfType<SceneContext>();
        if (sceneContext != null) return sceneContext.Container;
        return ProjectContext.Instance != null ? ProjectContext.Instance.Container : null;
    }

    private static T TryResolve<T>(DiContainer container)
    {
        try { return container.Resolve<T>(); }
        catch { return default; }
    }

    private static void Log(string message)
    {
        Debug.Log(message);
        try
        {
            if (_report == null)
            {
                string path = Path.Combine(SessionState.GetString(KeyOutDir, "."), "report.txt");
                _report = new StreamWriter(path, true) { AutoFlush = true };
            }
            _report.WriteLine($"{EditorApplication.timeSinceStartup:F3} {message}");
        }
        catch { /* 忽略写文件失败 */ }
    }

    private static void Abort(string reason)
    {
        Log(reason);
        Finish();
    }

    private static void Finish()
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Pump;
        try { _report?.Flush(); _report?.Dispose(); } catch { }
        _report = null;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.delayCall += () =>
            Debug.Log("[WaveCap] 取证完成，输出目录：" + SessionState.GetString(KeyOutDir, "?"));
    }
}
