using UnityEditor;
using UnityEngine;

namespace GameConfig.Editor
{
    public static class GameConfigMenu
    {
        [MenuItem("Tools/游戏配置/导入并校验", false, 100)]
        public static void ImportAndValidate()
        {
            var result = GameConfigImporter.ImportAll(GameConfigImporter.JsonPath);

            if (result.Success)
                Debug.Log("[游戏配置] " + result.Message);
            else
                Debug.LogError("[游戏配置] 导入失败:\n" + result.Message);
        }

        [MenuItem("Tools/游戏配置/仅校验", false, 101)]
        public static void ValidateOnly()
        {
            Debug.Log("[游戏配置] 校验报告:\n" + GameConfigValidator.Validate(GameConfigImporter.JsonPath));
        }
    }
}
