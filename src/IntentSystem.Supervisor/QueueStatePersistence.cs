using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Supervisor;

/// <summary>
/// G548: the single shared persistence layer every canonical
/// <c>queue-state.json</c> mutation writes through.
///
/// It lives in <c>IntentSystem.Supervisor</c> — the assembly that owns the
/// queue-state model and serializer, and the only one BOTH
/// <c>IntentSystem.Cli</c> and <c>IntentSystem.Drift</c> reference — so
/// "every canonical writer" can mean every writer in the solution, not just
/// the ones that happen to live in the CLI (G548 round 2: the drift
/// service's corrective enqueue and the bounded metadata writer were
/// reachable loss paths precisely because the guard had been placed one
/// layer too high).
///
/// <c>queue-state.json</c> is ONE file shared by every domain on a
/// multi-domain host, written concurrently by several loops from different
/// checkouts. Every canonical writer deserializes the whole file, mutates in
/// memory, and reserializes the whole file — so a read-modify-write race
/// does not merely conflict, it SILENTLY ERASES whatever the stale in-memory
/// copy happened not to contain.
///
/// Field incident, 2026-07-23 (host commit <c>2ab082cf</c>): a
/// sekiban-domain write recorded a G841 PR linkage from a base read an hour
/// earlier and dropped the intent-cli G545 queue item seeded in between.
/// Nothing errored; the commit message claimed only the linkage change. The
/// loss stayed invisible for four days, then surfaced as <c>closeout-plan
/// host-metadata-blocked</c> and combined with the <c>pr-is-draft</c>
/// recovery gate into a circular deadlock. Restoration took three canonical
/// surfaces plus an operator (host commit <c>c0897649</c>).
///
/// Three guarantees, all enforced here rather than re-implemented per
/// command:
/// <list type="number">
/// <item><b>Stale-base detection and re-application.</b> The state the caller
///   READ is compared against what is on disk NOW, at persist time. On a
///   mismatch the caller's own mutation — derived as an item-level delta
///   between its base and its outgoing state — is re-applied to the FRESH
///   state instead of persisting the stale copy. The re-application is
///   reported back so callers can surface it.</item>
/// <item><b>No-item-loss invariant.</b> Any execution unit present on disk
///   but missing from the outgoing state, and not named as an expected
///   removal, aborts the write loudly — naming the exact units and the
///   canonical recovery surfaces. Explicit removals (retire, completed
///   lifecycle) stay legitimate; the invariant targets UNREQUESTED loss
///   only.</item>
/// <item><b>Item-scoped re-application.</b> A re-applied mutation touches
///   only the units its delta actually covers, plus <c>updated_at</c>. Every
///   unrelated item is carried through byte-identically from the fresh
///   on-disk state.</item>
/// </list>
///
/// Deliberately NOT in this layer (out of scope, and recorded as such): a
/// per-domain queue-file split, file-locking daemons, cross-process mutexes,
/// and git-level merge strategy. The 2ab082cf loss happened inside a
/// fast-forward-clean history, so the defense has to sit at the writer,
/// before anything reaches a commit.
/// </summary>
public static class QueueStatePersistence
{
    /// <summary>
    /// G548: the canonical restoration path, proven end-to-end on
    /// 2026-07-27 (host commit <c>c0897649</c>). Named verbatim in the
    /// abort message so an operator hitting the invariant is never left
    /// guessing how to recover the state it just refused to overwrite.
    /// </summary>
    public const string RecoverySurfaces =
        "recover with: (1) `intent-cli automation queue-seed-from-packet <unit> --write` to re-seed a lost "
        + "item from its packet, (2) `intent-cli issue publish-flow <unit> --repo <owner/repo> --write` "
        + "(idempotent rerun) to restore its publish linkage, then (3) `intent-cli review closeout-plan "
        + "--pr <n> --repo <owner/repo> --write-recovered-linkage` to recover PR linkage from GitHub "
        + "closing references";

    /// <summary>
    /// G548 round 2: test seam invoked immediately before the guard reads the
    /// current on-disk state, with the target path. It exists so a
    /// COMMAND-level fixture can make the file move between a command's own
    /// read and its persist — the one interleaving a single-process test
    /// cannot otherwise produce, and the exact interleaving this guard is
    /// built for. Never set in production.
    /// </summary>
    public static Action<string>? BeforePersistHook { get; set; }

