using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideDesignThreadG654Tests
{
    private static readonly string[] ParentPayloadFieldNames =
    [
        "process",
        "preview_status",
        "agent_kind_neutral",
        "session_layer_rule",
        "domain",
        "team",
        "routing_root",
        "reachability",
        "wake_rule",
        "provenance",
        "approval",
        "dialog_answering_rule",
        "residual_approval",
        "merge_authority",
        "delegation_verification",
        "observation_boundary",
        "team_and_duty_split",
        "monitoring",
        "reporting",
        "negative_invariants",
    ];

    // G774 captures every parent field name and raw JSON value in rendered
    // order. Only the separately asserted packet_authoring_check block may
    // differ; changing any parent value, nesting, or field order changes this
    // oracle rather than weakening a full-payload assertion.
    private const string ParentPayloadOracleHash = "c43e27f362c39d9c737fc2269b1979bdf8ad9b7ecb07f3dd0491ba1325d0c54f";

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
    public void Json_PacketAuthoringCheckIsTheOnlyAdditivePayloadDelta_G774()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideDesignThreadCommand.Execute(
                CreateContext(),
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--routing-root", "/g774-parent", "--format", "json"],
                writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var fields = root.EnumerateObject().ToArray();

        Assert.Equal(
            ParentPayloadFieldNames.Append("packet_authoring_check"),
            fields.Select(field => field.Name));
        var parentPayloadOracle = ComputePayloadOracle(fields.Where(field => field.Name != "packet_authoring_check"));
        Assert.True(
            string.Equals(ParentPayloadOracleHash, parentPayloadOracle, StringComparison.Ordinal),
            $"Parent payload oracle changed. Expected '{ParentPayloadOracleHash}', actual '{parentPayloadOracle}'.");

        var check = root.GetProperty("packet_authoring_check");
        Assert.Equal(
            new[]
            {
                "before_publish",
                "per_criterion_satisfiability",
                "negative_criterion_scoping",
                "request_update_condition",
                "discriminating_pair",
                "recognition_examples",
                "g770_resolution_rule",
            },
            check.EnumerateObject().Select(field => field.Name));
        Assert.Contains("packet's own constraints", check.GetProperty("per_criterion_satisfiability").GetString()!, StringComparison.Ordinal);
        Assert.Contains("every negative criterion", check.GetProperty("negative_criterion_scoping").GetString()!, StringComparison.Ordinal);
        Assert.Contains("limiting word", check.GetProperty("negative_criterion_scoping").GetString()!, StringComparison.Ordinal);
        Assert.Contains("Request an update", check.GetProperty("request_update_condition").GetString()!, StringComparison.Ordinal);
        Assert.Contains("named discriminating pair", check.GetProperty("discriminating_pair").GetString()!, StringComparison.Ordinal);

        var examples = Join(check.GetProperty("recognition_examples"));
        Assert.Contains("G765 AC4/AC6", examples, StringComparison.Ordinal);
        Assert.Contains("G767 AC1/AC6", examples, StringComparison.Ordinal);
        Assert.Contains("G769 AC3/AC4", examples, StringComparison.Ordinal);
        Assert.Contains("root resolution", check.GetProperty("g770_resolution_rule").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_RendersPacketAuthoringCheckWithAllRecognitionExamples_G774()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(), [], writer));
        var output = writer.ToString();

        Assert.Contains("## 8. Packet-authoring self-check (G774)", output, StringComparison.Ordinal);
        Assert.Contains("per-criterion satisfiability", output, StringComparison.Ordinal);
        Assert.Contains("limiting word", output, StringComparison.Ordinal);
        Assert.Contains("Request an update", output, StringComparison.Ordinal);
        Assert.Contains("named discriminating pair", output, StringComparison.Ordinal);
        foreach (var recognitionExample in new[] { "G765 AC4/AC6", "G767 AC1/AC6", "G769 AC3/AC4", "root resolution" })
            Assert.Contains(recognitionExample, output, StringComparison.Ordinal);
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

    [Fact]
    public void EnglishAndJapaneseDocs_SemanticallyMirrorPacketAuthoringCheck_G774()
    {
        var en = ReadRepoFile("docs/en/12-agent-message-orchestration.md");
        var ja = ReadRepoFile("docs/ja/12-agent-message-orchestration.md");

        foreach (var recognitionExample in new[] { "G765 AC4/AC6", "G767 AC1/AC6", "G769 AC3/AC4", "root resolution" })
        {
            Assert.Contains(recognitionExample, en, StringComparison.Ordinal);
            Assert.Contains(recognitionExample, ja, StringComparison.Ordinal);
        }

        Assert.Contains("per-criterion satisfiability", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limiting word", en, StringComparison.Ordinal);
        Assert.Contains("request an update", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("discriminating pair", en, StringComparison.Ordinal);

        Assert.Contains("criterion ごとの充足可能性", ja, StringComparison.Ordinal);
        Assert.Contains("限定語", ja, StringComparison.Ordinal);
        Assert.Contains("更新を要求", ja, StringComparison.Ordinal);
        Assert.Contains("判別対", ja, StringComparison.Ordinal);
    }

    private static string Join(JsonElement array) => string.Join('\n', array.EnumerateArray().Select(item => item.GetString()));

    private static string ComputePayloadOracle(IEnumerable<JsonProperty> fields)
    {
        var payload = string.Join(
            "\u001E",
            fields.Select(field => field.Name + "\u001F" + field.Value.GetRawText()));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

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
