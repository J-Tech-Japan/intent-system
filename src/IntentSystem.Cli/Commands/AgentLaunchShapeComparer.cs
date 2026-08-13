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
/// Option aliases, order, and whitespace are immaterial. Recipe placeholders
/// are bound to the seat's recorded launch arguments/cwd before root comparison.
/// </summary>
internal static partial class AgentLaunchShapeComparer
{
    private sealed record ExpectedOption(string Name, string? Value, bool Optional);
    private sealed record ParsedOption(string Name, string? Value);
    private sealed record EnvelopeResult(AgentLaunchEnvelopeDrift Drift, IReadOnlyList<string> Differences);

    private const string SandboxOption = "--sandbox";
    private const string ApprovalOption = "--ask-for-approval";
    private const string RootOption = "--add-dir";
    private const string NetworkOption = "--network-access";
    private const string ConfigOption = "--config";

    private static readonly HashSet<string> BroadEnvelopeFlags = new(StringComparer.Ordinal)
    {
        "--allow-all",
        "--allow-all-paths",
        "--dangerously-bypass-approvals-and-sandbox",
        "--yolo",
    };

    private static readonly HashSet<string> CopilotUrlOptions = new(StringComparer.Ordinal)
    {
        "--allow-all-urls",
        "--allow-url",
        "--allow-domain",
    };

    public static AgentLaunchShapeComparison Compare(
        string kind,
        AgentLaunchRecipe recipe,
        IReadOnlyList<NotifyPaneProcess> processes,
        IReadOnlyList<string>? recordedLaunchArguments = null,
        string? recordedCwd = null,
        bool requireConcreteSeatRoots = false)
    {
        var recordedShape = FormatRecorded(recipe, recordedLaunchArguments, recordedCwd);
        var expected = BindConcreteSeatEnvelope(
            kind,
            ParseExpected(kind, recipe.Invocation),
            recordedLaunchArguments,
            recordedCwd);
        return CompareExpected(kind, recordedShape, expected, processes, requireConcreteSeatRoots);
    }

