using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class BranchLaneDecisionG669Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    public BranchLaneDecisionG669Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
        BranchLaneDecisionCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        BranchLaneDecisionCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
    }

    [Fact]
    public void ProposeOnlyFailsPublishGateAndWritesCompleteIndependentRecord()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "automation", "branch-lane-propose-record",
                "--execution-unit", "G669",
                "--actor", "design",
                "--rationale", "continuous lane keeps the queue on develop",
                "--evidence", "packet snapshot and registry revision",
                "--recorded-at", FixedNow.AddHours(-2).ToString("O"),
                "--write",
                "--format", "json",
            ],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        using var result = JsonDocument.Parse(writer.ToString());
        var record = result.RootElement.GetProperty("record");
        Assert.Equal("branch-lane-propose", record.GetProperty("record_kind").GetString());
        Assert.Equal("design", record.GetProperty("actor").GetString());
        Assert.Equal("design", record.GetProperty("actor_role").GetString());
        Assert.Equal("continuous", record.GetProperty("lane_id").GetString());
        Assert.Equal("develop", record.GetProperty("pr_base_branch").GetString());
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("recorded_at").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("evidence").GetString()));
        Assert.StartsWith("sha256:", record.GetProperty("fingerprint").GetString(), StringComparison.Ordinal);

        var gate = BranchLaneDecisionGate.Evaluate(workspace.RootPath, "G669");
        Assert.False(gate.Passed);
        Assert.Contains("confirm", gate.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(
            workspace.RootPath,
            ".intent-cli",
            "branch-lane-decisions",
            "G669",
            "propose.json")));
    }

    [Fact]
    public void PublishFlowRefusesBeforeGitHubWhenConfirmIsMissing()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();
        workspace.WriteGithubBody();
        Assert.Equal(0, RunRecordCommand(workspace, confirmation: false, "design"));

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G669", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.Contains("confirm", result.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(result.RootElement.GetProperty("created").GetBoolean());
    }

    [Fact]
    public void ConfirmWithoutProposalRefusesAndNamesBothRecords()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "automation", "branch-lane-confirm-record",
                "--execution-unit", "G669",
                "--actor", "orchestration",
                "--evidence", "independent packet verification",
                "--write",
            ],
            workspace.Context,
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("propose", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirm", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(
            workspace.RootPath,
            ".intent-cli",
            "branch-lane-decisions",
            "G669",
            "confirm.json")));
    }

    [Fact]
    public void SeparateProposalAndConfirmationPassTheGate()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();
        Assert.Equal(0, RunRecordCommand(workspace, confirmation: false, "design"));
        Assert.Equal(0, RunRecordCommand(
            workspace,
            confirmation: true,
            actor: "orchestration",
            recordedAt: FixedNow.AddMinutes(1)));

        var gate = BranchLaneDecisionGate.Evaluate(workspace.RootPath, "G669");
        Assert.True(gate.Passed, gate.Error);
        Assert.False(gate.Legacy);

        var propose = BranchLaneDecisionStore.ReadPropose(workspace.RootPath, "G669").Record;
        var confirm = BranchLaneDecisionStore.ReadConfirm(workspace.RootPath, "G669").Record;
        Assert.NotNull(propose);
        Assert.NotNull(confirm);
        Assert.NotEqual(propose!.Actor, confirm!.Actor);
        Assert.Equal(propose.Fingerprint, confirm.Fingerprint);
        Assert.NotEqual(propose.RecordedAt, confirm.RecordedAt);
    }

    [Fact]
    public void ConflictingRewriteAndWrongRoleFailClosed()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();
        Assert.Equal(0, RunRecordCommand(workspace, confirmation: false, "design"));

        using var rewriteWriter = new StringWriter();
        var rewriteExit = CommandRouter.Execute(
            [
                "automation", "branch-lane-propose-record",
                "--execution-unit", "G669",
                "--actor", "another-design-actor",
                "--rationale", "the packet lane is suitable for the requested flow",
                "--evidence", "packet snapshot review",
                "--write",
            ],
            workspace.Context,
            rewriteWriter);
        Assert.Equal(1, rewriteExit);
        Assert.Contains("conflicting propose", rewriteWriter.ToString(), StringComparison.OrdinalIgnoreCase);

        var proposePath = BranchLaneDecisionStore.ResolveFullPath(workspace.RootPath, "G669", confirmation: false);
        File.WriteAllText(
            proposePath,
            File.ReadAllText(proposePath).Replace(
                "\"actor_role\": \"design\"",
                "\"actor_role\": \"orchestration\"",
                StringComparison.Ordinal));
        Assert.Equal(1, RunRecordCommand(workspace, confirmation: true, "orchestration"));

        var gate = BranchLaneDecisionGate.Evaluate(workspace.RootPath, "G669");
        Assert.False(gate.Passed);
        Assert.Contains("confirm missing", gate.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MismatchedPacketSnapshotFailsClosedAndLegacyPacketStaysUngated()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();
        Assert.Equal(0, RunRecordCommand(workspace, confirmation: false, "design"));
        Assert.Equal(0, RunRecordCommand(workspace, confirmation: true, "orchestration"));

        workspace.WriteLanePacket(prBaseBranch: "main");
        var mismatch = BranchLaneDecisionGate.Evaluate(workspace.RootPath, "G669");
        Assert.False(mismatch.Passed);
        Assert.Contains("fingerprint", mismatch.Error, StringComparison.OrdinalIgnoreCase);

        workspace.WriteLegacyPacket();
        var legacy = BranchLaneDecisionGate.Evaluate(workspace.RootPath, "G669");
        Assert.True(legacy.Passed);
        Assert.True(legacy.Legacy);

        workspace.WriteQueue(QueueItemState.Queued);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new LaneFakeLister();
        var stalled = AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 0);
        Assert.DoesNotContain(stalled.Items, item =>
            item.Kind is AutomationStalledWorkCommand.KindBranchLaneDecisionPending
                or AutomationStalledWorkCommand.KindBranchRoutingConflict);
    }

    [Fact]
    public void StalledWorkAgeGatesPendingAndClearsAfterConfirmation()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();
        workspace.WriteQueue(QueueItemState.Queued);
        Assert.Equal(0, RunRecordCommand(
            workspace,
            confirmation: false,
            actor: "design",
            recordedAt: FixedNow.AddHours(-20)));

        AutomationStalledWorkCommand.CandidateListerFactory = () => new LaneFakeLister();
        var pending = AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 60);
        var pendingItem = Assert.Single(pending.Items, item =>
            item.Kind == AutomationStalledWorkCommand.KindBranchLaneDecisionPending);
        Assert.Equal("G669", pendingItem.ExecutionUnit);
        Assert.True(pendingItem.AgeMinutes >= 19 * 60);
        Assert.False(pendingItem.IsInformational);

        Assert.Equal(0, RunRecordCommand(workspace, confirmation: true, actor: "orchestration"));
        var cleared = AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 60);
        Assert.DoesNotContain(cleared.Items, item =>
            item.Kind == AutomationStalledWorkCommand.KindBranchLaneDecisionPending);
    }

    [Fact]
    public void RoutingConflictIsImmediateAndIncludesClosedPrValues()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();
        workspace.WriteQueue(QueueItemState.Review);

        var issue = new GitHubAutomationIssueCandidate
        {
            Number = 1447,
            Title = "G669 lane decision records",
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/1447",
            Body = """
                Lane: `continuous`
                Registry definition revision: `registry-r1`
                Start branch: `develop`
                Landing mode: `direct`
                Expected PR base branch: `main`
                """,
            State = "OPEN",
        };
        var closedPr = new GitHubAutomationPrCandidate
        {
            Number = 2047,
            Title = "G669 lane decision records",
            Url = "https://github.com/J-Tech-Japan/intent-system/pull/2047",
            State = "CLOSED",
            BaseRefName = "main",
            ClosingIssuesReferences =
            [
                new GitHubPrClosingIssueReference
                {
                    Number = 1447,
                    Repository = new GitHubPrClosingIssueRepository
                    {
                        Name = "intent-system",
                        Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                    },
                },
            ],
        };
        AutomationStalledWorkCommand.CandidateListerFactory = () =>
            new LaneFakeLister([issue], closedPrs: [closedPr]);

        var result = AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 9999);
        var conflict = Assert.Single(result.Items, item =>
            item.Kind == AutomationStalledWorkCommand.KindBranchRoutingConflict);
        Assert.Equal(0, conflict.AgeMinutes);
        Assert.Equal(2047, conflict.Pr!.Number);
        Assert.Contains("main", conflict.RecommendedAction, StringComparison.Ordinal);
        Assert.Equal("develop", conflict.RoutingValues!["packet.pr_base_branch"]);
        Assert.Equal("develop", conflict.RoutingValues["queue.pr_base_branch"]);
        Assert.Equal("main", conflict.RoutingValues["issue.pr_base_branch"]);
        Assert.Equal("main", conflict.RoutingValues["pr.pr_base_branch"]);
        Assert.Equal("continuous", conflict.RoutingValues["packet.lane_id"]);
        Assert.Equal("continuous", conflict.RoutingValues["issue.lane_id"]);
        Assert.Equal("registry-r1", conflict.RoutingValues["queue.definition_revision"]);
        Assert.Equal("direct", conflict.RoutingValues["issue.landing_mode"]);
    }

    [Fact]
    public void RoutingConflictComparesSnapshotFieldsByMeaning()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();
        workspace.WriteQueue(QueueItemState.Review, startBranch: "release/g669");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new LaneFakeLister();

        var result = AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 9999);
        var conflict = Assert.Single(result.Items, item =>
            item.Kind == AutomationStalledWorkCommand.KindBranchRoutingConflict);
        Assert.Equal("develop", conflict.RoutingValues!["packet.start_branch"]);
        Assert.Equal("release/g669", conflict.RoutingValues["queue.start_branch"]);
        Assert.Equal("develop", conflict.RoutingValues["packet.pr_base_branch"]);
        Assert.Equal("develop", conflict.RoutingValues["queue.pr_base_branch"]);
    }

    [Fact]
    public void AuthoringOnlyLaneUsesDistinctOperatorConfirmation_AndRefusesOrchestrationImpersonation()
    {
        using var workspace = new LaneWorkspace();
        workspace.WriteLanePacket();

        using (var modeWriter = new StringWriter())
        {
            Assert.Equal(0, TeamModeCommand.ExecuteSet(
                workspace.Context,
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--mode", TeamMode.AuthoringOnly, "--write", "--format", "json"],
                modeWriter));
        }

        using var proposalWriter = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            [
                "automation", "branch-lane-propose-record",
                "--execution-unit", "G669", "--actor", "design",
                "--rationale", "authoring lane proposal",
                "--evidence", "packet snapshot", "--domain", "intent-cli",
                "--write", "--format", "json",
            ],
            workspace.Context,
            proposalWriter));

        using var refusedWriter = new StringWriter();
        Assert.Equal(1, CommandRouter.Execute(
            [
                "automation", "branch-lane-confirm-record",
                "--execution-unit", "G669", "--actor", "operator", "--actor-role", "orchestration",
                "--evidence", "operator confirmation", "--domain", "intent-cli",
                "--write", "--format", "json",
            ],
            workspace.Context,
            refusedWriter));
        Assert.Contains("orchestration", refusedWriter.ToString(), StringComparison.OrdinalIgnoreCase);

        using var forgedActorWriter = new StringWriter();
        Assert.Equal(1, CommandRouter.Execute(
            [
                "automation", "branch-lane-confirm-record",
                "--execution-unit", "G669", "--actor", "orchestration", "--actor-role", "operator",
                "--evidence", "operator confirmation", "--domain", "intent-cli",
                "--write", "--format", "json",
            ],
            workspace.Context,
            forgedActorWriter));
        Assert.Contains("distinct operator identity", forgedActorWriter.ToString(), StringComparison.OrdinalIgnoreCase);

        using var confirmationWriter = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            [
                "automation", "branch-lane-confirm-record",
                "--execution-unit", "G669", "--actor", "operator", "--actor-role", "operator",
                "--evidence", "operator confirmation", "--domain", "intent-cli",
                "--write", "--format", "json",
            ],
            workspace.Context,
            confirmationWriter));

        var gate = BranchLaneDecisionGate.Evaluate(workspace.RootPath, "G669", TeamMode.AuthoringOnly);
        Assert.True(gate.Passed, gate.Error);
        using var confirmation = JsonDocument.Parse(confirmationWriter.ToString());
        Assert.Equal(TeamMode.AuthoringOnly, confirmation.RootElement.GetProperty("record").GetProperty("team_mode").GetString());
        Assert.Equal("operator", confirmation.RootElement.GetProperty("record").GetProperty("actor_role").GetString());
    }

    private static int RunRecordCommand(
        LaneWorkspace workspace,
        bool confirmation,
        string actor,
        DateTimeOffset? recordedAt = null)
    {
        using var writer = new StringWriter();
        var command = confirmation
            ? "branch-lane-confirm-record"
            : "branch-lane-propose-record";
        var args = new List<string>
        {
            "automation", command,
            "--execution-unit", "G669",
            "--actor", actor,
            "--evidence", confirmation ? "queue and branch verification" : "packet snapshot review",
            "--write",
            "--format", "json",
        };
        if (!confirmation)
        {
            args.Add("--rationale");
            args.Add("the packet lane is suitable for the requested flow");
        }
        if (recordedAt is not null)
        {
            args.Add("--recorded-at");
            args.Add(recordedAt.Value.ToString("O"));
        }

        return CommandRouter.Execute(args.ToArray(), workspace.Context, writer);
    }

    private sealed class LaneFakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> closedPrs;

        public LaneFakeLister(
            IReadOnlyList<GitHubAutomationIssueCandidate>? issues = null,
            IReadOnlyList<GitHubAutomationPrCandidate>? closedPrs = null)
        {
            this.issues = issues ?? Array.Empty<GitHubAutomationIssueCandidate>();
            this.closedPrs = closedPrs ?? Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => issues;

        public IReadOnlyList<GitHubAutomationPrCandidate> ListClosedPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => closedPrs;
    }

    private sealed class LaneWorkspace : IDisposable
    {
        public LaneWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("lane-decision-g669-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void WriteLanePacket(string prBaseBranch = "develop")
        {
            var packet = $$"""
                implementation_issue_packet:
                  source_execution_unit: "G669"
                  domain: "intent-cli"
                  branch_lane: "continuous"
                  routing_snapshot:
                    lane_id: "continuous"
                    definition_revision: "registry-r1"
                    start_branch: "develop"
                    pr_base_branch: "{{prBaseBranch}}"
                    landing_mode: "direct"
                """;
            WriteFile(".intent-cli/issues/G669/packet.yaml", packet);
        }

        public void WriteLegacyPacket()
        {
            WriteFile(
                ".intent-cli/issues/G669/packet.yaml",
                "implementation_issue_packet:\n  source_execution_unit: G669\n  domain: intent-cli\n");
        }

        public void WriteGithubBody()
        {
            WriteFile(".intent-cli/issues/G669/github-body.md", """
                # G669 lane decision records

                ## Goal
                Record the lane decision.

                ## Why This Slice Exists Now
                The lane must be independently confirmed.

                ## Current Observed State
                The packet declares a lane.

                ## Accepted Baseline You May Assume
                G668 routing snapshot exists.

                ## Target Repo / Path / Part
                intent-system / src / CLI.

                ## In Scope
                - record and verify lane decisions.

                ## Out Of Scope
                - merge and closeout.

                ## Acceptance Criteria
                - missing confirmation refuses publish.

                ## Verification
                - focused tests.

                ## Related Links
                - local contract.

                ## Base Branch Policy
                Policy: named-lane
                Expected PR base branch: develop
                """);
        }

        public void WriteQueue(QueueItemState state, string startBranch = "develop")
        {
            var item = new QueueItem
            {
                ExecutionUnit = "G669",
                Title = "G669 lane decision records",
                State = state,
                Dependencies = Array.Empty<string>(),
                BlockedBy = Array.Empty<string>(),
                ClarificationReturnPath = string.Empty,
                PacketPaths = new PacketPaths
                {
                    Yaml = ".intent-cli/issues/G669/packet.yaml",
                    Implementation = ".intent-cli/issues/G669/implementation.md",
                    ReviewContext = ".intent-cli/issues/G669/review-context.md",
                },
                RoutingSnapshot = new QueueRoutingSnapshot
                {
                    LaneId = "continuous",
                    DefinitionRevision = "registry-r1",
                    StartBranch = startBranch,
                    PrBaseBranch = "develop",
                    LandingMode = "direct",
                },
                LinkedIssue = new LinkedIssue
                {
                    Repo = "J-Tech-Japan/intent-system",
                    Number = 1447,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/1447",
                },
                LinkedPr = "2047",
                WorkerRole = "coder",
                ReviewRole = "reviewer",
                Priority = "normal",
            };
            var stateJson = new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = FixedNow.AddHours(-20),
                Items = [item],
            };
            File.WriteAllText(Context.GetQueueStatePath(), QueueStateSerializer.Serialize(stateJson));
        }

        private void WriteFile(string relativePath, string content)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for a test-only temporary workspace.
            }
        }
    }
}
