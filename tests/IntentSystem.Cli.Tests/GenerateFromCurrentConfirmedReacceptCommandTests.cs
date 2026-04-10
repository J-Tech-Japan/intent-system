using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedReacceptCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedRereview_CompletesExecutionUnits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalRereviewExecutor = GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor;
        var originalAcceptExecutor = GenerateFromCurrentConfirmedReacceptCommand.ReviewAcceptExecutor;

        try
        {
            GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor = (_, _, _) => new GenerateFromCurrentConfirmedRereviewResult
            {
                Domain = "auth",
                Route = "confirmed-rereview",
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
                FixRequestArtifactPaths =
                [
                    ".intent-cli/fix/AUTH-01.request.md",
                    ".intent-cli/fix/AUTH-02.request.md"
                ],
                ResubmittedExecutionUnits = ["AUTH-01", "AUTH-02"],
                ResubmittedPrRefs =
                [
                    "https://github.com/J-Tech-Japan/intent-system/pull/501",
                    "https://github.com/J-Tech-Japan/intent-system/pull/502"
                ],
                RereviewedExecutionUnits = ["AUTH-01", "AUTH-02"],
                RereviewedPrRefs =
                [
                    "https://github.com/J-Tech-Japan/intent-system/pull/501",
                    "https://github.com/J-Tech-Japan/intent-system/pull/502"
                ],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };
            GenerateFromCurrentConfirmedReacceptCommand.ReviewAcceptExecutor = (_, executionUnit) => new ReviewAcceptResult
            {
                ExecutionUnit = executionUnit,
                MergedPrRef = executionUnit switch
                {
                    "AUTH-01" => "https://github.com/J-Tech-Japan/intent-system/pull/501",
                    "AUTH-02" => "https://github.com/J-Tech-Japan/intent-system/pull/502",
                    _ => throw new InvalidOperationException($"Unexpected execution unit '{executionUnit}'.")
                },
                ClosedIssueRef = executionUnit switch
                {
                    "AUTH-01" => "https://github.com/J-Tech-Japan/intent-system/issues/501",
                    "AUTH-02" => "https://github.com/J-Tech-Japan/intent-system/issues/502",
                    _ => throw new InvalidOperationException($"Unexpected execution unit '{executionUnit}'.")
                }
            };

            var exitCode = GenerateFromCurrentConfirmedReacceptCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-reaccept processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/reviews/AUTH-01.request.json", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/501", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/501", output, StringComparison.Ordinal);
            Assert.Contains("Completed execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor = originalRereviewExecutor;
            GenerateFromCurrentConfirmedReacceptCommand.ReviewAcceptExecutor = originalAcceptExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutAcceptedCloseout()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalRereviewExecutor = GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor;

        try
        {
            GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor = (_, _, _) => new GenerateFromCurrentConfirmedRereviewResult
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
                FixRequestArtifactPaths = [],
                ResubmittedExecutionUnits = [],
                ResubmittedPrRefs = [],
                RereviewedExecutionUnits = [],
                RereviewedPrRefs = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before accepted closeout treatment."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedReacceptCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor = originalRereviewExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedRereview_StopsAtReconciliationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalRereviewExecutor = GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor;

        try
        {
            GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor = (_, _, _) => new GenerateFromCurrentConfirmedRereviewResult
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
                FixRequestArtifactPaths = [],
                ResubmittedExecutionUnits = [],
                ResubmittedPrRefs = [],
                RereviewedExecutionUnits = [],
                RereviewedPrRefs = [],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedReacceptCommand.Execute(
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
            GenerateFromCurrentConfirmedReacceptCommand.ConfirmedRereviewExecutor = originalRereviewExecutor;
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

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-confirmed-reaccept-tests-").FullName;

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
