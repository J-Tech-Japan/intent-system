namespace IntentSystem.Supervisor.Models;

public sealed record RunEvent
{
    public required DateTimeOffset Ts { get; init; }

    public required string ExecutionUnit { get; init; }

    public required string Event { get; init; }

    public required string By { get; init; }

    public string? LinkedIssue { get; init; }

    public string? LinkedPr { get; init; }

    public string? CommentRef { get; init; }

    public string? Reason { get; init; }
}