    /// <summary>
    /// G548 round 2: guarded write for the ONE canonical writer that mutates
    /// queue-state as raw JSON text rather than through the model —
    /// <c>metadata update</c>, deliberately a bounded controlled writer that
    /// preserves any field it does not own.
    ///
    /// On the ordinary path (the file has not moved since the caller read it)
    /// the caller's own text is written VERBATIM, so that bounded-writer
    /// property is fully preserved; the invariant is still enforced by
    /// deserializing both sides purely to compare item sets. Only when a
    /// concurrent write is actually detected does it fall back to the model
    /// round-trip used to re-apply the delta — which is exactly what every
    /// other canonical writer does unconditionally, so the rare stale path is
    /// no less faithful than the rest of the system.
    /// </summary>
    public static QueueStatePersistResult PersistRawJson(
        string queueStatePath,
        string baseRawText,
        string outgoingRawText,
        IReadOnlyCollection<string>? expectedRemovals = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueStatePath);
        ArgumentNullException.ThrowIfNull(baseRawText);
        ArgumentNullException.ThrowIfNull(outgoingRawText);

        BeforePersistHook?.Invoke(queueStatePath);

        if (!File.Exists(queueStatePath))
        {
            WriteText(queueStatePath, outgoingRawText);
            return QueueStatePersistResult.DirectWrite(null);
        }

        var onDiskRawText = File.ReadAllText(queueStatePath);

        if (!RawJsonMatches(onDiskRawText, baseRawText))
        {
            // Concurrent write detected. Re-applying an item-level delta needs
            // the model, so try that; if this file cannot round-trip through
            // the model (the very reason this writer works on raw text), abort
            // loud and repairable rather than normalizing a file this writer
            // exists NOT to normalize, or overwriting a change it cannot see.
            try
            {
                return PersistCore(
                    queueStatePath,
                    QueueStateSerializer.Deserialize(baseRawText),
                    QueueStateSerializer.Deserialize(outgoingRawText),
                    expectedRemovals,
                    skipHook: true);
            }
            catch (Exception exception) when (exception is not QueueStateItemLossException
                && exception is InvalidOperationException or System.Text.Json.JsonException)
            {
                throw new QueueStateItemLossException(
                    $"refusing to persist queue-state to {queueStatePath}: the file changed after it was read (a "
                    + "concurrent canonical write), and this bounded raw-text writer cannot re-apply its change onto "
                    + $"the new state ({exception.Message}). Nothing was written. Re-run this command against the "
                    + "current state.");
            }
        }

        // Clean base: enforce the invariant on execution units only — read
        // straight out of the JSON, never through the model, so a partial or
        // legacy queue file is checked exactly like any other instead of being
        // rejected by a contract this writer deliberately does not impose.
        var removalAllowList = new HashSet<string>(expectedRemovals ?? Array.Empty<string>(), StringComparer.Ordinal);
        var lost = ReadExecutionUnits(onDiskRawText)
            .Where(unit => !ReadExecutionUnits(outgoingRawText).Contains(unit) && !removalAllowList.Contains(unit))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(unit => unit, StringComparer.Ordinal)
            .ToArray();

        if (lost.Length > 0)
        {
            throw new QueueStateItemLossException(
                $"refusing to persist queue-state to {queueStatePath}: this write would remove {lost.Length} queue "
                + $"item(s) it was not asked to remove — {string.Join(", ", lost)}. This is the 2026-07-23 lost-update "
                + "shape (a write from a stale base silently erasing another domain's item); the write was aborted and "
                + $"the file is unchanged. Re-run this command against current state, or if an item is already lost, "
                + $"{RecoverySurfaces}.");
        }

