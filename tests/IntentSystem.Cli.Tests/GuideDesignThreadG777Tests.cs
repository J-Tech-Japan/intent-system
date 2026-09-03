using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G777: a liveness answer that names unreadable-record damage must render the
/// existing G773 repair route without changing any reader or repair behavior.
/// </summary>
public sealed class GuideDesignThreadG777Tests
{
    private static readonly string[] G776BaselinePayloadFieldNames =
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
        "packet_authoring_check",
        "external_residence_operating_contract",
    ];

    // The full G789 payload, including raw G774/G775/G789 blocks, stays
    // stable when the one G777 sibling is excluded.
    private const string G776BaselinePayloadOracleHash = "bfb054d9b5ce9006dbae2764d7ed5648e2dfa736054f0532987ebe857f2712b8";

    [Fact]
    public void RenderedLivenessGuidance_NamesTheSanctionedDryRunBeforeWriteResponse_G777()
    {
        using var document = JsonDocument.Parse(RenderJson());
        var response = document.RootElement.GetProperty("unreadable_repair_response").GetString()!;

        Assert.Contains("non-zero `unreadable_record_count`", response, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify supervise repair-unreadable", response, StringComparison.Ordinal);
        var dryRunIndex = response.IndexOf("`--dry-run`", StringComparison.Ordinal);
        var writeIndex = response.IndexOf("`--write`", StringComparison.Ordinal);
        Assert.True(dryRunIndex >= 0 && writeIndex > dryRunIndex, response);
        Assert.Contains("verbatim as evidence", response, StringComparison.Ordinal);
        Assert.Contains("no reconstruction claim", response, StringComparison.Ordinal);
        Assert.Contains("never automatic and never performed on read", response, StringComparison.Ordinal);

        var livenessGuidance = Section(RenderMarkdown(), "## 6. Team formula, duty split, and monitoring separation", "## 7. Outcome-shaped reporting");
        Assert.Contains("**unreadable-record response (G777):**", livenessGuidance, StringComparison.Ordinal);
        Assert.Contains(response, livenessGuidance, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_FieldDiffAddsOnlyG777Response_AndPreservesG774G775G776Baseline_G777()
    {
        using var document = JsonDocument.Parse(RenderJson());
        var fields = document.RootElement.EnumerateObject().ToArray();

        Assert.Equal(
            G776BaselinePayloadFieldNames.Append("unreadable_repair_response"),
            fields.Select(field => field.Name));

        var addition = Assert.Single(fields, field => field.Name == "unreadable_repair_response");
        Assert.Contains("repair-unreadable", addition.Value.GetString()!, StringComparison.Ordinal);

        var baseline = fields.Where(field => field.Name != "unreadable_repair_response").ToArray();
        var actualOracle = ComputePayloadOracle(baseline);
        Assert.True(
            string.Equals(G776BaselinePayloadOracleHash, actualOracle, StringComparison.Ordinal),
            $"G776 sibling baseline changed. Expected '{G776BaselinePayloadOracleHash}', actual '{actualOracle}'.");
    }

    [Fact]
    public void DocumentationMirrors_CrossReferenceTheRenderedLivenessResponse_G777()
    {
        var en = Section(
            ReadRepoFile("docs/en/12-agent-message-orchestration.md"),
            "### Evidence-preserving unreadable-record repair (G773 — preview-through-1.x)",
            "### Atomic per-record supervision history appends");
        var ja = Section(
            ReadRepoFile("docs/ja/12-agent-message-orchestration.md"),
            "### unreadable record を evidence として隔離する repair (G773 — preview-through-1.x)",
            "### supervision history の record 単位 append");

        Assert.Contains("The rendered `intent-cli guide design-thread` liveness guidance", en, StringComparison.Ordinal);
        Assert.Contains("non-zero `unreadable_record_count`", en, StringComparison.Ordinal);
        Assert.Contains("`--dry-run` first", en, StringComparison.Ordinal);
        Assert.Contains("`--write` only second", en, StringComparison.Ordinal);
        Assert.Contains("verbatim as evidence", en, StringComparison.Ordinal);
        Assert.Contains("no reconstruction claim", en, StringComparison.Ordinal);

        Assert.Contains("描画される `intent-cli guide design-thread` の liveness の案内", ja, StringComparison.Ordinal);
        Assert.Contains("0 以外の\n`unreadable_record_count`", ja, StringComparison.Ordinal);
        Assert.Contains("`--dry-run` で実行して証拠を確認", ja, StringComparison.Ordinal);
        Assert.Contains("二番目の操作として `--write`", ja, StringComparison.Ordinal);
        Assert.Contains("原文のバイト列のまま保存", ja, StringComparison.Ordinal);
        Assert.Contains("内容を再構成しない", ja, StringComparison.Ordinal);
    }

    private static string RenderJson()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideDesignThreadCommand.Execute(
                CreateContext(),
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--routing-root", "/g777-parent", "--format", "json"],
                writer));
        return writer.ToString();
    }

    private static string RenderMarkdown()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideDesignThreadCommand.Execute(
                CreateContext(),
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--routing-root", "/g777-parent"],
                writer));
        return writer.ToString();
    }

    private static string Section(string document, string start, string end)
    {
        var startIndex = document.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing section '{start}'.");
        var endIndex = document.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing section terminator '{end}'.");
        return document[startIndex..endIndex];
    }

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
