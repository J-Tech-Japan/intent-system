namespace IntentSystem.Cli.Commands;

/// <summary>
/// Insertion helper for the owner-approved decision entry into the parent
/// clarification return path (G182). Writes only into the
/// <c>## Recently Resolved</c> section; preserves all other content verbatim.
/// </summary>
internal static class ClarifyRecordWriter
{
    private const string RecentlyResolvedHeading = "## Recently Resolved";

    /// <summary>
    /// Returns the updated <c>open.md</c> content with a new accepted-decision
    /// entry inserted at the top of the <c>## Recently Resolved</c> section.
    /// If the section is missing, it is appended at the end of the file.
    /// </summary>
    public static string InsertDecision(
        string existingContent,
        ClarifyRecordDecision decision,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var existing = existingContent ?? string.Empty;
        var entry = FormatEntry(decision, timestamp);

        var lines = existing.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var headingIndex = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (string.Equals(
                    lines[index].TrimEnd(),
                    RecentlyResolvedHeading,
                    StringComparison.OrdinalIgnoreCase))
            {
                headingIndex = index;
                break;
            }
        }

        if (headingIndex < 0)
        {
            // Append a new section at the end. Ensure a blank line separator.
            var builder = new System.Text.StringBuilder();
            builder.Append(existing);
            if (existing.Length > 0 && !existing.EndsWith('\n'))
            {
                builder.Append('\n');
            }

            if (existing.Length > 0)
            {
                builder.Append('\n');
            }

            builder.AppendLine(RecentlyResolvedHeading);
            builder.AppendLine();
            builder.AppendLine(entry);
            return builder.ToString();
        }

        // Insert after the heading and any trailing blank lines so the new
        // entry becomes the first list item under "## Recently Resolved".
        var insertAt = headingIndex + 1;
        while (insertAt < lines.Count && string.IsNullOrWhiteSpace(lines[insertAt]))
        {
            insertAt++;
        }

        // Preserve a single blank line after the heading.
        var inserted = new List<string>(lines.Count + 4);
        inserted.AddRange(lines.Take(headingIndex + 1));
        inserted.Add(string.Empty);
        inserted.AddRange(entry.Split('\n'));
        if (insertAt < lines.Count)
        {
            inserted.Add(string.Empty);
            inserted.AddRange(lines.Skip(insertAt));
        }

        return string.Join("\n", inserted);
    }

    /// <summary>
    /// Plain text representation of the entry the command would write — used by
    /// <c>--dry-run</c> and the post-write confirmation output.
    /// </summary>
    public static string FormatEntry(ClarifyRecordDecision decision, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var lines = new List<string>
        {
            $"- {timestamp:yyyy-MM-ddTHH:mm:ssZ} — {SingleLine(decision.Question)}",
            $"  - Decision: {SingleLine(decision.Decision)}"
        };

        if (!string.IsNullOrWhiteSpace(decision.Rationale))
        {
            lines.Add($"  - Rationale: {SingleLine(decision.Rationale!)}");
        }

        return string.Join("\n", lines);
    }

    private static string SingleLine(string value)
    {
        return value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();
    }
}
