using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal interface INotifyProcessRunner
{
    NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments);
}

internal sealed class NotifyProcessRunner : INotifyProcessRunner
{
    public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start notification transport '{fileName}'.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new NotifyProcessResult(
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            throw new InvalidOperationException(
                $"Notification transport '{fileName}' could not start: {exception.Message}",
                exception);
        }
    }
}

internal sealed record NotifyDeliveryResult
{
    public required bool Resolved { get; init; }

    public required bool Delivered { get; init; }

    public string? Cause { get; init; }

    public string? ReaderPath { get; init; }

    public required string Summary { get; init; }
}

internal interface INotifyTransport
{
    NotifyDeliveryResult Deliver(
        string routingRoot,
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload,
        bool write);
}

internal sealed class AgmsgNotifyTransport : INotifyTransport
{
    private readonly INotifyProcessRunner runner;
    private readonly string scriptsDirectory;

    public AgmsgNotifyTransport(INotifyProcessRunner runner, string scriptsDirectory)
    {
        this.runner = runner;
        this.scriptsDirectory = scriptsDirectory;
    }

    public NotifyDeliveryResult Deliver(
        string routingRoot,
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload,
        bool write)
    {
        var teamScript = Path.Combine(scriptsDirectory, "team.sh");
        var sendScript = Path.Combine(scriptsDirectory, "send.sh");
        if (!File.Exists(teamScript) || !File.Exists(sendScript))
        {
            return Failure(
                "transport-unavailable",
                $"agmsg scripts were not found at '{scriptsDirectory}' (expected team.sh and send.sh).");
        }

        NotifyProcessResult roster;
        try
        {
            roster = runner.Run("bash", [teamScript, team]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("transport-unavailable", exception.Message);
        }

        if (roster.ExitCode != 0)
        {
            return Failure(
                "receiver-missing",
                $"agmsg roster lookup failed for team '{team}': {OneLine(roster.StandardError, roster.StandardOutput)}");
        }

        var rosterRoles = ParseRoster(roster.StandardOutput);
        foreach (var role in rolesToValidate.Distinct(StringComparer.Ordinal))
        {
            if (!rosterRoles.Contains(role))
            {
                return Failure(
                    "unknown-role",
                    $"agmsg role '{role}' is not registered in team '{team}' (registered: "
                    + $"{(rosterRoles.Count == 0 ? "none" : string.Join(", ", rosterRoles.OrderBy(value => value, StringComparer.Ordinal)))}).");
            }
        }

        if (!write)
        {
            return new NotifyDeliveryResult
            {
                Resolved = true,
                Delivered = false,
                Summary = $"Dry-run: would deliver notification to agmsg role '{toRole}' in team '{team}'.",
            };
        }

        NotifyProcessResult delivery;
        try
        {
            delivery = runner.Run("bash", [sendScript, team, fromRole, toRole, payload]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("transport-unavailable", exception.Message);
        }

        return delivery.ExitCode == 0
            ? new NotifyDeliveryResult
            {
                Resolved = true,
                Delivered = true,
                Summary = $"Delivered notification to agmsg role '{toRole}' in team '{team}'.",
            }
            : Failure(
                "transport-failure",
                $"agmsg delivery to role '{toRole}' in team '{team}' failed: "
                + OneLine(delivery.StandardError, delivery.StandardOutput));
    }

    internal static IReadOnlySet<string> ParseRoster(string output)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var typeStart = line.IndexOf(" (", StringComparison.Ordinal);
            if (typeStart > 0 && !line.StartsWith("Team:", StringComparison.Ordinal))
            {
                roles.Add(line[..typeStart]);
            }
        }

        return roles;
    }

    private static NotifyDeliveryResult Failure(string cause, string summary) => new()
    {
        Resolved = false,
        Delivered = false,
        Cause = cause,
        Summary = summary,
    };

