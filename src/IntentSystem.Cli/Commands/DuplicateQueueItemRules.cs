using System.Globalization;
using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G746: the partial-order rules for duplicate queue-state entries. Duplicate
/// entries are not resolved by array order, timestamp, or last-writer-wins.
/// A winner exists only when one complete entry strictly dominates every other
/// entry without discarding information carried by a competitor.
/// </summary>
internal static class DuplicateQueueItemRules
{
    private static readonly JsonSerializerOptions ProjectionJsonOptions = new()
    {
        WriteIndented = true,
    };

    public static IReadOnlyList<DuplicateQueueItemGroup> Analyze(
        IReadOnlyList<DuplicateQueueItemEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .GroupBy(entry => entry.ExecutionUnit, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var competing = group.OrderBy(entry => entry.Index).ToArray();
                var winners = competing
                    .Where(candidate => competing.All(other =>
                        candidate.Index == other.Index || Dominates(candidate, other)))
                    .ToArray();

                return new DuplicateQueueItemGroup
                {
                    ExecutionUnit = group.Key,
                    Entries = competing,
                    Winner = winners.Length == 1 ? winners[0] : null,
                };
            })
            .ToArray();
    }

    public static DuplicateQueueItemEntry FromProjection(
        StateDoctorQueueItem item,
        int fallbackIndex)
    {
        ArgumentNullException.ThrowIfNull(item);

        var index = item.SourceIndex >= 0 ? item.SourceIndex : fallbackIndex;
        var state = item.State ?? (item.Completed ? "completed" : "queued");
        var fields = item.ComparableFields ?? new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["state"] = state,
            ["linked_pr"] = item.LinkedPrUrl,
            ["linked_issue"] = FormatLinkedIssue(item.LinkedIssueRepo, item.LinkedIssueNumber, item.LinkedIssueUrl),
        };

        return new DuplicateQueueItemEntry
        {
            Index = index,
            ExecutionUnit = item.ExecutionUnit,
            Fields = fields,
            FullEntryJson = item.FullEntryJson ?? RenderProjection(item, state),
        };
    }

    public static DuplicateQueueItemEntry FromQueueItem(QueueItem item, int index)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new DuplicateQueueItemEntry
        {
            Index = index,
            ExecutionUnit = item.ExecutionUnit,
            Fields = GetComparableFields(item),
            FullEntryJson = RenderQueueItem(item),
        };
    }

    public static IReadOnlyDictionary<string, string?> GetComparableFields(QueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["title"] = item.Title,
            ["state"] = item.State.ToString(),
            ["dependencies"] = JsonSerializer.Serialize(item.Dependencies),
            ["blocked_by"] = JsonSerializer.Serialize(item.BlockedBy),
            ["clarification_return_path"] = item.ClarificationReturnPath,
            ["packet_paths"] = JsonSerializer.Serialize(new
            {
                implementation = item.PacketPaths.Implementation,
                review_context = item.PacketPaths.ReviewContext,
                yaml = item.PacketPaths.Yaml,
            }),
            ["routing_snapshot"] = item.RoutingSnapshot is null
                ? null
                : JsonSerializer.Serialize(new
                {
                    lane_id = item.RoutingSnapshot.LaneId,
                    definition_revision = item.RoutingSnapshot.DefinitionRevision,
                    start_branch = item.RoutingSnapshot.StartBranch,
                    pr_base_branch = item.RoutingSnapshot.PrBaseBranch,
                    landing_mode = item.RoutingSnapshot.LandingMode,
                }),
            ["linked_pr"] = item.LinkedPr,
            ["linked_issue"] = item.LinkedIssue is null
                ? null
                : JsonSerializer.Serialize(new
                {
                    repo = item.LinkedIssue.Repo,
                    number = item.LinkedIssue.Number,
                    url = item.LinkedIssue.Url,
                }),
            ["worker_role"] = item.WorkerRole,
            ["review_role"] = item.ReviewRole,
            ["priority"] = item.Priority,
            ["retirement_reason"] = item.RetirementReason,
            ["priority_revision"] = item.PriorityRevision.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static string FormatCompetingEntries(DuplicateQueueItemGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return string.Join(
            Environment.NewLine,
            group.Entries.Select(entry => $"entry[{entry.Index}]: {entry.FullEntryJson}"));
    }

    private static bool Dominates(DuplicateQueueItemEntry candidate, DuplicateQueueItemEntry other)
    {
        var strictlyMoreInformative = false;
        var keys = candidate.Fields.Keys
            .Concat(other.Fields.Keys)
            .Distinct(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            candidate.Fields.TryGetValue(key, out var candidateValue);
            other.Fields.TryGetValue(key, out var otherValue);

            if (string.Equals(candidateValue, otherValue, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(key, "state", StringComparison.Ordinal))
            {
                var stateComparison = CompareState(candidateValue, otherValue);
                if (stateComparison < 0)
                {
                    return false;
                }

                if (stateComparison > 0)
                {
                    strictlyMoreInformative = true;
                    continue;
                }

                // Unknown, non-equal states are competing information.
                return false;
            }

            var candidateHasInformation = HasInformation(candidateValue);
            var otherHasInformation = HasInformation(otherValue);
            if (candidateHasInformation && !otherHasInformation)
            {
                strictlyMoreInformative = true;
                continue;
            }

            // A missing candidate value cannot dominate information held by
            // its competitor, and two different populated values conflict.
            return false;
        }

        return strictlyMoreInformative;
    }

    private static int CompareState(string? candidate, string? other)
    {
        if (TryGetStateRank(candidate, out var candidateRank)
            && TryGetStateRank(other, out var otherRank))
        {
            return candidateRank.CompareTo(otherRank);
        }

        return 0;
    }

    private static bool TryGetStateRank(string? value, out int rank)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty);
        switch (normalized)
        {
            case "pending":
            case "queued":
                rank = 0;
                return true;
            case "active":
            case "issuepublished":
                rank = 1;
                return true;
            case "review":
                rank = 2;
                return true;
            case "fixing":
                rank = 3;
                return true;
            case "clarifyblocked":
                rank = 4;
                return true;
            case "blocked":
                rank = 5;
                return true;
            case "completed":
                rank = 6;
                return true;
            case "retired":
                rank = 7;
                return true;
            default:
                rank = 0;
                return false;
        }
    }

    private static bool HasInformation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "[]", StringComparison.Ordinal)
            || string.Equals(value, "{}", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string? FormatLinkedIssue(string? repo, int? number, string? url) =>
        number is { } issueNumber
            ? $"{repo}|{issueNumber.ToString(CultureInfo.InvariantCulture)}|{url}"
            : null;

    private static string RenderProjection(StateDoctorQueueItem item, string state)
    {
        var linkedIssue = item.LinkedIssueNumber is { } number
            ? new
            {
                repo = item.LinkedIssueRepo,
                number,
                url = item.LinkedIssueUrl,
            }
            : null;
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["execution_unit"] = item.ExecutionUnit,
            ["state"] = state,
            ["linked_pr"] = item.LinkedPrUrl,
            ["linked_issue"] = linkedIssue,
        };
        return JsonSerializer.Serialize(payload, ProjectionJsonOptions);
    }

    private static string RenderQueueItem(QueueItem item)
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Items = [item],
        };

        using var document = JsonDocument.Parse(QueueStateSerializer.Serialize(state));
        return document.RootElement.GetProperty("items")[0].GetRawText();
    }
}

internal sealed record DuplicateQueueItemEntry
{
    public required int Index { get; init; }
    public required string ExecutionUnit { get; init; }
    public required IReadOnlyDictionary<string, string?> Fields { get; init; }
    public required string FullEntryJson { get; init; }
}

internal sealed record DuplicateQueueItemGroup
{
    public required string ExecutionUnit { get; init; }
    public required IReadOnlyList<DuplicateQueueItemEntry> Entries { get; init; }
    public DuplicateQueueItemEntry? Winner { get; init; }
}
