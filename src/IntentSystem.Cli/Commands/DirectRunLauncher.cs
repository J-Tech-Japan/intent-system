using System.Diagnostics;

namespace IntentSystem.Cli.Commands;

internal sealed class DirectRunLauncher : IDirectRunLauncher
{
    private static readonly TimeSpan DefaultEarlyExitWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PersistentExitObservationWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DetachedCaptureSessionIdWaitWindow = TimeSpan.FromSeconds(10);
    private readonly IDirectRunProcessRunner processRunner;

    public DirectRunLauncher()
        : this(new DirectRunProcessRunner())
    {
    }

    internal DirectRunLauncher(IDirectRunProcessRunner processRunner)
    {
        this.processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public DirectRunLaunchResult Launch(
        string executionUnit,
        string entryKind,
        string requestArtifactPath,
        string providerEventLogPath,
        string provider,
        string model,
        string transport,
        string command,
        IReadOnlyList<string> argsTemplate,
        DateTimeOffset launchedAt,
        string workingDirectory,
        string absoluteRequestArtifactPath,
        string absoluteProviderEventLogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestArtifactPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(argsTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteRequestArtifactPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteProviderEventLogPath);

        var arguments = ResolveArguments(
            executionUnit,
            entryKind,
            requestArtifactPath,
            provider,
            model,
            transport,
            launchedAt,
            absoluteRequestArtifactPath,
            argsTemplate);
        var processInvocation = ResolveProcessInvocation(
            executionUnit,
            entryKind,
            provider,
            model,
            transport,
            command,
            arguments,
            launchedAt,
            workingDirectory,
            absoluteProviderEventLogPath,
            processRunner is DirectRunProcessRunner);
        var eventWriter = new DirectRunProviderEventWriter(absoluteProviderEventLogPath);
        var providerSessionId = string.Empty;
        string? detachedLaunchError = null;
        var process = processRunner.Start(
            workingDirectory,
            processInvocation.FileName,
            processInvocation.Arguments,
            processInvocation.InheritStandardInput,
            DefaultEarlyExitWindow,
            processId =>
            {
                if (processInvocation.UsesDetachedCaptureHelper && processInvocation.StartedProcessCarriesProviderSession)
                {
                    providerSessionId = $"pid:{processId}";
                    return;
                }
                if (processInvocation.UsesDetachedCaptureHelper)
                {
                    return;
                }

                providerSessionId = $"pid:{processId}";
                eventWriter.Append(DirectRunProviderEventFactory.CreateSessionMetadataEvent(
                    launchedAt,
                    executionUnit,
                    entryKind,
                    provider,
                    providerSessionId,
                    model,
                    transport,
                    command));
                StartPersistentExitMonitorIfNeeded(
                    processInvocation.RequiresPersistentExitMonitor,
                    processId,
                    absoluteProviderEventLogPath,
                    executionUnit,
                    entryKind,
                    provider,
                    providerSessionId,
                    launchedAt);
            },
            exitCode => AppendBackendExitEventIfMissing(
                    eventWriter,
                    absoluteProviderEventLogPath,
                    executionUnit,
                    entryKind,
                    provider,
                    providerSessionId,
                    exitCode,
                    launchedAt),
            raw =>
            {
                if (processInvocation.UsesDetachedCaptureHelper
                    && TryHandleDetachedCaptureHandshake(
                        raw,
                        eventWriter,
                        launchedAt,
                        executionUnit,
                        entryKind,
                        provider,
                        model,
                        transport,
                        command,
                        ref providerSessionId,
                        ref detachedLaunchError))
                {
                    return;
                }

                eventWriter.Append(DirectRunProviderEventFactory.CreateProviderEvent(
                    DateTimeOffset.UtcNow,
                    executionUnit,
                    entryKind,
                    provider,
                    providerSessionId,
                    raw));
            },
            raw =>
            {
                if (processInvocation.UsesDetachedCaptureHelper && detachedLaunchError is null)
                {
                    detachedLaunchError = raw;
                    return;
                }

                eventWriter.Append(DirectRunProviderEventFactory.CreateProviderEvent(
                    DateTimeOffset.UtcNow,
                    executionUnit,
                    entryKind,
                    provider,
                    providerSessionId,
                    raw));
            });

        BestEffortAppendBackendExitIfProcessExitedSoon(
            processInvocation.RequiresPersistentExitMonitor,
            process.ProcessId,
            eventWriter,
            absoluteProviderEventLogPath,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            launchedAt);

        if (processInvocation.UsesDetachedCaptureHelper)
        {
            providerSessionId = ResolveDetachedProviderSessionId(
                absoluteProviderEventLogPath,
                launchedAt,
                providerSessionId);
            StartDetachedProviderExitMonitorIfNeeded(
                processInvocation.StartedProcessCarriesProviderSession,
                absoluteProviderEventLogPath,
                executionUnit,
                entryKind,
                provider,
                providerSessionId,
                launchedAt);
        }

        if (process.ExitedEarly && process.ExitCode != 0)
        {
            if (processInvocation.UsesDetachedCaptureHelper && !string.IsNullOrWhiteSpace(detachedLaunchError))
            {
                throw new InvalidOperationException(
                    $"Direct run launch failed for provider '{provider}' using command '{command}': {detachedLaunchError}");
            }

            throw new InvalidOperationException(
                $"Direct run launch failed for provider '{provider}' using command '{command}' with exit code {process.ExitCode}.");
        }

        if (processInvocation.UsesDetachedCaptureHelper && string.IsNullOrWhiteSpace(providerSessionId))
        {
            throw new InvalidOperationException(
                $"Direct run launch failed for provider '{provider}' using command '{command}': detached capture helper did not report a provider session.");
        }

        return new DirectRunLaunchResult
        {
            RequestArtifactPath = requestArtifactPath,
            ProviderEventLogPath = providerEventLogPath,
            Provider = provider,
            Model = model,
            Transport = transport,
            ProviderSessionId = providerSessionId,
            TransportSummary =
                $"{transport} transport launched via '{command}' in '{workingDirectory}' for provider '{provider}'."
        };
    }

    private static string ResolveDetachedProviderSessionId(
        string providerEventLogPath,
        DateTimeOffset launchedAt,
        string fallbackSessionId)
    {
        var deadline = DateTimeOffset.UtcNow + DetachedCaptureSessionIdWaitWindow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var resolvedSessionId = TryReadDetachedProviderSessionId(providerEventLogPath, launchedAt);
            if (!string.IsNullOrWhiteSpace(resolvedSessionId))
            {
                return resolvedSessionId;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }

        return fallbackSessionId;
    }

    private static string? TryReadDetachedProviderSessionId(string providerEventLogPath, DateTimeOffset launchedAt)
    {
        if (!File.Exists(providerEventLogPath))
        {
            return null;
        }

        string? resolvedSessionId = null;
        foreach (var line in File.ReadLines(providerEventLogPath))
        {
            DirectRunProviderEvent providerEvent;
            try
            {
                providerEvent = DirectRunProviderEventJsonl.DeserializeLine(line);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or ArgumentException
                or System.Text.Json.JsonException)
            {
                continue;
            }

            if (!string.Equals(providerEvent.Kind, "session-metadata", StringComparison.Ordinal)
                || !DateTimeOffset.TryParse(
                    providerEvent.Timestamp,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var providerEventTimestamp)
                || providerEventTimestamp < launchedAt)
            {
                continue;
            }

            resolvedSessionId = providerEvent.SessionId;
        }

        return resolvedSessionId;
    }

    private static IReadOnlyList<string> ResolveArguments(
        string executionUnit,
        string entryKind,
        string requestArtifactPath,
        string provider,
        string model,
        string transport,
        DateTimeOffset launchedAt,
        string absoluteRequestArtifactPath,
        IReadOnlyList<string> argsTemplate)
    {
        var prompt = CreatePrompt(entryKind, absoluteRequestArtifactPath);
        var outputLastMessagePath = ResolveOutputLastMessagePath(requestArtifactPath, executionUnit, launchedAt);

        return argsTemplate
            .Select(argument => argument
                .Replace("{execution_unit}", executionUnit, StringComparison.Ordinal)
                .Replace("{entry_kind}", entryKind, StringComparison.Ordinal)
                .Replace("{provider}", provider, StringComparison.Ordinal)
                .Replace("{model}", model, StringComparison.Ordinal)
                .Replace("{transport}", transport, StringComparison.Ordinal)
                .Replace("{request_artifact_path}", absoluteRequestArtifactPath, StringComparison.Ordinal)
                .Replace("{upstream_request_artifact_path}", absoluteRequestArtifactPath, StringComparison.Ordinal)
                .Replace("{direct_run_artifact_path}", requestArtifactPath, StringComparison.Ordinal)
                .Replace("{output_schema_path}", ResolveOutputSchemaPath(requestArtifactPath, executionUnit, launchedAt), StringComparison.Ordinal)
                .Replace("{output_last_message_path}", outputLastMessagePath, StringComparison.Ordinal)
                .Replace("{prompt}", prompt, StringComparison.Ordinal))
            .ToArray();
    }

    private static string CreatePrompt(string entryKind, string absoluteRequestArtifactPath)
    {
        var prompt =
            $"Use the request artifact at '{absoluteRequestArtifactPath}' as the bounded source of truth for this direct run.";
        if (string.Equals(entryKind, "fix", StringComparison.Ordinal))
        {
            return prompt
                + " Continue beyond initial repository inspection and either complete the bounded repair attempt from that artifact"
                + " or end with a deterministic refusal or contract-gap explanation."
                + " Do not stop after a single inspection command without producing one of those outcomes.";
        }

        if (!string.Equals(entryKind, "review", StringComparison.Ordinal))
        {
            return prompt;
        }

        return prompt
            + " Your final response must be a single JSON object with a required string field 'disposition'"
            + " and a required field 'comment_body' that must be a string when a review comment is required or null when no comment is required."
            + " Use 'accepted' or 'approved' only when no review comment is required."
            + " Use 'comment', 'commented', 'fix-requested', or 'changes-requested' only when a deterministic review comment is required."
            + " Do not post GitHub or pull request comments, do not run 'gh pr comment', and do not publish or mutate any external review state from this direct review run."
            + " Persist only the review outcome JSON for downstream handling because the separate 'review comment' step owns PR comment publication."
            + " Do not return wrapper fields such as 'stop_reason', 'actions', or execution envelopes instead of 'disposition'."
            + " If you detect a deterministic contract gap or need follow-up work, still return 'disposition':'fix-requested' with an actionable 'comment_body'."
            + " For accepted or approved outcomes, return 'comment_body': null."
            + " Do not wrap the JSON in markdown fences.";
    }

    private static string ResolveOutputLastMessagePath(
        string requestArtifactPath,
        string executionUnit,
        DateTimeOffset launchedAt)
    {
        var normalizedPath = requestArtifactPath.Replace('\\', '/');
        var directory = Path.GetDirectoryName(normalizedPath.Replace('/', Path.DirectorySeparatorChar))
            ?.Replace(Path.DirectorySeparatorChar, '/')
            ?.TrimEnd('/')
            ?? ".";
        return $"{directory}/{executionUnit.Trim()}.{DirectRunCommandSupport.CreateCapturedMessageSuffix(launchedAt)}.last-message.json";
    }

    private static string ResolveOutputSchemaPath(
        string requestArtifactPath,
        string executionUnit,
        DateTimeOffset launchedAt)
    {
        var normalizedPath = requestArtifactPath.Replace('\\', '/');
        var directory = Path.GetDirectoryName(normalizedPath.Replace('/', Path.DirectorySeparatorChar))
            ?.Replace(Path.DirectorySeparatorChar, '/')
            ?.TrimEnd('/')
            ?? ".";
        return $"{directory}/{executionUnit.Trim()}.{DirectRunCommandSupport.CreateCapturedMessageSuffix(launchedAt)}.review-output-schema.json";
    }

    private static ResolvedProcessInvocation ResolveProcessInvocation(
        string executionUnit,
        string entryKind,
        string provider,
        string model,
        string transport,
        string command,
        IReadOnlyList<string> arguments,
        DateTimeOffset launchedAt,
        string workingDirectory,
        string absoluteProviderEventLogPath,
        bool detachHelperFromLauncherSession)
    {
        if (!ShouldShellWrapForPersistentExitLogging(provider, command))
        {
            return new ResolvedProcessInvocation
            {
                FileName = command,
                Arguments = arguments,
                RequiresPersistentExitMonitor = false,
                InheritStandardInput = false,
                UsesDetachedCaptureHelper = false,
                StartedProcessCarriesProviderSession = true
            };
        }

        var helperStartInfo = DirectRunDetachedCaptureCommand.CreateStartInfo(
            absoluteProviderEventLogPath,
            executionUnit,
            entryKind,
            provider,
            model,
            transport,
            launchedAt,
            workingDirectory,
            command,
            arguments);

        if (detachHelperFromLauncherSession
            && !OperatingSystem.IsWindows()
            && !string.Equals(entryKind, "review", StringComparison.Ordinal))
        {
            var detachedLauncherStartInfo = CreateDetachedHelperLauncherStartInfo(helperStartInfo);
            return new ResolvedProcessInvocation
            {
                FileName = detachedLauncherStartInfo.FileName,
                Arguments = detachedLauncherStartInfo.ArgumentList.ToArray(),
                RequiresPersistentExitMonitor = false,
                InheritStandardInput = false,
                UsesDetachedCaptureHelper = true,
                StartedProcessCarriesProviderSession = false
            };
        }

        return new ResolvedProcessInvocation
        {
            FileName = helperStartInfo.FileName,
            Arguments = helperStartInfo.ArgumentList.ToArray(),
            RequiresPersistentExitMonitor = false,
            InheritStandardInput = false,
            UsesDetachedCaptureHelper = true,
            StartedProcessCarriesProviderSession = true
        };
    }

    private static ProcessStartInfo CreateDetachedHelperLauncherStartInfo(ProcessStartInfo helperStartInfo)
    {
        var launcherStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        launcherStartInfo.ArgumentList.Add("-c");
        launcherStartInfo.ArgumentList.Add(
            """
            if command -v nohup >/dev/null 2>&1; then
                nohup "$@" >/dev/null 2>&1 </dev/null &
            else
                "$@" >/dev/null 2>&1 </dev/null &
            fi
            """);
        launcherStartInfo.ArgumentList.Add("direct-run-detached-capture-launcher");
        launcherStartInfo.ArgumentList.Add(helperStartInfo.FileName);
        foreach (var argument in helperStartInfo.ArgumentList)
        {
            launcherStartInfo.ArgumentList.Add(argument);
        }

        return launcherStartInfo;
    }

    private static bool ShouldShellWrapForPersistentExitLogging(string provider, string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        return string.Equals(provider, "codex", StringComparison.OrdinalIgnoreCase)
            || IsCodexLikeCommand(command);
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

    private static void AppendBackendExitEventIfMissing(
        DirectRunProviderEventWriter eventWriter,
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        int exitCode,
        DateTimeOffset launchedAt)
    {
        ArgumentNullException.ThrowIfNull(eventWriter);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);

        if (string.IsNullOrWhiteSpace(providerSessionId)
            || DirectRunSessionBoundary.HasBackendExitEvent(providerEventLogPath, providerSessionId, launchedAt))
        {
            if (!string.IsNullOrWhiteSpace(providerSessionId))
            {
                DirectRunTerminalArtifactUpdater.PersistTerminalRunStatusIfCurrent(
                    providerEventLogPath,
                    providerSessionId,
                    launchedAt,
                    exitCode);
            }

            return;
        }

        eventWriter.Append(DirectRunProviderEventFactory.CreateBackendExitEvent(
            DateTimeOffset.UtcNow,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            exitCode));
        DirectRunTerminalArtifactUpdater.PersistTerminalRunStatusIfCurrent(
            providerEventLogPath,
            providerSessionId,
            launchedAt,
            exitCode);
    }

    private static void StartPersistentExitMonitorIfNeeded(
        bool requiresPersistentExitMonitor,
        int processId,
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (!requiresPersistentExitMonitor || OperatingSystem.IsWindows())
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

        var launcherStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        launcherStartInfo.ArgumentList.Add("-c");
        launcherStartInfo.ArgumentList.Add(
            """
            if command -v nohup >/dev/null 2>&1; then
                nohup "$@" >/dev/null 2>&1 </dev/null &
            else
                "$@" >/dev/null 2>&1 </dev/null &
            fi
            """);
        launcherStartInfo.ArgumentList.Add("direct-run-exit-monitor-launcher");
        launcherStartInfo.ArgumentList.Add(monitorStartInfo.FileName);
        foreach (var argument in monitorStartInfo.ArgumentList)
        {
            launcherStartInfo.ArgumentList.Add(argument);
        }

        using var launcher = Process.Start(launcherStartInfo);
    }

    private static void StartDetachedProviderExitMonitorIfNeeded(
        bool startedProcessCarriesProviderSession,
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (startedProcessCarriesProviderSession
            || !TryParseProcessId(providerSessionId, out var processId))
        {
            return;
        }

        StartPersistentExitMonitorIfNeeded(
            requiresPersistentExitMonitor: true,
            processId,
            providerEventLogPath,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            launchedAt);
    }

    private static void BestEffortAppendBackendExitIfProcessExitedSoon(
        bool requiresPersistentExitMonitor,
        int processId,
        DirectRunProviderEventWriter eventWriter,
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (!requiresPersistentExitMonitor
            || OperatingSystem.IsWindows()
            || processId <= 0
            || string.IsNullOrWhiteSpace(providerSessionId)
            || DirectRunSessionBoundary.HasBackendExitEvent(providerEventLogPath, providerSessionId, launchedAt))
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow + PersistentExitObservationWindow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (DirectRunSessionBoundary.HasBackendExitEvent(providerEventLogPath, providerSessionId, launchedAt))
            {
                return;
            }

            if (!IsProcessAlive(processId))
            {
                AppendBackendExitEventIfMissing(
                    eventWriter,
                    providerEventLogPath,
                    executionUnit,
                    entryKind,
                    provider,
                    providerSessionId,
                    1,
                    launchedAt);
                return;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
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

    private static bool TryParseProcessId(string providerSessionId, out int processId)
    {
        processId = default;
        const string prefix = "pid:";
        return providerSessionId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(providerSessionId[prefix.Length..], out processId)
            && processId > 0;
    }

    private static bool TryHandleDetachedCaptureHandshake(
        string raw,
        DirectRunProviderEventWriter eventWriter,
        DateTimeOffset launchedAt,
        string executionUnit,
        string entryKind,
        string provider,
        string model,
        string transport,
        string command,
        ref string providerSessionId,
        ref string? detachedLaunchError)
    {
        if (raw.StartsWith("pid:", StringComparison.Ordinal))
        {
            providerSessionId = raw;
            return true;
        }

        if (raw.StartsWith("error:", StringComparison.Ordinal))
        {
            detachedLaunchError = raw["error:".Length..].Trim();
            return true;
        }

        return false;
    }

    private sealed record ResolvedProcessInvocation
    {
        public required string FileName { get; init; }

        public required IReadOnlyList<string> Arguments { get; init; }

        public required bool RequiresPersistentExitMonitor { get; init; }

        public required bool InheritStandardInput { get; init; }

        public required bool UsesDetachedCaptureHelper { get; init; }

        public required bool StartedProcessCarriesProviderSession { get; init; }
    }
}
