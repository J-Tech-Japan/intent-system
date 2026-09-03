using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G206: Testability seam for <c>intent-cli worker next-action</c>. The
/// production implementation shells out to <c>gh pr list</c> and
/// <c>gh issue list</c> with label filters; tests inject a fake to avoid
/// any GitHub network access.
/// </summary>
internal interface IGitHubAutomationCandidateLister
{
    IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels);

    /// <summary>
    /// G674: surface-aware overload. Existing fakes keep their old method and
    /// therefore remain valid; the production adapter uses the surface to
    /// attach the precise GraphQL-bound field remainder to a failure.
    /// </summary>
    IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels,
        GitHubAutomationReadSurface surface)
        => ListPullRequests(repo, requiredLabels);

    IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
        string repo,
        IReadOnlyCollection<string> requiredLabels);

    /// <summary>G674: surface-aware REST issue-list overload.</summary>
    IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
        string repo,
        IReadOnlyCollection<string> requiredLabels,
        GitHubAutomationReadSurface surface)
        => ListIssues(repo, requiredLabels);

    /// <summary>
    /// G448: list MERGED pull requests (with closing-issue references) for the
    /// unified state doctor's merged-PR-not-completed lane. Default returns an
    /// empty list so existing fakes/implementations keep compiling; the
    /// gh-backed lister overrides it with a <c>--state merged</c> query.
    /// </summary>
    IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
        => Array.Empty<GitHubAutomationPrCandidate>();

    IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels,
        GitHubAutomationReadSurface surface)
        => ListMergedPullRequests(repo, requiredLabels);

    /// <summary>
    /// G669: list CLOSED pull requests so a routing conflict remains visible
    /// after the PR closes. Existing fakes retain the empty default.
    /// </summary>
    IReadOnlyList<GitHubAutomationPrCandidate> ListClosedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
        => Array.Empty<GitHubAutomationPrCandidate>();

    IReadOnlyList<GitHubAutomationPrCandidate> ListClosedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels,
        GitHubAutomationReadSurface surface)
        => ListClosedPullRequests(repo, requiredLabels);

    /// <summary>
    /// G725: list published GitHub Releases so repository-level release
    /// closeout can be compared with the local version policy. Existing
    /// fakes retain the empty default, which represents a repository with no
    /// published release rather than inventing a release from local state.
    /// </summary>
    IReadOnlyList<GitHubAutomationReleaseCandidate> ListPublishedReleases(
        string repo)
        => Array.Empty<GitHubAutomationReleaseCandidate>();

    IReadOnlyList<GitHubAutomationReleaseCandidate> ListPublishedReleases(
        string repo,
        GitHubAutomationReadSurface surface)
        => ListPublishedReleases(repo);
}

/// <summary>
/// G206: Single PR candidate row returned by
/// <see cref="IGitHubAutomationCandidateLister.ListPullRequests"/>.
/// </summary>
internal sealed record GitHubAutomationPrCandidate
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; init; } = string.Empty;

    [JsonPropertyName("labels")]
    public IReadOnlyList<GitHubAutomationLabel> Labels { get; init; }
        = Array.Empty<GitHubAutomationLabel>();

    [JsonPropertyName("closingIssuesReferences")]
    public IReadOnlyList<GitHubPrClosingIssueReference> ClosingIssuesReferences { get; init; }
        = Array.Empty<GitHubPrClosingIssueReference>();

    /// <summary>
    /// G289: GitHub PR state ("OPEN" / "CLOSED" / "MERGED"). Empty for
    /// callers that pre-date the field; treated as open for backward compat.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    /// <summary>G669: the PR's actual target branch.</summary>
    [JsonPropertyName("baseRefName")]
    public string BaseRefName { get; init; } = string.Empty;

    /// <summary>
    /// G319: GitHub PR draft flag from <c>gh pr list --json isDraft</c>.
    /// Defaults to <c>false</c> so existing test fakes that don't seed it
    /// keep the prior non-draft behavior. Consumed by the host-loop-next-action
    /// approved-PR continuation lane (G297 forbids merging a draft PR
    /// even after an approval transition).
    /// </summary>
    [JsonPropertyName("isDraft")]
    public bool IsDraft { get; init; }

    /// <summary>
    /// G589: exact PR head the check rollup belongs to. Empty for callers and
    /// fixtures that pre-date CI-aware stalled-work; those callers retain the
    /// existing non-CI classification.
    /// </summary>
    [JsonPropertyName("headRefOid")]
    public string HeadRefOid { get; init; } = string.Empty;

    /// <summary>
    /// G793: the merge commit reported by GitHub for a merged PR. This is
    /// separate from <see cref="HeadRefOid"/>: the branch head proves what
    /// was reviewed, while the merge commit proves that the unit landed.
    /// </summary>
    [JsonPropertyName("mergeCommit")]
    public GitHubAutomationMergeCommit? MergeCommit { get; init; }

    /// <summary>
    /// G589: authoritative GitHub check/status rollup for <see cref="HeadRefOid"/>.
    /// The stalled-work analyzer normalizes CheckRun and StatusContext entries
    /// without performing any workflow transition.
    /// </summary>
    [JsonPropertyName("statusCheckRollup")]
    public IReadOnlyList<GitHubAutomationStatusCheckCandidate> StatusCheckRollup { get; init; }
        = Array.Empty<GitHubAutomationStatusCheckCandidate>();
}