        // The caller's own text is written VERBATIM, preserving the
        // bounded-writer property: no field this writer does not own is
        // touched, and nothing is normalized on its behalf.
        WriteText(queueStatePath, outgoingRawText);
        return QueueStatePersistResult.DirectWrite(null);
    }

    /// <summary>
    /// Structural comparison of two queue-state documents without the model,
    /// so whitespace/formatting differences never read as a concurrent write
    /// and a partial document never throws.
    /// </summary>
    private static bool RawJsonMatches(string left, string right)
    {
        try
        {
            using var leftDoc = System.Text.Json.JsonDocument.Parse(left);
            using var rightDoc = System.Text.Json.JsonDocument.Parse(right);
            return string.Equals(
                System.Text.Json.JsonSerializer.Serialize(leftDoc.RootElement),
                System.Text.Json.JsonSerializer.Serialize(rightDoc.RootElement),
                StringComparison.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            // Unparseable on either side: treat as different, so the caller
            // takes the loud path rather than overwriting blind.
            return false;
        }
    }

    /// <summary>
    /// Extracts <c>items[].execution_unit</c> directly from the JSON. The
    /// no-item-loss invariant only needs identity, and identity is the one
    /// field every queue document has regardless of how complete it is.
    /// </summary>
    private static IReadOnlyCollection<string> ReadExecutionUnits(string rawText)
    {
        var units = new List<string>();
        using var document = System.Text.Json.JsonDocument.Parse(rawText);
        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return units;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == System.Text.Json.JsonValueKind.Object
                && item.TryGetProperty("execution_unit", out var unit)
                && unit.ValueKind == System.Text.Json.JsonValueKind.String
                && unit.GetString() is { Length: > 0 } value)
            {
                units.Add(value);
            }
        }

        return units;
    }

    private static void WriteText(string queueStatePath, string text)
    {
        var directory = Path.GetDirectoryName(queueStatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(queueStatePath, text);
    }

    /// <summary>
    /// Persists <paramref name="outgoingState"/>, enforcing every guarantee
    /// described on this class.
    /// </summary>
    /// <param name="queueStatePath">Target <c>queue-state.json</c>.</param>
    /// <param name="baseState">
    /// The state the caller READ before mutating. Used both to derive the
    /// caller's item-level delta and to detect that the file has moved on
    /// since. Pass the deserialized state exactly as read — not a copy the
    /// caller has already mutated.
    /// </param>
    /// <param name="outgoingState">The mutated state the caller wants written.</param>
    /// <param name="expectedRemovals">
    /// Execution units this operation was explicitly asked to remove (retire,
    /// completed-item lifecycle, …). Anything else that disappears aborts.
    /// </param>
    /// <exception cref="QueueStateItemLossException">
    /// The write would drop an unrequested item.
    /// </exception>
    public static QueueStatePersistResult Persist(
        string queueStatePath,
        QueueState baseState,
        QueueState outgoingState,
        IReadOnlyCollection<string>? expectedRemovals = null) =>
        PersistCore(queueStatePath, baseState, outgoingState, expectedRemovals, skipHook: false);

    private static QueueStatePersistResult PersistCore(
        string queueStatePath,
        QueueState baseState,
        QueueState outgoingState,
        IReadOnlyCollection<string>? expectedRemovals,
        bool skipHook)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueStatePath);
        ArgumentNullException.ThrowIfNull(baseState);
        ArgumentNullException.ThrowIfNull(outgoingState);

        if (!skipHook)
        {
            BeforePersistHook?.Invoke(queueStatePath);
        }

        var removalAllowList = new HashSet<string>(expectedRemovals ?? Array.Empty<string>(), StringComparer.Ordinal);

        // The file may legitimately not exist yet (first write of a fresh
        // host). There is nothing to lose and nothing to be stale against.
        if (!File.Exists(queueStatePath))
        {
            Write(queueStatePath, outgoingState);
            return QueueStatePersistResult.DirectWrite(outgoingState);
        }

        QueueState onDiskState;
        try
        {
            onDiskState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.Text.Json.JsonException)
        {
            // An unreadable on-disk state cannot be diffed, so neither
            // guarantee can be established. Failing loud is the only safe
            // answer: silently overwriting is exactly the class of harm this
            // layer exists to prevent.
            throw new QueueStateItemLossException(
                $"refusing to persist queue-state to {queueStatePath}: the current on-disk state could not be read "
                + $"({exception.Message}), so this write cannot be checked for item loss. Repair or restore the file "
                + $"first — {RecoverySurfaces}.");
        }

        var stale = !StatesMatch(onDiskState, baseState);
        var finalState = outgoingState;
        IReadOnlyList<string> reappliedUnits = Array.Empty<string>();

        if (stale)
        {
            var delta = QueueStateItemDelta.Between(baseState, outgoingState);
            finalState = delta.ApplyTo(onDiskState, outgoingState.UpdatedAt);
            reappliedUnits = delta.TouchedExecutionUnits;

            AssertItemScoped(onDiskState, finalState, delta.TouchedExecutionUnits, queueStatePath);
        }

        AssertNoUnrequestedLoss(onDiskState, finalState, removalAllowList, queueStatePath);

        Write(queueStatePath, finalState);

        return stale
            ? QueueStatePersistResult.Reapplied(finalState, reappliedUnits)
            : QueueStatePersistResult.DirectWrite(finalState);
    }

    /// <summary>
    /// Compares two states through the SAME serializer round-trip, so pure
    /// formatting drift (a legacy file written before a serializer
    /// normalization, for instance) is never mistaken for a concurrent
    /// write. Only genuine content differences count as a stale base.
    /// </summary>
    private static bool StatesMatch(QueueState left, QueueState right) =>
        string.Equals(
            QueueStateSerializer.Serialize(left),
            QueueStateSerializer.Serialize(right),
            StringComparison.Ordinal);

    private static void AssertNoUnrequestedLoss(
        QueueState onDiskState,
        QueueState finalState,
        HashSet<string> removalAllowList,
        string queueStatePath)
    {
        var survivingUnits = new HashSet<string>(
            finalState.Items.Select(item => item.ExecutionUnit), StringComparer.Ordinal);

        var lost = onDiskState.Items
            .Select(item => item.ExecutionUnit)
            .Where(unit => !survivingUnits.Contains(unit) && !removalAllowList.Contains(unit))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(unit => unit, StringComparer.Ordinal)
            .ToArray();

        if (lost.Length == 0)
        {
            return;
        }

        throw new QueueStateItemLossException(
            $"refusing to persist queue-state to {queueStatePath}: this write would remove {lost.Length} queue "
            + $"item(s) it was not asked to remove — {string.Join(", ", lost)}. This is the 2026-07-23 lost-update "
            + "shape (a write from a stale base silently erasing another domain's item); the write was aborted and "
            + $"the file is unchanged. Re-run this command against current state, or if an item is already lost, "
            + $"{RecoverySurfaces}.");
    }

    /// <summary>
    /// Defense in depth: a re-applied mutation is item-scoped BY
    /// CONSTRUCTION (the delta only ever carries the units it touched), so
    /// this can only fire if that construction is ever broken. It is
    /// asserted rather than assumed, because the failure it guards against
    /// is precisely the silent one.
    /// </summary>
    private static void AssertItemScoped(
        QueueState onDiskState,
        QueueState finalState,
        IReadOnlyCollection<string> touchedUnits,
        string queueStatePath)
    {
        var touched = new HashSet<string>(touchedUnits, StringComparer.Ordinal);
        var finalByUnit = finalState.Items.ToDictionary(item => item.ExecutionUnit, StringComparer.Ordinal);

        var collateral = new List<string>();
        foreach (var item in onDiskState.Items)
        {
            if (touched.Contains(item.ExecutionUnit))
            {
                continue;
            }

            if (!finalByUnit.TryGetValue(item.ExecutionUnit, out var reapplied) || !QueueItemsEqual(item, reapplied))
            {
                collateral.Add(item.ExecutionUnit);
            }
        }

        if (collateral.Count == 0)
        {
            return;
        }

        throw new QueueStateItemLossException(
            $"refusing to persist queue-state to {queueStatePath}: re-applying this mutation onto the current "
            + $"on-disk state would also modify {collateral.Count} unrelated item(s) — "
            + $"{string.Join(", ", collateral.OrderBy(unit => unit, StringComparer.Ordinal))}. A re-applied mutation "
            + "must touch only the items it originally changed, plus updated_at.");
    }

    public static bool QueueItemsEqual(QueueItem left, QueueItem right) =>
        string.Equals(
            QueueStateSerializer.Serialize(SingleItemState(left)),
            QueueStateSerializer.Serialize(SingleItemState(right)),
            StringComparison.Ordinal);

    private static QueueState SingleItemState(QueueItem item) => new()
    {
        SchemaVersion = "1",
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Items = [item],
    };

    private static void Write(string queueStatePath, QueueState state)
    {
        var directory = Path.GetDirectoryName(queueStatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(state));
    }
}

