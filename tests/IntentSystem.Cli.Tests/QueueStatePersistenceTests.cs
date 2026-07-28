using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G548: the shared queue-state persistence guard. `queue-state.json` is one
/// file shared by every domain on a multi-domain host and written
/// concurrently from several checkouts, so a read-modify-write race does not
/// merely conflict — it silently erases whatever the stale in-memory copy
/// lacked.
///
/// The reproduced field incident (2026-07-23, host commit `2ab082cf`): a
/// sekiban-domain write recorded a G841 PR linkage from an hour-old base and
/// dropped the intent-cli G545 item seeded in between. Nothing errored. The
/// loss stayed invisible for four days.
/// </summary>
public sealed class QueueStatePersistenceTests : IDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("queue-state-persistence-tests-").FullName;

    private string QueueStatePath => Path.Combine(root, ".intent-cli", "queue-state.json");

    public void Dispose()
    {
        QueueStatePersistence.BeforePersistHook = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── The 2ab082cf two-writer regression ──────────────────────────────

    [Fact]
    public void Persist_TwoWritersSharingABase_BothChangesSurvive_ReproducesIncident2ab082cf_G548()
    {
        // Writer A and writer B both read the same base. A seeds a new
        // intent-cli item. B — still holding the old base — then records an
        // unrelated PR linkage on its own sekiban item. Before G548 that
        // second write erased A's item without a word.
        var sharedBase = State(BaseTime, Item("SKS-G841", QueueItemState.Active));
        WriteRaw(sharedBase);

        // Writer A: seeds G545 (an hour after the shared base was read).
        var writerASeeded = State(BaseTime.AddHours(1), sharedBase.Items[0], Item("G545", QueueItemState.Queued));
        QueueStatePersistence.Persist(QueueStatePath, sharedBase, writerASeeded);

        // Writer B: records a linkage from the STALE base — its outgoing
        // state has never heard of G545.
        var writerBLinked = State(
            BaseTime.AddHours(2),
            sharedBase.Items[0] with { LinkedPr = "https://github.com/J-Tech-Japan/sekiban/pull/841" });
        var result = QueueStatePersistence.Persist(QueueStatePath, sharedBase, writerBLinked);

        Assert.True(result.ReappliedOnFreshBase);
        Assert.Equal(["SKS-G841"], result.ReappliedExecutionUnits);

        var persisted = ReadBack();
        // Both changes survive.
        Assert.Equal(["SKS-G841", "G545"], persisted.Items.Select(item => item.ExecutionUnit).ToArray());
        Assert.Equal(
            "https://github.com/J-Tech-Japan/sekiban/pull/841",
            persisted.Items.Single(item => item.ExecutionUnit == "SKS-G841").LinkedPr);
        Assert.Equal(QueueItemState.Queued, persisted.Items.Single(item => item.ExecutionUnit == "G545").State);
        Assert.Equal(BaseTime.AddHours(2), persisted.UpdatedAt);
    }

    [Fact]
    public void Persist_StaleBaseReapplication_TouchesOnlyTheNamedItemAndUpdatedAt_G548()
    {
        // Byte-level assertion: every item the mutation did not name comes
        // through from the FRESH on-disk state completely unchanged.
        var sharedBase = State(BaseTime, Item("SKS-G841", QueueItemState.Active), Item("SKS-G850", QueueItemState.Queued));
        WriteRaw(sharedBase);

        // Another writer moves G850 forward and seeds G545 in between.
        var concurrent = State(
            BaseTime.AddMinutes(30),
            sharedBase.Items[0],
            sharedBase.Items[1] with { State = QueueItemState.Review, Priority = "high" },
            Item("G545", QueueItemState.Queued));
        WriteRaw(concurrent);

        var expectedUntouched = concurrent.Items
            .Where(item => item.ExecutionUnit != "SKS-G841")
            .Select(SerializeItem)
            .ToArray();

        var staleOutgoing = State(
            BaseTime.AddHours(1),
            sharedBase.Items[0] with { LinkedPr = "https://github.com/J-Tech-Japan/sekiban/pull/841" },
            sharedBase.Items[1]);
        var result = QueueStatePersistence.Persist(QueueStatePath, sharedBase, staleOutgoing);

        Assert.True(result.ReappliedOnFreshBase);
        Assert.Equal(["SKS-G841"], result.ReappliedExecutionUnits);

        var persisted = ReadBack();
        // The named item got exactly its own change.
        Assert.Equal(
            "https://github.com/J-Tech-Japan/sekiban/pull/841",
            persisted.Items.Single(item => item.ExecutionUnit == "SKS-G841").LinkedPr);
        // Everything else is byte-identical to the fresh state — the stale
        // copy's older view of G850 did NOT overwrite the newer one.
        Assert.Equal(
            expectedUntouched,
            persisted.Items.Where(item => item.ExecutionUnit != "SKS-G841").Select(SerializeItem).ToArray());
        Assert.Equal(BaseTime.AddHours(1), persisted.UpdatedAt);
    }

    [Fact]
    public void Persist_StaleBaseReapplication_PreservesFreshItemOrder_G548()
    {
        var sharedBase = State(BaseTime, Item("A1", QueueItemState.Queued));
        WriteRaw(sharedBase);
        WriteRaw(State(BaseTime.AddMinutes(5), Item("A1", QueueItemState.Queued), Item("B1", QueueItemState.Queued), Item("C1", QueueItemState.Queued)));

        var staleOutgoing = State(BaseTime.AddMinutes(10), sharedBase.Items[0] with { State = QueueItemState.Active });
        QueueStatePersistence.Persist(QueueStatePath, sharedBase, staleOutgoing);

        var persisted = ReadBack();
        Assert.Equal(["A1", "B1", "C1"], persisted.Items.Select(item => item.ExecutionUnit).ToArray());
        Assert.Equal(QueueItemState.Active, persisted.Items[0].State);
    }

    // ── No-item-loss invariant ──────────────────────────────────────────

    [Fact]
    public void Persist_WouldDropUnrequestedItems_AbortsLoud_NamingUnitsAndRecovery_G548()
    {
        var onDisk = State(BaseTime, Item("SKS-G841", QueueItemState.Active), Item("G545", QueueItemState.Queued), Item("G546", QueueItemState.Queued));
        WriteRaw(onDisk);
        var before = File.ReadAllText(QueueStatePath);

        // A caller whose base ALREADY matches disk but whose outgoing state
        // simply lost two items — the pure invariant case, with no staleness
        // involved.
        var outgoing = State(BaseTime.AddMinutes(1), onDisk.Items[0]);

        var exception = Assert.Throws<QueueStateItemLossException>(
            () => QueueStatePersistence.Persist(QueueStatePath, onDisk, outgoing));

        // Names the exact units, sorted, and the canonical recovery path.
        Assert.Contains("would remove 2 queue item(s) it was not asked to remove", exception.Message, StringComparison.Ordinal);
        Assert.Contains("G545, G546", exception.Message, StringComparison.Ordinal);
        Assert.Contains("queue-seed-from-packet", exception.Message, StringComparison.Ordinal);
        Assert.Contains("publish-flow", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--write-recovered-linkage", exception.Message, StringComparison.Ordinal);
        // The file is untouched.
        Assert.Equal(before, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void Persist_StaleBaseThatWouldHaveDroppedAnItem_IsRepairedRatherThanAborted_G548()
    {
        // The incident shape resolves through re-application, not through an
        // abort: the abort is the last line of defense, not the first.
        var sharedBase = State(BaseTime, Item("SKS-G841", QueueItemState.Active));
        WriteRaw(sharedBase);
        WriteRaw(State(BaseTime.AddMinutes(30), sharedBase.Items[0], Item("G545", QueueItemState.Queued)));

        var staleOutgoing = State(BaseTime.AddHours(1), sharedBase.Items[0] with { State = QueueItemState.Review });
        var result = QueueStatePersistence.Persist(QueueStatePath, sharedBase, staleOutgoing);

        Assert.True(result.ReappliedOnFreshBase);
        Assert.Equal(["SKS-G841", "G545"], ReadBack().Items.Select(item => item.ExecutionUnit).ToArray());
    }

    [Fact]
    public void Persist_UnreadableOnDiskState_AbortsRatherThanOverwriting_G548()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(QueueStatePath, "{ this is not queue state");
        var before = File.ReadAllText(QueueStatePath);

        var exception = Assert.Throws<QueueStateItemLossException>(
            () => QueueStatePersistence.Persist(QueueStatePath, State(BaseTime), State(BaseTime, Item("G545", QueueItemState.Queued))));

        Assert.Contains("could not be read", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be checked for item loss", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(QueueStatePath));
    }

    // ── Explicit removals stay legitimate ───────────────────────────────

    [Fact]
    public void Persist_ExplicitlyRequestedRemoval_IsAllowed_G548()
    {
        var onDisk = State(BaseTime, Item("SKS-G841", QueueItemState.Active), Item("G545", QueueItemState.Queued));
        WriteRaw(onDisk);

        var outgoing = State(BaseTime.AddMinutes(1), onDisk.Items[0]);
        var result = QueueStatePersistence.Persist(QueueStatePath, onDisk, outgoing, expectedRemovals: ["G545"]);

        Assert.False(result.ReappliedOnFreshBase);
        Assert.Equal(["SKS-G841"], ReadBack().Items.Select(item => item.ExecutionUnit).ToArray());
    }

    [Fact]
    public void Persist_ExplicitRemovalAllowListDoesNotExcuseOtherLosses_G548()
    {
        var onDisk = State(BaseTime, Item("SKS-G841", QueueItemState.Active), Item("G545", QueueItemState.Queued), Item("G546", QueueItemState.Queued));
        WriteRaw(onDisk);

        var outgoing = State(BaseTime.AddMinutes(1), onDisk.Items[0]);

        var exception = Assert.Throws<QueueStateItemLossException>(
            () => QueueStatePersistence.Persist(QueueStatePath, onDisk, outgoing, expectedRemovals: ["G545"]));

        Assert.Contains("would remove 1 queue item(s)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("G546", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("— G545,", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Persist_RetireStyleRewriteKeepsTheItem_AndIsNeverTreatedAsLoss_G548()
    {
        // G525 retire marks an item state=retired; it does not remove it, so
        // it needs no allow-list entry and must not trip the invariant.
        var onDisk = State(BaseTime, Item("G540", QueueItemState.Queued), Item("G545", QueueItemState.Queued));
        WriteRaw(onDisk);

        var outgoing = State(
            BaseTime.AddMinutes(1),
            onDisk.Items[0] with { State = QueueItemState.Retired, RetirementReason = "superseded" },
            onDisk.Items[1]);
        QueueStatePersistence.Persist(QueueStatePath, onDisk, outgoing);

        var persisted = ReadBack();
        Assert.Equal(QueueItemState.Retired, persisted.Items.Single(item => item.ExecutionUnit == "G540").State);
        Assert.Equal("superseded", persisted.Items.Single(item => item.ExecutionUnit == "G540").RetirementReason);
        Assert.Equal(2, persisted.Items.Count);
    }

    [Fact]
    public void Persist_ExplicitRemovalOnAStaleBase_StillRemovesOnlyThatUnit_G548()
    {
        var sharedBase = State(BaseTime, Item("G540", QueueItemState.Queued), Item("SKS-G841", QueueItemState.Active));
        WriteRaw(sharedBase);
        WriteRaw(State(BaseTime.AddMinutes(20), sharedBase.Items[0], sharedBase.Items[1], Item("G545", QueueItemState.Queued)));

        var staleOutgoing = State(BaseTime.AddMinutes(40), sharedBase.Items[1]);
        var result = QueueStatePersistence.Persist(QueueStatePath, sharedBase, staleOutgoing, expectedRemovals: ["G540"]);

        Assert.True(result.ReappliedOnFreshBase);
        Assert.Equal(["G540"], result.ReappliedExecutionUnits);
        // G540 removed as asked; the concurrently-seeded G545 survives.
        Assert.Equal(["SKS-G841", "G545"], ReadBack().Items.Select(item => item.ExecutionUnit).ToArray());
    }

    // ── Ordinary, non-racing writes ─────────────────────────────────────

    [Fact]
    public void Persist_CleanBase_WritesDirectlyWithoutReapplication_G548()
    {
        var onDisk = State(BaseTime, Item("G545", QueueItemState.Queued));
        WriteRaw(onDisk);

        var outgoing = State(BaseTime.AddMinutes(1), onDisk.Items[0] with { State = QueueItemState.Active });
        var result = QueueStatePersistence.Persist(QueueStatePath, onDisk, outgoing);

        Assert.False(result.ReappliedOnFreshBase);
        Assert.Empty(result.ReappliedExecutionUnits);
        Assert.Equal(QueueItemState.Active, ReadBack().Items.Single().State);
    }

    [Fact]
    public void Persist_MissingFile_WritesTheFirstStateAndCreatesTheDirectory_G548()
    {
        Assert.False(File.Exists(QueueStatePath));

        var outgoing = State(BaseTime, Item("G545", QueueItemState.Queued));
        var result = QueueStatePersistence.Persist(QueueStatePath, State(BaseTime), outgoing);

        Assert.False(result.ReappliedOnFreshBase);
        Assert.True(File.Exists(QueueStatePath));
        Assert.Equal(["G545"], ReadBack().Items.Select(item => item.ExecutionUnit).ToArray());
    }

    [Fact]
    public void Persist_AddingAnItemOnAStaleBase_AppendsWithoutDisturbingConcurrentAdds_G548()
    {
        var sharedBase = State(BaseTime, Item("SKS-G841", QueueItemState.Active));
        WriteRaw(sharedBase);
        WriteRaw(State(BaseTime.AddMinutes(10), sharedBase.Items[0], Item("G545", QueueItemState.Queued)));

        var staleOutgoing = State(BaseTime.AddMinutes(20), sharedBase.Items[0], Item("G546", QueueItemState.Queued));
        var result = QueueStatePersistence.Persist(QueueStatePath, sharedBase, staleOutgoing);

        Assert.True(result.ReappliedOnFreshBase);
        Assert.Equal(["G546"], result.ReappliedExecutionUnits);
        Assert.Equal(["SKS-G841", "G545", "G546"], ReadBack().Items.Select(item => item.ExecutionUnit).ToArray());
    }

    [Fact]
    public void Persist_FormattingOnlyDifference_IsNotMistakenForAConcurrentWrite_G548()
    {
        // A file written by an older serializer (different key order /
        // indentation) must not read as "someone else wrote this".
        var onDisk = State(BaseTime, Item("G545", QueueItemState.Queued));
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(
            QueueStatePath,
            QueueStateSerializer.Serialize(onDisk).Replace("\n", "\r\n", StringComparison.Ordinal) + "\n");

        var outgoing = State(BaseTime.AddMinutes(1), onDisk.Items[0] with { State = QueueItemState.Active });
        var result = QueueStatePersistence.Persist(QueueStatePath, onDisk, outgoing);

        Assert.False(result.ReappliedOnFreshBase);
        Assert.Equal(QueueItemState.Active, ReadBack().Items.Single().State);
    }

    // ── Command-level: re-application is observable in output ───────────

    [Fact]
    public void QueueTransitionCommand_OnAStaleBase_ReportsTheReapplicationAndNamesTheUnit_G548()
    {
        // The contract requires writer B to RECORD its re-application in its
        // own output — a writer that silently repairs itself teaches an
        // operator nothing about the contention that caused it.
        //
        // The interleaving is produced with the guard's BeforePersistHook: it
        // fires after the command has read queue-state and before the guard
        // reads it again, which is precisely the window a concurrent
        // canonical write occupies in the field.
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new Models.CliConfig
            {
                Project = new Models.ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
            },
        };

        WriteRaw(State(BaseTime, Item("SKS-G841", QueueItemState.Active)));

        var concurrentWriteDone = false;
        QueueStatePersistence.BeforePersistHook = _ =>
        {
            if (concurrentWriteDone)
            {
                return;
            }

            concurrentWriteDone = true;
            // Another domain's loop seeds G545 in the window.
            WriteRaw(State(BaseTime.AddMinutes(10), Item("SKS-G841", QueueItemState.Active), Item("G545", QueueItemState.Queued)));
        };

        try
        {
            using var writer = new StringWriter();
            var exitCode = QueueTransitionCommand.Execute(context, ["SKS-G841", "completed"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Transitioned SKS-G841 to completed.", output, StringComparison.Ordinal);
            // Observable, and it names the re-applied execution unit.
            Assert.Contains("queue-state changed after it was read", output, StringComparison.Ordinal);
            Assert.Contains("re-applied to the current state for SKS-G841", output, StringComparison.Ordinal);
            Assert.Contains("no other item was modified", output, StringComparison.Ordinal);

            var persisted = ReadBack();
            Assert.Equal(["SKS-G841", "G545"], persisted.Items.Select(item => item.ExecutionUnit).ToArray());
            Assert.Equal(QueueItemState.Completed, persisted.Items.Single(item => item.ExecutionUnit == "SKS-G841").State);
            Assert.Equal(QueueItemState.Queued, persisted.Items.Single(item => item.ExecutionUnit == "G545").State);
        }
        finally
        {
            QueueStatePersistence.BeforePersistHook = null;
        }
    }

    [Fact]
    public void QueueTransitionCommand_OnACleanBase_SaysNothingAboutReapplication_G548()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new Models.CliConfig
            {
                Project = new Models.ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
            },
        };

        WriteRaw(State(BaseTime, Item("SKS-G841", QueueItemState.Active)));

        using var writer = new StringWriter();
        var exitCode = QueueTransitionCommand.Execute(context, ["SKS-G841", "completed"], writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("re-applied", writer.ToString(), StringComparison.Ordinal);
    }

    // ── Raw-JSON writer guard (metadata update) ─────────────────────────

    [Fact]
    public void PersistRawJson_CleanBase_WritesTheCallersTextVerbatim_G548()
    {
        // The bounded controlled metadata writer must keep its raw-text
        // fidelity on the ordinary path.
        var onDisk = State(BaseTime, Item("G545", QueueItemState.Queued));
        WriteRaw(onDisk);
        var baseText = File.ReadAllText(QueueStatePath);
        var outgoingText = baseText.Replace("\"queued\"", "\"completed\"", StringComparison.Ordinal);

        var result = QueueStatePersistence.PersistRawJson(QueueStatePath, baseText, outgoingText);

        Assert.False(result.ReappliedOnFreshBase);
        Assert.Equal(outgoingText, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void PersistRawJson_WouldDropAnUnrequestedItem_AbortsLoud_G548()
    {
        var onDisk = State(BaseTime, Item("SKS-G841", QueueItemState.Active), Item("G545", QueueItemState.Queued));
        WriteRaw(onDisk);
        var baseText = File.ReadAllText(QueueStatePath);
        var outgoingText = QueueStateSerializer.Serialize(State(BaseTime.AddMinutes(1), onDisk.Items[0]));

        var exception = Assert.Throws<QueueStateItemLossException>(
            () => QueueStatePersistence.PersistRawJson(QueueStatePath, baseText, outgoingText));

        Assert.Contains("G545", exception.Message, StringComparison.Ordinal);
        Assert.Equal(baseText, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void PersistRawJson_StaleBase_FallsBackToTheModelGuardAndReapplies_G548()
    {
        var sharedBase = State(BaseTime, Item("SKS-G841", QueueItemState.Active));
        WriteRaw(sharedBase);
        var baseText = File.ReadAllText(QueueStatePath);

        // Concurrent seed of another domain's item.
        WriteRaw(State(BaseTime.AddMinutes(10), sharedBase.Items[0], Item("G545", QueueItemState.Queued)));

        var outgoingText = QueueStateSerializer.Serialize(
            State(BaseTime.AddMinutes(20), sharedBase.Items[0] with { State = QueueItemState.Completed }));

        var result = QueueStatePersistence.PersistRawJson(QueueStatePath, baseText, outgoingText);

        Assert.True(result.ReappliedOnFreshBase);
        Assert.Equal(["SKS-G841"], result.ReappliedExecutionUnits);
        var persisted = ReadBack();
        Assert.Equal(["SKS-G841", "G545"], persisted.Items.Select(item => item.ExecutionUnit).ToArray());
        Assert.Equal(QueueItemState.Completed, persisted.Items.Single(item => item.ExecutionUnit == "SKS-G841").State);
    }

    [Fact]
    public void PersistRawJson_PartialDocument_IsCheckedOnIdentityAlone_NotTheModelContract_G548()
    {
        // The bounded metadata writer operates on queue documents that need
        // not satisfy the full QueueItem contract — that is precisely why it
        // works on raw text. The guard must check identity out of the JSON,
        // never by deserializing, or it would reject the very files this
        // writer exists to handle.
        const string partial = """
            {"schema_version":"1","updated_at":"2026-07-23T09:00:00+00:00","items":[
              {"execution_unit":"SKS-G841","state":"active"},
              {"execution_unit":"G545","state":"queued"}
            ]}
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(QueueStatePath, partial);

        var outgoing = partial.Replace("\"active\"", "\"completed\"", StringComparison.Ordinal);
        var result = QueueStatePersistence.PersistRawJson(QueueStatePath, partial, outgoing);

        Assert.False(result.ReappliedOnFreshBase);
        Assert.Null(result.PersistedState);
        Assert.Equal(outgoing, File.ReadAllText(QueueStatePath));

        // ...and losing an item from that same partial document still aborts.
        var lossy = """
            {"schema_version":"1","updated_at":"2026-07-23T09:05:00+00:00","items":[
              {"execution_unit":"SKS-G841","state":"completed"}
            ]}
            """;
        var exception = Assert.Throws<QueueStateItemLossException>(
            () => QueueStatePersistence.PersistRawJson(QueueStatePath, outgoing, lossy));
        Assert.Contains("G545", exception.Message, StringComparison.Ordinal);
        Assert.Equal(outgoing, File.ReadAllText(QueueStatePath));
    }

    [Fact]
    public void PersistRawJson_StaleBaseOnAPartialDocument_AbortsLoudRatherThanNormalizing_G548()
    {
        // Re-application needs the model. When the document cannot round-trip
        // through it, the raw writer refuses — it will not normalize a file it
        // exists not to normalize, and it will not overwrite a change it
        // cannot see. Nothing is written either way.
        const string partialBase = """
            {"schema_version":"1","updated_at":"2026-07-23T09:00:00+00:00","items":[
              {"execution_unit":"SKS-G841","state":"active"}
            ]}
            """;
        const string concurrent = """
            {"schema_version":"1","updated_at":"2026-07-23T09:10:00+00:00","items":[
              {"execution_unit":"SKS-G841","state":"active"},
              {"execution_unit":"G545","state":"queued"}
            ]}
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(QueueStatePath, concurrent);

        var outgoing = partialBase.Replace("\"active\"", "\"completed\"", StringComparison.Ordinal);

        var exception = Assert.Throws<QueueStateItemLossException>(
            () => QueueStatePersistence.PersistRawJson(QueueStatePath, partialBase, outgoing));

        Assert.Contains("the file changed after it was read", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Re-run this command against the current state", exception.Message, StringComparison.Ordinal);
        Assert.Equal(concurrent, File.ReadAllText(QueueStatePath));
    }

    // ── Delta derivation ────────────────────────────────────────────────

    [Fact]
    public void Delta_CapturesOnlyTheUnitsTheMutationActuallyChanged_G548()
    {
        var baseState = State(BaseTime, Item("A1", QueueItemState.Queued), Item("B1", QueueItemState.Queued), Item("C1", QueueItemState.Queued));
        var outgoing = State(
            BaseTime.AddMinutes(1),
            baseState.Items[0],
            baseState.Items[1] with { State = QueueItemState.Active },
            Item("D1", QueueItemState.Queued));

        var delta = QueueStateItemDelta.Between(baseState, outgoing);

        Assert.Equal(["B1", "D1"], delta.Upserts.Select(item => item.ExecutionUnit).ToArray());
        Assert.Equal(["C1"], delta.Removals);
        Assert.Equal(["B1", "D1", "C1"], delta.TouchedExecutionUnits);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string SerializeItem(QueueItem item) =>
        QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Items = [item],
        });

    private void WriteRaw(QueueState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(state));
    }

    private QueueState ReadBack() => QueueStateSerializer.Deserialize(File.ReadAllText(QueueStatePath));

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
        Dependencies = Array.Empty<string>(),
        BlockedBy = Array.Empty<string>(),
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
