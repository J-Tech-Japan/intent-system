using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedActivateCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedAdvance_StartsExecutionUnits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalAdvanceExecutor = GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor;
        var originalStartExecutor = GenerateFromCurrentConfirmedActivateCommand.IntakeStartExecutor;

        try
        {
            GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor = (_, _) => new GenerateFromCurrentConfirmedAdvanceResult
            {
                Domain = "auth",
                Route = "confirmed-advance",
                ClarificationReturnArtifactPath = null,
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths =
                [
                    ".intent-cli/intake/auth.concept.yaml",
                    ".intent-cli/intake/auth.execution.md"
                ],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };
            GenerateFromCurrentConfirmedActivateCommand.IntakeStartExecutor = (_, _, _) => new IntakeStartResult
            {
                Domain = "auth",
                StartedExecutionUnits = ["AUTH-01", "AUTH-02"],
                GeneratedArtifactPaths =
                [
                    ".intent-cli/issues/AUTH-01/packet.yaml",
                    ".intent-cli/issues/AUTH-02/github-body.md"
                ],
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
                SkippedUnits = []
            };

            var exitCode = GenerateFromCurrentConfirmedActivateCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-activate processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/501", output, StringComparison.Ordinal);
            Assert.Contains("- /tmp/worktrees/AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/issues/AUTH-01/packet.yaml", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor = originalAdvanceExecutor;
            GenerateFromCurrentConfirmedActivateCommand.IntakeStartExecutor = originalStartExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutStart()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalAdvanceExecutor = GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor;

        try
        {
            GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor = (_, _) => new GenerateFromCurrentConfirmedAdvanceResult
            {
                Domain = "auth",
                Route = "clarification-return",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                ConfirmedReconstructionArtifactPath = null,
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                RegeneratedArtifactPaths = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedActivateCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor = originalAdvanceExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedAdvance_StopsAtReconciliationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalAdvanceExecutor = GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor;

        try
        {
            GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor = (_, _) => new GenerateFromCurrentConfirmedAdvanceResult
            {
                Domain = "auth",
                Route = "reconciliation-required",
                ClarificationReturnArtifactPath = null,
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths = [".intent-cli/intake/auth.confirmed-reconstruction.yaml"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedActivateCommand.Execute(
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
            GenerateFromCurrentConfirmedActivateCommand.ConfirmedAdvanceExecutor = originalAdvanceExecutor;
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-confirmed-activate-tests-").FullName;

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
