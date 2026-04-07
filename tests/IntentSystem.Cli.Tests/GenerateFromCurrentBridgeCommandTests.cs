using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentBridgeCommandTests
{
    [Fact]
    public void Execute_GivenReconstructedArtifacts_RegeneratesStandardIntakeAndInterviewArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
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
                    SourceConceptRefs =
                    [
                        "issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current",
                        "pr:117 https://github.com/J-Tech-Japan/intent-system/pull/117 [codex] Add reconstruction stage"
                    ]
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
            - Arbitrary text for the second bridge question.

            bridge_questions:
            - {"question_id":"iq-root","question_text":"Arbitrary text for the first bridge question.","reason":"Clarify root-near intent before standard intake resumes.","affects":["auth"],"blocking_or_nonblocking":"blocking"}
            - {"question_id":"iq-exec","question_text":"Arbitrary text for the second bridge question.","reason":"Clarify execution-near detail before standard intake resumes.","affects":["oauth"],"blocking_or_nonblocking":"nonblocking"}

            return_to_intent_paths:
            - README.md
            - AGENTS.md

            gaps:
            - Need stronger auth purpose signal.
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "stale.yaml"),
            "stale");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "billing", "iq-1.yaml"),
            "kept");
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentCommand.Execute(CreateContext(repoRoot), ["bridge", "auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Generate-from-current bridge processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Generated concept artifact: .intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/interviews/auth/iq-root.yaml", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/interviews/auth/iq-exec.md", output, StringComparison.Ordinal);
        Assert.Contains("Recommended updates:", output, StringComparison.Ordinal);
        Assert.Contains("- Clarify the auth domain mission.", output, StringComparison.Ordinal);
        Assert.Contains("- Inspect OAuth entry points.", output, StringComparison.Ordinal);
        Assert.Contains("Skipped bridge steps:", output, StringComparison.Ordinal);
        Assert.Contains("- none", output, StringComparison.Ordinal);

        var conceptArtifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml");
        Assert.True(File.Exists(conceptArtifactPath));
        var conceptPacket = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(conceptArtifactPath));
        Assert.Equal("auth", conceptPacket.DomainSlug);
        Assert.Equal("generate-from-current-bridge", conceptPacket.ConceptSource);
        Assert.Equal("Reconstruct auth domain intent.", conceptPacket.InitialGoal);
        Assert.Equal(["README.md", "AGENTS.md"], conceptPacket.UpstreamPaths);
        Assert.Contains("candidate_intent_nodes:", conceptPacket.ConceptText, StringComparison.Ordinal);
        Assert.Contains("recommended_follow_up_questions:", conceptPacket.ConceptText, StringComparison.Ordinal);
        Assert.Contains("Preserve repo-level auth guidance.", conceptPacket.Constraints, StringComparer.Ordinal);
        Assert.Contains("Reconcile current auth public contract.", conceptPacket.Constraints, StringComparer.Ordinal);
        Assert.Contains("Need stronger auth purpose signal.", conceptPacket.KnownUnknowns, StringComparer.Ordinal);

        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "stale.yaml")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "interviews", "billing", "iq-1.yaml")));

        var firstInterview = InterviewArtifactYaml.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-root.yaml")));
        Assert.Equal("iq-root", firstInterview.QuestionId);
        Assert.Equal("blocking", firstInterview.BlockingOrNonblocking);
        Assert.Equal("Clarify root-near intent before standard intake resumes.", firstInterview.Reason);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), firstInterview.CreatedAt);
        Assert.Equal(["auth"], firstInterview.Affects);
        Assert.Equal("Arbitrary text for the first bridge question.", firstInterview.QuestionText);
        Assert.Equal(
            ["Clarify the auth domain mission.", "Inspect OAuth entry points."],
            firstInterview.RecommendedUpdates);

        var secondInterview = InterviewArtifactYaml.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-exec.yaml")));
        Assert.Equal("iq-exec", secondInterview.QuestionId);
        Assert.Equal("nonblocking", secondInterview.BlockingOrNonblocking);
        Assert.Equal("Clarify execution-near detail before standard intake resumes.", secondInterview.Reason);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:01:00Z"), secondInterview.CreatedAt);
        Assert.Equal(["oauth"], secondInterview.Affects);
        Assert.Equal("Arbitrary text for the second bridge question.", secondInterview.QuestionText);

        var interviewMarkdown = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-root.md"));
        Assert.Contains("# Interview Question", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("recommended_updates:", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("gaps:", interviewMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoFollowUpQuestions_SkipsInterviewArtifactGeneration()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-concept.yaml"),
            ReconstructedConceptArtifactYaml.Serialize(
                new ReconstructedConceptArtifact
                {
                    DomainSlug = "auth",
                    InitialGoal = "Reconstruct auth domain intent.",
                    CandidateIntentNodes = [],
                    CandidateUserContext = [],
                    CandidateMeans = [],
                    CandidateRules = [],
                    CandidateSpecs = [],
                    CandidateExecutionUnits = [],
                    ConfidenceByAltitude = [],
                    SourceConceptRefs = []
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-interview.md"),
            GenerateFromCurrentReconstructionRenderer.RenderInterviewMarkdown(
                "auth",
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "stale.yaml"),
            "stale");
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentCommand.Execute(CreateContext(repoRoot), ["bridge", "auth"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No reconstructed follow-up questions were present; interview artifact generation was skipped.", writer.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repoRoot, ".intent-cli", "interviews", "auth")));
    }

    [Fact]
    public void Execute_GivenMissingReconstructedInterviewArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-concept.yaml"),
            ReconstructedConceptArtifactYaml.Serialize(
                new ReconstructedConceptArtifact
                {
                    DomainSlug = "auth",
                    InitialGoal = "Reconstruct auth domain intent.",
                    CandidateIntentNodes = [],
                    CandidateUserContext = [],
                    CandidateMeans = [],
                    CandidateRules = [],
                    CandidateSpecs = [],
                    CandidateExecutionUnits = [],
                    ConfidenceByAltitude = [],
                    SourceConceptRefs = []
                }));
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentCommand.Execute(CreateContext(repoRoot), ["bridge", "auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Reconstructed interview artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenConflictingReconstructedInterviewQuestions_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-concept.yaml"),
            ReconstructedConceptArtifactYaml.Serialize(
                new ReconstructedConceptArtifact
                {
                    DomainSlug = "auth",
                    InitialGoal = "Reconstruct auth domain intent.",
                    CandidateIntentNodes = [],
                    CandidateUserContext = [],
                    CandidateMeans = [],
                    CandidateRules = [],
                    CandidateSpecs = [],
                    CandidateExecutionUnits = [],
                    ConfidenceByAltitude = [],
                    SourceConceptRefs = []
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-interview.md"),
            """
            # Reconstructed Interview

            ## Domain

            `auth`

            selected_altitudes:
            - purpose

            root_near_intent_candidates:
            - Clarify the auth domain mission.

            execution_near_update_candidates:
            - none

            confidence_by_altitude:
            - purpose: medium

            source_concept_refs:
            - issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current

            recommended_follow_up_questions:
            - Human-facing question text.

            bridge_questions:
            - {"question_id":"iq-1","question_text":"Different bridge question text.","reason":"Clarify root-near intent before standard intake resumes.","affects":["auth"],"blocking_or_nonblocking":"blocking"}

            return_to_intent_paths:
            - README.md

            gaps:
            - none
            """);
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentCommand.Execute(CreateContext(repoRoot), ["bridge", "auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("aligned one-to-one", writer.ToString(), StringComparison.Ordinal);
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-bridge-tests-").FullName;

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
