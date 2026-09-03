using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G359: Loads the <c>execution_unit_regex</c> declared in
/// <c>intents/&lt;domain&gt;/automation/bindings.md</c> so the
/// <see cref="IntentNextSliceCommand"/> can filter packet candidates
/// from a shared <c>.intent-cli/issues</c> root by the requested
/// domain's namespace.
///
/// Resolution order matches the rest of the parent-aware analyzers
/// (<see cref="AutomationSummaryAnalyzer"/>,
/// <see cref="NextSliceClassifyAnalyzer"/>):
/// the parent intent repo root takes precedence over the child
/// <see cref="CliContext.RepoRoot"/> when configured, falling back to
/// the child root when no parent root is set. Missing bindings file,
/// missing <c>execution_unit_regex</c> field, and invalid regex
/// patterns all degrade to "no filter" so pre-G359 hosts and
/// misconfigured bindings never block next-slice planning.
/// </summary>
internal static class NextSliceDomainBindingsExecutionUnitRegex
{
    /// <summary>
    /// Tries to load and compile the <c>execution_unit_regex</c> for
    /// <paramref name="domain"/>. Returns <c>null</c> when the bindings
    /// file is missing, the field is absent, or the pattern fails to
    /// compile (degrades open so misconfiguration cannot block the
    /// host loop entirely).
    ///
    /// PR #824 review repair #3: callers that need to fail closed on
    /// missing/invalid bindings should use <see cref="Resolve"/>
    /// instead — it returns a structured outcome distinguishing
    /// <see cref="ExecutionUnitRegexResolutionKind.Present"/>,
    /// <see cref="ExecutionUnitRegexResolutionKind.MissingOrAbsent"/>,
    /// and <see cref="ExecutionUnitRegexResolutionKind.InvalidPattern"/>.
    /// </summary>
    public static Regex? TryLoad(CliContext context, string? domain)
        => Resolve(context, domain).Regex;

