using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // Immutable G776 parent oracle from cfdacb4a657d9a60ab82fea3faa435ff732f389f.
    // The current head is projected back to that parent by removing only the
    // G789 nested additions before this hash is computed.
    private const string G776BaselinePayloadOracleHash = "5ebb02d016c9afec671e58184f993705d0c6e597ecf334239b19d704bfaf3294";

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
        using var projected = JsonDocument.Parse(RemoveG789Additions(document.RootElement));
        var projectedFields = projected.RootElement.EnumerateObject().ToArray();

        // Project away later additive payloads before asserting the G777
        // field diff; G800 owns the separate research contract field.
        Assert.Equal(
            G776BaselinePayloadFieldNames.Append("unreadable_repair_response"),
            projectedFields.Select(field => field.Name));

        var addition = Assert.Single(fields, field => field.Name == "unreadable_repair_response");
        Assert.Contains("repair-unreadable", addition.Value.GetString()!, StringComparison.Ordinal);

        var baseline = projectedFields.Where(field => field.Name != "unreadable_repair_response").ToArray();
        var actualOracle = ComputePayloadOracle(baseline);
        Assert.True(
            string.Equals(G776BaselinePayloadOracleHash, actualOracle, StringComparison.Ordinal),
            $"G776 sibling baseline changed. Expected '{G776BaselinePayloadOracleHash}', actual '{actualOracle}'.");
        Console.WriteLine($"G789 guide design-thread G776 remainder oracle: expected={G776BaselinePayloadOracleHash}; actual={actualOracle}; removed=team_and_duty_split.review_seat_selection,external_residence_operating_contract.orca_operating_block");
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

    private static string RemoveG789Additions(JsonElement root)
    {
        var projected = JsonNode.Parse(root.GetRawText())!.AsObject();
        // G800 adds a separate research contract payload; keep this parent
        // oracle scoped to the pre-G800 guide surface.
        projected.Remove("research_delegation");
        projected["team_and_duty_split"]?.AsObject().Remove("review_seat_selection");
        projected["external_residence_operating_contract"]?.AsObject().Remove("orca_operating_block");
        projected["observation_boundary"]?.AsObject().Remove("inspect_route");
        return projected.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
