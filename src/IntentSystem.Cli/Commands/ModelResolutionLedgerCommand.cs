using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record ModelResolutionLedgerEntry
{
    [JsonPropertyName("informal_name")]
    public required string InformalName { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("full_invocation")]
    public string? FullInvocation { get; init; }

    [JsonPropertyName("refused_invocation")]
    public string? RefusedInvocation { get; init; }

    [JsonPropertyName("evidence")]
    public string? Evidence { get; init; }

    [JsonPropertyName("error_text")]
    public string? ErrorText { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }

    [JsonIgnore]
    public string Invocation => FullInvocation ?? RefusedInvocation ?? string.Empty;
}

internal sealed record ModelResolutionLedgerReadResult
{
    public required bool Resolved { get; init; }
    public required string Path { get; init; }
    public required IReadOnlyList<ModelResolutionLedgerEntry> Entries { get; init; }
    public string? Error { get; init; }
}

internal sealed record ModelResolutionLedgerWriteResult(bool Applied, string Path, string? Error);

/// <summary>
/// G685 host-local, append-only measurement store. It is neither provider
/// configuration nor a model catalogue; no network or provider process is
/// consulted by this store.
/// </summary>
internal static class ModelResolutionLedgerStore
{
    public const string RelativePath = ".intent-cli/model-resolution/ledger.jsonl";
    private const string IgnoreFileName = ".gitignore";
    private static readonly object Sync = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static Func<string, string, ModelResolutionLedgerWriteResult>? WriteOverride { get; set; }

    public static string ResolvePath(string hostRoot) => Path.GetFullPath(Path.Combine(
        hostRoot,
        RelativePath.Replace('/', Path.DirectorySeparatorChar)));

    public static ModelResolutionLedgerReadResult Read(string hostRoot)
    {
        var path = ResolvePath(hostRoot);
        lock (Sync)
        {
            if (!File.Exists(path))
            {
                return new ModelResolutionLedgerReadResult
                {
                    Resolved = true,
                    Path = path,
                    Entries = [],
                };
            }

            try
            {
                var entries = new List<ModelResolutionLedgerEntry>();
                var lineNumber = 0;
                foreach (var line in File.ReadLines(path))
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var entry = JsonSerializer.Deserialize<ModelResolutionLedgerEntry>(line, JsonOptions)
                        ?? throw new InvalidDataException($"Model-resolution ledger line {lineNumber} was empty.");
                    ValidateEntry(entry, lineNumber);
                    entries.Add(entry);
                }

                return new ModelResolutionLedgerReadResult
                {
                    Resolved = true,
                    Path = path,
                    Entries = entries,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or JsonException or InvalidDataException)
            {
                return new ModelResolutionLedgerReadResult
                {
                    Resolved = false,
                    Path = path,
                    Entries = [],
                    Error = $"Model-resolution ledger at '{path}' could not be read: {exception.Message}",
                };
            }
        }
    }

