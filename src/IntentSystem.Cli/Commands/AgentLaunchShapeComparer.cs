using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

internal sealed record AgentLaunchShapeComparison
{
    public required bool Resolved { get; init; }
    public required bool Conforming { get; init; }
    public required string RecordedShape { get; init; }
    public string? ObservedShape { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// G666 compares structured process argv with the measured per-kind recipe.
/// It deliberately does not read terminal text. Option order and whitespace
/// are immaterial; recorded placeholders accept one concrete value and square
/// bracket groups remain optional.
/// </summary>
internal static partial class AgentLaunchShapeComparer
{
    private sealed record ExpectedOption(string Name, string? Value, bool Optional);

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
                RecordedShape = recipe.Invocation,
                Summary = $"No structured argv for running agent kind '{kind}' was available; recipe conformance was not inferred.",
            };
        }

        var expected = ParseExpected(recipe.Invocation);
        foreach (var process in candidates)
        {
            var observed = ParseObserved(process.Argv!);
            if (Matches(expected, observed))
            {
                return new AgentLaunchShapeComparison
                {
                    Resolved = true,
                    Conforming = true,
                    RecordedShape = recipe.Invocation,
                    ObservedShape = FormatObserved(process),
                    Summary = $"Observed launch shape conforms structurally to the recorded '{kind}' recipe.",
                };
            }
        }

        var selected = candidates
            .OrderBy(process => process.Argv!.Count)
            .ThenBy(process => process.Pid)
            .First();
        return new AgentLaunchShapeComparison
        {
            Resolved = true,
            Conforming = false,
            RecordedShape = recipe.Invocation,
            ObservedShape = FormatObserved(selected),
            Summary = $"Observed launch shape for agent kind '{kind}' does not conform to its recorded recipe.",
        };
    }

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
                if (!tokens[index].StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = index + 1 < tokens.Count && !tokens[index + 1].StartsWith("--", StringComparison.Ordinal)
                    ? tokens[++index]
                    : null;
                result.Add(new ExpectedOption(tokens[index - (value is null ? 0 : 1)], value, optional));
            }
        }
        return result;
    }

    private static IReadOnlyList<(string Name, string? Value)> ParseObserved(IReadOnlyList<string> argv)
    {
        var result = new List<(string Name, string? Value)>();
        var firstOption = argv.Select((value, index) => (value, index))
            .FirstOrDefault(item => item.value.StartsWith("--", StringComparison.Ordinal)).index;
        if (firstOption == 0 && !argv[0].StartsWith("--", StringComparison.Ordinal))
        {
            return result;
        }

        for (var index = firstOption; index < argv.Count; index++)
        {
            if (!argv[index].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = argv[index];
            var value = index + 1 < argv.Count && !argv[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? argv[++index]
                : null;
            result.Add((name, value));
        }
        return result;
    }

    private static bool Matches(
        IReadOnlyList<ExpectedOption> expected,
        IReadOnlyList<(string Name, string? Value)> observed)
    {
        var remaining = observed.ToList();
        foreach (var item in expected.Where(item => !item.Optional))
        {
            var index = remaining.FindIndex(candidate => OptionMatches(item, candidate));
            if (index < 0)
            {
                return false;
            }
            remaining.RemoveAt(index);
        }

        foreach (var item in expected.Where(item => item.Optional))
        {
            var index = remaining.FindIndex(candidate => OptionMatches(item, candidate));
            if (index >= 0)
            {
                remaining.RemoveAt(index);
            }
        }

        return remaining.Count == 0;
    }

    private static bool OptionMatches(ExpectedOption expected, (string Name, string? Value) observed) =>
        string.Equals(expected.Name, observed.Name, StringComparison.Ordinal)
        && (expected.Value is null
            ? observed.Value is null
            : IsPlaceholder(expected.Value)
                ? !string.IsNullOrWhiteSpace(observed.Value)
                : string.Equals(expected.Value, observed.Value, StringComparison.Ordinal));

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
