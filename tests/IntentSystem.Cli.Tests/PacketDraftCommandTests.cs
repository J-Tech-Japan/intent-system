using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class PacketDraftCommandTests
{
    [Fact]
    public void Execute_GivenWriteMode_CreatesAllFourPacketFilesWithRequiredHeadings()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Packet draft — G244", output, StringComparison.Ordinal);
        Assert.Contains("mode: write", output, StringComparison.Ordinal);
        Assert.Contains("packet.yaml: created", output, StringComparison.Ordinal);
        Assert.Contains("implementation.md: created", output, StringComparison.Ordinal);
        Assert.Contains("review-context.md: created", output, StringComparison.Ordinal);
        Assert.Contains("github-body.md: created", output, StringComparison.Ordinal);
        Assert.Contains("missing sections: none", output, StringComparison.Ordinal);

        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G244");
        Assert.True(File.Exists(Path.Combine(packetDir, "packet.yaml")));
        Assert.True(File.Exists(Path.Combine(packetDir, "implementation.md")));
        Assert.True(File.Exists(Path.Combine(packetDir, "review-context.md")));
        var githubBody = File.ReadAllText(Path.Combine(packetDir, "github-body.md"));
        foreach (var section in PacketDraftCommand.RequiredContractSections)
        {
            Assert.Contains($"## {section}", githubBody, StringComparison.Ordinal);
        }
    }

    // ── G482: scaffold emits the complete publish-ready contract shape ───

    [Fact]
    public void Execute_Scaffold_EmitsEveryScaffoldHeadingIncludingStandaloneContract()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G482", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        var githubBody = File.ReadAllText(
            Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G482", "github-body.md"));

        // Every scaffolded heading (required gate + Standalone Child Issue
        // Contract) must appear: removing one from the shared constant OR the
        // template fails this test.
        foreach (var section in PublishContractSections.ScaffoldHeadings)
        {
            Assert.Contains($"## {section}", githubBody, StringComparison.Ordinal);
        }
        Assert.Contains("## Standalone Child Issue Contract", githubBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredContractSections_ShareSingleSourceOfTruthWithPublishValidator()
    {
        // G482: the packet scaffold's draft check and the publish-body validator
        // must reference one shared list so they can never drift apart.
        Assert.Same(PublishContractSections.Required, PacketDraftCommand.RequiredContractSections);
        Assert.Same(PublishContractSections.Required, IssueValidateBodyValidator.RequiredHeadings);
        Assert.Equal(PacketDraftCommand.RequiredContractSections, IssueValidateBodyValidator.RequiredHeadings);
    }

    [Fact]
    public void ScaffoldHeadings_AreSupersetOfRequiredPlusStandaloneContract()
    {
        Assert.Contains(
            PublishContractSections.StandaloneChildIssueContract,
            PublishContractSections.ScaffoldHeadings);
        foreach (var required in PublishContractSections.Required)
        {
            Assert.Contains(required, PublishContractSections.ScaffoldHeadings);
        }
    }

    [Fact]
    public void Execute_GivenExistingFiles_DoesNotOverwriteAndReportsSkipped()
    {
        using var workspace = new PacketDraftWorkspace();
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G244");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "packet.yaml"), "existing content");

        using var writer = new StringWriter();
        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("packet.yaml: skipped", output, StringComparison.Ordinal);
        Assert.Equal("existing content", File.ReadAllText(Path.Combine(packetDir, "packet.yaml")));
    }

    // ── G530: Facet context section in review-context.md ────────────────

    [Fact]
    public void Execute_FreshScaffold_ReviewContextGetsGracefulNoFacetContextNote()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G530", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var reviewContext = File.ReadAllText(
            Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G530", "review-context.md"));
        Assert.Contains("## Facet context", reviewContext, StringComparison.Ordinal);
        Assert.Contains("No facet-annotated nodes found", reviewContext, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ExistingPacketYamlDeclaresIntentReferences_ReviewContextListsOverlappingFacetNodes()
    {
        // G530: "regenerating an existing packet preserves hand-edits" —
        // packet.yaml already exists (hand-edited with real
        // intent_references) but review-context.md does not yet; running
        // `packet draft` again must not touch the existing packet.yaml, but
        // DOES generate review-context.md fresh, using packet.yaml's real
        // on-disk intent_references (never the template's empty `[]`).
        using var workspace = new PacketDraftWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        workspace.WriteFacetNode("decisions/adr-1.md", ["decider"], "Unrelated decision");
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G530");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(
            Path.Combine(packetDir, "packet.yaml"),
            """
            implementation_issue_packet:
              source_execution_unit: G530
              domain: intent-cli
              intent_references:
                - intents/intent-cli/identity/mission.md
            """);

        using var writer = new StringWriter();
        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G530"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("packet.yaml: skipped", output, StringComparison.Ordinal);
        Assert.Contains("review-context.md: created", output, StringComparison.Ordinal);

        var reviewContext = File.ReadAllText(Path.Combine(packetDir, "review-context.md"));
        Assert.Contains("### vocabulary", reviewContext, StringComparison.Ordinal);
        Assert.Contains("identity/mission", reviewContext, StringComparison.Ordinal);
        Assert.Contains("Mission", reviewContext, StringComparison.Ordinal);
        // The unrelated decision node does not overlap intent_references —
        // it must not leak into the section.
        Assert.DoesNotContain("decisions/adr-1", reviewContext, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ExistingReviewContextMd_NeverOverwritten_PreservesHandEditsRegardlessOfFacetNodes()
    {
        using var workspace = new PacketDraftWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G530");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(
            Path.Combine(packetDir, "packet.yaml"),
            """
            implementation_issue_packet:
              source_execution_unit: G530
              domain: intent-cli
              intent_references:
                - intents/intent-cli/identity/mission.md
            """);
        File.WriteAllText(Path.Combine(packetDir, "review-context.md"), "hand-edited review context, do not touch");

        using var writer = new StringWriter();
        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G530"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("review-context.md: skipped", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            "hand-edited review context, do not touch",
            File.ReadAllText(Path.Combine(packetDir, "review-context.md")));
    }

    [Fact]
    public void Execute_GivenDryRun_DoesNotWriteFilesAndReportsPlanned()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        var files = root.GetProperty("files");
        Assert.Equal(4, files.GetArrayLength());
        foreach (var file in files.EnumerateArray())
        {
            Assert.Equal("planned", file.GetProperty("status").GetString());
        }

        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G244");
        Assert.False(Directory.Exists(packetDir));
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsStructuredResult()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G244", root.GetProperty("execution_unit").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.Equal(0, root.GetProperty("missing_contract_sections").GetArrayLength());
    }

    [Fact]
    public void Execute_MissingExecutionUnit_ReturnsUsageError()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--execution-unit is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidExecutionUnitId_ReturnsUsageError()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "bad/id"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid execution-unit id", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("packet draft", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("--execution-unit", writer.ToString(), StringComparison.Ordinal);
    }

    // ----- G347: Base Branch Policy section in generated github-body.md -----

    [Fact]
    public void Execute_G347_GeneratedGithubBodyIncludesBaseBranchPolicySection()
    {
        // G347 AC: the generated github-body.md must contain a non-empty
        // `## Base Branch Policy` section with a parseable
        // `Expected PR base branch:` line. This locks the section shape so
        // downstream consumers (worker complete G347 check) can always parse it.
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G347", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G347");
        var githubBody = File.ReadAllText(Path.Combine(packetDir, "github-body.md"));

        Assert.Contains("## Base Branch Policy", githubBody, StringComparison.Ordinal);
        Assert.Contains("Expected PR base branch:", githubBody, StringComparison.Ordinal);

        // The parser used by worker-complete must be able to extract the value.
        var extracted = WorkerCompleteCommand.ParseExpectedBaseBranchFromIssueBody(githubBody);
        Assert.NotNull(extracted);
        Assert.False(string.IsNullOrWhiteSpace(extracted));
    }

    [Fact]
    public void Execute_G347_GeneratedGithubBodyContainsDirectMainPolicyByDefault()
    {
        // G347 AC: when the project config has no BaseBranchPolicy the default
        // (`direct-main` → `main`) is stamped into the generated body.
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G347B"],
            writer);

        Assert.Equal(0, exitCode);
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G347B");
        var githubBody = File.ReadAllText(Path.Combine(packetDir, "github-body.md"));

        Assert.Contains("Policy: `direct-main`", githubBody, StringComparison.Ordinal);
        Assert.Contains("Expected PR base branch: `main`", githubBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GeneratesOptionalIntentMaintenanceMetadata_WithoutAddingRequiredSections()
    {
        // G461 AC: new generated packet templates include the optional intent
        // maintenance sections/fields by default, but the metadata is OPTIONAL —
        // it must NOT be added to the required standalone-contract sections.
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G461", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G461");

        var packetYaml = File.ReadAllText(Path.Combine(packetDir, "packet.yaml"));
        foreach (var key in new[] { "intent_placement:", "knowledge_updates:", "adr:", "diagram:", "closeout_learning:", "write_back_required:" })
        {
            Assert.Contains(key, packetYaml, StringComparison.Ordinal);
        }

        var implementation = File.ReadAllText(Path.Combine(packetDir, "implementation.md"));
        Assert.Contains("Knowledge Maintenance", implementation, StringComparison.Ordinal);

        var githubBody = File.ReadAllText(Path.Combine(packetDir, "github-body.md"));
        Assert.Contains("## Knowledge Maintenance", githubBody, StringComparison.Ordinal);

        var reviewContext = File.ReadAllText(Path.Combine(packetDir, "review-context.md"));
        Assert.Contains("Knowledge Writeback Expectation", reviewContext, StringComparison.Ordinal);

        // Backward-compat invariant: the optional metadata is NOT a required section.
        Assert.DoesNotContain("Knowledge Maintenance", string.Join("\n", PacketDraftCommand.RequiredContractSections), StringComparison.Ordinal);
        Assert.DoesNotContain("intent_placement", string.Join("\n", PacketDraftCommand.RequiredContractSections), StringComparison.Ordinal);
    }

    private sealed class PacketDraftWorkspace : IDisposable
    {
        public PacketDraftWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("packet-draft-tests-").FullName;
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
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

        public string RepoRoot { get; }

        public CliContext Context { get; }

        /// <summary>G530: writes a facet-annotated intent-tree node under `intents/&lt;domain&gt;/&lt;relativePath&gt;`.</summary>
        public void WriteFacetNode(string relativePath, IReadOnlyList<string> facets, string title)
        {
            var fullPath = Path.Combine(
                RepoRoot, "intents", Context.Config.Project.Domain, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, $"---\nfacets: [{string.Join(", ", facets)}]\n---\n# {title}\n");
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
