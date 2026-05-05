using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideAutomationSetupCommandTests
{
    [Fact]
    public void Execute_ChildImplementMarkdown_EmitsPasteReadyPromptAndForbiddenSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide automation setup — child-implement", output, StringComparison.Ordinal);
        Assert.Contains("repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide model --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide onboarding --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide commands list --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation summary --format json", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", output, StringComparison.Ordinal);
        Assert.Contains("local skill files", output, StringComparison.Ordinal);
        Assert.Contains("## Label ownership", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation", output, StringComparison.Ordinal);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
        Assert.Contains("Set up the child implementation loop for `J-Tech-Japan/intent-system`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementJson_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("child-implement", root.GetProperty("kind").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("repo").GetString());
        Assert.Equal(4, root.GetProperty("first_calls").GetArrayLength());
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 3);
        Assert.Contains("Set up the child implementation loop", root.GetProperty("prompt").GetString()!, StringComparison.Ordinal);
        Assert.Contains("Manual `gh ... edit", root.GetProperty("label_ownership").GetString()!, StringComparison.Ordinal);
        Assert.Contains("worktree", root.GetProperty("worktree_friendly").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementPrompt_BansAdvancedRuntimeAndManualLabelMutation()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not call `intent-cli run`", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `dotnet run`", prompt, StringComparison.Ordinal);
        Assert.Contains("No manual `gh ... edit", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-target", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-pr-created", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceMarkdown_NamesEveryFirstCallAndStageStep()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide automation setup — host-review-next-slice", output, StringComparison.Ordinal);
        Assert.Contains("domain: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("target repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent status --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent next-slice --dry-run --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation pr-transition", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli closeout pr", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli packet draft", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli issue publish-flow", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_FirstCallsContainHostSpecificCommands()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent status --domain intent-cli", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent next-slice --dry-run --domain intent-cli", StringComparison.Ordinal));
        Assert.Equal(6, firstCalls.Length);
    }

    [Fact]
    public void Execute_MissingKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-bootstrap"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind must be 'child-implement' or 'host-review-next-slice'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementMissingRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required for --kind child-implement.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceMissingDomain_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain is required for --kind host-review-next-slice.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceMissingTargetRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--target-repo is required for --kind host-review-next-slice.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide automation setup", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_GuideAutomationSetupRoutedThroughGuideAutomation_Works()
    {
        // `intent-cli guide automation setup ...` flows through GuideAutomationCommand,
        // which delegates to GuideAutomationSetupCommand for the leading `setup` token.
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["setup", "--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("child-implement", document.RootElement.GetProperty("kind").GetString());
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
