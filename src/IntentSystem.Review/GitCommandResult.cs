namespace IntentSystem.Review;

public sealed record GitCommandResult
{
    public required int ExitCode { get; init; }

    public required string StdOut { get; init; }

    public required string StdErr { get; init; }
}
