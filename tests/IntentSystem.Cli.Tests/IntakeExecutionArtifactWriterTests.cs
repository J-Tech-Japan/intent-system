using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeExecutionArtifactWriterTests
{
    [Fact]
    public void Write_GivenDomainAndMarkdown_WritesExpectedArtifactPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");

        var artifactPath = IntakeExecutionArtifactWriter.Write("# Intake Execution Draft", "auth", repoRoot);

        Assert.Equal(
            Path.Combine(repoRoot, ".intent-cli", "intake", "auth.execution.md"),
            artifactPath);
        Assert.Equal("# Intake Execution Draft", File.ReadAllText(artifactPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-execution-writer-tests-").FullName;

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
