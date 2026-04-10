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
        string provider,
        string model,
        string transport,
        DateTimeOffset launchedAt,
        string workingDirectory,
        string absoluteRequestArtifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestArtifactPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteRequestArtifactPath);

        var command = ResolveCommand(provider, model, absoluteRequestArtifactPath);
        var process = processRunner.Start(
            workingDirectory,
            command.FileName,
            command.Arguments,
            DefaultEarlyExitWindow);

        if (process.ExitedEarly && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Direct run launch failed for provider '{provider}' using command '{command.FileName}' with exit code {process.ExitCode}.");
        }

        return new DirectRunLaunchResult
        {
            RequestArtifactPath = requestArtifactPath,
            Provider = provider,
            Model = model,
            Transport = transport,
            ProviderSessionId = $"pid:{process.ProcessId}",
            TransportSummary =
                $"{transport} transport launched via '{command.FileName}' in '{workingDirectory}' for provider '{provider}'."
        };
    }

    private static (string FileName, IReadOnlyList<string> Arguments) ResolveCommand(
        string provider,
        string model,
        string absoluteRequestArtifactPath)
    {
        var prompt =
            $"Use the request artifact at '{absoluteRequestArtifactPath}' as the bounded source of truth for this direct run.";

        return provider.Trim().ToLowerInvariant() switch
        {
            "codex" => ("codex", ["exec", "--model", model, prompt]),
            "claude" => ("claude", ["--print", "--model", model, "--output-format", "json", prompt]),
            _ => (provider, [prompt])
        };
    }
}
