using System.Collections.Generic;
using UnityEngine;

/// <summary>世界空间一次性粒子特效的播放入口。</summary>
public interface IVfxService
{
    /// <summary>在指定世界坐标播放一次特效（用 prefab 自身旋转）。未登记或未绑定 prefab 时静默跳过。</summary>
    void Play(VfxId id, Vector3 position);

    /// <summary>在指定世界坐标按给定旋转播放一次特效（斜坡对齐等场景使用）。</summary>
    void Play(VfxId id, Vector3 position, Quaternion rotation);
}

/// <summary>
/// 粒子特效服务：按 VfxId 查 VfxConfigSO 取 prefab，实例化到世界、播放、定时自毁。
/// 特效一律独立实例化，不挂到触发方对象下——触发方可能移动或被销毁，特效应留在原地播完。
/// 自毁时长由 prefab 自身 duration + 粒子寿命推算，不依赖 stopAction 配置是否正确。
/// 配置缺失（SO 未绑定 / id 未登记 / prefab 为空）一律降级为空操作，只在首次告警一次。
/// </summary>
public class VfxService : IVfxService
{
    private readonly VfxConfigSO _config;
    private readonly Dictionary<VfxId, VfxConfigSO.VfxEntry> _entries = new Dictionary<VfxId, VfxConfigSO.VfxEntry>();
    private readonly HashSet<VfxId> _warnedMissing = new HashSet<VfxId>();

    private Transform _root;

    public VfxService(VfxConfigSO config)
    {
        _config = config;

        if (_config == null)
        {
            Debug.LogWarning("[VfxService] GameInstaller 未绑定 VfxConfigSO：全部粒子特效关闭。");
            return;
        }

        foreach (VfxConfigSO.VfxEntry entry in _config.entries)
        {
            if (entry == null || entry.id == VfxId.None) continue;
            if (_entries.ContainsKey(entry.id))
            {
                Debug.LogWarning($"[VfxService] VfxConfig 中 {entry.id} 重复登记，以第一条为准。");
                continue;
            }
            _entries.Add(entry.id, entry);
        }
    }

    public void Play(VfxId id, Vector3 position)
    {
        VfxConfigSO.VfxEntry entry = ResolveEntry(id);
        if (entry == null) return;

        Play(entry, position, entry.prefab.transform.rotation);
    }

    public void Play(VfxId id, Vector3 position, Quaternion rotation)
    {
        VfxConfigSO.VfxEntry entry = ResolveEntry(id);
        if (entry == null) return;

        Play(entry, position, rotation);
    }

    private void Play(VfxConfigSO.VfxEntry entry, Vector3 position, Quaternion rotation)
    {
        EnsureRoot();

        ParticleSystem vfx = Object.Instantiate(entry.prefab, position + entry.positionOffset, rotation, _root);
        if (vfx == null) return;

        if (!Mathf.Approximately(entry.scale, 1f))
            vfx.transform.localScale = entry.prefab.transform.localScale * Mathf.Max(0f, entry.scale);

        vfx.Play(true);

        // duration + 最大粒子寿命 = 整段播完所需时间；再加配置余量容忍子发射器与拖尾。
        ParticleSystem.MainModule main = vfx.main;
        float lifetime = main.duration + main.startLifetime.constantMax + Mathf.Max(0f, entry.destroyPadding);
        Object.Destroy(vfx.gameObject, lifetime);
    }

    /// <summary>取登记项；未登记或 prefab 为空时返回 null，并对同一 id 只告警一次。</summary>
    private VfxConfigSO.VfxEntry ResolveEntry(VfxId id)
    {
        if (id == VfxId.None) return null;

        if (!_entries.TryGetValue(id, out VfxConfigSO.VfxEntry entry) || entry.prefab == null)
        {
            if (_warnedMissing.Add(id))
                Debug.LogWarning($"[VfxService] 特效 {id} 未在 VfxConfig 中登记或 prefab 未绑定：本次及后续播放跳过。");
            return null;
        }

        return entry;
    }

    /// <summary>特效统一挂在一个场景根节点下，避免散落污染 Hierarchy。</summary>
    private void EnsureRoot()
    {
        if (_root != null) return;
        _root = new GameObject("VfxRoot").transform;
    }
}
