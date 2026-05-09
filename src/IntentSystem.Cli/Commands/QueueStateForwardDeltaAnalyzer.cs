using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G312: pure analyzer that classifies a delta between two
/// <c>.intent-cli/queue-state.json</c> snapshots (HEAD blob vs.
/// working-tree blob) as <em>forward-only verified</em>,
/// <em>needs-operator-review</em>, or <em>invalid</em>. Forward-only
/// deltas are the deterministic metadata updates the host loop
/// performs after closeout/publish: adding <c>linked_pr</c> when it
/// was null, adding <c>linked_issue</c> when it was null, and the
/// monotonic <c>updated_at</c> bump. Anything else (item count
/// change, removed/changed link, state change, dependency edit,
/// etc.) requires operator review.
///
/// Pure data in / pure data out: no I/O, no <c>git</c> calls. The
/// caller is responsible for capturing the two JSON payloads.
/// </summary>
internal static class QueueStateForwardDeltaAnalyzer
{
    public const string ClassificationForwardOnly = "forward-only";
    public const string ClassificationNeedsOperatorReview = "needs-operator-review";
    public const string ClassificationInvalid = "invalid";

    /// <summary>
    /// Classify the delta between <paramref name="headJson"/> and
    /// <paramref name="workingJson"/>. When either payload fails to
    /// parse the result is <see cref="ClassificationInvalid"/> with
    /// the parse error in the summary.
    /// </summary>
    public static QueueStateForwardDeltaResult Analyze(string headJson, string workingJson)
    {
        ArgumentNullException.ThrowIfNull(headJson);
        ArgumentNullException.ThrowIfNull(workingJson);

        QueueState head;
        QueueState working;
        try
        {
            head = QueueStateSerializer.Deserialize(headJson);
        }
        catch (Exception exception) when (
            exception is JsonException
            or InvalidOperationException
            or ArgumentException)
        {
            return Invalid($"HEAD `.intent-cli/queue-state.json` did not parse: {exception.Message}");
        }
        try
        {
            working = QueueStateSerializer.Deserialize(workingJson);
        }
        catch (Exception exception) when (
            exception is JsonException
            or InvalidOperationException
            or ArgumentException)
        {
            return Invalid($"working-tree `.intent-cli/queue-state.json` did not parse: {exception.Message}");
        }

        if (!string.Equals(head.SchemaVersion, working.SchemaVersion, StringComparison.Ordinal))
        {
            return NeedsReview(
                $"schema_version changed from `{head.SchemaVersion}` to `{working.SchemaVersion}`; refuse forward-only auto-commit.",
                Array.Empty<QueueStateForwardChange>());
        }

        if (head.Items.Count != working.Items.Count)
        {
            return NeedsReview(
                $"items[] count changed from {head.Items.Count} to {working.Items.Count}; refuse forward-only auto-commit.",
                Array.Empty<QueueStateForwardChange>());
        }

        var changes = new List<QueueStateForwardChange>();
        for (var index = 0; index < head.Items.Count; index++)
        {
            var headItem = head.Items[index];
            var workingItem = working.Items[index];
            if (!string.Equals(headItem.ExecutionUnit, workingItem.ExecutionUnit, StringComparison.Ordinal))
            {
                return NeedsReview(
                    $"items[{index}].execution_unit changed from `{headItem.ExecutionUnit}` to `{workingItem.ExecutionUnit}`; refuse forward-only auto-commit (item reorder/rename).",
                    Array.Empty<QueueStateForwardChange>());
            }

            var itemResult = ClassifyItem(headItem, workingItem);
            if (itemResult.Verdict == ItemVerdict.Reject)
            {
                return NeedsReview(itemResult.Reason!, Array.Empty<QueueStateForwardChange>());
            }
            if (itemResult.Verdict == ItemVerdict.ForwardChange && itemResult.Change is { } change)
            {
                changes.Add(change);
            }
            // ItemVerdict.Unchanged: no-op
        }

        if (changes.Count == 0)
        {
            return NeedsReview(
                "queue-state.json is dirty but no field-level forward-only delta was detected; refuse to auto-commit (operator review).",
                Array.Empty<QueueStateForwardChange>());
        }

        return new QueueStateForwardDeltaResult
        {
            Classification = ClassificationForwardOnly,
            Summary = BuildForwardSummary(changes),
            Changes = changes,
        };
    }

    private enum ItemVerdict
    {
        Unchanged,
        ForwardChange,
        Reject,
    }

    private readonly record struct ItemClassification(
        ItemVerdict Verdict,
        QueueStateForwardChange? Change,
        string? Reason);

