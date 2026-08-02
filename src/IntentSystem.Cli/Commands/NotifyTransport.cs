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
    public required bool Delivered { get; init; }

    public string? Cause { get; init; }

    public required string Summary { get; init; }
}

internal interface INotifyTransport
{
    NotifyDeliveryResult Deliver(
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload);
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
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload)
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
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload)
    {
        NotifyProcessResult agentList;
        try
        {
            agentList = runner.Run(executable, ["agent", "list"]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("transport-unavailable", exception.Message);
        }

        if (agentList.ExitCode != 0)
        {
            return Failure(
                "transport-failure",
                $"herdr logical-role lookup failed: {OneLine(agentList.StandardError, agentList.StandardOutput)}");
        }

        IReadOnlyDictionary<string, HerdrRoleState> roles;
        try
        {
            roles = ParseRoles(agentList.StandardOutput);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("transport-invalid-response", exception.Message);
        }

        foreach (var role in rolesToValidate.Distinct(StringComparer.Ordinal))
        {
            if (!roles.TryGetValue(role, out var state))
            {
                return Failure(
                    "unknown-role",
                    $"herdr logical role '{role}' is not present in the recorded agent mapping.");
            }

            if (string.IsNullOrWhiteSpace(state.PaneId))
            {
                return Failure("pane-absent", $"herdr logical role '{role}' has no mapped pane.");
            }

            if (!state.AgentRunning)
            {
                return Failure("agent-not-running", $"herdr logical role '{role}' has no running agent.");
            }
        }

        NotifyProcessResult delivery;
        try
        {
            delivery = runner.Run(executable, ["agent", "prompt", toRole, payload]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("transport-unavailable", exception.Message);
        }

        if (delivery.ExitCode == 0)
        {
            return new NotifyDeliveryResult
            {
                Delivered = true,
                Summary = $"Delivered notification to herdr logical role '{toRole}' in team '{team}'.",
            };
        }

        var detail = OneLine(delivery.StandardError, delivery.StandardOutput);
        var cause = ClassifyPromptFailure(detail);
        return Failure(cause, $"herdr delivery to logical role '{toRole}' failed: {detail}");
    }

    internal static IReadOnlyDictionary<string, HerdrRoleState> ParseRoles(string output)
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

                if (!roles.TryAdd(name, new HerdrRoleState(paneId, running)))
                {
                    throw new InvalidOperationException(
                        $"herdr agent list returned duplicate logical-role mapping '{name}'.");
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

internal sealed record HerdrRoleState(string? PaneId, bool AgentRunning);

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
