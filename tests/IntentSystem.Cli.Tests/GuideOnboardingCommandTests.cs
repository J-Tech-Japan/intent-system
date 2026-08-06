using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideOnboardingCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_EmitsCanonicalSections()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide onboarding — zero-local-rules smoke path", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("## Host git repository data boundary", output, StringComparison.Ordinal);
        Assert.Contains("## Hard rules", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_EmitsStructuredSequenceAndBoundary()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        var sequence = root.GetProperty("first_call_sequence");
        Assert.True(sequence.GetArrayLength() >= 6);
        var commands = sequence.EnumerateArray().Select(e => e.GetProperty("command").GetString()).ToArray();
        Assert.Contains(commands, c => c!.StartsWith("intent-cli guide model", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli guide rules list", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli guide commands list", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli guide workflow suggest", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli intent status", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli intent next-slice --dry-run", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli interview next-question", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli automation summary", StringComparison.Ordinal));

        // Each step names its expected no-mutation behavior.
        foreach (var step in sequence.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(step.GetProperty("no_mutation").GetString()));
        }

        var boundary = root.GetProperty("host_data_boundary");
        Assert.True(boundary.GetProperty("canonical_roots").GetArrayLength() >= 4);
        Assert.True(boundary.GetProperty("boundaries").GetArrayLength() >= 3);
    }

    [Fact]
    public void Execute_FirstCallSequence_ReachesOrchestratorThreadChecklist_G540()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var sequence = document.RootElement.GetProperty("first_call_sequence");
        var steps = sequence.EnumerateArray().ToArray();

        var orchestratorStep = Assert.Single(steps, s => s.GetProperty("command").GetString()!
            .StartsWith("intent-cli guide orchestrator-thread", StringComparison.Ordinal));
        Assert.Contains("PRIMARY four-thread model", orchestratorStep.GetProperty("purpose").GetString(), StringComparison.Ordinal);
        Assert.Contains("selected session transport", orchestratorStep.GetProperty("purpose").GetString(), StringComparison.Ordinal);
        Assert.Contains("double-check", orchestratorStep.GetProperty("purpose").GetString(), StringComparison.Ordinal);

        // Reachable early in the sequence — right after `guide model`, ahead
        // of rules/commands/workflow discovery.
        var modelOrder = steps.Single(s => s.GetProperty("command").GetString()!
            .StartsWith("intent-cli guide model", StringComparison.Ordinal)).GetProperty("order").GetInt32();
        var orchestratorOrder = orchestratorStep.GetProperty("order").GetInt32();
        Assert.Equal(modelOrder + 1, orchestratorOrder);

        // guide model's own purpose now names the primary execution orchestration model too.
        var modelStep = steps.Single(s => s.GetProperty("command").GetString()!
            .StartsWith("intent-cli guide model", StringComparison.Ordinal));
        Assert.Contains("PRIMARY execution orchestration model", modelStep.GetProperty("purpose").GetString(), StringComparison.Ordinal);

        var sessionStep = steps.Single(s => s.GetProperty("command").GetString()!
            .StartsWith("intent-cli session-layer show", StringComparison.Ordinal));
        var sessionPurpose = sessionStep.GetProperty("purpose").GetString()!;
        Assert.Contains("fewer dependencies", sessionPurpose, StringComparison.Ordinal);
        Assert.Contains("supported, non-retired", sessionPurpose, StringComparison.Ordinal);
        Assert.DoesNotContain("agmsg` (PRIMARY", sessionPurpose, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FirstCallSequence_DoesNotIncludeMutatingCommands()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var commands = document.RootElement.GetProperty("first_call_sequence")
            .EnumerateArray().Select(e => e.GetProperty("command").GetString()).ToArray();
        foreach (var command in commands)
        {
            Assert.DoesNotContain("--write", command!, StringComparison.Ordinal);
            Assert.DoesNotContain("intent-cli run", command!, StringComparison.Ordinal);
            Assert.DoesNotContain("issue-publish", command!, StringComparison.Ordinal);
            Assert.DoesNotContain("pr-transition", command!, StringComparison.Ordinal);
            Assert.DoesNotContain("worker complete", command!, StringComparison.Ordinal);
            Assert.DoesNotContain("closeout pr", command!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_SmokePath_DoesNotMentionLocalSkillFilesOrRulesFolders()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();

        // gh-* skill names may appear inside the Hard Rules disclaimer that explicitly
        // says they are NOT required. Verify each occurrence appears alongside "required"
        // (i.e. inside the "No local skill files (...) required" sentence).
        AssertOnlyAppearsAsNotRequired(output, "gh-issue-to-pr");
        AssertOnlyAppearsAsNotRequired(output, "gh-fix-pr-comment");
        AssertOnlyAppearsAsNotRequired(output, "intents/rules/");
    }

    [Fact]
    public void Execute_HardRules_NameProviderAndLabelBoundaries()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var rules = document.RootElement.GetProperty("hard_rules")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(rules, rule => rule!.Contains("must not launch Codex/Claude", StringComparison.Ordinal));
        Assert.Contains(rules, rule => rule!.Contains("intent-target", StringComparison.Ordinal));
        Assert.Contains(rules, rule => rule!.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide onboarding", writer.ToString(), StringComparison.Ordinal);
    }

    private static void AssertOnlyAppearsAsNotRequired(string content, string needle)
    {
        var index = 0;
        while ((index = content.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            var windowStart = Math.Max(0, index - 80);
            var windowLength = Math.Min(content.Length - windowStart, 200);
            var window = content.Substring(windowStart, windowLength);
            Assert.True(
                window.Contains("required", StringComparison.Ordinal)
                    || window.Contains("No ", StringComparison.Ordinal)
                    || window.Contains("not required", StringComparison.Ordinal),
                $"Unexpected '{needle}' occurrence outside the not-required disclaimer: '{window}'");
            index += needle.Length;
        }
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
