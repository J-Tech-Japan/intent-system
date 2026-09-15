using System.Text.Json.Serialization;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834: the one gate evaluator shared by <c>review cross-runtime status</c> and
/// <c>automation pr-transition --transition approved</c>.
/// <para>
/// Only readable records of the PR whose execution unit, domain, team, and kind
/// match the resolution count; the rest are <c>foreign</c>. The relation is
/// recomputed from the current declared conductor runtime. For each runtime, its
/// latest record on the gated head (ordered by <c>recorded_at</c>, then file
/// name) decides. A declared team needs a same-runtime approve and a
/// cross-runtime approve on the head, no latest request-changes, no unresolved
/// earlier-head block, and no unreadable record.
/// </para>
/// </summary>
internal static class CrossRuntimeReviewGate
{
    public const string DecisionNotRequired = "not-required";
    public const string DecisionSatisfied = "satisfied";
    public const string DecisionBlocked = "blocked";
    public const string DecisionMissing = "missing";

    public const string StatusDeciding = "deciding";
    public const string StatusSuperseded = "superseded";
    public const string StatusStaleHead = "stale-head";
    public const string StatusForeign = "foreign";

    public static CrossRuntimeReviewGateResult Evaluate(
        CrossRuntimeReviewTeamDeclaration? declaration,
        CrossRuntimeReviewResolution resolution,
        string headSha,
        CrossRuntimeReviewReadResult read)
    {
        if (declaration is null)
        {
            return new CrossRuntimeReviewGateResult
            {
                Decision = DecisionNotRequired,
                Reasons = [],
                Records = [],
                Unreadable = read.Unreadable,
            };
        }

        var conductor = declaration.ConductorRuntime;
        var entries = new List<CrossRuntimeReviewGateRecordEntry>();
        var matching = new List<CrossRuntimeReviewStoredRecord>();
        foreach (var stored in read.Records)
        {
            var record = stored.Record;
            var isMatch = string.Equals(record.ExecutionUnit, resolution.ExecutionUnit, StringComparison.Ordinal)
                && string.Equals(record.Domain, resolution.Domain, StringComparison.Ordinal)
                && string.Equals(record.Team, resolution.Team, StringComparison.Ordinal)
                && string.Equals(record.Kind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal);
            if (isMatch)
            {
                matching.Add(stored);
            }
            else
            {
                entries.Add(Entry(stored, conductor, StatusForeign));
            }
        }

        // read.Records is already ordered by recorded_at, then file name.
        var onHead = matching.Where(stored => IsHead(stored.Record, headSha)).ToArray();
        var latestOnHead = onHead
            .GroupBy(stored => stored.Record.Runtime, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        foreach (var stored in matching)
        {
            var status = !IsHead(stored.Record, headSha)
                ? StatusStaleHead
                : ReferenceEquals(latestOnHead[stored.Record.Runtime], stored) ? StatusDeciding : StatusSuperseded;
            entries.Add(Entry(stored, conductor, status));
        }

        var reasons = new List<CrossRuntimeReviewGateReason>();
        foreach (var unreadable in read.Unreadable)
        {
            reasons.Add(new CrossRuntimeReviewGateReason
            {
                Cause = CrossRuntimeReviewCauses.RecordUnreadable,
                Detail = $"record '{unreadable.RelativePath}' failed validation and fails the gate closed: {unreadable.Error}",
                File = unreadable.RelativePath,
            });
        }

        var blockedRuntimes = latestOnHead.Values
            .Where(stored => string.Equals(stored.Record.Verdict, CrossRuntimeReviewVerdict.RequestChanges, StringComparison.Ordinal))
            .Select(stored => stored.Record.Runtime)
            .OrderBy(runtime => runtime, StringComparer.Ordinal)
            .ToArray();
        if (blockedRuntimes.Length > 0)
        {
            reasons.Add(new CrossRuntimeReviewGateReason
            {
                Cause = CrossRuntimeReviewCauses.Blocked,
                Detail = $"the latest record on head {headSha} from {string.Join(", ", blockedRuntimes)} is request-changes.",
                Runtimes = blockedRuntimes,
            });
        }

        foreach (var runtime in matching
                     .Select(stored => stored.Record.Runtime)
                     .Distinct(StringComparer.Ordinal)
                     .Where(runtime => !latestOnHead.ContainsKey(runtime))
                     .OrderBy(runtime => runtime, StringComparer.Ordinal))
        {
            var latestEarlier = matching.Last(stored => string.Equals(stored.Record.Runtime, runtime, StringComparison.Ordinal));
            if (string.Equals(latestEarlier.Record.Verdict, CrossRuntimeReviewVerdict.RequestChanges, StringComparison.Ordinal))
            {
                reasons.Add(new CrossRuntimeReviewGateReason
                {
                    Cause = CrossRuntimeReviewCauses.RereviewMissing,
                    Detail = $"{runtime} requested changes on head {latestEarlier.Record.HeadSha} and has no record on head {headSha}; that runtime must re-review the delta.",
                    Runtimes = [runtime],
                });
            }
        }

        bool HasApprove(string relation) => latestOnHead.Values.Any(stored =>
            string.Equals(CrossRuntimeReviewRecord.RelationFor(stored.Record.Runtime, conductor), relation, StringComparison.Ordinal)
            && string.Equals(stored.Record.Verdict, CrossRuntimeReviewVerdict.Approve, StringComparison.Ordinal));

        foreach (var relation in new[] { CrossRuntimeReviewRecord.RelationSameRuntime, CrossRuntimeReviewRecord.RelationCrossRuntime })
        {
            if (!HasApprove(relation))
            {
                reasons.Add(new CrossRuntimeReviewGateReason
                {
                    Cause = CrossRuntimeReviewCauses.Missing,
                    Detail = relation == CrossRuntimeReviewRecord.RelationSameRuntime
                        ? $"head {headSha} has no approve whose latest record comes from the conductor runtime '{conductor}' (independent same-runtime subagent review)."
                        : $"head {headSha} has no approve whose latest record comes from a runtime other than the conductor runtime '{conductor}' (cross-runtime review).",
                    Relation = relation,
                });
            }
        }

        var decision = reasons.Any(reason => reason.Cause is CrossRuntimeReviewCauses.RecordUnreadable or CrossRuntimeReviewCauses.Blocked)
            ? DecisionBlocked
            : reasons.Count > 0 ? DecisionMissing : DecisionSatisfied;

        return new CrossRuntimeReviewGateResult
        {
            Decision = decision,
            Reasons = reasons,
            Records = entries
                .OrderBy(entry => entry.RecordedAt)
                .ThenBy(entry => entry.File, StringComparer.Ordinal)
                .ToArray(),
            Unreadable = read.Unreadable,
        };
    }

    private static bool IsHead(CrossRuntimeReviewRecord record, string headSha) =>
        string.Equals(record.HeadSha, headSha, StringComparison.OrdinalIgnoreCase);

    private static CrossRuntimeReviewGateRecordEntry Entry(CrossRuntimeReviewStoredRecord stored, string conductor, string status) =>
        new()
        {
            File = stored.RelativePath,
            Status = status,
            ExecutionUnit = stored.Record.ExecutionUnit,
            Domain = stored.Record.Domain,
            Team = stored.Record.Team,
            Kind = stored.Record.Kind,
            Runtime = stored.Record.Runtime,
            RuntimeVersion = stored.Record.RuntimeVersion,
            Relation = CrossRuntimeReviewRecord.RelationFor(stored.Record.Runtime, conductor),
            HeadSha = stored.Record.HeadSha,
            Verdict = stored.Record.Verdict,
            RecordedAt = stored.Record.RecordedAt,
        };
}

internal sealed record CrossRuntimeReviewGateResult
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("reasons")]
    public required IReadOnlyList<CrossRuntimeReviewGateReason> Reasons { get; init; }

    [JsonPropertyName("records")]
    public required IReadOnlyList<CrossRuntimeReviewGateRecordEntry> Records { get; init; }

    [JsonPropertyName("unreadable")]
    public required IReadOnlyList<CrossRuntimeReviewUnreadableRecord> Unreadable { get; init; }

    public bool Passes => Decision is CrossRuntimeReviewGate.DecisionSatisfied or CrossRuntimeReviewGate.DecisionNotRequired;

    /// <summary>The refusal cause a caller reports first: fail-closed causes lead.</summary>
    public string? PrimaryCause =>
        new[]
        {
            CrossRuntimeReviewCauses.RecordUnreadable,
            CrossRuntimeReviewCauses.Blocked,
            CrossRuntimeReviewCauses.RereviewMissing,
            CrossRuntimeReviewCauses.Missing,
        }.FirstOrDefault(cause => Reasons.Any(reason => reason.Cause == cause));
}

internal sealed record CrossRuntimeReviewGateReason
{
    [JsonPropertyName("cause")]
    public required string Cause { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }

    [JsonPropertyName("runtimes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Runtimes { get; init; }

    [JsonPropertyName("relation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Relation { get; init; }

    [JsonPropertyName("file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? File { get; init; }
}

internal sealed record CrossRuntimeReviewGateRecordEntry
{
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("runtime")] public required string Runtime { get; init; }
    [JsonPropertyName("runtime_version")] public required string RuntimeVersion { get; init; }
    [JsonPropertyName("relation")] public required string Relation { get; init; }
    [JsonPropertyName("head_sha")] public required string HeadSha { get; init; }
    [JsonPropertyName("verdict")] public required string Verdict { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
}
