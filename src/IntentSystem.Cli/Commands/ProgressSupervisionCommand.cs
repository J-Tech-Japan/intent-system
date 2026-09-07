using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read/write command-owned progress evaluation.  It is deliberately
/// deterministic and event-first: no process, pane, shell, or model is
/// started by this command.  A host adapter may consume the emitted action.
/// </summary>
internal static class ProgressSupervisionCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    public const string Operation = "progress-supervision";
    public const string Usage = "Usage: intent-cli automation progress-supervision --domain <d> --team <t> --unit <u> [--evidence-file <path>] [--routing-root <root>] [--write] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var options, out var error))
        {
            Emit(writer, new { operation = Operation, state = "refused", error, usage = Usage }, FormatFrom(args));
            return 1;
        }

        var routingRoot = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot);
        var evidence = ReadEvidence(options, routingRoot, out var sourceFailures);
        if (evidence is null)
        {
            evidence = UnknownEvidence(options);
            sourceFailures = [.. sourceFailures, "evidence-not-found"];
        }
        else if (sourceFailures.Count > 0)
        {
            evidence = evidence with { SourceFailures = sourceFailures };
        }

        if (!string.Equals(evidence.Domain, options.Domain, StringComparison.Ordinal)
            || !string.Equals(evidence.Team, options.Team, StringComparison.Ordinal)
            || !string.Equals(evidence.Unit, options.Unit, StringComparison.Ordinal))
        {
            sourceFailures = [.. sourceFailures, "evidence-identity-mismatch"];
            evidence = evidence with { SourceFailures = sourceFailures };
        }

        var controllerOptions = new ProgressSupervisionOptions
        {
            DetectionFloorSeconds = options.FloorSeconds,
            JitterSeconds = options.JitterSeconds,
            MaxSweepSeconds = options.MaxSweepSeconds,
            RecoveryBackoffSeconds = options.RecoveryBackoffSeconds,
            MaxRecoveryAttempts = options.MaxRecoveryAttempts,
        };
        var qualified = ProgressSupervisionConstants.IsQualified(controllerOptions, out var qualification);
        var controller = new ProgressSupervisionController(controllerOptions);
        var now = options.Now ?? DateTimeOffset.UtcNow;
        var evaluation = controller.Evaluate(evidence, now);
        if (!qualified)
        {
            evaluation = evaluation with
            {
                State = "unknown",
                Healthy = false,
                Unknown = true,
                Action = "infrastructure-escalation",
                NextAction = "mitigate configuration and remeasure; do not widen the bound",
                MissingEvidence = [.. evaluation.MissingEvidence, "unqualified-detection-bound"],
                Summary = $"Cadence is unqualified: {qualification}.",
            };
        }

        if (options.Write)
        {
            ProgressSupervisionStore.WriteEvidence(routingRoot, evidence with
            {
                SemanticFingerprint = evidence.SemanticFingerprint is "" or "unknown"
                    ? evidence.StableFingerprint()
                    : evidence.SemanticFingerprint,
            });
        }

        var result = new ProgressSupervisionCommandResult
        {
            Operation = Operation,
            Mode = options.Write ? "write" : "read-only",
            RoutingRoot = routingRoot,
            Evidence = evidence,
            Evaluation = evaluation,
            Qualification = qualification,
            SourceFailures = sourceFailures,
            CommandsExecuted = options.Write ? "append evidence record only; no process, pane, shell, or model operation" : "none",
            Summary = evaluation.Summary,
        };
        Emit(writer, result, options.Format);
        return evaluation.State == "unknown" && options.RequireQualified ? 2 : 0;
    }

    private static ProgressSupervisionEvidence? ReadEvidence(
        CommandOptions options,
        string routingRoot,
        out IReadOnlyList<string> failures)
    {
        var errors = new List<string>();
        ProgressSupervisionEvidence? evidence = null;
        if (!string.IsNullOrWhiteSpace(options.EvidenceFile))
        {
            try
            {
                evidence = JsonSerializer.Deserialize<ProgressSupervisionEvidence>(File.ReadAllText(options.EvidenceFile!), JsonOptions);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                errors.Add($"evidence-file:{exception.Message}");
            }
        }
        else
        {
            evidence = ProgressSupervisionStore.ReadLatest(routingRoot, options.Domain, options.Team, options.Unit, out var storedFailures);
            errors.AddRange(storedFailures);
        }

        failures = errors;
        return evidence;
    }

    private static ProgressSupervisionEvidence UnknownEvidence(CommandOptions options) => new()
    {
        Domain = options.Domain,
        Team = options.Team,
        Project = options.Project ?? "unknown",
        Repo = options.Repo ?? "unknown",
        Unit = options.Unit,
        Phase = options.Phase ?? "unknown",
        Episode = options.Episode ?? "unknown",
        ObservedAt = options.Now ?? DateTimeOffset.UtcNow,
        EligibleAt = options.Now ?? DateTimeOffset.UtcNow,
        FreshnessAt = null,
        SourceFailures = ["evidence-not-found"],
    };

    private static void Emit(TextWriter writer, object payload, string format)
    {
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        writer.WriteLine("# Progress supervision (G812)");
        writer.WriteLine();
        writer.WriteLine("- mode: `" + (payload switch
        {
            ProgressSupervisionCommandResult result => result.Mode,
            _ => "read-only",
        }) + "`");
        writer.WriteLine("- operation: `progress-supervision`");
        writer.WriteLine("- no provider/model/pane/process operation is executed by this command");
        writer.WriteLine("- canonical route: `intent-cli automation progress-supervision --domain <d> --team <t> --unit <u> --format json`");
        writer.WriteLine();
        writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static string FormatFrom(string[] args) => args.SkipWhile(a => a != "--format").Skip(1).FirstOrDefault() ?? FormatMarkdown;

    private static bool TryParse(string[] args, out CommandOptions options, out string error)
    {
        string? domain = null, team = null, project = null, repo = null, unit = null, phase = null, episode = null, evidenceFile = null, routingRoot = null;
        var format = FormatMarkdown;
        var write = false;
        var now = (DateTimeOffset?)null;
        var floor = ProgressSupervisionConstants.DefaultFloorSeconds;
        var jitter = 0;
        var sweep = 0d;
        var backoff = ProgressSupervisionConstants.RecoveryBackoffSeconds;
        var maxAttempts = ProgressSupervisionConstants.MaxRecoveryAttempts;
        var requireQualified = true;
        error = string.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--write") { write = true; continue; }
            if (arg == "--allow-unqualified") { requireQualified = false; continue; }
            if (!TryValue(args, ref i, arg, out var value))
            {
                error = $"{arg} requires a value";
                options = new CommandOptions();
                return false;
            }
            switch (arg)
            {
                case "--domain": domain = value; break;
                case "--team": team = value; break;
                case "--project": project = value; break;
                case "--repo": repo = value; break;
                case "--unit": unit = value; break;
                case "--phase": phase = value; break;
                case "--episode": episode = value; break;
                case "--evidence-file": evidenceFile = value; break;
                case "--routing-root": routingRoot = value; break;
                case "--format": format = value is FormatJson or FormatMarkdown ? value : ""; break;
                case "--now": if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) { error = "--now must be ISO-8601"; options = new CommandOptions(); return false; } now = parsed; break;
                case "--floor-seconds": if (!int.TryParse(value, CultureInfo.InvariantCulture, out floor)) { error = "--floor-seconds must be an integer"; options = new CommandOptions(); return false; } break;
                case "--jitter-seconds": if (!int.TryParse(value, CultureInfo.InvariantCulture, out jitter)) { error = "--jitter-seconds must be an integer"; options = new CommandOptions(); return false; } break;
                case "--max-sweep-seconds": if (!double.TryParse(value, CultureInfo.InvariantCulture, out sweep)) { error = "--max-sweep-seconds must be numeric"; options = new CommandOptions(); return false; } break;
                case "--recovery-backoff-seconds": if (!int.TryParse(value, CultureInfo.InvariantCulture, out backoff)) { error = "--recovery-backoff-seconds must be an integer"; options = new CommandOptions(); return false; } break;
                case "--max-recovery-attempts": if (!int.TryParse(value, CultureInfo.InvariantCulture, out maxAttempts)) { error = "--max-recovery-attempts must be an integer"; options = new CommandOptions(); return false; } break;
                default: error = $"unknown option '{arg}'"; options = new CommandOptions(); return false;
            }
        }
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(unit))
        {
            error = "--domain, --team and --unit are required";
            options = new CommandOptions();
            return false;
        }
        if (string.IsNullOrEmpty(format)) { error = "--format must be markdown or json"; options = new CommandOptions(); return false; }
        options = new CommandOptions
        {
            Domain = domain,
            Team = team,
            Project = project,
            Repo = repo,
            Unit = unit,
            Phase = phase,
            Episode = episode,
            EvidenceFile = evidenceFile,
            RoutingRoot = routingRoot,
            Format = format,
            Write = write,
            Now = now,
            FloorSeconds = floor,
            JitterSeconds = jitter,
            MaxSweepSeconds = sweep,
            RecoveryBackoffSeconds = backoff,
            MaxRecoveryAttempts = maxAttempts,
            RequireQualified = requireQualified,
        };
        return true;
    }

    private static bool TryValue(string[] args, ref int index, string argument, out string value)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            value = string.Empty;
            return false;
        }
        if (argument is "--write" or "--allow-unqualified")
        {
            value = string.Empty;
            return true;
        }
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = string.Empty;
            return false;
        }
        value = args[++index];
        return true;
    }

    private sealed record CommandOptions
    {
        public string Domain { get; init; } = string.Empty;
        public string Team { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public string? Project { get; init; }
        public string? Repo { get; init; }
        public string? Phase { get; init; }
        public string? Episode { get; init; }
        public string? EvidenceFile { get; init; }
        public string? RoutingRoot { get; init; }
        public string Format { get; init; } = FormatMarkdown;
        public bool Write { get; init; }
        public DateTimeOffset? Now { get; init; }
        public int FloorSeconds { get; init; } = ProgressSupervisionConstants.DefaultFloorSeconds;
        public int JitterSeconds { get; init; }
        public double MaxSweepSeconds { get; init; }
        public int RecoveryBackoffSeconds { get; init; } = ProgressSupervisionConstants.RecoveryBackoffSeconds;
        public int MaxRecoveryAttempts { get; init; } = ProgressSupervisionConstants.MaxRecoveryAttempts;
        public bool RequireQualified { get; init; } = true;
    }

    private sealed record ProgressSupervisionCommandResult
    {
        [JsonPropertyName("operation")] public required string Operation { get; init; }
        [JsonPropertyName("mode")] public required string Mode { get; init; }
        [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
        [JsonPropertyName("evidence")] public required ProgressSupervisionEvidence Evidence { get; init; }
        [JsonPropertyName("evaluation")] public required ProgressSupervisionEvaluation Evaluation { get; init; }
        [JsonPropertyName("qualification")] public required string Qualification { get; init; }
        [JsonPropertyName("source_failures")] public IReadOnlyList<string> SourceFailures { get; init; } = [];
        [JsonPropertyName("commands_executed")] public required string CommandsExecuted { get; init; }
        [JsonPropertyName("summary")] public required string Summary { get; init; }
    }
}

internal static class NotifyProgressSupervisionCommand
{
    public const string Operation = "progress";
    public static int Execute(CliContext context, string[] args, TextWriter writer) => ProgressSupervisionCommand.Execute(context, args, writer);
}
