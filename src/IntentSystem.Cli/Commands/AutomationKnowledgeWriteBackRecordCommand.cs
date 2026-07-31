using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G564: <c>intent-cli automation knowledge-writeback-record --execution-unit
/// &lt;u&gt; --commit &lt;sha&gt; [--target &lt;path&gt;]... [--note &lt;text&gt;]
/// [--dry-run|--write] [--format json|markdown]</c> — records that the
/// write-backs a packet DECLARED were performed, with the host commit as
/// evidence.
///
/// This command records; it never writes intent content and never mutates the
/// intent tree (G300). The write-back is design's host-side act — this is the
/// statement that it happened, which is what makes the absence of one
/// detectable (<see cref="AutomationStalledWorkCommand.KindKnowledgeWritebackPending"/>).
///
/// Fail-closed, by design:
/// <list type="bullet">
///   <item>an execution unit with no packet is UNKNOWN — recording against it
///         would create evidence for an obligation nobody declared;</item>
///   <item>evidence that is not a commit-shaped SHA is malformed — "recorded"
///         must mean a reader can go look at the commit;</item>
///   <item>a second record with DIFFERENT evidence is refused, never
///         overwritten: replacing evidence silently is how an audit trail
///         stops being one. Re-recording the SAME commit is a no-op success,
///         so the command is safe to re-run in a retried closeout wake.</item>
/// </list>
/// </summary>
internal static class AutomationKnowledgeWriteBackRecordCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const int MinimumCommitLength = 7;
    private const int MaximumCommitLength = 40;

    /// <summary>Test seam: deterministic <c>recorded_at</c>.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-commit-sha> "
        + "[--target <path>]... [--note <text>] [--dry-run|--write] [--format json|markdown]";

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

        if (!TryParseArguments(args, out var executionUnit, out var commit, out var targets, out var note, out var write, out var format, out var parseError))
        {
            writer.WriteLine(parseError);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var packetPath = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit!, "packet.yaml");
        if (!File.Exists(packetPath))
        {
            return Fail(
                writer,
                format,
                executionUnit!,
                $"unknown execution unit '{executionUnit}': no packet at `{packetPath}`. A write-back record is "
                + "evidence for a DECLARED obligation, so it is never recorded against a unit this host cannot "
                + "resolve. Check the unit id, or run this from the host repo root that owns `.intent-cli/`.");
        }

        KnowledgeWriteBackDeclaration declaration;
        try
        {
            declaration = KnowledgeWriteBackDeclaration.Read(File.ReadAllText(packetPath));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return Fail(
                writer,
                format,
                executionUnit!,
                $"packet `{packetPath}` could not be read for its knowledge write-back declaration: {exception.Message}");
        }

        var recordPath = KnowledgeWriteBackRecord.ResolveFullPath(context.RepoRoot, executionUnit!);
        KnowledgeWriteBackRecord? existing = null;
        if (File.Exists(recordPath))
        {
            try
            {
                existing = KnowledgeWriteBackRecord.Deserialize(File.ReadAllText(recordPath));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return Fail(
                    writer,
                    format,
                    executionUnit!,
                    $"an existing record at `{recordPath}` could not be read: {exception.Message}. Refusing to "
                    + "overwrite unreadable evidence — repair or remove the artifact deliberately, then re-run.");
            }
        }

        if (existing is not null
            && !string.Equals(existing.HostCommit, commit, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(
                writer,
                format,
                executionUnit!,
                $"`{executionUnit}` already carries a write-back record for commit `{existing.HostCommit}` "
                + $"(recorded {existing.RecordedAt:O}); refusing to replace it with `{commit}`. Evidence is "
                + "append-only in spirit: record a LATER write-back as its own unit's record, or remove the "
                + "existing artifact deliberately if it was wrong.");
        }

        var warnings = new List<string>();
        if (!declaration.IsRequired)
        {
            warnings.Add(
                $"`{executionUnit}` declared no required knowledge write-back (`knowledge_updates.*.required` and "
                + "`closeout_learning.write_back_required` are all false or absent), so nothing was pending for it. "
                + "The record is still written — recording a write-back nobody demanded is harmless — but if the "
                + "tree genuinely owed something here, the packet's declaration was dishonest and that is the "
                + "defect to fix.");
        }

        var alreadyRecorded = existing is not null;
        var record = existing ?? new KnowledgeWriteBackRecord
        {
            ArtifactKind = KnowledgeWriteBackRecord.ArtifactKindValue,
            ExecutionUnit = executionUnit!,
            HostCommit = commit!,
            RecordedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            Targets = targets,
            Note = note,
        };

        var applied = false;
        if (write && !alreadyRecorded)
        {
            var directory = Path.GetDirectoryName(recordPath)
                ?? throw new InvalidOperationException("Knowledge write-back record path did not contain a directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(recordPath, KnowledgeWriteBackRecord.Serialize(record));
            applied = true;
        }

        var result = new KnowledgeWriteBackRecordResult
        {
            ExecutionUnit = executionUnit!,
            Mode = write ? "write" : "dry-run",
            Applied = applied,
            AlreadyRecorded = alreadyRecorded,
            HostCommit = record.HostCommit,
            RecordedAt = record.RecordedAt,
            RecordPath = KnowledgeWriteBackRecord.ResolveRelativePath(executionUnit!),
            Targets = record.Targets,
            DeclaredTargets = declaration.DeclaredTargets,
            DeclaredFacets = declaration.RequiredFacets,
            DeclarationRequired = declaration.IsRequired,
            Note = record.Note,
            Warnings = warnings,
            Error = null,
        };

        Emit(writer, format, result);
        return 0;
    }

    private static int Fail(TextWriter writer, string format, string executionUnit, string error)
    {
        var result = new KnowledgeWriteBackRecordResult
        {
            ExecutionUnit = executionUnit,
            Mode = "refused",
            Applied = false,
            AlreadyRecorded = false,
            HostCommit = null,
            RecordedAt = null,
            RecordPath = KnowledgeWriteBackRecord.ResolveRelativePath(executionUnit),
            Targets = Array.Empty<string>(),
            DeclaredTargets = Array.Empty<string>(),
            DeclaredFacets = Array.Empty<string>(),
            DeclarationRequired = false,
            Note = null,
            Warnings = Array.Empty<string>(),
            Error = error,
        };

        Emit(writer, format, result);
        return 1;
    }

    private static void Emit(TextWriter writer, string format, KnowledgeWriteBackRecordResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        writer.WriteLine($"# automation knowledge-writeback-record — `{result.ExecutionUnit}`");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- applied: {(result.Applied ? "true" : "false")}");
        writer.WriteLine($"- already_recorded: {(result.AlreadyRecorded ? "true" : "false")}");
        writer.WriteLine($"- record_path: `{result.RecordPath}`");
        if (!string.IsNullOrWhiteSpace(result.HostCommit))
        {
            writer.WriteLine($"- host_commit: `{result.HostCommit}`");
        }
        if (result.Targets.Count > 0)
        {
            writer.WriteLine($"- targets: {string.Join(", ", result.Targets)}");
        }
        if (result.DeclaredTargets.Count > 0)
        {
            writer.WriteLine($"- declared_targets: {string.Join(", ", result.DeclaredTargets)}");
        }
        if (result.DeclaredFacets.Count > 0)
        {
            writer.WriteLine($"- declared_facets: {string.Join(", ", result.DeclaredFacets)}");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine();
            writer.WriteLine($"## Error");
            writer.WriteLine($"- {result.Error}");
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? commit,
        out IReadOnlyList<string> targets,
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

        var collectedTargets = new List<string>();
        targets = collectedTargets;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--execution-unit":
                case "--unit":
                    if (index + 1 >= args.Length)
                    {
                        error = $"{argument} requires a value.";
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

                case "--target":
                    if (index + 1 >= args.Length)
                    {
                        error = "--target requires a value.";
                        return false;
                    }
                    collectedTargets.Add(args[++index]);
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
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(commit))
        {
            error =
                "--commit is required: a write-back record without host-commit evidence is not evidence. Use the "
                + "commit the write-back landed in (`git rev-parse HEAD` in the host repo).";
            return false;
        }

        if (!IsCommitShaped(commit!))
        {
            error =
                $"--commit '{commit}' is not a commit SHA ({MinimumCommitLength}-{MaximumCommitLength} hexadecimal "
                + "characters). Malformed evidence is refused rather than recorded — a reader must be able to go "
                + "look at the commit.";
            return false;
        }

        // Normalize so `--commit ABC123…` and `--commit abc123…` are the same
        // evidence for the idempotency check as well as on disk.
        commit = commit!.ToLowerInvariant();
        return true;
    }

    private static bool IsCommitShaped(string value) =>
        value.Length >= MinimumCommitLength
        && value.Length <= MaximumCommitLength
        && value.All(Uri.IsHexDigit);

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation knowledge-writeback-record");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine(IntentTreeCoEvolutionDuty.CloseoutCheck);
        writer.WriteLine();
        writer.WriteLine("Records that a packet-declared knowledge write-back was performed, with the host commit as evidence.");
        writer.WriteLine("  --dry-run (default) plans only; --write persists `.intent-cli/knowledge-writebacks/<unit>/record.json`.");
        writer.WriteLine("  Idempotent: re-recording the SAME commit is a no-op success; a DIFFERENT commit is refused, never overwritten.");
        writer.WriteLine("  Fail-closed: an unknown execution unit (no packet) and non-SHA evidence are both refused.");
        writer.WriteLine("  Never writes intent content — the tree is written by design; this command only records that it was.");
    }
}

internal sealed record KnowledgeWriteBackRecordResult
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

    [JsonPropertyName("targets")]
    public required IReadOnlyList<string> Targets { get; init; }

    [JsonPropertyName("declared_targets")]
    public required IReadOnlyList<string> DeclaredTargets { get; init; }

    [JsonPropertyName("declared_facets")]
    public required IReadOnlyList<string> DeclaredFacets { get; init; }

    [JsonPropertyName("declaration_required")]
    public required bool DeclarationRequired { get; init; }

    [JsonPropertyName("note")]
    public required string? Note { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("error")]
    public required string? Error { get; init; }
}
