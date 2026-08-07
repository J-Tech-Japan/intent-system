using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G641 measured supervision.  This is the composition layer over the
/// existing recipient recovery supervisor and the existing stalled-work/CI
/// inventories.  It never performs a lifecycle transition: findings are
/// persisted, and the owning logical role is woken through the team's
/// recorded transport.
/// </summary>
internal sealed class NotifyMeasuredSupervisor
{
    public const int DefaultDetectionBoundSeconds = 300;

    private readonly CliContext context;
    private readonly string routingRoot;
    private readonly string domain;
    private readonly string team;
    private readonly string? repo;
    private readonly string ownerRole;
    private readonly int intervalSeconds;
    private readonly int? declaredBoundSeconds;
    private readonly int staleMinutes;
    private readonly int claimedSilentMinutes;
    private readonly int backlogIdleMinutes;
    private readonly int repairSilentMinutes;
    private readonly bool autoRedispatch;
    private readonly bool write;
    private readonly string format;
    private readonly INotifyProcessRunner runner;
    private readonly string herdrExecutable;
    private readonly string agmsgScriptsDirectory;

    public NotifyMeasuredSupervisor(
        CliContext context,
        string routingRoot,
        string domain,
        string team,
        string? repo,
        string ownerRole,
        int intervalSeconds,
        int? declaredBoundSeconds,
        int staleMinutes,
        int claimedSilentMinutes,
        int backlogIdleMinutes,
        int repairSilentMinutes,
        bool autoRedispatch,
        bool write,
        string format,
        INotifyProcessRunner runner,
        string herdrExecutable,
        string agmsgScriptsDirectory)
    {
        this.context = context;
        this.routingRoot = routingRoot;
        this.domain = domain;
        this.team = team;
        this.repo = repo;
        this.ownerRole = ownerRole;
        this.intervalSeconds = intervalSeconds;
        this.declaredBoundSeconds = declaredBoundSeconds;
        this.staleMinutes = staleMinutes;
        this.claimedSilentMinutes = claimedSilentMinutes;
        this.backlogIdleMinutes = backlogIdleMinutes;
        this.repairSilentMinutes = repairSilentMinutes;
        this.autoRedispatch = autoRedispatch;
        this.write = write;
        this.format = format;
        this.runner = runner;
        this.herdrExecutable = herdrExecutable;
        this.agmsgScriptsDirectory = agmsgScriptsDirectory;
    }

    public NotifySupervisorPass RunOnce()
    {
        var now = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var state = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(),
            domain,
            team);
        if (!state.Resolved)
        {
            return new NotifySupervisorPass
            {
                Actions = [],
                Error = state.Error,
            };
        }

        var bound = ResolveBound(state, now);
        var previousCycle = state.LastCycle;
        long? gapSeconds = previousCycle is null
            ? null
            : Math.Max(0, (long)(now - previousCycle.CompletedAt).TotalSeconds);
        var actualIntervalSeconds = gapSeconds;
        bool? boundMet = bound.BoundSeconds is { } declared && actualIntervalSeconds is { } actual
            ? actual <= declared
            : null;
        bound.Status = bound.Status with
        {
            ActualIntervalSeconds = actualIntervalSeconds,
            BoundMet = boundMet,
        };
        var absentSinceLastCycle = boundMet is false;
        var liveness = new NotifySupervisionLiveness
        {
            Running = true,
            AbsentSinceLastCycle = absentSinceLastCycle,
            LastCycleAt = previousCycle?.CompletedAt,
            GapSeconds = gapSeconds,
            Summary = previousCycle is null
                ? "Supervision is running; no previous completed cycle exists, so an absence gap is not yet measurable."
                : absentSinceLastCycle
                    ? $"Supervision restarted after a {gapSeconds}s gap, exceeding the declared {bound.BoundSeconds}s detection bound."
                    : $"Supervision is running; the measured {gapSeconds}s cycle gap is within the declared bound.",
        };

        var observations = new List<NotifySupervisionObservation>();
        var actions = new List<NotifySupervisorAction>();
        var warnings = new List<string>();

        var openBefore = NotifyPendingDelegationStore.ReadOpen(routingRoot, domain, team, out var pendingError);
        if (pendingError is not null)
        {
            return new NotifySupervisorPass
            {
                Actions = [],
                Error = $"pending-store-unreadable: {pendingError}",
                Bound = bound.Status,
                Liveness = liveness,
            };
        }

