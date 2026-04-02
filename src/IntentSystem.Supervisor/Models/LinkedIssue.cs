namespace IntentSystem.Supervisor.Models;

public sealed record LinkedIssue
{
    public required string Repo { get; init; }

    public required int Number { get; init; }

    public required string Url { get; init; }
}
