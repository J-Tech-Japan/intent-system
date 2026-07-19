using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G536 review repair: the single, shared, fail-closed analyzer for the
/// three local durable artifacts that record a published GitHub issue for
/// an execution unit — <c>queue-state.json</c>'s <c>linked_issue</c>,
/// <c>publish.yaml</c>'s <c>issue-created</c> record, and
/// <c>runs.jsonl</c>'s <c>issue-created</c> event(s). Used by BOTH
/// <see cref="IssuePublishFlowCommand"/> (idempotent rerun / dry-run
/// planning) and <see cref="AutomationPublishRecoveryCommand"/> (gap
/// reporting), so the two surfaces can never disagree about which
/// artifacts are missing or in conflict for the same execution unit.
///
/// Read-only: never mutates any file. Callers restore missing artifacts
/// (using their own write helpers) and then re-invoke <see cref="Analyze"/>
/// to prove the restoration actually landed before claiming synced state
/// — this analyzer itself has no concept of "success," only "current
/// state."
/// </summary>
internal static class PublishDurableArtifactAnalyzer
{
    // Stable gap identifiers — shared verbatim between publish-flow and
    // publish-recovery output so an operator (or another tool) never has
    // to reconcile two different vocabularies for the same underlying gap.
    public const string GapQueueLinkedIssueMissing = "queue_linked_issue_missing";
    public const string GapPublishYamlMissing = "publish_yaml_missing";
    public const string GapRunsEventMissing = "runs_event_missing";

    // Fail-closed conditions — never treated as "missing" (which would
    // invite silent restoration/overwrite); always block identity
    // resolution entirely.
    public const string InvalidPublishYamlMalformed = "publish_yaml_malformed";
    public const string InvalidRunsMalformed = "runs_malformed";
    public const string InvalidRunsEventConflicting = "runs_event_conflicting";
    public const string InvalidCrossArtifactContradiction = "cross_artifact_contradiction";

