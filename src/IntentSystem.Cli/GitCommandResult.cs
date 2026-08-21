namespace IntentSystem.Cli;

internal sealed record GitRemoteCommandResult
{
    public required int ExitCode { get; init; }

    public required string StdOut { get; init; }

    public required string StdErr { get; init; }

    public bool TimedOut { get; init; }
}
