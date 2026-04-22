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
    public void Sync_GivenAcceptedWorktreePathsThatWouldBeOverwrittenByMerge_ReconcilesToMergedCommit()
    {
        using var tempDirectory = new TemporaryDirectory();
        var parentRepoRoot = tempDirectory.CreateDirectory("repo");
        var childRepoPath = tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        var runner = new FakeGitRunner(
            new Dictionary<string, GitCommandResult>
            {
                [FakeGitRunner.CreateCommandKey(["fetch", "origin", "main"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["switch", "main"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["merge", "--ff-only", "origin/main"])] = new GitCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr =
                        """
                        error: Your local changes to the following files would be overwritten by merge:
                          intents/toy-calc/specs/01-cli-surface.md
                        error: The following untracked working tree files would be overwritten by merge:
                          intents/toy-calc/specs/02-invalid-usage-contract.md
                        Please move or remove them before you merge.
                        Aborting
                        """
                },
                [FakeGitRunner.CreateCommandKey(["status", "--porcelain=v1", "--untracked-files=all"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut =
                        """
                         M intents/toy-calc/specs/01-cli-surface.md
                        ?? intents/toy-calc/specs/02-invalid-usage-contract.md
                        """,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["clean", "-fd", "--", "intents/toy-calc/specs/02-invalid-usage-contract.md"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["reset", "--hard", "abc123"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "HEAD is now at abc123 merge closeout" + Environment.NewLine,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["rev-parse", "HEAD"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "abc123" + Environment.NewLine,
                    StdErr = string.Empty
                }
            },
            statusSequence:
            [
                """
                 M intents/toy-calc/specs/01-cli-surface.md
                ?? intents/toy-calc/specs/02-invalid-usage-contract.md
                """,
                string.Empty
            ]);

        ChildRepoMainSynchronizer.Sync(parentRepoRoot, "submodules/child-repo", "abc123", runner);

        Assert.Equal(
            [
                $"{childRepoPath}::fetch origin main",
                $"{childRepoPath}::switch main",
                $"{childRepoPath}::merge --ff-only origin/main",
                $"{childRepoPath}::status --porcelain=v1 --untracked-files=all",
                $"{childRepoPath}::clean -fd -- intents/toy-calc/specs/02-invalid-usage-contract.md",
                $"{childRepoPath}::reset --hard abc123",
                $"{childRepoPath}::status --porcelain=v1 --untracked-files=all",
                $"{childRepoPath}::rev-parse HEAD"
            ],
            runner.Calls);
    }

    [Fact]
    public void Sync_GivenMergeOverwriteAndAdditionalDirtyPaths_ThrowsInvalidOperationException()
    {
        using var tempDirectory = new TemporaryDirectory();
        var parentRepoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        var runner = new FakeGitRunner(
            new Dictionary<string, GitCommandResult>
            {
                [FakeGitRunner.CreateCommandKey(["fetch", "origin", "main"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["switch", "main"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["merge", "--ff-only", "origin/main"])] = new GitCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr =
                        """
                        error: Your local changes to the following files would be overwritten by merge:
                          intents/toy-calc/specs/01-cli-surface.md
                        Please commit your changes or stash them before you merge.
                        Aborting
                        """
                },
                [FakeGitRunner.CreateCommandKey(["status", "--porcelain=v1", "--untracked-files=all"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut =
                        """
                         M intents/toy-calc/specs/01-cli-surface.md
                         M src/ToyCalc/Program.cs
                        """,
                    StdErr = string.Empty
                }
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => ChildRepoMainSynchronizer.Sync(parentRepoRoot, "submodules/child-repo", "abc123", runner));

        Assert.Contains("outside the merge-overwrite set", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_GivenMergeOverwriteAndAdditionalRepoLocalRuntimeState_ReconcilesToMergedCommit()
    {
        using var tempDirectory = new TemporaryDirectory();
        var parentRepoRoot = tempDirectory.CreateDirectory("repo");
        var childRepoPath = tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        var runner = new FakeGitRunner(
            new Dictionary<string, GitCommandResult>
            {
                [FakeGitRunner.CreateCommandKey(["fetch", "origin", "main"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["switch", "main"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["merge", "--ff-only", "origin/main"])] = new GitCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr =
                        """
                        error: Your local changes to the following files would be overwritten by merge:
                          src/ToyCalc/CommandLine.cs
                        Please commit your changes or stash them before you merge.
                        Aborting
                        """
                },
                [FakeGitRunner.CreateCommandKey(["status", "--porcelain=v1", "--untracked-files=all"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut =
                        """
                         M src/ToyCalc/CommandLine.cs
                         M .intent-cli/config.toml
                         M intents/toy-calc/clarifications/open.md
                         M intents/toy-calc/execution/01-issue-ready-slices.md
                        ?? .intent-cli/runs/TOY-CALC-V0-05.provider.jsonl
                        """,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["reset", "--hard", "abc123"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "HEAD is now at abc123 merge closeout" + Environment.NewLine,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["rev-parse", "HEAD"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "abc123" + Environment.NewLine,
                    StdErr = string.Empty
                }
            },
            statusSequence:
            [
                """
                 M src/ToyCalc/CommandLine.cs
                 M .intent-cli/config.toml
                 M intents/toy-calc/clarifications/open.md
                 M intents/toy-calc/execution/01-issue-ready-slices.md
                ?? .intent-cli/runs/TOY-CALC-V0-05.provider.jsonl
                """,
                """
                 M .intent-cli/config.toml
                 M intents/toy-calc/clarifications/open.md
                 M intents/toy-calc/execution/01-issue-ready-slices.md
                ?? .intent-cli/runs/TOY-CALC-V0-05.provider.jsonl
                """
            ]);

        ChildRepoMainSynchronizer.Sync(parentRepoRoot, "submodules/child-repo", "abc123", runner);

        Assert.Equal(
            [
                $"{childRepoPath}::fetch origin main",
                $"{childRepoPath}::switch main",
                $"{childRepoPath}::merge --ff-only origin/main",
                $"{childRepoPath}::status --porcelain=v1 --untracked-files=all",
                $"{childRepoPath}::reset --hard abc123",
                $"{childRepoPath}::status --porcelain=v1 --untracked-files=all",
                $"{childRepoPath}::rev-parse HEAD"
            ],
            runner.Calls);
    }

    [Fact]
    public void Sync_GivenMergeOverwriteAndSelectedExecutionUnitIntentDrift_ThrowsInvalidOperationException()
    {
        using var tempDirectory = new TemporaryDirectory();
        var parentRepoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        var runner = new FakeGitRunner(
            new Dictionary<string, GitCommandResult>
            {
                [FakeGitRunner.CreateCommandKey(["fetch", "origin", "main"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["switch", "main"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                },
                [FakeGitRunner.CreateCommandKey(["merge", "--ff-only", "origin/main"])] = new GitCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr =
                        """
                        error: Your local changes to the following files would be overwritten by merge:
                          src/ToyCalc/CommandLine.cs
                        Please commit your changes or stash them before you merge.
                        Aborting
                        """
                },
                [FakeGitRunner.CreateCommandKey(["status", "--porcelain=v1", "--untracked-files=all"])] = new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut =
                        """
                         M src/ToyCalc/CommandLine.cs
                         M .intent-cli/config.toml
                         M intents/toy-calc/specs/05-division-command.md
                        ?? .intent-cli/runs/TOY-CALC-V0-05.provider.jsonl
                        """,
                    StdErr = string.Empty
                }
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => ChildRepoMainSynchronizer.Sync(parentRepoRoot, "submodules/child-repo", "abc123", runner));

        Assert.Contains(
            "intents/toy-calc/specs/05-division-command.md",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".intent-cli/config.toml", exception.Message, StringComparison.Ordinal);
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

    private sealed class FakeGitRunner : IGitCommandRunner
    {
        private readonly string? headCommit;
        private readonly IReadOnlyDictionary<string, GitCommandResult>? scriptedResults;
        private readonly Queue<string>? statusSequence;

        public FakeGitRunner(string headCommit)
        {
            this.headCommit = headCommit;
        }

        public FakeGitRunner(
            IReadOnlyDictionary<string, GitCommandResult> scriptedResults,
            IReadOnlyList<string>? statusSequence = null)
        {
            this.scriptedResults = scriptedResults;
            this.statusSequence = statusSequence is null ? null : new Queue<string>(statusSequence);
        }

        public List<string> Calls { get; } = [];

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add($"{workingDirectory}::{string.Join(' ', arguments)}");

            if (scriptedResults is not null)
            {
                var key = CreateCommandKey(arguments);
                if (!scriptedResults.TryGetValue(key, out var result))
                {
                    throw new Xunit.Sdk.XunitException($"Unexpected git command: {string.Join(" ", arguments)}");
                }

                if (arguments.SequenceEqual(["status", "--porcelain=v1", "--untracked-files=all"])
                    && statusSequence is not null)
                {
                    return result with
                    {
                        StdOut = statusSequence.Count > 0
                            ? statusSequence.Dequeue()
                            : result.StdOut
                    };
                }

                return result;
            }

            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = arguments.SequenceEqual(["rev-parse", "HEAD"])
                    ? headCommit + Environment.NewLine
                    : string.Empty,
                StdErr = string.Empty
            };
        }

        public static string CreateCommandKey(IReadOnlyList<string> arguments)
        {
            return string.Join("\u001f", arguments);
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
