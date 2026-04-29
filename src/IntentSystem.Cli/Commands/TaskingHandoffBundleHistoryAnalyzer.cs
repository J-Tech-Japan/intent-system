using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G200 — pure logic that takes a directory enumeration result (a list of
/// candidate <c>*.json</c> file paths) and builds a deterministic
/// <see cref="TaskingHandoffBundleHistoryResult"/>. No directory enumeration
/// happens here; that concern is owned by
/// <see cref="TaskingHandoffBundleHistoryCommand"/>. No file write of any kind
/// happens here; the history command/analyzer pair is strictly read-only.
///
/// Classification rules:
/// <list type="bullet">
/// <item><description><c>valid_bundle</c>: deserializes as
/// <see cref="TaskingHandoffBundleArtifact"/> AND
/// <c>bundle_status == "local_only"</c> AND <c>is_published == false</c>
/// AND <c>is_automation_visible == false</c>.</description></item>
/// <item><description><c>invalid_bundle</c>: deserializes but one of those
/// three contract checks fails.</description></item>
/// <item><description><c>malformed</c>: not parseable as
/// <see cref="TaskingHandoffBundleArtifact"/>.</description></item>
/// <item><description><c>unreadable</c>: read I/O failure (caller passes the
/// pre-collected exception via <see cref="EntryInput"/>).</description></item>
/// </list>
/// </summary>
internal static class TaskingHandoffBundleHistoryAnalyzer
{
    /// <summary>
    /// Stable header phrase emitted at the top of both text and JSON
    /// outputs. Locked by tests so the wording cannot drift silently.
    /// </summary>
    public const string HeaderPhrase = "Tasking handoff bundle history";

    public static TaskingHandoffBundleHistoryResult Build(
        string fromDirectory,
        IReadOnlyList<EntryInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(fromDirectory);
        ArgumentNullException.ThrowIfNull(inputs);

        var sorted = inputs
            .OrderBy(input => input.FileName, StringComparer.Ordinal)
            .ToList();

        var entries = new List<HistoryEntry>(sorted.Count);
        var validCount = 0;
        var invalidCount = 0;
        var malformedCount = 0;
        var unreadableCount = 0;

        foreach (var input in sorted)
        {
            var entry = ClassifyEntry(input);
            entries.Add(entry);

            switch (entry.Classification)
            {
                case TaskingHandoffBundleHistoryConstants.Classifications.ValidBundle:
                    validCount++;
                    break;
                case TaskingHandoffBundleHistoryConstants.Classifications.InvalidBundle:
                    invalidCount++;
                    break;
                case TaskingHandoffBundleHistoryConstants.Classifications.Malformed:
                    malformedCount++;
                    break;
                case TaskingHandoffBundleHistoryConstants.Classifications.Unreadable:
                    unreadableCount++;
                    break;
            }
        }

        var entryCount = entries.Count;
        var summaryLine =
            $"{HeaderPhrase} for {fromDirectory} — "
            + $"{entryCount} {(entryCount == 1 ? "entry" : "entries")}, "
            + $"{validCount} valid, "
            + $"{invalidCount} invalid, "
            + $"{malformedCount} malformed, "
            + $"{unreadableCount} unreadable.";

        return new TaskingHandoffBundleHistoryResult
        {
            FromDirectory = fromDirectory,
            EntryCount = entryCount,
            ValidCount = validCount,
            InvalidCount = invalidCount,
            MalformedCount = malformedCount,
            UnreadableCount = unreadableCount,
            Entries = entries,
            SummaryLine = summaryLine
        };
    }

    private static HistoryEntry ClassifyEntry(EntryInput input)
    {
        if (input.ReadError is not null)
        {
            return new HistoryEntry
            {
                FileName = input.FileName,
                AbsolutePath = input.AbsolutePath,
                Classification = TaskingHandoffBundleHistoryConstants.Classifications.Unreadable,
                Domain = null,
                BundleStatus = null,
                IsPublished = null,
                IsAutomationVisible = null,
                SourceTaskPacketPath = null,
                SourcePreviewPath = null,
                SourceChecklistPath = null,
                SourceHandoffPath = null,
                ChecklistReadyForHandoff = null,
                Errors = new[] { $"unreadable: {input.ReadError}" }
            };
        }

        TaskingHandoffBundleArtifact? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(input.RawBytes!);
        }
        catch (JsonException exception)
        {
            return new HistoryEntry
            {
                FileName = input.FileName,
                AbsolutePath = input.AbsolutePath,
                Classification = TaskingHandoffBundleHistoryConstants.Classifications.Malformed,
                Domain = null,
                BundleStatus = null,
                IsPublished = null,
                IsAutomationVisible = null,
                SourceTaskPacketPath = null,
                SourcePreviewPath = null,
                SourceChecklistPath = null,
                SourceHandoffPath = null,
                ChecklistReadyForHandoff = null,
                Errors = new[] { $"parse failure: {exception.Message}" }
            };
        }

        if (bundle is null)
        {
            return new HistoryEntry
            {
                FileName = input.FileName,
                AbsolutePath = input.AbsolutePath,
                Classification = TaskingHandoffBundleHistoryConstants.Classifications.Malformed,
                Domain = null,
                BundleStatus = null,
                IsPublished = null,
                IsAutomationVisible = null,
                SourceTaskPacketPath = null,
                SourcePreviewPath = null,
                SourceChecklistPath = null,
                SourceHandoffPath = null,
                ChecklistReadyForHandoff = null,
                Errors = new[] { "parse failure: deserialized to null bundle." }
            };
        }

        var contractErrors = new List<string>();
        if (!string.Equals(
                bundle.BundleStatus,
                TaskingHandoffBundleConstants.LocalOnlyStatus,
                StringComparison.Ordinal))
        {
            contractErrors.Add(
                $"bundle_status must be '{TaskingHandoffBundleConstants.LocalOnlyStatus}' "
                + $"(got '{bundle.BundleStatus}').");
        }

        if (bundle.IsPublished)
        {
            contractErrors.Add("is_published must be false (got true).");
        }

        if (bundle.IsAutomationVisible)
        {
            contractErrors.Add("is_automation_visible must be false (got true).");
        }

        if (contractErrors.Count > 0)
        {
            return new HistoryEntry
            {
                FileName = input.FileName,
                AbsolutePath = input.AbsolutePath,
                Classification = TaskingHandoffBundleHistoryConstants.Classifications.InvalidBundle,
                Domain = null,
                BundleStatus = null,
                IsPublished = null,
                IsAutomationVisible = null,
                SourceTaskPacketPath = null,
                SourcePreviewPath = null,
                SourceChecklistPath = null,
                SourceHandoffPath = null,
                ChecklistReadyForHandoff = null,
                Errors = contractErrors
            };
        }

        return new HistoryEntry
        {
            FileName = input.FileName,
            AbsolutePath = input.AbsolutePath,
            Classification = TaskingHandoffBundleHistoryConstants.Classifications.ValidBundle,
            Domain = bundle.Domain,
            BundleStatus = bundle.BundleStatus,
            IsPublished = bundle.IsPublished,
            IsAutomationVisible = bundle.IsAutomationVisible,
            SourceTaskPacketPath = bundle.SourceTaskPacketPath,
            SourcePreviewPath = bundle.SourcePreviewPath,
            SourceChecklistPath = bundle.SourceChecklistPath,
            SourceHandoffPath = bundle.SourceHandoffPath,
            ChecklistReadyForHandoff = bundle.ChecklistReadyForHandoff,
            Errors = Array.Empty<string>()
        };
    }

    /// <summary>
    /// Per-file pre-read input passed from the command layer. The command does
    /// the read so the analyzer stays pure (no I/O).
    /// </summary>
    internal sealed record EntryInput
    {
        public required string FileName { get; init; }
        public required string AbsolutePath { get; init; }
        public byte[]? RawBytes { get; init; }
        public string? ReadError { get; init; }
    }
}
