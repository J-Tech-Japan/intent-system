using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class GitHubApiQuotaG673Tests : IDisposable
{
    public void Dispose()
    {
        WorkerNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationDoctorCommand.QuotaProbeFactory = null;
        AutomationInstalledCliSurfaceProbe.PathResolver = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
    }

    [Fact]
    public void Parser_UsesStructuredGraphQlRemainingAndReset_NotText()
    {
        var healthy = GitHubApiQuotaParser.Parse("""
            {
              "resources": {
                "graphql": {"limit": 5000, "used": 4999, "remaining": 1, "reset": 1786500748, "resetAt": "2026-08-12T19:32:28Z"}
              }
            }
            """)!;
        Assert.False(healthy.IsQuotaDegraded);
        Assert.Equal(1, healthy.Find("graphql")!.Remaining);

        var exhausted = GitHubApiQuotaParser.Parse("""
            {
              "resources": {
                "graphql": {"limit": 5000, "used": 5000, "remaining": 0, "reset": 1786500748, "resetAt": "2026-08-12T19:32:28Z"},
                "core": {"limit": 5000, "used": 1, "remaining": 4999, "reset": 1786500748}
              }
            }
            """)!;
        Assert.True(exhausted.IsQuotaDegraded);
        Assert.Equal(GitHubApiQuotaConstants.QuotaExhaustedCause, exhausted.DegradedState!.Cause);
        Assert.Equal("graphql", exhausted.DegradedState.Resource);
        Assert.Equal(0, exhausted.DegradedState.Remaining);
        Assert.Equal(1786500748, exhausted.DegradedState.Reset);
        Assert.Equal("2026-08-12T19:32:28Z", exhausted.DegradedState.ResetAt);
    }

    [Fact]
    public void WorkerNextAction_QuotaIsUnavailableAndDistinctFromEmpty()
    {
        using var workspace = TestWorkspace.Create();
        WorkerNextActionCommand.CandidateListerFactory = () => new QuotaLister();

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--github-only", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.Unavailable, result.Action);
        Assert.NotEqual(WorkerNextActionConstants.Actions.None, result.Action);
        Assert.True(result.Degraded);
        Assert.Equal(GitHubApiQuotaConstants.QuotaExhaustedCause, result.Cause);
        Assert.Equal("graphql", result.Resource);
        Assert.Equal(1786500748, result.Reset);
    }

    [Fact]
    public void WorkerNextAction_NonQuotaFailureHasDifferentCause()
    {
        using var workspace = TestWorkspace.Create();
        WorkerNextActionCommand.CandidateListerFactory = () => new NonQuotaLister();

        using var writer = new StringWriter();
        var exitCode = WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<WorkerNextActionResult>(writer.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.Unavailable, result.Action);
        Assert.False(result.Degraded);
        Assert.Equal("github-command-failed", result.Cause);
        Assert.NotEqual(GitHubApiQuotaConstants.QuotaExhaustedCause, result.Cause);
    }

    [Fact]
    public void HostLoopNextAction_QuotaIsNamedAndDoesNotRecommendMutation()
    {
        using var workspace = TestWorkspace.Create();
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new QuotaLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostLoopNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<HostLoopNextActionEmittedResult>(writer.ToString())!;
        Assert.Equal(GitHubApiQuotaConstants.DetectionUnavailableCause, result.Classification);
        Assert.False(result.MutationAllowed);
        Assert.True(result.Degraded);
        Assert.Equal("graphql", result.Resource);
        Assert.Equal(GitHubApiQuotaConstants.QuotaExhaustedCause, result.Cause);
    }

    [Fact]
    public void Doctor_ReportsEveryResourceAndFailsHealthOnQuota()
    {
        using var workspace = TestWorkspace.Create();
        InstallSurfaceProbe();
        AutomationDoctorCommand.QuotaProbeFactory = () => new FixedQuotaProbe(QuotaReport(exhausted: true));

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
        Assert.Equal("github-api-quota-degraded", result.Status);
        Assert.True(result.Degraded);
        Assert.Equal(GitHubApiQuotaConstants.QuotaExhaustedCause, result.Cause);
        Assert.Contains(result.GithubApiQuota!.Resources, row => row.Resource == "graphql" && row.Remaining == 0);
        Assert.Contains(result.GithubApiQuota.Resources, row => row.Resource == "core" && row.Remaining == 4999);
        Assert.Contains("reset_at", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_HealthyQuotaKeepsOkVerdict()
    {
        using var workspace = TestWorkspace.Create();
        InstallSurfaceProbe();
        AutomationDoctorCommand.QuotaProbeFactory = () => new FixedQuotaProbe(QuotaReport(exhausted: false));

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
        Assert.Equal("ok", result.Status);
        Assert.False(result.Degraded);
        Assert.Null(result.Cause);
        Assert.Equal(1, result.GithubApiQuota!.Find("graphql")!.Remaining);
    }

    [Fact]
    public void Doctor_NonQuotaRateLimitObservationIsNotHealthy()
    {
        using var workspace = TestWorkspace.Create();
        InstallSurfaceProbe();
        AutomationDoctorCommand.QuotaProbeFactory = () => new FixedQuotaProbe(new GitHubApiQuotaReport
        {
            Status = GitHubApiQuotaConstants.Error,
            Cause = GitHubApiQuotaConstants.QuotaObservationFailedCause,
        });

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
        Assert.Equal("github-api-error", result.Status);
        Assert.Equal(GitHubApiQuotaConstants.Error, result.GithubApiStatus);
        Assert.False(result.Degraded);
        Assert.Equal(GitHubApiQuotaConstants.QuotaObservationFailedCause, result.Cause);
    }

    [Fact]
    public void OrchestratorGuide_NamesQuotaBlindSpotAndDeliberateWait()
    {
        using var workspace = TestWorkspace.Create();
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            [
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--agent", "claude",
                "--format", "markdown",
            ],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("github-api-quota-exhausted", output, StringComparison.Ordinal);
        Assert.Contains("detection_unavailable", output, StringComparison.Ordinal);
        Assert.Contains("wait deliberately", output, StringComparison.Ordinal);
        Assert.Contains("no automatic retry", output, StringComparison.Ordinal);
        Assert.Contains("Issue #1442", output, StringComparison.Ordinal);
        Assert.Contains("G667", output, StringComparison.Ordinal);
    }

    private static void InstallSurfaceProbe()
    {
        AutomationInstalledCliSurfaceProbe.PathResolver = _ => "/bin/sh";
        AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, args) =>
            new InstalledCliProbeResult(1, string.Join(' ', args) + " review-start request-update approved", string.Empty);
    }

    private static GitHubApiQuotaReport QuotaReport(bool exhausted)
    {
        var graphql = new GitHubApiQuotaResource
        {
            Resource = "graphql",
            Limit = 5000,
            Used = exhausted ? 5000 : 4999,
            Remaining = exhausted ? 0 : 1,
            Reset = 1786500748,
            ResetAt = "2026-08-12T19:32:28Z",
        };
        return new GitHubApiQuotaReport
        {
            Status = exhausted ? GitHubApiQuotaConstants.Degraded : GitHubApiQuotaConstants.Healthy,
            Resources = new[]
            {
                graphql,
                new GitHubApiQuotaResource
                {
                    Resource = "core",
                    Limit = 5000,
                    Used = 1,
                    Remaining = 4999,
                    Reset = 1786500748,
                    ResetAt = "2026-08-12T19:32:28Z",
                },
            },
            DegradedState = exhausted
                ? new GitHubApiDegradedState
                {
                    Resource = "graphql",
                    Remaining = 0,
                    Reset = 1786500748,
                    ResetAt = "2026-08-12T19:32:28Z",
                }
                : null,
        };
    }

    private sealed class FixedQuotaProbe(GitHubApiQuotaReport report) : IGitHubApiQuotaProbe
    {
        public GitHubApiQuotaReport Read() => report;
    }

    private sealed class QuotaLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => throw new GitHubApiQuotaExceededException("gh pr list", State());

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => throw new GitHubApiQuotaExceededException("gh issue list", State());

        private static GitHubApiDegradedState State() => new()
        {
            Resource = "graphql",
            Remaining = 0,
            Reset = 1786500748,
            ResetAt = "2026-08-12T19:32:28Z",
        };
    }

    private sealed class NonQuotaLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => throw new GitHubApiRequestException(
                "github-command-failed", "gh pr list", "non-quota GitHub failure");

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationIssueCandidate>();
    }
}

