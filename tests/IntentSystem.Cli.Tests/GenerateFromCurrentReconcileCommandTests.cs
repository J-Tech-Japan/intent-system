using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentReconcileCommandTests
{
    [Fact]
    public void Execute_GivenCurrentDeveloperConfirmationWithoutClarify_WritesConfirmedReconstructionArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.developer-confirmation.yaml"),
            DeveloperConfirmationArtifactYaml.Serialize(
                new DeveloperConfirmationArtifact
                {
                    DomainSlug = "auth",
                    SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                    ReconstructedArtifactPaths =
                    [
                        ".intent-cli/intake/auth.reconstructed-concept.yaml",
                        ".intent-cli/intake/auth.reconstructed-interview.md"
                    ],
                    ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                    DecisionFilePath = "prepared/auth.decisions.md",
                    ConfirmedItems = ["confirm: validate current auth boundary"],
                    RejectedItems = ["reject: do not rewrite current auth ownership model"],
                    ClarifyItems = [],
                    DeferredItems = ["defer: return interface cleanup after clarification"],
                    BlockedItems = ["defer: return interface cleanup after clarification"],
                    DownstreamReadiness = "not-ready",
                    ReturnToIntentPaths = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"]
                }));
        using var writer = new StringWriter();
        var originalBestPracticeExecutor = GenerateFromCurrentReconcileCommand.BestPracticeExecutor;

        try
        {
            GenerateFromCurrentReconcileCommand.BestPracticeExecutor = (_, _) => new GenerateFromCurrentBestPracticeResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                ReviewedDimensions = ["architecture: needs-confirmation"],
                ModelRefs = [".intent/model-registry/auth-model.md"],
                KnowledgeRefs = [".intent/best-practices/security.md"],
                RecommendedIntentAdditions = [],
                RecommendedClarifications = [],
                DeveloperConfirmationItems = ["confirm: validate current auth boundary"],
                ReturnToIntentPaths = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"],
                ConfidenceDeltas = ["execution: medium -> high"],
                ReadinessStatus = "ready",
                SkippedStages = []
            };

            var exitCode = GenerateFromCurrentReconcileCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current reconcile processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/auth.confirmed-reconstruction.yaml", output, StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.confirmed-reconstruction.yaml");
            Assert.True(File.Exists(artifactPath));
            var artifact = ConfirmedReconstructionArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal("auth", artifact.DomainSlug);
            Assert.Single(artifact.ConfirmedItems);
            Assert.Single(artifact.RejectedItems);
            Assert.Single(artifact.DeferredItems);
            Assert.Single(artifact.BlockedItems);
            Assert.Equal("not-ready", artifact.DownstreamReadiness);
        }
        finally
        {
            GenerateFromCurrentReconcileCommand.BestPracticeExecutor = originalBestPracticeExecutor;
        }
    }

    [Fact]
    public void Execute_GivenCurrentDeveloperConfirmationWithClarify_GeneratesClarificationReturnInsteadOfConfirmedArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.developer-confirmation.yaml"),
            DeveloperConfirmationArtifactYaml.Serialize(
                new DeveloperConfirmationArtifact
                {
                    DomainSlug = "auth",
                    SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                    ReconstructedArtifactPaths =
                    [
                        ".intent-cli/intake/auth.reconstructed-concept.yaml",
                        ".intent-cli/intake/auth.reconstructed-interview.md"
                    ],
                    ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                    DecisionFilePath = "prepared/auth.decisions.md",
                    ConfirmedItems = ["confirm: validate current auth boundary"],
                    RejectedItems = [],
                    ClarifyItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                    DeferredItems = [],
                    BlockedItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                    DownstreamReadiness = "not-ready",
                    ReturnToIntentPaths = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"]
                }));
        using var writer = new StringWriter();
        var originalBestPracticeExecutor = GenerateFromCurrentReconcileCommand.BestPracticeExecutor;
        var originalClarifyExecutor = GenerateFromCurrentReconcileCommand.ClarifyExecutor;

        try
        {
            GenerateFromCurrentReconcileCommand.BestPracticeExecutor = (_, _) => new GenerateFromCurrentBestPracticeResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                ReviewedDimensions = ["architecture: needs-confirmation"],
                ModelRefs = [".intent/model-registry/auth-model.md"],
                KnowledgeRefs = [".intent/best-practices/performance.md"],
                RecommendedIntentAdditions = [],
                RecommendedClarifications = ["Clarify the authn/authz model and trust boundary for 'auth'."],
                DeveloperConfirmationItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                ReturnToIntentPaths = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"],
                ConfidenceDeltas = ["execution: medium -> high"],
                ReadinessStatus = "not-ready",
                SkippedStages = []
            };
            GenerateFromCurrentReconcileCommand.ClarifyExecutor = (_, _) => new GenerateFromCurrentClarifyResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                ClarifyItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                AffectedParentRefs = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"],
                Reasons = ["Clarify the authn/authz model and trust boundary for 'auth'."],
                Blockingness = ["blocking"],
                ReturnToIntentPaths = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentReconcileCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", output, StringComparison.Ordinal);
            Assert.DoesNotContain(".intent-cli/intake/auth.confirmed-reconstruction.yaml", output, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.confirmed-reconstruction.yaml")));
        }
        finally
        {
            GenerateFromCurrentReconcileCommand.BestPracticeExecutor = originalBestPracticeExecutor;
            GenerateFromCurrentReconcileCommand.ClarifyExecutor = originalClarifyExecutor;
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-reconcile-tests-").FullName;

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