    public static ModelResolutionLedgerWriteResult Append(
        string hostRoot,
        ModelResolutionLedgerEntry entry,
        bool write)
    {
        var path = ResolvePath(hostRoot);
        try
        {
            ValidateEntry(entry, lineNumber: null);
        }
        catch (InvalidDataException exception)
        {
            return new ModelResolutionLedgerWriteResult(false, path, exception.Message);
        }

        if (!write)
        {
            return new ModelResolutionLedgerWriteResult(false, path, null);
        }

        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, line);
        }

        lock (Sync)
        {
            try
            {
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                EnsureIgnoreRule(directory);
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return new ModelResolutionLedgerWriteResult(true, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new ModelResolutionLedgerWriteResult(false, path, exception.Message);
            }
        }
    }

    private static void EnsureIgnoreRule(string directory)
    {
        var ignorePath = Path.Combine(directory, IgnoreFileName);
        const string requiredRule = "ledger.jsonl";
        if (!File.Exists(ignorePath))
        {
            File.WriteAllText(ignorePath, requiredRule + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        var content = File.ReadAllText(ignorePath);
        var alreadyPresent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Any(line => string.Equals(line.Trim(), requiredRule, StringComparison.Ordinal));
        if (alreadyPresent)
        {
            return;
        }

        var separator = content.Length == 0 || content.EndsWith('\n')
            ? string.Empty
            : Environment.NewLine;
        File.AppendAllText(
            ignorePath,
            separator + requiredRule + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void ValidateEntry(ModelResolutionLedgerEntry entry, int? lineNumber)
    {
        var prefix = lineNumber is null ? "Model-resolution entry" : $"Model-resolution ledger line {lineNumber}";
        if (string.IsNullOrWhiteSpace(entry.InformalName)
            || string.IsNullOrWhiteSpace(entry.Kind)
            || entry.RecordedAt == default)
        {
            throw new InvalidDataException($"{prefix} is missing informal_name, kind, or recorded_at.");
        }

        if (entry.Outcome == ModelResolutionLedgerCommand.VerifiedOutcome)
        {
            if (string.IsNullOrWhiteSpace(entry.FullInvocation)
                || string.IsNullOrWhiteSpace(entry.Evidence)
                || entry.RefusedInvocation is not null
                || entry.ErrorText is not null)
            {
                throw new InvalidDataException(
                    $"{prefix} outcome verified requires full_invocation and evidence only.");
            }
            return;
        }

        if (entry.Outcome == ModelResolutionLedgerCommand.RefusedOutcome)
        {
            if (string.IsNullOrWhiteSpace(entry.RefusedInvocation)
                || string.IsNullOrWhiteSpace(entry.ErrorText)
                || entry.FullInvocation is not null
                || entry.Evidence is not null)
            {
                throw new InvalidDataException(
                    $"{prefix} outcome refused requires refused_invocation and error_text only.");
            }
            return;
        }

        throw new InvalidDataException($"{prefix} has unsupported outcome '{entry.Outcome}'.");
    }
}

internal sealed record AgentLiveArgvFallback
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("list_command")]
    public required string ListCommand { get; init; }

    [JsonPropertyName("selection")]
    public required string Selection { get; init; }

    [JsonPropertyName("inspect_command")]
    public required string InspectCommand { get; init; }

    [JsonPropertyName("argv_path")]
    public required string ArgvPath { get; init; }

    [JsonPropertyName("agreement_rule")]
    public required string AgreementRule { get; init; }

    [JsonPropertyName("human_fallback")]
    public required string HumanFallback { get; init; }
}

internal sealed record AgentLaunchEvidenceRecordStep
{
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("when")]
    public required string When { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("command_arguments")]
    public required IReadOnlyList<string> CommandArguments { get; init; }

    [JsonPropertyName("captured_fields")]
    public required IReadOnlyList<string> CapturedFields { get; init; }
}

internal sealed record AgentLaunchEvidenceWorkflow
{
    [JsonPropertyName("mandatory")]
    public required bool Mandatory { get; init; }

    [JsonPropertyName("rule")]
    public required string Rule { get; init; }

    [JsonPropertyName("verified")]
    public required AgentLaunchEvidenceRecordStep Verified { get; init; }

    [JsonPropertyName("refused")]
    public required AgentLaunchEvidenceRecordStep Refused { get; init; }

    [JsonPropertyName("provider_operation")]
    public required string ProviderOperation { get; init; }
}

internal static class AgentModelResolutionGuidance
{
    public const string PreviewStatus = "preview-through-1.x";
    public const string NeverGuessRule =
        "Never guess a bare model id and never consult a shipped model list; intent-cli ships no model identifiers or provider catalogue.";
    public const string Incident =
        "Measured 2026-08-12 on the btx-mvc host: the informal-name guess `--model sol` was refused with an account-shaped HTTP 400; recovery read a currently-running same-kind seat argv and reused its full invocation. The recovered provider id remains host-local evidence and is not shipped.";

    public static readonly IReadOnlyList<string> ResolutionOrder =
    [
        "Query the host-local measured ledger for an exact informal-name and kind hit.",
        "If absent, read a currently-running same-kind seat argv and use its full model/effort invocation as measured host evidence.",
        "If neither source resolves it, ask the human before emitting a launch command.",
    ];

    public const string RecordCommand =
        "intent-cli session-layer model-resolution record --kind <codex|claude> --informal-name <name> --outcome verified|refused --invocation <full-invocation> --evidence <verified-evidence>|--error <refusal-error> --write --format json";
    public const string QueryCommand =
        "intent-cli session-layer model-resolution query --kind <codex|claude> --informal-name <name> [--candidate-invocation <full-invocation>] --format json";

