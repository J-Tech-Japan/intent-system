using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class ClaimCommandG679Tests : IDisposable
{
    public ClaimCommandG679Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
    }

    [Fact]
    public async Task TwoClonesSameScope_ExactlyOnePushAcquiresAndLoserNamesHolder_G679()
    {
        using var repos = new ClaimRepositories();
        var first = Request("alice", "implementation");
        var second = Request("bob", "review");

        var results = await Task.WhenAll(
            Task.Run(() => ClaimCommand.RunTransaction(repos.FirstClone, first)),
            Task.Run(() => ClaimCommand.RunTransaction(repos.SecondClone, second)));

        var acquired = Assert.Single(results, result => result.Status == "acquired");
        var held = Assert.Single(results, result => result.Status == "held");
        Assert.True(acquired.PushSucceeded);
        Assert.False(held.PushSucceeded);
        Assert.Equal(acquired.Holder, held.Holder);
        Assert.NotNull(held.HolderTeam);
        Assert.DoesNotContain("force", held.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnrelatedRemoteAdvance_ReappliesFromFreshBaseAndBothScopesLand_G679()
    {
        using var repos = new ClaimRepositories();
        var first = Request("alice", "implementation");
        var second = Request("bob", "release") with
        {
            Scope = "release-prep:J-Tech-Japan/intent-system:0.19.1",
        };

        var results = await Task.WhenAll(
            Task.Run(() => ClaimCommand.RunTransaction(repos.FirstClone, first)),
            Task.Run(() => ClaimCommand.RunTransaction(repos.SecondClone, second)));

        Assert.All(results, result => Assert.Equal("acquired", result.Status));
        var inspection = repos.CloneForInspection();
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath(first.Scope))));
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath(second.Scope))));
        Assert.All(results, result => Assert.InRange(result.Attempts, 1, ClaimCommand.DefaultMaxAttempts));
    }

    [Fact]
    public void ExplicitTakeover_RecordsDisplacedHolderReasonAndAttribution_G679()
    {
        using var repos = new ClaimRepositories();
        var acquired = ClaimCommand.RunTransaction(repos.FirstClone, Request("alice", "implementation"));
        Assert.Equal("acquired", acquired.Status);

        var takeover = Request("bob", "orchestration") with
        {
            Operation = ClaimOperation.Takeover,
            Reason = "operator reassigned the release preparation",
            DisplacedHolder = "alice",
        };
        var result = ClaimCommand.RunTransaction(repos.SecondClone, takeover);

        Assert.Equal("taken-over", result.Status);
        Assert.True(result.PushSucceeded);
        Assert.Equal("alice", result.DisplacedHolder);
        Assert.NotNull(result.HistoryPath);

        var inspection = repos.CloneForInspection();
        var active = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(inspection, ClaimCommand.ClaimPath(takeover.Scope))));
        Assert.Equal("bob", active.RootElement.GetProperty("actor").GetString());
        var history = JsonDocument.Parse(File.ReadAllText(Path.Combine(inspection, result.HistoryPath!)));
        Assert.Equal("alice", history.RootElement.GetProperty("displaced_holder").GetString());
        Assert.Equal(takeover.Reason, history.RootElement.GetProperty("reason").GetString());
        Assert.Equal("bob", history.RootElement.GetProperty("actor").GetString());
    }

    [Fact]
    public void ExplicitRelease_RecordsActorTimestampAndReasonAndRemovesOnlyActiveClaim_G679()
    {
        using var repos = new ClaimRepositories();
        Assert.Equal("acquired",
            ClaimCommand.RunTransaction(repos.FirstClone, Request("alice", "implementation")).Status);
        var release = Request("alice", "implementation") with
        {
            Operation = ClaimOperation.Release,
            Reason = "unit closeout completed",
        };

        var result = ClaimCommand.RunTransaction(repos.FirstClone, release);

        Assert.Equal("released", result.Status);
        Assert.True(result.PushSucceeded);
        var inspection = repos.CloneForInspection();
        Assert.False(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath(release.Scope))));
        using var history = JsonDocument.Parse(File.ReadAllText(Path.Combine(inspection, result.HistoryPath!)));
        Assert.Equal("release", history.RootElement.GetProperty("operation").GetString());
        Assert.Equal("alice", history.RootElement.GetProperty("actor").GetString());
        Assert.Equal("unit closeout completed", history.RootElement.GetProperty("reason").GetString());
        Assert.True(history.RootElement.TryGetProperty("recorded_at", out _));
    }

    [Fact]
    public void Release_SameActorDifferentTeam_IsRefusedAndClaimRemains_G679()
    {
        using var repos = new ClaimRepositories();
        Assert.Equal("acquired",
            ClaimCommand.RunTransaction(repos.FirstClone, Request("shared-actor", "team-one")).Status);
        var wrongTeamRelease = Request("shared-actor", "team-two") with
        {
            Operation = ClaimOperation.Release,
            Reason = "different team attempted release",
        };

        var result = ClaimCommand.RunTransaction(repos.SecondClone, wrongTeamRelease);

        Assert.Equal("held", result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Equal("shared-actor", result.Holder);
        Assert.Equal("team-one", result.HolderTeam);
        Assert.Contains("actor and team", result.Detail, StringComparison.Ordinal);
        var inspection = repos.CloneForInspection();
        using var active = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(inspection, ClaimCommand.ClaimPath(wrongTeamRelease.Scope))));
        Assert.Equal("shared-actor", active.RootElement.GetProperty("actor").GetString());
        Assert.Equal("team-one", active.RootElement.GetProperty("team").GetString());
    }

    [Fact]
    public void TakeoverWithoutNamedDisplacedHolder_IsRefusedBeforeGitMutation_G679()
    {
        using var temp = new TempDirectory("claim-invalid-takeover-");
        using var writer = new StringWriter();

        var exit = ClaimCommand.ExecuteTakeover(
            Context(temp.Path),
            ["--scope", "execution-unit:G679", "--actor", "bob", "--team", "orchestration",
             "--reason", "operator reassigned", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--displaced-holder", writer.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, ".intent-cli", "claims")));
    }

    [Fact]
    public void PostCommitCleanupFailure_PreservesCommittedResultAndWarnsWithLeftoverPath_G738()
    {
        using var repos = new ClaimRepositories();
        using var output = new StringWriter();
        using var warnings = new StringWriter();
        var deleteAttempts = 0;
        string? leftoverPath = null;

        try
        {
            var exitCode = ClaimCommand.ExecuteAcquire(
                Context(repos.FirstClone),
                [
                    "--scope", "execution-unit:G738",
                    "--actor", "alice",
                    "--team", "implementation",
                    "--write",
                    "--format", "json",
                ],
                output,
                warnings,
                path =>
                {
                    leftoverPath = path;
                    deleteAttempts++;
                    throw new IOException("injected cleanup failure");
                });

            Assert.Equal(0, exitCode);
            using var emitted = JsonDocument.Parse(output.ToString());
            Assert.Equal("acquired", emitted.RootElement.GetProperty("status").GetString());
            Assert.True(emitted.RootElement.GetProperty("push_succeeded").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(emitted.RootElement.GetProperty("commit").GetString()));
            Assert.Equal(ClaimCommand.CleanupMaxAttempts, deleteAttempts);
            Assert.NotNull(leftoverPath);
            Assert.StartsWith(Path.GetTempPath(), leftoverPath!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(leftoverPath, warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("claim result and exit code are unchanged", warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("OS temp root", warnings.ToString(), StringComparison.Ordinal);

            var inspection = repos.CloneForInspection();
            Assert.True(File.Exists(Path.Combine(
                inspection, ClaimCommand.ClaimPath("execution-unit:G738"))));
        }
        finally
        {
            if (leftoverPath is not null && Directory.Exists(leftoverPath))
            {
                Directory.Delete(leftoverPath, recursive: true);
            }
        }
    }

    [Fact]
    public void PreCommitCleanupFailure_RemainsAFailure_G738()
    {
        using var repos = new ClaimRepositories();
        var scope = "execution-unit:G738";
        var acquired = ClaimCommand.RunTransaction(
            repos.FirstClone,
            new ClaimRequest(
                ClaimOperation.Acquire,
                scope,
                "alice",
                "implementation",
                null,
                null,
                true,
                "json",
                ClaimCommand.DefaultMaxAttempts));
        Assert.Equal("acquired", acquired.Status);

        using var warnings = new StringWriter();
        var deleteAttempts = 0;
        string? leftoverPath = null;
        try
        {
            var result = ClaimCommand.RunTransaction(
                repos.SecondClone,
                new ClaimRequest(
                    ClaimOperation.Acquire,
                    scope,
                    "bob",
                    "review",
                    null,
                    null,
                    true,
                    "json",
                    ClaimCommand.DefaultMaxAttempts),
                warnings,
                path =>
                {
                    leftoverPath = path;
                    deleteAttempts++;
                    throw new IOException("injected pre-commit cleanup failure");
                });

            Assert.Equal("held", result.Status);
            Assert.False(result.PushSucceeded);
            Assert.Equal("alice", result.Holder);
            Assert.Equal("implementation", result.HolderTeam);
            Assert.Equal(ClaimCommand.CleanupMaxAttempts, deleteAttempts);
            Assert.NotNull(leftoverPath);
            Assert.Contains(leftoverPath!, warnings.ToString(), StringComparison.Ordinal);
            Assert.Contains("claim result and exit code are unchanged", warnings.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (leftoverPath is not null && Directory.Exists(leftoverPath))
            {
                Directory.Delete(leftoverPath, recursive: true);
            }
        }
    }

    [Fact]
    public void FreshInit_ClaimsAreNonUnionAfterBroadJsonlRule_G679()
    {
        using var temp = new TempDirectory("claim-attributes-");
        Run(temp.Path, "git", "init", "--quiet");
        File.WriteAllText(Path.Combine(temp.Path, ".gitattributes"), "*.json merge=union\n");
        var context = Context(temp.Path);

        using var writer = new StringWriter();
        Assert.Equal(0, IntentInitCommand.Execute(
            context, ["--domain", "demo", "--write", "--format", "json"], writer));

        var lines = File.ReadAllLines(Path.Combine(temp.Path, ".gitattributes"));
        Assert.True(Array.IndexOf(lines, ".intent-cli/claims/** -merge")
            > Array.IndexOf(lines, ".intent-cli/**/*.jsonl merge=union"));
        Assert.Contains(".intent-cli/claims/** -merge", writer.ToString(), StringComparison.Ordinal);

        var claimAttr = Run(temp.Path, "git", "check-attr", "merge", "--", ".intent-cli/claims/example.json");
        var runAttr = Run(temp.Path, "git", "check-attr", "merge", "--", ".intent-cli/runs.jsonl");
        Assert.Contains("merge: unset", claimAttr, StringComparison.Ordinal);
        Assert.Contains("merge: union", runAttr, StringComparison.Ordinal);
    }

    [Fact]
    public void AgedClaim_IsDetectOnlyStaleFindingAndDoesNotMutateOwnership_G679()
    {
        using var temp = new TempDirectory("claim-stale-");
        var context = Context(temp.Path);
        Directory.CreateDirectory(Path.Combine(temp.Path, ".intent-cli", "claims"));
        var claim = new ClaimRecord(
            "1", "execution-unit:G679", "alice", "implementation",
            new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero), "abc123");
        var path = Path.Combine(temp.Path, ClaimCommand.ClaimPath(claim.Scope));
        File.WriteAllText(path, JsonSerializer.Serialize(claim));
        var before = File.ReadAllBytes(path);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyLister();
        AutomationStalledWorkCommand.UtcNowFactory = () =>
            new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationStalledWorkCommand.Execute(
            context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system",
             "--stale-minutes", "60", "--format", "json"],
            writer));

        using var document = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray(),
            candidate => candidate.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale);
        Assert.Equal("alice", item.GetProperty("claim_actor").GetString());
        Assert.Equal("implementation", item.GetProperty("claim_team").GetString());
        Assert.Equal("execution-unit:G679", item.GetProperty("claim_scope").GetString());
        Assert.True(item.TryGetProperty("last_evidence", out _));
        Assert.Equal("operator", item.GetProperty("required_actor").GetString());
        Assert.False(item.GetProperty("orchestrator_actionable").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void AgedClaim_SurvivesGitHubQuotaFailureAsPartialLocalFinding_G679()
    {
        using var temp = new TempDirectory("claim-stale-partial-");
        var context = Context(temp.Path);
        Directory.CreateDirectory(Path.Combine(temp.Path, ".intent-cli", "claims"));
        var claim = new ClaimRecord(
            "1", "execution-unit:G679", "alice", "implementation",
            new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero), "abc123");
        File.WriteAllText(
            Path.Combine(temp.Path, ClaimCommand.ClaimPath(claim.Scope)),
            JsonSerializer.Serialize(claim));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new QuotaLister();
        AutomationStalledWorkCommand.UtcNowFactory = () =>
            new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationStalledWorkCommand.Execute(
            context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system",
             "--stale-minutes", "60", "--format", "json"],
            writer));

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("partial").GetBoolean());
        Assert.False(document.RootElement.GetProperty("detection_available").GetBoolean());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray(),
            candidate => candidate.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale);
        Assert.True(item.GetProperty("partial").GetBoolean());
    }

    [Theory]
    [InlineData("execution-unit:G679")]
    [InlineData("release-prep:J-Tech-Japan/intent-system:0.19.1")]
    public void ScopeVocabulary_AcceptsOnlyPublishedKinds_G679(string scope)
    {
        Assert.True(ClaimCommand.TryValidateScope(scope, out _));
        Assert.False(ClaimCommand.TryValidateScope("issue:1470", out _));
    }

    private static ClaimRequest Request(string actor, string team) =>
        new(ClaimOperation.Acquire, "execution-unit:G679", actor, team,
            null, null, true, "json", ClaimCommand.DefaultMaxAttempts);

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

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-repos-");

        public ClaimRepositories()
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            SecondClone = Path.Combine(temp.Path, "second");
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
            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
            Run(temp.Path, "git", "clone", "--quiet", Bare, SecondClone);
        }

        public string Bare { get; }
        public string FirstClone { get; }
        public string SecondClone { get; }

        public string CloneForInspection()
        {
            var path = Path.Combine(temp.Path, $"inspect-{Guid.NewGuid():N}");
            Run(temp.Path, "git", "clone", "--quiet", Bare, path);
            return path;
        }

        public void Dispose() => temp.Dispose();
    }

    private sealed class EmptyLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => [];
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => [];
    }

    private sealed class QuotaLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => [];

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new GitHubApiQuotaExceededException("gh issue list", new GitHubApiDegradedState
            {
                Resource = "core",
                Remaining = 0,
                ResetAt = "2026-08-12T12:30:00Z",
            });
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
