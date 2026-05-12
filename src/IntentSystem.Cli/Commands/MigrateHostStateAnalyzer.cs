using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G331: pure analyzer that plans a one-shot migration of root-level
/// queue-state and runs.jsonl into the G327 role-scoped runtime
/// layout (<c>.intent-cli/runtime/&lt;domain&gt;/&lt;owner&gt;__&lt;repo&gt;/</c>).
///
/// The analyzer is read-only — it walks the legacy root state and the
/// already-migrated scoped state (if any) and returns a deterministic
/// plan describing:
///   • the queue items that belong to the target <c>(domain, owner/repo)</c>
///     pair (matched via GitHub <c>linked_issue.repo</c> / <c>linked_pr</c>);
///   • the runs.jsonl events that go with those items;
///   • ambiguous records that cannot be deterministically matched and
///     MUST stay in the legacy file until the operator disambiguates;
///   • idempotency: items / runs already present in scoped state are
///     omitted from <c>ItemsToAdd</c> / <c>RunsToAdd</c> so re-running
///     the migration is safe.
///
/// Pure — no I/O. The <see cref="MigrateHostStateCommand"/> wraps this
/// analyzer with file reads + writes + the archive step.
/// </summary>
internal static class MigrateHostStateAnalyzer
{
    public const string RoleDesign = "design";
    public const string RoleReviewRuntime = "review-runtime";

    public static MigrateHostStatePlan Analyze(MigrateHostStateInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputs.Domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputs.TargetRepo);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputs.Role);

        var matching = new List<QueueItem>();
        var ambiguities = new List<string>();
        var missingLinkage = new List<string>();
        var unresolved = new List<string>();

        // Walk legacy items and classify against the target repo.
        var legacyItems = inputs.LegacyQueueState?.Items ?? Array.Empty<QueueItem>();
        foreach (var item in legacyItems)
        {
            var match = ClassifyItem(item, inputs.TargetRepo);
            switch (match.Kind)
            {
                case ItemMatchKind.Match:
                    matching.Add(item);
                    break;
                case ItemMatchKind.MissingLinkage:
                    missingLinkage.Add($"queue item '{item.ExecutionUnit}' has no linked_issue / linked_pr; cannot deterministically attribute to {inputs.TargetRepo}.");
                    break;
                case ItemMatchKind.OtherRepo:
                    // Belongs to a different (domain, repo); legitimately
                    // stays in the legacy root for THIS migration. Not an
                    // unresolved gap.
                    break;
                case ItemMatchKind.Ambiguous:
                    ambiguities.Add($"queue item '{item.ExecutionUnit}' has conflicting linked_issue / linked_pr; refusing to migrate without disambiguation.");
                    break;
            }
        }

        var matchingUnits = matching
            .Select(i => i.ExecutionUnit)
            .ToHashSet(StringComparer.Ordinal);

        // Walk legacy runs and bind them to matching execution units OR
        // the target repo. Runs that match the target_repo field but
        // refer to a non-matching execution unit are flagged as
        // unresolved-legacy-records so the operator can decide.
        var matchingRuns = new List<RunEvent>();
        foreach (var runEvent in inputs.LegacyRuns ?? Array.Empty<RunEvent>())
        {
            var unit = runEvent.ExecutionUnit;
            var runRepo = runEvent.Repo;
            var unitMatches = !string.IsNullOrWhiteSpace(unit) && matchingUnits.Contains(unit);
            var repoMatches = !string.IsNullOrWhiteSpace(runRepo)
                && string.Equals(runRepo, inputs.TargetRepo, StringComparison.OrdinalIgnoreCase);

            if (unitMatches)
            {
                matchingRuns.Add(runEvent);
                continue;
            }

            if (repoMatches && !unitMatches)
            {
                // Run references the right repo but a unit we couldn't
                // match. Don't auto-migrate (the unit may have been
                // deleted or renamed); surface for operator review.
                unresolved.Add($"runs.jsonl event for execution_unit='{unit}' carries repo='{runRepo}' but no matching queue item; left in legacy for operator review.");
            }
        }

        // Idempotency: subtract items / runs that are already in scoped
        // state from the to-add lists. Two queue items collide on
        // execution_unit; two run events collide on (ts, execution_unit, event).
        var existingScopedUnits = (inputs.ExistingScopedQueueState?.Items ?? Array.Empty<QueueItem>())
            .Select(i => i.ExecutionUnit)
            .ToHashSet(StringComparer.Ordinal);
        var itemsToAdd = matching
            .Where(i => !existingScopedUnits.Contains(i.ExecutionUnit))
            .ToArray();

        var existingScopedRunKeys = (inputs.ExistingScopedRuns ?? Array.Empty<RunEvent>())
            .Select(RunKey)
            .ToHashSet(StringComparer.Ordinal);
        var runsToAdd = matchingRuns
            .Where(r => !existingScopedRunKeys.Contains(RunKey(r)))
            .ToArray();

        var alreadyMigrated = matching.Count > 0
            && itemsToAdd.Length == 0
            && runsToAdd.Length == 0;

        return new MigrateHostStatePlan
        {
            Domain = inputs.Domain,
            TargetRepo = inputs.TargetRepo,
            Role = inputs.Role,
            MatchingItems = matching,
            MatchingRuns = matchingRuns,
            Ambiguities = ambiguities,
            MissingGitHubLinkage = missingLinkage,
            UnresolvedLegacyRecords = unresolved,
            ItemsToAdd = itemsToAdd,
            RunsToAdd = runsToAdd,
            AlreadyMigrated = alreadyMigrated
        };
    }

    private static string RunKey(RunEvent runEvent) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{runEvent.Ts:O}|{runEvent.ExecutionUnit}|{runEvent.Event}");

    private static (ItemMatchKind Kind, string? Reason) ClassifyItem(QueueItem item, string targetRepo)
    {
        var linkedIssueRepo = item.LinkedIssue?.Repo;
        var linkedPr = item.LinkedPr;

        var issueMatches = !string.IsNullOrWhiteSpace(linkedIssueRepo)
            && string.Equals(linkedIssueRepo, targetRepo, StringComparison.OrdinalIgnoreCase);
        var prMatches = LinkedPrPointsAtRepo(linkedPr, targetRepo);

        // Both present and both consistent → match.
        if (issueMatches && (prMatches || string.IsNullOrWhiteSpace(linkedPr)))
        {
            return (ItemMatchKind.Match, null);
        }
        // Only linked_pr (no linked_issue) and it matches → match.
        if (prMatches && string.IsNullOrWhiteSpace(linkedIssueRepo))
        {
            return (ItemMatchKind.Match, null);
        }
        // No linked_issue and no linked_pr → cannot attribute.
        if (string.IsNullOrWhiteSpace(linkedIssueRepo) && string.IsNullOrWhiteSpace(linkedPr))
        {
            return (ItemMatchKind.MissingLinkage, null);
        }
        // linked_issue points at a different repo AND linked_pr points
        // at the target → conflicting signals.
        if (!string.IsNullOrWhiteSpace(linkedIssueRepo) && !issueMatches && prMatches)
        {
            return (ItemMatchKind.Ambiguous, null);
        }
        // Otherwise the item belongs to a different (domain, repo).
        return (ItemMatchKind.OtherRepo, null);
    }

    private static bool LinkedPrPointsAtRepo(string? linkedPr, string targetRepo)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return false;
        }
        var prefix = $"https://github.com/{targetRepo}/pull/";
        return linkedPr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private enum ItemMatchKind
    {
        Match,
        MissingLinkage,
        Ambiguous,
        OtherRepo
    }
}

