using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G596: explicit command-only lifecycle for durable obligations that require
/// a human operator. A notification stream can announce one of these records,
/// but it can never create or transition one.
/// </summary>
internal static class OperatorAttentionCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    internal static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static int ExecuteOpen(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(OpenUsage);
            return 0;
        }
        if (!TryParseMutationArguments(args, OperatorAttentionOperation.Open, out var input, out var error))
        {
            return EmitArgumentError(writer, error, OpenUsage);
        }

        var read = OperatorAttentionStore.Read(context.RepoRoot);
        if (read.Status == OperatorAttentionReadStatus.CannotDetermine)
        {
            return EmitMutationFailure(writer, input!, read.Status, read.Error!);
        }

        var existingRecords = read.Document?.Records ?? Array.Empty<OperatorAttentionRecord>();
        if (existingRecords.Any(record => string.Equals(record.RecordId, input!.RecordId, StringComparison.Ordinal)))
        {
            return EmitMutationFailure(
                writer,
                input!,
                OperatorAttentionReadStatus.Readable,
                $"record '{input!.RecordId}' already exists; records are never overwritten or reopened in place.");
        }

        if (input!.SupersedesRecordId is { } supersededId)
        {
            var superseded = existingRecords.FirstOrDefault(record =>
                string.Equals(record.RecordId, supersededId, StringComparison.Ordinal));
            if (superseded is null)
            {
                return EmitMutationFailure(writer, input, OperatorAttentionReadStatus.Readable,
                    $"--supersedes names unknown record '{supersededId}'.");
            }
            if (!string.Equals(superseded.Status, OperatorAttentionStatus.Superseded, StringComparison.Ordinal))
            {
                return EmitMutationFailure(writer, input, OperatorAttentionReadStatus.Readable,
                    $"record '{supersededId}' is '{superseded.Status}', not superseded; reopening is a new record that may reference only a terminal superseded record.");
            }
            if (!string.Equals(superseded.Domain, input.Domain, StringComparison.Ordinal)
                || !string.Equals(superseded.Team, input.Team, StringComparison.Ordinal))
            {
                return EmitMutationFailure(writer, input, OperatorAttentionReadStatus.Readable,
                    $"record '{supersededId}' belongs to domain '{superseded.Domain}' / team '{superseded.Team}', not '{input.Domain}' / '{input.Team}'.");
            }
        }

        var now = Now();
        var record = new OperatorAttentionRecord
        {
            RecordId = input.RecordId!,
            Domain = input.Domain!,
            Team = input.Team!,
            Owner = input.Owner!,
            BlockingReference = input.BlockingReference!,
            ActionNeeded = input.ActionNeeded!,
            EstablishingEvidence = input.Evidence!,
            Status = OperatorAttentionStatus.Open,
            OpenedAt = now,
            ResolutionEvidence = null,
            SupersedesRecordId = input.SupersedesRecordId,
            Transitions =
            [
                new OperatorAttentionTransition
                {
                    FromStatus = null,
                    ToStatus = OperatorAttentionStatus.Open,
                    TransitionedAt = now,
                    Evidence = input.Evidence!,
                },
            ],
        };

        var document = OperatorAttentionStore.BuildUpdated(read.Document, [.. existingRecords, record], now);
        return ApplyAndEmit(context, writer, input, document, record);
    }

    public static int ExecuteResolve(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(ResolveUsage);
            return 0;
        }
        return ExecuteTerminalTransition(context, args, writer, OperatorAttentionOperation.Resolve);
    }

    public static int ExecuteSupersede(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(SupersedeUsage);
            return 0;
        }
        return ExecuteTerminalTransition(context, args, writer, OperatorAttentionOperation.Supersede);
    }

    public static int ExecuteQuery(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(QueryUsage);
            return 0;
        }
        if (!TryParseQueryArguments(args, out var domain, out var team, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(QueryUsage);
            return 1;
        }

        var now = Now();
        var read = OperatorAttentionStore.Read(context.RepoRoot);
        var matched = read.Document?.Records
            .Where(record => domain is null || string.Equals(record.Domain, domain, StringComparison.Ordinal))
            .Where(record => team is null || string.Equals(record.Team, team, StringComparison.Ordinal))
            .OrderBy(record => record.OpenedAt)
            .ThenBy(record => record.RecordId, StringComparer.Ordinal)
            .Select(record => OperatorAttentionQueryRecord.From(record, now))
            .ToArray()
            ?? Array.Empty<OperatorAttentionQueryRecord>();
        var open = matched.Where(record => string.Equals(record.Status, OperatorAttentionStatus.Open, StringComparison.Ordinal)).ToArray();

        var result = new OperatorAttentionQueryResult
        {
            Status = read.Status switch
            {
                OperatorAttentionReadStatus.Missing => OperatorAttentionQueryStatus.CheckNotCompleted,
                OperatorAttentionReadStatus.CannotDetermine => OperatorAttentionQueryStatus.CannotDetermine,
                _ when open.Length > 0 => OperatorAttentionQueryStatus.AttentionPending,
                _ when matched.Length == 0 => OperatorAttentionQueryStatus.CheckNotCompleted,
                _ => OperatorAttentionQueryStatus.NoAttentionPending,
            },
            Domain = domain,
            Team = team,
            CheckedAt = now,
            StorePath = OperatorAttentionStore.RelativePath,
            OpenCount = open.Length,
            OpenRecords = open,
            Records = matched,
            Error = read.Error,
        };

        EmitQuery(writer, format, result);
        return result.Status is OperatorAttentionQueryStatus.AttentionPending
            or OperatorAttentionQueryStatus.NoAttentionPending
            ? 0
            : 1;
    }

    private static int ExecuteTerminalTransition(
        CliContext context,
        string[] args,
        TextWriter writer,
        OperatorAttentionOperation operation)
    {
        var usage = operation == OperatorAttentionOperation.Resolve ? ResolveUsage : SupersedeUsage;
        if (!TryParseMutationArguments(args, operation, out var input, out var error))
        {
            return EmitArgumentError(writer, error, usage);
        }

        var read = OperatorAttentionStore.Read(context.RepoRoot);
        if (read.Status != OperatorAttentionReadStatus.Readable)
        {
            return EmitMutationFailure(
                writer,
                input!,
                read.Status,
                read.Error ?? $"operator-attention store is {read.Status}; no transition can be established.");
        }

        var records = read.Document!.Records.ToArray();
        var index = Array.FindIndex(records, record =>
            string.Equals(record.RecordId, input!.RecordId, StringComparison.Ordinal));
        if (index < 0)
        {
            return EmitMutationFailure(writer, input!, read.Status, $"record '{input!.RecordId}' was not found.");
        }

        var existing = records[index];
        if (!string.Equals(existing.Status, OperatorAttentionStatus.Open, StringComparison.Ordinal))
        {
            return EmitMutationFailure(
                writer,
                input!,
                read.Status,
                $"record '{existing.RecordId}' is terminal '{existing.Status}' and can never be reopened or transitioned again.");
        }

        var now = Now();
        var nextStatus = operation == OperatorAttentionOperation.Resolve
            ? OperatorAttentionStatus.Resolved
            : OperatorAttentionStatus.Superseded;
        var evidence = operation == OperatorAttentionOperation.Resolve
            ? input!.ResolutionEvidence!
            : input!.Evidence!;
        var updated = existing with
        {
            Status = nextStatus,
            ResolutionEvidence = operation == OperatorAttentionOperation.Resolve ? evidence : null,
            Transitions =
            [
                .. existing.Transitions,
                new OperatorAttentionTransition
                {
                    FromStatus = OperatorAttentionStatus.Open,
                    ToStatus = nextStatus,
                    TransitionedAt = now,
                    Evidence = evidence,
                },
            ],
        };
        records[index] = updated;

        var document = OperatorAttentionStore.BuildUpdated(read.Document, records, now);
        return ApplyAndEmit(context, writer, input, document, updated);
    }

    private static int ApplyAndEmit(
        CliContext context,
        TextWriter writer,
        OperatorAttentionMutationInput input,
        OperatorAttentionStoreDocument document,
        OperatorAttentionRecord record)
    {
        if (input.Write)
        {
            OperatorAttentionStore.Write(context.RepoRoot, document);
        }

        var result = new OperatorAttentionMutationResult
        {
            Status = "ok",
            Mode = input.Write ? "write" : "dry-run",
            Applied = input.Write,
            Operation = input.Operation.ToString().ToLowerInvariant(),
            StorePath = OperatorAttentionStore.RelativePath,
            Record = OperatorAttentionQueryRecord.From(record, Now()),
            Error = null,
        };
        EmitMutation(writer, input.Format, result);
        return 0;
    }

    private static int EmitMutationFailure(
        TextWriter writer,
        OperatorAttentionMutationInput input,
        string readStatus,
        string error)
    {
        var result = new OperatorAttentionMutationResult
        {
            Status = readStatus == OperatorAttentionReadStatus.CannotDetermine
                ? OperatorAttentionQueryStatus.CannotDetermine
                : "refused",
            Mode = "refused",
            Applied = false,
            Operation = input.Operation.ToString().ToLowerInvariant(),
            StorePath = OperatorAttentionStore.RelativePath,
            Record = null,
            Error = error,
        };
        EmitMutation(writer, input.Format, result);
        return 1;
    }

    private static void EmitMutation(TextWriter writer, string format, OperatorAttentionMutationResult result)
    {
        if (format == FormatJson)
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        writer.WriteLine($"# operator-attention {result.Operation}");
        writer.WriteLine();
        writer.WriteLine($"- status: {result.Status}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- applied: {(result.Applied ? "true" : "false")}");
        writer.WriteLine($"- store_path: `{result.StorePath}`");
        if (result.Record is not null)
        {
            writer.WriteLine($"- record: `{result.Record.RecordId}` ({result.Record.Status})");
        }
        if (result.Error is not null)
        {
            writer.WriteLine($"- error: {result.Error}");
        }
    }

    private static void EmitQuery(TextWriter writer, string format, OperatorAttentionQueryResult result)
    {
        if (format == FormatJson)
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        writer.WriteLine("# operator-attention query");
        writer.WriteLine();
        writer.WriteLine($"- status: {result.Status}");
        writer.WriteLine($"- domain: {result.Domain ?? "(all)"}");
        writer.WriteLine($"- team: {result.Team ?? "(all)"}");
        writer.WriteLine($"- open_count: {result.OpenCount}");
        writer.WriteLine($"- store_path: `{result.StorePath}`");
        foreach (var record in result.OpenRecords)
        {
            writer.WriteLine($"- `{record.RecordId}` ({record.AgeMinutes}m): {record.ActionNeeded} — owner `{record.Owner}`");
        }
        if (result.Error is not null)
        {
            writer.WriteLine($"- error: {result.Error}");
        }
    }

    private static int EmitArgumentError(TextWriter writer, string error, string usage)
    {
        writer.WriteLine(error);
        writer.WriteLine(usage);
        return 1;
    }

    private static DateTimeOffset Now() =>
        (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();

    private static bool IsHelp(string[] args) =>
        args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal);

    private static bool TryParseMutationArguments(
        string[] args,
        OperatorAttentionOperation operation,
        out OperatorAttentionMutationInput? input,
        out string error)
    {
        input = null;
        error = string.Empty;
        string? recordId = null;
        string? domain = null;
        string? team = null;
        string? owner = null;
        string? blockingReference = null;
        string? actionNeeded = null;
        string? evidence = null;
        string? resolutionEvidence = null;
        string? supersedes = null;
        var write = false;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--record":
                case "--record-id":
                    if (!TryTakeValue(args, ref index, argument, out recordId, out error)) return false;
                    break;
                case "--domain":
                    if (!TryTakeValue(args, ref index, argument, out domain, out error)) return false;
                    break;
                case "--team":
                    if (!TryTakeValue(args, ref index, argument, out team, out error)) return false;
                    break;
                case "--owner":
                    if (!TryTakeValue(args, ref index, argument, out owner, out error)) return false;
                    break;
                case "--blocking-reference":
                    if (!TryTakeValue(args, ref index, argument, out blockingReference, out error)) return false;
                    break;
                case "--action-needed":
                    if (!TryTakeValue(args, ref index, argument, out actionNeeded, out error)) return false;
                    break;
                case "--evidence":
                    if (!TryTakeValue(args, ref index, argument, out evidence, out error)) return false;
                    break;
                case "--resolution-evidence":
                    if (!TryTakeValue(args, ref index, argument, out resolutionEvidence, out error)) return false;
                    break;
                case "--supersedes":
                    if (!TryTakeValue(args, ref index, argument, out supersedes, out error)) return false;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--dry-run":
                    write = false;
                    break;
                case "--format":
                    if (!TryTakeValue(args, ref index, argument, out var requestedFormat, out error)) return false;
                    format = requestedFormat!;
                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (!TryValidateIdentifier(recordId, "--record", out error)) return false;
        if (!TryValidateFormat(format, out error)) return false;

        if (operation == OperatorAttentionOperation.Open)
        {
            if (!TryValidateIdentifier(domain, "--domain", out error)
                || !TryValidateIdentifier(team, "--team", out error)
                || !RequireText(owner, "--owner", out error)
                || !RequireText(blockingReference, "--blocking-reference", out error)
                || !RequireText(actionNeeded, "--action-needed", out error)
                || !RequireText(evidence, "--evidence", out error))
            {
                return false;
            }
            if (supersedes is not null && !TryValidateIdentifier(supersedes, "--supersedes", out error)) return false;
        }
        else if (operation == OperatorAttentionOperation.Resolve)
        {
            if (!RequireText(resolutionEvidence, "--resolution-evidence", out error)) return false;
        }
        else if (!RequireText(evidence, "--evidence", out error))
        {
            return false;
        }

        input = new OperatorAttentionMutationInput
        {
            Operation = operation,
            RecordId = recordId,
            Domain = domain,
            Team = team,
            Owner = owner?.Trim(),
            BlockingReference = blockingReference?.Trim(),
            ActionNeeded = actionNeeded?.Trim(),
            Evidence = evidence?.Trim(),
            ResolutionEvidence = resolutionEvidence?.Trim(),
            SupersedesRecordId = supersedes,
            Write = write,
            Format = format,
        };
        return true;
    }

    private static bool TryParseQueryArguments(
        string[] args,
        out string? domain,
        out string? team,
        out string format,
        out string error)
    {
        domain = null;
        team = null;
        format = FormatMarkdown;
        error = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain":
                    if (!TryTakeValue(args, ref index, argument, out domain, out error)) return false;
                    break;
                case "--team":
                    if (!TryTakeValue(args, ref index, argument, out team, out error)) return false;
                    break;
                case "--format":
                    if (!TryTakeValue(args, ref index, argument, out var requestedFormat, out error)) return false;
                    format = requestedFormat!;
                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (domain is null && team is null)
        {
            error = "operator-attention query requires --domain, --team, or both; an unscoped host-wide answer is refused.";
            return false;
        }
        if (domain is not null && !TryValidateIdentifier(domain, "--domain", out error)) return false;
        if (team is not null && !TryValidateIdentifier(team, "--team", out error)) return false;
        return TryValidateFormat(format, out error);
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string argument,
        out string? value,
        out string error)
    {
        value = null;
        error = string.Empty;
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            error = $"{argument} requires a non-blank value.";
            return false;
        }
        value = args[++index].Trim();
        return true;
    }

    private static bool TryValidateIdentifier(string? value, string argument, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{argument} is required.";
            return false;
        }
        if (value.Length > 128 || value[0] == '.' || value.Contains("..", StringComparison.Ordinal)
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
        {
            error = $"{argument} '{value}' must be a canonical identifier using ASCII letters, digits, '-', '_' or '.', with no leading dot or '..'.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool RequireText(string? value, string argument, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{argument} is required and must state actionable evidence or context.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryValidateFormat(string format, out string error)
    {
        if (format is FormatJson or FormatMarkdown)
        {
            error = string.Empty;
            return true;
        }
        error = $"--format must be 'json' or 'markdown' (got '{format}').";
        return false;
    }

    private const string OpenUsage =
        "Usage: intent-cli operator-attention open --record <id> --domain <d> --team <t> --owner <owner> --blocking-reference <ref> --action-needed <text> --evidence <text> [--supersedes <record>] [--dry-run|--write] [--format json|markdown]";
    private const string ResolveUsage =
        "Usage: intent-cli operator-attention resolve --record <id> --resolution-evidence <text> [--dry-run|--write] [--format json|markdown]";
    private const string SupersedeUsage =
        "Usage: intent-cli operator-attention supersede --record <id> --evidence <text> [--dry-run|--write] [--format json|markdown]";
    private const string QueryUsage =
        "Usage: intent-cli operator-attention query [--domain <d>] [--team <t>] [--format json|markdown]";

    private enum OperatorAttentionOperation
    {
        Open,
        Resolve,
        Supersede,
    }

    private sealed record OperatorAttentionMutationInput
    {
        public required OperatorAttentionOperation Operation { get; init; }
        public required string? RecordId { get; init; }
        public required string? Domain { get; init; }
        public required string? Team { get; init; }
        public required string? Owner { get; init; }
        public required string? BlockingReference { get; init; }
        public required string? ActionNeeded { get; init; }
        public required string? Evidence { get; init; }
        public required string? ResolutionEvidence { get; init; }
        public required string? SupersedesRecordId { get; init; }
        public required bool Write { get; init; }
        public required string Format { get; init; }
    }
}

internal static class OperatorAttentionStore
{
    public const string RelativePath = $".intent-cli/{CliRuntimeContracts.OperatorAttentionFileName}";
    public const string ArtifactKind = "operator-attention-store";
    public const string SchemaVersion = "1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static OperatorAttentionReadResult Read(string repoRoot)
    {
        var path = ResolvePath(repoRoot);
        if (!File.Exists(path))
        {
            return new OperatorAttentionReadResult
            {
                Status = OperatorAttentionReadStatus.Missing,
                Document = null,
                Error = $"`{RelativePath}` is absent; the operator-attention check has not been completed for this host.",
            };
        }

        try
        {
            var document = JsonSerializer.Deserialize<OperatorAttentionStoreDocument>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException("store payload is JSON null.");
            Validate(document);
            return new OperatorAttentionReadResult
            {
                Status = OperatorAttentionReadStatus.Readable,
                Document = document,
                Error = null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new OperatorAttentionReadResult
            {
                Status = OperatorAttentionReadStatus.CannotDetermine,
                Document = null,
                Error = $"`{RelativePath}` cannot be read as authoritative operator-attention state: {exception.Message}",
            };
        }
    }

    public static OperatorAttentionStoreDocument BuildUpdated(
        OperatorAttentionStoreDocument? existing,
        IReadOnlyList<OperatorAttentionRecord> records,
        DateTimeOffset updatedAt) => new()
        {
            ArtifactKind = ArtifactKind,
            SchemaVersion = existing?.SchemaVersion ?? SchemaVersion,
            UpdatedAt = updatedAt.ToUniversalTime(),
            Records = records,
        };

    public static void Write(string repoRoot, OperatorAttentionStoreDocument document)
    {
        Validate(document);
        AtomicFileWriter.WriteAllText(
            ResolvePath(repoRoot),
            JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
    }

    public static string ResolvePath(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        return CliRuntimeContracts.GetOperatorAttentionPath(repoRoot);
    }

    private static void Validate(OperatorAttentionStoreDocument document)
    {
        if (!string.Equals(document.ArtifactKind, ArtifactKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"artifact_kind must be '{ArtifactKind}'.");
        }
        if (!string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"schema_version must be '{SchemaVersion}'.");
        }
        if (document.Records is null)
        {
            throw new InvalidOperationException("records must be an array.");
        }

        var recordsById = new Dictionary<string, OperatorAttentionRecord>(StringComparer.Ordinal);
        foreach (var record in document.Records)
        {
            ValidateRecord(record);
            if (!recordsById.TryAdd(record.RecordId, record))
            {
                throw new InvalidOperationException($"record_id '{record.RecordId}' appears more than once.");
            }
        }

        foreach (var record in document.Records.Where(record => record.SupersedesRecordId is not null))
        {
            if (!recordsById.TryGetValue(record.SupersedesRecordId!, out var superseded))
            {
                throw new InvalidOperationException($"record '{record.RecordId}' supersedes missing record '{record.SupersedesRecordId}'.");
            }
            if (!string.Equals(superseded.Status, OperatorAttentionStatus.Superseded, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"record '{record.RecordId}' references '{superseded.RecordId}', which is not superseded.");
            }
            if (!string.Equals(record.Domain, superseded.Domain, StringComparison.Ordinal)
                || !string.Equals(record.Team, superseded.Team, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"record '{record.RecordId}' and superseded record '{superseded.RecordId}' must share domain and team.");
            }
        }
    }

    private static void ValidateRecord(OperatorAttentionRecord record)
    {
        Require(record.RecordId, "record_id");
        Require(record.Domain, $"record '{record.RecordId}' domain");
        Require(record.Team, $"record '{record.RecordId}' team");
        Require(record.Owner, $"record '{record.RecordId}' owner");
        Require(record.BlockingReference, $"record '{record.RecordId}' blocking_reference");
        Require(record.ActionNeeded, $"record '{record.RecordId}' action_needed");
        Require(record.EstablishingEvidence, $"record '{record.RecordId}' establishing_evidence");
        if (!OperatorAttentionStatus.All.Contains(record.Status, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"record '{record.RecordId}' has unknown status '{record.Status}'; only open, resolved, superseded are valid.");
        }
        if (record.Transitions is null || record.Transitions.Count == 0)
        {
            throw new InvalidOperationException($"record '{record.RecordId}' must carry transition timestamps.");
        }

        var first = record.Transitions[0];
        if (first.FromStatus is not null
            || !string.Equals(first.ToStatus, OperatorAttentionStatus.Open, StringComparison.Ordinal)
            || first.TransitionedAt != record.OpenedAt
            || !string.Equals(first.Evidence, record.EstablishingEvidence, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"record '{record.RecordId}' must begin with one establishing transition to open at opened_at.");
        }

        for (var index = 0; index < record.Transitions.Count; index++)
        {
            var transition = record.Transitions[index];
            Require(transition.ToStatus, $"record '{record.RecordId}' transition to_status");
            Require(transition.Evidence, $"record '{record.RecordId}' transition evidence");
            if (!OperatorAttentionStatus.All.Contains(transition.ToStatus, StringComparer.Ordinal)
                || (transition.FromStatus is not null && !OperatorAttentionStatus.All.Contains(transition.FromStatus, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException($"record '{record.RecordId}' transition contains a status outside open/resolved/superseded.");
            }
            if (index > 0 && transition.TransitionedAt < record.Transitions[index - 1].TransitionedAt)
            {
                throw new InvalidOperationException($"record '{record.RecordId}' transition timestamps move backwards.");
            }
        }

        if (!string.Equals(record.Transitions[^1].ToStatus, record.Status, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"record '{record.RecordId}' status does not match its last transition.");
        }

        if (record.Status == OperatorAttentionStatus.Open)
        {
            if (record.Transitions.Count != 1 || record.ResolutionEvidence is not null)
            {
                throw new InvalidOperationException($"open record '{record.RecordId}' cannot carry a terminal transition or resolution evidence.");
            }
            return;
        }

        if (record.Transitions.Count != 2
            || !string.Equals(record.Transitions[1].FromStatus, OperatorAttentionStatus.Open, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"terminal record '{record.RecordId}' must have exactly one open-to-terminal transition.");
        }
        if (record.Status == OperatorAttentionStatus.Resolved)
        {
            Require(record.ResolutionEvidence, $"resolved record '{record.RecordId}' resolution_evidence");
            if (!string.Equals(record.ResolutionEvidence, record.Transitions[1].Evidence, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"resolved record '{record.RecordId}' has inconsistent resolution evidence.");
            }
        }
        else if (record.ResolutionEvidence is not null)
        {
            throw new InvalidOperationException($"superseded record '{record.RecordId}' is not resolved and cannot carry resolution_evidence.");
        }
    }

    private static void Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{field} must not be blank.");
        }
    }
}

internal static class OperatorAttentionStatus
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Superseded = "superseded";
    public static readonly IReadOnlyList<string> All = [Open, Resolved, Superseded];
}

internal static class OperatorAttentionReadStatus
{
    public const string Readable = "readable";
    public const string Missing = "check-not-completed";
    public const string CannotDetermine = "cannot-determine";
}

internal static class OperatorAttentionQueryStatus
{
    public const string AttentionPending = "attention-pending";
    public const string NoAttentionPending = "no-attention-pending";
    public const string CheckNotCompleted = "check-not-completed";
    public const string CannotDetermine = "cannot-determine";
}

internal sealed record OperatorAttentionStoreDocument
{
    [JsonPropertyName("artifact_kind")]
    public required string ArtifactKind { get; init; }
    [JsonPropertyName("schema_version")]
    public required string SchemaVersion { get; init; }
    [JsonPropertyName("updated_at")]
    public required DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("records")]
    public required IReadOnlyList<OperatorAttentionRecord> Records { get; init; }
}

