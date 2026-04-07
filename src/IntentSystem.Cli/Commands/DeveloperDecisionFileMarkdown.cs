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
    private static readonly string[] RequiredSections =
    [
        "Confirm",
        "Reject",
        "Clarify",
        "Defer"
    ];

    public static DeveloperDecisionFile Deserialize(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var sections = RequiredSections.ToDictionary(section => section, _ => new List<string>(), StringComparer.Ordinal);
        var seenSections = new HashSet<string>(StringComparer.Ordinal);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.None);
        var sawTitle = false;
        string? currentSection = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "# Developer Confirmation Decisions", StringComparison.Ordinal))
            {
                sawTitle = true;
                currentSection = null;
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var section = line[3..].Trim();
                if (!sections.ContainsKey(section))
                {
                    throw new InvalidOperationException($"Developer decision file section '{section}' is not supported.");
                }

                currentSection = section;
                seenSections.Add(section);
                continue;
            }

            if (!line.StartsWith("- ", StringComparison.Ordinal) || currentSection is null)
            {
                continue;
            }

            var value = line[2..].Trim();
            if (!string.Equals(value, "none", StringComparison.Ordinal))
            {
                sections[currentSection].Add(value);
            }
        }

        if (!sawTitle)
        {
            throw new InvalidOperationException("Developer decision file must start with '# Developer Confirmation Decisions'.");
        }

        var missingSection = RequiredSections.FirstOrDefault(section => !seenSections.Contains(section));
        if (missingSection is not null)
        {
            throw new InvalidOperationException($"Developer decision file must contain section '## {missingSection}'.");
        }

        return new DeveloperDecisionFile
        {
            ConfirmedItems = sections["Confirm"].ToArray(),
            RejectedItems = sections["Reject"].ToArray(),
            ClarifyItems = sections["Clarify"].ToArray(),
            DeferredItems = sections["Defer"].ToArray()
        };
    }
}
