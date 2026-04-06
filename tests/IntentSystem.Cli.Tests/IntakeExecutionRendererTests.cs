using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeExecutionRendererTests
{
    [Fact]
    public void RenderMarkdown_GivenExecutionRequest_RendersDeterministicArtifact()
    {
        var markdown = IntakeExecutionRenderer.RenderMarkdown(new IntakeExecutionRequest
        {
            Domain = "auth",
            ProposedExecutionUnits =
            [
                new IntakeExecutionUnitCandidate
                {
                    ExecutionUnitId = "AUTH-01",
                    SourceFilePath = "intents/intent-cli/concepts/auth-oauth2.md",
                    TargetPart = "concepts",
                    Dependencies = [],
                    ReadinessNotes =
                    [
                        "Source file path: intents/intent-cli/concepts/auth-oauth2.md",
                        "Current heading: # Auth Concept"
                    ],
                    VerificationHints =
                    [
                        "Review parent source file 'intents/intent-cli/concepts/auth-oauth2.md' for issue-ready scope.",
                        "dotnet test IntentSystem.sln"
                    ]
                }
            ]
        });

        Assert.Contains("# Intake Execution Draft", markdown, StringComparison.Ordinal);
        Assert.Contains("## Proposed Execution Units", markdown, StringComparison.Ordinal);
        Assert.Contains("### `AUTH-01`", markdown, StringComparison.Ordinal);
        Assert.Contains("source_file_path: intents/intent-cli/concepts/auth-oauth2.md", markdown, StringComparison.Ordinal);
        Assert.Contains("target_part: concepts", markdown, StringComparison.Ordinal);
        Assert.Contains("dependencies:", markdown, StringComparison.Ordinal);
        Assert.Contains("readiness_notes:", markdown, StringComparison.Ordinal);
        Assert.Contains("verification_hints:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenExecutionRequest_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakeExecutionRenderer.WriteSummary(
            writer,
            new IntakeExecutionRequest
            {
                Domain = "auth",
                ProposedExecutionUnits =
                [
                    new IntakeExecutionUnitCandidate
                    {
                        ExecutionUnitId = "AUTH-01",
                        SourceFilePath = "intents/intent-cli/concepts/auth-oauth2.md",
                        TargetPart = "concepts",
                        Dependencies = [],
                        ReadinessNotes = ["Current heading: # Auth Concept"],
                        VerificationHints = ["dotnet test IntentSystem.sln"]
                    },
                    new IntakeExecutionUnitCandidate
                    {
                        ExecutionUnitId = "AUTH-02",
                        SourceFilePath = "intents/intent-cli/intent-tree/means/auth-oauth2.md",
                        TargetPart = "intent-tree/means",
                        Dependencies = ["AUTH-01"],
                        ReadinessNotes = ["Current heading: # Auth Means"],
                        VerificationHints = ["Review parent source file"]
                    }
                ]
            },
            "/repo/.intent-cli/intake/auth.execution.md");

        var output = writer.ToString();
        Assert.Contains("Intake execution draft generated for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: /repo/.intent-cli/intake/auth.execution.md", output, StringComparison.Ordinal);
        Assert.Contains("Proposed execution units: 2", output, StringComparison.Ordinal);
        Assert.Contains("Dependencies: 1", output, StringComparison.Ordinal);
        Assert.Contains("Verification hints: 2", output, StringComparison.Ordinal);
    }
}
