using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentActivateCommandTests
{
    [Fact]
    public void Execute_GivenRealPipeline_NotReadyAfterBridge_RendersDeterministicSummary()
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
                ["activate", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current activate processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Source bundle artifact path: .intent-cli/intake/auth.current-sources.yaml", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.reconstructed-concept.yaml", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.reconstructed-interview.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/interviews/auth/iq-1.yaml", output, StringComparison.Ordinal);
            Assert.Contains("Readiness status: not-ready", output, StringComparison.Ordinal);
            Assert.Contains("Generated issue artifact paths:", output, StringComparison.Ordinal);
            Assert.Contains("Started execution units:", output, StringComparison.Ordinal);
            Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
            Assert.Contains("- issue-generation", output, StringComparison.Ordinal);
            Assert.Contains("- launch", output, StringComparison.Ordinal);

            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.current-sources.yaml")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.reconstructed-concept.yaml")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.reconstructed-interview.md")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "interviews", "auth", "iq-1.yaml")));
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.compile.md")));
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenReadyStageResults_ComposesToActiveSummary()
    {
        using var writer = new StringWriter();
        var originalAdvanceExecutor = GenerateFromCurrentActivateCommand.AdvanceExecutor;
        var originalStartExecutor = GenerateFromCurrentActivateCommand.IntakeStartExecutor;

        try
        {
            GenerateFromCurrentActivateCommand.AdvanceExecutor = (_, _) => new GenerateFromCurrentAdvanceResult
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
            GenerateFromCurrentActivateCommand.IntakeStartExecutor = (_, _, _) => new IntakeStartResult
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

            var exitCode = GenerateFromCurrentActivateCommand.Execute(
                CreateContext("/tmp/intent-system"),
                ["auth", "--from-path", "src/feature"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current activate processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/501", output, StringComparison.Ordinal);
            Assert.Contains("- /tmp/worktrees/AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/issues/AUTH-01/packet.yaml", output, StringComparison.Ordinal);
            Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentActivateCommand.AdvanceExecutor = originalAdvanceExecutor;
            GenerateFromCurrentActivateCommand.IntakeStartExecutor = originalStartExecutor;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentActivateCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-activate-tests-").FullName;

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
