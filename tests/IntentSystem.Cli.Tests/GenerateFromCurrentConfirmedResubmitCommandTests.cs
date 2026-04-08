using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedResubmitCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedFix_ComposesToResubmitSummary()
    {
        using var writer = new StringWriter();
        var originalFixExecutor = GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor;
        var originalRunResubmitExecutor = GenerateFromCurrentConfirmedResubmitCommand.RunResubmitExecutor;

        try
        {
            GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor = (_, _, _) => new GenerateFromCurrentConfirmedFixResult
            {
                Domain = "auth",
                Route = "confirmed-fix",
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
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };
            GenerateFromCurrentConfirmedResubmitCommand.RunResubmitExecutor = (_, executionUnit) => new RunResubmitResult
            {
                ExecutionUnit = executionUnit,
                Branch = $"issue-501-{executionUnit.ToLowerInvariant()}",
                WorktreePath = $"/tmp/worktrees/{executionUnit}",
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/501"
            };

            var exitCode = GenerateFromCurrentConfirmedResubmitCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-resubmit processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Resubmitted execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("Resubmitted PR refs:", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/501", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor = originalFixExecutor;
            GenerateFromCurrentConfirmedResubmitCommand.RunResubmitExecutor = originalRunResubmitExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutResubmitTrace()
    {
        using var writer = new StringWriter();
        var originalFixExecutor = GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor;

        try
        {
            GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor = (_, _, _) => new GenerateFromCurrentConfirmedFixResult
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
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before same linked PR resubmit."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedResubmitCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor = originalFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedFix_StopsAtReconciliationPath()
    {
        using var writer = new StringWriter();
        var originalFixExecutor = GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor;

        try
        {
            GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor = (_, _, _) => new GenerateFromCurrentConfirmedFixResult
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
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedResubmitCommand.Execute(
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
            GenerateFromCurrentConfirmedResubmitCommand.ConfirmedFixExecutor = originalFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentConfirmedResubmitCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
