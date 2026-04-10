using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class IntakeConceptCommandTests
{
    [Fact]
    public void Execute_GivenInteractiveInput_WritesConceptPacketArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalInputReaderFactory = IntakeConceptCommand.InputReaderFactory;

        try
        {
            IntakeConceptCommand.InputReaderFactory = () => new StringReader(
                "Add OAuth2 provider support." + Environment.NewLine +
                "Prefer GitHub first." + Environment.NewLine);

            var exitCode = IntakeConceptCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Intake concept artifact generated for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Concept source: interactive", output, StringComparison.Ordinal);

            var packet = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml")));
            Assert.Equal("auth", packet.DomainSlug);
            Assert.Equal("interactive", packet.ConceptSource);
            Assert.Equal(
                "Add OAuth2 provider support." + Environment.NewLine + "Prefer GitHub first.",
                packet.ConceptText);
            Assert.Equal("Add OAuth2 provider support.", packet.InitialGoal);
            Assert.Empty(packet.UpstreamPaths);
            Assert.Empty(packet.Constraints);
            Assert.Empty(packet.KnownUnknowns);
        }
        finally
        {
            IntakeConceptCommand.InputReaderFactory = originalInputReaderFactory;
        }
    }

    [Fact]
    public void Execute_GivenFromFileInput_WritesConceptPacketArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "concepts", "auth.txt"),
            "Add OAuth2 provider support." + Environment.NewLine + "Must work with GitHub.");
        using var writer = new StringWriter();

        var exitCode = IntakeConceptCommand.Execute(
            CreateContext(repoRoot),
            ["auth", "--from-file", "concepts/auth.txt"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Concept source: from-file", output, StringComparison.Ordinal);

        var packet = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml")));
        Assert.Equal("from-file", packet.ConceptSource);
        Assert.Equal("Add OAuth2 provider support." + Environment.NewLine + "Must work with GitHub.", packet.ConceptText);
        Assert.Equal("Add OAuth2 provider support.", packet.InitialGoal);
    }

    [Fact]
    public void Execute_GivenMissingInputFile_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IntakeConceptCommand.Execute(
            CreateContext(repoRoot),
            ["auth", "--from-file", "concepts/missing.txt"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake concept file was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeConceptCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-concept-tests-").FullName;

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
