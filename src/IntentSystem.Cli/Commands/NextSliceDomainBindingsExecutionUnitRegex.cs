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
    /// </summary>
    public static Regex? TryLoad(CliContext context, string? domain)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var bindingsPath = ResolveBindingsAbsolutePath(context, domain);
        if (string.IsNullOrWhiteSpace(bindingsPath) || !File.Exists(bindingsPath))
        {
            return null;
        }

        string content;
        try
        {
            content = File.ReadAllText(bindingsPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var pattern = ExtractExecutionUnitRegex(content);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return null;
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
