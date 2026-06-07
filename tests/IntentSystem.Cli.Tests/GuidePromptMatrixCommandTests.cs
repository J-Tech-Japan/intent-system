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
    public void Execute_ModeHostLoopJson_FirstCallsIncludeSevenCommands()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        // PR #829 review repair: same-repo-metadata-preflight is now
        // a first-call between automation summary and intent status,
        // so the count is 7 (was 6).
        Assert.Equal(7, firstCalls.Length);
        Assert.Contains(firstCalls, c => c!.Contains("intent status", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("next-slice --dry-run", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("automation same-repo-metadata-preflight", StringComparison.Ordinal));
        // The preflight MUST come after automation summary and
        // before intent status — the whole point of the gate is to
        // run BEFORE any queue-state read.
        var summaryIdx = Array.FindIndex(firstCalls, c => c!.Contains("automation summary", StringComparison.Ordinal));
        var preflightIdx = Array.FindIndex(firstCalls, c => c!.Contains("automation same-repo-metadata-preflight", StringComparison.Ordinal));
        var statusIdx = Array.FindIndex(firstCalls, c => c!.Contains("intent status", StringComparison.Ordinal));
        Assert.True(summaryIdx < preflightIdx, "automation summary must precede same-repo-metadata-preflight in first-calls");
        Assert.True(preflightIdx < statusIdx, "same-repo-metadata-preflight must precede intent status in first-calls");
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

    [Fact]
    public void Execute_ModeHostLoopJson_PromptContainsContinuationContract_ApprovedIsIntermediate()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // G454: the host-loop guidance MUST teach the continuation contract:
        // intent-pr-approved is intermediate, approval continues to merge +
        // closeout, and a partial stop is classified terminal/non-terminal.
        Assert.Contains("Host-loop continuation contract", prompt, StringComparison.Ordinal);
        Assert.Contains("host-loop-continuation/v1", prompt, StringComparison.Ordinal);
        Assert.Contains("G454", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-pr-approved`", prompt, StringComparison.Ordinal);
        Assert.Contains("INTERMEDIATE", prompt, StringComparison.Ordinal);
        // Approval continuation names merge verification + closeout.
        Assert.Contains("closeout pr", prompt, StringComparison.Ordinal);
        Assert.Contains("merged == true", prompt, StringComparison.Ordinal);
        // Rail recovery section present for partial-stop wakes.
        Assert.Contains("rail-recovery", prompt, StringComparison.OrdinalIgnoreCase);
        // Repair-and-retry-once for recoverable blockers.
        Assert.Contains("retry", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workspace-safe-dirty", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ModeHostLoopJson_ContinuationContract_TargetRepoSubstituted()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // The `<r>` placeholder inside the contract must be resolved to the
        // concrete target repo, never leaked as a literal token.
        Assert.Contains("closeout pr --pr <n> --repo J-Tech-Japan/intent-system", prompt, StringComparison.Ordinal);
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
    public void Execute_ModeHostOneshotJson_FirstCallsIncludeSevenCommands()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        // PR #829 review repair: same-repo-metadata-preflight is now
        // a first-call between automation summary and intent status.
        Assert.Equal(7, firstCalls.Length);
        Assert.Contains(firstCalls, c => c!.Contains("automation same-repo-metadata-preflight", StringComparison.Ordinal));
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
        Assert.Contains("Post-closeout fresh-state reload (G289, G357)", prompt, StringComparison.Ordinal);
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
    public void Execute_HostLoopPrompt_MentionsStructuredClarificationWorkflow_G310()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // The G310 rule names the structured clarification workflow.
        Assert.Contains("Structured clarification workflow (G310)", prompt, StringComparison.Ordinal);
        // It forbids free-form questions when clarification-required.
        Assert.Contains("do NOT ask the operator a free-form question", prompt, StringComparison.Ordinal);
        // It directs the agent at `clarification next --format markdown`.
        Assert.Contains("clarification next", prompt, StringComparison.Ordinal);
        Assert.Contains("--format markdown", prompt, StringComparison.Ordinal);
        // It directs the agent at `clarification answer --write` for durable recording.
        Assert.Contains("clarification answer", prompt, StringComparison.Ordinal);
        Assert.Contains("--write", prompt, StringComparison.Ordinal);
        // After the answer, the agent must re-run next-slice --dry-run to confirm progress.
        Assert.Contains("re-run `intent next-slice --dry-run`", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopPrompt_MentionsClosingReferenceMandatory_G311()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // The G311 rule must name itself, the three accepted keywords, and the gate.
        Assert.Contains("PR closing reference is mandatory (G311)", prompt, StringComparison.Ordinal);
        Assert.Contains("Closes #<issue>", prompt, StringComparison.Ordinal);
        Assert.Contains("Fixes #<issue>", prompt, StringComparison.Ordinal);
        Assert.Contains("Resolves #<issue>", prompt, StringComparison.Ordinal);
        Assert.Contains("worker complete --kind issue --outcome pr-created", prompt, StringComparison.Ordinal);
        Assert.Contains("refuses to mark complete", prompt, StringComparison.Ordinal);
        // Bare links such as `see #N` are explicitly excluded.
        Assert.Contains("not bare links", prompt, StringComparison.Ordinal);
        // The repair path must use `gh pr edit` (not raw label mutation).
        Assert.Contains("gh pr edit", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildOneshotPrompt_MentionsClosingReferenceMandatory_G311()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("PR closing reference is mandatory (G311)", prompt, StringComparison.Ordinal);
        Assert.Contains("Closes #<issue>", prompt, StringComparison.Ordinal);
        Assert.Contains("worker complete", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_AgentCodex_NamesCurrentThreadHeartbeatAndForbidsNewThread_G314()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // The G314 hard rule must appear with its name + agent-specific content.
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        Assert.Contains("current-thread", prompt, StringComparison.Ordinal);
        Assert.Contains("heartbeat", prompt, StringComparison.Ordinal);
        // The forbidden list must explicitly reject every common non-local
        // surface so Codex has no path to a separate-thread schedule.
        Assert.Contains("Do NOT spawn a new Codex thread", prompt, StringComparison.Ordinal);
        Assert.Contains("remote/cloud scheduler", prompt, StringComparison.Ordinal);
        Assert.Contains("external cron", prompt, StringComparison.Ordinal);
        Assert.Contains("out-of-process monitor", prompt, StringComparison.Ordinal);
        // The operator-supplied frequency must appear in the contract so the
        // agent is reminded which interval to wake at.
        Assert.Contains("at 5m", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_AgentClaude_NamesSameThreadLoopPrimitive_G314()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        Assert.Contains("same-thread", prompt, StringComparison.Ordinal);
        // The Claude rule names `/loop <frequency> <prompt>` as the
        // canonical wake primitive.
        Assert.Contains("/loop 5m <prompt>", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT create a new Claude Code thread", prompt, StringComparison.Ordinal);
        Assert.Contains("remote/cloud scheduler", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_StatesSchedulerInvariantAndAllows5mSequential_G444()
    {
        // G444: the host-loop prompt must state the safe scheduling invariant
        // (one active wake per host repo+domain), allow a 5m same-thread
        // sequential loop, forbid independent concurrent schedulers, and tell
        // the agent NOT to stop for scheduler-policy confirmation when the
        // invariant is satisfiable.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Scheduler invariant (G444)", prompt, StringComparison.Ordinal);
        Assert.Contains("ONE active wake per host repo + domain", prompt, StringComparison.Ordinal);
        Assert.Contains("5m same-thread sequential loop", prompt, StringComparison.Ordinal);
        Assert.Contains("two independent concurrent schedulers", prompt, StringComparison.Ordinal);
        Assert.Contains("do NOT stop to ask the operator for scheduler-policy confirmation", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopPrompt_AgentCodex_IncludesLocalSchedulingContract_G314()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        Assert.Contains("current-thread", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT spawn a new Codex thread", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopPrompt_AgentClaude_IncludesLocalSchedulingContract_G314()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        Assert.Contains("/loop 5m <prompt>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_AgentGeneric_StillIncludesContractWithoutAgentSpecificPrimitive_G314()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "generic", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        // Generic agent must still name same-thread / current-thread, must
        // still forbid remote/cloud schedules, but must NOT name `/loop` or
        // `Codex` as the primitive (those are agent-specific).
        Assert.Contains("current-thread", prompt, StringComparison.Ordinal);
        Assert.Contains("remote/cloud scheduler", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/loop 5m <prompt>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_AgentCodex_NoFrequency_StillEmitsSchedulingContract_G314()
    {
        // G314 review feedback on PR #732: the operator's typical setup
        // request omits `--frequency`; the agent calls `prompt-matrix`,
        // learns the contract, then asks the operator for the interval.
        // The unresolved-frequency branch must therefore still emit the
        // per-agent G314 contract so the agent knows BEFORE the interval
        // is resolved that it must use current-thread heartbeat / `/loop`
        // and never a remote scheduler.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        Assert.Contains("current-thread", prompt, StringComparison.Ordinal);
        Assert.Contains("heartbeat", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT spawn a new Codex thread", prompt, StringComparison.Ordinal);
        Assert.Contains("remote/cloud scheduler", prompt, StringComparison.Ordinal);
        Assert.Contains("external cron", prompt, StringComparison.Ordinal);
        Assert.Contains("out-of-process monitor", prompt, StringComparison.Ordinal);
        // Frequency is still unresolved — the IMPORTANT preamble must
        // still tell the agent to ask the operator before scheduling.
        Assert.Contains("frequency is unresolved", prompt, StringComparison.Ordinal);
        // The placeholder `<frequency>` should appear in the contract so
        // the agent has language to reuse once the operator answers.
        Assert.Contains("at <frequency>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_AgentClaude_NoFrequency_StillEmitsSchedulingContract_G314()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        Assert.Contains("same-thread", prompt, StringComparison.Ordinal);
        // Claude's wake primitive uses `/loop <frequency> <prompt>` — when
        // the operator hasn't picked an interval yet the placeholder should
        // be `<frequency>` so the agent can substitute later.
        Assert.Contains("/loop <frequency> <prompt>", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT create a new Claude Code thread", prompt, StringComparison.Ordinal);
        Assert.Contains("frequency is unresolved", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopPrompt_AgentCodex_NoFrequency_StillEmitsSchedulingContract_G314()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT spawn a new Codex thread", prompt, StringComparison.Ordinal);
        Assert.Contains("frequency is unresolved", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopPrompt_AgentClaude_NoFrequency_StillEmitsSchedulingContract_G314()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Local scheduling contract (G314)", prompt, StringComparison.Ordinal);
        Assert.Contains("/loop <frequency> <prompt>", prompt, StringComparison.Ordinal);
        Assert.Contains("frequency is unresolved", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_DocumentsDoctorBinarySourceAcceptance_G314()
    {
        // G314 acceptance: doctor `status: ok` accepts both
        // `path-global-tool` and `cwd-local-shim`. The prompt already
        // surfaces this — lock it down so a regression cannot drop it.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("path-global-tool", prompt, StringComparison.Ordinal);
        Assert.Contains("cwd-local-shim", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_OrdersPublishRecoveryBeforeReconcile_G313()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // G313 must name itself, the publish-recovery command, and the
        // ordering — publish-recovery FIRST, reconcile FALLBACK.
        Assert.Contains("Selected-PR linkage recovery (G284, G313)", prompt, StringComparison.Ordinal);
        Assert.Contains("automation publish-recovery", prompt, StringComparison.Ordinal);
        Assert.Contains("publish-recovery", prompt, StringComparison.Ordinal);
        Assert.Contains("publish.yaml", prompt, StringComparison.Ordinal);
        // `publish-recovery` must appear before `automation reconcile` in
        // the same recovery section so the agent runs publish-recovery first.
        var publishIndex = prompt.IndexOf("automation publish-recovery", StringComparison.Ordinal);
        var reconcileIndex = prompt.IndexOf("automation reconcile --lane host-review", StringComparison.Ordinal);
        Assert.True(publishIndex >= 0 && reconcileIndex >= 0,
            "expected both publish-recovery and reconcile to appear in the host-loop prompt");
        Assert.True(publishIndex < reconcileIndex,
            "G313: publish-recovery must be recommended BEFORE generic reconcile in the recovery ordering");
    }

    [Fact]
    public void Execute_HostOneshotPrompt_OrdersPublishRecoveryBeforeReconcile_G313()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Selected-PR linkage recovery (G284, G313)", prompt, StringComparison.Ordinal);
        var publishIndex = prompt.IndexOf("automation publish-recovery", StringComparison.Ordinal);
        var reconcileIndex = prompt.IndexOf("automation reconcile --lane host-review", StringComparison.Ordinal);
        Assert.True(publishIndex < reconcileIndex,
            "G313: publish-recovery must be recommended BEFORE generic reconcile in the host-oneshot recovery ordering");
    }

    [Fact]
    public void Execute_HostLoopPrompt_MentionsG315LinkedIssueRecoveryLane()
    {
        // G315: when queue-state already has linked_issue but linked_pr is
        // null, the host-loop prompt must tell agents that
        // `automation publish-recovery` now also covers this case (no
        // publish artifact required) and must order it BEFORE the generic
        // `automation reconcile` fallback.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("G315", prompt, StringComparison.Ordinal);
        Assert.Contains("Queue-state-backed linked_pr recovery (G315)", prompt, StringComparison.Ordinal);
        Assert.Contains("queue-linked-issue-closing-pr-recovery", prompt, StringComparison.Ordinal);
        Assert.Contains("closingIssuesReferences", prompt, StringComparison.Ordinal);
        // The G315 section must appear before any fallback to
        // `automation reconcile --lane host-review`, mirroring the G313
        // ordering in the recovery flow.
        var g315Index = prompt.IndexOf("Queue-state-backed linked_pr recovery (G315)", StringComparison.Ordinal);
        var reconcileIndex = prompt.LastIndexOf("automation reconcile --lane host-review", StringComparison.Ordinal);
        Assert.True(g315Index >= 0, "G315 hard rule missing from host-loop prompt");
        Assert.True(g315Index < reconcileIndex,
            "G315: queue-state-backed linked_pr recovery hard rule must appear before the final reconcile fallback reference");
    }

    [Fact]
    public void Execute_HostOneshotPrompt_MentionsG315LinkedIssueRecoveryLane()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("G315", prompt, StringComparison.Ordinal);
        Assert.Contains("Linked_pr recovery from existing linked_issue (G315)", prompt, StringComparison.Ordinal);
        Assert.Contains("queue-linked-issue-closing-pr-recovery", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_RequiresIntentAndPacketAwareReview_G316()
    {
        // G316 contract: host-loop must tell the reviewer that tests-pass
        // is necessary but not sufficient, must surface guide review's new
        // structured fields, must require packet/intent conformance
        // evidence in the approval summary, and must require classified
        // findings in request-update comments.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("G316", prompt, StringComparison.Ordinal);
        // The intent-and-packet-aware review block must name the four
        // canonical packet files and the intent-reference field.
        Assert.Contains("Intent-and-packet-aware review (G316)", prompt, StringComparison.Ordinal);
        Assert.Contains("packet.yaml", prompt, StringComparison.Ordinal);
        Assert.Contains("implementation.md", prompt, StringComparison.Ordinal);
        Assert.Contains("review-context.md", prompt, StringComparison.Ordinal);
        Assert.Contains("github-body.md", prompt, StringComparison.Ordinal);
        Assert.Contains("intent_reference_paths", prompt, StringComparison.Ordinal);
        Assert.Contains("tests_pass_is_necessary_not_sufficient", prompt, StringComparison.Ordinal);
        // Approval summary must require packet/intent evidence beyond tests.
        Assert.Contains("approval_summary_requirements", prompt, StringComparison.Ordinal);
        Assert.Contains("packet/intent evidence", prompt, StringComparison.Ordinal);
        // Request-update guidance must require finding classification.
        Assert.Contains("request_update_requirements", prompt, StringComparison.Ordinal);
        Assert.Contains("implementation-finding", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-ambiguity", prompt, StringComparison.Ordinal);
        // Tests-pass-not-sufficient must appear as a hard rule, not just
        // a stage hint.
        Assert.Contains("Tests-pass is necessary, not sufficient (G316)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostOneshotPrompt_RequiresIntentAndPacketAwareReview_G316()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("G316", prompt, StringComparison.Ordinal);
        Assert.Contains("Intent-and-packet-aware review (G316)", prompt, StringComparison.Ordinal);
        Assert.Contains("Tests-pass is necessary, not sufficient (G316)", prompt, StringComparison.Ordinal);
        Assert.Contains("packet.yaml", prompt, StringComparison.Ordinal);
        Assert.Contains("review-context.md", prompt, StringComparison.Ordinal);
        Assert.Contains("intent_reference_paths", prompt, StringComparison.Ordinal);
        Assert.Contains("approval_summary_requirements", prompt, StringComparison.Ordinal);
        Assert.Contains("request_update_requirements", prompt, StringComparison.Ordinal);
        Assert.Contains("implementation-finding", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoopPrompt_MentionsDurableStatePreflight_G312()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--frequency", "5m", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // The G312 rule must point at the durable-state preflight command
        // and name the verified-commit-ready / needs-operator-review /
        // unsafe-durable-state classifications, plus require a re-run of
        // host-sync-preflight after the auto-commit.
        Assert.Contains("automation durable-state-preflight", prompt, StringComparison.Ordinal);
        Assert.Contains("verified-commit-ready", prompt, StringComparison.Ordinal);
        Assert.Contains("needs-operator-review", prompt, StringComparison.Ordinal);
        Assert.Contains("unsafe-durable-state", prompt, StringComparison.Ordinal);
        Assert.Contains("recommended_commit_message", prompt, StringComparison.Ordinal);
        Assert.Contains("re-run `host-sync-preflight`", prompt, StringComparison.Ordinal);
        // The dirty-mixed branch keeps the unrelated portion under explicit
        // operator handling rather than mixing it into the auto-commit lane.
        Assert.Contains("dirty-mixed", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostOneshotPrompt_MentionsDurableStatePreflight_G312()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("automation durable-state-preflight", prompt, StringComparison.Ordinal);
        Assert.Contains("verified-commit-ready", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostOneshotPrompt_MentionsStructuredClarificationWorkflow_G310()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--domain", "intent-system", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Structured clarification workflow (G310)", prompt, StringComparison.Ordinal);
        Assert.Contains("do NOT ask the operator a free-form question", prompt, StringComparison.Ordinal);
        Assert.Contains("clarification next", prompt, StringComparison.Ordinal);
        Assert.Contains("clarification answer", prompt, StringComparison.Ordinal);
        Assert.Contains("re-run `intent next-slice --dry-run`", prompt, StringComparison.Ordinal);
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
        Assert.Contains("--agent must be 'claude', 'codex', 'generic', 'copilot', 'copilot-cloud', or 'copilot-local'", writer.ToString(), StringComparison.Ordinal);
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
        // G376: the host-loop draft guidance must follow the draft-aware
        // decision rather than unconditionally releasing the lease. A
        // review-ready, non-operator-intended draft is promoted and the
        // approval flow continues instead of looping in lease-release.
        Assert.Contains("draft-ready-to-promote", prompt, StringComparison.Ordinal);
        Assert.Contains("draft-request-update", prompt, StringComparison.Ordinal);
        Assert.Contains("host-review-diagnostics", prompt, StringComparison.Ordinal);
        Assert.Contains("--draft-review-ready", prompt, StringComparison.Ordinal);
        Assert.Contains("gh pr ready", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoop_DoesNotRepeatOperatorPolicyQuestion_AndRoutesVerificationGaps()
    {
        // G383: a visible/manual/runtime-gated verification AC must not make
        // the review loop re-ask the operator the same A/B/C policy question
        // every wake. The host-loop prompt routes deterministically:
        // standing-policy-approve, implementation-finding → PR feedback +
        // request-update, review-policy-gap → durable host signal recorded
        // once. The summary must state what was verified vs not run.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("Visible / manual / runtime-gated verification ACs (G383)", prompt, StringComparison.Ordinal);
        // Repeated-operator-question regression: explicitly forbids re-asking.
        Assert.Contains("do NOT ask the operator the same standing A/B/C", prompt, StringComparison.Ordinal);
        Assert.Contains("guide review-verification-policy", prompt, StringComparison.Ordinal);
        // Feedback-routing regression: impl gaps → PR feedback + request-update;
        // host-policy gaps → durable host clarification/signal recorded once.
        Assert.Contains("implementation-finding", prompt, StringComparison.Ordinal);
        Assert.Contains("review-policy-gap", prompt, StringComparison.Ordinal);
        Assert.Contains("record the host-owned policy decision ONCE", prompt, StringComparison.Ordinal);
        Assert.Contains("what was NOT run", prompt, StringComparison.Ordinal);
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
        Assert.Contains("**Child cwd is GitHub-contract-only (G300 / G330 / G333)**", prompt, StringComparison.Ordinal);
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
        Assert.Contains("**Child cwd is GitHub-contract-only (G300 / G330 / G333)**", prompt, StringComparison.Ordinal);
    }

    // ── G304 pre-wake host sync boundary ────────────────────────────────

    [Fact]
    public void Execute_HostLoop_ContainsPreWakeHostSyncRule()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("**Pre-wake host sync (G304)**", prompt, StringComparison.Ordinal);
        Assert.Contains("automation host-sync-preflight", prompt, StringComparison.Ordinal);
        Assert.Contains("classification: clean", prompt, StringComparison.Ordinal);
        Assert.Contains("classification: behind-origin", prompt, StringComparison.Ordinal);
        Assert.Contains("classification: dirty-host-durable-state", prompt, StringComparison.Ordinal);
        Assert.Contains("classification: dirty-unrelated-submodule", prompt, StringComparison.Ordinal);
        Assert.Contains("re-run host-sync-preflight before Stage 2", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostOneshot_ContainsPreWakeHostSyncRule()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("**Pre-wake host sync (G304)**", prompt, StringComparison.Ordinal);
        Assert.Contains("automation host-sync-preflight", prompt, StringComparison.Ordinal);
    }

    // ── G357 initial pull before host wake reads ─────────────────────────

    [Fact]
    public void Execute_HostLoop_G357_InitialPullAppearsBeforeHostSyncPreflight()
    {
        // G357: the host-loop prompt must instruct the agent to run
        // git pull --ff-only origin main BEFORE any intent-cli call or
        // host-sync-preflight (i.e., the pull text must appear first in
        // the loop body step 1).
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // The G357 pull text must be present.
        Assert.Contains("git pull --ff-only origin main", prompt, StringComparison.Ordinal);
        Assert.Contains("Initial pull (G357)", prompt, StringComparison.Ordinal);
        Assert.Contains("VERY FIRST action", prompt, StringComparison.Ordinal);

        // The pull text must appear BEFORE the host-sync-preflight text.
        var pullIndex = prompt.IndexOf("Initial pull (G357)", StringComparison.Ordinal);
        var preflightIndex = prompt.IndexOf("Pre-wake host sync (G304)", StringComparison.Ordinal);
        Assert.True(pullIndex >= 0, "G357 initial pull text missing from host-loop prompt");
        Assert.True(preflightIndex >= 0, "G304 pre-wake host sync text missing from host-loop prompt");
        Assert.True(pullIndex < preflightIndex,
            "G357 initial pull must appear before G304 host-sync-preflight in the loop body");
    }

    [Fact]
    public void Execute_HostOneshot_G357_InitialPullPresent()
    {
        // G357 also applies to host-oneshot (single-wake body).
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("git pull --ff-only origin main", prompt, StringComparison.Ordinal);
        Assert.Contains("Initial pull (G357)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoop_G357_SubmoduleCheckoutMismatchHandlingPresent()
    {
        // G357: the host-loop prompt must instruct the agent to handle
        // classification: submodule-checkout-mismatch via git submodule update
        // (not the safe-stash lane).
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("classification: submodule-checkout-mismatch", prompt, StringComparison.Ordinal);
        Assert.Contains("git submodule update --init <path>", prompt, StringComparison.Ordinal);
        Assert.Contains("submodule_checkout_mismatch_paths", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostLoop_G357_PostCloseoutReloadMentionsSubmoduleUpdate()
    {
        // G357: the post-closeout fresh-state reload (Stage 2) must also instruct
        // the agent to run git submodule update after the pull when the preflight
        // reports submodule_checkout_mismatch_paths.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("Post-closeout fresh-state reload (G289, G357)", prompt, StringComparison.Ordinal);
        // The post-closeout section must mention submodule update.
        var postCloseoutIdx = prompt.IndexOf("Post-closeout fresh-state reload (G289, G357)", StringComparison.Ordinal);
        var submoduleUpdateIdx = prompt.IndexOf("git submodule update --init", postCloseoutIdx, StringComparison.Ordinal);
        Assert.True(submoduleUpdateIdx >= 0,
            "Post-closeout section must mention git submodule update --init after the pull");
    }

    // ── G362 same-repo metadata branch override (PR #829 review repair) ──

    [Fact]
    public void Execute_HostLoop_G362_InitialPullDocumentsSameRepoMetadataBranchOverride()
    {
        // PR #829 review repair: in same-repo topology with a configured
        // metadata_source_branch, the host cwd is checked out on the
        // metadata branch (e.g. `main-metadata`), not on `main`. The
        // hardcoded `git pull --ff-only origin main` would fail
        // non-fast-forward or pull the wrong source. The initial-pull
        // text MUST document the substitution so operators know to use
        // `origin <metadata_source_branch>` instead, and MUST point
        // them at `automation summary` for the fields.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("Same-repo metadata branch override (G362)", prompt, StringComparison.Ordinal);
        Assert.Contains("same_repo_topology: true", prompt, StringComparison.Ordinal);
        Assert.Contains("metadata_source_branch", prompt, StringComparison.Ordinal);
        Assert.Contains("git pull --ff-only origin <metadata_source_branch>", prompt, StringComparison.Ordinal);
        // Override note must appear inside the initial-pull bullet
        // (before host-sync-preflight) so it's discoverable next to
        // the very first pull command.
        var initialPullIdx = prompt.IndexOf("Initial pull (G357)", StringComparison.Ordinal);
        var overrideIdx = prompt.IndexOf("Same-repo metadata branch override (G362)", StringComparison.Ordinal);
        var hostSyncIdx = prompt.IndexOf("Pre-wake host sync (G304)", StringComparison.Ordinal);
        Assert.True(initialPullIdx >= 0, "Initial pull marker missing");
        Assert.True(overrideIdx > initialPullIdx, "Override note must follow initial pull marker");
        Assert.True(overrideIdx < hostSyncIdx, "Override note must precede host-sync-preflight marker");
    }

    [Fact]
    public void Execute_HostOneshot_G362_InitialPullDocumentsSameRepoMetadataBranchOverride()
    {
        // PR #829 review repair, companion case: host-oneshot has the
        // same hardcoded `git pull --ff-only origin main` and the same
        // gap in same-repo topology. The one-shot body MUST also
        // document the substitution.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("Same-repo metadata branch override (G362)", prompt, StringComparison.Ordinal);
        Assert.Contains("metadata_source_branch", prompt, StringComparison.Ordinal);
        Assert.Contains("git pull --ff-only origin <metadata_source_branch>", prompt, StringComparison.Ordinal);
    }

    // ── G305 self-contained child one-shot guidance ─────────────────────

    [Fact]
    public void Execute_ChildOneshot_StatesAbsenceOfIntentCliInChildIsExpected()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Absence of `.intent-cli/` in the implementation repo is the expected steady state", prompt, StringComparison.Ordinal);
        Assert.Contains("MUST NOT by itself abort the child workflow (G305)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildOneshot_TellsAgentToTakeIssuePrNumberFromWorkerNextAction()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("**Worker selector is the source of truth (G305)**", prompt, StringComparison.Ordinal);
        Assert.Contains("NEVER from operator-supplied prompt text", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not invent issue/PR numbers from prompt memory", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildOneshot_ListsAbortConditionsForMissingCliAndAuth()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("**Abort conditions (G305)**", prompt, StringComparison.Ordinal);
        Assert.Contains("global `intent-cli` is missing from `PATH`", prompt, StringComparison.Ordinal);
        Assert.Contains("`gh auth status` fails", prompt, StringComparison.Ordinal);
        Assert.Contains("ambiguous repo / multiple matches", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-issue-in-progress", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildOneshot_IsSelfContained_ForbidsLocalRulesAndSkills()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not read `intents/rules/**`", prompt, StringComparison.Ordinal);
        Assert.Contains("local skill files", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `dotnet run` as a fallback", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not ask `intent-cli` to launch Claude/Codex", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoop_AlsoCarriesG305Rules()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("MUST NOT by itself abort the child workflow (G305)", prompt, StringComparison.Ordinal);
        Assert.Contains("**Worker selector is the source of truth (G305)**", prompt, StringComparison.Ordinal);
        Assert.Contains("**Abort conditions (G305)**", prompt, StringComparison.Ordinal);
    }

    // ── G346 persisted base branch policy ───────────────────────────────────
    [Fact]
    public void Execute_OmittedBaseBranchPolicy_UsesPersistedConfigPolicy_MainAi()
    {
        // G346: when --base-branch-policy is NOT supplied, prompt-matrix should
        // fall back to the policy stored in the host config (here: main-ai).
        var context = CreateContextWithPolicy("main-ai");
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            context,
            ["--mode", "child-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("main-ai", document.RootElement.GetProperty("base_branch_policy").GetString());
        Assert.Equal("main-ai", document.RootElement.GetProperty("expected_base_branch").GetString());
    }

    [Fact]
    public void Execute_OmittedBaseBranchPolicy_DefaultsToDirectMain_WhenConfigIsDirectMain()
    {
        // G346: when --base-branch-policy is omitted and config records direct-main
        // (the default), prompt-matrix returns direct-main + expected base = main.
        var context = CreateContext(); // default ProjectConfig has direct-main
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            context,
            ["--mode", "child-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("direct-main", document.RootElement.GetProperty("base_branch_policy").GetString());
        Assert.Equal("main", document.RootElement.GetProperty("expected_base_branch").GetString());
    }

    [Fact]
    public void Execute_ExplicitBaseBranchPolicy_OverridesConfigPolicy()
    {
        // G346: when --base-branch-policy is supplied, it wins over config even
        // when the config records main-ai.
        var context = CreateContextWithPolicy("main-ai");
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            context,
            ["--mode", "child-loop", "--base-branch-policy", "direct-main", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("direct-main", document.RootElement.GetProperty("base_branch_policy").GetString());
        Assert.Equal("main", document.RootElement.GetProperty("expected_base_branch").GetString());
    }

    [Fact]
    public void Execute_AllFourModes_UsePersistedPolicy_WhenNotOverridden()
    {
        // G346: all four mode entries carry the persisted policy when flag is absent.
        var context = CreateContextWithPolicy("main-ai");
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            Assert.Equal("main-ai", entry.GetProperty("base_branch_policy").GetString());
            Assert.Equal("main-ai", entry.GetProperty("expected_base_branch").GetString());
        }
    }

    // ── G451: host-loop guidance consumes the domain review standing policy ──

    [Fact]
    public void HostLoop_NoPolicyFile_ReviewPolicySourceIsBuiltInDefault_AndGuidanceConsumesPolicy()
    {
        var root = Directory.CreateTempSubdirectory("prompt-matrix-g451-").FullName;
        try
        {
            using var writer = new StringWriter();
            var exit = GuidePromptMatrixCommand.Execute(
                ContextAt(root),
                ["--mode", "host-loop", "--domain", "intent-cli", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(writer.ToString());
            Assert.Equal("built-in-default", doc.RootElement.GetProperty("review_policy_source").GetString());
            // The host-loop guidance instructs consuming the standing policy.
            Assert.Contains("Domain review standing policy (G451)", doc.RootElement.GetProperty("prompt").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HostLoop_WithPolicyFile_ReviewPolicySourceIsDomainFile()
    {
        var root = Directory.CreateTempSubdirectory("prompt-matrix-g451-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
            File.WriteAllText(
                Path.Combine(root, ".intent-cli", "review-policy.json"),
                """{ "draft_handling": { "rules": ["custom"] } }""");

            using var writer = new StringWriter();
            var exit = GuidePromptMatrixCommand.Execute(
                ContextAt(root),
                ["--mode", "host-loop", "--domain", "intent-cli", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(writer.ToString());
            // A present policy file CHANGES the host-loop guidance output.
            Assert.Equal("domain-file", doc.RootElement.GetProperty("review_policy_source").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ChildLoop_ReviewPolicySourceIsNull_ChildDoesNotMakeStandingPolicyDecisions()
    {
        var root = Directory.CreateTempSubdirectory("prompt-matrix-g451-").FullName;
        try
        {
            using var writer = new StringWriter();
            GuidePromptMatrixCommand.Execute(
                ContextAt(root),
                ["--mode", "child-loop", "--domain", "intent-cli", "--format", "json"],
                writer);

            using var doc = JsonDocument.Parse(writer.ToString());
            // null source is omitted from JSON (WhenWritingNull): child entries
            // carry no review_policy_source, or it is explicitly null.
            var hasSource = doc.RootElement.TryGetProperty("review_policy_source", out var source);
            Assert.True(!hasSource || source.ValueKind == JsonValueKind.Null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CliContext ContextAt(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

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

    private static CliContext CreateContextWithPolicy(string baseBranchPolicy)
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
                    WorktreeRoot = ".intent-cli/worktrees",
                    BaseBranchPolicy = baseBranchPolicy
                }
            }
        };
    }

    // ----- G348: same-repo topology tests -----

    [Fact]
    public void Execute_G348_TopologySameRepo_ChildLoopIncludesSameRepoForbiddenSources()
    {
        // G348 AC: child-loop with --topology same-repo must include
        // `.intent-cli/**` and `intents/**` in forbidden_sources.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--topology", "same-repo", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var sources = document.RootElement.GetProperty("forbidden_sources")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(sources, s => s!.Contains(".intent-cli/**", StringComparison.Ordinal));
        Assert.Contains(sources, s => s!.Contains("intents/**", StringComparison.Ordinal));
        // Standard sources must still be present.
        Assert.Contains(sources, s => s!.Contains("intents/rules/**", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G348_TopologySameRepo_ChildLoopPromptDescribesForbiddenPaths()
    {
        // G348 AC: same-repo child-loop prompt must explain that
        // .intent-cli/** and intents/** are visible but forbidden.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--topology", "same-repo", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains(".intent-cli/**", prompt, StringComparison.Ordinal);
        Assert.Contains("intents/**", prompt, StringComparison.Ordinal);
        Assert.Contains("FORBIDDEN", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G348_TopologySameRepo_ChildLoopEntryRecordsTopology()
    {
        // G348: topology field must be "same-repo" in the JSON entry.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--topology", "same-repo", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("same-repo", document.RootElement.GetProperty("topology").GetString());
    }

    [Fact]
    public void Execute_G348_TopologySameRepo_ChildOneshotAlsoIncludesSameRepoForbiddenSources()
    {
        // G348 AC: child-oneshot with --topology same-repo must also include
        // same-repo forbidden sources.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--topology", "same-repo", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var sources = document.RootElement.GetProperty("forbidden_sources")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(sources, s => s!.Contains(".intent-cli/**", StringComparison.Ordinal));
        Assert.Contains(sources, s => s!.Contains("intents/**", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G348_NoTopology_ForbiddenSourcesDoNotIncludeSameRepoPaths()
    {
        // G348: without --topology, .intent-cli/** must NOT appear in
        // forbidden_sources (it is only added for same-repo topology).
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var sources = document.RootElement.GetProperty("forbidden_sources")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.DoesNotContain(sources, s => s!.Contains(".intent-cli/**", StringComparison.Ordinal));
        // topology field must be absent/null.
        if (document.RootElement.TryGetProperty("topology", out var topProp))
        {
            Assert.Equal(JsonValueKind.Null, topProp.ValueKind);
        }
    }

    [Fact]
    public void Execute_G348_UnknownTopology_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--topology", "cross-repo", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be 'same-repo'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G348_Help_MentionsTopologyOption()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("--topology", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("same-repo", writer.ToString(), StringComparison.Ordinal);
    }

    // ----- G349: copilot-local vs copilot-cloud tests -----

    [Fact]
    public void Execute_G349_CopilotLocalHostLoop_ReturnsLocalHostAgentClassification()
    {
        // G349 AC: copilot-local host-loop must return agent_classification
        // "local_host_agent", not "unsupported_mode_agent_combination".
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--agent", "copilot-local", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("local_host_agent", root.GetProperty("agent_classification").GetString());
        Assert.Equal("host-loop", root.GetProperty("mode").GetString());
        Assert.Equal("copilot-local", root.GetProperty("agent").GetString());
    }

    [Fact]
    public void Execute_G349_CopilotLocalHostLoop_PromptMentionsIntentCliHostSurfaces()
    {
        // G349 AC: local Copilot host agent can ask intent-cli for
        // intent/clarification/packet/issue-publish workflow guidance.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--agent", "copilot-local", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-cli", prompt, StringComparison.Ordinal);
        Assert.Contains("packet draft", prompt, StringComparison.Ordinal);
        Assert.Contains("issue publish", prompt, StringComparison.Ordinal);
        Assert.Contains("clarification", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-interview", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G349_CopilotLocalHostOneshot_ReturnsLocalHostAgentClassification()
    {
        // G349 AC: host-oneshot with copilot-local returns local_host_agent.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--agent", "copilot-local", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("local_host_agent", document.RootElement.GetProperty("agent_classification").GetString());
    }

    [Fact]
    public void Execute_G349_CopilotLocalChildLoop_ReturnsLocalChildAgentHostStateFree()
    {
        // G349 AC: copilot-local in child-loop must return
        // "local_child_agent_host_state_free" — NOT local_host_agent.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot-local", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("local_child_agent_host_state_free", document.RootElement.GetProperty("agent_classification").GetString());
    }

    [Fact]
    public void Execute_G349_CopilotLocalChildOneshot_ReturnsLocalChildAgentHostStateFree()
    {
        // G349 AC: copilot-local in child-oneshot is also host-state-free.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot-local", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("local_child_agent_host_state_free", document.RootElement.GetProperty("agent_classification").GetString());
    }

    [Fact]
    public void Execute_G349_CopilotLocalChildLoop_PromptForbidsHostMetadataMutation()
    {
        // G349 AC: negative test — child cwd copilot-local guidance must
        // explicitly state .intent-cli/ and host metadata are forbidden.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot-local", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains(".intent-cli/", prompt, StringComparison.Ordinal);
        Assert.Contains("host metadata", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must NOT", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_G349_CopilotCloud_BackwardCompatAlias_BehavesLikeCopilot()
    {
        // G349: copilot-cloud is an alias for copilot — produces
        // "unsupported_local_loop" for child-loop (cloud/assignment path).
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot-cloud", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        // copilot-cloud normalises to copilot → same cloud-assignment classification.
        Assert.Equal("unsupported_local_loop", document.RootElement.GetProperty("agent_classification").GetString());
    }

    [Fact]
    public void Execute_G349_CopilotCloud_HostLoop_UnsupportedModeAgentCombination()
    {
        // G349: copilot-cloud + host-loop still returns
        // "unsupported_mode_agent_combination" (cloud Copilot never drives host loops).
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--agent", "copilot-cloud", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("unsupported_mode_agent_combination", document.RootElement.GetProperty("agent_classification").GetString());
    }

    [Fact]
    public void Execute_G349_Help_MentionsCopilotLocalAndCopilotCloud()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("copilot-local", output, StringComparison.Ordinal);
        Assert.Contains("copilot-cloud", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G349_CopilotLocalHostLoop_FirstCallsIncludeIntentCliSurfaces()
    {
        // G349: first_calls for local Copilot host mode must include
        // intent-cli automation summary (same as claude/codex host loops).
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--agent", "copilot-local", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.Contains("automation summary", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("intent status", StringComparison.Ordinal));
    }

    // ── G355: self-healing preflight policy ───────────────────────────────

    [Fact]
    public void Execute_G355_HostLoopPrompt_ContainsSelfHealingWakePolicy()
    {
        // G355 AC: Prompt-matrix host-loop text includes the self-healing
        // sequence in concise form.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // The host-loop must surface safe_repair_available as the gate.
        Assert.Contains("safe_repair_available", prompt, StringComparison.Ordinal);
        // And name the repair categories.
        Assert.Contains("issue-publish-gap", prompt, StringComparison.Ordinal);
        Assert.Contains("review-linkage-gap", prompt, StringComparison.Ordinal);
        Assert.Contains("host-artifact-repair", prompt, StringComparison.Ordinal);
        Assert.Contains("drafted-packet-mechanical-gap", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G355_HostLoopPrompt_EnforcesRetryOnceAndNoInfiniteLoop()
    {
        // G355 AC: After applying a repair, retry the same action exactly once;
        // never loop further on failure (no infinite repair loops).
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // Retry-once rule must be explicit.
        Assert.Contains("EXACTLY ONCE", prompt, StringComparison.OrdinalIgnoreCase);
        // No-infinite-loop must be explicit.
        Assert.Contains("no infinite", prompt, StringComparison.OrdinalIgnoreCase);
        // Commit-before-retry must be mentioned.
        Assert.Contains("commit and push", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_G355_HostLoopPrompt_UnsafeMetadataDoesNotRepair()
    {
        // G355 AC: When safe_repair_available is false (unsafe-metadata,
        // clarification-required), the host loop must stop with structured
        // clarification — never attempt a repair.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("safe_repair_available: false", prompt, StringComparison.Ordinal);
        Assert.Contains("unsafe-metadata", prompt, StringComparison.Ordinal);
        Assert.Contains("clarification-required", prompt, StringComparison.Ordinal);
        Assert.Contains("do NOT repair", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_G355_ChildLoopPrompt_ContainsSelfHealingGuidance()
    {
        // G355 AC: Child-loop guidance says to ask intent-cli for safe repair.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // Child-loop self-healing must reference the preflight commands.
        Assert.Contains("safe_repair_available", prompt, StringComparison.Ordinal);
        Assert.Contains("worker issue-preflight", prompt, StringComparison.Ordinal);
        Assert.Contains("worker pr-comment-preflight", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G355_ChildLoopPrompt_RefusesHostMetadataRepairsFromChildCwd()
    {
        // G355 AC: Child-loop guidance refuses host metadata repairs from
        // child cwd and routes them to host-artifact-repair-required.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // Child loop must NOT repair host metadata paths.
        Assert.Contains(".intent-cli/**", prompt, StringComparison.Ordinal);
        Assert.Contains("intents/**", prompt, StringComparison.Ordinal);
        Assert.Contains("host-artifact-repair-required", prompt, StringComparison.Ordinal);
        // And the child loop must say: return to host loop for handling.
        Assert.Contains("host loop", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_G355_HostLoopPrompt_ContainsStaleReviewLeaseCategory()
    {
        // G355 AC: Prompt-matrix host-loop text includes stale-review-lease
        // as a defined category with review-release as the repair action.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("stale-review-lease", prompt, StringComparison.Ordinal);
        Assert.Contains("review-release", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G355_HostLoopPrompt_ContainsWorkspaceSafeDirtyCategory()
    {
        // G355 AC: Prompt-matrix host-loop text includes workspace-safe-dirty
        // as a defined category with workspace-guard as the repair action.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("workspace-safe-dirty", prompt, StringComparison.Ordinal);
        Assert.Contains("workspace-guard", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G355_ChildLoopPrompt_ContainsChildSelectorLabelGapCategory()
    {
        // G355 AC: Child-loop guidance explicitly names child-selector-label-gap
        // as the child-side repair category and clarifies that host-only
        // categories must NOT be repaired by the child loop.
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("child-selector-label-gap", prompt, StringComparison.Ordinal);
        // The guidance must also enumerate the host-only categories that must
        // not be repaired by the child loop.
        Assert.Contains("stale-review-lease", prompt, StringComparison.Ordinal);
        Assert.Contains("workspace-safe-dirty", prompt, StringComparison.Ordinal);
    }

    // ── G388 implementation base branch resolution ──────────────────────────

    [Fact]
    public void Execute_G388_DomainConfigDevelopV2_EmitsDevelopV2AsExpectedBaseBranch()
    {
        // AC: domain config implementation_base_branch=develop-v2 must make the
        // guide emit develop-v2, not the direct-main/main default.
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithImplementationBaseBranch("develop-v2"),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("develop-v2", root.GetProperty("expected_base_branch").GetString());
        Assert.Equal("domain-config", root.GetProperty("implementation_base_branch_source").GetString());
        Assert.False(root.GetProperty("implementation_base_branch_is_default").GetBoolean());
        Assert.Contains("expected base branch: `develop-v2`", root.GetProperty("prompt").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G388_NoConfig_DefaultsToMain_AndStatesItIsADefault()
    {
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("main", root.GetProperty("expected_base_branch").GetString());
        Assert.Equal("policy-default", root.GetProperty("implementation_base_branch_source").GetString());
        Assert.True(root.GetProperty("implementation_base_branch_is_default").GetBoolean());
        // Must state where to configure a different branch.
        Assert.Contains("implementation_base_branch", root.GetProperty("implementation_base_branch_note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G388_ExplicitImplementationBaseOverride_WinsOverConfig()
    {
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithImplementationBaseBranch("main"),
            ["--mode", "child-loop", "--implementation-base", "develop-v2", "--allow-base-branch-override", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("develop-v2", root.GetProperty("expected_base_branch").GetString());
        Assert.Equal("explicit-argument", root.GetProperty("implementation_base_branch_source").GetString());
    }

    [Fact]
    public void Execute_G388_ExplicitConflictsWithConfig_NoOverride_FailsWithDiagnostic()
    {
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithImplementationBaseBranch("develop-v2"),
            ["--mode", "child-loop", "--implementation-base", "main", "--format", "json"],
            writer);

        Assert.NotEqual(0, exit);
        Assert.Contains("conflict", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("develop-v2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G388_DevelopV2_AllModesAgreeOnExpectedBaseBranch()
    {
        // AC: prompt-matrix entries agree on the same effective branch for the
        // same cwd/domain/repo (consistency with automation summary).
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithImplementationBaseBranch("develop-v2"),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exit);
        foreach (var entry in JsonDocument.Parse(writer.ToString()).RootElement.EnumerateArray())
        {
            Assert.Equal("develop-v2", entry.GetProperty("expected_base_branch").GetString());
        }
    }

    [Fact]
    public void Execute_G388_SameRepoTopology_ResolvesMetadataWriteBranch_WhenNoExplicitOrConfig()
    {
        // Review follow-up: the same-repo topology precedence tier must be
        // reachable at the command level — with same-repo topology active and
        // no explicit/domain-config branch, the guide resolves the same-repo
        // integration (metadata write) branch instead of defaulting to main.
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithSameRepoTopology(metadataWriteBranch: "main-metadata"),
            ["--mode", "child-loop", "--topology", "same-repo", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("main-metadata", root.GetProperty("expected_base_branch").GetString());
        Assert.Equal("same-repo-topology", root.GetProperty("implementation_base_branch_source").GetString());
        Assert.False(root.GetProperty("implementation_base_branch_is_default").GetBoolean());
        Assert.Contains("expected base branch: `main-metadata`", root.GetProperty("prompt").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G388_DomainConfig_WinsOverSameRepoTopologyBranch()
    {
        // Precedence: domain config (implementation_base_branch) outranks the
        // same-repo topology tier.
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithSameRepoTopology(metadataWriteBranch: "main-metadata", implementationBaseBranch: "develop-v2"),
            ["--mode", "child-loop", "--topology", "same-repo", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("develop-v2", root.GetProperty("expected_base_branch").GetString());
        Assert.Equal("domain-config", root.GetProperty("implementation_base_branch_source").GetString());
    }

    // ── G471 non-default base branch prose consistency ──────────────────────

    [Fact]
    public void Execute_G471_DevelopV2ChildLoop_PolicyDescriptionNeverSaysChildPrsTargetMain()
    {
        // AC: with implementation_base_branch=develop-v2 the child-loop prompt
        // must consistently say develop-v2 and must NOT say "Child PRs target
        // `main`" — the contradiction G471 removes.
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithImplementationBaseBranch("develop-v2"),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var prompt = JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Child PRs target `develop-v2` directly; host merges land on `develop-v2`.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Child PRs target `main` directly", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("host merges land on `main`", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G471_DevelopV2HostLoop_MergeExpectationUsesDevelopV2NotMain()
    {
        // AC: host-loop merge/closeout guidance validates against develop-v2 and
        // does not imply `main` for the implementation base branch.
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithImplementationBaseBranch("develop-v2"),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var prompt = JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("host merges land on `develop-v2`", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Child PRs target `main` directly", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G471_DevelopV2HostLoop_BaseBranchCheckCommandValidatesEffectiveBranch()
    {
        // Review repair AC: the generated host-loop base-branch-check COMMAND
        // (not only the prose) must validate against develop-v2. Forcing
        // `--policy direct-main` would make `automation base-branch-check`
        // ignore the configured branch and compare against `main`, falsely
        // reporting a mismatch — so the command must pass the effective branch
        // unambiguously via `--implementation-base develop-v2`.
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContextWithImplementationBaseBranch("develop-v2"),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var prompt = JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("base-branch-check --repo <r> --pr <n> --implementation-base develop-v2 --actual-base", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--policy direct-main", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G471_DefaultDirectMainHostLoop_BaseBranchCheckKeepsPolicyFlag()
    {
        // Regression: default direct-main hosts keep the canonical
        // `--policy direct-main` base-branch-check command (byte-stable).
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var prompt = JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("base-branch-check --repo <r> --pr <n> --policy direct-main --actual-base", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--implementation-base", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G471_DefaultDirectMain_KeepsCanonicalMainProse()
    {
        // Regression: existing direct-main projects (no override) keep the exact
        // canonical prose so the output is byte-stable.
        using var writer = new StringWriter();
        var exit = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        var prompt = JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Child PRs target `main` directly; host merges land on `main`.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Effective implementation / PR base branch:", prompt, StringComparison.Ordinal);
    }

    private static CliContext CreateContextWithImplementationBaseBranch(string implementationBaseBranch)
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "aic",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                    ImplementationBaseBranch = implementationBaseBranch
                }
            }
        };
    }

    private static CliContext CreateContextWithSameRepoTopology(
        string metadataWriteBranch,
        string implementationBaseBranch = "")
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "aic",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                    SameRepoTopology = true,
                    MetadataWriteBranch = metadataWriteBranch,
                    ImplementationBaseBranch = implementationBaseBranch
                }
            }
        };
    }
}