    public static readonly AgentLiveArgvFallback LiveArgvFallback = new()
    {
        Mode = "read-only",
        ListCommand = "herdr agent list",
        Selection =
            "Read result.agents[]; retain entries whose agent equals <resolved-kind>, agent_session is an object, interactive_ready is not false, agent_status is not unknown, and pane_id is non-empty. Sort by workspace_id then pane_id. Zero candidates proceeds to the human fallback; one candidate proceeds to argv inspection; multiple candidates must all be inspected.",
        InspectCommand = "herdr pane process-info --pane <selected-pane-id>",
        ArgvPath = "result.process_info.foreground_processes[].argv",
        AgreementRule =
            "From each selected pane, retain the foreground process whose argv executable matches <resolved-kind>. Use the full argv only when every inspected same-kind candidate reports the same model/effort invocation; disagreement is unresolved and proceeds to the human fallback.",
        HumanFallback =
            "Ask the human for the full invocation only after the ledger miss and this live same-kind argv procedure returns zero candidates, no readable argv, or disagreement.",
    };

    public static readonly AgentLaunchEvidenceWorkflow LaunchEvidenceWorkflow = new()
    {
        Mandatory = true,
        Rule =
            "After every rendered launch attempt, run exactly one matching record step before retrying or continuing: verified only after the READY proof captures the launched invocation plus banner/running-argv evidence; refused immediately after the captured error returns the seat to a shell. This is a required workflow step, not an operator-maintained ledger task.",
        Verified = CreateRecordStep(
            ModelResolutionLedgerCommand.VerifiedOutcome,
            "After the launched seat passes READY with captured banner/running-argv evidence.",
            "--evidence",
            "<captured-ready-banner-and-running-argv-evidence>"),
        Refused = CreateRecordStep(
            ModelResolutionLedgerCommand.RefusedOutcome,
            "Immediately after the exact launched invocation is refused and its captured error is visible.",
            "--error",
            "<captured-refusal-error-text>"),
        ProviderOperation = "none",
    };

    private static AgentLaunchEvidenceRecordStep CreateRecordStep(
        string outcome,
        string when,
        string evidenceOption,
        string evidencePlaceholder)
    {
        var arguments = new[]
        {
            "session-layer", "model-resolution", "record",
            "--kind", "<resolved-kind>",
            "--informal-name", "<captured-informal-name-and-effort>",
            "--outcome", outcome,
            "--invocation", "<captured-exact-launched-invocation>",
            evidenceOption, evidencePlaceholder,
            "--write", "--format", "json",
        };
        return new AgentLaunchEvidenceRecordStep
        {
            Outcome = outcome,
            When = when,
            Command = "intent-cli " + string.Join(' ', arguments.Select(RenderArgument)),
            CommandArguments = arguments,
            CapturedFields = outcome == ModelResolutionLedgerCommand.VerifiedOutcome
                ? ["kind", "informal name and effort", "exact launched invocation", "READY banner and running argv evidence"]
                : ["kind", "informal name and effort", "exact refused invocation", "refusal error text"],
        };
    }

    private static string RenderArgument(string value) =>
        value.StartsWith('<') && value.EndsWith('>') ? $"'{value}'" : value;
}

internal sealed record ModelResolutionRecordResult
{
    public required string Operation { get; init; }
    public required string PreviewStatus { get; init; }
    public required string CommandMode { get; init; }
    public required bool Applied { get; init; }
    public required string RecordPath { get; init; }
    public required ModelResolutionLedgerEntry Entry { get; init; }
    public required AgentModelFlagGrammar Grammar { get; init; }
    public required string ProviderOperation { get; init; }
    public string? Error { get; init; }
}

