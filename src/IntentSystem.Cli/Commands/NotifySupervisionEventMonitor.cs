using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Opt-in G659 event source. One notify supervise process owns one blocking
/// herdr wait per recorded seat; failures are recorded and the wait is
/// re-armed. The periodic supervisor remains a separate observation source.
/// Measured against herdr 0.8.0 on macOS; other versions/platforms are
/// emitted as unverified guidance rather than inferred compatibility.
/// </summary>
internal sealed class NotifySupervisionEventMonitor
{
    internal static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
    private static readonly TimeSpan RearmDelay = TimeSpan.FromSeconds(1);
    private static readonly string[] SettledStatuses = ["done", "blocked", "idle"];

    private readonly string routingRoot;
    private readonly string domain;
    private readonly string team;
    private readonly INotifyProcessRunner runner;
    private readonly string herdrExecutable;
    private readonly Func<string, Task> transitionObserved;
    private readonly Action<NotifySupervisionWaitEvent> waitEventObserved;

    public NotifySupervisionEventMonitor(
        string routingRoot,
        string domain,
        string team,
        INotifyProcessRunner runner,
        string herdrExecutable,
        Func<string, Task> transitionObserved,
        Action<NotifySupervisionWaitEvent> waitEventObserved)
    {
        this.routingRoot = routingRoot;
        this.domain = domain;
        this.team = team;
        this.runner = runner;
        this.herdrExecutable = herdrExecutable;
        this.transitionObserved = transitionObserved;
        this.waitEventObserved = waitEventObserved;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var topology = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (!topology.Resolved || topology.Topology is null)
        {
            RecordMonitorFailure("topology", "<unresolved>", "<unresolved>", topology.Summary);
            return;
        }

        IReadOnlyDictionary<string, HerdrAgentState> initial;
        try
        {
            var list = await runner.RunAsync(
                herdrExecutable,
                ["agent", "list"],
                cancellationToken).ConfigureAwait(false);
            if (list.ExitCode != 0)
            {
                RecordMonitorFailure("topology", topology.Topology.WorkspaceId, "<unresolved>",
                    $"herdr agent list exited {list.ExitCode}: {OneLine(list.StandardError, list.StandardOutput)}");
                return;
            }
            initial = HerdrNotifyTransport.ParseAgents(list.StandardOutput)
                .Where(agent => agent.PaneId is not null)
                .ToDictionary(agent => agent.PaneId!, StringComparer.Ordinal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            RecordMonitorFailure("topology", topology.Topology.WorkspaceId, "<unresolved>", exception.Message);
            return;
        }

        var waits = topology.Topology.Roles
            .Where(pair => string.Equals(pair.Value.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(pair.Value.PaneId))
            .Select(pair => MonitorSeatAsync(
                pair.Key,
                pair.Value.WorkspaceId ?? topology.Topology.WorkspaceId,
                pair.Value.PaneId!,
                initial.GetValueOrDefault(pair.Value.PaneId!)?.AgentStatus,
                cancellationToken))
            .ToArray();
        await Task.WhenAll(waits).ConfigureAwait(false);
    }

    private async Task MonitorSeatAsync(
        string role,
        string workspaceId,
        string paneId,
        string? initialStatus,
        CancellationToken cancellationToken)
    {
        var waitForSettled = string.Equals(initialStatus, "working", StringComparison.Ordinal);
        while (!cancellationToken.IsCancellationRequested)
        {
            var arguments = new List<string> { "agent", "wait", paneId };
            if (waitForSettled)
            {
                foreach (var status in SettledStatuses)
                {
                    arguments.Add("--until");
                    arguments.Add(status);
                }
            }
            else
            {
                arguments.Add("--until");
                arguments.Add("working");
            }

            try
            {
                var result = await runner.RunAsync(herdrExecutable, arguments, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    RecordMonitorFailure(role, workspaceId, paneId,
                        $"herdr agent wait exited {result.ExitCode}: {OneLine(result.StandardError, result.StandardOutput)}");
                    await Delay(RearmDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var observed = ParseWaitAgent(result.StandardOutput);
                if (!string.Equals(observed.WorkspaceId, workspaceId, StringComparison.Ordinal)
                    || !string.Equals(observed.PaneId, paneId, StringComparison.Ordinal))
                {
                    RecordMonitorFailure(role, workspaceId, paneId,
                        $"herdr agent wait returned workspace '{observed.WorkspaceId}' pane '{observed.PaneId}' for another seat.");
                    await Delay(RearmDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var settled = SettledStatuses.Contains(observed.AgentStatus, StringComparer.Ordinal);
                if (waitForSettled && settled)
                {
                    if (role is "implementation" or "review")
                    {
                        await transitionObserved(role).ConfigureAwait(false);
                    }
                    waitForSettled = false;
                }
                else if (!waitForSettled && string.Equals(observed.AgentStatus, "working", StringComparison.Ordinal))
                {
                    waitForSettled = true;
                }
                else
                {
                    RecordMonitorFailure(role, workspaceId, paneId,
                        $"herdr agent wait returned unexpected status '{observed.AgentStatus ?? "<unknown>"}'.");
                    await Delay(RearmDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
            {
                RecordMonitorFailure(role, workspaceId, paneId, exception.Message);
                await Delay(RearmDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void RecordMonitorFailure(string role, string workspaceId, string paneId, string detail) =>
        waitEventObserved(new NotifySupervisionWaitEvent
        {
            Role = role,
            WorkspaceId = workspaceId,
            PaneId = paneId,
            Outcome = "wait-died-or-errored",
            Detail = detail,
            ObservedAt = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            RearmAttempted = true,
        });

    private static HerdrAgentState ParseWaitAgent(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("result", out var result)
                || !result.TryGetProperty("agent", out var agent)
                || agent.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("herdr agent wait response did not contain result.agent.");
            }

            var paneId = ReadString(agent, "pane_id");
            var workspaceId = ReadString(agent, "workspace_id")
                ?? paneId?.Split(':', 2, StringSplitOptions.None)[0];
            return new HerdrAgentState(
                ReadString(agent, "name") ?? "<unnamed>",
                workspaceId,
                paneId,
                AgentRunning: true,
                ReadString(agent, "agent_status"),
                StateChangeSequence: ReadInt64(agent, "state_change_seq"),
                LastStateChangeAt: ReadDateTimeOffset(agent, "last_state_change_at"));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"herdr agent wait returned invalid JSON: {exception.Message}", exception);
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var parsed) ? parsed : null;

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDateTimeOffset(out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static string OneLine(string primary, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        return string.Join(" ", value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
