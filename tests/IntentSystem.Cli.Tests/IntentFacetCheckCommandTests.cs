using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G531: coverage for the read-only <c>intent facet-check</c> scaffold —
/// term extraction, lexical matching/collision, acceptance-property
/// coverage gaps, no-facet-data degradation, and the always-exit-0 /
/// always-carries-a-disclaimer contract.
/// </summary>
public sealed class IntentFacetCheckCommandTests
{
    // ── Extraction ───────────────────────────────────────────────────

    [Fact]
    public void ExtractCandidateTerms_BacktickIdentifier_Extracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Add the `CreateOrder` command.");

        Assert.Contains("CreateOrder", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_BacktickSpanWithSpaces_SkippedNotATerm()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Run `intent facet-check --domain d` first.");

        Assert.DoesNotContain(terms, t => t.Contains(' '));
    }

    [Fact]
    public void ExtractCandidateTerms_PlainCamelCaseWord_Extracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("This introduces OrderPlaced as a new concept.");

        Assert.Contains("OrderPlaced", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_WordEndingInCommandSuffix_Extracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("We add a new refund command.");

        // lowercase "command" alone has no case transition and isn't a
        // command/event token on its own — only a capitalized-suffix token
        // like "RefundCommand" should be picked up.
        Assert.DoesNotContain("command", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_PlainLowercaseProseWords_NotExtracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("This is an ordinary sentence about orders and events.");

        Assert.Empty(terms);
    }

    [Fact]
    public void ExtractCandidateTerms_DuplicateAcrossFormsAfterNormalization_DedupedKeepsFirstCasing()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Introduces `CreateOrder`. Later mentions CreateOrder again.");

        Assert.Single(terms, t => t == "CreateOrder");
    }

    // ── --terms mode: matching / collision / unmatched ──────────────

