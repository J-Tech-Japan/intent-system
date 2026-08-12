using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G674: locks the verified REST issue-list boundary, the field inventory,
/// and the output-compatible projection consumed by the automation analyzers.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class GitHubApiReadG674Tests : IDisposable
{
    public void Dispose()
    {
        GhCliGitHubAutomationCandidateLister.ProcessRunner = null;
        GhCliGitHubAutomationCandidateLister.QuotaProbeFactory = null;
    }

    [Fact]
    public void Inventory_RecordsEveryMeasuredIssueReadAndGraphQlRemainder()
    {
        var issueReads = GitHubApiReadInventory.VerifiedRestIssueReads;
        Assert.Contains(issueReads, read =>
            read.Surface == "worker-next-action"
            && read.CallSite == "intent-target-issues"
            && read.Endpoint.Contains("/issues?state=open", StringComparison.Ordinal)
            && read.ConsumedFields.Contains("labels[].name"));
        Assert.Contains(issueReads, read =>
            read.Surface == "heartbeat"
            && read.CallSite.Contains("inherited", StringComparison.Ordinal)
            && read.RestFields.Contains("html_url -> url"));

        var stalled = Assert.Single(GitHubApiReadInventory.GraphQlBoundReads,
            read => read.Surface == "stalled-work");
        Assert.Contains("closingIssuesReferences[].number", stalled.UnverifiedFields);
        Assert.Contains("statusCheckRollup[].conclusion", stalled.UnverifiedFields);
        Assert.Contains("check-runs alone", stalled.Reason, StringComparison.Ordinal);

        var worker = Assert.Single(GitHubApiReadInventory.GraphQlBoundReads,
            read => read.Surface == "worker-next-action");
        Assert.Equal(
            GitHubApiReadInventory.UnverifiedFieldsFor(GitHubAutomationReadSurface.WorkerNextAction),
            worker.UnverifiedFields);
    }

    [Fact]
    public void RestIssueArguments_UseReadOnlyIssuesEndpointAndPreserveLabelFilter()
    {
        var args = GhCliGitHubAutomationCandidateLister.BuildRestIssueListArguments(
            "J-Tech-Japan/intent-system",
            ["intent-target", "intent-pr-created"]);

        Assert.Equal("api", args[0]);
        Assert.Equal("repos/J-Tech-Japan/intent-system/issues", args[1]);
        Assert.Contains("--method", args);
        Assert.Contains("GET", args);
        Assert.Contains("--raw-field", args);
        Assert.Contains("state=open", args);
        Assert.Contains("per_page=100", args);
        Assert.Contains("labels=intent-target,intent-pr-created", args);
        Assert.Contains("--paginate", args);
        Assert.Contains("--slurp", args);
        Assert.DoesNotContain("graphql", args, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("issue", args, StringComparer.Ordinal);
    }

    [Fact]
    public void RestIssueDeserializer_FiltersPullRequestsAndNormalizesExistingShape()
    {
        const string restPages = """
            [[
              {
                "number": 1457,
                "title": "G674: REST",
                "html_url": "https://github.com/J-Tech-Japan/intent-system/issues/1457",
                "body": "Goal",
                "created_at": "2026-08-12T00:00:00Z",
                "updated_at": "2026-08-12T01:00:00Z",
                "labels": [{"name":"intent-target","color":"fff"}],
                "state": "open"
              },
              {
                "number": 1458,
                "title": "A PR row must not become an issue",
                "html_url": "https://github.com/J-Tech-Japan/intent-system/pull/1458",
                "pull_request": {"url":"https://api.github.com/repos/J-Tech-Japan/intent-system/pulls/1458"},
                "state": "open",
                "labels": []
              }
            ]]
            """;

        var issue = Assert.Single(
            GhCliGitHubAutomationCandidateLister.DeserializeRestIssueList(
                restPages,
                "`gh api repos/J-Tech-Japan/intent-system/issues` for J-Tech-Japan/intent-system"));

        Assert.Equal(1457, issue.Number);
        Assert.Equal("G674: REST", issue.Title);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/1457", issue.Url);
        Assert.Equal("OPEN", issue.State);
        Assert.Equal("intent-target", Assert.Single(issue.Labels).Name);
        Assert.Equal("2026-08-12T01:00:00Z", issue.UpdatedAt);
    }

    [Fact]
    public void RestProjection_IsByteCompatibleWithTheExistingCandidateShape()
    {
        const string graphqlShape = """
            [{
              "number":1457,
              "title":"G674: REST",
              "url":"https://github.com/J-Tech-Japan/intent-system/issues/1457",
              "createdAt":"2026-08-12T00:00:00Z",
              "body":"Goal",
              "updatedAt":"2026-08-12T01:00:00Z",
              "labels":[{"name":"intent-target"}],
              "state":"OPEN"
            }]
            """;
        const string restShape = """
            [{
              "number":1457,
              "title":"G674: REST",
              "html_url":"https://github.com/J-Tech-Japan/intent-system/issues/1457",
              "created_at":"2026-08-12T00:00:00Z",
              "body":"Goal",
              "updated_at":"2026-08-12T01:00:00Z",
              "labels":[{"name":"intent-target","color":"fff"}],
              "state":"open"
            }]
            """;

        var graphql = GhCliGitHubAutomationCandidateLister.DeserializeList<GitHubAutomationIssueCandidate>(
            graphqlShape,
            "`gh issue list` for J-Tech-Japan/intent-system");
        var rest = GhCliGitHubAutomationCandidateLister.DeserializeRestIssueList(
            restShape,
            "`gh api repos/J-Tech-Japan/intent-system/issues` for J-Tech-Japan/intent-system");

        Assert.Equal(JsonSerializer.Serialize(graphql), JsonSerializer.Serialize(rest));
    }

    [Fact]
    public void Lister_UsesRestWhenGraphQlIsExhaustedAndKeepsIssueFields()
    {
        GhCliGitHubAutomationCandidateLister.QuotaProbeFactory = () => new FixedQuotaProbe(new GitHubApiQuotaReport
        {
            Resources =
            [
                new GitHubApiQuotaResource { Resource = "graphql", Remaining = 0 },
                new GitHubApiQuotaResource { Resource = "core", Remaining = 1 },
            ],
        });
        var calls = new List<IReadOnlyList<string>>();
        GhCliGitHubAutomationCandidateLister.ProcessRunner = args =>
        {
            calls.Add(args);
            return new GhCliProcessResult(
                0,
                "[[{\"number\":1457,\"title\":\"G674\",\"html_url\":\"https://github.com/J-Tech-Japan/intent-system/issues/1457\",\"body\":\"Goal\",\"created_at\":\"2026-08-12T00:00:00Z\",\"updated_at\":\"2026-08-12T01:00:00Z\",\"labels\":[{\"name\":\"intent-target\"}],\"state\":\"open\"}]]",
                string.Empty);
        };

        var issues = new GhCliGitHubAutomationCandidateLister().ListIssues(
            "J-Tech-Japan/intent-system",
            ["intent-target"],
            GitHubAutomationReadSurface.WorkerNextAction);

        var issue = Assert.Single(issues);
        Assert.Equal(1457, issue.Number);
        Assert.Equal("api", calls[0][0]);
        Assert.Contains("repos/J-Tech-Japan/intent-system/issues", calls[0]);
        Assert.DoesNotContain("graphql", calls[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkerOutput_RemainsByteCompatibleWhenIssueTransportIsRest()
    {
        using var workspace = TestWorkspace.Create();
        var issue = new GitHubAutomationIssueCandidate
        {
            Number = 1457,
            Title = "G674",
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/1457",
            CreatedAt = "2026-08-12T00:00:00Z",
            UpdatedAt = "2026-08-12T01:00:00Z",
            Body = "Goal",
            Labels = [new GitHubAutomationLabel { Name = "intent-target" }],
            State = "OPEN",
        };

        WorkerNextActionCommand.CandidateListerFactory = () => new FixedIssueLister(issue);
        using var graphqlShapedWriter = new StringWriter();
        Assert.Equal(0, WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json"],
            graphqlShapedWriter));

        WorkerNextActionCommand.CandidateListerFactory = null;
        GhCliGitHubAutomationCandidateLister.ProcessRunner = args => args[0] == "pr"
            ? new GhCliProcessResult(0, "[]", string.Empty)
            : new GhCliProcessResult(
                0,
                "[[{\"number\":1457,\"title\":\"G674\",\"html_url\":\"https://github.com/J-Tech-Japan/intent-system/issues/1457\",\"body\":\"Goal\",\"created_at\":\"2026-08-12T00:00:00Z\",\"updated_at\":\"2026-08-12T01:00:00Z\",\"labels\":[{\"name\":\"intent-target\"}],\"state\":\"open\"}]]",
                string.Empty);
        using var restWriter = new StringWriter();
        Assert.Equal(0, WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json"],
            restWriter));

        var graphqlJson = JsonSerializer.Serialize(JsonDocument.Parse(graphqlShapedWriter.ToString()).RootElement);
        var restJson = JsonSerializer.Serialize(JsonDocument.Parse(restWriter.ToString()).RootElement);
        Assert.Equal(graphqlJson, restJson);
    }

    [Fact]
    public void QuotaParser_SelectsRequestedRestResourceInsteadOfGraphQl()
    {
        var report = GitHubApiQuotaParser.Parse("""
            {
              "resources": {
                "graphql": {"limit":5000,"used":5000,"remaining":0,"reset":1786500748},
                "core": {"limit":5000,"used":4999,"remaining":1,"reset":1786500748}
              }
            }
            """, GitHubApiQuotaConstants.RestCoreResource)!;

        Assert.Equal(GitHubApiQuotaConstants.Healthy, report.Status);
        Assert.False(report.IsQuotaDegraded);
        Assert.Equal(0, report.Find("graphql")!.Remaining);
        Assert.Equal(1, report.Find("core")!.Remaining);
    }

    [Fact]
    public void GraphQlBoundFailure_NamesDependencyAndUnverifiedFields()
    {
        var exception = GitHubApiFailureFactory.FromGhFailure(
            "list PRs in J-Tech-Japan/intent-system",
            "GraphQL: API rate limit exceeded",
            string.Empty,
            1,
            new FixedQuotaProbe(new GitHubApiQuotaReport
            {
                Resources =
                [
                    new GitHubApiQuotaResource
                    {
                        Resource = "graphql",
                        Remaining = 0,
                        Reset = 1786500748,
                    },
                    new GitHubApiQuotaResource
                    {
                        Resource = "core",
                        Remaining = 4948,
                    },
                ],
            }),
            GitHubApiQuotaConstants.GraphQlResource,
            GitHubApiReadDependencies.GraphQlBound,
            ["closingIssuesReferences[].number"]);

        Assert.True(exception.IsQuotaDegraded);
        Assert.Equal(GitHubApiReadDependencies.GraphQlBound, exception.DegradedState!.Dependency);
        Assert.Equal(["closingIssuesReferences[].number"], exception.DegradedState.UnverifiedFields);
        // The REST core row must not be selected for this failure.
        Assert.Equal("graphql", exception.DegradedState.Resource);
    }

    [Fact]
    public void RestBoundFailure_UsesCoreEvenWhenGraphQlIsAlsoExhausted()
    {
        var exception = GitHubApiFailureFactory.FromGhFailure(
            "list issues in J-Tech-Japan/intent-system via REST",
            "HTTP 403",
            string.Empty,
            1,
            new FixedQuotaProbe(new GitHubApiQuotaReport
            {
                Resources =
                [
                    new GitHubApiQuotaResource { Resource = "graphql", Remaining = 0 },
                    new GitHubApiQuotaResource { Resource = "core", Remaining = 0, Reset = 1786500748 },
                ],
            }),
            GitHubApiQuotaConstants.RestCoreResource,
            GitHubApiReadDependencies.RestCore);

        Assert.True(exception.IsQuotaDegraded);
        Assert.Equal("core", exception.DegradedState!.Resource);
        Assert.Equal(GitHubApiReadDependencies.RestCore, exception.DegradedState.Dependency);
        Assert.Null(exception.DegradedState.UnverifiedFields);
    }

    private sealed class FixedQuotaProbe(GitHubApiQuotaReport report) : IGitHubApiQuotaProbe
    {
        public GitHubApiQuotaReport? Read() => report;
    }

    private sealed class FixedIssueLister(GitHubAutomationIssueCandidate issue)
        : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => [issue];
    }
}
