using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideDesignThreadG654Tests
{
    [Theory]
    [InlineData("agmsg", false)]
    [InlineData("agmsg", true)]
    [InlineData("herdr-only", false)]
    [InlineData("herdr-only", true)]
    public void Guide_RendersSameContract_InEverySessionLayer_WithOrWithoutTeam(string sessionLayer, bool includeTeam)
    {
        using var writer = new StringWriter();
        var args = new List<string> { "--domain", "intent-cli", "--routing-root", "/host", "--format", "json" };
        if (includeTeam) args.InsertRange(2, new[] { "--team", "intent-cli-dev" });

        var exitCode = GuideDesignThreadCommand.Execute(CreateContext(), args.ToArray(), writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("design-thread-operating-contract", root.GetProperty("process").GetString());
        Assert.Equal("preview-through-1.x", root.GetProperty("preview_status").GetString());
        Assert.True(root.GetProperty("agent_kind_neutral").GetBoolean());
        Assert.Contains(sessionLayer, root.GetProperty("session_layer_rule").GetString()!, StringComparison.Ordinal);
        Assert.Equal(includeTeam, root.TryGetProperty("team", out _));
    }

    [Fact]
    public void Json_PinsWakeProvenanceApprovalAuthorityVerificationAndReportingContracts()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(), ["--format", "json"], writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        Assert.Equal(4, root.GetProperty("wake_rule").GetProperty("valid_outcomes").GetArrayLength());
        var invalid = Join(root.GetProperty("wake_rule").GetProperty("not_outcomes"));
        foreach (var value in new[] { "no-actionable", "running=true", "liveness", "unchanged", "no change" })
            Assert.Contains(value, invalid, StringComparison.Ordinal);

        Assert.Equal(
            new[] { "candidate", "accepted design", "packet", "queued unit", "published unit", "WIP" },
            root.GetProperty("provenance").GetProperty("vocabulary").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(5, root.GetProperty("provenance").GetProperty("external_origin_fields").GetArrayLength());

        Assert.Equal(
            new[] { "merge", "verify merge commit", "close linked issue", "transition queue", "append runs", "write back host state", "push host state" },
            root.GetProperty("approval").GetProperty("merge_transaction").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("once", root.GetProperty("approval").GetProperty("merge_rule").GetString()!, StringComparison.Ordinal);

        Assert.Contains("reviewDecision alone never proves", root.GetProperty("merge_authority").GetProperty("rule").GetString()!, StringComparison.Ordinal);
        Assert.Equal(3, root.GetProperty("delegation_verification").GetProperty("layers").GetArrayLength());
        Assert.Contains("G652", Join(root.GetProperty("delegation_verification").GetProperty("layers")), StringComparison.Ordinal);
        Assert.Contains("running=true", root.GetProperty("delegation_verification").GetProperty("rule").GetString()!, StringComparison.Ordinal);

        Assert.Contains("every stall class", root.GetProperty("team_and_duty_split").GetProperty("orchestration_ownership").GetString()!, StringComparison.Ordinal);
        Assert.Contains("review wedges", root.GetProperty("team_and_duty_split").GetProperty("orchestration_ownership").GetString()!, StringComparison.Ordinal);
        Assert.Equal(9, root.GetProperty("team_and_duty_split").GetProperty("design_escalations").GetArrayLength());
        Assert.Contains("greater than", root.GetProperty("monitoring").GetProperty("bound_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("persistent AGENTS", root.GetProperty("monitoring").GetProperty("deployment_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("minimal concrete operation", root.GetProperty("reporting").GetProperty("human_action_rule").GetString()!, StringComparison.Ordinal);

        var residual = root.GetProperty("residual_approval");
        var residualText = Join(residual.GetProperty("layers"))
            + "\n"
            + residual.GetProperty("no_policy_rule").GetString()
            + "\n"
            + residual.GetProperty("watcher_boundary").GetString();
        Assert.Contains("notify adjudicate", residualText, StringComparison.Ordinal);
        Assert.Contains("answerable_by", residualText, StringComparison.Ordinal);
        Assert.Contains("hard risk floor", residualText, StringComparison.Ordinal);
        Assert.Contains("caller-mismatched", residualText, StringComparison.Ordinal);
        Assert.DoesNotContain("design never answers", residualText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Markdown_IsAgentKindNeutral_AndContainsNoNormativeProviderNames()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(), ["--team", "intent-cli-dev"], writer));
        var output = writer.ToString();

        Assert.Contains("Four-outcome wake rule", output, StringComparison.Ordinal);
        Assert.Contains("four judgment-bearing threads plus one supervision process", output, StringComparison.Ordinal);
        Assert.Contains("at most once per design wake", output, StringComparison.Ordinal);
        Assert.DoesNotContain("codex", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Claude app safety", output, StringComparison.Ordinal);
        Assert.DoesNotContain("copilot", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogHelpAndNext_AllReachTheGuide()
    {
        Assert.Contains(GuideCommandsListCommand.Groups, entry => entry.Name == "guide design-thread" && entry.Role == "design");
        Assert.Contains(GuideHelpCommand.Subcommands, entry => entry.Name == "design-thread");

        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(CreateContext(), ["--format", "json"], writer));
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(GuideDesignThreadCommand.CommandName, document.RootElement.GetProperty("design_role_guide").GetString());
    }

    [Fact]
    public void EnglishAndJapaneseDocs_MirrorGuideContractAndPreviewLedger()
    {
        var en = ReadRepoFile("docs/en/12-agent-message-orchestration.md");
        var ja = ReadRepoFile("docs/ja/12-agent-message-orchestration.md");
        var enLedger = ReadRepoFile("docs/en/1.0-compatibility-ledger.md");
        var jaLedger = ReadRepoFile("docs/ja/1.0-compatibility-ledger.md");

        foreach (var doc in new[] { en, ja })
        {
            Assert.Contains("intent-cli guide design-thread", doc, StringComparison.Ordinal);
            Assert.Contains("no-actionable", doc, StringComparison.Ordinal);
            Assert.Contains("reviewDecision", doc, StringComparison.Ordinal);
            Assert.Contains("G652", doc, StringComparison.Ordinal);
            Assert.Contains("running=true", doc, StringComparison.Ordinal);
            Assert.Contains("AGENTS", doc, StringComparison.Ordinal);
        }
        foreach (var ledger in new[] { enLedger, jaLedger })
        {
            Assert.Contains("| `guide design-thread` |", ledger, StringComparison.Ordinal);
            Assert.Contains("| `notify adjudicate` |", ledger, StringComparison.Ordinal);
            Assert.Contains("exit_code", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }
    }

    private static string Join(JsonElement array) => string.Join('\n', array.EnumerateArray().Select(item => item.GetString()));

    private static CliContext CreateContext() => new()
    {
        RepoRoot = Path.GetTempPath(),
        Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" } },
    };

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            current = current.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
