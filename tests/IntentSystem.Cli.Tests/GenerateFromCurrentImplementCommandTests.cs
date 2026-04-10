using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentImplementCommandTests
{
    [Fact]
    public void Execute_GivenRealPipeline_NotReadyAfterBridge_DefersActivationAndImplement()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGitHubRunner();

            var exitCode = GenerateFromCurrentCommand.Execute(
                CreateContext(repoRoot),
                ["implement", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current implement processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Readiness status: not-ready", output, StringComparison.Ordinal);
            Assert.Contains("Generated issue artifact paths:", output, StringComparison.Ordinal);
            Assert.Contains("Implement request artifact paths:", output, StringComparison.Ordinal);
            Assert.Contains("- issue-generation", output, StringComparison.Ordinal);
            Assert.Contains("- launch", output, StringComparison.Ordinal);
            Assert.Contains("- implement-handoff", output, StringComparison.Ordinal);

            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.current-sources.yaml")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.reconstructed-concept.yaml")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.reconstructed-interview.md")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-1.yaml")));
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G124")));
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "implement", "G124.request.md")));
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenReadyStageResults_ComposesToImplementHandoffSummary()
    {
        using var writer = new StringWriter();
        var originalAdvanceExecutor = GenerateFromCurrentImplementCommand.AdvanceExecutor;
        var originalStartExecutor = GenerateFromCurrentImplementCommand.StartExecutor;
        var originalRunImplementExecutor = GenerateFromCurrentImplementCommand.RunImplementExecutor;

        try
        {
            GenerateFromCurrentImplementCommand.AdvanceExecutor = (_, _) => new GenerateFromCurrentAdvanceResult
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
                ReadinessStatus = "ready",
                SkippedStages = []
            };
            GenerateFromCurrentImplementCommand.StartExecutor = (_, _, _) => new IntakeStartResult
            {
                Domain = "auth",
                StartedExecutionUnits = ["G124", "G125"],
                GeneratedArtifactPaths =
                [
                    ".intent-cli/issues/G124/packet.yaml",
                    ".intent-cli/issues/G125/packet.yaml"
                ],
                CreatedIssueRefs =
                [
                    "https://github.com/J-Tech-Japan/intent-system/issues/124",
                    "https://github.com/J-Tech-Japan/intent-system/issues/125"
                ],
                WorktreePaths =
                [
                    ".intent-cli/worktrees/G124",
                    ".intent-cli/worktrees/G125"
                ],
                SkippedUnits = []
            };
            GenerateFromCurrentImplementCommand.RunImplementExecutor = (_, executionUnit) => new RunImplementResult
            {
                Request = new RunImplementRequest
                {
                    ExecutionUnit = executionUnit,
                    State = "active",
                    ImplementRole = "Claude",
                    QueueWorkerRole = "coder",
                    QueueReviewRole = "reviewer",
                    WorktreePath = $".intent-cli/worktrees/{executionUnit}",
                    ChildRepoPath = "submodules/intent-system",
                    Branch = $"issue-124-{executionUnit.ToLowerInvariant()}",
                    LinkedIssue = $"https://github.com/J-Tech-Japan/intent-system/issues/{executionUnit[1..]}",
                    LatestLinkedPr = null,
                    PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                    ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                    IssueTitle = $"[{executionUnit}] Implement",
                    Goal = "Generate implement handoff.",
                    TargetPart = "cli",
                    TargetRepo = "submodules/intent-system",
                    TargetPath = ".",
                    InScope = ["implement handoff"],
                    OutOfScope = ["submit"],
                    AcceptanceCriteria = ["handoff exists"],
                    DeterministicReviewChecks = ["deterministic summary"],
                    ExpectedEvidence = ["dotnet test"]
                },
                ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
            };

            var exitCode = GenerateFromCurrentImplementCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/issues/G124/packet.yaml", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/124", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/worktrees/G124", output, StringComparison.Ordinal);
            Assert.Contains("- G124", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/implement/G124.request.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/implement/G125.request.md", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentImplementCommand.AdvanceExecutor = originalAdvanceExecutor;
            GenerateFromCurrentImplementCommand.StartExecutor = originalStartExecutor;
            GenerateFromCurrentImplementCommand.RunImplementExecutor = originalRunImplementExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentImplementCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-implement-tests-").FullName;

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
