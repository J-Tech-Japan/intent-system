using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class NotifyCommand
{
    private const string OperationDelegate = "delegate";
    private const string OperationReport = "report";
    private const string OperationCollect = "collect";
    private const string OperationReconcile = "reconcile";
    private const string OperationEscalate = "escalate";
    private const string OperationDispose = "dispose";
    internal const string OperationStatus = "status";
    internal const string OperationSupervise = "supervise";
    private const string CompletionEventKind = "completion";
    private const string BlockedEventKind = "blocked";
    private const string QuestionEventKind = "question";
    private const string EscalationEventKind = "escalation";
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string CopilotObservedPasteRiskProfile = "copilot-autopilot-observed-paste-risk";
    private const int CopilotObservedPasteRiskWarningChars = 4096;
    private const string ReferenceFirstRemedy =
        "Use reference-first dispatch: put review substance in committed canonical review-context.md, push it, and delegate a terse pointer.";
    private const string DispositionKindSuperseded = "superseded";
    private const string DispositionKindAppliedElsewhere = "applied-elsewhere";

    private const string MissingRecipientWorkRootPlaceholder = "<role-work-root>";

    private const string DelegateUsage =
        "Usage: intent-cli notify delegate --domain <d> [--team <t>] --from <role> --to <role> --report-to <role> "
        + "--task-id <id> --objective <text> [--input <value>]... --expected-artifact <value> "
        + "[--expected-artifact <value>]... --result-nonce <nonce> [--routing-root <host-root>] "
        + "[--dry-run|--write] [--format markdown|json]";

    private const string ReportUsage =
        "Usage: intent-cli notify report --domain <d> --team <t> --from <role> --to <role> --task-id <id> "
        + "--status completed|blocked|question --artifact <value> --summary <text> "
        + "[--routing-root <host-root>] [--report-root <role-work-root>] [--dry-run|--write] [--format markdown|json]";

    private const string CollectUsage =
        "Usage: intent-cli notify collect --domain <d> --team <t> (--task-id <id> | --role <role> "
        + "[--since <cursor>] [--wait --timeout-ms <milliseconds>]) "
        + "[--routing-root <host-root>] [--report-root <role-work-root>] [--dry-run|--write] [--format markdown|json]";

    private const int MaximumRoleCollectTimeoutMilliseconds = 300_000;
    private const int RoleCollectPollMilliseconds = 25;

    private const string ReconcileUsage =
        "Usage: intent-cli notify reconcile --domain <d> --team <t> --task-id <id> "
        + "--routing-root <host-root> --report-root <role-work-root> [--dry-run|--write] [--format markdown|json]";

    private const string EscalateUsage =
        "Usage: intent-cli notify escalate --domain <d> --team <t> --from <role> --task-id <id> "
        + "--artifact <value> --summary <text> [--routing-root <host-root>] "
        + "[--dry-run|--write] [--format markdown|json]";

    private const string DisposeUsage =
        "Usage: intent-cli notify dispose --domain <d> --team <t> --task-id <id> "
        + "--kind superseded|applied-elsewhere --actor <actor> --reason <text> "
        + "[--superseding-task-id <id>] [--applied-outcome-evidence <text>] "
        + "[--routing-root <host-root>] [--dry-run|--write] [--format markdown|json]";

    private const string StatusUsage =
        "Usage: intent-cli notify status --task-id <id> [--domain <d> --team <t>] "
        + "[--routing-root <host-root>] [--format markdown|json]";

    private const string SuperviseUsage =
        "Usage: intent-cli notify supervise --domain <d> --team <t> [--interval <seconds>] "
        + "[--repo <owner/repo>] [--owner-role <role>] [--bound <seconds>] "
        + "[--repeat-backoff-seconds <seconds>] [--debounce-consecutive-observations <count>] "
        + "[--delegation-execution-window-seconds <seconds>; default 300] "
        + "[--stale-minutes <m>] [--claimed-silent-minutes <m>] [--backlog-idle-minutes <m>] "
        + "[--repair-silent-minutes <m>] [--auto-redispatch] [--event-mode] [--once] [--routing-root <host-root>] [--dry-run|--write] "
        + "[--herdr-executable <absolute-path>] [--bash-executable <absolute-path>] "
        + "[--pre-approve <agent-kind>:<prompt-class>]... [--pre-escalate <agent-kind>:<prompt-class>]... "
        + "[--shell-policy <json>]... "
        + "[--format markdown|json]\n"
        + NotifySuperviseLivenessCommand.Usage + "\n"
        + "Event mode: --event-mode keeps the blocking per-seat herdr wait inside this supervisor process and re-arms it after failure. It is the implementation of the normative SECOND wake source from herdr pane.agent_status_changed, alongside the independent interval safety floor; it does not change outcome or label behavior.\n"
        + NotifySuperviseShrinkCommand.Usage + "\n"
        + NotifySuperviseArchiveCommand.Usage + "\n"
        + NotifySuperviseRepairCycleHistoryCommand.Usage + "\n"
        + NotifySuperviseInstallCommand.Usage + "\n"
        + NotifySuperviseReconcileCommand.Usage;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly IReadOnlyDictionary<string, string> ReportReaderEventKinds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["completed"] = CompletionEventKind,
            ["blocked"] = BlockedEventKind,
            ["question"] = QuestionEventKind,
        };

    internal static IEnumerable<string> SupportedReportStatuses => ReportReaderEventKinds.Keys;

    internal static Func<INotifyProcessRunner>? ProcessRunnerFactory { get; set; }

    internal static Func<string>? AgmsgScriptsDirectoryFactory { get; set; }

    internal static Func<string>? HerdrExecutableFactory { get; set; }

    internal static Func<string>? BashExecutableFactory { get; set; }

    internal static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    public static int ExecuteDelegate(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationDelegate);

    public static int ExecuteReport(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationReport);

    public static int ExecuteCollect(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationCollect);

    public static int ExecuteReconcile(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationReconcile);

    public static int ExecuteEscalate(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationEscalate);

    public static int ExecuteDispose(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationDispose);

    public static int ExecuteStatus(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationStatus);

    public static int ExecuteSupervise(CliContext context, string[] args, TextWriter writer)
    {
        if (args.Length == 0)
        {
            return Execute(context, args, writer, OperationSupervise);
        }

        return args[0] switch
        {
            NotifySuperviseLivenessCommand.Operation =>
                NotifySuperviseLivenessCommand.Execute(context, args[1..], writer),
            NotifySuperviseArchiveCommand.Operation =>
                NotifySuperviseArchiveCommand.Execute(context, args[1..], writer),
            NotifySuperviseShrinkCommand.Operation =>
                NotifySuperviseShrinkCommand.Execute(context, args[1..], writer),
            NotifySuperviseRepairCycleHistoryCommand.Operation =>
                NotifySuperviseRepairCycleHistoryCommand.Execute(context, args[1..], writer),
            NotifySuperviseInstallCommand.Operation =>
                NotifySuperviseInstallCommand.Execute(context, args[1..], writer),
            NotifySuperviseReconcileCommand.ReconcileOperation =>
                NotifySuperviseReconcileCommand.Execute(
                    context,
                    args[1..],
                    writer,
                    NotifySuperviseReconcileCommand.ReconcileOperation),
            NotifySuperviseReconcileCommand.UninstallOperation =>
                NotifySuperviseReconcileCommand.Execute(
                    context,
                    args[1..],
                    writer,
                    NotifySuperviseReconcileCommand.UninstallOperation),
            _ => Execute(context, args, writer, OperationSupervise),
        };
    }

    private static int Execute(CliContext context, string[] args, TextWriter writer, string operation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(Usage(operation));
            return 0;
        }

        if (!TryParse(args, operation, out var options, out var error))
        {
            writer.WriteLine($"invalid-notification: {error}");
            writer.WriteLine(Usage(operation));
            return 1;
        }

        string routingRoot;
        try
        {
            routingRoot = Path.GetFullPath(options.RoutingRoot ?? context.RepoRoot);
            options = options with { RoutingRoot = routingRoot };
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            Emit(writer, options.Format, FailureResult(
                operation,
                options,
                SessionLayerMode.Default,
                "invalid-routing-root",
                $"Could not resolve --routing-root: {exception.Message}"));
            return 1;
        }

        string? reportRoot = null;
        if (string.Equals(operation, OperationReport, StringComparison.Ordinal)
            || string.Equals(operation, OperationCollect, StringComparison.Ordinal)
            || string.Equals(operation, OperationReconcile, StringComparison.Ordinal))
        {
            try
            {
                reportRoot = Path.GetFullPath(options.ReportRoot ?? context.RepoRoot);
                options = options with { ReportRoot = reportRoot };
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    SessionLayerMode.Default,
                    "invalid-report-root",
                    $"Could not resolve --report-root: {exception.Message}"));
                return 1;
            }
        }

        // Sender-local reports are deliberately consumed by an explicit
        // orchestration-side command. It has no transport or session-layer
        // dependency and is the only path allowed to reconcile host state
        // after a child seat has persisted its local report.
        if (string.Equals(operation, OperationReconcile, StringComparison.Ordinal))
        {
            return ExecuteReconcile(writer, options, routingRoot, reportRoot!);
        }

        TeamModeResolution teamMode;
        try
        {
            teamMode = TeamModeStore.Resolve(routingRoot, options.Domain!, options.Team);
        }
        catch (TeamModeResolutionException exception)
        {
            Emit(writer, options.Format, FailureResult(
                operation,
                options,
                SessionLayerMode.Default,
                TeamModeResolutionException.AmbiguousTeamScopeCode,
                exception.Message));
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Emit(writer, options.Format, FailureResult(
                operation,
                options,
                SessionLayerMode.Default,
                "team-mode-unreadable",
                exception.Message));
            return 1;
        }

        // G692 repair: a named worker delegation may omit --team when the
        // domain has exactly one team-scoped mode record. Carry the resolved
        // team into every subsequent store/transport call so a recorded
        // authoring-only team cannot silently become delivery. With no
        // recorded team context, fail before touching the outbox.
        if (string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            && options.Team is null)
        {
            if (teamMode.ResolvedTeam is null)
            {
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    SessionLayerMode.Default,
                    "team-required",
                    "notify delegate requires a team context; supply --team or record exactly one team-scoped mode for the domain."));
                return 1;
            }

            options = options with { Team = teamMode.ResolvedTeam };
        }

        // G691's named not-applicable NotifyCommand surface is supervision.
        // `supervise install` is routed to its own command above. Reporting,
        // escalation, status, and disposition remain usable on an
        // authoring-only team; their existing contracts are not delivery-seat
        // bootstrap. Publish/delegation/handoff policy belongs to later slices.
        if (teamMode.IsAuthoringOnly
            && string.Equals(operation, OperationSupervise, StringComparison.Ordinal))
        {
            const string notApplicable =
                "not-applicable-team-mode: authoring-only teams publish issues from the front door and have no notify worker lifecycle.";
            EmitSupervision(
                writer,
                new NotifySupervisorPass { Actions = [], Error = notApplicable },
                options.Domain!,
                options.Team!,
                options.IntervalSeconds ?? NotifySupervisor.DefaultIntervalSeconds,
                options.AutoRedispatch,
                options.Write,
                options.Format);
            return 1;
        }

        // G692: authoring-only has no worker seat to receive a delegation.
        // Refuse named worker roles before any outbox or transport path is
        // consulted; an authoring/design front door must not impersonate the
        // orchestration lane.
        if (teamMode.IsAuthoringOnly
            && string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            && TeamModeCapabilityMatrix.IsWorkerRole(options.ToRole))
        {
            Emit(writer, options.Format, FailureResult(
                operation,
                options,
                SessionLayerMode.Default,
                "not-applicable-team-mode",
                $"not-applicable-team-mode: authoring-only teams have no worker delegation lane; refusing notify delegate to worker role '{options.ToRole}'."));
            return 1;
        }

        if (string.Equals(operation, OperationDispose, StringComparison.Ordinal))
        {
            return ExecuteDispose(writer, options, routingRoot);
        }

        if (string.Equals(operation, OperationStatus, StringComparison.Ordinal))
        {
            return ExecuteStatus(context, writer, options, routingRoot);
        }

        if (string.Equals(operation, OperationSupervise, StringComparison.Ordinal))
        {
            return ExecuteSupervise(context, writer, options, routingRoot);
        }

        if (string.Equals(operation, OperationCollect, StringComparison.Ordinal))
        {
            return ExecuteCollect(writer, options, routingRoot, reportRoot!);
        }

        if (string.Equals(operation, OperationDelegate, StringComparison.Ordinal) && options.Write)
        {
            var delegateGuard = GuardDelegateOutboxLifecycle(writer, options, routingRoot);
            if (delegateGuard is not null) return delegateGuard.Value;
        }

        NotifyReportOutboxEntry? persistedReportOutbox = null;
        string? persistedOutboxPath = null;
        string? reportAdvisory = null;
        if (string.Equals(operation, OperationReport, StringComparison.Ordinal))
        {
            var pending = NotifyPendingDelegationStore.Find(routingRoot, options.Domain, options.Team, options.TaskId!);
            var openPending = pending.Resolved && pending.Record is { IsOpen: true };
            var disposedPending = pending.Resolved
                && pending.Record is { ReportArrived: false, Disposition: not null };
            var unmatched = !openPending && !disposedPending && pending.Error is null;
            if (!openPending && !disposedPending && !unmatched)
            {
                Emit(writer, options.Format, FailureResult(operation, options, SessionLayerMode.Default, "unknown-task-id",
                    $"Report task id '{options.TaskId}' does not match an open pending delegation. Known open task ids: {FormatKnownTaskIds(pending.KnownTaskIds)}."
                    + (pending.Error is null ? string.Empty : $" {pending.Error}")));
                return 1;
            }

            if (disposedPending)
            {
                reportAdvisory = BuildDisposedReportAdvisory(pending.Record!);
            }
            else if (unmatched)
            {
                reportAdvisory = BuildUnmatchedReportAdvisory(options.TaskId!, pending.KnownTaskIds);
            }
            persistedOutboxPath = NotifyReportOutboxStore.ResolvePath(reportRoot!, options.Domain!, options.Team!);
            if (options.Write)
            {
                persistedReportOutbox = new NotifyReportOutboxEntry
                {
                    Domain = options.Domain!, Team = options.Team!, TaskId = options.TaskId!,
                    ResultNonce = (openPending || disposedPending) ? pending.Record!.ResultNonce : null,
                    FromRole = options.FromRole!, ToRole = options.ToRole!, Status = options.Status!, Artifact = options.Artifact!,
                    Summary = NotifyEventWriter.NormalizeSummary(options.Summary!),
                    CreatedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(), DeliveryState = "prepared",
                };
                var write = NotifyReportOutboxStore.WriteNew(reportRoot!, persistedReportOutbox);
                persistedOutboxPath = write.Path;
                if (!write.Written)
                {
                    Emit(writer, options.Format, FailureResult(operation, options, SessionLayerMode.Default, "report-outbox-write-failed",
                        $"Could not persist report task '{options.TaskId}' before transport: {write.Error} No transport was attempted.",
                        outboxEntryPath: persistedOutboxPath));
                    return 1;
                }
                persistedReportOutbox = write.Entry ?? persistedReportOutbox;
            }
        }

        if (!string.Equals(operation, OperationEscalate, StringComparison.Ordinal))
        {
            var preflight = SessionLayerPreflight.Analyze(
                routingRoot,
                options.Domain!,
                options.Team!,
                options.ToRole!);
            var scope = preflight.Scopes.Single();
            var resolution = scope.Resolution ?? new SessionLayerModeResolution
            {
                Mode = scope.Mode ?? SessionLayerMode.Default,
                Source = SessionLayerModeSource.Default,
            };
            if (preflight.Ready is not true)
            {
                // G601 marker absence and G602 other-mode residue are
                // intentionally advisory. They must never hide the actionable
                // structural cause that made this delivery preflight fail
                // (for example, missing topology or an unsafe external reader).
                var primaryFinding = scope.Findings.FirstOrDefault(finding =>
                    !IsAdvisoryPreflightFinding(finding))
                    ?? scope.Findings.FirstOrDefault();
                var cause = primaryFinding?.Cause ?? "session-layer-not-ready";
                if (persistedReportOutbox is not null && options.Write)
                    NotifyReportOutboxStore.MarkUndelivered(reportRoot!, persistedReportOutbox, cause);
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    resolution.Mode,
                    cause,
                    (primaryFinding is null ? string.Empty : primaryFinding.Message + " ")
                    + scope.Summary + " " + preflight.Summary,
                    modeSource: scope.ModeSource,
                    preflight: preflight,
                    outboxEntryPath: persistedOutboxPath));
                return 1;
            }

            return ExecuteDelivery(writer, operation, options, resolution, preflight, reportAdvisory, persistedReportOutbox, reportRoot!);
        }

        SessionLayerModeResolution escalationResolution;
        try
        {
            escalationResolution = SessionLayerModeStore.Resolve(routingRoot, options.Domain!, options.Team);
        }
        catch (InvalidOperationException exception)
        {
            Emit(writer, options.Format, FailureResult(
                operation,
                options,
                SessionLayerMode.Default,
                "session-layer-mode-unreadable",
                exception.Message));
            return 1;
        }

        return ExecuteEscalation(writer, options, escalationResolution);
    }

    private static bool IsAdvisoryPreflightFinding(SessionLayerPreflightFinding finding) =>
        string.Equals(finding.Cause, "marker-not-generated", StringComparison.Ordinal)
        || string.Equals(finding.Cause, SessionLayerMigration.ResidueCause, StringComparison.Ordinal);

    private static int ExecuteCollect(
        TextWriter writer,
        NotifyOptions options,
        string routingRoot,
        string reportRoot)
    {
        if (options.Role is not null)
        {
            return ExecuteRoleCollect(writer, options, routingRoot);
        }

        var pending = NotifyPendingDelegationStore.Find(routingRoot, options.Domain, options.Team, options.TaskId!);
        var outbox = pending.Resolved && pending.Record is not null
            ? NotifyReportOutboxStore.Find(reportRoot, options.Domain!, options.Team!, options.TaskId!, pending.Record.ResultNonce)
            : new NotifyReportOutboxReadResult(true, NotifyReportOutboxStore.ResolvePath(reportRoot, options.Domain!, options.Team!), null, null);
        if (outbox.Entry is null || string.Equals(outbox.Entry.DeliveryState, "delivered", StringComparison.Ordinal))
        {
            var undelivered = NotifyReportOutboxStore.FindUndelivered(
                reportRoot,
                options.Domain!,
                options.Team!,
                options.TaskId!);
            if (undelivered.Entry is not null || !undelivered.Resolved) outbox = undelivered;
        }
        if (outbox.Entry is null && pending.Error is null)
        {
            outbox = NotifyReportOutboxStore.Find(reportRoot, options.Domain!, options.Team!, options.TaskId!);
        }
        if (!outbox.Resolved || outbox.Entry is null)
        {
            Emit(writer, options.Format, FailureResult(OperationCollect, options, SessionLayerMode.Default,
                "outbox-entry-unavailable", outbox.Error ?? $"No persisted outbox entry exists for task '{options.TaskId}'.",
                outboxEntryPath: outbox.Path));
            return 1;
        }

        if (string.Equals(outbox.Entry.DeliveryState, "delivered", StringComparison.Ordinal))
        {
            Emit(writer, options.Format, FailureResult(OperationCollect, options, SessionLayerMode.Default,
                "already-collected", $"Outbox entry for task '{options.TaskId}' is already delivered; collection is refused.",
                outboxEntryPath: outbox.Path));
            return 1;
        }

        if (!pending.Resolved && pending.Error is not null)
        {
            Emit(writer, options.Format, FailureResult(OperationCollect, options, SessionLayerMode.Default,
                "already-delivered", $"Task '{options.TaskId}' has no open matching pending record; collection is refused and never re-dispatches work.",
                outboxEntryPath: outbox.Path));
            return 1;
        }

        var collected = options with
        {
            FromRole = outbox.Entry.FromRole,
            ToRole = outbox.Entry.ToRole,
            Status = outbox.Entry.Status,
            Artifact = outbox.Entry.Artifact,
            Summary = outbox.Entry.Summary,
            ResultNonce = outbox.Entry.ResultNonce,
        };
        var preflight = SessionLayerPreflight.Analyze(routingRoot, collected.Domain!, collected.Team!, collected.ToRole!);
        var scope = preflight.Scopes.Single();
        var resolution = scope.Resolution ?? new SessionLayerModeResolution { Mode = scope.Mode ?? SessionLayerMode.Default, Source = SessionLayerModeSource.Default };
        if (preflight.Ready is not true)
        {
            var finding = scope.Findings.FirstOrDefault(finding => !IsAdvisoryPreflightFinding(finding)) ?? scope.Findings.FirstOrDefault();
            Emit(writer, collected.Format, FailureResult(OperationCollect, collected, resolution.Mode,
                finding?.Cause ?? "session-layer-not-ready", (finding?.Message ?? string.Empty) + " " + scope.Summary,
                modeSource: scope.ModeSource, preflight: preflight, outboxEntryPath: outbox.Path));
            return 1;
        }

        return ExecuteDelivery(
            writer,
            OperationCollect,
            collected,
            resolution,
            preflight,
            existingOutbox: outbox.Entry,
            reportRoot: reportRoot);
    }

    private static int ExecuteRoleCollect(
        TextWriter writer,
        NotifyOptions options,
        string routingRoot)
    {
        var topology = NotifyRoleTopologyStore.Resolve(routingRoot, options.Domain!, options.Team!);
        if (!topology.Resolved || topology.Topology is not { } teamTopology)
        {
            EmitRoleCollect(writer, options.Format, RoleCollectFailure(
                options,
                null,
                topology.Cause ?? "topology-unavailable",
                topology.Summary));
            return 1;
        }

        var roleResolution = NotifyRoleTopologyStore.ResolveRecordedRole(teamTopology, options.Role!);
        if (!roleResolution.Resolved || roleResolution.Record is not { } recordedRole)
        {
            EmitRoleCollect(writer, options.Format, RoleCollectFailure(
                options,
                null,
                roleResolution.Cause ?? "unknown-role",
                roleResolution.Summary));
            return 1;
        }

        if (!string.Equals(recordedRole.Resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal))
        {
            EmitRoleCollect(writer, options.Format, RoleCollectFailure(
                options,
                null,
                "role-reader-unavailable",
                $"Logical role '{options.Role}' is resident in herdr and has no external reader. "
                + "Role-scoped collect is available only for a role with a recorded external reader."));
            return 1;
        }

        if (!NotifyRoleTopologyStore.TryResolveReaderPath(
                routingRoot,
                recordedRole.Reader,
                out var recordedReaderPath,
                out var recordedReaderError))
        {
            EmitRoleCollect(writer, options.Format, RoleCollectFailure(
                options,
                null,
                "reader-unavailable",
                $"External logical role '{options.Role}' has no usable recorded reader: {recordedReaderError}"));
            return 1;
        }

        // The topology record is only the declared input. The effective path,
        // including scoped-versus-legacy compatibility, belongs to the same
        // resolver used by the writer and every other reader. Re-resolving on
        // each bounded wait pass also observes a canonical file appearing
        // after a previously missing reader.
        var readerPath = string.Empty;
        var stopwatch = options.Wait ? System.Diagnostics.Stopwatch.StartNew() : null;
        while (true)
        {
            if (!NotifyEventWriter.TryResolveReadPath(
                    routingRoot,
                    options.Domain!,
                    options.Team!,
                    recordedReaderPath,
                    out readerPath,
                    out var readerError))
            {
                EmitRoleCollect(writer, options.Format, RoleCollectFailure(
                    options,
                    null,
                    "reader-unavailable",
                    $"Could not resolve the effective reader for external logical role '{options.Role}': {readerError}"));
                return 1;
            }

            var read = ReadRoleCollectSnapshot(readerPath, recordedReaderPath, options.Since);
            if (!read.Succeeded)
            {
                EmitRoleCollect(writer, options.Format, RoleCollectFailure(
                    options,
                    readerPath,
                    read.Cause!,
                    read.Summary!));
                return 1;
            }

            if (read.Events.Count > 0)
            {
                EmitRoleCollect(writer, options.Format, RoleCollectSuccess(
                    options,
                    readerPath,
                    read,
                    outcome: "events",
                    cause: null,
                    timedOut: false,
                    summary: $"Collected {read.Events.Count} event(s) for external logical role '{options.Role}' from the effective reader."));
                return 0;
            }

            if (!options.Wait)
            {
                EmitRoleCollect(writer, options.Format, RoleCollectSuccess(
                    options,
                    readerPath,
                    read,
                    outcome: "no-events",
                    cause: "no-events",
                    timedOut: false,
                    summary: File.Exists(readerPath)
                        ? $"No events are available for external logical role '{options.Role}' after the supplied cursor; the read was non-blocking."
                        : $"The effective reader '{readerPath}' does not exist yet; treating it as no-events for external logical role '{options.Role}'."));
                return 0;
            }

            var elapsedMilliseconds = stopwatch!.ElapsedMilliseconds;
            var remainingMilliseconds = options.TimeoutMilliseconds!.Value - elapsedMilliseconds;
            if (remainingMilliseconds <= 0)
            {
                EmitRoleCollect(writer, options.Format, RoleCollectSuccess(
                    options,
                    readerPath,
                    read,
                    outcome: "no-new-events",
                    cause: "no-new-events",
                    timedOut: true,
                    summary: $"No new events arrived for external logical role '{options.Role}' before the bounded {options.TimeoutMilliseconds.Value}ms wait expired."));
                return 0;
            }

            // A synchronous bounded poll keeps the command single-process and
            // leaves no watcher, timer, or background task after return.
            Thread.Sleep((int)Math.Min(RoleCollectPollMilliseconds, remainingMilliseconds));
        }
    }

    private static NotifyRoleCollectResult RoleCollectSuccess(
        NotifyOptions options,
        string readerPath,
        NotifyRoleCollectReadResult read,
        string outcome,
        string? cause,
        bool timedOut,
        string summary) => new()
        {
            Operation = OperationCollect,
            RoutingRoot = options.RoutingRoot!,
            Domain = options.Domain!,
            Team = options.Team!,
            Role = options.Role!,
            ReaderPath = readerPath,
            CommandMode = options.Write ? "write" : "dry-run",
            Cursor = options.Since,
            NextCursor = read.NextCursor,
            Events = read.Events,
            Outcome = outcome,
            Wait = options.Wait,
            TimedOut = timedOut,
            Cause = cause,
            Summary = summary,
        };

    private static NotifyRoleCollectResult RoleCollectFailure(
        NotifyOptions options,
        string? readerPath,
        string cause,
        string summary) => new()
        {
            Operation = OperationCollect,
            RoutingRoot = options.RoutingRoot ?? string.Empty,
            Domain = options.Domain,
            Team = options.Team,
            Role = options.Role,
            ReaderPath = readerPath,
            CommandMode = options.Write ? "write" : "dry-run",
            Cursor = options.Since,
            NextCursor = string.Empty,
            Events = [],
            Outcome = "error",
            Wait = options.Wait,
            TimedOut = false,
            Cause = cause,
            Summary = summary,
        };

    private static void EmitRoleCollect(TextWriter writer, string format, NotifyRoleCollectResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# notify collect — {result.Role ?? "<role>"}");
        writer.WriteLine();
        writer.WriteLine($"- command mode: {result.CommandMode}");
        if (result.ReaderPath is not null)
        {
            writer.WriteLine($"- effective reader: `{result.ReaderPath}`");
        }
        if (result.Cursor is not null)
        {
            writer.WriteLine($"- cursor: `{result.Cursor}`");
        }
        writer.WriteLine($"- next cursor: `{result.NextCursor}`");
        writer.WriteLine($"- outcome: {result.Outcome}");
        writer.WriteLine($"- wait: {result.Wait.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- timed out: {result.TimedOut.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- events: {result.Events.Count}");
        if (result.Cause is not null)
        {
            writer.WriteLine($"- cause: {result.Cause}");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private static NotifyRoleCollectReadResult ReadRoleCollectSnapshot(
        string readerPath,
        string cursorReaderPath,
        string? suppliedCursor)
    {
        byte[] bytes;
        try
        {
            using var stream = new FileStream(
                readerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (FileNotFoundException)
        {
            bytes = [];
        }
        catch (DirectoryNotFoundException)
        {
            bytes = [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotifyRoleCollectReadResult.Failure(
                "reader-unreadable",
                $"The effective reader '{readerPath}' could not be read: {exception.Message}");
        }

        var completeEnd = CompleteLineEnd(bytes);
        var startOffset = 0;
        if (suppliedCursor is not null)
        {
            if (!TryDecodeRoleCollectCursor(suppliedCursor, out var cursor, out var cursorError)
                || cursor is null)
            {
                return NotifyRoleCollectReadResult.Failure(
                    "cursor-unhonourable",
                    $"The supplied --since cursor is unhonourable: {cursorError} refusing to reset or skip events.");
            }

            if (cursor.Version != 1
                || !string.Equals(cursor.ReaderDigest, Digest(cursorReaderPath), StringComparison.Ordinal)
                || cursor.ByteOffset < 0
                || cursor.ByteOffset > bytes.Length
                || cursor.ByteOffset > int.MaxValue
                || cursor.ByteOffset > 0 && bytes[cursor.ByteOffset - 1] != (byte)'\n'
                || cursor.CompleteLineCount != CountCompleteLines(bytes, (int)cursor.ByteOffset)
                || !string.Equals(
                    cursor.PrefixDigest,
                    Digest(bytes.AsSpan(0, (int)cursor.ByteOffset)),
                    StringComparison.Ordinal))
            {
                return NotifyRoleCollectReadResult.Failure(
                    "cursor-unhonourable",
                    "The supplied --since cursor no longer identifies an intact complete-line position in the same effective reader; refusing to reset or skip events.");
            }

            startOffset = (int)cursor.ByteOffset;
        }

        var events = new List<NotifyDesignEvent>();
        var position = startOffset;
        while (position < completeEnd)
        {
            var lineStart = position;
            while (position < completeEnd && bytes[position] != (byte)'\n')
            {
                position++;
            }

            var lineLength = position - lineStart;
            if (lineLength > 0 && bytes[lineStart + lineLength - 1] == (byte)'\r')
            {
                lineLength--;
            }

            if (lineLength == 0)
            {
                return NotifyRoleCollectReadResult.Failure(
                    "reader-invalid",
                    $"The effective reader '{readerPath}' contains an empty JSONL record; refusing to guess its cursor position.");
            }

            try
            {
                var designEvent = JsonSerializer.Deserialize<NotifyDesignEvent>(
                    bytes.AsSpan(lineStart, lineLength));
                if (designEvent is null)
                {
                    throw new JsonException("record deserialized to null");
                }

                events.Add(designEvent);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return NotifyRoleCollectReadResult.Failure(
                    "reader-invalid",
                    $"The effective reader '{readerPath}' contains an invalid JSONL record at byte {lineStart}: {exception.Message}");
            }

            position++;
        }

        return new NotifyRoleCollectReadResult
        {
            Succeeded = true,
            Events = events,
            NextCursor = EncodeRoleCollectCursor(cursorReaderPath, bytes, completeEnd),
        };
    }

    private static int CompleteLineEnd(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes[^1] == (byte)'\n')
        {
            return bytes.Length;
        }

        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        return lastNewline < 0 ? 0 : lastNewline + 1;
    }

    private static int CountCompleteLines(byte[] bytes, int end)
    {
        var count = 0;
        for (var index = 0; index < end; index++)
        {
            if (bytes[index] == (byte)'\n')
            {
                count++;
            }
        }

        return count;
    }

    private static string EncodeRoleCollectCursor(string readerPath, byte[] bytes, int byteOffset)
    {
        var cursor = new NotifyRoleCollectCursor
        {
            Version = 1,
            ReaderDigest = Digest(readerPath),
            ByteOffset = byteOffset,
            CompleteLineCount = CountCompleteLines(bytes, byteOffset),
            PrefixDigest = Digest(bytes.AsSpan(0, byteOffset)),
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(cursor);
        return Convert.ToBase64String(json)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryDecodeRoleCollectCursor(
        string value,
        out NotifyRoleCollectCursor? cursor,
        out string error)
    {
        cursor = null;
        error = "the cursor is not a valid opaque cursor token.";
        try
        {
            var encoded = value.Replace('-', '+')
                .Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            cursor = JsonSerializer.Deserialize<NotifyRoleCollectCursor>(json);
            if (cursor is null)
            {
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string Digest(string value) => Digest(Encoding.UTF8.GetBytes(value));

    private static string Digest(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));

    private static int ExecuteReconcile(
        TextWriter writer,
        NotifyOptions options,
        string routingRoot,
        string reportRoot)
    {
        var pending = NotifyPendingDelegationStore.Find(
            routingRoot,
            options.Domain,
            options.Team,
            options.TaskId!);
        if (!pending.Resolved || pending.Record is null)
        {
            EmitReconciliation(writer, options.Format, new NotifyReconciliationResult
            {
                RoutingRoot = routingRoot,
                ReportRoot = reportRoot,
                Domain = options.Domain!,
                Team = options.Team!,
                TaskId = options.TaskId!,
                CommandMode = options.Write ? "write" : "dry-run",
                ReportOutboxPath = NotifyReportOutboxStore.ResolvePath(reportRoot, options.Domain!, options.Team!),
                PendingRecordPath = pending.Path,
                Cause = pending.Error is null ? "unknown-task-id" : "pending-store-unreadable",
                Summary = pending.Error
                    ?? $"Task '{options.TaskId}' has no host-owned pending delegation record to reconcile."
                    + $" Known open task ids: {FormatKnownTaskIds(pending.KnownTaskIds)}",
            });
            return 1;
        }

        var pendingRecord = pending.Record;
        var outbox = NotifyReportOutboxStore.Find(
            reportRoot,
            options.Domain!,
            options.Team!,
            options.TaskId!,
            pendingRecord.ResultNonce);
        if (!outbox.Resolved)
        {
            EmitReconciliation(writer, options.Format, new NotifyReconciliationResult
            {
                RoutingRoot = routingRoot,
                ReportRoot = reportRoot,
                Domain = options.Domain!,
                Team = options.Team!,
                TaskId = options.TaskId!,
                CommandMode = options.Write ? "write" : "dry-run",
                ReportOutboxPath = outbox.Path,
                PendingRecordPath = pending.Path,
                Cause = "report-outbox-unreadable",
                Summary = outbox.Error ?? $"Sender-local report outbox '{outbox.Path}' could not be read.",
            });
            return 1;
        }

        var report = outbox.Entry;
        if (report is null)
        {
            EmitReconciliation(writer, options.Format, new NotifyReconciliationResult
            {
                RoutingRoot = routingRoot,
                ReportRoot = reportRoot,
                Domain = options.Domain!,
                Team = options.Team!,
                TaskId = options.TaskId!,
                CommandMode = options.Write ? "write" : "dry-run",
                ReportOutboxPath = outbox.Path,
                PendingRecordPath = pending.Path,
                Cause = "sender-local-report-not-found",
                Summary = $"No sender-local report for task '{options.TaskId}' and result nonce '{pendingRecord.ResultNonce ?? "<none>"}' was found at '{outbox.Path}'.",
            });
            return 1;
        }

        if (!string.Equals(report.DeliveryState, "delivered", StringComparison.Ordinal))
        {
            var recoveryCommand = NotifyReportOutboxStore.BuildCollectCommand(
                routingRoot,
                report,
                reportRoot);
            EmitReconciliation(writer, options.Format, new NotifyReconciliationResult
            {
                RoutingRoot = routingRoot,
                ReportRoot = reportRoot,
                Domain = options.Domain!,
                Team = options.Team!,
                TaskId = options.TaskId!,
                CommandMode = options.Write ? "write" : "dry-run",
                ReportOutboxPath = outbox.Path,
                ReportDeliveryState = report.DeliveryState,
                PendingRecordPath = pending.Path,
                Cause = "sender-local-report-not-delivered",
                RecoveryCommand = recoveryCommand,
                Summary = $"Sender-local report for task '{options.TaskId}' is '{report.DeliveryState}', not delivered; "
                    + $"recover the local handoff with '{recoveryCommand}'.",
            });
            return 1;
        }

        var reportedAt = (report.DeliveredAt ?? report.LastAttemptAt ?? UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var pendingReconciliation = NotifyPendingDelegationStore.ReconcileReport(
            routingRoot,
            pendingRecord,
            report.Status,
            report.Artifact,
            report.Summary,
            reportedAt,
            options.Write);
        if (pendingReconciliation.Error is not null)
        {
            EmitReconciliation(writer, options.Format, new NotifyReconciliationResult
            {
                RoutingRoot = routingRoot,
                ReportRoot = reportRoot,
                Domain = options.Domain!,
                Team = options.Team!,
                TaskId = options.TaskId!,
                CommandMode = options.Write ? "write" : "dry-run",
                ReportOutboxPath = outbox.Path,
                ReportDeliveryState = report.DeliveryState,
                PendingRecordPath = pendingReconciliation.Path,
                PendingAlreadyConverged = pendingReconciliation.AlreadyConverged,
                Cause = "pending-reconciliation-failed",
                Summary = $"The delivered sender-local report was read, but host pending reconciliation failed: {pendingReconciliation.Error}",
            });
            return 1;
        }

        var chain = ContinuationChainStore.RecordReportReceived(
            routingRoot,
            options.Domain!,
            options.Team!,
            options.TaskId!,
            report.ResultNonce,
            report.Status,
            report.Artifact,
            report.Summary,
            reportedAt,
            options.Write,
            source: "notify-reconcile");
        if (options.Write && !chain.Applied && !chain.AlreadyConverged)
        {
            EmitReconciliation(writer, options.Format, new NotifyReconciliationResult
            {
                RoutingRoot = routingRoot,
                ReportRoot = reportRoot,
                Domain = options.Domain!,
                Team = options.Team!,
                TaskId = options.TaskId!,
                CommandMode = options.Write ? "write" : "dry-run",
                ReportOutboxPath = outbox.Path,
                ReportDeliveryState = report.DeliveryState,
                PendingRecordPath = pendingReconciliation.Path,
                PendingReconciled = pendingReconciliation.Applied,
                PendingAlreadyConverged = pendingReconciliation.AlreadyConverged,
                ContinuationChainPath = chain.Path,
                ContinuationChain = chain.Record ?? chain.Preview,
                Cause = "continuation-chain-reconciliation-failed",
                Summary = $"Host pending state was reconciled, but continuation link '{ContinuationChainStore.ReportReceived}' could not be reconciled: {chain.Error}",
            });
            return 1;
        }

        var pendingReconciled = pendingReconciliation.Applied;
        var pendingAlreadyConverged = pendingReconciliation.AlreadyConverged;
        var chainReconciled = chain.Applied;
        var chainAlreadyConverged = chain.AlreadyConverged;
        var changed = pendingReconciled || chainReconciled;
        var alreadyConverged = pendingAlreadyConverged && chainAlreadyConverged;
        var summary = options.Write
            ? changed
                ? $"Orchestration reconciled delivered sender-local report '{report.EntryId ?? report.TaskId}' into host pending state and continuation link exactly once; retries are idempotent."
                : $"Delivered sender-local report '{report.EntryId ?? report.TaskId}' was already reconciled into host pending state and continuation link; no duplicate state was written."
            : alreadyConverged
                ? $"Dry-run verified that delivered sender-local report '{report.EntryId ?? report.TaskId}' is already reconciled; no store would change."
                : $"Dry-run verified that delivered sender-local report '{report.EntryId ?? report.TaskId}' would reconcile host pending state and continuation link without writing either store.";
        EmitReconciliation(writer, options.Format, new NotifyReconciliationResult
        {
            RoutingRoot = routingRoot,
            ReportRoot = reportRoot,
            Domain = options.Domain!,
            Team = options.Team!,
            TaskId = options.TaskId!,
            CommandMode = options.Write ? "write" : "dry-run",
            ReportOutboxPath = outbox.Path,
            ReportDeliveryState = report.DeliveryState,
            PendingRecordPath = pendingReconciliation.Path,
            PendingReconciled = pendingReconciled,
            PendingAlreadyConverged = pendingAlreadyConverged,
            ContinuationChainPath = chain.Path,
            ContinuationReconciled = chainReconciled,
            ContinuationAlreadyConverged = chainAlreadyConverged,
            ContinuationChain = chain.Record ?? chain.Preview,
            Reconciled = options.Write && changed,
            AlreadyConverged = options.Write ? !changed : alreadyConverged,
            WouldReconcile = !options.Write && !alreadyConverged,
            Summary = summary,
        });
        return 0;
    }

    private static int? GuardDelegateOutboxLifecycle(TextWriter writer, NotifyOptions options, string routingRoot)
    {
        var undelivered = NotifyReportOutboxStore.FindUndelivered(
            routingRoot,
            options.Domain!,
            options.Team!,
            options.TaskId!);
        if (!undelivered.Resolved)
        {
            Emit(writer, options.Format, FailureResult(
                OperationDelegate,
                options,
                SessionLayerMode.Default,
                "report-outbox-unavailable",
                undelivered.Error ?? $"Could not read the report outbox for task '{options.TaskId}'. No work was started.",
                outboxEntryPath: undelivered.Path));
            return 1;
        }

        if (undelivered.Entry is not null)
        {
            Emit(writer, options.Format, FailureResult(
                OperationDelegate,
                options,
                SessionLayerMode.Default,
                "undelivered-report-outbox",
                $"Task '{options.TaskId}' already has an undelivered report outbox entry. Recover it with "
                + $"'{NotifyReportOutboxStore.BuildCollectCommand(routingRoot, undelivered.Entry)}'; do not re-delegate the task. No work was started.",
                outboxEntryPath: undelivered.Path));
            return 1;
        }

        var existingGeneration = NotifyReportOutboxStore.Find(
            routingRoot,
            options.Domain!,
            options.Team!,
            options.TaskId!,
            options.ResultNonce);
        if (!existingGeneration.Resolved)
        {
            Emit(writer, options.Format, FailureResult(
                OperationDelegate,
                options,
                SessionLayerMode.Default,
                "report-outbox-unavailable",
                existingGeneration.Error ?? $"Could not read the report outbox for task '{options.TaskId}'. No work was started.",
                outboxEntryPath: existingGeneration.Path));
            return 1;
        }

        var pending = NotifyPendingDelegationStore.Find(routingRoot, options.Domain, options.Team, options.TaskId!);
        var sameOpenGeneration = pending.Resolved
            && pending.Record is { IsOpen: true }
            && string.Equals(pending.Record.ResultNonce, options.ResultNonce, StringComparison.Ordinal);
        if (existingGeneration.Entry is not null
            || (pending.Record is not null
                && !sameOpenGeneration
                && string.Equals(pending.Record.ResultNonce, options.ResultNonce, StringComparison.Ordinal)))
        {
            Emit(writer, options.Format, FailureResult(
                OperationDelegate,
                options,
                SessionLayerMode.Default,
                "result-nonce-already-used",
                $"Task '{options.TaskId}' has already used result nonce '{options.ResultNonce}'. Supply a fresh --result-nonce "
                + "or a new --task-id before delegating; no work was started.",
                outboxEntryPath: existingGeneration.Path));
            return 1;
        }

        return null;
    }

    private static int ExecuteDispose(TextWriter writer, NotifyOptions options, string routingRoot)
    {
        var lookup = NotifyPendingDelegationStore.Find(
            routingRoot,
            options.Domain,
            options.Team,
            options.TaskId!);
        if (!lookup.Resolved || lookup.Record is null)
        {
            var cause = lookup.Error is null ? "unknown-task-id" : "pending-store-unreadable";
            EmitDisposition(writer, options.Format, NotifyDispositionResult.Failure(
                routingRoot,
                options,
                cause,
                lookup.Error is null
                    ? $"Task id '{options.TaskId}' is unknown; it cannot be disposed. Known open task ids: {FormatKnownTaskIds(lookup.KnownTaskIds)}."
                    : lookup.Error,
                lookup.Path));
            return 1;
        }

        var record = lookup.Record;
        if (!record.IsOpen)
        {
            var settledKind = record.SettlementBasis == "disposition"
                ? "disposition-settled"
                : "report-settled";
            EmitDisposition(writer, options.Format, NotifyDispositionResult.Failure(
                routingRoot,
                options,
                "already-settled",
                $"Task id '{options.TaskId}' is already settled ({settledKind}); dispose applies only to an open pending delegation.",
                lookup.Path,
                record,
                settledKind));
            return 1;
        }

        var disposition = BuildDisposition(options);
        if (!options.Write)
        {
            EmitDisposition(writer, options.Format, NotifyDispositionResult.Success(
                routingRoot,
                options,
                disposition,
                written: false,
                path: lookup.Path ?? string.Empty,
                record: record));
            return 0;
        }

        var write = NotifyPendingDelegationStore.WriteDisposition(routingRoot, record, disposition);
        if (!write.Written)
        {
            EmitDisposition(writer, options.Format, NotifyDispositionResult.Failure(
                routingRoot,
                options,
                "pending-disposition-write-failed",
                $"Could not record disposition for open task id '{options.TaskId}': {write.Error}",
                write.Path,
                record));
            return 1;
        }

        EmitDisposition(writer, options.Format, NotifyDispositionResult.Success(
            routingRoot,
            options,
            disposition,
            written: true,
            path: write.Path,
            record: record));
        return 0;
    }

    private static NotifyPendingDisposition BuildDisposition(NotifyOptions options) => new()
    {
        Kind = options.DispositionKind!,
        Actor = options.Actor!,
        Timestamp = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
        Reason = NotifyEventWriter.NormalizeSummary(options.Reason!),
        SupersedingTaskId = options.SupersedingTaskId,
        AppliedOutcomeEvidence = options.AppliedOutcomeEvidence,
    };

    private static int ExecuteStatus(CliContext context, TextWriter writer, NotifyOptions options, string routingRoot)
    {
        var lookup = NotifyPendingDelegationStore.Find(
            routingRoot,
            options.Domain,
            options.Team,
            options.TaskId!);
        if (!lookup.Resolved || lookup.Record is null)
        {
            EmitStatus(writer, options.Format, NotifyStatusResult.Failure(
                routingRoot,
                options.Domain,
                options.Team,
                options.TaskId!,
                "unknown-task-id",
                $"No open or settled pending delegation record was found for supplied task id '{options.TaskId}'. "
                + $"Known open task ids: {FormatKnownTaskIds(lookup.KnownTaskIds)}"
                + (lookup.Error is null ? string.Empty : $" {lookup.Error}")));
            return 1;
        }

        var record = lookup.Record;
        if (record.Disposition is { } disposition)
        {
            EmitStatus(writer, options.Format, new NotifyStatusResult
            {
                Operation = NotifyCommand.OperationStatus,
                RoutingRoot = routingRoot,
                Domain = record.Domain,
                Team = record.Team,
                TaskId = record.TaskId,
                RecipientRole = record.RecipientRole,
                RecipientIdentity = record.RecipientIdentity,
                ExpectedArtifact = record.ExpectedArtifact,
                DispatchedAt = record.DispatchedAt,
                ReportArrived = record.ReportArrived,
                ReportStatus = record.ReportStatus,
                ReportArtifact = record.ReportArtifact,
                ReportSummary = record.ReportSummary,
                SettlementBasis = "disposition",
                Disposition = disposition,
                LateReportDisagreement = record.ReportArrived
                    ? BuildLateReportDisagreement(record)
                    : null,
                Verdict = "settled",
                Summary = record.ReportArrived
                    ? $"Task '{record.TaskId}' is settled with disposition '{disposition.Kind}', and a late report arrived; disagreement: {BuildLateReportDisagreement(record)}"
                    : $"Task '{record.TaskId}' is settled with disposition '{disposition.Kind}' recorded by '{disposition.Actor}'. No report is owed; the disposition is administrative settlement, not message refusal.",
            });
            return 0;
        }

        var transportMode = record.TransportMode;
        if (string.IsNullOrWhiteSpace(transportMode))
        {
            try
            {
                transportMode = SessionLayerModeStore.Resolve(routingRoot, record.Domain, record.Team).Mode;
            }
            catch (InvalidOperationException exception)
            {
                EmitStatus(writer, options.Format, NotifyStatusResult.Failure(
                    routingRoot,
                    record.Domain,
                    record.Team,
                    record.TaskId,
                    "session-layer-mode-unreadable",
                    exception.Message,
                    record));
                return 1;
            }
        }

        var runner = ProcessRunnerFactory?.Invoke() ?? new NotifyProcessRunner();
        var liveness = NotifyPendingLiveness.Probe(
            routingRoot,
            record,
            transportMode!,
            runner,
            options.HerdrExecutable ?? HerdrExecutableFactory?.Invoke() ?? NotifyTransportPaths.ResolveHerdrExecutable(),
            AgmsgScriptsDirectoryFactory?.Invoke() ?? NotifyTransportPaths.ResolveAgmsgScriptsDirectory(),
            options.BashExecutable ?? BashExecutableFactory?.Invoke() ?? NotifyTransportPaths.ResolveBashExecutable());
        if (!liveness.Resolved || liveness.Running is null)
        {
            EmitStatus(writer, options.Format, NotifyStatusResult.Failure(
                routingRoot,
                record.Domain,
                record.Team,
                record.TaskId,
                liveness.Cause ?? "liveness-unavailable",
                liveness.Summary,
                record,
                liveness.Source,
                liveness.Running));
            return 1;
        }

        var verdict = record.ReportArrived
            ? "settled"
            : liveness.State == NotifyPendingLivenessResult.RegistrationLostProcessPresent
                ? NotifyPendingLivenessResult.RegistrationLostProcessPresent
                : liveness.Running.Value
                    ? "live"
                    : "lost";
        var activityKey = record.WorkspaceId is not null && record.PaneId is not null
            ? $"activity:{record.WorkspaceId}:{record.PaneId}"
            : $"activity:{record.RecipientIdentity}";
        var supervision = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(),
            record.Domain,
            record.Team);
        long? priorStateChangeSequence = null;
        if (supervision.LastCycle?.LastObservedStateChangeSequences.TryGetValue(activityKey, out var observedSequence) == true)
        {
            priorStateChangeSequence = observedSequence;
        }

        var hasPriorActivityObservation = liveness.StateChangeSequence.HasValue && priorStateChangeSequence.HasValue;
        var activityAdvancing = liveness.StateChangeSequence is { } sequence
            && priorStateChangeSequence is { } priorSequence
            && sequence > priorSequence;
        var stateChangedAfterDispatch = liveness.LastStateChangeAt is { } lastStateChangeAt
            && lastStateChangeAt > record.DispatchedAt;
        var activityEvidence = activityAdvancing
            ? "observed-sequence-advance"
            : !hasPriorActivityObservation && stateChangedAfterDispatch
                ? "state-change-after-dispatch"
                : hasPriorActivityObservation
                    ? "observed-sequence-unchanged"
                    : "baseline-missing";
        var hasHerdrActivity = liveness.StateChangeSequence.HasValue
            || liveness.LastStateChangeAt.HasValue
            || liveness.AgentStatus is not null;
        var activityVerdict = liveness.Running == true && hasHerdrActivity
            ? !hasPriorActivityObservation
                ? string.Equals(liveness.AgentStatus, "working", StringComparison.Ordinal) && stateChangedAfterDispatch
                    ? "working"
                    : "activity-unknown"
                : string.Equals(liveness.AgentStatus, "working", StringComparison.Ordinal) && activityAdvancing
                    ? "working"
                    : "live-idle"
            : null;
        EmitStatus(writer, options.Format, new NotifyStatusResult
        {
            Operation = NotifyCommand.OperationStatus,
            RoutingRoot = routingRoot,
            Domain = record.Domain,
            Team = record.Team,
            TaskId = record.TaskId,
            RecipientRole = record.RecipientRole,
            RecipientIdentity = record.RecipientIdentity,
            ExpectedArtifact = record.ExpectedArtifact,
            DispatchedAt = record.DispatchedAt,
            RecipientRunning = liveness.Running,
            LivenessState = liveness.State,
            ProcessPresent = liveness.ProcessPresent,
            AgentSessionPresent = liveness.AgentSessionPresent,
            ResendPermitted = liveness.ResendPermitted,
            LivenessSource = liveness.Source,
            DeliveryBasis = liveness.DeliveryBasis,
            AgentStatus = liveness.AgentStatus,
            StateChangeSequence = liveness.StateChangeSequence,
            LastStateChangeAt = liveness.LastStateChangeAt,
            ActivityVerdict = activityVerdict,
            ActivityInputs = liveness.Running == true
                ? $"agent_status={liveness.AgentStatus ?? "<unknown>"}; state_change_seq={liveness.StateChangeSequence?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}; last_state_change_at={liveness.LastStateChangeAt?.ToString("O") ?? "<unknown>"}; activity_evidence={activityEvidence}; advancing_since_last_observation={activityAdvancing.ToString().ToLowerInvariant()}"
                : null,
            ReportArrived = record.ReportArrived,
            ReportStatus = record.ReportStatus,
            ReportArtifact = record.ReportArtifact,
            ReportSummary = record.ReportSummary,
            SettlementBasis = record.SettlementBasis,
            Verdict = verdict,
            Cause = null,
            Summary = liveness.Summary + (record.ReportArrived
                ? $" Matching report status '{record.ReportStatus}' arrived."
                : $" No matching report has arrived; verdict is '{verdict}'."),
        });
        return 0;
    }

    private static int ExecuteSupervise(
        CliContext context,
        TextWriter writer,
        NotifyOptions options,
        string routingRoot)
    {
        var runner = ProcessRunnerFactory?.Invoke() ?? new NotifyProcessRunner();
        var supervisor = new NotifyMeasuredSupervisor(
            context,
            routingRoot,
            options.Domain!,
            options.Team!,
            options.Repo,
            options.OwnerRole ?? "orchestration",
            options.IntervalSeconds ?? NotifySupervisor.DefaultIntervalSeconds,
            options.DetectionBoundSeconds,
            options.StaleMinutes ?? AutomationHeartbeatCommand.DefaultStaleMinutes,
            options.ClaimedSilentMinutes ?? AutomationStalledWorkCommand.DefaultClaimedSilentMinutes,
            options.BacklogIdleMinutes ?? AutomationStalledWorkCommand.DefaultBacklogIdleMinutes,
            options.RepairSilentMinutes ?? AutomationStalledWorkCommand.DefaultRepairSilentMinutes,
            options.AutoRedispatch,
            options.Write,
            options.Format,
            runner,
            options.HerdrExecutable ?? HerdrExecutableFactory?.Invoke() ?? NotifyTransportPaths.ResolveHerdrExecutable(),
            AgmsgScriptsDirectoryFactory?.Invoke() ?? NotifyTransportPaths.ResolveAgmsgScriptsDirectory(),
            options.EventMode,
            options.PreApprovalAcceptRules,
            options.PreApprovalEscalateRules,
            options.BashExecutable ?? BashExecutableFactory?.Invoke() ?? NotifyTransportPaths.ResolveBashExecutable(),
            scopedPolicies: options.ScopedPolicies,
            repeatBackoffSeconds: options.RepeatBackoffSeconds,
            debounceConsecutiveObservations: options.DebounceConsecutiveObservations,
            delegationExecutionWindowSeconds: options.DelegationExecutionWindowSeconds);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = null;
        if (!Console.IsOutputRedirected)
        {
            cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
        }

        try
        {
            return supervisor.RunLoop(writer, cancellation.Token, options.Once);
        }
        finally
        {
            if (cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    private static string FormatKnownTaskIds(IReadOnlyList<string> knownTaskIds) =>
        knownTaskIds.Count == 0 ? "<none>" : string.Join(", ", knownTaskIds);

    private static string BuildUnmatchedReportAdvisory(
        string taskId,
        IReadOnlyList<string> knownTaskIds) =>
        $"No open pending delegation matched supplied report task id '{taskId}'; the report was delivered "
        + $"without creating or resolving a pending record. Known open task ids: {FormatKnownTaskIds(knownTaskIds)}.";

    private static string BuildDisposedReportAdvisory(NotifyPendingDelegation record) =>
        $"Task '{record.TaskId}' was already settled with disposition '{record.Disposition!.Kind}' "
        + $"by '{record.Disposition.Actor}' because: {record.Disposition.Reason}. "
        + "The report was still delivered under the established carriage rule; disagreement: a late report arrived after the report expectation was disposed. "
        + "The report does not erase the disposition.";

    private static string BuildLateReportDisagreement(NotifyPendingDelegation record) =>
        $"late report status '{record.ReportStatus ?? "<unknown>"}' arrived after disposition '{record.Disposition!.Kind}' "
        + $"by '{record.Disposition.Actor}'; disposition remains the settlement basis";

    internal static void EmitSupervision(
        TextWriter writer,
        NotifySupervisorPass pass,
        string domain,
        string team,
        int intervalSeconds,
        bool autoRedispatch,
        bool write,
        string format = FormatJson,
        bool eventMode = false)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                operation = OperationSupervise,
                domain,
                team,
                interval_seconds = intervalSeconds,
                event_mode = eventMode,
                event_mode_evidence = eventMode ? "herdr-0.8.0-macos-measured; other-versions-and-platforms-unverified" : null,
                auto_redispatch = autoRedispatch,
                command_mode = write ? "write" : "dry-run",
                silent = pass.Silent,
                error = pass.Error,
                warnings = pass.Warnings,
                bound = pass.Bound,
                emission_policy = pass.EmissionPolicy,
                pre_approval_policy = pass.PreApprovalPolicy,
                liveness = pass.Liveness,
                actions = pass.Actions,
                findings = pass.Findings,
                recovery_records = pass.RecoveryRecords,
            }, JsonOptions));
            return;
        }

        writer.WriteLine($"# notify supervise — {domain}/{team}");
        writer.WriteLine();
        writer.WriteLine($"- interval: {intervalSeconds}s");
        writer.WriteLine($"- event mode: {eventMode.ToString().ToLowerInvariant()}");
        if (eventMode)
        {
            writer.WriteLine("- event mode evidence: herdr 0.8.0/macOS measured; other versions and platforms unverified");
        }
        writer.WriteLine($"- command mode: {(write ? "write" : "dry-run")}");
        writer.WriteLine($"- auto-redispatch: {autoRedispatch.ToString().ToLowerInvariant()}");
        if (pass.EmissionPolicy is { } emissionPolicy)
        {
            writer.WriteLine($"- emission policy: full cadence={emissionPolicy.FullCadenceSeconds}s; repeat backoff={emissionPolicy.RepeatBackoffSeconds}s; status debounce={emissionPolicy.DebounceConsecutiveObservations} consecutive observations; recorded at {emissionPolicy.RecordedAt:O}");
        }
        if (pass.Bound is { } bound)
        {
            writer.WriteLine($"- detection bound: {bound.BoundSeconds?.ToString(CultureInfo.InvariantCulture) ?? "<unrecorded>"}s ({bound.Status}); actual interval: {bound.ActualIntervalSeconds?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}s; met: {bound.BoundMet?.ToString().ToLowerInvariant() ?? "<unknown>"}");
        }
        if (pass.PreApprovalPolicy is { } policy)
        {
            writer.WriteLine($"- pre-approval policy: {policy.Status}; default={policy.DefaultDecision}; recorded={policy.Recorded.ToString().ToLowerInvariant()}; applicable={policy.Applicable.ToString().ToLowerInvariant()}; applicability={policy.ApplicabilityStatus}; path={policy.Path}");
            writer.WriteLine($"  - {policy.Summary}");
            if (policy.InapplicableAgentKinds.Count > 0)
            {
                writer.WriteLine($"  - inapplicable agent kinds: {string.Join(", ", policy.InapplicableAgentKinds)}");
            }
            if (policy.InapplicabilityReason is not null)
            {
                writer.WriteLine($"  - inapplicability reason: {policy.InapplicabilityReason}");
            }
        }
        if (pass.Liveness is { } liveness)
        {
            writer.WriteLine($"- supervisor liveness: running={liveness.Running.ToString().ToLowerInvariant()}; absent since last cycle={liveness.AbsentSinceLastCycle.ToString().ToLowerInvariant()}; gap={liveness.GapSeconds?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}s");
        }
        if (pass.Error is not null)
        {
            writer.WriteLine($"- error: {pass.Error}");
        }
        foreach (var warning in pass.Warnings)
        {
            writer.WriteLine($"- warning: {warning}");
        }
        foreach (var action in pass.Actions)
        {
            writer.WriteLine($"- {action.TaskId}: {action.Outcome} — {action.Summary}");
        }
        foreach (var finding in pass.Findings)
        {
            writer.WriteLine($"- finding {finding.Key}: {finding.Kind} — {finding.Summary} (wake delivered={finding.WakeDelivered.ToString().ToLowerInvariant()})");
            if (finding.Prompt is { } prompt)
            {
                writer.WriteLine($"  - observed prompt: agent_kind={prompt.AgentKind}; pane={prompt.Pane}; prompt_class={prompt.PromptClass}; decision={prompt.Decision}; rule={prompt.Rule}; exact_answer_scope={prompt.ExactAnswerScope ?? "<none>"}; observed_text={JsonSerializer.Serialize(prompt.ObservedText)}");
            }
        }
        foreach (var record in pass.RecoveryRecords.Where(record => record.ClearedAt is null && record.Parked))
        {
            writer.WriteLine($"- parked {record.Key}: first_seen={record.FirstSeenAt?.ToString("O") ?? "<unknown>"}; last_seen={record.LastSeenAt?.ToString("O") ?? "<unknown>"}; repeat_count={record.RepeatCount}; next emission cadence={record.EmissionCadenceSeconds?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}s");
        }
    }

    private static void EmitStatus(TextWriter writer, string format, NotifyStatusResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# notify status — {result.TaskId}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain ?? "<unknown>"}");
        writer.WriteLine($"- team: {result.Team ?? "<unknown>"}");
        writer.WriteLine($"- dispatched at: {result.DispatchedAt?.ToString("O") ?? "<unknown>"}");
        writer.WriteLine($"- recipient: {result.RecipientRole ?? "<unknown>"} ({result.RecipientIdentity ?? "<unknown>"})");
        writer.WriteLine($"- recipient running: {result.RecipientRunning?.ToString().ToLowerInvariant() ?? "<unknown>"}");
        writer.WriteLine($"- liveness state: {result.LivenessState ?? "<unknown>"}");
        writer.WriteLine($"- process present: {result.ProcessPresent?.ToString().ToLowerInvariant() ?? "<unknown>"}");
        writer.WriteLine($"- agent session present: {result.AgentSessionPresent?.ToString().ToLowerInvariant() ?? "<unknown>"}");
        writer.WriteLine($"- resend permitted: {result.ResendPermitted?.ToString().ToLowerInvariant() ?? "<unknown>"}");
        writer.WriteLine($"- liveness source: {result.LivenessSource ?? "<unknown>"}");
        writer.WriteLine($"- delivery basis: {result.DeliveryBasis ?? "<unknown>"}");
        writer.WriteLine($"- agent status: {result.AgentStatus ?? "<unknown>"}");
        writer.WriteLine($"- state change sequence: {result.StateChangeSequence?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}");
        writer.WriteLine($"- last state change at: {result.LastStateChangeAt?.ToString("O") ?? "<unknown>"}");
        writer.WriteLine($"- activity verdict: {result.ActivityVerdict ?? "<unknown>"}");
        writer.WriteLine($"- activity inputs: {result.ActivityInputs ?? "<unknown>"}");
        writer.WriteLine($"- report arrived: {result.ReportArrived?.ToString().ToLowerInvariant() ?? "<unknown>"}");
        writer.WriteLine($"- settlement basis: {result.SettlementBasis ?? "<unknown>"}");
        if (result.Disposition is { } disposition)
        {
            writer.WriteLine($"- disposition: {disposition.Kind} by {disposition.Actor} at {disposition.Timestamp:O}; reason: {disposition.Reason}");
        }
        if (result.LateReportDisagreement is not null)
        {
            writer.WriteLine($"- late report disagreement: {result.LateReportDisagreement}");
        }
        writer.WriteLine($"- verdict: {result.Verdict ?? "<unknown>"}");
        if (result.Cause is not null)
        {
            writer.WriteLine($"- cause: {result.Cause}");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private static int ExecuteDelivery(
        TextWriter writer,
        string operation,
        NotifyOptions options,
        SessionLayerModeResolution resolution,
        SessionLayerPreflightResult preflight,
        string? reportAdvisory = null,
        NotifyReportOutboxEntry? existingOutbox = null,
        string? reportRoot = null)
    {
        var isReport = string.Equals(operation, OperationReport, StringComparison.Ordinal)
            || string.Equals(operation, OperationCollect, StringComparison.Ordinal);
        var resolvedReportRoot = reportRoot ?? options.ReportRoot ?? options.RoutingRoot!;
        var senderLocalReport = isReport && !PathsEqual(resolvedReportRoot, options.RoutingRoot!);
        if (senderLocalReport)
        {
            reportAdvisory = CombineAdvisories(
                reportAdvisory,
                $"sender-local report handoff: '{resolvedReportRoot}' is the writable report root; host routing state at '{options.RoutingRoot}' is read/transport authority and is reconciled by the orchestration role.");
        }
        var reportCommand = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? BuildReportCommand(options)
            : null;
        var payload = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? BuildDelegatePayload(options, reportCommand!)
            : BuildReportPayload(options);
        NotifyPendingDelegation? reportPendingRecord = null;
        if (isReport)
        {
            var pending = NotifyPendingDelegationStore.Find(
                options.RoutingRoot!,
                options.Domain,
                options.Team,
                options.TaskId!);
            reportPendingRecord = pending.Resolved && pending.Record is { ReportArrived: false }
                ? pending.Record
                : null;
            if (string.Equals(operation, OperationCollect, StringComparison.Ordinal)
                && existingOutbox is not null
                && !string.Equals(reportPendingRecord?.ResultNonce, existingOutbox.ResultNonce, StringComparison.Ordinal))
            {
                reportPendingRecord = null;
            }
        }
        NotifyReportOutboxEntry? reportOutbox = existingOutbox;
        ContinuationChainRecord? continuationChain = null;
        string? outboxEntryPath = null;
        if (isReport)
        {
            outboxEntryPath = NotifyReportOutboxStore.ResolvePath(resolvedReportRoot, options.Domain!, options.Team!);
            if (options.Write && reportOutbox is null)
            {
                reportOutbox = new NotifyReportOutboxEntry
                {
                    Domain = options.Domain!,
                    Team = options.Team!,
                    TaskId = options.TaskId!,
                    ResultNonce = reportPendingRecord?.ResultNonce,
                    FromRole = options.FromRole!,
                    ToRole = options.ToRole!,
                    Status = options.Status!,
                    Artifact = options.Artifact!,
                    Summary = NotifyEventWriter.NormalizeSummary(options.Summary!),
                    CreatedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                    DeliveryState = "prepared",
                };
                var outboxWrite = NotifyReportOutboxStore.WriteNew(resolvedReportRoot, reportOutbox);
                outboxEntryPath = outboxWrite.Path;
                if (!outboxWrite.Written)
                {
                    Emit(writer, options.Format, FailureResult(operation, options, resolution.Mode,
                        "report-outbox-write-failed", $"Could not persist report task '{options.TaskId}' before transport: {outboxWrite.Error} No transport was attempted.",
                        modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default", preflight: preflight,
                        outboxEntryPath: outboxEntryPath));
                    return 1;
                }
                reportOutbox = outboxWrite.Entry ?? reportOutbox;
            }
        }

        var inlinePayloadWarning = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? ResolveInlinePayloadWarning(options, payload)
            : null;
        var envelopeDelivery = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? NotifyTaskEnvelopeDelivery.Resolve(options, payload)
            : NotifyTaskEnvelopeDelivery.Inline(payload);
        if (!envelopeDelivery.Resolved)
        {
            if (reportOutbox is not null && options.Write)
            {
                NotifyReportOutboxStore.MarkUndelivered(resolvedReportRoot, reportOutbox, envelopeDelivery.Cause!);
            }
            Emit(writer, options.Format, FailureResult(
                operation,
                options,
                resolution.Mode,
                envelopeDelivery.Cause!,
                envelopeDelivery.Summary,
                payload,
                reportCommand,
                modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                preflight: preflight,
                inlinePayloadWarning: inlinePayloadWarning,
                outboxEntryPath: outboxEntryPath));
            return 1;
        }

        NotifyPendingDelegation? pendingRecord = null;
        string? pendingRecordPath = null;
        if (string.Equals(operation, OperationDelegate, StringComparison.Ordinal))
        {
            pendingRecord = BuildPendingDelegation(options, resolution);
            if (options.Write)
            {
                var pendingWrite = NotifyPendingDelegationStore.WriteDispatch(
                    options.RoutingRoot!,
                    pendingRecord);
                if (!pendingWrite.Written)
                {
                    Emit(writer, options.Format, FailureResult(
                        operation,
                        options,
                        resolution.Mode,
                        "pending-record-write-failed",
                        $"Could not write the durable pending delegation record before notifying role '{options.ToRole}': "
                        + $"{pendingWrite.Error} No notification was sent; repair team-store access and retry notify.",
                        payload,
                        reportCommand,
                        modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                        preflight: preflight,
                        inlinePayloadWarning: inlinePayloadWarning,
                        pendingRecordPath: pendingWrite.Path));
                    return 1;
                }

                pendingRecordPath = pendingWrite.Path;
            }
        }

        if (envelopeDelivery.FileBacked && options.Write)
        {
            var write = NotifyTaskEnvelopeStore.Write(
                options.RoutingRoot!, options.Domain!, options.Team!, options.TaskId!, options.ResultNonce!, payload);
            if (!write.Written)
            {
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    resolution.Mode,
                    "task-file-write-failed",
                    $"Could not write the file-backed task envelope before notifying role '{options.ToRole}': {write.Error} "
                    + "No pointer was sent; repair host task-file access and retry notify.",
                    payload,
                    reportCommand,
                    modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                    preflight: preflight,
                    inlinePayloadWarning: inlinePayloadWarning,
                    deliveryMethod: NotifyTaskEnvelopeDelivery.FileBackedMethod,
                    taskFile: write.Path,
                    deliveryPointer: envelopeDelivery.Pointer));
                return 1;
            }

            envelopeDelivery = envelopeDelivery with { TaskFile = write.Path };
        }

        var runner = ProcessRunnerFactory?.Invoke() ?? new NotifyProcessRunner();
        var transport = string.Equals(resolution.Mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal)
            ? (INotifyTransport)new HerdrNotifyTransport(
                runner,
                options.HerdrExecutable ?? HerdrExecutableFactory?.Invoke() ?? NotifyTransportPaths.ResolveHerdrExecutable())
            : new AgmsgNotifyTransport(
                runner,
                AgmsgScriptsDirectoryFactory?.Invoke() ?? NotifyTransportPaths.ResolveAgmsgScriptsDirectory(),
                options.BashExecutable ?? BashExecutableFactory?.Invoke() ?? NotifyTransportPaths.ResolveBashExecutable());
        var roles = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? new[] { options.FromRole!, options.ToRole!, options.ReportToRole! }
            : new[] { options.FromRole!, options.ToRole! };
        var delivery = transport.Deliver(
            options.RoutingRoot!,
            options.Domain!,
            options.Team!,
            options.FromRole!,
            options.ToRole!,
            roles,
            envelopeDelivery.TransportPayload,
            options.Write,
            allowStoppedRecipient: string.Equals(operation, OperationReport, StringComparison.Ordinal));
        var deliveryPreflight = delivery.ActivePhase is null
            ? preflight
            : SessionLayerPreflight.WithActivePhase(
                preflight,
                delivery.ActivePhase.Status,
                delivery.ActivePhase.ContactedReceiver,
                delivery.ActivePhase.Summary);

        if (!delivery.Resolved)
        {
            if (reportOutbox is not null && options.Write)
            {
                NotifyReportOutboxStore.MarkUndelivered(resolvedReportRoot, reportOutbox, delivery.Cause ?? "transport-failure");
            }
            Emit(writer, options.Format, FailureResult(
                operation,
                options,
                resolution.Mode,
                delivery.Cause!,
                delivery.Summary + (outboxEntryPath is null ? string.Empty : $" Report is retained at '{outboxEntryPath}'."),
                payload,
                reportCommand,
                modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                preflight: deliveryPreflight,
                receiverStateOutcome: delivery.ReceiverStateOutcome,
                workingTransition: delivery.WorkingTransition,
                settleOutcome: delivery.SettleOutcome,
                resendPermitted: delivery.ResendPermitted,
                inlinePayloadWarning: inlinePayloadWarning,
                recipientWarning: delivery.RecipientWarning,
                deliveryMethod: envelopeDelivery.ResultDeliveryMethod,
                taskFile: envelopeDelivery.TaskFile,
                deliveryPointer: envelopeDelivery.ResultPointer,
                outboxEntryPath: outboxEntryPath));
            return 1;
        }

        var eventAppended = false;
        if (delivery.ReaderPath is not null && options.Write)
        {
            try
            {
                NotifyEventWriter.Append(delivery.ReaderPath, BuildReaderEvent(operation, options));
                eventAppended = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var routingWriteFailure = isReport && senderLocalReport;
                var cause = routingWriteFailure
                    ? "report-routing-root-write-required"
                    : "event-append-failed";
                if (reportOutbox is not null)
                {
                    NotifyReportOutboxStore.MarkUndelivered(resolvedReportRoot, reportOutbox, cause);
                }

                var summary = routingWriteFailure
                    ? $"Could not append notification to external role '{options.ToRole}' through recorded reader "
                      + $"'{delivery.ReaderPath}' in the current execution context: {exception.Message} "
                      + $"The attempted host-root write failed, so the sender-local report handoff is retained at '{outboxEntryPath}'. "
                      + "This measured write failure is a delegation-level routing fault, not an implementation-seat stall."
                    : $"Could not append notification to external role '{options.ToRole}' through recorded reader "
                      + $"'{delivery.ReaderPath}': {exception.Message} Fix reader access and retry notify.";
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    resolution.Mode,
                    cause,
                    summary,
                    payload,
                    reportCommand,
                    modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                    preflight: deliveryPreflight,
                    inlinePayloadWarning: inlinePayloadWarning,
                    deliveryMethod: envelopeDelivery.ResultDeliveryMethod,
                    taskFile: envelopeDelivery.TaskFile,
                    deliveryPointer: envelopeDelivery.ResultPointer,
                    outboxEntryPath: outboxEntryPath));
                return 1;
            }
        }

        if (eventAppended)
        {
            deliveryPreflight = SessionLayerPreflight.WithActivePhase(
                preflight,
                SessionLayerPreflight.ActiveAcknowledged,
                contactedReceiver: false,
                summary: "The canonical external-reader event append completed exactly once; pane transition observation does not apply.");
        }

        string? deliveryEvidenceAdvisory = null;
        if (pendingRecord is not null
            && options.Write
            && delivery.Resolved
            && (delivery.Delivered || eventAppended))
        {
            var deliveredAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
            var deliveryWrite = NotifyDelegationDeliveryStore.Write(
                options.RoutingRoot!,
                pendingRecord,
                deliveredAt);
            if (!deliveryWrite.Written)
            {
                deliveryEvidenceAdvisory =
                    $"Delivery succeeded for task '{pendingRecord.TaskId}', but durable delivery evidence could not be recorded: "
                    + $"{deliveryWrite.Error} Supervision will fail closed for the delivered-but-never-executed finding.";
            }
        }
        reportAdvisory = CombineAdvisories(reportAdvisory, deliveryEvidenceAdvisory);
        if (isReport
            && options.Write
            && reportPendingRecord is not null
            && !senderLocalReport)
        {
            var reportWrite = NotifyPendingDelegationStore.WriteReport(
                options.RoutingRoot!,
                reportPendingRecord,
                options.Status!,
                options.Artifact!,
                NotifyEventWriter.NormalizeSummary(options.Summary!),
                (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime());
            if (!reportWrite.Written)
            {
                if (reportOutbox is not null)
                {
                    NotifyReportOutboxStore.MarkUndelivered(resolvedReportRoot, reportOutbox, "pending-record-resolution-failed");
                }
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    resolution.Mode,
                    "pending-record-resolution-failed",
                    $"Report delivery succeeded, but resolving task '{options.TaskId}' in the pending store failed: "
                    + $"{reportWrite.Error} Preserve this report and repair the store before retrying.",
                    payload,
                    reportCommand,
                    modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                    preflight: deliveryPreflight,
                    receiverStateOutcome: delivery.ReceiverStateOutcome,
                    workingTransition: delivery.WorkingTransition,
                    settleOutcome: delivery.SettleOutcome,
                    resendPermitted: delivery.ResendPermitted,
                    inlinePayloadWarning: inlinePayloadWarning,
                    pendingRecordPath: reportWrite.Path,
                    outboxEntryPath: outboxEntryPath));
                return 1;
            }
        }

        // G695: a delivered report is the first durable link in the
        // completion-signal chain. Record it before the outbox is marked
        // delivered so a chain-write failure remains visible to collection and
        // cannot be mistaken for a fully settled signal.
        if (isReport && options.Write && !senderLocalReport)
        {
            var chainWrite = ContinuationChainStore.RecordReportReceived(
                options.RoutingRoot!,
                options.Domain!,
                options.Team!,
                options.TaskId!,
                options.ResultNonce ?? reportOutbox?.ResultNonce,
                options.Status!,
                options.Artifact!,
                NotifyEventWriter.NormalizeSummary(options.Summary!));
            continuationChain = chainWrite.Record ?? chainWrite.Preview;
            if (!chainWrite.Applied && !chainWrite.AlreadyConverged)
            {
                if (reportOutbox is not null)
                {
                    NotifyReportOutboxStore.MarkUndelivered(
                        resolvedReportRoot,
                        reportOutbox,
                        "continuation-chain-write-failed");
                }
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    resolution.Mode,
                    "continuation-chain-write-failed",
                    $"Report transport completed, but durable continuation-chain link '{ContinuationChainStore.ReportReceived}' could not be recorded: {chainWrite.Error}",
                    payload,
                    reportCommand,
                    modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                    preflight: deliveryPreflight,
                    receiverStateOutcome: delivery.ReceiverStateOutcome,
                    workingTransition: delivery.WorkingTransition,
                    settleOutcome: delivery.SettleOutcome,
                    resendPermitted: delivery.ResendPermitted,
                    inlinePayloadWarning: inlinePayloadWarning,
                    recipientWarning: delivery.RecipientWarning,
                    deliveryMethod: envelopeDelivery.ResultDeliveryMethod,
                    taskFile: envelopeDelivery.TaskFile,
                    deliveryPointer: envelopeDelivery.ResultPointer,
                    pendingRecordPath: pendingRecordPath,
                    outboxEntryPath: outboxEntryPath,
                    continuationChain: continuationChain));
                return 1;
            }
        }

        if (reportOutbox is not null && options.Write)
        {
            var deliveredWrite = NotifyReportOutboxStore.MarkDelivered(resolvedReportRoot, reportOutbox);
            outboxEntryPath = deliveredWrite.Path;
            if (!deliveredWrite.Written)
            {
                Emit(writer, options.Format, FailureResult(operation, options, resolution.Mode,
                    "report-outbox-delivery-mark-failed", $"Report transport completed but its outbox entry could not be marked delivered: {deliveredWrite.Error}",
                    modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default", preflight: deliveryPreflight,
                    outboxEntryPath: outboxEntryPath));
                return 1;
            }
        }

        Emit(writer, options.Format, SuccessResult(
            operation,
            options,
            resolution,
            delivered: delivery.Delivered || eventAppended,
            eventAppended,
            payload,
            reportCommand,
            AppendReportHandoffSummary(
                eventAppended
                    ? $"Delivered {operation} to external logical role '{options.ToRole}' in team '{options.Team}' "
                      + $"through recorded reader '{delivery.ReaderPath}'."
                    : delivery.Summary,
                senderLocalReport,
                eventAppended,
                options.Write,
                resolvedReportRoot,
                options.RoutingRoot!),
            eventPath: delivery.ReaderPath,
            preflight: deliveryPreflight,
            receiverStateOutcome: delivery.ReceiverStateOutcome,
            workingTransition: delivery.WorkingTransition,
            settleOutcome: delivery.SettleOutcome,
            resendPermitted: delivery.ResendPermitted,
            inlinePayloadWarning: inlinePayloadWarning,
            recipientWarning: delivery.RecipientWarning,
            deliveryMethod: envelopeDelivery.ResultDeliveryMethod,
            taskFile: envelopeDelivery.TaskFile,
            deliveryPointer: envelopeDelivery.ResultPointer,
            pendingRecordPath: pendingRecordPath,
            advisory: reportAdvisory,
            outboxEntryPath: outboxEntryPath,
            continuationChain: continuationChain));
        return 0;
    }

    private static string? CombineAdvisories(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second;
        if (string.IsNullOrWhiteSpace(second)) return first;
        return $"{first} {second}";
    }

    private static string AppendReportHandoffSummary(
        string summary,
        bool senderLocalReport,
        bool eventAppended,
        bool write,
        string reportRoot,
        string routingRoot)
    {
        if (!senderLocalReport)
        {
            return summary;
        }

        var hostWrite = eventAppended
            ? $"the host routing event was appended at '{routingRoot}'"
            : write
                ? "no host-root write was required for this recorded pane delivery"
                : "no host routing write was attempted in this dry-run";
        return $"{summary} Sender-local report handoff persisted under '{reportRoot}'; {hostWrite}. "
            + $"Host pending and continuation state at '{routingRoot}' remains for orchestration reconciliation.";
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string? ReportStorageMode(NotifyOptions options) =>
        options.ReportRoot is null
            ? null
            : PathsEqual(options.ReportRoot, options.RoutingRoot ?? string.Empty)
                ? "routing-root"
                : "sender-local-role-work-root";

    private static string? HostStateSync(NotifyOptions options) =>
        options.ReportRoot is not null
        && !PathsEqual(options.ReportRoot, options.RoutingRoot ?? string.Empty)
            ? "deferred-to-orchestration"
            : null;

    private static NotifyDesignEvent BuildReaderEvent(string operation, NotifyOptions options) => new()
    {
        Timestamp = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
        Team = options.Team!,
        Kind = ReaderEventKind(operation, options.Status),
        Unit = options.TaskId!,
        Summary = NotifyEventWriter.NormalizeSummary(
            string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
                ? options.Objective!
                : options.Summary!),
        Artifact = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? options.Inputs.FirstOrDefault() ?? options.ExpectedArtifacts[0]
            : options.Artifact!,
    };

    private static NotifyPendingDelegation BuildPendingDelegation(
        NotifyOptions options,
        SessionLayerModeResolution resolution)
    {
        string? resident = null;
        string? workspaceId = null;
        string? paneId = null;
        string? reader = null;
        string? cwd = null;
        string? kind = null;
        IReadOnlyList<string>? launchArguments = null;
        var identity = $"role={options.ToRole}";
        var topology = NotifyRoleTopologyStore.Resolve(options.RoutingRoot!, options.Domain!, options.Team!);
        var roleResolution = topology.Resolved && topology.Topology is { } teamTopology
            ? NotifyRoleTopologyStore.ResolveRecordedRole(teamTopology, options.ToRole!)
            : null;
        if (roleResolution?.Resolved == true
            && roleResolution.Record is { } recorded)
        {
            resident = recorded.Resident;
            workspaceId = recorded.WorkspaceId ?? topology.Topology!.WorkspaceId;
            paneId = recorded.PaneId;
            reader = recorded.Reader;
            cwd = recorded.Cwd;
            kind = recorded.Kind;
            launchArguments = recorded.LaunchArguments;
            identity = recorded.Resident switch
            {
                NotifyRecordedRole.HerdrResident =>
                    $"role={options.ToRole};workspace={workspaceId};pane={paneId}",
                NotifyRecordedRole.ExternalResident =>
                    $"role={options.ToRole};reader={reader}",
                _ => $"role={options.ToRole}",
            };
        }

        return new NotifyPendingDelegation
        {
            Domain = options.Domain!,
            Team = options.Team!,
            TaskId = options.TaskId!,
            DelegatingRole = options.FromRole,
            RecipientRole = options.ToRole!,
            ReportToRole = options.ReportToRole,
            RecipientIdentity = identity,
            ExpectedArtifact = string.Join("; ", options.ExpectedArtifacts),
            ExpectedArtifacts = options.ExpectedArtifacts.ToArray(),
            Objective = options.Objective,
            Inputs = options.Inputs.ToArray(),
            ResultNonce = options.ResultNonce,
            DispatchedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            TransportMode = resolution.Mode,
            Resident = resident,
            WorkspaceId = workspaceId,
            PaneId = paneId,
            Reader = reader,
            Cwd = cwd,
            Kind = kind,
            LaunchArguments = launchArguments,
        };
    }

    private static string ReaderEventKind(string operation, string? status)
    {
        if (string.Equals(operation, OperationDelegate, StringComparison.Ordinal))
        {
            return QuestionEventKind;
        }

        if (status is not null && ReportReaderEventKinds.TryGetValue(status, out var eventKind))
        {
            return eventKind;
        }

        throw new InvalidOperationException(
            $"Report status '{status}' does not map to a documented reader-event kind.");
    }

    private static int ExecuteEscalation(
        TextWriter writer,
        NotifyOptions options,
        SessionLayerModeResolution resolution)
    {
        var judgment = NotifyRecipientDeliveryJudgment.Resolve(
            options.RoutingRoot!,
            options.Domain!,
            options.Team!,
            "design");
        var path = judgment.UsesRecordedReaderAppend ? judgment.Target : null;
        if (path is null
            && !NotifyEventWriter.TryResolveWritePath(
                options.RoutingRoot!, options.Domain!, options.Team!, out path, out var error))
        {
            Emit(writer, options.Format, FailureResult(
                OperationEscalate,
                options,
                resolution.Mode,
                "invalid-team",
                error,
                deliveryBasis: judgment.Basis));
            return 1;
        }

        if (!options.Write)
        {
            Emit(writer, options.Format, SuccessResult(
                OperationEscalate,
                options,
                resolution,
                delivered: false,
                eventAppended: false,
                payload: null,
                reportCommand: null,
                $"Dry-run: would append escalation to '{path}'.",
                eventPath: path,
                deliveryBasis: judgment.Basis));
            return 0;
        }

        var escalation = new NotifyDesignEvent
        {
            Timestamp = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            Team = options.Team!,
            Kind = EscalationEventKind,
            Unit = options.TaskId!,
            Summary = NotifyEventWriter.NormalizeSummary(options.Summary!),
            Artifact = options.Artifact!,
        };
        try
        {
            NotifyEventWriter.Append(path, escalation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var finding = NotifyEscalationFailureStore.Append(
                options.RoutingRoot!,
                new NotifyEscalationAppendFailure
                {
                    Timestamp = escalation.Timestamp,
                    Domain = options.Domain!,
                    Team = options.Team!,
                    TaskId = options.TaskId!,
                    FromRole = options.FromRole!,
                    Artifact = options.Artifact!,
                    Summary = escalation.Summary,
                    ReaderPath = path,
                    DeliveryBasis = judgment.Basis ?? "residency-unresolved",
                    Error = exception.Message,
                });
            Emit(writer, options.Format, FailureResult(
                OperationEscalate,
                options,
                resolution.Mode,
                "event-append-failed",
                $"Could not append the design-boundary event: {exception.Message} "
                + (finding.Written
                    ? $"The genuine undelivered-escalation finding was retained at '{finding.Path}'."
                    : $"The undelivered-escalation finding also could not be retained at '{finding.Path}': {finding.Error}"),
                deliveryBasis: judgment.Basis));
            return 1;
        }

        var delivered = judgment.Judge(readerAppendSucceeded: true, paneWakeDelivered: false);
        Emit(writer, options.Format, SuccessResult(
            OperationEscalate,
            options,
            resolution,
            delivered,
            eventAppended: true,
            payload: null,
            reportCommand: null,
            delivered
                ? $"Appended escalation for task '{options.TaskId}' to the recorded design reader; durable append satisfied delivery."
                : $"Appended escalation for task '{options.TaskId}' to the design-boundary event channel; the pane-resident design role was not woken.",
            eventPath: path,
            deliveryBasis: judgment.Basis));
        return 0;
    }

    private static string BuildDelegatePayload(NotifyOptions options, string reportCommand)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"TASK {options.TaskId}");
        builder.AppendLine($"role: {options.ToRole}");
        builder.AppendLine($"objective: {options.Objective}");
        builder.AppendLine("inputs:");
        foreach (var input in options.Inputs)
        {
            builder.AppendLine($"  - {input}");
        }
        builder.AppendLine("expected-artifacts:");
        foreach (var artifact in options.ExpectedArtifacts)
        {
            builder.AppendLine($"  - {artifact}");
        }
        builder.AppendLine("reporting-contract:");
        builder.AppendLine($"  task-id: {options.TaskId}");
        builder.AppendLine($"  expected-artifact: {string.Join("; ", options.ExpectedArtifacts)}");
        builder.AppendLine($"  canonical-report-command: {reportCommand}");
        builder.AppendLine("  required-final-step: Run canonical-report-command after all other work; never hand-write a transport invocation.");
        builder.AppendLine("result-prefix: ORCH_RESULT");
        builder.AppendLine($"result-nonce: {options.ResultNonce}");
        builder.Append("completion-marker: When the artifact is ready, concatenate result-prefix, one space, result-nonce, one space, status, one space, and artifact; use completed, blocked, or question. Do not precompose the marker in this task block.");
        return builder.ToString();
    }

    private static string BuildReportCommand(NotifyOptions options)
    {
        var reportRoot = ResolveRecipientReportRoot(options);
        return $"intent-cli notify report --domain {options.Domain} --team {options.Team} --from {options.ToRole} "
        + $"--to {options.ReportToRole} --task-id {options.TaskId} --status <completed|blocked|question> "
        + $"--artifact <artifact> --summary <one-line-summary> --routing-root {ShellQuote(options.RoutingRoot!)} --report-root {reportRoot} "
        + "--write --format json";
    }

    private static string ResolveRecipientReportRoot(NotifyOptions options)
    {
        var topology = NotifyRoleTopologyStore.Resolve(options.RoutingRoot!, options.Domain!, options.Team!);
        var roleResolution = topology.Resolved && topology.Topology is { } teamTopology
            ? NotifyRoleTopologyStore.ResolveRecordedRole(teamTopology, options.ToRole!)
            : null;
        var cwd = roleResolution?.Resolved == true
            ? roleResolution.Record?.Cwd
            : null;
        return string.IsNullOrWhiteSpace(cwd)
            ? MissingRecipientWorkRootPlaceholder
            : ShellQuote(Path.GetFullPath(cwd));
    }

    private static string? BuildReconciliationCommand(string operation, NotifyOptions options) =>
        (string.Equals(operation, OperationReport, StringComparison.Ordinal)
            || string.Equals(operation, OperationCollect, StringComparison.Ordinal))
        && options.ReportRoot is not null
        && !PathsEqual(options.ReportRoot, options.RoutingRoot ?? string.Empty)
            ? $"intent-cli notify reconcile --domain {options.Domain} --team {options.Team} --task-id {options.TaskId} "
              + $"--routing-root {ShellQuote(options.RoutingRoot!)} --report-root {ShellQuote(options.ReportRoot)} --write --format json"
            : null;

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string BuildReportPayload(NotifyOptions options) => JsonSerializer.Serialize(new
    {
        notification = OperationReport,
        task_id = options.TaskId,
        status = options.Status,
        from_role = options.FromRole,
        artifact = options.Artifact,
        summary = NotifyEventWriter.NormalizeSummary(options.Summary!),
    });

    private static NotifyInlinePayloadWarning? ResolveInlinePayloadWarning(NotifyOptions options, string payload)
    {
        var topology = NotifyRoleTopologyStore.Resolve(options.RoutingRoot!, options.Domain!, options.Team!);
        var roleResolution = topology.Resolved && topology.Topology is { } teamTopology
            ? NotifyRoleTopologyStore.ResolveRecordedRole(teamTopology, options.ToRole!)
            : null;
        if (roleResolution?.Resolved != true
            || roleResolution.Record is not { } recipient
            || !string.Equals(recipient.Kind, "copilot", StringComparison.OrdinalIgnoreCase)
            || payload.Length <= CopilotObservedPasteRiskWarningChars)
        {
            return null;
        }

        return new NotifyInlinePayloadWarning
        {
            Profile = CopilotObservedPasteRiskProfile,
            PayloadChars = payload.Length,
            ThresholdChars = CopilotObservedPasteRiskWarningChars,
            Remedy = ReferenceFirstRemedy,
        };
    }

    private static NotifyResult SuccessResult(
        string operation,
        NotifyOptions options,
        SessionLayerModeResolution resolution,
        bool delivered,
        bool eventAppended,
        string? payload,
        string? reportCommand,
        string summary,
        string? eventPath = null,
        SessionLayerPreflightResult? preflight = null,
        string? receiverStateOutcome = null,
        string? workingTransition = null,
        string? settleOutcome = null,
        bool? resendPermitted = null,
        NotifyInlinePayloadWarning? inlinePayloadWarning = null,
        NotifyRecipientWarning? recipientWarning = null,
        string? deliveryMethod = null,
        string? taskFile = null,
        string? deliveryPointer = null,
        string? pendingRecordPath = null,
        string? advisory = null,
        string? outboxEntryPath = null,
        string? deliveryBasis = null,
        ContinuationChainRecord? continuationChain = null) => new()
        {
            Operation = operation,
            RoutingRoot = options.RoutingRoot!,
            ReportRoot = options.ReportRoot,
            ReportStorageMode = ReportStorageMode(options),
            HostStateSync = HostStateSync(options),
            Domain = options.Domain!,
            Team = options.Team!,
            Mode = resolution.Mode,
            ModeSource = resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
            CommandMode = options.Write ? "write" : "dry-run",
            FromRole = options.FromRole!,
            ToRole = options.ToRole,
            TaskId = options.TaskId!,
            Status = options.Status,
            Artifact = options.Artifact,
            Delivered = delivered,
            EventAppended = eventAppended,
            EventPath = eventPath,
            SessionLayerPreflight = preflight,
            ReceiverStateOutcome = receiverStateOutcome,
            WorkingTransition = workingTransition,
            SettleOutcome = settleOutcome,
            ResendPermitted = resendPermitted,
            InlinePayloadWarning = inlinePayloadWarning,
            RecipientWarning = recipientWarning,
            Advisory = advisory,
            Warnings = BuildWarnings(advisory, recipientWarning),
            DeliveryMethod = deliveryMethod,
            TaskFile = taskFile,
            DeliveryPointer = deliveryPointer,
            PendingRecordPath = pendingRecordPath,
            OutboxEntryPath = outboxEntryPath,
            DeliveryBasis = deliveryBasis,
            Cause = null,
            Payload = payload,
            ReportCommand = reportCommand,
            ReconciliationCommand = BuildReconciliationCommand(operation, options),
            ContinuationChain = continuationChain,
            Summary = summary,
        };

    private static NotifyResult FailureResult(
        string operation,
        NotifyOptions options,
        string mode,
        string cause,
        string summary,
        string? payload = null,
        string? reportCommand = null,
        string? modeSource = null,
        SessionLayerPreflightResult? preflight = null,
        string? receiverStateOutcome = null,
        string? workingTransition = null,
        string? settleOutcome = null,
        bool? resendPermitted = null,
        NotifyInlinePayloadWarning? inlinePayloadWarning = null,
        NotifyRecipientWarning? recipientWarning = null,
        string? deliveryMethod = null,
        string? taskFile = null,
        string? deliveryPointer = null,
        string? pendingRecordPath = null,
        string? advisory = null,
        string? outboxEntryPath = null,
        string? deliveryBasis = null,
        ContinuationChainRecord? continuationChain = null) => new()
        {
            Operation = operation,
            RoutingRoot = options.RoutingRoot ?? string.Empty,
            ReportRoot = options.ReportRoot,
            ReportStorageMode = ReportStorageMode(options),
            HostStateSync = HostStateSync(options),
            Domain = options.Domain!,
            Team = options.Team!,
            Mode = mode,
            ModeSource = modeSource,
            CommandMode = options.Write ? "write" : "dry-run",
            FromRole = options.FromRole!,
            ToRole = options.ToRole,
            TaskId = options.TaskId!,
            Status = options.Status,
            Artifact = options.Artifact,
            Delivered = false,
            EventAppended = false,
            EventPath = null,
            SessionLayerPreflight = preflight,
            ReceiverStateOutcome = receiverStateOutcome,
            WorkingTransition = workingTransition,
            SettleOutcome = settleOutcome,
            ResendPermitted = resendPermitted,
            InlinePayloadWarning = inlinePayloadWarning,
            RecipientWarning = recipientWarning,
            Advisory = advisory,
            Warnings = BuildWarnings(advisory, recipientWarning),
            DeliveryMethod = deliveryMethod,
            TaskFile = taskFile,
            DeliveryPointer = deliveryPointer,
            PendingRecordPath = pendingRecordPath,
            OutboxEntryPath = outboxEntryPath,
            DeliveryBasis = deliveryBasis,
            Cause = cause,
            Payload = payload,
            ReportCommand = reportCommand,
            ReconciliationCommand = BuildReconciliationCommand(operation, options),
            ContinuationChain = continuationChain,
            Summary = summary,
        };

    private static IReadOnlyList<string>? BuildWarnings(
        string? advisory,
        NotifyRecipientWarning? recipientWarning)
    {
        var warnings = new List<string>();
        if (recipientWarning is not null)
        {
            warnings.Add(recipientWarning.Message);
        }

        if (!string.IsNullOrWhiteSpace(advisory))
        {
            warnings.Add(advisory);
        }

        return warnings.Count == 0 ? null : warnings;
    }

    private static void Emit(TextWriter writer, string format, NotifyResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# notify {result.Operation} — {result.TaskId}");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode} ({result.ModeSource ?? "unresolved"})");
        writer.WriteLine($"- command mode: {result.CommandMode}");
        if (result.ReportRoot is not null)
        {
            writer.WriteLine($"- report root: {result.ReportRoot} ({result.ReportStorageMode ?? "unknown"})");
            if (result.HostStateSync is not null)
            {
                writer.WriteLine($"- host state sync: {result.HostStateSync}");
            }
        }
        writer.WriteLine($"- delivered: {result.Delivered.ToString().ToLowerInvariant()}");
        if (result.DeliveryBasis is not null)
        {
            writer.WriteLine($"- delivery basis: {result.DeliveryBasis}");
        }
        writer.WriteLine($"- event appended: {result.EventAppended.ToString().ToLowerInvariant()}");
        if (result.Cause is not null)
        {
            writer.WriteLine($"- cause: {result.Cause}");
        }
        if (result.SessionLayerPreflight is { } preflight)
        {
            writer.WriteLine($"- session-layer preflight: {preflight.Verdict}");
            writer.WriteLine($"- passive phase: {preflight.PassivePhase.Status}");
            writer.WriteLine($"- active phase: {preflight.ActivePhase.Status}");
        }
        if (result.ReceiverStateOutcome is not null)
        {
            writer.WriteLine($"- receiver outcome: {result.ReceiverStateOutcome}");
        }
        if (result.WorkingTransition is not null)
        {
            writer.WriteLine($"- working transition: {result.WorkingTransition}");
        }
        if (result.SettleOutcome is not null)
        {
            writer.WriteLine($"- settle outcome: {result.SettleOutcome}");
        }
        if (result.ResendPermitted is not null)
        {
            writer.WriteLine($"- resend permitted: {result.ResendPermitted.Value.ToString().ToLowerInvariant()}");
        }
        if (result.InlinePayloadWarning is { } warning)
        {
            writer.WriteLine($"- inline payload warning: profile={warning.Profile}; size={warning.PayloadChars} chars; "
                + $"threshold={warning.ThresholdChars} chars; remedy: {warning.Remedy}");
        }
        if (result.RecipientWarning is { } recipientWarning)
        {
            writer.WriteLine($"- recipient warning: role={recipientWarning.Role}; liveness={recipientWarning.ObservedLiveness}; {recipientWarning.Message}");
        }
        if (result.Advisory is not null)
        {
            writer.WriteLine($"- advisory: {result.Advisory}");
        }
        if (result.DeliveryMethod is not null)
        {
            writer.WriteLine($"- envelope delivery method: {result.DeliveryMethod}");
            writer.WriteLine($"- task file: `{result.TaskFile}`");
            writer.WriteLine($"- pane pointer: `{result.DeliveryPointer}`");
        }
        if (result.ReportCommand is not null)
        {
            writer.WriteLine($"- report command: `{result.ReportCommand}`");
        }
        if (result.ReconciliationCommand is not null)
        {
            writer.WriteLine($"- reconciliation command: `{result.ReconciliationCommand}`");
        }
        if (result.EventPath is not null)
        {
            writer.WriteLine($"- event path: `{result.EventPath}`");
        }
        if (result.PendingRecordPath is not null)
        {
            writer.WriteLine("- pending record: " + result.PendingRecordPath);
        }
        if (result.OutboxEntryPath is not null)
        {
            writer.WriteLine("- report outbox: " + result.OutboxEntryPath);
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private static void EmitReconciliation(
        TextWriter writer,
        string format,
        NotifyReconciliationResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# notify reconcile — {result.TaskId}");
        writer.WriteLine();
        writer.WriteLine($"- command mode: {result.CommandMode}");
        writer.WriteLine($"- routing root: {result.RoutingRoot}");
        writer.WriteLine($"- report root: {result.ReportRoot}");
        writer.WriteLine($"- report delivery state: {result.ReportDeliveryState ?? "<unavailable>"}");
        writer.WriteLine($"- pending reconciled: {result.PendingReconciled.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- pending already converged: {result.PendingAlreadyConverged.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- continuation reconciled: {result.ContinuationReconciled.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- continuation already converged: {result.ContinuationAlreadyConverged.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- reconciled: {result.Reconciled.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- already converged: {result.AlreadyConverged.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- would reconcile: {result.WouldReconcile.ToString().ToLowerInvariant()}");
        if (result.Cause is not null)
        {
            writer.WriteLine($"- cause: {result.Cause}");
        }
        if (result.RecoveryCommand is not null)
        {
            writer.WriteLine($"- recovery command: `{result.RecoveryCommand}`");
        }
        if (result.PendingRecordPath is not null)
        {
            writer.WriteLine($"- pending record: {result.PendingRecordPath}");
        }
        if (result.ReportOutboxPath is not null)
        {
            writer.WriteLine($"- report outbox: {result.ReportOutboxPath}");
        }
        if (result.ContinuationChainPath is not null)
        {
            writer.WriteLine($"- continuation chain: {result.ContinuationChainPath}");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private static void EmitDisposition(TextWriter writer, string format, NotifyDispositionResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# notify dispose — {result.TaskId}");
        writer.WriteLine();
        writer.WriteLine($"- command mode: {result.CommandMode}");
        writer.WriteLine($"- written: {result.Written.ToString().ToLowerInvariant()}");
        if (result.SettlementBasis is not null)
        {
            writer.WriteLine($"- settlement basis: {result.SettlementBasis}");
        }
        if (result.Disposition is { } disposition)
        {
            writer.WriteLine($"- disposition: {disposition.Kind}");
            writer.WriteLine($"- actor: {disposition.Actor}");
            writer.WriteLine($"- timestamp: {disposition.Timestamp:O}");
            writer.WriteLine($"- reason: {disposition.Reason}");
            if (disposition.SupersedingTaskId is not null)
            {
                writer.WriteLine($"- superseding task id: {disposition.SupersedingTaskId}");
            }
            if (disposition.AppliedOutcomeEvidence is not null)
            {
                writer.WriteLine($"- applied outcome evidence: {disposition.AppliedOutcomeEvidence}");
            }
        }
        if (result.Cause is not null)
        {
            writer.WriteLine($"- cause: {result.Cause}");
        }
        if (result.PendingRecordPath is not null)
        {
            writer.WriteLine($"- pending record: {result.PendingRecordPath}");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private static bool TryParse(string[] args, string operation, out NotifyOptions options, out string error)
    {
        options = null!;
        string? domain = null;
        string? team = null;
        string? fromRole = null;
        string? toRole = null;
        string? reportToRole = null;
        string? taskId = null;
        string? role = null;
        string? since = null;
        string? objective = null;
        string? resultNonce = null;
        string? status = null;
        string? artifact = null;
        string? summary = null;
        string? dispositionKind = null;
        string? actor = null;
        string? reason = null;
        string? supersedingTaskId = null;
        string? appliedOutcomeEvidence = null;
        string? routingRoot = null;
        string? reportRoot = null;
        string? repo = null;
        string? ownerRole = null;
        string? herdrExecutable = null;
        string? bashExecutable = null;
        int? intervalSeconds = null;
        int? detectionBoundSeconds = null;
        int? repeatBackoffSeconds = null;
        int? debounceConsecutiveObservations = null;
        int? delegationExecutionWindowSeconds = null;
        int? staleMinutes = null;
        int? claimedSilentMinutes = null;
        int? backlogIdleMinutes = null;
        int? repairSilentMinutes = null;
        int? timeoutMilliseconds = null;
        var inputs = new List<string>();
        var expectedArtifacts = new List<string>();
        var preApprovalAcceptRules = new List<NotifyPreApprovalRule>();
        var preApprovalEscalateRules = new List<NotifyPreApprovalRule>();
        var scopedPolicies = new List<NotifyScopedPromptPolicy>();
        var write = false;
        var autoRedispatch = false;
        var once = false;
        var eventMode = false;
        var wait = false;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain": if (!ReadValue(args, ref index, argument, out domain, out error)) return false; break;
                case "--team": if (!ReadValue(args, ref index, argument, out team, out error)) return false; break;
                case "--from": if (!ReadValue(args, ref index, argument, out fromRole, out error)) return false; break;
                case "--to": if (!ReadValue(args, ref index, argument, out toRole, out error)) return false; break;
                case "--report-to": if (!ReadValue(args, ref index, argument, out reportToRole, out error)) return false; break;
                case "--task-id": if (!ReadValue(args, ref index, argument, out taskId, out error)) return false; break;
                case "--role": if (!ReadValue(args, ref index, argument, out role, out error)) return false; break;
                case "--since": if (!ReadValue(args, ref index, argument, out since, out error)) return false; break;
                case "--objective": if (!ReadValue(args, ref index, argument, out objective, out error)) return false; break;
                case "--result-nonce": if (!ReadValue(args, ref index, argument, out resultNonce, out error)) return false; break;
                case "--status": if (!ReadValue(args, ref index, argument, out status, out error)) return false; break;
                case "--artifact": if (!ReadValue(args, ref index, argument, out artifact, out error)) return false; break;
                case "--summary": if (!ReadValue(args, ref index, argument, out summary, out error)) return false; break;
                case "--kind": if (!ReadValue(args, ref index, argument, out dispositionKind, out error)) return false; break;
                case "--actor": if (!ReadValue(args, ref index, argument, out actor, out error)) return false; break;
                case "--reason": if (!ReadValue(args, ref index, argument, out reason, out error)) return false; break;
                case "--superseding-task-id": if (!ReadValue(args, ref index, argument, out supersedingTaskId, out error)) return false; break;
                case "--applied-outcome-evidence": if (!ReadValue(args, ref index, argument, out appliedOutcomeEvidence, out error)) return false; break;
                case "--routing-root": if (!ReadValue(args, ref index, argument, out routingRoot, out error)) return false; break;
                case "--report-root": if (!ReadValue(args, ref index, argument, out reportRoot, out error)) return false; break;
                case "--repo": if (!ReadValue(args, ref index, argument, out repo, out error)) return false; break;
                case "--owner-role": if (!ReadValue(args, ref index, argument, out ownerRole, out error)) return false; break;
                case "--herdr-executable": if (!ReadValue(args, ref index, argument, out herdrExecutable, out error)) return false; break;
                case "--bash-executable": if (!ReadValue(args, ref index, argument, out bashExecutable, out error)) return false; break;
                case "--interval":
                    if (!ReadValue(args, ref index, argument, out var intervalValue, out error)
                        || !int.TryParse(intervalValue, out var parsedInterval))
                    {
                        error = "--interval requires a whole-number seconds value.";
                        return false;
                    }
                    intervalSeconds = parsedInterval;
                    break;
                case "--bound":
                case "--detection-bound":
                case "--bound-seconds":
                case "--detection-bound-seconds":
                    if (!ReadValue(args, ref index, argument, out var boundValue, out error)
                        || !int.TryParse(boundValue, out var parsedBound))
                    {
                        error = $"{argument} requires a whole-number seconds value.";
                        return false;
                    }
                    detectionBoundSeconds = parsedBound;
                    break;
                case "--delegation-execution-window-seconds":
                    if (!ReadValue(args, ref index, argument, out var delegationWindowValue, out error)
                        || !int.TryParse(delegationWindowValue, out var parsedDelegationWindow))
                    {
                        error = $"{argument} requires a whole-number seconds value.";
                        return false;
                    }
                    delegationExecutionWindowSeconds = parsedDelegationWindow;
                    break;
                case "--repeat-backoff-seconds":
                case "--backoff-seconds":
                    if (!ReadValue(args, ref index, argument, out var backoffValue, out error)
                        || !int.TryParse(backoffValue, out var parsedBackoff))
                    {
                        error = $"{argument} requires a whole-number seconds value.";
                        return false;
                    }
                    repeatBackoffSeconds = parsedBackoff;
                    break;
                case "--debounce-consecutive-observations":
                case "--status-debounce-consecutive":
                    if (!ReadValue(args, ref index, argument, out var debounceValue, out error)
                        || !int.TryParse(debounceValue, out var parsedDebounce))
                    {
                        error = $"{argument} requires a whole-number count value.";
                        return false;
                    }
                    debounceConsecutiveObservations = parsedDebounce;
                    break;
                case "--stale-minutes":
                    if (!ReadValue(args, ref index, argument, out var staleValue, out error)
                        || !int.TryParse(staleValue, out var parsedStale))
                    {
                        error = "--stale-minutes requires a whole-number minutes value.";
                        return false;
                    }
                    staleMinutes = parsedStale;
                    break;
                case "--claimed-silent-minutes":
                    if (!ReadValue(args, ref index, argument, out var claimedValue, out error)
                        || !int.TryParse(claimedValue, out var parsedClaimed))
                    {
                        error = "--claimed-silent-minutes requires a whole-number minutes value.";
                        return false;
                    }
                    claimedSilentMinutes = parsedClaimed;
                    break;
                case "--backlog-idle-minutes":
                    if (!ReadValue(args, ref index, argument, out var backlogValue, out error)
                        || !int.TryParse(backlogValue, out var parsedBacklog))
                    {
                        error = "--backlog-idle-minutes requires a whole-number minutes value.";
                        return false;
                    }
                    backlogIdleMinutes = parsedBacklog;
                    break;
                case "--repair-silent-minutes":
                    if (!ReadValue(args, ref index, argument, out var repairValue, out error)
                        || !int.TryParse(repairValue, out var parsedRepair))
                    {
                        error = "--repair-silent-minutes requires a whole-number minutes value.";
                        return false;
                    }
                    repairSilentMinutes = parsedRepair;
                    break;
                case "--timeout-ms":
                    if (!ReadValue(args, ref index, argument, out var timeoutValue, out error)
                        || !int.TryParse(timeoutValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTimeout))
                    {
                        error = "--timeout-ms requires a whole-number milliseconds value.";
                        return false;
                    }
                    timeoutMilliseconds = parsedTimeout;
                    break;
                case "--pre-approve":
                    if (!ReadValue(args, ref index, argument, out var acceptRuleValue, out error)
                        || !NotifyPreApprovalPolicyStore.TryParseRule(acceptRuleValue!, out var acceptRule))
                    {
                        error = "--pre-approve requires <agent-kind>:<prompt-class> using safe identifiers.";
                        return false;
                    }
                    if (!NotifyPreApprovalPolicyStore.TryValidateRule(acceptRule!, out error))
                    {
                        return false;
                    }
                    preApprovalAcceptRules.Add(acceptRule!);
                    break;
                case "--pre-escalate":
                    if (!ReadValue(args, ref index, argument, out var escalateRuleValue, out error)
                        || !NotifyPreApprovalPolicyStore.TryParseRule(escalateRuleValue!, out var escalateRule))
                    {
                        error = "--pre-escalate requires <agent-kind>:<prompt-class> using safe identifiers.";
                        return false;
                    }
                    if (!NotifyPreApprovalPolicyStore.TryValidateRule(escalateRule!, out error))
                    {
                        return false;
                    }
                    preApprovalEscalateRules.Add(escalateRule!);
                    break;
                case "--shell-policy":
                case "--pre-approve-shell":
                    if (!ReadValue(args, ref index, argument, out var shellPolicyValue, out error))
                    {
                        return false;
                    }
                    NotifyScopedPromptPolicy? shellPolicy;
                    try
                    {
                        shellPolicy = JsonSerializer.Deserialize<NotifyScopedPromptPolicy>(shellPolicyValue!, JsonOptions);
                    }
                    catch (JsonException exception)
                    {
                        error = $"{argument} requires one scoped policy JSON object: {exception.Message}";
                        return false;
                    }
                    if (shellPolicy is null)
                    {
                        error = $"{argument} requires one scoped policy JSON object.";
                        return false;
                    }
                    if (!NotifyPreApprovalPolicyStore.TryValidateScopedPolicy(
                        shellPolicy,
                        out error,
                        requireScratchLedgerCycleId: false))
                    {
                        return false;
                    }
                    scopedPolicies.Add(shellPolicy);
                    break;
                case "--input":
                    if (!ReadValue(args, ref index, argument, out var input, out error)) return false;
                    inputs.Add(input!);
                    break;
                case "--expected-artifact":
                    if (!ReadValue(args, ref index, argument, out var expectedArtifact, out error)) return false;
                    expectedArtifacts.Add(expectedArtifact!);
                    break;
                case "--write": write = true; break;
                case "--dry-run": write = false; break;
                case "--auto-redispatch": autoRedispatch = true; break;
                case "--once": once = true; break;
                case "--event-mode": eventMode = true; break;
                case "--wait": wait = true; break;
                case "--format":
                    if (!ReadValue(args, ref index, argument, out format, out error)) return false;
                    if (format is not FormatJson and not FormatMarkdown)
                    {
                        error = "--format must be markdown or json.";
                        return false;
                    }
                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        options = new NotifyOptions
        {
            Domain = domain,
            Team = team,
            FromRole = fromRole,
            ToRole = toRole,
            ReportToRole = reportToRole,
            TaskId = taskId,
            Role = role,
            Since = since,
            Objective = objective,
            Inputs = inputs,
            ExpectedArtifacts = expectedArtifacts,
            ResultNonce = resultNonce,
            Status = status,
            Artifact = artifact,
            Summary = summary,
            DispositionKind = dispositionKind,
            Actor = actor,
            Reason = reason,
            SupersedingTaskId = supersedingTaskId,
            AppliedOutcomeEvidence = appliedOutcomeEvidence,
            RoutingRoot = routingRoot,
            ReportRoot = reportRoot,
            Repo = repo,
            OwnerRole = ownerRole,
            HerdrExecutable = herdrExecutable,
            BashExecutable = bashExecutable,
            IntervalSeconds = intervalSeconds,
            DetectionBoundSeconds = detectionBoundSeconds,
            DelegationExecutionWindowSeconds = delegationExecutionWindowSeconds,
            RepeatBackoffSeconds = repeatBackoffSeconds,
            DebounceConsecutiveObservations = debounceConsecutiveObservations,
            StaleMinutes = staleMinutes,
            ClaimedSilentMinutes = claimedSilentMinutes,
            BacklogIdleMinutes = backlogIdleMinutes,
            RepairSilentMinutes = repairSilentMinutes,
            TimeoutMilliseconds = timeoutMilliseconds,
            PreApprovalAcceptRules = preApprovalAcceptRules,
            PreApprovalEscalateRules = preApprovalEscalateRules,
            ScopedPolicies = scopedPolicies,
            Write = write,
            AutoRedispatch = autoRedispatch,
            Once = once,
            EventMode = eventMode,
            Wait = wait,
            Format = format,
        };

        return Validate(operation, options, out error);
    }

    private static bool Validate(string operation, NotifyOptions options, out string error)
    {
        error = string.Empty;
        var requiredIdentity = string.Equals(operation, OperationStatus, StringComparison.Ordinal)
            ? new[] { ("--task-id", options.TaskId) }
            : string.Equals(operation, OperationCollect, StringComparison.Ordinal)
                ? options.Role is not null
                    ? new[] { ("--domain", options.Domain), ("--team", options.Team), ("--role", options.Role) }
                    : new[] { ("--domain", options.Domain), ("--team", options.Team), ("--task-id", options.TaskId) }
            : string.Equals(operation, OperationReconcile, StringComparison.Ordinal)
                ? new[] { ("--domain", options.Domain), ("--team", options.Team), ("--task-id", options.TaskId) }
            : string.Equals(operation, OperationSupervise, StringComparison.Ordinal)
                ? new[] { ("--domain", options.Domain), ("--team", options.Team) }
            : string.Equals(operation, OperationDispose, StringComparison.Ordinal)
                ? new[] { ("--domain", options.Domain), ("--team", options.Team), ("--task-id", options.TaskId) }
            : string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
                ? new[] { ("--domain", options.Domain), ("--from", options.FromRole), ("--task-id", options.TaskId) }
            : new[]
            {
                ("--domain", options.Domain),
                ("--team", options.Team),
                ("--from", options.FromRole),
                ("--task-id", options.TaskId),
            };
        foreach (var (name, value) in requiredIdentity)
        {
            if (!IsSafeIdentity(value))
            {
                error = $"{name} is required and must contain only letters, digits, '.', '_', ':', or '-' without path syntax.";
                return false;
            }
        }

        if (options.Team is not null
            && !NotifyEventWriter.TryValidateTeam(options.Team, out error))
        {
            return false;
        }

        if (!string.Equals(operation, OperationCollect, StringComparison.Ordinal)
            && (options.Role is not null
                || options.Since is not null
                || options.Wait
                || options.TimeoutMilliseconds is not null))
        {
            error = "--role, --since, --wait, and --timeout-ms are supported only by notify collect.";
            return false;
        }

        if (string.Equals(operation, OperationStatus, StringComparison.Ordinal))
        {
            if (options.Domain is not null && !IsSafeIdentity(options.Domain)
                || options.Team is not null && !IsSafeIdentity(options.Team))
            {
                error = "status accepts optional safe --domain and --team values.";
                return false;
            }

            if (options.Domain is not null ^ options.Team is not null)
            {
                error = "status requires --domain and --team together when either is supplied.";
                return false;
            }
        }

        if (options.ReportRoot is not null
            && operation is not OperationReport and not OperationCollect and not OperationReconcile)
        {
            error = "--report-root is supported only by notify report, notify collect, and notify reconcile.";
            return false;
        }
        if (string.Equals(operation, OperationReconcile, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(options.ReportRoot))
            {
                error = "reconcile requires --report-root for the sender-local role work root.";
                return false;
            }

            if (options.FromRole is not null
                || options.ToRole is not null
                || options.ReportToRole is not null
                || options.Status is not null
                || options.Artifact is not null
                || options.Summary is not null
                || options.ResultNonce is not null)
            {
                error = "reconcile accepts only domain, team, task-id, routing-root, report-root, write/dry-run, and format.";
                return false;
            }
        }
        if (string.Equals(operation, OperationSupervise, StringComparison.Ordinal))
        {
            if (options.IntervalSeconds is not null
                && (options.IntervalSeconds < 1 || options.IntervalSeconds > NotifySupervisor.MaximumIntervalSeconds))
            {
                error = $"supervise --interval must be between 1 and {NotifySupervisor.MaximumIntervalSeconds} seconds.";
                return false;
            }

            if (options.DetectionBoundSeconds is not null
                && (options.DetectionBoundSeconds < 1 || options.DetectionBoundSeconds > 86_400))
            {
                error = "supervise --bound must be between 1 and 86400 seconds.";
                return false;
            }

            if (options.DelegationExecutionWindowSeconds is not null
                && (options.DelegationExecutionWindowSeconds < 1
                    || options.DelegationExecutionWindowSeconds > NotifyMeasuredSupervisor.MaximumDelegationExecutionWindowSeconds))
            {
                error = $"supervise --delegation-execution-window-seconds must be between 1 and {NotifyMeasuredSupervisor.MaximumDelegationExecutionWindowSeconds} seconds.";
                return false;
            }

            if (options.RepeatBackoffSeconds is not null
                && (options.RepeatBackoffSeconds < 1 || options.RepeatBackoffSeconds > 86_400))
            {
                error = "supervise --repeat-backoff-seconds must be between 1 and 86400 seconds.";
                return false;
            }

            if (options.DebounceConsecutiveObservations is not null
                && (options.DebounceConsecutiveObservations < 1
                    || options.DebounceConsecutiveObservations > NotifySupervisionEmissionPolicy.MaximumDebounceConsecutiveObservations))
            {
                error = $"supervise --debounce-consecutive-observations must be between 1 and {NotifySupervisionEmissionPolicy.MaximumDebounceConsecutiveObservations}.";
                return false;
            }

            if (options.OwnerRole is not null && !IsSafeIdentity(options.OwnerRole))
            {
                error = "supervise --owner-role must be a safe logical-role name.";
                return false;
            }

            if (options.HerdrExecutable is not null && !Path.IsPathRooted(options.HerdrExecutable)
                || options.BashExecutable is not null && !Path.IsPathRooted(options.BashExecutable))
            {
                error = "supervise executable overrides must be absolute paths.";
                return false;
            }

            if (options.Repo is not null && !IsSafeRepository(options.Repo))
            {
                error = "supervise --repo must be an owner/repo value without path syntax.";
                return false;
            }

            if (options.StaleMinutes is < 0
                || options.ClaimedSilentMinutes is < 0
                || options.BacklogIdleMinutes is < 0
                || options.RepairSilentMinutes is < 0)
            {
                error = "supervise minute thresholds cannot be negative.";
                return false;
            }

            if (options.EventMode && options.Once)
            {
                error = "supervise --event-mode is continuous and cannot be combined with --once.";
                return false;
            }

            if ((options.PreApprovalAcceptRules.Count == 0) != (options.PreApprovalEscalateRules.Count == 0))
            {
                error = "A recorded pre-approval policy requires at least one --pre-approve and one --pre-escalate rule; without both, omit them and remain escalate-only.";
                return false;
            }
            if (!NotifyPreApprovalPolicyStore.TryValidateNoOverlap(
                options.PreApprovalAcceptRules,
                options.PreApprovalEscalateRules,
                out error))
            {
                return false;
            }
            if (!NotifyPreApprovalPolicyStore.TryValidateScopedPolicies(
                options.ScopedPolicies,
                out error,
                requireScratchLedgerCycleId: false))
            {
                return false;
            }
        }
        else if (string.Equals(operation, OperationDelegate, StringComparison.Ordinal))
        {
            if (!IsSafeIdentity(options.ToRole) || !IsSafeIdentity(options.ReportToRole))
            {
                error = "--to and --report-to are required safe logical-role names for delegate.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(options.Objective)
                || options.ExpectedArtifacts.Count == 0
                || !IsSafeIdentity(options.ResultNonce))
            {
                error = "delegate requires --objective, at least one --expected-artifact, and a safe --result-nonce.";
                return false;
            }
        }
        else if (string.Equals(operation, OperationReport, StringComparison.Ordinal))
        {
            if (!IsSafeIdentity(options.ToRole)
                || options.Status is null
                || !ReportReaderEventKinds.ContainsKey(options.Status)
                || string.IsNullOrWhiteSpace(options.Artifact)
                || string.IsNullOrWhiteSpace(options.Summary))
            {
                error = "report requires --to, --status completed|blocked|question, --artifact, and --summary.";
                return false;
            }
        }
        else if (string.Equals(operation, OperationCollect, StringComparison.Ordinal))
        {
            if ((options.TaskId is null) == (options.Role is null))
            {
                error = "collect requires exactly one of --task-id or --role.";
                return false;
            }

            if (options.FromRole is not null || options.ToRole is not null || options.Status is not null
                || options.Artifact is not null || options.Summary is not null)
            {
                error = options.Role is not null
                    ? "role-scoped collect accepts only domain, team, role, since, wait, timeout-ms, routing-root, write/dry-run, and format."
                    : "collect reads the persisted outbox entry; it accepts only domain, team, task-id, routing-root, write/dry-run, and format.";
                return false;
            }

            if (options.Role is null
                && (options.Since is not null || options.Wait || options.TimeoutMilliseconds is not null))
            {
                error = "--since, --wait, and --timeout-ms are supported only with role-scoped collect (--role).";
                return false;
            }

            if (options.Role is not null)
            {
                if (options.Wait && options.TimeoutMilliseconds is null)
                {
                    error = "role-scoped collect --wait requires --timeout-ms with a bounded duration.";
                    return false;
                }

                if (!options.Wait && options.TimeoutMilliseconds is not null)
                {
                    error = "role-scoped collect --timeout-ms requires --wait.";
                    return false;
                }

                if (options.TimeoutMilliseconds is not null
                    && (options.TimeoutMilliseconds < 1
                        || options.TimeoutMilliseconds > MaximumRoleCollectTimeoutMilliseconds))
                {
                    error = $"role-scoped collect --timeout-ms must be between 1 and {MaximumRoleCollectTimeoutMilliseconds} milliseconds.";
                    return false;
                }
            }

            if (options.Role is not null && options.TaskId is not null)
            {
                error = "collect requires exactly one of --task-id or --role; do not supply both.";
                return false;
            }
        }
        else if (string.Equals(operation, OperationReconcile, StringComparison.Ordinal))
        {
            // Reconciliation validation is complete above. It deliberately
            // does not resolve or contact a transport; the orchestration seat
            // owns only the two durable host stores.
        }
        else if (string.Equals(operation, OperationDispose, StringComparison.Ordinal))
        {
            if (options.DispositionKind is not (DispositionKindSuperseded or DispositionKindAppliedElsewhere)
                || !IsSafeIdentity(options.Actor)
                || string.IsNullOrWhiteSpace(options.Reason))
            {
                error = "dispose requires --kind superseded|applied-elsewhere, a safe --actor, and --reason.";
                return false;
            }

            if (options.SupersedingTaskId is not null && !IsSafeIdentity(options.SupersedingTaskId))
            {
                error = "--superseding-task-id must be a safe task id.";
                return false;
            }

            if (options.DispositionKind == DispositionKindSuperseded
                && !IsSafeIdentity(options.SupersedingTaskId))
            {
                error = "superseded disposition requires --superseding-task-id.";
                return false;
            }

            if (options.DispositionKind == DispositionKindAppliedElsewhere
                && string.IsNullOrWhiteSpace(options.AppliedOutcomeEvidence))
            {
                error = "applied-elsewhere disposition requires --applied-outcome-evidence.";
                return false;
            }
        }
        else if (string.Equals(operation, OperationStatus, StringComparison.Ordinal))
        {
            // Status validation is complete above; it is intentionally read-only
            // and has no artifact/summary requirement.
        }
        else if (string.IsNullOrWhiteSpace(options.Artifact) || string.IsNullOrWhiteSpace(options.Summary))
        {
            error = "escalate requires --artifact and --summary.";
            return false;
        }

        return true;
    }

    private static bool IsSafeIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private static bool IsSafeRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('/', StringSplitOptions.None);
        return parts.Length == 2
            && IsSafeIdentity(parts[0])
            && IsSafeIdentity(parts[1]);
    }

    private static bool ReadValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static string Usage(string operation) => operation switch
    {
        OperationDelegate => DelegateUsage,
        OperationReport => ReportUsage,
        OperationCollect => CollectUsage,
        OperationReconcile => ReconcileUsage,
        OperationStatus => StatusUsage,
        OperationSupervise => SuperviseUsage,
        OperationDispose => DisposeUsage,
        _ => EscalateUsage,
    };
}

internal sealed record NotifyOptions
{
    public string? Domain { get; init; }
    public string? Team { get; init; }
    public string? FromRole { get; init; }
    public string? ToRole { get; init; }
    public string? ReportToRole { get; init; }
    public string? TaskId { get; init; }
    public string? Role { get; init; }
    public string? Since { get; init; }
    public string? Objective { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public required IReadOnlyList<string> ExpectedArtifacts { get; init; }
    public string? ResultNonce { get; init; }
    public string? Status { get; init; }
    public string? Artifact { get; init; }
    public string? Summary { get; init; }
    public string? DispositionKind { get; init; }
    public string? Actor { get; init; }
    public string? Reason { get; init; }
    public string? SupersedingTaskId { get; init; }
    public string? AppliedOutcomeEvidence { get; init; }
    public string? RoutingRoot { get; init; }
    public string? ReportRoot { get; init; }
    public string? Repo { get; init; }
    public string? OwnerRole { get; init; }
    public string? HerdrExecutable { get; init; }
    public string? BashExecutable { get; init; }
    public int? IntervalSeconds { get; init; }
    public int? DetectionBoundSeconds { get; init; }
    public int? DelegationExecutionWindowSeconds { get; init; }
    public int? RepeatBackoffSeconds { get; init; }
    public int? DebounceConsecutiveObservations { get; init; }
    public int? StaleMinutes { get; init; }
    public int? ClaimedSilentMinutes { get; init; }
    public int? BacklogIdleMinutes { get; init; }
    public int? RepairSilentMinutes { get; init; }
    public int? TimeoutMilliseconds { get; init; }
    public required IReadOnlyList<NotifyPreApprovalRule> PreApprovalAcceptRules { get; init; }
    public required IReadOnlyList<NotifyPreApprovalRule> PreApprovalEscalateRules { get; init; }
    public required IReadOnlyList<NotifyScopedPromptPolicy> ScopedPolicies { get; init; }
    public bool AutoRedispatch { get; init; }
    public bool Once { get; init; }
    public bool EventMode { get; init; }
    public bool Wait { get; init; }
    public bool Write { get; init; }
    public required string Format { get; init; }
}

internal sealed record NotifyResult
{
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("report_root")] public string? ReportRoot { get; init; }
    [JsonPropertyName("report_storage_mode")] public string? ReportStorageMode { get; init; }
    [JsonPropertyName("host_state_sync")] public string? HostStateSync { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("mode")] public required string Mode { get; init; }
    [JsonPropertyName("mode_source")] public string? ModeSource { get; init; }
    [JsonPropertyName("command_mode")] public required string CommandMode { get; init; }
    [JsonPropertyName("from_role")] public required string FromRole { get; init; }
    [JsonPropertyName("to_role")] public string? ToRole { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("artifact")] public string? Artifact { get; init; }
    [JsonPropertyName("delivered")] public required bool Delivered { get; init; }
    [JsonPropertyName("event_appended")] public required bool EventAppended { get; init; }
    [JsonPropertyName("event_path")] public string? EventPath { get; init; }
    [JsonPropertyName("session_layer_preflight")] public SessionLayerPreflightResult? SessionLayerPreflight { get; init; }
    [JsonPropertyName("receiver_state_outcome")] public string? ReceiverStateOutcome { get; init; }
    [JsonPropertyName("working_transition")] public string? WorkingTransition { get; init; }
    [JsonPropertyName("settle_outcome")] public string? SettleOutcome { get; init; }
    [JsonPropertyName("resend_permitted")] public bool? ResendPermitted { get; init; }
    [JsonPropertyName("inline_payload_warning")] public NotifyInlinePayloadWarning? InlinePayloadWarning { get; init; }
    [JsonPropertyName("recipient_warning")] public NotifyRecipientWarning? RecipientWarning { get; init; }
    [JsonPropertyName("advisory")] public string? Advisory { get; init; }
    [JsonPropertyName("warnings")] public IReadOnlyList<string>? Warnings { get; init; }
    [JsonPropertyName("delivery_method")] public string? DeliveryMethod { get; init; }
    [JsonPropertyName("task_file")] public string? TaskFile { get; init; }
    [JsonPropertyName("delivery_pointer")] public string? DeliveryPointer { get; init; }
    [JsonPropertyName("pending_record_path")] public string? PendingRecordPath { get; init; }
    [JsonPropertyName("outbox_entry_path")] public string? OutboxEntryPath { get; init; }
    [JsonPropertyName("delivery_basis")] public string? DeliveryBasis { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("payload")] public string? Payload { get; init; }
    [JsonPropertyName("report_command")] public string? ReportCommand { get; init; }
    [JsonPropertyName("reconciliation_command")] public string? ReconciliationCommand { get; init; }
    [JsonPropertyName("continuation_chain")] public ContinuationChainRecord? ContinuationChain { get; init; }
    [JsonPropertyName("completion_signal_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletionSignalId => ContinuationChain?.CompletionSignalId;
    [JsonPropertyName("continuation_chain_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContinuationChainId => ContinuationChain?.ChainId;
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyRoleCollectResult
{
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("team")] public string? Team { get; init; }
    [JsonPropertyName("role")] public string? Role { get; init; }
    [JsonPropertyName("reader_path")] public string? ReaderPath { get; init; }
    [JsonPropertyName("command_mode")] public required string CommandMode { get; init; }
    [JsonPropertyName("cursor")] public string? Cursor { get; init; }
    [JsonPropertyName("next_cursor")] public required string NextCursor { get; init; }
    [JsonPropertyName("events")] public required IReadOnlyList<NotifyDesignEvent> Events { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("wait")] public bool Wait { get; init; }
    [JsonPropertyName("timed_out")] public bool TimedOut { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyRoleCollectReadResult
{
    public required bool Succeeded { get; init; }
    public IReadOnlyList<NotifyDesignEvent> Events { get; init; } = [];
    public required string NextCursor { get; init; }
    public string? Cause { get; init; }
    public string? Summary { get; init; }

    public static NotifyRoleCollectReadResult Failure(string cause, string summary) => new()
    {
        Succeeded = false,
        NextCursor = string.Empty,
        Cause = cause,
        Summary = summary,
    };
}

internal sealed record NotifyRoleCollectCursor
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("reader_digest")] public string ReaderDigest { get; init; } = string.Empty;
    [JsonPropertyName("byte_offset")] public long ByteOffset { get; init; }
    [JsonPropertyName("complete_line_count")] public int CompleteLineCount { get; init; }
    [JsonPropertyName("prefix_digest")] public string PrefixDigest { get; init; } = string.Empty;
}

internal sealed record NotifyReconciliationResult
{
    [JsonPropertyName("operation")] public string Operation { get; init; } = "reconcile";
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("report_root")] public required string ReportRoot { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("command_mode")] public required string CommandMode { get; init; }
    [JsonPropertyName("report_outbox_path")] public required string ReportOutboxPath { get; init; }
    [JsonPropertyName("report_delivery_state")] public string? ReportDeliveryState { get; init; }
    [JsonPropertyName("pending_record_path")] public string? PendingRecordPath { get; init; }
    [JsonPropertyName("pending_reconciled")] public bool PendingReconciled { get; init; }
    [JsonPropertyName("pending_already_converged")] public bool PendingAlreadyConverged { get; init; }
    [JsonPropertyName("continuation_chain_path")] public string? ContinuationChainPath { get; init; }
    [JsonPropertyName("continuation_reconciled")] public bool ContinuationReconciled { get; init; }
    [JsonPropertyName("continuation_already_converged")] public bool ContinuationAlreadyConverged { get; init; }
    [JsonPropertyName("continuation_chain")] public ContinuationChainRecord? ContinuationChain { get; init; }
    [JsonPropertyName("reconciled")] public bool Reconciled { get; init; }
    [JsonPropertyName("already_converged")] public bool AlreadyConverged { get; init; }
    [JsonPropertyName("would_reconcile")] public bool WouldReconcile { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("recovery_command")] public string? RecoveryCommand { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyDispositionResult
{
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("command_mode")] public required string CommandMode { get; init; }
    [JsonPropertyName("written")] public required bool Written { get; init; }
    [JsonPropertyName("settlement_basis")] public string? SettlementBasis { get; init; }
    [JsonPropertyName("existing_settlement_basis")] public string? ExistingSettlementBasis { get; init; }
    [JsonPropertyName("disposition")] public NotifyPendingDisposition? Disposition { get; init; }
    [JsonPropertyName("pending_record_path")] public string? PendingRecordPath { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }

    public static NotifyDispositionResult Success(
        string routingRoot,
        NotifyOptions options,
        NotifyPendingDisposition disposition,
        bool written,
        string path,
        NotifyPendingDelegation record) => new()
        {
            Operation = "dispose",
            RoutingRoot = routingRoot,
            Domain = options.Domain!,
            Team = options.Team!,
            TaskId = options.TaskId!,
            CommandMode = options.Write ? "write" : "dry-run",
            Written = written,
            SettlementBasis = "disposition",
            Disposition = disposition,
            PendingRecordPath = path,
            Summary = written
                ? $"Task '{record.TaskId}' was settled with disposition '{disposition.Kind}'; the open-delegation expectation ended and no transport was attempted."
                : $"Task '{record.TaskId}' is eligible for disposition '{disposition.Kind}'; dry-run made no durable change and no transport was attempted.",
        };

    public static NotifyDispositionResult Failure(
        string routingRoot,
        NotifyOptions options,
        string cause,
        string summary,
        string? path,
        NotifyPendingDelegation? record = null,
        string? existingSettlementBasis = null) => new()
        {
            Operation = "dispose",
            RoutingRoot = routingRoot,
            Domain = options.Domain!,
            Team = options.Team!,
            TaskId = options.TaskId!,
            CommandMode = options.Write ? "write" : "dry-run",
            Written = false,
            ExistingSettlementBasis = existingSettlementBasis ?? record?.SettlementBasis,
            Disposition = record?.Disposition,
            PendingRecordPath = path,
            Cause = cause,
            Summary = summary,
        };
}

internal sealed record NotifyStatusResult
{
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("team")] public string? Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("recipient_role")] public string? RecipientRole { get; init; }
    [JsonPropertyName("recipient_identity")] public string? RecipientIdentity { get; init; }
    [JsonPropertyName("expected_artifact")] public string? ExpectedArtifact { get; init; }
    [JsonPropertyName("dispatched_at")] public DateTimeOffset? DispatchedAt { get; init; }
    [JsonPropertyName("recipient_running")] public bool? RecipientRunning { get; init; }
    [JsonPropertyName("liveness_state")] public string? LivenessState { get; init; }
    [JsonPropertyName("process_present")] public bool? ProcessPresent { get; init; }
    [JsonPropertyName("agent_session_present")] public bool? AgentSessionPresent { get; init; }
    [JsonPropertyName("resend_permitted")] public bool? ResendPermitted { get; init; }
    [JsonPropertyName("liveness_source")] public string? LivenessSource { get; init; }
    [JsonPropertyName("delivery_basis")] public string? DeliveryBasis { get; init; }
    [JsonPropertyName("agent_status")] public string? AgentStatus { get; init; }
    [JsonPropertyName("state_change_seq")] public long? StateChangeSequence { get; init; }
    [JsonPropertyName("last_state_change_at")] public DateTimeOffset? LastStateChangeAt { get; init; }
    [JsonPropertyName("activity_verdict")] public string? ActivityVerdict { get; init; }
    [JsonPropertyName("activity_inputs")] public string? ActivityInputs { get; init; }
    [JsonPropertyName("report_arrived")] public bool? ReportArrived { get; init; }
    [JsonPropertyName("report_status")] public string? ReportStatus { get; init; }
    [JsonPropertyName("report_artifact")] public string? ReportArtifact { get; init; }
    [JsonPropertyName("report_summary")] public string? ReportSummary { get; init; }
    [JsonPropertyName("settlement_basis")] public string? SettlementBasis { get; init; }
    [JsonPropertyName("disposition")] public NotifyPendingDisposition? Disposition { get; init; }
    [JsonPropertyName("late_report_disagreement")] public string? LateReportDisagreement { get; init; }
    [JsonPropertyName("verdict")] public string? Verdict { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }

    public static NotifyStatusResult Failure(
        string routingRoot,
        string? domain,
        string? team,
        string taskId,
        string cause,
        string summary,
        NotifyPendingDelegation? record = null,
        string? livenessSource = null,
        bool? running = null) => new()
        {
            Operation = NotifyCommand.OperationStatus,
            RoutingRoot = routingRoot,
            Domain = record?.Domain ?? domain,
            Team = record?.Team ?? team,
            TaskId = taskId,
            RecipientRole = record?.RecipientRole,
            RecipientIdentity = record?.RecipientIdentity,
            ExpectedArtifact = record?.ExpectedArtifact,
            DispatchedAt = record?.DispatchedAt,
            RecipientRunning = running,
            LivenessSource = livenessSource,
            ReportArrived = record?.ReportArrived,
            ReportStatus = record?.ReportStatus,
            ReportArtifact = record?.ReportArtifact,
            ReportSummary = record?.ReportSummary,
            SettlementBasis = record?.SettlementBasis,
            Disposition = record?.Disposition,
            LateReportDisagreement = record is { ReportArrived: true, Disposition: not null }
                ? $"late report arrived after disposition '{record.Disposition.Kind}'"
                : null,
            Verdict = null,
            Cause = cause,
            Summary = summary,
        };
}

internal sealed record NotifyInlinePayloadWarning
{
    [JsonPropertyName("profile")] public required string Profile { get; init; }
    [JsonPropertyName("payload_chars")] public required int PayloadChars { get; init; }
    [JsonPropertyName("threshold_chars")] public required int ThresholdChars { get; init; }
    [JsonPropertyName("remedy")] public required string Remedy { get; init; }
}

internal static class NotifyEventWriter
{
    public const string EventsDirectoryRelativePath = ".intent-cli/events";

    public static bool TryValidateTeam(string team, out string error)
    {
        if (string.IsNullOrWhiteSpace(team)
            || team.StartsWith(".", StringComparison.Ordinal)
            || team.Contains('/')
            || team.Contains('\\')
            || team.Contains("..", StringComparison.Ordinal))
        {
            error = "Team name must be non-empty and must not start with '.', contain path separators, or contain '..'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string RelativePathFor(string domain, string team) =>
        $"{EventsDirectoryRelativePath}/{domain}/{team}.jsonl";

    public static string LegacyRelativePathFor(string team) =>
        $"{EventsDirectoryRelativePath}/{team}.jsonl";

    public static bool TryResolveWritePath(
        string repoRoot,
        string domain,
        string team,
        out string path,
        out string error)
    {
        if (!TryValidateSegment(domain, "Domain", out error)
            || !TryValidateTeam(team, out error))
        {
            path = string.Empty;
            return false;
        }

        path = ResolveRelative(repoRoot, RelativePathFor(domain, team));
        return true;
    }

    public static bool TryResolveRecordedWritePath(
        string repoRoot,
        string domain,
        string team,
        string recordedReaderPath,
        out string path,
        out string error)
    {
        if (!TryResolveWritePath(repoRoot, domain, team, out var scopedPath, out error))
        {
            path = string.Empty;
            return false;
        }

        var legacyPath = ResolveRelative(repoRoot, LegacyRelativePathFor(team));
        path = IsSamePath(recordedReaderPath, scopedPath) || IsSamePath(recordedReaderPath, legacyPath)
            ? scopedPath
            : recordedReaderPath;
        error = string.Empty;
        return true;
    }

    public static bool TryResolveReadPath(
        string repoRoot,
        string domain,
        string team,
        string? recordedReaderPath,
        out string path,
        out string error)
    {
        if (!TryResolveWritePath(repoRoot, domain, team, out var scopedPath, out error))
        {
            path = string.Empty;
            return false;
        }

        var legacyPath = ResolveRelative(repoRoot, LegacyRelativePathFor(team));
        if (recordedReaderPath is not null
            && !IsSamePath(recordedReaderPath, scopedPath)
            && !IsSamePath(recordedReaderPath, legacyPath))
        {
            path = recordedReaderPath;
            error = string.Empty;
            return true;
        }

        path = File.Exists(scopedPath) ? scopedPath : legacyPath;
        error = string.Empty;
        return true;
    }

    private static bool TryValidateSegment(string value, string label, out string error)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith(".", StringComparison.Ordinal)
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains("..", StringComparison.Ordinal))
        {
            error = $"{label} name must be non-empty and must not start with '.', contain path separators, or contain '..'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string ResolveRelative(string repoRoot, string relativePath) =>
        Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static bool IsSamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static void Append(string path, NotifyDesignEvent designEvent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(designEvent);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(line);
        writer.Write('\n');
    }

    public static string NormalizeSummary(string summary) =>
        string.Join(' ', summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

internal sealed record NotifyDesignEvent
{
    [JsonPropertyName("timestamp")] public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("unit")] public required string Unit { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("artifact")] public required string Artifact { get; init; }
}
