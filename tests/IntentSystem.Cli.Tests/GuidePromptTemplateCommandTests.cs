using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuidePromptTemplateCommandTests
{
    [Fact]
    public void Execute_NoArgs_MarkdownListsCatalogAndExecutionConditionRule()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptTemplateCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide prompt-template catalog", output, StringComparison.Ordinal);
        Assert.Contains("implementation-loop", output, StringComparison.Ordinal);
        Assert.Contains("review-next-slice-loop", output, StringComparison.Ordinal);
        Assert.Contains("Detailed fixed conditions must be obtained from `intent-cli` during execution", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonNoKind_ReturnsSignalTemplates()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptTemplateCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var kinds = document.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("kind").GetString())
            .ToArray();

        Assert.Contains("worker-signal", kinds);
        Assert.Contains("review-signals", kinds);
    }

    [Fact]
    public void Execute_ImplementationLoopJson_RendersProvidedValues()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptTemplateCommand.Execute(
            CreateContext(),
            [
                "--kind", "implementation-loop",
                "--domain", "intent-cli",
                "--agent", "Claude",
                "--frequency", "5m",
                "--cwd", "/repo",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("implementation-loop", root.GetProperty("kind").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("domain `intent-cli`", prompt, StringComparison.Ordinal);
        Assert.Contains("using `Claude` every `5m`", prompt, StringComparison.Ordinal);
        Assert.Contains("cwd is `/repo`", prompt, StringComparison.Ordinal);
        Assert.Contains("target repo is `J-Tech-Japan/intent-system`", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<domain>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReviewLoopJson_UsesWorkflowTaskGuideCommand()
    {
        using var writer = new StringWriter();
        GuidePromptTemplateCommand.Execute(
            CreateContext(),
            ["--kind", "review-next-slice-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains(
            "guide workflow task review-next-slice-loop",
            document.RootElement.GetProperty("detailed_guide_command").GetString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("implementation-loop")]
    [InlineData("review-next-slice-loop")]
    [InlineData("implementation-oneshot")]
    [InlineData("review-next-slice-oneshot")]
    [InlineData("init-interview")]
    [InlineData("packet-create")]
    [InlineData("issue-publish")]
    [InlineData("worker-signal")]
    [InlineData("review-signals")]
    public void Execute_EachKind_HasPlaceholdersAndIntentCliGuardrail(string kind)
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptTemplateCommand.Execute(
            CreateContext(),
            ["--kind", kind, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.NotEmpty(root.GetProperty("placeholders").EnumerateArray());
        Assert.Contains("intent-cli", root.GetProperty("prompt").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            "detailed fixed conditions must be obtained from intent-cli",
            string.Join("\n", root.GetProperty("guardrails").EnumerateArray().Select(e => e.GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Help_NamesExecutionTimeDetailBoundary()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptTemplateCommand.Execute(CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Detailed fixed conditions must be obtained from intent-cli during execution", writer.ToString(), StringComparison.Ordinal);
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
