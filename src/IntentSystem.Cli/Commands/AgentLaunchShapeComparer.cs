using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

internal enum AgentLaunchEnvelopeDrift
{
    None,
    Informational,
    Alarming,
}

internal sealed record AgentLaunchShapeComparison
{
    public required bool Resolved { get; init; }
    public required bool Conforming { get; init; }
    public required AgentLaunchEnvelopeDrift Drift { get; init; }
    public required string RecordedShape { get; init; }
    public string? ObservedShape { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// G684 compares only the security envelope in structured process argv. Model
/// and reasoning effort are human-selected wish fields and never participate.
/// Option order and whitespace are immaterial; recorded placeholders accept a
/// concrete value and square-bracket groups remain optional.
/// </summary>
internal static partial class AgentLaunchShapeComparer
{
    private sealed record ExpectedOption(string Name, string? Value, bool Optional);
    private sealed record ParsedOption(string Name, string? Value);
    private sealed record EnvelopeResult(AgentLaunchEnvelopeDrift Drift, IReadOnlyList<string> Differences);

    private static readonly HashSet<string> SandboxOptions = new(StringComparer.Ordinal)
    {
        "--sandbox",
        "--sandbox-mode",
    };

    private static readonly HashSet<string> ApprovalOptions = new(StringComparer.Ordinal)
    {
        "--ask-for-approval",
        "--approval-mode",
        "--approval-policy",
    };

    private static readonly HashSet<string> RootOptions = new(StringComparer.Ordinal)
    {
        "--add-dir",
        "--writable-root",
    };

    private static readonly HashSet<string> NetworkOptions = new(StringComparer.Ordinal)
    {
        "--network",
        "--network-access",
    };

    private static readonly HashSet<string> BroadEnvelopeFlags = new(StringComparer.Ordinal)
    {
        "--allow-all-paths",
        "--dangerously-bypass-approvals-and-sandbox",
        "--yolo",
    };

    public static AgentLaunchShapeComparison Compare(
        string kind,
        AgentLaunchRecipe recipe,
        IReadOnlyList<NotifyPaneProcess> processes)
    {
        var candidates = processes
            .Where(process => LooksLikeKind(process, kind) && process.Argv is { Count: > 0 })
            .ToArray();
        if (candidates.Length == 0)
        {
            return new AgentLaunchShapeComparison
            {
                Resolved = false,
                Conforming = false,
                Drift = AgentLaunchEnvelopeDrift.None,
                RecordedShape = recipe.Invocation,
                Summary = $"No structured argv for running agent kind '{kind}' was available; recipe conformance was not inferred.",
            };
        }

        var expected = ParseExpected(recipe.Invocation);
        var comparisons = candidates
            .Select(process => (Process: process, Result: CompareEnvelope(expected, ParseObserved(process.Argv!))))
            .ToArray();
        var selected = comparisons
            .OrderBy(item => item.Result.Drift)
            .ThenBy(item => item.Process.Argv!.Count)
            .ThenBy(item => item.Process.Pid)
            .First();

        if (selected.Result.Drift == AgentLaunchEnvelopeDrift.None)
        {
            return new AgentLaunchShapeComparison
            {
                Resolved = true,
                Conforming = true,
                Drift = AgentLaunchEnvelopeDrift.None,
                RecordedShape = recipe.Invocation,
                ObservedShape = FormatObserved(selected.Process),
                Summary = $"Observed launch envelope conforms structurally to the recorded '{kind}' recipe; model and reasoning effort are excluded by design.",
            };
        }

        var classification = selected.Result.Drift == AgentLaunchEnvelopeDrift.Alarming
            ? "alarming"
            : "informational (narrower)";
        return new AgentLaunchShapeComparison
        {
            Resolved = true,
            Conforming = false,
            Drift = selected.Result.Drift,
            RecordedShape = recipe.Invocation,
            ObservedShape = FormatObserved(selected.Process),
            Summary = $"Observed launch envelope for agent kind '{kind}' has {classification} recipe drift: "
                + string.Join("; ", selected.Result.Differences),
        };
    }

    private static EnvelopeResult CompareEnvelope(
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<ParsedOption> observed)
    {
        var alarming = new List<string>();
        var informational = new List<string>();

        CompareScalar("sandbox mode", SandboxOptions, expected, observed, SandboxRank, alarming, informational);
        CompareScalar("approval mode", ApprovalOptions, expected, observed, ApprovalRank, alarming, informational);
        CompareRoots(expected, observed, alarming, informational);
        CompareNetwork(expected, observed, alarming, informational);

        var broadFlags = observed
            .Where(item => BroadEnvelopeFlags.Contains(item.Name))
            .Select(item => item.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (broadFlags.Length > 0)
        {
            alarming.Add($"broader blanket envelope flag(s) present: {string.Join(", ", broadFlags)}");
        }

        if (alarming.Count > 0)
        {
            return new EnvelopeResult(AgentLaunchEnvelopeDrift.Alarming, alarming.Concat(informational).ToArray());
        }

        return informational.Count > 0
            ? new EnvelopeResult(AgentLaunchEnvelopeDrift.Informational, informational)
            : new EnvelopeResult(AgentLaunchEnvelopeDrift.None, []);
    }

    private static void CompareScalar(
        string field,
        IReadOnlySet<string> names,
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<ParsedOption> observed,
        Func<string, int?> rank,
        List<string> alarming,
        List<string> informational)
    {
        var recorded = expected.FirstOrDefault(item => names.Contains(item.Name));
        var actual = observed.LastOrDefault(item => names.Contains(item.Name));
        if (recorded is null)
        {
            if (actual is not null && rank(actual.Value ?? string.Empty) is > 0)
            {
                alarming.Add($"{field} '{actual.Value ?? "<missing-value>"}' is broader than the recipe's implicit bounded default");
            }
            return;
        }

        if (actual is null)
        {
            if (!recorded.Optional)
            {
                alarming.Add($"required {field} '{recorded.Value ?? "<flag>"}' is missing");
            }
            return;
        }

        if (OptionValueMatches(recorded.Value, actual.Value))
        {
            return;
        }

        var recordedRank = rank(recorded.Value ?? string.Empty);
        var observedRank = rank(actual.Value ?? string.Empty);
        if (recordedRank is not null && observedRank is not null && observedRank < recordedRank)
        {
            informational.Add($"{field} '{actual.Value}' is narrower than recorded '{recorded.Value}'");
        }
        else
        {
            alarming.Add($"{field} '{actual.Value ?? "<missing-value>"}' is broader than or incompatible with recorded '{recorded.Value ?? "<flag>"}'");
        }
    }

    private static void CompareRoots(
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<ParsedOption> observed,
        List<string> alarming,
        List<string> informational)
    {
        var recordedRoots = expected.Where(item => RootOptions.Contains(item.Name)).ToArray();
        var observedRoots = observed.Where(item => RootOptions.Contains(item.Name)).ToArray();
        if (recordedRoots.Length == 0)
        {
            if (observedRoots.Length > 0)
            {
                alarming.Add($"{observedRoots.Length} writable root(s) are present beyond the recorded recipe");
            }
            return;
        }

        if (observedRoots.Length == 0 && recordedRoots.Any(item => !item.Optional))
        {
            alarming.Add("required writable-root/add-dir bound is missing");
            return;
        }

        var remaining = observedRoots.ToList();
        var missingRequired = new List<ExpectedOption>();
        foreach (var root in recordedRoots.Where(item => !item.Optional))
        {
            var index = remaining.FindIndex(candidate =>
                string.Equals(candidate.Name, root.Name, StringComparison.Ordinal)
                && OptionValueMatches(root.Value, candidate.Value));
            if (index >= 0)
            {
                remaining.RemoveAt(index);
            }
            else
            {
                missingRequired.Add(root);
            }
        }

        foreach (var root in recordedRoots.Where(item => item.Optional))
        {
            var index = remaining.FindIndex(candidate =>
                string.Equals(candidate.Name, root.Name, StringComparison.Ordinal)
                && OptionValueMatches(root.Value, candidate.Value));
            if (index >= 0)
            {
                remaining.RemoveAt(index);
            }
        }

        if (remaining.Count > 0)
        {
            alarming.Add($"extra writable root(s) broaden the envelope: {string.Join(", ", remaining.Select(item => item.Value ?? "<missing-value>"))}");
        }
        if (missingRequired.Count > 0)
        {
            informational.Add($"fewer writable root(s) narrow the envelope; absent recorded root(s): {string.Join(", ", missingRequired.Select(item => item.Value ?? item.Name))}");
        }
    }

    private static void CompareNetwork(
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<ParsedOption> observed,
        List<string> alarming,
        List<string> informational)
    {
        var recorded = FindNetwork(expected.Select(item => new ParsedOption(item.Name, item.Value)));
        var actual = FindNetwork(observed);
        if (recorded is null && actual is null)
        {
            return;
        }

        var recordedRank = NetworkRank(recorded ?? "disabled");
        var observedRank = NetworkRank(actual ?? "disabled");
        if (recordedRank is null || observedRank is null)
        {
            if (!string.Equals(recorded, actual, StringComparison.OrdinalIgnoreCase))
            {
                alarming.Add($"network access '{actual ?? "<implicit-disabled>"}' is incompatible with recorded '{recorded ?? "<implicit-disabled>"}'");
            }
        }
        else if (observedRank > recordedRank)
        {
            alarming.Add($"network access '{actual}' is broader than recorded '{recorded ?? "<implicit-disabled>"}'");
        }
        else if (observedRank < recordedRank)
        {
            informational.Add($"network access '{actual ?? "<implicit-disabled>"}' is narrower than recorded '{recorded}'");
        }
    }

    private static string? FindNetwork(IEnumerable<ParsedOption> options)
    {
        foreach (var option in options.Reverse())
        {
            if (NetworkOptions.Contains(option.Name))
            {
                return option.Value ?? "enabled";
            }
            if (option.Name is "--config" or "-c"
                && option.Value is { } config
                && TryReadConfig(config, "sandbox_workspace_write.network_access", out var value))
            {
                return value;
            }
        }
        return null;
    }

    private static bool TryReadConfig(string config, string key, out string value)
    {
        var separator = config.IndexOf('=');
        if (separator > 0 && string.Equals(config[..separator].Trim(), key, StringComparison.Ordinal))
        {
            value = config[(separator + 1)..].Trim();
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static int? SandboxRank(string value) => value.ToLowerInvariant() switch
    {
        "read-only" => 0,
        "workspace-write" => 1,
        "danger-full-access" => 2,
        _ => null,
    };

    private static int? ApprovalRank(string value) => value.ToLowerInvariant() switch
    {
        "untrusted" => 0,
        "on-request" or "on-failure" => 1,
        "never" => 2,
        _ => null,
    };

    private static int? NetworkRank(string value) => value.ToLowerInvariant() switch
    {
        "false" or "disabled" or "deny" or "none" or "restricted" => 0,
        "true" or "enabled" or "allow" or "full" => 1,
        _ => null,
    };

    private static IReadOnlyList<ExpectedOption> ParseExpected(string invocation)
    {
        var separator = invocation.IndexOf(" -- ", StringComparison.Ordinal);
        var tail = separator >= 0 ? invocation[(separator + 4)..] : invocation;
        var result = new List<ExpectedOption>();
        foreach (Match match in RecipeToken().Matches(tail))
        {
            var optional = match.Groups["optional"].Success;
            var text = optional ? match.Groups["optional"].Value : match.Groups["required"].Value;
            var tokens = Tokenize(text);
            for (var index = 0; index < tokens.Count; index++)
            {
                if (!IsOption(tokens[index]))
                {
                    continue;
                }

                var parsed = SplitOption(tokens[index]);
                var value = parsed.Value ?? (index + 1 < tokens.Count && !IsOption(tokens[index + 1])
                    ? tokens[++index]
                    : null);
                result.Add(new ExpectedOption(parsed.Name, value, optional));
            }
        }
        return result;
    }

    private static IReadOnlyList<ParsedOption> ParseObserved(IReadOnlyList<string> argv)
    {
        var result = new List<ParsedOption>();
        for (var index = 0; index < argv.Count; index++)
        {
            if (!IsOption(argv[index]))
            {
                continue;
            }

            var parsed = SplitOption(argv[index]);
            var value = parsed.Value ?? (index + 1 < argv.Count && !IsOption(argv[index + 1])
                ? argv[++index]
                : null);
            result.Add(new ParsedOption(parsed.Name, value));
        }
        return result;
    }

    private static ParsedOption SplitOption(string token)
    {
        var separator = token.IndexOf('=');
        return separator > 0
            ? new ParsedOption(token[..separator], token[(separator + 1)..])
            : new ParsedOption(token, null);
    }

    private static bool IsOption(string value) =>
        value.StartsWith("--", StringComparison.Ordinal) || string.Equals(value, "-c", StringComparison.Ordinal);

    private static bool OptionValueMatches(string? expected, string? observed) =>
        expected is null
            ? observed is null
            : IsPlaceholder(expected)
                ? !string.IsNullOrWhiteSpace(observed)
                : string.Equals(expected, observed, StringComparison.Ordinal);

    private static bool LooksLikeKind(NotifyPaneProcess process, string kind)
    {
        static string Basename(string value) => Path.GetFileNameWithoutExtension(value);
        return string.Equals(process.Name, kind, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(process.Argv0)
                && string.Equals(Basename(process.Argv0), kind, StringComparison.OrdinalIgnoreCase))
            || process.Argv?.Any(value => string.Equals(Basename(value), kind, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool IsPlaceholder(string value) =>
        value.Length > 2 && value[0] == '<' && value[^1] == '>';

    private static IReadOnlyList<string> Tokenize(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string FormatObserved(NotifyPaneProcess process) =>
        process.Argv is { Count: > 0 }
            ? string.Join(' ', process.Argv)
            : process.CommandLine ?? "<unavailable>";

    [GeneratedRegex(@"\[(?<optional>[^\]]+)\]|(?<required>[^\s]+(?:\s+(?!\[)[^\s]+)?)")]
    private static partial Regex RecipeToken();
}
