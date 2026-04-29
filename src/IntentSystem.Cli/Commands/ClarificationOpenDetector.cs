namespace IntentSystem.Cli.Commands;

/// <summary>
/// Shared structural parser for parent-host
/// <c>intents/&lt;domain&gt;/clarifications/open.md</c> files. The host shape places
/// live blockers as list items under a <c>## Current Open Blockers</c> heading,
/// with an explicit no-blocker sentinel list item when nothing is currently
/// blocking. Detect open blockers structurally so durable prose / recently-resolved
/// notes do not falsely look like an open blocker (G179 review fix; reused by
/// G180 context collect).
/// </summary>
internal static class ClarificationOpenDetector
{
    public static bool HasOpenBlocker(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var lines = content!.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inBlockerSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                inBlockerSection = string.Equals(
                    heading,
                    "Current Open Blockers",
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inBlockerSection)
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal)
                && !trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                continue;
            }

            var item = trimmed[2..].Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (IsNoBlockerSentinel(item))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsNoBlockerSentinel(string item)
    {
        // Host-established no-blocker sentinel (Japanese). Match on the substring
        // so any minor surrounding wording (e.g. trailing punctuation, links) is
        // still treated as a non-actionable entry.
        const string japaneseSentinel = "現時点で child issue cut を要する root blocker はない";
        if (item.Contains(japaneseSentinel, StringComparison.Ordinal))
        {
            return true;
        }

        // Defensive English fallback for any future host that uses a parallel
        // English wording. Kept narrow on purpose so unrelated bullets do not
        // accidentally suppress real blockers.
        return item.Contains(
            "no root blocker requiring child issue cut",
            StringComparison.OrdinalIgnoreCase);
    }
}
