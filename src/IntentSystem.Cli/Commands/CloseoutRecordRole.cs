using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// The two roles that may record the closeout duties introduced by G698.
/// Route roles in a packet are a separate concern: this is the role of the
/// recorder who is asserting that the host-side duty was performed.
/// </summary>
internal static class CloseoutRecordRole
{
    public const string Design = "design";
    public const string Orchestration = "orchestration";

    public static IReadOnlyList<string> Allowed { get; } = [Design, Orchestration];

    public static bool TryNormalize(string? value, out string? normalized, out string error)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "a recording role is required; use '--role design' or '--role orchestration'.";
            return false;
        }

        var candidate = value.Trim().ToLowerInvariant();
        if (!Allowed.Contains(candidate, StringComparer.Ordinal))
        {
            error = $"recording role '{value}' is not supported; use 'design' or 'orchestration'.";
            return false;
        }

        normalized = candidate;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Resolves an explicit command role, an invocation-context role, or the
    /// compatibility default used by pre-G698 callers. The default remains
    /// design so an old command continues to write the legacy path while all
    /// newly created records still carry a role.
    /// </summary>
    public static bool TryResolve(
        string? requestedRole,
        string? invokingRole,
        out string? role,
        out string source,
        out string error)
    {
        if (!string.IsNullOrWhiteSpace(requestedRole))
        {
            if (!TryNormalize(requestedRole, out role, out error))
            {
                source = "explicit";
                return false;
            }

            source = "argument";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(invokingRole))
        {
            if (!TryNormalize(invokingRole, out role, out error))
            {
                source = "context";
                return false;
            }

            source = "context";
            return true;
        }

        role = Design;
        source = "compatibility-default";
        error = string.Empty;
        return true;
    }

    public static bool IsRoleScoped(string? requestedRole, string? invokingRole) =>
        !string.IsNullOrWhiteSpace(requestedRole) || !string.IsNullOrWhiteSpace(invokingRole);

    public static string FormatArgument(string role) => $"--role {role}";

    public static string Display(string? role) => role ?? "unattributed";
}

/// <summary>Summary emitted by read/verification surfaces for every record.</summary>
internal sealed record CloseoutRecordSummary
{
    [JsonPropertyName("record_path")]
    public required string RecordPath { get; init; }

    [JsonPropertyName("role")]
    public required string? Role { get; init; }

    [JsonPropertyName("host_commit")]
    public required string HostCommit { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }
}

/// <summary>
/// Shared path policy for role-scoped closeout evidence. The old
/// <c>record.json</c> remains the compatibility slot. Explicit roles use
/// <c>records/&lt;role&gt;.json</c>, which lets the two duties coexist without
/// rewriting an old unattributed artifact.
/// </summary>
internal static class RoleScopedCloseoutRecordStore
{
    public const string RoleRecordsDirectoryName = "records";

    public static string ResolveLegacyRelativePath(string rootRelativePath, string executionUnit)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return $"{rootRelativePath}/{executionUnit}/record.json";
    }

    public static string ResolveRoleRelativePath(string rootRelativePath, string executionUnit, string role)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var unitError))
        {
            throw new InvalidOperationException(unitError);
        }

        if (!CloseoutRecordRole.TryNormalize(role, out var normalized, out var roleError))
        {
            throw new InvalidOperationException(roleError);
        }

        return $"{rootRelativePath}/{executionUnit}/{RoleRecordsDirectoryName}/{normalized}.json";
    }

    public static string ResolveRoleFullPath(
        string repoRoot,
        string rootRelativePath,
        string executionUnit,
        string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var unitError))
        {
            throw new InvalidOperationException(unitError);
        }

        if (!CloseoutRecordRole.TryNormalize(role, out var normalizedRole, out var roleError))
        {
            throw new InvalidOperationException(roleError);
        }

        var root = Path.GetFullPath(Path.Combine(repoRoot, rootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var resolved = Path.GetFullPath(Path.Combine(
            root,
            executionUnit,
            RoleRecordsDirectoryName,
            $"{normalizedRole}.json"));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"resolved role record path for '{executionUnit}' escapes '{rootRelativePath}'.");
        }

        return resolved;
    }

    /// <summary>
    /// Lists the compatibility slot and all JSON role slots without trusting
    /// file names. Consumers validate the embedded execution unit and role,
    /// so a malformed or wrong-role artifact is visible as unreadable rather
    /// than silently dropped.
    /// </summary>
    public static IReadOnlyList<string> EnumerateExistingPaths(
        string repoRoot,
        string rootRelativePath,
        string executionUnit)
    {
        var paths = new List<string>();
        var legacy = Path.Combine(
            repoRoot,
            ResolveLegacyRelativePath(rootRelativePath, executionUnit).Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(legacy))
        {
            paths.Add(legacy);
        }

        var roleDirectory = Path.Combine(
            repoRoot,
            rootRelativePath.Replace('/', Path.DirectorySeparatorChar),
            executionUnit,
            RoleRecordsDirectoryName);
        if (Directory.Exists(roleDirectory))
        {
            paths.AddRange(Directory.EnumerateFiles(roleDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal));
        }

        return paths;
    }

    public static CloseoutRecordSummary Summary(string path, string repoRoot, string? role, string hostCommit, DateTimeOffset recordedAt) =>
        new()
        {
            RecordPath = Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
            Role = role,
            HostCommit = hostCommit,
            RecordedAt = recordedAt,
        };
}