/// <summary>G793: immutable merge SHA nested in GitHub's PR payload.</summary>
internal sealed record GitHubAutomationMergeCommit
{
    [JsonPropertyName("oid")]
    public string Oid { get; init; } = string.Empty;
}

/// <summary>G589: union-shaped row from GitHub's PR statusCheckRollup.</summary>
internal sealed record GitHubAutomationStatusCheckCandidate
{
    [JsonPropertyName("__typename")]
    public string TypeName { get; init; } = string.Empty;

    /// <summary>CheckRun lifecycle status, normally QUEUED / IN_PROGRESS / COMPLETED.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>CheckRun terminal conclusion, e.g. SUCCESS / FAILURE / SKIPPED.</summary>
    [JsonPropertyName("conclusion")]
    public string Conclusion { get; init; } = string.Empty;

    /// <summary>StatusContext state, e.g. PENDING / EXPECTED / SUCCESS / FAILURE / ERROR.</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;
}

/// <summary>
/// G206: Single issue candidate row returned by
/// <see cref="IGitHubAutomationCandidateLister.ListIssues"/>.
/// </summary>
internal sealed record GitHubAutomationIssueCandidate
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>G669: issue body declaration used for routing corroboration.</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// G533: GitHub's own "last modified" timestamp — bumped by any
    /// label change, comment, or other timeline event on the issue, so it
    /// is the closest available proxy for "last observable activity"
    /// without a dedicated per-issue timeline-events fetch. Empty for
    /// callers that pre-date the field; treated as unknown (falls back to
    /// <see cref="CreatedAt"/>) by consumers.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; init; } = string.Empty;

    [JsonPropertyName("labels")]
    public IReadOnlyList<GitHubAutomationLabel> Labels { get; init; }
        = Array.Empty<GitHubAutomationLabel>();

    /// <summary>
    /// G289: GitHub issue state ("OPEN" / "CLOSED"). Empty for callers that
    /// pre-date the field; treated as open for backward compat.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;
}

internal sealed record GitHubAutomationLabel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>G725: published stable-release metadata used by stalled-work.</summary>
internal sealed record GitHubAutomationReleaseCandidate
{
    [JsonPropertyName("tagName")]
    public string TagName { get; init; } = string.Empty;

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; init; } = string.Empty;

    [JsonPropertyName("isDraft")]
    public bool IsDraft { get; init; }

    [JsonPropertyName("isPrerelease")]
    public bool IsPrerelease { get; init; }
}

/// <summary>G674: captured read-only <c>gh</c> process result for adapter tests.</summary>
internal sealed record GhCliProcessResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// G206/G674: default lister that shells out to <c>gh pr list</c> for the
/// GraphQL-bound PR reads and <c>gh api</c> for the verified REST issue reads.
/// The only file in the worker next-action surface permitted to call
/// <c>Process.Start</c> — the analyzer and command layers must remain pure.
/// PR listing also includes body and closing issue metadata so host selectors
/// can model issue-linked PR fallback without extra mutation-capable calls.
/// </summary>
internal sealed class GhCliGitHubAutomationCandidateLister : IGitHubAutomationCandidateLister
{
    /// <summary>
    /// G673 test seam for the structured <c>gh api rate_limit</c> observation
    /// made after a failed GitHub read. Production leaves this null.
    /// </summary>
    internal static Func<IGitHubApiQuotaProbe>? QuotaProbeFactory { get; set; }

