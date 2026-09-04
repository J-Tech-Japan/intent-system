using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only evidence used by G741/G788 to distinguish a delivered delegation
/// without an observable start from a delegation whose execution has become
/// visible. Unknown or unreadable evidence is deliberately not a finding.
/// </summary>
internal sealed record NotifyDelegationExecutionEvidence
{
    public bool? PendingLedgerPresent { get; init; }
    public bool? ReportOutboxPresent { get; init; }
    public bool? NotificationEventsPresent { get; init; }
    public bool? QueueStatePresent { get; init; }
    public bool? ContinuationChainPresent { get; init; }
    public bool? ExpectedArtifactPresent { get; init; }
    public required string PendingLedgerSource { get; init; }
    public required string ReportOutboxSource { get; init; }
    public required string NotificationEventsSource { get; init; }
    public required string QueueStateSource { get; init; }
    public required string ContinuationChainSource { get; init; }
    public required string ExpectedArtifactSource { get; init; }
    public string? ExecutionUnitToken { get; init; }
    public bool HasTokenlessDownstreamDelegation { get; init; }
    public IReadOnlyList<string> PendingLedgerDetails { get; init; } = [];
    public IReadOnlyList<string> ReportOutboxDetails { get; init; } = [];
    public IReadOnlyList<string> NotificationEventDetails { get; init; } = [];
    public IReadOnlyList<string> QueueStateDetails { get; init; } = [];
    public IReadOnlyList<string> ContinuationChainDetails { get; init; } = [];
    public IReadOnlyList<string> ExpectedArtifactDetails { get; init; } = [];
    public IReadOnlyList<string> TokenlessDownstreamDetails { get; init; } = [];
    public string? Error { get; init; }

    internal bool IsResolved => Error is null
        && PendingLedgerPresent is not null
        && ReportOutboxPresent is not null
        && NotificationEventsPresent is not null
        && QueueStatePresent is not null
        && ContinuationChainPresent is not null
        && ExpectedArtifactPresent is not null;

    internal bool HasExecutionEvidence => IsResolved
        && (PendingLedgerPresent == true
            || ReportOutboxPresent == true
            || NotificationEventsPresent == true
            || QueueStatePresent == true
            || ContinuationChainPresent == true
            || ExpectedArtifactPresent == true);

    /// <summary>
    /// The fixed, source-derived wording used by the true-stall summary. The
    /// numbers are derived from the same details persisted in the cycle, so a
    /// conclusion never claims an absence that the resolver did not measure.
    /// </summary>
    internal string SourceCountSummary => string.Join(
        "; ",
        $"pending-ledger={PendingLedgerDetails.Count}",
        $"report-outbox={ReportOutboxDetails.Count}",
        $"notification-events={NotificationEventDetails.Count}",
        $"queue-state={QueueStateDetails.Count}",
        $"continuation-chain={ContinuationChainDetails.Count}",
        $"expected-artifact={ExpectedArtifactDetails.Count}");

    internal IReadOnlyList<string> BuildConsultedObservations(string taskId)
    {
        var observations = new List<string>
        {
            $"delegation-execution-evidence: task_id={taskId}; unit={ExecutionUnitToken ?? "<none>"}; {SourceCountSummary}",
            $"pending-ledger:count={PendingLedgerDetails.Count}; source={PendingLedgerSource}",
            $"report-outbox:count={ReportOutboxDetails.Count}; source={ReportOutboxSource}",
            $"notification-events:count={NotificationEventDetails.Count}; source={NotificationEventsSource}",
            $"queue-state:count={QueueStateDetails.Count}; source={QueueStateSource}",
            $"continuation-chain:count={ContinuationChainDetails.Count}; source={ContinuationChainSource}",
            $"expected-artifact:count={ExpectedArtifactDetails.Count}; source={ExpectedArtifactSource}",
        };
        observations.AddRange(PendingLedgerDetails);
        observations.AddRange(ReportOutboxDetails);
        observations.AddRange(NotificationEventDetails);
        observations.AddRange(QueueStateDetails);
        observations.AddRange(ContinuationChainDetails);
        observations.AddRange(ExpectedArtifactDetails);
        observations.AddRange(TokenlessDownstreamDetails);
        return observations;
    }

