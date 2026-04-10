using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeInterviewCommandTests
{
    [Fact]
    public void Execute_GivenConceptOnlyDomain_MaterializesBootstrapInterviewArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            IntakeConceptArtifactYaml.Serialize(CreateConceptPacket()));
        using var writer = new StringWriter();

        var exitCode = IntakeInterviewCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake interview bootstrap processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Bootstrap status: generated", output, StringComparison.Ordinal);
        Assert.Contains("- iq-goal", output, StringComparison.Ordinal);
        Assert.Contains("- iq-constraints", output, StringComparison.Ordinal);
        Assert.Contains("- iq-unknowns", output, StringComparison.Ordinal);

        var goalArtifact = InterviewArtifactYaml.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-goal.yaml")));
        Assert.Equal(".intent-cli/intake/auth.concept.yaml", goalArtifact.SourceConceptRef);
        Assert.Equal("iq-goal", goalArtifact.QuestionId);
        Assert.Equal("blocking", goalArtifact.BlockingOrNonblocking);
        Assert.Equal(["auth"], goalArtifact.Affects);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), goalArtifact.CreatedAt);
        Assert.Contains("Add OAuth2 provider support", goalArtifact.QuestionText, StringComparison.Ordinal);

        var constraintArtifact = InterviewArtifactYaml.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-constraints.yaml")));
        Assert.Equal("nonblocking", constraintArtifact.BlockingOrNonblocking);
        Assert.Equal(["auth", "constraints"], constraintArtifact.Affects);

        var unknownArtifact = InterviewArtifactYaml.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-unknowns.yaml")));
        Assert.Equal("blocking", unknownArtifact.BlockingOrNonblocking);
        Assert.Equal(["auth", "unknowns"], unknownArtifact.Affects);

        using var interviewWriter = new StringWriter();
        var interviewStartExitCode = InterviewStartCommand.Execute(CreateContext(repoRoot), ["auth"], interviewWriter);
        Assert.Equal(0, interviewStartExitCode);
        Assert.Contains("Question id: iq-goal", interviewWriter.ToString(), StringComparison.Ordinal);

        using var compileWriter = new StringWriter();
        var compileExitCode = IntakeCompileCommand.Execute(CreateContext(repoRoot), ["auth"], compileWriter);
        Assert.Equal(0, compileExitCode);
        Assert.Contains("Intake compile is not ready for domain 'auth'.", compileWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("Next interview question:", compileWriter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("No interview artifacts found", compileWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenExistingInterviewTree_SkipsWithoutOverwrite()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            IntakeConceptArtifactYaml.Serialize(CreateConceptPacket()));
        var existingArtifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-existing.yaml"),
            "kept");
        using var writer = new StringWriter();

        var exitCode = IntakeInterviewCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Bootstrap status: skipped", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/interviews/auth/iq-existing.yaml", output, StringComparison.Ordinal);
        Assert.Equal("kept", File.ReadAllText(existingArtifactPath));
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-goal.yaml")));
    }

    [Fact]
    public void Execute_GivenMissingConceptArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IntakeInterviewCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake concept artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeInterviewCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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

    private static IntentSystem.ConceptIntake.Models.ConceptIntakePacket CreateConceptPacket()
    {
        return new IntentSystem.ConceptIntake.Models.ConceptIntakePacket
        {
            DomainSlug = "auth",
            ConceptSource = "interactive",
            ConceptText = "Add OAuth2 provider support.",
            UpstreamPaths = ["README.md", "AGENTS.md"],
            InitialGoal = "Add OAuth2 provider support.",
            Constraints = ["Preserve packaged invocation.", "Do not add Node tooling."],
            KnownUnknowns = ["Which auth flow is canonical?", "How should device-code fit?"]
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-interview-tests-").FullName;

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
