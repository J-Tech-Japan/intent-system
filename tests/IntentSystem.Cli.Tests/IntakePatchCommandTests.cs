using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakePatchCommandTests
{
    [Fact]
    public void Execute_GivenFoldinArtifactAndParentFiles_WritesPatchDraft()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.foldin.md"),
            """
            # Intake Fold-In Draft

            ## Domain

            `auth`

            ## Interview Coverage

            answered_question_ids:
            - iq-1
            - iq-2

            ## Parent Source-Of-Truth Update Candidates

            recommended_updates:
            - Add device-code note
            - Document OAuth2 fallback

            return_to_intent_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + "Existing line");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept" + Environment.NewLine + "Known flow");
        using var writer = new StringWriter();

        var exitCode = IntakePatchCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake patch draft generated for domain 'auth'.", output, StringComparison.Ordinal);
        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.patch.md");
        Assert.True(File.Exists(artifactPath));
        var markdown = File.ReadAllText(artifactPath);
        Assert.Contains("target_file_paths:", markdown, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/concepts/auth-oauth2.md", markdown, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/intent-tree/means/auth-oauth2.md", markdown, StringComparison.Ordinal);
        Assert.Contains("### `intents/intent-cli/intent-tree/means/auth-oauth2.md`", markdown, StringComparison.Ordinal);
        Assert.Contains("current_file_state: present", markdown, StringComparison.Ordinal);
        Assert.Contains("recommended_updates:Add device-code note", markdown, StringComparison.Ordinal);
        Assert.Contains("Apply update candidate: Add device-code note", markdown, StringComparison.Ordinal);
        Assert.Contains("# Auth Means", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingParentFile_RendersMissingFileState()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.foldin.md"),
            """
            # Intake Fold-In Draft

            ## Domain

            `auth`

            answered_question_ids:
            - iq-1

            recommended_updates:
            - Add device-code note

            return_to_intent_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            """);
        using var writer = new StringWriter();

        var exitCode = IntakePatchCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var markdown = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.patch.md"));
        Assert.Contains("current_file_state: missing", markdown, StringComparison.Ordinal);
        Assert.Contains("[missing]", markdown, StringComparison.Ordinal);
        Assert.Contains("Draft this target file as a new parent source-of-truth entry if creation is intended.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingFoldinArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IntakePatchCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake fold-in artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakePatchCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-patch-command-tests-").FullName;

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
