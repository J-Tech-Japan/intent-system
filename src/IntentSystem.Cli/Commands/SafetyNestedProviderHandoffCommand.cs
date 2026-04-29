using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G187: <c>intent-cli safety nested-provider-handoff</c>. First accepted
/// behavior of the nested-provider safety guard. Produces a deterministic
/// artifact-only handoff JSON file describing the requested nested-provider
/// action and emits a human-facing summary stating that nested provider
/// execution was NOT started. The command itself never spawns a process and
/// never makes a GitHub network call. The artifact's
/// <c>nested_provider_launched</c> field is always literal <c>false</c> for
/// this slice.
/// </summary>
internal static class SafetyNestedProviderHandoffCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions ArtifactSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam. Always remains uncalled for this slice; tests assert the
    /// invariant by registering a sentinel that flips a flag if invoked.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(
                args,
                out var executionUnit,
                out var intendedRole,
                out var intendedAction,
                out var targetRepo,
                out var targetPath,
                out var packetPath,
                out var reviewContextPath,
                out var requestedCommand,
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

        var summaryLine =
            $"{SafetyNestedProviderHandoffArtifact.NotStartedMessagePrefix}; artifact written to {resolvedOutPath}.";

        var artifact = new SafetyNestedProviderHandoffArtifact
        {
            ExecutionUnit = executionUnit,
            IntendedRole = intendedRole,
            IntendedAction = intendedAction,
            TargetRepo = targetRepo,
            TargetPath = targetPath,
            PacketPath = string.IsNullOrEmpty(packetPath) ? null : packetPath,
            ReviewContextPath = string.IsNullOrEmpty(reviewContextPath) ? null : reviewContextPath,
            RequestedCommand = requestedCommand,
            GeneratedAtUtc = FormatUtcTimestamp(TimestampFactory()),
            NestedProviderLaunched = false,
            ArtifactPath = resolvedOutPath,
            SummaryLine = summaryLine
        };

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

    private static void WriteTextSummary(TextWriter writer, SafetyNestedProviderHandoffArtifact artifact)
    {
        writer.WriteLine(
            $"{SafetyNestedProviderHandoffArtifact.NotStartedMessagePrefix}. Artifact written to {artifact.ArtifactPath}.");
        writer.WriteLine($"execution_unit: {artifact.ExecutionUnit}");
        writer.WriteLine($"intended_role: {artifact.IntendedRole}");
        writer.WriteLine($"intended_action: {artifact.IntendedAction}");
        writer.WriteLine($"target_repo: {artifact.TargetRepo}");
        writer.WriteLine($"target_path: {artifact.TargetPath}");
        writer.WriteLine($"packet_path: {artifact.PacketPath ?? "(none)"}");
        writer.WriteLine($"review_context_path: {artifact.ReviewContextPath ?? "(none)"}");
        writer.WriteLine($"requested_command: {artifact.RequestedCommand}");
        writer.WriteLine($"generated_at_utc: {artifact.GeneratedAtUtc}");
        writer.WriteLine(
            $"nested_provider_launched: {artifact.NestedProviderLaunched.ToString().ToLowerInvariant()}");
    }

    private static bool TryParseArguments(
        string[] args,
        out string executionUnit,
        out string intendedRole,
        out string intendedAction,
        out string targetRepo,
        out string targetPath,
        out string packetPath,
        out string reviewContextPath,
        out string requestedCommand,
        out string outPath,
        out string format,
        out string error)
    {
        executionUnit = string.Empty;
        intendedRole = string.Empty;
        intendedAction = string.Empty;
        targetRepo = string.Empty;
        targetPath = string.Empty;
        packetPath = string.Empty;
        reviewContextPath = string.Empty;
        requestedCommand = string.Empty;
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

                case "--intended-role":
                    if (!TryReadValue(args, ref index, "--intended-role", out intendedRole, out error))
                    {
                        return false;
                    }

                    break;

                case "--intended-action":
                    if (!TryReadValue(args, ref index, "--intended-action", out intendedAction, out error))
                    {
                        return false;
                    }

                    break;

                case "--target-repo":
                    if (!TryReadValue(args, ref index, "--target-repo", out targetRepo, out error))
                    {
                        return false;
                    }

                    break;

                case "--target-path":
                    if (!TryReadValue(args, ref index, "--target-path", out targetPath, out error))
                    {
                        return false;
                    }

                    break;

                case "--packet-path":
                    if (!TryReadOptionalValue(args, ref index, "--packet-path", out packetPath, out error))
                    {
                        return false;
                    }

                    break;

                case "--review-context-path":
                    if (!TryReadOptionalValue(args, ref index, "--review-context-path", out reviewContextPath, out error))
                    {
                        return false;
                    }

                    break;

                case "--requested-command":
                    if (!TryReadValue(args, ref index, "--requested-command", out requestedCommand, out error))
                    {
                        return false;
                    }

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
                    error = $"Unknown argument '{argument}'. Supported: --execution-unit, --intended-role, --intended-action, --target-repo, --target-path, --packet-path, --review-context-path, --requested-command, --out, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(intendedRole))
        {
            error = "--intended-role is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(intendedAction))
        {
            error = "--intended-action is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetRepo))
        {
            error = "--target-repo is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            error = "--target-path is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestedCommand))
        {
            error = "--requested-command is required.";
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

    private static bool TryReadOptionalValue(string[] args, ref int index, string flag, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;
        if (index + 1 >= args.Length)
        {
            error = $"{flag} requires a value.";
            return false;
        }

        // Optional flags allow empty string to mean "treat as not provided".
        value = args[index + 1];
        index++;
        return true;
    }

    internal static string FormatUtcTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
