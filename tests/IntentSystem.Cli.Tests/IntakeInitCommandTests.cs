using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeInitCommandTests
{
    [Fact]
    public void Execute_GivenTextAndWorkRepoPath_ScaffoldsBootstrapAndConnectsExistingCommands()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        _ = tempDirectory.CreateDirectory("work-repo");
        using var writer = new StringWriter();

        var exitCode = IntakeInitCommand.Execute(
            CreateContext(repoRoot),
            ["billing", "--text", "Bootstrap billing intake.", "--work-repo-path", "../work-repo"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake init processed for domain 'billing'.", output, StringComparison.Ordinal);
        Assert.Contains("Generated paths:", output, StringComparison.Ordinal);
        Assert.Contains("Skipped paths:", output, StringComparison.Ordinal);

        var config = CliConfigLoader.LoadFromFile(Path.Combine(repoRoot, ".intent-cli", "config.toml"));
        Assert.Equal("billing", config.Project.Domain);
        Assert.Equal("../work-repo", config.Project.WorkRepoPath);

        var conceptPacket = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "intake", "billing.concept.yaml")));
        Assert.Equal("billing", conceptPacket.DomainSlug);
        Assert.Equal("text", conceptPacket.ConceptSource);
        Assert.Equal("Bootstrap billing intake.", conceptPacket.InitialGoal);

        Assert.True(File.Exists(Path.Combine(repoRoot, "intents", "billing", "README.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "intents", "billing", "clarifications", "open.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "intents", "billing", "intent-tree", "00-map.md")));

        using var statusWriter = new StringWriter();
        var statusExitCode = ProjectStatusCommand.Execute(
            new CliContext
            {
                RepoRoot = repoRoot,
                Config = config
            },
            [],
            statusWriter);
        Assert.Equal(0, statusExitCode);
        Assert.Contains("Work repo path:", statusWriter.ToString(), StringComparison.Ordinal);

        using var interviewWriter = new StringWriter();
        var interviewExitCode = IntakeInterviewCommand.Execute(CreateContext(repoRoot), ["billing"], interviewWriter);
        Assert.Equal(0, interviewExitCode);
        Assert.Contains("Bootstrap status: skipped", interviewWriter.ToString(), StringComparison.Ordinal);

        using var interviewStartWriter = new StringWriter();
        var interviewStartExitCode = InterviewStartCommand.Execute(CreateContext(repoRoot), ["billing"], interviewStartWriter);
        Assert.Equal(0, interviewStartExitCode);
        Assert.Contains("Question id: iq-goal", interviewStartWriter.ToString(), StringComparison.Ordinal);

        using var compileWriter = new StringWriter();
        var compileExitCode = IntakeCompileCommand.Execute(CreateContext(repoRoot), ["billing"], compileWriter);
        Assert.Equal(0, compileExitCode);
        Assert.Contains("Intake compile is not ready for domain 'billing'.", compileWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenFromFileInput_UsesFileContentsForConceptArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        _ = tempDirectory.CreateDirectory("work-repo");
        tempDirectory.CreateFile(Path.Combine("repo", "billing.txt"), "Bootstrap billing from file.");
        using var writer = new StringWriter();

        var exitCode = IntakeInitCommand.Execute(
            CreateContext(repoRoot),
            ["billing", "--from-file", "billing.txt", "--work-repo-path", "../work-repo"],
            writer);

        Assert.Equal(0, exitCode);
        var conceptPacket = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "intake", "billing.concept.yaml")));
        Assert.Equal("from-file", conceptPacket.ConceptSource);
        Assert.Equal("Bootstrap billing from file.", conceptPacket.ConceptText);
    }

    [Fact]
    public void Execute_GivenExistingFiles_SkipsWithoutOverwrite()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        _ = tempDirectory.CreateDirectory("work-repo");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "config.toml"), "kept-config");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "billing.concept.yaml"),
            IntakeConceptArtifactYaml.Serialize(new IntentSystem.ConceptIntake.Models.ConceptIntakePacket
            {
                DomainSlug = "billing",
                ConceptSource = "text",
                ConceptText = "Existing billing concept.",
                UpstreamPaths = [],
                InitialGoal = "Existing billing concept.",
                Constraints = [],
                KnownUnknowns = []
            }));
        tempDirectory.CreateFile(Path.Combine("repo", "intents", "billing", "README.md"), "kept-readme");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "interviews", "billing", "iq-existing.yaml"), "kept-interview");
        using var writer = new StringWriter();

        var exitCode = IntakeInitCommand.Execute(
            CreateContext(repoRoot),
            ["billing", "--text", "New billing concept.", "--work-repo-path", "../work-repo"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains(".intent-cli/config.toml", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/interviews/billing/iq-existing.yaml", output, StringComparison.Ordinal);
        Assert.Equal("kept-config", File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "config.toml")));
        Assert.Equal("kept-readme", File.ReadAllText(Path.Combine(repoRoot, "intents", "billing", "README.md")));
    }

    [Fact]
    public void Execute_GivenBothTextAndFromFile_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        _ = tempDirectory.CreateDirectory("work-repo");
        tempDirectory.CreateFile(Path.Combine("repo", "billing.txt"), "ignored");
        using var writer = new StringWriter();

        var exitCode = IntakeInitCommand.Execute(
            CreateContext(repoRoot),
            ["billing", "--text", "inline", "--from-file", "billing.txt", "--work-repo-path", "../work-repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("exactly one", writer.ToString(), StringComparison.Ordinal);
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-init-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
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
