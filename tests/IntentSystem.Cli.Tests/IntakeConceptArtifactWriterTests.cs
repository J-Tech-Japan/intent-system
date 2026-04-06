using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeConceptArtifactWriterTests
{
    [Fact]
    public void Write_GivenDomainAndYaml_WritesExpectedArtifactPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");

        var artifactPath = IntakeConceptArtifactWriter.Write("domain_slug: auth" + Environment.NewLine, "auth", repoRoot);

        Assert.Equal(
            Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml"),
            artifactPath);
        Assert.Equal("domain_slug: auth" + Environment.NewLine, File.ReadAllText(artifactPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-concept-writer-tests-").FullName;

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
