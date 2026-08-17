using UnityEditor;
using UnityEngine;

namespace GameConfig.Editor
{
    public sealed class GameConfigValidationWindow : EditorWindow
    {
        [MenuItem("Tools/游戏配置/验证窗口", false, 102)]
        public static void Open()
        {
            GetWindow<GameConfigValidationWindow>("游戏配置验证");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("游戏配置导入器骨架（阶段1）", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("导入并校验", GUILayout.Height(28)))
                GameConfigMenu.ImportAndValidate();

            if (GUILayout.Button("仅校验", GUILayout.Height(28)))
                GameConfigMenu.ValidateOnly();

            EditorGUILayout.HelpBox(
                "数值唯一来源为 Config/Excel/游戏数值配置.xlsx。" +
                "先用 Tools/ConfigExporter 导出，再在此导入。",
                MessageType.Info);
        }
    }
}
