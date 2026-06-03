using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G448: coverage for the unified host-metadata state doctor — the pure
/// analyzer's four required drift categories plus the command's read-only
/// vs fail-closed <c>--write</c> behavior and host-only prohibition.
/// </summary>
public sealed class AutomationStateDoctorTests : IDisposable
{
    private const string Repo = "J-Tech-Japan/intent-system";

    public AutomationStateDoctorTests()
    {
        AutomationStateDoctorCommand.CandidateListerFactory = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    public void Dispose()
    {
        AutomationStateDoctorCommand.CandidateListerFactory = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    // ---- Analyzer: required AC drift categories ------------------------------

    [Fact]
    public void Analyze_MissingLinkedPr_FromUniqueClosingPr_IsHighConfidence()
    {
        var queue = new[]
        {
            new StateDoctorQueueItem
            {
                ExecutionUnit = "G300",
                LinkedIssueRepo = Repo,
                LinkedIssueNumber = 703,
                LinkedPrUrl = null,
                Completed = false,
            },
        };
        var prs = new[] { Pr(706, merged: false, closes: 703) };

        var analysis = AutomationStateDoctorAnalyzer.Analyze(Repo, queue, Array.Empty<StateDoctorPublishEvidence>(), prs);

        var finding = Assert.Single(analysis.Findings, f => f.Category == AutomationStateDoctorCategories.MissingLinkedPr);
        Assert.Equal(AutomationStateDoctorConfidence.High, finding.Confidence);
        Assert.Equal(AutomationStateDoctorRepairKinds.SetQueueLinkedPr, finding.RepairKind);
        Assert.Equal(706, finding.PrNumber);
        Assert.Empty(analysis.UnsafeFindings);
    }

    [Fact]
    public void Analyze_MissingLinkedIssue_FromPublishArtifact_IsHighConfidence()
    {
        var queue = new[]
        {
            new StateDoctorQueueItem
            {
                ExecutionUnit = "G300",
                LinkedIssueRepo = null,
                LinkedIssueNumber = null,
                LinkedPrUrl = null,
                Completed = false,
            },
        };
        var publish = new[]
        {
            new StateDoctorPublishEvidence
            {
                ExecutionUnit = "G300",
                IssueRepo = Repo,
                IssueNumber = 703,
                IssueUrl = $"https://github.com/{Repo}/issues/703",
            },
        };

        var analysis = AutomationStateDoctorAnalyzer.Analyze(Repo, queue, publish, Array.Empty<StateDoctorPr>());

        var finding = Assert.Single(analysis.Findings, f => f.Category == AutomationStateDoctorCategories.MissingLinkedIssue);
        Assert.Equal(AutomationStateDoctorConfidence.High, finding.Confidence);
        Assert.Equal(AutomationStateDoctorRepairKinds.SetQueueLinkedIssue, finding.RepairKind);
        Assert.Equal(703, finding.IssueNumber);
        Assert.Empty(analysis.UnsafeFindings);
    }

    [Fact]
    public void Analyze_MergedPrNotCompleted_IsHighConfidence()
    {
        var queue = new[]
        {
            new StateDoctorQueueItem
            {
                ExecutionUnit = "G300",
                LinkedIssueRepo = Repo,
                LinkedIssueNumber = 703,
                LinkedPrUrl = $"https://github.com/{Repo}/pull/706",
                Completed = false,
            },
        };
        var prs = new[] { Pr(706, merged: true, closes: 703) };

        var analysis = AutomationStateDoctorAnalyzer.Analyze(Repo, queue, Array.Empty<StateDoctorPublishEvidence>(), prs);

        var finding = Assert.Single(analysis.Findings, f => f.Category == AutomationStateDoctorCategories.MergedPrNotCompleted);
        Assert.Equal(AutomationStateDoctorConfidence.High, finding.Confidence);
        Assert.Equal(AutomationStateDoctorRepairKinds.MarkQueueCompleted, finding.RepairKind);
        Assert.Equal(706, finding.PrNumber);
    }

    [Fact]
    public void Analyze_DuplicateIssueEvidence_IsUnsafe_NoRepair()
    {
        var queue = new[]
        {
            new StateDoctorQueueItem { ExecutionUnit = "G300", LinkedIssueRepo = Repo, LinkedIssueNumber = 703, LinkedPrUrl = null, Completed = false },
            new StateDoctorQueueItem { ExecutionUnit = "G301", LinkedIssueRepo = Repo, LinkedIssueNumber = 703, LinkedPrUrl = null, Completed = false },
        };
        var prs = new[] { Pr(706, merged: false, closes: 703) };

        var analysis = AutomationStateDoctorAnalyzer.Analyze(Repo, queue, Array.Empty<StateDoctorPublishEvidence>(), prs);

        var unsafeEntry = Assert.Single(analysis.UnsafeFindings);
        Assert.Equal(AutomationStateDoctorUnsafeKinds.DuplicateIssueEvidence, unsafeEntry.Kind);
        // Fail-closed: the ambiguous issue must NOT also produce a linked_pr repair.
        Assert.DoesNotContain(analysis.Findings, f => f.IssueNumber == 703);
    }

    [Fact]
    public void Analyze_MultiplePrsCloseSameIssue_IsAmbiguous_NoLinkedPrRepair()
    {
        var queue = new[]
        {
            new StateDoctorQueueItem { ExecutionUnit = "G300", LinkedIssueRepo = Repo, LinkedIssueNumber = 703, LinkedPrUrl = null, Completed = false },
        };
        var prs = new[] { Pr(706, merged: false, closes: 703), Pr(707, merged: false, closes: 703) };

        var analysis = AutomationStateDoctorAnalyzer.Analyze(Repo, queue, Array.Empty<StateDoctorPublishEvidence>(), prs);

        Assert.DoesNotContain(analysis.Findings, f => f.Category == AutomationStateDoctorCategories.MissingLinkedPr);
        Assert.Contains(analysis.UnsafeFindings, u => u.Kind == AutomationStateDoctorUnsafeKinds.AmbiguousPrLinkage);
    }

    [Fact]
    public void Analyze_FullyLinkedCompletedItem_ProducesNoFindings()
    {
        var queue = new[]
        {
            new StateDoctorQueueItem
            {
                ExecutionUnit = "G300",
                LinkedIssueRepo = Repo,
                LinkedIssueNumber = 703,
                LinkedPrUrl = $"https://github.com/{Repo}/pull/706",
                Completed = true,
            },
        };
        var prs = new[] { Pr(706, merged: true, closes: 703) };

        var analysis = AutomationStateDoctorAnalyzer.Analyze(Repo, queue, Array.Empty<StateDoctorPublishEvidence>(), prs);

        Assert.Empty(analysis.Findings);
        Assert.Empty(analysis.UnsafeFindings);
    }

    // ---- Command: read-only vs fail-closed write ----------------------------

    [Fact]
    public void Execute_ReadOnly_ReportsDrift_ButDoesNotMutateQueueState()
    {
        using var workspace = new DoctorWorkspace();
        workspace.WriteQueueState(BuildQueue(("G300", null, null, QueueItemState.Queued)));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);
        AutomationStateDoctorCommand.CandidateListerFactory = () => new FakeLister(
            open: new[] { Pr(706, merged: false, closes: 703) });

        using var writer = new StringWriter();
        var exit = AutomationStateDoctorCommand.Execute(workspace.Context, ["--repo", Repo, "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("read-only", doc.RootElement.GetProperty("mode").GetString());
        Assert.True(doc.RootElement.GetProperty("findings").GetArrayLength() >= 1);
        Assert.All(
            doc.RootElement.GetProperty("findings").EnumerateArray(),
            f => Assert.False(f.GetProperty("applied").GetBoolean()));

        var after = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Null(after.Items[0].LinkedIssue);
        Assert.Null(after.Items[0].LinkedPr);
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_Write_AppliesHighConfidenceForwardOnlyRepair_AndAppendsRunEvent()
    {
        using var workspace = new DoctorWorkspace();
        workspace.WriteQueueState(BuildQueue(("G300", null, null, QueueItemState.Queued)));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);
        AutomationStateDoctorCommand.CandidateListerFactory = () => new FakeLister(
            open: new[] { Pr(706, merged: false, closes: 703) });

        using var writer = new StringWriter();
        var exit = AutomationStateDoctorCommand.Execute(workspace.Context, ["--repo", Repo, "--write", "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("write", doc.RootElement.GetProperty("mode").GetString());

        var after = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var item = after.Items[0];
        Assert.NotNull(item.LinkedIssue);
        Assert.Equal(703, item.LinkedIssue!.Number);
        Assert.Contains("/pull/706", item.LinkedPr!, StringComparison.Ordinal);

        var runs = File.ReadAllText(workspace.Context.GetRunLogPath());
        Assert.Contains("state-doctor-repair", runs, StringComparison.Ordinal);
        Assert.Contains("G300", runs, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Write_AmbiguousDrift_FailsClosed_NoMutation()
    {
        using var workspace = new DoctorWorkspace();
        workspace.WriteQueueState(BuildQueue(
            ("G300", Li(703), null, QueueItemState.Queued),
            ("G301", Li(703), null, QueueItemState.Queued)));
        AutomationStateDoctorCommand.CandidateListerFactory = () => new FakeLister(
            open: new[] { Pr(706, merged: false, closes: 703) });

        using var writer = new StringWriter();
        var exit = AutomationStateDoctorCommand.Execute(workspace.Context, ["--repo", Repo, "--write", "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("unsafe_findings").GetArrayLength() >= 1);

        var after = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.All(after.Items, item => Assert.Null(item.LinkedPr));
        // Fail-closed write: no run log appended for an ambiguous-only result.
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_OldHostWithNoQueueState_DoesNotCrash_AndReportsNoDrift()
    {
        using var workspace = new DoctorWorkspace();
        // No queue-state.json written — simulates a brand-new / old host.
        AutomationStateDoctorCommand.CandidateListerFactory = () => new FakeLister(open: Array.Empty<StateDoctorPr>());

        using var writer = new StringWriter();
        var exit = AutomationStateDoctorCommand.Execute(workspace.Context, ["--repo", Repo, "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("findings").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("unsafe_findings").GetArrayLength());
    }

    [Fact]
    public void Execute_ChildLoopContext_IsRejected_HostOnly()
    {
        using var workspace = new DoctorWorkspace();
        using var writer = new StringWriter();
        var exit = AutomationStateDoctorCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--child-loop-context", "--format", "json"],
            writer);

        Assert.Equal(2, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationStateDoctorUnsafeKinds.ChildLoopProhibited,
            doc.RootElement.GetProperty("unsafe_findings")[0].GetProperty("kind").GetString());
    }

    // ---- builders / fakes ---------------------------------------------------

    private static StateDoctorPr Pr(int number, bool merged, params int[] closes) =>
        new()
        {
            Number = number,
            Url = $"https://github.com/{Repo}/pull/{number}",
            Merged = merged,
            ClosingIssueNumbers = closes,
        };

    private static LinkedIssue Li(int number) =>
        new() { Repo = Repo, Number = number, Url = $"https://github.com/{Repo}/issues/{number}" };

    private static string BuildQueue(params (string Unit, LinkedIssue? Issue, string? Pr, QueueItemState State)[] items)
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero),
            Items = items.Select(i => new QueueItem
            {
                ExecutionUnit = i.Unit,
                Title = $"{i.Unit} title",
                State = i.State,
                Dependencies = Array.Empty<string>(),
                BlockedBy = Array.Empty<string>(),
                ClarificationReturnPath = string.Empty,
                PacketPaths = new PacketPaths
                {
                    Yaml = $".intent-cli/issues/{i.Unit}/packet.yaml",
                    Implementation = $".intent-cli/issues/{i.Unit}/implementation.md",
                    ReviewContext = $".intent-cli/issues/{i.Unit}/review-context.md",
                },
                LinkedIssue = i.Issue,
                LinkedPr = i.Pr,
                WorkerRole = "Claude",
                ReviewRole = "Codex",
                Priority = "normal",
            }).ToArray(),
        };
        return QueueStateSerializer.Serialize(state);
    }

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<StateDoctorPr> open;
        private readonly IReadOnlyList<StateDoctorPr> merged;

        public FakeLister(IReadOnlyList<StateDoctorPr> open, IReadOnlyList<StateDoctorPr>? merged = null)
        {
            this.open = open;
            this.merged = merged ?? open.Where(p => p.Merged).ToArray();
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            open.Select(ToCandidate).ToArray();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            merged.Select(ToCandidate).ToArray();

        private static GitHubAutomationPrCandidate ToCandidate(StateDoctorPr pr) => new()
        {
            Number = pr.Number,
            Url = pr.Url,
            State = pr.Merged ? "MERGED" : "OPEN",
            ClosingIssuesReferences = pr.ClosingIssueNumbers
                .Select(n => new GitHubPrClosingIssueReference { Number = n })
                .ToArray(),
        };
    }

    private sealed class DoctorWorkspace : IDisposable
    {
        private readonly string installedCliPath;

        public DoctorWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("state-doctor-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            installedCliPath = Path.Combine(RootPath, ".intent-cli", "installed-cli-stub");
            File.WriteAllText(installedCliPath, "stub");

            // Satisfy the installed-CLI surface probe deterministically without a
            // real subprocess: a resolvable path + a stdout covering every
            // required capability token.
            AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = () => installedCliPath;
            AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, _) =>
                new InstalledCliProbeResult(
                    0,
                    "automation summary host-review-preflight issue-publish pr-transition review-start request-update approved",
                    string.Empty);

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

        public void WriteQueueState(string serialized) =>
            File.WriteAllText(Context.GetQueueStatePath(), serialized);

        public void WritePublishArtifact(string executionUnit, int createdIssueNumber)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = executionUnit,
                PublishStatus = "published",
                PacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{executionUnit}/github-body.md",
                CreatedIssueNumber = createdIssueNumber,
                CreatedIssueUrl = $"https://github.com/{Repo}/issues/{createdIssueNumber}",
                PublishedLabelName = "intent-target",
            };
            File.WriteAllText(Path.Combine(dir, "publish.yaml"), IssuePublishArtifactYaml.Serialize(artifact));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
