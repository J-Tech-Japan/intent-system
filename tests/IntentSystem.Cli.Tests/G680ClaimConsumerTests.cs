using System.Diagnostics;
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
    public void SharedVerifier_InvalidScopeIsRefusedBeforeNoStoreCompatibility_G680()
    {
        using var repos = new ClaimRepositories();

        var command = Execute(writer => ClaimVerificationCommand.Execute(
            Context(repos.SecondClone),
            ["--scope", "issue:1472", "--team", "team-b", "--format", "json"],
            writer));

        Assert.Equal(1, command.ExitCode);
        using var document = JsonDocument.Parse(command.Output);
        Assert.False(document.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(ClaimOwnershipVerification.StatusInvalid,
            document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.GetProperty("store_configured").GetBoolean());
        Assert.Contains("execution-unit:<EU>",
            document.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StaleLoserClone_AllConsumersReadRemoteOtherTeamHolder_G680()
    {
        using var repos = new ClaimRepositories();
        var loserContext = Context(repos.SecondClone);
        WritePreparedPacket(repos.SecondClone, "G680");

        // Both clones already exist before the winning push. The loser has no
        // local claims directory and must not infer not-configured from that.
        var acquired = ClaimCommand.RunTransaction(
            repos.FirstClone, Request("execution-unit:G680", "alice", "team-a"));
        Assert.Equal("acquired", acquired.Status);
        Assert.True(acquired.PushSucceeded);
        Assert.False(Directory.Exists(Path.Combine(repos.SecondClone, ClaimCommand.ClaimsDirectory)));

        var packet = Execute(writer => PacketDraftCommand.Execute(
            loserContext,
            ["--execution-unit", "G680", "--team", "team-b", "--format", "json"],
            writer));
        var seed = Execute(writer => AutomationQueueSeedFromPacketCommand.Execute(
            loserContext,
            ["--execution-unit", "G680", "--team", "team-b", "--format", "json"],
            writer));
        var publish = Execute(writer => IssuePublishFlowCommand.Execute(
            loserContext,
            ["G680", "--repo", "J-Tech-Japan/intent-system", "--team", "team-b", "--format", "json"],
            writer));

        foreach (var result in new[] { packet, seed, publish })
        {
            Assert.Equal(1, result.ExitCode);
            AssertOtherTeamHolder(result.Output, "execution-unit:G680", "alice", "team-a");
        }

        WorkerNextActionCommand.CandidateListerFactory = () =>
            new FakeLister(BuildIssue(labels: ["intent-target"]));
        var worker = Execute(writer => WorkerNextActionCommand.Execute(
            loserContext,
            ["--repo", "J-Tech-Japan/intent-system", "--team", "team-b", "--format", "json"],
            writer));
        Assert.Equal(0, worker.ExitCode);
        using (var workerDocument = JsonDocument.Parse(worker.Output))
        {
            Assert.Equal(WorkerNextActionConstants.Actions.Wait,
                workerDocument.RootElement.GetProperty("action").GetString());
            Assert.Contains("alice", workerDocument.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
            Assert.Contains("team-a", workerDocument.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
        }

        var nextSlice = IntentNextSliceCommand.Analyze(
            loserContext, "intent-cli", "J-Tech-Japan/intent-system",
            runtimeCreationAllowed: false, team: "team-b");
        Assert.Null(nextSlice.Candidate);
        var exclusion = Assert.Single(nextSlice.ClaimExclusions!);
        Assert.Equal("G680", exclusion.ExecutionUnit);
        Assert.Equal("alice", exclusion.Holder);
        Assert.Equal("team-a", exclusion.HolderTeam);
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
        using var repos = new ClaimRepositories();
        var context = Context(repos.SecondClone);
        WritePreparedPacket(repos.SecondClone, "G680");
        Assert.False(Directory.Exists(Path.Combine(repos.SecondClone, ClaimCommand.ClaimsDirectory)));

        Assert.Equal(
            Execute(writer => PacketDraftCommand.Execute(
                context, ["--execution-unit", "G680", "--dry-run", "--format", "json"], writer)),
            Execute(writer => PacketDraftCommand.Execute(
                context, ["--execution-unit", "G680", "--team", "team-a", "--dry-run", "--format", "json"], writer)));

        Assert.Equal(
            Execute(writer => AutomationQueueSeedFromPacketCommand.Execute(
                context, ["--execution-unit", "G680", "--format", "json"], writer)),
            Execute(writer => AutomationQueueSeedFromPacketCommand.Execute(
                context, ["--execution-unit", "G680", "--team", "team-a", "--format", "json"], writer)));

        Assert.Equal(
            Execute(writer => IssuePublishFlowCommand.Execute(
                context, ["G680", "--repo", "J-Tech-Japan/intent-system", "--format", "json"], writer)),
            Execute(writer => IssuePublishFlowCommand.Execute(
                context, ["G680", "--repo", "J-Tech-Japan/intent-system", "--team", "team-a", "--format", "json"], writer)));

        var lister = new FakeLister(BuildIssue(labels: ["intent-target"]));
        WorkerNextActionCommand.CandidateListerFactory = () => lister;
        Assert.Equal(
            Execute(writer => WorkerNextActionCommand.Execute(
                context, ["--repo", "J-Tech-Japan/intent-system", "--format", "json"], writer)),
            Execute(writer => WorkerNextActionCommand.Execute(
                context, ["--repo", "J-Tech-Japan/intent-system", "--team", "team-a", "--format", "json"], writer)));

        Assert.Equal(
            Execute(writer => IntentNextSliceCommand.Execute(
                context,
                ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
                writer)),
            Execute(writer => IntentNextSliceCommand.Execute(
                context,
                ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--team", "team-a", "--format", "json"],
                writer)));
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
    public async Task ClaimThenDraft_PushCasRaceStopsAfterOneRecomputedRetry_G680()
    {
        using var repos = new ClaimRepositories();
        var contenders = new[]
        {
            (Root: repos.FirstClone, Actor: "alice", Team: "team-a"),
            (Root: repos.SecondClone, Actor: "bob", Team: "team-b"),
        };

        var firstRace = await Task.WhenAll(contenders.Select(contender => Task.Run(() =>
            ClaimCommand.RunTransaction(
                contender.Root, Request("execution-unit:G900", contender.Actor, contender.Team)))));
        var acquired = Assert.Single(firstRace, result => result.Status == "acquired");
        Assert.Single(firstRace, result => result.Status == "held");
        var winner = Assert.Single(contenders, contender => contender.Actor == acquired.Holder);
        var loser = Assert.Single(contenders, contender => contender.Actor != acquired.Holder);

        var winnerDraft = Execute(writer => PacketDraftCommand.Execute(
            Context(winner.Root),
            ["--execution-unit", "G900", "--team", winner.Team, "--format", "json"],
            writer));
        Assert.Equal(0, winnerDraft.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(winner.Root, ".intent-cli", "issues", "G900")));
        Assert.False(Directory.Exists(Path.Combine(loser.Root, ".intent-cli", "issues", "G900")));
        Assert.False(Directory.Exists(Path.Combine(repos.ThirdClone, ".intent-cli", "issues", "G900")));

        // The loser explicitly refreshes the canonical base and recomputes
        // one next number. A third pre-existing clone wins that N+1 scope
        // before the loser's sole retry, modeling the second loss.
        Run(loser.Root, "git", "pull", "--ff-only", "origin", "main");
        var recomputed = RecomputeNextExecutionUnit(loser.Root);
        Assert.Equal("G901", recomputed);
        var secondWinner = ClaimCommand.RunTransaction(
            repos.ThirdClone, Request($"execution-unit:{recomputed}", "charlie", "team-c"));
        Assert.Equal("acquired", secondWinner.Status);

        var onlyRetry = ClaimCommand.RunTransaction(
            loser.Root, Request($"execution-unit:{recomputed}", loser.Actor, loser.Team));
        Assert.Equal("held", onlyRetry.Status);
        Assert.Equal(1, onlyRetry.Attempts);
        Assert.Equal("charlie", onlyRetry.Holder);
        Assert.Equal("team-c", onlyRetry.HolderTeam);

        var inspection = repos.CloneForInspection();
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath("execution-unit:G900"))));
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath("execution-unit:G901"))));
        Assert.False(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath("execution-unit:G902"))));
        Assert.False(Directory.Exists(Path.Combine(loser.Root, ".intent-cli", "issues", "G901")));
        Assert.False(Directory.Exists(Path.Combine(loser.Root, ".intent-cli", "issues", "G902")));
        Assert.False(Directory.Exists(Path.Combine(repos.ThirdClone, ".intent-cli", "issues", "G901")));
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
            Assert.Contains("release-prep:<owner/repo>:0.20.0", release, StringComparison.Ordinal);
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

    private static void AssertOtherTeamHolder(
        string output, string scope, string holder, string holderTeam)
    {
        using var document = JsonDocument.Parse(output);
        Assert.Equal(scope, document.RootElement.GetProperty("scope").GetString());
        Assert.Equal(holder, document.RootElement.GetProperty("holder").GetString());
        Assert.Equal(holderTeam, document.RootElement.GetProperty("holder_team").GetString());
        Assert.Equal(ClaimOwnershipVerification.StatusHeldByOtherTeam,
            document.RootElement.GetProperty("status").GetString());
    }

    private static ClaimRequest Request(string scope, string actor, string team) =>
        new(ClaimOperation.Acquire, scope, actor, team, null, null, true, "json", ClaimCommand.DefaultMaxAttempts);

    private static CliContext Context(string root) => new()
    {
        RepoRoot = root,
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

    private static string RecomputeNextExecutionUnit(string root)
    {
        var directory = Path.Combine(root, ClaimCommand.ClaimsDirectory);
        var highest = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .Select(json => JsonSerializer.Deserialize<ClaimRecord>(json))
            .Where(record => record?.Scope.StartsWith("execution-unit:G", StringComparison.Ordinal) == true)
            .Select(record => int.Parse(record!.Scope["execution-unit:G".Length..]))
            .DefaultIfEmpty(0)
            .Max();
        return $"G{highest + 1}";
    }

    private static string Run(string workdir, string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    private static void WritePreparedPacket(string root, string executionUnit)
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", executionUnit);
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
            Context = G680ClaimConsumerTests.Context(Root);
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
            => G680ClaimConsumerTests.WritePreparedPacket(Root, executionUnit);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("g680-claim-repos-").FullName;

        public ClaimRepositories()
        {
            Bare = Path.Combine(root, "origin.git");
            var seed = Path.Combine(root, "seed");
            FirstClone = Path.Combine(root, "first");
            SecondClone = Path.Combine(root, "second");
            ThirdClone = Path.Combine(root, "third");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "seed");
            Run(seed, "git", "config", "user.email", "seed@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "seed\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(root, "git", "clone", "--quiet", Bare, FirstClone);
            Run(root, "git", "clone", "--quiet", Bare, SecondClone);
            Run(root, "git", "clone", "--quiet", Bare, ThirdClone);
        }

        public string Bare { get; }
        public string FirstClone { get; }
        public string SecondClone { get; }
        public string ThirdClone { get; }

        public string CloneForInspection()
        {
            var path = Path.Combine(root, $"inspect-{Guid.NewGuid():N}");
            Run(root, "git", "clone", "--quiet", Bare, path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
