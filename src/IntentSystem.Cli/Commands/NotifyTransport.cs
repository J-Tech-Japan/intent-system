using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal interface INotifyProcessRunner
{
    NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments);

    Task<NotifyProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        Task.Run(() => Run(fileName, arguments), cancellationToken);
}

internal sealed class NotifyProcessRunner : INotifyProcessRunner
{
    public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = CreateStartInfo(fileName, arguments);

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

    public async Task<NotifyProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(fileName, arguments);
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start notification transport '{fileName}'.");
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                throw;
            }

            return new NotifyProcessResult(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            throw new InvalidOperationException(
                $"Notification transport '{fileName}' could not start: {exception.Message}",
                exception);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }
}

internal sealed record NotifyDeliveryResult
{
    public required bool Resolved { get; init; }

    public required bool Delivered { get; init; }

    public string? Cause { get; init; }

    public string? ReaderPath { get; init; }

    public string? ReceiverStateOutcome { get; init; }

    public string? WorkingTransition { get; init; }

    public string? SettleOutcome { get; init; }

    public bool? ResendPermitted { get; init; }

    public SessionLayerPreflightPhaseResult? ActivePhase { get; init; }

    /// <summary>G638: report delivery may proceed with an advisory seat-liveness warning.</summary>
    public NotifyRecipientWarning? RecipientWarning { get; init; }

    public required string Summary { get; init; }
}

internal interface INotifyTransport
{
    NotifyDeliveryResult Deliver(
        string routingRoot,
        string domain,
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload,
        bool write,
        bool allowStoppedRecipient = false);
}

internal sealed class AgmsgNotifyTransport : INotifyTransport
{
    private readonly INotifyProcessRunner runner;
    private readonly string scriptsDirectory;
    private readonly string bashExecutable;

    public AgmsgNotifyTransport(
        INotifyProcessRunner runner,
        string scriptsDirectory,
        string? bashExecutable = null)
    {
        this.runner = runner;
        this.scriptsDirectory = scriptsDirectory;
        this.bashExecutable = bashExecutable ?? "bash";
    }

