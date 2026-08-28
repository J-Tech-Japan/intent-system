using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class QueueStatePersistenceG746RepairTests : IDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("queue-state-g746-repair-tests-").FullName;

    private string QueueStatePath => Path.Combine(root, ".intent-cli", "queue-state.json");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Persist_CleanOutgoingDuplicate_FailsClosedAndLeavesTheFileUnchanged_G746()
    {
        var onDisk = State(BaseTime, Item("G746", QueueItemState.Queued));
        WriteRaw(onDisk);
        var before = File.ReadAllText(QueueStatePath);
        var duplicateOutgoing = State(
            BaseTime.AddMinutes(1),
            onDisk.Items[0],
            onDisk.Items[0] with { Title = "duplicate G746 row" });

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => QueueStatePersistence.Persist(QueueStatePath, onDisk, duplicateOutgoing));

        AssertDuplicateDiagnostic(exception, "QueueStatePersistence.Persist outgoing state");
        Assert.Equal(before, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void Persist_StaleOnDiskDuplicate_FailsClosedBeforeAnyReapplication_G746()
    {
        var baseState = State(BaseTime, Item("G746", QueueItemState.Queued));
        WriteRaw(baseState);
        var duplicateOnDisk = State(
            BaseTime.AddMinutes(1),
            baseState.Items[0],
            baseState.Items[0] with { Title = "duplicate G746 row" });
        WriteRaw(duplicateOnDisk);
        var before = File.ReadAllText(QueueStatePath);
        var outgoing = State(
            BaseTime.AddMinutes(2),
            baseState.Items[0] with { State = QueueItemState.Active });

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => QueueStatePersistence.Persist(QueueStatePath, baseState, outgoing));

        AssertDuplicateDiagnostic(exception, "QueueStateItemDelta.ApplyTo fresh state");
        Assert.Equal(before, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void Delta_Between_DuplicateBaseState_FailsClosedWithFullEntries_G746()
    {
        var item = Item("G746", QueueItemState.Queued);
        var duplicateBase = State(BaseTime, item, item with { Title = "second duplicate" });
        var outgoing = State(BaseTime.AddMinutes(1), item);

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => QueueStateItemDelta.Between(duplicateBase, outgoing));

        AssertDuplicateDiagnostic(exception, "QueueStateItemDelta.Between base state");
    }

    [Fact]
    public void Delta_Between_DuplicateOutgoingState_FailsClosedBeforeDerivingTheDelta_G746()
    {
        var item = Item("G746", QueueItemState.Queued);
        var baseState = State(BaseTime, item);
        var duplicateOutgoing = State(BaseTime.AddMinutes(1), item, item with { Title = "second duplicate" });

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => QueueStateItemDelta.Between(baseState, duplicateOutgoing));

        AssertDuplicateDiagnostic(exception, "QueueStateItemDelta.Between outgoing state");
    }

    [Fact]
    public void Delta_ApplyTo_DuplicateFreshState_FailsClosedWithoutReturningAMutatedState_G746()
    {
        var item = Item("G746", QueueItemState.Queued);
        var duplicateFresh = State(BaseTime, item, item with { Title = "second duplicate" });
        var delta = new QueueStateItemDelta
        {
            Upserts = [item with { State = QueueItemState.Active }],
            Removals = [],
        };

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => delta.ApplyTo(duplicateFresh, BaseTime.AddMinutes(1)));

        AssertDuplicateDiagnostic(exception, "QueueStateItemDelta.ApplyTo fresh state");
    }

    [Fact]
    public void Delta_ApplyTo_DuplicateUpserts_FailsClosedBeforeApplyingAnyChange_G746()
    {
        var item = Item("G746", QueueItemState.Queued);
        var freshState = State(BaseTime, item);
        var delta = new QueueStateItemDelta
        {
            Upserts = [
                item with { State = QueueItemState.Active },
                item with { State = QueueItemState.Review, Title = "second duplicate" },
            ],
            Removals = [],
        };

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => delta.ApplyTo(freshState, BaseTime.AddMinutes(1)));

        AssertDuplicateDiagnostic(exception, "QueueStateItemDelta.ApplyTo upserts");
    }

    [Fact]
    public void PersistRawJson_StaleDuplicateBase_FailsClosedBeforeRawMapOverwrite_G746()
    {
        var item = Item("G746", QueueItemState.Queued);
        var onDisk = State(BaseTime, item);
        WriteRaw(onDisk);
        var before = File.ReadAllText(QueueStatePath);
        var duplicateBase = State(BaseTime, item, item with { Title = "second duplicate" });
        var outgoing = State(BaseTime.AddMinutes(1), item with { State = QueueItemState.Active });

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => QueueStatePersistence.PersistRawJson(
                QueueStatePath,
                QueueStateSerializer.Serialize(duplicateBase),
                QueueStateSerializer.Serialize(outgoing)));

        AssertDuplicateDiagnostic(exception, "QueueStatePersistence raw stale delta");
        Assert.Equal(before, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void PersistRawJson_CurrentDuplicate_FailsClosedBeforeComparingOrWriting_G746()
    {
        var item = Item("G746", QueueItemState.Queued);
        var duplicateOnDisk = State(BaseTime, item, item with { Title = "second duplicate" });
        WriteRaw(duplicateOnDisk);
        var before = File.ReadAllText(QueueStatePath);
        var unique = State(BaseTime, item);

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => QueueStatePersistence.PersistRawJson(
                QueueStatePath,
                QueueStateSerializer.Serialize(unique),
                QueueStateSerializer.Serialize(unique with { UpdatedAt = BaseTime.AddMinutes(1) })));

        AssertDuplicateDiagnostic(exception, "QueueStatePersistence.PersistRawJson current on-disk state");
        Assert.Equal(before, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void PersistRawJson_CleanOutgoingDuplicate_FailsClosedAndPreservesVerbatimState_G746()
    {
        var item = Item("G746", QueueItemState.Queued);
        var onDisk = State(BaseTime, item);
        WriteRaw(onDisk);
        var before = File.ReadAllText(QueueStatePath);
        var duplicateOutgoing = State(
            BaseTime.AddMinutes(1),
            item,
            item with { Title = "second duplicate" });

        var exception = Assert.Throws<QueueStateDuplicateExecutionUnitException>(
            () => QueueStatePersistence.PersistRawJson(
                QueueStatePath,
                QueueStateSerializer.Serialize(onDisk),
                QueueStateSerializer.Serialize(duplicateOutgoing)));

        AssertDuplicateDiagnostic(exception, "QueueStatePersistence.PersistRawJson outgoing state");
        Assert.Equal(before, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void Delta_ApplyTo_UniqueStateStillPreservesDuplicateFreeBehavior_G746()
    {
        var freshState = State(BaseTime, Item("G745", QueueItemState.Queued));
        var delta = new QueueStateItemDelta
        {
            Upserts = [Item("G746", QueueItemState.Active)],
            Removals = [],
        };

        var applied = delta.ApplyTo(freshState, BaseTime.AddMinutes(1));

        Assert.Equal(["G745", "G746"], applied.Items.Select(item => item.ExecutionUnit).ToArray());
        Assert.Equal(QueueItemState.Active, applied.Items.Single(item => item.ExecutionUnit == "G746").State);
    }

    private static void AssertDuplicateDiagnostic(Exception exception, string operation)
    {
        Assert.Contains("duplicate-queue-item", exception.Message, StringComparison.Ordinal);
        Assert.Contains("execution_unit entries", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'G746'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("entry[0]", exception.Message, StringComparison.Ordinal);
        Assert.Contains("entry[1]", exception.Message, StringComparison.Ordinal);
        Assert.Contains(operation, exception.Message, StringComparison.Ordinal);
        Assert.Contains("no mutation was performed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("same key has already been added", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentException", exception.Message, StringComparison.Ordinal);
    }

    private void WriteRaw(QueueState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(state));
    }

    private static QueueState State(DateTimeOffset updatedAt, params QueueItem[] items) => new()
    {
        SchemaVersion = "1",
        UpdatedAt = updatedAt,
        Items = items,
    };

    private static QueueItem Item(string executionUnit, QueueItemState state) => new()
    {
        ExecutionUnit = executionUnit,
        Title = $"{executionUnit} title",
        State = state,
        Dependencies = [],
        BlockedBy = [],
        ClarificationReturnPath = string.Empty,
        PacketPaths = new PacketPaths
        {
            Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
            Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
            ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
        },
        WorkerRole = "Claude",
        ReviewRole = "Codex",
        Priority = "normal",
    };
}
