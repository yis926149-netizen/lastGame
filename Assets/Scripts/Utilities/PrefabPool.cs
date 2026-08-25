using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 通用预制体对象池（P3 对象池）：按 prefab 分桶，取用时按需扩容，回收时 SetActive(false)。
/// 用于替代运行期反复 Instantiate/Destroy 的瞬态对象（指示器、特效等），降低 GC 与实例化卡顿。
///
/// 用法：
///   var go = _pool.Get(prefab, parent);   // 激活状态返回；池空则 Instantiate
///   _pool.Release(prefab, go);            // SetActive(false) 后入池；重复回收被忽略
///   _pool.Clear();                        // 销毁全部池中实例（切场景/销毁时调用）
///
/// 注意：池只管"存活/回收"，父级与坐标由调用方在 Get 后自行设置（对齐 Instantiate 后自行摆放的既有写法）。
/// </summary>
public sealed class PrefabPool
{
    private readonly Transform _defaultParent;
    private readonly Dictionary<GameObject, Queue<GameObject>> _buckets =
        new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, HashSet<GameObject>> _pooled =
        new Dictionary<GameObject, HashSet<GameObject>>();

    public PrefabPool(Transform defaultParent = null)
    {
        _defaultParent = defaultParent;
    }

    /// <summary>取出一个激活状态的实例；池空则 Instantiate。prefab 为 null 时返回 null。</summary>
    public GameObject Get(GameObject prefab, Transform parent = null, bool worldPositionStays = false)
    {
        if (prefab == null)
        {
            Debug.LogError("[PrefabPool] Get 传入的 prefab 为 null。");
            return null;
        }

        if (_buckets.TryGetValue(prefab, out Queue<GameObject> bucket) && bucket.Count > 0)
        {
            GameObject instance = bucket.Dequeue();
            _pooled[prefab].Remove(instance);

            // 池中实例可能被外部意外 Destroy，防御性跳过并重建。
            if (instance == null)
                return Get(prefab, parent, worldPositionStays);

            instance.SetActive(true);
            Reparent(instance, parent, worldPositionStays);
            return instance;
        }

        Transform targetParent = parent != null ? parent : _defaultParent;
        GameObject created = targetParent != null
            ? Object.Instantiate(prefab, targetParent)
            : Object.Instantiate(prefab);
        return created;
    }

    /// <summary>回收实例（SetActive(false) 后入池）。重复回收同一实例会被忽略。</summary>
    public void Release(GameObject prefab, GameObject instance)
    {
        if (prefab == null || instance == null) return;

        if (!_buckets.TryGetValue(prefab, out Queue<GameObject> bucket))
        {
            bucket = new Queue<GameObject>();
            _buckets[prefab] = bucket;
            _pooled[prefab] = new HashSet<GameObject>();
        }

        if (!_pooled[prefab].Add(instance)) return; // 已在池中
        instance.SetActive(false);
        bucket.Enqueue(instance);
    }

    /// <summary>清空池并销毁所有池中实例（组件销毁/切场景时调用，避免泄漏）。</summary>
    public void Clear()
    {
        foreach (KeyValuePair<GameObject, Queue<GameObject>> kv in _buckets)
        {
            while (kv.Value.Count > 0)
            {
                GameObject instance = kv.Value.Dequeue();
                if (instance != null) Object.Destroy(instance);
            }
        }
        _buckets.Clear();
        _pooled.Clear();
    }

    private void Reparent(GameObject instance, Transform parent, bool worldPositionStays)
    {
        if (parent != null)
            instance.transform.SetParent(parent, worldPositionStays);
        else if (_defaultParent != null)
            instance.transform.SetParent(_defaultParent, false);
    }
}