    public NotifyDeliveryResult Deliver(
        string routingRoot,
        string domain,
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload,
        bool write,
        bool allowStoppedRecipient = false)
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
            roster = runner.Run(bashExecutable, [teamScript, team]);
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
                ActivePhase = Skipped("Dry-run resolved the recorded agmsg roster without contacting the recipient."),
                Summary = $"Dry-run: would deliver notification to agmsg role '{toRole}' in team '{team}'.",
            };
        }

        NotifyProcessResult delivery;
        try
        {
            delivery = runner.Run(bashExecutable, [sendScript, team, fromRole, toRole, payload]);
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
                ActivePhase = new SessionLayerPreflightPhaseResult
                {
                    Status = SessionLayerPreflight.ActiveAcknowledged,
                    Checked = true,
                    ContactedReceiver = true,
                    Summary = "The recorded agmsg transport acknowledged the bounded send operation.",
                },
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
        ActivePhase = Skipped("Active receiver delivery did not start because recorded-route resolution failed."),
        Summary = summary,
    };

    private static SessionLayerPreflightPhaseResult Skipped(string summary) => new()
    {
        Status = SessionLayerPreflight.ActiveSkipped,
        Checked = false,
        ContactedReceiver = false,
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
    private const string BoundedPromptTimeoutMilliseconds = "10000";
    private readonly INotifyProcessRunner runner;
    private readonly string executable;

    public HerdrNotifyTransport(INotifyProcessRunner runner, string executable)
    {
        this.runner = runner;
        this.executable = executable;
    }

    public NotifyDeliveryResult Deliver(
        string routingRoot,
        string domain,
        string team,
        string fromRole,
        string toRole,
        IReadOnlyList<string> rolesToValidate,
        string payload,
        bool write,
        bool allowStoppedRecipient = false)
    {
        var topologyResolution = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (!topologyResolution.Resolved)
        {
            return Failure(topologyResolution.Cause!, topologyResolution.Summary);
        }

        var topology = topologyResolution.Topology!;
        foreach (var role in rolesToValidate.Distinct(StringComparer.Ordinal))
        {
            var roleResolution = NotifyRoleTopologyStore.ResolveRecordedRole(topology, role);
            if (!roleResolution.Resolved)
            {
                return Failure(
                    roleResolution.Cause ?? "unknown-role",
                    roleResolution.Summary + " " + NotifyRoleTopologyStore.TopologyRemedy(team));
            }
        }

        var recipientResolution = NotifyRoleTopologyStore.ResolveRecordedRole(topology, toRole);
        if (!recipientResolution.Resolved || recipientResolution.Record is not { } recipient)
        {
            return Failure(
                recipientResolution.Cause ?? "unknown-role",
                recipientResolution.Summary + " " + NotifyRoleTopologyStore.TopologyRemedy(team));
        }

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
                ActivePhase = new SessionLayerPreflightPhaseResult
                {
                    Status = SessionLayerPreflight.ActiveNotApplicable,
                    Checked = false,
                    ContactedReceiver = false,
                    Summary = "The recipient is an external recorded reader; pane working-state observation does not apply.",
                },
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

        IReadOnlyList<HerdrAgentState> agents;
        try
        {
            agents = ParseAgents(agentList.StandardOutput);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(
                "transport-invalid-response",
                $"{exception.Message} Inspect the installed herdr agent-list response schema and retry notify.");
        }

        var recordedPane = deliveryTarget.Target!;
        var atRecordedPane = agents
            .Where(agent => string.Equals(agent.WorkspaceId, topology.WorkspaceId, StringComparison.Ordinal)
                && string.Equals(agent.PaneId, recordedPane, StringComparison.Ordinal))
            .ToArray();
        var runningAtRecordedPane = atRecordedPane.Where(agent => agent.AgentRunning).ToArray();
        if (runningAtRecordedPane.Length == 0)
        {
            var foreignWorkspaceAgents = agents
                .Where(agent => string.Equals(agent.PaneId, recordedPane, StringComparison.Ordinal)
                    && !string.Equals(agent.WorkspaceId, topology.WorkspaceId, StringComparison.Ordinal))
                .ToArray();
            if (foreignWorkspaceAgents.Length > 0)
            {
                return Failure(
                    "pane-foreign-workspace",
                    $"Team '{team}' records workspace '{topology.WorkspaceId}' and pane '{recordedPane}' for logical "
                    + $"role '{toRole}', but that pane was reported only under foreign workspace(s): "
                    + $"{FormatAgents(foreignWorkspaceAgents)}. Agent names are diagnostic only and are never a "
                    + "routing fallback. Correct the recorded workspace/pane or re-provision the intended recipient.");
            }

            // A stopped/undetected herdr registration is not proof that the
            // recipient process is gone. Corroborate the exact recorded pane
            // before choosing any absence outcome or the stopped-seat report
            // exception.
            var processInfo = NotifyPaneProcessReader.Read(runner, executable, recordedPane);
            if (!processInfo.Resolved)
            {
                return Failure(
                    processInfo.Cause ?? "process-corroboration-unavailable",
                    $"herdr reported no running registration for logical role '{toRole}' at recorded pane '{recordedPane}', but process corroboration was unavailable: {processInfo.Summary}");
            }

            if (processInfo.Processes.Count > 0)
            {
                return RegistrationLostProcessPresent(
                    team,
                    toRole,
                    topology.WorkspaceId,
                    recordedPane,
                    processInfo.Processes.Count,
                    atRecordedPane.Length > 0
                        && atRecordedPane.Any(agent => !agent.AgentSessionPresent));
            }

            if (atRecordedPane.Length > 0)
            {
                if (allowStoppedRecipient)
                {
                    var warning = new NotifyRecipientWarning
                    {
                        Role = toRole,
                        ObservedLiveness = "not-running",
                        Message = $"Recipient role '{toRole}' is recorded at workspace '{topology.WorkspaceId}' "
                            + $"pane '{recordedPane}', but the observed seat is not running. The report will still "
                            + "be delivered to the recorded pane and remain unread until that role wakes.",
                    };
                    if (!write)
                    {
                        return new NotifyDeliveryResult
                        {
                            Resolved = true,
                            Delivered = false,
                            RecipientWarning = warning,
                            ReceiverStateOutcome = "seat-not-running",
                            WorkingTransition = "not-observed",
                            SettleOutcome = "unread",
                            ResendPermitted = false,
                            ActivePhase = Skipped("Dry-run retained the recorded pane despite the recipient seat being stopped."),
                            Summary = $"Dry-run: would deliver report to stopped logical role '{toRole}' at recorded pane '{recordedPane}'.",
                        };
                    }

                    NotifyProcessResult reportDelivery;
                    try
                    {
                        reportDelivery = runner.Run(executable, ["agent", "prompt", recordedPane, payload]);
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Failure(
                            "transport-unavailable",
                            $"{exception.Message} The stopped-seat report warning remains advisory; retry delivery when the transport is available.",
                            recipientWarning: warning);
                    }

                    if (reportDelivery.ExitCode == 0)
                    {
                        return new NotifyDeliveryResult
                        {
                            Resolved = true,
                            Delivered = true,
                            RecipientWarning = warning,
                            ReceiverStateOutcome = "seat-not-running",
                            WorkingTransition = "not-observed",
                            SettleOutcome = "unread",
                            ResendPermitted = false,
                            ActivePhase = new SessionLayerPreflightPhaseResult
                            {
                                Status = SessionLayerPreflight.ActiveAcknowledged,
                                Checked = true,
                                ContactedReceiver = true,
                                Summary = "Report prompt was submitted to the recorded pane; recipient seat liveness remains stopped.",
                            },
                            Summary = $"Delivered report to stopped logical role '{toRole}' at recorded pane '{recordedPane}'. "
                                + "The report is delivered and will remain unread until that role wakes.",
                        };
                    }

                    return Failure(
                        "transport-failure",
                        $"Report prompt to stopped logical role '{toRole}' at recorded pane '{recordedPane}' failed: "
                        + OneLine(reportDelivery.StandardError, reportDelivery.StandardOutput),
                        recipientWarning: warning);
                }

                return Failure(
                    "agent-not-running",
                    $"Team '{team}' recorded workspace '{topology.WorkspaceId}' pane '{recordedPane}' for logical "
                    + $"role '{toRole}', but no running agent is eligible there (observed: "
                    + $"{FormatAgents(atRecordedPane)}). Start exactly one agent at that recorded workspace/pane; "
                    + "agent names are diagnostic only and are never a routing fallback.");
            }

            return Failure(
                "pane-absent",
                $"Team '{team}' recorded workspace '{topology.WorkspaceId}' pane '{recordedPane}' for logical role "
                + $"'{toRole}', but herdr reported no agent at that exact workspace/pane (observed agents: "
                + $"{FormatAgents(agents)}). Start exactly one running agent there; agent names are diagnostic only "
                + "and are never a routing fallback. "
                + NotifyRoleTopologyStore.TopologyRemedy(team));
        }

        if (runningAtRecordedPane.Length > 1)
        {
            return Failure(
                "multiple-agents-at-pane",
                $"Team '{team}' recorded workspace '{topology.WorkspaceId}' pane '{recordedPane}' for logical role "
                + $"'{toRole}', but {runningAtRecordedPane.Length} running agents were reported there: "
                + $"{FormatAgents(runningAtRecordedPane)}. Exactly one running agent is required; agent names are "
                + "diagnostic only and are never a routing fallback.");
        }

        var state = runningAtRecordedPane[0];

        if (!write)
        {
            return new NotifyDeliveryResult
            {
                Resolved = true,
                Delivered = false,
                ActivePhase = Skipped("Dry-run resolved the recorded herdr pane without prompting it."),
                Summary = $"Dry-run: would deliver notification to herdr logical role '{toRole}' in team '{team}' "
                    + $"workspace '{topology.WorkspaceId}' at recorded pane '{recipient.PaneId}'.",
            };
        }

        var alreadyWorking = string.Equals(state.AgentStatus, "working", StringComparison.Ordinal);
        IReadOnlyList<string> promptArguments = alreadyWorking
            ? ["agent", "prompt", deliveryTarget.Target!, payload]
            :
            [
                "agent", "prompt", deliveryTarget.Target!, payload,
                "--wait",
                "--until", "working",
                "--timeout", BoundedPromptTimeoutMilliseconds,
            ];
        NotifyProcessResult delivery;
        try
        {
            // A pane id is globally unique and is the explicit target recorded for this
            // team's workspace. Passing the logical name here would re-enter herdr's
            // global name namespace after we had just scoped validation to the team.
            delivery = runner.Run(executable, promptArguments);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(
                "transport-unavailable",
                $"{exception.Message} Verify the installed herdr executable and recorded recipient pane, then "
                + "retry notify.");
        }

        if (delivery.ExitCode == 0 && alreadyWorking)
        {
            return new NotifyDeliveryResult
            {
                Resolved = true,
                Delivered = true,
                ReceiverStateOutcome = "already-working",
                WorkingTransition = "unobservable",
                SettleOutcome = "not-applicable",
                ResendPermitted = false,
                ActivePhase = new SessionLayerPreflightPhaseResult
                {
                    Status = SessionLayerPreflight.ActiveUnobservable,
                    Checked = true,
                    ContactedReceiver = true,
                    Summary = "The recorded recipient was already working. Prompt submission succeeded, so delivery is "
                        + "accepted, but that active turn makes this prompt's working transition unobservable.",
                },
                Summary = $"Delivered notification to already-working herdr logical role '{toRole}' in team '{team}' "
                    + $"workspace '{topology.WorkspaceId}' at recorded pane '{recipient.PaneId}'; working "
                    + "transition is unobservable.",
            };
        }

        if (delivery.ExitCode == 0)
        {
            NotifyProcessResult settled;
            try
            {
                settled = runner.Run(
                    executable,
                    [
                        "agent", "wait", deliveryTarget.Target!,
                        "--until", "idle",
                        "--until", "done",
                        "--until", "blocked",
                        "--timeout", BoundedPromptTimeoutMilliseconds,
                    ]);
            }
            catch (InvalidOperationException)
            {
                return new NotifyDeliveryResult
                {
                    Resolved = true,
                    Delivered = true,
                    ReceiverStateOutcome = "working-observed-in-progress",
                    WorkingTransition = "observed",
                    SettleOutcome = "pending",
                    ResendPermitted = false,
                    ActivePhase = new SessionLayerPreflightPhaseResult
                    {
                        Status = SessionLayerPreflight.ActiveInProgress,
                        Checked = true,
                        ContactedReceiver = true,
                        Summary = "Delivery succeeded after observed working; the recipient remains working and its settled acknowledgement is pending.",
                    },
                    Summary = $"Delivery succeeded to team '{team}' workspace '{topology.WorkspaceId}' pane "
                        + $"'{recordedPane}' after the observed unattended working transition. The recipient is still "
                        + "working and its settled acknowledgement is pending; do not resend while it is working.",
                };
            }

            if (settled.ExitCode == 0)
            {
                return new NotifyDeliveryResult
                {
                    Resolved = true,
                    Delivered = true,
                    ReceiverStateOutcome = "idle-transitions",
                    WorkingTransition = "observed",
                    SettleOutcome = "observed",
                    ResendPermitted = false,
                    ActivePhase = new SessionLayerPreflightPhaseResult
                    {
                        Status = SessionLayerPreflight.ActiveObserved,
                        Checked = true,
                        ContactedReceiver = true,
                        Summary = "Bounded herdr prompt-wait observed the settled recipient enter unattended work; "
                            + "a second bounded agent wait then observed its fresh settled acknowledgement state.",
                    },
                    Summary = $"Delivered notification to herdr logical role '{toRole}' in team '{team}' workspace "
                        + $"'{topology.WorkspaceId}' at recorded pane '{recipient.PaneId}' after a bounded observed "
                        + "unattended working transition and fresh settled acknowledgement.",
                };
            }

            return new NotifyDeliveryResult
            {
                Resolved = true,
                Delivered = true,
                ReceiverStateOutcome = "working-observed-in-progress",
                WorkingTransition = "observed",
                SettleOutcome = "pending",
                ResendPermitted = false,
                ActivePhase = new SessionLayerPreflightPhaseResult
                {
                    Status = SessionLayerPreflight.ActiveInProgress,
                    Checked = true,
                    ContactedReceiver = true,
                    Summary = "Delivery succeeded after observed working; the recipient remains working and its settled acknowledgement is pending.",
                },
                Summary = $"Delivery succeeded to team '{team}' workspace '{topology.WorkspaceId}' pane "
                    + $"'{recordedPane}' after the observed unattended working transition. The recipient is still "
                    + "working and its settled acknowledgement is pending; do not resend while it is working.",
            };
        }

        var detail = OneLine(delivery.StandardError, delivery.StandardOutput);
        var transitionUnobserved = !alreadyWorking
            && (detail.Contains("agent_prompt_stalled", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("state change", StringComparison.OrdinalIgnoreCase));
        if (transitionUnobserved)
        {
            return new NotifyDeliveryResult
            {
                Resolved = false,
                Delivered = false,
                Cause = "receiver-transition-unobserved",
                ReceiverStateOutcome = "idle-stays-idle",
                WorkingTransition = "not-observed",
                SettleOutcome = "not-applicable",
                ResendPermitted = true,
                ActivePhase = new SessionLayerPreflightPhaseResult
                {
                    Status = SessionLayerPreflight.ActiveNotObserved,
                    Checked = true,
                    ContactedReceiver = true,
                    Summary = "The bounded unattended prompt did not produce the required settled-to-working-to-settled acknowledgement.",
                },
                Summary = $"herdr accepted or attempted the prompt for settled logical role '{toRole}' in team "
                    + $"'{team}', but no unattended working transition and fresh settled acknowledgement were "
                    + $"observed within {BoundedPromptTimeoutMilliseconds}ms ({detail}). Not delivered; resend is "
                    + "permitted because the required unattended working transition was never observed.",
            };
        }

        var cause = ClassifyPromptFailure(detail);
        if (cause is "pane-absent" or "agent-not-running")
        {
            var processInfo = NotifyPaneProcessReader.Read(runner, executable, recordedPane);
            if (!processInfo.Resolved)
            {
                return Failure(
                    processInfo.Cause ?? "process-corroboration-unavailable",
                    $"herdr prompt reported '{cause}' for logical role '{toRole}', but process corroboration was unavailable: {processInfo.Summary}",
                    activePhase: new SessionLayerPreflightPhaseResult
                    {
                        Status = SessionLayerPreflight.ActiveNotObserved,
                        Checked = true,
                        ContactedReceiver = true,
                        Summary = "The prompt failure could not be corroborated by the recorded pane process state.",
                    });
            }

            if (processInfo.Processes.Count > 0)
            {
                return RegistrationLostProcessPresent(
                    team,
                    toRole,
                    topology.WorkspaceId,
                    recordedPane,
                    processInfo.Processes.Count,
                    atRecordedPane.Length > 0
                        && atRecordedPane.Any(agent => !agent.AgentSessionPresent));
            }
        }

        return Failure(
            cause,
            $"herdr delivery to logical role '{toRole}' in team '{team}' workspace '{topology.WorkspaceId}' failed: "
            + $"{detail} Inspect the recorded recipient pane and running-agent state, then retry notify.",
            activePhase: new SessionLayerPreflightPhaseResult
            {
                Status = SessionLayerPreflight.ActiveNotObserved,
                Checked = true,
                ContactedReceiver = true,
                Summary = "The recorded herdr prompt failed before a bounded receiver acknowledgement was observed.",
            });
    }

    internal static IReadOnlyList<HerdrAgentState> ParseAgents(string output)
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

            var parsed = new List<HerdrAgentState>();
            foreach (var agent in agents.EnumerateArray())
            {
                var name = ReadString(agent, "name") ?? "<unnamed>";
                var paneId = ReadString(agent, "pane_id");
                var agentWorkspaceId = ReadString(agent, "workspace_id") ?? WorkspaceFromPane(paneId);
                var agentKind = ReadString(agent, "agent");
                var status = ReadString(agent, "agent_status");
                var stateChangeSequence = ReadInt64(agent, "state_change_seq");
                var lastStateChangeAt = ReadDateTimeOffset(agent, "last_state_change_at");
                var cwd = ReadString(agent, "cwd") ?? ReadString(agent, "foreground_cwd");
                bool? interactiveReady = agent.TryGetProperty("interactive_ready", out var ready)
                    && ready.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? ready.GetBoolean()
                    : null;
                var explicitlyNotReady = interactiveReady == false;
                var hasSession = agent.TryGetProperty("agent_session", out var session)
                    && session.ValueKind == JsonValueKind.Object;
                var running = !string.IsNullOrWhiteSpace(agentKind)
                    && hasSession
                    && !explicitlyNotReady
                    && !string.Equals(status, "unknown", StringComparison.Ordinal);

                parsed.Add(new HerdrAgentState(name, agentWorkspaceId, paneId, running, status, cwd, stateChangeSequence, lastStateChangeAt, agentKind, interactiveReady, hasSession));
            }

            return parsed;
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

    private static long? ReadInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var parsed) ? parsed : null;

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;

    private static string? WorkspaceFromPane(string? paneId)
    {
        var separator = paneId?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return separator > 0 ? paneId![..separator] : null;
    }

    private static string FormatAgents(IEnumerable<HerdrAgentState> agents)
    {
        var formatted = agents
            .Select(agent => $"{agent.Name}@{agent.WorkspaceId ?? "<no-workspace>"}/{agent.PaneId ?? "<no-pane>"}"
                + $"[{agent.AgentStatus ?? "unknown"}; running={agent.AgentRunning.ToString().ToLowerInvariant()}]")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return formatted.Length == 0 ? "none" : string.Join(", ", formatted);
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
                || detail.Contains("undetected", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("agent_not_found", StringComparison.OrdinalIgnoreCase)))
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

    private static NotifyDeliveryResult Failure(
        string cause,
        string summary,
        SessionLayerPreflightPhaseResult? activePhase = null,
        NotifyRecipientWarning? recipientWarning = null,
        string? receiverStateOutcome = null,
        string? workingTransition = null,
        string? settleOutcome = null,
        bool? resendPermitted = null) => new()
        {
            Resolved = false,
            Delivered = false,
            Cause = cause,
            ReceiverStateOutcome = receiverStateOutcome,
            WorkingTransition = workingTransition,
            SettleOutcome = settleOutcome,
            ResendPermitted = resendPermitted,
            ActivePhase = activePhase ?? Skipped("Active receiver delivery did not start because recorded-route resolution failed."),
            RecipientWarning = recipientWarning,
            Summary = summary,
        };

    private static NotifyDeliveryResult RegistrationLostProcessPresent(
        string team,
        string role,
        string workspace,
        string pane,
        int processCount,
        bool agentSessionMissing) => Failure(
            NotifyPendingLivenessResult.RegistrationLostProcessPresent,
            $"Team '{team}' has no running herdr registration for logical role '{role}' at recorded workspace '{workspace}' pane '{pane}', but {processCount} foreground process(es) remain. {NotifyPendingLiveness.RegistrationRecoveryGuidance(agentSessionMissing)} Resend is permitted after the registration is repaired.",
            activePhase: new SessionLayerPreflightPhaseResult
            {
                Status = SessionLayerPreflight.ActiveNotObserved,
                Checked = true,
                ContactedReceiver = false,
                Summary = "The recorded pane process is present while the herdr registration is absent; delivery was not attempted.",
            },
            receiverStateOutcome: NotifyPendingLivenessResult.RegistrationLostProcessPresent,
            workingTransition: "unobservable",
            settleOutcome: "not-applicable",
            resendPermitted: true);

    private static SessionLayerPreflightPhaseResult Skipped(string summary) => new()
    {
        Status = SessionLayerPreflight.ActiveSkipped,
        Checked = false,
        ContactedReceiver = false,
        Summary = summary,
    };

    private static string OneLine(params string[] values)
    {
        var value = values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "no detail";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

internal sealed record NotifyRecipientWarning
{
    [System.Text.Json.Serialization.JsonPropertyName("role")]
    public required string Role { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("observed_liveness")]
    public required string ObservedLiveness { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public required string Message { get; init; }
}

internal sealed record HerdrAgentState(
    string Name,
    string? WorkspaceId,
    string? PaneId,
    bool AgentRunning,
    string? AgentStatus,
    string? Cwd = null,
    long? StateChangeSequence = null,
    DateTimeOffset? LastStateChangeAt = null,
    string? AgentKind = null,
    bool? InteractiveReady = null,
    bool AgentSessionPresent = false);

internal static class NotifyTransportPaths
{
    public const string AgmsgScriptsEnvironmentVariable = "INTENT_CLI_AGMSG_SCRIPTS";
    public const string HerdrExecutableEnvironmentVariable = "INTENT_CLI_HERDR_EXECUTABLE";
    public const string BashExecutableEnvironmentVariable = "INTENT_CLI_BASH_EXECUTABLE";

    // Test seams are deliberately path-only: production resolution never
    // starts a shell or a process while emitting a scheduler artifact.
    internal static Func<string, string?>? ExecutableResolverOverride { get; set; }

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
        return ResolveExecutableOrName(string.IsNullOrWhiteSpace(configured) ? "herdr" : configured);
    }

    public static string ResolveBashExecutable()
    {
        var configured = Environment.GetEnvironmentVariable(BashExecutableEnvironmentVariable);
        // Preserve the long-standing bare default for direct command/test
        // runners; an explicit environment override is still resolved for
        // scheduler emission and normal runtime use.
        return string.IsNullOrWhiteSpace(configured) ? "bash" : ResolveExecutableOrName(configured);
    }

    public static string? ResolveIntentCliExecutable() => ResolveExecutable("intent-cli");

    public static string? ResolveExecutable(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (ExecutableResolverOverride is { } resolver)
        {
            return NormalizeResolvedPath(resolver(executable));
        }

        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable) ? Path.GetFullPath(executable) : null;
        }

        var processPath = Environment.ProcessPath;
        if (string.Equals(executable, "intent-cli", StringComparison.Ordinal)
            && IsIntentCliProcess(processPath))
        {
            return Path.GetFullPath(processPath!);
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            if (OperatingSystem.IsWindows() && Path.GetExtension(executable).Length == 0)
            {
                foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
                {
                    candidate = Path.Combine(directory, executable + extension);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
        }

        return null;
    }

    private static string ResolveExecutableOrName(string executable) =>
        ResolveExecutable(executable) ?? executable;

    private static string? NormalizeResolvedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Path.IsPathRooted(path) ? Path.GetFullPath(path) : ResolveExecutable(path);
    }

    private static bool IsIntentCliProcess(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathRooted(processPath)) return false;
        var name = Path.GetFileNameWithoutExtension(processPath);
        return string.Equals(name, "intent-cli", StringComparison.OrdinalIgnoreCase)
            && File.Exists(processPath);
    }
}
