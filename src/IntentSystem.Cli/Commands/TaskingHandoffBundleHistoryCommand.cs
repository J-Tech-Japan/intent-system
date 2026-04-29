using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G200: <c>intent-cli tasking handoff-bundle-history</c>. A LOCAL operator
/// list/scan command that enumerates top-level <c>*.json</c> files in a
/// directory and classifies each as a G194
/// <see cref="TaskingHandoffBundleArtifact"/> handoff bundle, an invalid
/// bundle, malformed JSON, or unreadable. The command performs no GitHub
/// network calls, applies no labels, launches no provider processes, and does
/// NOT touch <c>.intent-cli/queue-state.json</c> or
/// <c>.intent-cli/runs.jsonl</c>. It emits to STDOUT only — no artifact file
/// is written, ever.
///
/// Sits beside G190..G199 under the same <c>tasking</c> group. It does NOT
/// replace any of those commands, nor does it touch
/// <c>issue plan-candidate</c>, <c>issue prepare</c>, or
/// <c>issue publish-reviewed</c>.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, and no provider launcher.
/// The associated tests validate the no-provider-launch invariant via the
/// <see cref="NestedProviderLauncher"/> sentinel and a source-scan assertion.
///
/// Non-recursive: only top-level <c>*.json</c> files in the supplied directory
/// are scanned. Subdirectory contents are ignored. Sorted by file name
/// (ordinal) for deterministic output.
///
/// Exit conventions:
/// <list type="bullet">
/// <item><description>Missing/blank <c>--from-directory</c> → exit 1.</description></item>
/// <item><description>Path missing on disk → exit 1.</description></item>
/// <item><description>Path exists but is not a directory → exit 1.</description></item>
/// <item><description>Empty directory → exit 0 with empty entries.</description></item>
/// <item><description>Any successful scan (including malformed/invalid entries
/// reported in the result) → exit 0.</description></item>
/// </list>
/// </summary>
internal static class TaskingHandoffBundleHistoryCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G190..G199 <c>NestedProviderLauncher</c>.
    /// G200 must NEVER invoke this delegate. Tests register a sentinel that
    /// flips a flag if invoked; the history path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromDirectory, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedFromDirectory = Path.GetFullPath(fromDirectory);

        if (File.Exists(resolvedFromDirectory) && !Directory.Exists(resolvedFromDirectory))
        {
            writer.WriteLine(
                $"--from-directory path is not a directory: {fromDirectory}");
            return 1;
        }

        if (!Directory.Exists(resolvedFromDirectory))
        {
            writer.WriteLine(
                $"--from-directory path does not exist (not found): {fromDirectory}");
            return 1;
        }

        var jsonFiles = Directory
            .EnumerateFiles(resolvedFromDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .ToList();

        var inputs = new List<TaskingHandoffBundleHistoryAnalyzer.EntryInput>(jsonFiles.Count);
        foreach (var path in jsonFiles)
        {
            var fileName = Path.GetFileName(path);
            byte[]? bytes = null;
            string? readError = null;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                readError = exception.Message;
            }

            inputs.Add(new TaskingHandoffBundleHistoryAnalyzer.EntryInput
            {
                FileName = fileName,
                AbsolutePath = path,
                RawBytes = bytes,
                ReadError = readError
            });
        }

        var result = TaskingHandoffBundleHistoryAnalyzer.Build(fromDirectory, inputs);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOutputOptions));
        }
        else
        {
            WriteTextSummary(writer, result);
        }

        return 0;
    }

    private static void WriteTextSummary(TextWriter writer, TaskingHandoffBundleHistoryResult result)
    {
        writer.WriteLine(result.SummaryLine);

        if (result.EntryCount == 0)
        {
            writer.WriteLine($"no bundle entries found in {result.FromDirectory}");
            return;
        }

        foreach (var entry in result.Entries)
        {
            var domain = entry.Domain ?? "?";
            var status = entry.BundleStatus ?? "?";
            var ready = entry.ChecklistReadyForHandoff is null
                ? "?"
                : entry.ChecklistReadyForHandoff.Value.ToString();
            var errors = entry.Errors.Count;
            writer.WriteLine(
                $"[{entry.Classification}] {entry.FileName} — domain: {domain}, status: {status}, ready: {ready}, errors: {errors}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromDirectory,
        out string format,
        out string error)
    {
        fromDirectory = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-directory":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-directory requires a non-empty value.";
                        return false;
                    }

                    fromDirectory = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error =
                        $"Unknown argument '{argument}'. Supported: --from-directory, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromDirectory))
        {
            error = "--from-directory is required.";
            return false;
        }

        return true;
    }
}
