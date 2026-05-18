namespace IntentSystem.Cli.Commands;

/// <summary>
/// G365: domain-aware lookup that derives the active domain for a
/// requested target repo from the host's
/// <c>.intent-cli/host-binding.toml</c>. The host-loop-next-action and
/// host-review-diagnostics commands consult this resolver BEFORE
/// falling back to the host's configured
/// <see cref="CliConfig.Project"/>.<see cref="ProjectConfig.Domain"/>
/// so a host that operates against a different target repo than the
/// one its config records does not silently probe
/// <see cref="IntentNextSliceCommand"/> with the wrong domain.
///
/// Resolution order:
/// <list type="number">
///   <item>Parent intent repo root (when configured) — the canonical
///   binding for cross-repo hosts.</item>
///   <item>Child <see cref="CliContext.RepoRoot"/> — single-repo
///   hosts and in-memory test fixtures.</item>
/// </list>
///
/// Outcomes:
/// <list type="bullet">
///   <item><c>Match</c>: binding's <c>target_repo</c> matches the
///   requested repo (case-insensitive). The binding's <c>domain</c>
///   is returned and should be used as if the operator had typed
///   <c>--domain</c>.</item>
///   <item><c>Mismatch</c>: a binding exists but its
///   <c>target_repo</c> records a DIFFERENT repo. Returning this
///   distinct outcome lets the caller surface a structured
///   <c>missing-domain-binding</c> diagnostic instead of silently
///   probing with the wrong domain.</item>
///   <item><c>Missing</c>: no binding file present, the file does not
///   declare a <c>[host]</c> section, or the <c>target_repo</c> field
///   is blank. The caller may fall back to other domain sources
///   (e.g. <c>--domain</c>, config) without raising a missing-binding
///   diagnostic.</item>
/// </list>
///
/// The parser is intentionally minimal (no external TOML dependency
/// pulled in here) and mirrors the tolerance contract of
/// <see cref="IntentHostCheckCommand.ParseHostBinding"/>: a malformed
/// or unparseable file degrades to <c>Missing</c> so misconfiguration
/// never blocks the host loop entirely.
/// </summary>
internal static class HostBindingDomainResolver
{
    public static HostBindingDomainResolution Resolve(CliContext context, string repo)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var bindingPath = ResolveBindingPath(context);
        if (string.IsNullOrWhiteSpace(bindingPath) || !File.Exists(bindingPath))
        {
            return HostBindingDomainResolution.Missing(bindingPath);
        }

        string content;
        try
        {
            content = File.ReadAllText(bindingPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HostBindingDomainResolution.Missing(bindingPath);
        }

        (string? domain, string? targetRepo) parsed;
        try
        {
            parsed = ParseHostBinding(content);
        }
        catch (Exception)
        {
            return HostBindingDomainResolution.Missing(bindingPath);
        }

        if (string.IsNullOrWhiteSpace(parsed.domain) || string.IsNullOrWhiteSpace(parsed.targetRepo))
        {
            return HostBindingDomainResolution.Missing(bindingPath);
        }

        if (string.Equals(parsed.targetRepo, repo, StringComparison.OrdinalIgnoreCase))
        {
            return HostBindingDomainResolution.Match(parsed.domain!, bindingPath);
        }

        return HostBindingDomainResolution.Mismatch(parsed.domain!, parsed.targetRepo!, bindingPath);
    }

    private static string? ResolveBindingPath(CliContext context)
    {
        var parentRoot = context.ResolveParentIntentRepoRootPath();
        if (!string.IsNullOrWhiteSpace(parentRoot))
        {
            return Path.Combine(parentRoot, ".intent-cli", "host-binding.toml");
        }
        if (string.IsNullOrWhiteSpace(context.RepoRoot))
        {
            return null;
        }
        return Path.Combine(context.RepoRoot, ".intent-cli", "host-binding.toml");
    }

    /// <summary>
    /// Minimal manual TOML parse for the <c>[host]</c> section so this
    /// resolver does not pull a TOML dependency into the
    /// <c>Commands</c> namespace. Recognizes only the two fields the
    /// resolver needs (<c>domain</c>, <c>target_repo</c>); other
    /// fields are ignored. Quoted and unquoted scalar values are both
    /// accepted. Mirrors the tolerance contract of
    /// <see cref="IntentHostCheckCommand.ParseHostBinding"/> so a
    /// malformed file degrades to <c>Missing</c> in
    /// <see cref="Resolve"/> rather than throwing.
    /// </summary>
    internal static (string? Domain, string? TargetRepo) ParseHostBinding(string toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
        {
            return (null, null);
        }

        string? domain = null;
        string? targetRepo = null;
        var inHost = false;

        var normalized = toml.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var rawLine in normalized.Split('\n'))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                var section = trimmed[1..^1].Trim();
                inHost = string.Equals(section, "host", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inHost)
            {
                continue;
            }

            var equalsIndex = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..equalsIndex].Trim();
            var value = trimmed[(equalsIndex + 1)..].Trim();

            // Strip an optional trailing inline comment ` # ...`.
            var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                value = value[..commentIndex].Trim();
            }

            // Strip surrounding quotes if present.
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (string.Equals(key, "domain", StringComparison.OrdinalIgnoreCase))
            {
                domain = string.IsNullOrWhiteSpace(value) ? null : value;
            }
            else if (string.Equals(key, "target_repo", StringComparison.OrdinalIgnoreCase))
            {
                targetRepo = string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return (domain, targetRepo);
    }
}

/// <summary>
/// G365: outcome of a <see cref="HostBindingDomainResolver.Resolve"/>
/// call. See <see cref="HostBindingDomainResolver"/> docs for the
/// semantics of each value.
/// </summary>
internal enum HostBindingDomainResolutionKind
{
    /// <summary>No usable binding file present.</summary>
    Missing,
    /// <summary>Binding's <c>target_repo</c> matches the requested repo.</summary>
    Match,
    /// <summary>Binding present but its <c>target_repo</c> records a different repo.</summary>
    Mismatch,
}

/// <summary>
/// G365: structured outcome of
/// <see cref="HostBindingDomainResolver.Resolve"/>. Callers branch on
/// <see cref="Kind"/>; <see cref="Domain"/> and
/// <see cref="BoundTargetRepo"/> are populated only for the Match /
/// Mismatch outcomes.
/// </summary>
internal sealed record HostBindingDomainResolution
{
    public required HostBindingDomainResolutionKind Kind { get; init; }
    public string? Domain { get; init; }
    public string? BoundTargetRepo { get; init; }
    public string? BindingPath { get; init; }

    public static HostBindingDomainResolution Match(string domain, string bindingPath) => new()
    {
        Kind = HostBindingDomainResolutionKind.Match,
        Domain = domain,
        BindingPath = bindingPath,
    };

    public static HostBindingDomainResolution Mismatch(string domain, string boundTargetRepo, string bindingPath) => new()
    {
        Kind = HostBindingDomainResolutionKind.Mismatch,
        Domain = domain,
        BoundTargetRepo = boundTargetRepo,
        BindingPath = bindingPath,
    };

    public static HostBindingDomainResolution Missing(string? bindingPath) => new()
    {
        Kind = HostBindingDomainResolutionKind.Missing,
        BindingPath = bindingPath,
    };
}
