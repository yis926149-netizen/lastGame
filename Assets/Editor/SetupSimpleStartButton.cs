#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一次性编辑器工具：在 StartScene 的 Canvas 下创建 SimpleStartButton GameObject 并完成基础配置。
/// 执行完毕后可删除本脚本（或保留也不影响运行时）。
/// 菜单：Tools / Setup SimpleStartButton
/// </summary>
public static class SetupSimpleStartButton
{
    [MenuItem("Tools/Setup SimpleStartButton")]
    public static void Run()
    {
        // 1. 打开 StartScene
        string scenePath = "Assets/Scenes/StartScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // 2. 找到 Canvas
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[SetupSimpleStartButton] 未在 StartScene 中找到 Canvas，操作中止。");
            return;
        }

        // 3. 防重复：若已存在同名节点则跳过
        Transform existing = canvas.transform.Find("SimpleStartButton");
        if (existing != null)
        {
            Debug.LogWarning("[SetupSimpleStartButton] Canvas 下已存在 SimpleStartButton，跳过创建。");
            return;
        }

        // 4. 创建父节点 GameObject（挂脚本用）
        GameObject root = new GameObject("SimpleStartButton");
        root.transform.SetParent(canvas.transform, false);

        // 5. 添加 RectTransform 并居中
        RectTransform rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot     = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = Vector2.zero;

        // 6. 创建实际 Button 子节点
        GameObject btnGO = new GameObject("StartBtn");
        btnGO.transform.SetParent(root.transform, false);

        RectTransform btnRect = btnGO.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0.5f);
        btnRect.anchorMax = new Vector2(0.5f, 0.5f);
        btnRect.pivot     = new Vector2(0.5f, 0.5f);
        btnRect.anchoredPosition = Vector2.zero;
        btnRect.sizeDelta = new Vector2(300f, 80f);

        // Image（Button 依赖）
        Image img = btnGO.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 1f, 1f);

        // Button 组件
        Button btn = btnGO.AddComponent<Button>();

        // 7. 文本子节点
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(btnGO.transform, false);

        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin        = Vector2.zero;
        textRect.anchorMax        = Vector2.one;
        textRect.offsetMin        = Vector2.zero;
        textRect.offsetMax        = Vector2.zero;

        UnityEngine.UI.Text label = textGO.AddComponent<UnityEngine.UI.Text>();
        label.text      = "开始游戏";
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize  = 36;
        label.color     = Color.white;
        // 使用内置默认字体，避免 null 引用
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 8. 挂载 SimpleStartButton 脚本并配置引用
        SimpleStartButton ssb = root.AddComponent<SimpleStartButton>();

        // 通过 SerializedObject 设置 _button（私有序列化字段）
        SerializedObject so  = new SerializedObject(ssb);
        SerializedProperty sp = so.FindProperty("_button");
        sp.objectReferenceValue = btn;
        so.ApplyModifiedProperties();

        // 9. 绑定 onClick 事件
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            btn.onClick,
            ssb.OnStartClicked
        );

        // 10. 将原有 open GameObject 设为 inactive（如存在）
        GameObject openGO = GameObject.Find("open");
        if (openGO != null)
        {
            openGO.SetActive(false);
            Debug.Log("[SetupSimpleStartButton] 已将 open GameObject 设为 inactive。");
        }
        else
        {
            Debug.LogWarning("[SetupSimpleStartButton] 未找到 open GameObject，可能已为 inactive 或名称不同，请手动检查。");
        }

        // 11. 保存场景
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[SetupSimpleStartButton] 完成。Canvas 下已添加 SimpleStartButton，_delaySeconds 默认 3 秒，可在 Inspector 调整。");
    }
}
#endif
