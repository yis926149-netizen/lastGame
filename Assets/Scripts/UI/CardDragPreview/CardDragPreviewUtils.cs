using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 卡牌拖拽世界空间预览的「拖拽期停用 / 落地恢复」工具（改造计划 §4.4）。
/// 拖拽期把模型实例退化为纯视觉体：禁用物理、导航、音频、粒子、灯光、
/// world-space 血条与全部 MonoBehaviour（Animator 保留）；不改 Layer、不改 Renderer。
/// 落地前 RestoreForDeployment 按 PrepareForDrag 记录的原始状态逐一恢复——
/// 不是无条件置 true：prefab 上本就禁用的组件必须保持禁用。
/// 一律「禁用」而非「移除」，避免破坏 Prefab 组件之间的依赖关系。
/// </summary>
public static class CardDragPreviewUtils
{
    internal struct ComponentToggle<T> where T : Component
    {
        public T Component;
        public bool WasEnabled;
    }

    internal struct RigidbodyToggle
    {
        public Rigidbody Body;
        public bool WasKinematic;
        public bool DetectCollisions;
    }

    internal struct AnimatorToggle
    {
        public Animator Animator;
        public bool ApplyRootMotion;
        public AnimatorCullingMode CullingMode;
    }

    /// <summary>PrepareForDrag 记录的组件状态快照；RestoreForDeployment 据此还原。</summary>
    public sealed class PreparationState
    {
        internal readonly List<ComponentToggle<Behaviour>> Behaviours = new List<ComponentToggle<Behaviour>>();
        internal readonly List<ComponentToggle<Collider>> Colliders = new List<ComponentToggle<Collider>>();
        internal readonly List<RigidbodyToggle> Rigidbodies = new List<RigidbodyToggle>();
        internal readonly List<ComponentToggle<Renderer>> Renderers = new List<ComponentToggle<Renderer>>();
        internal readonly List<ComponentToggle<ParticleSystem>> Particles = new List<ComponentToggle<ParticleSystem>>();
        internal readonly List<AnimatorToggle> Animators = new List<AnimatorToggle>();
        internal readonly List<ComponentToggle<SkinnedMeshRenderer>> Skinned = new List<ComponentToggle<SkinnedMeshRenderer>>();
    }

    /// <summary>
    /// 把刚实例化的预览模型退化为纯视觉体（只在 Begin 阶段调用一次，每帧禁止 GetComponentsInChildren）。
    /// 记录全部被改动组件的原始状态，供 RestoreForDeployment 落地前还原。
    /// </summary>
    public static PreparationState PrepareForDrag(GameObject instance)
    {
        var state = new PreparationState();
        if (instance == null) return state;

        // 运行时逻辑控制器（UnitMovementController / BuildingController / 血条脚本等）：
        // 无法逐一枚举类型，统一禁用除 Animator 之外的所有 MonoBehaviour。
        // Animator / NavMeshAgent / AudioSource 等不是 MonoBehaviour，天然不受影响（下方单独处理）。
        foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;
            state.Behaviours.Add(new ComponentToggle<Behaviour> { Component = behaviour, WasEnabled = behaviour.enabled });
            behaviour.enabled = false;
        }

