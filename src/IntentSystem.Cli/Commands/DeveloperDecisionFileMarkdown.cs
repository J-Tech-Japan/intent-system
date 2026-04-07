namespace IntentSystem.Cli.Commands;

internal sealed record DeveloperDecisionFile
{
    public required IReadOnlyList<string> ConfirmedItems { get; init; }

    public required IReadOnlyList<string> RejectedItems { get; init; }

    public required IReadOnlyList<string> ClarifyItems { get; init; }

    public required IReadOnlyList<string> DeferredItems { get; init; }
}

internal static class DeveloperDecisionFileMarkdown
{
    private static readonly string[] SupportedDecisionPrefixes =
    [
        "confirm:",
        "reject:",
        "clarify:",
        "defer:"
    ];

    public static DeveloperDecisionFile Deserialize(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var confirmedItems = new List<string>();
        var rejectedItems = new List<string>();
        var clarifyItems = new List<string>();
        var deferredItems = new List<string>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.None);

        foreach (var rawLine in lines)
        {
            var line = NormalizeDecisionLine(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryReadDecision(line, out var decisionType, out var decisionValue))
            {
                switch (decisionType)
                {
                    case "confirm":
                        confirmedItems.Add(decisionValue);
                        break;
                    case "reject":
                        rejectedItems.Add(decisionValue);
                        break;
                    case "clarify":
                        clarifyItems.Add(decisionValue);
                        break;
                    case "defer":
                        deferredItems.Add(decisionValue);
                        break;
                }
            }
        }

        if (confirmedItems.Count == 0
            && rejectedItems.Count == 0
            && clarifyItems.Count == 0
            && deferredItems.Count == 0)
        {
            throw new InvalidOperationException(
                "Prepared developer decision file must contain at least one bounded decision line using 'confirm:', 'reject:', 'clarify:', or 'defer:'.");
        }

        return new DeveloperDecisionFile
        {
            ConfirmedItems = confirmedItems,
            RejectedItems = rejectedItems,
            ClarifyItems = clarifyItems,
            DeferredItems = deferredItems
        };
    }

    private static string NormalizeDecisionLine(string rawLine)
    {
        var line = rawLine.Trim();
        if (line.Length == 0)
        {
            return string.Empty;
        }

        if (line.StartsWith("- ", StringComparison.Ordinal)
            || line.StartsWith("* ", StringComparison.Ordinal))
        {
            return line[2..].TrimStart();
        }

        var periodIndex = line.IndexOf(". ", StringComparison.Ordinal);
        if (periodIndex > 0 && line[..periodIndex].All(char.IsDigit))
        {
            return line[(periodIndex + 2)..].TrimStart();
        }

        return line;
    }

    private static bool TryReadDecision(string line, out string decisionType, out string decisionValue)
    {
        foreach (var prefix in SupportedDecisionPrefixes)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                decisionType = prefix[..^1];
                decisionValue = line;
                return true;
            }
        }

        decisionType = string.Empty;
        decisionValue = string.Empty;
        return false;
    }
}
