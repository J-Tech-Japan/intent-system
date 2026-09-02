using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugImplementationRepairG782Tests
{
    [Fact]
    public void Execute_G782_AcceptsGuideListedLinkFlagsAndRecordsOptionalFields()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = temporaryDirectory.CreateDirectory("repo");
        CreateBaseArtifacts(temporaryDirectory, "repo", "BUG-1706");
        var originalTimestampFactory = BugImplementationRepairCommand.TimestampFactory;
        var recordedAt = new DateTimeOffset(2026, 9, 2, 12, 30, 0, TimeSpan.Zero);
        using var writer = new StringWriter();

        try
        {
            BugImplementationRepairCommand.TimestampFactory = () => recordedAt;

            var exitCode = BugImplementationRepairCommand.Execute(
                CreateContext(repoRoot),
                [
                    "BUG-1706",
                    "--execution-unit", "G782",
                    "--issue-number", "1706",
                    "--issue-url", "https://github.com/J-Tech-Japan/intent-system/issues/1706",
                    "--actor", "implementation",
                    "--note", "ready for issue cut"
                ],
                writer);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            BugImplementationRepairCommand.TimestampFactory = originalTimestampFactory;
        }

        var yaml = File.ReadAllText(ArtifactPath(repoRoot, "BUG-1706"));
        var artifact = BugImplementationRepairArtifactYaml.Deserialize(yaml);
        Assert.Equal("G782", artifact.RepairExecutionUnit);
        Assert.Equal(1706, artifact.RepairIssueNumber);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/1706", artifact.RepairIssueUrl);
        Assert.Equal("implementation", artifact.RecordedBy);
        Assert.Equal("ready for issue cut", artifact.Note);
        Assert.Equal(recordedAt, artifact.RecordedAt);
        Assert.Contains("repair_execution_unit", yaml, StringComparison.Ordinal);
        Assert.Contains("repair_issue_number", yaml, StringComparison.Ordinal);
        Assert.Contains("repair_issue_url", yaml, StringComparison.Ordinal);
        Assert.Contains("recorded_by", yaml, StringComparison.Ordinal);
        Assert.Contains("recorded_at", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G782_RejectsMismatchedIssueNumberAndUrlWithoutWritingArtifact()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = temporaryDirectory.CreateDirectory("repo");
        CreateBaseArtifacts(temporaryDirectory, "repo", "BUG-1706");
        using var writer = new StringWriter();

        var exitCode = BugImplementationRepairCommand.Execute(
            CreateContext(repoRoot),
            [
                "BUG-1706",
                "--issue-number", "1706",
                "--issue-url", "https://github.com/J-Tech-Japan/intent-system/issues/1705"
            ],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("1706", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("https://github.com/J-Tech-Japan/intent-system/issues/1705", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(ArtifactPath(repoRoot, "BUG-1706")));
    }

    [Fact]
    public void Execute_G782_RerunsPreserveLinksAndReplacementNamesPriorValues()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = temporaryDirectory.CreateDirectory("repo");
        CreateBaseArtifacts(temporaryDirectory, "repo", "BUG-1706");
        var originalTimestampFactory = BugImplementationRepairCommand.TimestampFactory;
        var firstRecordedAt = new DateTimeOffset(2026, 9, 2, 12, 30, 0, TimeSpan.Zero);
        var replacementRecordedAt = firstRecordedAt.AddMinutes(1);

        try
        {
            BugImplementationRepairCommand.TimestampFactory = () => firstRecordedAt;
            using (var initialWriter = new StringWriter())
            {
                Assert.Equal(
                    0,
                    BugImplementationRepairCommand.Execute(
                        CreateContext(repoRoot),
                        [
                            "BUG-1706",
                            "--execution-unit", "G782",
                            "--issue-number", "1706",
                            "--issue-url", "https://github.com/J-Tech-Japan/intent-system/issues/1706",
                            "--actor", "first-actor",
                            "--note", "first note"
                        ],
                        initialWriter));
            }

            BugImplementationRepairCommand.TimestampFactory = () => replacementRecordedAt;
            using (var noFlagsWriter = new StringWriter())
            {
                Assert.Equal(
                    0,
                    BugImplementationRepairCommand.Execute(CreateContext(repoRoot), ["BUG-1706"], noFlagsWriter));
            }

            var preserved = BugImplementationRepairArtifactYaml.Deserialize(File.ReadAllText(ArtifactPath(repoRoot, "BUG-1706")));
            Assert.Equal("G782", preserved.RepairExecutionUnit);
            Assert.Equal(1706, preserved.RepairIssueNumber);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/1706", preserved.RepairIssueUrl);
            Assert.Equal("first-actor", preserved.RecordedBy);
            Assert.Equal("first note", preserved.Note);
            Assert.Equal(firstRecordedAt, preserved.RecordedAt);

            using var replacementWriter = new StringWriter();
            Assert.Equal(
                0,
                BugImplementationRepairCommand.Execute(
                    CreateContext(repoRoot),
                    [
                        "BUG-1706",
                        "--execution-unit", "G783",
                        "--issue-number", "1707",
                        "--issue-url", "https://github.com/J-Tech-Japan/intent-system/issues/1707",
                        "--actor", "second-actor",
                        "--note", "second note"
                    ],
                    replacementWriter));

            var replaced = BugImplementationRepairArtifactYaml.Deserialize(File.ReadAllText(ArtifactPath(repoRoot, "BUG-1706")));
            Assert.Equal("G783", replaced.RepairExecutionUnit);
            Assert.Equal(1707, replaced.RepairIssueNumber);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/1707", replaced.RepairIssueUrl);
            Assert.Equal("second-actor", replaced.RecordedBy);
            Assert.Equal("second note", replaced.Note);
            Assert.Equal(replacementRecordedAt, replaced.RecordedAt);
            Assert.Contains("Previous recorded repair link", replacementWriter.ToString(), StringComparison.Ordinal);
            Assert.Contains("repair_execution_unit=G782", replacementWriter.ToString(), StringComparison.Ordinal);
            Assert.Contains("repair_issue_number=1706", replacementWriter.ToString(), StringComparison.Ordinal);
            Assert.Contains("first note", replacementWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            BugImplementationRepairCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void ImplementationIssue_G782_UsesRecordedRepairTargetAsTheOnlyPacketTarget()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = temporaryDirectory.CreateDirectory("repo");
        CreateBaseArtifacts(temporaryDirectory, "repo", "BUG-1706");
        temporaryDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-1706.implementation-repair.yaml"),
            BugImplementationRepairArtifactYaml.Serialize(CreateRepairArtifact("BUG-1706", "G782")));
        temporaryDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G782", "packet.yaml"),
            CreateLegacyPacketYaml());
        temporaryDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var originalPublisherFactory = BugImplementationIssueCommand.PublisherFactory;
        var originalGitRunnerFactory = BugImplementationIssueCommand.GitCommandRunnerFactory;
        var publisher = new CapturingPublisher();

        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => publisher;
            BugImplementationIssueCommand.GitCommandRunnerFactory = () => new FakeGitCommandRunner();

            using var writer = new StringWriter();
            var exitCode = BugImplementationIssueCommand.Execute(CreateContext(repoRoot), ["BUG-1706"], writer);

            Assert.Equal(0, exitCode);
            var artifact = BugImplementationIssueArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-1706.implementation-issue.yaml")));
            Assert.Equal([".intent-cli/issues/G782/packet.yaml"], artifact.ImplementationRepairTargets);
            Assert.NotNull(publisher.Body);
            Assert.Contains("Packet target: .intent-cli/issues/G782/packet.yaml", publisher.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Packet target: .intent-cli/issues/G25/packet.yaml", publisher.Body, StringComparison.Ordinal);
        }
        finally
        {
            BugImplementationIssueCommand.PublisherFactory = originalPublisherFactory;
            BugImplementationIssueCommand.GitCommandRunnerFactory = originalGitRunnerFactory;
        }
    }

    [Fact]
    public void ImplementationIssue_G782_RefusesRecordedG337PacketWithPublishFlowRoute()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repoRoot = temporaryDirectory.CreateDirectory("repo");
        CreateBaseArtifacts(temporaryDirectory, "repo", "BUG-1706");
        temporaryDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-1706.implementation-repair.yaml"),
            BugImplementationRepairArtifactYaml.Serialize(CreateRepairArtifact("BUG-1706", "G782")));
        temporaryDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G782", "packet.yaml"),
            CreateG337PacketYaml());
        var originalPublisherFactory = BugImplementationIssueCommand.PublisherFactory;

        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => new ThrowingPublisher();
            using var writer = new StringWriter();

            var exitCode = BugImplementationIssueCommand.Execute(CreateContext(repoRoot), ["BUG-1706"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains(".intent-cli/issues/G782/packet.yaml", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("implementation_issue_packet", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("execution_unit", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("intent-cli issue publish-flow G782", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            BugImplementationIssueCommand.PublisherFactory = originalPublisherFactory;
        }
    }

    private static void CreateBaseArtifacts(TemporaryDirectory temporaryDirectory, string repoPath, string bugId)
    {
        temporaryDirectory.CreateFile(
            Path.Combine(repoPath, ".intent-cli", "bugs", $"{bugId}.report.yaml"),
            BugReportArtifactYaml.Serialize(new BugReportArtifact
            {
                DomainSlug = "intent-cli",
                BugId = bugId,
                Title = "Repair packet links",
                ReportSource = "inline-text",
                ProblemStatement = "The repair packet link must survive reruns.",
                SuspectedFailureLocus = "implementation repair command",
                OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
                AffectedIntentRefs = ["intents/intent-cli/means/implementation.md"],
                AffectedRuleSpecRefs = ["intents/rules/bug-repair.md"],
                ClarificationCandidates = [],
                LinkedExecutionUnits = ["G25"],
                LinkedIssueRefs = [],
                LinkedPrRefs = [],
                LinkedReviewRefs = []
            }));
        temporaryDirectory.CreateFile(
            Path.Combine(repoPath, ".intent-cli", "bugs", $"{bugId}.triage.yaml"),
            BugTriageArtifactYaml.Serialize(new BugTriageArtifact
            {
                BugId = bugId,
                ReportRef = $".intent-cli/bugs/{bugId}.report.yaml",
                TriageClassification = "implementation-mismatch",
                DownstreamAction = "implementation-only",
                ClarificationRequired = false,
                ClarificationReasons = [],
                OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL"],
                LinkedReviewRefs = [],
                ResolvedExecutionUnits = ["G25"],
                ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
                ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
                ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
                UnresolvedExecutionUnits = [],
                ImplementationRepairCandidates = ["G25"],
                IntentRepairCandidates = []
            }));
        temporaryDirectory.CreateFile(
            Path.Combine(repoPath, ".intent-cli", "bugs", $"{bugId}.plan.yaml"),
            BugExecutionArtifactYaml.Serialize(new BugExecutionArtifact
            {
                BugId = bugId,
                ReportRef = $".intent-cli/bugs/{bugId}.report.yaml",
                TriageRef = $".intent-cli/bugs/{bugId}.triage.yaml",
                DownstreamAction = "implementation-only",
                ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
                ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
                ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
                ImplementationTaskCandidates = ["G25"],
                IntentTaskCandidates = [],
                ClarificationRequired = false,
                ReadyToLaunch = true
            }));
    }

    private static BugImplementationRepairArtifact CreateRepairArtifact(string bugId, string repairExecutionUnit)
    {
        return new BugImplementationRepairArtifact
        {
            BugId = bugId,
            ExecutionRef = $".intent-cli/bugs/{bugId}.plan.yaml",
            ImplementationTaskCandidates = ["G25"],
            ImplementationRepairTargets = [".intent-cli/issues/G25/packet.yaml"],
            SuggestedIssueTitle = $"Implementation repair: Repair packet links ({bugId})",
            SuggestedGoal = "Repair the recorded packet link.",
            ReadyToIssueCut = true,
            RepairExecutionUnit = repairExecutionUnit
        };
    }

    private static string CreateLegacyPacketYaml()
    {
        return """
        execution_unit: G782
        implementation_issue:
          issue_title: "[G782] Repair packet links"
          goal: "Use the recorded repair packet."
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "bug repair links"
          dependencies: []
          technical_baseline: []
          project_local_guide: []
          intent_baseline: []
          intent_references: []
          rules_and_specs: []
          in_scope: []
          out_of_scope: []
          acceptance_criteria: []
          verification_evidence: []
        review:
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateG337PacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G782] Repair packet links"
          issue_kind: "bugfix"
          source_execution_unit: "G782"
          goal: "Use the recorded repair packet."
          in_scope: []
          out_of_scope: []
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "bug repair links"
          dependencies: []
          technical_baseline: []
          project_local_guide: []
          intent_baseline: []
          intent_references: []
          rules_and_specs: []
          acceptance_criteria: []
          verification_evidence: []
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G782"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references: []
          rules_and_specs: []
          acceptance_criteria: []
          deterministic_review_checks: []
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string ArtifactPath(string repoRoot, string bugId)
    {
        return Path.Combine(repoRoot, ".intent-cli", "bugs", $"{bugId}.implementation-repair.yaml");
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
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private sealed class CapturingPublisher : IQueueDispatchPublisher
    {
        public string? Body { get; private set; }

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            Body = body;
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 1706,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/1706"
            };
        }
    }

    private sealed class ThrowingPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            throw new InvalidOperationException("CreateIssue should not be reached for a recorded G337 packet.");
        }
    }

    private sealed class FakeGitCommandRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-g782-bug-repair-tests-").FullName;

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
