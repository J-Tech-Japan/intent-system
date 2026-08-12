using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G673/G674: stable machine-readable vocabulary for an upstream GitHub API
/// availability observation. Candidate issue reads use REST while the
/// unverified PR remainder stays on GraphQL; this type describes the
/// structured quota signal for either dependency.
/// </summary>
internal static class GitHubApiQuotaConstants
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string Error = "error";
    public const string QuotaExhaustedCause = "github-api-quota-exhausted";
    public const string QuotaObservationFailedCause = "github-api-rate-limit-unavailable";
    public const string DetectionUnavailableCause = "detection-unavailable";
    public const string GraphQlResource = "graphql";
    public const string RestCoreResource = "core";
}

/// <summary>One resource row from <c>gh api rate_limit</c>.</summary>
internal sealed record GitHubApiQuotaResource
{
    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    [JsonPropertyName("limit")]
    public long? Limit { get; init; }

    [JsonPropertyName("used")]
    public long? Used { get; init; }

    [JsonPropertyName("remaining")]
    public long? Remaining { get; init; }

    [JsonPropertyName("reset")]
    public long? Reset { get; init; }

    [JsonPropertyName("reset_at")]
    public string? ResetAt { get; init; }
}

/// <summary>
/// The named degraded state emitted when a GitHub-consulting read is known to
/// be blocked by an exhausted API resource.  Consumers must use the fields,
/// not parse the human-readable message.
/// </summary>
internal sealed record GitHubApiDegradedState
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = GitHubApiQuotaConstants.Degraded;

    [JsonPropertyName("cause")]
    public string Cause { get; init; } = GitHubApiQuotaConstants.QuotaExhaustedCause;

    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    [JsonPropertyName("remaining")]
    public long? Remaining { get; init; }

    [JsonPropertyName("reset")]
    public long? Reset { get; init; }

    [JsonPropertyName("reset_at")]
    public string? ResetAt { get; init; }

    /// <summary>
    /// G674: identifies the surface's read dependency when the quota state is
    /// emitted. REST-backed issue reads use <c>rest-core</c>; reads with an
    /// unverified field remain explicitly <c>graphql-bound</c>.
    /// </summary>
    [JsonPropertyName("dependency")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Dependency { get; init; }

    /// <summary>G674: fields that kept this read GraphQL-bound.</summary>
    [JsonPropertyName("unverified_fields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? UnverifiedFields { get; init; }
}

/// <summary>Structured snapshot of all resources reported by GitHub.</summary>
internal sealed record GitHubApiQuotaReport
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = GitHubApiQuotaConstants.Healthy;

    [JsonPropertyName("resources")]
    public IReadOnlyList<GitHubApiQuotaResource> Resources { get; init; }
        = Array.Empty<GitHubApiQuotaResource>();

    [JsonPropertyName("degraded_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GitHubApiDegradedState? DegradedState { get; init; }

    [JsonPropertyName("cause")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cause { get; init; }

    [JsonIgnore]
    public bool IsQuotaDegraded => DegradedState is not null;

    public GitHubApiQuotaResource? Find(string resource) => Resources.FirstOrDefault(row =>
        string.Equals(row.Resource, resource, StringComparison.OrdinalIgnoreCase));

    public GitHubApiDegradedState? Exhausted(string resource)
    {
        var row = Find(resource);
        return row?.Remaining is <= 0
            ? new GitHubApiDegradedState
            {
                Resource = row.Resource,
                Remaining = row.Remaining,
                Reset = row.Reset,
                ResetAt = row.ResetAt,
            }
            : null;
    }
}

/// <summary>
/// Parses the structured JSON returned by GitHub's rate-limit endpoint.  No
/// stderr/free-text matching is used for quota recognition.
/// </summary>
internal static class GitHubApiQuotaParser
{
    public static GitHubApiQuotaReport? Parse(string? json)
        => Parse(json, GitHubApiQuotaConstants.GraphQlResource);

    /// <summary>
    /// Parse the snapshot against the resource used by the failed read.
    /// G673's parameterless form remains GraphQL-oriented for the doctor and
    /// compatibility fixtures; G674 uses this overload so a REST failure is
    /// not misclassified from an exhausted GraphQL row.
    /// </summary>
    public static GitHubApiQuotaReport? Parse(string? json, string resourceName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var resources = new List<GitHubApiQuotaResource>();
            if (root.TryGetProperty("resources", out var resourceObject)
                && resourceObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in resourceObject.EnumerateObject())
                {
                    if (TryReadResource(property.Name, property.Value, out var resource))
                    {
                        resources.Add(resource);
                    }
                }
            }

            // GitHub also exposes the classic core row at the top level as
            // `rate`. Keep it available when a fixture/server omits the
            // resources object, without inventing a quota verdict.
            if (root.TryGetProperty("rate", out var rateObject)
                && rateObject.ValueKind == JsonValueKind.Object
                && !resources.Any(row => string.Equals(row.Resource, "core", StringComparison.OrdinalIgnoreCase))
                && TryReadResource("core", rateObject, out var rate))
            {
                resources.Add(rate);
            }

            var observed = resources.FirstOrDefault(row =>
                string.Equals(row.Resource, resourceName, StringComparison.OrdinalIgnoreCase));
            if (observed is null)
            {
                return new GitHubApiQuotaReport
                {
                    Status = GitHubApiQuotaConstants.Error,
                    Resources = resources,
                    Cause = GitHubApiQuotaConstants.QuotaObservationFailedCause,
                };
            }

            var degraded = observed.Remaining is <= 0
                ? ToDegradedState(observed)
                : null;

            return new GitHubApiQuotaReport
            {
                Status = degraded is null
                    ? GitHubApiQuotaConstants.Healthy
                    : GitHubApiQuotaConstants.Degraded,
                Resources = resources,
                DegradedState = degraded,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadResource(
        string name,
        JsonElement value,
        out GitHubApiQuotaResource resource)
    {
        resource = null!;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var reset = ReadInt64(value, "reset");
        var resetAt = ReadString(value, "resetAt")
            ?? ReadString(value, "reset_at")
            ?? (reset is { } resetEpoch
                ? DateTimeOffset.FromUnixTimeSeconds(resetEpoch).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
                : null);

        resource = new GitHubApiQuotaResource
        {
            Resource = name,
            Limit = ReadInt64(value, "limit"),
            Used = ReadInt64(value, "used"),
            Remaining = ReadInt64(value, "remaining"),
            Reset = reset,
            ResetAt = resetAt,
        };
        return true;
    }

    private static GitHubApiDegradedState ToDegradedState(GitHubApiQuotaResource resource) =>
        new()
        {
            Resource = resource.Resource,
            Remaining = resource.Remaining,
            Reset = resource.Reset,
            ResetAt = resource.ResetAt,
        };

    private static long? ReadInt64(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number)
            ? number
            : null;
    }

    private static string? ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

/// <summary>Read-only seam used by candidate listing and automation doctor.</summary>
internal interface IGitHubApiQuotaProbe
{
    GitHubApiQuotaReport? Read();
}

/// <summary>
/// Minimal quota observation call.  It is intentionally a single request,
/// with no retry, sleep, scheduling, caching, batching, or request budgeting.
/// </summary>
internal sealed class GhCliGitHubApiQuotaProbe : IGitHubApiQuotaProbe
{
    private static GitHubApiQuotaReport Unavailable() => new()
    {
        Status = GitHubApiQuotaConstants.Error,
        Cause = GitHubApiQuotaConstants.QuotaObservationFailedCause,
    };

    public GitHubApiQuotaReport? Read()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("api");
        startInfo.ArgumentList.Add("rate_limit");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Unavailable();
            }

            var stdout = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return Unavailable();
            }

            return GitHubApiQuotaParser.Parse(stdout) ?? Unavailable();
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException)
        {
            return Unavailable();
        }
    }
}

/// <summary>
/// Typed GitHub read failure.  The stable <see cref="Cause"/> is the
/// machine-readable distinction between quota, authentication, malformed
/// payload, and other command failures.
/// </summary>
internal class GitHubApiRequestException : InvalidOperationException
{
    public GitHubApiRequestException(
        string cause,
        string operation,
        string message,
        GitHubApiDegradedState? degradedState = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Cause = cause;
        Operation = operation;
        DegradedState = degradedState;
    }

    public string Cause { get; }

    public string Operation { get; }

    public GitHubApiDegradedState? DegradedState { get; }

    public bool IsQuotaDegraded =>
        string.Equals(Cause, GitHubApiQuotaConstants.QuotaExhaustedCause, StringComparison.Ordinal);
}

internal sealed class GitHubApiQuotaExceededException : GitHubApiRequestException
{
    public GitHubApiQuotaExceededException(string operation, GitHubApiDegradedState degradedState)
        : base(
            GitHubApiQuotaConstants.QuotaExhaustedCause,
            operation,
            $"GitHub API quota exhausted for resource '{degradedState.Resource}'; reset at {degradedState.ResetAt ?? degradedState.Reset?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.",
            degradedState)
    {
    }
}

internal static class GitHubApiFailureFactory
{
    public static GitHubApiRequestException FromGhFailure(
        string operation,
        string? stderr,
        string? stdout,
        int exitCode,
        IGitHubApiQuotaProbe? quotaProbe,
        string? quotaResource = null,
        string? dependency = null,
        IReadOnlyList<string>? unverifiedFields = null)
    {
        var classification = GitHubCliJsonBoundary.ClassifyProcessFailure(stderr, stdout);
        var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        var detail = GitHubCliJsonBoundary.SanitizePreview(errorBody);

        // Authentication is a separate structured command class already
        // established by the boundary. Do not turn it into quota merely
        // because a stale rate-limit snapshot is available.
        if (string.Equals(
                classification,
                GitHubCliJsonBoundary.Classifications.GithubAuthFailed,
                StringComparison.Ordinal))
        {
            return new GitHubApiRequestException(
                classification,
                operation,
                $"[{classification}] `gh` failed to {operation} with exit {exitCode}: {detail}");
        }

        var quota = quotaProbe?.Read();
        var degraded = quotaResource is null
            ? quota?.DegradedState
            : quota?.Exhausted(quotaResource)
                ?? (quota?.DegradedState is { } observed
                    && string.Equals(observed.Resource, quotaResource, StringComparison.OrdinalIgnoreCase)
                    ? observed
                    : null);
        if (degraded is not null)
        {
            if (dependency is not null || unverifiedFields is not null)
            {
                degraded = degraded with
                {
                    Dependency = dependency,
                    UnverifiedFields = unverifiedFields,
                };
            }
            return new GitHubApiQuotaExceededException(operation, degraded);
        }

        return new GitHubApiRequestException(
            classification,
            operation,
            $"[{classification}] `gh` failed to {operation} with exit {exitCode}: {detail}");
    }

    public static InvalidOperationException JsonInvalid(string operation, string message) =>
        new(
            $"[{GitHubCliJsonBoundary.Classifications.GithubJsonInvalid}] {message}");
}