internal sealed record OperatorAttentionRecord
{
    [JsonPropertyName("record_id")]
    public required string RecordId { get; init; }
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }
    [JsonPropertyName("team")]
    public required string Team { get; init; }
    [JsonPropertyName("owner")]
    public required string Owner { get; init; }
    [JsonPropertyName("blocking_reference")]
    public required string BlockingReference { get; init; }
    [JsonPropertyName("action_needed")]
    public required string ActionNeeded { get; init; }
    [JsonPropertyName("establishing_evidence")]
    public required string EstablishingEvidence { get; init; }
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    [JsonPropertyName("opened_at")]
    public required DateTimeOffset OpenedAt { get; init; }
    [JsonPropertyName("resolution_evidence")]
    public required string? ResolutionEvidence { get; init; }
    [JsonPropertyName("supersedes_record_id")]
    public required string? SupersedesRecordId { get; init; }
    [JsonPropertyName("transitions")]
    public required IReadOnlyList<OperatorAttentionTransition> Transitions { get; init; }
}

internal sealed record OperatorAttentionTransition
{
    [JsonPropertyName("from_status")]
    public required string? FromStatus { get; init; }
    [JsonPropertyName("to_status")]
    public required string ToStatus { get; init; }
    [JsonPropertyName("transitioned_at")]
    public required DateTimeOffset TransitionedAt { get; init; }
    [JsonPropertyName("evidence")]
    public required string Evidence { get; init; }
}

