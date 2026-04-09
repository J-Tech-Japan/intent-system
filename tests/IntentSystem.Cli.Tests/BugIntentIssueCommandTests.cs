using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentIssueCommandTests
{
    [Fact]
    public void Execute_GivenReadyIntentRepair_CreatesSingleParentIssueAndWritesArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory("parent-intent");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.intent-repair.yaml"),
            BugIntentRepairArtifactYaml.Serialize(CreateRepairArtifact(readyToIssueCut: true)));
        using var writer = new StringWriter();
        var originalPublisherFactory = BugIntentIssueCommand.PublisherFactory;
        var originalGitRunnerFactory = BugIntentIssueCommand.GitCommandRunnerFactory;

        try
        {
            BugIntentIssueCommand.PublisherFactory = () => new FakePublisher();
            BugIntentIssueCommand.GitCommandRunnerFactory = () => new FakeGitCommandRunner();

            var exitCode = BugIntentIssueCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Bug intent-issue artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugIntentIssueArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.intent-issue.yaml")));
            Assert.Equal(".intent-cli/bugs/BUG-123.intent-repair.yaml", artifact.IntentRepairRef);
            Assert.Equal("Intent repair: OAuth callback loop (BUG-123)", artifact.CreatedIssueTitle);
            Assert.Equal("https://github.com/J-Tech-Japan/MyIntentHost/issues/53", artifact.CreatedIssueUrl);
            Assert.Equal(53, artifact.CreatedIssueNumber);
            Assert.Equal(
                ["intent:intents/intent-cli/means/auth.md", "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
                artifact.ParentRepairTargets);
        }
        finally
        {
            BugIntentIssueCommand.PublisherFactory = originalPublisherFactory;
            BugIntentIssueCommand.GitCommandRunnerFactory = originalGitRunnerFactory;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyIntentRepair_DoesNotCreateIssue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.intent-repair.yaml"),
            BugIntentRepairArtifactYaml.Serialize(CreateRepairArtifact(bugId: "BUG-124", readyToIssueCut: false)));
        using var writer = new StringWriter();
        var originalPublisherFactory = BugIntentIssueCommand.PublisherFactory;

        try
        {
            BugIntentIssueCommand.PublisherFactory = () => new ThrowingPublisher();

            var exitCode = BugIntentIssueCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

            Assert.Equal(0, exitCode);

            var artifact = BugIntentIssueArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.intent-issue.yaml")));
            Assert.Equal("Intent repair: OAuth callback loop (BUG-124)", artifact.CreatedIssueTitle);
            Assert.Null(artifact.CreatedIssueUrl);
            Assert.Null(artifact.CreatedIssueNumber);
            Assert.Empty(artifact.ParentRepairTargets);
        }
        finally
        {
            BugIntentIssueCommand.PublisherFactory = originalPublisherFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingIntentRepairArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugIntentIssueCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug intent-repair artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    private static BugIntentRepairArtifact CreateRepairArtifact(string bugId = "BUG-123", bool readyToIssueCut = true)
    {
        return new BugIntentRepairArtifact
        {
            BugId = bugId,
            ExecutionRef = $".intent-cli/bugs/{bugId}.plan.yaml",
            IntentTaskCandidates =
            [
                "intents/intent-cli/means/auth.md",
                "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ],
            ParentRepairTargets = readyToIssueCut
                ? ["intent:intents/intent-cli/means/auth.md", "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"]
                : [],
            SuggestedIssueTitle = $"Intent repair: OAuth callback loop ({bugId})",
            SuggestedGoal = readyToIssueCut
                ? $"Repair parent intent targets for 'OAuth callback loop' ({bugId}) using .intent-cli/bugs/{bugId}.plan.yaml: intent:intents/intent-cli/means/auth.md, rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
                : $"Prepare parent intent repair for 'OAuth callback loop' ({bugId}) once issue-cut blockers are cleared from .intent-cli/bugs/{bugId}.plan.yaml.",
            ReadyToIssueCut = readyToIssueCut
        };
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
                    ParentIntentRepoRoot = "../parent-intent"
                }
            }
        };
    }

    private sealed class FakePublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 53,
                Url = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53"
            };
        }
    }

    private sealed class ThrowingPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            throw new InvalidOperationException("CreateIssue should not be called when ready_to_issue_cut is false.");
        }
    }

    private sealed class FakeGitCommandRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "git@github.com:J-Tech-Japan/MyIntentHost.git" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-intent-issue-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
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
