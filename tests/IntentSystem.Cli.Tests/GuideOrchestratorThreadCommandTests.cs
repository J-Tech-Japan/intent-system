using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G487: coverage for the optional agmsg-backed orchestrator-thread guide
/// surface — mode separation (no mixed-mode timer races), the three thread
/// prompts, the structured agmsg reply contract, the orchestrator first-wake,
/// and the safety boundaries — across both markdown and JSON output.
/// </summary>
public sealed class GuideOrchestratorThreadCommandTests
{
    [Fact]
    public void Execute_Markdown_SeparatesTimerLoopFromOrchestratorMode_AndForbidsMixedTimers()
    {
        var output = RunMarkdown(["--domain", "estivo", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("# Guide — agmsg-backed orchestrator thread (G487)", output, StringComparison.Ordinal);
        Assert.Contains("## Mode separation", output, StringComparison.Ordinal);
        Assert.Contains("timer-loop mode", output, StringComparison.Ordinal);
        Assert.Contains("orchestrator-message mode", output, StringComparison.Ordinal);
        // The existing timer-loop mode is preserved, not replaced.
        Assert.Contains("still fully supported", output, StringComparison.Ordinal);
        // Mixed-mode timer race is explicitly forbidden.
        Assert.Contains("do NOT launch the implementation/review recurring timer loops", output, StringComparison.Ordinal);
        // agmsg is signal-only; intent-cli/GitHub authoritative.
        Assert.Contains("agmsg", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli and GitHub remain authoritative", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_EmitsThreePasteReadyThreadPrompts()
    {
        var output = RunMarkdown(["--domain", "estivo", "--target-repo", "owner/repo", "--agent", "codex"]);

        Assert.Contains("### orchestrator", output, StringComparison.Ordinal);
        Assert.Contains("### implementation", output, StringComparison.Ordinal);
        Assert.Contains("### review", output, StringComparison.Ordinal);
        // Placeholders are substituted into the prompts.
        Assert.Contains("domain `estivo`", output, StringComparison.Ordinal);
        Assert.Contains("`owner/repo`", output, StringComparison.Ordinal);
        Assert.Contains("`codex`", output, StringComparison.Ordinal);
        // Implementation thread still derives issue/PR numbers from worker next-action, not agmsg text.
        Assert.Contains("worker next-action", output, StringComparison.Ordinal);
        Assert.Contains("Closes #<issue>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_EmitsStructuredReplyContractAndFirstWakeAndBoundaries()
    {
        var output = RunMarkdown([]);

        // Reply contract: accepted / progress / completed / blocked.
        Assert.Contains("## agmsg reply contract", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"accepted\"", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"progress\"", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"blocked\"", output, StringComparison.Ordinal);

        // First wake: read replies, ask intent-cli, verify GitHub, one message per wake.
        Assert.Contains("## Orchestrator first wake", output, StringComparison.Ordinal);
        Assert.Contains("Send AT MOST ONE message this wake", output, StringComparison.Ordinal);

        // Safety boundaries.
        Assert.Contains("## Safety boundaries", output, StringComparison.Ordinal);
        Assert.Contains("No raw label mutation", output, StringComparison.Ordinal);
        Assert.Contains("No hand-editing queue-state", output, StringComparison.Ordinal);
        Assert.Contains("never replaces semantic review", output, StringComparison.Ordinal);
        Assert.Contains("Fail closed on duplicate orchestrators", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasStableShape_WithThreeThreadsAndReplyContract()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "estivo", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        var roles = root.GetProperty("threads").EnumerateArray()
            .Select(t => t.GetProperty("role").GetString())
            .ToArray();
        Assert.Equal(new[] { "orchestrator", "implementation", "review" }, roles);

        var contract = root.GetProperty("agmsg_reply_contract");
        Assert.True(contract.TryGetProperty("accepted", out _));
        Assert.True(contract.TryGetProperty("progress", out _));
        Assert.True(contract.TryGetProperty("completed", out _));
        Assert.True(contract.TryGetProperty("blocked", out _));

        Assert.NotEmpty(root.GetProperty("orchestrator_first_wake").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("safety_boundaries").EnumerateArray());

        var mode = root.GetProperty("mode_separation");
        Assert.True(mode.TryGetProperty("timer_loop_mode", out _));
        Assert.True(mode.TryGetProperty("orchestrator_message_mode", out _));
        Assert.True(mode.TryGetProperty("mixed_mode_warning", out _));
    }

    [Fact]
    public void Execute_UnknownArgument_ExitsOne()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(), ["--nope"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Help_ExplainsOptionalAndNonReplacing()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide orchestrator-thread", output, StringComparison.Ordinal);
        Assert.Contains("OPTIONAL", output, StringComparison.Ordinal);
        Assert.Contains("not replaced", output, StringComparison.Ordinal);
    }

    private static string RunMarkdown(string[] args)
    {
        using var writer = new StringWriter();
        var fullArgs = args.Concat(new[] { "--format", "markdown" }).ToArray();
        var exitCode = GuideOrchestratorThreadCommand.Execute(CreateContext(), fullArgs, writer);
        Assert.Equal(0, exitCode);
        return writer.ToString();
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
                    WorktreeRoot = ".intent-cli/worktrees",
                },
            },
        };
    }
}
