using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// Round-4 review repair: the canonical identity is a full tuple — exact
/// repo, positive issue number, canonical GitHub issue URL — not merely a
/// number. Every present signal's repo is validated against the confirmed
/// target repo (not just against each other), every present signal must be
/// internally self-consistent (its own number/url/repo fields must agree
/// with each other), and a malformed/unreadable queue-state.json now fails
/// closed exactly like publish.yaml/runs.jsonl already did, rather than
/// being silently treated as absent.
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
    public const string InvalidQueueStateMalformed = "queue_state_malformed";
    public const string InvalidQueueStateDuplicateExecutionUnit = "queue_state_duplicate_execution_unit";
    public const string InvalidPublishYamlMalformed = "publish_yaml_malformed";
    public const string InvalidRunsMalformed = "runs_malformed";
    public const string InvalidRunsEventConflicting = "runs_event_conflicting";
    public const string InvalidCrossArtifactContradiction = "cross_artifact_contradiction";

    private static readonly Regex GithubIssueUrlRegex = new(
        @"^https://github\.com/(?<repo>[^\s/]+/[^\s/]+)/issues/(?<number>\d+)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RepoNumberDescriptorRegex = new(
        @"^(?<repo>[^\s/]+/[^\s/]+)#(?<number>\d+)$",
        RegexOptions.Compiled);

    private static readonly Regex BareNumberDescriptorRegex = new(
        @"^#(?<number>\d+)$",
        RegexOptions.Compiled);

    public static PublishDurableArtifactAnalysis Analyze(
        string executionUnit,
        string repo,
        string queueStatePath,
        string publishYamlPath,
        string runLogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var queueSignal = ReadQueueSignal(queueStatePath, executionUnit);
        if (queueSignal.Kind == ArtifactSignalKind.Invalid)
        {
            return PublishDurableArtifactAnalysis.Invalid(
                queueSignal.InvalidReason!, queueSignal.Detail!, queueStatePath);
        }

        var publishSignal = ReadPublishSignal(publishYamlPath, executionUnit);
        if (publishSignal.Kind == ArtifactSignalKind.Invalid)
        {
            return PublishDurableArtifactAnalysis.Invalid(
                publishSignal.InvalidReason!, publishSignal.Detail!, publishYamlPath);
        }

        var runsSignal = ReadRunsSignal(runLogPath, executionUnit);
        if (runsSignal.Kind == ArtifactSignalKind.Invalid)
        {
            return PublishDurableArtifactAnalysis.Invalid(
                runsSignal.InvalidReason!, runsSignal.Detail!, runLogPath);
        }

        var present = new List<IdentityEntry>();
        if (queueSignal.Kind == ArtifactSignalKind.Present)
        {
            present.Add(new IdentityEntry("queue-state.json", queueSignal.Repo, queueSignal.IssueNumber, queueSignal.IssueUrl));
        }
        if (publishSignal.Kind == ArtifactSignalKind.Present)
        {
            present.Add(new IdentityEntry("publish.yaml", publishSignal.Repo, publishSignal.IssueNumber, publishSignal.IssueUrl));
        }
        if (runsSignal.Kind == ArtifactSignalKind.Present)
        {
            present.Add(new IdentityEntry("runs.jsonl", runsSignal.Repo, runsSignal.IssueNumber, runsSignal.IssueUrl));
        }

        if (present.Count == 0)
        {
            return PublishDurableArtifactAnalysis.NoExistingIssue();
        }

        // The confirmed target repo participates in reconciliation as its
        // own entry — any present signal whose repo disagrees with the
        // repo this command run is scoped to is a genuine contradiction,
        // not merely a mismatch between two artifacts.
        var reconciliationEntries = new List<IdentityEntry> { new("--repo (confirmed target)", repo, null, null) };
        reconciliationEntries.AddRange(present);

        if (!TryReconcileIdentities(reconciliationEntries, out var reconciled, out var conflictDetail))
        {
            return PublishDurableArtifactAnalysis.Invalid(
                InvalidCrossArtifactContradiction,
                $"durable artifacts disagree on the canonical issue identity for '{executionUnit}': {conflictDetail}.",
                queueStatePath);
        }

        var canonicalNumber = reconciled.Number;
        var canonicalUrl = reconciled.Url
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

    private readonly record struct IdentityEntry(string Source, string? Repo, int? Number, string? Url);

    /// <summary>
    /// Field-by-field n-way reconciliation shared by both the top-level
    /// cross-artifact check and the within-runs.jsonl multi-event check.
    /// Any field (repo case-insensitive, number, url exact) with more than
    /// one DISTINCT non-null value among the entries is a contradiction.
    /// Entries that leave a field null (an unknown/unparseable value, e.g.
    /// a legacy runs event with no repo prefix) never contradict — only
    /// values that are actually present can disagree.
    /// </summary>
    private static bool TryReconcileIdentities(
        IReadOnlyList<IdentityEntry> entries,
        out IdentityEntry reconciled,
        out string conflictDetail)
    {
        var distinctRepos = entries
            .Select(e => e.Repo)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var distinctNumbers = entries
            .Select(e => e.Number)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .Distinct()
            .ToArray();
        var distinctUrls = entries
            .Select(e => e.Url)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctRepos.Length > 1 || distinctNumbers.Length > 1 || distinctUrls.Length > 1)
        {
            var parts = entries
                .Where(e => e.Repo is not null || e.Number.HasValue || e.Url is not null)
                .Select(e => $"{e.Source} records repo={e.Repo ?? "?"}, number={(e.Number.HasValue ? e.Number.Value.ToString(CultureInfo.InvariantCulture) : "?")}, url={e.Url ?? "?"}");
            conflictDetail = string.Join("; ", parts);
            reconciled = default;
            return false;
        }

        conflictDetail = string.Empty;
        reconciled = new IdentityEntry(
            "reconciled",
            distinctRepos.Length == 1 ? distinctRepos[0] : null,
            distinctNumbers.Length == 1 ? distinctNumbers[0] : null,
            distinctUrls.Length == 1 ? distinctUrls[0] : null);
        return true;
    }

    private enum ArtifactSignalKind
    {
        /// <summary>No usable identity from this artifact (file absent, or present but not yet recording a created issue) — not an error.</summary>
        Absent,
        /// <summary>This artifact records a specific, internally self-consistent issue identity.</summary>
        Present,
        /// <summary>This artifact exists but could not be read/parsed, is internally self-contradictory, or (runs.jsonl only) records CONFLICTING issue-created events — fails the whole analysis closed.</summary>
        Invalid,
    }

    private readonly record struct ArtifactSignal(
        ArtifactSignalKind Kind,
        string? Repo,
        int? IssueNumber,
        string? IssueUrl,
        string? Detail,
        string? InvalidReason = null)
    {
        public static ArtifactSignal Absent() => new(ArtifactSignalKind.Absent, null, null, null, null);
        public static ArtifactSignal Present(string? repo, int? number, string? url) => new(ArtifactSignalKind.Present, repo, number, url, null);
        public static ArtifactSignal Invalid(string reason, string detail) => new(ArtifactSignalKind.Invalid, null, null, null, detail, reason);
    }

    private static ArtifactSignal ReadQueueSignal(string queueStatePath, string executionUnit)
    {
        if (!File.Exists(queueStatePath))
        {
            return ArtifactSignal.Absent();
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            // Round-4 review repair: a malformed queue-state.json must fail
            // closed here too — publish.yaml or runs.jsonl surviving alone
            // must never authorize restoration/repair around a
            // queue-state.json this analyzer could not actually read.
            return ArtifactSignal.Invalid(InvalidQueueStateMalformed, $"queue-state.json could not be parsed: {exception.Message}");
        }

        // Round-5 review repair: collect EVERY item matching this execution
        // unit rather than returning on the first match — identity must
        // never depend on JSON array order, and a second item (whether it
        // agrees or conflicts with the first) is itself a data-integrity
        // problem that must fail closed, not be silently shadowed.
        var matchingIndices = new List<int>();
        for (var index = 0; index < queueState.Items.Count; index++)
        {
            if (string.Equals(queueState.Items[index].ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                matchingIndices.Add(index);
            }
        }

        if (matchingIndices.Count == 0)
        {
            return ArtifactSignal.Absent();
        }

        if (matchingIndices.Count > 1)
        {
            var detail = string.Join("; ", matchingIndices.Select(index =>
            {
                var linkedIssue = queueState.Items[index].LinkedIssue;
                return $"items[{index}] linked_issue={(linkedIssue is null ? "null" : $"{linkedIssue.Repo}#{linkedIssue.Number} ({linkedIssue.Url})")}";
            }));
            return ArtifactSignal.Invalid(InvalidQueueStateDuplicateExecutionUnit,
                $"queue-state.json has {matchingIndices.Count} items for execution_unit '{executionUnit}': {detail}.");
        }

        var item = queueState.Items[matchingIndices[0]];
        if (item.LinkedIssue is not { } linked || string.IsNullOrWhiteSpace(linked.Url))
        {
            return ArtifactSignal.Absent();
        }

        if (linked.Number is not int number || number <= 0)
        {
            return ArtifactSignal.Invalid(InvalidQueueStateMalformed,
                $"queue-state.json linked_issue for '{executionUnit}' has no positive issue number.");
        }

        if (!TryParseGithubIssueUrl(linked.Url, out var urlRepo, out var urlNumber) || urlNumber != number)
        {
            return ArtifactSignal.Invalid(InvalidQueueStateMalformed,
                $"queue-state.json linked_issue for '{executionUnit}' is internally inconsistent: number={number}, url='{linked.Url}'.");
        }

        if (!string.IsNullOrWhiteSpace(linked.Repo) && !string.Equals(linked.Repo, urlRepo, StringComparison.OrdinalIgnoreCase))
        {
            return ArtifactSignal.Invalid(InvalidQueueStateMalformed,
                $"queue-state.json linked_issue for '{executionUnit}' is internally inconsistent: repo='{linked.Repo}' does not match url='{linked.Url}'.");
        }

        return ArtifactSignal.Present(!string.IsNullOrWhiteSpace(linked.Repo) ? linked.Repo : urlRepo, number, linked.Url);
    }

    private static ArtifactSignal ReadPublishSignal(string publishYamlPath, string executionUnit)
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

        // Round-4 review repair: a publish.yaml whose own execution_unit
        // field doesn't match the unit this path is scoped to is corrupted
        // data (e.g. copied from another unit's packet), not a genuine
        // signal for THIS unit — must fail closed, never be silently
        // accepted because the issue number happened to look plausible.
        if (!string.Equals(artifact.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            return ArtifactSignal.Invalid(InvalidPublishYamlMalformed,
                $"publish.yaml at the '{executionUnit}' packet path records execution_unit '{artifact.ExecutionUnit}', not '{executionUnit}'.");
        }

        if (artifact.CreatedIssueNumber is not int number || number <= 0)
        {
            return ArtifactSignal.Invalid(InvalidPublishYamlMalformed,
                $"publish.yaml for '{executionUnit}' has no positive created_issue_number.");
        }

        if (!TryParseGithubIssueUrl(artifact.CreatedIssueUrl, out var urlRepo, out var urlNumber) || urlNumber != number)
        {
            return ArtifactSignal.Invalid(InvalidPublishYamlMalformed,
                $"publish.yaml for '{executionUnit}' created_issue_url ('{artifact.CreatedIssueUrl}') is not a canonical GitHub issue URL matching created_issue_number {number}.");
        }

        return ArtifactSignal.Present(urlRepo, number, artifact.CreatedIssueUrl);
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

        var identities = new List<IdentityEntry>();
        for (var index = 0; index < matching.Length; index++)
        {
            if (!TryParseRunsIssueIdentity(matching[index], out var repo, out var number, out var url))
            {
                return ArtifactSignal.Invalid(
                    InvalidRunsMalformed,
                    $"an issue-created run event for '{executionUnit}' could not be parsed into a recognizable, "
                    + "self-consistent issue identity (neither linked_issue nor reason matched a supported "
                    + "repo#number or issue-URL shape, or the two fields contradicted each other).");
            }

            identities.Add(new IdentityEntry($"runs.jsonl[{index}]", repo, number, url));
        }

        // Duplicate-IDENTICAL events (every field agrees) are fine — not a
        // gap, not an error. Events that disagree on ANY field (repo,
        // number, or url) are a genuine conflict, not a coincidence.
        if (!TryReconcileIdentities(identities, out var reconciled, out var conflictDetail))
        {
            return ArtifactSignal.Invalid(
                InvalidRunsEventConflicting,
                $"runs.jsonl carries {matching.Length} issue-created event(s) for '{executionUnit}' naming conflicting identities: {conflictDetail}.");
        }

        return ArtifactSignal.Present(reconciled.Repo, reconciled.Number, reconciled.Url);
    }

    /// <summary>
    /// G536 review repair: parses an issue-created run event's identity
    /// from whichever documented shape is present, into a full
    /// (repo, number, url) tuple. The canonical shape (see
    /// <see cref="IssuePublishFlowCommand.AppendIssueCreatedRunEvent"/>)
    /// writes <c>linked_issue: "&lt;repo&gt;#&lt;number&gt;"</c> and
    /// <c>reason: "&lt;issue-url&gt;"</c>. Historical events recorded
    /// before that convention may carry only a raw issue URL, or a bare
    /// <c>#&lt;number&gt;</c> with no repo prefix, in either field.
    ///
    /// Round-4 review repair: a canonical GitHub issue URL is required —
    /// any <c>/issues/</c>-containing string that does NOT match
    /// <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;/issues/&lt;number&gt;</c>
    /// exactly is no longer accepted as if it were canonical. When both
    /// <c>linked_issue</c> and <c>reason</c> yield a repo and/or number,
    /// they must agree with each other — a single event whose own fields
    /// contradict each other is itself malformed, not merely "missing a
    /// repo." Returns false whenever no number can be resolved at all, or
    /// the event's own fields are self-contradictory.
    /// </summary>
    private static bool TryParseRunsIssueIdentity(RunEvent runEvent, out string? repo, out int? number, out string? url)
    {
        repo = null;
        number = null;
        url = null;

        string? descriptorRepo = null;
        int? descriptorNumber = null;
        string? urlRepo = null;
        int? urlNumber = null;
        string? urlValue = null;

        if (!string.IsNullOrWhiteSpace(runEvent.LinkedIssue))
        {
            var candidate = runEvent.LinkedIssue.Trim();
            if (TryParseGithubIssueUrl(candidate, out var parsedRepo, out var parsedNumber))
            {
                urlRepo = parsedRepo;
                urlNumber = parsedNumber;
                urlValue = candidate;
            }
            else
            {
                var descriptorMatch = RepoNumberDescriptorRegex.Match(candidate);
                if (descriptorMatch.Success)
                {
                    descriptorRepo = descriptorMatch.Groups["repo"].Value;
                    descriptorNumber = int.Parse(descriptorMatch.Groups["number"].Value, CultureInfo.InvariantCulture);
                }
                else
                {
                    var bareMatch = BareNumberDescriptorRegex.Match(candidate);
                    if (bareMatch.Success)
                    {
                        descriptorNumber = int.Parse(bareMatch.Groups["number"].Value, CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(runEvent.Reason)
            && TryParseGithubIssueUrl(runEvent.Reason.Trim(), out var reasonRepo, out var reasonNumber))
        {
            if (urlNumber is int existingUrlNumber && existingUrlNumber != reasonNumber)
            {
                return false;
            }
            if (urlRepo is not null && !string.Equals(urlRepo, reasonRepo, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            urlRepo ??= reasonRepo;
            urlNumber ??= reasonNumber;
            urlValue ??= runEvent.Reason.Trim();
        }

        if (descriptorNumber is int dNum && urlNumber is int uNum && dNum != uNum)
        {
            return false;
        }
        if (descriptorRepo is not null && urlRepo is not null
            && !string.Equals(descriptorRepo, urlRepo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var resolvedNumber = urlNumber ?? descriptorNumber;
        if (resolvedNumber is not int finalNumber || finalNumber <= 0)
        {
            return false;
        }

        repo = urlRepo ?? descriptorRepo;
        number = finalNumber;
        url = urlValue;
        return true;
    }

    private static bool TryParseGithubIssueUrl(string candidate, out string repo, out int number)
    {
        repo = string.Empty;
        number = 0;
        var match = GithubIssueUrlRegex.Match(candidate.Trim());
        if (!match.Success)
        {
            return false;
        }

        repo = match.Groups["repo"].Value;
        return int.TryParse(
            match.Groups["number"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out number) && number > 0;
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