internal sealed record OperatorAttentionReadResult
{
    public required string Status { get; init; }
    public required OperatorAttentionStoreDocument? Document { get; init; }
    public required string? Error { get; init; }
}

internal sealed record OperatorAttentionQueryRecord
{
    [JsonPropertyName("record_id")]
    public required string RecordId { get; init; }
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }
    [JsonPropertyName("team")]
    public required string Team { get; init; }
    [JsonPropertyName("owner")]
    public required string Owner { get; init; }
    [JsonPropertyName("blocking_reference")]
    public required string BlockingReference { get; init; }
    [JsonPropertyName("action_needed")]
    public required string ActionNeeded { get; init; }
    [JsonPropertyName("establishing_evidence")]
    public required string EstablishingEvidence { get; init; }
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    [JsonPropertyName("opened_at")]
    public required DateTimeOffset OpenedAt { get; init; }
    [JsonPropertyName("age_minutes")]
    public required int AgeMinutes { get; init; }
    [JsonPropertyName("resolution_evidence")]
    public required string? ResolutionEvidence { get; init; }
    [JsonPropertyName("supersedes_record_id")]
    public required string? SupersedesRecordId { get; init; }
    [JsonPropertyName("transitions")]
    public required IReadOnlyList<OperatorAttentionTransition> Transitions { get; init; }

    public static OperatorAttentionQueryRecord From(OperatorAttentionRecord record, DateTimeOffset now) => new()
    {
        RecordId = record.RecordId,
        Domain = record.Domain,
        Team = record.Team,
        Owner = record.Owner,
        BlockingReference = record.BlockingReference,
        ActionNeeded = record.ActionNeeded,
        EstablishingEvidence = record.EstablishingEvidence,
        Status = record.Status,
        OpenedAt = record.OpenedAt,
        AgeMinutes = Math.Max(0, (int)Math.Floor((now - record.OpenedAt).TotalMinutes)),
        ResolutionEvidence = record.ResolutionEvidence,
        SupersedesRecordId = record.SupersedesRecordId,
        Transitions = record.Transitions,
    };
}

internal sealed record OperatorAttentionMutationResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }
    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }
    [JsonPropertyName("store_path")]
    public required string StorePath { get; init; }
    [JsonPropertyName("record")]
    public required OperatorAttentionQueryRecord? Record { get; init; }
    [JsonPropertyName("error")]
    public required string? Error { get; init; }
}

internal sealed record OperatorAttentionQueryResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    [JsonPropertyName("domain")]
    public required string? Domain { get; init; }
    [JsonPropertyName("team")]
    public required string? Team { get; init; }
    [JsonPropertyName("checked_at")]
    public required DateTimeOffset CheckedAt { get; init; }
    [JsonPropertyName("store_path")]
    public required string StorePath { get; init; }
    [JsonPropertyName("open_count")]
    public required int OpenCount { get; init; }
    [JsonPropertyName("open_records")]
    public required IReadOnlyList<OperatorAttentionQueryRecord> OpenRecords { get; init; }
    [JsonPropertyName("records")]
    public required IReadOnlyList<OperatorAttentionQueryRecord> Records { get; init; }
    [JsonPropertyName("error")]
    public required string? Error { get; init; }
}
