using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G525 (+ PR #1154 semantic re-review repair): focused coverage for
/// <c>automation issue-retire</c> — the canonical atomic transition that
/// supersedes a published <c>intent-target</c> issue that can never be
/// started as authored. Covers partial-failure recovery (a retry must
/// converge even after the issue is already closed), repo-exact queue
/// linkage + authoritative domain resolution (G522 boundary — a title
/// prefix alone must never create/mutate a queue entry), and a fail-closed
/// refusal for already-Completed work.
/// </summary>
public sealed class AutomationIssueRetireCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 16, 0, 0, TimeSpan.Zero);
    private const string Repo = "J-Tech-Japan/intent-system";

    public AutomationIssueRetireCommandTests()
    {
        // Default to an empty fake lister so tests that never override it
        // (most: they only care about the issue snapshot / label / queue
        // seams) cannot silently fall back to a REAL `gh pr list` subprocess
        // for the open-linked-PR check — that fallback happens to succeed
        // on a machine with an authenticated `gh` (e.g. a dev box) but fails
        // in CI, which is exactly the class of environment-dependent test
        // bug already hit once in this execution unit.
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister();
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
        var labelMutator = new FakeLabelMutator(new[] { "intent-target" });
        AutomationIssueRetireCommand.LabelMutatorFactory = () => labelMutator;
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "SKS-G812: Oversized single-slice contract", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "decomposed",
                "--note", "oversized; split into successor slices", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Equal("SKS-G812", result.ExecutionUnit);
        Assert.Equal("intent-cli", result.Domain);

        // GitHub mutation.
        var closed = Assert.Single(retirementMutator.Closed);
        Assert.Equal(Repo, closed.Repo);
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
        workspace.WriteQueueState(BuildQueueStateJson("G600", QueueItemState.Queued, Repo, linkedIssueNumber: 2001));
        AutomationIssueRetireCommand.LabelMutatorFactory = () => new FakeLabelMutator(new[] { "intent-target" });
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[2001] = OpenSnapshot(2001, "G600: Some slice", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "2001", "--reason", "superseded", "--domain", "intent-cli", "--write", "--format", "json"],
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
        var labelMutator = new FakeLabelMutator(new[] { "intent-target" });
        AutomationIssueRetireCommand.LabelMutatorFactory = () => labelMutator;
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "SKS-G812: Oversized single-slice contract", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "obsolete", "--domain", "intent-cli", "--format", "json"],
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
        var pr = BuildPr(1900, closingIssueNumber: 1744);
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister(prs: [pr]);
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "SKS-G812: Oversized single-slice contract", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "superseded", "--write", "--format", "json"],
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
        AutomationIssueRetireCommand.CandidateListerFactory = () => new FakeLister();
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "SKS-G812: Oversized single-slice contract", "intent-target", "intent-issue-in-progress");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "superseded", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("intent-issue-in-progress", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("declined-contract-incomplete", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(retirementMutator.Closed);
    }

    [Fact]
    public void Execute_Idempotent_AlreadyRetiredWithRunsEventPresent_IsPureNoOp()
    {
        using var workspace = new RetireWorkspace();
        workspace.WriteQueueState(BuildQueueStateJson("SKS-G812", QueueItemState.Retired, Repo, linkedIssueNumber: 1744, retirementReason: "decomposed"));
        workspace.WriteRunsLog(BuildRunEventLine("SKS-G812", AutomationIssueRetireCommand.RetireRunEventName));
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "decomposed", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(writer.ToString())!;
        Assert.True(result.AlreadyRetired);
        Assert.False(result.Applied);
        // No GitHub mutation attempted at all — the durable state alone
        // proves idempotency without needing a closed-issue GitHub lookup.
        Assert.Empty(retirementMutator.Closed);
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.Context.GetRunLogPath())));
    }

    [Fact]
    public void Execute_DryRun_AlreadyRetired_IsNoOpEvenWhenRunsEventMissing()
    {
        // Dry-run must never mutate, even to "finish" a missing runs.jsonl
        // event from a prior partial write.
        using var workspace = new RetireWorkspace();
        workspace.WriteQueueState(BuildQueueStateJson("SKS-G812", QueueItemState.Retired, Repo, linkedIssueNumber: 1744, retirementReason: "decomposed"));
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "decomposed", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(writer.ToString())!;
        Assert.True(result.AlreadyRetired);
        Assert.False(result.Applied);
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_Write_AlreadyRetiredButRunsEventMissing_FinishesOnlyTheRunsAppend()
    {
        // Review repair (partial-failure recovery, stage 3->4): queue-state
        // already shows Retired (a prior --write got that far) but
        // runs.jsonl never got the event (process died / disk fault right
        // after the queue write). A retry must not silently no-op forever —
        // it finishes exactly the missing step, with zero GitHub calls.
        using var workspace = new RetireWorkspace();
        workspace.WriteQueueState(BuildQueueStateJson("SKS-G812", QueueItemState.Retired, Repo, linkedIssueNumber: 1744, retirementReason: "decomposed"));
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "decomposed", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(writer.ToString())!;
        Assert.True(result.AlreadyRetired);
        Assert.True(result.Applied);
        Assert.Empty(retirementMutator.Closed);

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.Context.GetRunLogPath()));
        var runEvent = Assert.Single(events);
        Assert.Equal("SKS-G812", runEvent.ExecutionUnit);
        Assert.Equal(AutomationIssueRetireCommand.RetireRunEventName, runEvent.Event);
    }

    [Fact]
    public void Execute_IssueNotFoundAtAll_FailsClosedWithoutGuessing()
    {
        using var workspace = new RetireWorkspace();
        var retirementMutator = new FakeRetirementMutator();
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "9999", "--reason", "obsolete", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(retirementMutator.Closed);
        Assert.False(File.Exists(workspace.Context.GetQueueStatePath()));
    }

    [Fact]
    public void Execute_RejectsUnrecognizedReason()
    {
        using var workspace = new RetireWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "cancelled", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason must be one of", writer.ToString(), StringComparison.Ordinal);
    }

    // ── Semantic re-review finding #3: fail closed for Completed work ──────

    [Fact]
    public void Execute_RefusesToRetireCompletedQueueItem_ZeroMutation()
    {
        using var workspace = new RetireWorkspace();
        var originalJson = BuildQueueStateJson("G500", QueueItemState.Completed, Repo, linkedIssueNumber: 1744);
        workspace.WriteQueueState(originalJson);
        // No factories are seeded at all — if the refusal reached any
        // GitHub call, the default gh-cli-backed implementation would be
        // constructed and attempt a real subprocess, which would fail loudly
        // in this sandboxed test run. Reaching EmitResult without that
        // happening is itself proof of zero GitHub interaction.

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "obsolete", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Completed", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalJson, File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    // ── Semantic re-review finding #2: repo-exact linkage + domain (G522) ──

    [Fact]
    public void Execute_SameIssueNumberDifferentRepo_NeverMatchesOtherRepoQueueItem()
    {
        using var workspace = new RetireWorkspace();
        var otherRepoJson = BuildQueueStateJson("OTHER-UNIT", QueueItemState.Queued, "Other-Org/other-repo", linkedIssueNumber: 1744);
        workspace.WriteQueueState(otherRepoJson);
        AutomationIssueRetireCommand.LabelMutatorFactory = () => new FakeLabelMutator(new[] { "intent-target" });
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "G999: unrelated slice in the actual target repo", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "superseded", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(writer.ToString())!;
        // Derived from THIS repo's issue title, not the other repo's entry.
        Assert.Equal("G999", result.ExecutionUnit);

        var queueAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Equal(2, queueAfter.Items.Count);
        var otherItem = Assert.Single(queueAfter.Items, item => item.ExecutionUnit == "OTHER-UNIT");
        Assert.Equal(QueueItemState.Queued, otherItem.State);
        Assert.Equal("Other-Org/other-repo", otherItem.LinkedIssue!.Repo);
        var newItem = Assert.Single(queueAfter.Items, item => item.ExecutionUnit == "G999");
        Assert.Equal(QueueItemState.Retired, newItem.State);
        Assert.Equal(Repo, newItem.LinkedIssue!.Repo);
    }

    [Fact]
    public void Execute_UnderivableDomain_NoPacketNoExplicitDomain_FailsLoudWithCandidates()
    {
        using var workspace = new RetireWorkspace();
        workspace.WriteIntentsDomainDirectory("intent-cli");
        workspace.WriteIntentsDomainDirectory("other-domain");
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "G777: a title prefix with no packet.yaml anywhere", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "obsolete", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("could not be derived", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("other-domain", output, StringComparison.Ordinal);
        Assert.Contains("--domain", output, StringComparison.Ordinal);
        // Prefix alone must never create a queue entry.
        Assert.Empty(retirementMutator.Closed);
        Assert.False(File.Exists(workspace.Context.GetQueueStatePath()));
    }

    [Fact]
    public void Execute_ContradictingDomain_PacketDeclaresDifferentDomainThanExplicitDomain_FailsLoud()
    {
        // Doubles as the "misleading title" defense: the title prefix looks
        // like it belongs to the operator's intended domain, but the real
        // packet.yaml on disk says otherwise — the packet, never the title,
        // is authoritative.
        using var workspace = new RetireWorkspace();
        workspace.WritePacketYaml("G525", "billing");
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "G525: looks like it belongs to intent-cli", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "obsolete", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("contradicts", output, StringComparison.Ordinal);
        Assert.Contains("billing", output, StringComparison.Ordinal);
        Assert.Empty(retirementMutator.Closed);
        Assert.False(File.Exists(workspace.Context.GetQueueStatePath()));
    }

    // ── Semantic re-review finding #1: partial-failure recovery ────────────

    [Fact]
    public void Execute_Write_RecoversAfterCloseSucceedsButLabelRemovalFails()
    {
        using var workspace = new RetireWorkspace();
        var labelMutator = new FakeLabelMutator(new[] { "intent-target" })
        {
            ThrowOnApply = new InvalidOperationException("gh label remove: simulated transient failure"),
        };
        AutomationIssueRetireCommand.LabelMutatorFactory = () => labelMutator;
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "SKS-G812: Oversized single-slice contract", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var firstWriter = new StringWriter();
        var firstExitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "decomposed", "--domain", "intent-cli", "--write", "--format", "json"],
            firstWriter);

        Assert.Equal(1, firstExitCode);
        Assert.Contains("already closed", firstWriter.ToString(), StringComparison.Ordinal);
        Assert.Single(retirementMutator.Closed); // close DID happen — stage pinned after A, before B.
        Assert.False(File.Exists(workspace.Context.GetQueueStatePath()));

        // Retry: the issue is now CLOSED (not planned) with labels still
        // present — exactly what the first attempt's failure left behind.
        retirementMutator.Snapshots[1744] = ClosedNotPlannedSnapshot(1744, "SKS-G812: Oversized single-slice contract", new[] { "intent-target" });
        labelMutator.ThrowOnApply = null;

        using var secondWriter = new StringWriter();
        var secondExitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "decomposed", "--domain", "intent-cli", "--write", "--format", "json"],
            secondWriter);

        Assert.Equal(0, secondExitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueRetireResult>(secondWriter.ToString())!;
        Assert.True(result.Applied);
        // Not re-closed — the retry recognized the issue was already closed.
        Assert.Single(retirementMutator.Closed);
        Assert.Single(labelMutator.Transitions);

        var queueAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var item = Assert.Single(queueAfter.Items);
        Assert.Equal(QueueItemState.Retired, item.State);
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.Context.GetRunLogPath())));
    }

    [Fact]
    public void Execute_Write_RefusesRecoveryWhenClosedForAnUnrelatedReason()
    {
        // A CLOSED issue whose stateReason is NOT "not planned" (e.g. closed
        // via a merge) must never be treated as an issue-retire recovery
        // target — only this command's own close reason authorizes resuming.
        using var workspace = new RetireWorkspace();
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = new IssueSnapshot
        {
            State = "CLOSED",
            StateReason = "COMPLETED",
            Title = "SKS-G812: Oversized single-slice contract",
            Url = $"https://github.com/{Repo}/issues/1744",
            Labels = Array.Empty<string>(),
        };
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "obsolete", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("COMPLETED", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(retirementMutator.Closed);
        Assert.False(File.Exists(workspace.Context.GetQueueStatePath()));
    }

    [Fact]
    public void Execute_Write_QueueStateWriteFails_RecoversOnRetryWithoutReClosing()
    {
        // Fault injected AFTER close + label removal succeed, around queue
        // persistence: make the pre-existing queue-state.json read-only so
        // the overwrite throws, then retry after restoring write access.
        using var workspace = new RetireWorkspace();
        workspace.WriteQueueState(BuildQueueStateJson("G600", QueueItemState.Queued, Repo, linkedIssueNumber: 2001));
        var queueStatePath = workspace.Context.GetQueueStatePath();
        var labelMutator = new FakeLabelMutator(new[] { "intent-target" });
        AutomationIssueRetireCommand.LabelMutatorFactory = () => labelMutator;
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[2001] = OpenSnapshot(2001, "G600: Some slice", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(queueStatePath, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        using var firstWriter = new StringWriter();
        var firstExitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "2001", "--reason", "superseded", "--domain", "intent-cli", "--write", "--format", "json"],
            firstWriter);

        if (OperatingSystem.IsWindows())
        {
            // Read-only fault injection is unix-permission-specific; skip
            // the assertions that depend on the write actually failing.
            return;
        }

        Assert.Equal(1, firstExitCode);
        Assert.Contains("queue-state update failed", firstWriter.ToString(), StringComparison.Ordinal);
        Assert.Single(retirementMutator.Closed);
        Assert.Single(labelMutator.Transitions);

        File.SetUnixFileMode(
            queueStatePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        retirementMutator.Snapshots[2001] = ClosedNotPlannedSnapshot(2001, "G600: Some slice", Array.Empty<string>());

        using var secondWriter = new StringWriter();
        var secondExitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "2001", "--reason", "superseded", "--domain", "intent-cli", "--write", "--format", "json"],
            secondWriter);

        Assert.Equal(0, secondExitCode);
        Assert.Single(retirementMutator.Closed); // still not re-closed.
        var queueAfter = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var item = Assert.Single(queueAfter.Items);
        Assert.Equal(QueueItemState.Retired, item.State);
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.Context.GetRunLogPath())));
    }

    [Fact]
    public void Execute_Write_RunsAppendFails_RetryFinishesOnlyTheMissingEvent()
    {
        // Fault injected around the runs.jsonl append (after queue
        // persistence succeeds): pre-seed runs.jsonl as read-only so the
        // append throws even though the queue-state write already landed.
        using var workspace = new RetireWorkspace();
        AutomationIssueRetireCommand.LabelMutatorFactory = () => new FakeLabelMutator(new[] { "intent-target" });
        var retirementMutator = new FakeRetirementMutator();
        retirementMutator.Snapshots[1744] = OpenSnapshot(1744, "SKS-G812: Oversized single-slice contract", "intent-target");
        AutomationIssueRetireCommand.RetirementMutatorFactory = () => retirementMutator;

        var runsPath = workspace.Context.GetRunLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
        File.WriteAllText(runsPath, string.Empty);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(runsPath, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        using var firstWriter = new StringWriter();
        var firstExitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "decomposed", "--domain", "intent-cli", "--write", "--format", "json"],
            firstWriter);

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(1, firstExitCode);
        Assert.Contains("runs.jsonl event failed", firstWriter.ToString(), StringComparison.Ordinal);
        // Queue-state DID persist as Retired even though the runs append failed.
        var queueAfterFirst = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Equal(QueueItemState.Retired, Assert.Single(queueAfterFirst.Items).State);

        File.SetUnixFileMode(runsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        using var secondWriter = new StringWriter();
        var secondExitCode = AutomationIssueRetireCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--issue", "1744", "--reason", "decomposed", "--domain", "intent-cli", "--write", "--format", "json"],
            secondWriter);

        Assert.Equal(0, secondExitCode);
        var secondResult = JsonSerializer.Deserialize<AutomationIssueRetireResult>(secondWriter.ToString())!;
        Assert.True(secondResult.AlreadyRetired);
        Assert.True(secondResult.Applied);
        // The retry never re-attempted any GitHub mutation — still just the
        // ONE close from the first attempt; queue-state already showed
        // Retired, so only the missing runs event was finished.
        Assert.Single(retirementMutator.Closed);
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(runsPath));
        Assert.Single(events);
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
        var queueStateJson = BuildQueueStateJson("SKS-G812", QueueItemState.Retired, Repo, linkedIssueNumber: 1744, retirementReason: "decomposed");

        var result = MetadataValidateAnalyzer.Analyze(new MetadataValidateInputs
        {
            ExecutionUnit = "SKS-G812",
            QueueStateJson = queueStateJson,
        });

        Assert.DoesNotContain(result.Errors, finding => finding.Code == MetadataValidateConstants.Codes.QueueEntryMissing);
        Assert.DoesNotContain(result.Errors, finding => finding.Code == MetadataValidateConstants.Codes.CompletedMissingClosure);
    }

    private static IssueSnapshot OpenSnapshot(int number, string title, params string[] labels) => new()
    {
        State = "OPEN",
        StateReason = string.Empty,
        Title = title,
        Url = $"https://github.com/{Repo}/issues/{number}",
        Labels = labels,
    };

    private static IssueSnapshot ClosedNotPlannedSnapshot(int number, string title, IReadOnlyList<string> labels) => new()
    {
        State = "CLOSED",
        StateReason = "NOT_PLANNED",
        Title = title,
        Url = $"https://github.com/{Repo}/issues/{number}",
        Labels = labels,
    };

    private static string BuildRunEventLine(string executionUnit, string eventName) =>
        RunLogSerializer.SerializeLine(new RunEvent
        {
            Ts = FixedNow.AddMinutes(-5),
            ExecutionUnit = executionUnit,
            Event = eventName,
            By = "intent-cli automation issue-retire (G525)",
            Reason = "decomposed",
        });

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
        string executionUnit, QueueItemState state, string linkedIssueRepo, int linkedIssueNumber, string? retirementReason = null)
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
                        Repo = linkedIssueRepo,
                        Number = linkedIssueNumber,
                        Url = $"https://github.com/{linkedIssueRepo}/issues/{linkedIssueNumber}",
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
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;

        public FakeLister(IReadOnlyList<GitHubAutomationPrCandidate>? prs = null)
        {
            this.prs = prs ?? Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class FakeLabelMutator : IGitHubLabelMutator
    {
        private readonly IReadOnlyList<string> labels;
        public List<Transition> Transitions { get; } = new();
        public Exception? ThrowOnApply { get; set; }

        public FakeLabelMutator(IReadOnlyList<string> labels) => this.labels = labels;

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            if (ThrowOnApply is not null)
            {
                throw ThrowOnApply;
            }
            Transitions.Add(new Transition(kind, number, addLabels.ToArray(), removeLabels.ToArray()));
        }

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed record Transition(string Kind, int Number, IReadOnlyList<string> AddLabels, IReadOnlyList<string> RemoveLabels);

    private sealed class FakeRetirementMutator : IGitHubIssueRetirementMutator
    {
        public List<ClosedIssue> Closed { get; } = new();
        public Dictionary<int, IssueSnapshot> Snapshots { get; } = new();
        public Exception? ThrowOnClose { get; set; }

        public void CloseAsNotPlanned(string repo, int issueNumber, string comment)
        {
            if (ThrowOnClose is not null)
            {
                throw ThrowOnClose;
            }
            Closed.Add(new ClosedIssue(repo, issueNumber, comment));
        }

        public IssueSnapshot GetSnapshot(string repo, int issueNumber)
        {
            if (Snapshots.TryGetValue(issueNumber, out var snapshot))
            {
                return snapshot;
            }
            throw new InvalidOperationException(
                $"[github-cli-generic] gh failed to view issue #{issueNumber} in {repo}: not found (fake).");
        }
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

        public void WriteRunsLog(string line)
        {
            var runsPath = Context.GetRunLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
            File.WriteAllText(runsPath, line + "\n");
        }

        public void WritePacketYaml(string executionUnit, string domain)
        {
            var packetDir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(packetDir);
            File.WriteAllText(Path.Combine(packetDir, "packet.yaml"), $"domain: {domain}\n");
        }

        public void WriteIntentsDomainDirectory(string domain)
        {
            Directory.CreateDirectory(Path.Combine(RootPath, "intents", domain));
        }

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
                if (!OperatingSystem.IsWindows())
                {
                    var queueStatePath = Context.GetQueueStatePath();
                    if (File.Exists(queueStatePath))
                    {
                        File.SetUnixFileMode(
                            queueStatePath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                    }
                    var runsPath = Context.GetRunLogPath();
                    if (File.Exists(runsPath))
                    {
                        File.SetUnixFileMode(
                            runsPath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                    }
                }
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