    /// <summary>
    /// G674 test seam for the read-only <c>gh</c> invocation. Production leaves
    /// this null; tests use it to prove the REST adapter shape without a
    /// network call or a replacement transport binary.
    /// </summary>
    internal static Func<IReadOnlyList<string>, GhCliProcessResult>? ProcessRunner { get; set; }

    /// <summary>
    /// G206: comma-separated <c>gh pr list --json</c> field list. Exposed
    /// internally so adapter-shape regression tests can lock the supported
    /// subset.
    /// </summary>
    // G289: also request `state` so the analyzer can defensively filter
    // closed issues / PRs from WIP detection even when callers (e.g. test
    // fakes) don't pre-apply `--state open`.
    // G533: also request `updatedAt` — the closest available proxy for
    // "last observable issue activity" (label change, comment, etc.)
    // without a dedicated per-issue timeline-events fetch, used by
    // `automation stalled-work`'s claimed-but-silent detection.
    internal const string ListJsonFields = "number,title,url,body,createdAt,updatedAt,labels,state";

    // G319: also request `isDraft` so the host-loop-next-action analyzer
    // can map an approved-but-draft PR to `approved-pr-draft-blocked`
    // (G297) instead of attempting a merge.
    internal const string PrListJsonFields =
        "number,title,url,body,baseRefName,createdAt,updatedAt,labels,closingIssuesReferences,state,isDraft,headRefOid,mergeCommit,statusCheckRollup";

    /// <summary>
    /// G206: builds the <c>gh pr list</c> argument list shared by the live
    /// adapter and adapter-shape tests.
    /// </summary>
    internal static IReadOnlyList<string> BuildPrListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "pr",
            "list",
            "--repo", repo,
            "--state", "open",
            "--json", PrListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    /// <summary>
    /// G206: builds the legacy <c>gh issue list</c> argument list. The
    /// surface-aware overload below is the only REST path; keeping this
    /// builder preserves unmeasured callers' transport and output contract.
    /// </summary>
    internal static IReadOnlyList<string> BuildIssueListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "issue",
            "list",
            "--repo", repo,
            "--state", "open",
            "--json", ListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    /// <summary>
    /// G448: builds the <c>gh pr list --state merged</c> argument list used by
    /// the unified state doctor's merged-PR lane.
    /// </summary>
    internal static IReadOnlyList<string> BuildMergedPrListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "pr",
            "list",
            "--repo", repo,
            "--state", "merged",
            "--json", PrListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    internal static IReadOnlyList<string> BuildClosedPrListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "pr",
            "list",
            "--repo", repo,
            "--state", "closed",
            "--json", PrListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    /// <summary>
    /// G725: builds the read-only release list used to observe published
    /// stable releases. Draft and prerelease rows are filtered after
    /// deserialization so the transport remains a simple existing <c>gh</c>
    /// read and the no-release case remains an honest empty observation.
    /// </summary>
    internal static IReadOnlyList<string> BuildReleaseListArguments(string repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        return
        [
            "release",
            "list",
            "--repo", repo,
            "--json", "tagName,publishedAt,isDraft,isPrerelease",
            "--limit", "100",
        ];
    }

