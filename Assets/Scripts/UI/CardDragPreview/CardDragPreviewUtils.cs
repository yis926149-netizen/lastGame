using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 卡牌拖拽预览的模型「纯展示化」工具（实施计划 §5.5）。
/// 预览实例只用于被预览相机拍摄，必须剥离一切运行时行为：
/// 物理、导航、音频、粒子、灯光、world-space 血条、逻辑控制器与交互事件。
/// 一律「禁用」而非「移除」，避免破坏 Prefab 上组件之间的依赖关系。
/// </summary>
public static class CardDragPreviewUtils
{
    /// <summary>预览专用 Layer 名称（ProjectSettings/TagManager.asset 中的 User Layer 9）。</summary>
    public const string PreviewLayerName = "CardPreview";

    /// <summary>
    /// 把刚实例化的预览模型剥离成纯视觉体：递归设置 Layer、禁用非视觉组件。
    /// 只在 Begin 阶段调用一次（每帧禁止 GetComponentsInChildren）。
    /// </summary>
    public static void StripToVisual(GameObject instance, int previewLayer)
    {
        if (instance == null) return;

        SetLayerRecursively(instance.transform, previewLayer);

        // 物理：禁用碰撞体，刚体转为运动学并冻结，避免预览体参与模拟或射线。
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            if (collider != null) collider.enabled = false;

        foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
        {
            if (body == null) continue;
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        // 导航：预览体不应寻路，也不应占用 NavMesh。
        foreach (UnityEngine.AI.NavMeshAgent agent in instance.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
            if (agent != null) agent.enabled = false;

        foreach (UnityEngine.AI.NavMeshObstacle obstacle in instance.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true))
            if (obstacle != null) obstacle.enabled = false;

        // 音频 / 粒子 / 拖尾 / 灯光：预览不出声、不产生额外渲染与光照污染。
        foreach (AudioSource audio in instance.GetComponentsInChildren<AudioSource>(true))
        {
            if (audio == null) continue;
            audio.Stop();
            audio.enabled = false;
        }

        foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (particle == null) continue;
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        foreach (ParticleSystemRenderer particleRenderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
            if (particleRenderer != null) particleRenderer.enabled = false;

        foreach (TrailRenderer trail in instance.GetComponentsInChildren<TrailRenderer>(true))
            if (trail != null) trail.enabled = false;

        foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            if (light != null) light.enabled = false;

        // world-space 血条等 UI：整块禁用（Canvas 关掉即不再渲染与提交网格）。
        foreach (Canvas canvas in instance.GetComponentsInChildren<Canvas>(true))
            if (canvas != null) canvas.enabled = false;

        foreach (GraphicRaycaster raycaster in instance.GetComponentsInChildren<GraphicRaycaster>(true))
            if (raycaster != null) raycaster.enabled = false;

        // 交互事件：预览体不接收任何指针事件。
        foreach (EventTrigger trigger in instance.GetComponentsInChildren<EventTrigger>(true))
            if (trigger != null) trigger.enabled = false;

        // 运行时逻辑控制器（UnitMovementController / BuildingController / 血条脚本等）：
        // 无法逐一枚举类型，统一禁用除 Animator 之外的所有 MonoBehaviour。
        // Animator 不是 MonoBehaviour，天然保留（待机动画）；Renderer 同理不受影响。
        foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != null) behaviour.enabled = false;

        // 动画：保留 Animator 播放待机动画，但禁止 Root Motion 让模型漂移出取景框。
        foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
        {
            if (animator == null) continue;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // 蒙皮网格：强制按当前骨骼姿势重算包围盒。
        // 默认走「离屏用绑定姿势缓存盒」的路径，预览体常年在相机视锥外的独立 Layer，
        // 取景时读到的会是与实际姿势不符的缓存盒，导致单位模型在 RT 内整体偏移。
        foreach (SkinnedMeshRenderer skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (skinned != null) skinned.updateWhenOffscreen = true;
    }

    /// <summary>递归设置 Layer（模型根与全部子物体，含未激活节点）。</summary>
    public static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null) return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    /// <summary>
    /// 计算模型的渲染包围盒（世界空间）。仅在 Begin 阶段调用一次用于相机取景。
    /// 无任何 Renderer 时返回 false（调用方回退到默认正交尺寸）。
    ///
    /// 注意两个坑（曾导致「部分模型不显示 / 单位偏移」）：
    /// 1) 不能用 renderer.enabled 过滤。StripToVisual 已禁用全部 MonoBehaviour，
    ///    部分 Prefab 的渲染器本就默认关闭、或由脚本运行时开启，按 enabled 过滤会漏掉真实网格，
    ///    全漏时返回 false 退回默认正交尺寸，取景必然错位。
    /// 2) 蒙皮网格不能用 sharedMesh.bounds 经 renderer.transform 换算：
    ///    其顶点在绑定姿势空间、由骨骼 bindposes 驱动，与渲染器自身 transform 无关
    ///    （渲染器常挂在与实际网格位置无关的节点上），换算结果会整体偏移。
    ///    必须靠 ForceEvaluatePose 先求值骨骼姿势，再直接读 renderer.bounds。
    /// </summary>
    public static bool TryGetRenderBounds(GameObject instance, out Bounds bounds)
    {
        bounds = default;
        if (instance == null) return false;

        bool found = false;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            // 粒子/拖尾已禁用，其 bounds 无意义；只统计实际网格类渲染器。
            if (renderer == null) continue;
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer) continue;

            Bounds rendererBounds = renderer.bounds;
            if (rendererBounds.size.sqrMagnitude <= 0f) continue;

            if (!found)
            {
                bounds = rendererBounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds);
            }
        }

        return found;
    }

    /// <summary>
    /// 强制求值一次骨骼姿势，让 SkinnedMeshRenderer.bounds 在 Instantiate 当帧即可用。
    /// 蒙皮网格的世界包围盒由骨骼当前姿势决定，实例化当帧 Animator 尚未求值，
    /// 直接读 bounds 会拿到错位/空盒；必须在取景前显式 Update(0) 推进一次。
    /// 只在 Begin 阶段调用一次（每帧禁止 GetComponentsInChildren）。
    /// </summary>
    public static void ForceEvaluatePose(GameObject instance)
    {
        if (instance == null) return;

        foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
        {
            if (animator == null || !animator.isActiveAndEnabled) continue;
            // Update(0) 不推进时间，只按当前状态求值一次骨骼矩阵。
            animator.Update(0f);
        }
    }
}
