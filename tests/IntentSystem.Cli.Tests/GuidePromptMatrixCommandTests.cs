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

    // ── G279: parameterized rendering tests ──────────────────────────────

    [Fact]
    public void Execute_ChildLoopWithDomainAndAgentClaude_RendersConcretePromptWithoutPlaceholdersAndMentionsLoop()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("claude", root.GetProperty("agent").GetString());
        Assert.Equal("5m", root.GetProperty("frequency").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.DoesNotContain("<DOMAIN>", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-system", prompt, StringComparison.Ordinal);
        Assert.Contains("/loop 5m", prompt, StringComparison.Ordinal);
        Assert.Contains("same-thread", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopWithRepoAndAgentCodex_RendersConcretePromptWithoutPlaceholdersAndMentionsCurrentThread()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--frequency", "20m", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("codex", root.GetProperty("agent").GetString());
        Assert.Equal("20m", root.GetProperty("frequency").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.DoesNotContain("<DOMAIN>", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<TARGET-REPO>", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-system", prompt, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/intent-system", prompt, StringComparison.Ordinal);
        Assert.Contains("Codex current-thread", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopOmittedFrequency_RendersAskBeforeScheduleInsteadOfDefaultInterval()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("claude", root.GetProperty("agent").GetString());
        Assert.False(root.TryGetProperty("frequency", out _));
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("ask the operator for the desired frequency", prompt, StringComparison.Ordinal);
        Assert.Contains("Never guess", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/loop 5m", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Frequency: ", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopWithConcreteParams_NamesNextSlicePreApprovalForOnePublishPerWake()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("pre-approval to publish exactly one next-slice issue per wake", prompt, StringComparison.Ordinal);
        Assert.Contains("issue-cut-ready", prompt, StringComparison.Ordinal);
        Assert.Contains("WIP empty", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostOneshot_StillRequiresExplicitOperatorAcceptance()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("With operator acceptance", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("pre-approval to publish exactly one next-slice issue per wake", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_MentionsPostCloseoutFreshStateReload_G289()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Post-closeout fresh-state reload (G289)", prompt, StringComparison.Ordinal);
        Assert.Contains("intent next-slice --dry-run", prompt, StringComparison.Ordinal);
        Assert.Contains("authoritative", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_MentionsOperatorApprovedWipCapOverride_G288()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("--allow-wip-cap-override", prompt, StringComparison.Ordinal);
        Assert.Contains("wip-cap-overridden", prompt, StringComparison.Ordinal);
        Assert.Contains("Operator-approved queue warming (G288)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_RecommendsReviewReleaseOnHostMetadataBlocker_G292()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("review-release", prompt, StringComparison.Ordinal);
        Assert.Contains("Release the review lease on host-metadata blockers (G292)", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-pr-reviewing", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_GatesPrCommentsOnHostMetadataClassification_G287()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // The host-metadata gating block names both classifications and the
        // host-side recovery command path.
        Assert.Contains("host-metadata-blocked", prompt, StringComparison.Ordinal);
        Assert.Contains("implementation-review-finding", prompt, StringComparison.Ordinal);
        Assert.Contains("blocker_classification", prompt, StringComparison.Ordinal);
        Assert.Contains("do NOT post a PR comment", prompt, StringComparison.Ordinal);
        Assert.Contains("automation reconcile", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostOneshotPrompt_GatesPrCommentsOnHostMetadataClassification_G287()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("host-metadata-blocked", prompt, StringComparison.Ordinal);
        Assert.Contains("implementation-review-finding", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_MentionsConvergenceClassifications_G286()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("issue-publish-ready", prompt, StringComparison.Ordinal);
        Assert.Contains("unsafe-metadata", prompt, StringComparison.Ordinal);
        Assert.Contains("repaired-and-retry", prompt, StringComparison.Ordinal);
        Assert.Contains("--stale-clarification-metadata", prompt, StringComparison.Ordinal);
        Assert.Contains("--reconcile-unsafe-stop", prompt, StringComparison.Ordinal);
        Assert.Contains("--reconcile-repairs-available", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_MentionsStaleClarificationMetadataAsNonStop_G285()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("stale-clarification-metadata", prompt, StringComparison.Ordinal);
        Assert.Contains("Stale clarification metadata (G285)", prompt, StringComparison.Ordinal);
        // The warning is explicitly classified as non-stop / not Hard Clarification.
        Assert.Contains("do NOT treat it as Hard Clarification", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostOneshotPrompt_MentionsStaleClarificationMetadataAsNonStop_G285()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("stale-clarification-metadata", prompt, StringComparison.Ordinal);
        Assert.Contains("Stale clarification metadata (G285)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopWithFrequencyAndAgentClaude_PromptNamesStaleCliAbort()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("stale", prompt, StringComparison.Ordinal);
        Assert.Contains("automation doctor", prompt, StringComparison.Ordinal);
        Assert.Contains("abort the wake before any mutation", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bananas")]
    [InlineData("5")]
    [InlineData("m")]
    [InlineData("0m")]
    [InlineData("5min")]
    [InlineData("5d")]
    [InlineData("-5m")]
    [InlineData("5 m")]
    public void Execute_InvalidFrequencyValue_ReturnsUsageError(string invalidFrequency)
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--frequency", invalidFrequency, "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("--frequency must match <NNm|NNh>", output, StringComparison.Ordinal);
        Assert.Contains($"Got '{invalidFrequency}'", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("5m")]
    [InlineData("20m")]
    [InlineData("1h")]
    [InlineData("12h")]
    public void Execute_ValidFrequencyValue_IsAccepted(string validFrequency)
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--frequency", validFrequency, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(validFrequency, document.RootElement.GetProperty("frequency").GetString());
    }

    [Fact]
    public void Execute_InvalidAgentValue_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "gemini", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--agent must be 'claude', 'codex', or 'generic'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopFrequencyResolved_FrequencyGuidanceFieldReflectsResolution()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var freqGuidance = document.RootElement.GetProperty("frequency_guidance").GetString();
        Assert.Equal("5m (operator-resolved)", freqGuidance);
    }

    [Fact]
    public void Execute_ChildLoopAgentGenericDefault_OmitsAgentSpecificSchedulingPrimitive()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("generic", root.GetProperty("agent").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.DoesNotContain("/loop 5m", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Codex current-thread", prompt, StringComparison.Ordinal);
        Assert.Contains("local same-thread/current-thread automation", prompt, StringComparison.Ordinal);
    }

    // ── G294 base branch policy ─────────────────────────────────────────

    [Fact]
    public void Execute_DefaultBaseBranchPolicy_IsDirectMain()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("direct-main", root.GetProperty("base_branch_policy").GetString());
        Assert.Equal("main", root.GetProperty("expected_base_branch").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("Base branch policy: `direct-main`", prompt, StringComparison.Ordinal);
        Assert.Contains("expected base branch: `main`", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopWithMainAiPolicy_RendersMainAiBaseBranch()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--base-branch-policy", "main-ai", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("main-ai", root.GetProperty("base_branch_policy").GetString());
        Assert.Equal("main-ai", root.GetProperty("expected_base_branch").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("Base branch policy: `main-ai`", prompt, StringComparison.Ordinal);
        Assert.Contains("Child PRs target `main-ai`", prompt, StringComparison.Ordinal);
        Assert.Contains("automation base-branch-check", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopWithMainAiPolicy_SurfacesPolicyInPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--base-branch-policy", "main-ai", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("main-ai", root.GetProperty("base_branch_policy").GetString());
        Assert.Equal("main-ai", root.GetProperty("expected_base_branch").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("Closeout / merge expectation honors", prompt, StringComparison.Ordinal);
        Assert.Contains("`main-ai → main` batch PR", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsUnknownBaseBranchPolicy()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--base-branch-policy", "trunk"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--base-branch-policy must be 'direct-main' or 'main-ai'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AllFourModes_CarryBaseBranchPolicy_WhenSet()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--base-branch-policy", "main-ai", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            Assert.Equal("main-ai", entry.GetProperty("base_branch_policy").GetString());
            Assert.Equal("main-ai", entry.GetProperty("expected_base_branch").GetString());
        }
    }

    // ── G296 PR draft state ─────────────────────────────────────────────

    [Fact]
    public void Execute_ChildLoop_TellsAgentToCreateNonDraftPrsByDefault()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("**PR draft state (G296)**", prompt, StringComparison.Ordinal);
        Assert.Contains("ready-for-review", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT pass `--draft`", prompt, StringComparison.Ordinal);
        Assert.Contains("worker result-summary --pr-draft", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildOneshot_TellsAgentToCreateNonDraftPrsByDefault()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("**PR draft state (G296)**", prompt, StringComparison.Ordinal);
        Assert.Contains("ready-for-review", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT pass `--draft`", prompt, StringComparison.Ordinal);
    }

    // ── G297 draft-aware host approval ──────────────────────────────────

    [Fact]
    public void Execute_HostLoop_TellsAgentToCheckDraftBeforeApproval_AndPassMergedToCloseout()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Draft-aware approval (G297)", prompt, StringComparison.Ordinal);
        Assert.Contains("isDraft", prompt, StringComparison.Ordinal);
        Assert.Contains("draft-merge-blocked", prompt, StringComparison.Ordinal);
        Assert.Contains("review-release", prompt, StringComparison.Ordinal);
        Assert.Contains("--pr-merged $IS_MERGED", prompt, StringComparison.Ordinal);
        Assert.Contains("Stage 2 (next-slice publish) is gated on `closeout pr --write` succeeding", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostOneshot_TellsAgentToCheckDraftBeforeApproval()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Draft-aware approval (G297)", prompt, StringComparison.Ordinal);
        Assert.Contains("isDraft", prompt, StringComparison.Ordinal);
        Assert.Contains("--pr-merged $IS_MERGED", prompt, StringComparison.Ordinal);
    }

    // ── G300 child cwd is GitHub-contract-only ──────────────────────────

    [Fact]
    public void Execute_ChildLoop_StatesImplementationRepoMustNotContainIntentCli()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("**Child cwd is GitHub-contract-only (G300)**", prompt, StringComparison.Ordinal);
        Assert.Contains("MUST NOT", prompt, StringComparison.Ordinal);
        Assert.Contains("queue-state", prompt, StringComparison.Ordinal);
        // G300 must not couple the child loop to host-only `automation
        // reconcile` — the child prompt only states that reconciliation is
        // host-owned, never naming the command.
        Assert.DoesNotContain("automation reconcile", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildOneshot_StatesImplementationRepoMustNotContainIntentCli()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("**Child cwd is GitHub-contract-only (G300)**", prompt, StringComparison.Ordinal);
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
