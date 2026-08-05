using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class NotifyCommand
{
    private const string OperationDelegate = "delegate";
    private const string OperationReport = "report";
    private const string OperationEscalate = "escalate";
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

    private const string DelegateUsage =
        "Usage: intent-cli notify delegate --domain <d> --team <t> --from <role> --to <role> --report-to <role> "
        + "--task-id <id> --objective <text> [--input <value>]... --expected-artifact <value> "
        + "[--expected-artifact <value>]... --result-nonce <nonce> [--routing-root <host-root>] "
        + "[--dry-run|--write] [--format markdown|json]";

    private const string ReportUsage =
        "Usage: intent-cli notify report --domain <d> --team <t> --from <role> --to <role> --task-id <id> "
        + "--status completed|blocked|question --artifact <value> --summary <text> "
        + "[--routing-root <host-root>] [--dry-run|--write] [--format markdown|json]";

    private const string EscalateUsage =
        "Usage: intent-cli notify escalate --domain <d> --team <t> --from <role> --task-id <id> "
        + "--artifact <value> --summary <text> [--routing-root <host-root>] "
        + "[--dry-run|--write] [--format markdown|json]";

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

    internal static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    public static int ExecuteDelegate(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationDelegate);

    public static int ExecuteReport(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationReport);

    public static int ExecuteEscalate(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, OperationEscalate);

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
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    resolution.Mode,
                    cause,
                    (primaryFinding is null ? string.Empty : primaryFinding.Message + " ")
                    + scope.Summary + " " + preflight.Summary,
                    modeSource: scope.ModeSource,
                    preflight: preflight));
                return 1;
            }

            return ExecuteDelivery(writer, operation, options, resolution, preflight);
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

    private static int ExecuteDelivery(
        TextWriter writer,
        string operation,
        NotifyOptions options,
        SessionLayerModeResolution resolution,
        SessionLayerPreflightResult preflight)
    {
        var reportCommand = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? BuildReportCommand(options)
            : null;
        var payload = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? BuildDelegatePayload(options, reportCommand!)
            : BuildReportPayload(options);
        var inlinePayloadWarning = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? ResolveInlinePayloadWarning(options, payload)
            : null;
        var envelopeDelivery = string.Equals(operation, OperationDelegate, StringComparison.Ordinal)
            ? NotifyTaskEnvelopeDelivery.Resolve(options, payload)
            : NotifyTaskEnvelopeDelivery.Inline(payload);
        if (!envelopeDelivery.Resolved)
        {
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
                inlinePayloadWarning: inlinePayloadWarning));
            return 1;
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
                HerdrExecutableFactory?.Invoke() ?? NotifyTransportPaths.ResolveHerdrExecutable())
            : new AgmsgNotifyTransport(
                runner,
                AgmsgScriptsDirectoryFactory?.Invoke() ?? NotifyTransportPaths.ResolveAgmsgScriptsDirectory());
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
            options.Write);
        var deliveryPreflight = delivery.ActivePhase is null
            ? preflight
            : SessionLayerPreflight.WithActivePhase(
                preflight,
                delivery.ActivePhase.Status,
                delivery.ActivePhase.ContactedReceiver,
                delivery.ActivePhase.Summary);

        if (!delivery.Resolved)
        {
            Emit(writer, options.Format, FailureResult(
                operation,
                options,
                resolution.Mode,
                delivery.Cause!,
                delivery.Summary,
                payload,
                reportCommand,
                modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                preflight: deliveryPreflight,
                receiverStateOutcome: delivery.ReceiverStateOutcome,
                workingTransition: delivery.WorkingTransition,
                settleOutcome: delivery.SettleOutcome,
                resendPermitted: delivery.ResendPermitted,
                inlinePayloadWarning: inlinePayloadWarning,
                deliveryMethod: envelopeDelivery.ResultDeliveryMethod,
                taskFile: envelopeDelivery.TaskFile,
                deliveryPointer: envelopeDelivery.ResultPointer));
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
                Emit(writer, options.Format, FailureResult(
                    operation,
                    options,
                    resolution.Mode,
                    "event-append-failed",
                    $"Could not append notification to external role '{options.ToRole}' through recorded reader "
                    + $"'{delivery.ReaderPath}': {exception.Message} Fix reader access and retry notify.",
                    payload,
                    reportCommand,
                    modeSource: resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
                    preflight: deliveryPreflight,
                    inlinePayloadWarning: inlinePayloadWarning,
                    deliveryMethod: envelopeDelivery.ResultDeliveryMethod,
                    taskFile: envelopeDelivery.TaskFile,
                    deliveryPointer: envelopeDelivery.ResultPointer));
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

        Emit(writer, options.Format, SuccessResult(
            operation,
            options,
            resolution,
            delivered: delivery.Delivered || eventAppended,
            eventAppended,
            payload,
            reportCommand,
            eventAppended
                ? $"Delivered {operation} to external logical role '{options.ToRole}' in team '{options.Team}' "
                  + $"through recorded reader '{delivery.ReaderPath}'."
                : delivery.Summary,
            eventPath: delivery.ReaderPath,
            preflight: deliveryPreflight,
            receiverStateOutcome: delivery.ReceiverStateOutcome,
            workingTransition: delivery.WorkingTransition,
            settleOutcome: delivery.SettleOutcome,
            resendPermitted: delivery.ResendPermitted,
            inlinePayloadWarning: inlinePayloadWarning,
            deliveryMethod: envelopeDelivery.ResultDeliveryMethod,
            taskFile: envelopeDelivery.TaskFile,
            deliveryPointer: envelopeDelivery.ResultPointer));
        return 0;
    }

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
        if (!NotifyEventWriter.TryResolvePath(options.RoutingRoot!, options.Team!, out var path, out var error))
        {
            Emit(writer, options.Format, FailureResult(
                OperationEscalate,
                options,
                resolution.Mode,
                "invalid-team",
                error));
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
                eventPath: path));
            return 0;
        }

        try
        {
            NotifyEventWriter.Append(path, new NotifyDesignEvent
            {
                Timestamp = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Team = options.Team!,
                Kind = EscalationEventKind,
                Unit = options.TaskId!,
                Summary = NotifyEventWriter.NormalizeSummary(options.Summary!),
                Artifact = options.Artifact!,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Emit(writer, options.Format, FailureResult(
                OperationEscalate,
                options,
                resolution.Mode,
                "event-append-failed",
                $"Could not append the design-boundary event: {exception.Message}"));
            return 1;
        }

        Emit(writer, options.Format, SuccessResult(
            OperationEscalate,
            options,
            resolution,
            delivered: false,
            eventAppended: true,
            payload: null,
            reportCommand: null,
            $"Appended escalation for task '{options.TaskId}' to the design-boundary event channel.",
            eventPath: path));
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

    private static string BuildReportCommand(NotifyOptions options) =>
        $"intent-cli notify report --domain {options.Domain} --team {options.Team} --from {options.ToRole} "
        + $"--to {options.ReportToRole} --task-id {options.TaskId} --status <completed|blocked|question> "
        + $"--artifact <artifact> --summary <one-line-summary> --routing-root {ShellQuote(options.RoutingRoot!)} "
        + "--write --format json";

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
        if (!topology.Resolved
            || topology.Topology is null
            || !topology.Topology.Roles.TryGetValue(options.ToRole!, out var recipient)
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
        string? deliveryMethod = null,
        string? taskFile = null,
        string? deliveryPointer = null) => new()
        {
            Operation = operation,
            RoutingRoot = options.RoutingRoot!,
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
            DeliveryMethod = deliveryMethod,
            TaskFile = taskFile,
            DeliveryPointer = deliveryPointer,
            Cause = null,
            Payload = payload,
            ReportCommand = reportCommand,
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
        string? deliveryMethod = null,
        string? taskFile = null,
        string? deliveryPointer = null) => new()
        {
            Operation = operation,
            RoutingRoot = options.RoutingRoot ?? string.Empty,
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
            DeliveryMethod = deliveryMethod,
            TaskFile = taskFile,
            DeliveryPointer = deliveryPointer,
            Cause = cause,
            Payload = payload,
            ReportCommand = reportCommand,
            Summary = summary,
        };

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
        writer.WriteLine($"- delivered: {result.Delivered.ToString().ToLowerInvariant()}");
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
        if (result.EventPath is not null)
        {
            writer.WriteLine($"- event path: `{result.EventPath}`");
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
        string? objective = null;
        string? resultNonce = null;
        string? status = null;
        string? artifact = null;
        string? summary = null;
        string? routingRoot = null;
        var inputs = new List<string>();
        var expectedArtifacts = new List<string>();
        var write = false;
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
                case "--objective": if (!ReadValue(args, ref index, argument, out objective, out error)) return false; break;
                case "--result-nonce": if (!ReadValue(args, ref index, argument, out resultNonce, out error)) return false; break;
                case "--status": if (!ReadValue(args, ref index, argument, out status, out error)) return false; break;
                case "--artifact": if (!ReadValue(args, ref index, argument, out artifact, out error)) return false; break;
                case "--summary": if (!ReadValue(args, ref index, argument, out summary, out error)) return false; break;
                case "--routing-root": if (!ReadValue(args, ref index, argument, out routingRoot, out error)) return false; break;
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
            Objective = objective,
            Inputs = inputs,
            ExpectedArtifacts = expectedArtifacts,
            ResultNonce = resultNonce,
            Status = status,
            Artifact = artifact,
            Summary = summary,
            RoutingRoot = routingRoot,
            Write = write,
            Format = format,
        };

        return Validate(operation, options, out error);
    }

    private static bool Validate(string operation, NotifyOptions options, out string error)
    {
        error = string.Empty;
        foreach (var (name, value) in new[]
        {
            ("--domain", options.Domain),
            ("--team", options.Team),
            ("--from", options.FromRole),
            ("--task-id", options.TaskId),
        })
        {
            if (!IsSafeIdentity(value))
            {
                error = $"{name} is required and must contain only letters, digits, '.', '_', ':', or '-' without path syntax.";
                return false;
            }
        }

        if (!NotifyEventWriter.TryValidateTeam(options.Team!, out error))
        {
            return false;
        }

        if (string.Equals(operation, OperationDelegate, StringComparison.Ordinal))
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
    public string? Objective { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public required IReadOnlyList<string> ExpectedArtifacts { get; init; }
    public string? ResultNonce { get; init; }
    public string? Status { get; init; }
    public string? Artifact { get; init; }
    public string? Summary { get; init; }
    public string? RoutingRoot { get; init; }
    public bool Write { get; init; }
    public required string Format { get; init; }
}

internal sealed record NotifyResult
{
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
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
    [JsonPropertyName("delivery_method")] public string? DeliveryMethod { get; init; }
    [JsonPropertyName("task_file")] public string? TaskFile { get; init; }
    [JsonPropertyName("delivery_pointer")] public string? DeliveryPointer { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("payload")] public string? Payload { get; init; }
    [JsonPropertyName("report_command")] public string? ReportCommand { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
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

    public static bool TryResolvePath(string repoRoot, string team, out string path, out string error)
    {
        if (!TryValidateTeam(team, out error))
        {
            path = string.Empty;
            return false;
        }

        path = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "events", $"{team}.jsonl"));
        return true;
    }

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
