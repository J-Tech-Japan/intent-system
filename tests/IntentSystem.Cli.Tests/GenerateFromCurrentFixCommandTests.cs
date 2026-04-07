using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentFixCommandTests
{
    [Fact]
    public void Execute_GivenRealPipeline_NotReadyAfterBridge_DefersFixHandoff()
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
                ["fix", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution", "--from-file", "repair-comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current fix processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Readiness status: not-ready", output, StringComparison.Ordinal);
            Assert.Contains("Fix request artifact paths:", output, StringComparison.Ordinal);
            Assert.Contains("- fix-handoff", output, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "fix", "G134.request.md")));
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenReadyStageResults_ComposesToFixRequestSummary()
    {
        using var writer = new StringWriter();
        var originalCommentExecutor = GenerateFromCurrentFixCommand.CommentExecutor;
        var originalRunFixExecutor = GenerateFromCurrentFixCommand.RunFixExecutor;

        try
        {
            GenerateFromCurrentFixCommand.CommentExecutor = (_, _) => new GenerateFromCurrentCommentResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G134/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/134"],
                WorktreePaths = [".intent-cli/worktrees/G134"],
                StartedExecutionUnits = ["G134"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G134.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/134"],
                ReviewExecutionUnits = ["G134"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G134.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/G134.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/134#issuecomment-1"],
                FixingExecutionUnits = ["G134"],
                ReadinessStatus = "ready",
                SkippedStages = []
            };
            GenerateFromCurrentFixCommand.RunFixExecutor = (_, executionUnit) => new RunFixResult
            {
                Request = new RunFixRequest
                {
                    ExecutionUnit = executionUnit,
                    State = "fixing",
                    ImplementRole = "Claude",
                    QueueWorkerRole = "coder",
                    QueueReviewRole = "reviewer",
                    WorktreePath = $".intent-cli/worktrees/{executionUnit}",
                    ChildRepoPath = "submodules/intent-system",
                    Branch = $"issue-134-{executionUnit.ToLowerInvariant()}",
                    LinkedIssue = $"https://github.com/J-Tech-Japan/intent-system/issues/{executionUnit[1..]}",
                    LatestLinkedPr = $"https://github.com/J-Tech-Japan/intent-system/pull/{executionUnit[1..]}",
                    LatestCommentRef = $"https://github.com/J-Tech-Japan/intent-system/pull/{executionUnit[1..]}#issuecomment-1",
                    PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                    ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                    ReviewCommentArtifactRef = $".intent-cli/reviews/{executionUnit}.comment.json",
                    ReviewRequestRef = $".intent-cli/reviews/{executionUnit}.request.json",
                    ReviewCommentBodyPath = "/tmp/repair-comment.md",
                    IssueTitle = $"[{executionUnit}] Fix",
                    Goal = "Generate fix handoff.",
                    TargetPart = "cli",
                    TargetRepo = "submodules/intent-system",
                    TargetPath = ".",
                    InScope = ["fix handoff"],
                    OutOfScope = ["resubmit"],
                    AcceptanceCriteria = ["handoff exists"],
                    DeterministicReviewChecks = ["deterministic summary"],
                    ExpectedEvidence = ["dotnet test"]
                },
                ArtifactPath = $".intent-cli/fix/{executionUnit}.request.md"
            };

            var exitCode = GenerateFromCurrentFixCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature", "--from-file", "repair-comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/reviews/G134.comment.json", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/fix/G134.request.md", output, StringComparison.Ordinal);
            Assert.Contains("Fixing execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- G134", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentFixCommand.CommentExecutor = originalCommentExecutor;
            GenerateFromCurrentFixCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentFixCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-fix-tests-").FullName;

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
