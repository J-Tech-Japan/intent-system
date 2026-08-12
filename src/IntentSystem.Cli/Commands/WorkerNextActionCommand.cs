using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G206: <c>intent-cli worker next-action</c> command. Selects at most one
/// coding-automation target (PR repair / issue-to-PR / none) deterministically
/// per the priority order in #517.
///
/// No-mutation invariants (verified by tests):
/// - never invokes <c>NestedProviderLauncher</c>;
/// - command + analyzer files contain no <c>Process.Start</c>, no
///   <c>gh issue edit</c>/<c>gh pr edit</c>/<c>gh pr merge</c>/
///   <c>gh pr close</c>/<c>gh pr reopen</c>/<c>gh pr comment</c>/
///   <c>gh pr review</c>, no <c>resolveReviewThread</c> mutation;
/// - whole-workspace byte-snapshot before/after asserts no file is created
///   or modified;
/// - the only file in the worker next-action surface that calls
///   <c>Process.Start</c> is the lister adapter
///   <see cref="GhCliGitHubAutomationCandidateLister"/>, by design.
/// </summary>
internal static class WorkerNextActionCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>
    /// Test seam: tests inject a fake <see cref="IGitHubAutomationCandidateLister"/>
    /// here so no real GitHub network call is made. Production callers leave
    /// this null and the default <see cref="GhCliGitHubAutomationCandidateLister"/>
    /// is used.
    /// </summary>
    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    /// <summary>
    /// G392 test seam: PR comments / reviews / review-threads lookup used by the
    /// shared pr-comment-preflight consult on a selected <c>pr-comment-fix</c>
    /// candidate. Tests inject a fake <see cref="IGitHubPrCommentsLookup"/>;
    /// production callers leave this null and the default
    /// <see cref="GhCliGitHubPrCommentsLookup"/> is used. Reused from the G204
    /// preflight command so both surfaces share one classifier on one data shape.
    /// </summary>
    public static Func<IGitHubPrCommentsLookup>? CommentsLookupFactory { get; set; }

    /// <summary>
    /// G392 test seam: source-issue lookup used by the shared
    /// pr-comment-preflight consult. Tests inject a fake
    /// <see cref="IGitHubIssueLookup"/>; production callers leave this null and
    /// the default <see cref="GhCliGitHubIssueLookup"/> is used.
    /// </summary>
    public static Func<IGitHubIssueLookup>? IssueLookupFactory { get; set; }

    /// <summary>
    /// Test sentinel: must NEVER be invoked. Tests assert it remains
    /// uninvoked across all command paths to lock the "no nested provider
    /// launch" guarantee.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var repo, out var workdir, out var team, out var githubOnly, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IGitHubAutomationCandidateLister lister;
        try
        {
            lister = CandidateListerFactory?.Invoke()
                ?? new GhCliGitHubAutomationCandidateLister();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub lister: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationPrCandidate> prs;
        IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        try
        {
            prs = lister.ListPullRequests(
                repo!,
                new[] { WorkerNextActionConstants.Labels.IntentTarget },
                GitHubAutomationReadSurface.WorkerNextAction);
            issues = lister.ListIssues(
                repo!,
                new[] { WorkerNextActionConstants.Labels.IntentTarget },
                GitHubAutomationReadSurface.WorkerNextAction);
        }
        catch (GitHubApiRequestException exception)
        {
            var unavailable = BuildUnavailableResult(repo!, githubOnly, exception);
            if (string.Equals(format, FormatJson, StringComparison.Ordinal))
            {
                writer.WriteLine(JsonSerializer.Serialize(unavailable, JsonOptions));
            }
            else
            {
                WriteText(writer, unavailable);
            }

            // A quota blind spot is a named degraded observation, not a
            // successful empty selection and not an unclassified crash. The
            // caller decides whether/how to wait; the command does not.
            return exception.IsQuotaDegraded ? 0 : 1;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine(
                $"failed to list automation candidates for {repo}: {exception.Message}");
            return 1;
        }

        WorkerNextActionResult result;
        try
        {
            result = WorkerNextActionAnalyzer.Analyze(repo!, prs, issues);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException)
        {
            writer.WriteLine($"failed to classify next-action for {repo}: {exception.Message}");
            return 1;
        }

        // G392: when the label/closing-ref selector picked a `pr-comment-fix`
        // target, consult the shared `worker pr-comment-preflight` classifier on
        // that one PR (fetching the comments + source issue the label selector
        // cannot see). If preflight reports actionable:false the two surfaces
        // would otherwise disagree — so the selector downgrades its own choice
        // to a stable `wait` rather than handing the child loop a PR that
        // preflight would refuse to claim. Fail-open: any lookup/analyze error
        // keeps the analyzer's decision (the consult can only ever make the
        // selection MORE conservative, never less).
        var resolvedWorkdir = string.IsNullOrWhiteSpace(workdir)
            ? Directory.GetCurrentDirectory()
            : workdir!;
        result = ConsultPreflightForPrCommentFix(result, repo!, resolvedWorkdir, prs);
        result = ConsultExecutionUnitClaim(result, context.RepoRoot, team, issues);

        // G281: --workdir is child worktree CONTEXT only — selection runs against
        // GitHub state for --repo, never against the workdir's local filesystem.
        // We surface diagnostic warnings when workdir is supplied but does not
        // look like a git worktree (or does not exist), without ever flipping
        // the selection to no-actionable. The parent host's `.intent-cli`
        // remains the durable state root from the command's cwd.
        if (!string.IsNullOrWhiteSpace(workdir))
        {
            var workdirWarnings = new List<string>(result.Warnings);
            foreach (var warning in BuildWorkdirWarnings(workdir!))
            {
                workdirWarnings.Add(warning);
            }
            if (workdirWarnings.Count != result.Warnings.Count)
            {
                result = result with { Warnings = workdirWarnings };
            }
        }

        // G333: surface the strict child-loop assertion on the result.
        if (githubOnly)
        {
            result = result with { GithubOnly = true };
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    private static WorkerNextActionResult BuildUnavailableResult(
        string repo,
        bool githubOnly,
        GitHubApiRequestException exception) =>
        new()
        {
            Action = WorkerNextActionConstants.Actions.Unavailable,
            Repo = repo,
            Number = null,
            Url = null,
            Reason = exception.Message,
            RecommendedWorkflow = null,
            Warnings = new[]
            {
                "GitHub consultation was unavailable; do not interpret this as an empty actionable set.",
            },
            MustCreatePr = null,
            AllowedTerminalOutcomes = null,
            ForbiddenTerminalOutcomes = null,
            SourceClassification = exception.Cause,
            GithubOnly = githubOnly,
            GithubApiStatus = exception.IsQuotaDegraded
                ? GitHubApiQuotaConstants.Degraded
                : GitHubApiQuotaConstants.Error,
            Degraded = exception.IsQuotaDegraded,
            Cause = exception.Cause,
            DegradedState = exception.DegradedState,
        };

    /// <summary>
    /// G392: shared-actionability consult. When the label/closing-ref selector
    /// chose <c>pr-comment-fix</c>, run the canonical
    /// <see cref="WorkerPrCommentPreflightAnalyzer"/> on that single PR — the
    /// same classifier <c>worker pr-comment-preflight</c> uses — and downgrade
    /// the result to a stable <c>wait</c> if preflight reports
    /// <c>actionable:false</c>. This guarantees the two surfaces never disagree
    /// on whether a PR is child-claimable.
    ///
    /// Fail-open by design: the consult only ever turns a <c>pr-comment-fix</c>
    /// into a <c>wait</c> when a SUCCESSFUL preflight analysis says the PR is
    /// not actionable. Any lookup or analyze error (transient <c>gh</c> failure,
    /// missing candidate) leaves the analyzer's decision untouched, so the
    /// consult can only make the selection more conservative, never less — and a
    /// transient comment-fetch failure cannot abort the selector.
    /// </summary>
    private static WorkerNextActionResult ConsultPreflightForPrCommentFix(
        WorkerNextActionResult result,
        string repo,
        string resolvedWorkdir,
        IReadOnlyList<GitHubAutomationPrCandidate> prs)
    {
        if (!string.Equals(result.Action, WorkerNextActionConstants.Actions.PrCommentFix, StringComparison.Ordinal)
            || result.Number is not { } prNumber)
        {
            return result;
        }

        var candidate = prs.FirstOrDefault(pr => pr.Number == prNumber);
        if (candidate is null)
        {
            return result;
        }

        WorkerPrCommentPreflightResult preflight;
        try
        {
            var prPayload = BuildPrLookupResult(candidate);

            var commentsLookup = CommentsLookupFactory?.Invoke() ?? new GhCliGitHubPrCommentsLookup();
            var commentsPayload = commentsLookup.Lookup(repo, prNumber);
            if (commentsPayload is null)
            {
                return result;
            }

            var sourceCandidate = WorkerPrReviewPreflightAnalyzer.TraceSourceIssue(prPayload, repo);
            GitHubIssueLookupResult? sourceIssuePayload = null;
            if (sourceCandidate is { } traced)
            {
                var issueLookup = IssueLookupFactory?.Invoke() ?? new GhCliGitHubIssueLookup();
                sourceIssuePayload = issueLookup.Lookup(traced.Repo, traced.Number);
            }

            preflight = WorkerPrCommentPreflightAnalyzer.Analyze(
                prPayload,
                commentsPayload,
                repo,
                prNumber,
                resolvedWorkdir,
                sourceCandidate,
                sourceIssuePayload);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException
            or ArgumentException
            or FormatException)
        {
            // Fail-open: keep the analyzer's pr-comment-fix decision. The worker
            // will still surface a non-actionable PR post-claim via its G372
            // terminal outcomes (already-resolved / host-artifact-repair).
            return result;
        }

        if (preflight.Actionable)
        {
            return result;
        }

        return result with
        {
            Action = WorkerNextActionConstants.Actions.Wait,
            Reason = $"label/closing-ref selector picked PR #{prNumber} as pr-comment-fix, but the shared "
                + $"worker pr-comment-preflight classifier reports actionable=false "
                + $"(classification={preflight.Classification}); the child loop must not claim it. "
                + "Route per the preflight classification (e.g. wait for actionable review feedback, or "
                + "escalate host-metadata-only feedback to the host repair lane).",
            SourceClassification = WorkerNextActionConstants.SourceClassifications.PrCommentPreflightNotActionable,
            MustCreatePr = null,
            AllowedTerminalOutcomes = null,
            ForbiddenTerminalOutcomes = null,
        };
    }

    /// <summary>
    /// G680: an issue-to-PR action starts execution-unit work, so the selected
    /// issue must pass the same Git-backed ownership judgment as packet draft
    /// and publish. Hosts without a claims store return the analyzer result
    /// byte-for-byte unchanged. Existing label exclusions remain in the
    /// analyzer as defence in depth.
    /// </summary>
    private static WorkerNextActionResult ConsultExecutionUnitClaim(
        WorkerNextActionResult result,
        string repoRoot,
        string? team,
        IReadOnlyList<GitHubAutomationIssueCandidate> issues)
    {
        if (!string.Equals(result.Action, WorkerNextActionConstants.Actions.IssueToPr, StringComparison.Ordinal)
            || result.Number is not { } issueNumber
            || !Directory.Exists(Path.Combine(repoRoot, ClaimCommand.ClaimsDirectory.Replace('/', Path.DirectorySeparatorChar))))
        {
            return result;
        }

        var issue = issues.FirstOrDefault(candidate => candidate.Number == issueNumber);
        var executionUnit = issue is null ? null : LeadingExecutionUnitPattern.Match(issue.Title).Value;
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            return result with
            {
                Action = WorkerNextActionConstants.Actions.Wait,
                Reason = $"claim verification refused issue #{issueNumber}: could not resolve a leading execution-unit token from the issue title on a claims-enabled host.",
                SourceClassification = WorkerNextActionConstants.SourceClassifications.ClaimScopeUnresolved,
                MustCreatePr = null,
                AllowedTerminalOutcomes = null,
                ForbiddenTerminalOutcomes = null,
            };
        }

        var verification = ClaimOwnershipVerifier.Verify(
            repoRoot, $"execution-unit:{executionUnit}", team);
        if (verification.Passed)
        {
            return result;
        }

        return result with
        {
            Action = WorkerNextActionConstants.Actions.Wait,
            Reason = verification.Detail,
            SourceClassification = WorkerNextActionConstants.SourceClassifications.ClaimRefused,
            MustCreatePr = null,
            AllowedTerminalOutcomes = null,
            ForbiddenTerminalOutcomes = null,
        };
    }

    private static readonly System.Text.RegularExpressions.Regex LeadingExecutionUnitPattern = new(
        @"^(?:[A-Z][A-Z0-9]*-G?[0-9]+|G[0-9]+)(?![A-Za-z0-9])",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// G392: project a <see cref="GitHubAutomationPrCandidate"/> (already
    /// fetched by the label selector with body / labels / closing-ref / state /
    /// draft fields) into the <see cref="GitHubPrLookupResult"/> shape the
    /// shared preflight classifier consumes — so the consult needs no extra PR
    /// lookup, only the comments + source-issue fetches the selector lacks.
    /// </summary>
    private static GitHubPrLookupResult BuildPrLookupResult(GitHubAutomationPrCandidate candidate)
    {
        return new GitHubPrLookupResult
        {
            Number = candidate.Number,
            State = candidate.State,
            Title = candidate.Title,
            Body = candidate.Body,
            IsDraft = candidate.IsDraft,
            Closed = string.Equals(candidate.State, "CLOSED", StringComparison.OrdinalIgnoreCase),
            Merged = string.Equals(candidate.State, "MERGED", StringComparison.OrdinalIgnoreCase),
            Labels = candidate.Labels
                .Select(label => new GitHubPrLabel { Name = label.Name })
                .ToArray(),
            ClosingIssuesReferences = candidate.ClosingIssuesReferences,
        };
    }

    /// <summary>
    /// G281: emit advisory warnings when <c>--workdir</c> is supplied but does
    /// not look like a usable child git worktree. Selection itself does NOT
    /// depend on the child workdir state — the warnings are operator-facing
    /// hints that the worktree may be missing or stale before they try to
    /// run the chosen workflow against it.
    /// </summary>
    internal static IEnumerable<string> BuildWorkdirWarnings(string workdir)
    {
        if (!Directory.Exists(workdir))
        {
            yield return $"workdir '{workdir}' does not exist; selection used GitHub state from --repo only";
            yield break;
        }

        var gitPath = Path.Combine(workdir, ".git");
        if (!Directory.Exists(gitPath) && !File.Exists(gitPath))
        {
            yield return $"workdir '{workdir}' is not a git worktree (no .git entry); selection used GitHub state from --repo only";
        }
    }

    private static void WriteText(TextWriter writer, WorkerNextActionResult result)
    {
        writer.WriteLine($"# Worker next-action for {result.Repo}");
        writer.WriteLine();
        writer.WriteLine($"- action: {result.Action}");
        if (result.Number is { } number)
        {
            writer.WriteLine($"- number: {number}");
        }
        if (!string.IsNullOrEmpty(result.Url))
        {
            writer.WriteLine($"- url: {result.Url}");
        }
        writer.WriteLine($"- reason: {result.Reason}");
        if (!string.IsNullOrEmpty(result.RecommendedWorkflow))
        {
            writer.WriteLine($"- recommended_workflow: {result.RecommendedWorkflow}");
        }
        if (!string.IsNullOrEmpty(result.SourceClassification))
        {
            writer.WriteLine($"- source_classification: {result.SourceClassification}");
        }
        writer.WriteLine($"- github_api_status: {result.GithubApiStatus}");
        writer.WriteLine($"- degraded: {result.Degraded.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrEmpty(result.Cause))
        {
            writer.WriteLine($"- cause: {result.Cause}");
        }
        if (result.DegradedState is { } degraded)
        {
            writer.WriteLine($"- resource: {degraded.Resource}");
            writer.WriteLine($"- remaining: {degraded.Remaining?.ToString() ?? "unknown"}");
            writer.WriteLine($"- reset_at: {degraded.ResetAt ?? degraded.Reset?.ToString() ?? "unknown"}");
        }
        writer.WriteLine();

        writer.WriteLine("## Warnings");
        if (result.Warnings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out string? team,
        out bool githubOnly,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        team = null;
        githubOnly = false;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1];
                    index++;
                    break;

                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }
                    workdir = args[index + 1];
                    index++;
                    break;

                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a value.";
                        return false;
                    }
                    team = args[index + 1];
                    index++;
                    break;

                case "--github-only":
                    // G333: strict child-loop assertion. `worker next-action`
                    // is already GitHub-label-based by data flow — no
                    // queue-state read; selection comes from `gh issue
                    // list` / `gh pr list` via the candidate lister.
                    // The flag explicitly records the binding on the
                    // result so the host loop / operator can audit.
                    githubOnly = true;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --repo <owner/repo> [--workdir <path>] [--team <team>] [--github-only] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }

        return true;
    }
}