        // 物理：禁用碰撞体，刚体转为运动学并冻结碰撞检测，避免预览体参与模拟或射线。
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null) continue;
            state.Colliders.Add(new ComponentToggle<Collider> { Component = collider, WasEnabled = collider.enabled });
            collider.enabled = false;
        }

        foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
        {
            if (body == null) continue;
            state.Rigidbodies.Add(new RigidbodyToggle
            {
                Body = body,
                WasKinematic = body.isKinematic,
                DetectCollisions = body.detectCollisions,
            });
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        // 导航：预览体不应寻路，也不应占用 NavMesh。
        foreach (NavMeshAgent agent in instance.GetComponentsInChildren<NavMeshAgent>(true))
        {
            if (agent == null) continue;
            state.Behaviours.Add(new ComponentToggle<Behaviour> { Component = agent, WasEnabled = agent.enabled });
            agent.enabled = false;
        }

        foreach (NavMeshObstacle obstacle in instance.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null) continue;
            state.Behaviours.Add(new ComponentToggle<Behaviour> { Component = obstacle, WasEnabled = obstacle.enabled });
            obstacle.enabled = false;
        }

        // 音频 / 粒子 / 拖尾 / 灯光：预览不出声、不产生额外渲染与光照污染。
        foreach (AudioSource audio in instance.GetComponentsInChildren<AudioSource>(true))
        {
            if (audio == null) continue;
            state.Behaviours.Add(new ComponentToggle<Behaviour> { Component = audio, WasEnabled = audio.enabled });
            audio.Stop();
            audio.enabled = false;
        }

        foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (particle == null) continue;
            state.Particles.Add(new ComponentToggle<ParticleSystem> { Component = particle, WasEnabled = particle.isPlaying });
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        foreach (ParticleSystemRenderer particleRenderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            if (particleRenderer == null) continue;
            state.Renderers.Add(new ComponentToggle<Renderer> { Component = particleRenderer, WasEnabled = particleRenderer.enabled });
            particleRenderer.enabled = false;
        }

        foreach (TrailRenderer trail in instance.GetComponentsInChildren<TrailRenderer>(true))
        {
            if (trail == null) continue;
            state.Renderers.Add(new ComponentToggle<Renderer> { Component = trail, WasEnabled = trail.enabled });
            trail.enabled = false;
        }

        foreach (Light light in instance.GetComponentsInChildren<Light>(true))
        {
            if (light == null) continue;
            state.Behaviours.Add(new ComponentToggle<Behaviour> { Component = light, WasEnabled = light.enabled });
            light.enabled = false;
        }

        // world-space 血条等 UI：整块禁用（Canvas 关掉即不再渲染与提交网格）。
        foreach (Canvas canvas in instance.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas == null) continue;
            state.Behaviours.Add(new ComponentToggle<Behaviour> { Component = canvas, WasEnabled = canvas.enabled });
            canvas.enabled = false;
        }

        foreach (GraphicRaycaster raycaster in instance.GetComponentsInChildren<GraphicRaycaster>(true))
        {
            if (raycaster == null) continue;
            state.Behaviours.Add(new ComponentToggle<Behaviour> { Component = raycaster, WasEnabled = raycaster.enabled });
            raycaster.enabled = false;
        }

        // 交互事件：预览体不接收任何指针事件。
        foreach (EventTrigger trigger in instance.GetComponentsInChildren<EventTrigger>(true))
        {
            if (trigger == null) continue;
            state.Behaviours.Add(new ComponentToggle<Behaviour> { Component = trigger, WasEnabled = trigger.enabled });
            trigger.enabled = false;
        }

        // 动画：保留 Animator 播放待机动画，但禁止 Root Motion 让模型漂出悬停位置。
        foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
        {
            if (animator == null) continue;
            state.Animators.Add(new AnimatorToggle
            {
                Animator = animator,
                ApplyRootMotion = animator.applyRootMotion,
                CullingMode = animator.cullingMode,
            });
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // 蒙皮网格：强制按当前骨骼姿势更新包围盒（主相机视锥剔除判定用）。
        foreach (SkinnedMeshRenderer skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned == null) continue;
            state.Skinned.Add(new ComponentToggle<SkinnedMeshRenderer> { Component = skinned, WasEnabled = skinned.updateWhenOffscreen });
            skinned.updateWhenOffscreen = true;
        }

        return state;
    }

    /// <summary>
    /// 落地前按 PrepareForDrag 记录的原始状态逐一恢复（不是无条件置 true）。
    /// 必须在 SpawnUnit/SpawnBuilding 接线之前调用。
    /// </summary>
    public static void RestoreForDeployment(GameObject instance, PreparationState state)
    {
        if (instance == null || state == null) return;

        foreach (var toggle in state.Behaviours)
            if (toggle.Component != null) toggle.Component.enabled = toggle.WasEnabled;

        foreach (var toggle in state.Colliders)
            if (toggle.Component != null) toggle.Component.enabled = toggle.WasEnabled;

        foreach (var toggle in state.Rigidbodies)
        {
            if (toggle.Body == null) continue;
            toggle.Body.isKinematic = toggle.WasKinematic;
            toggle.Body.detectCollisions = toggle.DetectCollisions;
        }

        foreach (var toggle in state.Renderers)
            if (toggle.Component != null) toggle.Component.enabled = toggle.WasEnabled;

        // 粒子：拖拽前正在播放的恢复播放；本就停止的保持停止。
        foreach (var toggle in state.Particles)
        {
            if (toggle.Component == null) continue;
            if (toggle.WasEnabled) toggle.Component.Play();
        }

        foreach (var toggle in state.Animators)
        {
            if (toggle.Animator == null) continue;
            toggle.Animator.applyRootMotion = toggle.ApplyRootMotion;
            toggle.Animator.cullingMode = toggle.CullingMode;
        }

        foreach (var toggle in state.Skinned)
            if (toggle.Component != null) toggle.Component.updateWhenOffscreen = toggle.WasEnabled;
    }
}
