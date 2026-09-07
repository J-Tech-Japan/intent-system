using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G811's completion channel is an identity-bound, append-only handshake:
/// external consumption creates one receipt, Steward publishes one return
/// acknowledgement, and the originating resident consumes it on its next
/// task-scoped turn. These tests intentionally use synthetic stores only.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifyCompletionChannelG811Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 18, 0, 0, TimeSpan.Zero);
    private readonly string root = Directory.CreateTempSubdirectory("notify-g811-").FullName;

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void CompletionIdentityReceiptAndReturnAckAreReplaySafeAndPreserveResidentBinding_G811()
    {
        var pending = Pending();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);

        var receipt = Receipt("cursor-1", Now);
        var firstReceipt = NotifyCompletionChannelStore.WriteReceipt(root, receipt, write: true);
        Assert.True(firstReceipt.Written);

        // A replay observed on a later cursor/time is the same completion
        // identity, not a conflicting second consumption.
        var replayReceipt = NotifyCompletionChannelStore.WriteReceipt(
            root,
            receipt with { Cursor = "cursor-2", ConsumedAt = Now.AddSeconds(2) },
            write: true);
        Assert.True(replayReceipt.AlreadyConverged);
        Assert.Null(replayReceipt.Error);

        var ack = new NotifyReturnAck
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = pending.ResultNonce,
            Artifact = pending.ExpectedArtifact,
            StewardRole = LogicalRoleNormalizer.Steward,
            RecipientRole = pending.RecipientRole,
            RecipientIdentity = pending.RecipientIdentity,
            Resident = pending.Resident!,
            WorkspaceId = pending.WorkspaceId,
            PaneId = pending.PaneId,
            ConsumptionReceiptId = receipt.ReceiptId,
            ConsumptionCursor = receipt.Cursor,
            AvailableAt = Now,
        };
        var firstAck = NotifyCompletionChannelStore.WriteAck(root, ack, write: true);
        Assert.True(firstAck.Written);
        Assert.True(NotifyCompletionChannelStore.WriteAck(root, ack with { AvailableAt = Now.AddSeconds(1) }, write: true).AlreadyConverged);

        var dryRun = NotifyCompletionChannelStore.ConsumeAck(root, ack, Now.AddSeconds(2), write: false);
        Assert.False(dryRun.Written);
        Assert.Null(dryRun.Error);
        Assert.Null(NotifyCompletionChannelStore.FindAck(root, Domain, Team, pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact).Ack!.ConsumedAt);

        var consumed = NotifyCompletionChannelStore.ConsumeAck(root, ack, Now.AddSeconds(3), write: true);
        Assert.True(consumed.Written);
        var final = NotifyCompletionChannelStore.FindAck(root, Domain, Team, pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact).Ack;
        Assert.Equal(Now.AddSeconds(3), final!.ConsumedAt);
        Assert.Equal(pending.RecipientRole, final.RecipientRole);
        Assert.Equal(pending.RecipientIdentity, final.RecipientIdentity);
        Assert.Equal(pending.WorkspaceId, final.WorkspaceId);
        Assert.Equal(pending.PaneId, final.PaneId);
        Assert.True(NotifyCompletionChannelStore.ConsumeAck(root, ack, Now.AddSeconds(4), write: true).AlreadyConverged);
    }

    [Fact]
    public void IdentityMismatchesAndUnknownTasksRefuseWithoutAdvancingReceiptOrAck_G811()
    {
        var pending = Pending();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);
        Assert.True(NotifyCompletionChannelStore.WriteReceipt(root, Receipt("cursor-1", Now), write: true).Written);

        var unknown = NotifyCompletionChannelStore.FindAck(root, Domain, Team, "G811-unknown", "nonce", pending.ExpectedArtifact);
        Assert.Null(unknown.Ack);
        var wrongAck = new NotifyReturnAck
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = "wrong-nonce",
            Artifact = pending.ExpectedArtifact,
            StewardRole = LogicalRoleNormalizer.Steward,
            RecipientRole = pending.RecipientRole,
            RecipientIdentity = pending.RecipientIdentity,
            Resident = pending.Resident!,
            WorkspaceId = pending.WorkspaceId,
            PaneId = pending.PaneId,
            ConsumptionReceiptId = "not-recorded",
            ConsumptionCursor = "cursor-1",
            AvailableAt = Now,
        };
        Assert.True(NotifyCompletionChannelStore.WriteAck(root, wrongAck, write: false).Error is null);
        Assert.Null(NotifyCompletionChannelStore.FindAck(root, Domain, Team, pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact).Ack);
        var missingReceipt = NotifyCompletionChannelStore.FindReceipt(
            root, Domain, Team, pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact,
            LogicalRoleNormalizer.Steward, "unseen-cursor");
        Assert.Null(missingReceipt.Receipt);
    }

    [Fact]
    public void HealthSeparatesDeliveredUnreconciledMissingAckAndResidentPendingStates_G811()
    {
        var pending = Pending();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);
        Assert.True(NotifyReportOutboxStore.WriteNew(root, new NotifyReportOutboxEntry
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = pending.ResultNonce,
            FromRole = "implementation",
            ToRole = "steward",
            Status = "completed",
            Artifact = pending.ExpectedArtifact,
            Summary = "completed",
            CreatedAt = Now,
            DeliveryState = "delivered",
        }).Written);

        var unreconciled = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now, measuredSweeps: 3);
        Assert.Equal("failed", unreconciled.State);
        Assert.Equal(1, unreconciled.DeliveredUnreconciledCount);
        Assert.Null(unreconciled.LastFloorCycleAt);
        Assert.False(unreconciled.Qualified is false && unreconciled.State == "healthy");

        Assert.True(NotifyPendingDelegationStore.WriteReport(root, pending, "completed", pending.ExpectedArtifact, "done", Now).Written);
        var missingAck = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now, measuredSweeps: 3);
        Assert.Equal("failed", missingAck.State);
        Assert.NotNull(missingAck.MissingReturnAckAgeSeconds);

        var receipt = Receipt("cursor-1", Now);
        Assert.True(NotifyCompletionChannelStore.WriteReceipt(root, receipt, write: true).Written);
        var ack = new NotifyReturnAck
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = pending.ResultNonce,
            Artifact = pending.ExpectedArtifact,
            StewardRole = LogicalRoleNormalizer.Steward,
            RecipientRole = pending.RecipientRole,
            RecipientIdentity = pending.RecipientIdentity,
            Resident = pending.Resident!,
            WorkspaceId = pending.WorkspaceId,
            PaneId = pending.PaneId,
            ConsumptionReceiptId = receipt.ReceiptId,
            ConsumptionCursor = receipt.Cursor,
            AvailableAt = Now,
        };
        Assert.True(NotifyCompletionChannelStore.WriteAck(root, ack, write: true).Written);
        var pendingResident = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now, measuredSweeps: 3);
        Assert.Equal("degraded", pendingResident.State);
        Assert.NotNull(pendingResident.ResidentConsumptionPendingAgeSeconds);

        Assert.True(NotifyCompletionChannelStore.ConsumeAck(root, ack, Now.AddSeconds(1), write: true).Written);
        var settled = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now.AddSeconds(1), measuredSweeps: 3);
        Assert.Equal("healthy", settled.State);
        Assert.Equal(301, settled.BoundSeconds);
        Assert.Contains("ceil(300+0+0)+1", settled.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidanceExposesCanonicalCollectAcknowledgeReconcileHealthAndNoWakeBoundary_G811()
    {
        var markdown = CompletionChannelGuidance.AppendMarkdown("base");
        Assert.Contains("G811 completion channel", markdown, StringComparison.Ordinal);
        Assert.Contains("notify acknowledge", markdown, StringComparison.Ordinal);
        Assert.Contains("no pane/model wake", markdown, StringComparison.Ordinal);

        var json = CompletionChannelGuidance.AppendJson("{\"route\":\"guide\"}");
        using var document = JsonDocument.Parse(json);
        var channel = document.RootElement.GetProperty("completion_channel");
        Assert.Contains("notify collect", channel.GetProperty("StewardCollectCommand").GetString()!, StringComparison.Ordinal);
        Assert.Contains("--receipt-cursor", channel.GetProperty("StewardAcknowledgeCommand").GetString()!, StringComparison.Ordinal);
        Assert.Contains("notify reconcile", channel.GetProperty("ReconcileCommand").GetString()!, StringComparison.Ordinal);
        Assert.Contains("measured complete sweeps", channel.GetProperty("BoundRule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("No provider, pane, focus", channel.GetProperty("Boundary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalStewardCollectThenIdentityBoundResidentCollectCompletesTheCanonicalHandshake_G811()
    {
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            },
        };
        var readerRelative = $".intent-cli/events/{Domain}/{Team}.jsonl";
        var readerPath = Path.Combine(root, readerRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(readerPath)!);
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
        File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "w1",
            roles = new Dictionary<string, object>
            {
                ["steward"] = new { resident = NotifyRecordedRole.ExternalResident, reader = readerRelative },
                ["builder"] = new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "w1", pane_id = "w1:p2" },
            },
        }));

        var pending = Pending();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);
        NotifyEventWriter.Append(readerPath, new NotifyDesignEvent
        {
            Timestamp = Now,
            Team = Team,
            Kind = "completion",
            Unit = pending.TaskId,
            Summary = "task completed",
            Artifact = pending.ExpectedArtifact,
            ResultNonce = pending.ResultNonce,
            CompletionIdentity = NotifyCompletionChannelStore.CompletionIdentity(pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact),
        });

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                context,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
        }

        var collect = Run(context, ["notify", "collect", "--domain", Domain, "--team", Team, "--role", "steward", "--write", "--format", "json"]);
        Assert.Equal(0, collect.ExitCode);
        Assert.Equal(1, collect.Result.GetProperty("events").GetArrayLength());
        var receipt = Assert.Single(collect.Result.GetProperty("consumption_receipts").EnumerateArray());
        Assert.True(receipt.GetProperty("written").GetBoolean());
        var cursor = collect.Result.GetProperty("next_cursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        var ack = Run(context, ["notify", "acknowledge", "--domain", Domain, "--team", Team, "--from", "steward", "--task-id", pending.TaskId, "--result-nonce", pending.ResultNonce!, "--artifact", pending.ExpectedArtifact, "--receipt-cursor", cursor!, "--write", "--format", "json"]);
        Assert.Equal(0, ack.ExitCode);
        Assert.True(ack.Result.GetProperty("written").GetBoolean());
        Assert.Equal(pending.RecipientIdentity, ack.Result.GetProperty("ack").GetProperty("recipient_identity").GetString());

        Assert.True(NotifyPendingDelegationStore.WriteReport(root, pending, "completed", pending.ExpectedArtifact, "done", Now).Written);
        var resident = Run(context, ["notify", "collect", "--domain", Domain, "--team", Team, "--task-id", pending.TaskId, "--role", "builder", "--write", "--format", "json"]);
        Assert.Equal(0, resident.ExitCode);
        Assert.True(resident.Result.GetProperty("consumed").GetBoolean());
        Assert.True(resident.Result.GetProperty("ack").GetProperty("consumed_at").ValueKind != JsonValueKind.Null);
        Assert.DoesNotContain(NotifyCompletionChannelStore.ReadAllAcks(root, Domain, Team, out var readError), item => item.ConsumedAt is null);
        Assert.Null(readError);
    }

    private static (int ExitCode, JsonElement Result) Run(CliContext context, string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, context, writer);
        return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
    }

    private NotifyPendingDelegation Pending() => new()
    {
        Domain = Domain,
        Team = Team,
        TaskId = "G811-demo",
        TaskKind = "implementation",
        DelegatingRole = "orchestration",
        RecipientRole = "builder",
        ReportToRole = "orchestration",
        RecipientIdentity = "resident=herdr;workspace=w1;pane=w1:p2",
        ExpectedArtifact = "https://example.test/pr/1762",
        ResultNonce = "g811-nonce",
        DispatchedAt = Now,
        Resident = NotifyRecordedRole.HerdrResident,
        WorkspaceId = "w1",
        PaneId = "w1:p2",
        Reader = ".intent-cli/events/intent-cli/intent-cli-dev.jsonl",
        Cwd = "/tmp/g811-child",
        Kind = "completion",
        TransportMode = "herdr-only",
    };

    private NotifyConsumptionReceipt Receipt(string cursor, DateTimeOffset consumedAt) => new()
    {
        ReceiptId = NotifyCompletionChannelStore.BuildReceiptId("G811-demo", "g811-nonce", "https://example.test/pr/1762", LogicalRoleNormalizer.Steward, cursor),
        Domain = Domain,
        Team = Team,
        Role = LogicalRoleNormalizer.Steward,
        TaskId = "G811-demo",
        ResultNonce = "g811-nonce",
        Artifact = "https://example.test/pr/1762",
        Cursor = cursor,
        ReaderPath = Path.Combine(root, "reader.jsonl"),
        ConsumedAt = consumedAt,
    };
}