/// <summary>
/// G548: the item-level difference between the state a caller read and the
/// state it wants written — i.e. the caller's mutation, expressed in a form
/// that can be re-applied to a DIFFERENT base. Derived rather than supplied,
/// so every existing writer gets stale-base recovery without restructuring
/// its own mutation logic.
/// </summary>
public sealed record QueueStateItemDelta
{
    /// <summary>Items added or modified by the mutation, in outgoing order.</summary>
    public required IReadOnlyList<QueueItem> Upserts { get; init; }

    /// <summary>Execution units the mutation removed from its own base.</summary>
    public required IReadOnlyList<string> Removals { get; init; }

    /// <summary>Every unit this mutation touches — the exact scope a re-application may modify.</summary>
    public IReadOnlyList<string> TouchedExecutionUnits =>
        Upserts.Select(item => item.ExecutionUnit).Concat(Removals).Distinct(StringComparer.Ordinal).ToArray();

    public static QueueStateItemDelta Between(QueueState baseState, QueueState outgoingState)
    {
        var baseByUnit = baseState.Items.ToDictionary(item => item.ExecutionUnit, StringComparer.Ordinal);
        var outgoingUnits = new HashSet<string>(
            outgoingState.Items.Select(item => item.ExecutionUnit), StringComparer.Ordinal);

        var upserts = outgoingState.Items
            .Where(item => !baseByUnit.TryGetValue(item.ExecutionUnit, out var original)
                || !QueueStatePersistence.QueueItemsEqual(original, item))
            .ToArray();

        var removals = baseState.Items
            .Select(item => item.ExecutionUnit)
            .Where(unit => !outgoingUnits.Contains(unit))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new QueueStateItemDelta { Upserts = upserts, Removals = removals };
    }

