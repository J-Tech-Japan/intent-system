namespace IntentSystem.Review.Tests;

public sealed class ChildRepoMainSynchronizerTests
{
    [Fact]
    public void Sync_GivenMergedCommit_RunsFetchSwitchFastForwardAndHeadCheck()
    {
        using var tempDirectory = new TemporaryDirectory();
        var parentRepoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        var runner = new FakeGitRunner(headCommit: "abc123");

        ChildRepoMainSynchronizer.Sync(parentRepoRoot, "submodules/child-repo", "abc123", runner);

        var childRepoPath = Path.Combine(parentRepoRoot, "submodules", "child-repo");
        Assert.Equal(
            [
                $"{childRepoPath}::fetch origin main",
                $"{childRepoPath}::switch main",
                $"{childRepoPath}::merge --ff-only origin/main",
                $"{childRepoPath}::rev-parse HEAD"
            ],
            runner.Calls);
    }

    [Fact]
    public void Sync_GivenHeadMismatch_ThrowsInvalidOperationException()
    {
        using var tempDirectory = new TemporaryDirectory();
        var parentRepoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        var runner = new FakeGitRunner(headCommit: "def456");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ChildRepoMainSynchronizer.Sync(parentRepoRoot, "submodules/child-repo", "abc123", runner));

        Assert.Contains("must match merged commit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage_GivenChildRepoRef_StagesParentSubmodulePointer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var parentRepoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        var runner = new FakeGitRunner(headCommit: "abc123");

        ParentSubmodulePointerUpdater.Stage(parentRepoRoot, "submodules/child-repo", runner);

        Assert.Equal(
            [$"{parentRepoRoot}::add submodules/child-repo"],
            runner.Calls);
    }

    private sealed class FakeGitRunner(string headCommit) : IGitCommandRunner
    {
        public List<string> Calls { get; } = [];

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add($"{workingDirectory}::{string.Join(' ', arguments)}");

            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = arguments.SequenceEqual(["rev-parse", "HEAD"])
                    ? headCommit + Environment.NewLine
                    : string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-review-sync-tests-").FullName;

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
