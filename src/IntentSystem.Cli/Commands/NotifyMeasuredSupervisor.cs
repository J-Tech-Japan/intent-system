using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    private const int FallbackAbsenceHeadroomSeconds = 60;
    private const string DesignRole = "design";
    private const string ObservationConflictKind = "observation-conflict";

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
    private readonly string bashExecutable;
    private readonly bool eventMode;
    private readonly IReadOnlyList<NotifyPreApprovalRule> preApprovalAccept;
    private readonly IReadOnlyList<NotifyPreApprovalRule> preApprovalEscalate;
    private readonly IReadOnlyList<NotifyScopedPromptPolicy> scopedPolicies;
    private readonly NotifySupervisionWriterIdentity writerIdentity;
    private readonly Func<NotifySupervisionWriterIdentity, bool> writerIsLive;
    private readonly Func<AutomationStalledWorkResult>? stalledWorkAnalyzer;
    private readonly int? configuredRepeatBackoffSeconds;
    private readonly int? configuredDebounceConsecutiveObservations;
    private readonly Func<DateTimeOffset, IReadOnlyList<NotifySupervisionObservation>>? observationProvider;
    private readonly object supervisionSync = new();
    private readonly object writerSync = new();

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
        string agmsgScriptsDirectory,
        bool eventMode = false,
        IReadOnlyList<NotifyPreApprovalRule>? preApprovalAccept = null,
        IReadOnlyList<NotifyPreApprovalRule>? preApprovalEscalate = null,
        string? bashExecutable = null,
        NotifySupervisionWriterIdentity? writerIdentity = null,
        Func<NotifySupervisionWriterIdentity, bool>? writerIsLive = null,
        Func<AutomationStalledWorkResult>? stalledWorkAnalyzer = null,
        IReadOnlyList<NotifyScopedPromptPolicy>? scopedPolicies = null,
        int? repeatBackoffSeconds = null,
        int? debounceConsecutiveObservations = null,
        Func<DateTimeOffset, IReadOnlyList<NotifySupervisionObservation>>? observationProvider = null)
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
        this.bashExecutable = bashExecutable ?? "bash";
        this.eventMode = eventMode;
        this.preApprovalAccept = preApprovalAccept ?? [];
        this.preApprovalEscalate = preApprovalEscalate ?? [];
        this.scopedPolicies = scopedPolicies ?? [];
        this.writerIdentity = writerIdentity ?? NotifySupervisionWriterIdentity.Current();
        this.writerIsLive = writerIsLive ?? (other => other.IsLiveOn(this.writerIdentity));
        this.stalledWorkAnalyzer = stalledWorkAnalyzer;
        this.configuredRepeatBackoffSeconds = repeatBackoffSeconds;
        this.configuredDebounceConsecutiveObservations = debounceConsecutiveObservations;
        this.observationProvider = observationProvider;
    }

    public NotifySupervisorPass RunOnce() => RunOnce("interval");

    internal NotifySupervisorPass RunOnce(string trigger)
    {
        lock (supervisionSync)
        {
            return RunOnceCore(trigger);
        }
    }

    private NotifySupervisorPass RunOnceCore(string trigger)
    {
        try
        {
            if (TeamModeStore.Resolve(routingRoot, domain, team).IsAuthoringOnly)
            {
                return new NotifySupervisorPass
                {
                    Actions = [],
                    Error = "not-applicable-team-mode: authoring-only teams have no supervision process.",
                };
            }
        }
        catch (InvalidOperationException exception)
        {
            return new NotifySupervisorPass
            {
                Actions = [],
                Error = $"team-mode-unreadable: {exception.Message}",
            };
        }

        var now = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var cycleId = Guid.NewGuid().ToString("N");
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
        var emissionPolicy = ResolveEmissionPolicy(state, now);
        var emissionPolicyWrite = NotifySupervisionStore.RecordEmissionPolicy(
            context.ResolveSupervisionArtifactRootPath(),
            emissionPolicy,
            write);
        var preApprovalPolicy = ResolvePreApprovalPolicy(now, cycleId, out var policyError);
        if (policyError is not null)
        {
            return new NotifySupervisorPass
            {
                Actions = [],
                Bound = bound.Status,
                EmissionPolicy = emissionPolicy,
                PreApprovalPolicy = preApprovalPolicy,
                Error = policyError,
            };
        }
        var previousCycle = state.LastCycle;
        var previousIntervalCycle = state.LastIntervalCycle;
        long? gapSeconds = previousIntervalCycle is null
            ? null
            : Math.Max(0, (long)(now - previousIntervalCycle.CompletedAt).TotalSeconds);
        var actualIntervalSeconds = string.Equals(trigger, "interval", StringComparison.Ordinal)
            ? gapSeconds
            : null;
        bool? boundMet = bound.BoundSeconds is { } explicitBoundSeconds && actualIntervalSeconds is { } actual
            ? actual <= explicitBoundSeconds
            : null;
        bound.Status = bound.Status with
        {
            ActualIntervalSeconds = actualIntervalSeconds,
            BoundMet = boundMet,
        };
        // A team may not have recorded an explicit bound yet, but the loop
        // still has a configured cadence (persisted on each cycle). Give the
        // fallback self-absence threshold measured headroom beyond that
        // cadence: normal cycle work and scheduler jitter must not look like
        // downtime. The explicit bound result remains null when no bound was
        // recorded, so output never claims an unmeasured promise.
        var cadenceIntervalSeconds = previousIntervalCycle?.IntervalSeconds is > 0
            ? previousIntervalCycle.IntervalSeconds
            : intervalSeconds;
        var absenceThresholdSeconds = bound.BoundSeconds
            ?? FallbackAbsenceThresholdSeconds(cadenceIntervalSeconds);
        var absenceThresholdKind = bound.BoundSeconds is null
            ? "configured-interval"
            : "declared-bound";
        var duplicateSupervisor = DetectDuplicateSupervisor(
            previousCycle,
            state.InstalledSupervisor,
            now,
            absenceThresholdSeconds);
        var absentSinceLastCycle = string.Equals(trigger, "interval", StringComparison.Ordinal)
            && gapSeconds is { } gap
            && gap > absenceThresholdSeconds;
        var liveness = new NotifySupervisionLiveness
        {
            Running = true,
            AbsentSinceLastCycle = absentSinceLastCycle,
            LastCycleAt = previousIntervalCycle?.CompletedAt,
            GapSeconds = gapSeconds,
            AbsenceThresholdSeconds = absenceThresholdSeconds,
            AbsenceThresholdKind = absenceThresholdKind,
            Summary = previousIntervalCycle is null
                ? "Supervision is running; no previous completed cycle exists, so an absence gap is not yet measurable."
                : absentSinceLastCycle
                    ? bound.BoundSeconds is { } declaredSeconds
                        ? $"Supervision restarted after a {gapSeconds}s gap, exceeding the declared {declaredSeconds}s detection bound."
                        : $"Supervision restarted after a {gapSeconds}s gap, exceeding the configured {cadenceIntervalSeconds}s cadence's {absenceThresholdSeconds}s self-absence threshold; no detection bound was declared."
                    : bound.BoundSeconds is { }
                        ? $"Supervision is running; the measured {gapSeconds}s cycle gap is within the declared detection bound."
                        : $"Supervision is running; the measured {gapSeconds}s cycle gap is within the configured {cadenceIntervalSeconds}s cadence and its {absenceThresholdSeconds}s self-absence threshold; no detection bound was declared.",
            CadenceIntervalSeconds = cadenceIntervalSeconds,
        };

        var observations = new List<NotifySupervisionObservation>();
        var actions = new List<NotifySupervisorAction>();
        var warnings = new List<string>();
        if (emissionPolicyWrite.Error is not null)
        {
            warnings.Add($"supervision-emission-policy-write-failed: {emissionPolicyWrite.Error}");
        }
        if (duplicateSupervisor is not null)
        {
            observations.Add(duplicateSupervisor);
        }
        var boundBelowInterval = declaredBoundSeconds.HasValue && declaredBoundSeconds.Value < intervalSeconds;
        if (boundBelowInterval)
        {
            warnings.Add($"declared bound {declaredBoundSeconds!.Value}s is smaller than configured interval {intervalSeconds}s; normal cadence will structurally exceed the bound and the value was not corrected.");
        }

        var openBefore = NotifyPendingDelegationStore.ReadOpen(routingRoot, domain, team, out var pendingError);
        if (pendingError is not null)
        {
            return new NotifySupervisorPass
            {
                Actions = [],
                Error = $"pending-store-unreadable: {pendingError}",
                Bound = bound.Status,
                EmissionPolicy = emissionPolicy,
                Liveness = liveness,
            };
        }

        var transportFailures = NotifyTransportPreflight.Check(
            routingRoot,
            domain,
            team,
            openBefore,
            eventMode,
            runner,
            herdrExecutable,
            agmsgScriptsDirectory,
            bashExecutable);
        var transportUnavailable = transportFailures.Count > 0;
        foreach (var failure in transportFailures)
        {
            observations.Add(new NotifySupervisionObservation
            {
                Key = $"transport:{failure.Binary}",
                Kind = "supervision-degraded",
                OwnerRole = ownerRole,
                Source = "supervision-transport-preflight",
                Summary = $"Supervision degraded: transport unavailable; binary '{failure.Binary}' could not be started: {failure.Error} No recipient-lost judgment or per-delegation wake was made in this cycle.",
                Cause = failure.Cause,
                WakeSuppressed = true,
            });
        }

        // Keep G630's ordered, fail-closed recipient recovery unchanged when
        // the transport process itself is startable. A failed preflight exits
        // this recipient path before any liveness judgment.
        var legacy = new NotifySupervisorPass { Actions = [] };
        if (!transportUnavailable)
        {
            legacy = new NotifySupervisor(
                context,
                routingRoot,
                domain,
                team,
                autoRedispatch,
                write,
                format,
                runner,
                herdrExecutable,
                agmsgScriptsDirectory,
                notifier: DeliverRecoveryNotification,
                bashExecutable: bashExecutable).RunOnce();
            actions.AddRange(legacy.Actions);
            if (legacy.Error is not null)
            {
                warnings.Add(legacy.Error);
            }
        }

        var openByTask = openBefore.ToDictionary(item => item.TaskId, StringComparer.Ordinal);
        var observedSequences = new Dictionary<string, long>(StringComparer.Ordinal);
        var observedTimes = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var observedStatuses = new Dictionary<string, string>(StringComparer.Ordinal);
        var observedInteractiveReadiness = new Dictionary<string, bool?>(StringComparer.Ordinal);
        var observedStatusConsecutiveCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var observedStatusRunFrom = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!transportUnavailable)
        {
            foreach (var pending in openBefore)
            {
                var transportMode = string.IsNullOrWhiteSpace(pending.TransportMode)
                    ? SessionLayerMode.Agmsg
                    : pending.TransportMode!;
                var activity = NotifyPendingLiveness.Probe(routingRoot, pending, transportMode, runner, herdrExecutable, agmsgScriptsDirectory, bashExecutable);
                if (!activity.Resolved || activity.Running != true || activity.StateChangeSequence is not { } sequence)
                {
                    continue;
                }

                var paneKey = pending.WorkspaceId is not null && pending.PaneId is not null
                    ? $"activity:{pending.WorkspaceId}:{pending.PaneId}"
                    : $"activity:{pending.RecipientIdentity}";
                observedSequences[paneKey] = sequence;
                if (activity.LastStateChangeAt is { } changedAt)
                {
                    observedTimes[paneKey] = changedAt;
                }

                long? priorStateChangeSequence = null;
                if (previousCycle?.LastObservedStateChangeSequences.TryGetValue(paneKey, out var observedSequence) == true)
                {
                    priorStateChangeSequence = observedSequence;
                }

                var hasPriorActivityObservation = priorStateChangeSequence.HasValue;
                var advanced = priorStateChangeSequence is { } priorSequence && sequence > priorSequence;
                var stateChangedAfterDispatch = activity.LastStateChangeAt is { } lastStateChangeAt
                    && lastStateChangeAt > pending.DispatchedAt;
                var working = string.Equals(activity.AgentStatus, "working", StringComparison.Ordinal)
                    && (advanced || (!hasPriorActivityObservation && stateChangedAfterDispatch));
            // A first observation establishes a durable baseline but is never
            // proof that no activity occurred. Only a later observation can
            // classify an unchanged/non-working recipient as live-idle.
                var idle = hasPriorActivityObservation && !working;
                var beyondThreshold = declaredBoundSeconds.HasValue
                    && now - pending.DispatchedAt > TimeSpan.FromSeconds(declaredBoundSeconds.Value);
                if (!pending.ReportArrived && idle && beyondThreshold)
                {
                    observations.Add(new NotifySupervisionObservation
                    {
                        Key = $"live-idle:{paneKey}",
                        Kind = "live-idle-no-report",
                        OwnerRole = pending.DelegatingRole ?? ownerRole,
                        SubjectRole = pending.RecipientRole,
                        Source = "herdr.activity",
                        Summary = $"Recipient '{pending.RecipientRole}' is live-idle with no report beyond the declared {declaredBoundSeconds!.Value}s threshold; inspect the recorded terminal pane. No recovery sequence was entered.",
                        DetectableAt = pending.DispatchedAt.AddSeconds(declaredBoundSeconds!.Value),
                        WakeSuppressed = !IsOwnerSubject(pending.RecipientRole),
                        WorkspaceId = pending.WorkspaceId,
                        PaneId = pending.PaneId,
                        RegistrationDefinition = "a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane",
                        RegistrationLookup = $"notify pending liveness lookup source='{activity.Source}' for recipient='{pending.RecipientIdentity}' at workspace='{pending.WorkspaceId}' pane='{pending.PaneId}'",
                        RegistrationResult = $"running={activity.Running}; agent_status='{activity.AgentStatus ?? "missing"}'; state_change_seq={activity.StateChangeSequence?.ToString(CultureInfo.InvariantCulture) ?? "missing"}",
                        ConsultedObservations =
                        [
                            $"activity:{paneKey}: running={activity.Running}; agent_status={activity.AgentStatus ?? "missing"}; state_change_seq={activity.StateChangeSequence?.ToString(CultureInfo.InvariantCulture) ?? "missing"}",
                        ],
                        Evidence =
                        [
                            "registration_definition:a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane",
                            $"registration_lookup:notify pending liveness lookup source='{activity.Source}' for recipient='{pending.RecipientIdentity}' at workspace='{pending.WorkspaceId}' pane='{pending.PaneId}'",
                            $"registration_result:running={activity.Running}; agent_status='{activity.AgentStatus ?? "missing"}'; state_change_seq={activity.StateChangeSequence?.ToString(CultureInfo.InvariantCulture) ?? "missing"}",
                            $"consulted_observations:activity:{paneKey}",
                        ],
                    });
                }
            }

            observations.AddRange(ReadRecordedSeatTransitions(
                now,
                trigger,
                previousCycle,
                observedSequences,
                observedTimes,
                observedStatuses,
                observedInteractiveReadiness,
                observedStatusConsecutiveCounts,
                observedStatusRunFrom,
                emissionPolicy.DebounceConsecutiveObservations));
            foreach (var action in legacy.Actions)
            {
                var record = openByTask.GetValueOrDefault(action.TaskId);
                var registrationLoss = string.Equals(
                    action.Verdict,
                    NotifyPendingLivenessResult.RegistrationLostProcessPresent,
                    StringComparison.Ordinal);
                var paneKey = record is { WorkspaceId: not null, PaneId: not null }
                    ? $"registration:{record.WorkspaceId}:{record.PaneId}"
                    : $"registration:{record?.RecipientIdentity ?? action.RecipientRole}";
                observations.Add(new NotifySupervisionObservation
                {
                    Key = registrationLoss ? paneKey : $"recipient:{action.TaskId}",
                    Kind = registrationLoss
                        ? NotifyPendingLivenessResult.RegistrationLostProcessPresent
                        : "recipient-lost",
                    OwnerRole = record?.DelegatingRole ?? ownerRole,
                    SubjectRole = action.RecipientRole,
                    Source = registrationLoss ? "notify-pending-liveness" : "notify-pending",
                    Summary = action.Summary,
                    DetectableAt = null,
                    WakeAlreadyAttempted = registrationLoss ? false : action.Recovered,
                    WakeAlreadyDelivered = registrationLoss ? false : action.Recovered && action.Cause is null,
                    ResendPermitted = registrationLoss ? true : null,
                    WakeCause = action.Cause,
                    WorkspaceId = registrationLoss ? record?.WorkspaceId : null,
                    PaneId = registrationLoss ? record?.PaneId : null,
                    RegistrationDefinition = registrationLoss
                        ? "a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane"
                        : null,
                    RegistrationLookup = registrationLoss
                        ? $"notify pending liveness lookup for recipient='{action.RecipientRole}' at workspace='{record?.WorkspaceId ?? "missing"}' pane='{record?.PaneId ?? "missing"}'"
                        : null,
                    RegistrationResult = registrationLoss ? action.Verdict : null,
                    ConsultedObservations = registrationLoss
                        ? [$"notify-pending-liveness: verdict={action.Verdict}; source={action.Cause ?? "recorded liveness"}"]
                        : null,
                    Evidence = registrationLoss
                        ?
                        [
                            "registration_definition:a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane",
                            $"registration_lookup:notify pending liveness lookup for recipient='{action.RecipientRole}' at workspace='{record?.WorkspaceId ?? "missing"}' pane='{record?.PaneId ?? "missing"}'",
                            $"registration_result:{action.Verdict}",
                            $"consulted_observations:notify-pending-liveness verdict={action.Verdict}",
                        ]
                        : null,
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(repo))
        {
            try
            {
                var stalled = stalledWorkAnalyzer?.Invoke()
                    ?? AutomationStalledWorkCommand.Analyze(
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
                    // Informational lifecycle observations, including an
                    // actively claimed repair and pending CI, are not wake
                    // findings. Actionable terminal classes are retained.
                    if (item.IsInformational
                        && !string.Equals(
                            item.Kind,
                            AutomationStalledWorkCommand.KindAwaitingOperatorMerge,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var reference = item.Pr?.Number.ToString(CultureInfo.InvariantCulture)
                        ?? item.Issue?.Number.ToString(CultureInfo.InvariantCulture)
                        ?? "none";
                    var continuation = TryProjectContinuationFinding(item);
                    observations.Add(new NotifySupervisionObservation
                    {
                        Key = item.DedupeKey is { Length: > 0 }
                            ? $"stalled:{item.DedupeKey}"
                            : $"stalled:{item.Kind}:{item.ExecutionUnit}:{reference}",
                        Kind = continuation?.Kind ?? item.Kind,
                        OwnerRole = string.Equals(
                            item.Kind,
                            AutomationStalledWorkCommand.KindAwaitingOperatorMerge,
                            StringComparison.Ordinal)
                            ? DesignRole
                            : ownerRole,
                        Source = "automation-stalled-work",
                        Summary = continuation?.Summary ?? item.RecommendedAction,
                        Evidence = continuation?.Evidence,
                        OwedTransition = continuation?.OwedTransition ?? item.OwedTransition,
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

        var acknowledgedEscalations = state.StallHistory
            .Where(record => string.Equals(record.Kind, "undelivered-escalation", StringComparison.Ordinal)
                && record.WakeDelivered)
            .Select(record => record.Key)
            .ToHashSet(StringComparer.Ordinal);
        observations.AddRange(ReadUndeliveredEscalations(now, previousCycle is not null)
            .Where(observation => !acknowledgedEscalations.Contains(observation.Key)));
        observations.AddRange(ReadUndeliveredReportOutbox());
        if (!transportUnavailable)
        {
            observations.AddRange(ReadRecipeDriftObservations());
            observations.AddRange(ReadObservedPrompts(state.PromptAudits, cycleId));
            observations.AddRange(ReadAbsentSeats(now));
        }

        if (absentSinceLastCycle)
        {
            observations.Add(new NotifySupervisionObservation
            {
                Key = "supervisor:liveness",
                Kind = "supervisor-not-running",
                OwnerRole = ownerRole,
                Source = "supervision-cycle",
                Summary = bound.BoundSeconds is { } absenceDeclaredSeconds
                    ? $"Supervision was absent for {gapSeconds}s, beyond the declared {absenceDeclaredSeconds}s detection bound."
                    : $"Supervision was absent for {gapSeconds}s, beyond the configured {cadenceIntervalSeconds}s cadence's {absenceThresholdSeconds}s self-absence threshold; no detection bound was declared.",
                DetectableAt = null,
                WakeAlreadyAttempted = false,
                WakeAlreadyDelivered = false,
            });
        }

        if (observationProvider is not null)
        {
            observations.AddRange(observationProvider(now));
        }

        // G707: make a finding earn its conclusion from the observations
        // already collected in this cycle.  This is deliberately before the
        // durable emission loop so the resulting conflict is itself subject
        // to G699's same-key cadence and park state.
        observations = CorroborateSameCycleObservations(
            observations,
            observedStatuses,
            observedInteractiveReadiness);

        var currentKeys = observations.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var records = new List<NotifySupervisionStallRecord>();
        var findings = new List<NotifySupervisionFinding>();
        foreach (var observation in observations
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.First()))
        {
            var existing = state.ActiveStalls.GetValueOrDefault(observation.Key);
            if (existing is not null
                && observation.WakeSuppressed
                && !string.Equals(observation.Kind, ObservationConflictKind, StringComparison.Ordinal))
            {
                // Activity-backed live-idle is an informational, once-only
                // finding. Keep its durable record active, but do not emit a
                // duplicate result on unchanged cycles and never wake or enter
                // the G630 recovery path for it.
                var retained = RefreshEmissionState(
                    existing,
                    observation,
                    wake: null,
                    now,
                    emissionPolicy);
                PersistStallUpdate(retained.Record, warnings, "supervision-stall-update-write-failed");
                records.Add(retained.Record);
                if (observation.Kind is "supervision-degraded" or "recipe-drift" or "profile-invalid")
                {
                    // Cycle-level facts are not once-only activity findings:
                    // surface one finding for each cycle, still once for the
                    // cycle rather than once per delegation or observation.
                    findings.Add(ToFinding(retained.Record));
                }
                else if (string.Equals(observation.Kind, "duplicate-supervisor", StringComparison.Ordinal)
                    && (!observation.UseEmissionBackoff || retained.ShouldEmit))
                {
                    // G704: duplicate writers are cycle observations too, but
                    // they must use the same recorded backoff/park contract as
                    // every other repeated key.
                    findings.Add(ToFinding(retained.Record));
                }
                continue;
            }

            if (existing is not null && existing.WakeDelivered)
            {
                var retained = RefreshEmissionState(
                    existing,
                    observation,
                    wake: null,
                    now,
                    emissionPolicy);
                records.Add(retained.Record);
                PersistStallUpdate(retained.Record, warnings, "supervision-stall-update-write-failed");
                if (string.Equals(
                    observation.Kind,
                    AutomationStalledWorkCommand.KindAwaitingOperatorMerge,
                    StringComparison.Ordinal))
                {
                    // G678: entering the patient state notifies design once.
                    // An unchanged supervision cycle keeps the durable state
                    // active but emits no repeat finding and sends no repeat
                    // wake. Human wait duration is never escalation evidence.
                    continue;
                }
                if (retained.ShouldEmit)
                {
                    findings.Add(ToFinding(retained.Record));
                }
                continue;
            }

            var wake = observation.WakeSuppressed
                ? new NotifySupervisionWakeResult { Attempted = false, Delivered = false, Summary = "Finding is informational; inspect the terminal and do not enter recovery." }
                : observation.WakeAlreadyAttempted && existing is null
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
            RecordSettledTransitionChain(observation, wake, now, warnings);
            var baseRecord = existing ?? new NotifySupervisionStallRecord
            {
                Key = observation.Key,
                Kind = observation.Kind,
                OwnerRole = observation.OwnerRole,
                SubjectRole = observation.SubjectRole,
                WakeTargetRole = ResolveWakeTarget(observation),
                WakeClass = ResolveWakeClass(observation),
                Source = observation.Source,
                Summary = observation.Summary,
                Cause = observation.Cause,
                ResendPermitted = observation.ResendPermitted,
                Prompt = observation.Prompt,
                Evidence = observation.Evidence,
                OwedTransition = observation.OwedTransition,
                RegistrationDefinition = observation.RegistrationDefinition,
                RegistrationLookup = observation.RegistrationLookup,
                RegistrationResult = observation.RegistrationResult,
                ConsultedObservations = observation.ConsultedObservations,
                DetectableAt = previousCycle is null ? null : observation.DetectableAt,
                DetectableAtUnknown = previousCycle is null || observation.DetectableAt is null,
                SurfacedAt = now,
            };
            var updated = RefreshEmissionState(
                baseRecord,
                observation,
                wake,
                now,
                emissionPolicy);
            records.Add(updated.Record);
            if (updated.ShouldEmit)
            {
                findings.Add(ToFinding(updated.Record));
            }
            PersistStallUpdate(
                updated.Record,
                warnings,
                existing is null
                    ? "supervision-stall-write-failed"
                    : existing.WakeAttempted && !existing.WakeDelivered && wake.Attempted
                        ? "supervision-stall-retry-write-failed"
                        : "supervision-stall-update-write-failed");
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

        var completedAt = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var cycle = new NotifySupervisionCycle
        {
            CycleId = cycleId,
            StartedAt = now,
            CompletedAt = completedAt,
            Writer = writerIdentity,
            Trigger = trigger,
            IntervalSeconds = intervalSeconds,
            RepeatBackoffSeconds = emissionPolicy.RepeatBackoffSeconds,
            DebounceConsecutiveObservations = emissionPolicy.DebounceConsecutiveObservations,
            CadenceIntervalSeconds = cadenceIntervalSeconds,
            BoundSeconds = bound.BoundSeconds,
            ActualIntervalSeconds = actualIntervalSeconds,
            BoundMet = boundMet,
            AbsenceThresholdSeconds = absenceThresholdSeconds,
            AbsenceThresholdKind = absenceThresholdKind,
            AbsentSinceLastCycle = absentSinceLastCycle,
            GapSeconds = gapSeconds,
            BoundBelowInterval = boundBelowInterval,
            LastObservedStateChangeSequences = observedSequences,
            LastObservedStateChangeTimes = observedTimes,
            LastObservedAgentStatuses = observedStatuses,
            LastObservedAgentStatusConsecutiveCounts = observedStatusConsecutiveCounts,
            LastObservedAgentStatusRunFrom = observedStatusRunFrom,
            Transitions = observations
                .Where(observation => string.Equals(observation.Kind, "seat-state-transition", StringComparison.Ordinal))
                .Select(observation =>
                {
                    var finding = findings.FirstOrDefault(item => string.Equals(item.Key, observation.Key, StringComparison.Ordinal));
                    return new NotifySupervisionTransition
                    {
                        Key = observation.Key,
                        Role = observation.SubjectRole!,
                        WorkspaceId = observation.WorkspaceId!,
                        PaneId = observation.PaneId!,
                        FromStatus = observation.FromStatus!,
                        ToStatus = observation.ToStatus!,
                        StateChangeSequence = observation.StateChangeSequence!.Value,
                        Source = observation.Source,
                        ObservedAt = now,
                        LatencySeconds = observation.StateChangedAt is { } changedAt
                            ? Math.Max(0, (long)(now - changedAt).TotalSeconds)
                            : null,
                        WakeAttempted = finding?.WakeAttempted ?? false,
                        WakeDelivered = finding?.WakeDelivered ?? false,
                    };
                })
                .ToArray(),
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
            EmissionPolicy = emissionPolicy,
            PreApprovalPolicy = preApprovalPolicy,
            Liveness = liveness,
            Warnings = warnings,
        };
    }

    public int RunLoop(TextWriter writer, CancellationToken cancellationToken, bool once)
    {
        var first = RunOnce();
        EmitPass(writer, first, force: once || eventMode);
        if (once || cancellationToken.IsCancellationRequested)
        {
            return first.ExitCode;
        }

        using var eventCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? eventMonitor = null;
        if (eventMode && !first.Findings.Any(finding => string.Equals(finding.Kind, "supervision-degraded", StringComparison.Ordinal)))
        {
            eventMonitor = new NotifySupervisionEventMonitor(
                routingRoot,
                domain,
                team,
                runner,
                herdrExecutable,
                role =>
                {
                    var pass = RunOnce("event");
                    EmitPass(writer, pass, force: true);
                    return Task.CompletedTask;
                },
                waitEvent => RecordWaitEvent(writer, waitEvent)).RunAsync(eventCancellation.Token);
        }

        var exitCode = first.ExitCode;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NotifySupervisor.Delay(TimeSpan.FromSeconds(intervalSeconds));
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                var pass = RunOnce();
                exitCode = pass.ExitCode;
                EmitPass(writer, pass, force: false);
            }
        }
        finally
        {
            eventCancellation.Cancel();
            if (eventMonitor is not null)
            {
                try
                {
                    eventMonitor.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (eventCancellation.IsCancellationRequested)
                {
                }
            }
        }
        return exitCode;
    }

    private void EmitPass(TextWriter writer, NotifySupervisorPass pass, bool force)
    {
        if (pass.Silent && !force)
        {
            return;
        }
        lock (writerSync)
        {
            NotifyCommand.EmitSupervision(
                writer, pass, domain, team, intervalSeconds, autoRedispatch, write, format, eventMode);
        }
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

    private NotifySupervisionEmissionPolicy ResolveEmissionPolicy(
        NotifySupervisionReadResult state,
        DateTimeOffset now)
    {
        var repeatBackoffSeconds = configuredRepeatBackoffSeconds
            ?? state.EmissionPolicy?.RepeatBackoffSeconds
            ?? NotifySupervisionEmissionPolicy.DefaultRepeatBackoffSeconds;
        var debounceConsecutiveObservations = configuredDebounceConsecutiveObservations
            ?? state.EmissionPolicy?.DebounceConsecutiveObservations
            ?? NotifySupervisionEmissionPolicy.DefaultDebounceConsecutiveObservations;
        return new NotifySupervisionEmissionPolicy
        {
            Domain = domain,
            Team = team,
            FullCadenceSeconds = intervalSeconds,
            RepeatBackoffSeconds = repeatBackoffSeconds,
            DebounceConsecutiveObservations = debounceConsecutiveObservations,
            RecordedAt = now,
        };
    }

    private NotifyPreApprovalPolicyStatus ResolvePreApprovalPolicy(
        DateTimeOffset now,
        string cycleId,
        out string? error)
    {
        error = null;
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var read = NotifyPreApprovalPolicyStore.Read(artifactRoot, domain, team);
        if (!read.Resolved)
        {
            error = $"pre-approval-policy-unreadable: {read.Error}";
            return MissingPolicyStatus(read.Path, "unreadable");
        }

        var declarationSupplied = preApprovalAccept.Count > 0
            || preApprovalEscalate.Count > 0
            || scopedPolicies.Count > 0;
        if (declarationSupplied)
        {
            var boundScopedPolicies = BindScopedPoliciesToCycle(scopedPolicies, cycleId, out var bindingError);
            if (bindingError is not null)
            {
                error = $"pre-approval-policy-cycle-binding-failed: {bindingError}";
                return MissingPolicyStatus(read.Path, "invalid-cycle-binding");
            }

            var policy = NotifyPreApprovalPolicyStore.WithCurrentApplicability(new NotifyPreApprovalPolicy
            {
                Domain = domain,
                Team = team,
                RecordedAt = now,
                Accept = preApprovalAccept,
                Escalate = preApprovalEscalate,
                ScopedPolicies = boundScopedPolicies,
            });
            var recorded = NotifyPreApprovalPolicyStore.Record(artifactRoot, policy, write);
            if (recorded.Error is not null)
            {
                error = $"pre-approval-policy-write-failed: {recorded.Error}";
            }
            return new NotifyPreApprovalPolicyStatus
            {
                Recorded = recorded.Applied,
                Status = policy.Applicable
                    ? recorded.Applied ? "recorded" : "preview-unrecorded"
                    : recorded.Applied ? "recorded-inapplicable" : "preview-inapplicable",
                DefaultDecision = "escalate",
                Path = recorded.Path,
                Accept = policy.Accept,
                Escalate = policy.Escalate,
                ScopedPolicies = policy.ScopedPolicies,
                Applicable = policy.Applicable,
                ApplicabilityStatus = policy.ApplicabilityStatus,
                InapplicableAgentKinds = policy.InapplicableAgentKinds,
                InapplicabilityReason = policy.InapplicabilityReason,
                Summary = PolicySummary(policy, recorded.Applied, write),
            };
        }

        if (read.Policy is { } existing)
        {
            if (read.RefreshRequired && write)
            {
                var refreshed = NotifyPreApprovalPolicyStore.Record(artifactRoot, existing, write: true);
                if (refreshed.Error is not null)
                {
                    error = $"pre-approval-policy-write-failed: {refreshed.Error}";
                }
            }
            return new NotifyPreApprovalPolicyStatus
            {
                Recorded = true,
                Status = existing.Applicable ? "recorded" : "recorded-inapplicable",
                DefaultDecision = "escalate",
                Path = read.Path,
                Accept = existing.Accept,
                Escalate = existing.Escalate,
                ScopedPolicies = existing.ScopedPolicies,
                Applicable = existing.Applicable,
                ApplicabilityStatus = existing.ApplicabilityStatus,
                InapplicableAgentKinds = existing.InapplicableAgentKinds,
                InapplicabilityReason = existing.InapplicabilityReason,
                Summary = PolicySummary(existing, recorded: true, write),
            };
        }

        return MissingPolicyStatus(read.Path, "escalate-only");
    }

    private static IReadOnlyList<NotifyScopedPromptPolicy> BindScopedPoliciesToCycle(
        IReadOnlyList<NotifyScopedPromptPolicy> policies,
        string cycleId,
        out string? error)
    {
        error = null;
        var bound = new List<NotifyScopedPromptPolicy>(policies.Count);
        foreach (var policy in policies)
        {
            if (!string.Equals(policy.Scope, ShellCommandPolicyRegistry.OwnedScratchDeleteScope, StringComparison.Ordinal))
            {
                bound.Add(policy);
                continue;
            }

            if (string.IsNullOrWhiteSpace(policy.ScratchLedgerCycleId))
            {
                bound.Add(policy with { ScratchLedgerCycleId = cycleId });
                continue;
            }

            if (!string.Equals(policy.ScratchLedgerCycleId, cycleId, StringComparison.Ordinal))
            {
                error = $"owned-scratch-delete policy '{policy.PolicyId}' carries scratch_ledger_cycle_id '{policy.ScratchLedgerCycleId}', which is not the current wake/cycle identity '{cycleId}'; stale identities are refused.";
                return [];
            }

            bound.Add(policy);
        }

        return bound;
    }

    private static NotifyPreApprovalPolicyStatus MissingPolicyStatus(string path, string status) => new()
    {
        Recorded = false,
        Status = status,
        DefaultDecision = "escalate",
        Path = path,
        Accept = [],
        Escalate = [],
        ScopedPolicies = [],
        Applicable = false,
        ApplicabilityStatus = "not-recorded",
        InapplicableAgentKinds = [],
        InapplicabilityReason = null,
        Summary = "No per-team pre-approval policy is recorded; orchestration must escalate every residual prompt and accept none.",
    };

    private static string PolicySummary(NotifyPreApprovalPolicy policy, bool recorded, bool write)
    {
        if (!policy.Applicable)
        {
            var kinds = string.Join(", ", policy.InapplicableAgentKinds.Select(kind => $"'{kind}'"));
            var prefix = recorded
                ? "The per-team pre-approval policy is recorded but inapplicable"
                : write
                    ? "The per-team pre-approval policy could not be recorded and is inapplicable"
                    : "Dry-run: the proposed per-team pre-approval policy is inapplicable";
            return $"{prefix}: one or more recorded rules or scoped policies are unavailable for agent kind(s) {kinds}. "
                + "The affected entries cannot currently apply; residual prompts for uncovered or invalid scopes are escalate-only by construction.";
        }

        var shellSummary = policy.ScopedPolicies.Count == 0
            ? string.Empty
            : $" {policy.ScopedPolicies.Count} scoped shell policy instance(s) are recorded; shell answers require a matching AST scope and remain orchestration-only.";
        return recorded
            ? "A durable per-team pre-approval policy is recorded and every named agent kind has a prompt-class producer; every unmatched prompt shape escalates." + shellSummary
            : "Dry-run: every named agent kind has a prompt-class producer, but the per-team pre-approval policy was not recorded. Until a write succeeds, adjudication remains escalate-only." + shellSummary;
    }

    private static int FallbackAbsenceThresholdSeconds(int cadenceSeconds) =>
        Math.Max(cadenceSeconds * 2, cadenceSeconds + FallbackAbsenceHeadroomSeconds);

    private NotifySupervisionObservation? DetectDuplicateSupervisor(
        NotifySupervisionCycle? previousCycle,
        NotifySupervisionInstalledSupervisor? installedSupervisor,
        DateTimeOffset now,
        int recentThresholdSeconds)
    {
        var installedWriter = installedSupervisor?.Writer;
        if (installedWriter is not null
            && !writerIdentity.IsSameWriter(installedWriter)
            && writerIsLive(installedWriter))
        {
            var installedAgeSeconds = previousCycle is null
                ? 0
                : Math.Max(0, (long)(now - previousCycle.CompletedAt).TotalSeconds);
            var installedLabel = $"intent-cli.supervise.{domain}.{team}";
            return new NotifySupervisionObservation
            {
                Key = "supervisor:duplicate",
                Kind = "duplicate-supervisor",
                OwnerRole = ownerRole,
                Source = "supervision-cycle",
                Summary = $"Duplicate supervisor detected: current writer pid={writerIdentity.Pid}, process_start_time={writerIdentity.ProcessStartTime:O}, host='{writerIdentity.Host}' differs from installed writer pid={installedWriter.Pid}, process_start_time={installedWriter.ProcessStartTime:O}, host='{installedWriter.Host}'; latest cycle age={installedAgeSeconds}s (recent threshold={recentThresholdSeconds}s). Duplicate-wake cost: both writers can wake the same stall, duplicating wakes for the same stall. Remedy: converge on the G658 per-team scheduler label '{installedLabel}'. Detection only; no terminal-content evidence was used, and no process was killed, stopped, ranked, elected, locked, or leased.",
                Cause = $"duplicate-writer:current={writerIdentity.Pid}/{writerIdentity.ProcessStartTime:O}/{writerIdentity.Host};installed={installedWriter.Pid}/{installedWriter.ProcessStartTime:O}/{installedWriter.Host}",
                DetectableAt = previousCycle?.CompletedAt ?? installedSupervisor!.RecordedAt,
                WakeSuppressed = true,
                UseEmissionBackoff = true,
            };
        }

        var otherWriter = previousCycle?.Writer;
        if (otherWriter is null
            || writerIdentity.IsSameWriter(otherWriter)
            || !writerIsLive(otherWriter))
        {
            return null;
        }

        var ageSeconds = Math.Max(0, (long)(now - previousCycle!.CompletedAt).TotalSeconds);
        if (ageSeconds > recentThresholdSeconds)
        {
            return null;
        }

        var label = $"intent-cli.supervise.{domain}.{team}";
        return new NotifySupervisionObservation
        {
            Key = "supervisor:duplicate",
            Kind = "duplicate-supervisor",
            OwnerRole = ownerRole,
            Source = "supervision-cycle",
            Summary = $"Duplicate supervisor detected: current writer pid={writerIdentity.Pid}, process_start_time={writerIdentity.ProcessStartTime:O}, host='{writerIdentity.Host}'; other live writer pid={otherWriter.Pid}, process_start_time={otherWriter.ProcessStartTime:O}, host='{otherWriter.Host}'; other cycle age={ageSeconds}s (recent threshold={recentThresholdSeconds}s). Duplicate-wake cost: both writers can wake the same stall, duplicating wakes for the same stall. Remedy: converge on the G658 per-team scheduler label '{label}'. Detection only; no process was killed, stopped, ranked, elected, locked, or leased, and duplicate seat processes are out of scope.",
            DetectableAt = previousCycle.CompletedAt,
            WakeSuppressed = true,
        };
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

        var wakeTargetRole = ResolveWakeTarget(observation);
        if (string.IsNullOrWhiteSpace(wakeTargetRole))
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = false,
                Delivered = false,
                Cause = "wake-target-role-missing",
                Summary = "The finding has no eligible wake target role; no transport was invented.",
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
            DelegatingRole = wakeTargetRole,
            RecipientRole = wakeTargetRole,
            ReportToRole = wakeTargetRole,
            RecipientIdentity = $"supervision-target={wakeTargetRole}",
            ExpectedArtifact = "supervision finding acknowledgement",
            ExpectedArtifacts = ["supervision finding acknowledgement"],
            Objective = observation.Summary,
            Inputs = [observation.Source],
            DispatchedAt = now,
            TransportMode = resolution.Mode,
        };
        if (observation.Prompt is { } prompt)
        {
            return WakeAndAdjudicatePrompt(record, observation, prompt, now);
        }
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
                subject_role = observation.SubjectRole,
                wake_target_role = wakeTargetRole,
                wake_class = ResolveWakeClass(observation),
                must_transition = false,
            }),
            runner,
            herdrExecutable,
            agmsgScriptsDirectory,
            bashExecutable);
        return new NotifySupervisionWakeResult
        {
            Attempted = true,
            Delivered = delivery.Resolved && delivery.Delivered,
            Cause = delivery.Resolved && delivery.Delivered ? null : delivery.Cause ?? "wake-undelivered",
            Summary = delivery.Summary,
        };
    }

    private NotifySupervisorDeliveryResult DeliverRecoveryNotification(
        NotifyPendingDelegation record,
        string notification)
    {
        if (!IsOwnerSubject(record.RecipientRole))
        {
            return NotifySupervisorDelivery.Send(
                routingRoot,
                record,
                notification,
                runner,
                herdrExecutable,
                agmsgScriptsDirectory,
                bashExecutable);
        }

        var payload = JsonNode.Parse(notification)?.AsObject() ?? new JsonObject();
        payload["subject_role"] = record.RecipientRole;
        payload["wake_target_role"] = DesignRole;
        payload["wake_class"] = "escalation";
        return NotifySupervisorDelivery.Send(
            routingRoot,
            record with { DelegatingRole = DesignRole },
            payload.ToJsonString(),
            runner,
            herdrExecutable,
            agmsgScriptsDirectory,
            bashExecutable);
    }

    private bool IsOwnerSubject(string? subjectRole) =>
        string.Equals(subjectRole, ownerRole, StringComparison.Ordinal);

    private string ResolveWakeTarget(NotifySupervisionObservation observation) =>
        observation.Prompt?.AdjudicationTargetRole
            ?? (IsOwnerSubject(observation.SubjectRole) ? DesignRole : observation.OwnerRole);

    private string? ResolveWakeClass(NotifySupervisionObservation observation) =>
        observation.Prompt is { Decision: "accept" }
            ? "bounded-prompt-answer"
            : observation.Prompt is not null || IsOwnerSubject(observation.SubjectRole) ? "escalation" : null;

    private IReadOnlyList<NotifySupervisionObservation> ReadUndeliveredEscalations(
        DateTimeOffset now,
        bool priorCycleExists)
    {
        var observations = new List<NotifySupervisionObservation>();
        var judgment = NotifyRecipientDeliveryJudgment.Resolve(
            routingRoot,
            domain,
            team,
            DesignRole);
        var recordedReaderPath = judgment.UsesRecordedReaderAppend ? judgment.Target : null;
        NotifyEventWriter.TryResolveReadPath(
            routingRoot,
            domain,
            team,
            recordedReaderPath,
            out var path,
            out _);

        if (path is not null && File.Exists(path))
        {
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

                    // The event's existence proves its append.  The shared
                    // residency contract decides whether that append is
                    // delivery or whether a pane wake is still required.
                    if (judgment.Judge(readerAppendSucceeded: true, paneWakeDelivered: false))
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
                        Summary = $"Escalation '{unit}' is durable in events.jsonl but delivery basis '{judgment.Basis ?? "unresolved"}' was not satisfied; the pane-resident role was not woken.",
                        DetectableAt = priorCycleExists ? timestamp : null,
                        WakeAlreadyAttempted = false,
                        WakeAlreadyDelivered = false,
                    });
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                observations.Add(new NotifySupervisionObservation
                {
                    Key = "escalation-store:unreadable",
                    Kind = "undelivered-escalation",
                    OwnerRole = ownerRole,
                    Source = "notify-escalate",
                    Summary = $"The escalation event channel could not be read: {exception.Message}",
                    DetectableAt = null,
                });
            }
        }

        var appendFailures = NotifyEscalationFailureStore.Read(routingRoot, domain, team, out var failureError);
        if (failureError is not null)
        {
            observations.Add(new NotifySupervisionObservation
            {
                Key = "escalation-append-failure-store:unreadable",
                Kind = "undelivered-escalation",
                OwnerRole = ownerRole,
                Source = "notify-escalate-append-failed",
                Summary = failureError,
                DetectableAt = null,
                WakeSuppressed = true,
            });
        }

        observations.AddRange(appendFailures.Select(failure => new NotifySupervisionObservation
        {
            Key = $"escalation-append-failed:{failure.TaskId}:{failure.Timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)}",
            Kind = "undelivered-escalation",
            OwnerRole = ownerRole,
            Source = "notify-escalate-append-failed",
            Summary = $"Escalation '{failure.TaskId}' was not appended to recorded reader '{failure.ReaderPath}'; delivery basis '{failure.DeliveryBasis}' remains unsatisfied: {failure.Error}",
            DetectableAt = priorCycleExists ? failure.Timestamp : null,
            WakeSuppressed = true,
        }));

        return observations;
    }

    private IReadOnlyList<NotifySupervisionObservation> ReadUndeliveredReportOutbox()
    {
        var entries = NotifyReportOutboxStore.ReadUndelivered(routingRoot, domain, team, out var error);
        if (error is not null)
        {
            return
            [
                new NotifySupervisionObservation
                {
                    Key = "report-outbox:unreadable",
                    Kind = "undelivered-report-outbox",
                    OwnerRole = ownerRole,
                    Source = "notify-report-outbox",
                    Summary = $"The report outbox could not be read: {error}",
                    WakeSuppressed = true,
                },
            ];
        }

        return entries.Select(entry => new NotifySupervisionObservation
        {
            Key = $"report-outbox:{entry.EntryId ?? $"{entry.TaskId}:{entry.ResultNonce ?? "legacy"}"}",
            Kind = "undelivered-report-outbox",
            OwnerRole = entry.FromRole,
            Source = "notify-report-outbox",
            Summary = $"Report outbox entry for task '{entry.TaskId}' is undelivered at '{NotifyReportOutboxStore.ResolvePath(routingRoot, domain, team)}'. Collect it with `{NotifyReportOutboxStore.BuildCollectCommand(routingRoot, entry)}`; do not re-delegate the task.",
            DetectableAt = entry.CreatedAt,
            WakeSuppressed = true,
        }).ToArray();
    }

    /// <summary>
    /// G707: findings that depend on a registration or activity conclusion
    /// must consult the non-terminal seat observations collected by the same
    /// supervision cycle.  A contradictory producer result becomes one
    /// observation-conflict per recorded seat.  The conflict is intentionally
    /// an ordinary observation so G699 owns its repeated-key backoff/park
    /// behavior and no recovery path is entered for an inconclusive result.
    /// </summary>
    private static List<NotifySupervisionObservation> CorroborateSameCycleObservations(
        IReadOnlyList<NotifySupervisionObservation> observations,
        IReadOnlyDictionary<string, string> observedStatuses,
        IReadOnlyDictionary<string, bool?> observedInteractiveReadiness)
    {
        var candidates = observations
            .Where(IsCorroboratableFinding)
            .Where(observation => IsContradictedBySameCycleObservation(
                observation,
                observedStatuses,
                observedInteractiveReadiness))
            .ToArray();
        if (candidates.Length == 0)
        {
            return observations.ToList();
        }

        var conflicts = candidates
            .GroupBy(ObservationSubjectKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<NotifySupervisionObservation>(observations.Count);
        foreach (var observation in observations)
        {
            var subjectKey = ObservationSubjectKey(observation);
            if (!conflicts.TryGetValue(subjectKey, out var contradictory)
                || !IsCorroboratableFinding(observation))
            {
                result.Add(observation);
                continue;
            }

            if (emitted.Add(subjectKey))
            {
                result.Add(BuildObservationConflict(
                    contradictory,
                    observedStatuses,
                    observedInteractiveReadiness));
            }
        }

        return result;
    }

    private static bool IsCorroboratableFinding(NotifySupervisionObservation observation) =>
        observation.Kind is NotifyPendingLivenessResult.RegistrationLostProcessPresent or "live-idle-no-report"
        && !string.IsNullOrWhiteSpace(observation.WorkspaceId)
        && !string.IsNullOrWhiteSpace(observation.PaneId);

    private static bool IsContradictedBySameCycleObservation(
        NotifySupervisionObservation observation,
        IReadOnlyDictionary<string, string> observedStatuses,
        IReadOnlyDictionary<string, bool?> observedInteractiveReadiness)
    {
        var seatKey = $"seat-state:{observation.WorkspaceId}:{observation.PaneId}";
        var hasStatus = observedStatuses.TryGetValue(seatKey, out var status);
        var hasReady = observedInteractiveReadiness.TryGetValue(seatKey, out var interactiveReady);
        if (!hasStatus && !hasReady)
        {
            return false;
        }

        return observation.Kind == NotifyPendingLivenessResult.RegistrationLostProcessPresent
            ? status is "working" or "idle" || interactiveReady == true
            : status == "working" || interactiveReady == true;
    }

    private static string ObservationSubjectKey(NotifySupervisionObservation observation) =>
        $"{observation.WorkspaceId}:{observation.PaneId}";

    private static NotifySupervisionObservation BuildObservationConflict(
        IReadOnlyList<NotifySupervisionObservation> contradictory,
        IReadOnlyDictionary<string, string> observedStatuses,
        IReadOnlyDictionary<string, bool?> observedInteractiveReadiness)
    {
        var first = contradictory[0];
        var seatKey = $"seat-state:{first.WorkspaceId}:{first.PaneId}";
        var status = observedStatuses.TryGetValue(seatKey, out var observedStatus)
            ? observedStatus
            : "missing";
        var readiness = observedInteractiveReadiness.TryGetValue(seatKey, out var observedReady)
            ? observedReady?.ToString() ?? "missing"
            : "missing";
        var kinds = string.Join(", ", contradictory.Select(item => item.Kind).Distinct(StringComparer.Ordinal));
        var role = first.SubjectRole ?? "unknown";
        var definition = "a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane";
        var lookup = string.Join(
            "; ",
            contradictory.Select(item => item.RegistrationLookup ?? $"producer='{item.Source}' lookup was not named"));
        var result = $"inconclusive; producer_findings=[{kinds}]; same-cycle seat-state agent_status='{status}', interactive_ready={readiness}";
        var consulted = contradictory
            .Select(item => $"producer:{item.Kind}; source={item.Source}; key={item.Key}")
            .Append($"seat-state:{seatKey}; agent_status={status}; interactive_ready={readiness}")
            .ToArray();

        return new NotifySupervisionObservation
        {
            Key = $"{ObservationConflictKind}:{first.WorkspaceId}:{first.PaneId}",
            Kind = ObservationConflictKind,
            OwnerRole = first.OwnerRole,
            SubjectRole = role,
            Source = "supervision-cycle.corroboration",
            Summary = $"Verification first: same-cycle observation-conflict for recorded seat '{role}' at workspace '{first.WorkspaceId}' pane '{first.PaneId}'. The {kinds} conclusion is inconclusive because seat-state observed agent_status='{status}' and interactive_ready={readiness}. Compare the registration lookup with these consulted observations before any recovery decision; no automatic action is authorized.",
            DetectableAt = first.DetectableAt,
            WakeSuppressed = true,
            WorkspaceId = first.WorkspaceId,
            PaneId = first.PaneId,
            Evidence =
            [
                "same-cycle-corroboration: contradictory non-terminal observation retained",
                $"registration_definition:{definition}",
                $"registration_lookup:{lookup}",
                $"registration_result:{result}",
                $"consulted_observations:{string.Join(" | ", consulted)}",
            ],
            RegistrationDefinition = definition,
            RegistrationLookup = lookup,
            RegistrationResult = result,
            ConsultedObservations = consulted,
        };
    }

    private IReadOnlyList<NotifySupervisionObservation> ReadRecordedSeatTransitions(
        DateTimeOffset now,
        string trigger,
        NotifySupervisionCycle? previousCycle,
        IDictionary<string, long> observedSequences,
        IDictionary<string, DateTimeOffset> observedTimes,
        IDictionary<string, string> observedStatuses,
        IDictionary<string, bool?> observedInteractiveReadiness,
        IDictionary<string, int> observedStatusConsecutiveCounts,
        IDictionary<string, string> observedStatusRunFrom,
        int debounceConsecutiveObservations)
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
            var workspaceId = recorded.WorkspaceId ?? topology.Topology.WorkspaceId;
            var agent = agents.SingleOrDefault(candidate =>
                string.Equals(candidate.WorkspaceId, workspaceId, StringComparison.Ordinal)
                && string.Equals(candidate.PaneId, recorded.PaneId, StringComparison.Ordinal));
            var seatKey = $"seat-state:{workspaceId}:{recorded.PaneId}";
            if (agent is not null)
            {
                // Readiness is retained even when the agent status/sequence
                // is incomplete.  A positive same-cycle readiness result is
                // still a non-terminal observation that can contradict a
                // missing-registration conclusion.
                observedInteractiveReadiness[seatKey] = agent.InteractiveReady;
            }
            if (agent?.AgentStatus is null || agent.StateChangeSequence is not { } sequence)
            {
                continue;
            }

            observedStatuses[seatKey] = agent.AgentStatus;
            if (agent.LastStateChangeAt is { } changedAt)
            {
                observedTimes[seatKey] = changedAt;
            }

            var priorStatus = previousCycle?.LastObservedAgentStatuses.GetValueOrDefault(seatKey);
            var priorSequence = previousCycle?.LastObservedStateChangeSequences.GetValueOrDefault(seatKey);
            var priorCount = previousCycle?.LastObservedAgentStatusConsecutiveCounts.GetValueOrDefault(seatKey) ?? 0;
            var priorRunFrom = previousCycle?.LastObservedAgentStatusRunFrom.GetValueOrDefault(seatKey);
            var consecutiveCount = string.Equals(priorStatus, agent.AgentStatus, StringComparison.Ordinal)
                ? Math.Max(1, priorCount) + 1
                : 1;
            var runFrom = string.Equals(priorStatus, agent.AgentStatus, StringComparison.Ordinal)
                ? priorRunFrom ?? priorStatus ?? agent.AgentStatus
                : priorStatus ?? agent.AgentStatus;
            observedStatusConsecutiveCounts[seatKey] = consecutiveCount;
            observedStatusRunFrom[seatKey] = runFrom;
            var settled = agent.AgentStatus is "done" or "blocked" or "idle";
            var pendingSettledTransition = role is ("implementation" or "review")
                && string.Equals(runFrom, "working", StringComparison.Ordinal)
                && settled
                && priorSequence is not null
                && sequence > priorSequence.Value;
            if (!pendingSettledTransition || consecutiveCount >= debounceConsecutiveObservations)
            {
                // A debounce-suppressed settled transition must retain the
                // prior sequence so the single state-change advance remains
                // available when the consecutive-observation threshold is
                // reached. Status, count, and run-from are still recorded on
                // every poll above.
                observedSequences[seatKey] = sequence;
            }

            if (!pendingSettledTransition || consecutiveCount < debounceConsecutiveObservations)
            {
                continue;
            }

            observations.Add(new NotifySupervisionObservation
            {
                Key = $"seat-transition:{workspaceId}:{recorded.PaneId}:{sequence}",
                Kind = "seat-state-transition",
                OwnerRole = ownerRole,
                SubjectRole = role,
                Source = string.Equals(trigger, "event", StringComparison.Ordinal)
                    ? "herdr.agent-wait.event"
                    : "herdr.agent-list.interval",
                Summary = $"Recorded seat '{role}' transitioned working→{agent.AgentStatus} at workspace '{workspaceId}' pane '{recorded.PaneId}' (state_change_seq={sequence}). Wake the owner role for composite verification; this state signal alone does not assert success.",
                DetectableAt = agent.LastStateChangeAt,
                WorkspaceId = workspaceId,
                PaneId = recorded.PaneId,
                FromStatus = "working",
                ToStatus = agent.AgentStatus,
                StateChangeSequence = sequence,
                StateChangedAt = agent.LastStateChangeAt,
                Evidence =
                [
                    $"completion-signal:{BuildSettledTransitionSignalId(workspaceId, recorded.PaneId, sequence)}",
                    $"seat:{role}",
                    "from:working",
                    $"to:{agent.AgentStatus}",
                    $"state-change-seq:{sequence.ToString(CultureInfo.InvariantCulture)}",
                ],
                OwedTransition = "canonical-state-classification",
            });
        }
        return observations;
    }

    private void RecordSettledTransitionChain(
        NotifySupervisionObservation observation,
        NotifySupervisionWakeResult wake,
        DateTimeOffset now,
        ICollection<string> warnings)
    {
        if (!write
            || !string.Equals(observation.Kind, "seat-state-transition", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(observation.WorkspaceId)
            || string.IsNullOrWhiteSpace(observation.PaneId)
            || observation.StateChangeSequence is not { } sequence)
        {
            return;
        }

        var taskId = BuildSettledTransitionTaskId(observation.WorkspaceId, observation.PaneId, sequence);
        var resultNonce = $"state-change-{sequence.ToString(CultureInfo.InvariantCulture)}";
        var report = ContinuationChainStore.RecordReportReceived(
            routingRoot,
            domain,
            team,
            taskId,
            resultNonce,
            "settled",
            $"seat:{observation.SubjectRole ?? "unknown"}:{observation.WorkspaceId}:{observation.PaneId}",
            observation.Summary,
            observation.StateChangedAt ?? now,
            write: true,
            source: "herdr-state-transition");
        if (report.Error is not null || report.Record is null)
        {
            warnings.Add($"continuation-chain-write-failed: {report.Error ?? "settled transition report link was not recorded."}");
            return;
        }

        var signalId = report.Record.CompletionSignalId;
        var chainId = report.Record.ChainId;
        var attempt = ContinuationChainStore.RecordLink(
            routingRoot,
            domain,
            team,
            signalId,
            taskId,
            chainId,
            ContinuationChainStore.OrchestrationWakeAttempted,
            "notify-supervise",
            [
                $"wake-target:{ResolveWakeTarget(observation)}",
                $"wake-attempted:{wake.Attempted.ToString().ToLowerInvariant()}",
            ],
            blocker: wake.Attempted ? null : wake.Cause,
            timestamp: now,
            write: true);
        if (attempt.Error is not null)
        {
            warnings.Add($"continuation-chain-write-failed: {attempt.Error}");
            return;
        }

        if (!wake.Delivered)
        {
            return;
        }

        var delivered = ContinuationChainStore.RecordLink(
            routingRoot,
            domain,
            team,
            signalId,
            taskId,
            chainId,
            ContinuationChainStore.WakeDeliveredOrObserved,
            "notify-supervise",
            ["delivery:observed", $"wake-summary:{wake.Summary}"],
            timestamp: now,
            write: true);
        if (delivered.Error is not null)
        {
            warnings.Add($"continuation-chain-write-failed: {delivered.Error}");
        }
    }

    private static string BuildSettledTransitionTaskId(
        string workspaceId,
        string paneId,
        long sequence) =>
        $"seat-transition:{workspaceId}:{paneId}:{sequence.ToString(CultureInfo.InvariantCulture)}";

    private static string BuildSettledTransitionSignalId(
        string workspaceId,
        string paneId,
        long sequence) => ContinuationChainStore.BuildCompletionSignalId(
            BuildSettledTransitionTaskId(workspaceId, paneId, sequence),
            $"state-change-{sequence.ToString(CultureInfo.InvariantCulture)}");

    internal void RecordWaitEvent(TextWriter writer, NotifySupervisionWaitEvent waitEvent)
    {
        lock (supervisionSync)
        {
            var state = NotifySupervisionStore.Read(
                context.ResolveSupervisionArtifactRootPath(), domain, team);
            var previous = state.LastCycle;
            var cycle = new NotifySupervisionCycle
            {
                CycleId = Guid.NewGuid().ToString("N"),
                StartedAt = waitEvent.ObservedAt,
                CompletedAt = waitEvent.ObservedAt,
                Writer = writerIdentity,
                Trigger = "event-wait",
                IntervalSeconds = intervalSeconds,
                CadenceIntervalSeconds = state.LastIntervalCycle?.CadenceIntervalSeconds ?? intervalSeconds,
                BoundSeconds = declaredBoundSeconds ?? state.Bound?.BoundSeconds,
                AbsenceThresholdSeconds = state.LastIntervalCycle?.AbsenceThresholdSeconds,
                AbsenceThresholdKind = state.LastIntervalCycle?.AbsenceThresholdKind,
                LastObservedStateChangeSequences = previous?.LastObservedStateChangeSequences
                    ?? new Dictionary<string, long>(StringComparer.Ordinal),
                LastObservedStateChangeTimes = previous?.LastObservedStateChangeTimes
                    ?? new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal),
                LastObservedAgentStatuses = previous?.LastObservedAgentStatuses
                    ?? new Dictionary<string, string>(StringComparer.Ordinal),
                WaitEvents = [waitEvent],
            };
            NotifySupervisionStore.RecordCycle(
                NotifySupervisionStore.ResolveCyclePath(
                    context.ResolveSupervisionArtifactRootPath(), domain, team),
                cycle,
                write);
        }

        EmitPass(writer, new NotifySupervisorPass
        {
            Actions = [],
            Warnings =
            [
                $"event-wait-rearmed: role '{waitEvent.Role}' workspace '{waitEvent.WorkspaceId}' pane "
                + $"'{waitEvent.PaneId}' wait died or errored and was re-armed: {waitEvent.Detail}",
            ],
        }, force: true);
    }

    private IReadOnlyList<NotifySupervisionObservation> ReadRecipeDriftObservations()
    {
        var topology = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (!topology.Resolved || topology.Topology is null)
        {
            if (string.Equals(topology.Cause, "profile-invalid", StringComparison.Ordinal))
            {
                return
                [
                    new NotifySupervisionObservation
                    {
                        Key = $"profile-invalid:{domain}:{team}",
                        Kind = "profile-invalid",
                        OwnerRole = ownerRole,
                        Source = "recorded-topology+envelope-profile",
                        Summary = topology.Summary + " No registry fallback is permitted; G686 treats this as a distinct profile-invalid finding.",
                        WakeAlreadyAttempted = false,
                        WakeAlreadyDelivered = false,
                        WakeSuppressed = true,
                        Cause = "profile-invalid",
                    },
                ];
            }
            return [];
        }

        var recordedSeats = topology.Topology.Roles
            .Where(entry => string.Equals(entry.Value.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(entry.Value.PaneId)
                && !string.IsNullOrWhiteSpace(entry.Value.Kind)
                && (entry.Value.EnvelopeProfileOverride is not null
                    || entry.Value.EnvelopeProfileReference is not null
                    || AgentLaunchRecipeRegistry.Find(entry.Value.Kind) is not null))
            .ToArray();
        if (recordedSeats.Length == 0)
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
        foreach (var (role, recorded) in recordedSeats)
        {
            var profile = recorded.EnvelopeProfileOverride
                ?? (recorded.EnvelopeProfileReference is { } profileName
                    ? topology.Topology.EnvelopeProfiles.GetValueOrDefault(profileName)
                    : null);
            var recipe = profile is null ? AgentLaunchRecipeRegistry.Find(recorded.Kind!) : null;
            if (profile is null && recipe is null)
            {
                continue;
            }

            var workspaceId = recorded.WorkspaceId ?? topology.Topology.WorkspaceId;
            var running = agents.Any(agent =>
                string.Equals(agent.WorkspaceId, workspaceId, StringComparison.Ordinal)
                && string.Equals(agent.PaneId, recorded.PaneId, StringComparison.Ordinal)
                && agent.AgentRunning);
            if (!running)
            {
                continue;
            }

            var processInfo = NotifyPaneProcessReader.Read(runner, herdrExecutable, recorded.PaneId!);
            if (!processInfo.Resolved)
            {
                continue;
            }

            var comparison = profile is not null
                ? AgentLaunchShapeComparer.Compare(recorded.Kind!, profile, processInfo.Processes)
                : AgentLaunchShapeComparer.Compare(
                    recorded.Kind!,
                    recipe!,
                    processInfo.Processes,
                    recorded.LaunchArguments,
                    recorded.Cwd,
                    requireConcreteSeatRoots: true);
            if (!comparison.Resolved || comparison.Conforming)
            {
                continue;
            }

            observations.Add(new NotifySupervisionObservation
            {
                Key = $"recipe-drift:{workspaceId}:{recorded.PaneId}",
                Kind = "recipe-drift",
                OwnerRole = ownerRole,
                SubjectRole = role,
                Source = profile is null
                    ? "recorded-topology+agent-launch-recipe+pane.process-info"
                    : "recorded-topology+envelope-profile+pane.process-info",
                Summary = $"Running seat '{role}' at workspace '{workspaceId}' pane '{recorded.PaneId}' has recipe drift: "
                    + $"observed launch shape `{comparison.ObservedShape}`; recorded '{recorded.Kind}' "
                    + (profile is null ? "recipe " : $"envelope profile '{profile.Name}' ")
                    + $"`{comparison.RecordedShape}`. Classification: "
                    + (comparison.Drift == AgentLaunchEnvelopeDrift.Alarming ? "alarming" : "informational-narrower")
                    + $"; {comparison.Summary}. Model and reasoning effort are operator-choice wish fields excluded "
                    + "from this envelope comparison by design. This finding restarts or corrects nothing, answers "
                    + "no dialog, sends no keys, and wakes no role.",
                DetectableAt = null,
                WakeAlreadyAttempted = false,
                WakeAlreadyDelivered = false,
                WakeSuppressed = true,
                Cause = comparison.Drift == AgentLaunchEnvelopeDrift.Alarming
                    ? "recipe-envelope-alarming"
                    : "recipe-envelope-narrower",
            });
        }

        return observations;
    }

    private IReadOnlyList<NotifySupervisionObservation> ReadObservedPrompts(
        IReadOnlyList<NotifyPromptAudit> promptAudits,
        string cycleId)
    {
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

        var policyRead = NotifyPreApprovalPolicyStore.Read(
            context.ResolveSupervisionArtifactRootPath(), domain, team);
        var policy = policyRead.Resolved ? policyRead.Policy : null;
        var observations = new List<NotifySupervisionObservation>();
        foreach (var (role, recorded) in topology.Topology.Roles)
        {
            if (string.Equals(role, DesignRole, StringComparison.Ordinal)
                || !string.Equals(recorded.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(recorded.PaneId))
            {
                continue;
            }

            var workspaceId = recorded.WorkspaceId ?? topology.Topology.WorkspaceId;
            var agent = agents.SingleOrDefault(candidate =>
                string.Equals(candidate.WorkspaceId, workspaceId, StringComparison.Ordinal)
                && string.Equals(candidate.PaneId, recorded.PaneId, StringComparison.Ordinal));
            if (agent?.AgentRunning != true
                || !string.Equals(agent.AgentStatus, "blocked", StringComparison.Ordinal))
            {
                continue;
            }

            var agentKind = recorded.Kind ?? agent.AgentKind;
            if (string.IsNullOrWhiteSpace(agentKind))
            {
                agentKind = "unknown";
            }

            NotifyProcessResult paneRead;
            try
            {
                paneRead = runner.Run(
                    herdrExecutable,
                    ["agent", "read", recorded.PaneId, "--source", "detection", "--lines", "200"]);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            var observedText = paneRead.ExitCode == 0 ? paneRead.StandardOutput.Trim() : string.Empty;
            if (observedText.Length == 0)
            {
                continue;
            }

            var classified = AgentLaunchRecipeRegistry.Classify(agentKind, observedText);
            var authorization = PromptAdjudicationPipeline.Evaluate(
                classified,
                policy,
                actorRole: null,
                recorded.Cwd,
                promptAudits,
                currentCycleId: cycleId);
            var decision = authorization.Decision;
            var rule = authorization.Rule;
            var textHash = PromptDialogCas.HashText(observedText);
            var shortTextHash = textHash[..16];
            var sequence = classified.Known && agent.StateChangeSequence is { } stateSequence
                ? $"{classified.PromptClass}:{stateSequence.ToString(CultureInfo.InvariantCulture)}"
                : $"{classified.PromptClass}:{shortTextHash}";
            var promptKey = $"observed-prompt:{workspaceId}:{recorded.PaneId}:{sequence}";
            var pendingExecution = FindUnresolvedPromptExecution(promptAudits, promptKey);
            if (pendingExecution is not null)
            {
                decision = "escalate";
                rule = pendingExecution.Rule;
                authorization = authorization with
                {
                    Decision = decision,
                    Rule = rule,
                    Summary = "A durable bounded-answer execution is still pending; reconciliation is required and no retry is authorized.",
                    AnswerKeys = [],
                    ExactAnswerScope = null,
                    MechanicalExecutor = null,
                    ScopeOrRuleId = rule,
                };
            }
            var prompt = new NotifyObservedPrompt
            {
                CycleId = cycleId,
                AgentKind = agentKind,
                Pane = recorded.PaneId,
                ObservedText = observedText,
                PromptClass = classified.PromptClass,
                Decision = decision,
                Rule = rule,
                ReconciliationAttemptId = pendingExecution?.AttemptId,
                ExactAnswerScope = decision == "accept" ? authorization.ExactAnswerScope : null,
                AnswerKeys = decision == "accept" ? authorization.AnswerKeys : [],
                MatchedScopes = authorization.MatchedScopes,
                CommandDigest = authorization.CommandDigest,
                DialogHash = authorization.DialogHash,
                PolicySummary = authorization.Summary,
                AnswerableBy = authorization.AnswerableBy,
                RiskTags = authorization.RiskTags,
                DecisionActorRole = authorization.DecisionActorRole,
                AdjudicationTargetRole = authorization.DecisionActorRole,
                ScopeOrRuleId = authorization.ScopeOrRuleId,
                StateChangeSequence = agent.StateChangeSequence,
                ObservedTextHash = textHash,
            };
            observations.Add(new NotifySupervisionObservation
            {
                Key = promptKey,
                Kind = "observed-prompt",
                OwnerRole = authorization.DecisionActorRole,
                SubjectRole = role,
                Source = "herdr.agent-status+agent.read+agent-launch-recipe+g690-adjudication",
                Summary = $"Dialog-blocked seat '{role}' kind '{agentKind}' pane '{recorded.PaneId}' emitted prompt-class '{classified.PromptClass}' with decision '{decision}'. "
                    + (pendingExecution is not null
                        ? "A durable execution-pending audit has no terminal outcome; reconciliation is required and no answer may be retried."
                        : authorization.Summary),
                WorkspaceId = workspaceId,
                PaneId = recorded.PaneId,
                Prompt = prompt,
            });
        }
        return observations;
    }

    private NotifySupervisionWakeResult WakeAndAdjudicatePrompt(
        NotifyPendingDelegation record,
        NotifySupervisionObservation observation,
        NotifyObservedPrompt prompt,
        DateTimeOffset now)
    {
        var auditPath = NotifySupervisionStore.ResolveCyclePath(
            context.ResolveSupervisionArtifactRootPath(), domain, team);
        var attemptId = prompt.ReconciliationAttemptId ?? Guid.NewGuid().ToString("N");
        var initialOutcome = prompt.ReconciliationAttemptId is not null
            ? "bounded-answer-outcome-unknown-reconciliation-required"
            : prompt.Decision == "accept" ? "authorized-before-execution" : "escalate-only";
        var audit = new NotifyPromptAudit
        {
            CycleId = prompt.CycleId,
            AttemptId = attemptId,
            PromptKey = observation.Key,
            Seat = observation.SubjectRole ?? "unknown",
            Pane = prompt.Pane,
            AgentKind = prompt.AgentKind,
            PromptClass = prompt.PromptClass,
            Rule = prompt.Rule,
            Actor = prompt.DecisionActorRole,
            DecisionActorRole = prompt.DecisionActorRole,
            MechanicalExecutor = prompt.MechanicalExecutor,
            ScopeOrRuleId = prompt.ScopeOrRuleId,
            StateChangeSequence = prompt.StateChangeSequence,
            ObservedTextHash = prompt.ObservedTextHash,
            Timestamp = now,
            Outcome = initialOutcome,
            ExactAnswerScope = prompt.ExactAnswerScope,
            MatchedScopes = prompt.MatchedScopes,
            CommandDigest = prompt.CommandDigest,
            DialogHash = prompt.DialogHash,
        };
        var auditWrite = NotifySupervisionStore.RecordPromptAudit(auditPath, audit, write: true);
        if (!auditWrite.Applied)
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = false,
                Delivered = false,
                Cause = "prompt-audit-write-failed",
                Summary = $"The prompt audit could not be durably appended before action: {auditWrite.Error ?? "not applied"}. No wake or answer was attempted.",
            };
        }

        var delivery = NotifySupervisorDelivery.Send(
            routingRoot,
            record,
            JsonSerializer.Serialize(new
            {
                notification = "observed-prompt",
                kind = observation.Kind,
                source = observation.Source,
                key = observation.Key,
                subject_role = observation.SubjectRole,
                wake_target_role = prompt.AdjudicationTargetRole,
                wake_class = ResolveWakeClass(observation),
                agent_kind = prompt.AgentKind,
                pane = prompt.Pane,
                observed_text = prompt.ObservedText,
                prompt_class = prompt.PromptClass,
                decision = prompt.Decision,
                rule = prompt.Rule,
                exact_answer_scope = prompt.ExactAnswerScope,
                matched_scopes = prompt.MatchedScopes,
                command_digest = prompt.CommandDigest,
                dialog_hash = prompt.DialogHash,
                answerable_by = prompt.AnswerableBy,
                risk_tags = prompt.RiskTags,
                decision_actor_role = prompt.DecisionActorRole,
                mechanical_executor = prompt.MechanicalExecutor,
                scope_or_rule_id = prompt.ScopeOrRuleId,
                state_change_sequence = prompt.StateChangeSequence,
                observed_text_hash = prompt.ObservedTextHash,
                cycle_id = prompt.CycleId,
                policy_summary = prompt.PolicySummary,
                answer_keys = prompt.Decision == "accept" ? prompt.AnswerKeys : null,
                must_transition = false,
            }),
            runner,
            herdrExecutable,
            agmsgScriptsDirectory,
            bashExecutable);
        if (!delivery.Resolved || !delivery.Delivered || prompt.Decision != "accept")
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = true,
                Delivered = delivery.Resolved && delivery.Delivered,
                Cause = delivery.Resolved && delivery.Delivered ? null : delivery.Cause ?? "wake-undelivered",
                Summary = prompt.Decision == "accept"
                    ? $"{delivery.Summary} The bounded answer was not executed because the orchestration wake was not delivered."
                    : $"{delivery.Summary} Escalate-only adjudication executed no answer.",
            };
        }

        var cas = VerifyLivePromptCas(observation.WorkspaceId, prompt);
        if (!cas.Matches)
        {
            var casAudit = NotifySupervisionStore.RecordPromptAudit(
                auditPath,
                audit with
                {
                    Timestamp = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                    Outcome = "stale-dialog-cas-refused",
                    Rule = prompt.Rule + " (stale-dialog-cas-refused)",
                },
                write: true);
            return new NotifySupervisionWakeResult
            {
                Attempted = true,
                Delivered = true,
                Cause = "stale-dialog-cas-refused",
                Summary = $"The wake was delivered, but no key was sent: {cas.Summary}"
                    + (casAudit.Applied ? " The CAS refusal was audited." : $" The CAS refusal audit failed: {casAudit.Error ?? "not applied"}."),
            };
        }

        if (!string.Equals(prompt.DecisionActorRole, PromptCapabilityResolver.OrchestrationRole, StringComparison.Ordinal))
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = true,
                Delivered = true,
                Cause = null,
                Summary = "The declared decision actor was notified, but the supervisor did not execute keys; the actor must use canonical notify adjudicate for any bounded answer.",
            };
        }

        var pendingAudit = NotifySupervisionStore.RecordPromptAudit(
            auditPath,
            audit with
            {
                Timestamp = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Outcome = "bounded-answer-execution-pending",
            },
            write: true);
        if (!pendingAudit.Applied)
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = true,
                Delivered = true,
                Cause = "prompt-execution-intent-audit-write-failed",
                Summary = $"Orchestration received the bounded prompt, but its execution-pending audit could not be durably appended: {pendingAudit.Error ?? "not applied"}. No answer was attempted.",
            };
        }

        NotifyProcessResult execution;
        try
        {
            execution = runner.Run(herdrExecutable, ["agent", "send-keys", prompt.Pane, .. prompt.AnswerKeys]);
        }
        catch (InvalidOperationException exception)
        {
            execution = new NotifyProcessResult(1, string.Empty, exception.Message);
        }
        var outcome = execution.ExitCode == 0 ? "bounded-answer-executed" : "bounded-answer-failed";
        var finalAudit = NotifySupervisionStore.RecordPromptAudit(
            auditPath,
            audit with
            {
                Timestamp = (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Outcome = outcome,
            },
            write: true);
        if (!finalAudit.Applied)
        {
            return new NotifySupervisionWakeResult
            {
                Attempted = true,
                Delivered = false,
                Cause = "prompt-final-audit-write-failed",
                Summary = $"The bounded answer returned exit {execution.ExitCode}, but its final audit could not be appended: {finalAudit.Error ?? "not applied"}. The durable execution-pending record prevents an unaudited retry and requires reconciliation.",
            };
        }
        return new NotifySupervisionWakeResult
        {
            Attempted = true,
            Delivered = true,
            Cause = execution.ExitCode == 0 ? null : "bounded-prompt-answer-failed",
            Summary = execution.ExitCode == 0
                ? $"Orchestration received the exact rule/dialog/scope and executed only registry keys [{string.Join(", ", prompt.AnswerKeys)}]; both authorization and outcome were audited."
                : $"Orchestration was woken, but the exact bounded answer failed: {execution.StandardError}",
        };
    }

    private PromptDialogCasResult VerifyLivePromptCas(string? workspaceId, NotifyObservedPrompt prompt)
    {
        NotifyProcessResult roster;
        try
        {
            roster = runner.Run(herdrExecutable, ["agent", "list"]);
        }
        catch (InvalidOperationException exception)
        {
            return new PromptDialogCasResult { Matches = false, Summary = $"The live agent roster could not be read: {exception.Message}" };
        }
        if (roster.ExitCode != 0)
        {
            return new PromptDialogCasResult { Matches = false, Summary = "The live agent roster could not be read before bounded execution." };
        }

        IReadOnlyList<HerdrAgentState> agents;
        try
        {
            agents = HerdrNotifyTransport.ParseAgents(roster.StandardOutput);
        }
        catch (InvalidOperationException exception)
        {
            return new PromptDialogCasResult { Matches = false, Summary = $"The live agent roster was not parseable: {exception.Message}" };
        }

        var agent = agents.SingleOrDefault(candidate =>
            string.Equals(candidate.PaneId, prompt.Pane, StringComparison.Ordinal)
            && (workspaceId is null || string.Equals(candidate.WorkspaceId, workspaceId, StringComparison.Ordinal)));
        if (agent is null)
        {
            return new PromptDialogCasResult { Matches = false, Summary = "The adjudicated pane is no longer present in the live roster." };
        }

        NotifyProcessResult paneRead;
        try
        {
            paneRead = runner.Run(
                herdrExecutable,
                ["agent", "read", prompt.Pane, "--source", "detection", "--lines", "200"]);
        }
        catch (InvalidOperationException exception)
        {
            return new PromptDialogCasResult { Matches = false, Summary = $"The live dialog could not be reread: {exception.Message}" };
        }
        if (paneRead.ExitCode != 0)
        {
            return new PromptDialogCasResult { Matches = false, Summary = "The live dialog could not be reread before bounded execution." };
        }

        var liveText = paneRead.StandardOutput.Trim();
        return PromptDialogCas.Verify(
            prompt.Pane,
            agent.PaneId ?? string.Empty,
            prompt.StateChangeSequence,
            agent.StateChangeSequence,
            prompt.ObservedTextHash ?? string.Empty,
            PromptDialogCas.HashText(liveText));
    }

    private static NotifyPromptAudit? FindUnresolvedPromptExecution(
        IReadOnlyList<NotifyPromptAudit> audits,
        string promptKey) => audits
        .Where(audit => string.Equals(audit.PromptKey, promptKey, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(audit.AttemptId))
        .GroupBy(audit => audit.AttemptId!, StringComparer.Ordinal)
        .Select(group => group.OrderBy(audit => audit.Timestamp).Last())
        .LastOrDefault(audit => string.Equals(
            audit.Outcome,
            "bounded-answer-execution-pending",
            StringComparison.Ordinal));

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
            // Design is the operator-facing rung above supervision, not a
            // seat that this loop watches or attempts to recover.
            if (string.Equals(role, DesignRole, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(recorded.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(recorded.PaneId))
            {
                continue;
            }

            var workspaceId = recorded.WorkspaceId ?? topology.Topology.WorkspaceId;
            var agentsAtPane = agents
                .Where(agent => string.Equals(agent.WorkspaceId, workspaceId, StringComparison.Ordinal)
                    && string.Equals(agent.PaneId, recorded.PaneId, StringComparison.Ordinal))
                .ToArray();
            var agentSessionMissing = agentsAtPane.Length > 0
                && agentsAtPane.Any(agent => !agent.AgentSessionPresent);
            var running = agents.Any(agent =>
                string.Equals(agent.WorkspaceId, workspaceId, StringComparison.Ordinal)
                && string.Equals(agent.PaneId, recorded.PaneId, StringComparison.Ordinal)
                && agent.AgentRunning);
            if (!running)
            {
                var processInfo = NotifyPaneProcessReader.Read(runner, herdrExecutable, recorded.PaneId);
                if (!processInfo.Resolved)
                {
                    // Do not turn an unverified process observation into a
                    // false seat-absent finding. The next bounded cycle can
                    // retry corroboration.
                    continue;
                }

                var paneKey = $"registration:{workspaceId}:{recorded.PaneId}";
                var registrationDefinition = "a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane";
                var registrationLookup = $"herdr agent list matched workspace='{workspaceId}' pane='{recorded.PaneId}' with running_agent=false, agent_session={(agentsAtPane.Length == 0 ? "not-observed" : agentSessionMissing ? "missing" : "present")}; pane process-info returned foreground_processes={processInfo.Processes.Count.ToString(CultureInfo.InvariantCulture)}";
                if (processInfo.Processes.Count > 0)
                {
                    observations.Add(new NotifySupervisionObservation
                    {
                        Key = paneKey,
                        Kind = NotifyPendingLivenessResult.RegistrationLostProcessPresent,
                        OwnerRole = ownerRole,
                        SubjectRole = role,
                        Source = "recorded-topology+pane.process-info",
                        Summary = $"Recorded herdr seat '{role}' has no running registration at workspace '{workspaceId}' pane '{recorded.PaneId}', but {processInfo.Processes.Count} foreground process(es) remain. {NotifyPendingLiveness.RegistrationRecoveryGuidance(agentSessionMissing)} Resend is permitted after repair.",
                        DetectableAt = null,
                        WakeAlreadyAttempted = false,
                        WakeAlreadyDelivered = false,
                        ResendPermitted = true,
                        WorkspaceId = workspaceId,
                        PaneId = recorded.PaneId,
                        RegistrationDefinition = registrationDefinition,
                        RegistrationLookup = registrationLookup,
                        RegistrationResult = "registration-missing; foreground-processes-present",
                        ConsultedObservations =
                        [
                            $"recorded-topology: role='{role}' workspace='{workspaceId}' pane='{recorded.PaneId}'",
                            $"herdr.agent-list: running_agent=false; agent_session={(agentsAtPane.Length == 0 ? "not-observed" : agentSessionMissing ? "missing" : "present")}",
                            $"pane.process-info: foreground_processes={processInfo.Processes.Count.ToString(CultureInfo.InvariantCulture)}",
                        ],
                        Evidence =
                        [
                            $"registration_definition:{registrationDefinition}",
                            $"registration_lookup:{registrationLookup}",
                            "registration_result:registration-missing; foreground-processes-present",
                            $"consulted_observations:role={role}; agent_session={(agentsAtPane.Length == 0 ? "not-observed" : agentSessionMissing ? "missing" : "present")}; foreground_processes={processInfo.Processes.Count.ToString(CultureInfo.InvariantCulture)}",
                        ],
                    });
                    continue;
                }

                observations.Add(new NotifySupervisionObservation
                {
                    Key = $"seat:{workspaceId}:{recorded.PaneId}",
                    Kind = "seat-absent",
                    OwnerRole = ownerRole,
                    SubjectRole = role,
                    Source = "recorded-topology",
                    Summary = $"Recorded herdr seat '{role}' is absent from workspace '{workspaceId}' pane '{recorded.PaneId}' and no foreground process corroborates it.",
                    DetectableAt = null,
                    WakeAlreadyAttempted = false,
                    WakeAlreadyDelivered = false,
                    WorkspaceId = workspaceId,
                    PaneId = recorded.PaneId,
                    RegistrationDefinition = registrationDefinition,
                    RegistrationLookup = registrationLookup,
                    RegistrationResult = "registration-missing; foreground-processes-absent",
                    ConsultedObservations =
                    [
                        $"recorded-topology: role='{role}' workspace='{workspaceId}' pane='{recorded.PaneId}'",
                        "herdr.agent-list: running_agent=false",
                        "pane.process-info: foreground_processes=0",
                    ],
                    Evidence =
                    [
                        $"registration_definition:{registrationDefinition}",
                        $"registration_lookup:{registrationLookup}",
                        "registration_result:registration-missing; foreground-processes-absent",
                        $"consulted_observations:role={role}; foreground_processes=0",
                    ],
                });
            }
        }

        return observations;
    }

    private void PersistStallUpdate(
        NotifySupervisionStallRecord record,
        ICollection<string> warnings,
        string warningPrefix)
    {
        var writeResult = NotifySupervisionStore.OpenStall(
            NotifySupervisionStore.ResolveStallPath(
                context.ResolveSupervisionArtifactRootPath(),
                domain,
                team),
            record,
            write);
        if (writeResult.Error is not null)
        {
            warnings.Add($"{warningPrefix}: {writeResult.Error}");
        }
    }

    private (NotifySupervisionStallRecord Record, bool ShouldEmit, bool StateChanged) RefreshEmissionState(
        NotifySupervisionStallRecord baseline,
        NotifySupervisionObservation observation,
        NotifySupervisionWakeResult? wake,
        DateTimeOffset now,
        NotifySupervisionEmissionPolicy policy)
    {
        var fingerprint = BuildStateFingerprint(observation);
        var stateChanged = baseline.StateFingerprint is not null
            && !string.Equals(baseline.StateFingerprint, fingerprint, StringComparison.Ordinal);
        var firstSeen = stateChanged
            ? now
            : baseline.FirstSeenAt ?? baseline.SurfacedAt;
        var repeatCount = stateChanged
            ? 1
            : baseline.FirstSeenAt is null && baseline.RepeatCount == 0
                ? 1
                : Math.Max(1, baseline.RepeatCount) + 1;
        var shouldEmit = stateChanged
            || baseline.FirstSeenAt is null && baseline.RepeatCount == 0
            || baseline.LastEmittedAt is null
            || now - baseline.LastEmittedAt.Value >= TimeSpan.FromSeconds(policy.RepeatBackoffSeconds);
        var parked = !stateChanged && repeatCount > 1;
        var record = baseline with
        {
            Kind = observation.Kind,
            OwnerRole = observation.OwnerRole,
            SubjectRole = observation.SubjectRole ?? baseline.SubjectRole,
            WakeTargetRole = ResolveWakeTarget(observation),
            WakeClass = ResolveWakeClass(observation) ?? baseline.WakeClass,
            Source = observation.Source,
            Summary = observation.Summary,
            DetectableAt = stateChanged ? observation.DetectableAt : baseline.DetectableAt ?? observation.DetectableAt,
            DetectableAtUnknown = stateChanged
                ? observation.DetectableAt is null
                : baseline.DetectableAtUnknown || observation.DetectableAt is null,
            SurfacedAt = stateChanged ? now : baseline.SurfacedAt,
            WakeAttempted = wake?.Attempted ?? baseline.WakeAttempted,
            WakeDelivered = wake?.Delivered ?? baseline.WakeDelivered,
            WakeCause = wake?.Cause ?? baseline.WakeCause,
            ResendPermitted = observation.ResendPermitted ?? baseline.ResendPermitted,
            Cause = observation.Cause ?? baseline.Cause,
            Prompt = observation.Prompt ?? baseline.Prompt,
            Evidence = observation.Evidence ?? baseline.Evidence,
            OwedTransition = observation.OwedTransition ?? baseline.OwedTransition,
            RegistrationDefinition = observation.RegistrationDefinition ?? baseline.RegistrationDefinition,
            RegistrationLookup = observation.RegistrationLookup ?? baseline.RegistrationLookup,
            RegistrationResult = observation.RegistrationResult ?? baseline.RegistrationResult,
            ConsultedObservations = observation.ConsultedObservations ?? baseline.ConsultedObservations,
            FirstSeenAt = firstSeen,
            LastSeenAt = now,
            RepeatCount = repeatCount,
            LastEmittedAt = shouldEmit ? now : baseline.LastEmittedAt,
            Parked = parked,
            ParkReason = parked
                ? $"same-key observation repeated; next finding emission is no more frequent than the recorded {policy.RepeatBackoffSeconds}s backoff cadence"
                : null,
            EmissionCadenceSeconds = policy.RepeatBackoffSeconds,
            StateFingerprint = fingerprint,
        };
        return (record, shouldEmit, stateChanged);
    }

    private static string BuildStateFingerprint(NotifySupervisionObservation observation)
    {
        var stableSummary = string.Equals(observation.Kind, "duplicate-supervisor", StringComparison.Ordinal)
            ? "duplicate-supervisor"
            : observation.Summary;
        var stableCause = string.Equals(observation.Kind, "duplicate-supervisor", StringComparison.Ordinal)
            ? "duplicate-supervisor"
            : observation.Cause ?? string.Empty;
        var canonical = string.Join(
            "\u001f",
            observation.Kind,
            observation.Source,
            observation.OwnerRole,
            observation.SubjectRole ?? string.Empty,
            stableSummary,
            stableCause,
            observation.OwedTransition ?? string.Empty,
            string.Join("\u001e", observation.Evidence ?? []),
            observation.ToStatus ?? string.Empty,
            observation.RegistrationDefinition ?? string.Empty,
            observation.RegistrationLookup ?? string.Empty,
            observation.RegistrationResult ?? string.Empty,
            string.Join("\u001e", observation.ConsultedObservations ?? []));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static NotifySupervisionFinding ToFinding(NotifySupervisionStallRecord record) => new()
    {
        Key = record.Key,
        Kind = record.Kind,
        OwnerRole = record.OwnerRole,
        SubjectRole = record.SubjectRole,
        WakeTargetRole = record.WakeTargetRole,
        WakeClass = record.WakeClass,
        Source = record.Source,
        Summary = record.Summary,
        ResendPermitted = record.ResendPermitted,
        DetectableAt = record.DetectableAt,
        SurfacedAt = record.SurfacedAt,
        WakeAttempted = record.WakeAttempted,
        WakeDelivered = record.WakeDelivered,
        Cause = record.Cause ?? record.WakeCause,
        Prompt = record.Prompt,
        Evidence = record.Evidence,
        OwedTransition = record.OwedTransition,
        RegistrationDefinition = record.RegistrationDefinition,
        RegistrationLookup = record.RegistrationLookup,
        RegistrationResult = record.RegistrationResult,
        ConsultedObservations = record.ConsultedObservations,
        FirstSeenAt = record.FirstSeenAt,
        LastSeenAt = record.LastSeenAt,
        RepeatCount = record.RepeatCount,
        Parked = record.Parked,
        EmissionCadenceSeconds = record.EmissionCadenceSeconds,
    };

    /// <summary>
    /// G695: retain the detector's canonical evidence while naming the exact
    /// continuation owed by the three incident shapes from #1491. The
    /// detector kinds remain stable; this is an additive supervisor-facing
    /// projection and never executes the named transition.
    /// </summary>
    private static ContinuationFindingProjection? TryProjectContinuationFinding(StalledWorkItem item)
    {
        var evidence = item.ContinuationEvidence ?? [];
        var exactHead = item.PrHeadSha ?? EvidenceValue(evidence, "exact-head:");
        var ciOutcome = item.CiOutcome ?? EvidenceValue(evidence, "checks:");
        var landingMode = item.LandingMode ?? EvidenceValue(evidence, "lane:");
        if (string.Equals(item.Kind, AutomationStalledWorkCommand.KindApprovedNotMerged, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(landingMode)
                || string.Equals(landingMode, BranchLaneLandingModes.Direct, StringComparison.Ordinal))
            && string.Equals(ciOutcome, StalledWorkCiOutcomes.AllGreen, StringComparison.Ordinal)
            && item.Pr is { Number: > 0 }
            && !string.IsNullOrWhiteSpace(exactHead))
        {
            var directEvidence = evidence.Count > 0
                ? evidence
                : new[]
            {
                $"pr:#{item.Pr.Number.ToString(CultureInfo.InvariantCulture)}",
                "lane:direct",
                $"exact-head:{exactHead}",
                $"checks:{StalledWorkCiOutcomes.AllGreen}",
                "approval:intent-pr-approved",
            };
            return new ContinuationFindingProjection(
                "approved-direct-lane-merge-closeout-owed",
                "merge-then-closeout",
                directEvidence,
                "Continuation finding: approved direct-lane PR "
                + $"#{item.Pr.Number.ToString(CultureInfo.InvariantCulture)} is exact-head green and owes merge then closeout. "
                + $"Evidence: {string.Join(", ", directEvidence)}.");
        }

        if (string.Equals(item.Kind, AutomationStalledWorkCommand.KindKnowledgeWritebackPending, StringComparison.Ordinal)
            && item.DeclaredWriteBackTargets is { Count: > 0 })
        {
            var writeBackEvidence = evidence.Count > 0
                ? evidence.ToList()
                :
                [
                    $"execution-unit:{item.ExecutionUnit}",
                    "closeout-recorded",
                    $"declared-targets:{string.Join(",", item.DeclaredWriteBackTargets)}",
                ];
            if (item.Pr is { Number: > 0 })
            {
                writeBackEvidence.Insert(1, $"merged-pr:#{item.Pr.Number.ToString(CultureInfo.InvariantCulture)}");
            }

            return new ContinuationFindingProjection(
                "merged-pr-knowledge-writeback-dispatch-owed",
                "knowledge-writeback-dispatch",
                writeBackEvidence,
                "Continuation finding: merged PR closeout has declared knowledge write-back debt for "
                + $"'{item.ExecutionUnit}'; dispatch the named write-back obligation. "
                + $"Evidence: {string.Join(", ", writeBackEvidence)}.");
        }

        if (string.Equals(item.Kind, AutomationStalledWorkCommand.KindBacklogReadyIdle, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(item.ExecutionUnit))
        {
            var backlogEvidence = evidence.Count > 0
                ? evidence
                : new[]
            {
                $"candidate:{item.ExecutionUnit}",
                "wip:empty",
                "publish-gate:issue-cut-ready",
                $"idle-minutes:{item.AgeMinutes.ToString(CultureInfo.InvariantCulture)}",
            };
            return new ContinuationFindingProjection(
                "actionable-queue-next-slice-publication-owed",
                "publish-next-slice",
                backlogEvidence,
                "Continuation finding: actionable queue is idle and owes publication of "
                + $"next slice '{item.ExecutionUnit}'. Evidence: {string.Join(", ", backlogEvidence)}.");
        }

        return null;
    }

    private static string? EvidenceValue(IReadOnlyList<string> evidence, string prefix) =>
        evidence.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.Ordinal)) is { } match
            ? match[prefix.Length..]
            : null;

    private sealed record ContinuationFindingProjection(
        string Kind,
        string OwedTransition,
        IReadOnlyList<string> Evidence,
        string Summary);
}

