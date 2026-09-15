using System.Text.Json.Serialization;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834/G835: the one gate evaluator shared by cross-runtime review status and
/// publish-flow / pr-transition.
/// </summary>
internal static class CrossRuntimeReviewGate
{
    public const string DecisionNotRequired = "not-required";
    public const string DecisionSatisfied = "satisfied";
    public const string DecisionBlocked = "blocked";
    public const string DecisionMissing = "missing";
    public const string DecisionIdempotentNotGated = "idempotent-not-gated";

    public const string StatusDeciding = "deciding";
    public const string StatusSuperseded = "superseded";
    public const string StatusStaleHead = "stale-head";
    public const string StatusStaleDigest = "stale-digest";
    public const string StatusStaleEpoch = "stale-epoch";
    public const string StatusForeign = "foreign";

    public static CrossRuntimeReviewGateResult Evaluate(
        CrossRuntimeReviewTeamDeclaration? declaration,
        CrossRuntimeReviewResolution resolution,
        string headSha,
        CrossRuntimeReviewReadResult read) =>
        EvaluateImplementation(declaration, resolution, headSha, read);

    public static CrossRuntimeReviewGateResult EvaluateDesign(
        CrossRuntimeReviewTeamDeclaration? declaration,
        CrossRuntimeReviewResolution resolution,
        string packetDigest,
        CrossRuntimeDesignReviewReadResult read)
    {
        if (declaration is null)
        {
            return NotRequired(read.Unreadable);
        }

        var conductor = declaration.ConductorRuntime;
        var entries = new List<CrossRuntimeReviewGateRecordEntry>();
        var matching = new List<CrossRuntimeDesignReviewStoredRecord>();
        foreach (var stored in read.Records)
        {
            var record = stored.Record;
            var isMatch = string.Equals(record.ExecutionUnit, resolution.ExecutionUnit, StringComparison.Ordinal)
                && string.Equals(record.Domain, resolution.Domain, StringComparison.Ordinal)
                && string.Equals(record.Team, resolution.Team, StringComparison.Ordinal)
                && string.Equals(record.Kind, CrossRuntimeReviewRecord.KindDesign, StringComparison.Ordinal);
            if (isMatch)
            {
                matching.Add(stored);
            }
            else
            {
                entries.Add(DesignEntry(stored, conductor, StatusForeign));
            }
        }

        var epochBoundary = ComputeEpochBoundary(matching, packetDigest);
        var onDigest = matching
            .Where(stored => IsDigest(stored.Record, packetDigest) && IsInCurrentEpoch(stored, epochBoundary))
            .ToArray();
        var latestOnDigest = onDigest
            .GroupBy(stored => stored.Record.Runtime, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        foreach (var stored in matching)
        {
            var status = !IsDigest(stored.Record, packetDigest)
                ? StatusStaleDigest
                : !IsInCurrentEpoch(stored, epochBoundary)
                    ? StatusStaleEpoch
                    : ReferenceEquals(latestOnDigest.GetValueOrDefault(stored.Record.Runtime), stored) ? StatusDeciding : StatusSuperseded;
            entries.Add(DesignEntry(stored, conductor, status));
        }

        return BuildDecision(
            conductor,
            packetDigest,
            latestOnDigest.Values.Select(stored => (stored.Record.Runtime, stored.Record.Verdict, stored.Record.PacketDigest)).ToArray(),
            matching.Select(stored => (stored.Record.Runtime, stored.Record.Verdict, stored.Record.PacketDigest, stored.Record.RecordedAt, stored.FileName)).ToArray(),
            read.Unreadable,
            entries,
            keyLabel: "digest");
    }

    private static CrossRuntimeReviewGateResult EvaluateImplementation(
        CrossRuntimeReviewTeamDeclaration? declaration,
        CrossRuntimeReviewResolution resolution,
        string headSha,
        CrossRuntimeReviewReadResult read)
    {
        if (declaration is null)
        {
            return NotRequired(read.Unreadable);
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
                entries.Add(ImplementationEntry(stored, conductor, StatusForeign));
            }
        }

        var onHead = matching.Where(stored => IsHead(stored.Record, headSha)).ToArray();
        var latestOnHead = onHead
            .GroupBy(stored => stored.Record.Runtime, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        foreach (var stored in matching)
        {
            var status = !IsHead(stored.Record, headSha)
                ? StatusStaleHead
                : ReferenceEquals(latestOnHead.GetValueOrDefault(stored.Record.Runtime), stored) ? StatusDeciding : StatusSuperseded;
            entries.Add(ImplementationEntry(stored, conductor, status));
        }

        return BuildDecision(
            conductor,
            headSha,
            latestOnHead.Values.Select(stored => (stored.Record.Runtime, stored.Record.Verdict, stored.Record.HeadSha)).ToArray(),
            matching.Select(stored => (stored.Record.Runtime, stored.Record.Verdict, stored.Record.HeadSha, stored.Record.RecordedAt, stored.FileName)).ToArray(),
            read.Unreadable,
            entries,
            keyLabel: "head");
    }

    private static CrossRuntimeReviewGateResult NotRequired(IReadOnlyList<CrossRuntimeReviewUnreadableRecord> unreadable) =>
        new()
        {
            Decision = DecisionNotRequired,
            Reasons = [],
            Records = [],
            Unreadable = unreadable,
        };

    private static CrossRuntimeReviewGateResult BuildDecision(
        string conductor,
        string key,
        IReadOnlyList<(string Runtime, string Verdict, string RecordKey)> latestOnKey,
        IReadOnlyList<(string Runtime, string Verdict, string RecordKey, DateTimeOffset RecordedAt, string FileName)> matching,
        IReadOnlyList<CrossRuntimeReviewUnreadableRecord> unreadable,
        List<CrossRuntimeReviewGateRecordEntry> entries,
        string keyLabel)
    {
        var reasons = new List<CrossRuntimeReviewGateReason>();
        foreach (var item in unreadable)
        {
            reasons.Add(new CrossRuntimeReviewGateReason
            {
                Cause = CrossRuntimeReviewCauses.RecordUnreadable,
                Detail = $"record '{item.RelativePath}' failed validation and fails the gate closed: {item.Error}",
                File = item.RelativePath,
            });
        }

        var blockedRuntimes = latestOnKey
            .Where(item => string.Equals(item.Verdict, CrossRuntimeReviewVerdict.RequestChanges, StringComparison.Ordinal))
            .Select(item => item.Runtime)
            .OrderBy(runtime => runtime, StringComparer.Ordinal)
            .ToArray();
        if (blockedRuntimes.Length > 0)
        {
            reasons.Add(new CrossRuntimeReviewGateReason
            {
                Cause = CrossRuntimeReviewCauses.Blocked,
                Detail = $"the latest record on {keyLabel} {key} from {string.Join(", ", blockedRuntimes)} is request-changes.",
                Runtimes = blockedRuntimes,
            });
        }

        var latestOnKeyByRuntime = latestOnKey.ToDictionary(item => item.Runtime, item => item, StringComparer.Ordinal);
        foreach (var runtime in matching
                     .Select(item => item.Runtime)
                     .Distinct(StringComparer.Ordinal)
                     .Where(runtime => !latestOnKeyByRuntime.ContainsKey(runtime))
                     .OrderBy(runtime => runtime, StringComparer.Ordinal))
        {
            var latestEarlier = matching.Last(item => string.Equals(item.Runtime, runtime, StringComparison.Ordinal));
            if (string.Equals(latestEarlier.Verdict, CrossRuntimeReviewVerdict.RequestChanges, StringComparison.Ordinal))
            {
                reasons.Add(new CrossRuntimeReviewGateReason
                {
                    Cause = CrossRuntimeReviewCauses.RereviewMissing,
                    Detail = $"{runtime} requested changes on {keyLabel} {latestEarlier.RecordKey} and has no record on {keyLabel} {key}; that runtime must re-review the delta.",
                    Runtimes = [runtime],
                });
            }
        }

        bool HasApprove(string relation) => latestOnKey.Any(item =>
            string.Equals(CrossRuntimeReviewRecord.RelationFor(item.Runtime, conductor), relation, StringComparison.Ordinal)
            && string.Equals(item.Verdict, CrossRuntimeReviewVerdict.Approve, StringComparison.Ordinal));

        foreach (var relation in new[] { CrossRuntimeReviewRecord.RelationSameRuntime, CrossRuntimeReviewRecord.RelationCrossRuntime })
        {
            if (!HasApprove(relation))
            {
                reasons.Add(new CrossRuntimeReviewGateReason
                {
                    Cause = CrossRuntimeReviewCauses.Missing,
                    Detail = relation == CrossRuntimeReviewRecord.RelationSameRuntime
                        ? $"{keyLabel} {key} has no approve whose latest record comes from the conductor runtime '{conductor}' (independent same-runtime subagent review)."
                        : $"{keyLabel} {key} has no approve whose latest record comes from a runtime other than the conductor runtime '{conductor}' (cross-runtime review).",
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
            Unreadable = unreadable,
        };
    }

    private sealed record EpochBoundary(DateTimeOffset RecordedAt, string FileName);

    private static EpochBoundary? ComputeEpochBoundary(
        IReadOnlyList<CrossRuntimeDesignReviewStoredRecord> matching,
        string packetDigest)
    {
        var latestOther = matching
            .Where(stored => !IsDigest(stored.Record, packetDigest))
            .OrderBy(stored => stored.Record.RecordedAt)
            .ThenBy(stored => stored.FileName, StringComparer.Ordinal)
            .LastOrDefault();
        return latestOther is null ? null : new EpochBoundary(latestOther.Record.RecordedAt, latestOther.FileName);
    }

    private static bool IsInCurrentEpoch(CrossRuntimeDesignReviewStoredRecord stored, EpochBoundary? boundary)
    {
        if (boundary is null)
        {
            return true;
        }

        if (stored.Record.RecordedAt > boundary.RecordedAt)
        {
            return true;
        }

        if (stored.Record.RecordedAt < boundary.RecordedAt)
        {
            return false;
        }

        return string.CompareOrdinal(stored.FileName, boundary.FileName) > 0;
    }

    private static bool IsHead(CrossRuntimeReviewRecord record, string headSha) =>
        string.Equals(record.HeadSha, headSha, StringComparison.OrdinalIgnoreCase);

    private static bool IsDigest(CrossRuntimeDesignReviewRecord record, string packetDigest) =>
        string.Equals(record.PacketDigest, packetDigest, StringComparison.OrdinalIgnoreCase);

    private static CrossRuntimeReviewGateRecordEntry ImplementationEntry(
        CrossRuntimeReviewStoredRecord stored,
        string conductor,
        string status) =>
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

    private static CrossRuntimeReviewGateRecordEntry DesignEntry(
        CrossRuntimeDesignReviewStoredRecord stored,
        string conductor,
        string status) =>
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
            PacketDigest = stored.Record.PacketDigest,
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
    [JsonPropertyName("head_sha")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HeadSha { get; init; }
    [JsonPropertyName("packet_digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PacketDigest { get; init; }
    [JsonPropertyName("verdict")] public required string Verdict { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
}
