namespace IntentSystem.Cli;

internal sealed record GitHubCommandResult
{
    public required int ExitCode { get; init; }

    public required string StdOut { get; init; }

    public required string StdErr { get; init; }
}
