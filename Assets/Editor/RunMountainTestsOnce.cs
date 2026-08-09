using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 【临时】手动触发：运行程序化山脉 EditMode 测试套件并把结果写入 Temp/mountain_test_results.txt。
/// 用于验证 2026-08-06 山形修复（角点规则：max→均值→脊线连续修订 + 平坦区转向权重）。
/// 验证后删除本文件。
/// </summary>
public static class RunMountainTestsOnce
{
    [MenuItem("Tools/程序化山脉/运行山脉测试（EditMode，结果写 Temp）")]
    private static void Run()
    {
        RunSuite(false);
    }

    /// <summary>批处理入口：-executeMethod RunMountainTestsOnce.ExecuteForBatch（结束自动退出）。</summary>
    public static void ExecuteForBatch()
    {
        RunSuite(true);
    }

    private static void RunSuite(bool exitOnFinish)
    {
        if (File.Exists("Temp/mountain_test_results.txt")) File.Delete("Temp/mountain_test_results.txt");
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        var filter = new Filter
        {
            testMode = TestMode.EditMode,
            testNames = new[]
            {
                "MountainGeometryTests", "RidgeGeneratorTests", "MountainCellRuleTests",
                "MountainTopologyTests", "MountainTopologyRouteTests", "MountainMaterialContractTests",
                "MountainVisibilityRuleTests", "MountainVisibilityResolverTests", "MountainHighlightGateTests",
                "MountainStage6SourceContractTests", "MountainStage7SourceContractTests", "MapMutationMountainTests",
                "MountainStage7VisualContractTests", "MountainStage7PerformanceContractTests"
            }
        };
        api.RegisterCallbacks(new Callbacks(exitOnFinish));
        api.Execute(new ExecutionSettings(filter));
    }

    private sealed class Callbacks : ICallbacks
    {
        private readonly bool _exitOnFinish;

        public Callbacks(bool exitOnFinish)
        {
            _exitOnFinish = exitOnFinish;
        }

        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Overall: {result.ResultState}");
            sb.AppendLine($"Passed: {result.PassCount}, Failed: {result.FailCount}, Skipped: {result.SkipCount}, Inconclusive: {result.InconclusiveCount}");
            Dump(result, sb);
            File.WriteAllText("Temp/mountain_test_results.txt", sb.ToString());
            Debug.Log($"[RunMountainTestsOnce] {result.ResultState} P{result.PassCount} F{result.FailCount} S{result.SkipCount}");
            if (_exitOnFinish) EditorApplication.Exit(result.FailCount > 0 ? 2 : 0);
        }

        private static void Dump(ITestResultAdaptor node, System.Text.StringBuilder sb)
        {
            if (node.Test != null && !node.Test.IsSuite && node.ResultState != "Passed")
            {
                sb.AppendLine($"[{node.ResultState}] {node.Test.FullName}");
                if (!string.IsNullOrEmpty(node.Message))
                    sb.AppendLine("    " + node.Message.Replace("\n", "\n    "));
            }
            if (node.Children == null) return;
            foreach (ITestResultAdaptor child in node.Children) Dump(child, sb);
        }

        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
    }
}