    internal static NotifyDelegationExecutionEvidence Resolve(
        string routingRoot,
        NotifyPendingDelegation record,
        DateTimeOffset deliveredAt)
    {
        string pendingLedgerPath;
        string reportOutboxPath;
        string queueStatePath;
        string continuationPath;
        try
        {
            pendingLedgerPath = NotifyPendingDelegationStore.ResolvePath(routingRoot, record.Domain, record.Team);
            reportOutboxPath = NotifyReportOutboxStore.ResolvePath(routingRoot, record.Domain, record.Team);
            queueStatePath = CliRuntimeContracts.GetQueueStatePath(routingRoot);
            continuationPath = ContinuationChainStore.ResolvePath(routingRoot, record.Domain, record.Team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Unresolved(routingRoot, exception.Message);
        }

        var eventPaths = ResolveEventPaths(routingRoot, record, out var eventResolutionError);
        var pendingDetails = new List<string>();
        var reportOutboxDetails = new List<string>();
        var notificationEventDetails = new List<string>();
        var queueStateDetails = new List<string>();
        var continuationChainDetails = new List<string>();
        var expectedArtifactDetails = new List<string>();
        var tokenlessDownstreamDetails = new List<string>();
        var expectedArtifactSources = new List<string>();
        string? error = eventResolutionError;

        var unitPattern = ResolveUnitPattern(routingRoot, record.Domain, out var unitPatternError);
        if (error is null && unitPatternError is not null)
        {
            error = unitPatternError;
        }
        var unitToken = unitPattern is null
            ? null
            : ExtractExecutionUnitToken(
                record.TaskId,
                record.Objective,
                record.Inputs,
                unitPattern);

        var expectedArtifactPresent = false;
        IReadOnlyList<string> expectedArtifacts = record.ExpectedArtifacts is { Count: > 0 } artifacts
            ? artifacts
            : [record.ExpectedArtifact];
        foreach (var expectedArtifact in expectedArtifacts)
        {
            if (!TryResolveArtifactPath(expectedArtifact, record.Cwd, out var artifactPath))
            {
                expectedArtifactSources.Add($"non-filesystem:{expectedArtifact}");
                continue;
            }

            expectedArtifactSources.Add(artifactPath);
            if (error is not null)
            {
                continue;
            }

            try
            {
                if (File.Exists(artifactPath) || Directory.Exists(artifactPath))
                {
                    expectedArtifactPresent = true;
                    expectedArtifactDetails.Add($"expected-artifact:path={artifactPath}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = $"expected-artifact source '{artifactPath}' could not be checked: {exception.Message}";
            }
        }

        var pendingLedgerPresent = false;
        var hasTokenlessDownstream = false;
        if (error is null)
        {
            var ledger = NotifyPendingDelegationStore.ReadAll(routingRoot, record.Domain, record.Team, out var ledgerError);
            if (ledgerError is not null)
            {
                error = ledgerError;
            }
            else if (unitPattern is not null && unitToken is not null)
            {
                var downstream = ledger.Where(candidate =>
                        !string.Equals(candidate.TaskId, record.TaskId, StringComparison.Ordinal)
                        && string.Equals(candidate.DelegatingRole, record.RecipientRole, StringComparison.OrdinalIgnoreCase)
                        && candidate.DispatchedAt > deliveredAt)
                    .ToArray();
                foreach (var candidate in downstream)
                {
                    var candidateUnitToken = ExtractExecutionUnitToken(
                        candidate.TaskId,
                        candidate.Objective,
                        candidate.Inputs,
                        unitPattern);
                    if (string.Equals(candidateUnitToken, unitToken, StringComparison.OrdinalIgnoreCase))
                    {
                        pendingLedgerPresent = true;
                        pendingDetails.Add(
                            $"pending-ledger:task_id={candidate.TaskId}; delegating_role={candidate.DelegatingRole}; dispatched_at={candidate.DispatchedAt:O}; source={pendingLedgerPath}");
                    }
                    else if (candidateUnitToken is null)
                    {
                        hasTokenlessDownstream = true;
                        tokenlessDownstreamDetails.Add(
                            $"pending-ledger:tokenless-downstream task_id={candidate.TaskId}; delegating_role={candidate.DelegatingRole}; dispatched_at={candidate.DispatchedAt:O}; source={pendingLedgerPath}");
                    }
                }
            }
        }

        var reportOutboxPresent = false;
        if (error is null)
        {
            var outbox = NotifyReportOutboxStore.ReadAll(routingRoot, record.Domain, record.Team, out var outboxError);
            if (outboxError is not null)
            {
                error = outboxError;
            }
            else if (unitPattern is not null && unitToken is not null)
            {
                foreach (var entry in outbox.Where(entry => entry.CreatedAt > deliveredAt
                    && !string.Equals(entry.TaskId, record.TaskId, StringComparison.Ordinal)))
                {
                    var entryUnitToken = ExtractExecutionUnitToken(
                        entry.TaskId,
                        entry.Summary,
                        [entry.Artifact],
                        unitPattern);
                    if (!string.Equals(entryUnitToken, unitToken, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    reportOutboxPresent = true;
                    reportOutboxDetails.Add(
                        $"report-outbox:task_id={entry.TaskId}; from_role={entry.FromRole}; created_at={entry.CreatedAt:O}; source={reportOutboxPath}");
                }
            }
        }

        var notificationEventsPresent = false;
        if (error is null)
        {
            foreach (var eventPath in eventPaths)
            {
                if (!File.Exists(eventPath))
                {
                    continue;
                }

                try
                {
                    foreach (var line in File.ReadLines(eventPath))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        using var document = JsonDocument.Parse(line);
                        var root = document.RootElement;
                        var eventUnit = ReadString(root, "unit");
                        var summary = ReadString(root, "summary");
                        var artifact = ReadString(root, "artifact");
                        if (!root.TryGetProperty("timestamp", out var timestampElement)
                            || !DateTimeOffset.TryParse(
                                timestampElement.GetString(),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind,
                                out var timestamp)
                            || timestamp <= deliveredAt)
                        {
                            continue;
                        }

                        // G741's exact-task event is still direct report evidence.
                        // G788 adds a distinct child-report path which must carry the
                        // configured unit token rather than accidentally treating any
                        // unrelated post-delivery event as progress.
                        var isExactTaskReport = string.Equals(
                            eventUnit,
                            record.TaskId,
                            StringComparison.Ordinal);
                        var eventUnitToken = unitPattern is null
                            ? null
                            : ExtractExecutionUnitToken(
                                eventUnit,
                                summary,
                                artifact is null ? null : [artifact],
                                unitPattern);
                        var isTokenCarryingChildReport = unitPattern is not null
                            && unitToken is not null
                            && !string.Equals(eventUnit, record.TaskId, StringComparison.Ordinal)
                            && string.Equals(eventUnitToken, unitToken, StringComparison.OrdinalIgnoreCase);
                        if (!isExactTaskReport && !isTokenCarryingChildReport)
                        {
                            continue;
                        }

                        notificationEventsPresent = true;
                        var kind = ReadString(root, "kind") ?? "unknown";
                        notificationEventDetails.Add(
                            $"notification-events:unit={eventUnit ?? "<none>"}; kind={kind}; timestamp={timestamp:O}; source={eventPath}");
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                {
                    error = $"notification-events source '{eventPath}' could not be read: {exception.Message}";
                    break;
                }
            }
        }

        var queueStatePresent = false;
        if (error is null && File.Exists(queueStatePath))
        {
            try
            {
                var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
                if (unitToken is not null && queueState.UpdatedAt > deliveredAt)
                {
                    foreach (var item in queueState.Items.Where(item =>
                        string.Equals(item.ExecutionUnit, unitToken, StringComparison.OrdinalIgnoreCase)
                        && (!string.IsNullOrWhiteSpace(item.LinkedPr) || item.State != QueueItemState.Queued)))
                    {
                        queueStatePresent = true;
                        queueStateDetails.Add(
                            $"queue-state:execution_unit={item.ExecutionUnit}; state={item.State.ToString().ToLowerInvariant()}; linked_pr={item.LinkedPr ?? "<none>"}; updated_at={queueState.UpdatedAt:O}; source={queueStatePath}");
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                error = $"queue-state source '{queueStatePath}' could not be read: {exception.Message}";
            }
        }

        var continuationChainPresent = false;
        if (error is null)
        {
            var continuation = ContinuationChainStore.Read(
                routingRoot,
                record.Domain,
                record.Team,
                taskId: record.TaskId);
            if (!continuation.Resolved)
            {
                error = continuation.Error ?? $"continuation-chain source '{continuation.Path}' could not be read.";
            }
            else
            {
                var transitionLinks = continuation.Records
                    .SelectMany(chain => chain.Links)
                    .Where(link => link.Name is ContinuationChainStore.CanonicalStateClassified
                        or ContinuationChainStore.RequiredContinuationStarted
                        or ContinuationChainStore.NamedBlockerRecorded)
                    .ToArray();
                continuationChainPresent = transitionLinks.Length > 0;
                continuationChainDetails.AddRange(transitionLinks.Select(link =>
                    $"continuation-chain:link={link.Name}; timestamp={link.Timestamp:O}; source={link.Source}"));
            }
        }

        return new NotifyDelegationExecutionEvidence
        {
            PendingLedgerPresent = error is null ? pendingLedgerPresent : null,
            ReportOutboxPresent = error is null ? reportOutboxPresent : null,
            NotificationEventsPresent = error is null ? notificationEventsPresent : null,
            QueueStatePresent = error is null ? queueStatePresent : null,
            ContinuationChainPresent = error is null ? continuationChainPresent : null,
            ExpectedArtifactPresent = error is null ? expectedArtifactPresent : null,
            PendingLedgerSource = pendingLedgerPath,
            ReportOutboxSource = reportOutboxPath,
            NotificationEventsSource = eventPaths.Count == 0
                ? "notification-events:none"
                : string.Join("|", eventPaths),
            QueueStateSource = queueStatePath,
            ContinuationChainSource = continuationPath,
            ExpectedArtifactSource = expectedArtifactSources.Count == 0
                ? "expected-artifact:none"
                : string.Join("|", expectedArtifactSources),
            ExecutionUnitToken = unitToken,
            HasTokenlessDownstreamDelegation = error is null && hasTokenlessDownstream,
            PendingLedgerDetails = pendingDetails,
            ReportOutboxDetails = reportOutboxDetails,
            NotificationEventDetails = notificationEventDetails,
            QueueStateDetails = queueStateDetails,
            ContinuationChainDetails = continuationChainDetails,
            ExpectedArtifactDetails = expectedArtifactDetails,
            TokenlessDownstreamDetails = tokenlessDownstreamDetails,
            Error = error,
        };
    }

    /// <summary>
    /// Extracts one configured, case-insensitive execution-unit token in record
    /// field order: task id, objective, then inputs. All G788 source checks
    /// call this rule, so a later token cannot override an earlier token from
    /// the same carrier.
    /// </summary>
    internal static string? ExtractExecutionUnitToken(
        string? taskId,
        string? objective,
        IEnumerable<string?>? inputs,
        Regex executionUnitPattern)
    {
        var orderedFields = new List<string?> { taskId, objective };
        if (inputs is not null)
        {
            orderedFields.AddRange(inputs);
        }

        foreach (var value in orderedFields)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (Match candidate in CandidateExecutionUnitPattern.Matches(value))
            {
                if (executionUnitPattern.IsMatch(candidate.Value))
                {
                    return candidate.Value.ToUpperInvariant();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a Steward's downstream reference against the same measured
    /// evidence set used by G788.  A reference is accepted only when it is
    /// present in a token-carrying pending ledger, report outbox, or
    /// notification event (or names the measured execution-unit queue/chain
    /// evidence).  A syntactically safe but unknown identifier therefore
    /// cannot satisfy the Steward judgement gate.
    /// </summary>
    internal static bool TryResolveDownstreamReference(
        string routingRoot,
        NotifyPendingDelegation parent,
        string? reference,
        out string evidence,
        out string error)
    {
        evidence = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
        {
            error = "downstream delegation reference is required.";
            return false;
        }

        var unitPattern = ResolveUnitPattern(routingRoot, parent.Domain, out var patternError);
        if (patternError is not null)
        {
            error = patternError;
            return false;
        }

        var parentToken = unitPattern is null
            ? null
            : ExtractExecutionUnitToken(parent.TaskId, parent.Objective, parent.Inputs, unitPattern);
        if (parentToken is null)
        {
            error = $"downstream delegation reference '{reference}' could not be matched because the parent execution-unit token is absent.";
            return false;
        }

        var measured = Resolve(routingRoot, parent, parent.DispatchedAt);
        if (!measured.IsResolved)
        {
            error = measured.Error
                ?? $"downstream delegation reference '{reference}' could not be resolved because evidence sources are unreadable.";
            return false;
        }

        var details = measured.PendingLedgerDetails
            .Concat(measured.ReportOutboxDetails)
            .Concat(measured.NotificationEventDetails)
            .Concat(measured.QueueStateDetails)
            .Concat(measured.ContinuationChainDetails)
            .ToArray();
        foreach (var detail in details)
        {
            if (detail.Contains($"task_id={reference}", StringComparison.Ordinal)
                || detail.Contains($"unit={reference}", StringComparison.Ordinal))
            {
                evidence = detail;
                error = string.Empty;
                return true;
            }
        }

        // Queue and continuation evidence carry the configured unit token,
        // not a child task id.  Permit that explicit token reference only
        // when one of those non-artifact carriers was actually measured.
        if (string.Equals(reference, parentToken, StringComparison.OrdinalIgnoreCase)
            && (measured.QueueStateDetails.Count > 0 || measured.ContinuationChainDetails.Count > 0))
        {
            evidence = measured.QueueStateDetails.FirstOrDefault()
                ?? measured.ContinuationChainDetails.First();
            error = string.Empty;
            return true;
        }

        error = $"downstream delegation reference '{reference}' did not resolve in G788 execution evidence ({measured.SourceCountSummary}).";
        return false;
    }

    private static readonly Regex CandidateExecutionUnitPattern = new(
        @"(?<![A-Za-z0-9])(?:[A-Za-z][A-Za-z0-9]*-)?G[0-9]+(?![A-Za-z0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DefaultExecutionUnitPattern = new(
        @"^(?:[A-Za-z][A-Za-z0-9]*-)?G[0-9]+$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static Regex? ResolveUnitPattern(string routingRoot, string domain, out string? error)
    {
        var resolution = NextSliceDomainBindingsExecutionUnitRegex.ResolveAtRoot(routingRoot, domain);
        if (resolution.Kind == ExecutionUnitRegexResolutionKind.InvalidPattern)
        {
            error = $"execution-unit regex source '{resolution.BindingsPath}' is invalid: {resolution.Detail}";
            return null;
        }

        if (resolution.Pattern is null)
        {
            error = null;
            return DefaultExecutionUnitPattern;
        }

        try
        {
            error = null;
            return new Regex(
                resolution.Pattern,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException exception)
        {
            error = $"execution-unit regex source '{resolution.BindingsPath}' is invalid: {exception.Message}";
            return null;
        }
    }

    private static IReadOnlyList<string> ResolveEventPaths(
        string routingRoot,
        NotifyPendingDelegation record,
        out string? error)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (!NotifyEventWriter.TryResolveReadPath(
            routingRoot,
            record.Domain,
            record.Team,
            record.Reader,
            out var eventPath,
            out error))
        {
            return [];
        }

        paths.Add(eventPath);
        var eventsRoot = Path.Combine(routingRoot, NotifyEventWriter.EventsDirectoryRelativePath, record.Domain);
        if (Directory.Exists(eventsRoot))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(eventsRoot, "*.jsonl", SearchOption.TopDirectoryOnly))
                {
                    paths.Add(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = $"notification-events source '{eventsRoot}' could not be enumerated: {exception.Message}";
                return [];
            }
        }

        error = null;
        return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static NotifyDelegationExecutionEvidence Unresolved(string routingRoot, string error) => new()
    {
        PendingLedgerSource = routingRoot,
        ReportOutboxSource = routingRoot,
        NotificationEventsSource = routingRoot,
        QueueStateSource = routingRoot,
        ContinuationChainSource = routingRoot,
        ExpectedArtifactSource = routingRoot,
        Error = error,
    };

    private static bool TryResolveArtifactPath(
        string value,
        string? cwd,
        out string path)
    {
        path = string.Empty;
        var candidate = value.Trim();
        if (candidate.Length == 0)
        {
            return false;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            candidate = uri.LocalPath;
        }
        else if (candidate.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Path.IsPathRooted(candidate))
        {
            if (string.IsNullOrWhiteSpace(cwd))
            {
                return false;
            }

            candidate = Path.Combine(cwd, candidate);
        }

        try
        {
            path = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
