using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G841 AC10: bug implementation-issue site 10 parse refusal and G337 recovery.</summary>
[Collection(RunSubmitCommandCollection.Name)]
public sealed class G841BugImplementationIssueTests
{
    [Fact]
    public void BugImplementationIssue_QuotedHashPacket_ExitsWithG337RecoveryMessage_NotFormatException()
    {
        using var temp = new TemporaryDirectory();
        var repoRoot = temp.CreateDirectory("repo");
        WriteRepairArtifacts(temp, "repo", "BUG-G841", "G841");
        temp.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G841", "packet.yaml"),
            """
            implementation_issue_packet:
              issue_title: "G841 repair"
              domain: intent-cli
              target_repo: submodules/intent-system
              source_artifact: "review of PR #1823"
            """);
        temp.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));

        var publisher = new CapturingPublisher();
        var originalPublisher = BugImplementationIssueCommand.PublisherFactory;
        var originalGit = BugImplementationIssueCommand.GitCommandRunnerFactory;
        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => publisher;
            BugImplementationIssueCommand.GitCommandRunnerFactory = () => new ResolvedRepoGitRunner("J-Tech-Japan/intent-system");

            using var writer = new StringWriter();
            var exitCode = BugImplementationIssueCommand.Execute(CreateContext(repoRoot), ["BUG-G841"], writer);
            var output = writer.ToString();

            Assert.Equal(1, exitCode);
            Assert.Contains(
                "Recorded repair target '.intent-cli/issues/G841/packet.yaml' uses the G337 'implementation_issue_packet' schema, but bug implementation-issue expects the legacy ProjectionPacketRuntimeReader 'execution_unit' schema. Run `intent-cli issue publish-flow G841 --repo J-Tech-Japan/intent-system --write` to publish the recorded repair packet before retrying.",
                output,
                StringComparison.Ordinal);
            Assert.DoesNotContain("FormatException", output, StringComparison.Ordinal);
            Assert.Equal(0, publisher.CallCount);
        }
        finally
        {
            BugImplementationIssueCommand.PublisherFactory = originalPublisher;
            BugImplementationIssueCommand.GitCommandRunnerFactory = originalGit;
        }
    }

    [Fact]
    public void BugImplementationIssue_UnparseablePacket_RefusesWithPacketPathAndParserMessage()
    {
        using var temp = new TemporaryDirectory();
        var repoRoot = temp.CreateDirectory("repo");
        WriteRepairArtifacts(temp, "repo", "BUG-G841U", "G841U");
        temp.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G841U", "packet.yaml"),
            G841TestHelpers.UnparseableYaml);
        temp.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));

        var publisher = new CapturingPublisher();
        var originalPublisher = BugImplementationIssueCommand.PublisherFactory;
        var originalGit = BugImplementationIssueCommand.GitCommandRunnerFactory;
        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => publisher;
            BugImplementationIssueCommand.GitCommandRunnerFactory = () => new ResolvedRepoGitRunner("J-Tech-Japan/intent-system");

            using var writer = new StringWriter();
            var exitCode = BugImplementationIssueCommand.Execute(CreateContext(repoRoot), ["BUG-G841U"], writer);
            var output = writer.ToString();

            Assert.Equal(1, exitCode);
            Assert.Contains(".intent-cli/issues/G841U/packet.yaml", output, StringComparison.Ordinal);
            Assert.Contains("could not be parsed", output, StringComparison.Ordinal);
            Assert.DoesNotContain("FormatException", output, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace", output, StringComparison.Ordinal);
            Assert.Equal(0, publisher.CallCount);
        }
        finally
        {
            BugImplementationIssueCommand.PublisherFactory = originalPublisher;
            BugImplementationIssueCommand.GitCommandRunnerFactory = originalGit;
        }
    }

    private static void WriteRepairArtifacts(TemporaryDirectory temp, string repoPath, string bugId, string unit)
    {
        temp.CreateFile(
            Path.Combine(repoPath, ".intent-cli", "bugs", $"{bugId}.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact(bugId, unit)));
        temp.CreateFile(
            Path.Combine(repoPath, ".intent-cli", "bugs", $"{bugId}.triage.yaml"),
            BugTriageArtifactYaml.Serialize(CreateBugTriageArtifact(bugId, unit)));
        temp.CreateFile(
            Path.Combine(repoPath, ".intent-cli", "bugs", $"{bugId}.plan.yaml"),
            BugExecutionArtifactYaml.Serialize(CreateBugExecutionArtifact(bugId, unit)));
        temp.CreateFile(
            Path.Combine(repoPath, ".intent-cli", "bugs", $"{bugId}.implementation-repair.yaml"),
            BugImplementationRepairArtifactYaml.Serialize(CreateRepairArtifact(bugId, unit)));
    }

    private static BugImplementationRepairArtifact CreateRepairArtifact(string bugId, string unit) =>
        new()
        {
            BugId = bugId,
            ExecutionRef = $".intent-cli/bugs/{bugId}.plan.yaml",
            ImplementationTaskCandidates = [unit],
            ImplementationRepairTargets = [$".intent-cli/issues/{unit}/packet.yaml"],
            SuggestedIssueTitle = $"Implementation repair ({bugId})",
            SuggestedGoal = "repair",
            ReadyToIssueCut = true,
            RepairExecutionUnit = unit,
        };

    private static BugReportArtifact CreateBugReportArtifact(string bugId, string unit) =>
        new()
        {
            DomainSlug = "intent-cli",
            BugId = bugId,
            Title = "G841 packet parse",
            ReportSource = "from-file",
            ProblemStatement = "parse failure",
            SuspectedFailureLocus = "packet.yaml",
            OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
            AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
            AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            ClarificationCandidates = [],
            LinkedExecutionUnits = [unit],
            LinkedIssueRefs = [],
            LinkedPrRefs = [],
            LinkedReviewRefs = [],
        };

    private static BugTriageArtifact CreateBugTriageArtifact(string bugId, string unit) =>
        new()
        {
            BugId = bugId,
            ReportRef = $".intent-cli/bugs/{bugId}.report.yaml",
            TriageClassification = "implementation-mismatch",
            DownstreamAction = "dual-track",
            ClarificationRequired = false,
            ClarificationReasons = [],
            OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL"],
            LinkedReviewRefs = [],
            ResolvedExecutionUnits = [unit],
            ResolvedImplementationRefs = [$".intent-cli/issues/{unit}/implementation.md"],
            ResolvedReviewContextRefs = [$".intent-cli/issues/{unit}/review-context.md"],
            ResolvedPacketRefs = [$".intent-cli/issues/{unit}/packet.yaml"],
            UnresolvedExecutionUnits = [],
            ImplementationRepairCandidates = [unit],
            IntentRepairCandidates = [],
        };

    private static BugExecutionArtifact CreateBugExecutionArtifact(string bugId, string unit) =>
        new()
        {
            BugId = bugId,
            ReportRef = $".intent-cli/bugs/{bugId}.report.yaml",
            TriageRef = $".intent-cli/bugs/{bugId}.triage.yaml",
            DownstreamAction = "dual-track",
            ResolvedImplementationRefs = [$".intent-cli/issues/{unit}/implementation.md"],
            ResolvedReviewContextRefs = [$".intent-cli/issues/{unit}/review-context.md"],
            ResolvedPacketRefs = [$".intent-cli/issues/{unit}/packet.yaml"],
            ImplementationTaskCandidates = [unit],
            IntentTaskCandidates = [],
            ClarificationRequired = false,
            ReadyToLaunch = true,
        };

    private static CliContext CreateContext(string repoRoot) => new()
    {
        RepoRoot = repoRoot,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

    private sealed class CapturingPublisher : IQueueDispatchPublisher
    {
        public int CallCount { get; private set; }

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            CallCount++;
            return new LinkedIssue { Repo = targetRepo, Number = 1, Url = "https://example.invalid/1" };
        }
    }

    private sealed class ResolvedRepoGitRunner(string repo) : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments) =>
            new()
            {
                ExitCode = 0,
                StdOut = $"git@github.com:{repo}.git{Environment.NewLine}",
                StdErr = string.Empty,
            };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("g841-bug-implementation-issue-").FullName;

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
