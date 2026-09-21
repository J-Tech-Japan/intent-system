using System.Text;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class G847IssueBodyGateTests
{
    [Theory]
    [InlineData(65535, true)]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    [InlineData(70000, false)]
    public void IssueCreate_BoundaryIsInclusive(int bodyBytes, bool accepted)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = CreateIssueFixture(temporaryDirectory, AsciiBytes(bodyBytes));
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunIssueCreate(repoRoot, publisher, writer);

        Assert.Equal(accepted ? 0 : 1, exitCode);
        Assert.Equal(accepted ? 1 : 0, publisher.CallCount);
        if (!accepted)
        {
            Assert.Equal(
                $"Issue body is {bodyBytes} bytes, which exceeds the 65536-byte limit.{Environment.NewLine}",
                writer.ToString());
        }
    }

    [Theory]
    [InlineData(65535, true)]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    [InlineData(70000, false)]
    public void QueueDispatch_BoundaryIsInclusive(int bodyBytes, bool accepted)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = CreateQueueFixture(temporaryDirectory, AsciiBytes(bodyBytes));
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunQueueDispatch(repoRoot, publisher, writer);

        Assert.Equal(accepted ? 0 : 1, exitCode);
        Assert.Equal(accepted ? 1 : 0, publisher.CallCount);
        if (!accepted)
        {
            Assert.Equal(
                $"Issue body is {bodyBytes} bytes, which exceeds the 65536-byte limit.{Environment.NewLine}",
                writer.ToString());
        }
    }

    [Theory]
    [InlineData(65535, true)]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    [InlineData(70000, false)]
    public void BugImplementationIssue_BoundaryIsInclusive(int bodyBytes, bool accepted)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = CreateBugFixtureAtBodySize(temporaryDirectory, bodyBytes);
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunBugImplementationIssue(repoRoot, publisher, writer);

        Assert.Equal(accepted ? 0 : 1, exitCode);
        Assert.Equal(accepted ? 1 : 0, publisher.CallCount);
        if (accepted)
        {
            Assert.Equal(bodyBytes, Encoding.UTF8.GetByteCount(publisher.Body));
        }
        else
        {
            Assert.Contains("Issue body is ", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("which exceeds the 65536-byte limit.", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IssueCreate_Utf8BomBodyCountsDecodedContent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = CreateIssueFixture(temporaryDirectory, BomBytes(65536));
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunIssueCreate(repoRoot, publisher, writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(65536, Encoding.UTF8.GetByteCount(publisher.Body));
        Assert.DoesNotContain('\uFEFF', publisher.Body);
    }

    [Fact]
    public void IssueCreate_Utf8BomBodyOverTheDecodedLimitIsRefused()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = CreateIssueFixture(temporaryDirectory, BomBytes(65537));
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunIssueCreate(repoRoot, publisher, writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, publisher.CallCount);
        Assert.Equal(
            $"Issue body is 65537 bytes, which exceeds the 65536-byte limit.{Environment.NewLine}",
            writer.ToString());
    }

    [Fact]
    public void QueueDispatch_Utf8BomBodyCountsDecodedContent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = CreateQueueFixture(temporaryDirectory, BomBytes(65536));
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunQueueDispatch(repoRoot, publisher, writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(65536, Encoding.UTF8.GetByteCount(publisher.Body));
        Assert.DoesNotContain('\uFEFF', publisher.Body);
    }

    [Fact]
    public void QueueDispatch_Utf8BomBodyOverTheDecodedLimitIsRefused()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = CreateQueueFixture(temporaryDirectory, BomBytes(65537));
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunQueueDispatch(repoRoot, publisher, writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, publisher.CallCount);
        Assert.Equal(
            $"Issue body is 65537 bytes, which exceeds the 65536-byte limit.{Environment.NewLine}",
            writer.ToString());
    }

    [Fact]
    public void IssueCreate_InvalidUtf8IsRefusedBeforePublishing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var bodyPath = Path.Combine(temporaryDirectory.RootPath, "repo", ".intent-cli", "issues", "G847", "github-body.md");
        var repoRoot = CreateIssueFixture(temporaryDirectory, InvalidUtf8Fixture());
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunIssueCreate(repoRoot, publisher, writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, publisher.CallCount);
        Assert.Equal(
            $"Issue body is not valid UTF-8 at byte offset 1000 in {bodyPath}.{Environment.NewLine}",
            writer.ToString());
    }

    [Fact]
    public void QueueDispatch_InvalidUtf8IsRefusedBeforePublishing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var bodyPath = Path.Combine(temporaryDirectory.RootPath, "repo", ".intent-cli", "issues", "G847", "github-body.md");
        var repoRoot = CreateQueueFixture(temporaryDirectory, InvalidUtf8Fixture());
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunQueueDispatch(repoRoot, publisher, writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, publisher.CallCount);
        Assert.Equal(
            $"Issue body is not valid UTF-8 at byte offset 1000 in {bodyPath}.{Environment.NewLine}",
            writer.ToString());
    }

    [Theory]
    [InlineData("repair")]
    [InlineData("target")]
    [InlineData("execution")]
    [InlineData("triage")]
    [InlineData("report")]
    public void BugImplementationIssue_InvalidUtf8InEachDistinctArtifactIsRefusedBeforePublishing(string artifact)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = CreateBugFixture(temporaryDirectory);
        var artifactPath = BugArtifactPath(temporaryDirectory.RootPath, artifact);
        File.WriteAllBytes(artifactPath, InvalidUtf8Fixture());
        var publisher = new CapturingPublisher();
        using var writer = new StringWriter();

        var exitCode = RunBugImplementationIssue(repoRoot, publisher, writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, publisher.CallCount);
        Assert.Equal(
            $"Issue body is not valid UTF-8 at byte offset 1000 in {artifactPath}.{Environment.NewLine}",
            writer.ToString());
    }

    private static int RunIssueCreate(string repoRoot, CapturingPublisher publisher, StringWriter writer)
    {
        var originalPublisherFactory = IssueCreateCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssueCreateCommand.GitCommandRunnerFactory;
        try
        {
            IssueCreateCommand.PublisherFactory = () => publisher;
            IssueCreateCommand.GitCommandRunnerFactory = () => new FakeGitCommandRunner();
            return IssueCreateCommand.Execute(CreateContext(repoRoot), ["G847"], writer);
        }
        finally
        {
            IssueCreateCommand.PublisherFactory = originalPublisherFactory;
            IssueCreateCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    private static int RunQueueDispatch(string repoRoot, CapturingPublisher publisher, StringWriter writer)
    {
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        try
        {
            QueueDispatchCommand.PublisherFactory = () => publisher;
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeGitCommandRunner();
            return QueueDispatchCommand.Execute(CreateContext(repoRoot), ["G847"], writer);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    private static int RunBugImplementationIssue(string repoRoot, CapturingPublisher publisher, StringWriter writer)
    {
        var originalPublisherFactory = BugImplementationIssueCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = BugImplementationIssueCommand.GitCommandRunnerFactory;
        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => publisher;
            BugImplementationIssueCommand.GitCommandRunnerFactory = () => new FakeGitCommandRunner();
            return BugImplementationIssueCommand.Execute(CreateContext(repoRoot), ["BUG-G847"], writer);
        }
        finally
        {
            BugImplementationIssueCommand.PublisherFactory = originalPublisherFactory;
            BugImplementationIssueCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    private static string CreateIssueFixture(TemporaryDirectory temporaryDirectory, byte[] bodyBytes)
    {
        var repoRoot = temporaryDirectory.CreateDirectory("repo");
        temporaryDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        temporaryDirectory.WriteText(Path.Combine("repo", ".intent-cli", "issues", "G847", "packet.yaml"), IssuePacketYaml());
        temporaryDirectory.WriteBytes(Path.Combine("repo", ".intent-cli", "issues", "G847", "github-body.md"), bodyBytes);
        temporaryDirectory.WriteText(Path.Combine("repo", ".intent-cli", "issues", "G847", "publish.yaml"), IssuePublishYaml());
        return repoRoot;
    }

    private static string CreateQueueFixture(TemporaryDirectory temporaryDirectory, byte[] bodyBytes)
    {
        var repoRoot = temporaryDirectory.CreateDirectory("repo");
        temporaryDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        temporaryDirectory.WriteText(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        temporaryDirectory.WriteText(Path.Combine("repo", ".intent-cli", "runs.jsonl"), string.Empty);
        temporaryDirectory.WriteText(Path.Combine("repo", ".intent-cli", "issues", "G847", "packet.yaml"), QueuePacketYaml());
        temporaryDirectory.WriteBytes(Path.Combine("repo", ".intent-cli", "issues", "G847", "github-body.md"), bodyBytes);
        return repoRoot;
    }

    private static string CreateBugFixtureAtBodySize(TemporaryDirectory temporaryDirectory, int bodyBytes)
    {
        var repoRoot = CreateBugFixture(temporaryDirectory);
        WriteBugReport(repoRoot, "P");
        var publisher = new CapturingPublisher();
        using var baselineWriter = new StringWriter();
        var baselineExitCode = RunBugImplementationIssue(repoRoot, publisher, baselineWriter);
        Assert.Equal(0, baselineExitCode);

        var baselineBodyBytes = Encoding.UTF8.GetByteCount(publisher.Body);
        var titleSuffix = (bodyBytes - baselineBodyBytes) % 2 == 0 ? string.Empty : "T";
        if (titleSuffix.Length != 0)
        {
            WriteBugRepair(
                repoRoot,
                suggestedIssueTitle: "[G847] JSON payload body gate" + titleSuffix);
            baselineBodyBytes++;
        }

        var problemLength = Math.Max(1, 1 + (bodyBytes - baselineBodyBytes) / 2);
        WriteBugReport(repoRoot, new string('P', problemLength));

        if (bodyBytes <= 65536)
        {
            var finalPublisher = new CapturingPublisher();
            using var finalWriter = new StringWriter();
            var finalExitCode = RunBugImplementationIssue(repoRoot, finalPublisher, finalWriter);
            Assert.Equal(0, finalExitCode);
            Assert.Equal(1, finalPublisher.CallCount);
            Assert.Equal(bodyBytes, Encoding.UTF8.GetByteCount(finalPublisher.Body));
        }

        return repoRoot;
    }

    private static string CreateBugFixture(TemporaryDirectory temporaryDirectory)
    {
        var repoRoot = temporaryDirectory.CreateDirectory("repo");
        temporaryDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        temporaryDirectory.WriteText(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-G847.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReport(string.Empty)));
        temporaryDirectory.WriteText(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-G847.triage.yaml"),
            BugTriageArtifactYaml.Serialize(CreateBugTriage()));
        temporaryDirectory.WriteText(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-G847.plan.yaml"),
            BugExecutionArtifactYaml.Serialize(CreateBugExecution()));
        WriteBugRepair(repoRoot, "[G847] JSON payload body gate");
        temporaryDirectory.WriteText(Path.Combine("repo", ".intent-cli", "issues", "G847", "packet.yaml"), BugPacketYaml());
        return repoRoot;
    }

    private static void WriteBugRepair(string repoRoot, string suggestedIssueTitle)
    {
        var artifact = new BugImplementationRepairArtifact
        {
            BugId = "BUG-G847",
            ExecutionRef = ".intent-cli/bugs/BUG-G847.plan.yaml",
            ImplementationTaskCandidates = ["G847"],
            ImplementationRepairTargets = [".intent-cli/issues/G847/packet.yaml"],
            SuggestedIssueTitle = suggestedIssueTitle,
            SuggestedGoal = "Refuse oversized or malformed JSON-payload issue bodies.",
            ReadyToIssueCut = true,
            RepairExecutionUnit = "G847"
        };
        File.WriteAllText(
            Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-G847.implementation-repair.yaml"),
            BugImplementationRepairArtifactYaml.Serialize(artifact));
    }

    private static void WriteBugReport(string repoRoot, string problemStatement)
    {
        File.WriteAllText(
            Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-G847.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReport(problemStatement)));
    }

    private static BugReportArtifact CreateBugReport(string problemStatement) => new()
    {
        DomainSlug = "intent-cli",
        BugId = "BUG-G847",
        Title = "JSON payload body gate",
        ReportSource = "test",
        ProblemStatement = problemStatement,
        SuspectedFailureLocus = "issue body transmission",
        OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
        AffectedIntentRefs = ["intents/intent-cli/means/orchestration.md"],
        AffectedRuleSpecRefs = ["intents/rules/issue-lifecycle-and-landing.md"],
        ClarificationCandidates = [],
        LinkedExecutionUnits = ["G847"],
        LinkedIssueRefs = [],
        LinkedPrRefs = [],
        LinkedReviewRefs = []
    };

    private static BugTriageArtifact CreateBugTriage() => new()
    {
        BugId = "BUG-G847",
        ReportRef = ".intent-cli/bugs/BUG-G847.report.yaml",
        TriageClassification = "implementation-mismatch",
        DownstreamAction = "implementation-only",
        ClarificationRequired = false,
        ClarificationReasons = [],
        OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL"],
        LinkedReviewRefs = [],
        ResolvedExecutionUnits = ["G847"],
        ResolvedImplementationRefs = [".intent-cli/issues/G847/implementation.md"],
        ResolvedReviewContextRefs = [".intent-cli/issues/G847/review-context.md"],
        ResolvedPacketRefs = [".intent-cli/issues/G847/packet.yaml"],
        UnresolvedExecutionUnits = [],
        ImplementationRepairCandidates = ["G847"],
        IntentRepairCandidates = []
    };

    private static BugExecutionArtifact CreateBugExecution() => new()
    {
        BugId = "BUG-G847",
        ReportRef = ".intent-cli/bugs/BUG-G847.report.yaml",
        TriageRef = ".intent-cli/bugs/BUG-G847.triage.yaml",
        DownstreamAction = "implementation-only",
        ResolvedImplementationRefs = [".intent-cli/issues/G847/implementation.md"],
        ResolvedReviewContextRefs = [".intent-cli/issues/G847/review-context.md"],
        ResolvedPacketRefs = [".intent-cli/issues/G847/packet.yaml"],
        ImplementationTaskCandidates = ["G847"],
        IntentTaskCandidates = [],
        ClarificationRequired = false,
        ReadyToLaunch = true
    };

    private static string BugArtifactPath(string root, string artifact) => artifact switch
    {
        "repair" => Path.Combine(root, "repo", ".intent-cli", "bugs", "BUG-G847.implementation-repair.yaml"),
        "target" => Path.Combine(root, "repo", ".intent-cli", "issues", "G847", "packet.yaml"),
        "execution" => Path.Combine(root, "repo", ".intent-cli", "bugs", "BUG-G847.plan.yaml"),
        "triage" => Path.Combine(root, "repo", ".intent-cli", "bugs", "BUG-G847.triage.yaml"),
        "report" => Path.Combine(root, "repo", ".intent-cli", "bugs", "BUG-G847.report.yaml"),
        _ => throw new ArgumentOutOfRangeException(nameof(artifact), artifact, "Unknown bug artifact.")
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
                WorktreeRoot = ".intent-cli/worktrees"
            }
        }
    };

    private static QueueState CreateQueueState() => new()
    {
        SchemaVersion = "1",
        UpdatedAt = DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
        Items =
        [
            new QueueItem
            {
                ExecutionUnit = "G847",
                Title = "[G847] JSON payload body gate",
                State = QueueItemState.Queued,
                Dependencies = [],
                BlockedBy = [],
                ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                PacketPaths = new PacketPaths
                {
                    Implementation = ".intent-cli/issues/G847/implementation.md",
                    ReviewContext = ".intent-cli/issues/G847/review-context.md",
                    Yaml = ".intent-cli/issues/G847/packet.yaml"
                },
                WorkerRole = "coder",
                ReviewRole = "reviewer",
                Priority = "high"
            }
        ]
    };

    private static string IssuePacketYaml() => """
    execution_unit: G847
    implementation_issue:
      issue_title: "[G847] JSON payload body gate"
      target_repo: "submodules/intent-system"
      target_path: "src/IntentSystem.Cli"
      target_part: "JSON-payload issue body transmission"
      dependencies: []
    """;

    private static string IssuePublishYaml() => """
    execution_unit: G847
    publish_status: drafted
    packet_path: ".intent-cli/issues/G847/packet.yaml"
    issue_body_path: ".intent-cli/issues/G847/github-body.md"
    created_issue_number: null
    created_issue_url: null
    published_label_name: null
    """;

    private static string QueuePacketYaml() => """
    implementation_issue_packet:
      issue_title: "[G847] JSON payload body gate"
      issue_kind: "bugfix"
      source_execution_unit: "G847"
      goal: "Refuse oversized or malformed JSON-payload issue bodies."
      in_scope:
        - "issue body gate"
      out_of_scope:
        - "body-file routes"
      target_repo: "submodules/intent-system"
      target_path: "src/IntentSystem.Cli"
      target_part: "JSON-payload issue body transmission"
      dependencies: []
      technical_baseline:
        - "C# / .NET"
      project_local_guide:
        - "AGENTS.md"
      intent_baseline:
        - "submitted body content is the measured quantity"
      intent_references:
        - "ICL.P.PRODUCT_GOAL"
      rules_and_specs:
        - "intents/rules/issue-lifecycle-and-landing.md"
      acceptance_criteria:
        - "oversized body is refused"
      verification_evidence:
        - "focused tests pass"
      review_mode: "deterministic-review"
      completion_action: "wait-for-deterministic-review"
      landing_policy: "merge-after-review"

    review_context_packet:
      source_execution_unit: "G847"
      parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
      intent_references:
        - "ICL.P.PRODUCT_GOAL"
      rules_and_specs:
        - "intents/rules/issue-lifecycle-and-landing.md"
      acceptance_criteria:
        - "oversized body is refused"
      deterministic_review_checks:
        - "publisher is not called"
      clarification_return_path: "intents/intent-cli/clarifications/open.md"
    """;

    private static string BugPacketYaml() => """
    execution_unit: G847
    implementation_issue:
      issue_title: "[G847] JSON payload body gate"
      goal: "Refuse oversized or malformed JSON-payload issue bodies."
      target_repo: "submodules/intent-system"
      target_path: "src/IntentSystem.Cli"
      target_part: "JSON-payload issue body transmission"
      dependencies: []
      technical_baseline:
        - "C# / .NET"
      project_local_guide:
        - "AGENTS.md"
      intent_baseline:
        - "submitted body content is the measured quantity"
      intent_references:
        - "ICL.P.PRODUCT_GOAL"
      rules_and_specs:
        - "intents/rules/issue-lifecycle-and-landing.md"
      in_scope:
        - "issue body gate"
      out_of_scope:
        - "body-file routes"
      acceptance_criteria:
        - "oversized body is refused"
      verification_evidence:
        - "focused tests pass"
    review:
      clarification_return_path: "intents/intent-cli/clarifications/open.md"
    """;

    private static byte[] AsciiBytes(int count) => Encoding.UTF8.GetBytes(new string('A', count));

    private static byte[] BomBytes(int contentBytes)
    {
        return [0xEF, 0xBB, 0xBF, .. AsciiBytes(contentBytes)];
    }

    private static byte[] InvalidUtf8Fixture()
    {
        var bytes = AsciiBytes(50_003);
        bytes[1000] = 0xFF;
        return bytes;
    }

    private sealed class CapturingPublisher : IQueueDispatchPublisher
    {
        public int CallCount { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            CallCount++;
            Body = body;
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 847,
                Url = "https://example.invalid/issues/847"
            };
        }

        public void AddLabel(string targetRepo, int issueNumber, string labelName)
        {
        }
    }

    private sealed class FakeGitCommandRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments) => new()
        {
            ExitCode = 0,
            StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
            StdErr = string.Empty
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("g847-issue-body-gate-").FullName;

        public string RootPath => rootPath;

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public string WriteText(string relativePath, string contents)
        {
            var path = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public string WriteBytes(string relativePath, byte[] bytes)
        {
            var path = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
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
