using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G195: <c>intent-cli tasking handoff-bundle-inspect</c>. A LOCAL operator
/// read/inspect command that consumes a G194
/// <see cref="TaskingHandoffBundleArtifact"/> handoff bundle artifact (JSON) and
/// renders a deterministic concise terminal summary to STDOUT. The command
/// performs no GitHub network calls, applies no labels, launches no provider
/// processes, and does NOT touch <c>.intent-cli/queue-state.json</c> or
/// <c>.intent-cli/runs.jsonl</c>. It also does NOT write any artifact file —
/// inspect renders to STDOUT only.
///
/// Sits beside G190 <c>handoff</c>, G191 <c>task-packet</c>, G192
/// <c>task-packet-preview</c>, G193 <c>task-packet-checklist</c>, and G194
/// <c>handoff-bundle</c> under the same <c>tasking</c> group. It does NOT
/// replace any of those commands, nor does it touch <c>issue plan-candidate</c>,
/// <c>issue prepare</c>, or <c>issue publish-reviewed</c>.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, and no provider launcher.
/// The associated tests validate the no-provider-launch invariant via the
/// <see cref="NestedProviderLauncher"/> sentinel and a source-scan assertion.
///
/// Strict no-output: on missing input file, malformed JSON, or a bundle whose
/// status is not <c>"local_only"</c>, the command exits 1.
/// </summary>
internal static class TaskingHandoffBundleInspectCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G190/G191/G192/G193/G194 <c>NestedProviderLauncher</c>.
    /// G195 must NEVER invoke this delegate. Tests register a sentinel that
    /// flips a flag if invoked; the inspect path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromBundle, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedFromBundle = Path.GetFullPath(fromBundle);
        if (!File.Exists(resolvedFromBundle))
        {
            writer.WriteLine($"--from-bundle path does not exist: {fromBundle}");
            return 1;
        }

        byte[] bundleBytes;
        try
        {
            bundleBytes = File.ReadAllBytes(resolvedFromBundle);
        }
        catch (Exception exception)
        {
            writer.WriteLine($"Failed to read --from-bundle: {exception.Message}");
            return 1;
        }

        TaskingHandoffBundleArtifact? sourceBundle;
        try
        {
            sourceBundle = JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(bundleBytes);
        }
        catch (JsonException exception)
        {
            writer.WriteLine(
                $"Failed to parse --from-bundle as a G194 handoff bundle: {exception.Message}");
            return 1;
        }

        if (sourceBundle is null)
        {
            writer.WriteLine(
                "--from-bundle parsed to a null bundle; expected a G194 handoff bundle JSON object.");
            return 1;
        }

        if (!string.Equals(
                sourceBundle.BundleStatus,
                TaskingHandoffBundleConstants.LocalOnlyStatus,
                StringComparison.Ordinal))
        {
            writer.WriteLine(
                $"--from-bundle bundle_status must be '{TaskingHandoffBundleConstants.LocalOnlyStatus}' "
                + $"(got '{sourceBundle.BundleStatus}'). Refusing to inspect a non-local bundle.");
            return 1;
        }

        var summary = TaskingHandoffBundleInspectAnalyzer.Build(sourceBundle);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(summary, JsonOutputOptions));
        }
        else
        {
            WriteTextSummary(writer, summary);
        }

        return 0;
    }

    private static void WriteTextSummary(TextWriter writer, TaskingHandoffBundleInspectSummary summary)
    {
        writer.WriteLine(summary.SummaryLine);
        writer.WriteLine();
        writer.WriteLine($"Domain: {summary.Domain}");
        writer.WriteLine($"Bundle status: {summary.BundleStatus}");
        writer.WriteLine($"Is published: {summary.IsPublished}");
        writer.WriteLine($"Is automation visible: {summary.IsAutomationVisible}");
        writer.WriteLine($"Ready for handoff: {summary.ChecklistReadyForHandoff}");
        writer.WriteLine();
        writer.WriteLine("Source references:");
        writer.WriteLine(
            $"- Preview:    {summary.SourcePreviewPath}     (sha256: {summary.SourcePreviewSha256})");
        writer.WriteLine(
            $"- Checklist:  {summary.SourceChecklistPath}   (sha256: {summary.SourceChecklistSha256})");
        writer.WriteLine(
            $"- Task packet: {summary.SourceTaskPacketPath} (sha256: {summary.SourceTaskPacketSha256})");
        writer.WriteLine(
            $"- Handoff:    {summary.SourceHandoffPath}     (sha256: {summary.SourceHandoffSha256})");
        writer.WriteLine();
        writer.WriteLine("Checklist:");
        writer.WriteLine(
            $"- Passed ({summary.PassedCheckCount}): {FormatCheckIds(summary.PassedCheckIds)}");
        writer.WriteLine(
            $"- Failed ({summary.FailedCheckCount}): {FormatCheckIds(summary.FailedCheckIds)}");
        writer.WriteLine();
        writer.WriteLine($"Recommended worker action: {summary.RecommendedWorkerAction}");
    }

    private static string FormatCheckIds(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", ids);
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromBundle,
        out string format,
        out string error)
    {
        fromBundle = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-bundle":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-bundle requires a non-empty value.";
                        return false;
                    }

                    fromBundle = args[index + 1];
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
                        $"Unknown argument '{argument}'. Supported: --from-bundle, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromBundle))
        {
            error = "--from-bundle is required.";
            return false;
        }

        return true;
    }
}
