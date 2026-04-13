using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal sealed class DirectRunLauncher : IDirectRunLauncher
{
    private static readonly TimeSpan DefaultEarlyExitWindow = TimeSpan.FromMilliseconds(500);
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
                    providerSessionId);
            },
            exitCode => AppendBackendExitEventIfMissing(
                    eventWriter,
                    absoluteProviderEventLogPath,
                    executionUnit,
                    entryKind,
                    provider,
                    providerSessionId,
                    exitCode),
            raw => eventWriter.Append(CreateProviderEvent(DateTimeOffset.UtcNow, executionUnit, entryKind, provider, providerSessionId, raw)),
            raw => eventWriter.Append(CreateProviderEvent(DateTimeOffset.UtcNow, executionUnit, entryKind, provider, providerSessionId, raw)));

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
                provider_log_path=$1
                execution_unit=$2
                entry_kind=$3
                provider=$4
                shift 4
                session_id="pid:$$"
                child_pid=""
                exit_code=0
                backend_exit_written=0
                append_backend_exit() {
                    if [ "$backend_exit_written" -ne 0 ]; then
                        return
                    fi
                    backend_exit_written=1
                    timestamp=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
                    printf '%s\n' "{\"ts\":\"$timestamp\",\"execution_unit\":\"$execution_unit\",\"provider\":\"$provider\",\"entry_kind\":\"$entry_kind\",\"session_id\":\"$session_id\",\"kind\":\"provider-event\",\"payload\":{\"type\":\"backend-exit\",\"exit_code\":$exit_code}}" >> "$provider_log_path"
                }
                handle_signal() {
                    signal_name=$1
                    if [ -n "$child_pid" ] && kill -0 "$child_pid" 2>/dev/null; then
                        kill -s "$signal_name" "$child_pid" 2>/dev/null || true
                        wait "$child_pid"
                        exit_code=$?
                    fi
                    append_backend_exit
                    trap - EXIT HUP INT TERM
                    exit "$exit_code"
                }
                trap 'append_backend_exit' EXIT
                trap 'handle_signal HUP' HUP
                trap 'handle_signal INT' INT
                trap 'handle_signal TERM' TERM
                "$@" &
                child_pid=$!
                wait "$child_pid"
                exit_code=$?
                exit "$exit_code"
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
        int exitCode)
    {
        ArgumentNullException.ThrowIfNull(eventWriter);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);

        if (string.IsNullOrWhiteSpace(providerSessionId) || HasBackendExitEvent(providerEventLogPath, providerSessionId))
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
        string providerSessionId)
    {
        if (!requiresPersistentExitMonitor || OperatingSystem.IsWindows())
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            """
            pid=$1
            provider_log_path=$2
            execution_unit=$3
            entry_kind=$4
            provider=$5
            session_id=$6
            while kill -0 "$pid" 2>/dev/null; do
                sleep 1
            done
            if [ -f "$provider_log_path" ] && grep -F "\"session_id\":\"$session_id\"" "$provider_log_path" | grep -F "\"type\":\"backend-exit\"" >/dev/null 2>&1; then
                exit 0
            fi
            timestamp=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
            printf '%s\n' "{\"ts\":\"$timestamp\",\"execution_unit\":\"$execution_unit\",\"provider\":\"$provider\",\"entry_kind\":\"$entry_kind\",\"session_id\":\"$session_id\",\"kind\":\"provider-event\",\"payload\":{\"type\":\"backend-exit\",\"exit_code\":1}}" >> "$provider_log_path"
            """);
        startInfo.ArgumentList.Add("direct-run-exit-monitor");
        startInfo.ArgumentList.Add(processId.ToString());
        startInfo.ArgumentList.Add(providerEventLogPath);
        startInfo.ArgumentList.Add(executionUnit);
        startInfo.ArgumentList.Add(entryKind);
        startInfo.ArgumentList.Add(provider);
        startInfo.ArgumentList.Add(providerSessionId);

        var monitor = Process.Start(startInfo);
        monitor?.Dispose();
    }

    private static bool HasBackendExitEvent(string providerEventLogPath, string providerSessionId)
    {
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        foreach (var line in File.ReadLines(providerEventLogPath))
        {
            if (line.Contains($"\"session_id\":\"{providerSessionId}\"", StringComparison.Ordinal)
                && line.Contains("\"kind\":\"provider-event\"", StringComparison.Ordinal)
                && line.Contains("\"type\":\"backend-exit\"", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
