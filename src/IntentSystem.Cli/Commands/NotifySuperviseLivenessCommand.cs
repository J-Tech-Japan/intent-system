using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G765: a read-only observer of the persisted supervision state. This is
/// intentionally separate from <see cref="NotifyMeasuredSupervisor"/> so an
/// absent supervisor can still be diagnosed by another operator-facing
/// command.
/// </summary>
internal static class NotifySuperviseLivenessCommand
{
    public const string Operation = "liveness";
    public const string Usage =
        "Usage: intent-cli notify supervise liveness --domain <d> --team <t> "
        + "[--routing-root <host-root>] [--format markdown|json]";

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

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

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var options, out var error))
        {
            EmitFailure(writer, error, options?.Format ?? FormatMarkdown);
            return 1;
        }

        string artifactRoot;
        try
        {
            _ = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot);
            artifactRoot = context.ResolveSupervisionArtifactRootPath();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            EmitFailure(writer, $"invalid-routing-root: {exception.Message}", options.Format);
            return 1;
        }

        var state = NotifySupervisionStore.Read(artifactRoot, options.Domain, options.Team);
        if (!state.Resolved)
        {
            EmitFailure(writer, state.Error ?? "supervision-state-unreadable", options.Format);
            return 1;
        }

        var now = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var lastCycle = state.LastIntervalCycle ?? state.LastCycle;
        var elapsedSeconds = lastCycle is { } cycle
            ? (long?)Math.Max(0, (long)(now - cycle.CompletedAt).TotalSeconds)
            : null;
        var boundSeconds = state.Bound?.BoundSeconds;
        var absentSinceLastCycle = lastCycle is null
            || boundSeconds is { } bound && elapsedSeconds is { } elapsed && elapsed > bound;

        var schedulerLabel = $"intent-cli.supervise.{options.Domain}.{options.Team}";
        var artifacts = NotifySuperviseArtifactInventory.FindManagedArtifacts(
            artifactRoot,
            schedulerLabel,
            NotifySuperviseArtifactInventory.UserProfileDirectoryFactory());
        var installedArtifactPresent = state.InstalledSupervisor is { ArtifactPath: { } path }
            && File.Exists(path);
        var schedulerJobLoaded = state.InstalledSupervisor is not null && installedArtifactPresent;
        var schedulerEvidence = schedulerJobLoaded
            ? "installed first-cycle record and scheduler artifact are both present; no OS lifecycle query was executed"
            : "no installed first-cycle record with a present scheduler artifact; no OS lifecycle query was executed";

        var result = new NotifySuperviseLivenessResult
        {
            Operation = "supervise-liveness",
            RoutingRoot = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot),
            Domain = options.Domain,
            Team = options.Team,
            CommandMode = "read-only",
            LastCompletedCycleAt = lastCycle?.CompletedAt,
            DeclaredBoundSeconds = boundSeconds,
            ElapsedSeconds = elapsedSeconds,
            AbsentSinceLastCycle = absentSinceLastCycle,
            SchedulerJobLoaded = schedulerJobLoaded,
            SchedulerJobEvidence = schedulerEvidence,
            SchedulerArtifactPaths = artifacts,
            CommandsExecuted = "none (persisted supervision state and artifact metadata only)",
            Summary = BuildSummary(lastCycle, boundSeconds, elapsedSeconds, schedulerJobLoaded),
        };

        Emit(writer, result, options.Format);
        return 0;
    }

    private static string BuildSummary(
        NotifySupervisionCycle? lastCycle,
        int? boundSeconds,
        long? elapsedSeconds,
        bool schedulerJobLoaded)
    {
        var cycleSummary = lastCycle is null
            ? "No completed supervision cycle is recorded; no supervisor process is required to produce this answer."
            : $"The last completed supervision cycle was {elapsedSeconds}s ago."
                + (boundSeconds is { } bound
                    ? $" The declared detection bound is {bound}s."
                    : " No detection bound was declared.");
        var absenceSummary = lastCycle is null || boundSeconds is { } declared && elapsedSeconds is { } elapsed && elapsed > declared
            ? " Supervision is absent or beyond its declared bound."
            : " Supervision liveness is within the available evidence.";
        return $"Read-only liveness: {cycleSummary}{absenceSummary} Scheduler job loaded={schedulerJobLoaded.ToString().ToLowerInvariant()} based on durable installation evidence; the supervisor was not run.";
    }

    private static void EmitFailure(TextWriter writer, string error, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                operation = "supervise-liveness",
                command_mode = "read-only",
                success = false,
                error,
                commands_executed = "none",
            }, JsonOptions));
            return;
        }

        writer.WriteLine($"supervise-liveness-failed: {error}");
    }

    private static void Emit(TextWriter writer, NotifySuperviseLivenessResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine("# notify supervise liveness");
        writer.WriteLine();
        writer.WriteLine($"- domain/team: {result.Domain}/{result.Team}");
        writer.WriteLine($"- command mode: {result.CommandMode}");
        writer.WriteLine($"- last completed cycle: {result.LastCompletedCycleAt?.ToString("O") ?? "<none>"}");
        writer.WriteLine($"- declared bound: {result.DeclaredBoundSeconds?.ToString(CultureInfo.InvariantCulture) ?? "<unrecorded>"}s");
        writer.WriteLine($"- elapsed: {result.ElapsedSeconds?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}s");
        writer.WriteLine($"- absent since last cycle: {result.AbsentSinceLastCycle.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- scheduler job loaded: {result.SchedulerJobLoaded.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- scheduler evidence: {result.SchedulerJobEvidence}");
        writer.WriteLine($"- commands executed: {result.CommandsExecuted}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private static bool TryParse(string[] args, out LivenessOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        string? routingRoot = null;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!ReadValue(args, ref index, "--domain", out domain, out error)) return Fail(out options);
                    break;
                case "--team":
                    if (!ReadValue(args, ref index, "--team", out team, out error)) return Fail(out options);
                    break;
                case "--routing-root":
                    if (!ReadValue(args, ref index, "--routing-root", out routingRoot, out error)) return Fail(out options);
                    break;
                case "--format":
                    if (!ReadValue(args, ref index, "--format", out format, out error)) return Fail(out options);
                    if (format is not FormatJson and not FormatMarkdown)
                    {
                        error = "--format must be markdown or json.";
                        return Fail(out options);
                    }
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return Fail(out options);
            }
        }

        if (!IsSafeIdentity(domain) || !IsSafeIdentity(team))
        {
            error = "--domain and --team are required safe identity values.";
            return Fail(out options);
        }

        options = new LivenessOptions
        {
            Domain = domain!,
            Team = team!,
            RoutingRoot = routingRoot,
            Format = format!,
        };
        return true;
    }

    private static bool ReadValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static bool IsSafeIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private static bool Fail(out LivenessOptions options)
    {
        options = null!;
        return false;
    }

    private sealed record LivenessOptions
    {
        public required string Domain { get; init; }
        public required string Team { get; init; }
        public string? RoutingRoot { get; init; }
        public required string Format { get; init; }
    }
}

internal sealed record NotifySuperviseLivenessResult
{
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("command_mode")] public required string CommandMode { get; init; }
    [JsonPropertyName("last_completed_cycle_at")] public DateTimeOffset? LastCompletedCycleAt { get; init; }
    [JsonPropertyName("declared_bound_seconds")] public int? DeclaredBoundSeconds { get; init; }
    [JsonPropertyName("elapsed_seconds")] public long? ElapsedSeconds { get; init; }
    [JsonPropertyName("absent_since_last_cycle")] public required bool AbsentSinceLastCycle { get; init; }
    [JsonPropertyName("scheduler_job_loaded")] public required bool SchedulerJobLoaded { get; init; }
    [JsonPropertyName("scheduler_job_evidence")] public required string SchedulerJobEvidence { get; init; }
    [JsonPropertyName("scheduler_artifact_paths")] public required IReadOnlyList<string> SchedulerArtifactPaths { get; init; }
    [JsonPropertyName("commands_executed")] public required string CommandsExecuted { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}
