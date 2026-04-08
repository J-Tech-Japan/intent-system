using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugReportRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugReportRenderer.WriteSummary(
            writer,
            new BugReportArtifact
            {
                DomainSlug = "auth",
                BugId = "BUG-123",
                Title = "OAuth callback loop",
                ReportSource = "from-file",
                ProblemStatement = "Observed callback loop after login.",
                SuspectedFailureLocus = "auth/callback handler state transition after provider return",
                OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
                AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
                AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
                ClarificationCandidates = ["Should provider retry reuse callback state token?"],
                LinkedExecutionUnits = ["G25"],
                LinkedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/178"],
                LinkedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180"],
                LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"]
            },
            ".intent-cli/bugs/BUG-123.report.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug report artifact generated for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Bug ID: BUG-123", output, StringComparison.Ordinal);
        Assert.Contains("Title: OAuth callback loop", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.report.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Suspected failure locus: auth/callback handler state transition after provider return", output, StringComparison.Ordinal);
        Assert.Contains("Original instruction refs: 1", output, StringComparison.Ordinal);
        Assert.Contains("Affected intent refs: 1", output, StringComparison.Ordinal);
        Assert.Contains("Affected rule/spec refs: 1", output, StringComparison.Ordinal);
        Assert.Contains("Clarification candidates: 1", output, StringComparison.Ordinal);
        Assert.Contains("Linked execution units: 1", output, StringComparison.Ordinal);
        Assert.Contains("Linked issue refs: 1", output, StringComparison.Ordinal);
        Assert.Contains("Linked PR refs: 1", output, StringComparison.Ordinal);
        Assert.Contains("Linked review refs: 1", output, StringComparison.Ordinal);
    }
}