[Collection("AutomationStalledWorkSharedState")]
public sealed class StalledWorkQuotaG673Tests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 20, 0, 0, TimeSpan.Zero);

    public StalledWorkQuotaG673Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => Now;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
    }

    [Fact]
    public void QuotaKeepsLocalDelegationFindingAsPartialAndMarksDetectionUnavailable()
    {
        using var workspace = TestWorkspace.Create();
        var delegation = new NotifyPendingDelegation
        {
            Domain = "intent-cli",
            Team = "intent-cli-dev",
            TaskId = "G673-local-finding",
            RecipientRole = "implementation",
            RecipientIdentity = "seat",
            ExpectedArtifact = "PR",
            ResultNonce = "nonce",
            DispatchedAt = Now.AddHours(-2),
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.RootPath, delegation).Written);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new QuotaLister();

        var result = AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 45);

        Assert.True(result.Partial);
        Assert.False(result.DetectionAvailable);
        Assert.True(result.DetectionUnavailable);
        Assert.Equal("unavailable", result.DetectionStatus);
        Assert.Equal(GitHubApiQuotaConstants.QuotaExhaustedCause, result.Cause);
        var finding = Assert.Single(result.Items, item => item.Kind == AutomationStalledWorkCommand.KindPendingDelegationOpen);
        Assert.True(finding.Partial);
        Assert.Equal("G673-local-finding", finding.ExecutionUnit);
    }

    [Fact]
    public void Heartbeat_PropagatesDetectionUnavailableInsteadOfHealthy()
    {
        using var workspace = TestWorkspace.Create();
        AutomationStalledWorkCommand.CandidateListerFactory = () => new QuotaLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            [
                "--domain", "intent-cli",
                "--repo", "J-Tech-Japan/intent-system",
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("detection-unavailable", root.GetProperty("verdict").GetString());
        Assert.False(root.GetProperty("detection_available").GetBoolean());
        Assert.True(root.GetProperty("partial").GetBoolean());
        Assert.Equal(GitHubApiQuotaConstants.QuotaExhaustedCause, root.GetProperty("cause").GetString());
        Assert.Contains("reset_at", root.GetProperty("reason").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void NonQuotaFailureIsStructuredAndKeepsLocalFindingPartial()
    {
        using var workspace = TestWorkspace.Create();
        var delegation = new NotifyPendingDelegation
        {
            Domain = "intent-cli",
            Team = "intent-cli-dev",
            TaskId = "G673-nonquota-local-finding",
            RecipientRole = "implementation",
            RecipientIdentity = "seat",
            ExpectedArtifact = "PR",
            ResultNonce = "nonce",
            DispatchedAt = Now.AddHours(-2),
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.RootPath, delegation).Written);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new NonQuotaLister();

        var result = AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 45);

        Assert.True(result.Partial);
        Assert.False(result.DetectionAvailable);
        Assert.Equal(GitHubApiQuotaConstants.Error, result.GithubApiStatus);
        Assert.False(result.Degraded);
        Assert.Equal("github-command-failed", result.Cause);
        Assert.Null(result.DegradedState);
        Assert.True(Assert.Single(result.Items, item => item.Kind == AutomationStalledWorkCommand.KindPendingDelegationOpen).Partial);
    }

    private sealed class QuotaLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => throw new GitHubApiQuotaExceededException("gh pr list", State());

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => throw new GitHubApiQuotaExceededException("gh issue list", State());

        private static GitHubApiDegradedState State() => new()
        {
            Resource = "graphql",
            Remaining = 0,
            Reset = 1786500748,
            ResetAt = "2026-08-12T19:32:28Z",
        };
    }

    private sealed class NonQuotaLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => throw new GitHubApiRequestException(
                "github-command-failed", "gh issue list", "non-quota GitHub failure");

        public IReadOnlyList<GitHubAutomationPrCandidate> ListClosedPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();
    }
}

internal sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string rootPath)
    {
        RootPath = rootPath;
        Context = new CliContext
        {
            RepoRoot = rootPath,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                },
            },
        };
    }

    public string RootPath { get; }

    public CliContext Context { get; }

    public static TestWorkspace Create()
    {
        var root = Directory.CreateTempSubdirectory("g673-quota-tests-").FullName;
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        return new TestWorkspace(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
