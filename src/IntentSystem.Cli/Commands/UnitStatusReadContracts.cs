namespace IntentSystem.Cli.Commands;

/// <summary>Plain-data seam used to test bounded reads without a live GitHub or Git process.</summary>
internal interface IUnitStatusSnapshotReader
{
    UnitStatusClaimSnapshot ReadClaimSnapshot(CliContext context, string executionUnit);

    UnitStatusRemoteSnapshot ObserveGitHub(
        CliContext context,
        string repo,
        int issue,
        int pullRequest,
        string executionUnit,
        string domain,
        string team);

    UnitStatusRemoteSnapshot ObserveGitHubIssue(
        CliContext context,
        string repo,
        int issue,
        string executionUnit);
}

internal sealed record UnitStatusClaimSnapshot
{
    public string? State { get; init; }
    public string? Cause { get; init; }
    public string? Detail { get; init; }
    public string? MetadataRef { get; init; }
    public string? MetadataOid { get; init; }
    public string? LocalHeadSha { get; init; }
    public string? LocalHeadRef { get; init; }
    public string? UnavailableClass { get; init; }
    public UnitStatusClaimRecordFact? ActiveClaim { get; init; }
    public IReadOnlyList<UnitStatusClaimRecordFact> History { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

internal sealed record UnitStatusClaimRecordFact
{
    public required string Path { get; init; }
    public required string Scope { get; init; }
    public required string Actor { get; init; }
    public required string Team { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public string? Operation { get; init; }
    public DateTimeOffset? RecordedAt { get; init; }
    public string? DisplacedHolder { get; init; }
    public string? DisplacedTeam { get; init; }
    public DateTimeOffset? DisplacedClaimedAt { get; init; }
}

internal sealed record UnitStatusRemoteSnapshot
{
    public required string State { get; init; }
    public string? Cause { get; init; }
    public string? Detail { get; init; }
    public string? HeadBefore { get; init; }
    public string? HeadAfter { get; init; }
    public string? HeadSha { get; init; }
    public string? MergeCommitSha { get; init; }
    public bool? Merged { get; init; }
    public IReadOnlyList<string> IssueLabels { get; init; } = [];
    public IReadOnlyList<string> PullRequestLabels { get; init; } = [];
    public IReadOnlyList<UnitStatusObservedReview> Reviews { get; init; } = [];
    public IReadOnlyList<UnitStatusObservedCheck> Checks { get; init; } = [];
    public IReadOnlyList<UnitStatusFact> Facts { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

internal sealed record UnitStatusPullRequestRead(
    string HeadSha,
    bool Merged,
    string? MergeCommitSha,
    IReadOnlyList<string> ObservedLabels);

internal sealed record UnitStatusReadFailure(string Cause, string Detail);

internal sealed record UnitStatusReviewBodyParse(
    bool IsStructured,
    bool NamedIndependent,
    string? Relation,
    string? ExecutionUnit,
    string? HeadSha,
    string? Kind,
    string? Verdict,
    string? Runtime,
    string? ConductorRuntime,
    string? CitedRecord,
    bool Conflicting);
