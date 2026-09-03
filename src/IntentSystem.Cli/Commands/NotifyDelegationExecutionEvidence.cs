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
            : ExtractExecutionUnitToken(record.TaskId, unitPattern);

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
                    if (CarriesExecutionUnitToken(candidate, unitToken, unitPattern))
                    {
                        pendingLedgerPresent = true;
                        pendingDetails.Add(
                            $"pending-ledger:task_id={candidate.TaskId}; delegating_role={candidate.DelegatingRole}; dispatched_at={candidate.DispatchedAt:O}; source={pendingLedgerPath}");
                    }
                    else
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
                    && !string.Equals(entry.TaskId, record.TaskId, StringComparison.Ordinal)
                    && CarriesExecutionUnitToken(entry, unitToken, unitPattern)))
                {
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
                        var isTokenCarryingChildReport = unitPattern is not null
                            && unitToken is not null
                            && !string.Equals(eventUnit, record.TaskId, StringComparison.Ordinal)
                            && CarriesExecutionUnitToken(unitToken, unitPattern, eventUnit, summary, artifact);
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
    /// Extracts one configured execution-unit token from a task id or prose
    /// field. All G788 source checks call this rule, so the same token governs
    /// downstream delegation, child-report, event, and queue matching.
    /// </summary>
    internal static string? ExtractExecutionUnitToken(string? value, Regex executionUnitPattern) =>
        ExtractExecutionUnitTokens(value, executionUnitPattern).FirstOrDefault();

    private static readonly Regex CandidateExecutionUnitPattern = new(
        @"(?<![A-Za-z0-9])(?:[A-Za-z][A-Za-z0-9]*-)?G[0-9]+(?![A-Za-z0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DefaultExecutionUnitPattern = new(
        @"^(?:[A-Za-z][A-Za-z0-9]*-)?G[0-9]+$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static IEnumerable<string> ExtractExecutionUnitTokens(string? value, Regex executionUnitPattern)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (Match candidate in CandidateExecutionUnitPattern.Matches(value))
        {
            if (executionUnitPattern.IsMatch(candidate.Value))
            {
                yield return candidate.Value.ToUpperInvariant();
            }
        }
    }

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

    private static bool CarriesExecutionUnitToken(
        NotifyPendingDelegation candidate,
        string unitToken,
        Regex executionUnitPattern)
    {
        var values = new List<string?> { candidate.TaskId, candidate.Objective };
        values.AddRange(candidate.Inputs ?? []);
        return CarriesExecutionUnitToken(unitToken, executionUnitPattern, values.ToArray());
    }

    private static bool CarriesExecutionUnitToken(
        NotifyReportOutboxEntry entry,
        string unitToken,
        Regex executionUnitPattern) =>
        CarriesExecutionUnitToken(
            unitToken,
            executionUnitPattern,
            entry.TaskId,
            entry.Summary,
            entry.Artifact);

    private static bool CarriesExecutionUnitToken(
        string unitToken,
        Regex executionUnitPattern,
        params string?[] values) => values
        .SelectMany(value => ExtractExecutionUnitTokens(value, executionUnitPattern))
        .Any(candidate => string.Equals(candidate, unitToken, StringComparison.OrdinalIgnoreCase));

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
