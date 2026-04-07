using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentRereviewCommandTests
{
    [Fact]
    public void Execute_GivenRealPipeline_NotReadyAfterBridge_DefersRereviewEntry()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("repo", "repair-comment.md"), "repair in place");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGitHubRunner();

            var exitCode = GenerateFromCurrentCommand.Execute(
                CreateContext(repoRoot),
                ["rereview", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution", "--from-file", "repair-comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current rereview processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Readiness status: not-ready", output, StringComparison.Ordinal);
            Assert.Contains("Rereviewed execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- rereview-entry", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenReadyStageResults_ComposesToRereviewSummary()
    {
        using var writer = new StringWriter();
        var originalResubmitExecutor = GenerateFromCurrentRereviewCommand.ResubmitExecutor;
        var originalRunRereviewExecutor = GenerateFromCurrentRereviewCommand.RunRereviewExecutor;

        try
        {
            GenerateFromCurrentRereviewCommand.ResubmitExecutor = (_, _) => new GenerateFromCurrentResubmitResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                StandardIntakeArtifactPaths =
                [
                    ".intent-cli/intake/auth.concept.yaml",
                    ".intent-cli/interviews/auth/iq-1.yaml"
                ],
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G138/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/138"],
                WorktreePaths = [".intent-cli/worktrees/G138"],
                StartedExecutionUnits = ["G138"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G138.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/138"],
                ReviewExecutionUnits = ["G138"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G138.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/G138.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/138#issuecomment-1"],
                FixingExecutionUnits = ["G138"],
                FixRequestArtifactPaths = [".intent-cli/fix/G138.request.md"],
                ResubmittedExecutionUnits = ["G138"],
                ResubmittedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/138"],
                ReadinessStatus = "ready",
                SkippedStages = []
            };
            GenerateFromCurrentRereviewCommand.RunRereviewExecutor = (_, executionUnit) => new RunRereviewResult
            {
                ExecutionUnit = executionUnit,
                LinkedPr = $"https://github.com/J-Tech-Japan/intent-system/pull/{executionUnit[1..]}"
            };

            var exitCode = GenerateFromCurrentRereviewCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature", "--from-file", "repair-comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/fix/G138.request.md", output, StringComparison.Ordinal);
            Assert.Contains("Rereviewed execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- G138", output, StringComparison.Ordinal);
            Assert.Contains("Rereviewed PR refs:", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/138", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentRereviewCommand.ResubmitExecutor = originalResubmitExecutor;
            GenerateFromCurrentRereviewCommand.RunRereviewExecutor = originalRunRereviewExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentRereviewCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-rereview-tests-").FullName;

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
