using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideArtifactIntakeCommandTests
{
    // ── external-issue lane ──────────────────────────────────────────────────

    [Fact]
    public void Execute_ExternalIssue_Markdown_EmitsSummaryAndMetadataRequirements()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-issue", "--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide artifact-intake — external-issue (G438)", output, StringComparison.Ordinal);
        Assert.Contains("repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("## Summary", output, StringComparison.Ordinal);
        Assert.Contains("## Metadata requirements", output, StringComparison.Ordinal);
        Assert.Contains("source_artifact", output, StringComparison.Ordinal);
        Assert.Contains("relevant_intents", output, StringComparison.Ordinal);
        Assert.Contains("expected_outcome", output, StringComparison.Ordinal);
        Assert.Contains("## Guard rails", output, StringComparison.Ordinal);
        Assert.Contains("## Suggested steps", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden actions", output, StringComparison.Ordinal);
        Assert.Contains("## Ambiguity policy", output, StringComparison.Ordinal);
        // intent-target must not be applied via comment/label alone
        Assert.Contains("intent-target", output, StringComparison.Ordinal);
        // automation issue-publish should appear in suggested steps
        Assert.Contains("automation issue-publish", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ExternalIssue_Json_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-issue", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("external-issue", root.GetProperty("lane").GetString());
        Assert.True(root.GetProperty("metadata_requirements").GetArrayLength() >= 4);
        Assert.True(root.GetProperty("guard_rails").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("suggested_steps").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("forbidden_actions").GetArrayLength() >= 3);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("ambiguity_policy").GetString()));
        Assert.False(root.GetProperty("shadow_issue_required").GetBoolean());
        Assert.False(root.GetProperty("operator_confirmation_required").GetBoolean());
    }

    [Fact]
    public void Execute_ExternalIssue_ForbiddenActions_DoNotIncludeCommentOnlyHandoff()
    {
        // G438: comment-only handoff must not be treated as imported/ready.
        using var writer = new StringWriter();
        GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-issue", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var forbiddenActions = document.RootElement
            .GetProperty("forbidden_actions")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToList();

        // Some forbidden action must mention label/comment-only behavior
        Assert.Contains(
            forbiddenActions,
            a => a.Contains("intent-target", StringComparison.OrdinalIgnoreCase)
                && (a.Contains("before", StringComparison.OrdinalIgnoreCase)
                    || a.Contains("without", StringComparison.OrdinalIgnoreCase)));
    }

    // ── external-pr-review lane ──────────────────────────────────────────────

    [Fact]
    public void Execute_ExternalPrReview_Markdown_EmitsShadowIssueSection()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-pr-review", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide artifact-intake — external-pr-review (G438)", output, StringComparison.Ordinal);
        Assert.Contains("## Shadow issue", output, StringComparison.Ordinal);
        Assert.Contains("linked_issue", output, StringComparison.Ordinal);
        Assert.Contains("review_focus", output, StringComparison.Ordinal);
        Assert.Contains("interview/clarification", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ExternalPrReview_Json_ShadowIssueRequiredIsTrue()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-pr-review", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("external-pr-review", root.GetProperty("lane").GetString());
        Assert.True(root.GetProperty("shadow_issue_required").GetBoolean());
        Assert.False(root.GetProperty("operator_confirmation_required").GetBoolean());
        Assert.True(root.GetProperty("metadata_requirements").GetArrayLength() >= 4);
    }

    [Fact]
    public void Execute_ExternalPrReview_ForbiddenActions_DoNotSkipShadowIssue()
    {
        // G438: shadow issue must not be skipped for external PRs.
        using var writer = new StringWriter();
        GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-pr-review", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var forbiddenActions = document.RootElement
            .GetProperty("forbidden_actions")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToList();

        // Some forbidden action must mention shadow issue
        Assert.Contains(
            forbiddenActions,
            a => a.Contains("shadow", StringComparison.OrdinalIgnoreCase));
    }

    // ── external-pr-adopt lane ───────────────────────────────────────────────

    [Fact]
    public void Execute_ExternalPrAdopt_Markdown_EmitsOperatorConfirmationSection()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-pr-adopt", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide artifact-intake — external-pr-adopt (G438)", output, StringComparison.Ordinal);
        Assert.Contains("## Operator confirmation", output, StringComparison.Ordinal);
        Assert.Contains("## Shadow issue", output, StringComparison.Ordinal);
        Assert.Contains("adoption_rationale", output, StringComparison.Ordinal);
        Assert.Contains("provenance", output, StringComparison.Ordinal);
        Assert.Contains("operator_confirmation", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ExternalPrAdopt_Json_OperatorConfirmationAndShadowIssueRequired()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-pr-adopt", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("external-pr-adopt", root.GetProperty("lane").GetString());
        Assert.True(root.GetProperty("shadow_issue_required").GetBoolean());
        Assert.True(root.GetProperty("operator_confirmation_required").GetBoolean());
        Assert.True(root.GetProperty("metadata_requirements").GetArrayLength() >= 6);
    }

    [Fact]
    public void Execute_ExternalPrAdopt_ForbiddenActions_DoNotAutomaticallyAdopt()
    {
        // G438: adoption must never be automatic.
        using var writer = new StringWriter();
        GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-pr-adopt", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var forbiddenActions = document.RootElement
            .GetProperty("forbidden_actions")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToList();

        Assert.Contains(
            forbiddenActions,
            a => a.Contains("automatic", StringComparison.OrdinalIgnoreCase)
                || a.Contains("automatically", StringComparison.OrdinalIgnoreCase));
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public void Execute_MissingLane_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--lane is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedLane_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "non-existent-lane"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --lane 'non-existent-lane'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-issue", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-issue", "--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide artifact-intake", output, StringComparison.Ordinal);
        Assert.Contains("G438", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RepoField_IsPreservedInJson()
    {
        using var writer = new StringWriter();
        GuideArtifactIntakeCommand.Execute(
            CreateContext("intent-cli"),
            ["--lane", "external-issue", "--repo", "my-org/my-repo", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("my-org/my-repo", document.RootElement.GetProperty("repo").GetString());
    }

    [Fact]
    public void Execute_AllLanes_DefaultFormatIsMarkdown()
    {
        foreach (var lane in new[] { "external-issue", "external-pr-review", "external-pr-adopt" })
        {
            using var writer = new StringWriter();
            var exitCode = GuideArtifactIntakeCommand.Execute(
                CreateContext("intent-cli"),
                ["--lane", lane],
                writer);

            Assert.Equal(0, exitCode);
            // Markdown output starts with #
            var output = writer.ToString();
            Assert.StartsWith("#", output.TrimStart(), StringComparison.Ordinal);
        }
    }

    private static CliContext CreateContext(string domain)
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = domain,
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