    /// <summary>
    /// Re-applies this mutation onto <paramref name="freshState"/>. An
    /// upserted unit already present is replaced in place (preserving the
    /// fresh state's item ORDER, so unrelated items never shuffle); a new
    /// unit is appended. Removals drop only the named units. Everything else
    /// is carried through untouched.
    /// </summary>
    public QueueState ApplyTo(QueueState freshState, DateTimeOffset updatedAt)
    {
        var upsertByUnit = Upserts.ToDictionary(item => item.ExecutionUnit, StringComparer.Ordinal);
        var removalSet = new HashSet<string>(Removals, StringComparer.Ordinal);

        var items = new List<QueueItem>();
        foreach (var item in freshState.Items)
        {
            if (removalSet.Contains(item.ExecutionUnit))
            {
                continue;
            }

            items.Add(upsertByUnit.TryGetValue(item.ExecutionUnit, out var replacement) ? replacement : item);
        }

        var existingUnits = new HashSet<string>(
            freshState.Items.Select(item => item.ExecutionUnit), StringComparer.Ordinal);
        foreach (var upsert in Upserts.Where(upsert => !existingUnits.Contains(upsert.ExecutionUnit)))
        {
            items.Add(upsert);
        }

        return freshState with { Items = items, UpdatedAt = updatedAt };
    }
}

/// <summary>G548: outcome of a guarded queue-state write.</summary>
public sealed record QueueStatePersistResult
{
    /// <summary>
    /// The state actually written to disk. Null for the raw-text path, which
    /// deliberately never materializes the model.
    /// </summary>
    public required QueueState? PersistedState { get; init; }

    /// <summary>
    /// True when the on-disk file had changed since the caller read it and
    /// the caller's mutation was re-applied onto that fresher state. Callers
    /// surface this so a re-application is never invisible.
    /// </summary>
    public required bool ReappliedOnFreshBase { get; init; }

    /// <summary>The execution units the re-applied mutation covered.</summary>
    public required IReadOnlyList<string> ReappliedExecutionUnits { get; init; }

    public static QueueStatePersistResult DirectWrite(QueueState? state) => new()
    {
        PersistedState = state,
        ReappliedOnFreshBase = false,
        ReappliedExecutionUnits = Array.Empty<string>(),
    };

    public static QueueStatePersistResult Reapplied(QueueState state, IReadOnlyList<string> units) => new()
    {
        PersistedState = state,
        ReappliedOnFreshBase = true,
        ReappliedExecutionUnits = units,
    };
}

/// <summary>
/// G548: raised when a queue-state write would drop an item it was not asked
/// to drop, when the current on-disk state cannot be read to check that, or
/// when a re-applied mutation would reach outside its own item scope. An
/// <see cref="InvalidOperationException"/> so the many canonical writers that
/// already catch that type report it through their existing failure paths
/// instead of crashing.
/// </summary>
public sealed class QueueStateItemLossException : InvalidOperationException
{
    public QueueStateItemLossException(string message)
        : base(message)
    {
    }
}
