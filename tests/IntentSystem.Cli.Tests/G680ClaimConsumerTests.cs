using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class G680ClaimConsumerTests : IDisposable
{
    public G680ClaimConsumerTests()
    {
        WorkerNextActionCommand.CandidateListerFactory = null;
    }

    public void Dispose()
    {
        WorkerNextActionCommand.CandidateListerFactory = null;
    }

    [Fact]
    public void SharedVerifier_DistinguishesNoStoreUnheldOwnedAndOtherTeam_G680()
    {
        using var workspace = new Workspace();
        const string scope = "execution-unit:G680";

        var noStore = ClaimOwnershipVerifier.Verify(workspace.Root, scope, "team-b");
        Assert.True(noStore.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusNotConfigured, noStore.Status);

        Directory.CreateDirectory(Path.Combine(workspace.Root, ClaimCommand.ClaimsDirectory));
        var unheld = ClaimOwnershipVerifier.Verify(workspace.Root, scope, "team-b");
        Assert.False(unheld.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusUnheld, unheld.Status);
        Assert.Contains(scope, unheld.Detail, StringComparison.Ordinal);
        Assert.Contains("holder is none", unheld.Detail, StringComparison.Ordinal);

        workspace.WriteClaim(scope, "alice", "team-a");
        var owned = ClaimOwnershipVerifier.Verify(workspace.Root, scope, "team-a");
        Assert.True(owned.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, owned.Status);

        var other = ClaimOwnershipVerifier.Verify(workspace.Root, scope, "team-b");
        Assert.False(other.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusHeldByOtherTeam, other.Status);
        Assert.Equal("alice", other.Holder);
        Assert.Equal("team-a", other.HolderTeam);
        Assert.Contains("team-b", other.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void PacketQueueAndPublish_RefuseOtherTeamBeforeMutation_WithSameEvidence_G680()
    {
        using var workspace = new Workspace();
        workspace.WriteClaim("execution-unit:G680", "alice", "team-a");

        var packet = Execute((writer) => PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G680", "--team", "team-b", "--format", "json"],
            writer));
        var seed = Execute((writer) => AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G680", "--team", "team-b", "--format", "json"],
            writer));
        var publish = Execute((writer) => IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G680", "--repo", "J-Tech-Japan/intent-system", "--team", "team-b", "--format", "json"],
            writer));

        foreach (var result in new[] { packet, seed, publish })
        {
            Assert.Equal(1, result.ExitCode);
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            Assert.Equal("execution-unit:G680", root.GetProperty("scope").GetString());
            Assert.Equal("alice", root.GetProperty("holder").GetString());
            Assert.Equal("team-a", root.GetProperty("holder_team").GetString());
            Assert.Equal(ClaimOwnershipVerification.StatusHeldByOtherTeam, root.GetProperty("status").GetString());
        }

        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli", "issues", "G680")));
        Assert.False(File.Exists(Path.Combine(workspace.Root, ".intent-cli", "queue-state.json")));
        Assert.False(File.Exists(Path.Combine(workspace.Root, ".intent-cli", "runs.jsonl")));
    }

    [Fact]
    public void NoClaimsStore_AllGatedSurfacesRemainByteIdenticalWhenTeamIsSupplied_G680()
    {
        using var workspace = new Workspace();

        Assert.Equal(
            Execute(writer => PacketDraftCommand.Execute(
                workspace.Context, ["--execution-unit", "G680", "--dry-run", "--format", "json"], writer)),
            Execute(writer => PacketDraftCommand.Execute(
                workspace.Context, ["--execution-unit", "G680", "--team", "team-a", "--dry-run", "--format", "json"], writer)));

        Assert.Equal(
            Execute(writer => AutomationQueueSeedFromPacketCommand.Execute(
                workspace.Context, ["--execution-unit", "G680", "--format", "json"], writer)),
            Execute(writer => AutomationQueueSeedFromPacketCommand.Execute(
                workspace.Context, ["--execution-unit", "G680", "--team", "team-a", "--format", "json"], writer)));

        Assert.Equal(
            Execute(writer => IssuePublishFlowCommand.Execute(
                workspace.Context, ["G680", "--repo", "J-Tech-Japan/intent-system", "--format", "json"], writer)),
            Execute(writer => IssuePublishFlowCommand.Execute(
                workspace.Context, ["G680", "--repo", "J-Tech-Japan/intent-system", "--team", "team-a", "--format", "json"], writer)));

        var lister = new FakeLister(BuildIssue(labels: ["intent-target"]));
        WorkerNextActionCommand.CandidateListerFactory = () => lister;
        Assert.Equal(
            Execute(writer => WorkerNextActionCommand.Execute(
                workspace.Context, ["--repo", "J-Tech-Japan/intent-system", "--format", "json"], writer)),
            Execute(writer => WorkerNextActionCommand.Execute(
                workspace.Context, ["--repo", "J-Tech-Japan/intent-system", "--team", "team-a", "--format", "json"], writer)));
    }

    [Fact]
    public void WorkerNextAction_ClaimVerdictGoverns_AndLabelRefusalRemainsDefenceInDepth_G680()
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, ClaimCommand.ClaimsDirectory));
        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister(BuildIssue(labels: ["intent-target"]));

        var unheld = Execute(writer => WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--team", "team-b", "--format", "json"],
            writer));
        Assert.Equal(0, unheld.ExitCode);
        using (var document = JsonDocument.Parse(unheld.Output))
        {
            Assert.Equal(WorkerNextActionConstants.Actions.Wait, document.RootElement.GetProperty("action").GetString());
            Assert.Equal(WorkerNextActionConstants.SourceClassifications.ClaimRefused, document.RootElement.GetProperty("source_classification").GetString());
            Assert.Contains("holder is none", document.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
        }

        workspace.WriteClaim("execution-unit:G680", "alice", "team-a");
        var otherTeam = Execute(writer => WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--team", "team-b", "--format", "json"],
            writer));
        using (var document = JsonDocument.Parse(otherTeam.Output))
        {
            Assert.Equal(WorkerNextActionConstants.Actions.Wait, document.RootElement.GetProperty("action").GetString());
            Assert.Contains("alice", document.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
            Assert.Contains("team-a", document.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
        }

        var ownTeam = Execute(writer => WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--team", "team-a", "--format", "json"],
            writer));
        using (var document = JsonDocument.Parse(ownTeam.Output))
        {
            Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, document.RootElement.GetProperty("action").GetString());
        }

        WorkerNextActionCommand.CandidateListerFactory = () => new FakeLister(
            BuildIssue(labels: ["intent-target", "intent-issue-in-progress"]));
        var labelled = Execute(writer => WorkerNextActionCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--team", "team-a", "--format", "json"],
            writer));
        using var labelledDocument = JsonDocument.Parse(labelled.Output);
        Assert.Equal(WorkerNextActionConstants.Actions.None, labelledDocument.RootElement.GetProperty("action").GetString());
    }

    [Fact]
    public void NextSlice_ExcludesClaimedElsewhereButKeepsOwnAndUnheldCandidates_G680()
    {
        using var workspace = new Workspace();
        workspace.WritePreparedPacket("G680");
        workspace.WriteClaim("execution-unit:G680", "alice", "team-a");

        var otherTeam = IntentNextSliceCommand.Analyze(
            workspace.Context, "intent-cli", "J-Tech-Japan/intent-system", runtimeCreationAllowed: false, team: "team-b");
        Assert.Null(otherTeam.Candidate);
        var exclusion = Assert.Single(otherTeam.ClaimExclusions!);
        Assert.Equal("G680", exclusion.ExecutionUnit);
        Assert.Equal("alice", exclusion.Holder);
        Assert.Equal("team-a", exclusion.HolderTeam);
        Assert.Contains(IntentNextSliceCommand.WarningClaimedElsewhere, otherTeam.Warnings);

        var ownTeam = IntentNextSliceCommand.Analyze(
            workspace.Context, "intent-cli", "J-Tech-Japan/intent-system", runtimeCreationAllowed: false, team: "team-a");
        Assert.Equal("G680", ownTeam.Candidate?.ExecutionUnit);
        Assert.Null(ownTeam.ClaimExclusions);

        File.Delete(Path.Combine(workspace.Root, ClaimCommand.ClaimPath("execution-unit:G680")));
        var unheld = IntentNextSliceCommand.Analyze(
            workspace.Context, "intent-cli", "J-Tech-Japan/intent-system", runtimeCreationAllowed: false, team: "team-b");
        Assert.Equal("G680", unheld.Candidate?.ExecutionUnit);
        Assert.Null(unheld.ClaimExclusions);
    }

    [Fact]
    public void ClaimThenDraft_WinnerScaffoldsN_LoserRetriesNextNumberExactlyOnce_G680()
    {
        using var workspace = new Workspace();
        workspace.WriteClaim("execution-unit:G900", "alice", "team-a");

        var winner = Execute(writer => PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G900", "--team", "team-a", "--format", "json"],
            writer));
        Assert.Equal(0, winner.ExitCode);

        var loserAtN = Execute(writer => PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G900", "--team", "team-b", "--format", "json"],
            writer));
        Assert.Equal(1, loserAtN.ExitCode);

        // The protocol permits one fresh-base recompute. Model that single
        // retry with the next number; packet draft still refuses any scaffold
        // that is not preceded by the winning claim.
        workspace.WriteClaim("execution-unit:G901", "bob", "team-b");
        var onlyRetry = Execute(writer => PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G901", "--team", "team-b", "--format", "json"],
            writer));
        Assert.Equal(0, onlyRetry.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli", "issues", "G900")));
        Assert.True(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli", "issues", "G901")));
    }

    [Fact]
    public void GuidesAndDocs_RenderAllClaimAwareRoutesWithEnglishJapaneseParity_G680()
    {
        using var workspace = new Workspace();

        var packetGuide = Execute(writer => GuideWorkflowTaskPacketDraftCommand.Execute(
            workspace.Context, ["--format", "markdown"], writer)).Output;
        Assert.Contains("claim-then-draft", packetGuide, StringComparison.Ordinal);
        Assert.Contains("exactly once", packetGuide, StringComparison.Ordinal);
        Assert.Contains("--team <team>", packetGuide, StringComparison.Ordinal);

        var publishGuide = Execute(writer => GuideWorkflowTaskIssuePublishCommand.Execute(
            workspace.Context, ["--format", "markdown"], writer)).Output;
        Assert.Contains("claim verify", publishGuide, StringComparison.Ordinal);
        Assert.Contains("holder team", publishGuide, StringComparison.Ordinal);

        var nextGuide = Execute(writer => GuideNextCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "J-Tech-Japan/intent-system", "--role", "design"],
            writer)).Output;
        Assert.Contains("intent-cli claim verify", nextGuide, StringComparison.Ordinal);
        Assert.Contains("retries that new scope exactly once", nextGuide, StringComparison.Ordinal);

        var orchestratorGuide = Execute(writer => GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"],
            writer)).Output;
        Assert.Contains("intent-cli claim verify", orchestratorGuide, StringComparison.Ordinal);
        Assert.Contains("release-prep", orchestratorGuide, StringComparison.Ordinal);

        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var packets = File.ReadAllText(Path.Combine(root, "docs", language, "04-packets-issues.md"));
            var loop = File.ReadAllText(Path.Combine(root, "docs", language, "05-implementation-loop.md"));
            var release = File.ReadAllText(Path.Combine(root, "docs", language, "09-developer-reference.md"));
            var ledger = File.ReadAllText(Path.Combine(root, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("claim-then-draft", packets, StringComparison.Ordinal);
            Assert.Contains("claim verify", loop, StringComparison.Ordinal);
            Assert.Contains("release-prep:<owner/repo>:0.19.1", release, StringComparison.Ordinal);
            Assert.Contains("G680", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }
    }

    private static (int ExitCode, string Output) Execute(Func<StringWriter, int> action)
    {
        using var writer = new StringWriter();
        var exitCode = action(writer);
        return (exitCode, writer.ToString());
    }

    private static GitHubAutomationIssueCandidate BuildIssue(IReadOnlyList<string> labels) =>
        new()
        {
            Number = 1472,
            Title = "G680 claim consumers",
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/1472",
            CreatedAt = "2026-08-12T00:00:00Z",
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private sealed class FakeLister(GitHubAutomationIssueCandidate issue) : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => [];

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => [issue];
    }

    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Root = Directory.CreateTempSubdirectory("g680-claim-consumers-").FullName;
            Context = new CliContext
            {
                RepoRoot = Root,
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

        public string Root { get; }
        public CliContext Context { get; }

        public void WriteClaim(string scope, string actor, string team)
        {
            var path = Path.Combine(Root, ClaimCommand.ClaimPath(scope));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new ClaimRecord(
                "1", scope, actor, team, DateTimeOffset.Parse("2026-08-12T00:00:00Z"), "abc123")));
        }

        public void WritePreparedPacket(string executionUnit)
        {
            var directory = Path.Combine(Root, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"), $"""
                implementation_issue_packet:
                  source_execution_unit: {executionUnit}
                  domain: intent-cli
                  target_repo: J-Tech-Japan/intent-system
                """);
            File.WriteAllText(Path.Combine(directory, "implementation.md"), "# implementation\n");
            File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
            var body = "# " + executionUnit + " claim consumer\n\n"
                + string.Join("\n\n", PacketDraftCommand.RequiredContractSections.Select(section =>
                    $"## {section}\n\n" + (section == "Related Links" ? "- https://example.test/G680" : "Complete contract content.")))
                + "\n";
            File.WriteAllText(Path.Combine(directory, "github-body.md"), body);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
