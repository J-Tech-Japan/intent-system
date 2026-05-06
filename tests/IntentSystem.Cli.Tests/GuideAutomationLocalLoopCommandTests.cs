using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// focused-guide-automation-local-loop-tests
/// </summary>
public sealed class GuideAutomationLocalLoopCommandTests
{
    // ── claude variant ────────────────────────────────────────────────────

    [Fact]
    public void Execute_AgentClaude_Markdown_EmitsPromptWithLoopSemantics()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide automation local-loop — agent: claude", output, StringComparison.Ordinal);
        Assert.Contains("/loop 5m", output, StringComparison.Ordinal);
        Assert.Contains("/loop 20m", output, StringComparison.Ordinal);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AgentClaude_Json_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("local-loop", root.GetProperty("kind").GetString());
        Assert.Equal("claude", root.GetProperty("agent").GetString());
        Assert.False(root.GetProperty("clarification_required").GetBoolean());
        Assert.True(root.TryGetProperty("prompt", out _));
        Assert.True(root.TryGetProperty("scheduler_guidance", out _));
        Assert.True(root.TryGetProperty("frequency_policy", out _));
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 3);
    }

    [Fact]
    public void Execute_AgentClaude_Json_PromptUsesSameThreadLoopForbidsCloudScheduler()
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("/loop 5m", prompt, StringComparison.Ordinal);
        Assert.Contains("/loop 20m", prompt, StringComparison.Ordinal);
        Assert.Contains("same-thread", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cloud", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_AgentClaude_Json_PromptForbidsLocalSkillsAndRulesFiles()
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intents/rules/**", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AgentClaude_Json_PromptIncludesGuideAutomationSetupInstruction()
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-cli guide automation setup --kind child-implement", prompt, StringComparison.Ordinal);
    }

    // ── codex variant ─────────────────────────────────────────────────────

    [Fact]
    public void Execute_AgentCodex_Markdown_EmitsPromptWithHeartbeatSemantics()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "codex", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide automation local-loop — agent: codex", output, StringComparison.Ordinal);
        Assert.Contains("automation/heartbeat", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AgentCodex_Json_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "codex", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("local-loop", root.GetProperty("kind").GetString());
        Assert.Equal("codex", root.GetProperty("agent").GetString());
        Assert.False(root.GetProperty("clarification_required").GetBoolean());
    }

    [Fact]
    public void Execute_AgentCodex_Json_PromptUsesLocalHeartbeatForbidsCloudScheduler()
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "codex", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("automation/heartbeat", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current thread", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cloud", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_AgentCodex_Json_PromptIncludesGuideAutomationSetupInstruction()
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "codex", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-cli guide automation setup --kind child-implement", prompt, StringComparison.Ordinal);
    }

    // ── generic variant ───────────────────────────────────────────────────

    [Fact]
    public void Execute_AgentGeneric_Json_RequiresClarificationAndProviderNeutral()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "generic", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("generic", root.GetProperty("agent").GetString());
        Assert.True(root.GetProperty("clarification_required").GetBoolean());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("Claude Code", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_AgentGeneric_Json_PromptAsksClarificationBeforeScheduling()
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "generic", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("confirm the agent runtime", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not create any scheduler", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── unknown (omitted) agent ───────────────────────────────────────────

    [Fact]
    public void Execute_AgentOmitted_Json_RequiresClarificationAndAsksOperator()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("unknown", root.GetProperty("agent").GetString());
        Assert.True(root.GetProperty("clarification_required").GetBoolean());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("ask the operator", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("claude", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("codex", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_AgentOmitted_Json_PromptForbidsSchedulingBeforeConfirmation()
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not create any cron job, scheduler, automation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local-loop --agent", prompt, StringComparison.Ordinal);
    }

    // ── frequency policy ──────────────────────────────────────────────────

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("generic")]
    public void Execute_AllAgents_Json_FrequencyPolicyCoversHighAndLowCadence(string agentArg)
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", agentArg, "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var freqPolicy = document.RootElement.GetProperty("frequency_policy").GetString()!;
        Assert.Contains("5 minutes", freqPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", freqPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask", freqPolicy, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("generic")]
    public void Execute_AllAgents_Json_PromptForbidsIntentsRulesAndSkillFiles(string agentArg)
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", agentArg, "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intents/rules/**", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("generic")]
    public void Execute_AllAgents_Json_ForbiddenSourcesContainCloudScheduler(string agentArg)
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", agentArg, "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var sources = document.RootElement.GetProperty("forbidden_sources")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(sources, s => s!.Contains("cloud", StringComparison.OrdinalIgnoreCase));
    }

    // ── error cases ───────────────────────────────────────────────────────

    [Fact]
    public void Execute_UnsupportedAgent_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "gpt"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--agent must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--unknown-flag", "x"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide automation local-loop", output, StringComparison.Ordinal);
        Assert.Contains("claude", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("codex", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generic", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── routing via GuideAutomationCommand ────────────────────────────────

    [Fact]
    public void Dispatch_LocalLoopRoutedThroughGuideAutomationCommand_Works()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["local-loop", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("local-loop", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("claude", document.RootElement.GetProperty("agent").GetString());
    }

    // ── repo parameter ────────────────────────────────────────────────────

    [Fact]
    public void Execute_WithRepo_EmitsRepoInResult()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("J-Tech-Japan/intent-system", document.RootElement.GetProperty("repo").GetString());
    }

    [Fact]
    public void Execute_WithoutRepo_RepoFieldIsAbsentOrNull()
    {
        using var writer = new StringWriter();
        GuideAutomationLocalLoopCommand.Execute(
            CreateContext(),
            ["--agent", "claude", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var hasRepo = document.RootElement.TryGetProperty("repo", out var repoElement);
        Assert.True(!hasRepo || repoElement.ValueKind == JsonValueKind.Null);
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
