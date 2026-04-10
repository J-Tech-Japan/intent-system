using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ProjectStatusCommandTests
{
    [Fact]
    public void Execute_GivenCliContext_WritesRuntimeBaseline()
    {
        var repoRoot = Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "intent-system");
        var context = CreateContext(repoRoot, ".intent-cli");
        using var writer = new StringWriter();

        var exitCode = ProjectStatusCommand.Execute(context, [], writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("intent-cli", output, StringComparison.Ordinal);
        Assert.Contains(repoRoot, output, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(repoRoot, ".intent-cli"), output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRelativeArtifactRoot_ResolvesArtifactPathAgainstRepoRoot()
    {
        var repoRoot = Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "intent-system");
        var context = CreateContext(repoRoot, ".intent-cli/artifacts");
        using var writer = new StringWriter();

        _ = ProjectStatusCommand.Execute(context, [], writer);

        Assert.Contains(
            Path.Combine(repoRoot, ".intent-cli", "artifacts"),
            writer.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRelativeWorkRepoPath_ResolvesAgainstRepoRoot()
    {
        var repoRoot = Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "intent-system");
        var context = new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorkRepoPath = "../Sekiban-dcb/dcb"
                }
            }
        };
        using var writer = new StringWriter();

        _ = ProjectStatusCommand.Execute(context, [], writer);

        Assert.Contains(
            $"Work repo path: {Path.GetFullPath(Path.Combine(repoRoot, "..", "Sekiban-dcb", "dcb"))}",
            writer.ToString(),
            StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot, string artifactRoot)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = artifactRoot
                }
            }
        };
    }
}
