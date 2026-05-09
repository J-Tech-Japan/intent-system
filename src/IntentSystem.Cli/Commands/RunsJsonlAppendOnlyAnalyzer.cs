using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G312: pure analyzer that classifies a delta between two
/// <c>.intent-cli/runs.jsonl</c> snapshots (HEAD blob vs.
/// working-tree blob) as <em>append-only</em>,
/// <em>needs-operator-review</em>, or <em>invalid</em>.
/// Append-only deltas keep all existing lines byte-identical and
/// add zero or more new well-formed <see cref="RunEvent"/> lines at
/// the end. Anything else (in-place edit, deletion, line reorder,
/// invalid JSON on a new line) requires operator review.
///
/// Pure data in / pure data out: no I/O, no <c>git</c> calls. The
/// caller is responsible for capturing the two blobs.
/// </summary>
internal static class RunsJsonlAppendOnlyAnalyzer
{
    public const string ClassificationAppendOnly = "append-only";
    public const string ClassificationNeedsOperatorReview = "needs-operator-review";
    public const string ClassificationInvalid = "invalid";

    public static RunsJsonlAppendOnlyResult Analyze(string headContent, string workingContent)
    {
        ArgumentNullException.ThrowIfNull(headContent);
        ArgumentNullException.ThrowIfNull(workingContent);

        var headLines = SplitNonEmptyLines(headContent);
        var workingLines = SplitNonEmptyLines(workingContent);

        if (workingLines.Count < headLines.Count)
        {
            return NeedsReview(
                $"runs.jsonl shrank from {headLines.Count} to {workingLines.Count} lines; refuse append-only auto-commit (rollback / deletion).",
                appendedEventCount: 0);
        }

        for (var index = 0; index < headLines.Count; index++)
        {
            if (!string.Equals(headLines[index], workingLines[index], StringComparison.Ordinal))
            {
                return NeedsReview(
                    $"runs.jsonl line {index + 1} was modified in place; refuse append-only auto-commit.",
                    appendedEventCount: 0);
            }
        }

        var appendedRaw = workingLines.Skip(headLines.Count).ToArray();
        if (appendedRaw.Length == 0)
        {
            // No HEAD-line changes AND no new lines → file is byte-identical
            // ignoring trailing-newline whitespace; nothing to auto-commit.
            return NeedsReview(
                "runs.jsonl is dirty but contains no appended events (likely whitespace-only change); refuse auto-commit.",
                appendedEventCount: 0);
        }

        // Every appended line must parse as a well-formed RunEvent.
        for (var index = 0; index < appendedRaw.Length; index++)
        {
            try
            {
                _ = RunLogSerializer.DeserializeLine(appendedRaw[index]);
            }
            catch (Exception exception) when (
                exception is JsonException
                or InvalidOperationException
                or ArgumentException)
            {
                return Invalid(
                    $"runs.jsonl appended line {headLines.Count + index + 1} is not a well-formed RunEvent: {exception.Message}");
            }
        }

        return new RunsJsonlAppendOnlyResult
        {
            Classification = ClassificationAppendOnly,
            Summary = $"runs.jsonl is append-only with {appendedRaw.Length} new event(s).",
            AppendedEventCount = appendedRaw.Length,
        };
    }

    private static IReadOnlyList<string> SplitNonEmptyLines(string content)
    {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }
        return normalized
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static RunsJsonlAppendOnlyResult Invalid(string summary) => new()
    {
        Classification = ClassificationInvalid,
        Summary = summary,
        AppendedEventCount = 0,
    };

    private static RunsJsonlAppendOnlyResult NeedsReview(string summary, int appendedEventCount) => new()
    {
        Classification = ClassificationNeedsOperatorReview,
        Summary = summary,
        AppendedEventCount = appendedEventCount,
    };
}

internal sealed record RunsJsonlAppendOnlyResult
{
    public required string Classification { get; init; }
    public required string Summary { get; init; }
    public required int AppendedEventCount { get; init; }
}
