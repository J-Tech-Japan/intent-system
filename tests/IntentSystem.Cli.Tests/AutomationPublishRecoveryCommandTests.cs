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
    public void Execute_G390_SameRepoMetadata_UniqueLinkedPrRepair_IsRepairReady()
    {
        // G390: same-repo metadata topology + a PR #3639-style fixture where the
        // PR uniquely closes the linked issue and only linked_pr is missing →
        // a high-confidence writeable metadata-branch repair.
        using var workspace = new RecoveryWorkspace(sameRepoMetadata: true);
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
        Assert.Equal(
            "same-repo-metadata-linkage-repair-ready",
            doc.RootElement.GetProperty("same_repo_metadata_linkage_classification").GetString());
    }

    [Fact]
    public void Execute_G390_SingleRootTopology_ClassificationIsNotApplicable()
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
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            "not-applicable",
            doc.RootElement.GetProperty("same_repo_metadata_linkage_classification").GetString());
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
    public void Execute_G390_Write_AppendsLinkedPrRecoveredRunEvent()
    {
        // G390 review Finding 1: a --write recovery must record a durable
        // runs.jsonl event so the linked_pr repair is auditable and
        // closeout-plan can observe it.
        using var workspace = new RecoveryWorkspace(sameRepoMetadata: true);
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
        var runsPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        Assert.True(File.Exists(runsPath), "expected runs.jsonl to be appended on --write recovery");
        var runs = File.ReadAllText(runsPath);
        Assert.Contains(AutomationPublishRecoveryCommand.RecoveryRunEventName, runs, StringComparison.Ordinal);
        Assert.Contains("SKS-G219", runs, StringComparison.Ordinal);
        Assert.Contains("559", runs, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G390_DryRun_DoesNotAppendRunEvent()
    {
        using var workspace = new RecoveryWorkspace(sameRepoMetadata: true);
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
        var runsPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        Assert.False(File.Exists(runsPath), "dry-run must not append a run event");
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

    // --- G351: --pr scoped recovery ----

    [Fact]
    public void Execute_G351_ScopedToPr_G346Fixture_ProducesSingleRepair_DryRun()
    {
        // G351 AC fixture: queue item G346 has linked_issue=#795, linked_pr=null.
        // An unrelated G999 item also has missing linked_pr. --pr 796 scopes
        // the result to only G346 — G999 must not appear.
        using var workspace = new RecoveryWorkspace();
        var li795 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var li888 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 888,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/888" };
        var qs = BuildQueueStateMulti(
            ("G346", li795, null),
            ("G999", li888, null));
        workspace.WriteQueueState(qs);

        // PR #796 closes #795; PR #900 closes #888.
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "Closes #795"), BuildPr(900, "Closes #888") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "796", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.Equal(796, root.GetProperty("selected_pr").GetInt32());
        Assert.Equal(1, root.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, root.GetProperty("unsafe_stops").GetArrayLength());
        var repair = root.GetProperty("safe_repairs")[0];
        Assert.Equal("G346", repair.GetProperty("execution_unit").GetString());
        Assert.Equal(795, repair.GetProperty("linked_issue_number").GetInt32());
        Assert.Equal(796, repair.GetProperty("linked_pr_number").GetInt32());
    }

    [Fact]
    public void Execute_G351_ScopedToPr_Write_AppliesRepairForSelectedPrOnly()
    {
        // G351 AC: --pr --write applies the repair for the selected PR's
        // linked queue item and does NOT mutate unrelated G999 item.
        using var workspace = new RecoveryWorkspace();
        var li795 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var li888 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 888,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/888" };
        workspace.WriteQueueState(BuildQueueStateMulti(("G346", li795, null), ("G999", li888, null)));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "Closes #795"), BuildPr(900, "Closes #888") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "796", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(1, doc.RootElement.GetProperty("applied_count").GetInt32());

        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var g346 = queueAfter.Items.First(i => i.ExecutionUnit == "G346");
        var g999 = queueAfter.Items.First(i => i.ExecutionUnit == "G999");
        // G346 got the repair.
        Assert.Contains("/pull/796", g346.LinkedPr!, StringComparison.Ordinal);
        // G999 was NOT mutated.
        Assert.Null(g999.LinkedPr);
    }

    [Fact]
    public void Execute_G351_ScopedToPr_InvalidPrNumber_ReturnsError()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G346", linkedIssue: null, linkedPr: null));
        using var writer = new StringWriter();

        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "not-a-number"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G351_ScopedToPr_NoPrInOpenList_ReturnsEmptyNoOp()
    {
        // G351: when the selected PR is not in the open list (already merged),
        // the scoped result is empty — no repairs and no unsafe stops.
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        workspace.WriteQueueState(BuildQueueState("G346", linkedIssue: li, linkedPr: null));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(900, "Closes #900") }); // PR #796 is NOT in the list

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "796", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());
    }

    [Fact]
    public void Execute_G351_ScopedToPr_PrHasNoClosingRef_ProducesConciseScopedUnsafeStop()
    {
        // G351 snapshot verification: with --pr, when the selected PR has no
        // closing reference, the output contains exactly one unsafe stop (scoped)
        // and does NOT include stops from unrelated queue items.
        using var workspace = new RecoveryWorkspace();
        var li795 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var li888 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 888,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/888" };
        workspace.WriteQueueState(BuildQueueStateMulti(("G346", li795, null), ("G999", li888, null)));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "PR without closing reference") }); // no Closes #N

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "796", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        // Exactly one concise unsafe stop, not a flood of two stops.
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());
        var stop = doc.RootElement.GetProperty("unsafe_stops")[0];
        // The stop must mention the selected PR number.
        Assert.Contains("796", stop.GetProperty("reason").GetString()!, StringComparison.Ordinal);
    }

    private static string BuildQueueStateMulti(params (string ExecutionUnit, LinkedIssue? LinkedIssue, string? LinkedPr)[] items)
    {
        var queueItems = items.Select(i => new QueueItem
        {
            ExecutionUnit = i.ExecutionUnit,
            Title = $"{i.ExecutionUnit} title",
            State = QueueItemState.Queued,
            Dependencies = Array.Empty<string>(),
            BlockedBy = Array.Empty<string>(),
            ClarificationReturnPath = string.Empty,
            PacketPaths = new PacketPaths
            {
                Yaml = $".intent-cli/issues/{i.ExecutionUnit}/packet.yaml",
                Implementation = $".intent-cli/issues/{i.ExecutionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{i.ExecutionUnit}/review-context.md"
            },
            LinkedIssue = i.LinkedIssue,
            LinkedPr = i.LinkedPr,
            WorkerRole = "Claude",
            ReviewRole = "Codex",
            Priority = "normal"
        }).ToArray();
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 5, 14, 0, 0, 0, TimeSpan.Zero),
            Items = queueItems
        };
        return QueueStateSerializer.Serialize(state);
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
        public RecoveryWorkspace(bool sameRepoMetadata = false)
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
                        WorktreeRoot = ".intent-cli/worktrees",
                        // G390: same-repo metadata topology (metadata on a
                        // dedicated branch) so the linkage classification is
                        // exercised at the command level.
                        SameRepoTopology = sameRepoMetadata,
                        MetadataWriteBranch = sameRepoMetadata ? "intent-metadata" : string.Empty,
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