internal sealed record NotifySupervisionObservation
{
    public required string Key { get; init; }
    public required string Kind { get; init; }
    public required string OwnerRole { get; init; }
    public string? SubjectRole { get; init; }
    public required string Source { get; init; }
    public required string Summary { get; init; }
    public bool? ResendPermitted { get; init; }
    public DateTimeOffset? DetectableAt { get; init; }
    public bool WakeAlreadyAttempted { get; init; }
    public bool WakeAlreadyDelivered { get; init; }
    public string? WakeCause { get; init; }
    public string? Cause { get; init; }
    public bool WakeSuppressed { get; init; }
    public bool UseEmissionBackoff { get; init; }
    public IReadOnlyList<string>? Evidence { get; init; }
    public string? OwedTransition { get; init; }
    public string? WorkspaceId { get; init; }
    public string? PaneId { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }
    public long? StateChangeSequence { get; init; }
    public DateTimeOffset? StateChangedAt { get; init; }
    public string? RegistrationDefinition { get; init; }
    public string? RegistrationLookup { get; init; }
    public string? RegistrationResult { get; init; }
    public IReadOnlyList<string>? ConsultedObservations { get; init; }
    public NotifyObservedPrompt? Prompt { get; init; }
}

internal sealed record NotifyObservedPrompt
{
    [System.Text.Json.Serialization.JsonPropertyName("cycle_id")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CycleId { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("agent_kind")]
    public required string AgentKind { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("pane")]
    public required string Pane { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("observed_text")]
    public required string ObservedText { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("prompt_class")]
    public required string PromptClass { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("decision")]
    public required string Decision { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("rule")]
    public required string Rule { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ReconciliationAttemptId { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("exact_answer_scope")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ExactAnswerScope { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("matched_scopes")]
    public IReadOnlyList<string> MatchedScopes { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("command_digest")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CommandDigest { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("dialog_hash")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DialogHash { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("policy_summary")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PolicySummary { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("answerable_by")]
    public string AnswerableBy { get; init; } = PromptCapabilityResolver.OrchestrationRole;
    [System.Text.Json.Serialization.JsonPropertyName("risk_tags")]
    public IReadOnlyList<string> RiskTags { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("decision_actor_role")]
    public string DecisionActorRole { get; init; } = PromptCapabilityResolver.OrchestrationRole;
    [System.Text.Json.Serialization.JsonPropertyName("adjudication_target_role")]
    public string AdjudicationTargetRole { get; init; } = PromptCapabilityResolver.OrchestrationRole;
    [System.Text.Json.Serialization.JsonPropertyName("mechanical_executor")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? MechanicalExecutor { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("scope_or_rule_id")]
    public string ScopeOrRuleId { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("state_change_sequence")]
    public long? StateChangeSequence { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("observed_text_hash")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ObservedTextHash { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> AnswerKeys { get; init; } = [];
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

    [System.Text.Json.Serialization.JsonPropertyName("cadence_interval_seconds")]
    public required int CadenceIntervalSeconds { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("absence_threshold_seconds")]
    public required int AbsenceThresholdSeconds { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("absence_threshold_kind")]
    public required string AbsenceThresholdKind { get; init; }

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

    [System.Text.Json.Serialization.JsonPropertyName("subject_role")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SubjectRole { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("wake_target_role")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? WakeTargetRole { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("wake_class")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? WakeClass { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public required string Source { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("resend_permitted")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? ResendPermitted { get; init; }

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

    [System.Text.Json.Serialization.JsonPropertyName("observed_prompt")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NotifyObservedPrompt? Prompt { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("evidence")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Evidence { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("owed_transition")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? OwedTransition { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("registration_definition")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationDefinition { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("registration_lookup")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationLookup { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("registration_result")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationResult { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("consulted_observations")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ConsultedObservations { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("first_seen")]
    public DateTimeOffset? FirstSeenAt { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("last_seen")]
    public DateTimeOffset? LastSeenAt { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("repeat_count")]
    public int RepeatCount { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("parked")]
    public bool Parked { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("emission_cadence_seconds")]
    public int? EmissionCadenceSeconds { get; init; }
}
