using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class TeamModeG692Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private readonly string root = Directory.CreateTempSubdirectory("team-mode-g692-").FullName;

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SharedCapabilityMatrix_MarksOnlyDeliverySeatClassesNotApplicableForAuthoring()
    {
        var authoring = TeamModeCapabilityMatrix.FromResolution(new TeamModeResolution
        {
            Mode = TeamMode.AuthoringOnly,
            Source = TeamModeSource.Recorded,
        });
        var delivery = TeamModeCapabilityMatrix.FromResolution(new TeamModeResolution
        {
            Mode = TeamMode.Delivery,
            Source = TeamModeSource.Default,
        });

        Assert.Equal(TeamMode.AuthoringOnly, authoring.TeamMode);
        Assert.Equal("recorded", authoring.ModeSource);
        Assert.Equal(
            [
                TeamModeCapabilityClasses.Worker,
                TeamModeCapabilityClasses.Review,
                TeamModeCapabilityClasses.Ci,
                TeamModeCapabilityClasses.Delegation,
                TeamModeCapabilityClasses.Supervisor,
            ],
            authoring.NotApplicableClasses);
        Assert.Contains(TeamModeCapabilityClasses.ContractReadiness, authoring.ActiveClasses);
        Assert.Contains(TeamModeCapabilityClasses.BranchLane, authoring.ActiveClasses);
        Assert.Equal(TeamModeCapabilityClasses.PublishDurableStateDrift, authoring.ClassForDoctorCategory("merged-pr-not-completed"));
        Assert.Equal(TeamModeCapabilityClasses.Worker, authoring.ClassForQueueState(IntentSystem.Supervisor.Models.QueueItemState.Active));
        Assert.Equal(TeamModeCapabilityClasses.Review, authoring.ClassForQueueState(IntentSystem.Supervisor.Models.QueueItemState.Review));

        Assert.Empty(delivery.NotApplicableClasses);
        Assert.Equal(TeamModeCapabilityClasses.All, delivery.ActiveClasses);
        Assert.True(delivery.IsApplicable(TeamModeCapabilityClasses.Delegation));
    }

    [Fact]
    public void StalledWorkIntentStatusAndStatusBrief_ExposeTheSameAuthoringMatrix()
    {
        SetAuthoringOnly();
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyLister();

        var stalled = AutomationStalledWorkCommand.Analyze(
            Context,
            Domain,
            Repo,
            staleMinutes: 9999,
            team: Team);
        Assert.NotNull(stalled.CapabilityMatrix);

        using var statusWriter = new StringWriter();
        Assert.Equal(0, IntentStatusCommand.Execute(
            Context,
            ["--domain", Domain, "--team", Team, "--format", "json"],
            statusWriter));
        using var status = JsonDocument.Parse(statusWriter.ToString());

        using var briefWriter = new StringWriter();
        Assert.Equal(0, StatusBriefCommand.Execute(
            Context,
            ["--domain", Domain, "--team", Team, "--format", "json"],
            briefWriter));
        using var brief = JsonDocument.Parse(briefWriter.ToString());

        var stalledMatrix = stalled.CapabilityMatrix!;
        var statusMatrix = status.RootElement.GetProperty("capability_matrix");
        var briefMatrix = brief.RootElement.GetProperty("capability_matrix");
        Assert.Equal(stalledMatrix.TeamMode, statusMatrix.GetProperty("team_mode").GetString());
        Assert.Equal(stalledMatrix.TeamMode, briefMatrix.GetProperty("team_mode").GetString());
        Assert.Equal(
            stalledMatrix.NotApplicableClasses,
            statusMatrix.GetProperty("not_applicable_classes").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(
            stalledMatrix.NotApplicableClasses,
            briefMatrix.GetProperty("not_applicable_classes").EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    [Fact]
    public void NoTeam_UsesTheUniqueTeamScopedAuthoringRecordAcrossSharedConsumers()
    {
        SetAuthoringOnly();
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyLister();

        var resolved = TeamModeCapabilityMatrix.Resolve(Context.RepoRoot, Domain, team: null);
        Assert.Equal(TeamMode.AuthoringOnly, resolved.TeamMode);
        Assert.Equal("recorded", resolved.ModeSource);

        var stalled = AutomationStalledWorkCommand.Analyze(
            Context,
            Domain,
            Repo,
            staleMinutes: 9999,
            team: null);
        Assert.Equal(TeamMode.AuthoringOnly, stalled.CapabilityMatrix!.TeamMode);

        using var statusWriter = new StringWriter();
        Assert.Equal(0, IntentStatusCommand.Execute(
            Context,
            ["--domain", Domain, "--format", "json"],
            statusWriter));
        using var status = JsonDocument.Parse(statusWriter.ToString());

        using var briefWriter = new StringWriter();
        Assert.Equal(0, StatusBriefCommand.Execute(
            Context,
            ["--domain", Domain, "--format", "json"],
            briefWriter));
        using var brief = JsonDocument.Parse(briefWriter.ToString());

        Assert.Equal(TeamMode.AuthoringOnly, status.RootElement.GetProperty("capability_matrix").GetProperty("team_mode").GetString());
        Assert.Equal(TeamMode.AuthoringOnly, brief.RootElement.GetProperty("capability_matrix").GetProperty("team_mode").GetString());

        using var stalledWriter = new StringWriter();
        Assert.Equal(0, AutomationStalledWorkCommand.Execute(
            Context,
            ["--domain", Domain, "--repo", Repo, "--format", "json"],
            stalledWriter));
        using var stalledJson = JsonDocument.Parse(stalledWriter.ToString());
        Assert.Equal(TeamMode.AuthoringOnly, stalledJson.RootElement.GetProperty("capability_matrix").GetProperty("team_mode").GetString());
    }

    [Fact]
    public void NoTeam_WithMultipleTeamScopedRecords_FailsClosedByNameAcrossMatrixConsumers()
    {
        SetAuthoringOnly();
        using var secondModeWriter = new StringWriter();
        Assert.Equal(0, TeamModeCommand.ExecuteSet(
            Context,
            ["--domain", Domain, "--team", "other-team", "--mode", TeamMode.Delivery, "--write", "--format", "json"],
            secondModeWriter));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyLister();

        using var statusWriter = new StringWriter();
        Assert.Equal(1, IntentStatusCommand.Execute(
            Context,
            ["--domain", Domain, "--format", "json"],
            statusWriter));
        Assert.Contains(TeamModeResolutionException.AmbiguousTeamScopeCode, statusWriter.ToString(), StringComparison.Ordinal);

        using var briefWriter = new StringWriter();
        Assert.Equal(1, StatusBriefCommand.Execute(
            Context,
            ["--domain", Domain, "--format", "json"],
            briefWriter));
        Assert.Contains(TeamModeResolutionException.AmbiguousTeamScopeCode, briefWriter.ToString(), StringComparison.Ordinal);

        using var stalledWriter = new StringWriter();
        Assert.Equal(1, AutomationStalledWorkCommand.Execute(
            Context,
            ["--domain", Domain, "--repo", Repo, "--format", "json"],
            stalledWriter));
        Assert.Contains(TeamModeResolutionException.AmbiguousTeamScopeCode, stalledWriter.ToString(), StringComparison.Ordinal);

        using var notifyWriter = new StringWriter();
        Assert.Equal(1, CommandRouter.Execute(
            [
                "notify", "delegate", "--domain", Domain,
                "--from", "design", "--to", "implementation", "--report-to", "orchestration",
                "--task-id", "G692-ambiguous-team", "--objective", "ambiguous team must fail closed",
                "--expected-artifact", "none", "--result-nonce", "g692-ambiguous", "--routing-root", root,
                "--dry-run", "--format", "json",
            ],
            Context,
            notifyWriter));
        using var notify = JsonDocument.Parse(notifyWriter.ToString());
        Assert.Equal(
            TeamModeResolutionException.AmbiguousTeamScopeCode,
            notify.RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void AuthoringOnlyNamedWorkerDelegateRefusesBeforeOutboxOrTransport()
    {
        SetAuthoringOnly();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "notify", "delegate", "--domain", Domain,
                "--from", "design", "--to", "implementation", "--report-to", "orchestration",
                "--task-id", "G692-worker-refusal", "--objective", "worker lane must not be impersonated",
                "--input", "issue #1497", "--expected-artifact", "draft PR URL",
                "--result-nonce", "g692-refusal", "--routing-root", root, "--dry-run", "--format", "json",
            ],
            Context,
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not-applicable-team-mode", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("implementation", writer.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, ".intent-cli", "notify")));
    }

    private void SetAuthoringOnly()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, TeamModeCommand.ExecuteSet(
            Context,
            ["--domain", Domain, "--team", Team, "--mode", TeamMode.AuthoringOnly, "--write", "--format", "json"],
            writer));
    }

    private CliContext Context => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };

    private sealed class EmptyLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => [];

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => [];
    }
}
