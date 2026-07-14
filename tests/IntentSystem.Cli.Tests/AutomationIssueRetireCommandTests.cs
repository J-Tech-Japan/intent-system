using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G525: focused coverage for <c>automation issue-retire</c> — the canonical
/// atomic transition that supersedes a published <c>intent-target</c> issue
/// that can never be started as authored.
/// </summary>
public sealed class AutomationIssueRetireCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 16, 0, 0, TimeSpan.Zero);

    public AutomationIssueRetireCommandTests()
    {
        AutomationIssueRetireCommand.CandidateListerFactory = null;
        AutomationIssueRetireCommand.LabelMutatorFactory = null;
        AutomationIssueRetireCommand.RetirementMutatorFactory = null;
        AutomationIssueRetireCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationIssueRetireCommand.CandidateListerFactory = null;
        AutomationIssueRetireCommand.LabelMutatorFactory = null;
        AutomationIssueRetireCommand.RetirementMutatorFactory = null;
        AutomationIssueRetireCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_Write_ClosesIssueRemovesLabelsRetiresQueueItem_AppendsRunsEvent()
    {
        // G525 field scenario: a published, never-delegated issue has NO
        // pre-existing queue-state entry — the command must derive the
        // execution unit from the title and CREATE the entry.
        using var workspace = new RetireWorkspace();
        var issue = BuildIssue(1744, "SKS-G812: Oversized single-slice contract", ["intent-target"]);
        var lister = new FakeLister(issues: [issue]);
        AutomationIssueRetireCommand.CandidateListerFactory = () => lister;
        var labelMutator = new FakeLabelMutator(new[] { "intent-target" });
        AutomationIssueRetireCommand.LabelMutatorFactory = () => labelMutator;
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1744", "--reason", "decomposed",
                "--note", "oversized; split into successor slices", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Equal("SKS-G812", result.ExecutionUnit);

        // GitHub mutation.
        var closed = Assert.Single(retirementMutator.Closed);
        Assert.Equal("J-Tech-Japan/intent-system", closed.Repo);
        Assert.Equal(1744, closed.IssueNumber);
        Assert.Contains("decomposed", closed.Comment, StringComparison.Ordinal);
        Assert.Contains("oversized; split into successor slices", closed.Comment, StringComparison.Ordinal);
        var transition = Assert.Single(labelMutator.Transitions);
        Assert.Equal("issue", transition.Kind);
        Assert.Equal(1744, transition.Number);
        Assert.Contains("intent-target", transition.RemoveLabels);
        Assert.Empty(transition.AddLabels);

        // Durable state.
        var queueAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var item = Assert.Single(queueAfter.Items);
        Assert.Equal("SKS-G812", item.ExecutionUnit);
        Assert.Equal(QueueItemState.Retired, item.State);
        Assert.Contains("decomposed", item.RetirementReason, StringComparison.Ordinal);
        Assert.Equal(1744, item.LinkedIssue!.Number);

        var runsPath = workspace.Context.GetRunLogPath();
        Assert.True(File.Exists(runsPath));
        var runLine = File.ReadAllText(runsPath).Trim();
        var runEvent = RunLogSerializer.DeserializeLine(runLine);
        Assert.Equal(AutomationIssueRetireCommand.RetireRunEventName, runEvent.Event);
        Assert.Equal("SKS-G812", runEvent.ExecutionUnit);
        Assert.Contains("decomposed", runEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Write_ExistingQueueItem_UpdatesInPlace()
    {
        using var workspace = new RetireWorkspace();
        workspace.WriteQueueState(BuildQueueStateJson("G600", QueueItemState.Queued, linkedIssueNumber: 2001));
        var issue = BuildIssue(2001, "G600: Some slice", ["intent-target"]);
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);
        AutomationIssueRetireCommand.LabelMutatorFactory = () => new FakeLabelMutator(new[] { "intent-target" });
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => new FakeRetirementMutator();

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "2001", "--reason", "superseded", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var queueAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var item = Assert.Single(queueAfter.Items);
        Assert.Equal("G600", item.ExecutionUnit);
        Assert.Equal(QueueItemState.Retired, item.State);
        Assert.Equal("superseded", item.RetirementReason);
    }

    [Fact]
    public void Execute_DryRun_ListsPlannedMutations_DoesNotMutateAnything()
    {
        using var workspace = new RetireWorkspace();
        var issue = BuildIssue(1744, "SKS-G812: Oversized single-slice contract", ["intent-target"]);
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);
        var labelMutator = new FakeLabelMutator(new[] { "intent-target" });
        AutomationIssueRetireCommand.LabelMutatorFactory = () => labelMutator;
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1744", "--reason", "obsolete", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(writer.ToString())!;
        Assert.False(result.Applied);
        Assert.NotEmpty(result.PlannedMutations);

        Assert.Empty(retirementMutator.Closed);
        Assert.Empty(labelMutator.Transitions);
        Assert.False(File.Exists(workspace.Context.GetQueueStatePath()));
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_RefusesWhenOpenLinkedPrExists()
    {
        using var workspace = new RetireWorkspace();
        var issue = BuildIssue(1744, "SKS-G812: Oversized single-slice contract", ["intent-target"]);
        var pr = BuildPr(1900, closingIssueNumber: 1744);
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1744", "--reason", "superseded", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("OPEN PR #1900", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(retirementMutator.Closed);
        Assert.False(File.Exists(workspace.Context.GetQueueStatePath()));
    }

    [Fact]
    public void Execute_RefusesWhenActiveClaimExists()
    {
        using var workspace = new RetireWorkspace();
        var issue = BuildIssue(1744, "SKS-G812: Oversized single-slice contract", ["intent-target", "intent-issue-in-progress"]);
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1744", "--reason", "superseded", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("intent-issue-in-progress", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("declined-contract-incomplete", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(retirementMutator.Closed);
    }

    [Fact]
    public void Execute_Idempotent_AlreadyRetiredQueueItem_IsNoOp()
    {
        using var workspace = new RetireWorkspace();
        var alreadyRetired = BuildQueueStateJson("SKS-G812", QueueItemState.Retired, linkedIssueNumber: 1744, retirementReason: "decomposed");
        workspace.WriteQueueState(alreadyRetired);
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1744", "--reason", "decomposed", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(writer.ToString())!;
        Assert.True(result.AlreadyRetired);
        Assert.False(result.Applied);
        // No GitHub mutation attempted at all — the durable state alone
        // proves idempotency without needing a closed-issue GitHub lookup.
        Assert.Empty(retirementMutator.Closed);
    }

    [Fact]
    public void Execute_IssueNotFoundAmongOpenIssues_FailsClosedWithoutGuessing()
    {
        using var workspace = new RetireWorkspace();
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister();
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "9999", "--reason", "obsolete", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not found among OPEN issues", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(retirementMutator.Closed);
    }

    [Fact]
    public void Execute_RejectsUnrecognizedReason()
    {
        using var workspace = new RetireWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1744", "--reason", "cancelled", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason must be one of", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredIssue_ClearsWipGatingForHostReviewPreflight()
    {
        // G525 AC: retired items clear WIP gating. This exercises the SAME
        // AutomationHostReviewPreflightCommand path G523/host-review-preflight
        // uses, proving the "before retire" (blocked) vs. "after retire"
        // (ready) transition — retiring removes intent-target and closes the
        // issue, so it naturally disappears from the live GitHub scan that
        // WIP gating already relies on (no host-review-preflight code change
        // needed).
        using var beforeWorkspace = new RetireWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new HostPreflightFakeLister
        {
            Issues = [BuildHostPreflightIssue(1744, "wip", "https://github.com/J-Tech-Japan/intent-system/issues/1744",
                "2026-07-14T00:00:00Z", ["intent-target"])],
        };
        using var beforeWriter = new StringWriter();
        AutomationHostReviewPreflightCommand.Execute(
            beforeWorkspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "SKS-G814", "--format", "json"],
            beforeWriter);
        var beforeResult = JsonDocument.Parse(beforeWriter.ToString());
        Assert.Equal("skip-next-slice-due-to-wip", beforeResult.RootElement.GetProperty("action").GetString());

        // After retire: the issue is closed and intent-target removed, so
        // it no longer appears in the live open-issues scan at all.
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new HostPreflightFakeLister();
        using var afterWriter = new StringWriter();
        AutomationHostReviewPreflightCommand.Execute(
            beforeWorkspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "SKS-G814", "--format", "json"],
            afterWriter);
        var afterResult = JsonDocument.Parse(afterWriter.ToString());
        Assert.Equal("candidate-ready", afterResult.RootElement.GetProperty("action").GetString());

        AutomationHostReviewPreflightCommand.CandidateListerFactory = null;
    }

    [Fact]
    public void MetadataValidate_RecognizesRetiredLifecycle_NoQueueEntryMissingAnomaly()
    {
        // G525 AC: metadata validate must not flag a retired unit's
        // queue-state entry as missing/inconsistent. Field incident: a
        // hand-authored noncanonical recovery previously left `metadata
        // validate` unable to recognize the resulting state; the canonical
        // command always creates/updates a queue-state entry on retire so
        // this anomaly cannot recur.
        var queueStateJson = BuildQueueStateJson("SKS-G812", QueueItemState.Retired, linkedIssueNumber: 1744, retirementReason: "decomposed");

        var result = MetadataValidateAnalyzer.Analyze(new MetadataValidateInputs
        {
            ExecutionUnit = "SKS-G812",
            QueueStateJson = queueStateJson,
        });

        Assert.DoesNotContain(result.Errors, finding => finding.Code == MetadataValidateConstants.Codes.QueueEntryMissing);
        Assert.DoesNotContain(result.Errors, finding => finding.Code == MetadataValidateConstants.Codes.CompletedMissingClosure);
    }

    private static GitHubAutomationIssueCandidate BuildIssue(int number, string title, string[] labels) => new()
    {
        Number = number,
        Title = title,
        Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
        CreatedAt = FixedNow.AddDays(-3).ToString("O"),
        State = "OPEN",
        Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
    };

    private static GitHubAutomationPrCandidate BuildPr(int number, int closingIssueNumber) => new()
    {
        Number = number,
        Title = "Some PR",
        Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
        CreatedAt = FixedNow.AddDays(-1).ToString("O"),
        UpdatedAt = FixedNow.AddDays(-1).ToString("O"),
        State = "OPEN",
        ClosingIssuesReferences = new[]
        {
            new GitHubPrClosingIssueReference
            {
                Number = closingIssueNumber,
                Repository = new GitHubPrClosingIssueRepository
                {
                    Name = "intent-system",
                    Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                },
            },
        },
    };

    private static string BuildQueueStateJson(
        string executionUnit, QueueItemState state, int linkedIssueNumber, string? retirementReason = null)
    {
        var queueState = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = FixedNow,
            Items = new[]
            {
                new QueueItem
                {
                    ExecutionUnit = executionUnit,
                    Title = $"{executionUnit} title",
                    State = state,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = linkedIssueNumber,
                        Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{linkedIssueNumber}",
                    },
                    LinkedPr = null,
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = "normal",
                    RetirementReason = retirementReason,
                },
            },
        };
        return QueueStateSerializer.Serialize(queueState);
    }

    private static GitHubAutomationIssueCandidate BuildHostPreflightIssue(
        int number, string state, string url, string createdAt, string[] labels) => new()
    {
        Number = number,
        Title = "wip issue",
        Url = url,
        CreatedAt = createdAt,
        State = "OPEN",
        Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
    };

    private sealed class HostPreflightFakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationIssueCandidate> Issues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();
        public IReadOnlyList<GitHubAutomationPrCandidate> Prs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => Prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => Issues;
    }

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;

        public FakeLister(
            IReadOnlyList<GitHubAutomationIssueCandidate>? issues = null,
            IReadOnlyList<GitHubAutomationPrCandidate>? prs = null)
        {
            this.issues = issues ?? Array.Empty<GitHubAutomationIssueCandidate>();
            this.prs = prs ?? Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }

    private sealed class FakeLabelMutator : IGitHubLabelMutator
    {
        private readonly IReadOnlyList<string> labels;
        public List<Transition> Transitions { get; } = new();

        public FakeLabelMutator(IReadOnlyList<string> labels) => this.labels = labels;

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            Transitions.Add(new Transition(kind, number, addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed record Transition(string Kind, int Number, IReadOnlyList<string> AddLabels, IReadOnlyList<string> RemoveLabels);

    private sealed class FakeRetirementMutator : IGitHubIssueRetirementMutator
    {
        public List<ClosedIssue> Closed { get; } = new();

        public void CloseAsNotPlanned(string repo, int issueNumber, string comment) =>
            Closed.Add(new ClosedIssue(repo, issueNumber, comment));
    }

    private sealed record ClosedIssue(string Repo, int IssueNumber, string Comment);

    private sealed class RetireWorkspace : IDisposable
    {
        public RetireWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("issue-retire-tests-").FullName;
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
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
            WriteInstalledCliScript();
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WriteQueueState(string json) => File.WriteAllText(Context.GetQueueStatePath(), json);

        // Without a cwd-local shim, AutomationInstalledCliSurfaceProbe falls back to
        // searching PATH for a globally installed intent-cli — present on a dev
        // machine but absent on CI runners, which made the WIP-gating test pass
        // locally and fail in CI. Writing the shim here removes that environment
        // dependency (mirrors AutomationHostReviewPreflightCommandTests's workspace).
        private void WriteInstalledCliScript()
        {
            var binPath = Path.Combine(RootPath, ".intent-cli", "bin");
            Directory.CreateDirectory(binPath);
            var scriptPath = Path.Combine(binPath, "intent-cli");
            File.WriteAllText(
                scriptPath,
                "#!/bin/sh\n"
                + "case \"$*\" in\n"
                + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
                + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
                + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
                + "  'automation pr-transition')\n"
                + "    echo '--transition is required (review-start, request-update, or approved).'\n"
                + "    exit 1\n"
                + "    ;;\n"
                + "  *) echo \"unexpected probe: $*\"; exit 1 ;;\n"
                + "esac\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    scriptPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }
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
