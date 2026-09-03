namespace IntentSystem.Cli.Commands;

/// <summary>
/// G674: identifies the automation surface for which a shared GitHub read is
/// being made.  The overloads on <see cref="IGitHubAutomationCandidateLister"/>
/// keep older test doubles source-compatible while production can attach the
/// field-equivalence record to the transport failure.
/// </summary>
internal enum GitHubAutomationReadSurface
{
    Unspecified,
    WorkerNextAction,
    HostLoopNextAction,
    HostReviewPreflight,
    Reconcile,
    StalledWork,
    Heartbeat,
}

/// <summary>
/// G674's machine-readable transport dependency vocabulary.  These values
/// deliberately describe a quota resource, not an implementation detail of
/// the process launcher.
/// </summary>
internal static class GitHubApiReadDependencies
{
    public const string RestCore = "rest-core";
    public const string GraphQlBound = "graphql-bound";
}

/// <summary>
/// One verified REST field-equivalence record.  The endpoint is a template so
/// the record remains useful for every repository and label filter.
/// </summary>
internal sealed record GitHubApiReadEquivalence
{
    public required string Surface { get; init; }

    public required string CallSite { get; init; }

    public required string Endpoint { get; init; }

    public required IReadOnlyList<string> ConsumedFields { get; init; }

    public required IReadOnlyList<string> RestFields { get; init; }
}

/// <summary>
/// One intentionally un-migrated read.  A missing field-complete REST
/// equivalent is a reason to keep the call GraphQL-bound; it must not be
/// silently approximated from a similarly shaped response.
/// </summary>
internal sealed record GitHubApiGraphQlBoundRead
{
    public required string Surface { get; init; }

    public required string CallSite { get; init; }

    public required IReadOnlyList<string> UnverifiedFields { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// G674 field inventory and equivalence ledger.  Keep this list close to the
/// adapter so a transport change cannot land without naming its exact
/// consumed fields and endpoint.  The six #1442-measured surfaces share the
/// issue-list adapter; heartbeat inherits the stalled-work read.
/// </summary>
internal static class GitHubApiReadInventory
{
    private static readonly IReadOnlyList<string> IssueConsumedFields =
    [
        "number",
        "title",
        "url",
        "createdAt",
        "body",
        "updatedAt",
        "labels[].name",
        "state",
    ];

    private static readonly IReadOnlyList<string> IssueRestFields =
    [
        "number",
        "title",
        "html_url -> url",
        "created_at -> createdAt",
        "body",
        "updated_at -> updatedAt",
        "labels[].name",
        "state (normalized to the existing upper-case vocabulary)",
        "pull_request (adapter-only PR exclusion)",
    ];

    private static readonly IReadOnlyList<string> ClosingReferenceFields =
    [
        "closingIssuesReferences[].number",
        "closingIssuesReferences[].repository.owner.login",
        "closingIssuesReferences[].repository.name",
        "mergeCommit.oid",
    ];

    private static readonly IReadOnlyList<string> StatusRollupFields =
    [
        "statusCheckRollup[].__typename",
        "statusCheckRollup[].status",
        "statusCheckRollup[].conclusion",
        "statusCheckRollup[].state",
    ];

    public static IReadOnlyList<GitHubApiReadEquivalence> VerifiedRestIssueReads { get; } =
    [
        Equivalence("worker-next-action", "intent-target-issues"),
        Equivalence("host-loop-next-action", "open-issues"),
        Equivalence("host-review-preflight", "intent-target-issues"),
        Equivalence("host-review-preflight", "published-intent-target-issues"),
        Equivalence("reconcile", "published-intent-target-issues"),
        Equivalence("stalled-work", "open-issues"),
        Equivalence("heartbeat", "stalled-work/open-issues (inherited)"),
    ];

    public static IReadOnlyList<GitHubApiGraphQlBoundRead> GraphQlBoundReads { get; } =
    [
        Bound(
            "worker-next-action",
            "open-prs",
            ClosingReferenceFields,
            "REST issue/PR listing does not provide the GraphQL closing-issue reference object; body parsing is only a documented fallback and is not field-equivalent."),
        Bound(
            "host-loop-next-action",
            "open-prs",
            ClosingReferenceFields,
            "The approved continuation and source-issue linkage consume GitHub's closingIssuesReferences object."),
        Bound(
            "host-review-preflight",
            "open-prs",
            ClosingReferenceFields,
            "Review fallback consumes repository-qualified closing-issue references."),
        Bound(
            "reconcile",
            "open-prs",
            ClosingReferenceFields,
            "Safe label and queue repairs require the exact closing-issue reference set."),
        Bound(
            "stalled-work",
            "open-prs / merged-prs / closed-prs",
            ClosingReferenceFields.Concat(StatusRollupFields).ToArray(),
            "Closing references and the CheckRun/StatusContext union are both consumed; check-runs alone are not a proven equivalent of the union."),
        Bound(
            "heartbeat",
            "stalled-work PR reads (inherited)",
            ClosingReferenceFields.Concat(StatusRollupFields).ToArray(),
            "Heartbeat delegates GitHub detection to stalled-work and inherits its GraphQL-bound PR remainder."),
    ];

    public static IReadOnlyList<string> UnverifiedFieldsFor(GitHubAutomationReadSurface surface) =>
        surface switch
        {
            GitHubAutomationReadSurface.StalledWork
            or GitHubAutomationReadSurface.Heartbeat =>
                ClosingReferenceFields.Concat(StatusRollupFields).ToArray(),
            GitHubAutomationReadSurface.WorkerNextAction
            or GitHubAutomationReadSurface.HostLoopNextAction
            or GitHubAutomationReadSurface.HostReviewPreflight
            or GitHubAutomationReadSurface.Reconcile =>
                ClosingReferenceFields.ToArray(),
            _ => ClosingReferenceFields.Concat(StatusRollupFields).ToArray(),
        };

    private static GitHubApiReadEquivalence Equivalence(string surface, string callSite) => new()
    {
        Surface = surface,
        CallSite = callSite,
        Endpoint = "GET /repos/{owner}/{repo}/issues?state=open&labels=<comma-separated-labels>&per_page=100 (gh api --paginate --slurp)",
        ConsumedFields = IssueConsumedFields,
        RestFields = IssueRestFields,
    };

    private static GitHubApiGraphQlBoundRead Bound(
        string surface,
        string callSite,
        IReadOnlyList<string> fields,
        string reason) => new()
    {
        Surface = surface,
        CallSite = callSite,
        UnverifiedFields = fields,
        Reason = reason,
    };
}
