using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedFixCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedComment_GeneratesFixRequestArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalCommentExecutor = GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor;
        var originalRunFixExecutor = GenerateFromCurrentConfirmedFixCommand.RunFixExecutor;

        try
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor = (_, _, _) => new GenerateFromCurrentConfirmedCommentResult
            {
                Domain = "auth",
                Route = "confirmed-comment",
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
                PostedCommentArtifactPaths =
                [
                    ".intent-cli/reviews/AUTH-01.comment.json",
                    ".intent-cli/reviews/AUTH-02.comment.json"
                ],
                CommentRefs =
                [
                    "https://github.com/J-Tech-Japan/intent-system/pull/501#issuecomment-1",
                    "https://github.com/J-Tech-Japan/intent-system/pull/502#issuecomment-2"
                ],
                FixingExecutionUnits = ["AUTH-01", "AUTH-02"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
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
                ["auth", "--from-path", "src/feature", "--from-file", "comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-fix processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/fix/AUTH-01.request.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/fix/AUTH-02.request.md", output, StringComparison.Ordinal);
            Assert.Contains("Fixing execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor = originalCommentExecutor;
            GenerateFromCurrentConfirmedFixCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutFixHandoff()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalCommentExecutor = GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor;

        try
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor = (_, _, _) => new GenerateFromCurrentConfirmedCommentResult
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
                PostedCommentArtifactPaths = [],
                CommentRefs = [],
                FixingExecutionUnits = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before repair worker handoff treatment."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedFixCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature", "--from-file", "comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor = originalCommentExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedComment_StopsAtReconciliationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalCommentExecutor = GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor;

        try
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor = (_, _, _) => new GenerateFromCurrentConfirmedCommentResult
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
                PostedCommentArtifactPaths = [],
                CommentRefs = [],
                FixingExecutionUnits = [],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedFixCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature", "--from-file", "comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains(".intent-cli/intake/auth.confirmed-reconstruction.yaml", output, StringComparison.Ordinal);
            Assert.Contains("reconciliation is not ready", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            GenerateFromCurrentConfirmedFixCommand.ConfirmedCommentExecutor = originalCommentExecutor;
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
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
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

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