    private static ItemClassification ClassifyItem(QueueItem head, QueueItem working)
    {
        // All scalar/metadata fields (other than LinkedIssue / LinkedPr)
        // must be byte-identical for the delta to be classified as
        // forward-only. Anything else is operator-review territory.
        if (!string.Equals(head.Title, working.Title, StringComparison.Ordinal)
            || head.State != working.State
            || !string.Equals(head.ClarificationReturnPath, working.ClarificationReturnPath, StringComparison.Ordinal)
            || !string.Equals(head.WorkerRole, working.WorkerRole, StringComparison.Ordinal)
            || !string.Equals(head.ReviewRole, working.ReviewRole, StringComparison.Ordinal)
            || !string.Equals(head.Priority, working.Priority, StringComparison.Ordinal))
        {
            return new ItemClassification(
                ItemVerdict.Reject,
                null,
                $"items[execution_unit=`{head.ExecutionUnit}`] has scalar metadata changes (title/state/role/priority/clarification_return_path); refuse forward-only auto-commit.");
        }

        if (!SequenceEquals(head.Dependencies, working.Dependencies)
            || !SequenceEquals(head.BlockedBy, working.BlockedBy))
        {
            return new ItemClassification(
                ItemVerdict.Reject,
                null,
                $"items[execution_unit=`{head.ExecutionUnit}`] dependencies / blocked_by changed; refuse forward-only auto-commit.");
        }

        if (!PacketPathsEqual(head.PacketPaths, working.PacketPaths))
        {
            return new ItemClassification(
                ItemVerdict.Reject,
                null,
                $"items[execution_unit=`{head.ExecutionUnit}`] packet_paths changed; refuse forward-only auto-commit.");
        }

        // Now classify the LinkedIssue / LinkedPr deltas.
        var linkedIssueDelta = ClassifyLinkedIssue(head, working);
        if (linkedIssueDelta.Verdict == ItemVerdict.Reject)
        {
            return linkedIssueDelta;
        }

        var linkedPrDelta = ClassifyLinkedPr(head, working);
        if (linkedPrDelta.Verdict == ItemVerdict.Reject)
        {
            return linkedPrDelta;
        }

        // Combine: at most one forward-change per item is currently
        // expected, but support both kinds appearing on the same wake
        // (e.g., a publish wrote both linked_issue and linked_pr).
        if (linkedIssueDelta.Verdict == ItemVerdict.ForwardChange
            && linkedPrDelta.Verdict == ItemVerdict.ForwardChange)
        {
            return new ItemClassification(
                ItemVerdict.ForwardChange,
                new QueueStateForwardChange
                {
                    ExecutionUnit = head.ExecutionUnit,
                    Kind = QueueStateForwardChangeKind.AddedLinkedIssueAndPr,
                    LinkedPrUrl = linkedPrDelta.Change?.LinkedPrUrl,
                    LinkedIssueRepo = linkedIssueDelta.Change?.LinkedIssueRepo,
                    LinkedIssueNumber = linkedIssueDelta.Change?.LinkedIssueNumber,
                    LinkedIssueUrl = linkedIssueDelta.Change?.LinkedIssueUrl,
                },
                null);
        }
        if (linkedIssueDelta.Verdict == ItemVerdict.ForwardChange)
        {
            return linkedIssueDelta;
        }
        if (linkedPrDelta.Verdict == ItemVerdict.ForwardChange)
        {
            return linkedPrDelta;
        }
        return new ItemClassification(ItemVerdict.Unchanged, null, null);
    }

    private static ItemClassification ClassifyLinkedIssue(QueueItem head, QueueItem working)
    {
        if (head.LinkedIssue is null && working.LinkedIssue is null)
        {
            return new ItemClassification(ItemVerdict.Unchanged, null, null);
        }

        if (head.LinkedIssue is null && working.LinkedIssue is { } addedIssue)
        {
            return new ItemClassification(
                ItemVerdict.ForwardChange,
                new QueueStateForwardChange
                {
                    ExecutionUnit = head.ExecutionUnit,
                    Kind = QueueStateForwardChangeKind.AddedLinkedIssue,
                    LinkedIssueRepo = addedIssue.Repo,
                    LinkedIssueNumber = addedIssue.Number,
                    LinkedIssueUrl = addedIssue.Url,
                },
                null);
        }

        if (head.LinkedIssue is { } existing && working.LinkedIssue is { } updated)
        {
            if (string.Equals(existing.Repo, updated.Repo, StringComparison.Ordinal)
                && existing.Number == updated.Number
                && string.Equals(existing.Url, updated.Url, StringComparison.Ordinal))
            {
                return new ItemClassification(ItemVerdict.Unchanged, null, null);
            }
            return new ItemClassification(
                ItemVerdict.Reject,
                null,
                $"items[execution_unit=`{head.ExecutionUnit}`] linked_issue changed from existing value; forward-only auto-commit only adds, never replaces.");
        }

        // working.LinkedIssue is null, head.LinkedIssue is not — that's a removal.
        return new ItemClassification(
            ItemVerdict.Reject,
            null,
            $"items[execution_unit=`{head.ExecutionUnit}`] linked_issue was removed; refuse forward-only auto-commit (rollback).");
    }

