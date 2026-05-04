using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideCollaborateCommandTests
{
    [Fact]
    public void Execute_FeatureIntakeMarkdown_EmitsBoundariesAndCommandSequence()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext("intent-cli"),
            ["--kind", "feature-intake", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Collaborate — feature-intake — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("## Responsibility boundaries", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guides and records", output, StringComparison.Ordinal);
        Assert.Contains("AI agent interviews and summarizes", output, StringComparison.Ordinal);
        Assert.Contains("Operator decides", output, StringComparison.Ordinal);
        Assert.Contains("## Suggested command sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent status --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent search --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent next-slice --dry-run --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli packet draft", output, StringComparison.Ordinal);
        Assert.Contains("## Interview rules", output, StringComparison.Ordinal);
        Assert.Contains("## Draft handoff rules", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FeatureIntakeJson_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext("intent-cli"),
            ["--kind", "feature-intake", "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("sekiban-as-a-service", root.GetProperty("domain").GetString());
        Assert.Equal("feature-intake", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("responsibility_boundaries").GetArrayLength() >= 4);
        Assert.True(root.GetProperty("suggested_command_sequence").GetArrayLength() >= 4);
        Assert.True(root.GetProperty("interview_rules").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("draft_handoff_rules").GetArrayLength() >= 3);
    }

    [Fact]
    public void Execute_DefaultDomainComesFromContext()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext("custom-domain"),
            ["--kind", "feature-intake", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("# Collaborate — feature-intake — custom-domain", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext("intent-cli"),
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext("intent-cli"),
            ["--kind", "post-launch-retrospective"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --kind 'post-launch-retrospective'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext("intent-cli"),
            ["--kind", "feature-intake", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext("intent-cli"),
            ["--kind", "feature-intake", "--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext("intent-cli"),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide collaborate", writer.ToString(), StringComparison.Ordinal);
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
