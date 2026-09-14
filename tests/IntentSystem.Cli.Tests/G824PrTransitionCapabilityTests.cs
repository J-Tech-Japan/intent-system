using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G824: automation summary previously hand-maintained the pr-transition label
/// sets and misreported both request-update and approved. The capability and
/// prose now derive from the plans the command executes; these tests pin that
/// equality for every advertised transition.
/// </summary>
public sealed class G824PrTransitionCapabilityTests
{
    [Theory]
    [InlineData("review-start")]
    [InlineData("request-update")]
    [InlineData("approved")]
    public void Capability_LabelSets_EqualTheExecutedPlan(string transition)
    {
        var (add, remove) = AutomationPrTransitionCommand.PlannedLabels(transition);
        var capability = Assert.Single(AutomationSummaryConstants.AutomationCommandCapabilities,
            c => string.Equals(c.Transition, transition, StringComparison.Ordinal));

        Assert.Equal(add, capability.AddLabels);
        Assert.Equal(remove, capability.RemoveLabels);

        var prose = Assert.Single(AutomationSummaryConstants.HostPrTransitionCommands,
            line => line.Contains($"--transition {transition} ", StringComparison.Ordinal));
        Assert.All(add.Concat(remove), label => Assert.Contains(label, prose, StringComparison.Ordinal));
    }

    [Fact]
    public void RequestUpdate_Capability_NamesApprovedAsSuperseded()
    {
        var capability = Assert.Single(AutomationSummaryConstants.AutomationCommandCapabilities,
            c => string.Equals(c.Capability, "pr-transition.request-update", StringComparison.Ordinal));
        Assert.Contains("intent-pr-approved", capability.RemoveLabels);
    }

    [Fact]
    public void Approved_Capability_NamesEveryLabelItRemoves()
    {
        var capability = Assert.Single(AutomationSummaryConstants.AutomationCommandCapabilities,
            c => string.Equals(c.Capability, "pr-transition.approved", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "intent-pr-request-update", "intent-pr-rereview-ready", "intent-pr-reviewing", "intent-pr-update-in-progress", "rereview-ready" },
            capability.RemoveLabels.OrderBy(label => label, StringComparer.Ordinal));
    }
}
