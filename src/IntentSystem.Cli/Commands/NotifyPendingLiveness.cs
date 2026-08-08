namespace IntentSystem.Cli.Commands;

internal sealed record NotifyPendingLivenessResult
{
    public const string RegistrationLostProcessPresent = "registration-lost-process-present";

    public required bool Resolved { get; init; }
    public bool? Running { get; init; }
    public string State { get; init; } = "unavailable";
    public bool? ProcessPresent { get; init; }
    public bool? ResendPermitted { get; init; }
    public required string Source { get; init; }
    public required string Summary { get; init; }
    public string? Cause { get; init; }
}

/// <summary>
/// Reads the same recipient liveness judgment used by <c>notify delegate</c>
/// without prompting, waiting, restarting, or otherwise acting on a process.
/// </summary>
internal static class NotifyPendingLiveness
{
    public static NotifyPendingLivenessResult Probe(
        string routingRoot,
        NotifyPendingDelegation record,
        string mode,
        INotifyProcessRunner runner,
        string herdrExecutable,
        string agmsgScriptsDirectory)
    {
        if (string.Equals(record.Resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal))
        {
            return new NotifyPendingLivenessResult
            {
                Resolved = true,
                Running = true,
                State = "live",
                Source = "external-reader",
                Summary = "The recipient is an external recorded reader; recorded reader deliverability is the live judgment and no process running flag applies.",
            };
        }

        return string.Equals(mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal)
            ? ProbeHerdr(record, runner, herdrExecutable)
            : ProbeAgmsg(record, runner, agmsgScriptsDirectory);
    }

    private static NotifyPendingLivenessResult ProbeHerdr(
        NotifyPendingDelegation record,
        INotifyProcessRunner runner,
        string executable)
    {
        if (string.IsNullOrWhiteSpace(record.WorkspaceId) || string.IsNullOrWhiteSpace(record.PaneId))
        {
            return Failure(
                "recipient-identity-missing",
                "The pending record does not carry the recorded herdr workspace and pane identity.");
        }

        NotifyProcessResult response;
        try
        {
            response = runner.Run(executable, ["agent", "list"]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("transport-unavailable", exception.Message);
        }

        if (response.ExitCode != 0)
        {
            return Failure(
                "transport-failure",
                $"herdr agent list failed while checking task '{record.TaskId}': {OneLine(response.StandardError, response.StandardOutput)}");
        }

        IReadOnlyList<HerdrAgentState> agents;
        try
        {
            agents = HerdrNotifyTransport.ParseAgents(response.StandardOutput);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("transport-invalid-response", exception.Message);
        }

        var atRecordedPane = agents
            .Where(agent => string.Equals(agent.WorkspaceId, record.WorkspaceId, StringComparison.Ordinal)
                && string.Equals(agent.PaneId, record.PaneId, StringComparison.Ordinal))
            .ToArray();
        var running = atRecordedPane.Where(agent => agent.AgentRunning).ToArray();
        if (running.Length > 1)
        {
            return Failure(
                "multiple-agents-at-pane",
                $"The recorded recipient identity '{record.RecipientIdentity}' has {running.Length} running agents; "
                + "the delegate liveness judgment requires exactly one.");
        }

        if (running.Length == 1)
        {
            return new NotifyPendingLivenessResult
            {
                Resolved = true,
                Running = true,
                State = "live",
                ProcessPresent = null,
                Source = "herdr.agent_running",
                Summary = $"The recorded recipient identity '{record.RecipientIdentity}' has one agent with running=true.",
            };
        }

        // A herdr registration is not process liveness.  Before declaring a
        // recipient lost, corroborate the exact recorded pane's foreground
        // process state.  An unregistered but present process is a distinct,
        // non-recovery state: no kill/start/register action is safe here.
        var processInfo = NotifyPaneProcessReader.Read(runner, executable, record.PaneId);
        if (!processInfo.Resolved)
        {
            return Failure(
                processInfo.Cause ?? "process-corroboration-unavailable",
                $"herdr reported no running registration for recorded recipient '{record.RecipientIdentity}', but process corroboration was unavailable: {processInfo.Summary}");
        }

        if (processInfo.Processes.Count > 0)
        {
            return new NotifyPendingLivenessResult
            {
                Resolved = true,
                Running = false,
                State = NotifyPendingLivenessResult.RegistrationLostProcessPresent,
                ProcessPresent = true,
                ResendPermitted = true,
                Source = "herdr.registration-and-process",
                Summary = $"The recorded recipient identity '{record.RecipientIdentity}' has no running herdr registration, but {processInfo.Processes.Count} foreground process(es) remain at the recorded pane '{record.PaneId}'. Registration is lost while the recipient is likely alive; re-register the agent at the recorded pane. No recovery action is safe.",
            };
        }

        return new NotifyPendingLivenessResult
        {
            Resolved = true,
            Running = false,
            State = "lost",
            ProcessPresent = false,
            Source = "herdr.agent_running+pane.process-info",
            Summary = $"The recorded recipient identity '{record.RecipientIdentity}' has no agent with running=true (status strings are ignored), no foreground process at the recorded pane, and is corroborated lost.",
        };
    }

    private static NotifyPendingLivenessResult ProbeAgmsg(
        NotifyPendingDelegation record,
        INotifyProcessRunner runner,
        string scriptsDirectory)
    {
        var teamScript = Path.Combine(scriptsDirectory, "team.sh");
        if (!File.Exists(teamScript))
        {
            return Failure(
                "transport-unavailable",
                $"agmsg team roster script was not found at '{scriptsDirectory}'.");
        }

        NotifyProcessResult response;
        try
        {
            response = runner.Run("bash", [teamScript, record.Team]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("transport-unavailable", exception.Message);
        }

        if (response.ExitCode != 0)
        {
            return Failure(
                "transport-failure",
                $"agmsg roster lookup failed while checking task '{record.TaskId}': {OneLine(response.StandardError, response.StandardOutput)}");
        }

        var registered = AgmsgNotifyTransport.ParseRoster(response.StandardOutput).Contains(record.RecipientRole);
        return new NotifyPendingLivenessResult
        {
            Resolved = true,
            Running = registered,
            State = registered ? "live" : "lost",
            Source = "agmsg.team_roster",
            Summary = registered
                ? $"The delegate roster still contains recipient role '{record.RecipientRole}'."
                : $"The delegate roster no longer contains recipient role '{record.RecipientRole}'.",
        };
    }

    private static NotifyPendingLivenessResult Failure(string cause, string summary) => new()
    {
        Resolved = false,
        Running = null,
        Source = "unavailable",
        Cause = cause,
        Summary = summary,
    };

    private static string OneLine(params string[] values)
    {
        var value = values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "no detail";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
