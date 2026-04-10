using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmedBridgeCommandTests
{
    [Fact]
    public void Execute_GivenReadyConfirmedReconstruction_RegeneratesStandardIntakeArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.confirmed-reconstruction.yaml"),
            ConfirmedReconstructionArtifactYaml.Serialize(
                new ConfirmedReconstructionArtifact
                {
                    DomainSlug = "auth",
                    SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                    ReconstructedArtifactPaths =
                    [
                        ".intent-cli/intake/auth.reconstructed-concept.yaml",
                        ".intent-cli/intake/auth.reconstructed-interview.md"
                    ],
                    ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                    DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                    ConfirmedItems = ["confirm: validate current auth boundary"],
                    RejectedItems = ["reject: do not rewrite current auth ownership model"],
                    DeferredItems = [],
                    BlockedItems = [],
                    ReturnToIntentPaths = ["README.md", "AGENTS.md"],
                    DownstreamReadiness = "ready"
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-concept.yaml"),
            ReconstructedConceptArtifactYaml.Serialize(
                new ReconstructedConceptArtifact
                {
                    DomainSlug = "auth",
                    InitialGoal = "Reconstruct auth domain intent.",
                    CandidateIntentNodes = ["Clarify the auth domain mission."],
                    CandidateUserContext = ["Validate expected auth personas."],
                    CandidateMeans = ["Inspect OAuth entry points."],
                    CandidateRules = ["Preserve repo-level auth guidance."],
                    CandidateSpecs = ["Reconcile current auth public contract."],
                    CandidateExecutionUnits = ["Execution candidate from src/Auth/Login.cs."],
                    ConfidenceByAltitude = ["purpose: medium", "execution: high"],
                    SourceConceptRefs = ["issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current"]
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-interview.md"),
            """
            # Reconstructed Interview

            ## Domain

            `auth`

            selected_altitudes:
            - purpose
            - execution

            root_near_intent_candidates:
            - Clarify the auth domain mission.

            execution_near_update_candidates:
            - Inspect OAuth entry points.

            confidence_by_altitude:
            - purpose: medium
            - execution: high

            source_concept_refs:
            - issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current

            recommended_follow_up_questions:
            - Arbitrary text for the first bridge question.

            bridge_questions:
            - {"question_id":"iq-root","question_text":"Arbitrary text for the first bridge question.","reason":"Clarify root-near intent before standard intake resumes.","affects":["auth"],"blocking_or_nonblocking":"blocking"}

            return_to_intent_paths:
            - README.md
            - AGENTS.md

            gaps:
            - Need stronger auth purpose signal.
            """);
        using var writer = new StringWriter();
        var originalExecutor = GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor;

        try
        {
            GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor = (_, _) => new GenerateFromCurrentReconcileResult
            {
                Domain = "auth",
                Route = "confirmed-handoff",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                ClarificationReturnArtifactPath = null,
                ConfirmedItems = ["confirm: validate current auth boundary"],
                RejectedItems = ["reject: do not rewrite current auth ownership model"],
                DeferredItems = [],
                BlockedItems = [],
                ClarifyItems = [],
                ReturnToIntentPaths = ["README.md", "AGENTS.md"],
                DownstreamReadiness = "ready"
            };

            var exitCode = GenerateFromCurrentConfirmedBridgeCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirmed-bridge processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/interviews/auth/iq-root.yaml", output, StringComparison.Ordinal);

            var conceptPacket = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml")));
            Assert.Equal("generate-from-current-confirmed-bridge", conceptPacket.ConceptSource);
            Assert.Contains("confirmed_items:", conceptPacket.ConceptText, StringComparison.Ordinal);
            Assert.Contains("confirm: validate current auth boundary", conceptPacket.ConceptText, StringComparison.Ordinal);
            Assert.Equal(["README.md", "AGENTS.md"], conceptPacket.UpstreamPaths);

            var interview = InterviewArtifactYaml.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-root.yaml")));
            Assert.Equal("iq-root", interview.QuestionId);
        }
        finally
        {
            GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenClarificationReturnRoute_StopsWithoutRegeneratingStandardIntakeArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalExecutor = GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor;

        try
        {
            GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor = (_, _) => new GenerateFromCurrentReconcileResult
            {
                Domain = "auth",
                Route = "clarification-return",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ConfirmedReconstructionArtifactPath = null,
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                ConfirmedItems = [],
                RejectedItems = [],
                DeferredItems = [],
                BlockedItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                ClarifyItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                ReturnToIntentPaths = ["README.md"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedBridgeCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/intake/auth.clarification-return.yaml", writer.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml")));
        }
        finally
        {
            GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyConfirmedReconstruction_StopsAtReconciliationPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalExecutor = GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor;

        try
        {
            GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor = (_, _) => new GenerateFromCurrentReconcileResult
            {
                Domain = "auth",
                Route = "confirmed-handoff",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                ClarificationReturnArtifactPath = null,
                ConfirmedItems = ["confirm: validate current auth boundary"],
                RejectedItems = [],
                DeferredItems = ["defer: return interface cleanup after clarification"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                ClarifyItems = [],
                ReturnToIntentPaths = ["README.md"],
                DownstreamReadiness = "not-ready"
            };

            var exitCode = GenerateFromCurrentConfirmedBridgeCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains(".intent-cli/intake/auth.confirmed-reconstruction.yaml", output, StringComparison.Ordinal);
            Assert.Contains("reconciliation is not ready", output, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml")));
        }
        finally
        {
            GenerateFromCurrentConfirmedBridgeCommand.ReconcileExecutor = originalExecutor;
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-confirmed-bridge-tests-").FullName;

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
