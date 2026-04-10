namespace IntentSystem.Cli.Commands;

internal interface IDirectRunLauncher
{
    DirectRunLaunchResult Launch(
        string executionUnit,
        string entryKind,
        string requestArtifactPath,
        string provider,
        string model,
        string transport,
        DateTimeOffset launchedAt,
        string workingDirectory,
        string absoluteRequestArtifactPath);
}
