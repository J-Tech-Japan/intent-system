using IntentSystem.Review;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class RunSuperviseCommand
{
    private const string TransitionActor = "intent-cli";

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static Func<CliContext, string, RunImplementResult> RunImplementExecutor { get; set; } =
        RunImplementCommand.ExecuteCore;

    public static Func<CliContext, string, RunFixResult> RunFixExecutor { get; set; } =
        RunFixCommand.ExecuteCore;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run supervise command requires an execution unit.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context, args[0]);
            RunSuperviseRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static RunSuperviseResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueStatePath = context.GetQueueStatePath();
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null);
        if (queueState is null)
        {
            throw new InvalidOperationException($"No queue state found at {queueStatePath}");
        }

        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
        if (queueItem is null)
        {
            throw new InvalidOperationException($"Execution unit '{executionUnit}' was not found in queue state.");
        }

        if (queueItem.State is not (QueueItemState.Active or QueueItemState.Fixing))
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' must be active or fixing before run supervise.");
        }

        if (queueItem.LinkedIssue is null)
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' must have a linked issue before run supervise.");
        }

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            throw new InvalidOperationException($"Run log was not found at {runLogPath}");
        }

        var packetPath = Path.Combine(
            context.RepoRoot,
            queueItem.PacketPaths.Yaml.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

        var sessionArtifactRef = RunSupervisionSessionArtifactPathResolver.Resolve(
            context.Config.Supervision.ArtifactRoot,
            executionUnit);
        var sessionArtifactPath = ResolveArtifactPath(context.RepoRoot, sessionArtifactRef);
        var now = TimestampFactory();
        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        var supervisionContext = ResolveSupervisionContext(context, queueItem, runEvents, packetPath);
        var heartbeatTimeout = TimeSpan.FromMinutes(context.Config.Supervision.StaleHeartbeatTimeoutMinutes);
        var retryDelay = TimeSpan.FromMinutes(context.Config.Supervision.RetryDelayMinutes);
        var retryBudget = context.Config.Supervision.RetryBudget;

        var session = File.Exists(sessionArtifactPath)
            ? RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionArtifactPath))
            : null;

        if (session is not null
            && session.Status == RunSupervisionSessionStatus.Blocked
            && queueItem.State is QueueItemState.Active or QueueItemState.Fixing)
        {
            session = null;
        }

        if (session is null)
        {
            var createdSession = CreateSession(supervisionContext, now, retryBudget);
            return FinalizeSessionInitialization(
                context,
                queueState,
                executionUnit,
                sessionArtifactPath,
                sessionArtifactRef,
                runLogPath,
                createdSession,
                now);
        }

        if (session.WorkerEntry != supervisionContext.WorkerEntry)
        {
            var realignedSession = CreateSession(supervisionContext, now, retryBudget) with
            {
                CreatedAt = session.CreatedAt
            };
            return FinalizeSessionInitialization(
                context,
                queueState,
                executionUnit,
                sessionArtifactPath,
                sessionArtifactRef,
                runLogPath,
                realignedSession,
                now);
        }

        session = session with
        {
            QueueState = supervisionContext.QueueState,
            WorktreePath = supervisionContext.WorktreePath,
            ChildRepoPath = supervisionContext.ChildRepoPath,
            Branch = supervisionContext.Branch,
            LinkedIssue = supervisionContext.LinkedIssue,
            LinkedPr = supervisionContext.LinkedPr,
            CommentRef = supervisionContext.CommentRef,
            HandoffArtifactRef = supervisionContext.HandoffArtifactRef,
            RetryBudget = retryBudget
        };

        if (TryCaptureDeadWorkerSessionFailure(
                context,
                executionUnit,
                session.WorkerEntry,
                out var deadWorkerReason))
        {
            if (session.RetryCount >= session.RetryBudget)
            {
                return ExhaustRetryBudget(
                    context,
                    queueState,
                    executionUnit,
                    sessionArtifactPath,
                    sessionArtifactRef,
                    runLogPath,
                    session,
                    now,
                    deadWorkerReason);
            }

            var interruptedSession = session with
            {
                UpdatedAt = now,
                LastInterruptionReason = deadWorkerReason
            };

            return AttemptAutoResume(
                context,
                queueState,
                executionUnit,
                sessionArtifactPath,
                sessionArtifactRef,
                runLogPath,
                interruptedSession,
                now);
        }

        if (session.Status == RunSupervisionSessionStatus.RetryScheduled)
        {
            if (session.NextRetryAt is null)
            {
                throw new InvalidOperationException(
                    $"Run supervision session for '{executionUnit}' must contain next_retry_at while retry is scheduled.");
            }

            if (now < session.NextRetryAt.Value)
            {
                var waitingSession = session with { UpdatedAt = now };
                PersistSession(sessionArtifactPath, waitingSession);
                return CreateResult(sessionArtifactRef, waitingSession);
            }

            return AttemptAutoResume(
                context,
                queueState,
                executionUnit,
                sessionArtifactPath,
                sessionArtifactRef,
                runLogPath,
                session,
                now);
        }

        if (now - session.LastHeartbeatAt > heartbeatTimeout)
        {
            if (session.RetryCount >= session.RetryBudget)
            {
                return ExhaustRetryBudget(
                    context,
                    queueState,
                    executionUnit,
                    sessionArtifactPath,
                    sessionArtifactRef,
                    runLogPath,
                    session,
                    now,
                    $"Retry budget exhausted after {session.RetryCount} attempts.");
            }

            var reason =
                $"Heartbeat expired after {heartbeatTimeout.TotalMinutes:0} minutes while supervising '{executionUnit}'.";
            var scheduledSession = session with
            {
                Status = RunSupervisionSessionStatus.RetryScheduled,
                UpdatedAt = now,
                NextRetryAt = now.Add(retryDelay),
                LastInterruptionReason = reason
            };

            PersistSession(sessionArtifactPath, scheduledSession);
            AppendRunEvents(
                runLogPath,
                [
                    new RunEvent
                    {
                        Ts = now,
                        ExecutionUnit = executionUnit,
                        Event = "retry-scheduled",
                        By = TransitionActor,
                        LinkedPr = scheduledSession.LinkedPr,
                        CommentRef = scheduledSession.CommentRef,
                        Reason = reason
                    }
                ]);

            return CreateResult(sessionArtifactRef, scheduledSession, retryScheduled: true);
        }

        var monitoringSession = session with
        {
            Status = RunSupervisionSessionStatus.Monitoring,
            UpdatedAt = now,
            LastHeartbeatAt = now,
            NextRetryAt = null,
            LastInterruptionReason = null
        };
        PersistSession(sessionArtifactPath, monitoringSession);
        return CreateResult(sessionArtifactRef, monitoringSession);
    }

    private static RunSuperviseResult FinalizeSessionInitialization(
        CliContext context,
        QueueState queueState,
        string executionUnit,
        string sessionArtifactPath,
        string sessionArtifactRef,
        string runLogPath,
        RunSupervisionSession session,
        DateTimeOffset now)
    {
        if (TryCaptureDeadWorkerSessionFailure(
                context,
                executionUnit,
                session.WorkerEntry,
                out var deadWorkerReason))
        {
            if (session.RetryCount >= session.RetryBudget)
            {
                return ExhaustRetryBudget(
                    context,
                    queueState,
                    executionUnit,
                    sessionArtifactPath,
                    sessionArtifactRef,
                    runLogPath,
                    session,
                    now,
                    deadWorkerReason);
            }

            var interruptedSession = session with
            {
                UpdatedAt = now,
                LastInterruptionReason = deadWorkerReason
            };

            return AttemptAutoResume(
                context,
                queueState,
                executionUnit,
                sessionArtifactPath,
                sessionArtifactRef,
                runLogPath,
                interruptedSession,
                now);
        }

        PersistSession(sessionArtifactPath, session);
        return CreateResult(sessionArtifactRef, session);
    }

    private static RunSuperviseResult AttemptAutoResume(
        CliContext context,
        QueueState queueState,
        string executionUnit,
        string sessionArtifactPath,
        string sessionArtifactRef,
        string runLogPath,
        RunSupervisionSession session,
        DateTimeOffset now)
    {
        var retryAttemptEvent = new RunEvent
        {
            Ts = now,
            ExecutionUnit = executionUnit,
            Event = "retry-attempted",
            By = TransitionActor,
            LinkedPr = session.LinkedPr,
            CommentRef = session.CommentRef,
            Reason = session.LastInterruptionReason
        };

        try
        {
            var resumedSession = session.WorkerEntry switch
            {
                RunSupervisionWorkerEntry.Implement =>
                    BuildResumedSession(session, RunImplementExecutor(context, executionUnit), now),
                RunSupervisionWorkerEntry.Fix =>
                    BuildResumedSession(session, RunFixExecutor(context, executionUnit), now),
                _ => throw new InvalidOperationException(
                    $"Unsupported worker entry '{session.WorkerEntry}'.")
            };

            PersistSession(sessionArtifactPath, resumedSession);
            AppendRunEvents(
                runLogPath,
                [
                    retryAttemptEvent,
                    new RunEvent
                    {
                        Ts = now,
                        ExecutionUnit = executionUnit,
                        Event = "auto-resumed",
                        By = TransitionActor,
                        LinkedPr = resumedSession.LinkedPr,
                        CommentRef = resumedSession.CommentRef,
                        Reason = FormatWorkerEntry(resumedSession.WorkerEntry)
                    }
                ]);

            return CreateResult(sessionArtifactRef, resumedSession, autoResumed: true);
        }
        catch (InvalidOperationException exception)
        {
            AppendRunEvents(runLogPath, [retryAttemptEvent]);
            return BlockForTerminalFailure(
                context,
                queueState,
                executionUnit,
                sessionArtifactPath,
                sessionArtifactRef,
                runLogPath,
                session,
                now,
                $"Non-retryable auto-resume failure: {exception.Message}",
                incrementRetryCount: true);
        }
    }

    private static RunSuperviseResult ExhaustRetryBudget(
        CliContext context,
        QueueState queueState,
        string executionUnit,
        string sessionArtifactPath,
        string sessionArtifactRef,
        string runLogPath,
        RunSupervisionSession session,
        DateTimeOffset now,
        string reason)
    {
        return BlockForTerminalFailure(
            context,
            queueState,
            executionUnit,
            sessionArtifactPath,
            sessionArtifactRef,
            runLogPath,
            session,
            now,
            reason,
            incrementRetryCount: false);
    }

    private static RunSuperviseResult BlockForTerminalFailure(
        CliContext context,
        QueueState queueState,
        string executionUnit,
        string sessionArtifactPath,
        string sessionArtifactRef,
        string runLogPath,
        RunSupervisionSession session,
        DateTimeOffset now,
        string reason,
        bool incrementRetryCount)
    {
        var transition = QueueManager.TransitionBlocking(
            queueState,
            executionUnit,
            QueueItemState.Blocked,
            reason,
            TransitionActor,
            now);
        var blockedSession = session with
        {
            Status = RunSupervisionSessionStatus.Blocked,
            QueueState = FormatQueueState(QueueItemState.Blocked),
            UpdatedAt = now,
            NextRetryAt = null,
            LastInterruptionReason = reason,
            RetryCount = incrementRetryCount ? session.RetryCount + 1 : session.RetryCount
        };

        PersistQueueState(context, transition.UpdatedState);
        PersistSession(sessionArtifactPath, blockedSession);
        AppendRunEvents(
            runLogPath,
            [
                new RunEvent
                {
                    Ts = now,
                    ExecutionUnit = executionUnit,
                    Event = "retry-exhausted",
                    By = TransitionActor,
                    LinkedPr = session.LinkedPr,
                    CommentRef = session.CommentRef,
                    Reason = reason
                },
                transition.Event
            ]);

        return CreateResult(sessionArtifactRef, blockedSession, blocked: true);
    }

    private static RunSupervisionSession BuildResumedSession(
        RunSupervisionSession session,
        RunImplementResult result,
        DateTimeOffset now)
    {
        return session with
        {
            Status = RunSupervisionSessionStatus.Monitoring,
            QueueState = result.Request.State,
            WorktreePath = result.Request.WorktreePath,
            ChildRepoPath = result.Request.ChildRepoPath,
            Branch = result.Request.Branch,
            LinkedIssue = result.Request.LinkedIssue,
            LinkedPr = result.Request.LatestLinkedPr,
            CommentRef = null,
            HandoffArtifactRef = result.ArtifactPath,
            RetryCount = session.RetryCount + 1,
            UpdatedAt = now,
            LastHeartbeatAt = now,
            NextRetryAt = null,
            LastInterruptionReason = null
        };
    }

    private static RunSupervisionSession BuildResumedSession(
        RunSupervisionSession session,
        RunFixResult result,
        DateTimeOffset now)
    {
        return session with
        {
            Status = RunSupervisionSessionStatus.Monitoring,
            QueueState = result.Request.State,
            WorktreePath = result.Request.WorktreePath,
            ChildRepoPath = result.Request.ChildRepoPath,
            Branch = result.Request.Branch,
            LinkedIssue = result.Request.LinkedIssue,
            LinkedPr = result.Request.LatestLinkedPr,
            CommentRef = result.Request.LatestCommentRef,
            HandoffArtifactRef = result.ArtifactPath,
            RetryCount = session.RetryCount + 1,
            UpdatedAt = now,
            LastHeartbeatAt = now,
            NextRetryAt = null,
            LastInterruptionReason = null
        };
    }

    private static RunSupervisionContext ResolveSupervisionContext(
        CliContext context,
        QueueItem queueItem,
        IReadOnlyList<RunEvent> runEvents,
        string packetPath)
    {
        var packet = ProjectionPacketRuntimeReader.Read(File.ReadAllText(packetPath));
        if (string.IsNullOrWhiteSpace(packet.TargetRepo))
        {
            throw new InvalidOperationException("Projection packet must contain a target repo.");
        }

        var childRepoPath = ResolveChildRepoPath(context.RepoRoot, packet.TargetRepo);
        if (!Directory.Exists(childRepoPath))
        {
            throw new InvalidOperationException($"Child repo path was not found at {childRepoPath}");
        }

        var worktreePath = RunStartCommand.ResolveWorktreePath(context, queueItem.ExecutionUnit);
        if (!Directory.Exists(worktreePath))
        {
            throw new InvalidOperationException($"Worktree path was not found at {worktreePath}");
        }

        var workerEntry = ResolveWorkerEntry(queueItem.State);
        var handoffArtifactRef = ResolveHandoffArtifactRef(workerEntry, queueItem.ExecutionUnit);
        var handoffArtifactPath = ResolveArtifactPath(context.RepoRoot, handoffArtifactRef);
        if (!File.Exists(handoffArtifactPath))
        {
            throw new InvalidOperationException($"Run handoff artifact was not found at {handoffArtifactPath}");
        }

        return new RunSupervisionContext
        {
            ExecutionUnit = queueItem.ExecutionUnit,
            WorkerEntry = workerEntry,
            QueueState = FormatQueueState(queueItem.State),
            WorktreePath = worktreePath,
            ChildRepoPath = childRepoPath,
            Branch = RunStartCommand.ResolveBranchName(queueItem.ExecutionUnit, queueItem.LinkedIssue!),
            LinkedIssue = queueItem.LinkedIssue!.Url,
            LinkedPr = LatestLinkedPrResolver.TryResolve(runEvents, queueItem.ExecutionUnit),
            CommentRef = TryResolveLatestCommentRef(runEvents, queueItem.ExecutionUnit),
            HandoffArtifactRef = handoffArtifactRef
        };
    }

    private static RunSupervisionSession CreateSession(
        RunSupervisionContext context,
        DateTimeOffset now,
        int retryBudget)
    {
        return new RunSupervisionSession
        {
            ExecutionUnit = context.ExecutionUnit,
            WorkerEntry = context.WorkerEntry,
            Status = RunSupervisionSessionStatus.Monitoring,
            QueueState = context.QueueState,
            WorktreePath = context.WorktreePath,
            ChildRepoPath = context.ChildRepoPath,
            Branch = context.Branch,
            LinkedIssue = context.LinkedIssue,
            LinkedPr = context.LinkedPr,
            CommentRef = context.CommentRef,
            HandoffArtifactRef = context.HandoffArtifactRef,
            RetryCount = 0,
            RetryBudget = retryBudget,
            CreatedAt = now,
            UpdatedAt = now,
            LastHeartbeatAt = now
        };
    }

    private static RunSupervisionWorkerEntry ResolveWorkerEntry(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.Active => RunSupervisionWorkerEntry.Implement,
            QueueItemState.Fixing => RunSupervisionWorkerEntry.Fix,
            _ => throw new InvalidOperationException(
                $"Unsupported queue state '{FormatQueueState(state)}' for run supervise.")
        };
    }

    private static string ResolveHandoffArtifactRef(RunSupervisionWorkerEntry workerEntry, string executionUnit)
    {
        return workerEntry switch
        {
            RunSupervisionWorkerEntry.Implement => RunImplementArtifactPathResolver.Resolve(executionUnit),
            RunSupervisionWorkerEntry.Fix => RunFixArtifactPathResolver.Resolve(executionUnit),
            _ => throw new InvalidOperationException($"Unsupported worker entry '{workerEntry}'.")
        };
    }

    private static string? TryResolveLatestCommentRef(IReadOnlyList<RunEvent> runEvents, string executionUnit)
    {
        for (var index = runEvents.Count - 1; index >= 0; index--)
        {
            var runEvent = runEvents[index];
            if (!string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(runEvent.CommentRef))
            {
                return runEvent.CommentRef;
            }
        }

        return null;
    }

    private static string ResolveChildRepoPath(string repoRoot, string childRepoRef)
    {
        return Path.IsPathRooted(childRepoRef)
            ? Path.GetFullPath(childRepoRef)
            : Path.GetFullPath(Path.Combine(repoRoot, childRepoRef));
    }

    private static string ResolveArtifactPath(string repoRoot, string artifactRef)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool TryCaptureDeadWorkerSessionFailure(
        CliContext context,
        string executionUnit,
        RunSupervisionWorkerEntry workerEntry,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        reason = string.Empty;
        var expectedEntryKind = ResolveDirectRunEntryKind(workerEntry);
        var requestArtifactPath = ResolveDirectRunRequestArtifactPath(context, executionUnit);
        var resultArtifactPath = ResolveDirectRunResultArtifactPath(context, executionUnit);
        if (!File.Exists(requestArtifactPath) || !File.Exists(resultArtifactPath))
        {
            return false;
        }

        var requestArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(requestArtifactPath));
        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
        if (!string.Equals(requestArtifact.EntryKind, expectedEntryKind, StringComparison.Ordinal)
            || !string.Equals(resultArtifact.EntryKind, expectedEntryKind, StringComparison.Ordinal)
            || !string.Equals(resultArtifact.SessionId, requestArtifact.ProviderSessionId, StringComparison.Ordinal)
            || !string.Equals(resultArtifact.RunStatus, "running", StringComparison.Ordinal)
            || !TryParseSessionProcessId(requestArtifact.ProviderSessionId, out var processId)
            || IsProcessAlive(processId))
        {
            return false;
        }

        var providerLogPath = ResolveDirectRunProviderLogPath(context, executionUnit);
        if (!File.Exists(providerLogPath))
        {
            return false;
        }

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerLogPath));
        var currentProviderEvents = SelectCurrentSessionEvents(providerEvents, requestArtifact.ProviderSessionId, requestArtifact.LaunchedAt);

        if (TryResolveTerminalFailureReason(
                currentProviderEvents,
                executionUnit,
                requestArtifact.ProviderSessionId,
                out reason))
        {
            File.WriteAllText(
                resultArtifactPath,
                DirectRunResultArtifactJson.Serialize(resultArtifact with
                {
                    RunStatus = "failed"
                }));

            return true;
        }

        var backendExitEvent = DirectRunProviderEventFactory.CreateBackendExitEvent(
            DateTimeOffset.UtcNow,
            executionUnit,
            expectedEntryKind,
            requestArtifact.Provider,
            requestArtifact.ProviderSessionId,
            exitCode: 1);
        new DirectRunProviderEventWriter(providerLogPath).Append(backendExitEvent);

        File.WriteAllText(
            resultArtifactPath,
            DirectRunResultArtifactJson.Serialize(resultArtifact with
            {
                RunStatus = "failed"
            }));

        reason =
            $"Worker session '{requestArtifact.ProviderSessionId}' for '{executionUnit}' is no longer alive and no terminal provider event was captured.";
        return true;
    }

    private static bool TryResolveTerminalFailureReason(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string executionUnit,
        string providerSessionId,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        reason = string.Empty;
        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            var providerEvent = providerEvents[index];
            if (!HasBackendExitType(providerEvent))
            {
                continue;
            }

            if (providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
                && exitCodeElement.TryGetInt32(out var exitCode))
            {
                reason =
                    $"Worker session '{providerSessionId}' for '{executionUnit}' exited with backend exit code {exitCode}.";
            }
            else
            {
                reason =
                    $"Worker session '{providerSessionId}' for '{executionUnit}' exited after a terminal backend-exit event.";
            }

            return true;
        }

        return false;
    }

    private static string ResolveDirectRunRequestArtifactPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return ResolveArtifactPath(context.RepoRoot, $"{root}/{executionUnit.Trim()}.request.json");
    }

    private static string ResolveDirectRunResultArtifactPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return ResolveArtifactPath(context.RepoRoot, $"{root}/{executionUnit.Trim()}.result.json");
    }

    private static string ResolveDirectRunProviderLogPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return ResolveArtifactPath(context.RepoRoot, $"{root}/{executionUnit.Trim()}.provider.jsonl");
    }

    private static string ResolveDirectRunEntryKind(RunSupervisionWorkerEntry workerEntry)
    {
        return workerEntry switch
        {
            RunSupervisionWorkerEntry.Implement => "implement",
            RunSupervisionWorkerEntry.Fix => "fix",
            _ => throw new InvalidOperationException($"Unsupported worker entry '{workerEntry}'.")
        };
    }

    private static IReadOnlyList<DirectRunProviderEvent> SelectCurrentSessionEvents(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string launchedSessionId,
        string launchedAt)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        if (!DirectRunSessionBoundary.TryParseLaunchedAt(launchedAt, out var parsedLaunchedAt))
        {
            parsedLaunchedAt = default;
        }

        return DirectRunSessionBoundary.SelectEvents(
            providerEvents,
            launchedSessionId,
            parsedLaunchedAt == default ? null : parsedLaunchedAt);
    }

    private static bool TryParseSessionProcessId(string providerSessionId, out int processId)
    {
        processId = default;

        const string prefix = "pid:";
        if (!providerSessionId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(
            providerSessionId[prefix.Length..],
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out processId);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasBackendExitType(DirectRunProviderEvent providerEvent)
    {
        ArgumentNullException.ThrowIfNull(providerEvent);

        return providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal);
    }

    private static void PersistQueueState(CliContext context, QueueState queueState)
    {
        var queueStatePath = context.GetQueueStatePath();
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(queueState));
    }

    private static void PersistSession(string sessionArtifactPath, RunSupervisionSession session)
    {
        var directoryPath = Path.GetDirectoryName(sessionArtifactPath)
            ?? throw new InvalidOperationException("Run supervision session path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(sessionArtifactPath, RunSupervisionSessionArtifactJson.Serialize(session));
    }

    private static void AppendRunEvents(string runLogPath, IReadOnlyList<RunEvent> runEvents)
    {
        if (runEvents.Count == 0)
        {
            return;
        }

        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);

        var serialized = string.Join(
            Environment.NewLine,
            runEvents.Select(RunLogSerializer.SerializeLine));
        File.AppendAllText(runLogPath, serialized + Environment.NewLine);
    }

    private static RunSuperviseResult CreateResult(
        string sessionArtifactRef,
        RunSupervisionSession session,
        bool retryScheduled = false,
        bool autoResumed = false,
        bool blocked = false)
    {
        return new RunSuperviseResult
        {
            ExecutionUnit = session.ExecutionUnit,
            SessionArtifactPath = sessionArtifactRef,
            WorkerEntry = session.WorkerEntry,
            SessionStatus = session.Status,
            RetryCount = session.RetryCount,
            RetryBudget = session.RetryBudget,
            HandoffArtifactRef = session.HandoffArtifactRef,
            NextRetryAt = session.NextRetryAt?.ToString("O"),
            RetryScheduled = retryScheduled,
            AutoResumed = autoResumed,
            Blocked = blocked
        };
    }

    private static string FormatQueueState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }

    private static string FormatWorkerEntry(RunSupervisionWorkerEntry workerEntry)
    {
        return workerEntry switch
        {
            RunSupervisionWorkerEntry.Implement => "run implement",
            RunSupervisionWorkerEntry.Fix => "run fix",
            _ => throw new InvalidOperationException($"Unsupported worker entry '{workerEntry}'.")
        };
    }

    private sealed record RunSupervisionContext
    {
        public string ExecutionUnit { get; init; } = string.Empty;

        public required RunSupervisionWorkerEntry WorkerEntry { get; init; }

        public required string QueueState { get; init; }

        public required string WorktreePath { get; init; }

        public required string ChildRepoPath { get; init; }

        public required string Branch { get; init; }

        public required string LinkedIssue { get; init; }

        public string? LinkedPr { get; init; }

        public string? CommentRef { get; init; }

        public required string HandoffArtifactRef { get; init; }
    }
}
