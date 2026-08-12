using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class OperatorMergeG678Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);
    private readonly string root = Directory.CreateTempSubdirectory("operator-merge-g678-").FullName;
    private DateTimeOffset now = FixedNow;

    public OperatorMergeG678Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => now;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApprovedGreenOperatorLaneIsPatientVisibleAndDirectLaneRemainsActionable_G678()
    {
        var context = CreateContext();
        WritePacket(BranchLaneLandingModes.OperatorMerge);
        WriteQueue(QueueItemState.Review, BranchLaneLandingModes.OperatorMerge);
        var issue = Issue();
        var operatorPr = OpenApprovedPr();
        AutomationStalledWorkCommand.CandidateListerFactory = () =>
            new FakeLister([issue], [operatorPr]);

        var patient = AutomationStalledWorkCommand.Analyze(
            context, Domain, Repo, staleMinutes: 10_000);

        Assert.False(patient.Stalled);
        var waiting = Assert.Single(patient.Items);
        Assert.Equal(AutomationStalledWorkCommand.KindAwaitingOperatorMerge, waiting.Kind);
        Assert.True(waiting.IsInformational);
        Assert.Equal(0, waiting.AgeMinutes);
        Assert.Equal("main-hotfix", waiting.LaneId);
        Assert.Equal(BranchLaneLandingModes.OperatorMerge, waiting.LandingMode);
        Assert.Equal(1468, waiting.Pr!.Number);
        Assert.Equal("all-green", waiting.CiOutcome);
        Assert.Contains("intent-pr-approved", waiting.ApprovalEvidence!);
        Assert.False(waiting.OrchestratorActionable);
        Assert.DoesNotContain("merge PR", waiting.RecommendedAction, StringComparison.OrdinalIgnoreCase);

        WritePacket(BranchLaneLandingModes.Direct);
        WriteQueue(QueueItemState.Review, BranchLaneLandingModes.Direct);
        var direct = AutomationStalledWorkCommand.Analyze(
            context, Domain, Repo, staleMinutes: 0);
        var directFinding = Assert.Single(direct.Items, item =>
            item.Kind == AutomationStalledWorkCommand.KindApprovedNotMerged);
        Assert.False(directFinding.IsInformational);
        Assert.Contains("merge PR #1468", directFinding.RecommendedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void HumanMergeImmediatelyResumesCloseoutWithoutAnyMergeRecommendation_G678()
    {
        var context = CreateContext();
        WritePacket(BranchLaneLandingModes.OperatorMerge);
        WriteQueue(QueueItemState.Review, BranchLaneLandingModes.OperatorMerge);
        AutomationStalledWorkCommand.CandidateListerFactory = () =>
            new FakeLister([], [], [MergedPr()]);

        var result = AutomationStalledWorkCommand.Analyze(
            context, Domain, Repo, staleMinutes: 10_000);

        var detected = Assert.Single(result.Items);
        Assert.Equal(AutomationStalledWorkCommand.KindOperatorMergeDetected, detected.Kind);
        Assert.Equal(0, detected.AgeMinutes);
        Assert.Equal(BranchLaneLandingModes.OperatorMerge, detected.LandingMode);
        Assert.Contains("closeout pr", detected.RecommendedAction, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr merge", detected.RecommendedAction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("merge PR", detected.RecommendedAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostNextActionNeverOffersMergeForOperatorLaneAndDirectBehaviorIsPreserved_G678()
    {
        var operatorInput = new HostLoopNextActionInput
        {
            Repo = Repo,
            Domain = Domain,
            OpenIntentTargetPrOrIssueExists = true,
            ApprovedPrPendingMergeCloseout = new ApprovedPrContinuation
            {
                Number = 1468,
                Url = "https://github.com/J-Tech-Japan/intent-system/pull/1468",
                LaneId = "main-hotfix",
                LandingMode = BranchLaneLandingModes.OperatorMerge,
                ChecksGreen = true,
            },
        };

        var waiting = HostLoopNextActionAnalyzer.Analyze(operatorInput);
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationAwaitingOperatorMerge, waiting.Classification);
        Assert.False(waiting.MutationAllowed);
        Assert.Null(waiting.RecommendedCommand);
        Assert.DoesNotContain("merge via", string.Join("\n", waiting.Evidence), StringComparison.OrdinalIgnoreCase);

        var direct = HostLoopNextActionAnalyzer.Analyze(operatorInput with
        {
            ApprovedPrPendingMergeCloseout = operatorInput.ApprovedPrPendingMergeCloseout! with
            {
                LandingMode = BranchLaneLandingModes.Direct,
            },
        });
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationApprovedPrMergeCloseout, direct.Classification);
        Assert.True(direct.MutationAllowed);
        Assert.Contains("merge + closeout", direct.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidesNamePatientStateHumanAuthorityAndReviewNotDebt_G678()
    {
        var continuation = HostLoopContinuationContract.Default;
        var patient = Assert.Single(continuation.StopClassifications, stop =>
            stop.StopState == HostLoopContinuationContract.StopAwaitingOperatorMerge);
        Assert.True(patient.Terminal);
        Assert.Contains("not review debt", patient.Meaning, StringComparison.Ordinal);
        Assert.Contains("human merge", patient.NextCommand, StringComparison.Ordinal);

        var context = CreateContext();
        using var writer = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            context,
            ["--domain", Domain, "--target-repo", Repo, "--agent", "codex", "--format", "markdown"],
            writer));
        var markdown = writer.ToString();
        Assert.Contains("awaiting-operator-merge", markdown, StringComparison.Ordinal);
        Assert.Contains("not review debt", markdown, StringComparison.Ordinal);
        Assert.Contains("No intent-cli path may merge", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnglishJapaneseDocsAndPreviewLedgerStayInParity_G678()
    {
        var repoRoot = FindRepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var packets = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "04-packets-issues.md"));
            var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("landing_mode = \"operator-merge\"", packets, StringComparison.Ordinal);
            Assert.Contains("awaiting-operator-merge", packets, StringComparison.Ordinal);
            Assert.Contains("G678", packets, StringComparison.Ordinal);
            Assert.Contains("operator-merge", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }
    }

    private CliContext CreateContext()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        return new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = Domain,
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                },
            },
        };
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IntentSystem.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private void WritePacket(string landingMode)
    {
        var path = Path.Combine(root, ".intent-cli", "issues", "G678", "packet.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            implementation_issue_packet:
              source_execution_unit: G678
              domain: intent-cli
              branch_lane: main-hotfix
              routing_snapshot:
                lane_id: main-hotfix
                definition_revision: registry-g678
                start_branch: main
                pr_base_branch: main
                landing_mode: {{landingMode}}
            """);
    }

    private void WriteQueue(QueueItemState state, string landingMode)
    {
        var queue = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = now,
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G678",
                    Title = "G678 operator merge",
                    State = state,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = ".intent-cli/issues/G678/packet.yaml",
                        Implementation = ".intent-cli/issues/G678/implementation.md",
                        ReviewContext = ".intent-cli/issues/G678/review-context.md",
                    },
                    RoutingSnapshot = new QueueRoutingSnapshot
                    {
                        LaneId = "main-hotfix",
                        DefinitionRevision = "registry-g678",
                        StartBranch = "main",
                        PrBaseBranch = "main",
                        LandingMode = landingMode,
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = Repo,
                        Number = 1467,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/1467",
                    },
                    LinkedPr = "1468",
                    WorkerRole = "implementation",
                    ReviewRole = "review",
                    Priority = "normal",
                },
            ],
        };
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), QueueStateSerializer.Serialize(queue));
    }

    private static GitHubAutomationIssueCandidate Issue() => new()
    {
        Number = 1467,
        Title = "G678: operator merge",
        Url = "https://github.com/J-Tech-Japan/intent-system/issues/1467",
        State = "OPEN",
        Body = "Lane: `main-hotfix`\nLanding mode: `operator-merge`\n",
        Labels = [new GitHubAutomationLabel { Name = "intent-target" }],
    };

    private static GitHubAutomationPrCandidate OpenApprovedPr() => new()
    {
        Number = 1468,
        Title = "G678 operator merge",
        Url = "https://github.com/J-Tech-Japan/intent-system/pull/1468",
        State = "OPEN",
        UpdatedAt = FixedNow.AddHours(-2).ToString("O"),
        HeadRefOid = "g678-head",
        Labels =
        [
            new GitHubAutomationLabel { Name = "intent-target" },
            new GitHubAutomationLabel { Name = "intent-pr-approved" },
        ],
        StatusCheckRollup =
        [
            new GitHubAutomationStatusCheckCandidate
            {
                TypeName = "CheckRun",
                Status = "COMPLETED",
                Conclusion = "SUCCESS",
            },
        ],
        ClosingIssuesReferences = [ClosingReference()],
    };

    private static GitHubAutomationPrCandidate MergedPr() => OpenApprovedPr() with
    {
        State = "MERGED",
        UpdatedAt = FixedNow.ToString("O"),
    };

    private static GitHubPrClosingIssueReference ClosingReference() => new()
    {
        Number = 1467,
        Repository = new GitHubPrClosingIssueRepository
        {
            Name = "intent-system",
            Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
        },
    };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> merged;

        public FakeLister(
            IReadOnlyList<GitHubAutomationIssueCandidate> issues,
            IReadOnlyList<GitHubAutomationPrCandidate> prs,
            IReadOnlyList<GitHubAutomationPrCandidate>? merged = null)
        {
            this.issues = issues;
            this.prs = prs;
            this.merged = merged ?? [];
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => issues;

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => merged;
    }

}