    [Fact]
    public void Execute_TermsMode_MatchedTermReportsRelatedVocabularyNode()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.Equal("CreateOrder", termReport.GetProperty("term").GetString());
        Assert.False(termReport.GetProperty("unmatched").GetBoolean());
        var related = termReport.GetProperty("related_nodes").EnumerateArray().ToArray();
        var node = Assert.Single(related);
        Assert.Equal("commands/create-order", node.GetProperty("id").GetString());
    }

    [Fact]
    public void Execute_TermsMode_MatchedVocabularyNode_AlsoReportedAsCollision()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "create-order", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        var collisions = termReport.GetProperty("collisions").EnumerateArray().ToArray();
        var collision = Assert.Single(collisions);
        Assert.Equal("commands/create-order", collision.GetProperty("id").GetString());
    }

    [Fact]
    public void Execute_TermsMode_NonVocabularyMatch_NotReportedAsCollision()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("invariants/create-order.md", ["invariant"], "Create Order Invariant");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.False(termReport.GetProperty("unmatched").GetBoolean());
        Assert.Empty(termReport.GetProperty("collisions").EnumerateArray());
    }

    [Fact]
    public void Execute_TermsMode_NoMatchingNode_UnmatchedTrue()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "ShipPackage", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.True(termReport.GetProperty("unmatched").GetBoolean());
        Assert.Empty(termReport.GetProperty("related_nodes").EnumerateArray());
    }

    [Fact]
    public void Execute_TermsMode_MultiFacetNode_ReportedOnceNotOncePerFacet()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary", "invariant"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.Single(termReport.GetProperty("related_nodes").EnumerateArray());
    }

    [Fact]
    public void Execute_TermsMode_NoCoverageSection_NullNotFabricatedGap()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.TryGetProperty("coverage", out _));
    }

    [Fact]
    public void Execute_JsonFormat_EveryResultCarriesDisclaimer()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "AnyTerm", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var disclaimer = document.RootElement.GetProperty("disclaimer").GetString();
        Assert.False(string.IsNullOrWhiteSpace(disclaimer));
        Assert.Contains("not semantic verification", disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    // ── no-facet-data degradation ────────────────────────────────────

    [Fact]
    public void Execute_UnannotatedDomain_NoFacetDataTrue_ExitZeroNotAnError()
    {
        using var workspace = new FacetCheckWorkspace();
        // No facet nodes written at all.

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("no_facet_data").GetBoolean());
        // Extraction/matching still runs — just reports unmatched, not suppressed.
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.True(termReport.GetProperty("unmatched").GetBoolean());
    }

    // ── --packet mode: extraction + coverage ─────────────────────────

    [Fact]
    public void Execute_PacketMode_ExtractsTermsFromGithubBodyAndImplementation()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");
        workspace.WritePacket(
            "G900",
            githubBody: "## Goal\n\nAdd the `CreateOrder` command.\n",
            implementation: "Implements OrderPlaced handling.\n",
            intentReferences: []);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var terms = document.RootElement.GetProperty("terms").EnumerateArray().Select(t => t.GetProperty("term").GetString()).ToArray();
        Assert.Contains("CreateOrder", terms);
        Assert.Contains("OrderPlaced", terms);
    }

    [Fact]
    public void Execute_PacketMode_CoverageListsOverlappingAcceptancePropertyNode()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("acceptance/order-flow.md", ["acceptance-property"], "Order flow acceptance");
        workspace.WritePacket(
            "G900",
            githubBody: "## Goal\n\nAdd the `CreateOrder` command.\n",
            implementation: string.Empty,
            intentReferences: ["intents/intent-cli/acceptance/order-flow.md"]);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("coverage");
        Assert.False(coverage.GetProperty("gap").GetBoolean());
        var node = Assert.Single(coverage.GetProperty("nodes").EnumerateArray());
        Assert.Equal("acceptance/order-flow", node.GetProperty("id").GetString());
    }

    [Fact]
    public void Execute_PacketMode_NoOverlappingAcceptancePropertyNode_GapTrue()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("acceptance/unrelated.md", ["acceptance-property"], "Unrelated acceptance");
        workspace.WritePacket(
            "G900",
            githubBody: "## Goal\n\nAdd the `CreateOrder` command.\n",
            implementation: string.Empty,
            intentReferences: ["intents/intent-cli/commands"]);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("coverage");
        Assert.True(coverage.GetProperty("gap").GetBoolean());
        Assert.Empty(coverage.GetProperty("nodes").EnumerateArray());
    }

    [Fact]
    public void Execute_PacketMode_MissingPacketDirectory_ReturnsUsageErrorNotSilentEmptyResult()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G999-missing", "--format", "json"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No packet directory found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownFormat_PacketMode_RendersTermsAndCoverageSections()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");
        workspace.WritePacket(
            "G900",
            githubBody: "## Goal\n\nAdd the `CreateOrder` command.\n",
            implementation: string.Empty,
            intentReferences: []);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Facet check — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("## Terms", output, StringComparison.Ordinal);
        Assert.Contains("### `CreateOrder`", output, StringComparison.Ordinal);
        Assert.Contains("## Acceptance-property coverage", output, StringComparison.Ordinal);
        Assert.Contains("Gap: yes", output, StringComparison.Ordinal);
    }

    // ── usage / exit-code contract ────────────────────────────────────

    [Fact]
    public void Execute_NeitherPacketNorTerms_ReturnsUsageError()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(workspace.Context, ["--domain", "intent-cli"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires exactly one of --packet or --terms", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_BothPacketAndTerms_ReturnsUsageError()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--terms", "CreateOrder"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("mutually exclusive", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TermsWithEmptyElement_ReturnsUsageError()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder,,ShipPackage"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("no empty elements", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FindingsPresent_StillExitsZero_NeverAGate()
    {
        using var workspace = new FacetCheckWorkspace();
        // A term that will be unmatched — still exit 0, never a gate.
        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "TotallyUnknownThing"], writer);

        Assert.Equal(0, exitCode);
    }

    private sealed class FacetCheckWorkspace : IDisposable
    {
        public FacetCheckWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("facet-check-tests-").FullName;
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RepoRoot { get; }

        public CliContext Context { get; }

        public void WriteFacetNode(string relativePath, IReadOnlyList<string> facets, string title)
        {
            var fullPath = Path.Combine(
                RepoRoot, "intents", Context.Config.Project.Domain, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, $"---\nfacets: [{string.Join(", ", facets)}]\n---\n# {title}\n");
        }

        public void WritePacket(string executionUnit, string githubBody, string implementation, IReadOnlyList<string> intentReferences)
        {
            var packetDir = Path.Combine(RepoRoot, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(packetDir);
            File.WriteAllText(Path.Combine(packetDir, "github-body.md"), githubBody);
            File.WriteAllText(Path.Combine(packetDir, "implementation.md"), implementation);
            var referencesYaml = intentReferences.Count == 0
                ? "intent_references: []"
                : "intent_references:\n" + string.Join("\n", intentReferences.Select(r => $"  - {r}"));
            File.WriteAllText(
                Path.Combine(packetDir, "packet.yaml"),
                $"""
                implementation_issue_packet:
                  source_execution_unit: {executionUnit}
                  domain: intent-cli
                  {referencesYaml}
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot))
            {
                Directory.Delete(RepoRoot, recursive: true);
            }
        }
    }
}