/// <summary>
/// G331: inputs to <see cref="MigrateHostStateAnalyzer.Analyze"/>.
/// </summary>
internal sealed record MigrateHostStateInputs
{
    public required string Domain { get; init; }
    public required string TargetRepo { get; init; }
    public required string Role { get; init; }

    /// <summary>Legacy root queue-state (null when no file on disk).</summary>
    public QueueState? LegacyQueueState { get; init; }

    /// <summary>Legacy root runs.jsonl events (empty when no file).</summary>
    public required IReadOnlyList<RunEvent> LegacyRuns { get; init; }

    /// <summary>
    /// Existing scoped queue-state (null when this is the first
    /// migration of the target). Used for idempotency.
    /// </summary>
    public QueueState? ExistingScopedQueueState { get; init; }

    /// <summary>
    /// Existing scoped runs.jsonl events. Used for idempotency.
    /// </summary>
    public required IReadOnlyList<RunEvent> ExistingScopedRuns { get; init; }
}

/// <summary>
/// G331: deterministic migration plan emitted by the analyzer.
/// </summary>
internal sealed record MigrateHostStatePlan
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>
    /// Queue items the migration will move into scoped state (or
    /// already migrated when <c>AlreadyMigrated</c> is true).
    /// </summary>
    [JsonPropertyName("matching_items")]
    public required IReadOnlyList<QueueItem> MatchingItems { get; init; }

    /// <summary>
    /// Run events that will move into scoped runs.jsonl.
    /// </summary>
    [JsonPropertyName("matching_runs")]
    public required IReadOnlyList<RunEvent> MatchingRuns { get; init; }

    /// <summary>
    /// Items the analyzer refuses to migrate because the linkage
    /// signals conflict (e.g. linked_issue.repo and linked_pr point at
    /// different repos). The operator must disambiguate.
    /// </summary>
    [JsonPropertyName("ambiguities")]
    public required IReadOnlyList<string> Ambiguities { get; init; }

    /// <summary>
    /// Items that match no repo and have no GitHub linkage at all.
    /// Cannot be safely attributed; left in legacy for now.
    /// </summary>
    [JsonPropertyName("missing_github_linkage")]
    public required IReadOnlyList<string> MissingGitHubLinkage { get; init; }

    /// <summary>
    /// Legacy runs.jsonl events that point at the target repo but at
    /// an execution unit that no longer exists in queue-state.
    /// </summary>
    [JsonPropertyName("unresolved_legacy_records")]
    public required IReadOnlyList<string> UnresolvedLegacyRecords { get; init; }

    /// <summary>
    /// Subset of <see cref="MatchingItems"/> not yet present in scoped
    /// state. Empty when the migration is a no-op (idempotency).
    /// </summary>
    [JsonPropertyName("items_to_add")]
    public required IReadOnlyList<QueueItem> ItemsToAdd { get; init; }

    /// <summary>
    /// Subset of <see cref="MatchingRuns"/> not yet present in scoped
    /// runs.jsonl. Empty when the migration is a no-op.
    /// </summary>
    [JsonPropertyName("runs_to_add")]
    public required IReadOnlyList<RunEvent> RunsToAdd { get; init; }

    /// <summary>
    /// True when every matching item / run is already in scoped state
    /// — running the writer is a no-op. Re-running the migration
    /// surfaces this signal so operators know it's safe.
    /// </summary>
    [JsonPropertyName("already_migrated")]
    public required bool AlreadyMigrated { get; init; }
}
