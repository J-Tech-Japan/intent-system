using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeExecutionArtifactMarkdownTests
{
    [Fact]
    public void Deserialize_GivenRenderedMarkdown_ReturnsExecutionRequest()
    {
        var markdown =
            """
            # Intake Execution Draft

            ## Domain

            `auth`

            ## Proposed Execution Units

            ### `AUTH-01`

            source_file_path: intents/intent-cli/concepts/oauth2.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Source file path: intents/intent-cli/concepts/oauth2.md
            verification_hints:
            - dotnet test IntentSystem.sln

            ### `AUTH-02`

            source_file_path: intents/intent-cli/execution/03-readiness-and-verification.md
            target_part: execution/readiness
            dependencies:
            - AUTH-01
            readiness_notes:
            - Source file path: intents/intent-cli/execution/03-readiness-and-verification.md
            verification_hints:
            - Review readiness notes.
            """;

        var result = IntakeExecutionArtifactMarkdown.Deserialize(markdown);

        Assert.Equal("auth", result.Domain);
        Assert.Equal(2, result.ProposedExecutionUnits.Count);
        Assert.Equal("AUTH-01", result.ProposedExecutionUnits[0].ExecutionUnitId);
        Assert.Empty(result.ProposedExecutionUnits[0].Dependencies);
        Assert.Equal("AUTH-02", result.ProposedExecutionUnits[1].ExecutionUnitId);
        Assert.Equal(["AUTH-01"], result.ProposedExecutionUnits[1].Dependencies);
        Assert.Equal("execution/readiness", result.ProposedExecutionUnits[1].TargetPart);
    }

    [Fact]
    public void Deserialize_GivenMissingDomain_ThrowsInvalidOperationException()
    {
        var markdown =
            """
            # Intake Execution Draft

            ## Proposed Execution Units
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => IntakeExecutionArtifactMarkdown.Deserialize(markdown));

        Assert.Contains("must contain a domain", exception.Message, StringComparison.Ordinal);
    }
}
