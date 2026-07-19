using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G405 tests for <c>intent-cli intent analyze-tree</c>.</summary>
public sealed class IntentAnalyzeTreeCommandTests
{
    // ──────────────────────────────────────────────
    // Dry-run (no --write)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithoutWrite_PerformsDryRun_AndCreatesNoFiles()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateFlatFile(hostRoot, "auth", "intent.md",
            "# Auth intent\n\n## Mission\nWe auth.\n\n## Features\nLogin stuff.");
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth"],
            writer);

        Assert.Equal(0, exitCode);
        // No destination files created
        Assert.False(Directory.Exists(Path.Combine(hostRoot, "intents", "auth", "identity")));
        Assert.False(Directory.Exists(Path.Combine(hostRoot, "intents", "auth", ".restructure-backup")));

        var output = writer.ToString();
        Assert.Contains("dry-run", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--write", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithoutWrite_OutputsProposals()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateFlatFile(hostRoot, "auth", "intent.md",
            "# Auth\n\n## Mission\nWe auth.\n\n## Features\nLogin stuff.");
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Proposed relocations", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mission", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Write mode
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithWrite_CreatesBackupCopy()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateFlatFile(hostRoot, "auth", "intent.md",
            "# Auth\n\n## Mission\nWe auth.");
        using var writer = new StringWriter();

        IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            writer);

        var backupPath = Path.Combine(hostRoot, "intents", "auth", ".restructure-backup", "intent.md");
        Assert.True(File.Exists(backupPath), "Backup copy should exist.");
    }

    [Fact]
    public void Execute_WithWrite_CreatesDestinationStubFiles()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateFlatFile(hostRoot, "auth", "intent.md",
            "# Auth\n\n## Mission\nWe auth.\n\n## Features\nLogin stuff.");
        using var writer = new StringWriter();

        IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            writer);

        // At least one destination stub should have been written
        var output = writer.ToString();
        Assert.Contains("Destination stubs created", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithWrite_IsNonDestructive_OriginalFileUnchanged()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        const string originalContent = "# Auth\n\n## Mission\nOriginal content.";
        CreateFlatFile(hostRoot, "auth", "intent.md", originalContent);

        IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            TextWriter.Null);

        var original = File.ReadAllText(Path.Combine(hostRoot, "intents", "auth", "intent.md"));
        Assert.Equal(originalContent, original);
    }

    [Fact]
    public void Execute_WithWrite_IdempotentBackup_DoesNotOverwriteExistingBackup()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateFlatFile(hostRoot, "auth", "intent.md", "# Auth\n\n## Mission\nV1.");

        // First run: creates backup
        IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            TextWriter.Null);

        // Modify the source file
        File.WriteAllText(
            Path.Combine(hostRoot, "intents", "auth", "intent.md"),
            "# Auth\n\n## Mission\nV2.");

        // Second run: should NOT overwrite existing backup
        IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            TextWriter.Null);

        var backupContent = File.ReadAllText(
            Path.Combine(hostRoot, "intents", "auth", ".restructure-backup", "intent.md"));
        Assert.Contains("V1", backupContent, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Empty / missing domain
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_MissingDomainDirectory_ReturnsZeroAndReportsNoFiles()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "nonexistent"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("flat-files-analyzed: 0", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DomainWithNoFlatFiles_ReturnsZeroAndReportsNoProposals()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        // Create a tree-structured domain (no flat markdown files at top level)
        Directory.CreateDirectory(Path.Combine(hostRoot, "intents", "auth", "identity"));
        File.WriteAllText(Path.Combine(hostRoot, "intents", "auth", "identity", "mission.md"), "# Mission");

        using var writer = new StringWriter();
        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("proposals: 0", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Reference detection
    // ──────────────────────────────────────────────

    [Fact]
    public void ExtractReferences_DetectsMarkdownLinks()
    {
        var content = "See [overview](../product/overview.md) for details.";
        var refs = IntentAnalyzeTreeCommand.ExtractReferences(content, "intents/auth/intent.md");
        Assert.Contains(refs, r => r.Kind == "markdown-link" && r.Value.Contains("overview.md"));
    }

    [Fact]
    public void ExtractReferences_DetectsExecutionUnitIds()
    {
        var content = "This implements G403 and G404 patterns.";
        var refs = IntentAnalyzeTreeCommand.ExtractReferences(content, "intents/auth/intent.md");
        Assert.Contains(refs, r => r.Kind == "execution-unit-id" && r.Value == "G403");
        Assert.Contains(refs, r => r.Kind == "execution-unit-id" && r.Value == "G404");
    }

    [Fact]
    public void ExtractReferences_DetectsGitHubUrls()
    {
        var content = "See https://github.com/J-Tech-Japan/intent-system/issues/909 for tracking.";
        var refs = IntentAnalyzeTreeCommand.ExtractReferences(content, "intents/auth/intent.md");
        Assert.Contains(refs, r => r.Kind == "github-url" && r.Value.Contains("/issues/909"));
    }

    [Fact]
    public void ExtractReferences_DetectsHeadingAnchors()
    {
        var content = "# My Section\n## Sub Section";
        var refs = IntentAnalyzeTreeCommand.ExtractReferences(content, "intents/auth/intent.md");
        Assert.Contains(refs, r => r.Kind == "heading-anchor");
    }

    [Fact]
    public void ExtractReferences_DetectsPacketPaths()
    {
        var content = "See intents/auth/packets/wave-1.md for details.";
        var refs = IntentAnalyzeTreeCommand.ExtractReferences(content, "intents/auth/intent.md");
        Assert.Contains(refs, r => r.Kind == "packet-path" && r.Value.Contains("packets/wave-1.md"));
    }

    // ──────────────────────────────────────────────
    // Heading extraction
    // ──────────────────────────────────────────────

    [Fact]
    public void ExtractHeadings_ExtractsH2AndH3()
    {
        var content = "# Title\n\n## Section A\n\ntext\n\n### Sub B\n\nmore text\n\n## Section C";
        var headings = IntentAnalyzeTreeCommand.ExtractHeadings(content);
        Assert.Equal(3, headings.Count);
        Assert.Equal("Section A", headings[0].Text);
        Assert.Equal(2, headings[0].Level);
        Assert.Equal("Sub B", headings[1].Text);
        Assert.Equal(3, headings[1].Level);
        Assert.Equal("Section C", headings[2].Text);
    }

    // ──────────────────────────────────────────────
    // Category destination suggestion
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("Mission statement", "identity/")]
    [InlineData("Feature: Login", "features/")]
    [InlineData("Technology stack", "technology/")]
    [InlineData("Open questions", "clarifications/")]
    [InlineData("Design decisions", "decisions/")]
    public void SuggestDestination_ReturnsExpectedCategory(string heading, string expectedPrefix)
    {
        var headingInfo = new IntentAnalyzeTreeCommand.HeadingInfo(heading, 2, 0);
        var destination = IntentAnalyzeTreeCommand.SuggestDestination("auth", headingInfo);
        Assert.StartsWith(expectedPrefix, destination, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // SlugifyHeading
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("Mission Statement", "mission-statement")]
    [InlineData("Feature: Login", "feature-login")]
    [InlineData("Open Q&A", "open-qa")]
    public void SlugifyHeading_ProducesExpectedSlug(string input, string expected)
    {
        var slug = IntentAnalyzeTreeCommand.SlugifyHeading(input);
        Assert.Equal(expected, slug);
    }

    // ──────────────────────────────────────────────
    // JSON output
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_FormatJson_OutputIsValidJson()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateFlatFile(hostRoot, "auth", "intent.md", "# Auth\n\n## Mission\nWe auth.");
        using var writer = new StringWriter();

        IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--format", "json"],
            writer);

        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(writer.ToString());
        Assert.Equal("auth", element.GetProperty("domain").GetString());
        Assert.False(element.GetProperty("write_applied").GetBoolean());
    }

    // ──────────────────────────────────────────────
    // Argument validation
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_MissingDomain_ReturnsExitCode1()
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(tmp.CreateDirectory("host")),
            [],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("auth domain")]
    public void Execute_InvalidDomainSlug_ReturnsExitCode1(string badDomain)
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(tmp.CreateDirectory("host")),
            ["--domain", badDomain],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("slug", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    // Child worktree refusal
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_InsideChildWorktree_ReturnsExitCode1()
    {
        using var tmp = new TemporaryDirectory();
        var childRoot = tmp.CreateDirectory(".intent-cli/worktrees/some-branch");
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(childRoot),
            ["--domain", "auth"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("child worktree", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    // Facet coverage (G529)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_NodesWithFacets_ReportsPerFacetCounts()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        var featuresDir = Path.Combine(hostRoot, "intents", "auth", "features", "login");
        Directory.CreateDirectory(featuresDir);
        File.WriteAllText(
            Path.Combine(featuresDir, "overview.md"),
            "---\nfacets: [vocabulary, invariant]\n---\n# Login overview\n");
        File.WriteAllText(
            Path.Combine(featuresDir, "decisions.md"),
            "---\nfacets: [invariant]\n---\n# Login decisions\n");
        File.WriteAllText(
            Path.Combine(featuresDir, "requirements.md"),
            "# Login requirements\n\nNo facets frontmatter.\n");
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = System.Text.Json.JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("facet_coverage");
        Assert.Equal(1, coverage.GetProperty("vocabulary").GetInt32());
        Assert.Equal(2, coverage.GetProperty("invariant").GetInt32());
        // "decider" and "acceptance-property" have zero nodes — only
        // positive counts are reported, so they must be absent entirely.
        Assert.False(coverage.TryGetProperty("decider", out _));
        Assert.False(coverage.TryGetProperty("acceptance-property", out _));
    }

    [Fact]
    public void Execute_BlockFormFacetsAndDuplicateValues_CountedConsistentlyWithFlowForm()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        var featuresDir = Path.Combine(hostRoot, "intents", "auth", "features", "login");
        Directory.CreateDirectory(featuresDir);
        // Block form.
        File.WriteAllText(
            Path.Combine(featuresDir, "overview.md"),
            "---\nfacets:\n  - vocabulary\n  - invariant\n---\n# Login overview\n");
        // Flow form with a duplicate value — a duplicate within ONE node
        // must count as ONE occurrence of that facet, not two.
        File.WriteAllText(
            Path.Combine(featuresDir, "decisions.md"),
            "---\nfacets: [invariant, invariant]\n---\n# Login decisions\n");
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = System.Text.Json.JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("facet_coverage");
        Assert.Equal(1, coverage.GetProperty("vocabulary").GetInt32());
        // One from overview.md (block form) + one from decisions.md (the
        // duplicate collapses to a single occurrence) = 2, not 3.
        Assert.Equal(2, coverage.GetProperty("invariant").GetInt32());
    }

    [Fact]
    public void Execute_MalformedFacetsNode_ExcludedFromCoverage_NotCounted()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        var identityDir = Path.Combine(hostRoot, "intents", "auth", "identity");
        Directory.CreateDirectory(identityDir);
        File.WriteAllText(
            Path.Combine(identityDir, "mission.md"),
            "---\nfacets: invariant\n---\n# Mission\n");
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = System.Text.Json.JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("facet_coverage");
        Assert.False(coverage.TryGetProperty("invariant", out _));
    }

    [Fact]
    public void Execute_NoNodesWithFacets_FacetCoverageIsEmpty()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateFlatFile(hostRoot, "auth", "intent.md", "# Auth\n\n## Mission\nWe auth.");
        using var writer = new StringWriter();

        var exitCode = IntentAnalyzeTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = System.Text.Json.JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("facet_coverage");
        Assert.Equal(0, coverage.EnumerateObject().Count());
    }

    [Fact]
    public void Execute_Markdown_RendersFacetCoverageSection()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        var identityDir = Path.Combine(hostRoot, "intents", "auth", "identity");
        Directory.CreateDirectory(identityDir);
        File.WriteAllText(
            Path.Combine(identityDir, "mission.md"),
            "---\nfacets: [decider]\n---\n# Mission\n");
        using var writer = new StringWriter();

        IntentAnalyzeTreeCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        var output = writer.ToString();
        Assert.Contains("## Facet coverage", output, StringComparison.Ordinal);
        Assert.Contains("decider: 1", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static void CreateFlatFile(string hostRoot, string domain, string fileName, string content)
    {
        var dir = Path.Combine(hostRoot, "intents", domain);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    private static CliContext CreateContext(string repoRoot) =>
        new()
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "bootstrap",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath =
            Directory.CreateTempSubdirectory("intent-cli-analyze-tree-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
