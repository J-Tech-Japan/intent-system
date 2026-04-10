using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review.Models;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedFixCommandTests
{
    [Fact]
    public void Execute_GivenExistingFixingStateAndCommentArtifacts_GeneratesFixRequestArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        string[]? confirmedReviewArgs = null;
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "AUTH-01.comment.json"),
            ReviewCommentArtifactSerializer.Serialize(
                new ReviewCommentArtifact
                {
                    ExecutionUnit = "AUTH-01",
                    ReviewRequestRef = ".intent-cli/reviews/AUTH-01.request.json",
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/501",
                    CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/501#issuecomment-1",
                    BodyPath = "comment.md"
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "AUTH-02.comment.json"),
            ReviewCommentArtifactSerializer.Serialize(
                new ReviewCommentArtifact
                {
                    ExecutionUnit = "AUTH-02",
                    ReviewRequestRef = ".intent-cli/reviews/AUTH-02.request.json",
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/502",
                    CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/502#issuecomment-2",
                    BodyPath = "comment.md"
                }));
        using var writer = new StringWriter();
        var originalReviewExecutor = GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor;
        var originalRunFixExecutor = GenerateFromCurrentConfirmedFixCommand.RunFixExecutor;

        try
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor = (_, args, _) =>
            {
                confirmedReviewArgs = args;

                return new GenerateFromCurrentConfirmedReviewResult
                {
                    Domain = "auth",
                    Route = "confirmed-review",
                    ClarificationReturnArtifactPath = null,
                    ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                    UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                    UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                    RegeneratedArtifactPaths =
                    [
                        ".intent-cli/intake/auth.concept.yaml",
                        ".intent-cli/intake/auth.execution.md",
                        ".intent-cli/issues/AUTH-01/packet.yaml"
                    ],
                    StartedExecutionUnits = ["AUTH-01", "AUTH-02"],
                    CreatedIssueRefs =
                    [
                        "https://github.com/J-Tech-Japan/intent-system/issues/501",
                        "https://github.com/J-Tech-Japan/intent-system/issues/502"
                    ],
                    WorktreePaths =
                    [
                        "/tmp/worktrees/AUTH-01",
                        "/tmp/worktrees/AUTH-02"
                    ],
                    ImplementRequestArtifactPaths =
                    [
                        ".intent-cli/implement/AUTH-01.request.md",
                        ".intent-cli/implement/AUTH-02.request.md"
                    ],
                    CreatedPrRefs =
                    [
                        "https://github.com/J-Tech-Japan/intent-system/pull/501",
                        "https://github.com/J-Tech-Japan/intent-system/pull/502"
                    ],
                    ReviewExecutionUnits = ["AUTH-01", "AUTH-02"],
                    ReviewRequestArtifactPaths =
                    [
                        ".intent-cli/reviews/AUTH-01.request.json",
                        ".intent-cli/reviews/AUTH-02.request.json"
                    ],
                    ConfirmedItems = ["confirm: validate current auth boundary"],
                    BlockedItems = [],
                    DownstreamReadiness = "ready"
                };
            };
            GenerateFromCurrentConfirmedFixCommand.RunFixExecutor = (_, executionUnit) => new RunFixResult
            {
                Request = new RunFixRequest
                {
                    ExecutionUnit = executionUnit,
                    State = "fixing",
                    ImplementRole = "Claude",
                    QueueWorkerRole = "Claude",
                    QueueReviewRole = "Codex",
                    WorktreePath = $"/tmp/worktrees/{executionUnit}",
                    ChildRepoPath = "/tmp/child",
                    Branch = $"issue-500-{executionUnit.ToLowerInvariant()}",
                    LinkedIssue = $"https://github.com/J-Tech-Japan/intent-system/issues/{(executionUnit == "AUTH-01" ? "501" : "502")}",
                    LatestLinkedPr = $"https://github.com/J-Tech-Japan/intent-system/pull/{(executionUnit == "AUTH-01" ? "501" : "502")}",
                    LatestCommentRef = $"https://github.com/J-Tech-Japan/intent-system/pull/{(executionUnit == "AUTH-01" ? "501" : "502")}#issuecomment-{(executionUnit == "AUTH-01" ? "1" : "2")}",
                    PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                    ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                    ReviewCommentArtifactRef = $".intent-cli/reviews/{executionUnit}.comment.json",
                    ReviewRequestRef = $".intent-cli/reviews/{executionUnit}.request.json",
                    ReviewCommentBodyPath = "comment.md",
                    IssueTitle = $"[{executionUnit}] Title",
                    Goal = "Goal",
                    TargetPart = "Target",
                    TargetRepo = "submodules/intent-system",
                    TargetPath = ".",
                    InScope = [],
                    OutOfScope = [],
                    AcceptanceCriteria = [],
                    DeterministicReviewChecks = [],
                    ExpectedEvidence = []
                },
                ArtifactPath = $".intent-cli/fix/{executionUnit}.request.md"
            };

            var exitCode = GenerateFromCurrentConfirmedFixCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-fix processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.NotNull(confirmedReviewArgs);
            Assert.DoesNotContain("--from-file", confirmedReviewArgs!, StringComparer.Ordinal);
            Assert.Contains("- .intent-cli/reviews/AUTH-01.comment.json", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/501#issuecomment-1", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/fix/AUTH-01.request.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/fix/AUTH-02.request.md", output, StringComparison.Ordinal);
            Assert.Contains("Fixing execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor = originalReviewExecutor;
            GenerateFromCurrentConfirmedFixCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutFixHandoff()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalReviewExecutor = GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor;

        try
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor = (_, _, _) => new GenerateFromCurrentConfirmedReviewResult
            {
                Domain = "auth",
                Route = "clarification-return",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                RegeneratedArtifactPaths = [],
                StartedExecutionUnits = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                ImplementRequestArtifactPaths = [],
                CreatedPrRefs = [],
                ReviewExecutionUnits = [],
                ReviewRequestArtifactPaths = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before repair worker handoff treatment."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedFixCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor = originalReviewExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedReview_StopsAtReconciliationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalReviewExecutor = GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor;

        try
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor = (_, _, _) => new GenerateFromCurrentConfirmedReviewResult
            {
                Domain = "auth",
                Route = "reconciliation-required",
                ClarificationReturnArtifactPath = null,
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths = [".intent-cli/intake/auth.confirmed-reconstruction.yaml"],
                StartedExecutionUnits = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                ImplementRequestArtifactPaths = [],
                CreatedPrRefs = [],
                ReviewExecutionUnits = [],
                ReviewRequestArtifactPaths = [],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedFixCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains(".intent-cli/intake/auth.confirmed-reconstruction.yaml", output, StringComparison.Ordinal);
            Assert.Contains("reconciliation is not ready", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedReviewExecutor = originalReviewExecutor;
        }
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
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T06:15:00Z"),
            Items =
            [
                CreateItem("AUTH-01"),
                CreateItem("AUTH-02")
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit)
    {
        var issueNumber = executionUnit == "AUTH-01" ? 501 : 502;

        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Confirmed Fix",
            State = QueueItemState.Fixing,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            LinkedIssue = new LinkedIssue
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = issueNumber,
                Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}"
            },
            WorkerRole = "Claude",
            ReviewRole = "Codex",
            Priority = "high"
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-confirmed-fix-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

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
