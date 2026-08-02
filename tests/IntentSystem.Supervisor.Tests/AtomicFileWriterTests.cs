using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Supervisor.Tests;

public sealed class AtomicFileWriterTests : IDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("atomic-file-writer-tests-").FullName;

    private string QueueStatePath => Path.Combine(root, ".intent-cli", "queue-state.json");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void QueueStatePersist_InterruptedAfterTempFlush_PreservesPriorParseableContent_G579()
    {
        var priorState = State(BaseTime, QueueItemState.Queued);
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(priorState));
        var priorText = File.ReadAllText(QueueStatePath);

        var outgoingState = State(BaseTime.AddMinutes(1), QueueItemState.Active);
        using var hook = AtomicFileWriter.RegisterBeforeMoveHook(QueueStatePath, tempPath =>
        {
            Assert.Equal(Path.GetDirectoryName(QueueStatePath), Path.GetDirectoryName(tempPath));
            Assert.StartsWith(".queue-state.json.", Path.GetFileName(tempPath), StringComparison.Ordinal);
            Assert.EndsWith(".tmp", tempPath, StringComparison.Ordinal);

            var flushedState = QueueStateSerializer.Deserialize(File.ReadAllText(tempPath));
            Assert.Equal(QueueItemState.Active, Assert.Single(flushedState.Items).State);

            throw new SimulatedWriteInterruptionException();
        });

        Assert.Throws<SimulatedWriteInterruptionException>(
            () => QueueStatePersistence.Persist(QueueStatePath, priorState, outgoingState));

        Assert.Equal(priorText, File.ReadAllText(QueueStatePath));
        var survivingState = QueueStateSerializer.Deserialize(File.ReadAllText(QueueStatePath));
        Assert.Equal(QueueItemState.Queued, Assert.Single(survivingState.Items).State);
        Assert.Empty(TemporarySiblings());
    }

    [Fact]
    public void PersistRawJson_InterruptedAfterTempFlush_PreservesPriorContent_G579()
    {
        var priorState = State(BaseTime, QueueItemState.Queued);
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        var priorText = QueueStateSerializer.Serialize(priorState);
        File.WriteAllText(QueueStatePath, priorText);
        var outgoingText = QueueStateSerializer.Serialize(State(BaseTime.AddMinutes(1), QueueItemState.Review));

        using var hook = AtomicFileWriter.RegisterBeforeMoveHook(
            QueueStatePath,
            _ => throw new SimulatedWriteInterruptionException());

        Assert.Throws<SimulatedWriteInterruptionException>(
            () => QueueStatePersistence.PersistRawJson(QueueStatePath, priorText, outgoingText));

        Assert.Equal(priorText, File.ReadAllText(QueueStatePath));
        Assert.Equal(
            QueueItemState.Queued,
            Assert.Single(QueueStateSerializer.Deserialize(File.ReadAllText(QueueStatePath)).Items).State);
        Assert.Empty(TemporarySiblings());
    }

    [Fact]
    public void WriteAllText_SuccessfullyReplacesTarget_WithoutTempLitter_G579()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(QueueStatePath, "prior");

        AtomicFileWriter.WriteAllText(QueueStatePath, "replacement");

        Assert.Equal("replacement", File.ReadAllText(QueueStatePath));
        Assert.Empty(TemporarySiblings());
    }

    private string[] TemporarySiblings() =>
        Directory.GetFiles(Path.GetDirectoryName(QueueStatePath)!, ".queue-state.json.*.tmp");

    private static QueueState State(DateTimeOffset updatedAt, QueueItemState state) => new()
    {
        SchemaVersion = "1",
        UpdatedAt = updatedAt,
        Items =
        [
            new QueueItem
            {
                ExecutionUnit = "G579",
                Title = "Atomic canonical state writes",
                State = state,
                Dependencies = [],
                BlockedBy = [],
                ClarificationReturnPath = string.Empty,
                PacketPaths = new PacketPaths
                {
                    Yaml = ".intent-cli/issues/G579/packet.yaml",
                    Implementation = ".intent-cli/issues/G579/implementation.md",
                    ReviewContext = ".intent-cli/issues/G579/review-context.md",
                },
                WorkerRole = "implementation",
                ReviewRole = "review",
                Priority = "normal",
            },
        ],
    };

    private sealed class SimulatedWriteInterruptionException : Exception
    {
    }
}
