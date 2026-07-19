using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G404 tests for <c>intent-cli intent init-tree</c>.</summary>
public sealed class IntentInitTreeCommandTests
{
    // ──────────────────────────────────────────────
    // Dry-run (no --write)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithoutWrite_PerformsDryRun_AndCreatesNoFiles()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--target-repo", "owner/repo"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(hostRoot, "intents", "auth")));

        var output = writer.ToString();
        Assert.Contains("dry-run", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--write", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Default init (product-app)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithWrite_DefaultProjectType_CreatesExpectedFiles()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--target-repo", "owner/repo", "--write"],
            writer);

        Assert.Equal(0, exitCode);

        var domainRoot = Path.Combine(hostRoot, "intents", "auth");
        Assert.True(File.Exists(Path.Combine(domainRoot, "manifest.yaml")));
        Assert.True(File.Exists(Path.Combine(domainRoot, "README.md")));
        Assert.True(File.Exists(Path.Combine(domainRoot, "identity", "mission.md")));
    }

    [Fact]
    public void Execute_WithWrite_DefaultProjectType_CreatesAllRecommendedCategories()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            writer);

        var domainRoot = Path.Combine(hostRoot, "intents", "auth");
        // product-app expected categories
        foreach (var cat in new[] { "identity", "product", "features", "technology", "operations", "decisions", "clarifications", "packets", "links" })
        {
            Assert.True(
                File.Exists(Path.Combine(domainRoot, cat, ".gitkeep")),
                $"Expected .gitkeep in category '{cat}'");
        }
    }

    [Fact]
    public void Execute_WithWrite_ManifestContainsTreeV1AndProjectType()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--target-repo", "owner/repo", "--write"],
            writer);

        var manifest = File.ReadAllText(Path.Combine(hostRoot, "intents", "auth", "manifest.yaml"));
        Assert.Contains("layout_version: tree-v1", manifest, StringComparison.Ordinal);
        Assert.Contains("project_type: product-app", manifest, StringComparison.Ordinal);
        Assert.Contains("target_repo: owner/repo", manifest, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // G441 first-run automation bindings scaffold
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_G441_WithWrite_CreatesAutomationBindings_WithPermissiveRegexAndChildRepo()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--target-repo", "owner/repo", "--write"],
            writer);

        var bindingsPath = Path.Combine(hostRoot, "intents", "auth", "automation", "bindings.md");
        Assert.True(File.Exists(bindingsPath), "init-tree must scaffold automation/bindings.md");

        var bindings = File.ReadAllText(bindingsPath);
        Assert.Contains("execution_unit_regex: .*", bindings, StringComparison.Ordinal);
        Assert.Contains("child_repo: owner/repo", bindings, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G441_BindingsScaffold_IsRecognizedByNextSliceResolver_AsPresent()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();
        var context = CreateContext(hostRoot);

        IntentInitTreeCommand.Execute(
            context,
            ["--domain", "auth", "--target-repo", "owner/repo", "--write"],
            writer);

        // The first-run deadlock was next-slice reporting `missing-domain-bindings`
        // after init/init-tree. With the scaffold present the resolver must report
        // a compiled, Present execution_unit_regex instead.
        var resolution = NextSliceDomainBindingsExecutionUnitRegex.Resolve(context, "auth");
        Assert.Equal(ExecutionUnitRegexResolutionKind.Present, resolution.Kind);
        Assert.NotNull(resolution.Regex);
        Assert.True(resolution.Regex!.IsMatch("any-execution-unit-id"));
    }

    [Fact]
    public void Execute_G441_NoTargetRepo_OmitsChildRepoField_ButKeepsRegex()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            writer);

        var bindings = File.ReadAllText(
            Path.Combine(hostRoot, "intents", "auth", "automation", "bindings.md"));
        Assert.Contains("execution_unit_regex: .*", bindings, StringComparison.Ordinal);
        // No placeholder child_repo value that downstream analyzers would treat as real.
        Assert.DoesNotContain("child_repo: owner/repo", bindings, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G441_BindingsScaffold_IsIdempotent_NotOverwritten()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        var bindingsPath = Path.Combine(hostRoot, "intents", "auth", "automation", "bindings.md");
        Directory.CreateDirectory(Path.GetDirectoryName(bindingsPath)!);
        File.WriteAllText(bindingsPath, "execution_unit_regex: ^auth-\n");

        using var writer = new StringWriter();
        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            writer);

        // Existing operator-authored bindings must be preserved.
        Assert.Equal("execution_unit_regex: ^auth-\n", File.ReadAllText(bindingsPath));
    }

    // ──────────────────────────────────────────────
    // Custom project types
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("library-tool", "api", "users")]
    [InlineData("infrastructure", "environments", "runbooks")]
    [InlineData("research-prototype", "hypothesis", "experiments")]
    public void Execute_WithWrite_CustomProjectType_CreatesTypeSpecificCategories(
        string projectType, string expectedCat1, string expectedCat2)
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "sdk", "--project-type", projectType, "--write"],
            writer);

        var domainRoot = Path.Combine(hostRoot, "intents", "sdk");
        Assert.True(File.Exists(Path.Combine(domainRoot, expectedCat1, ".gitkeep")),
            $"Expected .gitkeep in '{expectedCat1}' for project-type '{projectType}'");
        Assert.True(File.Exists(Path.Combine(domainRoot, expectedCat2, ".gitkeep")),
            $"Expected .gitkeep in '{expectedCat2}' for project-type '{projectType}'");
    }

    [Fact]
    public void Execute_WithWrite_LibraryTool_HasApiAndUsers_NotProduct()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "sdk", "--project-type", "library-tool", "--write"],
            writer);

        var domainRoot = Path.Combine(hostRoot, "intents", "sdk");
        Assert.True(File.Exists(Path.Combine(domainRoot, "api", ".gitkeep")));
        Assert.True(File.Exists(Path.Combine(domainRoot, "users", ".gitkeep")));
        Assert.False(File.Exists(Path.Combine(domainRoot, "product", ".gitkeep")));
        Assert.False(File.Exists(Path.Combine(domainRoot, "links", ".gitkeep")));
    }

    // ──────────────────────────────────────────────
    // Idempotency
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithWrite_Idempotent_DoesNotOverwriteExistingFiles()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer1 = new StringWriter();
        using var writer2 = new StringWriter();

        // First run: creates files
        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            writer1);

        // Modify a file
        var missionPath = Path.Combine(hostRoot, "intents", "auth", "identity", "mission.md");
        File.WriteAllText(missionPath, "# Custom mission content");

        // Second run: must not overwrite
        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            writer2);

        Assert.Equal("# Custom mission content", File.ReadAllText(missionPath));

        var output2 = writer2.ToString();
        Assert.Contains("[existing]", output2, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_SecondRun_ReportsExistingNotCreated()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        // First run
        IntentInitTreeCommand.Execute(CreateContext(hostRoot), ["--domain", "auth", "--write"], TextWriter.Null);

        // Second run
        using var writer = new StringWriter();
        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var json = writer.ToString();
        var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        var written = result.GetProperty("written_paths").GetArrayLength();
        Assert.Equal(0, written);
    }

    // ──────────────────────────────────────────────
    // JSON output
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_FormatJson_OutputIsValidJson()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--format", "json"],
            writer);

        var json = writer.ToString();
        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        Assert.Equal("auth", element.GetProperty("domain").GetString());
        Assert.Equal("product-app", element.GetProperty("project_type").GetString());
    }

    // ──────────────────────────────────────────────
    // Argument validation
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_MissingDomain_ReturnExitCode1()
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(tmp.CreateDirectory("host")),
            ["--target-repo", "owner/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownProjectType_ReturnsExitCode1()
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(tmp.CreateDirectory("host")),
            ["--domain", "auth", "--project-type", "bogus-type"],
            writer);

        Assert.Equal(1, exitCode);
    }

    // ──────────────────────────────────────────────
    // Slug validation — path-traversal safety
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("../outside")]
    [InlineData("feature/../../other")]
    [InlineData("auth domain")]
    [InlineData("auth!")]
    [InlineData("../etc/passwd")]
    public void Execute_InvalidDomainSlug_ReturnsExitCode1_AndCreatesNoFiles(string badDomain)
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", badDomain, "--write"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("slug", output, StringComparison.OrdinalIgnoreCase);
        // No files must have been created
        Assert.False(Directory.Exists(Path.Combine(hostRoot, "intents")));
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("my-domain")]
    [InlineData("domain_v2")]
    [InlineData("Auth123")]
    public void Execute_ValidDomainSlug_ReturnsZero(string goodDomain)
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", goodDomain, "--write"],
            writer);

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("feature/../../other")]
    [InlineData("auth domain")]
    [InlineData("auth!")]
    public void IsValidSlug_RejectsUnsafeValues(string value)
    {
        Assert.False(IntentInitTreeCommand.IsValidSlug(value));
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("my-domain")]
    [InlineData("domain_v2")]
    public void IsValidSlug_AcceptsSafeValues(string value)
    {
        Assert.True(IntentInitTreeCommand.IsValidSlug(value));
    }

    [Fact]
    public void Execute_Help_ReturnsZeroAndUsage()
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(tmp.CreateDirectory("host")),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("init-tree", output, StringComparison.Ordinal);
        Assert.Contains("--domain", output, StringComparison.Ordinal);
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

        var exitCode = IntentInitTreeCommand.Execute(
            CreateContext(childRoot),
            ["--domain", "auth", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("child worktree", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    // ResolveCategories unit tests
    // ──────────────────────────────────────────────

    [Fact]
    public void ResolveCategories_ProductApp_IncludesAllNineCategories()
    {
        var categories = IntentInitTreeCommand.ResolveCategories(IntentInitTreeCommand.ProjectTypeProductApp);
        var keys = categories.Select(c => c.Key).ToList();
        Assert.Contains("identity", keys);
        Assert.Contains("product", keys);
        Assert.Contains("features", keys);
        Assert.Contains("technology", keys);
        Assert.Contains("operations", keys);
        Assert.Contains("decisions", keys);
        Assert.Contains("clarifications", keys);
        Assert.Contains("packets", keys);
        Assert.Contains("links", keys);
        Assert.Equal(9, categories.Count);
    }

    [Fact]
    public void ResolveCategories_LibraryTool_HasApiAndUsersNotProductOrLinks()
    {
        var categories = IntentInitTreeCommand.ResolveCategories(IntentInitTreeCommand.ProjectTypeLibraryTool);
        var keys = categories.Select(c => c.Key).ToList();
        Assert.Contains("api", keys);
        Assert.Contains("users", keys);
        Assert.DoesNotContain("product", keys);
        Assert.DoesNotContain("links", keys);
    }

    // ──────────────────────────────────────────────
    // Facets scaffold comment (G529)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithWrite_MissionStarterIncludesCommentedFacetsExample_AndLintStaysClean()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        IntentInitTreeCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            TextWriter.Null);

        var missionContent = File.ReadAllText(Path.Combine(hostRoot, "intents", "auth", "identity", "mission.md"));
        // Present as a comment (explaining all four values), not a live field.
        Assert.Contains("# facets: [vocabulary]", missionContent, StringComparison.Ordinal);
        Assert.Contains("vocabulary", missionContent, StringComparison.Ordinal);
        Assert.Contains("invariant", missionContent, StringComparison.Ordinal);
        Assert.Contains("decider", missionContent, StringComparison.Ordinal);
        Assert.Contains("acceptance-property", missionContent, StringComparison.Ordinal);
        Assert.DoesNotContain("\nfacets:", missionContent, StringComparison.Ordinal);

        using var lintWriter = new StringWriter();
        IntentLintLayoutCommand.Execute(CreateContext(hostRoot), ["--domain", "auth"], lintWriter);
        Assert.DoesNotContain("INVALID-FACET", lintWriter.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

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
            Directory.CreateTempSubdirectory("intent-cli-init-tree-tests-").FullName;

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