    /// <summary>
    /// G674: builds the REST issue-list request. <c>--paginate --slurp</c>
    /// preserves the old adapter's 200-row upper bound by flattening the
    /// server's ordered pages in the deserializer; no cross-agent cache,
    /// batch, or retry machinery is introduced.
    /// </summary>
    internal static IReadOnlyList<string> BuildRestIssueListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "api",
            $"repos/{repo}/issues",
            "--method", "GET",
            "--raw-field", "state=open",
            "--raw-field", "per_page=100",
            "--paginate",
            "--slurp",
        };
        if (requiredLabels.Count > 0)
        {
            args.Add("--raw-field");
            args.Add($"labels={string.Join(',', requiredLabels)}");
        }

        return args;
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildPrListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list PRs in {repo}");
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"`gh pr list` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels,
        GitHubAutomationReadSurface surface)
    {
        var args = BuildPrListArguments(repo, requiredLabels);
        var stdout = RunGh(
            args,
            $"list PRs in {repo}",
            GitHubApiQuotaConstants.GraphQlResource,
            GitHubApiReadDependencies.GraphQlBound,
            GitHubApiReadInventory.UnverifiedFieldsFor(surface));
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"`gh pr list` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildIssueListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list issues in {repo}");
        return DeserializeList<GitHubAutomationIssueCandidate>(stdout, $"`gh issue list` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
        string repo,
        IReadOnlyCollection<string> requiredLabels,
        GitHubAutomationReadSurface surface)
    {
        var args = BuildRestIssueListArguments(repo, requiredLabels);
        var stdout = RunGh(
            args,
            $"list issues in {repo} via REST",
            GitHubApiQuotaConstants.RestCoreResource,
            GitHubApiReadDependencies.RestCore);
        return DeserializeRestIssueList(stdout, $"`gh api repos/{repo}/issues` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListClosedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildClosedPrListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list closed PRs in {repo}");
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"gh pr list state closed for {repo}");
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListClosedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels,
        GitHubAutomationReadSurface surface)
    {
        var args = BuildClosedPrListArguments(repo, requiredLabels);
        var stdout = RunGh(
            args,
            $"list closed PRs in {repo}",
            GitHubApiQuotaConstants.GraphQlResource,
            GitHubApiReadDependencies.GraphQlBound,
            GitHubApiReadInventory.UnverifiedFieldsFor(surface));
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"gh pr list state closed for {repo}");
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildMergedPrListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list merged PRs in {repo}");
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"`gh pr list --state merged` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationReleaseCandidate> ListPublishedReleases(
        string repo)
    {
        var args = BuildReleaseListArguments(repo);
        var stdout = RunGh(args, $"list published releases in {repo}");
        return DeserializeList<GitHubAutomationReleaseCandidate>(
            stdout,
            $"`gh release list` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationReleaseCandidate> ListPublishedReleases(
        string repo,
        GitHubAutomationReadSurface surface)
    {
        var args = BuildReleaseListArguments(repo);
        var stdout = RunGh(
            args,
            $"list published releases in {repo}",
            GitHubApiQuotaConstants.GraphQlResource,
            GitHubApiReadDependencies.GraphQlBound,
            GitHubApiReadInventory.UnverifiedFieldsFor(surface));
        return DeserializeList<GitHubAutomationReleaseCandidate>(
            stdout,
            $"`gh release list` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels,
        GitHubAutomationReadSurface surface)
    {
        var args = BuildMergedPrListArguments(repo, requiredLabels);
        var stdout = RunGh(
            args,
            $"list merged PRs in {repo}",
            GitHubApiQuotaConstants.GraphQlResource,
            GitHubApiReadDependencies.GraphQlBound,
            GitHubApiReadInventory.UnverifiedFieldsFor(surface));
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"`gh pr list --state merged` for {repo}");
    }

    private static string RunGh(
        IReadOnlyList<string> arguments,
        string description,
        string? quotaResource = null,
        string? dependency = null,
        IReadOnlyList<string>? unverifiedFields = null)
    {
        GhCliProcessResult processResult;
        try
        {
            processResult = ProcessRunner?.Invoke(arguments)
                ?? RunGhProcess(arguments, description);
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException)
        {
            throw new GitHubApiRequestException(
                "github-transport-error",
                description,
                $"[github-transport-error] could not invoke `gh` to {description}: {exception.Message}",
                innerException: exception);
        }

        if (processResult.ExitCode != 0)
        {
            // G673: only the API's structured rate-limit response can name a
            // quota failure. No stderr/free-text quota matching is used.
            var quotaProbe = QuotaProbeFactory?.Invoke() ?? new GhCliGitHubApiQuotaProbe();
            throw GitHubApiFailureFactory.FromGhFailure(
                description,
                processResult.Stderr,
                processResult.Stdout,
                processResult.ExitCode,
                quotaProbe,
                quotaResource,
                dependency,
                unverifiedFields);
        }

        return processResult.Stdout;
    }

    private static GhCliProcessResult RunGhProcess(
        IReadOnlyList<string> arguments,
        string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"failed to start `gh` process to {description}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GhCliProcessResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// G385: deserialize the candidate list through the hardened JSON boundary.
    /// Internal so adapter tests can lock the contamination contract without a
    /// real <c>gh</c> subprocess.
    /// </summary>
    internal static IReadOnlyList<T> DeserializeList<T>(string stdout, string callDescription)
    {
        // G385: harden the parser boundary against contaminated stdout (BOM,
        // trailing update notices, warning lines printed alongside the array —
        // observed under Windows PowerShell native-command capture). Parse only
        // the validated JSON array; on genuine failure raise a structured,
        // sanitized diagnostic rather than a raw JsonException.
        var extraction = GitHubCliJsonBoundary.ExtractJsonArray(stdout, callDescription);
        if (!extraction.Succeeded)
        {
            throw GitHubApiFailureFactory.JsonInvalid(
                callDescription,
                extraction.DiagnosticMessage ?? "gh stdout was not valid JSON");
        }

        try
        {
            var result = JsonSerializer.Deserialize<List<T>>(extraction.Json);
            return (IReadOnlyList<T>?)result ?? Array.Empty<T>();
        }
        catch (JsonException exception)
        {
            // The array shape was already validated, so a typed-deserialize
            // failure here is a schema mismatch, not contamination — still
            // surface a sanitized, classified diagnostic.
            throw new GitHubApiRequestException(
                GitHubCliJsonBoundary.Classifications.GithubJsonInvalid,
                callDescription,
                $"[{GitHubCliJsonBoundary.Classifications.GithubJsonInvalid}] could not map {callDescription} "
                + $"JSON to the expected shape: {exception.Message}; sanitized preview: "
                + $"\"{GitHubCliJsonBoundary.SanitizePreview(extraction.Json)}\"",
                innerException: exception);
        }
    }

    /// <summary>
    /// G674: maps the REST issues endpoint to the pre-existing issue-candidate
    /// shape. GitHub's REST issue collection includes pull requests, so the
    /// adapter excludes rows carrying the REST-only <c>pull_request</c>
    /// marker before exposing anything to the analyzers. Slurped pages are
    /// flattened in server order and capped at the old <c>--limit 200</c>.
    /// </summary>
    internal static IReadOnlyList<GitHubAutomationIssueCandidate> DeserializeRestIssueList(
        string stdout,
        string callDescription)
    {
        var extraction = GitHubCliJsonBoundary.ExtractJsonArray(stdout, callDescription);
        if (!extraction.Succeeded)
        {
            throw GitHubApiFailureFactory.JsonInvalid(
                callDescription,
                extraction.DiagnosticMessage ?? "gh stdout was not valid JSON");
        }

        try
        {
            using var document = JsonDocument.Parse(extraction.Json);
            var candidates = new List<GitHubAutomationIssueCandidate>();
            foreach (var pageOrIssue in document.RootElement.EnumerateArray())
            {
                if (pageOrIssue.ValueKind == JsonValueKind.Array)
                {
                    foreach (var issue in pageOrIssue.EnumerateArray())
                    {
                        AddRestIssue(issue, candidates);
                    }
                }
                else
                {
                    AddRestIssue(pageOrIssue, candidates);
                }

                if (candidates.Count >= 200)
                {
                    break;
                }
            }

            return candidates.Take(200).ToArray();
        }
        catch (JsonException exception)
        {
            throw new GitHubApiRequestException(
                GitHubCliJsonBoundary.Classifications.GithubJsonInvalid,
                callDescription,
                $"[{GitHubCliJsonBoundary.Classifications.GithubJsonInvalid}] could not map {callDescription} REST JSON: "
                + $"{exception.Message}; sanitized preview: \"{GitHubCliJsonBoundary.SanitizePreview(extraction.Json)}\"",
                innerException: exception);
        }
    }

    private static void AddRestIssue(
        JsonElement issue,
        List<GitHubAutomationIssueCandidate> candidates)
    {
        if (issue.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("REST issue page contained a non-object row.");
        }

        // The REST collection is intentionally an issue+PR collection. The
        // old `gh issue list` contract returned issues only.
        if (issue.TryGetProperty("pull_request", out var pullRequest)
            && pullRequest.ValueKind is not JsonValueKind.Null
            and not JsonValueKind.Undefined)
        {
            return;
        }

        var labels = new List<GitHubAutomationLabel>();
        if (issue.TryGetProperty("labels", out var labelArray)
            && labelArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labelArray.EnumerateArray())
            {
                if (label.ValueKind == JsonValueKind.Object
                    && label.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String)
                {
                    labels.Add(new GitHubAutomationLabel { Name = name.GetString() ?? string.Empty });
                }
            }
        }

        candidates.Add(new GitHubAutomationIssueCandidate
        {
            Number = ReadInt32(issue, "number"),
            Title = ReadString(issue, "title"),
            Url = ReadString(issue, "html_url"),
            CreatedAt = ReadString(issue, "created_at"),
            Body = ReadString(issue, "body"),
            UpdatedAt = ReadString(issue, "updated_at"),
            Labels = labels,
            State = ReadString(issue, "state").ToUpperInvariant(),
        });
    }

    private static int ReadInt32(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var number)
                ? number
                : 0;

    private static string ReadString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
}
