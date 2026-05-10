using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationPublishRecoveryCommandTests : IDisposable
{
    public AutomationPublishRecoveryCommandTests()
    {
        AutomationPublishRecoveryCommand.CandidateListerFactory = null;
    }

    public void Dispose()
    {
        AutomationPublishRecoveryCommand.CandidateListerFactory = null;
    }

    [Fact]
    public void Execute_DryRun_ProducesHighConfidenceRepair_ButDoesNotMutateQueueState()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: null, linkedPr: null));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(706, "Closes #703") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());

        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Null(queueAfter.Items[0].LinkedIssue);
        Assert.Null(queueAfter.Items[0].LinkedPr);
    }

    [Fact]
    public void Execute_Write_AppliesRepair_AndUpdatesQueueState()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: null, linkedPr: null));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(706, "Closes #703") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("write", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("applied_count").GetInt32());

        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var item = queueAfter.Items[0];
        Assert.NotNull(item.LinkedIssue);
        Assert.Equal(703, item.LinkedIssue!.Number);
        Assert.Equal("J-Tech-Japan/intent-system", item.LinkedIssue.Repo);
        Assert.Contains("/pull/706", item.LinkedPr!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AmbiguousMultiplePrs_DoesNotWrite_EvenWithWriteFlag()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: null, linkedPr: null));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(706, "Closes #703"), BuildPr(707, "Closes #703") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());

        // Mutation invariant.
        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Null(queueAfter.Items[0].LinkedIssue);
        Assert.Null(queueAfter.Items[0].LinkedPr);
    }

    [Fact]
    public void Execute_AlreadyLinkedItem_NotIncluded()
    {
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 703, Url = "https://github.com/J-Tech-Japan/intent-system/issues/703" };
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: li, linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/706"));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(706, "Closes #703") });

        using var writer = new StringWriter();
        AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());
    }

    // --- G315: queue-state-backed linked_pr lane (no publish.yaml needed) ----

    [Fact]
    public void Execute_LinkedIssuePresentNoPr_DryRun_ReportsG315HighConfidenceRepair()
    {
        // SKS-G219-style fixture: queue already has linked_issue=#558,
        // linked_pr=null. PR #559 closes #558. No publish.yaml needed.
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #558") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());

        var repair = doc.RootElement.GetProperty("safe_repairs")[0];
        Assert.Equal(
            PublishRecoveryAnalyzer.RepairTypeLinkedIssueClosingPr,
            repair.GetProperty("type").GetString());
        Assert.Equal(558, repair.GetProperty("linked_issue_number").GetInt32());
        Assert.Equal(559, repair.GetProperty("linked_pr_number").GetInt32());
    }

    [Fact]
    public void Execute_LinkedIssuePresentNoPr_Write_FillsLinkedPr_PreservesLinkedIssue()
    {
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #558") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var item = queueAfter.Items[0];
        Assert.NotNull(item.LinkedIssue);
        Assert.Equal(558, item.LinkedIssue!.Number);
        Assert.Equal("J-Tech-Japan/intent-system", item.LinkedIssue.Repo);
        // The original linked_issue URL must be preserved verbatim — the
        // G315 lane only fills in linked_pr.
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/558", item.LinkedIssue.Url);
        Assert.Contains("/pull/559", item.LinkedPr!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_LinkedIssuePresentNoPr_NoClosingPr_StaysUnsafe_NoMutation()
    {
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));

        // PR exists but doesn't close #558 — operator must repair the PR
        // body before host metadata can recover.
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "no closing reference here") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());
        Assert.Equal(
            PublishRecoveryAnalyzer.UnsafeNoClosingPrForLinkedIssue,
            doc.RootElement.GetProperty("unsafe_stops")[0].GetProperty("kind").GetString());

        // Mutation invariant — the queue row stays unchanged.
        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Null(queueAfter.Items[0].LinkedPr);
        Assert.NotNull(queueAfter.Items[0].LinkedIssue);
    }

    [Fact]
    public void Execute_RequiresRepoFlag()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: null, linkedPr: null));
        using var writer = new StringWriter();

        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    private static string BuildQueueState(string executionUnit, LinkedIssue? linkedIssue, string? linkedPr)
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
            Items = new[]
            {
                new QueueItem
                {
                    ExecutionUnit = executionUnit,
                    Title = $"{executionUnit} title",
                    State = QueueItemState.Queued,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md"
                    },
                    LinkedIssue = linkedIssue,
                    LinkedPr = linkedPr,
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = "normal"
                }
            }
        };
        return QueueStateSerializer.Serialize(state);
    }

    private static GitHubAutomationPrCandidate BuildPr(int number, string body) =>
        new()
        {
            Number = number,
            Title = $"PR {number}",
            Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
            Body = body,
            CreatedAt = "2026-05-08T00:00:00Z",
            UpdatedAt = "2026-05-08T00:00:00Z",
            Labels = Array.Empty<GitHubAutomationLabel>(),
            State = "OPEN"
        };

    private sealed class FakePrLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        public FakePrLister(IReadOnlyList<GitHubAutomationPrCandidate> prs) => this.prs = prs;
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class RecoveryWorkspace : IDisposable
    {
        public RecoveryWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("publish-recovery-tests-").FullName;
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
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void WriteQueueState(string toml)
        {
            File.WriteAllText(Context.GetQueueStatePath(), toml);
        }

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
                CreatedIssueUrl = $"https://github.com/J-Tech-Japan/intent-system/issues/{createdIssueNumber}",
                PublishedLabelName = "intent-target"
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
