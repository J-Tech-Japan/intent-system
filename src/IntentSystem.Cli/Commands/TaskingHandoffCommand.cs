using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G190: <c>intent-cli tasking handoff</c>. A LOCAL handoff command that
/// packages the existing G179 status brief, G180 context collect, and G185
/// next-slice classify signals into ONE deterministic artifact for an outer
/// tasking thread. The command performs no GitHub network calls, applies no
/// labels, launches no provider processes, and does NOT touch
/// <c>.intent-cli/queue-state.json</c> or <c>.intent-cli/runs.jsonl</c>.
///
/// This command is a pure composer. It does NOT replace
/// <c>status brief</c>, <c>context collect</c>, <c>next-slice classify</c>,
/// <c>issue plan-candidate</c>, <c>issue prepare</c>, or
/// <c>issue publish-reviewed</c>; it composes their analyzers in-process. The
/// reviewed publish boundary remains owned by issue prepare /
/// publish-reviewed.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, and no provider launcher.
/// The associated tests validate the no-provider-launch invariant via the
/// <see cref="NestedProviderLauncher"/> sentinel and a source-scan assertion.
/// </summary>
internal static class TaskingHandoffCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions ArtifactSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G189 <c>IssuePlanCandidateCommand.TimestampFactory</c>.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Test seam mirroring G187 <c>SafetyNestedProviderHandoffCommand.NestedProviderLauncher</c>.
    /// G190 must NEVER invoke this delegate. Tests register a sentinel that
    /// flips a flag if invoked; the handoff path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var domainOverride, out var outPath, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedOutPath = Path.GetFullPath(outPath);
        var artifactDirectory = Path.GetDirectoryName(resolvedOutPath);
        if (!string.IsNullOrEmpty(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
        }

        // Reuse IssuePrepareCommand.FormatUtcTimestamp for ISO8601 UTC formatting.
        var generatedAt = IssuePrepareCommand.FormatUtcTimestamp(TimestampFactory());

        var packet = TaskingHandoffAnalyzer.Build(
            context,
            domainOverride,
            generatedAt,
            resolvedOutPath);

        var serialized = JsonSerializer.Serialize(packet, ArtifactSerializerOptions);
        File.WriteAllText(resolvedOutPath, serialized);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(serialized);
        }
        else
        {
            WriteTextSummary(writer, packet);
        }

        return 0;
    }

    private static void WriteTextSummary(TextWriter writer, TaskingHandoffPacket packet)
    {
        writer.WriteLine(packet.SummaryLine);
        writer.WriteLine($"artifact_path: {packet.ArtifactPath}");
        writer.WriteLine($"domain: {packet.Domain}");
        writer.WriteLine($"is_published: {packet.IsPublished.ToString().ToLowerInvariant()}");
        writer.WriteLine(
            $"is_automation_visible: {packet.IsAutomationVisible.ToString().ToLowerInvariant()}");
        writer.WriteLine($"tasking_handoff_status: {packet.TaskingHandoffStatus}");
        writer.WriteLine(
            $"status_brief: {(packet.StatusBrief is null ? $"(error: {packet.StatusBriefError ?? "unavailable"})" : "embedded")}");
        writer.WriteLine(
            $"context_collect: {(packet.ContextCollect is null ? $"(error: {packet.ContextCollectError ?? "unavailable"})" : "embedded")}");
        writer.WriteLine(
            $"next_slice_classify: {(packet.NextSliceClassify is null ? $"(error: {packet.NextSliceClassifyError ?? "unavailable"})" : packet.NextSliceClassify.Classification)}");
        writer.WriteLine($"generated_at_utc: {packet.GeneratedAtUtc}");
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domainOverride,
        out string outPath,
        out string format,
        out string error)
    {
        domainOverride = null;
        outPath = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--out":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--out requires a non-empty value.";
                        return false;
                    }

                    outPath = args[index + 1];
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
                    error = $"Unknown argument '{argument}'. Supported: --domain, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            error = "--out is required.";
            return false;
        }

        return true;
    }
}
