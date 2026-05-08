using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntentInitCommandTests
{
    [Fact]
    public void Execute_WithoutWrite_PerformsDryRun_AndCreatesNoFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--target-repo", "owner/repo"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Path.Combine(hostRoot, ".intent-cli", "config.toml")));
        Assert.False(File.Exists(Path.Combine(hostRoot, "intents", "auth", "README.md")));

        var output = writer.ToString();
        Assert.Contains("Mode: dry-run", output, StringComparison.Ordinal);
        Assert.Contains("`.intent-cli/config.toml`", output, StringComparison.Ordinal);
        Assert.Contains("`intents/auth/README.md`", output, StringComparison.Ordinal);
        Assert.Contains("--write", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithWrite_CreatesScaffold_AndIsIdempotentOnSecondRun()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var firstWriter = new StringWriter();

        var firstExitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            firstWriter);

        Assert.Equal(0, firstExitCode);
        var configPath = Path.Combine(hostRoot, ".intent-cli", "config.toml");
        var readmePath = Path.Combine(hostRoot, "intents", "auth", "README.md");
        var clarificationsPath = Path.Combine(hostRoot, "intents", "auth", "clarifications", "open.md");
        var intentMapPath = Path.Combine(hostRoot, "intents", "auth", "intent-tree", "00-map.md");
        Assert.True(File.Exists(configPath));
        Assert.True(File.Exists(readmePath));
        Assert.True(File.Exists(clarificationsPath));
        Assert.True(File.Exists(intentMapPath));

        var configContent = File.ReadAllText(configPath);
        Assert.Contains("domain = \"auth\"", configContent, StringComparison.Ordinal);
        Assert.Contains("artifact_root = \".intent-cli\"", configContent, StringComparison.Ordinal);

        File.WriteAllText(readmePath, "operator override\n");

        using var secondWriter = new StringWriter();
        var secondExitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            secondWriter);

        Assert.Equal(0, secondExitCode);
        Assert.Equal("operator override\n", File.ReadAllText(readmePath));
        var secondOutput = secondWriter.ToString();
        Assert.Contains("Already initialized", secondOutput, StringComparison.Ordinal);
        Assert.Contains("Existing paths", secondOutput, StringComparison.Ordinal);
        Assert.Contains("`intents/auth/README.md`", secondOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FromChildWorktree_ReturnsExitCodeOne_WithGuidance()
    {
        using var tempDirectory = new TemporaryDirectory();
        var childRoot = tempDirectory.CreateDirectory(Path.Combine("host", ".intent-cli", "worktrees", "auth-2026"));
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(childRoot),
            ["--domain", "auth", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("automation child worktree", output, StringComparison.Ordinal);
        Assert.Contains("parent host repository", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(childRoot, ".intent-cli", "config.toml")));
    }

    [Fact]
    public void Execute_MissingDomain_ReturnsExitCodeOne_WithUsageHint()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--write"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("--domain", output, StringComparison.Ordinal);
        Assert.Contains("Usage: intent-cli intent init", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsInvalidDomainSlug()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "not a slug", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be a slug", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_ProducesParseableJson()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var json = writer.ToString().Trim();
        Assert.StartsWith("{", json, StringComparison.Ordinal);
        Assert.Contains("\"domain\": \"auth\"", json, StringComparison.Ordinal);
        Assert.Contains("\"write_applied\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"planned_paths\"", json, StringComparison.Ordinal);
        Assert.Contains("\"written_paths\"", json, StringComparison.Ordinal);
        Assert.Contains("\"existing_paths\"", json, StringComparison.Ordinal);
        Assert.Contains("\"next_steps\"", json, StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot)
    {
        return new CliContext
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
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intent-init-tests-").FullName;

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
