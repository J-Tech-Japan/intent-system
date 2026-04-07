namespace IntentSystem.Cli.Commands;

internal sealed record ReconstructedBridgeQuestion
{
    public required string QuestionId { get; init; }

    public required string QuestionText { get; init; }

    public required string Reason { get; init; }

    public required IReadOnlyList<string> Affects { get; init; }

    public required string BlockingOrNonblocking { get; init; }
}
