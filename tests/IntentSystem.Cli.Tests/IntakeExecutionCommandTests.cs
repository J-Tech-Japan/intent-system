using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeExecutionCommandTests
{
    [Fact]
    public void Execute_GivenUpdatedParentSourceFiles_WritesExecutionDraft()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept" + Environment.NewLine + "- Reconcile this source concept file with the current fold-in draft.");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + "- Add device-code note");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "payments.md"),
            "# Payments");
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake execution draft generated for domain 'auth'.", output, StringComparison.Ordinal);
        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.execution.md");
        Assert.True(File.Exists(artifactPath));
        var markdown = File.ReadAllText(artifactPath);
        Assert.Contains("### `AUTH-01`", markdown, StringComparison.Ordinal);
        Assert.Contains("### `AUTH-02`", markdown, StringComparison.Ordinal);
        Assert.Contains("source_file_path: intents/intent-cli/concepts/auth-oauth2.md", markdown, StringComparison.Ordinal);
        Assert.Contains("source_file_path: intents/intent-cli/intent-tree/means/auth-oauth2.md", markdown, StringComparison.Ordinal);
        Assert.Contains("target_part: concepts", markdown, StringComparison.Ordinal);
        Assert.Contains("target_part: intent-tree/means", markdown, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("payments.md", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoMatchingParentSourceFiles_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "payments.md"),
            "# Payments");
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No updated parent source files were found for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.execution.md")));
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
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
                    Domain = "intent-system",
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-execution-command-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(fullPath, contents);
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
