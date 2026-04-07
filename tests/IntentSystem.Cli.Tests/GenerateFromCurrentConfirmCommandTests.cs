using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentConfirmCommandTests
{
    [Fact]
    public void Execute_GivenRealPipeline_GeneratesDeveloperConfirmationArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var parentRepoRoot = tempDirectory.CreateDirectory("parent");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "model-registry"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "best-practices"));
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "model-registry", "auth-model.md"), "# auth model");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "best-practices", "security.md"), "# security");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("parent", "intents", "intent-cli", "specs", "11-reconstruction-review-and-confirmation.md"), "# review");
        tempDirectory.CreateFile(Path.Combine("parent", "intents", "rules", "reconstruction-feedback-loop.md"), "# loop");
        tempDirectory.CreateFile(
            Path.Combine("repo", "prepared", "auth.decisions.md"),
            """
            # Developer Confirmation Decisions

            ## Confirm
            - confirm: validate the best-practice review suggestions for 'auth' against parent rules/specs before any canonical mutation.
            - confirm: choose which of the 2 suggested intent additions should return to the parent intent tree.

            ## Reject
            - reject: explicitly reject any suggested intent addition that conflicts with project rules or specs.

            ## Clarify
            - none

            ## Defer
            - none
            """);
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGitHubRunner();

            var exitCode = GenerateFromCurrentCommand.Execute(
                CreateContext(repoRoot, parentRepoRoot),
                ["confirm", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "purpose,rules,execution", "--from-file", "prepared/auth.decisions.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current confirm processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/auth.developer-confirmation.yaml", output, StringComparison.Ordinal);
            Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.developer-confirmation.yaml");
            Assert.True(File.Exists(artifactPath));
            var artifact = DeveloperConfirmationArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal("auth", artifact.DomainSlug);
            Assert.Equal("prepared/auth.decisions.md", artifact.DecisionFilePath);
            Assert.Equal("ready", artifact.DownstreamReadiness);
            Assert.Equal(2, artifact.ConfirmedItems.Count);
            Assert.Single(artifact.RejectedItems);
            Assert.Empty(artifact.BlockedItems);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenUndecidedItems_ReturnsBlockedNotReadyArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "prepared", "auth.decisions.md"),
            """
            # Developer Confirmation Decisions

            ## Confirm
            - confirm: validate current auth boundary

            ## Reject
            - none

            ## Clarify
            - none

            ## Defer
            - none
            """);
        using var writer = new StringWriter();
        var originalExecutor = GenerateFromCurrentConfirmCommand.BestPracticeExecutor;

        try
        {
            GenerateFromCurrentConfirmCommand.BestPracticeExecutor = (_, _) => new GenerateFromCurrentBestPracticeResult
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
                DeveloperConfirmationItems =
                [
                    "confirm: validate current auth boundary",
                    "confirm: choose the parent return path"
                ],
                ReturnToIntentPaths = ["README.md"],
                ConfidenceDeltas = ["execution: medium -> high"],
                ReadinessStatus = "ready",
                SkippedStages = []
            };

            var exitCode = GenerateFromCurrentConfirmCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature", "--from-file", "prepared/auth.decisions.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Downstream readiness: not-ready", output, StringComparison.Ordinal);
            Assert.Contains("confirm: choose the parent return path", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentConfirmCommand.BestPracticeExecutor = originalExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingFromFileArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentConfirmCommand.Execute(
            CreateContext("/tmp/intent-system"),
            ["auth", "--from-path", "src/feature"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-file", writer.ToString(), StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot, string? parentIntentRepoRoot = null)
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
                    WorktreeRoot = ".intent-cli/worktrees",
                    ParentIntentRepoRoot = parentIntentRepoRoot ?? string.Empty
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-confirm-tests-").FullName;

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
