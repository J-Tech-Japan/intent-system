namespace IntentSystem.Cli.Commands;

internal sealed record DirectRunProcessLaunchResult
{
    public required int ProcessId { get; init; }

    public required bool ExitedEarly { get; init; }

    public required int ExitCode { get; init; }
}
