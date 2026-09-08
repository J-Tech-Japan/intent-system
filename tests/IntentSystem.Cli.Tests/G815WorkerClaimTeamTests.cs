using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G815: the child worker must carry the explicit invoking team through the
/// issue claim path. These tests exercise the production command boundary,
/// the selector/claim pair, and the read-only refusal/no-label guarantees.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G815WorkerClaimTeamTests : IDisposable
{
    public G815WorkerClaimTeamTests()
    {
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
        WorkerNextActionCommand.CandidateListerFactory = null;
    }

    public void Dispose()
    {
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
        WorkerNextActionCommand.CandidateListerFactory = null;
    }

    [Fact]
    public void MatchingTeamPasses_OmittedAndWrongTeamRefuse_WithoutLabelEffects()
    {
        using var fixture = new ClaimFixture();
        fixture.WriteClaim("G815", "builder", "intent-cli-dev");
        var mutator = new RecordingMutator("intent-target");
        WorkerClaimCommand.MutatorFactory = () => mutator;
        WorkerClaimCommand.IssueLookupFactory = () => new IssueLookup("G815 team forwarding");

        var matching = ExecuteClaim(fixture.Context, "--team", "intent-cli-dev");
        Assert.True(matching.ExitCode == 0, string.Join(" | ", matching.Result.Errors));
        Assert.True(matching.Result.Proceed);
        Assert.False(matching.Result.Applied);
        Assert.Empty(mutator.Transitions);

        var omitted = ExecuteClaim(fixture.Context);
        Assert.Equal(2, omitted.ExitCode);
        Assert.False(omitted.Result.Proceed);
        Assert.Contains(omitted.Result.Errors, error =>
            error.Contains("--team is required", StringComparison.Ordinal));
        Assert.Empty(mutator.Transitions);

        var wrong = ExecuteClaim(fixture.Context, "--team", "other-team");
        Assert.Equal(2, wrong.ExitCode);
        Assert.False(wrong.Result.Proceed);
        Assert.Contains(wrong.Result.Errors, error =>
            error.Contains("does not hold it", StringComparison.Ordinal));
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void InvalidAuthorityRefusesClosedWithoutMutation()
    {
        using var fixture = new ClaimFixture();
        fixture.WriteRawClaim("G815", "not-json\n");
        var mutator = new RecordingMutator("intent-target");
        WorkerClaimCommand.MutatorFactory = () => mutator;
        WorkerClaimCommand.IssueLookupFactory = () => new IssueLookup("G815 authority unavailable");

        var result = ExecuteClaim(fixture.Context, "--team", "intent-cli-dev");

        Assert.Equal(2, result.ExitCode);
        Assert.False(result.Result.Proceed);
        Assert.Contains(result.Result.Errors, error =>
            error.Contains(WorkerClaimCompleteConstants.ErrorCodes.ClaimRegistryRefused, StringComparison.Ordinal));
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void BlankTeamArgumentIsRejectedBeforeAnyGitHubCall()
    {
        using var fixture = new ClaimFixture();
        using var writer = new StringWriter();

        var exitCode = WorkerClaimCommand.Execute(
            fixture.Context,
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "815",
                "--team", "",
                "--dry-run",
                "--github-only",
                "--format", "json",
            ],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--team requires a non-empty value", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SelectorAndClaimUseTheSameExplicitTeam_AndEligibleIssueBeatsBlockedClaim()
    {
        using var fixture = new ClaimFixture();
        fixture.WriteClaim("G815", "other-builder", "other-team");
        fixture.WriteClaim("G816", "builder", "intent-cli-dev");
        var blocked = Candidate(815, "G815 blocked", "2026-09-08T00:00:00Z", "intent-target", "intent-issue-in-progress");
        var eligible = Candidate(816, "G816 eligible", "2026-09-08T01:00:00Z", "intent-target");
        WorkerNextActionCommand.CandidateListerFactory = () => new CandidateLister(blocked, eligible);

        using var nextWriter = new StringWriter();
        var nextExit = WorkerNextActionCommand.Execute(
            fixture.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--team", "intent-cli-dev", "--github-only", "--format", "json"],
            nextWriter);

        Assert.Equal(0, nextExit);
        var next = JsonSerializer.Deserialize<WorkerNextActionResult>(nextWriter.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, next.Action);
        Assert.Equal(816, next.Number);

        WorkerClaimCommand.MutatorFactory = () => new RecordingMutator("intent-target");
        WorkerClaimCommand.IssueLookupFactory = () => new IssueLookup("G816 eligible");
        var claim = ExecuteClaim(fixture.Context, "--team", "intent-cli-dev", "--number", "816");
        Assert.Equal(0, claim.ExitCode);
        Assert.True(claim.Result.Proceed);
    }

    [Fact]
    public void GuidesRenderTeamArgumentAndExplicitMissingTeamPrerequisite_InMarkdownAndJson()
    {
        using var issueJsonWriter = new StringWriter();
        Assert.Equal(0, GuideWorkerIssueToPrCommand.Execute(
            GuideContext(),
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            issueJsonWriter));
        using var issueJson = JsonDocument.Parse(issueJsonWriter.ToString());
        Assert.Equal("intent-cli-dev", issueJson.RootElement.GetProperty("team").GetString());
        var issuePrompt = issueJson.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("worker next-action --repo <OWNER>/<REPO> --team intent-cli-dev", issuePrompt, StringComparison.Ordinal);
        Assert.Contains("worker claim --kind issue --number <n> --repo <OWNER>/<REPO> --team intent-cli-dev", issuePrompt, StringComparison.Ordinal);

        using var missingTeamWriter = new StringWriter();
        Assert.Equal(0, GuideWorkerIssueToPrCommand.Execute(
            GuideContext(),
            ["--format", "markdown"],
            missingTeamWriter));
        Assert.Contains("--team <TEAM>", missingTeamWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("never infer or substitute a team", missingTeamWriter.ToString(), StringComparison.Ordinal);

        using var loopWriter = new StringWriter();
        Assert.Equal(0, GuideWorkflowTaskImplementationLoopCommand.Execute(
            GuideContext(),
            ["--team", "intent-cli-dev", "--format", "json"],
            loopWriter));
        using var loopJson = JsonDocument.Parse(loopWriter.ToString());
        Assert.Contains("--team intent-cli-dev", loopJson.RootElement.GetProperty("prompt").GetString()!, StringComparison.Ordinal);

        using var orchestrationWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            GuideContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "codex", "--team", "intent-cli-dev", "--format", "markdown"],
            orchestrationWriter));
        Assert.Contains("worker next-action", orchestrationWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("--team intent-cli-dev", orchestrationWriter.ToString(), StringComparison.Ordinal);
    }

    private static (int ExitCode, WorkerClaimResult Result) ExecuteClaim(
        CliContext context,
        params string[] extra)
    {
        var args = new List<string>
        {
            "--repo", "J-Tech-Japan/intent-system",
            "--kind", "issue",
            "--number", "815",
            "--dry-run",
            "--github-only",
            "--format", "json",
        };
        for (var index = 0; index < extra.Length; index++) args.Add(extra[index]);

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(context, args.ToArray(), writer);
        return (exitCode, JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!);
    }

    private static CliContext GuideContext() => new()
    {
        RepoRoot = Path.GetTempPath(),
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" }
        }
    };

    private static GitHubAutomationIssueCandidate Candidate(
        int number,
        string title,
        string createdAt,
        params string[] labels) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
            CreatedAt = createdAt,
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private sealed class CandidateLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;

        public CandidateLister(params GitHubAutomationIssueCandidate[] issues) => this.issues = issues;

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        private readonly IReadOnlyList<string> labels;

        public RecordingMutator(params string[] labels) => this.labels = labels;

        public List<(IReadOnlyList<string> Add, IReadOnlyList<string> Remove)> Transitions { get; } = new();
        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) => labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();
        public void ApplyLabelTransitions(string repo, string kind, int number, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) => Transitions.Add((addLabels.ToArray(), removeLabels.ToArray()));
        public void ApplyReconcileTransitions(string repo, string kind, int number, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) => throw new NotSupportedException();
    }

    private sealed class IssueLookup : IGitHubIssueLookup
    {
        private readonly string title;

        public IssueLookup(string title) => this.title = title;

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => new()
        {
            Number = issueNumber,
            State = "OPEN",
            Title = title,
            Body = "# contract",
            Labels = new[] { new GitHubIssueLabel { Name = "intent-target" } },
        };
    }

    private sealed class ClaimFixture : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("g815-claim-").FullName;
        public CliContext Context { get; }

        public ClaimFixture()
        {
            Context = new CliContext
            {
                RepoRoot = root,
                Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" } }
            };
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
        }

        public void WriteClaim(string executionUnit, string actor, string team) => WriteRawClaim(
            executionUnit,
            JsonSerializer.Serialize(new ClaimRecord(
                "1",
                $"execution-unit:{executionUnit}",
                actor,
                team,
                DateTimeOffset.UtcNow,
                "g815-fixture")));

        public void WriteRawClaim(string executionUnit, string json)
        {
            var scope = $"execution-unit:{executionUnit}";
            var relative = ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
