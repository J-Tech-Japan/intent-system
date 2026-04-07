using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentBestPracticeCommandTests
{
    [Fact]
    public void Execute_GivenRealPipeline_GeneratesBestPracticeReviewArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "model-registry"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "best-practices"));
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "model-registry", "auth-model.md"), "# auth model");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "best-practices", "security.md"), "# security");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;
        var originalParentRefProvider = GenerateFromCurrentBestPracticeCommand.ParentRuleSpecRefProvider;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGitHubRunner();
            GenerateFromCurrentBestPracticeCommand.ParentRuleSpecRefProvider = () =>
            [
                "intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md",
                "intents/rules/reconstruction-feedback-loop.md"
            ];

            var exitCode = GenerateFromCurrentCommand.Execute(
                CreateContext(repoRoot),
                ["best-practice", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "purpose,rules,execution"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current best-practice processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/auth.best-practice-review.md", output, StringComparison.Ordinal);
            Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
            Assert.Contains("Model refs:", output, StringComparison.Ordinal);
            Assert.Contains("Knowledge refs:", output, StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.best-practice-review.md");
            Assert.True(File.Exists(artifactPath));
            var artifact = File.ReadAllText(artifactPath);
            Assert.Contains("# Best Practice Review", artifact, StringComparison.Ordinal);
            Assert.Contains("model_refs:", artifact, StringComparison.Ordinal);
            Assert.Contains("- .intent/model-registry/auth-model.md", artifact, StringComparison.Ordinal);
            Assert.Contains("knowledge_refs:", artifact, StringComparison.Ordinal);
            Assert.Contains("- .intent/best-practices/security.md", artifact, StringComparison.Ordinal);
            Assert.Contains("parent_rule_spec_refs:", artifact, StringComparison.Ordinal);
            Assert.Contains("recommended_intent_additions:", artifact, StringComparison.Ordinal);
            Assert.Contains("developer_confirmation_items:", artifact, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
            GenerateFromCurrentBestPracticeCommand.ParentRuleSpecRefProvider = originalParentRefProvider;
        }
    }

    [Fact]
    public void Execute_GivenMissingProjectInputs_ReturnsNotReadyReview()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();
        var originalSourceBundleExecutor = GenerateFromCurrentBestPracticeCommand.SourceBundleExecutor;
        var originalReconstructionExecutor = GenerateFromCurrentBestPracticeCommand.ReconstructionExecutor;
        var originalModelRefProvider = GenerateFromCurrentBestPracticeCommand.ModelRefProvider;
        var originalKnowledgeRefProvider = GenerateFromCurrentBestPracticeCommand.KnowledgeRefProvider;
        var originalParentRefProvider = GenerateFromCurrentBestPracticeCommand.ParentRuleSpecRefProvider;

        try
        {
            GenerateFromCurrentBestPracticeCommand.SourceBundleExecutor = (_, _) => new GenerateFromCurrentResult
            {
                Domain = "auth",
                ArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                SourceRoot = "src/feature",
                SelectedIssueScope = "none",
                SelectedPrScope = "none",
                SelectedAltitudes = ["execution"],
                SelectedPaths = ["src/feature/FeatureA.cs"],
                SourceRefs = ["code:src/feature/FeatureA.cs"],
                SamplingNotes = [],
                Gaps = []
            };
            GenerateFromCurrentBestPracticeCommand.ReconstructionExecutor = (_, _) => new GenerateFromCurrentReconstructionResult
            {
                Domain = "auth",
                ConceptArtifactPath = ".intent-cli/intake/auth.reconstructed-concept.yaml",
                InterviewArtifactPath = ".intent-cli/intake/auth.reconstructed-interview.md",
                SelectedAltitudes = ["execution"],
                CandidateIntentNodes = [],
                CandidateExecutionUnits = ["Execution candidate from src/feature/FeatureA.cs."],
                ConfidenceByAltitude = ["execution: medium"],
                SourceConceptRefs = [],
                RecommendedFollowUpQuestions = [],
                ReturnToIntentPaths = ["README.md"],
                Gaps = ["Need stronger auth signal."]
            };
            GenerateFromCurrentBestPracticeCommand.ModelRefProvider = _ => [];
            GenerateFromCurrentBestPracticeCommand.KnowledgeRefProvider = _ => [];
            GenerateFromCurrentBestPracticeCommand.ParentRuleSpecRefProvider = () => [];

            var exitCode = GenerateFromCurrentBestPracticeCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Readiness status: not-ready", output, StringComparison.Ordinal);
            Assert.Contains("- model-registry-review", output, StringComparison.Ordinal);
            Assert.Contains("- best-practice-knowledge-review", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentBestPracticeCommand.SourceBundleExecutor = originalSourceBundleExecutor;
            GenerateFromCurrentBestPracticeCommand.ReconstructionExecutor = originalReconstructionExecutor;
            GenerateFromCurrentBestPracticeCommand.ModelRefProvider = originalModelRefProvider;
            GenerateFromCurrentBestPracticeCommand.KnowledgeRefProvider = originalKnowledgeRefProvider;
            GenerateFromCurrentBestPracticeCommand.ParentRuleSpecRefProvider = originalParentRefProvider;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentBestPracticeCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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

    private sealed class FakeGitHubRunner : IGitHubCommandRunner
    {
        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["issue", "view", "114", "--comments", "--json", "number,title,body,url,state,comments"]))
            {
                return Success("""{"number":114,"title":"[G44] Generate From Current","body":"Reconstruct from selected current signals.","url":"https://github.com/J-Tech-Japan/intent-system/issues/114","state":"OPEN","comments":[{"body":"Need deterministic output."}]}""");
            }

            if (arguments.SequenceEqual(["pr", "view", "113", "--comments", "--json", "number,title,body,url,state,isDraft,mergeStateStatus,comments,reviews"]))
            {
                return Success("""{"number":113,"title":"[codex] Add intake activate command","body":"Adds intake activate.","url":"https://github.com/J-Tech-Japan/intent-system/pull/113","state":"OPEN","isDraft":true,"mergeStateStatus":"CLEAN","comments":[{"body":"Looks good."}],"reviews":[{"state":"COMMENTED","body":"Scope stayed thin."}]}""");
            }

            throw new InvalidOperationException($"Unexpected gh arguments: {string.Join(' ', arguments)}");
        }

        private static GitHubCommandResult Success(string stdOut)
        {
            return new GitHubCommandResult
            {
                ExitCode = 0,
                StdOut = stdOut,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-best-practice-tests-").FullName;

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
