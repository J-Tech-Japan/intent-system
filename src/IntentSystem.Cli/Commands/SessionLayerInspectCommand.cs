using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G790: read-only inspection of the operator-recorded session-layer topology
/// and the live herdr state at each recorded pane.  This surface deliberately
/// shares the topology reader and process-runner seam used by notify, while it
/// never prompts, sends keys, changes focus, or manages a process.
/// </summary>
internal static class SessionLayerInspectCommand
{
    internal const int MaximumTailLines = 200;

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string Usage =
        "Usage: intent-cli session-layer inspect --domain <d> --team <t> [--role <role>] "
        + "[--tail <lines>; maximum 200] [--herdr-executable <absolute-path>] "
        + "[--routing-root <host-root>] [--format markdown|json]";

    internal const string ObservationRule =
        "Terminal pane reading is permitted only for operational liveness diagnosis: determine whether a seat is alive or responding after an explicit operator or authorized orchestration diagnostic request. Terminal content is never parsed, promoted, or cited as canonical workflow evidence.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
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
            writer.WriteLine("Read-only recorded-topology and live-herdr observation; no focus default, prompt, or process management.");
            return 0;
        }

        if (!TryParseArguments(args, out var options, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }

        string routingRoot;
        try
        {
            routingRoot = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return EmitArgumentFailure(writer, options, $"--routing-root is invalid: {exception.Message}");
        }

        NotifyTopologyResolution topologyResolution;
        try
        {
            topologyResolution = NotifyRoleTopologyStore.Resolve(routingRoot, options.Domain, options.Team);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            topologyResolution = new NotifyTopologyResolution
            {
                Resolved = false,
                Cause = "topology-unavailable",
                Summary = $"Recorded session-layer topology could not be inspected: {exception.Message}",
            };
        }

        if (!topologyResolution.Resolved || topologyResolution.Topology is null)
        {
            // Inspection is an observation surface.  Missing or unreadable
            // session-layer state is useful output, not a command failure.
            var unavailable = new SessionLayerInspectResult
            {
                Domain = options.Domain,
                Team = options.Team,
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(options.Domain, options.Team),
                TopologyAvailable = false,
                LiveQueryAttempted = false,
                ObservationRule = ObservationRule,
                TailRequested = options.Tail,
                TailLimit = options.Tail is null ? null : Math.Min(options.Tail.Value, MaximumTailLines),
                Roles = [],
                UnavailableReason = topologyResolution.Cause ?? "topology-unavailable",
                Summary = topologyResolution.Summary,
            };
            Emit(writer, options.Format, unavailable);
            return 0;
        }

        var topology = topologyResolution.Topology;
        var selectedRoles = ResolveRoles(topology, options.Role, out var roleError);
        if (roleError is not null)
        {
            var refused = new SessionLayerInspectResult
            {
                Domain = options.Domain,
                Team = options.Team,
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(options.Domain, options.Team),
                TopologyAvailable = true,
                LiveQueryAttempted = false,
                ObservationRule = ObservationRule,
                TailRequested = options.Tail,
                TailLimit = options.Tail is null ? null : Math.Min(options.Tail.Value, MaximumTailLines),
                Roles = [],
                UnavailableReason = "unknown-role",
                Summary = roleError,
            };
            Emit(writer, options.Format, refused);
            return 1;
        }

        if (options.Tail is not null
            && selectedRoles.Count == 1
            && !string.Equals(selectedRoles[0].Record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
        {
            return EmitArgumentFailure(
                writer,
                options,
                $"--tail requires a herdr-resident role with a recorded pane; role '{selectedRoles[0].Role}' is external.");
        }

        var herdrRoles = selectedRoles
            .Where(role => string.Equals(role.Record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
            .ToArray();
        var agentObservation = herdrRoles.Length == 0
            ? AgentObservation.NotAttempted
            : ReadAgents(options.HerdrExecutable);

        var roleResults = selectedRoles
            .Select(role => BuildRoleResult(routingRoot, topology, role, options, agentObservation))
            .ToArray();

        var result = new SessionLayerInspectResult
        {
            Domain = options.Domain,
            Team = options.Team,
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(options.Domain, options.Team),
            TopologyAvailable = true,
            LiveQueryAttempted = herdrRoles.Length > 0,
            ObservationRule = ObservationRule,
            TailRequested = options.Tail,
            TailLimit = options.Tail is null ? null : Math.Min(options.Tail.Value, MaximumTailLines),
            Roles = roleResults,
            UnavailableReason = agentObservation.Available ? null : agentObservation.Error,
            Summary = BuildSummary(options, roleResults, agentObservation),
        };
        Emit(writer, options.Format, result);
        return 0;
    }

    private static IReadOnlyList<SelectedRole> ResolveRoles(
        NotifyTeamTopology topology,
        string? requestedRole,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(requestedRole))
        {
            return topology.Roles
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new SelectedRole(entry.Key, entry.Value))
                .ToArray();
        }

        var resolution = NotifyRoleTopologyStore.ResolveRecordedRole(topology, requestedRole);
        if (!resolution.Resolved || resolution.Record is null || resolution.RecordedRole is null)
        {
            error = resolution.Summary;
            return [];
        }

        return [new SelectedRole(resolution.RecordedRole, resolution.Record)];
    }

    private static SessionLayerInspectRoleResult BuildRoleResult(
        string routingRoot,
        NotifyTeamTopology topology,
        SelectedRole selected,
        InspectOptions options,
        AgentObservation agentObservation)
    {
        var record = selected.Record;
        var workspaceId = record.WorkspaceId ?? topology.WorkspaceId;
        var isHerdr = string.Equals(record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal);
        HerdrAgentState? matchingAgent = null;
        string? unavailableReason = null;

        if (isHerdr)
        {
            if (string.IsNullOrWhiteSpace(record.PaneId))
            {
                unavailableReason = "recorded-pane-missing";
            }
            else if (!agentObservation.Available)
            {
                unavailableReason = agentObservation.Error;
            }
            else
            {
                var matches = agentObservation.Agents
                    .Where(agent => string.Equals(agent.WorkspaceId, workspaceId, StringComparison.Ordinal)
                        && string.Equals(agent.PaneId, record.PaneId, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length == 1)
                {
                    matchingAgent = matches[0];
                }
                else if (matches.Length == 0)
                {
                    unavailableReason = $"no-agent-at-recorded-pane: workspace '{workspaceId}', pane '{record.PaneId}'.";
                }
                else
                {
                    unavailableReason = $"multiple-agents-at-recorded-pane: workspace '{workspaceId}', pane '{record.PaneId}'.";
                }
            }
        }

        IReadOnlyList<string>? tail = null;
        string? tailUnavailableReason = null;
        if (options.Tail is { } requestedTail && isHerdr && !string.IsNullOrWhiteSpace(record.PaneId))
        {
            if (!agentObservation.Available)
            {
                tailUnavailableReason = agentObservation.Error;
            }
            else if (requestedTail == 0)
            {
                tail = [];
            }
            else
            {
                var tailObservation = ReadPaneTail(
                    options.HerdrExecutable,
                    record.PaneId!,
                    Math.Min(requestedTail, MaximumTailLines));
                tail = tailObservation.Lines;
                tailUnavailableReason = tailObservation.Error;
            }
        }

        return new SessionLayerInspectRoleResult
        {
            Role = selected.Role,
            Resident = record.Resident,
            WorkspaceId = workspaceId,
            PaneId = record.PaneId,
            Reader = record.Reader,
            Kind = record.Kind,
            Frontend = record.Frontend,
            Live = matchingAgent is null ? null : CreateLiveState(matchingAgent),
            UnavailableReason = unavailableReason,
            Tail = tail,
            TailUnavailableReason = tailUnavailableReason,
        };
    }

    private static SessionLayerInspectLiveState CreateLiveState(HerdrAgentState agent) => new()
    {
        WorkspaceId = agent.WorkspaceId,
        PaneId = agent.PaneId,
        Agent = agent.Name,
        AgentRunning = agent.AgentRunning,
        AgentStatus = agent.AgentStatus,
        AgentKind = agent.AgentKind,
        Cwd = agent.Cwd,
        InteractiveReady = agent.InteractiveReady,
        AgentSessionPresent = agent.AgentSessionPresent,
        StateChangeSequence = agent.StateChangeSequence,
        LastStateChangeAt = agent.LastStateChangeAt,
    };

    private static AgentObservation ReadAgents(string? explicitExecutable)
    {
        var runner = NotifyCommand.ProcessRunnerFactory?.Invoke() ?? new NotifyProcessRunner();
        var executable = explicitExecutable
            ?? NotifyCommand.HerdrExecutableFactory?.Invoke()
            ?? NotifyTransportPaths.ResolveHerdrExecutable();

        NotifyProcessResult response;
        try
        {
            response = runner.Run(executable, ["agent", "list"]);
        }
        catch (Exception exception)
        {
            return AgentObservation.Failure($"herdr agent list unavailable: {exception.Message}");
        }

        if (response.ExitCode != 0)
        {
            return AgentObservation.Failure(
                $"herdr agent list failed with exit code {response.ExitCode}: {OneLine(response.StandardError, response.StandardOutput)}");
        }

        try
        {
            return AgentObservation.Success(HerdrNotifyTransport.ParseAgents(response.StandardOutput));
        }
        catch (Exception exception)
        {
            return AgentObservation.Failure($"herdr agent list response was unreadable: {exception.Message}");
        }
    }

    private static TailObservation ReadPaneTail(string? explicitExecutable, string paneId, int count)
    {
        var runner = NotifyCommand.ProcessRunnerFactory?.Invoke() ?? new NotifyProcessRunner();
        var executable = explicitExecutable
            ?? NotifyCommand.HerdrExecutableFactory?.Invoke()
            ?? NotifyTransportPaths.ResolveHerdrExecutable();

        NotifyProcessResult response;
        try
        {
            // The pane id is the explicit target. No focused/current pane form
            // is accepted, and the output is returned as observation only.
            response = runner.Run(executable, ["pane", "read", "--source", "recent-unwrapped", paneId]);
        }
        catch (Exception exception)
        {
            return TailObservation.Failure($"herdr pane read unavailable: {exception.Message}");
        }

        if (response.ExitCode != 0)
        {
            return TailObservation.Failure(
                $"herdr pane read failed with exit code {response.ExitCode}: {OneLine(response.StandardError, response.StandardOutput)}");
        }

        var lines = SplitLines(response.StandardOutput);
        var tail = lines.Count <= count
            ? lines
            : lines.Skip(lines.Count - count).ToArray();
        return new TailObservation(tail, null);
    }

    private static IReadOnlyList<string> SplitLines(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var normalized = output.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        return normalized.Split('\n').ToArray();
    }

    private static string BuildSummary(
        InspectOptions options,
        IReadOnlyList<SessionLayerInspectRoleResult> roles,
        AgentObservation observation)
    {
        var herdrCount = roles.Count(role => string.Equals(role.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal));
        var externalCount = roles.Count - herdrCount;
        var summary = $"Inspected {roles.Count.ToString(CultureInfo.InvariantCulture)} recorded role(s) "
            + $"({herdrCount.ToString(CultureInfo.InvariantCulture)} herdr, {externalCount.ToString(CultureInfo.InvariantCulture)} external) "
            + "without mutation or focus-default access.";
        if (herdrCount > 0 && !observation.Available)
        {
            summary += $" Live herdr state was unavailable: {observation.Error}";
        }
        if (options.Tail is { } tail)
        {
            summary += $" Pane tail requested={tail.ToString(CultureInfo.InvariantCulture)}, cap={Math.Min(tail, MaximumTailLines).ToString(CultureInfo.InvariantCulture)}.";
        }
        return summary;
    }

    private static int EmitArgumentFailure(TextWriter writer, InspectOptions options, string error)
    {
        if (string.Equals(options.Format, FormatJson, StringComparison.Ordinal))
        {
            var result = new SessionLayerInspectResult
            {
                Domain = options.Domain,
                Team = options.Team,
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(options.Domain, options.Team),
                TopologyAvailable = false,
                LiveQueryAttempted = false,
                ObservationRule = ObservationRule,
                TailRequested = options.Tail,
                TailLimit = options.Tail is null ? null : Math.Min(options.Tail.Value, MaximumTailLines),
                Roles = [],
                UnavailableReason = "invalid-arguments",
                Summary = error,
            };
            Emit(writer, options.Format, result);
        }
        else
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
        }
        return 1;
    }

    private static void Emit(TextWriter writer, string format, SessionLayerInspectResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# Session-layer inspect — {result.Domain} / {result.Team}");
        writer.WriteLine();
        writer.WriteLine($"- topology: {(result.TopologyAvailable ? "available" : "unavailable")}");
        writer.WriteLine($"- record: `{result.RecordPath}`");
        writer.WriteLine($"- live query attempted: {result.LiveQueryAttempted.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- observation rule: {result.ObservationRule}");
        if (result.TailRequested is { } tail)
        {
            writer.WriteLine($"- tail: requested={tail}; cap={result.TailLimit}");
        }
        if (result.UnavailableReason is not null)
        {
            writer.WriteLine($"- unavailable: {result.UnavailableReason}");
        }
        writer.WriteLine($"- {result.Summary}");
        foreach (var role in result.Roles)
        {
            writer.WriteLine();
            writer.WriteLine($"## {role.Role}");
            writer.WriteLine($"- recorded: resident={role.Resident}; workspace={role.WorkspaceId}; pane={role.PaneId ?? "absent"}; kind={role.Kind ?? "absent"}; frontend={role.Frontend ?? "absent"}; reader={role.Reader ?? "absent"}");
            if (role.Live is { } live)
            {
                writer.WriteLine($"- live: workspace={live.WorkspaceId ?? "absent"}; pane={live.PaneId ?? "absent"}; agent={live.Agent ?? "absent"}; running={live.AgentRunning.ToString().ToLowerInvariant()}; status={live.AgentStatus ?? "absent"}; kind={live.AgentKind ?? "absent"}; cwd={live.Cwd ?? "absent"}");
            }
            else if (role.Resident == NotifyRecordedRole.ExternalResident)
            {
                writer.WriteLine("- live: not-applicable (external recorded reader)");
            }
            else if (role.UnavailableReason is not null)
            {
                writer.WriteLine($"- live: unavailable ({role.UnavailableReason})");
            }
            if (role.Tail is not null)
            {
                writer.WriteLine("- tail:");
                foreach (var line in role.Tail) writer.WriteLine($"  {line}");
            }
            if (role.TailUnavailableReason is not null)
            {
                writer.WriteLine($"- tail unavailable: {role.TailUnavailableReason}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, out InspectOptions options, out string error)
    {
        string? domain = null;
        string? team = null;
        string? role = null;
        string? executable = null;
        string? routingRoot = null;
        int? tail = null;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument is "--domain" or "--team" or "--role" or "--tail" or "--herdr-executable" or "--routing-root" or "--format")
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    error = $"{argument} requires a value.";
                    options = default!;
                    return false;
                }

                var value = args[index].Trim();
                switch (argument)
                {
                    case "--domain": domain = value; break;
                    case "--team": team = value; break;
                    case "--role": role = value; break;
                    case "--herdr-executable":
                        if (!Path.IsPathRooted(value))
                        {
                            error = "--herdr-executable must be an absolute path.";
                            options = default!;
                            return false;
                        }
                        executable = value;
                        break;
                    case "--routing-root": routingRoot = value; break;
                    case "--format":
                        if (value is not (FormatJson or FormatMarkdown))
                        {
                            error = $"--format must be '{FormatJson}' or '{FormatMarkdown}'.";
                            options = default!;
                            return false;
                        }
                        format = value;
                        break;
                    case "--tail":
                        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTail)
                            || parsedTail < 0)
                        {
                            error = "--tail must be a non-negative integer.";
                            options = default!;
                            return false;
                        }
                        tail = parsedTail;
                        break;
                }
                continue;
            }

            error = $"Unknown argument '{argument}'.";
            options = default!;
            return false;
        }

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team))
        {
            error = "--domain and --team are required.";
            options = default!;
            return false;
        }

        if (tail is not null && string.IsNullOrWhiteSpace(role))
        {
            error = "--tail requires --role so the pane target is explicit.";
            options = default!;
            return false;
        }

        options = new InspectOptions(domain, team, role, tail, executable, routingRoot, format);
        return true;
    }

    private static string OneLine(params string[] values)
    {
        var value = values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "no detail";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record InspectOptions(
        string Domain,
        string Team,
        string? Role,
        int? Tail,
        string? HerdrExecutable,
        string? RoutingRoot,
        string Format);

    private sealed record SelectedRole(string Role, NotifyRecordedRole Record);

    private sealed record AgentObservation(bool Available, IReadOnlyList<HerdrAgentState> Agents, string? Error)
    {
        public static AgentObservation NotAttempted { get; } = new(true, [], null);

        public static AgentObservation Success(IReadOnlyList<HerdrAgentState> agents) => new(true, agents, null);

        public static AgentObservation Failure(string error) => new(false, [], error);
    }

    private sealed record TailObservation(IReadOnlyList<string> Lines, string? Error)
    {
        public static TailObservation Failure(string error) => new([], error);
    }
}

