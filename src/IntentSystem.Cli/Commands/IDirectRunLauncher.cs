namespace IntentSystem.Cli.Commands;

internal interface IDirectRunLauncher
{
    DirectRunLaunchResult Launch(
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
        string absoluteProviderEventLogPath);
}
