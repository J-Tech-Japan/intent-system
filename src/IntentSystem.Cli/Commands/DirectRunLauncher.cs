using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal sealed class DirectRunLauncher : IDirectRunLauncher
{
    private static readonly TimeSpan DefaultEarlyExitWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PersistentExitObservationWindow = TimeSpan.FromSeconds(3);
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
            absoluteRequestArtifactPath,
            argsTemplate);
        var processInvocation = ResolveProcessInvocation(
            executionUnit,
            entryKind,
            provider,
            command,
            arguments,
            absoluteProviderEventLogPath);
        var eventWriter = new DirectRunProviderEventWriter(absoluteProviderEventLogPath);
        var providerSessionId = string.Empty;
        var process = processRunner.Start(
            workingDirectory,
            processInvocation.FileName,
            processInvocation.Arguments,
            DefaultEarlyExitWindow,
            processId =>
            {
                providerSessionId = $"pid:{processId}";
                eventWriter.Append(CreateSessionMetadataEvent(
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
            raw => eventWriter.Append(CreateProviderEvent(DateTimeOffset.UtcNow, executionUnit, entryKind, provider, providerSessionId, raw)),
            raw => eventWriter.Append(CreateProviderEvent(DateTimeOffset.UtcNow, executionUnit, entryKind, provider, providerSessionId, raw)));

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

        if (process.ExitedEarly && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Direct run launch failed for provider '{provider}' using command '{command}' with exit code {process.ExitCode}.");
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

    private static IReadOnlyList<string> ResolveArguments(
        string executionUnit,
        string entryKind,
        string requestArtifactPath,
        string provider,
        string model,
        string transport,
        string absoluteRequestArtifactPath,
        IReadOnlyList<string> argsTemplate)
    {
        var prompt =
            $"Use the request artifact at '{absoluteRequestArtifactPath}' as the bounded source of truth for this direct run.";

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
                .Replace("{prompt}", prompt, StringComparison.Ordinal))
            .ToArray();
    }

    private static ResolvedProcessInvocation ResolveProcessInvocation(
        string executionUnit,
        string entryKind,
        string provider,
        string command,
        IReadOnlyList<string> arguments,
        string absoluteProviderEventLogPath)
    {
        if (!ShouldShellWrapForPersistentExitLogging(provider, command))
        {
            return new ResolvedProcessInvocation
            {
                FileName = command,
                Arguments = arguments,
                RequiresPersistentExitMonitor = false
            };
        }

        return new ResolvedProcessInvocation
        {
            FileName = "/bin/sh",
            Arguments =
            [
                "-c",
                """
                shift 4
                exec "$@"
                """,
                "direct-run-wrapper",
                absoluteProviderEventLogPath,
                executionUnit,
                entryKind,
                provider,
                command,
                .. arguments
            ],
            RequiresPersistentExitMonitor = true
        };
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
            return;
        }

        eventWriter.Append(CreateBackendExitEvent(
            DateTimeOffset.UtcNow,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            exitCode));
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

    private static DirectRunProviderEvent CreateSessionMetadataEvent(
        DateTimeOffset launchedAt,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        string model,
        string transport,
        string command)
    {
        return new DirectRunProviderEvent
        {
            Timestamp = launchedAt.ToString("O"),
            ExecutionUnit = executionUnit,
            Provider = provider,
            EntryKind = entryKind,
            SessionId = providerSessionId,
            Kind = "session-metadata",
            Payload = JsonSerializer.SerializeToElement(new
            {
                model,
                transport,
                command
            })
        };
    }

    private static DirectRunProviderEvent CreateProviderEvent(
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        string raw)
    {
        return new DirectRunProviderEvent
        {
            Timestamp = timestamp.ToString("O"),
            ExecutionUnit = executionUnit,
            Provider = provider,
            EntryKind = entryKind,
            SessionId = providerSessionId,
            Kind = "provider-event",
            Payload = ParsePayload(raw)
        };
    }

    private static DirectRunProviderEvent CreateBackendExitEvent(
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        int exitCode)
    {
        return new DirectRunProviderEvent
        {
            Timestamp = timestamp.ToString("O"),
            ExecutionUnit = executionUnit,
            Provider = provider,
            EntryKind = entryKind,
            SessionId = providerSessionId,
            Kind = "provider-event",
            Payload = JsonSerializer.SerializeToElement(new
            {
                type = "backend-exit",
                exit_code = exitCode
            })
        };
    }

    private static JsonElement ParsePayload(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(raw);
        }
    }

    private sealed record ResolvedProcessInvocation
    {
        public required string FileName { get; init; }

        public required IReadOnlyList<string> Arguments { get; init; }

        public required bool RequiresPersistentExitMonitor { get; init; }
    }
}
