using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedRereviewCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedResubmit_ComposesToRereviewSummary()
    {
        using var writer = new StringWriter();
        var originalResubmitExecutor = GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor;
        var originalRunRereviewExecutor = GenerateFromCurrentConfirmedRereviewCommand.RunRereviewExecutor;

        try
        {
            GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor = (_, _, _) => new GenerateFromCurrentConfirmedResubmitResult
            {
                Domain = "auth",
                Route = "confirmed-resubmit",
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
                StartedExecutionUnits = ["AUTH-01"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/501"],
                WorktreePaths = ["/tmp/worktrees/AUTH-01"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/AUTH-01.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501"],
                ReviewExecutionUnits = ["AUTH-01"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/AUTH-01.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/AUTH-01.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501#issuecomment-1"],
                FixingExecutionUnits = ["AUTH-01"],
                FixRequestArtifactPaths = [".intent-cli/fix/AUTH-01.request.md"],
                ResubmittedExecutionUnits = ["AUTH-01"],
                ResubmittedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };
            GenerateFromCurrentConfirmedRereviewCommand.RunRereviewExecutor = (_, executionUnit) => new RunRereviewResult
            {
                ExecutionUnit = executionUnit,
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/501"
            };

            var exitCode = GenerateFromCurrentConfirmedRereviewCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-rereview processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Rereviewed execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("Rereviewed PR refs:", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/501", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor = originalResubmitExecutor;
            GenerateFromCurrentConfirmedRereviewCommand.RunRereviewExecutor = originalRunRereviewExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutRereviewEntry()
    {
        using var writer = new StringWriter();
        var originalResubmitExecutor = GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor;

        try
        {
            GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor = (_, _, _) => new GenerateFromCurrentConfirmedResubmitResult
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
                ReviewRequestArtifactPaths = [],
                PostedCommentArtifactPaths = [],
                CommentRefs = [],
                FixingExecutionUnits = [],
                FixRequestArtifactPaths = [],
                ResubmittedExecutionUnits = [],
                ResubmittedPrRefs = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before same linked PR rereview."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedRereviewCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor = originalResubmitExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedResubmit_StopsAtReconciliationPath()
    {
        using var writer = new StringWriter();
        var originalResubmitExecutor = GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor;

        try
        {
            GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor = (_, _, _) => new GenerateFromCurrentConfirmedResubmitResult
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
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedRereviewCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains(".intent-cli/intake/auth.confirmed-reconstruction.yaml", output, StringComparison.Ordinal);
            Assert.Contains("reconciliation is not ready", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            GenerateFromCurrentConfirmedRereviewCommand.ConfirmedResubmitExecutor = originalResubmitExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentConfirmedRereviewCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
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
}