    /// <summary>
    /// PR #824 review repair #3: structured resolver so callers can
    /// fail closed when bindings are missing or malformed instead of
    /// silently treating both cases as "no filter". The bindings-
    /// missing path is the operator's responsibility to fix (drop a
    /// bindings.md for the active domain) and the pattern-invalid
    /// path is the operator's responsibility to repair (typo in the
    /// scalar). Callers that should keep the legacy fail-open
    /// posture continue to use <see cref="TryLoad"/>.
    /// </summary>
    public static ExecutionUnitRegexResolution Resolve(CliContext context, string? domain)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ExecutionUnitRegexResolution.MissingOrAbsent("(no domain configured)");
        }

        var bindingsPath = ResolveBindingsAbsolutePath(context, domain);
        return ResolveBindingsPath(bindingsPath);
    }

    /// <summary>
    /// Resolves the same domain binding from an explicit runtime root. Host
    /// supervisors use this rather than their caller's cwd so an explicit
    /// <c>--routing-root</c> governs the unit-token boundary as well as the
    /// evidence files it already governs.
    /// </summary>
    internal static ExecutionUnitRegexResolution ResolveAtRoot(string root, string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ExecutionUnitRegexResolution.MissingOrAbsent("(no domain configured)");
        }

        string bindingsPath;
        try
        {
            bindingsPath = Path.GetFullPath(Path.Combine(root, "intents", domain, "automation", "bindings.md"));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ExecutionUnitRegexResolution.MissingOrAbsent($"(unresolved: {exception.Message})");
        }

        return ResolveBindingsPath(bindingsPath);
    }

    private static ExecutionUnitRegexResolution ResolveBindingsPath(string? bindingsPath)
    {
        if (string.IsNullOrWhiteSpace(bindingsPath) || !File.Exists(bindingsPath))
        {
            return ExecutionUnitRegexResolution.MissingOrAbsent(bindingsPath ?? "(unresolved)");
        }

        string content;
        try
        {
            content = File.ReadAllText(bindingsPath);
        }
        catch (IOException exception)
        {
            return ExecutionUnitRegexResolution.MissingOrAbsent($"{bindingsPath}: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return ExecutionUnitRegexResolution.MissingOrAbsent($"{bindingsPath}: {exception.Message}");
        }

        var pattern = ExtractExecutionUnitRegex(content);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return ExecutionUnitRegexResolution.MissingOrAbsent(bindingsPath);
        }

        try
        {
            var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            return ExecutionUnitRegexResolution.Present(regex, pattern, bindingsPath);
        }
        catch (ArgumentException exception)
        {
            return ExecutionUnitRegexResolution.InvalidPattern(pattern, exception.Message, bindingsPath);
        }
    }

    private static string? ResolveBindingsAbsolutePath(CliContext context, string domain)
    {
        // PR #822 review fix: when a parent intent repo root is
        // configured, the PARENT bindings.md is authoritative. The
        // previous order (child first, parent fallback) let a stale or
        // partial child workspace bindings.md override the host's
        // authoritative bindings and drop `execution_unit_regex`,
        // letting the wrong namespace leak back into
        // `intent next-slice --dry-run`. This now mirrors the parent-
        // aware lookup contract used by AutomationSummaryAnalyzer and
        // NextSliceClassifyAnalyzer: parent root takes precedence when
        // set, child root is the fallback for host-colocated workspace
        // layouts (and the in-memory test fixtures).
        var parentRoot = context.ResolveParentIntentRepoRootPath();
        if (!string.IsNullOrWhiteSpace(parentRoot))
        {
            return Path.Combine(parentRoot, "intents", domain, "automation", "bindings.md");
        }

        if (string.IsNullOrWhiteSpace(context.RepoRoot))
        {
            return null;
        }
        return Path.Combine(context.RepoRoot, "intents", domain, "automation", "bindings.md");
    }

    /// <summary>
    /// Minimal deterministic parser for the <c>execution_unit_regex</c>
    /// scalar. Tolerates YAML frontmatter delimited by <c>---</c>, and
    /// also accepts the same key written as a top-level inline field
    /// outside frontmatter (mirroring
    /// <see cref="AutomationSummaryAnalyzer"/>'s tolerant scan).
    /// </summary>
    internal static string? ExtractExecutionUnitRegex(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            // Skip indented (nested) lines; only top-level keys are
            // recognized.
            if (line[0] == ' ' || line[0] == '\t')
            {
                continue;
            }

            // Skip markdown headings, list markers, frontmatter
            // delimiters, comments.
            if (line.StartsWith('#')
                || line.StartsWith("- ", StringComparison.Ordinal)
                || string.Equals(line.Trim(), "---", StringComparison.Ordinal))
            {
                continue;
            }

            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0)
            {
                continue;
            }

            var key = line[..colonIndex].Trim();
            if (!string.Equals(key, "execution_unit_regex", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(colonIndex + 1)..].Trim();

            // Strip surrounding quotes if present.
            if (value.Length >= 2
                && ((value[0] == '\'' && value[^1] == '\'')
                    || (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}

/// <summary>
/// PR #824 review repair #3: 3-state outcome of resolving the active
/// domain's <c>execution_unit_regex</c>. Callers that should fail
/// closed on missing or invalid bindings inspect <see cref="Kind"/>
/// rather than treating a null regex as "no filter".
/// </summary>
internal enum ExecutionUnitRegexResolutionKind
{
    /// <summary>Bindings file present, field present, pattern compiles.</summary>
    Present,
    /// <summary>Bindings file missing, field absent, or read failed.</summary>
    MissingOrAbsent,
    /// <summary>Pattern present but does not compile (operator typo).</summary>
    InvalidPattern,
}

/// <summary>
/// PR #824 review repair #3: structured outcome of
/// <see cref="NextSliceDomainBindingsExecutionUnitRegex.Resolve"/>.
/// </summary>
internal sealed record ExecutionUnitRegexResolution
{
    public required ExecutionUnitRegexResolutionKind Kind { get; init; }
    public Regex? Regex { get; init; }
    public string? Pattern { get; init; }
    public string? BindingsPath { get; init; }
    public string? Detail { get; init; }

    public static ExecutionUnitRegexResolution Present(Regex regex, string pattern, string bindingsPath) => new()
    {
        Kind = ExecutionUnitRegexResolutionKind.Present,
        Regex = regex,
        Pattern = pattern,
        BindingsPath = bindingsPath,
    };

    public static ExecutionUnitRegexResolution MissingOrAbsent(string bindingsPath) => new()
    {
        Kind = ExecutionUnitRegexResolutionKind.MissingOrAbsent,
        BindingsPath = bindingsPath,
    };

    public static ExecutionUnitRegexResolution InvalidPattern(string pattern, string detail, string bindingsPath) => new()
    {
        Kind = ExecutionUnitRegexResolutionKind.InvalidPattern,
        Pattern = pattern,
        Detail = detail,
        BindingsPath = bindingsPath,
    };
}
