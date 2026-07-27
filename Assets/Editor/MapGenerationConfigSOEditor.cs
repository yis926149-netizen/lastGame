using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapGenerationConfigSO))]
public class MapGenerationConfigSOEditor : UnityEditor.Editor
{
    SerializedProperty heightGenerationMode;
    SerializedProperty heightPaletteMap;
    SerializedProperty heightNoiseAmplitude;
    SerializedProperty heightNoiseFrequency;

    void OnEnable()
    {
        heightGenerationMode = serializedObject.FindProperty("heightGenerationMode");
        heightPaletteMap   = serializedObject.FindProperty("heightPaletteMap");
        heightNoiseAmplitude  = serializedObject.FindProperty("heightNoiseAmplitude");
        heightNoiseFrequency  = serializedObject.FindProperty("heightNoiseFrequency");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        bool isPaletteMode = heightGenerationMode.enumValueIndex == (int)Enums.HeightGenerationMode.PaletteMap;

        SerializedProperty prop = serializedObject.GetIterator();
        prop.NextVisible(true);

        while (prop.NextVisible(false))
        {
            bool isPaletteField = prop.name == "heightPaletteMap"
                               || prop.name == "heightNoiseAmplitude"
                               || prop.name == "heightNoiseFrequency";

            if (isPaletteField && !isPaletteMode)
                continue;

            EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
