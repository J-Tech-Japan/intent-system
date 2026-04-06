using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeFoldinCommandTests
{
    [Fact]
    public void Execute_GivenCompileArtifact_WritesFoldinDraft()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.compile.md"),
            """
            # Intake Compile

            ## Domain

            `auth`

            answered_question_ids:
            - iq-1
            - iq-2

            recommended_updates:
            - Add device-code note

            return_to_intent_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeFoldinCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake fold-in draft generated for domain 'auth'.", output, StringComparison.Ordinal);
        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.foldin.md");
        Assert.True(File.Exists(artifactPath));
        var markdown = File.ReadAllText(artifactPath);
        Assert.Contains("# Intake Fold-In Draft", markdown, StringComparison.Ordinal);
        Assert.Contains("answered_question_ids:", markdown, StringComparison.Ordinal);
        Assert.Contains("- iq-1", markdown, StringComparison.Ordinal);
        Assert.Contains("recommended_updates:", markdown, StringComparison.Ordinal);
        Assert.Contains("- Add device-code note", markdown, StringComparison.Ordinal);
        Assert.Contains("return_to_intent_paths:", markdown, StringComparison.Ordinal);
        Assert.Contains("source_concept_refs:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingCompileArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IntakeFoldinCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake compile artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMismatchedCompileArtifactDomain_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.compile.md"),
            """
            # Intake Compile

            ## Domain

            `payments`

            answered_question_ids:
            - iq-1

            recommended_updates:
            - Add device-code note

            return_to_intent_paths:
            - intents/intent-cli/intent-tree/means/payments.md

            source_concept_refs:
            - intents/intent-cli/concepts/payments.md
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeFoldinCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not match requested domain", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.foldin.md")));
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeFoldinCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-foldin-command-tests-").FullName;

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