    public static PublishDurableArtifactAnalysis Analyze(
        string executionUnit,
        string repo,
        string queueStatePath,
        string publishYamlPath,
        string runLogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var queueSignal = ReadQueueSignal(queueStatePath, executionUnit, repo);
        var publishSignal = ReadPublishSignal(publishYamlPath);
        var runsSignal = ReadRunsSignal(runLogPath, executionUnit);

        // Any artifact that is individually invalid (unreadable/malformed,
        // or runs carrying conflicting issue-created events) fails the
        // WHOLE analysis closed immediately — never collapsed into
        // "missing," which would invite silently overwriting data we
        // could not actually read.
        if (publishSignal.Kind == ArtifactSignalKind.Invalid)
        {
            return PublishDurableArtifactAnalysis.Invalid(
                InvalidPublishYamlMalformed, publishSignal.Detail!, publishYamlPath);
        }

        if (runsSignal.Kind == ArtifactSignalKind.Invalid)
        {
            return PublishDurableArtifactAnalysis.Invalid(
                runsSignal.InvalidReason!, runsSignal.Detail!, runLogPath);
        }

        var present = new List<(string Source, int? Number, string? Url)>();
        if (queueSignal.Kind == ArtifactSignalKind.Present)
        {
            present.Add(("queue-state.json", queueSignal.IssueNumber, queueSignal.IssueUrl));
        }
        if (publishSignal.Kind == ArtifactSignalKind.Present)
        {
            present.Add(("publish.yaml", publishSignal.IssueNumber, publishSignal.IssueUrl));
        }
        if (runsSignal.Kind == ArtifactSignalKind.Present)
        {
            present.Add(("runs.jsonl", runsSignal.IssueNumber, runsSignal.IssueUrl));
        }

        if (present.Count == 0)
        {
            return PublishDurableArtifactAnalysis.NoExistingIssue();
        }

        var distinctNumbers = present
            .Select(p => p.Number)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .Distinct()
            .ToArray();

        if (distinctNumbers.Length > 1)
        {
            var detail = string.Join("; ", present
                .Where(p => p.Number.HasValue)
                .Select(p => $"{p.Source} records issue #{p.Number}"));
            return PublishDurableArtifactAnalysis.Invalid(
                InvalidCrossArtifactContradiction,
                $"durable artifacts disagree on the issue number for '{executionUnit}': {detail}.",
                queueStatePath);
        }

        var canonicalNumber = distinctNumbers.Length == 1 ? distinctNumbers[0] : (int?)null;
        var canonicalUrl = present.Select(p => p.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
            ?? (canonicalNumber.HasValue ? $"https://github.com/{repo}/issues/{canonicalNumber.Value}" : null);

        var gaps = new List<string>();
        if (queueSignal.Kind != ArtifactSignalKind.Present)
        {
            gaps.Add(GapQueueLinkedIssueMissing);
        }
        if (publishSignal.Kind != ArtifactSignalKind.Present)
        {
            gaps.Add(GapPublishYamlMissing);
        }
        if (runsSignal.Kind != ArtifactSignalKind.Present)
        {
            gaps.Add(GapRunsEventMissing);
        }

        return PublishDurableArtifactAnalysis.ExistingIssue(canonicalNumber, canonicalUrl!, gaps);
    }

    private enum ArtifactSignalKind
    {
        /// <summary>No usable identity from this artifact (file absent, or present but not yet recording a created issue) — not an error.</summary>
        Absent,
        /// <summary>This artifact records a specific issue identity.</summary>
        Present,
        /// <summary>This artifact exists but could not be read/parsed, or (runs.jsonl only) records CONFLICTING issue-created events — fails the whole analysis closed.</summary>
        Invalid,
    }

    private readonly record struct ArtifactSignal(
        ArtifactSignalKind Kind,
        int? IssueNumber,
        string? IssueUrl,
        string? Detail,
        string? InvalidReason = null)
    {
        public static ArtifactSignal Absent() => new(ArtifactSignalKind.Absent, null, null, null);
        public static ArtifactSignal Present(int? number, string? url) => new(ArtifactSignalKind.Present, number, url, null);
        public static ArtifactSignal Invalid(string reason, string detail) => new(ArtifactSignalKind.Invalid, null, null, detail, reason);
    }

    private static ArtifactSignal ReadQueueSignal(string queueStatePath, string executionUnit, string repo)
    {
        if (!File.Exists(queueStatePath))
        {
            return ArtifactSignal.Absent();
        }

        try
        {
            var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            foreach (var item in queueState.Items)
            {
                if (!string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal))
                {
                    continue;
                }

                if (item.LinkedIssue is { } linked && !string.IsNullOrWhiteSpace(linked.Url))
                {
                    return ArtifactSignal.Present(linked.Number, linked.Url);
                }

                return ArtifactSignal.Absent();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            // A malformed queue-state.json is a pre-existing, separately
            // handled fail-closed condition (the G363 atomic-seed gate and
            // publish-flow's own queue-state read both already refuse to
            // proceed on an unparseable file) — treat as absent here so
            // this analyzer doesn't duplicate that diagnostic; the caller
            // will hit the existing queue-state-level refusal first.
        }

        return ArtifactSignal.Absent();
    }

    private static ArtifactSignal ReadPublishSignal(string publishYamlPath)
    {
        if (!File.Exists(publishYamlPath))
        {
            return ArtifactSignal.Absent();
        }

        IssuePublishArtifact artifact;
        try
        {
            artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishYamlPath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return ArtifactSignal.Invalid(InvalidPublishYamlMalformed, $"publish.yaml could not be parsed: {exception.Message}");
        }

        if (!string.Equals(artifact.PublishStatus, IssuePublishFlowCommand.PublishStatusIssueCreated, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(artifact.CreatedIssueUrl))
        {
            // Not yet published (still draft / different lifecycle stage) —
            // no identity signal from this source, not an error.
            return ArtifactSignal.Absent();
        }

        return ArtifactSignal.Present(artifact.CreatedIssueNumber, artifact.CreatedIssueUrl);
    }

    private static ArtifactSignal ReadRunsSignal(string runLogPath, string executionUnit)
    {
        if (!File.Exists(runLogPath))
        {
            return ArtifactSignal.Absent();
        }

        IReadOnlyList<RunEvent> events;
        try
        {
            events = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            return ArtifactSignal.Invalid(InvalidRunsMalformed, $"runs.jsonl could not be parsed: {exception.Message}");
        }

        var matching = events
            .Where(runEvent =>
                string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                && string.Equals(runEvent.Event, IssuePublishFlowCommand.IssueCreatedEventName, StringComparison.Ordinal))
            .ToArray();

        if (matching.Length == 0)
        {
            return ArtifactSignal.Absent();
        }

        var parsedIdentities = new List<(int? Number, string? Url)>();
        foreach (var runEvent in matching)
        {
            if (!TryParseRunsIssueIdentity(runEvent, out var number, out var url))
            {
                return ArtifactSignal.Invalid(
                    InvalidRunsMalformed,
                    $"an issue-created run event for '{executionUnit}' could not be parsed into a recognizable "
                    + "issue identity (neither linked_issue nor reason matched a supported repo#number or issue-URL shape).");
            }

            parsedIdentities.Add((number, url));
        }

        var distinctNumbers = parsedIdentities
            .Select(p => p.Number)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .Distinct()
            .ToArray();

        if (distinctNumbers.Length > 1)
        {
            return ArtifactSignal.Invalid(
                InvalidRunsEventConflicting,
                $"runs.jsonl carries {matching.Length} issue-created events for '{executionUnit}' naming "
                + $"conflicting issue numbers: {string.Join(", ", distinctNumbers.Select(n => $"#{n}"))}.");
        }

        // Duplicate-IDENTICAL events (same number repeated) are fine — not
        // a gap, not an error; they simply all agree.
        var canonical = parsedIdentities[0];
        return ArtifactSignal.Present(canonical.Number, canonical.Url);
    }

    /// <summary>
    /// G536 review repair: parses an issue-created run event's identity
    /// from whichever documented shape is present. The canonical shape
    /// (<see cref="IssuePublishFlowCommand.AppendIssueCreatedRunEvent"/>)
    /// writes <c>linked_issue: "&lt;repo&gt;#&lt;number&gt;"</c> and
    /// <c>reason: "&lt;issue-url&gt;"</c>. Historical events recorded before
    /// that convention may carry only a raw issue URL in either field.
    /// Returns false when neither field matches a recognized shape at all
    /// — this is what makes an unrecognizable event MALFORMED rather than
    /// silently treated as absent.
    /// </summary>
    private static bool TryParseRunsIssueIdentity(RunEvent runEvent, out int? number, out string? url)
    {
        number = null;
        url = null;

        if (!string.IsNullOrWhiteSpace(runEvent.LinkedIssue))
        {
            var hashIndex = runEvent.LinkedIssue.LastIndexOf('#');
            if (hashIndex >= 0
                && hashIndex + 1 < runEvent.LinkedIssue.Length
                && int.TryParse(
                    runEvent.LinkedIssue[(hashIndex + 1)..],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedFromDescriptor))
            {
                number = parsedFromDescriptor;
                url = !string.IsNullOrWhiteSpace(runEvent.Reason) ? runEvent.Reason : null;
                return true;
            }

            if (TryParseIssueNumberFromUrl(runEvent.LinkedIssue, out var parsedFromLinkedUrl))
            {
                number = parsedFromLinkedUrl;
                url = runEvent.LinkedIssue;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(runEvent.Reason) && TryParseIssueNumberFromUrl(runEvent.Reason, out var parsedFromReason))
        {
            number = parsedFromReason;
            url = runEvent.Reason;
            return true;
        }

        return false;
    }

    private static bool TryParseIssueNumberFromUrl(string candidate, out int number)
    {
        number = 0;
        if (!candidate.Contains("/issues/", StringComparison.Ordinal))
        {
            return false;
        }

        var index = candidate.LastIndexOf('/');
        if (index < 0 || index + 1 >= candidate.Length)
        {
            return false;
        }

        return int.TryParse(
            candidate[(index + 1)..],
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out number);
    }
}

/// <summary>
/// G536 review repair: the outcome of <see cref="PublishDurableArtifactAnalyzer.Analyze"/>.
/// Exactly one of three shapes: no existing issue (safe to create), an
/// existing issue with zero or more missing-artifact gaps to restore, or
/// an invalid/contradictory state that must fail closed and never create.
/// </summary>
internal sealed record PublishDurableArtifactAnalysis
{
    public required bool HasExistingIssue { get; init; }

    public int? CanonicalIssueNumber { get; init; }

    public string? CanonicalIssueUrl { get; init; }

    /// <summary>Stable gap identifiers for artifacts missing relative to the canonical issue. Empty when fully synced.</summary>
    public required IReadOnlyList<string> Gaps { get; init; }

    /// <summary>Set only when the analysis fails closed (malformed data or a cross-artifact contradiction) — never create, never silently restore.</summary>
    public string? InvalidReason { get; init; }

    public string? InvalidDetail { get; init; }

    public string? InvalidArtifactPath { get; init; }

    public bool IsInvalid => InvalidReason is not null;

    public bool IsFullySynced => HasExistingIssue && !IsInvalid && Gaps.Count == 0;

    public static PublishDurableArtifactAnalysis NoExistingIssue() => new()
    {
        HasExistingIssue = false,
        Gaps = Array.Empty<string>(),
    };

    public static PublishDurableArtifactAnalysis ExistingIssue(int? issueNumber, string issueUrl, IReadOnlyList<string> gaps) => new()
    {
        HasExistingIssue = true,
        CanonicalIssueNumber = issueNumber,
        CanonicalIssueUrl = issueUrl,
        Gaps = gaps,
    };

    public static PublishDurableArtifactAnalysis Invalid(string reason, string detail, string artifactPath) => new()
    {
        HasExistingIssue = true,
        Gaps = Array.Empty<string>(),
        InvalidReason = reason,
        InvalidDetail = detail,
        InvalidArtifactPath = artifactPath,
    };
}
