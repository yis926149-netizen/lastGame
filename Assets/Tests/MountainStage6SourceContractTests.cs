using System.IO;
using NUnit.Framework;

/// <summary>
/// 【程序化山脉】阶段 6.9：阶段 6 调用方收口源码契约测试。
/// 防止 6.4~6.8 的调用方收口被后续重构回滚：
///  - 6.4 HexHighlightRenderer 门禁（玩家通道过滤有效山格，DebugDirtyChunk 豁免）
///  - 6.5 PlayerInputHandler 放置预览委托 ICardDropHandler.CanDeployTo（统一部署资格）；UIController 攻击范围过滤山格；
///    调试工具走显式诊断豁免入口
///  - 6.8 CostLabelRenderer 山格不显示探索费用标签
/// </summary>
public class MountainStage6SourceContractTests
{
    private const string ScriptsRoot = "Assets/Scripts";

    private static string ReadScript(string relativePath)
    {
        return File.ReadAllText(Path.Combine(ScriptsRoot, relativePath));
    }

    [Test]
    public void HexHighlightRenderer_GateBlocksPlayerChannelsAndExemptsDebugChannel()
    {
        string renderer = ReadScript("Managers/HexHighlightRenderer.cs");

        StringAssert.Contains("IsBlockedByMountainGate", renderer);
        StringAssert.Contains("IsEffectiveMountainCell", renderer);
        StringAssert.Contains("HexHighlightChannel.DebugDirtyChunk", renderer);
        StringAssert.Contains("SetHighlightedCellsDiagnostic", renderer,
            "显式诊断豁免入口必须存在（阶段 6.4）");
    }

    [Test]
    public void PlayerInputHandler_CardPlacementDelegatesToDropHandler()
    {
        string handler = ReadScript("Core/Services/PlayerInputHandler.cs");

        StringAssert.Contains("CanDeployTo", handler,
            "拖牌放置预览必须委托 ICardDropHandler.CanDeployTo（统一部署资格，阶段 6.5）");
    }

    [Test]
    public void UIController_AttackRangeHighlightsFilterMountainCells()
    {
        string ui = ReadScript("UI/UIController.cs");

        StringAssert.Contains("MountainCellRule.IsEffectiveMountainCell(cell)", ui,
            "攻击范围/可达高亮集合必须过滤有效山格（阶段 6.5）");
    }

    [Test]
    public void MapHeightEditTestController_UsesDiagnosticExemptionEntry()
    {
        string controller = ReadScript("Managers/MapHeightEditTestController.cs");

        StringAssert.Contains("SetHighlightedCellsDiagnostic", controller,
            "调试高度编辑工具必须走显式诊断豁免（阶段 6.4）");
        StringAssert.DoesNotContain("SetHighlightedCells(HexHighlightChannel.Selection", controller,
            "调试工具不得静默使用玩家门禁通道");
    }

    [Test]
    public void CostLabelRenderer_FiltersEffectiveMountainCells()
    {
        string renderer = ReadScript("Managers/CostLabelRenderer.cs");

        StringAssert.Contains("MountainCellRule.IsEffectiveMountainCell(cell)", renderer,
            "山格不得显示探索费用标签/可交互 marker（决策 ⑩，阶段 6.8）");
    }

    [Test]
    public void TemporaryVisibilityService_ComposesMountainVisibilityRule()
    {
        string service = ReadScript("Core/Services/Visibility/TemporaryVisibilityService.cs");

        StringAssert.Contains("MountainVisibilityRule.IsPermanentlyVisible(cell)", service,
            "可见性单一合并点必须保持（阶段 6.2）");
    }
}
