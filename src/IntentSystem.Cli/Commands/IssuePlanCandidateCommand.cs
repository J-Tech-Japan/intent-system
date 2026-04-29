using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G189: <c>intent-cli issue plan-candidate</c>. A LOCAL planner that proposes
/// candidate child issue specs without publishing. The command performs no
/// GitHub network calls, applies no labels, and does NOT touch
/// <c>.intent-cli/queue-state.json</c> or <c>.intent-cli/runs.jsonl</c>.
///
/// The reviewed publish boundary remains owned by <c>issue prepare</c> /
/// <c>issue publish-reviewed</c>; this command name was chosen to avoid those
/// names. Output is explicitly marked unpublished and not automation-visible
/// (see <see cref="IssuePlanCandidateArtifact"/>).
///
/// Network-mutation invariance test note: this command's hot path contains no
/// process spawn, no shell-out to GitHub tooling, and no HTTP or issue-creator
/// abstractions. The associated tests validate the no-GitHub-network
/// invariant by asserting queue-state and runs.jsonl byte-equivalence
/// pre/post and (defensively) by source-scanning this file for any such
/// call. A dedicated injection seam was deliberately not introduced — the
/// command is purely artifact-write.
/// </summary>
internal static class IssuePlanCandidateCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions ArtifactSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring <c>SafetyNestedProviderHandoffCommand.TimestampFactory</c>.
    /// </summary>
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(
                args,
                out var executionUnit,
                out var title,
                out var contextPaths,
                out var packetPath,
                out var reviewContextPath,
                out var outPath,
                out var format,
                out var error))
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

        var artifact = IssuePlanCandidateAnalyzer.Build(
            repoRoot: context.RepoRoot,
            executionUnit: executionUnit,
            title: title,
            contextPaths: contextPaths,
            packetPath: packetPath,
            reviewContextPath: reviewContextPath,
            generatedAtUtc: generatedAt,
            resolvedArtifactPath: resolvedOutPath);

        var serialized = JsonSerializer.Serialize(artifact, ArtifactSerializerOptions);
        File.WriteAllText(resolvedOutPath, serialized);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(serialized);
        }
        else
        {
            WriteTextSummary(writer, artifact);
        }

        return 0;
    }

    private static void WriteTextSummary(TextWriter writer, IssuePlanCandidateArtifact artifact)
    {
        writer.WriteLine(artifact.SummaryLine);
        writer.WriteLine($"artifact_path: {artifact.ArtifactPath}");
        writer.WriteLine($"execution_unit: {artifact.ExecutionUnit}");
        writer.WriteLine($"title: {artifact.Title}");
        writer.WriteLine($"is_published: {artifact.IsPublished.ToString().ToLowerInvariant()}");
        writer.WriteLine(
            $"is_automation_visible: {artifact.IsAutomationVisible.ToString().ToLowerInvariant()}");
        writer.WriteLine($"candidate_spec_status: {artifact.CandidateSpecStatus}");
        writer.WriteLine($"context_paths: {artifact.ContextPaths.Count}");
        foreach (var reference in artifact.ContextPaths)
        {
            writer.WriteLine($"  - {reference.Path} (exists={reference.Exists.ToString().ToLowerInvariant()})");
        }

        writer.WriteLine($"packet_path: {(artifact.PacketPath is null ? "(none)" : artifact.PacketPath.Path)}");
        writer.WriteLine(
            $"review_context_path: {(artifact.ReviewContextPath is null ? "(none)" : artifact.ReviewContextPath.Path)}");
        writer.WriteLine($"generated_at_utc: {artifact.GeneratedAtUtc}");
    }

    private static bool TryParseArguments(
        string[] args,
        out string executionUnit,
        out string title,
        out IReadOnlyList<string> contextPaths,
        out string? packetPath,
        out string? reviewContextPath,
        out string outPath,
        out string format,
        out string error)
    {
        executionUnit = string.Empty;
        title = string.Empty;
        var contextPathsBuffer = new List<string>();
        contextPaths = contextPathsBuffer;
        packetPath = null;
        reviewContextPath = null;
        outPath = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--execution-unit":
                    if (!TryReadValue(args, ref index, "--execution-unit", out executionUnit, out error))
                    {
                        return false;
                    }

                    break;

                case "--title":
                    if (!TryReadValue(args, ref index, "--title", out title, out error))
                    {
                        return false;
                    }

                    break;

                case "--context-path":
                    if (!TryReadValue(args, ref index, "--context-path", out var contextPathValue, out error))
                    {
                        return false;
                    }

                    contextPathsBuffer.Add(contextPathValue);
                    break;

                case "--packet-path":
                    if (!TryReadValue(args, ref index, "--packet-path", out var packetPathValue, out error))
                    {
                        return false;
                    }

                    packetPath = packetPathValue;
                    break;

                case "--review-context-path":
                    if (!TryReadValue(args, ref index, "--review-context-path", out var reviewContextPathValue, out error))
                    {
                        return false;
                    }

                    reviewContextPath = reviewContextPathValue;
                    break;

                case "--out":
                    if (!TryReadValue(args, ref index, "--out", out outPath, out error))
                    {
                        return false;
                    }

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
                    error = $"Unknown argument '{argument}'. Supported: --execution-unit, --title, --context-path, --packet-path, --review-context-path, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            error = "--title is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            error = "--out is required.";
            return false;
        }

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string flag, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            error = $"{flag} requires a non-empty value.";
            return false;
        }

        value = args[index + 1];
        index++;
        return true;
    }
}
