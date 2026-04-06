using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeExecutionApplyRendererTests
{
    [Fact]
    public void WriteSummary_GivenResult_WritesChangedPathsAndDependencies()
    {
        var result = new IntakeExecutionApplyResult
        {
            Domain = "auth",
            AppliedUnitCount = 2,
            ChangedFilePaths =
            [
                "intents/intent-cli/execution/03-readiness-and-verification.md",
                "intents/intent-cli/execution/05-post-mvp-sub-slices.md"
            ],
            PreservedDependencyRefs = ["AUTH-01"]
        };
        using var writer = new StringWriter();

        IntakeExecutionApplyRenderer.WriteSummary(writer, result);

        var output = writer.ToString();
        Assert.Contains("Intake execution apply completed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Applied unit count: 2", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/execution/05-post-mvp-sub-slices.md", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_WritesNoneMarkers()
    {
        var result = new IntakeExecutionApplyResult
        {
            Domain = "auth",
            AppliedUnitCount = 0,
            ChangedFilePaths = [],
            PreservedDependencyRefs = []
        };
        using var writer = new StringWriter();

        IntakeExecutionApplyRenderer.WriteSummary(writer, result);

        var output = writer.ToString();
        Assert.Contains("Applied unit count: 0", output, StringComparison.Ordinal);
        Assert.Equal(2, output.Split("- none", StringSplitOptions.None).Length - 1);
    }
}
