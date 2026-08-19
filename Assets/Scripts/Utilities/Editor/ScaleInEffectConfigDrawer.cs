using UnityEditor;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：ScaleInEffectConfig 的自定义 Inspector 绘制器。
//          每个条目头部显示“动画类型”下拉框，根据所选类型只暴露对应的参数：
//          · Scale    → target / scaleUpMultiplier / scaleDownDuration / scaleDownEase / overshoot / hideBeforePlay
//          · Fade     → fadeTargets / fadeDuration / fadeEase
//          · Position → positionTargets / positionOffset / positionDuration / positionEase
//          通用参数（delaySeconds / useUnscaledTime / onComplete）始终显示。
//****************************************

[CustomPropertyDrawer(typeof(ScaleInEffectConfig))]
public class ScaleInEffectConfigDrawer : PropertyDrawer
{
    private static readonly string[] CommonFields = { "delaySeconds", "useUnscaledTime", "onComplete" };
    private static readonly string[] ScaleFields = { "target", "scaleUpMultiplier", "scaleDownDuration", "scaleDownEase", "overshoot", "hideBeforePlay" };
    private static readonly string[] FadeFields = { "fadeTargets", "fadeDuration", "fadeEase" };
    private static readonly string[] PositionFields = { "positionTargets", "positionOffset", "positionDuration", "positionEase" };

    private const float TypeDropdownWidth = 130f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // 头部行：折叠箭头 + 类型下拉框
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded)
            return height;

        height += EditorGUIUtility.standardVerticalSpacing;

        var type = (ScaleInEffectType)property.FindPropertyRelative("type").enumValueIndex;
        foreach (var f in CommonFields)
            height += FieldHeight(property, f);

        foreach (var f in GetFields(type))
            height += FieldHeight(property, f);

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var typeProp = property.FindPropertyRelative("type");
        var type = (ScaleInEffectType)typeProp.enumValueIndex;

        var headerRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        // 左侧：折叠箭头（Element N）
        var foldoutRect = new Rect(headerRect.x, headerRect.y, headerRect.width - TypeDropdownWidth, headerRect.height);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

        // 右侧：动画类型下拉框
        var enumRect = new Rect(headerRect.xMax - TypeDropdownWidth, headerRect.y, TypeDropdownWidth, headerRect.height);
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(enumRect, typeProp, GUIContent.none);
        if (EditorGUI.EndChangeCheck())
            typeProp.serializedObject.ApplyModifiedProperties();

        if (!property.isExpanded)
            return;

        EditorGUI.indentLevel++;
        float y = headerRect.yMax + EditorGUIUtility.standardVerticalSpacing;

        foreach (var f in CommonFields)
            y = DrawField(position, property, f, y);

        foreach (var f in GetFields(type))
            y = DrawField(position, property, f, y);

        EditorGUI.indentLevel--;
    }

    private static float FieldHeight(SerializedProperty property, string name)
    {
        var p = property.FindPropertyRelative(name);
        if (p == null) return 0f;
        return EditorGUI.GetPropertyHeight(p, true) + EditorGUIUtility.standardVerticalSpacing;
    }

    private static float DrawField(Rect position, SerializedProperty property, string name, float y)
    {
        var p = property.FindPropertyRelative(name);
        if (p == null) return y;

        float h = EditorGUI.GetPropertyHeight(p, true);
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), p, true);
        return y + h + EditorGUIUtility.standardVerticalSpacing;
    }

    private static string[] GetFields(ScaleInEffectType type)
    {
        switch (type)
        {
            case ScaleInEffectType.Fade: return FadeFields;
            case ScaleInEffectType.Position: return PositionFields;
            default: return ScaleFields;
        }
    }
}
