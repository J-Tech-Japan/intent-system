using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

public sealed class RepoRootResolverTests
{
    [Fact]
    public void Resolve_GivenNestedWorkingDirectory_ReturnsNearestAncestorContainingIntentCliDirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
        var nestedDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature", "bin"));

        var resolved = RepoRootResolver.Resolve(nestedDirectory);

        Assert.Equal(repoRoot, resolved);
    }

    [Fact]
    public void Resolve_GivenRepoRootPath_ReturnsSameDirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));

        var resolved = RepoRootResolver.Resolve(repoRoot);

        Assert.Equal(repoRoot, resolved);
    }

    [Fact]
    public void Resolve_GivenDirectoryTreeWithoutIntentCli_ReturnsNull()
    {
        using var tempDirectory = new TemporaryDirectory();
        var nestedDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));

        var resolved = RepoRootResolver.Resolve(nestedDirectory);

        Assert.Null(resolved);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

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
