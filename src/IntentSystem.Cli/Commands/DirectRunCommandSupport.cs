using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal enum DirectRunEntryKind
{
    Implement,
    Fix,
    Review
}

internal sealed record DirectRunResolvedPolicy
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string Transport { get; init; }

    public required string Command { get; init; }

    public required IReadOnlyList<string> ArgsTemplate { get; init; }
}

internal static class DirectRunCommandSupport
{
    private const string LifecycleEventActor = "intent-cli";
    private const string LifecycleEventName = "provider-lifecycle";
    private const string ReviewCompletionWaitWindowEnvVar = "INTENT_DIRECT_RUN_REVIEW_COMPLETION_WAIT_MS";
    private const string ReviewSessionMaxWaitWindowEnvVar = "INTENT_DIRECT_RUN_REVIEW_SESSION_MAX_WAIT_MS";
    private static readonly TimeSpan FixBoundaryObservationWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FixBoundaryPostTerminationWaitWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultReviewCompletionWaitWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultReviewSessionMaxWaitWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReviewCompletionPollInterval = TimeSpan.FromMilliseconds(250);

    public static DirectRunLaunchResult CreateAndLaunch(
        CliContext context,
        DirectRunEntryKind entryKind,
        string executionUnit,
        string upstreamRequestRef,
        string workingDirectory,
        IDirectRunLauncher launcher,
        DateTimeOffset launchedAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(upstreamRequestRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(launcher);

        var policy = ResolvePolicy(context, entryKind);
        var entryKindValue = FormatEntryKind(entryKind);
        var relativeArtifactPath = ResolveArtifactPath(context, executionUnit);
        var relativeProviderEventLogPath = ResolveProviderEventLogPath(context, executionUnit);
        var relativeCapturedMessagePath = ResolveCapturedMessagePath(context, executionUnit, launchedAt);
        var relativeReviewOutputSchemaPath = ResolveReviewOutputSchemaPath(context, executionUnit, launchedAt);
        var absoluteArtifactPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar)));
        var absoluteProviderEventLogPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeProviderEventLogPath.Replace('/', Path.DirectorySeparatorChar)));
        var absoluteCapturedMessagePath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeCapturedMessagePath.Replace('/', Path.DirectorySeparatorChar)));
        var absoluteReviewOutputSchemaPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeReviewOutputSchemaPath.Replace('/', Path.DirectorySeparatorChar)));
        var absoluteUpstreamRequestPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, upstreamRequestRef.Replace('/', Path.DirectorySeparatorChar)));
        if (entryKind == DirectRunEntryKind.Review && File.Exists(absoluteCapturedMessagePath))
        {
            File.Delete(absoluteCapturedMessagePath);
        }

        if (entryKind == DirectRunEntryKind.Review)
        {
            PersistReviewOutputSchema(absoluteReviewOutputSchemaPath);
        }

        var launchResult = launcher.Launch(
            executionUnit,
            entryKindValue,
            relativeArtifactPath,
            relativeProviderEventLogPath,
            policy.Provider,
            policy.Model,
            policy.Transport,
            policy.Command,
            policy.ArgsTemplate,
            launchedAt,
            workingDirectory,
            absoluteUpstreamRequestPath,
            absoluteProviderEventLogPath);

        var artifact = new DirectRunRequestArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = executionUnit,
            EntryKind = entryKindValue,
            UpstreamRequestRef = upstreamRequestRef,
            Provider = launchResult.Provider,
            Model = launchResult.Model,
            Transport = launchResult.Transport,
            LaunchedAt = launchedAt.ToString("O"),
            ProviderSessionId = launchResult.ProviderSessionId,
            TransportSummary = launchResult.TransportSummary
        };

        var directoryPath = Path.GetDirectoryName(absoluteArtifactPath)
            ?? throw new InvalidOperationException("Direct run request artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absoluteArtifactPath, DirectRunRequestArtifactJson.Serialize(artifact));

        if (ShouldAwaitRealReviewCompletion(entryKind, launcher, policy.Command)
            && !WaitForReviewCompletionBoundary(
                absoluteProviderEventLogPath,
                absoluteCapturedMessagePath,
                launchResult.ProviderSessionId,
                launchedAt))
        {
            ClassifyMissingReviewCompletionBoundary(
                absoluteProviderEventLogPath,
                executionUnit,
                entryKindValue,
                launchResult.Provider,
                launchResult.ProviderSessionId);
        }

        if (ShouldAwaitFixBoundary(entryKind, launcher, policy.Command))
        {
            AwaitFixBoundary(
                absoluteProviderEventLogPath,
                executionUnit,
                entryKindValue,
                launchResult.Provider,
                launchResult.ProviderSessionId,
                launchedAt);
        }

        var synthesis = SynthesizeAndPersistResult(
            context,
            executionUnit,
            entryKindValue,
            upstreamRequestRef,
            launchedAt,
            launchResult);
        var effectiveRunStatus = DirectRunTerminalArtifactUpdater.FinalizeDeadFixSessionIfCurrent(
            absoluteProviderEventLogPath,
            executionUnit,
            entryKindValue,
            launchResult.Provider,
            launchResult.ProviderSessionId,
            launchedAt,
            synthesis.RunStatus);
        StartDeferredFixExitMonitorIfNeeded(
            absoluteProviderEventLogPath,
            executionUnit,
            entryKindValue,
            launchResult.Provider,
            launchResult.ProviderSessionId,
            launchedAt,
            effectiveRunStatus);

        return launchResult with
        {
            ResultArtifactPath = synthesis.ResultArtifactPath,
            RunStatus = effectiveRunStatus
        };
    }

    private static bool ShouldAwaitRealReviewCompletion(
        DirectRunEntryKind entryKind,
        IDirectRunLauncher launcher,
        string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return entryKind == DirectRunEntryKind.Review
            && launcher is DirectRunLauncher
            && IsCodexLikeCommand(command);
    }

    private static bool ShouldAwaitFixBoundary(
        DirectRunEntryKind entryKind,
        IDirectRunLauncher launcher,
        string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return entryKind == DirectRunEntryKind.Fix
            && launcher is DirectRunLauncher
            && IsCodexLikeCommand(command);
    }

    private static bool WaitForReviewCompletionBoundary(
        string providerEventLogPath,
        string capturedMessagePath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        var sessionDeadline = DateTimeOffset.UtcNow + ResolveReviewSessionMaxWaitWindow();
        while (DateTimeOffset.UtcNow < sessionDeadline)
        {
            if (HasExplicitReviewOutcome(
                    providerEventLogPath,
                    capturedMessagePath,
                    providerSessionId,
                    launchedAt))
            {
                return true;
            }

            if (HasReviewSessionTerminated(providerEventLogPath, providerSessionId, launchedAt))
            {
                return WaitForReviewOutcomeAfterSessionTermination(
                    providerEventLogPath,
                    capturedMessagePath,
                    providerSessionId,
                    launchedAt);
            }

            Thread.Sleep(ReviewCompletionPollInterval);
        }

        return false;
    }

    private static TimeSpan ResolveReviewCompletionWaitWindow()
    {
        return ResolvePositiveWaitWindow(
            ReviewCompletionWaitWindowEnvVar,
            DefaultReviewCompletionWaitWindow);
    }

    private static TimeSpan ResolveReviewSessionMaxWaitWindow()
    {
        return ResolvePositiveWaitWindow(
            ReviewSessionMaxWaitWindowEnvVar,
            DefaultReviewSessionMaxWaitWindow);
    }

    private static TimeSpan ResolvePositiveWaitWindow(string environmentVariableName, TimeSpan defaultValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);

        var raw = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(raw)
            && int.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var milliseconds)
            && milliseconds > 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        return defaultValue;
    }

    private static void ClassifyMissingReviewCompletionBoundary(
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId)
    {
        TryTerminateProviderSession(providerSessionId);

        var timestamp = DateTimeOffset.UtcNow;
        var writer = new DirectRunProviderEventWriter(providerEventLogPath);
        writer.Append(new DirectRunProviderEvent
        {
            Timestamp = timestamp.ToString("O"),
            ExecutionUnit = executionUnit,
            Provider = provider,
            EntryKind = entryKind,
            SessionId = providerSessionId,
            Kind = "provider-event",
            Payload = JsonSerializer.SerializeToElement(new
            {
                type = "contract-gap",
                run_status = "failed",
                reason = "review-completion-boundary-timeout"
            })
        });
        writer.Append(DirectRunProviderEventFactory.CreateBackendExitEvent(
            timestamp,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            1));
    }

    private static void AwaitFixBoundary(
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        var deadline = DateTimeOffset.UtcNow + FixBoundaryObservationWindow;
        DateTimeOffset? deepProgressDeadline = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (TryReadCurrentProviderEvents(
                providerEventLogPath,
                providerSessionId,
                launchedAt,
                out var currentProviderEvents)
                && (DirectRunFixOutcomeSupport.HasExplicitContractGapSignal(currentProviderEvents)
                    || TryResolveBackendExitCode(currentProviderEvents, out _)))
            {
                return;
            }

            if (TryReadCurrentProviderEvents(
                    providerEventLogPath,
                    providerSessionId,
                    launchedAt,
                    out currentProviderEvents)
                && DirectRunFixOutcomeSupport.HasDeepExecutionProgressSignal(currentProviderEvents))
            {
                deepProgressDeadline ??= DateTimeOffset.UtcNow + FixBoundaryPostTerminationWaitWindow;
                if (deepProgressDeadline.Value > deadline)
                {
                    deadline = deepProgressDeadline.Value;
                }
            }

            if (!IsProviderSessionAlive(providerSessionId))
            {
                WaitForFixBoundaryAfterSessionTermination(
                    providerEventLogPath,
                    executionUnit,
                    entryKind,
                    provider,
                    providerSessionId,
                    launchedAt);
                return;
            }

            Thread.Sleep(ReviewCompletionPollInterval);
        }
    }

    private static void WaitForFixBoundaryAfterSessionTermination(
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        var deadline = DateTimeOffset.UtcNow + FixBoundaryPostTerminationWaitWindow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (TryReadCurrentProviderEvents(
                providerEventLogPath,
                providerSessionId,
                launchedAt,
                out var currentProviderEvents)
                && (DirectRunFixOutcomeSupport.HasExplicitContractGapSignal(currentProviderEvents)
                    || TryResolveBackendExitCode(currentProviderEvents, out _)))
            {
                return;
            }

            Thread.Sleep(ReviewCompletionPollInterval);
        }

        ClassifyMissingFixBoundary(
            providerEventLogPath,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            launchedAt);
    }

    private static void ClassifyMissingFixBoundary(
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (!TryReadCurrentProviderEvents(
                providerEventLogPath,
                providerSessionId,
                launchedAt,
                out var currentProviderEvents))
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var boundaryEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            currentProviderEvents,
            timestamp,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            providerSessionAlive: false);
        if (boundaryEvent is null)
        {
            return;
        }

        var writer = new DirectRunProviderEventWriter(providerEventLogPath);
        writer.Append(boundaryEvent);
        if (!DirectRunSessionBoundary.HasBackendExitEvent(providerEventLogPath, providerSessionId, launchedAt))
        {
            writer.Append(DirectRunProviderEventFactory.CreateBackendExitEvent(
                timestamp,
                executionUnit,
                entryKind,
                provider,
                providerSessionId,
                1));
        }
    }

    private static void TryTerminateProviderSession(string providerSessionId)
    {
        if (!TryParseSessionProcessId(providerSessionId, out var processId))
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit((int)TimeSpan.FromSeconds(2).TotalMilliseconds);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
        }
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

    private static void StartDeferredFixExitMonitorIfNeeded(
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt,
        string runStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runStatus);

        if (OperatingSystem.IsWindows()
            || !string.Equals(entryKind, "fix", StringComparison.Ordinal)
            || !string.Equals(runStatus, "running", StringComparison.Ordinal)
            || !TryParseSessionProcessId(providerSessionId, out var processId))
        {
            return;
        }

        var monitorStartInfo = DirectRunExitMonitorCommand.CreateDetachedStartInfo(
            processId,
            providerEventLogPath,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            launchedAt);
        StartDeferredFixExitMonitor(monitorStartInfo);
    }

    private static void StartDeferredFixExitMonitor(ProcessStartInfo monitorStartInfo)
    {
        ArgumentNullException.ThrowIfNull(monitorStartInfo);

        var monitor = Process.Start(monitorStartInfo);
        if (monitor is null)
        {
            return;
        }

        using (monitor)
        {
            try
            {
                monitor.StandardInput.Close();
            }
            catch (InvalidOperationException)
            {
            }

            monitor.StandardOutput.Dispose();
            monitor.StandardError.Dispose();
        }
    }

    private static bool WaitForReviewOutcomeAfterSessionTermination(
        string providerEventLogPath,
        string capturedMessagePath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        var deadline = DateTimeOffset.UtcNow + ResolveReviewCompletionWaitWindow();
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (HasExplicitReviewOutcome(
                    providerEventLogPath,
                    capturedMessagePath,
                    providerSessionId,
                    launchedAt))
            {
                return true;
            }

            Thread.Sleep(ReviewCompletionPollInterval);
        }

        return HasExplicitReviewOutcome(
            providerEventLogPath,
            capturedMessagePath,
            providerSessionId,
            launchedAt);
    }

    private static bool HasExplicitReviewOutcome(
        string providerEventLogPath,
        string capturedMessagePath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (!TryReadCurrentProviderEvents(
                providerEventLogPath,
                providerSessionId,
                launchedAt,
                out var currentProviderEvents))
        {
            return false;
        }

        return DirectRunReviewOutcomeSupport.TryResolveExplicitReviewOutcome(currentProviderEvents, out _)
            || DirectRunReviewOutcomeSupport.TryReadReviewOutcomeFromCapturedMessagePath(
                capturedMessagePath,
                out _,
                out _,
                out _);
    }

    private static bool HasReviewSessionTerminated(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (TryReadCurrentProviderEvents(
                providerEventLogPath,
                providerSessionId,
                launchedAt,
                out var currentProviderEvents)
            && TryResolveBackendExitCode(currentProviderEvents, out _))
        {
            return true;
        }

        return !IsProviderSessionAlive(providerSessionId);
    }

    private static bool TryReadCurrentProviderEvents(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt,
        out IReadOnlyList<DirectRunProviderEvent> currentProviderEvents)
    {
        currentProviderEvents = [];
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        try
        {
            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            currentProviderEvents = SelectCurrentSessionEvents(providerEvents, providerSessionId, launchedAt);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or JsonException)
        {
            return false;
        }
    }

    private static bool IsProviderSessionAlive(string providerSessionId)
    {
        if (!TryParseSessionProcessId(providerSessionId, out var processId))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();
            return !process.HasExited;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryResolveBackendExitCode(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        out int exitCode)
    {
        exitCode = default;

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            var providerEvent = providerEvents[index];
            if (!HasBackendExitType(providerEvent))
            {
                continue;
            }

            if (TryReadInt32(providerEvent.Payload, "exit_code", out exitCode)
                || TryReadInt32(providerEvent.Payload, "exitCode", out exitCode))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBackendExitType(DirectRunProviderEvent providerEvent)
    {
        ArgumentNullException.ThrowIfNull(providerEvent);

        return providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal);
    }

    private static DirectRunResolvedPolicy ResolvePolicy(
        CliContext context,
        DirectRunEntryKind entryKind)
    {
        var directRun = context.Config.DirectRun;
        var entryConfig = entryKind switch
        {
            DirectRunEntryKind.Implement => directRun.Implement,
            DirectRunEntryKind.Fix => directRun.Fix,
            DirectRunEntryKind.Review => directRun.Review,
            _ => throw new InvalidOperationException($"Unsupported direct run entry kind '{entryKind}'.")
        };

        var fallbackProvider = entryKind switch
        {
            DirectRunEntryKind.Implement => context.Config.Roles.Implement,
            DirectRunEntryKind.Fix => context.Config.Roles.Implement,
            DirectRunEntryKind.Review => context.Config.Roles.Review,
            _ => throw new InvalidOperationException($"Unsupported direct run entry kind '{entryKind}'.")
        };

        var provider = FirstNonEmpty(entryConfig.Provider, directRun.Provider, fallbackProvider);
        var model = ResolveModel(
            provider,
            FirstNonEmpty(entryConfig.Model, directRun.Model, CliRuntimeContracts.DefaultDirectRunModel));
        var transport = FirstNonEmpty(
            entryConfig.Transport,
            directRun.Transport,
            CliRuntimeContracts.DefaultDirectRunTransport);
        var command = FirstNonEmpty(entryConfig.Command, directRun.Command, ResolveDefaultCommand(provider));
        var argsTemplate = FirstNonEmptyList(
            entryConfig.Args,
            directRun.Args,
            ResolveDefaultArgsTemplate(provider, entryKind));

        return new DirectRunResolvedPolicy
        {
            Provider = provider,
            Model = model,
            Transport = transport,
            Command = command,
            ArgsTemplate = argsTemplate
        };
    }

    private static string ResolveModel(string provider, string configuredModel)
    {
        if (string.Equals(provider.Trim(), "codex", StringComparison.OrdinalIgnoreCase)
            && string.Equals(configuredModel.Trim(), CliRuntimeContracts.DefaultDirectRunModel, StringComparison.OrdinalIgnoreCase))
        {
            return CliRuntimeContracts.DefaultCodexDirectRunModel;
        }

        return configuredModel;
    }

    private static string ResolveArtifactPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.request.json";
    }

    private static string ResolveProviderEventLogPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.provider.jsonl";
    }

    private static string ResolveResultArtifactPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.result.json";
    }

    internal static string ResolveCapturedMessagePath(
        CliContext context,
        string executionUnit,
        DateTimeOffset launchedAt)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.{CreateCapturedMessageSuffix(launchedAt)}.last-message.json";
    }

    internal static string ResolveReviewOutputSchemaPath(
        CliContext context,
        string executionUnit,
        DateTimeOffset launchedAt)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.{CreateCapturedMessageSuffix(launchedAt)}.review-output-schema.json";
    }

    internal static string CreateCapturedMessageSuffix(DateTimeOffset launchedAt)
    {
        return launchedAt.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void PersistReviewOutputSchema(string absoluteReviewOutputSchemaPath)
    {
        var directoryPath = Path.GetDirectoryName(absoluteReviewOutputSchemaPath)
            ?? throw new InvalidOperationException("Review output schema path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(
            absoluteReviewOutputSchemaPath,
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "disposition": {
                  "type": "string",
                  "enum": [
                    "accepted",
                    "approved",
                    "comment",
                    "commented",
                    "fix-requested",
                    "changes-requested"
                  ]
                },
                "comment_body": {
                  "type": [
                    "string",
                    "null"
                  ],
                  "minLength": 1
                }
              },
              "required": [
                "disposition",
                "comment_body"
              ]
            }
            """);
    }

    private static string FormatEntryKind(DirectRunEntryKind entryKind)
    {
        return entryKind switch
        {
            DirectRunEntryKind.Implement => "implement",
            DirectRunEntryKind.Fix => "fix",
            DirectRunEntryKind.Review => "review",
            _ => throw new InvalidOperationException($"Unsupported direct run entry kind '{entryKind}'.")
        };
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException("Direct run policy resolution must produce a non-empty value.");
    }

    private static IReadOnlyList<string> FirstNonEmptyList(params IReadOnlyList<string>[] values)
    {
        foreach (var value in values)
        {
            if (value.Count > 0)
            {
                return value;
            }
        }

        throw new InvalidOperationException("Direct run policy resolution must produce a non-empty args template.");
    }

    private static string ResolveDefaultCommand(string provider)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "codex" => "codex",
            "claude" => "claude",
            _ => provider
        };
    }

    private static bool IsCodexLikeCommand(string command)
    {
        var fileName = Path.GetFileName(command.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var commandStem = Path.GetFileNameWithoutExtension(fileName);
        return commandStem.StartsWith("codex", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ResolveDefaultArgsTemplate(string provider, DirectRunEntryKind entryKind)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "codex" when entryKind == DirectRunEntryKind.Review => ["exec", "--json", "--model", "{model}", "--output-schema", "{output_schema_path}", "--output-last-message", "{output_last_message_path}", "{prompt}"],
            "codex" => ["exec", "--model", "{model}", "{prompt}"],
            "claude" => ["--print", "--model", "{model}", "--output-format", "json", "{prompt}"],
            _ => ["{prompt}"]
        };
    }

    private static DirectRunSynthesisResult SynthesizeAndPersistResult(
        CliContext context,
        string executionUnit,
        string entryKind,
        string upstreamRequestRef,
        DateTimeOffset launchedAt,
        DirectRunLaunchResult launchResult)
    {
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null)
            ?? throw new InvalidOperationException($"No queue state found at {context.GetQueueStatePath()}");
        var queueItem = queueState.Items.FirstOrDefault(item =>
                            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException(
                            $"Execution unit '{executionUnit}' was not found in queue state for direct run result synthesis.");

        var providerEventLogPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            launchResult.ProviderEventLogPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(providerEventLogPath))
        {
            throw new InvalidOperationException(
                $"Provider raw event log was not found at {providerEventLogPath}");
        }

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        if (providerEvents.Count == 0)
        {
            throw new InvalidOperationException(
                $"Provider raw event log at {providerEventLogPath} did not contain any events.");
        }

        var currentProviderEvents = SelectCurrentSessionEvents(providerEvents, launchResult.ProviderSessionId, launchedAt);

        var runLogPath = context.GetRunLogPath();
        var existingRunEvents = File.Exists(runLogPath)
            ? RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath))
            : [];
        var latestLinkedPr = LatestLinkedPrResolver.TryResolve(existingRunEvents, executionUnit);
        var model = ResolveModel(currentProviderEvents, launchResult.Model);
        var sessionId = ResolveSessionId(currentProviderEvents, launchResult.ProviderSessionId);
        var provider = ResolveProvider(currentProviderEvents, launchResult.Provider);
        var providerSessionAlive = IsProviderSessionAlive(sessionId);
        var fixContractGapEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            currentProviderEvents,
            DateTimeOffset.UtcNow,
            executionUnit,
            entryKind,
            provider,
            sessionId,
            providerSessionAlive);
        if (fixContractGapEvent is not null)
        {
            var writer = new DirectRunProviderEventWriter(providerEventLogPath);
            writer.Append(fixContractGapEvent);
            providerEvents = [.. providerEvents, fixContractGapEvent];
            currentProviderEvents = [.. currentProviderEvents, fixContractGapEvent];
        }

        var runStatus = ResolveRunStatus(currentProviderEvents);
        if (string.Equals(entryKind, "review", StringComparison.Ordinal))
        {
            var capturedMessagePath = Path.GetFullPath(Path.Combine(
                context.RepoRoot,
                ResolveCapturedMessagePath(context, executionUnit, launchedAt).Replace('/', Path.DirectorySeparatorChar)));
            var capturedOutcomeEvent = DirectRunReviewOutcomeSupport.TryCreateReviewOutcomeEventFromCapturedMessage(
                currentProviderEvents,
                capturedMessagePath,
                DateTimeOffset.UtcNow,
                executionUnit,
                entryKind,
                provider,
                sessionId);
            if (capturedOutcomeEvent is not null)
            {
                var writer = new DirectRunProviderEventWriter(providerEventLogPath);
                writer.Append(capturedOutcomeEvent);
                providerEvents = [.. providerEvents, capturedOutcomeEvent];
                currentProviderEvents = [.. currentProviderEvents, capturedOutcomeEvent];
            }
        }

        string? reviewOutcome = null;
        string? reviewCommentBodyPath = null;
        if (string.Equals(entryKind, "review", StringComparison.Ordinal)
            && DirectRunReviewOutcomeSupport.TryResolveCanonicalReviewOutcome(
                runStatus,
                existingReviewOutcome: null,
                currentProviderEvents,
                out var resolvedReviewOutcome))
        {
            reviewOutcome = resolvedReviewOutcome;
            if (DirectRunReviewOutcomeSupport.IsCommentOutcome(reviewOutcome))
            {
                if (!DirectRunReviewOutcomeSupport.TryResolveReviewCommentBodyPath(
                        context,
                        executionUnit,
                        currentProviderEvents,
                        out var resolvedCommentBodyPath))
                {
                    throw new InvalidOperationException(
                        $"Review direct run for '{executionUnit}' requested comment but no deterministic comment body was found.");
                }

                reviewCommentBodyPath = resolvedCommentBodyPath;
            }

            var outcomeEvent = DirectRunReviewOutcomeSupport.CreateCanonicalReviewOutcomeEventIfNeeded(
                currentProviderEvents,
                DateTimeOffset.UtcNow,
                executionUnit,
                entryKind,
                provider,
                sessionId,
                reviewOutcome,
                reviewCommentBodyPath);
            if (outcomeEvent is not null)
            {
                var writer = new DirectRunProviderEventWriter(providerEventLogPath);
                writer.Append(outcomeEvent);
                providerEvents = [.. providerEvents, outcomeEvent];
                currentProviderEvents = [.. currentProviderEvents, outcomeEvent];
            }
        }

        runStatus = string.Equals(entryKind, "review", StringComparison.Ordinal)
            ? DirectRunReviewOutcomeSupport.ResolveEffectiveReviewRunStatus(runStatus, reviewOutcome)
            : runStatus;

        var worktreePath = RunStartCommand.ResolveWorktreePath(context, executionUnit);
        var resultArtifactPath = ResolveResultArtifactPath(context, executionUnit);
        var absoluteResultArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            resultArtifactPath.Replace('/', Path.DirectorySeparatorChar)));

        var artifact = new DirectRunResultArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = executionUnit,
            EntryKind = entryKind,
            UpstreamRequestRef = upstreamRequestRef,
            Provider = provider,
            Model = model,
            SessionId = sessionId,
            RunStatus = runStatus,
            ReviewOutcome = reviewOutcome,
            ReviewCommentBodyPath = reviewCommentBodyPath,
            RawLogRef = launchResult.ProviderEventLogPath,
            PacketRef = queueItem.PacketPaths.Yaml,
            ReviewContextRef = queueItem.PacketPaths.ReviewContext,
            LinkedIssue = queueItem.LinkedIssue is null
                ? null
                : new DirectRunLinkedIssueContext
                {
                    Repo = queueItem.LinkedIssue.Repo,
                    Number = queueItem.LinkedIssue.Number,
                    Url = queueItem.LinkedIssue.Url
                },
            LinkedPr = CreateLinkedPullRequestContext(latestLinkedPr),
            Worktree = new DirectRunWorktreeContext
            {
                Path = worktreePath
            }
        };

        var resultDirectoryPath = Path.GetDirectoryName(absoluteResultArtifactPath)
            ?? throw new InvalidOperationException("Direct run result artifact path did not contain a directory.");
        Directory.CreateDirectory(resultDirectoryPath);
        File.WriteAllText(absoluteResultArtifactPath, DirectRunResultArtifactJson.Serialize(artifact));

        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = launchedAt,
                ExecutionUnit = executionUnit,
                Event = LifecycleEventName,
                By = LifecycleEventActor,
                LinkedIssue = artifact.LinkedIssue?.Url,
                LinkedPr = artifact.LinkedPr?.Url,
                EntryKind = entryKind,
                Provider = provider,
                Model = model,
                SessionId = sessionId,
                RunStatus = runStatus,
                RawLogRef = artifact.RawLogRef,
                ResultRef = resultArtifactPath,
                PacketRef = artifact.PacketRef,
                ReviewContextRef = artifact.ReviewContextRef,
                WorktreePath = artifact.Worktree.Path
            }) + Environment.NewLine);

        return new DirectRunSynthesisResult
        {
            ResultArtifactPath = resultArtifactPath,
            RunStatus = runStatus
        };
    }

    private static IReadOnlyList<DirectRunProviderEvent> SelectCurrentSessionEvents(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string launchedSessionId,
        DateTimeOffset launchedAt)
    {
        return DirectRunSessionBoundary.SelectEvents(providerEvents, launchedSessionId, launchedAt);
    }

    private static string ResolveProvider(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string fallbackProvider)
    {
        foreach (var providerEvent in providerEvents)
        {
            if (!string.IsNullOrWhiteSpace(providerEvent.Provider))
            {
                return providerEvent.Provider;
            }
        }

        return fallbackProvider;
    }

    private static string ResolveSessionId(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string fallbackSessionId)
    {
        foreach (var providerEvent in providerEvents)
        {
            if (!string.IsNullOrWhiteSpace(providerEvent.SessionId))
            {
                return providerEvent.SessionId;
            }
        }

        return fallbackSessionId;
    }

    private static string ResolveModel(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string fallbackModel)
    {
        foreach (var providerEvent in providerEvents)
        {
            if (!string.Equals(providerEvent.Kind, "session-metadata", StringComparison.Ordinal))
            {
                continue;
            }

            if (providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("model", out var modelElement)
                && modelElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var model = modelElement.GetString();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    return model;
                }
            }
        }

        return fallbackModel;
    }

    private static string ResolveRunStatus(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            var payload = providerEvents[index].Payload;
            var runStatus = TryNormalizeRunStatusFromPayload(payload);
            if (!string.IsNullOrWhiteSpace(runStatus))
            {
                return runStatus;
            }
        }

        return "running";
    }

    private static string? TryNormalizeRunStatusFromPayload(System.Text.Json.JsonElement payload)
    {
        if (payload.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        if (TryReadString(payload, "run_status", out var runStatus))
        {
            return NormalizeRunStatus(runStatus);
        }

        if (TryReadString(payload, "status", out var status))
        {
            return NormalizeRunStatus(status);
        }

        if (TryReadString(payload, "disposition", out var disposition))
        {
            return NormalizeRunStatus(disposition);
        }

        if (TryReadInt32(payload, "exit_code", out var exitCode)
            || TryReadInt32(payload, "exitCode", out exitCode))
        {
            return exitCode == 0 ? "succeeded" : "failed";
        }

        return null;
    }

    private static bool TryReadString(
        System.Text.Json.JsonElement payload,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (!payload.TryGetProperty(propertyName, out var element)
            || element.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadInt32(
        System.Text.Json.JsonElement payload,
        string propertyName,
        out int value)
    {
        value = 0;

        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return int.TryParse(
                element.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        return false;
    }

    private static string NormalizeRunStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "success" or "completed" => "succeeded",
            "error" => "failed",
            var normalized when !string.IsNullOrWhiteSpace(normalized) => normalized,
            _ => "running"
        };
    }

    private static DirectRunLinkedPullRequestContext? CreateLinkedPullRequestContext(string? linkedPrUrl)
    {
        if (string.IsNullOrWhiteSpace(linkedPrUrl))
        {
            return null;
        }

        var repo = default(string);
        var number = default(int?);

        if (TryParseGitHubPullRequestUrl(linkedPrUrl, out var parsedRepo, out var parsedNumber))
        {
            repo = parsedRepo;
            number = parsedNumber;
        }

        return new DirectRunLinkedPullRequestContext
        {
            Repo = repo,
            Number = number,
            Url = linkedPrUrl
        };
    }

    private static bool TryParseGitHubPullRequestUrl(
        string pullRequestUrl,
        out string repo,
        out int number)
    {
        repo = string.Empty;
        number = 0;

        if (!Uri.TryCreate(pullRequestUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4
            || !string.Equals(segments[2], "pull", StringComparison.Ordinal)
            || !int.TryParse(segments[3], out number))
        {
            return false;
        }

        repo = $"{segments[0]}/{segments[1]}";
        return true;
    }

    private sealed record DirectRunSynthesisResult
    {
        public required string ResultArtifactPath { get; init; }

        public required string RunStatus { get; init; }
    }
}
