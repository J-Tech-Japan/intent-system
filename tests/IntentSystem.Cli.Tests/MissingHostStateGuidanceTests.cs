using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class MissingHostStateGuidanceTests
{
    [Fact]
    public void Emit_DefaultMarkdown_NamesHostRepoVsChildRepoVsTargetRepo()
    {
        using var writer = new StringWriter();

        var exitCode = MissingHostStateGuidance.Emit(
            writer,
            new[] { "guide", "review", "--pr", "4", "--repo", "J-Tech-Japan/TraceForge" },
            "/Users/dev/TraceForge/submodules/traceforge");

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("missing host state (G299)", output, StringComparison.Ordinal);
        Assert.Contains("Host repo cwd: _unresolved_", output, StringComparison.Ordinal);
        Assert.Contains("Child implementation repo cwd: `/Users/dev/TraceForge/submodules/traceforge`", output, StringComparison.Ordinal);
        Assert.Contains("Target GitHub repo: `J-Tech-Japan/TraceForge`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_JsonFormat_ProducesMachineReadableMissingHostStateRecord()
    {
        using var writer = new StringWriter();

        var exitCode = MissingHostStateGuidance.Emit(
            writer,
            new[] { "guide", "review", "--pr", "4", "--repo", "J-Tech-Japan/TraceForge", "--format", "json" },
            "/Users/dev/TraceForge/submodules/traceforge");

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("missing-host-state", root.GetProperty("status").GetString());
        Assert.Equal("guide review", root.GetProperty("command").GetString());
        Assert.Equal("/Users/dev/TraceForge/submodules/traceforge", root.GetProperty("cwd").GetString());
        Assert.Equal(".intent-cli", root.GetProperty("missing").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("host_repo_cwd").ValueKind);
        Assert.Equal("/Users/dev/TraceForge/submodules/traceforge", root.GetProperty("child_repo_cwd").GetString());
        Assert.Equal("J-Tech-Japan/TraceForge", root.GetProperty("target_github_repo").GetString());
        Assert.Equal(4, root.GetProperty("hard_rules").GetArrayLength());
        Assert.True(root.GetProperty("next_steps").GetArrayLength() >= 4);
    }

    [Fact]
    public void Emit_HardRules_ForbidFallbackToOrdinaryGitHubReview()
    {
        using var writer = new StringWriter();
        MissingHostStateGuidance.Emit(
            writer,
            new[] { "guide", "review", "--pr", "4", "--format", "json" },
            "/tmp/child");

        using var document = JsonDocument.Parse(writer.ToString());
        var rules = document.RootElement.GetProperty("hard_rules")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        Assert.Contains(rules, r => r.Contains("Do NOT fall back to ordinary GitHub review", StringComparison.Ordinal));
        Assert.Contains(rules, r => r.Contains("Implementation findings", StringComparison.Ordinal)
                                 && r.Contains("may still become PR comments", StringComparison.Ordinal)
                                 && r.Contains("host metadata", StringComparison.Ordinal));
        Assert.Contains(rules, r => r.Contains("gh ... edit --add-label", StringComparison.Ordinal));
        Assert.Contains(rules, r => r.Contains("intent-cli run", StringComparison.Ordinal));
    }

    [Fact]
    public void Emit_NextSteps_TellsAgentToReRunFromParentHostRepoRoot()
    {
        using var writer = new StringWriter();
        MissingHostStateGuidance.Emit(
            writer,
            new[] { "worker", "next-action", "--repo", "J-Tech-Japan/TraceForge", "--workdir", "/tmp/child", "--format", "json" },
            "/tmp/child");

        using var document = JsonDocument.Parse(writer.ToString());
        var steps = document.RootElement.GetProperty("next_steps")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        Assert.Contains(steps, s => s.Contains("Re-run", StringComparison.Ordinal)
                                 && s.Contains("parent host repo root", StringComparison.Ordinal)
                                 && s.Contains("`.intent-cli/`", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("child implementation repo", StringComparison.Ordinal)
                                 && s.Contains("submodule", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("structured clarification", StringComparison.Ordinal));
    }

    [Fact]
    public void Emit_OmitsTargetRepoNote_WhenRepoFlagAbsent()
    {
        using var writer = new StringWriter();
        MissingHostStateGuidance.Emit(
            writer,
            new[] { "guide", "review", "--format", "json" },
            "/tmp/child");

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("target_github_repo").ValueKind);
        var steps = document.RootElement.GetProperty("next_steps")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.DoesNotContain(steps, s => s.Contains("workflow target, NOT the host", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("guide", "review", "guide review")]
    [InlineData("worker", "next-action", "worker next-action")]
    [InlineData("closeout", "pr", "closeout pr")]
    [InlineData("review", "run", "review run")]
    public void Emit_CommandName_Reflects_TheInvocation(string group, string sub, string expected)
    {
        using var writer = new StringWriter();
        MissingHostStateGuidance.Emit(
            writer,
            new[] { group, sub, "--format", "json" },
            "/tmp/child");

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(expected, document.RootElement.GetProperty("command").GetString());
    }
}
