namespace IntentSystem.Supervisor.Models;

public sealed record LinkedIssue
{
    public required string Repo { get; init; }

    public int? Number { get; init; }

    public string? Url { get; init; }
}
