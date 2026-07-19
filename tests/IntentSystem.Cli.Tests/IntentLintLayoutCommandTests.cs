using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G405 tests for <c>intent-cli intent lint-layout</c>.</summary>
public sealed class IntentLintLayoutCommandTests
{
    // ──────────────────────────────────────────────
    // Clean tree domain
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_CleanTreeDomain_ReportsNoWarnings()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");
        using var writer = new StringWriter();

        var exitCode = IntentLintLayoutCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("clean", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No layout problems detected", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[MISSING-", output, StringComparison.Ordinal);
        Assert.DoesNotContain("[LARGE-", output, StringComparison.Ordinal);
        Assert.DoesNotContain("[BROKEN-", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Missing manifest (flat domain)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_FlatDomainWithNoManifest_ReportsMissingManifestWarning()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        Directory.CreateDirectory(Path.Combine(hostRoot, "intents", "auth"));
        using var writer = new StringWriter();

        var exitCode = IntentLintLayoutCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("MISSING-MANIFEST", output, StringComparison.Ordinal);
        Assert.Contains("init-tree", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Missing domain directory
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_MissingDomainDirectory_ReportsMissingDomainWarning()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentLintLayoutCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "nonexistent"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("MISSING-DOMAIN", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Large flat file detection
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_LargeFlatFile_ReportsLargeFlatFileWarning()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        var domainDir = Path.Combine(hostRoot, "intents", "auth");
        Directory.CreateDirectory(domainDir);

        // Create a file larger than the threshold
        var largeContent = new string('x', IntentAnalyzeTreeCommand.LargeFlatFileBytesThreshold + 1);
        File.WriteAllText(Path.Combine(domainDir, "big-file.md"), largeContent);

        using var writer = new StringWriter();
        var exitCode = IntentLintLayoutCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("LARGE-FLAT-FILE", output, StringComparison.Ordinal);
        Assert.Contains("analyze-tree", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_SmallFlatFile_DoesNotReportLargeFlatFileWarning()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");

        // Add a small flat file (should not trigger warning)
        File.WriteAllText(
            Path.Combine(hostRoot, "intents", "auth", "small.md"),
            "# Small\nJust a bit of text.");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.DoesNotContain("LARGE-FLAT-FILE", writer.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Broken relative link detection
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_BrokenRelativeLink_ReportsBrokenLinkWarning()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");

        // Add a file with a broken relative link
        var identityDir = Path.Combine(hostRoot, "intents", "auth", "identity");
        Directory.CreateDirectory(identityDir);
        File.WriteAllText(
            Path.Combine(identityDir, "mission.md"),
            "# Mission\nSee [requirements](../nonexistent/requirements.md) for details.");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.Contains("BROKEN-RELATIVE-LINK", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ValidRelativeLink_DoesNotReportBrokenLink()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");

        var identityDir = Path.Combine(hostRoot, "intents", "auth", "identity");
        Directory.CreateDirectory(identityDir);
        // Create target file
        File.WriteAllText(Path.Combine(identityDir, "values.md"), "# Values");
        // Create file linking to it with a valid relative link
        File.WriteAllText(
            Path.Combine(identityDir, "mission.md"),
            "# Mission\nSee [values](values.md) for details.");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.DoesNotContain("BROKEN-RELATIVE-LINK", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AbsoluteHttpLink_DoesNotReportBrokenLink()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");

        var identityDir = Path.Combine(hostRoot, "intents", "auth", "identity");
        Directory.CreateDirectory(identityDir);
        File.WriteAllText(
            Path.Combine(identityDir, "mission.md"),
            "# Mission\nSee [GitHub](https://github.com/J-Tech-Japan/intent-system) for details.");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.DoesNotContain("BROKEN-RELATIVE-LINK", writer.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Features index check
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_FeaturesDirectoryWithoutIndex_ReportsMissingFeaturesIndex()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");

        // Create features dir without index
        Directory.CreateDirectory(Path.Combine(hostRoot, "intents", "auth", "features"));

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.Contains("MISSING-FEATURES-INDEX", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FeatureSubFolderWithoutOverview_ReportsMissingFeatureOverview()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");

        // Create feature subfolder without overview.md
        var featureDir = Path.Combine(hostRoot, "intents", "auth", "features", "login");
        Directory.CreateDirectory(featureDir);
        File.WriteAllText(Path.Combine(hostRoot, "intents", "auth", "features", "index.md"), "# Features");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.Contains("MISSING-FEATURE-OVERVIEW", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FeatureSubFolderWithOverview_DoesNotReportMissingOverview()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");

        var featureDir = Path.Combine(hostRoot, "intents", "auth", "features", "login");
        Directory.CreateDirectory(featureDir);
        File.WriteAllText(Path.Combine(featureDir, "overview.md"), "# Login overview");
        File.WriteAllText(Path.Combine(hostRoot, "intents", "auth", "features", "index.md"), "# Features");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.DoesNotContain("MISSING-FEATURE-OVERVIEW", writer.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Missing category folders
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_ManifestListsMissingCategoryFolder_ReportsWarning()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        var domainDir = Path.Combine(hostRoot, "intents", "auth");
        Directory.CreateDirectory(domainDir);

        // Manifest references a category folder that doesn't exist
        File.WriteAllText(Path.Combine(domainDir, "manifest.yaml"),
            "version: \"1\"\nlayout_version: tree-v1\nproject_type: product-app\ncategories:\n  identity: identity/\n  missing-cat: missing-cat/\n");

        // Create identity folder but not missing-cat
        Directory.CreateDirectory(Path.Combine(domainDir, "identity"));

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.Contains("MISSING-CATEGORY-FOLDER", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("missing-cat", writer.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Facets (G529)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_NodeWithValidFacets_ReportsNoInvalidFacetWarning()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");
        File.WriteAllText(
            Path.Combine(hostRoot, "intents", "auth", "identity", "mission.md"),
            "---\nfacets: [vocabulary, invariant]\n---\n# Mission\n");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        var output = writer.ToString();
        Assert.DoesNotContain("INVALID-FACET", output, StringComparison.Ordinal);
        Assert.Contains("clean", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_NodeWithUnknownFacetValue_ReportsInvalidFacetWarningNamingNodeValueAndAllowedSet()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");
        File.WriteAllText(
            Path.Combine(hostRoot, "intents", "auth", "identity", "mission.md"),
            "---\nfacets: [projection]\n---\n# Mission\n");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        var output = writer.ToString();
        Assert.Contains("INVALID-FACET", output, StringComparison.Ordinal);
        Assert.Contains("intents/auth/identity/mission.md", output, StringComparison.Ordinal);
        Assert.Contains("projection", output, StringComparison.Ordinal);
        Assert.Contains("vocabulary", output, StringComparison.Ordinal);
        Assert.Contains("invariant", output, StringComparison.Ordinal);
        Assert.Contains("decider", output, StringComparison.Ordinal);
        Assert.Contains("acceptance-property", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NodeWithMixOfValidAndInvalidFacets_ReportsOnlyTheInvalidOne()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");
        File.WriteAllText(
            Path.Combine(hostRoot, "intents", "auth", "identity", "mission.md"),
            "---\nfacets: [decider, projection]\n---\n# Mission\n");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        var output = writer.ToString();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(output, "INVALID-FACET"));
        Assert.Contains("projection", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NodeWithNoFacetsFrontmatter_StaysBackwardCompatible()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");
        // No frontmatter at all — the pre-G529 default shape.
        File.WriteAllText(
            Path.Combine(hostRoot, "intents", "auth", "identity", "mission.md"),
            "# Mission\n\nNo frontmatter here.\n");

        using var writer = new StringWriter();
        var exitCode = IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.DoesNotContain("INVALID-FACET", output, StringComparison.Ordinal);
        Assert.Contains("clean", output, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    // Mixed flat/tree compatibility
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_MixedFlatAndTreeDomain_LintsBothFlatAndTreeProblems()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");

        // Add a large flat file alongside the tree
        var largeContent = new string('x', IntentAnalyzeTreeCommand.LargeFlatFileBytesThreshold + 1);
        File.WriteAllText(Path.Combine(hostRoot, "intents", "auth", "legacy.md"), largeContent);

        // Add a feature folder without overview
        Directory.CreateDirectory(Path.Combine(hostRoot, "intents", "auth", "features", "mfa"));
        File.WriteAllText(Path.Combine(hostRoot, "intents", "auth", "features", "index.md"), "# Features");

        using var writer = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], writer);

        var output = writer.ToString();
        Assert.Contains("LARGE-FLAT-FILE", output, StringComparison.Ordinal);
        Assert.Contains("MISSING-FEATURE-OVERVIEW", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // JSON output
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_FormatJson_OutputIsValidJson()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        CreateMinimalTreeDomain(hostRoot, "auth");
        using var writer = new StringWriter();

        IntentLintLayoutCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--format", "json"],
            writer);

        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(writer.ToString());
        Assert.Equal("auth", element.GetProperty("domain").GetString());
        Assert.True(element.GetProperty("is_tree_domain").GetBoolean());
        Assert.True(element.GetProperty("clean").GetBoolean());
    }

    // ──────────────────────────────────────────────
    // Argument validation
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_MissingDomain_ReturnsExitCode1()
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentLintLayoutCommand.Execute(
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

        var exitCode = IntentLintLayoutCommand.Execute(
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

        var exitCode = IntentLintLayoutCommand.Execute(
            CreateContext(childRoot),
            ["--domain", "auth"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("child worktree", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static void CreateMinimalTreeDomain(string hostRoot, string domain)
    {
        var domainDir = Path.Combine(hostRoot, "intents", domain);
        Directory.CreateDirectory(domainDir);
        File.WriteAllText(Path.Combine(domainDir, "manifest.yaml"),
            "version: \"1\"\nlayout_version: tree-v1\nproject_type: product-app\ncategories:\n  identity: identity/\n");
        Directory.CreateDirectory(Path.Combine(domainDir, "identity"));
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
            Directory.CreateTempSubdirectory("intent-cli-lint-layout-tests-").FullName;

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
