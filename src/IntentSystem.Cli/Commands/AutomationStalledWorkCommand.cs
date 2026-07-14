using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G523: <c>intent-cli automation stalled-work --domain &lt;d&gt; --repo &lt;r&gt;
/// [--stale-minutes &lt;m&gt;] [--format json|markdown]</c> — read-only
/// inventory of pending pipeline transitions with ages, so a single
/// orchestrator wake (or an external heartbeat) can detect a stall without a
/// human cross-checking GitHub labels, PR state, and queue-state by hand.
///
/// Categories:
/// <list type="bullet">
/// <item><c>published-not-delegated</c> — an OPEN issue carries
///   <c>intent-target</c> but no claim label
///   (<c>intent-issue-in-progress</c> / <c>intent-pr-created</c>) yet.</item>
/// <item><c>pr-created-not-reviewing</c> — the source issue carries
///   <c>intent-pr-created</c> and its closing PR has not had the
///   <c>review-start</c> transition applied (no <c>intent-pr-reviewing</c> /
///   <c>intent-pr-approved</c> on the PR).</item>
/// <item><c>merged-not-closed-out</c> — a MERGED PR's linked queue item is
///   not <see cref="QueueItemState.Completed"/>.</item>
/// </list>
///
/// Age is approximated from the relevant GitHub entity's `createdAt` /
/// `updatedAt` timestamp (GitHub does not expose per-label-application
/// timestamps), which is the closest available proxy for "how long has this
/// been pending".
///
/// Strictly read-only: no GitHub mutation, no queue-state/runs.jsonl write,
/// no label change. Domain scoping follows the G522 direction — items whose
/// derived execution unit does not match the requested domain's
/// `execution_unit_regex` binding (`intents/&lt;domain&gt;/automation/bindings.md`)
/// are excluded; a missing/invalid binding degrades to "no filter" (with a
/// surfaced warning) rather than blocking this diagnostic-only surface.
/// </summary>
internal static class AutomationStalledWorkCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string KindPublishedNotDelegated = "published-not-delegated";
    public const string KindPrCreatedNotReviewing = "pr-created-not-reviewing";
    public const string KindMergedNotClosedOut = "merged-not-closed-out";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation stalled-work --domain <name> --repo <owner/repo> [--stale-minutes <m>] [--format json|markdown]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(args, out var domain, out var repo, out var staleMinutes, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        IGitHubAutomationCandidateLister lister;
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues;
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
        IReadOnlyList<GitHubAutomationPrCandidate> mergedPrs;
        try
        {
            lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
            openIssues = lister.ListIssues(repo!, Array.Empty<string>());
            openPrs = lister.ListPullRequests(repo!, Array.Empty<string>());
            mergedPrs = lister.ListMergedPullRequests(repo!, Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to read GitHub state for {repo}: {exception.Message}");
            return 1;
        }

        var regexResolution = NextSliceDomainBindingsExecutionUnitRegex.Resolve(context, domain);
        var warnings = new List<string>();
        if (regexResolution.Regex is null)
        {
            warnings.Add(
                $"domain '{domain}' has no resolvable `execution_unit_regex` binding "
                + $"({regexResolution.BindingsPath ?? "(unresolved)"}) — items are NOT filtered by domain; "
                + "results may include other domains' work on a shared host.");
        }

        var now = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var items = new List<StalledWorkItem>();

        CollectPublishedNotDelegated(openIssues, regexResolution.Regex, repo!, now, items);
        CollectPrCreatedNotReviewing(openIssues, openPrs, regexResolution.Regex, repo!, now, items);
        CollectMergedNotClosedOut(context, domain!, repo!, mergedPrs, regexResolution.Regex, now, items, warnings);

        var filtered = items
            .Where(item => item.AgeMinutes >= staleMinutes)
            .OrderByDescending(item => item.AgeMinutes)
            .ToArray();

        var result = new AutomationStalledWorkResult
        {
            Domain = domain!,
            Repo = repo!,
            StaleMinutesThreshold = staleMinutes,
            Stalled = filtered.Length > 0,
            Items = filtered,
            Warnings = warnings,
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    private static void CollectPublishedNotDelegated(
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        System.Text.RegularExpressions.Regex? domainRegex,
        string repo,
        DateTimeOffset now,
        List<StalledWorkItem> items)
    {
        foreach (var issue in openIssues)
        {
            if (!IsOpen(issue.State))
            {
                continue;
            }

            var labels = LabelSet(issue.Labels);
            if (!labels.Contains(WorkerNextActionConstants.Labels.IntentTarget))
            {
                continue;
            }

            // Already claimed or delegated — not a stall in this category.
            if (labels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress)
                || labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated))
            {
                continue;
            }

            var executionUnit = ExecutionUnitFromTitle(issue.Title);
            if (domainRegex is not null && !domainRegex.IsMatch(executionUnit))
            {
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindPublishedNotDelegated,
                ExecutionUnit = executionUnit,
                Issue = new StalledWorkRef { Number = issue.Number, Url = issue.Url },
                Pr = null,
                AgeMinutes = ComputeAgeMinutes(issue.CreatedAt, now),
                RecommendedAction =
                    $"intent-cli worker claim --repo {repo} --kind issue --number {issue.Number} --github-only --write",
            });
        }
    }

    private static void CollectPrCreatedNotReviewing(
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        System.Text.RegularExpressions.Regex? domainRegex,
        string repo,
        DateTimeOffset now,
        List<StalledWorkItem> items)
    {
        var issuesWithPrCreated = openIssues
            .Where(issue => IsOpen(issue.State) && LabelSet(issue.Labels).Contains(WorkerNextActionConstants.Labels.IntentPrCreated))
            .ToDictionary(issue => issue.Number);

        if (issuesWithPrCreated.Count == 0)
        {
            return;
        }

        foreach (var pr in openPrs)
        {
            if (!IsOpen(pr.State) || pr.IsDraft)
            {
                continue;
            }

            var prLabels = LabelSet(pr.Labels);
            if (prLabels.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing)
                || prLabels.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrApproved))
            {
                continue;
            }

            GitHubAutomationIssueCandidate? matchedIssue = null;
            foreach (var reference in pr.ClosingIssuesReferences)
            {
                if (reference.Number > 0
                    && ReferenceMatchesRepo(reference, repo)
                    && issuesWithPrCreated.TryGetValue(reference.Number, out var candidate))
                {
                    matchedIssue = candidate;
                    break;
                }
            }

            if (matchedIssue is null)
            {
                continue;
            }

            var executionUnit = ExecutionUnitFromTitle(matchedIssue.Title);
            if (domainRegex is not null && !domainRegex.IsMatch(executionUnit))
            {
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindPrCreatedNotReviewing,
                ExecutionUnit = executionUnit,
                Issue = new StalledWorkRef { Number = matchedIssue.Number, Url = matchedIssue.Url },
                Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                AgeMinutes = ComputeAgeMinutes(pr.CreatedAt, now),
                RecommendedAction =
                    $"intent-cli automation pr-transition --repo {repo} --pr {pr.Number} --transition review-start --write",
            });
        }
    }

    private static void CollectMergedNotClosedOut(
        CliContext context,
        string domain,
        string repo,
        IReadOnlyList<GitHubAutomationPrCandidate> mergedPrs,
        System.Text.RegularExpressions.Regex? domainRegex,
        DateTimeOffset now,
        List<StalledWorkItem> items,
        List<string> warnings)
    {
        if (mergedPrs.Count == 0)
        {
            return;
        }

        var queueStateLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(context.RepoRoot, domain, repo);
        if (!File.Exists(queueStateLocation.Path))
        {
            warnings.Add($"queue-state not found at '{queueStateLocation.Path}'; skipped merged-not-closed-out check.");
            return;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStateLocation.Path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            warnings.Add($"queue-state at '{queueStateLocation.Path}' could not be parsed: {exception.Message}");
            return;
        }

        foreach (var pr in mergedPrs)
        {
            var prToken = pr.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var matchedItem = queueState.Items.FirstOrDefault(item => MatchesLinkedPr(item.LinkedPr, repo, prToken));
            if (matchedItem is null || matchedItem.State == QueueItemState.Completed)
            {
                continue;
            }

            if (domainRegex is not null && !domainRegex.IsMatch(matchedItem.ExecutionUnit))
            {
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindMergedNotClosedOut,
                ExecutionUnit = matchedItem.ExecutionUnit,
                Issue = matchedItem.LinkedIssue is { Number: { } linkedIssueNumber } linkedIssue
                    ? new StalledWorkRef { Number = linkedIssueNumber, Url = linkedIssue.Url ?? string.Empty }
                    : null,
                Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                // Best-effort merge-time proxy: `gh pr list` does not expose a
                // dedicated `mergedAt` field in the requested field set;
                // `updatedAt` is set to the merge time for a merged PR.
                AgeMinutes = ComputeAgeMinutes(pr.UpdatedAt, now),
                RecommendedAction =
                    $"intent-cli closeout pr --pr {pr.Number} --repo {repo} --domain {domain} --pr-merged true --write --format json",
            });
        }
    }

    private static bool IsOpen(string state) =>
        string.IsNullOrEmpty(state) || string.Equals(state, "OPEN", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> LabelSet(IReadOnlyList<GitHubAutomationLabel> labels) =>
        labels.Select(label => label.Name).ToHashSet(StringComparer.Ordinal);

    private static bool ReferenceMatchesRepo(GitHubPrClosingIssueReference reference, string repo)
    {
        if (reference.Repository is not { Name.Length: > 0, Owner.Login.Length: > 0 } repository)
        {
            // No repository descriptor — assume same-repo (gh omits it for
            // same-repo closing references in some field-set combinations).
            return true;
        }
        var candidateRepo = $"{repository.Owner!.Login}/{repository.Name}";
        return string.Equals(candidateRepo, repo, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLinkedPr(string? linkedPr, string repo, string prToken)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return false;
        }

        if (string.Equals(linkedPr, prToken, StringComparison.Ordinal))
        {
            return true;
        }

        if (linkedPr!.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return linkedPr.StartsWith($"https://github.com/{repo}/pull/", StringComparison.OrdinalIgnoreCase)
                && linkedPr.EndsWith($"/{prToken}", StringComparison.Ordinal);
        }

        return linkedPr!.EndsWith($"/{prToken}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Derives the candidate execution unit from a title following the
    /// established convention used across this repository's own issues/PRs
    /// (e.g. <c>"G523: Add automation stalled-work surface..."</c>).
    /// </summary>
    private static string ExecutionUnitFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }
        var colonIndex = title.IndexOf(':');
        return (colonIndex > 0 ? title[..colonIndex] : title).Trim();
    }

    private static int ComputeAgeMinutes(string timestamp, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(timestamp)
            || !DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return 0;
        }
        var minutes = (now - parsed).TotalMinutes;
        return minutes > 0 ? (int)minutes : 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? repo,
        out int staleMinutes,
        out string format,
        out string error)
    {
        domain = null;
        repo = null;
        staleMinutes = 0;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--stale-minutes":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedMinutes)
                        || parsedMinutes < 0)
                    {
                        error = "--stale-minutes requires a non-negative integer.";
                        return false;
                    }
                    staleMinutes = parsedMinutes;
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "automation stalled-work requires '--domain <name>'.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "automation stalled-work requires '--repo <owner/repo>'.";
            return false;
        }
        return true;
    }

    private static void WriteMarkdown(TextWriter writer, AutomationStalledWorkResult result)
    {
        writer.WriteLine($"# automation stalled-work — `{result.Domain}` / `{result.Repo}`");
        writer.WriteLine();
        writer.WriteLine($"- stale_minutes_threshold: {result.StaleMinutesThreshold}");
        writer.WriteLine($"- stalled: {(result.Stalled ? "true" : "false")}");
        writer.WriteLine($"- items: {result.Items.Count}");
        writer.WriteLine();

        if (result.Items.Count == 0)
        {
            writer.WriteLine("No stalled work detected.");
        }
        else
        {
            foreach (var item in result.Items)
            {
                writer.WriteLine($"## `{item.ExecutionUnit}` — {item.Kind} ({item.AgeMinutes}m)");
                if (item.Issue is { } issue)
                {
                    writer.WriteLine($"- issue: #{issue.Number} — {issue.Url}");
                }
                if (item.Pr is { } pr)
                {
                    writer.WriteLine($"- pr: #{pr.Number} — {pr.Url}");
                }
                writer.WriteLine($"- recommended_action: `{item.RecommendedAction}`");
                writer.WriteLine();
            }
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }
}

internal sealed record AutomationStalledWorkResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("stale_minutes_threshold")]
    public required int StaleMinutesThreshold { get; init; }

    [JsonPropertyName("stalled")]
    public required bool Stalled { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<StalledWorkItem> Items { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }
}

internal sealed record StalledWorkItem
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("issue")]
    public StalledWorkRef? Issue { get; init; }

    [JsonPropertyName("pr")]
    public StalledWorkRef? Pr { get; init; }

    [JsonPropertyName("age_minutes")]
    public required int AgeMinutes { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }
}

internal sealed record StalledWorkRef
{
    [JsonPropertyName("number")]
    public required int Number { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}
