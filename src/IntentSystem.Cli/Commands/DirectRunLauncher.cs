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
        var eventWriter = new DirectRunProviderEventWriter(absoluteProviderEventLogPath);
        var providerSessionId = string.Empty;
        var process = processRunner.Start(
            workingDirectory,
            command,
            arguments,
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
            },
            exitCode => eventWriter.Append(CreateBackendExitEvent(
                DateTimeOffset.UtcNow,
                executionUnit,
                entryKind,
                provider,
                providerSessionId,
                exitCode)),
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
}
