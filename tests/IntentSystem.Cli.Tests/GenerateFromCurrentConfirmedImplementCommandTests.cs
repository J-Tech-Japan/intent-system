using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedImplementCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedActivate_GeneratesImplementRequestArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalActivateExecutor = GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor;
        var originalRunImplementExecutor = GenerateFromCurrentConfirmedImplementCommand.RunImplementExecutor;

        try
        {
            GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor = (_, _, _) => new GenerateFromCurrentConfirmedActivateResult
            {
                Domain = "auth",
                Route = "confirmed-activate",
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
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };
            GenerateFromCurrentConfirmedImplementCommand.RunImplementExecutor = (_, executionUnit) => new RunImplementResult
            {
                Request = new RunImplementRequest
                {
                    ExecutionUnit = executionUnit,
                    State = "active",
                    ImplementRole = "Claude",
                    QueueWorkerRole = "Claude",
                    QueueReviewRole = "Codex",
                    WorktreePath = $"/tmp/worktrees/{executionUnit}",
                    ChildRepoPath = "/tmp/child",
                    Branch = $"issue-500-{executionUnit.ToLowerInvariant()}",
                    LinkedIssue = $"https://github.com/J-Tech-Japan/intent-system/issues/{(executionUnit == "AUTH-01" ? "501" : "502")}",
                    LatestLinkedPr = null,
                    PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                    ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
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
                ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
            };

            var exitCode = GenerateFromCurrentConfirmedImplementCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-implement processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/implement/AUTH-01.request.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/implement/AUTH-02.request.md", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor = originalActivateExecutor;
            GenerateFromCurrentConfirmedImplementCommand.RunImplementExecutor = originalRunImplementExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutImplementHandoff()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalActivateExecutor = GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor;

        try
        {
            GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor = (_, _, _) => new GenerateFromCurrentConfirmedActivateResult
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
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedImplementCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor = originalActivateExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedActivate_StopsAtReconciliationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalActivateExecutor = GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor;

        try
        {
            GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor = (_, _, _) => new GenerateFromCurrentConfirmedActivateResult
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
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedImplementCommand.Execute(
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
            GenerateFromCurrentConfirmedImplementCommand.ConfirmedActivateExecutor = originalActivateExecutor;
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-confirmed-implement-tests-").FullName;

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