        // Keep G630's ordered, fail-closed recipient recovery unchanged.
        var legacy = new NotifySupervisor(
            context,
            routingRoot,
            domain,
            team,
            autoRedispatch,
            write,
            format,
            runner,
            herdrExecutable,
            agmsgScriptsDirectory).RunOnce();
        actions.AddRange(legacy.Actions);
        if (legacy.Error is not null)
        {
            warnings.Add(legacy.Error);
        }

        var openByTask = openBefore.ToDictionary(item => item.TaskId, StringComparer.Ordinal);
        foreach (var action in legacy.Actions)
        {
            var record = openByTask.GetValueOrDefault(action.TaskId);
            observations.Add(new NotifySupervisionObservation
            {
                Key = $"recipient:{action.TaskId}",
                Kind = "recipient-lost",
                OwnerRole = record?.DelegatingRole ?? ownerRole,
                Source = "notify-pending",
                Summary = action.Summary,
                DetectableAt = null,
                WakeAlreadyAttempted = action.Recovered,
                WakeAlreadyDelivered = action.Recovered && action.Cause is null,
                WakeCause = action.Cause,
            });
        }

        if (!string.IsNullOrWhiteSpace(repo))
        {
            try
            {
                var stalled = AutomationStalledWorkCommand.Analyze(
                    context,
                    domain,
                    repo,
                    staleMinutes,
                    claimedSilentMinutes,
                    backlogIdleMinutes,
                    repairSilentMinutes);
                warnings.AddRange(stalled.Warnings);
                foreach (var item in stalled.Items)
                {
                    // A pending CI check is an active wait, not a stall.  The
                    // terminal CI classes are retained and woken here.
                    if (string.Equals(item.Kind, AutomationStalledWorkCommand.KindCiPending, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var reference = item.Pr?.Number.ToString(CultureInfo.InvariantCulture)
                        ?? item.Issue?.Number.ToString(CultureInfo.InvariantCulture)
                        ?? "none";
                    observations.Add(new NotifySupervisionObservation
                    {
                        Key = $"stalled:{item.Kind}:{item.ExecutionUnit}:{reference}",
                        Kind = item.Kind,
                        OwnerRole = ownerRole,
                        Source = "automation-stalled-work",
                        Summary = item.RecommendedAction,
                        DetectableAt = previousCycle is null
                            ? null
                            : now.AddMinutes(-Math.Max(0, item.AgeMinutes)),
                        WakeAlreadyAttempted = false,
                        WakeAlreadyDelivered = false,
                    });
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                warnings.Add($"stalled-work-unavailable: {exception.Message}");
            }
        }

        observations.AddRange(ReadUndeliveredEscalations(now, previousCycle is not null));
        observations.AddRange(ReadAbsentSeats(now));

        if (boundMet is false)
        {
            observations.Add(new NotifySupervisionObservation
            {
                Key = "supervisor:liveness",
                Kind = "supervisor-not-running",
                OwnerRole = ownerRole,
                Source = "supervision-cycle",
                Summary = $"Supervision was absent for {gapSeconds}s, beyond the declared {bound.BoundSeconds}s detection bound.",
                DetectableAt = null,
                WakeAlreadyAttempted = false,
                WakeAlreadyDelivered = false,
            });
        }

        var currentKeys = observations.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var records = new List<NotifySupervisionStallRecord>();
        var findings = new List<NotifySupervisionFinding>();
        foreach (var observation in observations
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.First()))
        {
            var existing = state.ActiveStalls.GetValueOrDefault(observation.Key);
            if (existing is not null && existing.WakeDelivered)
            {
                records.Add(existing with { Summary = observation.Summary });
                findings.Add(ToFinding(records[^1]));
                continue;
            }

            var wake = observation.WakeAlreadyAttempted && existing is null
                ? new NotifySupervisionWakeResult
                {
                    Attempted = true,
                    Delivered = observation.WakeAlreadyDelivered,
                    Cause = observation.WakeCause,
                    Summary = observation.WakeAlreadyDelivered
                        ? "The existing recovery path already woke the owning role."
                        : "The existing recovery path did not deliver a wake.",
                }
                : WakeOwner(observation, now);
            var record = existing ?? new NotifySupervisionStallRecord
            {
                Key = observation.Key,
                Kind = observation.Kind,
                OwnerRole = observation.OwnerRole,
                Source = observation.Source,
                Summary = observation.Summary,
                DetectableAt = previousCycle is null ? null : observation.DetectableAt,
                DetectableAtUnknown = previousCycle is null || observation.DetectableAt is null,
                SurfacedAt = now,
            };
            record = record with
            {
                Summary = observation.Summary,
                WakeAttempted = wake.Attempted,
                WakeDelivered = wake.Delivered,
                WakeCause = wake.Cause,
            };
            records.Add(record);
            findings.Add(ToFinding(record));
            if (existing is null)
            {
                var open = NotifySupervisionStore.OpenStall(
                    NotifySupervisionStore.ResolveStallPath(
                        context.ResolveSupervisionArtifactRootPath(),
                        domain,
                        team),
                    record,
                    write);
                if (open.Error is not null)
                {
                    warnings.Add($"supervision-stall-write-failed: {open.Error}");
                }
            }
            else if (existing.WakeAttempted && !existing.WakeDelivered && wake.Attempted)
            {
                // A failed wake is retried on the next bounded cycle.  The
                // second open event preserves that retry evidence without
                // rewriting the original detectable/surfaced timestamps.
                var retry = NotifySupervisionStore.OpenStall(
                    NotifySupervisionStore.ResolveStallPath(
                        context.ResolveSupervisionArtifactRootPath(),
                        domain,
                        team),
                    record,
                    write);
                if (retry.Error is not null)
                {
                    warnings.Add($"supervision-stall-retry-write-failed: {retry.Error}");
                }
            }
        }

        foreach (var stale in state.ActiveStalls.Values.Where(item => !currentKeys.Contains(item.Key)))
        {
            var clear = NotifySupervisionStore.ClearStall(
                NotifySupervisionStore.ResolveStallPath(
                    context.ResolveSupervisionArtifactRootPath(),
                    domain,
                    team),
                stale.Key,
                now,
                write);
            if (clear.Error is not null)
            {
                warnings.Add($"supervision-stall-clear-failed: {clear.Error}");
            }

            records.Add(stale with
            {
                ClearedAt = now,
                DurationSeconds = stale.DetectableAt is { } detectableAt
                    ? Math.Max(0, (long)(now - detectableAt).TotalSeconds)
                    : null,
            });
        }

        var cycle = new NotifySupervisionCycle
        {
            CycleId = Guid.NewGuid().ToString("N"),
            StartedAt = now,
            CompletedAt = now,
            IntervalSeconds = intervalSeconds,
            BoundSeconds = bound.BoundSeconds,
            ActualIntervalSeconds = actualIntervalSeconds,
            BoundMet = boundMet,
            AbsentSinceLastCycle = absentSinceLastCycle,
            GapSeconds = gapSeconds,
        };
        var cycleWrite = NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(
                context.ResolveSupervisionArtifactRootPath(),
                domain,
                team),
            cycle,
            write);
        if (cycleWrite.Error is not null)
        {
            warnings.Add($"supervision-cycle-write-failed: {cycleWrite.Error}");
        }

        var recoveryState = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(),
            domain,
            team);
        var recoveryRecords = recoveryState.Resolved
            ? recoveryState.StallHistory
            : records;
        return new NotifySupervisorPass
        {
            Actions = actions,
            Findings = findings,
            RecoveryRecords = recoveryRecords,
            Bound = bound.Status,
            Liveness = liveness,
            Warnings = warnings,
        };
    }

    public int RunLoop(TextWriter writer, CancellationToken cancellationToken, bool once)
    {
        do
        {
            var pass = RunOnce();
            if (!pass.Silent || once)
            {
                NotifyCommand.EmitSupervision(
                    writer,
                    pass,
                    domain,
                    team,
                    intervalSeconds,
                    autoRedispatch,
                    write,
                    format);
            }

            if (once || cancellationToken.IsCancellationRequested)
            {
                return pass.ExitCode;
            }

            NotifySupervisor.Delay(TimeSpan.FromSeconds(intervalSeconds));
        }
        while (!cancellationToken.IsCancellationRequested);

        return 0;
    }

    private (int? BoundSeconds, NotifySupervisionBoundStatus Status) ResolveBound(
        NotifySupervisionReadResult state,
        DateTimeOffset now)
    {
        var boundSeconds = declaredBoundSeconds ?? state.Bound?.BoundSeconds;
        var recorded = state.Bound is not null;
        if (declaredBoundSeconds is { } declared && (!recorded || state.Bound!.BoundSeconds != declared))
        {
            var writeResult = NotifySupervisionStore.RecordBound(
                context.ResolveSupervisionArtifactRootPath(),
                new NotifySupervisionBound
                {
                    Domain = domain,
                    Team = team,
                    BoundSeconds = declared,
                    RecordedAt = now,
                },
                write);
            recorded = writeResult.Applied || (state.Bound is not null && state.Bound.BoundSeconds == declared);
        }

        var status = boundSeconds is null
            ? "unrecorded"
            : recorded ? "recorded" : "preview-unrecorded";
        return (boundSeconds, new NotifySupervisionBoundStatus
        {
            BoundSeconds = boundSeconds,
            Recorded = recorded,
            Status = status,
            Path = NotifySupervisionStore.ResolveBoundPath(
                context.ResolveSupervisionArtifactRootPath(),
                domain,
                team),
        });
    }

    private NotifySupervisionWakeResult WakeOwner(
        NotifySupervisionObservation observation,
        DateTimeOffset now)
    {
        if (!write)
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = false,
                Delivered = false,
                Summary = "Dry-run: the owning role would be woken through its recorded transport.",
            };
        }

        if (string.IsNullOrWhiteSpace(observation.OwnerRole))
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = false,
                Delivered = false,
                Cause = "owner-role-missing",
                Summary = "The finding has no owning logical role; no transport was invented.",
            };
        }

        SessionLayerModeResolution resolution;
        try
        {
            resolution = SessionLayerModeStore.Resolve(routingRoot, domain, team);
        }
        catch (InvalidOperationException exception)
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = true,
                Delivered = false,
                Cause = "session-layer-mode-unreadable",
                Summary = exception.Message,
            };
        }

        var record = new NotifyPendingDelegation
        {
            Domain = domain,
            Team = team,
            TaskId = "supervision-" + observation.Key.Replace(':', '-'),
            DelegatingRole = observation.OwnerRole,
            RecipientRole = observation.OwnerRole,
            ReportToRole = observation.OwnerRole,
            RecipientIdentity = $"supervision-owner={observation.OwnerRole}",
            ExpectedArtifact = "supervision finding acknowledgement",
            ExpectedArtifacts = ["supervision finding acknowledgement"],
            Objective = observation.Summary,
            Inputs = [observation.Source],
            DispatchedAt = now,
            TransportMode = resolution.Mode,
        };
        var delivery = NotifySupervisorDelivery.Send(
            routingRoot,
            record,
            JsonSerializer.Serialize(new
            {
                notification = "supervise-finding",
                kind = observation.Kind,
                source = observation.Source,
                key = observation.Key,
                summary = observation.Summary,
                must_transition = false,
            }),
            runner,
            herdrExecutable,
            agmsgScriptsDirectory);
        return new NotifySupervisionWakeResult
        {
            Attempted = true,
            Delivered = delivery.Resolved && delivery.Delivered,
            Cause = delivery.Resolved && delivery.Delivered ? null : delivery.Cause ?? "wake-undelivered",
            Summary = delivery.Summary,
        };
    }

    private IReadOnlyList<NotifySupervisionObservation> ReadUndeliveredEscalations(
        DateTimeOffset now,
        bool priorCycleExists)
    {
        if (!NotifyEventWriter.TryResolvePath(routingRoot, team, out var path, out _)
            || !File.Exists(path))
        {
            return [];
        }

        var observations = new List<NotifySupervisionObservation>();
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("kind", out var kind)
                    || !string.Equals(kind.GetString(), "escalation", StringComparison.Ordinal))
                {
                    continue;
                }

                var unit = root.TryGetProperty("unit", out var unitElement)
                    ? unitElement.GetString() ?? "unknown"
                    : "unknown";
                var artifact = root.TryGetProperty("artifact", out var artifactElement)
                    ? artifactElement.GetString() ?? unit
                    : unit;
                var summary = root.TryGetProperty("summary", out var summaryElement)
                    ? summaryElement.GetString() ?? string.Empty
                    : string.Empty;
                // Notify's own recovery/finding wake is already delivered by
                // the transport adapter.  It uses the same six-field event
                // schema as an operator escalation, so exclude those marked
                // payloads rather than creating a self-sustaining wake loop.
                if (artifact.StartsWith("supervision-", StringComparison.Ordinal)
                    || summary.Contains("supervise-finding", StringComparison.Ordinal)
                    || summary.Contains("supervise-recovery", StringComparison.Ordinal))
                {
                    continue;
                }
                var timestamp = root.TryGetProperty("timestamp", out var timestampElement)
                    && timestampElement.TryGetDateTimeOffset(out var recordedAt)
                    ? recordedAt.ToUniversalTime()
                    : (DateTimeOffset?)null;
                var key = $"escalation:{unit}:{timestamp?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? artifact}";
                observations.Add(new NotifySupervisionObservation
                {
                    Key = key,
                    Kind = "undelivered-escalation",
                    OwnerRole = ownerRole,
                    Source = "notify-escalate",
                    Summary = $"Escalation '{unit}' is durable in events.jsonl with delivered:false; the owning role was not woken.",
                    DetectableAt = priorCycleExists ? timestamp : null,
                    WakeAlreadyAttempted = false,
                    WakeAlreadyDelivered = false,
                });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return
            [
                new NotifySupervisionObservation
                {
                    Key = "escalation-store:unreadable",
                    Kind = "undelivered-escalation",
                    OwnerRole = ownerRole,
                    Source = "notify-escalate",
                    Summary = $"The escalation event channel could not be read: {exception.Message}",
                    DetectableAt = null,
                },
            ];
        }

        return observations;
    }

    private IReadOnlyList<NotifySupervisionObservation> ReadAbsentSeats(DateTimeOffset now)
    {
        SessionLayerModeResolution mode;
        try
        {
            mode = SessionLayerModeStore.Resolve(routingRoot, domain, team);
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        if (!string.Equals(mode.Mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal))
        {
            return [];
        }

        var topology = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (!topology.Resolved || topology.Topology is null)
        {
            return [];
        }

        NotifyProcessResult roster;
        try
        {
            roster = runner.Run(herdrExecutable, ["agent", "list"]);
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        if (roster.ExitCode != 0)
        {
            return [];
        }

        IReadOnlyList<HerdrAgentState> agents;
        try
        {
            agents = HerdrNotifyTransport.ParseAgents(roster.StandardOutput);
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        var observations = new List<NotifySupervisionObservation>();
        foreach (var (role, recorded) in topology.Topology.Roles)
        {
            if (!string.Equals(recorded.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(recorded.PaneId))
            {
                continue;
            }

            var running = agents.Any(agent =>
                string.Equals(agent.WorkspaceId, recorded.WorkspaceId ?? topology.Topology.WorkspaceId, StringComparison.Ordinal)
                && string.Equals(agent.PaneId, recorded.PaneId, StringComparison.Ordinal)
                && agent.AgentRunning);
            if (!running)
            {
                observations.Add(new NotifySupervisionObservation
                {
                    Key = $"seat:{role}:{recorded.PaneId}",
                    Kind = "seat-absent",
                    OwnerRole = ownerRole,
                    Source = "recorded-topology",
                    Summary = $"Recorded herdr seat '{role}' is absent from workspace '{topology.Topology.WorkspaceId}' pane '{recorded.PaneId}'.",
                    DetectableAt = null,
                    WakeAlreadyAttempted = false,
                    WakeAlreadyDelivered = false,
                });
            }
        }

        return observations;
    }

    private static NotifySupervisionFinding ToFinding(NotifySupervisionStallRecord record) => new()
    {
        Key = record.Key,
        Kind = record.Kind,
        OwnerRole = record.OwnerRole,
        Source = record.Source,
        Summary = record.Summary,
        DetectableAt = record.DetectableAt,
        SurfacedAt = record.SurfacedAt,
        WakeAttempted = record.WakeAttempted,
        WakeDelivered = record.WakeDelivered,
        Cause = record.WakeCause,
    };
}

internal sealed record NotifySupervisionObservation
{
    public required string Key { get; init; }
    public required string Kind { get; init; }
    public required string OwnerRole { get; init; }
    public required string Source { get; init; }
    public required string Summary { get; init; }
    public DateTimeOffset? DetectableAt { get; init; }
    public bool WakeAlreadyAttempted { get; init; }
    public bool WakeAlreadyDelivered { get; init; }
    public string? WakeCause { get; init; }
}

internal sealed record NotifySupervisionWakeResult
{
    public required bool Attempted { get; init; }
    public required bool Delivered { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
}

internal sealed record NotifySupervisionBoundStatus
{
    [System.Text.Json.Serialization.JsonPropertyName("bound_seconds")]
    public int? BoundSeconds { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("recorded")]
    public bool Recorded { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public required string Status { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("actual_interval_seconds")]
    public long? ActualIntervalSeconds { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("bound_met")]
    public bool? BoundMet { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("path")]
    public string? Path { get; init; }
}

internal sealed record NotifySupervisionLiveness
{
    [System.Text.Json.Serialization.JsonPropertyName("running")]
    public required bool Running { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("absent_since_last_cycle")]
    public required bool AbsentSinceLastCycle { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("last_cycle_at")]
    public DateTimeOffset? LastCycleAt { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("gap_seconds")]
    public long? GapSeconds { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

internal sealed record NotifySupervisionFinding
{
    [System.Text.Json.Serialization.JsonPropertyName("key")]
    public required string Key { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("owner_role")]
    public required string OwnerRole { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public required string Source { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("detectable_at")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DetectableAt { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("surfaced_at")]
    public required DateTimeOffset SurfacedAt { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("wake_attempted")]
    public bool WakeAttempted { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("wake_delivered")]
    public bool WakeDelivered { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("cause")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Cause { get; init; }
}
