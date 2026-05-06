using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuidePromptMatrixCommandTests
{
    // ── all-modes tests ──────────────────────────────────────────────────

    [Fact]
    public void Execute_NoArgs_DefaultsToMarkdownAllFourModes()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide prompt matrix", output, StringComparison.Ordinal);
        Assert.Contains("child-loop", output, StringComparison.Ordinal);
        Assert.Contains("host-loop", output, StringComparison.Ordinal);
        Assert.Contains("child-oneshot", output, StringComparison.Ordinal);
        Assert.Contains("host-oneshot", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonNoMode_ReturnsArrayOfFourEntries()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(4, document.RootElement.GetArrayLength());
    }

    [Fact]
    public void Execute_JsonNoMode_AllEntriesHaveRequiredFields()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            Assert.True(entry.TryGetProperty("mode", out _), "mode missing");
            Assert.True(entry.TryGetProperty("kind", out _), "kind missing");
            Assert.True(entry.TryGetProperty("target", out _), "target missing");
            Assert.True(entry.TryGetProperty("frequency_guidance", out _), "frequency_guidance missing");
            Assert.True(entry.TryGetProperty("forbidden_sources", out _), "forbidden_sources missing");
            Assert.True(entry.TryGetProperty("first_calls", out _), "first_calls missing");
            Assert.True(entry.TryGetProperty("prompt", out _), "prompt missing");
        }
    }

    [Fact]
    public void Execute_JsonNoMode_AllEntriesContainForbiddenSources()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var sources = entry.GetProperty("forbidden_sources")
                .EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.Contains(sources, s => s!.Contains("intents/rules/**", StringComparison.Ordinal));
            Assert.Contains(sources, s => s!.Contains("local skill files", StringComparison.Ordinal));
            Assert.Contains(sources, s => s!.Contains("copied prompt files", StringComparison.Ordinal));
        }
    }

    // ── child-loop tests ─────────────────────────────────────────────────

    [Fact]
    public void Execute_ModeChildLoopJson_ReturnsLoopKindAndChildTarget()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("child-loop", root.GetProperty("mode").GetString());
        Assert.Equal("loop", root.GetProperty("kind").GetString());
        Assert.Equal("child", root.GetProperty("target").GetString());
    }

    [Fact]
    public void Execute_ModeChildLoopJson_RecurringFrequencyGuidanceContains5mAnd20m()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var freq = document.RootElement.GetProperty("frequency_guidance").GetString()!;
        Assert.Contains("5 minutes", freq, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", freq, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ModeChildLoopJson_FirstCallsIncludeGuideCommands()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli guide model", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli guide onboarding", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli guide commands list", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("automation summary", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_ModeChildLoopJson_PromptContainsFrequencyAskAndRecurringGuidance()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("ask the operator for the desired frequency", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5 minutes", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ModeChildLoopJson_PromptContainsForbiddenCalls()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not call `intent-cli run`", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `dotnet run`", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-target", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-pr-created", prompt, StringComparison.Ordinal);
    }

    // ── host-loop tests ──────────────────────────────────────────────────

    [Fact]
    public void Execute_ModeHostLoopJson_ReturnsLoopKindAndHostTarget()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("host-loop", root.GetProperty("mode").GetString());
        Assert.Equal("loop", root.GetProperty("kind").GetString());
        Assert.Equal("host", root.GetProperty("target").GetString());
    }

    [Fact]
    public void Execute_ModeHostLoopJson_RecurringFrequencyGuidanceContains5mAnd20m()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var freq = document.RootElement.GetProperty("frequency_guidance").GetString()!;
        Assert.Contains("5 minutes", freq, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", freq, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ModeHostLoopJson_FirstCallsIncludeSixCommands()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(6, firstCalls.Length);
        Assert.Contains(firstCalls, c => c!.Contains("intent status", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("next-slice --dry-run", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_ModeHostLoopJson_PromptContainsFrequencyAskAndRecurringGuidance()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("ask the operator for the desired frequency", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5 minutes", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── child-oneshot tests ──────────────────────────────────────────────

    [Fact]
    public void Execute_ModeChildOneshotJson_ReturnsOneshotKindAndChildTarget()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("child-oneshot", root.GetProperty("mode").GetString());
        Assert.Equal("oneshot", root.GetProperty("kind").GetString());
        Assert.Equal("child", root.GetProperty("target").GetString());
    }

    [Fact]
    public void Execute_ModeChildOneshotJson_FrequencyGuidanceIsNAForbidden()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var freq = document.RootElement.GetProperty("frequency_guidance").GetString()!;
        Assert.Contains("N/A", freq, StringComparison.Ordinal);
        Assert.Contains("forbidden", freq, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ModeChildOneshotJson_PromptContainsSchedulerProhibition()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not create or update any automation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cron", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("monitor", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recurring wakeup", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ModeChildOneshotJson_PromptContainsForbiddenCalls()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not call `intent-cli run`", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `dotnet run`", prompt, StringComparison.Ordinal);
    }

    // ── host-oneshot tests ───────────────────────────────────────────────

    [Fact]
    public void Execute_ModeHostOneshotJson_ReturnsOneshotKindAndHostTarget()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("host-oneshot", root.GetProperty("mode").GetString());
        Assert.Equal("oneshot", root.GetProperty("kind").GetString());
        Assert.Equal("host", root.GetProperty("target").GetString());
    }

    [Fact]
    public void Execute_ModeHostOneshotJson_FrequencyGuidanceIsNAForbidden()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var freq = document.RootElement.GetProperty("frequency_guidance").GetString()!;
        Assert.Contains("N/A", freq, StringComparison.Ordinal);
        Assert.Contains("forbidden", freq, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ModeHostOneshotJson_PromptContainsSchedulerProhibition()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not create or update any automation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cron", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("monitor", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recurring wakeup", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ModeHostOneshotJson_FirstCallsIncludeSixCommands()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(6, firstCalls.Length);
    }

    // ── domain / target-repo placeholder tests ───────────────────────────

    [Fact]
    public void Execute_NoDomainNoTargetRepo_UsesPlaceholders()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var hostLoop = document.RootElement.EnumerateArray()
            .First(e => e.GetProperty("mode").GetString() == "host-loop");
        var prompt = hostLoop.GetProperty("prompt").GetString()!;
        Assert.Contains("<DOMAIN>", prompt, StringComparison.Ordinal);
        Assert.Contains("<TARGET-REPO>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithDomainAndTargetRepo_UsesParsedValues()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var hostLoop = document.RootElement.EnumerateArray()
            .First(e => e.GetProperty("mode").GetString() == "host-loop");
        var prompt = hostLoop.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-cli", prompt, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/intent-system", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<DOMAIN>", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<TARGET-REPO>", prompt, StringComparison.Ordinal);
    }

    // ── markdown output tests ─────────────────────────────────────────────

    [Fact]
    public void Execute_MarkdownAllModes_ContainsAllModeHeaders()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Mode: child-loop", output, StringComparison.Ordinal);
        Assert.Contains("## Mode: host-loop", output, StringComparison.Ordinal);
        Assert.Contains("## Mode: child-oneshot", output, StringComparison.Ordinal);
        Assert.Contains("## Mode: host-oneshot", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownAllModes_ContainsForbiddenSourcesSections()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--format", "markdown"],
            writer);

        var output = writer.ToString();
        Assert.Contains("### Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", output, StringComparison.Ordinal);
        Assert.Contains("local skill files", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownSingleMode_ContainsOnlyThatModeHeader()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "markdown"],
            writer);

        var output = writer.ToString();
        Assert.Contains("## Mode: child-oneshot", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Mode: child-loop", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Mode: host-loop", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Mode: host-oneshot", output, StringComparison.Ordinal);
    }

    // ── error cases ───────────────────────────────────────────────────────

    [Fact]
    public void Execute_UnknownMode_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "unknown-mode"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--mode must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
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
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--bogus", "value"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--bogus'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide prompt-matrix", writer.ToString(), StringComparison.Ordinal);
    }

    // ── routing via CommandRouter ─────────────────────────────────────────

    [Fact]
    public void Dispatch_GuidePromptMatrixRoutedThroughCommandRouter_Works()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "prompt-matrix", "--mode", "child-loop", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("child-loop", document.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public void Dispatch_GuidePromptMatrixJsonAllModes_RoutedSuccessfully()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "prompt-matrix", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(4, document.RootElement.GetArrayLength());
    }

    // ── all entries must ban run and dotnet-run ──────────────────────────

    [Theory]
    [InlineData("child-loop")]
    [InlineData("host-loop")]
    [InlineData("child-oneshot")]
    [InlineData("host-oneshot")]
    public void Execute_AnyMode_PromptBansDotnetRunAndIntentCliRun(string modeValue)
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", modeValue, "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not run `dotnet run`", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli run", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("child-loop")]
    [InlineData("host-loop")]
    [InlineData("child-oneshot")]
    [InlineData("host-oneshot")]
    public void Execute_AnyMode_ForbiddenSourcesContainsAllThree(string modeValue)
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", modeValue, "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var sources = document.RootElement.GetProperty("forbidden_sources")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.True(sources.Length >= 3);
        Assert.Contains(sources, s => s!.Contains("intents/rules/**", StringComparison.Ordinal));
        Assert.Contains(sources, s => s!.Contains("local skill files", StringComparison.Ordinal));
        Assert.Contains(sources, s => s!.Contains("copied prompt files", StringComparison.Ordinal));
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