    private static ItemClassification ClassifyLinkedPr(QueueItem head, QueueItem working)
    {
        var headEmpty = string.IsNullOrEmpty(head.LinkedPr);
        var workingEmpty = string.IsNullOrEmpty(working.LinkedPr);

        if (headEmpty && workingEmpty)
        {
            return new ItemClassification(ItemVerdict.Unchanged, null, null);
        }

        if (headEmpty && !workingEmpty)
        {
            return new ItemClassification(
                ItemVerdict.ForwardChange,
                new QueueStateForwardChange
                {
                    ExecutionUnit = head.ExecutionUnit,
                    Kind = QueueStateForwardChangeKind.AddedLinkedPr,
                    LinkedPrUrl = working.LinkedPr,
                },
                null);
        }

        if (!headEmpty && !workingEmpty)
        {
            if (string.Equals(head.LinkedPr, working.LinkedPr, StringComparison.Ordinal))
            {
                return new ItemClassification(ItemVerdict.Unchanged, null, null);
            }
            return new ItemClassification(
                ItemVerdict.Reject,
                null,
                $"items[execution_unit=`{head.ExecutionUnit}`] linked_pr changed from `{head.LinkedPr}` to `{working.LinkedPr}`; forward-only auto-commit only adds, never replaces.");
        }

        // working empty, head non-empty — removal.
        return new ItemClassification(
            ItemVerdict.Reject,
            null,
            $"items[execution_unit=`{head.ExecutionUnit}`] linked_pr was removed; refuse forward-only auto-commit (rollback).");
    }

    private static bool SequenceEquals(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool PacketPathsEqual(PacketPaths a, PacketPaths b) =>
        string.Equals(a.Yaml, b.Yaml, StringComparison.Ordinal)
        && string.Equals(a.Implementation, b.Implementation, StringComparison.Ordinal)
        && string.Equals(a.ReviewContext, b.ReviewContext, StringComparison.Ordinal);

    private static QueueStateForwardDeltaResult Invalid(string summary) => new()
    {
        Classification = ClassificationInvalid,
        Summary = summary,
        Changes = Array.Empty<QueueStateForwardChange>(),
    };

    private static QueueStateForwardDeltaResult NeedsReview(
        string summary,
        IReadOnlyList<QueueStateForwardChange> changes) => new()
        {
            Classification = ClassificationNeedsOperatorReview,
            Summary = summary,
            Changes = changes,
        };

    private static string BuildForwardSummary(IReadOnlyList<QueueStateForwardChange> changes)
    {
        var pieces = changes.Select(change => change.Kind switch
        {
            QueueStateForwardChangeKind.AddedLinkedPr =>
                $"added linked_pr=`{change.LinkedPrUrl}` on `{change.ExecutionUnit}`",
            QueueStateForwardChangeKind.AddedLinkedIssue =>
                $"added linked_issue (#{change.LinkedIssueNumber}, {change.LinkedIssueRepo}) on `{change.ExecutionUnit}`",
            QueueStateForwardChangeKind.AddedLinkedIssueAndPr =>
                $"added linked_issue (#{change.LinkedIssueNumber}, {change.LinkedIssueRepo}) and linked_pr=`{change.LinkedPrUrl}` on `{change.ExecutionUnit}`",
            _ => $"forward delta on `{change.ExecutionUnit}`",
        });
        return $"forward-only queue-state.json delta: {string.Join("; ", pieces)}.";
    }
}

internal enum QueueStateForwardChangeKind
{
    AddedLinkedPr,
    AddedLinkedIssue,
    AddedLinkedIssueAndPr,
}

internal sealed record QueueStateForwardChange
{
    public required string ExecutionUnit { get; init; }
    public required QueueStateForwardChangeKind Kind { get; init; }
    public string? LinkedPrUrl { get; init; }
    public string? LinkedIssueRepo { get; init; }
    public int? LinkedIssueNumber { get; init; }
    public string? LinkedIssueUrl { get; init; }
}

internal sealed record QueueStateForwardDeltaResult
{
    public required string Classification { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<QueueStateForwardChange> Changes { get; init; }
}
