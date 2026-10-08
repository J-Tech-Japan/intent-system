using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class UnitStatusReadAdapterG855Tests
{
    private const string Repo = "J-Tech-Japan/intent-system";
    private const int Issue = 1862;
    private const int PullRequest = 1864;
    private const string Head = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Head2 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void GithubSnapshotUsesOnlyBoundedGetRequestsAndBracketsTheCurrentHead()
    {
        using var host = new TempHost();
        var runner = new FakeGitHub((arguments, _) => DefaultResponse(arguments));
        var adapter = new UnitStatusReadAdapter(runner, new FakeGit());

        var snapshot = adapter.ObserveGitHub(host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal("completed", snapshot.State);
        Assert.Equal(Head, snapshot.HeadBefore);
        Assert.Equal(Head, snapshot.HeadAfter);
        Assert.Equal(Head, snapshot.HeadSha);
        Assert.Equal(6, runner.Calls.Count);
        Assert.All(runner.Calls, call =>
        {
            Assert.Equal("api", call[0]);
            Assert.Equal("--method", call[1]);
            Assert.Equal("GET", call[2]);
            Assert.DoesNotContain("graphql", call, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("POST", call, StringComparer.OrdinalIgnoreCase);
        });
        Assert.Equal(2, runner.Calls.Count(call => call[3] == $"repos/{Repo}/pulls/{PullRequest}"));
        Assert.Contains(runner.Calls, call => call[3] == $"repos/{Repo}/issues/{Issue}");
        Assert.Contains(runner.Calls, call => call[3].StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?per_page=100&page=1", StringComparison.Ordinal));
        Assert.Contains(runner.Calls, call => call[3].StartsWith($"repos/{Repo}/commits/{Head}/check-runs?per_page=100&page=1", StringComparison.Ordinal));
        Assert.Contains(runner.Calls, call => call[3].StartsWith($"repos/{Repo}/commits/{Head}/statuses?per_page=100&page=1", StringComparison.Ordinal));
        Assert.Equal(UnitStatusStates.Missing, snapshot.Facts.Single(fact => fact.Id == "issue-completion-marker").State);
        Assert.Equal(UnitStatusStates.Missing, snapshot.Facts.Single(fact => fact.Id == "observed-ci").State);
        Assert.Equal(UnitStatusStates.Missing, snapshot.Facts.Single(fact => fact.Id == "approval-head-receipt").State);
    }

    [Fact]
    public void CommitStatusesUseTheExactShaRequestWhenDocumentedRowsOmitSha()
    {
        using var host = new TempHost();
        var runner = new FakeGitHub((arguments, _) =>
        {
            if (arguments[3].StartsWith($"repos/{Repo}/commits/{Head}/statuses?", StringComparison.Ordinal))
            {
                return Json("""
                    [{"id":91,"context":"ci/unit","state":"success","description":"all good","target_url":"https://ci.example.test/run/91","created_at":"2026-10-07T00:00:00Z","updated_at":"2026-10-07T00:00:01Z","creator":{"login":"ci-bot"}}]
                    """);
            }

            return DefaultResponse(arguments);
        });

        var snapshot = new UnitStatusReadAdapter(runner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal("completed", snapshot.State);
        var status = Assert.Single(snapshot.Checks);
        Assert.Equal("commit-status", status.Source);
        Assert.Equal("ci/unit", status.Identity);
        Assert.Equal("91", status.RecordId);
        Assert.Equal(Head, status.Sha);
        Assert.Equal(UnitStatusStates.Done, snapshot.Facts.Single(fact => fact.Id == "observed-ci").State);

        var wrongShaRunner = new FakeGitHub((arguments, _) =>
            arguments[3].StartsWith($"repos/{Repo}/commits/{Head}/statuses?", StringComparison.Ordinal)
                ? Json($"[{{\"id\":92,\"context\":\"ci/unit\",\"state\":\"success\",\"sha\":\"{Head2}\"}}]")
                : DefaultResponse(arguments));
        var wrongSha = new UnitStatusReadAdapter(wrongShaRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(UnitStatusStates.Unavailable, wrongSha.Facts.Single(fact => fact.Id == "observed-ci").State);
        Assert.Equal(UnitStatusStates.ReadFailure, wrongSha.Facts.Single(fact => fact.Id == "observed-ci").UnavailableClass);
    }

    [Fact]
    public void ReadAdapterRejectsUnlistedGithubRequestsBeforeRunner()
    {
        using var host = new TempHost();
        var runner = new FakeGitHub((arguments, _) => DefaultResponse(arguments));
        var adapter = new UnitStatusReadAdapter(runner, new FakeGit());
        IReadOnlyList<string>[] invalidRequests =
        [
            ["api", "--method", "POST", $"repos/{Repo}/issues/{Issue}"],
            ["api", "--method", "GET", $"repos/{Repo}/issues/{Issue}", "--paginate"],
            ["api", "--method", "GET", $"https://api.github.com/repos/{Repo}/issues/{Issue}"],
            ["api", "--method", "GET", $"repos/../intent-system/issues/{Issue}"],
            ["api", "--method", "GET", $"repos/{Repo}/issues/{Issue}?per_page=100&page=1"],
            ["api", "--method", "GET", $"repos/{Repo}/pulls/{PullRequest}/reviews?per_page=100&page=21"],
            ["api", "--method", "GET", $"repos/{Repo}/pulls/{PullRequest}/reviews?per_page=100&page=1&state=all"],
            ["api", "--method", "GET", "graphql", "-f", "query=mutation"],
        ];

        foreach (var arguments in invalidRequests)
        {
            Assert.False(adapter.TryReadJson(host.Context, arguments, out var document, out _));
            Assert.Null(document);
        }

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void ReadAdapterRejectsUnlistedGitCommandsAndEscapingPathsBeforeRunner()
    {
        using var host = new TempHost();
        var runner = new FakeGit();
        var adapter = new UnitStatusReadAdapter(new FakeGitHub((arguments, _) => DefaultResponse(arguments)), runner);
        var claimPath = ClaimCommand.ClaimPath("execution-unit:G855");
        var historyDirectory = $"{ClaimCommand.ClaimsDirectory}/history/{Path.GetFileNameWithoutExtension(claimPath)}";

        IReadOnlyList<string>[] invalidCommands =
        [
            ["fetch", "origin", "main"],
            ["reset", "--hard", "HEAD"],
            ["rev-parse", "--verify", "HEAD", "--quiet"],
            ["rev-parse", "--verify", "refs/remotes/origin/metadata^{commit}"],
            ["ls-tree", "-r", "-z", "--name-only", "HEAD", "--", claimPath, historyDirectory],
            ["ls-tree", "-r", "-z", "--name-only", Head, "--", claimPath, "../outside"],
            ["show", $"{Head}:../outside/secret.json"],
        ];

        foreach (var arguments in invalidCommands)
        {
            Assert.Null(adapter.ReadGitText(host.Context.RepoRoot, arguments, out _));
        }

        Assert.Null(adapter.ReadGitText(
            host.Context.RepoRoot,
            ["ls-tree", "-r", "-z", "--name-only", Head, "--", ".intent-cli/claims/" + new string('a', 64) + ".json", ".intent-cli/claims/history/" + new string('a', 64)],
            out _,
            snapshotOid: Head,
            claimPath: ".intent-cli/claims/" + new string('a', 64) + ".json",
            historyDirectory: ".intent-cli/claims/history/" + new string('b', 64)));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void CanonicalCrossRuntimeAndGenericIndependentReviewBodiesBindToHeadAndExposeVerdict()
    {
        using var host = new TempHost();
        var body = $$"""
## Cross-runtime review: approve
- Reviewer: cross-runtime review
- Runtime: claude
- Conductor runtime: codex
- Kind: implementation
- Execution unit: G855
- Head SHA: {{Head}}
        - Verdict: approve
""";
        var runner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? Json(ReviewArray(body, Head))
            : DefaultResponse(arguments));
        var adapter = new UnitStatusReadAdapter(runner, new FakeGit());

        var snapshot = adapter.ObserveGitHub(host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal("completed", snapshot.State);
        var posted = Assert.Single(snapshot.Facts, fact => fact.Id == "posted-review");
        Assert.Equal(UnitStatusStates.Done, posted.State);
        var review = Assert.Single(snapshot.Reviews);
        Assert.Equal("approve", review.Verdict);
        Assert.Equal("cross-runtime", review.Relation);
        Assert.Equal(UnitStatusStates.Done, snapshot.Facts.Single(fact => fact.Id == "delta-review").State);
    }

    [Fact]
    public void CanonicalSameRuntimeReviewBindsToHeadAndPreservesRequestChangesVerdict()
    {
        using var host = new TempHost();
        var body = $$"""
## Independent same-runtime subagent review: request-changes
- reviewer: independent same-runtime subagent review
- runtime: codex
- runtime version: codex-test
- conductor runtime: codex
- head SHA: {{Head}}
- kind: implementation
- execution unit: G855
- verdict: request-changes
""";
        var runner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? Json(ReviewArray(body, Head))
            : DefaultResponse(arguments));
        var adapter = new UnitStatusReadAdapter(runner, new FakeGit());

        var snapshot = adapter.ObserveGitHub(host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        var posted = Assert.Single(snapshot.Reviews);
        Assert.Equal("same-runtime", posted.Relation);
        Assert.Equal("request-changes", posted.Verdict);
        Assert.Equal(UnitStatusStates.Done, snapshot.Facts.Single(fact => fact.Id == "posted-review").State);
    }

    [Fact]
    public void PriorHeadReviewRemainsVisibleAndDoesNotBlockCurrentHeadReview()
    {
        using var host = new TempHost();
        string Body(string head, string verdict) => $$"""
## Independent same-runtime subagent review: {{verdict}}
- reviewer: independent same-runtime subagent review
- runtime: codex
- runtime version: codex-test
- conductor runtime: codex
- head SHA: {{head}}
- kind: implementation
- execution unit: G855
- verdict: {{verdict}}
""";

        var staleOnlyRunner = new FakeGitHub((arguments, _) =>
        {
            if (arguments[3] == $"repos/{Repo}/pulls/{PullRequest}") return Json(PullJson(Head2));
            if (arguments[3].StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?", StringComparison.Ordinal))
                return Json(ReviewArray(Body(Head, "request-changes"), Head));
            if (arguments[3].StartsWith($"repos/{Repo}/commits/{Head2}/check-runs?", StringComparison.Ordinal))
                return Json("{\"total_count\":0,\"check_runs\":[]}");
            if (arguments[3].StartsWith($"repos/{Repo}/commits/{Head2}/statuses?", StringComparison.Ordinal)) return Json("[]");
            return DefaultResponse(arguments);
        });
        var staleOnly = new UnitStatusReadAdapter(staleOnlyRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal("completed", staleOnly.State);
        Assert.Equal(Head, Assert.Single(staleOnly.Reviews).HeadSha);
        var stalePosted = staleOnly.Facts.Single(fact => fact.Id == "posted-review");
        Assert.Equal(UnitStatusStates.Missing, stalePosted.State);
        Assert.Equal("posted-review-not-recorded-current-head", stalePosted.Cause);
        var staleDelta = staleOnly.Facts.Single(fact => fact.Id == "delta-review");
        Assert.Equal(UnitStatusStates.Missing, staleDelta.State);
        Assert.Equal("delta-review-missing", staleDelta.Cause);

        var bothHeadsRunner = new FakeGitHub((arguments, _) =>
        {
            if (arguments[3] == $"repos/{Repo}/pulls/{PullRequest}") return Json(PullJson(Head2));
            if (arguments[3].StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?", StringComparison.Ordinal))
                return Json(ReviewRows(
                    (Body(Head, "request-changes"), Head, "CHANGES_REQUESTED"),
                    (Body(Head2, "approve"), Head2, "APPROVED")));
            if (arguments[3].StartsWith($"repos/{Repo}/commits/{Head2}/check-runs?", StringComparison.Ordinal))
                return Json("{\"total_count\":0,\"check_runs\":[]}");
            if (arguments[3].StartsWith($"repos/{Repo}/commits/{Head2}/statuses?", StringComparison.Ordinal)) return Json("[]");
            return DefaultResponse(arguments);
        });
        var bothHeads = new UnitStatusReadAdapter(bothHeadsRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal("completed", bothHeads.State);
        Assert.Equal(2, bothHeads.Reviews.Count);
        Assert.Contains(bothHeads.Reviews, review => review.HeadSha == Head && review.Verdict == "request-changes");
        Assert.Contains(bothHeads.Reviews, review => review.HeadSha == Head2 && review.Verdict == "approve");
        Assert.Equal(UnitStatusStates.Done, bothHeads.Facts.Single(fact => fact.Id == "posted-review").State);
        Assert.Equal("delta-review-current-head", bothHeads.Facts.Single(fact => fact.Id == "delta-review").Cause);
    }

    [Fact]
    public void SquashMergeCommitDoesNotReplacePrHeadForCurrentReviewIdentity()
    {
        using var host = new TempHost();
        var body = $$"""
## Independent same-runtime subagent review: request-changes
- reviewer: independent same-runtime subagent review
- runtime: codex
- runtime version: codex-test
- conductor runtime: codex
- head SHA: {{Head}}
- kind: implementation
- execution unit: G855
- verdict: request-changes
""";
        var runner = new FakeGitHub((arguments, _) =>
        {
            if (arguments[3] == $"repos/{Repo}/pulls/{PullRequest}")
                return Json($"{{\"number\":{PullRequest},\"head\":{{\"sha\":\"{Head}\"}},\"merged\":true,\"merge_commit_sha\":\"{Head2}\",\"labels\":[]}}");
            if (arguments[3].StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?", StringComparison.Ordinal))
                return Json(ReviewArray(body, Head));
            return DefaultResponse(arguments);
        });

        var snapshot = new UnitStatusReadAdapter(runner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal("completed", snapshot.State);
        Assert.Equal(Head, snapshot.HeadSha);
        Assert.Equal(Head2, snapshot.MergeCommitSha);
        Assert.True(snapshot.Merged);
        Assert.Equal(UnitStatusStates.Done, snapshot.Facts.Single(fact => fact.Id == "posted-review").State);
        Assert.Equal("delta-review-current-head", snapshot.Facts.Single(fact => fact.Id == "delta-review").Cause);
        Assert.Equal(Head, Assert.Single(snapshot.Reviews).HeadSha);
    }

    [Fact]
    public void ReviewRowsRetainOrderedVerdictsAndRejectTiedConflictingIdentity()
    {
        using var host = new TempHost();
        string Body(string verdict) => $$"""
## Independent same-runtime subagent review: {{verdict}}
- reviewer: independent same-runtime subagent review
- runtime: codex
- runtime version: codex-test
- conductor runtime: codex
- head SHA: {{Head}}
- kind: implementation
- execution unit: G855
- verdict: {{verdict}}
""";
        var orderedRunner = new FakeGitHub((arguments, _) =>
            arguments[3].StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?", StringComparison.Ordinal)
                ? Json(ReviewRows(
                    (Body("request-changes"), Head, "CHANGES_REQUESTED"),
                    (Body("approve"), Head, "APPROVED")))
                : DefaultResponse(arguments));
        var ordered = new UnitStatusReadAdapter(orderedRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(new[] { "request-changes", "approve" }, ordered.Reviews.Select(review => review.Verdict));
        Assert.Equal(UnitStatusStates.Done, ordered.Facts.Single(fact => fact.Id == "posted-review").State);

        var sameSubmissionTime = "2026-10-07T00:00:00Z";
        var tiedRunner = new FakeGitHub((arguments, _) =>
            arguments[3].StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?", StringComparison.Ordinal)
                ? Json(ReviewRowsWithIds(
                    (7, Body("request-changes"), Head, "CHANGES_REQUESTED", sameSubmissionTime),
                    (7, Body("approve"), Head, "APPROVED", sameSubmissionTime)))
                : DefaultResponse(arguments));
        var tied = new UnitStatusReadAdapter(tiedRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        var posted = tied.Facts.Single(fact => fact.Id == "posted-review");
        Assert.Equal(UnitStatusStates.Unavailable, posted.State);
        Assert.Equal("review-identity-conflict", posted.Cause);
        Assert.Equal(UnitStatusStates.IdentityConflict, posted.UnavailableClass);
    }

    [Fact]
    public void GenericStructuredIndependentReviewIsAcceptedWithoutADeclaredTeamRelation()
    {
        using var host = new TempHost();
        const string historicalTeam = "team-from-legacy-record";
        var body = $$"""
## Independent subagent review: approve
- reviewer: independent subagent review
- kind: implementation
- execution unit: G855
- head SHA: {{Head}}
- verdict: approve
""";
        var runner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? Json(ReviewArray(body, Head))
            : DefaultResponse(arguments));

        var snapshot = new UnitStatusReadAdapter(runner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", historicalTeam);

        var review = Assert.Single(snapshot.Reviews);
        Assert.Null(review.Relation);
        Assert.Equal("approve", review.Verdict);
        Assert.Equal(UnitStatusStates.Done, snapshot.Facts.Single(fact => fact.Id == "posted-review").State);
    }

    [Fact]
    public void CitedCrossRuntimeReviewMustResolveToDigestValidatedLocalRecord()
    {
        using var host = new TempHost();
        G839CrossRuntimeReviewRecordWriter.WriteImplementationRecord(
            host.Context.RepoRoot, Repo, PullRequest, "G855", "intent-cli", "intent-cli-dev", "codex", "approve", Head,
            DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
        var recordPath = Assert.Single(CrossRuntimeReviewStore.Read(host.Context.RepoRoot, Repo, PullRequest).Records).RelativePath;
        string Body(string citedRecord) => $$"""
## Cross-runtime review: approve
- reviewer: cross-runtime review
- runtime: codex
- runtime version: codex-test
- conductor runtime: claude
- head SHA: {{Head}}
- kind: implementation
- execution unit: G855
- verdict: approve

Recorded as `{{citedRecord}}` by `intent-cli review cross-runtime record`.
""";

        var validRunner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? Json(ReviewArray(Body(recordPath), Head))
            : DefaultResponse(arguments));
        var valid = new UnitStatusReadAdapter(validRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(recordPath, Assert.Single(valid.Reviews).CitedRecordPath);
        Assert.Equal(UnitStatusStates.Done, valid.Facts.Single(fact => fact.Id == "posted-review").State);

        var unavailableRunner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? new GitHubCommandResult { ExitCode = 1, StdOut = "", StdErr = "HTTP 503 unavailable" }
            : DefaultResponse(arguments));
        var localOnly = new UnitStatusReadAdapter(unavailableRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(UnitStatusStates.Unavailable, localOnly.State);
        var unreadableHistory = localOnly.Facts.Single(fact => fact.Id == "delta-review");
        Assert.Equal(UnitStatusStates.Unavailable, unreadableHistory.State);
        Assert.Equal(UnitStatusStates.ReadFailure, unreadableHistory.UnavailableClass);

        var localWithReadableRemote = new UnitStatusReadAdapter(
            new FakeGitHub((arguments, _) => DefaultResponse(arguments)), new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(UnitStatusStates.Done, localWithReadableRemote.Facts.Single(fact => fact.Id == "delta-review").State);

        var invalidRunner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? Json(ReviewArray(Body(".intent-cli/reviews/not-recorded.json"), Head))
            : DefaultResponse(arguments));
        var invalid = new UnitStatusReadAdapter(invalidRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(UnitStatusStates.IdentityConflict, invalid.Facts.Single(fact => fact.Id == "posted-review").UnavailableClass);
    }

    [Fact]
    public void ConflictingStructuredReviewIdentityIsUnavailableAndDismissedReviewDoesNotSatisfy()
    {
        using var host = new TempHost();
        var conflicted = $$"""
## Cross-runtime review: approve
- reviewer: cross-runtime review
- runtime: claude
- runtime version: claude-test
- conductor runtime: codex
- head SHA: {{Head}}
- head SHA: {{Head2}}
- kind: implementation
- execution unit: G855
- verdict: approve
""";
        var conflictRunner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? Json(ReviewArray(conflicted, Head))
            : DefaultResponse(arguments));
        var conflict = new UnitStatusReadAdapter(conflictRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(UnitStatusStates.Unavailable, conflict.State);
        Assert.Equal(UnitStatusStates.IdentityConflict, conflict.Facts.Single(fact => fact.Id == "posted-review").UnavailableClass);

        var dismissedBody = $$"""
## Cross-runtime review: approve
- reviewer: cross-runtime review
- runtime: claude
- runtime version: claude-test
- conductor runtime: codex
- head SHA: {{Head}}
- kind: implementation
- execution unit: G855
- verdict: approve
""";
        var dismissedRunner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? Json(ReviewArray(dismissedBody, Head, "DISMISSED"))
            : DefaultResponse(arguments));
        var dismissed = new UnitStatusReadAdapter(dismissedRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal("DISMISSED", Assert.Single(dismissed.Reviews).ReviewState);
        Assert.Equal(UnitStatusStates.Missing, dismissed.Facts.Single(fact => fact.Id == "posted-review").State);
        Assert.Contains(dismissed.Warnings, warning => warning.Contains("dismissed", StringComparison.Ordinal));
    }

    [Fact]
    public void NamedUnstructuredReviewIsProvenanceLimitAndDismissedReviewDoesNotSatisfy()
    {
        using var host = new TempHost();
        var unstructured = "Reviewer: independent subagent review\nNotes: historical prose without unit/head identity";
        var runner = new FakeGitHub((arguments, _) => arguments[3].EndsWith("/reviews?per_page=100&page=1", StringComparison.Ordinal)
            ? Json(ReviewArray(unstructured, null))
            : DefaultResponse(arguments));
        var adapter = new UnitStatusReadAdapter(runner, new FakeGit());

        var snapshot = adapter.ObserveGitHub(host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal("completed", snapshot.State);
        var posted = snapshot.Facts.Single(fact => fact.Id == "posted-review");
        Assert.Equal(UnitStatusStates.Unavailable, posted.State);
        Assert.Equal("legacy-review-identity-unrecorded", posted.Cause);
        Assert.Equal(UnitStatusStates.ProvenanceLimit, posted.UnavailableClass);
        Assert.Equal(UnitStatusStates.Unavailable, snapshot.Facts.Single(fact => fact.Id == "delta-review").State);
    }

    [Fact]
    public void ActionsAttemptAndSkippedExternalCheckRemainInCompleteInventory()
    {
        using var host = new TempHost();
        var runner = new FakeGitHub((arguments, _) => arguments[3] switch
        {
            var endpoint when endpoint.StartsWith($"repos/{Repo}/commits/{Head}/check-runs", StringComparison.Ordinal) => Json("""
                {"total_count":2,"check_runs":[
                  {"id":11,"name":"build","status":"completed","conclusion":"success","head_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","app":{"slug":"github-actions"},"details_url":"https://github.com/J-Tech-Japan/intent-system/actions/runs/77"},
                  {"id":12,"name":"lint","status":"completed","conclusion":"skipped","head_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","app":{"slug":"external-ci"},"details_url":null}
                ]}
                """),
            $"repos/{Repo}/actions/runs/77" => Json("""{"id":77,"head_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","run_attempt":2}"""),
            _ => DefaultResponse(arguments),
        });
        var adapter = new UnitStatusReadAdapter(runner, new FakeGit());

        var snapshot = adapter.ObserveGitHub(host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal("completed", snapshot.State);
        Assert.Equal(2, snapshot.Checks.Count);
        var actions = snapshot.Checks.Single(check => check.Identity == "github-actions/build");
        Assert.Equal(77, actions.RunId);
        Assert.Equal(2, actions.Attempt);
        var external = snapshot.Checks.Single(check => check.Identity == "external-ci/lint");
        Assert.Null(external.RunId);
        Assert.Null(external.Attempt);
        Assert.Equal("not-actions", external.AttemptBasis);
        Assert.Equal(UnitStatusStates.Missing, snapshot.Facts.Single(fact => fact.Id == "observed-ci").State);
    }

    [Fact]
    public void ActionsDifferentRunIdsForSameContextCannotUseOldGreenAsCurrent()
    {
        using var host = new TempHost();
        var runner = new FakeGitHub((arguments, _) => arguments[3] switch
        {
            var endpoint when endpoint.StartsWith($"repos/{Repo}/commits/{Head}/check-runs", StringComparison.Ordinal) => Json($$"""
                {"total_count":2,"check_runs":[
                  {"id":101,"name":"build","status":"completed","conclusion":"success","head_sha":"{{Head}}","app":{"slug":"github-actions"},"details_url":"https://github.com/{{Repo}}/actions/runs/70"},
                  {"id":102,"name":"build","status":"completed","conclusion":"failure","head_sha":"{{Head}}","app":{"slug":"github-actions"},"details_url":"https://github.com/{{Repo}}/actions/runs/71"}
                ]}
                """),
            $"repos/{Repo}/actions/runs/70" => Json($$"""{"id":70,"head_sha":"{{Head}}","run_attempt":3}"""),
            $"repos/{Repo}/actions/runs/71" => Json($$"""{"id":71,"head_sha":"{{Head}}","run_attempt":1}"""),
            _ => DefaultResponse(arguments),
        });

        var snapshot = new UnitStatusReadAdapter(runner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        var ci = snapshot.Facts.Single(fact => fact.Id == "observed-ci");
        Assert.Equal(UnitStatusStates.Unavailable, ci.State);
        Assert.Equal(UnitStatusStates.IdentityConflict, ci.UnavailableClass);
        Assert.Equal("github-check-run-identity-conflict", ci.Cause);
        Assert.Equal(new long?[] { 70, 71 }, snapshot.Checks.Select(check => check.RunId).OrderBy(id => id).ToArray());
        Assert.Equal(new int?[] { 1, 3 }, snapshot.Checks.Select(check => check.Attempt).OrderBy(attempt => attempt).ToArray());
        Assert.Equal(2, ci.Evidence.Count);
    }

    [Fact]
    public void ActionsAttemptsAreOrderedOnlyWithinTheSameRunId()
    {
        UnitStatusObservedCheck Check(int attempt, string conclusion, string recordId) => new()
        {
            Source = "check-run",
            Identity = "github-actions/build",
            RecordId = recordId,
            Sha = Head,
            Status = "completed",
            Conclusion = conclusion,
            RunId = 70,
            Attempt = attempt,
            AttemptBasis = "actions-run",
        };

        var oldGreen = Check(1, "success", "101");
        var latestFailure = Check(2, "failure", "102");
        var first = UnitStatusReadAdapter.CiFact([oldGreen, latestFailure], Head, Repo, PullRequest);
        var reversed = UnitStatusReadAdapter.CiFact([latestFailure, oldGreen], Head, Repo, PullRequest);

        Assert.Equal(UnitStatusStates.Missing, first.State);
        Assert.Equal("observed-ci-not-successful", first.Cause);
        Assert.Equal(UnitStatusStates.Missing, reversed.State);
        Assert.Equal(first.Cause, reversed.Cause);
    }

    [Fact]
    public void HeadRaceRetriesOnceThenReturnsUnavailableForAllHeadBoundFacts()
    {
        using var host = new TempHost();
        var runner = new FakeGitHub((arguments, prRead) =>
        {
            if (arguments[3] == $"repos/{Repo}/pulls/{PullRequest}")
            {
                var sha = prRead switch { 1 => Head, 2 => Head2, 3 => Head2, _ => "cccccccccccccccccccccccccccccccccccccccc" };
                return Json(PullJson(sha));
            }
            return DefaultResponse(arguments);
        });
        var adapter = new UnitStatusReadAdapter(runner, new FakeGit());

        var snapshot = adapter.ObserveGitHub(host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal(UnitStatusStates.Unavailable, snapshot.State);
        Assert.Equal(4, runner.Calls.Count(call => call[3] == $"repos/{Repo}/pulls/{PullRequest}"));
        Assert.Equal("snapshot-head-changed", snapshot.Cause);
        Assert.Equal(UnitStatusStates.Unavailable, snapshot.Facts.Single(fact => fact.Id == "posted-review").State);
        Assert.Equal(UnitStatusStates.IdentityConflict, snapshot.Facts.Single(fact => fact.Id == "observed-ci").UnavailableClass);
    }

    [Fact]
    public void PaginationBoundExhaustionMakesSnapshotUnavailableAndPreservesNoFalseAbsence()
    {
        using var host = new TempHost();
        var page = "[" + string.Join(",", Enumerable.Range(1, 100).Select(index =>
            $"{{\"id\":{index},\"state\":\"COMMENTED\",\"commit_id\":\"{Head}\",\"body\":\"ordinary review\"}}")) + "]";
        var runner = new FakeGitHub((arguments, _) => arguments[3].StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?", StringComparison.Ordinal)
            ? Json(page)
            : DefaultResponse(arguments));
        var adapter = new UnitStatusReadAdapter(runner, new FakeGit());

        var snapshot = adapter.ObserveGitHub(host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal(UnitStatusStates.Unavailable, snapshot.State);
        Assert.Equal("pagination-bound-exhausted", snapshot.Cause);
        var posted = snapshot.Facts.Single(fact => fact.Id == "posted-review");
        Assert.Equal(UnitStatusStates.Unavailable, posted.State);
        Assert.Equal(UnitStatusStates.ReadFailure, posted.UnavailableClass);
        Assert.Equal(20, runner.Calls.Count(call => call[3].StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?", StringComparison.Ordinal)));
    }

    [Fact]
    public void ShortCheckRunPageDoesNotOverrideReportedTotalCount()
    {
        using var host = new TempHost();
        var runner = new FakeGitHub((arguments, _) => arguments[3].StartsWith($"repos/{Repo}/commits/{Head}/check-runs?", StringComparison.Ordinal)
            ? Json("{\"total_count\":101,\"check_runs\":[{\"id\":1,\"name\":\"build\",\"status\":\"completed\",\"conclusion\":\"success\",\"head_sha\":\"" + Head + "\",\"app\":{\"slug\":\"external-ci\"},\"details_url\":null}]}")
            : DefaultResponse(arguments));
        var snapshot = new UnitStatusReadAdapter(runner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");

        Assert.Equal(UnitStatusStates.Unavailable, snapshot.State);
        Assert.Equal("pagination-bound-exhausted", snapshot.Facts.Single(fact => fact.Id == "observed-ci").Cause);
        Assert.Equal(20, runner.Calls.Count(call => call[3].StartsWith($"repos/{Repo}/commits/{Head}/check-runs?", StringComparison.Ordinal)));
    }

    [Fact]
    public void APIAndMalformedInventoryFailuresRemainUnavailableInsteadOfFalseAbsence()
    {
        using var host = new TempHost();
        var apiFailureRunner = new FakeGitHub((arguments, _) => arguments[3] == $"repos/{Repo}/issues/{Issue}"
            ? new GitHubCommandResult { ExitCode = 1, StdOut = "", StdErr = "HTTP 403 forbidden" }
            : DefaultResponse(arguments));
        var apiFailure = new UnitStatusReadAdapter(apiFailureRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(UnitStatusStates.Unavailable, apiFailure.State);
        Assert.Equal(UnitStatusStates.ReadFailure, apiFailure.Facts.Single(fact => fact.Id == "issue-completion-marker").UnavailableClass);
        Assert.Equal("github-read-failed", apiFailure.Facts.Single(fact => fact.Id == "issue-completion-marker").Cause);

        var malformedRunner = new FakeGitHub((arguments, _) => arguments[3].StartsWith($"repos/{Repo}/commits/{Head}/check-runs?", StringComparison.Ordinal)
            ? Json("{not-json")
            : DefaultResponse(arguments));
        var malformed = new UnitStatusReadAdapter(malformedRunner, new FakeGit()).ObserveGitHub(
            host.Context, Repo, Issue, PullRequest, "G855", "intent-cli", "intent-cli-dev");
        Assert.Equal(UnitStatusStates.Unavailable, malformed.State);
        Assert.Equal("github-json-invalid", malformed.Facts.Single(fact => fact.Id == "observed-ci").Cause);
    }

    [Fact]
    public void LocalClaimSnapshotReadsOnlyConfiguredRefAndExactUnitPaths()
    {
        using var host = new TempHost();
        var claimPath = ClaimCommand.ClaimPath("execution-unit:G855");
        var historyDirectory = $"{ClaimCommand.ClaimsDirectory}/history/{Path.GetFileNameWithoutExtension(claimPath)}";
        var historyPath = historyDirectory + "/release.json";
        var oid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var runner = new FakeGit((_, arguments) => arguments[0] switch
        {
            "rev-parse" when arguments.Contains("--abbrev-ref", StringComparer.Ordinal) => new GitRemoteCommandResult { ExitCode = 0, StdOut = "feature/unit", StdErr = "" },
            "rev-parse" when arguments.Contains("HEAD", StringComparer.Ordinal) => new GitRemoteCommandResult { ExitCode = 0, StdOut = Head, StdErr = "" },
            "rev-parse" => new GitRemoteCommandResult { ExitCode = 0, StdOut = oid, StdErr = "" },
            "ls-tree" => new GitRemoteCommandResult { ExitCode = 0, StdOut = claimPath + "\0" + historyPath + "\0", StdErr = "" },
            "show" when arguments[^1] == $"{oid}:{claimPath}" => new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = JsonSerializer.Serialize(new ClaimRecord("1", "execution-unit:G855", "builder", "intent-cli-dev",
                    DateTimeOffset.Parse("2026-10-01T00:00:00Z"), Head)),
                StdErr = "",
            },
            "show" => new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = JsonSerializer.Serialize(new ClaimHistoryRecord("1", "release", "execution-unit:G855", "builder", "intent-cli-dev",
                    DateTimeOffset.Parse("2026-10-02T00:00:00Z"), "done", "builder", "intent-cli-dev",
                    DateTimeOffset.Parse("2026-10-01T00:00:00Z"), Head)),
                StdErr = "",
            },
            _ => throw new InvalidOperationException("unexpected git request"),
        });
        var adapter = new UnitStatusReadAdapter(new FakeGitHub((args, _) => DefaultResponse(args)), runner);

        var snapshot = adapter.ReadClaimSnapshot(host.Context, "G855");

        Assert.Equal("completed", snapshot.State);
        Assert.Equal(Head, snapshot.LocalHeadSha);
        Assert.Equal("feature/unit", snapshot.LocalHeadRef);
        Assert.Equal("refs/remotes/origin/metadata", snapshot.MetadataRef);
        Assert.Equal(oid, snapshot.MetadataOid);
        Assert.Equal("builder", snapshot.ActiveClaim?.Actor);
        Assert.Equal("release", Assert.Single(snapshot.History).Operation);
        Assert.Equal(6, runner.Calls.Count);
        Assert.All(runner.Calls, call => Assert.Contains(call.Arguments[0], new[] { "rev-parse", "ls-tree", "show" }));
    }

    private static GitHubCommandResult DefaultResponse(IReadOnlyList<string> arguments)
    {
        var endpoint = arguments[3];
        if (endpoint == $"repos/{Repo}/pulls/{PullRequest}") return Json(PullJson());
        if (endpoint == $"repos/{Repo}/issues/{Issue}") return Json($"{{\"number\":{Issue},\"labels\":[]}}");
        if (endpoint.StartsWith($"repos/{Repo}/pulls/{PullRequest}/reviews?", StringComparison.Ordinal)) return Json("[]");
        if (endpoint.StartsWith($"repos/{Repo}/commits/{Head}/check-runs?", StringComparison.Ordinal)) return Json("{\"total_count\":0,\"check_runs\":[]}");
        if (endpoint.StartsWith($"repos/{Repo}/commits/{Head}/statuses?", StringComparison.Ordinal)) return Json("[]");
        return new GitHubCommandResult { ExitCode = 1, StdOut = "", StdErr = "HTTP 404 unexpected endpoint" };
    }

    private static string PullJson(string head = Head) =>
        $"{{\"number\":{PullRequest},\"head\":{{\"sha\":\"{head}\"}},\"merged\":false,\"merge_commit_sha\":null,\"labels\":[]}}";

    private static string ReviewArray(string body, string? commitId, string state = "COMMENTED") => ReviewRows((body, commitId, state));

    private static string ReviewRows(params (string Body, string? CommitId, string State)[] rows) => ReviewRowsWithIds(
        rows.Select((row, index) => (
            index + 1,
            row.Body,
            row.CommitId,
            row.State,
            $"2026-10-07T00:00:{index:D2}Z")).ToArray());

    private static string ReviewRowsWithIds(params (int Id, string Body, string? CommitId, string State, string SubmittedAt)[] rows) =>
        JsonSerializer.Serialize(rows.Select(row => new
        {
            id = row.Id,
            state = row.State,
            commit_id = row.CommitId,
            body = row.Body,
            html_url = $"https://github.com/{Repo}/pull/{PullRequest}#pullrequestreview-{row.Id}",
            submitted_at = row.SubmittedAt,
            user = new { login = "reviewer" },
        }));

    private static GitHubCommandResult Json(string json) => new() { ExitCode = 0, StdOut = json, StdErr = "" };

    private sealed class FakeGitHub(Func<IReadOnlyList<string>, int, GitHubCommandResult> response) : IGitHubCommandRunner
    {
        private int _pullReads;
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            var copy = arguments.ToArray();
            Calls.Add(copy);
            if (copy.Length > 3 && copy[3] == $"repos/{Repo}/pulls/{PullRequest}") _pullReads++;
            return response(copy, _pullReads);
        }
    }

    private sealed class FakeGit(Func<string, IReadOnlyList<string>, GitRemoteCommandResult> response) : IGitRemoteCommandRunner
    {
        public FakeGit() : this((_, _) => throw new InvalidOperationException("unexpected git request")) { }

        public List<(string Root, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var copy = arguments.ToArray();
            Calls.Add((workingDirectory, copy));
            return response(workingDirectory, copy);
        }
    }

    private sealed class TempHost : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "g855-adapter-" + Guid.NewGuid().ToString("N"));
        public TempHost() => Directory.CreateDirectory(_root);
        public CliContext Context => new()
        {
            RepoRoot = _root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", MetadataSourceBranch = "metadata" },
            },
        };
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
