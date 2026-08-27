using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only evidence used by G741 to distinguish a delivered delegation
/// without an observable start from a delegation whose work has become
/// visible. Unknown or unreadable evidence is deliberately not a finding.
/// </summary>
internal sealed record NotifyDelegationExecutionEvidence
{
    public bool? ExpectedArtifactPresent { get; init; }
    public bool? TargetEntityTransitionPresent { get; init; }
    public required string ArtifactSource { get; init; }
    public required string TargetEntitySource { get; init; }
    public IReadOnlyList<string> ArtifactDetails { get; init; } = [];
    public IReadOnlyList<string> TargetEntityDetails { get; init; } = [];
    public string? Error { get; init; }

    internal bool IsResolved => Error is null
        && ExpectedArtifactPresent is not null
        && TargetEntityTransitionPresent is not null;

    internal static NotifyDelegationExecutionEvidence Resolve(
        string routingRoot,
        NotifyPendingDelegation record,
        DateTimeOffset deliveredAt)
    {
        var artifactDetails = new List<string>();
        var artifactSources = new List<string>();
        string? error = null;
        var artifactPresent = false;

        IReadOnlyList<string> expectedArtifacts = record.ExpectedArtifacts is { Count: > 0 } artifacts
            ? artifacts
            : [record.ExpectedArtifact];
        foreach (var expectedArtifact in expectedArtifacts)
        {
            if (TryResolveArtifactPath(expectedArtifact, record.Cwd, out var artifactPath))
            {
                artifactSources.Add(artifactPath);
                try
                {
                    if (File.Exists(artifactPath) || Directory.Exists(artifactPath))
                    {
                        artifactPresent = true;
                        artifactDetails.Add($"path:{artifactPath}");
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    error = $"expected artifact source '{artifactPath}' could not be checked: {exception.Message}";
                    break;
                }
            }
            else
            {
                artifactSources.Add($"non-filesystem:{expectedArtifact}");
            }
        }

        var eventSource = ResolveEventSource(routingRoot, record, out var eventPath, out var eventResolutionError);
        artifactSources.Add(eventSource);
        if (error is null && !string.IsNullOrWhiteSpace(eventResolutionError))
        {
            error = eventResolutionError;
        }

        if (error is null && eventPath is not null && File.Exists(eventPath))
        {
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
                    if (!root.TryGetProperty("unit", out var unit)
                        || !string.Equals(unit.GetString(), record.TaskId, StringComparison.Ordinal)
                        || !root.TryGetProperty("timestamp", out var timestampElement)
                        || !DateTimeOffset.TryParse(
                            timestampElement.GetString(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out var timestamp)
                        || timestamp <= deliveredAt)
                    {
                        continue;
                    }

                    artifactPresent = true;
                    var kind = root.TryGetProperty("kind", out var kindElement)
                        ? kindElement.GetString() ?? "unknown"
                        : "unknown";
                    artifactDetails.Add($"event:{eventPath};kind={kind};timestamp={timestamp:O}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                error = $"notification event source '{eventPath}' could not be read: {exception.Message}";
            }
        }

        var artifactSource = artifactSources.Count == 0
            ? "expected-artifacts:none; notification-events:none"
            : $"expected-artifacts={string.Join("|", artifactSources)}";

        var continuation = ContinuationChainStore.Read(
            routingRoot,
            record.Domain,
            record.Team,
            taskId: record.TaskId);
        var transitionSource = $"continuation-chain:{continuation.Path}; links={ContinuationChainStore.CanonicalStateClassified}|{ContinuationChainStore.RequiredContinuationStarted}|{ContinuationChainStore.NamedBlockerRecorded}";
        var transitionDetails = new List<string>();
        bool? transitionPresent = null;
        if (error is null && !continuation.Resolved)
        {
            error = continuation.Error ?? $"continuation-chain source '{continuation.Path}' could not be read.";
        }
        else if (error is null)
        {
            var transitionLinks = continuation.Records
                .SelectMany(chain => chain.Links)
                .Where(link => link.Name is ContinuationChainStore.CanonicalStateClassified
                    or ContinuationChainStore.RequiredContinuationStarted
                    or ContinuationChainStore.NamedBlockerRecorded)
                .ToArray();
            transitionPresent = transitionLinks.Length > 0;
            transitionDetails.AddRange(transitionLinks.Select(link =>
                $"link:{link.Name};timestamp={link.Timestamp:O};source={link.Source}"));
        }

        return new NotifyDelegationExecutionEvidence
        {
            ExpectedArtifactPresent = error is null ? artifactPresent : null,
            TargetEntityTransitionPresent = error is null ? transitionPresent : null,
            ArtifactSource = artifactSource,
            TargetEntitySource = transitionSource,
            ArtifactDetails = artifactDetails,
            TargetEntityDetails = transitionDetails,
            Error = error,
        };
    }

    private static string ResolveEventSource(
        string routingRoot,
        NotifyPendingDelegation record,
        out string? path,
        out string? error)
    {
        if (NotifyEventWriter.TryResolveReadPath(
            routingRoot,
            record.Domain,
            record.Team,
            record.Reader,
            out path,
            out error))
        {
            return $"notification-events:{path}";
        }

        path = null;
        return $"notification-events:unresolved:{error}";
    }

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
