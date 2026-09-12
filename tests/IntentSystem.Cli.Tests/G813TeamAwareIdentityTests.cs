using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G813: focused production-boundary coverage for the explicit invoking team
/// and the read-only host-loop identity envelope.  The fixtures intentionally
/// use only in-memory seams or a temporary claim store; no host metadata,
/// queue state, or GitHub label is touched by these tests.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G813TeamAwareIdentityTests : IDisposable
{
    public G813TeamAwareIdentityTests()
    {
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.IdentityCaptureFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = null;
    }

    public void Dispose()
    {
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.IdentityCaptureFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = null;
    }

    [Fact]
    public void WorkerClaim_LegacyImplementationActor_MatchingTeamProceeds_DifferentTeamRefusesWithoutMutation()
    {
        using var fixture = new LocalClaimFixture();
        fixture.WriteClaim("G813", "implementation", "intent-cli-dev");
        var mutator = new RecordingMutator("intent-target");
        WorkerClaimCommand.MutatorFactory = () => mutator;
        WorkerClaimCommand.IssueLookupFactory = () => new IssueLookup("G813 team-aware claim");

        var matching = ExecuteClaim(fixture, "intent-cli-dev");
        Assert.Equal(0, matching.ExitCode);
        Assert.True(matching.Result.Proceed);
        Assert.False(matching.Result.Applied);
        Assert.Equal("intent-cli-dev", matching.Result.Team);
        Assert.Equal("intent-cli-dev", matching.Result.InvokingTeam);
        Assert.Empty(mutator.Transitions);

        var differing = ExecuteClaim(fixture, "other-team");
        Assert.Equal(2, differing.ExitCode);
        Assert.False(differing.Result.Proceed);
        Assert.False(differing.Result.Applied);
        Assert.Contains(differing.Result.Errors, error =>
            error.Contains("does not hold it", StringComparison.Ordinal));
        Assert.Empty(mutator.Transitions);

        var omitted = ExecuteClaim(fixture, null);
        Assert.Equal(2, omitted.ExitCode);
        Assert.False(omitted.Result.Proceed);
        Assert.Contains(omitted.Result.Errors, error =>
            error.Contains("--team is required", StringComparison.Ordinal));
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void HostLoop_QualifiedTeamIdentity_IsRetainedAcrossJsonAndMarkdown_WithoutMutation()
    {
        var lister = new CountingLister();
        ConfigureIdentityFixtures(lister);

        var args = new[]
        {
            "--repo", "J-Tech-Japan/intent-system",
            "--domain", "intent-cli",
            "--team", "intent-cli-dev",
            "--task-id", "G813-implementation-v1",
            "--result-nonce", "G813-IMPLEMENTATION-TEAM-AWARE-20260912-1",
            "--routing-root", "/registered-host",
            "--sync-classification", "clean",
            "--timeout-seconds", "30",
            "--format", "json",
        };

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, AutomationHostLoopNextActionCommand.Execute(
            CreateContext(), args, jsonWriter));
        using var json = JsonDocument.Parse(jsonWriter.ToString());
        var root = json.RootElement;
        Assert.Equal("automation host-loop-next-action", root.GetProperty("operation").GetString());
        Assert.Equal("1", root.GetProperty("version").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("requested_repo").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("resolved_repo").GetString());
        Assert.Equal("intent-cli", root.GetProperty("requested_domain").GetString());
        Assert.Equal("intent-cli", root.GetProperty("resolved_domain").GetString());
        Assert.Equal("intent-cli-dev", root.GetProperty("requested_team").GetString());
        Assert.Equal("intent-cli-dev", root.GetProperty("resolved_team").GetString());
        Assert.Equal("intent-cli-dev", root.GetProperty("team").GetString());
        Assert.Equal("G813-implementation-v1", root.GetProperty("task_id").GetString());
        Assert.Equal("G813-IMPLEMENTATION-TEAM-AWARE-20260912-1", root.GetProperty("result_nonce").GetString());
        Assert.Equal("qualified", root.GetProperty("identity_qualification").GetString());
        Assert.Equal(
            "J-Tech-Japan/intent-system/intent-cli/intent-cli-dev/G813-implementation-v1/G813-IMPLEMENTATION-TEAM-AWARE-20260912-1/generation-1/digest-1",
            root.GetProperty("completion_identity").GetString());
        Assert.Equal("generation-1", root.GetProperty("dispatch_generation").GetString());
        Assert.Equal("digest-1", root.GetProperty("dispatch_digest").GetString());
        Assert.Equal("/registered-host", root.GetProperty("routing_root").GetString());
        Assert.Equal("/child", root.GetProperty("captured_cwd").GetString());
        Assert.False(root.GetProperty("mutation_allowed").GetBoolean());
        Assert.Equal(2, lister.TotalCalls);

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, AutomationHostLoopNextActionCommand.Execute(
            CreateContext(), args[..^2].Concat(["--format", "markdown"]).ToArray(), markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("team: `intent-cli-dev`", markdown, StringComparison.Ordinal);
        Assert.Contains("task_id: `G813-implementation-v1`", markdown, StringComparison.Ordinal);
        Assert.Contains("identity_qualification: `qualified`", markdown, StringComparison.Ordinal);
        Assert.Contains("captured_origin: `https://github.com/J-Tech-Japan/intent-system.git`", markdown, StringComparison.Ordinal);
        Assert.Equal(4, lister.TotalCalls);
    }

    [Fact]
    public void HostLoop_MissingIdentity_RefusesBeforeCandidateEnumeration()
    {
        var lister = new CountingLister();
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => lister;
        AutomationHostLoopNextActionCommand.IdentityCaptureFactory = _ => new HostLoopIdentityCapture
        {
            Cwd = "/child",
            Origin = "https://github.com/J-Tech-Japan/intent-system.git",
            Ref = "claude/g813-completion-channel",
            Head = "head-1",
        };
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ =>
            new FixedSyncProbe(HostSyncPreflightAnalyzer.ClassificationClean);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--domain", "intent-cli",
                "--team", "intent-cli-dev",
                "--task-id", "G813-implementation-v1",
                "--routing-root", "/registered-host",
                "--sync-classification", "clean",
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.Equal(AutomationHostLoopNextActionCommand.ClassificationIdentityUnresolved,
            root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("mutation_allowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("candidate_execution_unit").ValueKind);
        Assert.Equal("identity-unresolved", root.GetProperty("identity_qualification").GetString());
        var evidence = string.Join(" ", root.GetProperty("evidence").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Contains("result_nonce", evidence, StringComparison.Ordinal);
        Assert.Contains("dispatch_generation", evidence, StringComparison.Ordinal);
        Assert.Equal(0, lister.TotalCalls);
    }

    [Fact]
    public void HostLoop_DirtyIdentity_RefusesBeforeCandidateEnumeration()
    {
        var lister = new CountingLister();
        ConfigureIdentityFixtures(lister);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--domain", "intent-cli",
                "--team", "intent-cli-dev",
                "--task-id", "G813-implementation-v1",
                "--result-nonce", "G813-dirty",
                "--routing-root", "/registered-host",
                "--sync-classification", "dirty-mixed",
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.Equal(HostLoopNextActionAnalyzer.ClassificationDirtyHostState,
            root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("mutation_allowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("candidate_execution_unit").ValueKind);
        var evidence = string.Join(" ", root.GetProperty("evidence").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Contains("dirty-mixed", evidence, StringComparison.Ordinal);
        Assert.Contains("remote GitHub enumeration was not attempted", evidence, StringComparison.Ordinal);
        Assert.Equal(0, lister.TotalCalls);
    }

    private static void ConfigureIdentityFixtures(CountingLister lister)
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => lister;
        AutomationHostLoopNextActionCommand.IdentityCaptureFactory = _ => new HostLoopIdentityCapture
        {
            Cwd = "/child",
            Origin = "https://github.com/J-Tech-Japan/intent-system.git",
            Ref = "claude/g813-completion-channel",
            Head = "head-1",
            DispatchGeneration = "generation-1",
            DispatchDigest = "digest-1",
            RecipientContext = "intent-cli-dev",
            Owner = "builder",
            Action = "issue-to-pr",
            Deadline = "2026-09-12T06:00:00Z",
        };
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ =>
            new FixedNextSliceProbe("no-actionable-item");
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ =>
            new FixedRecoveryProbe();
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ =>
            new FixedSyncProbe(HostSyncPreflightAnalyzer.ClassificationClean);
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = _ =>
            new FixedDriftProbe();
    }

    private static (int ExitCode, WorkerClaimResult Result) ExecuteClaim(
        LocalClaimFixture fixture,
        string? team)
    {
        var args = new List<string>
        {
            "--repo", "J-Tech-Japan/intent-system",
            "--kind", "issue",
            "--number", "813",
            "--dry-run",
            "--github-only",
            "--format", "json",
        };
        if (team is not null)
        {
            args.Add("--team");
            args.Add(team);
        }

        using var writer = new StringWriter();
        var exit = WorkerClaimCommand.Execute(fixture.Context, args.ToArray(), writer);
        return (exit, JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!);
    }

    private static CliContext CreateContext() => new()
    {
        RepoRoot = Path.GetTempPath(),
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

    private sealed class CountingLister : IGitHubAutomationCandidateLister
    {
        public int TotalCalls { get; private set; }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels)
        {
            TotalCalls++;
            return Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels)
        {
            TotalCalls++;
            return Array.Empty<GitHubAutomationIssueCandidate>();
        }
    }

    private sealed class FixedNextSliceProbe : INextSliceDryRunProbe
    {
        private readonly string outcome;

        public FixedNextSliceProbe(string outcome) => this.outcome = outcome;

        public NextSliceProbeResult Probe(string repo, string domain) => new()
        {
            RecommendedOutcome = outcome,
        };
    }

    private sealed class FixedRecoveryProbe : IPublishRecoveryProbe
    {
        public PublishRecoveryProbeResult Probe(string repo) => new()
        {
            SafeRepairCount = 0,
            UnsafeStopCount = 0,
        };
    }

    private sealed class FixedSyncProbe : IHostSyncPreflightProbe
    {
        private readonly string classification;

        public FixedSyncProbe(string classification) => this.classification = classification;

        public HostSyncPreflightProbeResult Probe() => new() { Classification = classification };
    }

    private sealed class FixedDriftProbe : ICloseoutDriftCheckProbe
    {
        public CloseoutDriftCheckProbeResult Probe(string repo) => new()
        {
            SafeRepairCount = 0,
            UnsafeStopCount = 0,
        };
    }

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        private readonly IReadOnlyList<string> labels;

        public RecordingMutator(params string[] labels) => this.labels = labels;

        public List<(IReadOnlyList<string> Add, IReadOnlyList<string> Remove)> Transitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            Transitions.Add((addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
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
            Body = "# G813 contract",
            Labels = new[] { new GitHubIssueLabel { Name = "intent-target" } },
        };
    }

    private sealed class LocalClaimFixture : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("g813-claim-").FullName;

        public LocalClaimFixture()
        {
            Context = new CliContext
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
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
        }

        public CliContext Context { get; }

        public void WriteClaim(string executionUnit, string actor, string team)
        {
            var scope = $"execution-unit:{executionUnit}";
            var path = Path.Combine(
                root,
                ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var record = new ClaimRecord(
                "1",
                scope,
                actor,
                team,
                DateTimeOffset.UtcNow,
                "g813-focused-fixture");
            File.WriteAllText(path, JsonSerializer.Serialize(record));
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
