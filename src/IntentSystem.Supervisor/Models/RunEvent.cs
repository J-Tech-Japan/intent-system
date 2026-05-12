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

    public string? EntryKind { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? SessionId { get; init; }

    public string? RunStatus { get; init; }

    public string? RawLogRef { get; init; }

    public string? ResultRef { get; init; }

    public string? PacketRef { get; init; }

    public string? ReviewContextRef { get; init; }

    public string? WorktreePath { get; init; }

    /// <summary>
    /// G324: GitHub repository the event refers to, in
    /// <c>owner/repo</c> form. Optional; populated by closeout / PR
    /// transition events that bind durable bookkeeping to a specific
    /// GitHub repository.
    /// </summary>
    public string? Repo { get; init; }

    /// <summary>
    /// G324: GitHub PR number the event refers to. Optional; populated
    /// alongside <see cref="Repo"/> by closeout / PR-merge events so
    /// downstream consumers can correlate without re-parsing the
    /// linked_pr URL.
    /// </summary>
    public int? Pr { get; init; }
}
