using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G645: records that the guide routes a packet declared were updated. This
/// command records evidence only; it never edits guide prose or the intent
/// tree. It is shaped like the G564 write-back recorder so a closeout wake can
/// perform both checks at the same cadence.
/// </summary>
internal static class AutomationGuideReachabilityRecordCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-commit-sha> "
        + "[--note <text>] [--dry-run|--write] [--format json|markdown]";

    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var executionUnit, out var commit, out var note, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        string packetPath;
        string recordPath;
        try
        {
            packetPath = GuideReachabilityRecord.ResolvePacketPath(context.RepoRoot, executionUnit!);
            recordPath = GuideReachabilityRecord.ResolveFullPath(context.RepoRoot, executionUnit!);
        }
        catch (InvalidOperationException exception)
        {
            return Fail(writer, format, executionUnit ?? string.Empty, exception.Message);
        }

        if (!File.Exists(packetPath))
        {
            return Fail(
                writer,
                format,
                executionUnit!,
                $"unknown execution unit '{executionUnit}': no packet at '{packetPath}'. A reachability record is "
                + "evidence for a declared guide route, so it is never recorded for a unit this host cannot resolve.");
        }

        GuideReachabilityDeclaration declaration;
        try
        {
            declaration = GuideReachabilityDeclaration.Read(File.ReadAllText(packetPath));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return Fail(
                writer,
                format,
                executionUnit!,
                $"packet '{packetPath}' could not be read for its guide-reachability declaration: {exception.Message}");
        }

        if (!declaration.IsDeclared)
        {
            return Fail(
                writer,
                format,
                executionUnit!,
                $"packet '{packetPath}' has no guide_reachability declaration. Absence is not an explicit "
                + "no-surface decision; declare routes or set no_role_facing_surface: true before recording.");
        }

        var guideSurfaces = declaration.Routes.Select(route => route.GuideSurface).Distinct(StringComparer.Ordinal).ToArray();
        var roles = declaration.Routes.Select(route => route.Role).Distinct(StringComparer.Ordinal).ToArray();

        if (declaration.NoRoleFacingSurface)
        {
            var noSurface = new GuideReachabilityRecordResult
            {
                ExecutionUnit = executionUnit!,
                Mode = write ? "write" : "dry-run",
                Applied = false,
                AlreadyRecorded = false,
                HostCommit = commit,
                RecordedAt = null,
                RecordPath = GuideReachabilityRecord.ResolveRelativePath(executionUnit!),
                GuideSurfaces = Array.Empty<string>(),
                Roles = Array.Empty<string>(),
                DeclarationPresent = true,
                NoRoleFacingSurface = true,
                Warning = "explicit no_role_facing_surface declaration: no reachability debt exists and no record is required.",
                Error = null,
            };
            Emit(writer, format, noSurface);
            return 0;
        }

        GuideReachabilityRecord? existing = null;
        if (File.Exists(recordPath))
        {
            try
            {
                existing = GuideReachabilityRecord.Deserialize(File.ReadAllText(recordPath), executionUnit!);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return Fail(
                    writer,
                    format,
                    executionUnit!,
                    $"an existing record at '{recordPath}' could not be read: {exception.Message}. Refusing to overwrite unreadable evidence.");
            }
        }

        if (existing is not null
            && !string.Equals(existing.HostCommit, commit, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(
                writer,
                format,
                executionUnit!,
                $"'{executionUnit}' already carries a reachability record for commit '{existing.HostCommit}' "
                + $"(recorded {existing.RecordedAt:O}); refusing to replace it with '{commit}'.");
        }

        var alreadyRecorded = existing is not null;
        var record = existing ?? new GuideReachabilityRecord
        {
            ArtifactKind = GuideReachabilityRecord.ArtifactKindValue,
            ExecutionUnit = executionUnit!,
            HostCommit = commit!,
            RecordedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            GuideSurfaces = guideSurfaces,
            Roles = roles,
            Note = note,
        };

        var applied = false;
        if (write && !alreadyRecorded)
        {
            var directory = Path.GetDirectoryName(recordPath)
                ?? throw new InvalidOperationException("Guide reachability record path did not contain a directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(recordPath, GuideReachabilityRecord.Serialize(record));
            applied = true;
        }

        Emit(writer, format, new GuideReachabilityRecordResult
        {
            ExecutionUnit = executionUnit!,
            Mode = write ? "write" : "dry-run",
            Applied = applied,
            AlreadyRecorded = alreadyRecorded,
            HostCommit = record.HostCommit,
            RecordedAt = record.RecordedAt,
            RecordPath = GuideReachabilityRecord.ResolveRelativePath(executionUnit!),
            GuideSurfaces = guideSurfaces,
            Roles = roles,
            DeclarationPresent = true,
            NoRoleFacingSurface = false,
            Warning = null,
            Error = null,
        });
        return 0;
    }

    private static int Fail(TextWriter writer, string format, string executionUnit, string error)
    {
        Emit(writer, format, new GuideReachabilityRecordResult
        {
            ExecutionUnit = executionUnit,
            Mode = "refused",
            Applied = false,
            AlreadyRecorded = false,
            HostCommit = null,
            RecordedAt = null,
            RecordPath = string.IsNullOrWhiteSpace(executionUnit)
                ? $"{GuideReachabilityRecord.RecordRootRelativePath}/<unit>/record.json"
                : GuideReachabilityRecord.ResolveRelativePath(executionUnit),
            GuideSurfaces = Array.Empty<string>(),
            Roles = Array.Empty<string>(),
            DeclarationPresent = false,
            NoRoleFacingSurface = false,
            Warning = null,
            Error = error,
        });
        return 1;
    }

    private static void Emit(TextWriter writer, string format, GuideReachabilityRecordResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        writer.WriteLine($"# automation guide-reachability-record — '{result.ExecutionUnit}'");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- applied: {(result.Applied ? "true" : "false")}");
        writer.WriteLine($"- already_recorded: {(result.AlreadyRecorded ? "true" : "false")}");
        writer.WriteLine($"- declaration_present: {(result.DeclarationPresent ? "true" : "false")}");
        writer.WriteLine($"- no_role_facing_surface: {(result.NoRoleFacingSurface ? "true" : "false")}");
        writer.WriteLine($"- record_path: '{result.RecordPath}'");
        if (!string.IsNullOrWhiteSpace(result.HostCommit))
        {
            writer.WriteLine($"- host_commit: '{result.HostCommit}'");
        }
        if (result.GuideSurfaces.Count > 0)
        {
            writer.WriteLine($"- guide_surfaces: {string.Join(", ", result.GuideSurfaces)}");
        }
        if (result.Roles.Count > 0)
        {
            writer.WriteLine($"- roles: {string.Join(", ", result.Roles)}");
        }
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine();
            writer.WriteLine("## Error");
            writer.WriteLine($"- {result.Error}");
        }
        if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            writer.WriteLine();
            writer.WriteLine("## Warning");
            writer.WriteLine($"- {result.Warning}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? commit,
        out string? note,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = null;
        commit = null;
        note = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--execution-unit":
                case "--unit":
                    if (index + 1 >= args.Length)
                    {
                        error = $"{args[index]} requires a value.";
                        return false;
                    }
                    executionUnit = args[++index];
                    break;
                case "--commit":
                    if (index + 1 >= args.Length)
                    {
                        error = "--commit requires a value.";
                        return false;
                    }
                    commit = args[++index];
                    break;
                case "--note":
                    if (index + 1 >= args.Length)
                    {
                        error = "--note requires a value.";
                        return false;
                    }
                    note = args[++index];
                    break;
                case "--write":
                    write = true;
                    break;
                case "--dry-run":
                    write = false;
                    break;
                case "--format":
                    if (index + 1 >= args.Length)
                    {
                        error = "--format requires a value.";
                        return false;
                    }
                    format = args[++index];
                    if (!string.Equals(format, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"Unsupported --format '{format}'. Use json or markdown.";
                        return false;
                    }
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var unitError))
        {
            error = $"--execution-unit is invalid: {unitError}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(commit))
        {
            error = "--commit is required: a reachability record without host-commit evidence is not evidence.";
            return false;
        }

        if (!KnowledgeWriteBackRecord.IsCommitShaped(commit))
        {
            error = $"--commit '{commit}' is not a commit SHA ({KnowledgeWriteBackRecord.MinimumCommitLength}-"
                + $"{KnowledgeWriteBackRecord.MaximumCommitLength} hexadecimal characters).";
            return false;
        }

        commit = commit!.ToLowerInvariant();
        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation guide-reachability-record");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine(GuideReachabilityDuty.Standard);
        writer.WriteLine(GuideReachabilityDuty.CloseoutCheck);
        writer.WriteLine();
        writer.WriteLine("Records route evidence only; it never writes guide content and never blocks merge or closeout.");
        writer.WriteLine("  --dry-run (default) plans only; --write persists '.intent-cli/guide-reachability/<unit>/record.json'.");
        writer.WriteLine("  An explicit no_role_facing_surface declaration is a successful no-op; an absent declaration is refused.");
    }
}

internal sealed record GuideReachabilityRecordResult
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("already_recorded")]
    public required bool AlreadyRecorded { get; init; }

    [JsonPropertyName("host_commit")]
    public required string? HostCommit { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset? RecordedAt { get; init; }

    [JsonPropertyName("record_path")]
    public required string RecordPath { get; init; }

    [JsonPropertyName("guide_surfaces")]
    public required IReadOnlyList<string> GuideSurfaces { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<string> Roles { get; init; }

    [JsonPropertyName("declaration_present")]
    public required bool DeclarationPresent { get; init; }

    [JsonPropertyName("no_role_facing_surface")]
    public required bool NoRoleFacingSurface { get; init; }

    [JsonPropertyName("warning")]
    public required string? Warning { get; init; }

    [JsonPropertyName("error")]
    public required string? Error { get; init; }
}