    private static string OneLine(params string[] values)
    {
        var value = values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "no detail";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

internal sealed class HerdrNotifyTransport : INotifyTransport
{
    private readonly INotifyProcessRunner runner;
    private readonly string executable;

    public HerdrNotifyTransport(INotifyProcessRunner runner, string executable)
    {
        this.runner = runner;
        this.executable = executable;
    }

    public NotifyDeliveryResult Deliver(
        string routingRoot,
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload,
        bool write)
    {
        var topologyResolution = NotifyRoleTopologyStore.Resolve(routingRoot, team);
        if (!topologyResolution.Resolved)
        {
            return Failure(topologyResolution.Cause!, topologyResolution.Summary);
        }

        var topology = topologyResolution.Topology!;
        foreach (var role in rolesToValidate.Distinct(StringComparer.Ordinal))
        {
            if (!topology.Roles.ContainsKey(role))
            {
                return Failure(
                    "unknown-role",
                    $"Recorded role topology '{topology.SourcePath}' for team '{team}' workspace "
                    + $"'{topology.WorkspaceId}' does not contain logical role '{role}' (found in that team scope: "
                    + $"{FormatRoles(topology.Roles.Keys)}). Record that role for this team before retrying notify. "
                    + NotifyRoleTopologyStore.TopologyRemedy(team));
            }
        }

        var recipient = topology.Roles[toRole];
        var deliveryTarget = NotifyRoleTopologyStore.ResolveDeliveryTarget(routingRoot, topology, toRole);
        if (!deliveryTarget.Resolved)
        {
            return Failure(
                deliveryTarget.Cause!,
                $"{deliveryTarget.Summary} {NotifyRoleTopologyStore.TopologyRemedy(team)}");
        }

        if (string.Equals(recipient.Resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal))
        {
            return new NotifyDeliveryResult
            {
                Resolved = true,
                Delivered = false,
                ReaderPath = deliveryTarget.Target,
                Summary = write
                    ? $"Resolved external logical role '{toRole}' in team '{team}' to recorded reader "
                      + $"'{deliveryTarget.Target}'."
                    : $"Dry-run: would append notification for external logical role '{toRole}' in team '{team}' "
                      + $"to recorded reader '{deliveryTarget.Target}'.",
            };
        }

        NotifyProcessResult agentList;
        try
        {
            agentList = runner.Run(executable, ["agent", "list"]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(
                "transport-unavailable",
                $"{exception.Message} Verify the installed herdr executable and retry notify.");
        }

        if (agentList.ExitCode != 0)
        {
            return Failure(
                "transport-failure",
                $"herdr agent list lookup for team '{team}' workspace '{topology.WorkspaceId}' failed: "
                + $"{OneLine(agentList.StandardError, agentList.StandardOutput)} Inspect herdr agent list/API state "
                + "for that workspace and retry notify.");
        }

        IReadOnlyDictionary<string, HerdrRoleState> roles;
        try
        {
            roles = ParseRoles(agentList.StandardOutput, topology.WorkspaceId);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(
                "transport-invalid-response",
                $"{exception.Message} Inspect the installed herdr agent-list response schema and retry notify.");
        }

        if (!roles.TryGetValue(toRole, out var state))
        {
            return Failure(
                "unknown-role",
                $"herdr agent list for team '{team}' workspace '{topology.WorkspaceId}' does not contain logical "
                + $"role '{toRole}' (found in that workspace: {FormatRoles(roles.Keys)}). Verify the team's recorded "
                + "workspace and start the intended recipient there before retrying; agents in other workspaces are "
                + $"not eligible. {NotifyRoleTopologyStore.TopologyRemedy(team)}");
        }

        if (string.IsNullOrWhiteSpace(state.PaneId))
        {
            return Failure(
                "pane-absent",
                $"herdr agent list found logical role '{toRole}' in team '{team}' workspace "
                + $"'{topology.WorkspaceId}' without a pane. Re-provision the recorded recipient before retrying.");
        }

        if (!string.Equals(state.PaneId, deliveryTarget.Target, StringComparison.Ordinal))
        {
            return Failure(
                "pane-mismatch",
                $"herdr agent list found logical role '{toRole}' in team '{team}' workspace "
                + $"'{topology.WorkspaceId}' at pane '{state.PaneId ?? "none"}', but '{topology.SourcePath}' records "
                + $"pane '{deliveryTarget.Target}'. Refresh the recorded topology before retrying. "
                + NotifyRoleTopologyStore.TopologyRemedy(team));
        }

        if (!state.AgentRunning)
        {
            return Failure(
                "agent-not-running",
                $"herdr logical role '{toRole}' in team '{team}' workspace '{topology.WorkspaceId}' has no running "
                + "agent. Start and verify that recorded recipient before retrying.");
        }

        if (!write)
        {
            return new NotifyDeliveryResult
            {
                Resolved = true,
                Delivered = false,
                Summary = $"Dry-run: would deliver notification to herdr logical role '{toRole}' in team '{team}' "
                    + $"workspace '{topology.WorkspaceId}' at recorded pane '{recipient.PaneId}'.",
            };
        }

        NotifyProcessResult delivery;
        try
        {
            // A pane id is globally unique and is the explicit target recorded for this
            // team's workspace. Passing the logical name here would re-enter herdr's
            // global name namespace after we had just scoped validation to the team.
            delivery = runner.Run(executable, ["agent", "prompt", deliveryTarget.Target!, payload]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(
                "transport-unavailable",
                $"{exception.Message} Verify the installed herdr executable and recorded recipient pane, then "
                + "retry notify.");
        }

        if (delivery.ExitCode == 0)
        {
            return new NotifyDeliveryResult
            {
                Resolved = true,
                Delivered = true,
                Summary = $"Delivered notification to herdr logical role '{toRole}' in team '{team}' workspace "
                    + $"'{topology.WorkspaceId}' at recorded pane '{recipient.PaneId}'.",
            };
        }

        var detail = OneLine(delivery.StandardError, delivery.StandardOutput);
        var cause = ClassifyPromptFailure(detail);
        return Failure(
            cause,
            $"herdr delivery to logical role '{toRole}' in team '{team}' workspace '{topology.WorkspaceId}' failed: "
            + $"{detail} Inspect the recorded recipient pane and running-agent state, then retry notify.");
    }

    internal static IReadOnlyDictionary<string, HerdrRoleState> ParseRoles(string output, string workspaceId)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("result", out var result)
                || !result.TryGetProperty("agents", out var agents)
                || agents.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("herdr agent list response did not contain result.agents.");
            }

            var roles = new Dictionary<string, HerdrRoleState>(StringComparer.Ordinal);
            foreach (var agent in agents.EnumerateArray())
            {
                var name = ReadString(agent, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var paneId = ReadString(agent, "pane_id");
                var agentWorkspaceId = ReadString(agent, "workspace_id") ?? WorkspaceFromPane(paneId);
                if (!string.Equals(agentWorkspaceId, workspaceId, StringComparison.Ordinal))
                {
                    continue;
                }

                var agentKind = ReadString(agent, "agent");
                var status = ReadString(agent, "agent_status");
                var explicitlyNotReady = agent.TryGetProperty("interactive_ready", out var ready)
                    && ready.ValueKind == JsonValueKind.False;
                var hasSession = agent.TryGetProperty("agent_session", out var session)
                    && session.ValueKind == JsonValueKind.Object;
                var running = !string.IsNullOrWhiteSpace(agentKind)
                    && hasSession
                    && !explicitlyNotReady
                    && !string.Equals(status, "unknown", StringComparison.Ordinal);

                if (!roles.TryAdd(name, new HerdrRoleState(agentWorkspaceId, paneId, running)))
                {
                    throw new InvalidOperationException(
                        $"herdr agent list returned duplicate logical role '{name}' in workspace '{workspaceId}'. "
                        + "Rename or remove the competing agent before retrying.");
                }
            }

            return roles;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"herdr agent list returned invalid JSON: {exception.Message}",
                exception);
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? WorkspaceFromPane(string? paneId)
    {
        var separator = paneId?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return separator > 0 ? paneId![..separator] : null;
    }

    private static string FormatRoles(IEnumerable<string> roles)
    {
        var ordered = roles.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return ordered.Length == 0 ? "none" : string.Join(", ", ordered);
    }

    private static string ClassifyPromptFailure(string detail)
    {
        if (detail.Contains("pane", StringComparison.OrdinalIgnoreCase)
            && (detail.Contains("absent", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("not found", StringComparison.OrdinalIgnoreCase)))
        {
            return "pane-absent";
        }

        if (detail.Contains("agent", StringComparison.OrdinalIgnoreCase)
            && (detail.Contains("not running", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("undetected", StringComparison.OrdinalIgnoreCase)))
        {
            return "agent-not-running";
        }

        if (detail.Contains("receiver", StringComparison.OrdinalIgnoreCase)
            && detail.Contains("missing", StringComparison.OrdinalIgnoreCase))
        {
            return "receiver-missing";
        }

        return "transport-failure";
    }

    private static NotifyDeliveryResult Failure(string cause, string summary) => new()
    {
        Resolved = false,
        Delivered = false,
        Cause = cause,
        Summary = summary,
    };

    private static string OneLine(params string[] values)
    {
        var value = values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "no detail";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

internal sealed record HerdrRoleState(string? WorkspaceId, string? PaneId, bool AgentRunning);

internal static class NotifyTransportPaths
{
    public const string AgmsgScriptsEnvironmentVariable = "INTENT_CLI_AGMSG_SCRIPTS";
    public const string HerdrExecutableEnvironmentVariable = "INTENT_CLI_HERDR_EXECUTABLE";

    public static string ResolveAgmsgScriptsDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(AgmsgScriptsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".agents", "skills", "agmsg", "scripts");
    }

    public static string ResolveHerdrExecutable()
    {
        var configured = Environment.GetEnvironmentVariable(HerdrExecutableEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured) ? "herdr" : configured;
    }
}
