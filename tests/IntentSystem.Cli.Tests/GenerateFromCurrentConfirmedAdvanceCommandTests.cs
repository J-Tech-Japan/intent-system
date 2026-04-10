using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedAdvanceCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedBridge_AdvancesToUpdatedExecutionSourceOfTruth()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalBridgeExecutor = GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor;
        var originalAdvanceExecutor = GenerateFromCurrentConfirmedAdvanceCommand.IntakeAdvanceExecutor;

        try
        {
            GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor = (_, _) => new GenerateFromCurrentConfirmedBridgeResult
            {
                Domain = "auth",
                Route = "confirmed-bridge",
                ConceptArtifactPath = ".intent-cli/intake/auth.concept.yaml",
                InterviewArtifactPaths =
                [
                    ".intent-cli/interviews/auth/iq-root.yaml",
                    ".intent-cli/interviews/auth/iq-root.md"
                ],
                ClarificationReturnArtifactPath = null,
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                RegeneratedArtifactPaths =
                [
                    ".intent-cli/intake/auth.concept.yaml",
                    ".intent-cli/interviews/auth/iq-root.yaml",
                    ".intent-cli/interviews/auth/iq-root.md"
                ],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            };
            GenerateFromCurrentConfirmedAdvanceCommand.IntakeAdvanceExecutor = (_, _) => new IntakeAdvanceResult
            {
                Domain = "auth",
                ReadinessStatus = "ready",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths =
                [
                    ".intent-cli/intake/auth.compile.md",
                    ".intent-cli/intake/auth.foldin.md",
                    ".intent-cli/intake/auth.patch.md",
                    ".intent-cli/intake/auth.execution.md"
                ],
                SkippedStages = []
            };

            var exitCode = GenerateFromCurrentConfirmedAdvanceCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-advance processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- intents/intent-cli/concepts/auth-oauth2.md", output, StringComparison.Ordinal);
            Assert.Contains("- intents/intent-cli/execution/05-post-mvp-sub-slices.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.execution.md", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor = originalBridgeExecutor;
            GenerateFromCurrentConfirmedAdvanceCommand.IntakeAdvanceExecutor = originalAdvanceExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutAdvance()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalBridgeExecutor = GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor;

        try
        {
            GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor = (_, _) => new GenerateFromCurrentConfirmedBridgeResult
            {
                Domain = "auth",
                Route = "clarification-return",
                ConceptArtifactPath = null,
                InterviewArtifactPaths = [],
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                ConfirmedReconstructionArtifactPath = null,
                RegeneratedArtifactPaths = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedAdvanceCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor = originalBridgeExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedBridge_StopsAtReconciliationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalBridgeExecutor = GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor;

        try
        {
            GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor = (_, _) => new GenerateFromCurrentConfirmedBridgeResult
            {
                Domain = "auth",
                Route = "reconciliation-required",
                ConceptArtifactPath = null,
                InterviewArtifactPaths = [],
                ClarificationReturnArtifactPath = null,
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                RegeneratedArtifactPaths = [".intent-cli/intake/auth.confirmed-reconstruction.yaml"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedAdvanceCommand.Execute(
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
            GenerateFromCurrentConfirmedAdvanceCommand.ConfirmedBridgeExecutor = originalBridgeExecutor;
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-confirmed-advance-tests-").FullName;

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