internal sealed record ModelResolutionQueryResult
{
    public required string Operation { get; init; }
    public required string PreviewStatus { get; init; }
    public required bool Resolved { get; init; }
    public required string Status { get; init; }
    public required string RecordPath { get; init; }
    public required string Kind { get; init; }
    public required string InformalName { get; init; }
    public required AgentModelFlagGrammar Grammar { get; init; }
    public ModelResolutionLedgerEntry? PositiveEntry { get; init; }
    public ModelResolutionLedgerEntry? NegativeEntry { get; init; }
    public bool? CandidateRetryPermitted { get; init; }
    public required IReadOnlyList<string> ResolutionOrder { get; init; }
    public AgentLiveArgvFallback? LiveArgvFallback { get; init; }
    public required string NextStep { get; init; }
    public required string NeverGuessRule { get; init; }
    public required string ProviderOperation { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Canonical append/query surface for G685. It records evidence supplied by a
/// launching thread but never launches a provider or validates a model id.
/// </summary>
internal static class ModelResolutionLedgerCommand
{
    public const string VerifiedOutcome = "verified";
    public const string RefusedOutcome = "refused";
    private const string Usage =
        "Usage: intent-cli session-layer model-resolution record|query [options]";
    private const string RecordUsage =
        "Usage: intent-cli session-layer model-resolution record --kind <codex|claude> --informal-name <name> "
        + "--outcome verified|refused --invocation <full-invocation> (--evidence <text>|--error <text>) "
        + "[--dry-run|--write] [--format markdown|json]";
    private const string QueryUsage =
        "Usage: intent-cli session-layer model-resolution query --kind <codex|claude> --informal-name <name> "
        + "[--candidate-invocation <full-invocation>] [--format markdown|json]";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal static Func<DateTimeOffset> UtcNowFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0] == "--help"))
        {
            writer.WriteLine(Usage);
            writer.WriteLine(RecordUsage);
            writer.WriteLine(QueryUsage);
            writer.WriteLine("Preview-through-1.x host-local measurement only; launches no provider and performs no provider validation.");
            return args.Length == 0 ? 1 : 0;
        }

        return args[0] switch
        {
            "record" => ExecuteRecord(context, args[1..], writer),
            "query" => ExecuteQuery(context, args[1..], writer),
            _ => Unknown(args[0], writer),
        };
    }

    private static int ExecuteRecord(CliContext context, string[] args, TextWriter writer)
    {
        if (!TryParseRecord(args, out var parsed, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(RecordUsage);
            return 1;
        }

        var grammar = AgentLaunchRecipeRegistry.FindModelFlagGrammar(parsed.Kind!);
        if (grammar is null)
        {
            writer.WriteLine(UnknownKind(parsed.Kind!));
            return 1;
        }

        var entry = new ModelResolutionLedgerEntry
        {
            InformalName = parsed.InformalName!,
            Kind = grammar.Kind,
            Outcome = parsed.Outcome!,
            FullInvocation = parsed.Outcome == VerifiedOutcome ? parsed.Invocation : null,
            RefusedInvocation = parsed.Outcome == RefusedOutcome ? parsed.Invocation : null,
            Evidence = parsed.Outcome == VerifiedOutcome ? parsed.Evidence : null,
            ErrorText = parsed.Outcome == RefusedOutcome ? parsed.Error : null,
            RecordedAt = UtcNowFactory().ToUniversalTime(),
        };
        var writeResult = ModelResolutionLedgerStore.Append(context.RepoRoot, entry, parsed.Write);
        var result = new ModelResolutionRecordResult
        {
            Operation = "model-resolution-record",
            PreviewStatus = AgentModelResolutionGuidance.PreviewStatus,
            CommandMode = parsed.Write ? "write" : "dry-run",
            Applied = writeResult.Applied,
            RecordPath = writeResult.Path,
            Entry = entry,
            Grammar = grammar,
            ProviderOperation = "none",
            Error = writeResult.Error,
        };
        Emit(writer, parsed.Format, result,
            $"Model resolution {entry.Outcome} evidence for '{entry.InformalName}' ({entry.Kind}) "
            + (writeResult.Applied ? "was appended." : parsed.Write ? "was not appended." : "would be appended."));
        return writeResult.Error is null ? 0 : 1;
    }

    private static int ExecuteQuery(CliContext context, string[] args, TextWriter writer)
    {
        if (!TryParseQuery(args, out var parsed, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(QueryUsage);
            return 1;
        }

        var grammar = AgentLaunchRecipeRegistry.FindModelFlagGrammar(parsed.Kind!);
        if (grammar is null)
        {
            writer.WriteLine(UnknownKind(parsed.Kind!));
            return 1;
        }

        var read = ModelResolutionLedgerStore.Read(context.RepoRoot);
        if (!read.Resolved)
        {
            var failure = new ModelResolutionQueryResult
            {
                Operation = "model-resolution-query",
                PreviewStatus = AgentModelResolutionGuidance.PreviewStatus,
                Resolved = false,
                Status = "ledger-unreadable",
                RecordPath = read.Path,
                Kind = grammar.Kind,
                InformalName = parsed.InformalName!,
                Grammar = grammar,
                ResolutionOrder = AgentModelResolutionGuidance.ResolutionOrder,
                NextStep = "repair-ledger-read",
                NeverGuessRule = AgentModelResolutionGuidance.NeverGuessRule,
                ProviderOperation = "none",
                Error = read.Error,
            };
            Emit(writer, parsed.Format, failure, read.Error!);
            return 1;
        }

        var matching = read.Entries.Where(entry =>
                string.Equals(entry.Kind, grammar.Kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.InformalName, parsed.InformalName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RecordedAt)
            .ToArray();
        var positive = matching.LastOrDefault(entry => entry.Outcome == VerifiedOutcome);
        var negative = matching.LastOrDefault(entry => entry.Outcome == RefusedOutcome);
        if (positive is not null)
        {
            var laterSameInvocationRefusal = matching.LastOrDefault(entry =>
                entry.RecordedAt > positive.RecordedAt
                && entry.Outcome == RefusedOutcome
                && string.Equals(entry.RefusedInvocation, positive.FullInvocation, StringComparison.Ordinal));
            if (laterSameInvocationRefusal is not null)
            {
                positive = null;
                negative = laterSameInvocationRefusal;
            }
        }
        bool? retryPermitted = null;
        if (parsed.CandidateInvocation is not null)
        {
            var latestCandidate = matching.LastOrDefault(entry =>
                string.Equals(entry.Invocation, parsed.CandidateInvocation, StringComparison.Ordinal));
            retryPermitted = latestCandidate?.Outcome != RefusedOutcome;
            if (retryPermitted == false) negative = latestCandidate;
        }

        var status = retryPermitted == false
            ? "refused-invocation"
            : positive is not null
                ? "ledger-hit"
                : negative is not null
                    ? "negative-evidence-available"
                    : "ledger-miss";
        var result = new ModelResolutionQueryResult
        {
            Operation = "model-resolution-query",
            PreviewStatus = AgentModelResolutionGuidance.PreviewStatus,
            Resolved = positive is not null && retryPermitted != false,
            Status = status,
            RecordPath = read.Path,
            Kind = grammar.Kind,
            InformalName = parsed.InformalName!,
            Grammar = grammar,
            PositiveEntry = positive,
            NegativeEntry = negative,
            CandidateRetryPermitted = retryPermitted,
            ResolutionOrder = AgentModelResolutionGuidance.ResolutionOrder,
            LiveArgvFallback = positive is null || retryPermitted == false
                ? AgentModelResolutionGuidance.LiveArgvFallback
                : null,
            NextStep = positive is not null && retryPermitted != false
                ? "use-ledger-full-invocation"
                : "inspect-live-same-kind-argv",
            NeverGuessRule = AgentModelResolutionGuidance.NeverGuessRule,
            ProviderOperation = "none",
        };
        Emit(writer, parsed.Format, result, status switch
        {
            "ledger-hit" => $"Host-local ledger hit for '{parsed.InformalName}' ({grammar.Kind}).",
            "refused-invocation" => "The candidate invocation has negative evidence and must not be retried.",
            "negative-evidence-available" => "Negative host-local evidence exists; inspect it, then continue to live argv or ask the human.",
            _ => "No host-local ledger hit; continue to live same-kind argv, then ask the human.",
        });
        return 0;
    }

    private static bool TryParseRecord(string[] args, out ParsedRecord parsed, out string error)
    {
        string? kind = null, informalName = null, outcome = null, invocation = null, evidence = null, refusalError = null;
        var write = false;
        var format = "markdown";
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option == "--write") { write = true; continue; }
            if (option == "--dry-run") { write = false; continue; }
            if (!TryRead(args, ref index, option, out var value, out error))
            {
                parsed = new ParsedRecord();
                return false;
            }
            switch (option)
            {
                case "--kind": kind = value; break;
                case "--informal-name": informalName = value; break;
                case "--outcome": outcome = value; break;
                case "--invocation": invocation = value; break;
                case "--evidence": evidence = value; break;
                case "--error": refusalError = value; break;
                case "--format" when value is "markdown" or "json": format = value; break;
                case "--format":
                    parsed = new ParsedRecord(); error = "--format must be markdown or json."; return false;
                default:
                    parsed = new ParsedRecord(); error = $"Unknown argument '{option}'."; return false;
            }
        }

        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(informalName)
            || string.IsNullOrWhiteSpace(outcome) || string.IsNullOrWhiteSpace(invocation))
        {
            parsed = new ParsedRecord();
            error = "--kind, --informal-name, --outcome, and --invocation are required.";
            return false;
        }
        if (outcome is not (VerifiedOutcome or RefusedOutcome))
        {
            parsed = new ParsedRecord(); error = "--outcome must be verified or refused."; return false;
        }
        if (outcome == VerifiedOutcome && (string.IsNullOrWhiteSpace(evidence) || refusalError is not null))
        {
            parsed = new ParsedRecord(); error = "A verified outcome requires --evidence and does not accept --error."; return false;
        }
        if (outcome == RefusedOutcome && (string.IsNullOrWhiteSpace(refusalError) || evidence is not null))
        {
            parsed = new ParsedRecord(); error = "A refused outcome requires --error and does not accept --evidence."; return false;
        }

        parsed = new ParsedRecord(kind.Trim(), informalName.Trim(), outcome, invocation.Trim(), evidence?.Trim(), refusalError?.Trim(), write, format);
        error = string.Empty;
        return true;
    }

    private static bool TryParseQuery(string[] args, out ParsedQuery parsed, out string error)
    {
        string? kind = null, informalName = null, candidate = null;
        var format = "markdown";
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (!TryRead(args, ref index, option, out var value, out error))
            {
                parsed = new ParsedQuery();
                return false;
            }
            switch (option)
            {
                case "--kind": kind = value; break;
                case "--informal-name": informalName = value; break;
                case "--candidate-invocation": candidate = value; break;
                case "--format" when value is "markdown" or "json": format = value; break;
                case "--format": parsed = new ParsedQuery(); error = "--format must be markdown or json."; return false;
                default: parsed = new ParsedQuery(); error = $"Unknown argument '{option}'."; return false;
            }
        }
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(informalName))
        {
            parsed = new ParsedQuery(); error = "--kind and --informal-name are required."; return false;
        }
        parsed = new ParsedQuery(kind.Trim(), informalName.Trim(), candidate?.Trim(), format);
        error = string.Empty;
        return true;
    }

    private static bool TryRead(string[] args, ref int index, string option, out string value, out string error)
    {
        if (option is not ("--kind" or "--informal-name" or "--outcome" or "--invocation"
            or "--evidence" or "--error" or "--candidate-invocation" or "--format"))
        {
            value = string.Empty; error = $"Unknown argument '{option}'."; return false;
        }
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            value = string.Empty; error = $"{option} requires a value."; return false;
        }
        value = args[index]; error = string.Empty; return true;
    }

    private static string UnknownKind(string kind) =>
        $"No measured model/effort flag grammar is recorded for kind '{kind}'. Known values: "
        + string.Join(", ", AgentLaunchRecipeRegistry.RecordedModelFlagGrammars.Select(value => value.Kind).OrderBy(value => value, StringComparer.Ordinal))
        + ". Refusing to invent grammar.";

    private static int Unknown(string subcommand, TextWriter writer)
    {
        writer.WriteLine($"Unknown session-layer model-resolution subcommand '{subcommand}'.");
        writer.WriteLine(Usage);
        return 1;
    }

    private static void Emit<T>(TextWriter writer, string format, T result, string summary)
    {
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }
        writer.WriteLine("# Host-local model resolution (G685)");
        writer.WriteLine();
        writer.WriteLine($"- status: **{AgentModelResolutionGuidance.PreviewStatus}**");
        writer.WriteLine($"- {summary}");
        if (result is ModelResolutionQueryResult { LiveArgvFallback: { } fallback } query)
        {
            writer.WriteLine($"- next step: **{query.NextStep}**");
            writer.WriteLine($"- list same-kind seats: `{fallback.ListCommand}`");
            writer.WriteLine($"- selection: {fallback.Selection}");
            writer.WriteLine($"- inspect selected argv: `{fallback.InspectCommand}` → `{fallback.ArgvPath}`");
            writer.WriteLine($"- agreement: {fallback.AgreementRule}");
            writer.WriteLine($"- human fallback: {fallback.HumanFallback}");
        }
        writer.WriteLine($"- {AgentModelResolutionGuidance.NeverGuessRule}");
        writer.WriteLine("- provider operation: **none**");
    }

    private sealed record ParsedRecord(
        string? Kind = null,
        string? InformalName = null,
        string? Outcome = null,
        string? Invocation = null,
        string? Evidence = null,
        string? Error = null,
        bool Write = false,
        string Format = "markdown");

    private sealed record ParsedQuery(
        string? Kind = null,
        string? InformalName = null,
        string? CandidateInvocation = null,
        string Format = "markdown");
}