internal sealed record SessionLayerInspectResult
{
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string RecordPath { get; init; }
    public required bool TopologyAvailable { get; init; }
    public required bool LiveQueryAttempted { get; init; }
    public required string ObservationRule { get; init; }
    public int? TailRequested { get; init; }
    public int? TailLimit { get; init; }
    public required IReadOnlyList<SessionLayerInspectRoleResult> Roles { get; init; }
    public string? UnavailableReason { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerInspectRoleResult
{
    public required string Role { get; init; }
    public required string Resident { get; init; }
    public required string WorkspaceId { get; init; }
    public string? PaneId { get; init; }
    public string? Reader { get; init; }
    public string? Kind { get; init; }
    public string? Frontend { get; init; }
    public SessionLayerInspectLiveState? Live { get; init; }
    public string? UnavailableReason { get; init; }
    public IReadOnlyList<string>? Tail { get; init; }
    public string? TailUnavailableReason { get; init; }
}

internal sealed record SessionLayerInspectLiveState
{
    public string? WorkspaceId { get; init; }
    public string? PaneId { get; init; }
    public string? Agent { get; init; }
    public required bool AgentRunning { get; init; }
    public string? AgentStatus { get; init; }
    public string? AgentKind { get; init; }
    public string? Cwd { get; init; }
    public bool? InteractiveReady { get; init; }
    public bool AgentSessionPresent { get; init; }
    public long? StateChangeSequence { get; init; }
    public DateTimeOffset? LastStateChangeAt { get; init; }
}
