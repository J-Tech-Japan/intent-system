using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G404 tests for <c>intent-cli intent add-feature</c>.</summary>
public sealed class IntentAddFeatureCommandTests
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

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(hostRoot, "intents", "auth", "features", "login")));

        var output = writer.ToString();
        Assert.Contains("--write", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Default add-feature
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithWrite_CreatesAllSevenFeatureFiles()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            writer);

        Assert.Equal(0, exitCode);
        var featureDir = Path.Combine(hostRoot, "intents", "auth", "features", "login");

        foreach (var fileName in IntentAddFeatureCommand.FeatureFiles)
        {
            Assert.True(File.Exists(Path.Combine(featureDir, fileName)),
                $"Expected feature file '{fileName}' to exist.");
        }
    }

    [Fact]
    public void Execute_WithWrite_CreatesOrUpdatesFeatureIndex()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            writer);

        var indexPath = Path.Combine(hostRoot, "intents", "auth", "features", "index.md");
        Assert.True(File.Exists(indexPath));

        var indexContent = File.ReadAllText(indexPath);
        Assert.Contains("login", indexContent, StringComparison.Ordinal);
        Assert.Contains("login/overview.md", indexContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithWrite_SecondFeature_AppendsToExistingIndex()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        // First feature
        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);

        // Second feature
        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "mfa", "--write"],
            TextWriter.Null);

        var indexPath = Path.Combine(hostRoot, "intents", "auth", "features", "index.md");
        var indexContent = File.ReadAllText(indexPath);
        Assert.Contains("login/overview.md", indexContent, StringComparison.Ordinal);
        Assert.Contains("mfa/overview.md", indexContent, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Cross-linking content
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithWrite_OverviewContainsCrossLinks()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);

        var overview = File.ReadAllText(
            Path.Combine(hostRoot, "intents", "auth", "features", "login", "overview.md"));

        Assert.Contains("requirements.md", overview, StringComparison.Ordinal);
        Assert.Contains("acceptance.md", overview, StringComparison.Ordinal);
        Assert.Contains("decisions.md", overview, StringComparison.Ordinal);
        Assert.Contains("open-questions.md", overview, StringComparison.Ordinal);
        Assert.Contains("packets.md", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithWrite_OpenQuestionsLinksBackToClarifications()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);

        var openQuestions = File.ReadAllText(
            Path.Combine(hostRoot, "intents", "auth", "features", "login", "open-questions.md"));

        Assert.Contains("clarifications", openQuestions, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithWrite_PacketsFileLinksBackToPacketsFolder()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);

        var packets = File.ReadAllText(
            Path.Combine(hostRoot, "intents", "auth", "features", "login", "packets.md"));

        Assert.Contains("packets", packets, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Idempotency
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_WithWrite_Idempotent_DoesNotOverwriteExistingFeatureFiles()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        // First run: creates files
        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);

        // Modify a feature file
        var overviewPath = Path.Combine(hostRoot, "intents", "auth", "features", "login", "overview.md");
        File.WriteAllText(overviewPath, "# Custom overview");

        // Second run: must not overwrite
        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);

        Assert.Equal("# Custom overview", File.ReadAllText(overviewPath));
    }

    [Fact]
    public void Execute_SecondRun_ReportsExistingFiles_WrittenCountZero()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        // First run
        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);

        // Second run
        using var writer = new StringWriter();
        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var json = writer.ToString();
        var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        var written = result.GetProperty("written_paths").GetArrayLength();
        Assert.Equal(0, written);
    }

    [Fact]
    public void Execute_Idempotent_IndexNotDuplicated()
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");

        // Run twice
        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);
        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            TextWriter.Null);

        var indexPath = Path.Combine(hostRoot, "intents", "auth", "features", "index.md");
        var content = File.ReadAllText(indexPath);
        // Count occurrences of login/overview.md — should be exactly one
        var occurrences = content.Split("login/overview.md").Length - 1;
        Assert.Equal(1, occurrences);
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

        IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", "login", "--format", "json"],
            writer);

        var json = writer.ToString();
        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        Assert.Equal("auth", element.GetProperty("domain").GetString());
        Assert.Equal("login", element.GetProperty("feature_name").GetString());
    }

    // ──────────────────────────────────────────────
    // Argument validation
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_MissingDomain_ReturnsExitCode1()
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(tmp.CreateDirectory("host")),
            ["--name", "login"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingName_ReturnsExitCode1()
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(tmp.CreateDirectory("host")),
            ["--domain", "auth"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--name", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Help_ReturnsZeroAndUsage()
    {
        using var tmp = new TemporaryDirectory();
        using var writer = new StringWriter();

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(tmp.CreateDirectory("host")),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("add-feature", output, StringComparison.Ordinal);
        Assert.Contains("--domain", output, StringComparison.Ordinal);
        Assert.Contains("--name", output, StringComparison.Ordinal);
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

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", badDomain, "--name", "login", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("slug", output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(hostRoot, "intents")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("feature/../../other")]
    [InlineData("auth domain")]
    [InlineData("login!")]
    public void Execute_InvalidFeatureNameSlug_ReturnsExitCode1_AndCreatesNoFiles(string badName)
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--name", badName, "--write"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("slug", output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(hostRoot, "intents")));
    }

    [Theory]
    [InlineData("auth", "login")]
    [InlineData("my-domain", "user-auth")]
    [InlineData("domain_v2", "feature_1")]
    public void Execute_ValidSlugs_ReturnsZero(string domain, string featureName)
    {
        using var tmp = new TemporaryDirectory();
        var hostRoot = tmp.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", domain, "--name", featureName, "--write"],
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
        Assert.False(IntentAddFeatureCommand.IsValidSlug(value));
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("my-feature")]
    [InlineData("feature_v2")]
    public void IsValidSlug_AcceptsSafeValues(string value)
    {
        Assert.True(IntentAddFeatureCommand.IsValidSlug(value));
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

        var exitCode = IntentAddFeatureCommand.Execute(
            CreateContext(childRoot),
            ["--domain", "auth", "--name", "login", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("child worktree", writer.ToString(), StringComparison.OrdinalIgnoreCase);
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
            Directory.CreateTempSubdirectory("intent-cli-add-feature-tests-").FullName;

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
