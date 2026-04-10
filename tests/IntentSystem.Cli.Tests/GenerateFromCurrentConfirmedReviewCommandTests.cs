using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedReviewCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedSubmit_GeneratesReviewRequestArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalSubmitExecutor = GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor;
        var originalReviewRunExecutor = GenerateFromCurrentConfirmedReviewCommand.ReviewRunExecutor;

        try
        {
            GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor = (_, _, _) => new GenerateFromCurrentConfirmedSubmitResult
            {
                Domain = "auth",
                Route = "confirmed-submit",
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
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };
            GenerateFromCurrentConfirmedReviewCommand.ReviewRunExecutor = (_, executionUnit) => new ReviewRunResult
            {
                ExecutionUnit = executionUnit,
                ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json"
            };

            var exitCode = GenerateFromCurrentConfirmedReviewCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-review processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/reviews/AUTH-01.request.json", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/reviews/AUTH-02.request.json", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/501", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor = originalSubmitExecutor;
            GenerateFromCurrentConfirmedReviewCommand.ReviewRunExecutor = originalReviewRunExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutReviewRequestGeneration()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalSubmitExecutor = GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor;

        try
        {
            GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor = (_, _, _) => new GenerateFromCurrentConfirmedSubmitResult
            {
                Domain = "auth",
                Route = "clarification-return",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                ConfirmedReconstructionArtifactPath = null,
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                RegeneratedArtifactPaths = [],
                StartedExecutionUnits = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                ImplementRequestArtifactPaths = [],
                CreatedPrRefs = [],
                ReviewExecutionUnits = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before review-request treatment."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedReviewCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor = originalSubmitExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedSubmit_StopsAtReconciliationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalSubmitExecutor = GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor;

        try
        {
            GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor = (_, _, _) => new GenerateFromCurrentConfirmedSubmitResult
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
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedReviewCommand.Execute(
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
            GenerateFromCurrentConfirmedReviewCommand.ConfirmedSubmitExecutor = originalSubmitExecutor;
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-confirmed-review-tests-").FullName;

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
