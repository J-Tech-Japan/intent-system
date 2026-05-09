using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G301: pure analyzer for wrong-host detection. Compares the host
/// repo's recorded binding (from <c>.intent-cli/host-binding.toml</c>,
/// produced by <c>intent-cli intent init</c>) against the observed git
/// remote URL of the current cwd. When the bound host repo does not
/// match the observed remote, the analyzer returns a structured
/// mismatch result so callers can fail closed and surface remediation
/// steps to the operator instead of silently mutating parent state in
/// the wrong host.
///
/// The contract is intentionally pure — no `gh` calls, no file I/O.
/// Callers (`intent init`, `intent next-slice`, `automation
/// issue-publish`, etc.) capture the binding and the observed remote
/// and pass them in.
/// </summary>
internal static class WrongHostGuard
{
    public const string StatusOk = "ok";
    public const string StatusUnbound = "unbound";
    public const string StatusMismatch = "wrong-host";

    public static WrongHostGuardResult Check(
        string domain,
        string? boundHostRepo,
        string? observedRemoteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var bound = NormalizeRepo(boundHostRepo);
        var observed = ExtractOwnerRepo(observedRemoteUrl);

        if (string.IsNullOrEmpty(bound))
        {
            return new WrongHostGuardResult
            {
                Status = StatusUnbound,
                Domain = domain,
                BoundHostRepo = null,
                ObservedHostRepo = observed,
                ObservedRemoteUrl = observedRemoteUrl,
                Summary = $"No `host_repo` is recorded in `.intent-cli/host-binding.toml` for domain `{domain}`. Wrong-host detection is disabled until the binding is set.",
                RemediationSteps = new[]
                {
                    $"Re-run `intent-cli intent init --domain {domain} --host-repo <owner/repo> --target-repo <owner/repo> --write` from this host repo to record the canonical binding.",
                    "If this host is not the canonical host for the domain, stop and clarify with the operator before mutating parent state."
                }
            };
        }

        if (string.IsNullOrEmpty(observed))
        {
            return new WrongHostGuardResult
            {
                Status = StatusUnbound,
                Domain = domain,
                BoundHostRepo = bound,
                ObservedHostRepo = null,
                ObservedRemoteUrl = observedRemoteUrl,
                Summary = $"`host_repo` is bound to `{bound}` but the observed git remote could not be parsed. Cannot prove this is the canonical host; treat as unbound and surface to the operator.",
                RemediationSteps = new[]
                {
                    "Capture the canonical host remote with `git -C <host-repo> remote get-url origin` and pass the parsed `<owner>/<repo>` to `WrongHostGuard.Check`.",
                    "If the host has no remote (local-only), explicitly disable the wrong-host check via operator policy; do not silently proceed."
                }
            };
        }

        if (string.Equals(bound, observed, StringComparison.OrdinalIgnoreCase))
        {
            return new WrongHostGuardResult
            {
                Status = StatusOk,
                Domain = domain,
                BoundHostRepo = bound,
                ObservedHostRepo = observed,
                ObservedRemoteUrl = observedRemoteUrl,
                Summary = $"Host binding matches: domain `{domain}` is operating from its canonical host `{bound}`.",
                RemediationSteps = Array.Empty<string>()
            };
        }

        return new WrongHostGuardResult
        {
            Status = StatusMismatch,
            Domain = domain,
            BoundHostRepo = bound,
            ObservedHostRepo = observed,
            ObservedRemoteUrl = observedRemoteUrl,
            Summary = $"Wrong-host operation detected for domain `{domain}` (G301). `.intent-cli/host-binding.toml` records `host_repo = \"{bound}\"`, but the current cwd's git remote points at `{observed}`. Refusing to silently mutate parent state.",
            RemediationSteps = new[]
            {
                $"`cd` to the canonical host repo `{bound}` and re-run the failing command there.",
                $"If `{observed}` is the new intended host for `{domain}`, the operator must explicitly migrate: re-run `intent-cli intent init --domain {domain} --host-repo {observed} --write` (after copying durable state across hosts) and document the migration.",
                $"If `{observed}` is the WRONG cwd (e.g. you accidentally cd'd into another host), stop and surface the gap rather than guessing. Do not commit parent state to `{observed}` while the binding still names `{bound}`.",
                "Never silently rewrite host-binding.toml from a non-canonical host; cross-host migration is operator-driven by design."
            }
        };
    }

    private static string? NormalizeRepo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value.Trim();
    }

    /// <summary>
    /// Extract <c>owner/repo</c> from a git remote URL. Accepts both
    /// HTTPS (<c>https://github.com/owner/repo[.git]</c>) and SSH
    /// (<c>git@github.com:owner/repo[.git]</c>) shapes. Returns null
    /// when the URL cannot be normalized — callers treat null as
    /// "observed-unknown" rather than mismatch so unbound checks stay
    /// distinct from wrong-host failures.
    /// </summary>
    internal static string? ExtractOwnerRepo(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var trimmed = remoteUrl.Trim();
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        // Accept owner/repo passed verbatim (test seam / explicit binding).
        if (!trimmed.Contains("://", StringComparison.Ordinal)
            && !trimmed.Contains('@', StringComparison.Ordinal)
            && trimmed.Count(c => c == '/') == 1)
        {
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return $"{parts[0]}/{parts[1]}";
            }
        }

        // HTTPS: https://github.com/<owner>/<repo>
        var httpsIndex = trimmed.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
        if (httpsIndex >= 0)
        {
            var afterHost = trimmed[(httpsIndex + "github.com/".Length)..];
            var segments = afterHost.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 2)
            {
                return $"{segments[0]}/{segments[1]}";
            }
        }

        // SSH: git@github.com:<owner>/<repo>
        var sshIndex = trimmed.IndexOf("github.com:", StringComparison.OrdinalIgnoreCase);
        if (sshIndex >= 0)
        {
            var afterHost = trimmed[(sshIndex + "github.com:".Length)..];
            var segments = afterHost.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 2)
            {
                return $"{segments[0]}/{segments[1]}";
            }
        }

        return null;
    }
}

internal sealed record WrongHostGuardResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("bound_host_repo")]
    public string? BoundHostRepo { get; init; }

    [JsonPropertyName("observed_host_repo")]
    public string? ObservedHostRepo { get; init; }

    [JsonPropertyName("observed_remote_url")]
    public string? ObservedRemoteUrl { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("remediation_steps")]
    public required IReadOnlyList<string> RemediationSteps { get; init; }
}
