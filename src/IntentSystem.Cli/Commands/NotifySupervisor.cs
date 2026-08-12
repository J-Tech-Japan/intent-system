using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifySupervisorAction
{
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("recipient_role")] public required string RecipientRole { get; init; }
    [JsonPropertyName("verdict")] public required string Verdict { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("recovered")] public bool Recovered { get; init; }
    [JsonPropertyName("readiness_nonce")] public string? ReadinessNonce { get; init; }
    [JsonPropertyName("auto_redispatch")] public bool AutoRedispatch { get; init; }
    [JsonPropertyName("auto_redispatched")] public bool AutoRedispatched { get; init; }
    [JsonPropertyName("resend_permitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ResendPermitted { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifySupervisorPass
{
    public required IReadOnlyList<NotifySupervisorAction> Actions { get; init; }
    public IReadOnlyList<NotifySupervisionFinding> Findings { get; init; } = [];
    public IReadOnlyList<NotifySupervisionStallRecord> RecoveryRecords { get; init; } = [];
    public NotifySupervisionBoundStatus? Bound { get; init; }
    public NotifyPreApprovalPolicyStatus? PreApprovalPolicy { get; init; }
    public NotifySupervisionLiveness? Liveness { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? Error { get; init; }
    public bool Silent => Actions.Count == 0 && Findings.Count == 0 && Warnings.Count == 0 && Error is null;
    public int ExitCode => Error is null
        && Actions.All(action => action.Cause is null)
        && Findings.All(finding => finding.Cause is null)
        && !Findings.Any(finding => string.Equals(finding.Kind, "supervisor-not-running", StringComparison.Ordinal))
        && Bound?.BoundMet is not false
        ? 0
        : 1;
}

internal sealed record NotifySupervisorDeliveryResult
{
    public required bool Resolved { get; init; }
    public required bool Delivered { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
}

internal sealed record NotifySupervisorRedispatchResult
{
    public required bool Resolved { get; init; }
    public required bool Redispatched { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
}

internal sealed record NotifySupervisorProcess(
    long Pid,
    string? Cwd,
    string? Name);

internal sealed record NotifySupervisorProcessInfo
{
    public required bool Resolved { get; init; }
    public required IReadOnlyList<NotifySupervisorProcess> Processes { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// One bounded wake of the G630 recipient supervisor. The loop is deliberately
/// separate from the command parser so tests can exercise several wakes without
/// starting an unbounded process. Live and settled records are intentionally
/// silent; only a lost record or an unverifiable recovery step produces output.
/// </summary>
internal sealed class NotifySupervisor
{
    public const int DefaultIntervalSeconds = 30;
    public const int MaximumIntervalSeconds = 3600;
    private const int ProcessCheckTimeoutMilliseconds = 10_000;
    private const int PromptTimeoutMilliseconds = 10_000;
    private const int ReadinessReadAttempts = 4;
    private const int ReadinessReadDelayMilliseconds = 100;
    private const string ReadinessPrefix = "READY ";

    internal static Func<string> NonceFactory { get; set; } = () => Guid.NewGuid().ToString("N");
    internal static Action<TimeSpan> Delay { get; set; } = Thread.Sleep;

    private readonly CliContext context;
    private readonly string routingRoot;
    private readonly string domain;
    private readonly string team;
    private readonly bool autoRedispatch;
    private readonly bool write;
    private readonly string format;
    private readonly INotifyProcessRunner runner;
    private readonly string herdrExecutable;
    private readonly string agmsgScriptsDirectory;
    private readonly string bashExecutable;
    private readonly Func<NotifyPendingDelegation, NotifySupervisorRedispatchResult>? redispatch;
    private readonly Func<NotifyPendingDelegation, string, NotifySupervisorDeliveryResult>? notifier;

    public NotifySupervisor(
        CliContext context,
        string routingRoot,
        string domain,
        string team,
        bool autoRedispatch,
        bool write,
        string format,
        INotifyProcessRunner runner,
        string herdrExecutable,
        string agmsgScriptsDirectory,
        Func<NotifyPendingDelegation, NotifySupervisorRedispatchResult>? redispatch = null,
        Func<NotifyPendingDelegation, string, NotifySupervisorDeliveryResult>? notifier = null,
        string? bashExecutable = null)
    {
        this.context = context;
        this.routingRoot = routingRoot;
        this.domain = domain;
        this.team = team;
        this.autoRedispatch = autoRedispatch;
        this.write = write;
        this.format = format;
        this.runner = runner;
        this.herdrExecutable = herdrExecutable;
        this.agmsgScriptsDirectory = agmsgScriptsDirectory;
        this.bashExecutable = bashExecutable ?? "bash";
        this.redispatch = redispatch;
        this.notifier = notifier;
    }

    public NotifySupervisorPass RunOnce()
    {
        var open = NotifyPendingDelegationStore.ReadOpen(routingRoot, domain, team, out var readError);
        if (readError is not null)
        {
            return FailurePass(
                "pending-store-unreadable",
                $"Could not read open pending delegations for team '{team}': {readError}");
        }

        var actions = new List<NotifySupervisorAction>();
        foreach (var record in open)
        {
            var transportMode = record.TransportMode ?? SessionLayerMode.Agmsg;
            if (transportMode is not (SessionLayerMode.Agmsg or SessionLayerMode.HerdrOnly))
            {
                actions.Add(Stopped(record, "session-layer-mode-invalid",
                    $"Pending delegation recorded unsupported session-layer mode '{transportMode}'; no supervision action was taken."));
                continue;
            }

            var liveness = NotifyPendingLiveness.Probe(
                routingRoot,
                record,
                transportMode,
                runner,
                herdrExecutable,
                agmsgScriptsDirectory,
                bashExecutable);
            if (string.Equals(
                liveness.State,
                NotifyPendingLivenessResult.RegistrationLostProcessPresent,
                StringComparison.Ordinal))
            {
                // Registration loss with a corroborating foreground process
                // is not G630 recipient loss. Surface the distinct state but
                // never kill, restart, register, or redispatch automatically.
                actions.Add(RegistrationLostProcessPresent(record, liveness.Summary));
                continue;
            }

            if (!liveness.Resolved || liveness.Running is null)
            {
                actions.Add(Stopped(record, liveness.Cause ?? "liveness-unavailable", liveness.Summary));
                continue;
            }

            if (liveness.Running.Value)
            {
                // Healthy recipients are the normal path. Silence is a
                // deliberate contract, not a missing result.
                continue;
            }

            actions.Add(write
                ? RecoverLost(record)
                : PreviewLost(record));
        }

        return new NotifySupervisorPass { Actions = actions };
    }

    public int RunLoop(int intervalSeconds, bool once, TextWriter writer, CancellationToken cancellationToken)
    {
        if (intervalSeconds is < 1 or > MaximumIntervalSeconds)
        {
            writer.WriteLine($"invalid-supervision: --interval must be between 1 and {MaximumIntervalSeconds} seconds.");
            return 1;
        }

        do
        {
            var pass = RunOnce();
            if (!pass.Silent)
            {
                NotifyCommand.EmitSupervision(writer, pass, domain, team, intervalSeconds, autoRedispatch, write, format);
            }

            if (once || cancellationToken.IsCancellationRequested)
            {
                return pass.ExitCode;
            }

            Delay(TimeSpan.FromSeconds(intervalSeconds));
        }
        while (!cancellationToken.IsCancellationRequested);

        return 0;
    }

    private NotifySupervisorAction RecoverLost(NotifyPendingDelegation record)
    {
        if (!string.Equals(record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
        {
            return Stopped(record, "recovery-unsupported-resident",
                "The lost pending delegation is not backed by a herdr resident; no replacement was started.");
        }

        var missing = MissingRecoveryField(record);
        if (missing is not null)
        {
            return Stopped(record, "recovery-contract-incomplete",
                $"Recovery stopped before mutation because the pending record is missing {missing}; "
                + "the recorded unattended-launch recipe and delegating role are required.");
        }

        var initialAgents = ReadAgents(record);
        if (!initialAgents.Resolved)
        {
            return Stopped(record, initialAgents.Cause!, initialAgents.Summary);
        }

        var initialRunningCount = RunningAgentCountAtRecordedPane(initialAgents.Agents!, record);
        if (initialRunningCount > 0)
        {
            return Stopped(record,
                initialRunningCount > 1 ? "multiple-agents-at-pane" : "recipient-not-gone",
                initialRunningCount > 1
                    ? $"Recovery stopped because {initialRunningCount} running recipients occupy recorded workspace '{record.WorkspaceId}' pane '{record.PaneId}'."
                    : $"Recovery stopped because recipient '{record.RecipientRole}' is running again at recorded "
                      + $"workspace '{record.WorkspaceId}' pane '{record.PaneId}'; it may have been mid-exit.");
        }

        var processInfo = ReadProcessInfo(record);
        if (!processInfo.Resolved)
        {
            return Stopped(record, processInfo.Cause!, processInfo.Summary);
        }

        if (processInfo.Processes.Count > 0)
        {
            var attribution = VerifyProcessOwnership(record, processInfo.Processes);
            if (attribution is not null)
            {
                return Stopped(record, "process-attribution-unverified", attribution);
            }

            foreach (var process in processInfo.Processes)
            {
                var killed = runner.Run("kill", ["-TERM", process.Pid.ToString(CultureInfo.InvariantCulture)]);
                if (killed.ExitCode != 0)
                {
                    return Stopped(record, "old-process-kill-failed",
                        $"Could not terminate old process pid {process.Pid} for recorded cwd '{record.Cwd}': "
                        + OneLine(killed.StandardError, killed.StandardOutput));
                }
            }

            var afterKill = ReadProcessInfo(record);
            if (!afterKill.Resolved)
            {
                return Stopped(record, afterKill.Cause!, afterKill.Summary);
            }

            if (afterKill.Processes.Count > 0)
            {
                return Stopped(record, "old-process-still-present",
                    $"Recovery stopped because one or more old processes remain at recorded pane '{record.PaneId}' "
                    + "after the pid-targeted termination; no replacement was started.");
            }
        }

        var gone = ReadAgents(record);
        if (!gone.Resolved)
        {
            return Stopped(record, gone.Cause!, gone.Summary);
        }

        var goneRunningCount = RunningAgentCountAtRecordedPane(gone.Agents!, record);
        if (goneRunningCount > 0)
        {
            return Stopped(record,
                goneRunningCount > 1 ? "multiple-agents-at-pane" : "recipient-not-gone",
                goneRunningCount > 1
                    ? $"Recovery stopped because {goneRunningCount} running recipients reappeared before replacement could start."
                    : "Recovery stopped because a running recipient reappeared before replacement could start.");
        }

        var start = StartRecipe(record);
        if (!start.Resolved)
        {
            return Stopped(record, start.Cause!, start.Summary);
        }

        var started = ReadAgents(record);
        if (!started.Resolved)
        {
            return Stopped(record, started.Cause!, started.Summary);
        }

        var startedRunningCount = RunningAgentCountAtRecordedPane(started.Agents!, record);
        if (startedRunningCount != 1)
        {
            return Stopped(record,
                startedRunningCount > 1 ? "multiple-agents-at-pane" : "replacement-not-running",
                startedRunningCount > 1
                    ? $"The recorded launch recipe returned successfully, but {startedRunningCount} running replacements were observed at workspace '{record.WorkspaceId}' pane '{record.PaneId}'."
                    : $"The recorded launch recipe returned successfully, but no running replacement was observed at "
                      + $"workspace '{record.WorkspaceId}' pane '{record.PaneId}'.");
        }

        var startedAgent = AgentAtRecordedPane(started.Agents!, record)!;
        if (!string.Equals(startedAgent.Cwd, record.Cwd, StringComparison.Ordinal))
        {
            return Stopped(record, "replacement-cwd-mismatch",
                $"The replacement at pane '{record.PaneId}' reported cwd '{startedAgent.Cwd ?? "missing"}', not "
                + $"the recorded role cwd '{record.Cwd}'. Readiness was not claimed.");
        }

        var nonce = NonceFactory();
        var registration = Register(record, nonce);
        if (!registration.Resolved)
        {
            return Stopped(record, registration.Cause!, registration.Summary);
        }

        var readiness = ProveReadiness(record, nonce);
        if (!readiness.Resolved)
        {
            return Stopped(record, readiness.Cause!, readiness.Summary);
        }

        var autoRedispatched = false;
        NotifySupervisorRedispatchResult? redispatchResult = null;
        if (autoRedispatch)
        {
            redispatchResult = write
                ? (redispatch?.Invoke(record) ?? DefaultRedispatch(record))
                : new NotifySupervisorRedispatchResult
                {
                    Resolved = true,
                    Redispatched = false,
                    Summary = "Dry-run: readiness was proven; auto-redispatch was not sent.",
                };
            autoRedispatched = redispatchResult.Redispatched;
        }

        var notification = BuildLossNotification(record, nonce, autoRedispatched, redispatchResult);
        var notificationResult = write
            ? (notifier?.Invoke(record, notification) ?? NotifySupervisorDelivery.Send(
                routingRoot,
                record,
                notification,
                runner,
                herdrExecutable,
                agmsgScriptsDirectory,
                bashExecutable))
            : new NotifySupervisorDeliveryResult
            {
                Resolved = true,
                Delivered = false,
                Summary = "Dry-run: readiness was proven; the delegating role was not notified.",
            };

        if (!notificationResult.Resolved || (write && !notificationResult.Delivered))
        {
            return new NotifySupervisorAction
            {
                TaskId = record.TaskId,
                RecipientRole = record.RecipientRole,
                Verdict = "lost",
                Outcome = "recovered-but-not-reported",
                Recovered = true,
                ReadinessNonce = nonce,
                AutoRedispatch = autoRedispatch,
                AutoRedispatched = autoRedispatched,
                Cause = notificationResult.Cause ?? "loss-notification-failed",
                Summary = "Replacement readiness was proven, but the required loss notification could not be "
                    + $"delivered to delegating role '{record.DelegatingRole}': {notificationResult.Summary}",
            };
        }

        var redispatchFailure = redispatchResult is { Resolved: false }
            ? redispatchResult.Cause ?? "auto-redispatch-failed"
            : null;
        return new NotifySupervisorAction
        {
            TaskId = record.TaskId,
            RecipientRole = record.RecipientRole,
            Verdict = "lost",
            Outcome = autoRedispatched ? "recovered-and-redispatched" : "recovered-and-reported",
            Recovered = true,
            ReadinessNonce = nonce,
            AutoRedispatch = autoRedispatch,
            AutoRedispatched = autoRedispatched,
            Cause = redispatchFailure,
            Summary = redispatchFailure is null
                ? notificationResult.Summary
                : notificationResult.Summary + $" Auto-redispatch did not complete: {redispatchResult!.Summary}",
        };
    }

    private static NotifySupervisorAction PreviewLost(NotifyPendingDelegation record) => new()
    {
        TaskId = record.TaskId,
        RecipientRole = record.RecipientRole,
        Verdict = "lost",
        Outcome = "dry-run-would-recover",
        Recovered = false,
        AutoRedispatch = false,
        AutoRedispatched = false,
        Summary = $"Dry-run: recipient '{record.RecipientRole}' is lost; would verify process ownership, start the "
            + "recorded recipe, prove a response-line nonce, and notify the delegating role before any optional re-dispatch.",
    };

    private static NotifySupervisorAction RegistrationLostProcessPresent(
        NotifyPendingDelegation record,
        string summary) => new()
        {
            TaskId = record.TaskId,
            RecipientRole = record.RecipientRole,
            Verdict = NotifyPendingLivenessResult.RegistrationLostProcessPresent,
            Outcome = NotifyPendingLivenessResult.RegistrationLostProcessPresent,
            Recovered = false,
            AutoRedispatch = false,
            AutoRedispatched = false,
            ResendPermitted = true,
            Cause = null,
            Summary = summary,
        };

    private NotifySupervisorRedispatchResult DefaultRedispatch(NotifyPendingDelegation record)
    {
        var expectedArtifacts = record.ExpectedArtifacts?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (string.IsNullOrWhiteSpace(record.DelegatingRole)
            || string.IsNullOrWhiteSpace(record.ReportToRole)
            || string.IsNullOrWhiteSpace(record.Objective)
            || expectedArtifacts is not { Length: > 0 })
        {
            return new NotifySupervisorRedispatchResult
            {
                Resolved = false,
                Redispatched = false,
                Cause = "redispatch-contract-missing",
                Summary = "The pending record does not contain the original delegation contract needed for auto-redispatch.",
            };
        }

        var args = new List<string>
        {
            "notify", "delegate",
            "--domain", record.Domain,
            "--team", record.Team,
            "--from", record.DelegatingRole,
            "--to", record.RecipientRole,
            "--report-to", record.ReportToRole,
            "--task-id", record.TaskId,
            "--objective", record.Objective,
        };

        foreach (var input in record.Inputs ?? [])
        {
            args.Add("--input");
            args.Add(input);
        }
        foreach (var artifact in expectedArtifacts)
        {
            args.Add("--expected-artifact");
            args.Add(artifact);
        }

        args.Add("--result-nonce");
        args.Add(NonceFactory());
        args.Add("--routing-root");
        args.Add(routingRoot);
        args.Add("--write");
        args.Add("--format");
        args.Add("json");
        if (Path.IsPathRooted(herdrExecutable))
        {
            args.Add("--herdr-executable");
            args.Add(herdrExecutable);
        }
        if (Path.IsPathRooted(bashExecutable))
        {
            args.Add("--bash-executable");
            args.Add(bashExecutable);
        }

        var previousFactory = NotifyCommand.ProcessRunnerFactory;
        NotifyCommand.ProcessRunnerFactory = () => runner;
        try
        {
            using var output = new StringWriter();
            var exitCode = NotifyCommand.ExecuteDelegate(context, args.ToArray(), output);
            if (exitCode != 0)
            {
                return new NotifySupervisorRedispatchResult
                {
                    Resolved = false,
                    Redispatched = false,
                    Cause = "auto-redispatch-failed",
                    Summary = OneLine(output.ToString()),
                };
            }

            return new NotifySupervisorRedispatchResult
            {
                Resolved = true,
                Redispatched = true,
                Summary = "The lost task was re-dispatched through the normal notify delegate path.",
            };
        }
        finally
        {
            NotifyCommand.ProcessRunnerFactory = previousFactory;
        }
    }

    private string BuildLossNotification(
        NotifyPendingDelegation record,
        string nonce,
        bool autoRedispatched,
        NotifySupervisorRedispatchResult? redispatchResult) => JsonSerializer.Serialize(new
        {
            notification = "supervise-recovery",
            task_id = record.TaskId,
            recipient_role = record.RecipientRole,
            recovered = true,
            lost = true,
            must_redispatch = !autoRedispatched,
            auto_redispatched = autoRedispatched,
            readiness_nonce = nonce,
            summary = autoRedispatched
                ? "Recipient recovered and the lost task was re-dispatched."
                : "Recipient recovered; the in-flight task was lost and must be re-dispatched.",
            redispatch_detail = redispatchResult?.Summary,
        });

    private NotifySupervisorStepResult StartRecipe(NotifyPendingDelegation record)
    {
        if (string.IsNullOrWhiteSpace(record.Kind) || string.IsNullOrWhiteSpace(record.Cwd))
        {
            return NotifySupervisorStepResult.Failure(
                "launch-recipe-unknown",
                "The pending record has no recorded agent kind and role cwd, so no unattended-launch recipe can be selected.");
        }

        var arguments = new List<string>
        {
            "agent", "start", record.RecipientRole,
            "--kind", record.Kind,
            "--pane", record.PaneId!,
            "--timeout", ProcessCheckTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
        };
        if (record.LaunchArguments is { Count: > 0 })
        {
            arguments.Add("--");
            arguments.AddRange(record.LaunchArguments);
        }

        NotifyProcessResult result;
        try
        {
            result = runner.Run(herdrExecutable, arguments);
        }
        catch (InvalidOperationException exception)
        {
            return NotifySupervisorStepResult.Failure("launch-failed", exception.Message);
        }

        return result.ExitCode == 0
            ? NotifySupervisorStepResult.Success("The recorded unattended-launch recipe returned successfully.")
            : NotifySupervisorStepResult.Failure(
                "launch-failed",
                $"The recorded unattended-launch recipe failed: {OneLine(result.StandardError, result.StandardOutput)}");
    }

    private NotifySupervisorStepResult Register(NotifyPendingDelegation record, string nonce)
    {
        var actas = string.Equals(record.Kind, "claude", StringComparison.OrdinalIgnoreCase)
            ? $"/agmsg actas {record.RecipientRole}"
            : $"$agmsg actas {record.RecipientRole}";
        var prompt = actas + ". Register this role, then reply with exactly "
            + $"{ReadinessPrefix}{nonce} on a separate response line.";
        NotifyProcessResult result;
        try
        {
            result = runner.Run(
                herdrExecutable,
                [
                    "agent", "prompt", record.PaneId!, prompt,
                    "--wait", "--until", "working",
                    "--timeout", PromptTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
                ]);
        }
        catch (InvalidOperationException exception)
        {
            return NotifySupervisorStepResult.Failure("registration-failed", exception.Message);
        }

        return result.ExitCode == 0
            ? NotifySupervisorStepResult.Success("The replacement received its registering prompt.")
            : NotifySupervisorStepResult.Failure(
                "registration-failed",
                $"The replacement registering prompt failed: {OneLine(result.StandardError, result.StandardOutput)}");
    }

    private NotifySupervisorStepResult ProveReadiness(NotifyPendingDelegation record, string nonce)
    {
        var expected = ReadinessPrefix + nonce;
        for (var attempt = 0; attempt < ReadinessReadAttempts; attempt++)
        {
            NotifyProcessResult read;
            try
            {
                read = runner.Run(
                    herdrExecutable,
                    ["agent", "read", record.PaneId!, "--source", "recent-unwrapped", "--lines", "200"]);
            }
            catch (InvalidOperationException exception)
            {
                return NotifySupervisorStepResult.Failure("readiness-read-failed", exception.Message);
            }

            if (read.ExitCode != 0)
            {
                return NotifySupervisorStepResult.Failure(
                    "readiness-read-failed",
                    $"Could not read the replacement response line: {OneLine(read.StandardError, read.StandardOutput)}");
            }

            if (HasResponseLine(read.StandardOutput, expected))
            {
                return NotifySupervisorStepResult.Success(
                    $"Readiness was proven by response line '{expected}'; a pane-wide nonce match was not used.");
            }

            if (attempt + 1 < ReadinessReadAttempts)
            {
                Delay(TimeSpan.FromMilliseconds(ReadinessReadDelayMilliseconds));
            }
        }

        return NotifySupervisorStepResult.Failure(
            "readiness-unproven",
            $"No response line exactly matching '{expected}' was observed. A nonce in an unsent prompt echo is not readiness.");
    }

    private NotifySupervisorAgentListResult ReadAgents(NotifyPendingDelegation record)
    {
        NotifyProcessResult result;
        try
        {
            result = runner.Run(herdrExecutable, ["agent", "list"]);
        }
        catch (InvalidOperationException exception)
        {
            return NotifySupervisorAgentListResult.Failure("agent-list-failed", exception.Message);
        }

        if (result.ExitCode != 0)
        {
            return NotifySupervisorAgentListResult.Failure(
                "agent-list-failed",
                $"herdr agent list failed while supervising task '{record.TaskId}': {OneLine(result.StandardError, result.StandardOutput)}");
        }

        try
        {
            return NotifySupervisorAgentListResult.Success(HerdrNotifyTransport.ParseAgents(result.StandardOutput));
        }
        catch (InvalidOperationException exception)
        {
            return NotifySupervisorAgentListResult.Failure("agent-list-invalid", exception.Message);
        }
    }

    private NotifySupervisorProcessInfo ReadProcessInfo(NotifyPendingDelegation record)
    {
        NotifyProcessResult result;
        try
        {
            result = runner.Run(herdrExecutable, ["pane", "process-info", "--pane", record.PaneId!]);
        }
        catch (InvalidOperationException exception)
        {
            return FailureProcessInfo("process-info-failed", exception.Message);
        }

        if (result.ExitCode != 0)
        {
            return FailureProcessInfo(
                "process-info-failed",
                $"herdr pane process-info failed for pane '{record.PaneId}': {OneLine(result.StandardError, result.StandardOutput)}");
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var processInfo = document.RootElement.GetProperty("result").GetProperty("process_info");
            var processes = new List<NotifySupervisorProcess>();
            if (!processInfo.TryGetProperty("foreground_processes", out var foreground)
                || foreground.ValueKind != JsonValueKind.Array)
            {
                return FailureProcessInfo(
                    "process-info-invalid",
                    $"herdr pane process-info did not report a foreground_processes array for pane '{record.PaneId}'; refusing to assume the old process is gone.");
            }

            if (foreground.ValueKind == JsonValueKind.Array)
            {
                foreach (var process in foreground.EnumerateArray())
                {
                    if (!process.TryGetProperty("pid", out var pidElement)
                        || !pidElement.TryGetInt64(out var pid)
                        || pid <= 0)
                    {
                        return FailureProcessInfo(
                            "process-info-invalid",
                            $"herdr pane process-info returned a foreground process without a valid pid for pane '{record.PaneId}'.");
                    }

                    var cwd = process.TryGetProperty("cwd", out var cwdElement)
                        && cwdElement.ValueKind == JsonValueKind.String
                        ? cwdElement.GetString()
                        : null;
                    var name = process.TryGetProperty("name", out var nameElement)
                        && nameElement.ValueKind == JsonValueKind.String
                        ? nameElement.GetString()
                        : null;
                    processes.Add(new NotifySupervisorProcess(pid, cwd, name));
                }
            }

            return new NotifySupervisorProcessInfo
            {
                Resolved = true,
                Processes = processes,
                Summary = $"Read process ownership for recorded pane '{record.PaneId}'.",
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return FailureProcessInfo(
                "process-info-invalid",
                $"herdr pane process-info returned an unreadable process shape for pane '{record.PaneId}': {exception.Message}");
        }
    }

    private static string? VerifyProcessOwnership(
        NotifyPendingDelegation record,
        IReadOnlyList<NotifySupervisorProcess> processes)
    {
        foreach (var process in processes)
        {
            if (string.IsNullOrWhiteSpace(process.Cwd))
            {
                return $"Process pid {process.Pid} did not report a cwd; refusing a cross-team kill.";
            }

            if (!string.Equals(process.Cwd, record.Cwd, StringComparison.Ordinal))
            {
                return $"Process pid {process.Pid} reports cwd '{process.Cwd}', not recorded role cwd '{record.Cwd}'; refusing a cross-team kill.";
            }
        }

        return null;
    }

    private static bool HasResponseLine(string output, string expected) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Any(line => string.Equals(line, expected, StringComparison.Ordinal));

    private static int RunningAgentCountAtRecordedPane(
        IReadOnlyList<HerdrAgentState> agents,
        NotifyPendingDelegation record) => agents.Count(agent =>
            string.Equals(agent.WorkspaceId, record.WorkspaceId, StringComparison.Ordinal)
            && string.Equals(agent.PaneId, record.PaneId, StringComparison.Ordinal)
            && agent.AgentRunning);

    private static HerdrAgentState? AgentAtRecordedPane(
        IReadOnlyList<HerdrAgentState> agents,
        NotifyPendingDelegation record)
    {
        var running = agents
            .Where(agent => string.Equals(agent.WorkspaceId, record.WorkspaceId, StringComparison.Ordinal)
                && string.Equals(agent.PaneId, record.PaneId, StringComparison.Ordinal)
                && agent.AgentRunning)
            .ToArray();
        return running.Length == 1 ? running[0] : null;
    }

    private static string? MissingRecoveryField(NotifyPendingDelegation record)
    {
        if (string.IsNullOrWhiteSpace(record.WorkspaceId)) return "workspace_id";
        if (string.IsNullOrWhiteSpace(record.PaneId)) return "pane_id";
        if (string.IsNullOrWhiteSpace(record.Cwd)) return "cwd";
        if (string.IsNullOrWhiteSpace(record.Kind)) return "kind / unattended-launch recipe";
        if (string.IsNullOrWhiteSpace(record.DelegatingRole)) return "delegating_role";
        if (string.IsNullOrWhiteSpace(record.ReportToRole)) return "report_to_role";
        return null;
    }

    private static NotifySupervisorAction Stopped(
        NotifyPendingDelegation record,
        string cause,
        string summary) => new()
        {
            TaskId = record.TaskId,
            RecipientRole = record.RecipientRole,
            Verdict = "lost",
            Outcome = "stopped-fail-closed",
            Recovered = false,
            AutoRedispatch = false,
            AutoRedispatched = false,
            Cause = cause,
            Summary = summary,
        };

    private static NotifySupervisorPass FailurePass(string cause, string summary) => new()
    {
        Error = $"{cause}: {summary}",
        Actions = [],
    };

    private static NotifySupervisorProcessInfo FailureProcessInfo(string cause, string summary) => new()
    {
        Resolved = false,
        Processes = [],
        Cause = cause,
        Summary = summary,
    };

    private static string OneLine(params string[] values)
    {
        var value = values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "no detail";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record NotifySupervisorStepResult
    {
        public required bool Resolved { get; init; }
        public string? Cause { get; init; }
        public required string Summary { get; init; }

        public static NotifySupervisorStepResult Success(string summary) => new()
        {
            Resolved = true,
            Summary = summary,
        };

        public static NotifySupervisorStepResult Failure(string cause, string summary) => new()
        {
            Resolved = false,
            Cause = cause,
            Summary = summary,
        };
    }

    private sealed record NotifySupervisorAgentListResult
    {
        public required bool Resolved { get; init; }
        public IReadOnlyList<HerdrAgentState>? Agents { get; init; }
        public string? Cause { get; init; }
        public required string Summary { get; init; }

        public static NotifySupervisorAgentListResult Success(IReadOnlyList<HerdrAgentState> agents) => new()
        {
            Resolved = true,
            Agents = agents,
            Summary = "Read the recorded herdr agent list.",
        };

        public static NotifySupervisorAgentListResult Failure(string cause, string summary) => new()
        {
            Resolved = false,
            Cause = cause,
            Summary = summary,
        };
    }
}

internal static class NotifySupervisorDelivery
{
    public static NotifySupervisorDeliveryResult Send(
        string routingRoot,
        NotifyPendingDelegation record,
        string payload,
        INotifyProcessRunner runner,
        string herdrExecutable,
        string agmsgScriptsDirectory,
        string? bashExecutable = null)
    {
        if (string.IsNullOrWhiteSpace(record.DelegatingRole))
        {
            return Failure("delegating-role-missing", "The pending record has no delegating role for the loss notification.");
        }

        SessionLayerModeResolution resolution;
        try
        {
            resolution = string.IsNullOrWhiteSpace(record.TransportMode)
                ? SessionLayerModeStore.Resolve(routingRoot, record.Domain, record.Team)
                : new SessionLayerModeResolution
                {
                    Mode = record.TransportMode,
                    Source = SessionLayerModeSource.Recorded,
                };
        }
        catch (InvalidOperationException exception)
        {
            return Failure("session-layer-mode-unreadable", exception.Message);
        }

        if (resolution.Mode is not (SessionLayerMode.Agmsg or SessionLayerMode.HerdrOnly))
        {
            return Failure(
                "session-layer-mode-invalid",
                $"Pending delegation recorded unsupported session-layer mode '{resolution.Mode}'; loss notification was not sent.");
        }

        var transport = string.Equals(resolution.Mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal)
            ? (INotifyTransport)new HerdrNotifyTransport(runner, herdrExecutable)
            : new AgmsgNotifyTransport(runner, agmsgScriptsDirectory, bashExecutable);
        var delivery = transport.Deliver(
            routingRoot,
            record.Domain,
            record.Team,
            "supervisor",
            record.DelegatingRole,
            [record.DelegatingRole],
            payload,
            write: true);
        if (!delivery.Resolved)
        {
            return Failure(delivery.Cause ?? "loss-notification-failed", delivery.Summary);
        }

        if (delivery.ReaderPath is not null)
        {
            try
            {
                NotifyEventWriter.Append(delivery.ReaderPath, new NotifyDesignEvent
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Team = record.Team,
                    Kind = "escalation",
                    Unit = record.TaskId,
                    Summary = NotifyEventWriter.NormalizeSummary(payload),
                    Artifact = record.TaskId,
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure("loss-notification-event-failed", exception.Message);
            }
        }

        return new NotifySupervisorDeliveryResult
        {
            Resolved = true,
            Delivered = true,
            Summary = $"Notified delegating role '{record.DelegatingRole}' that task '{record.TaskId}' was lost and its recipient was recovered.",
        };
    }

    private static NotifySupervisorDeliveryResult Failure(string cause, string summary) => new()
    {
        Resolved = false,
        Delivered = false,
        Cause = cause,
        Summary = summary,
    };
}
