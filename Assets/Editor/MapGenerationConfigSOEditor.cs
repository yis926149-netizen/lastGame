using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapGenerationConfigSO))]
public class MapGenerationConfigSOEditor : UnityEditor.Editor
{
    SerializedProperty heightGenerationMode;
    SerializedProperty heightPaletteMap;
    SerializedProperty heightNoiseAmplitude;
    SerializedProperty heightNoiseFrequency;

    SerializedProperty fogEdgeStyle;
    SerializedProperty fogEdgeAnimSpeed;

    void OnEnable()
    {
        heightGenerationMode = serializedObject.FindProperty("heightGenerationMode");
        heightPaletteMap   = serializedObject.FindProperty("heightPaletteMap");
        heightNoiseAmplitude  = serializedObject.FindProperty("heightNoiseAmplitude");
        heightNoiseFrequency  = serializedObject.FindProperty("heightNoiseFrequency");

        fogEdgeStyle = serializedObject.FindProperty("fogEdgeStyle");
        fogEdgeAnimSpeed = serializedObject.FindProperty("fogEdgeAnimSpeed");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        bool isPaletteMode = heightGenerationMode.enumValueIndex == (int)Enums.HeightGenerationMode.PaletteMap;
        bool isWideSmooth = fogEdgeStyle.enumValueIndex == (int)Enums.FogEdgeStyle.WideSmooth;

        SerializedProperty prop = serializedObject.GetIterator();
        prop.NextVisible(true);

        while (prop.NextVisible(false))
        {
            bool isPaletteField = prop.name == "heightPaletteMap"
                               || prop.name == "heightNoiseAmplitude"
                               || prop.name == "heightNoiseFrequency";

            if (isPaletteField && !isPaletteMode)
                continue;

            bool isWideSmoothField = prop.name == "fogEdgeAnimSpeed";
            if (isWideSmoothField && !isWideSmooth)
                continue;

            EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
