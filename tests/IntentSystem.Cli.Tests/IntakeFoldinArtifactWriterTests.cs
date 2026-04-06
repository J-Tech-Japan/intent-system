using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeFoldinArtifactWriterTests
{
    [Fact]
    public void Write_GivenDomainAndMarkdown_WritesExpectedArtifactPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");

        var artifactPath = IntakeFoldinArtifactWriter.Write("# Intake Fold-In Draft", "auth", repoRoot);

        Assert.Equal(
            Path.Combine(repoRoot, ".intent-cli", "intake", "auth.foldin.md"),
            artifactPath);
        Assert.Equal("# Intake Fold-In Draft", File.ReadAllText(artifactPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-foldin-writer-tests-").FullName;

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
