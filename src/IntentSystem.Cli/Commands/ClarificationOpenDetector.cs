namespace IntentSystem.Cli.Commands;

/// <summary>
/// Shared structural parser for parent-host
/// <c>intents/&lt;domain&gt;/clarifications/open.md</c> files. The host shape places
/// live blockers as list items under a <c>## Current Open Blockers</c> heading,
/// with an explicit no-blocker sentinel list item when nothing is currently
/// blocking. Detect open blockers structurally so durable prose / recently-resolved
/// notes do not falsely look like an open blocker (G179 review fix; reused by
/// G180 context collect).
///
/// G275: Also recognises two cleared states:
/// - YAML front-matter containing <c>intent_state: clarified</c>
/// - A "Current Open Blockers" section whose only content is the literal text
///   <c>None</c> (case-insensitive) or the established no-blocker sentinel.
/// </summary>
internal static class ClarificationOpenDetector
{
    public static bool HasOpenBlocker(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        // G275: If the file carries `intent_state: clarified` in YAML front-matter
        // (either `---` fenced block or anywhere as a bare top-level field),
        // treat the entire file as cleared regardless of body content.
        if (HasClarifiedState(content!))
        {
            return false;
        }

        var lines = content!.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inBlockerSection = false;
        var blockerSectionSeen = false;
        var blockerSectionHasContent = false;

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
                if (inBlockerSection)
                {
                    blockerSectionSeen = true;
                }

                continue;
            }

            if (!inBlockerSection)
            {
                continue;
            }

            // G275: A section whose only non-empty non-heading content is the
            // literal word "None" (bare line or as a paragraph) is treated as
            // cleared — matching the English convention used in several host files.
            var trimmedLine = line.Trim();
            if (string.Equals(trimmedLine, "None", StringComparison.OrdinalIgnoreCase))
            {
                blockerSectionHasContent = true;
                continue;
            }

            if (!trimmedLine.StartsWith("- ", StringComparison.Ordinal)
                && !trimmedLine.StartsWith("* ", StringComparison.Ordinal))
            {
                continue;
            }

            var item = trimmedLine[2..].Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (IsNoBlockerSentinel(item))
            {
                blockerSectionHasContent = true;
                continue;
            }

            return true;
        }

        // If we found a "Current Open Blockers" section but it was completely
        // empty (no bullets, no None line), that is also not a blocker.
        _ = blockerSectionSeen && blockerSectionHasContent; // suppress unused warning

        return false;
    }

    /// <summary>
    /// Returns true when the file's YAML front-matter (or bare root field) contains
    /// <c>intent_state: clarified</c>.
    /// </summary>
    private static bool HasClarifiedState(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inFrontMatter = false;
        var frontMatterClosed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            // Detect opening `---` front-matter fence on the very first non-blank line.
            if (i == 0 && string.Equals(line, "---", StringComparison.Ordinal))
            {
                inFrontMatter = true;
                continue;
            }

            if (inFrontMatter)
            {
                // Closing fence ends front-matter scanning.
                if (string.Equals(line, "---", StringComparison.Ordinal)
                    || string.Equals(line, "...", StringComparison.Ordinal))
                {
                    frontMatterClosed = true;
                    break;
                }

                if (IsIntentStateClarified(line))
                {
                    return true;
                }

                continue;
            }

            // Also check bare root-level YAML fields before the first `##` heading
            // for files that use unfenced front-matter conventions.
            if (!frontMatterClosed && !line.StartsWith("#", StringComparison.Ordinal))
            {
                if (IsIntentStateClarified(line))
                {
                    return true;
                }
            }

            // Stop scanning once body content (headings, bullets) begins.
            if (line.StartsWith("# ", StringComparison.Ordinal)
                || line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }
        }

        return false;
    }

    private static bool IsIntentStateClarified(string line)
    {
        // Match `intent_state: clarified` (with optional surrounding whitespace).
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("intent_state:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = trimmed["intent_state:".Length..].Trim();
        return string.Equals(value, "clarified", StringComparison.OrdinalIgnoreCase);
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
