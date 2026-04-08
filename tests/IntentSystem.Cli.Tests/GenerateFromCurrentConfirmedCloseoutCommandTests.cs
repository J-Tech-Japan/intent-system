using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedCloseoutCommandTests
{
    [Fact]
    public void Execute_GivenNoRepairComment_UsesAcceptedCloseoutPath()
    {
        using var writer = new StringWriter();
        var originalAcceptExecutor = GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedAcceptExecutor;
        var originalReacceptExecutor = GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedReacceptExecutor;

        try
        {
            GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedAcceptExecutor = (_, _, _) => new GenerateFromCurrentConfirmedAcceptResult
            {
                Domain = "auth",
                Route = "confirmed-accept",
                ClarificationReturnArtifactPath = null,
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths = [".intent-cli/intake/auth.concept.yaml"],
                StartedExecutionUnits = ["AUTH-01"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/501"],
                WorktreePaths = ["/tmp/worktrees/AUTH-01"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/AUTH-01.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501"],
                ReviewExecutionUnits = ["AUTH-01"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/AUTH-01.request.json"],
                MergedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501"],
                ClosedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/501"],
                CompletedExecutionUnits = ["AUTH-01"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };
            GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedReacceptExecutor = (_, _, _) =>
                throw new InvalidOperationException("confirmed reaccept path should not run");

            var exitCode = GenerateFromCurrentConfirmedCloseoutCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Selected closeout path: accepted-closeout", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
            Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
            Assert.Contains("- repair-in-place-accepted-closeout", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedAcceptExecutor = originalAcceptExecutor;
            GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedReacceptExecutor = originalReacceptExecutor;
        }
    }

    [Fact]
    public void Execute_GivenRepairComment_UsesRepairAcceptedCloseoutPath()
    {
        using var writer = new StringWriter();
        var originalAcceptExecutor = GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedAcceptExecutor;
        var originalReacceptExecutor = GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedReacceptExecutor;

        try
        {
            GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedAcceptExecutor = (_, _, _) =>
                throw new InvalidOperationException("confirmed accept path should not run");
            GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedReacceptExecutor = (_, _, _) => new GenerateFromCurrentConfirmedReacceptResult
            {
                Domain = "auth",
                Route = "confirmed-reaccept",
                ClarificationReturnArtifactPath = null,
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths = [".intent-cli/intake/auth.concept.yaml"],
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
                RereviewedExecutionUnits = ["AUTH-01"],
                RereviewedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501"],
                CompletedExecutionUnits = ["AUTH-01"],
                ClosedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/501"],
                MergedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };

            var exitCode = GenerateFromCurrentConfirmedCloseoutCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature", "--from-file", "repair-comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Selected closeout path: repair-in-place-accepted-closeout", output, StringComparison.Ordinal);
            Assert.Contains("Comment refs:", output, StringComparison.Ordinal);
            Assert.Contains("- accepted-closeout", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedAcceptExecutor = originalAcceptExecutor;
            GenerateFromCurrentConfirmedCloseoutCommand.ConfirmedReacceptExecutor = originalReacceptExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentConfirmedCloseoutCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