    public static AgentLaunchShapeComparison Compare(
        string kind,
        AgentLaunchEnvelopeProfile profile,
        IReadOnlyList<NotifyPaneProcess> processes)
    {
        if (!string.Equals(kind, profile.Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentLaunchShapeComparison
            {
                Resolved = false,
                Conforming = false,
                Drift = AgentLaunchEnvelopeDrift.Alarming,
                RecordedShape = FormatRecorded(profile),
                Summary = $"Envelope profile '{profile.Name}' kind '{profile.Kind}' does not match recorded seat kind '{kind}'; profile-invalid is fail-closed.",
            };
        }

        return CompareExpected(kind, FormatRecorded(profile), ExpectedFromProfile(profile), processes, false);
    }

    private static AgentLaunchShapeComparison CompareExpected(
        string kind,
        string recordedShape,
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<NotifyPaneProcess> processes,
        bool requireConcreteSeatRoots)
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
                RecordedShape = recordedShape,
                Summary = $"No structured argv for running agent kind '{kind}' was available; recipe conformance was not inferred.",
            };
        }

        var concreteRootsResolved = expected
            .Where(item => item.Name == RootOption)
            .All(item => !IsPlaceholder(item.Value));
        var comparisons = candidates
            .Select(process => (Process: process, Result: CompareEnvelope(
                kind,
                expected,
                ParseObserved(kind, process.Argv!),
                requireConcreteSeatRoots && !concreteRootsResolved)))
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
                RecordedShape = recordedShape,
                ObservedShape = FormatObserved(selected.Process),
                Summary = $"Observed launch envelope conforms structurally to the recorded '{kind}' baseline; model and reasoning effort are excluded by design.",
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
            RecordedShape = recordedShape,
            ObservedShape = FormatObserved(selected.Process),
            Summary = $"Observed launch envelope for agent kind '{kind}' has {classification} recipe drift: "
                + string.Join("; ", selected.Result.Differences),
        };
    }

    private static IReadOnlyList<ExpectedOption> ExpectedFromProfile(AgentLaunchEnvelopeProfile profile)
    {
        var expected = new List<ExpectedOption>
        {
            new(SandboxOption, profile.SandboxMode, Optional: false),
            new(ApprovalOption, profile.ApprovalMode, Optional: false),
            new(NetworkOption, profile.NetworkAccess, Optional: false),
        };
        expected.AddRange(profile.WritableRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => new ExpectedOption(RootOption, NormalizeRoot(root), Optional: false)));

        if (string.Equals(profile.Kind, "copilot", StringComparison.OrdinalIgnoreCase))
        {
            expected.AddRange(ParseTokens(profile.Kind, profile.PermissionOptions)
                .Where(option => option.Name == "--allow-all-tools" || CopilotUrlOptions.Contains(option.Name))
                .Select(option => new ExpectedOption(option.Name, option.Value, Optional: false)));
            expected.AddRange(profile.NetworkUrls
                .Select(url => new ExpectedOption("--allow-url", url, Optional: false)));
        }

        return expected;
    }

    private static IReadOnlyList<ExpectedOption> BindConcreteSeatEnvelope(
        string kind,
        IReadOnlyList<ExpectedOption> recipe,
        IReadOnlyList<string>? recordedLaunchArguments,
        string? recordedCwd)
    {
        var concrete = recordedLaunchArguments is { Count: > 0 }
            ? ParseObserved(kind, recordedLaunchArguments)
            : [];
        var result = recipe.ToList();

        OverlayScalar(result, concrete, SandboxOption);
        OverlayScalar(result, concrete, ApprovalOption);
        OverlayScalar(result, concrete, NetworkOption);
        var concreteNetwork = FindNetwork(concrete);
        if (concreteNetwork is not null)
        {
            result.RemoveAll(item => item.Name == NetworkOption);
            result.Add(new ExpectedOption(NetworkOption, concreteNetwork, Optional: false));
        }

        if (string.Equals(kind, "copilot", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var option in concrete.Where(item =>
                item.Name == "--allow-all-tools" || CopilotUrlOptions.Contains(item.Name)))
            {
                if (!result.Any(item => item.Name == option.Name && OptionValueMatches(item.Value, option.Value)))
                {
                    result.Add(new ExpectedOption(option.Name, option.Value, Optional: false));
                }
            }
        }

        var concreteRoots = concrete.Where(item => item.Name == RootOption && !string.IsNullOrWhiteSpace(item.Value)).ToArray();
        if (concreteRoots.Length > 0 || !string.IsNullOrWhiteSpace(recordedCwd))
        {
            result.RemoveAll(item => item.Name == RootOption);
            if (concreteRoots.Length > 0)
            {
                result.AddRange(concreteRoots.Select(item => new ExpectedOption(RootOption, NormalizeRoot(item.Value!), Optional: false)));
            }
            else
            {
                result.Add(new ExpectedOption(RootOption, NormalizeRoot(recordedCwd!), Optional: false));
            }
        }

        return result;
    }

    private static void OverlayScalar(
        List<ExpectedOption> result,
        IReadOnlyList<ParsedOption> concrete,
        string name)
    {
        var value = concrete.LastOrDefault(item => item.Name == name);
        if (value is null)
        {
            return;
        }

        result.RemoveAll(item => item.Name == name);
        result.Add(new ExpectedOption(name, value.Value, Optional: false));
    }

    private static EnvelopeResult CompareEnvelope(
        string kind,
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<ParsedOption> observed,
        bool concreteSeatRootsUnavailable)
    {
        var alarming = new List<string>();
        var informational = new List<string>();

        CompareScalar("sandbox mode", SandboxOption, expected, observed, SandboxRank, alarming, informational);
        CompareScalar("approval mode", ApprovalOption, expected, observed, ApprovalRank, alarming, informational);
        CompareRoots(expected, observed, alarming, informational);
        if (concreteSeatRootsUnavailable)
        {
            alarming.Add("concrete recorded writable-root boundary is unavailable for this seat; placeholder equality is not accepted");
        }
        CompareNetwork(expected, observed, alarming, informational);
        if (string.Equals(kind, "copilot", StringComparison.OrdinalIgnoreCase))
        {
            CompareCopilotPermissions(expected, observed, alarming, informational);
        }

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
        string name,
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<ParsedOption> observed,
        Func<string, int?> rank,
        List<string> alarming,
        List<string> informational)
    {
        var recorded = expected.LastOrDefault(item => item.Name == name);
        var actual = observed.LastOrDefault(item => item.Name == name);
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
        var recordedRoots = expected.Where(item => item.Name == RootOption).ToList();
        var observedRoots = observed
            .Where(item => item.Name == RootOption && !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => new ParsedOption(item.Name, NormalizeRoot(item.Value!)))
            .ToList();
        if (recordedRoots.Count == 0)
        {
            if (observedRoots.Count > 0)
            {
                alarming.Add($"{observedRoots.Count} writable root(s) are present beyond the recorded recipe");
            }
            return;
        }

        // Preserve compatibility only when the caller lacks concrete seat
        // evidence. Production supervision always binds launch_args or cwd.
        if (recordedRoots.Any(item => IsPlaceholder(item.Value)))
        {
            CompareUnboundRoots(recordedRoots, observedRoots, alarming, informational);
            return;
        }

        var remainingRecorded = recordedRoots
            .Select(item => item with { Value = NormalizeRoot(item.Value!) })
            .ToList();
        var remainingObserved = observedRoots.ToList();

        for (var index = remainingObserved.Count - 1; index >= 0; index--)
        {
            var exact = remainingRecorded.FindIndex(item => RootEquals(item.Value!, remainingObserved[index].Value!));
            if (exact >= 0)
            {
                remainingRecorded.RemoveAt(exact);
                remainingObserved.RemoveAt(index);
            }
        }

        for (var index = remainingObserved.Count - 1; index >= 0; index--)
        {
            var narrower = remainingRecorded.FindIndex(item => IsDescendant(remainingObserved[index].Value!, item.Value!));
            if (narrower >= 0)
            {
                informational.Add($"writable root '{remainingObserved[index].Value}' is narrower than recorded '{remainingRecorded[narrower].Value}'");
                remainingRecorded.RemoveAt(narrower);
                remainingObserved.RemoveAt(index);
            }
        }

        for (var index = remainingObserved.Count - 1; index >= 0; index--)
        {
            var broader = remainingRecorded.FindIndex(item => IsDescendant(item.Value!, remainingObserved[index].Value!));
            if (broader >= 0)
            {
                alarming.Add($"writable root '{remainingObserved[index].Value}' is broader than recorded '{remainingRecorded[broader].Value}'");
                remainingRecorded.RemoveAt(broader);
                remainingObserved.RemoveAt(index);
            }
        }

        while (remainingObserved.Count > 0 && remainingRecorded.Count > 0)
        {
            alarming.Add($"unrelated writable root substitution '{remainingObserved[0].Value}' does not preserve recorded boundary '{remainingRecorded[0].Value}'");
            remainingObserved.RemoveAt(0);
            remainingRecorded.RemoveAt(0);
        }

        if (remainingObserved.Count > 0)
        {
            alarming.Add($"extra writable root(s) broaden the envelope: {string.Join(", ", remainingObserved.Select(item => item.Value))}");
        }
        var missingRequired = remainingRecorded.Where(item => !item.Optional).ToArray();
        if (missingRequired.Length > 0)
        {
            informational.Add($"fewer writable root(s) narrow the envelope; absent recorded root(s): {string.Join(", ", missingRequired.Select(item => item.Value))}");
        }
    }

    private static void CompareUnboundRoots(
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<ParsedOption> observed,
        List<string> alarming,
        List<string> informational)
    {
        var required = expected.Count(item => !item.Optional);
        if (observed.Count > expected.Count)
        {
            alarming.Add($"extra writable root(s) broaden the envelope: {string.Join(", ", observed.Skip(expected.Count).Select(item => item.Value))}");
        }
        if (observed.Count < required)
        {
            informational.Add($"fewer writable root(s) narrow the envelope; {required - observed.Count} required concrete boundary value(s) are absent");
        }
    }

    private static void CompareCopilotPermissions(
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<ParsedOption> observed,
        List<string> alarming,
        List<string> informational)
    {
        var recordedAllTools = expected.Any(item => item.Name == "--allow-all-tools");
        var observedAllTools = observed.Any(item => item.Name == "--allow-all-tools");
        if (recordedAllTools && !observedAllTools)
        {
            informational.Add("Copilot permission envelope omits recorded --allow-all-tools and is narrower");
        }
        else if (!recordedAllTools && observedAllTools)
        {
            alarming.Add("Copilot permission envelope adds --allow-all-tools beyond the recorded recipe");
        }

        var recordedUrls = expected
            .Where(item => CopilotUrlOptions.Contains(item.Name))
            .Select(FormatOption)
            .ToHashSet(StringComparer.Ordinal);
        var observedUrls = observed
            .Where(item => CopilotUrlOptions.Contains(item.Name))
            .Select(FormatOption)
            .ToHashSet(StringComparer.Ordinal);
        var broaderUrls = observedUrls.Except(recordedUrls, StringComparer.Ordinal).ToArray();
        if (broaderUrls.Length > 0)
        {
            alarming.Add($"Copilot URL/network access broadens the recorded envelope: {string.Join(", ", broaderUrls)}");
        }
        var omittedUrls = recordedUrls.Except(observedUrls, StringComparer.Ordinal).ToArray();
        if (omittedUrls.Length > 0)
        {
            informational.Add($"Copilot URL/network access is narrower; omitted recorded form(s): {string.Join(", ", omittedUrls)}");
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
            if (option.Name == NetworkOption)
            {
                return option.Value ?? "enabled";
            }
            if (option.Name == ConfigOption
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

    private static IReadOnlyList<ExpectedOption> ParseExpected(string kind, string invocation)
    {
        var separator = invocation.IndexOf(" -- ", StringComparison.Ordinal);
        var tail = separator >= 0 ? invocation[(separator + 4)..] : invocation;
        var result = new List<ExpectedOption>();
        foreach (Match match in RecipeToken().Matches(tail))
        {
            var optional = match.Groups["optional"].Success;
            var text = optional ? match.Groups["optional"].Value : match.Groups["required"].Value;
            result.AddRange(ParseTokens(kind, Tokenize(text)).Select(item => new ExpectedOption(item.Name, item.Value, optional)));
        }
        return result;
    }

    private static IReadOnlyList<ParsedOption> ParseObserved(string kind, IReadOnlyList<string> argv) =>
        ParseTokens(kind, argv);

    private static IReadOnlyList<ParsedOption> ParseTokens(string kind, IReadOnlyList<string> tokens)
    {
        var result = new List<ParsedOption>();
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!IsOption(tokens[index]))
            {
                continue;
            }

            var parsed = SplitOption(tokens[index]);
            var name = NormalizeOptionName(kind, parsed.Name);
            var value = parsed.Value ?? (index + 1 < tokens.Count && !IsOption(tokens[index + 1])
                ? tokens[++index]
                : null);
            result.Add(new ParsedOption(name, value));
        }
        return result;
    }

    private static string NormalizeOptionName(string kind, string name)
    {
        if (string.Equals(kind, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return name switch
            {
                "-s" or "--sandbox-mode" => SandboxOption,
                "-a" or "--approval-mode" or "--approval-policy" => ApprovalOption,
                "-c" => ConfigOption,
                "-m" => "--model",
                _ => NormalizeGenericOption(name),
            };
        }
        return NormalizeGenericOption(name);
    }

    private static string NormalizeGenericOption(string name) => name switch
    {
        "--sandbox-mode" => SandboxOption,
        "--approval-mode" or "--approval-policy" => ApprovalOption,
        "--writable-root" => RootOption,
        "--network" => NetworkOption,
        _ => name,
    };

    private static ParsedOption SplitOption(string token)
    {
        var separator = token.IndexOf('=');
        return separator > 0
            ? new ParsedOption(token[..separator], token[(separator + 1)..])
            : new ParsedOption(token, null);
    }

    private static bool IsOption(string value) =>
        value.Length > 1 && value[0] == '-';

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

    private static bool IsPlaceholder(string? value) =>
        value is { Length: > 2 } && value[0] == '<' && value[^1] == '>';

    private static string NormalizeRoot(string value)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.TrimEndingDirectorySeparator(value);
        }
    }

    private static bool RootEquals(string left, string right) =>
        string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsDescendant(string candidate, string parent)
    {
        if (RootEquals(candidate, parent))
        {
            return false;
        }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var primaryPrefix = Path.EndsInDirectorySeparator(parent)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        if (candidate.StartsWith(primaryPrefix, comparison))
        {
            return true;
        }
        var alternatePrefix = parent.EndsWith(Path.AltDirectorySeparatorChar)
            ? parent
            : parent + Path.AltDirectorySeparatorChar;
        return candidate.StartsWith(alternatePrefix, comparison);
    }

    private static string FormatOption(ParsedOption option) =>
        option.Value is null ? option.Name : $"{option.Name}={option.Value}";

    private static string FormatOption(ExpectedOption option) =>
        option.Value is null ? option.Name : $"{option.Name}={option.Value}";

    private static IReadOnlyList<string> Tokenize(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string FormatObserved(NotifyPaneProcess process) =>
        process.Argv is { Count: > 0 }
            ? string.Join(' ', process.Argv)
            : process.CommandLine ?? "<unavailable>";

    private static string FormatRecorded(
        AgentLaunchRecipe recipe,
        IReadOnlyList<string>? recordedLaunchArguments,
        string? recordedCwd)
    {
        if (recordedLaunchArguments is { Count: > 0 })
        {
            return $"{recipe.Invocation} [concrete seat launch args: {string.Join(' ', recordedLaunchArguments)}]";
        }
        return string.IsNullOrWhiteSpace(recordedCwd)
            ? recipe.Invocation
            : $"{recipe.Invocation} [concrete seat cwd root: {NormalizeRoot(recordedCwd)}]";
    }

    private static string FormatRecorded(AgentLaunchEnvelopeProfile profile) =>
        $"profile '{profile.Name}' kind={profile.Kind}; sandbox={profile.SandboxMode}; approval={profile.ApprovalMode}; "
        + $"roots_policy={profile.RootsPolicy}; writable_roots=[{string.Join(", ", profile.WritableRoots)}]; "
        + $"network={profile.NetworkAccess}; transport={profile.TransportMode}; evidence={profile.Evidence}; "
        + $"recorded_at={profile.RecordedAt}; digest={profile.Digest ?? "<computed>"}";

    [GeneratedRegex(@"\[(?<optional>[^\]]+)\]|(?<required>[^\s]+(?:\s+(?!\[)[^\s]+)?)")]
    private static partial Regex RecipeToken();
}
