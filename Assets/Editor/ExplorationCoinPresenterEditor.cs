using UnityEditor;
using UnityEngine;

/// <summary>
/// ExplorationCoinPresenter 自定义 Inspector：下拉框切换表现方案，
/// 只有选中对应方案才暴露相关字段。
/// </summary>
[CustomEditor(typeof(ExplorationCoinPresenter))]
public class ExplorationCoinPresenterEditor : Editor
{
	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		SerializedProperty styleProp = serializedObject.FindProperty("_effectStyle");
		EditorGUILayout.PropertyField(styleProp, new GUIContent("特效方案"));

		var style = (ExplorationCoinPresenter.CoinRewardEffectStyle)styleProp.enumValueIndex;
		if (style == ExplorationCoinPresenter.CoinRewardEffectStyle.CoinModel)
		{
			EditorGUILayout.PropertyField(serializedObject.FindProperty("_coinPrefab"), new GUIContent("金币 Prefab"));
		}
		else
		{
			EditorGUILayout.PropertyField(serializedObject.FindProperty("_addCoinsUIPrefab"), new GUIContent("AddCoinsUI Prefab"));
		}

		EditorGUILayout.PropertyField(serializedObject.FindProperty("_initialPoolSize"), new GUIContent("对象池初始大小"));

		serializedObject.ApplyModifiedProperties();
	}
}
