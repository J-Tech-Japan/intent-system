using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G833: a solo-conductor team keeps every capability class active, so a
/// recorded pending delegation still surfaces in stalled-work, and the shared
/// status surfaces emit the named capability matrix.
/// </summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class G833SoloConductorStalledWorkTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly string root = Directory.CreateTempSubdirectory("g833-solo-stalled-").FullName;

    public G833SoloConductorStalledWorkTests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => Now;
        AutomationStalledWorkCommand.GitCommandRunnerFactory = null;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationStalledWorkCommand.GitCommandRunnerFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PendingDelegation_StillSurfacesForASoloTeam()
    {
        SetSolo();
        WriteFile(".intent-cli/issues/G833/packet.yaml", "implementation_issue_packet:\n  domain: intent-cli\n");
        var issue = new GitHubAutomationIssueCandidate
        {
            Number = 1809,
            Title = "G833: solo conductor",
            Url = $"https://github.com/{Repo}/issues/1809",
            CreatedAt = Now.AddMinutes(-70).ToString("O"),
            UpdatedAt = Now.AddMinutes(-70).ToString("O"),
            State = "OPEN",
            Labels = [new GitHubAutomationLabel { Name = "intent-target" }],
        };
        var pending = new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G833-intake",
            DelegatingRole = "architect",
            RecipientRole = "orchestrator",
            RecipientIdentity = "orchestrator-seat",
            ExpectedArtifact = $"https://github.com/{Repo}/issues/G833",
            ExpectedArtifacts = [$"https://github.com/{Repo}/issues/G833"],
            Objective = "Deliver G833 intake to orchestrator.",
            Inputs = [$"https://github.com/{Repo}/issues/G833"],
            ResultNonce = "G833-intake-nonce",
            DispatchedAt = Now.AddMinutes(-55),
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);
        Assert.True(NotifyDelegationDeliveryStore.Write(root, pending, Now.AddMinutes(-42)).Written);

        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister([issue]);
        var result = AutomationStalledWorkCommand.Analyze(Context, Domain, Repo, staleMinutes: 0, team: Team);

        Assert.Equal(TeamMode.SoloConductor, result.CapabilityMatrix!.TeamMode);
        var finding = Assert.Single(result.Items, item => item.ExecutionUnit == "G833");
        Assert.Equal(AutomationStalledWorkCommand.KindPublishedIntakeAwaitingDispatch, finding.Kind);
        Assert.Equal("G833-intake", finding.IntakeTaskId);
    }

    [Fact]
    public void StatusAndBrief_EmitTheSoloMatrix_WithEveryClassActive()
    {
        SetSolo();
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister([]);

        foreach (var execute in new Func<CliContext, string[], TextWriter, int>[] { IntentStatusCommand.Execute, StatusBriefCommand.Execute })
        {
            using var writer = new StringWriter();
            Assert.Equal(0, execute(Context, ["--domain", Domain, "--team", Team, "--format", "json"], writer));
            using var document = JsonDocument.Parse(writer.ToString());
            var matrix = document.RootElement.GetProperty("capability_matrix");
            Assert.Equal(TeamMode.SoloConductor, matrix.GetProperty("team_mode").GetString());
            Assert.Empty(matrix.GetProperty("not_applicable_classes").EnumerateArray());
        }
    }

    private void SetSolo()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, TeamModeCommand.ExecuteSet(
            Context,
            ["--domain", Domain, "--team", Team, "--mode", TeamMode.SoloConductor, "--write", "--format", "json"],
            writer));
    }

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private CliContext Context => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };

    private sealed class FakeLister(IReadOnlyList<GitHubAutomationIssueCandidate> issues) : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => [];
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }
}
