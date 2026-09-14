using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G823 (#1778): publish-lifecycle-repair binds each unit to the repository in
/// its created_issue_url before any evidence lookup. On a host that carries
/// several repositories' packets an issue number is not an identifier (node 03,
/// G603), and evidence is looked up by number against --repo, so a unit bound
/// elsewhere must never reach analysis, lookup, or write.
/// </summary>
// G823: both classes replace the command's static lister and lookup seams,
// so they must not run in parallel.
[Collection("PublishLifecycleRepairSharedState")]
public sealed class G823PublishLifecycleRepositoryBindingTests : IDisposable
{
    private const string RepoX = "J-Tech-Japan/sekiban-dcb-ts";
    private const string RepoY = "J-Tech-Japan/SekibanWasmRuntime";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public G823PublishLifecycleRepositoryBindingTests() => ResetSeams();

    public void Dispose() => ResetSeams();

    [Fact]
    public void IssueScope_NearPair_EachRepositorySelectsOnlyItsOwnUnit()
    {
        using var host = new FixtureHost();
        host.WriteArtifact("SDT-G69", 133, $"https://github.com/{RepoX}/issues/133", "issue-created");
        host.WriteArtifact("SWR-G010", 133, $"https://github.com/{RepoY}/issues/133", "issue-created");
        var lookups = host.InstallTargetIssue(133);

        var x = host.Run(RepoX, "--issue", "133");
        Assert.Equal(["SDT-G69"], x.Entries.Select(e => e.ExecutionUnit));
        var xExcluded = Assert.Single(x.Excluded);
        Assert.Equal("SWR-G010", xExcluded.ExecutionUnit);
        Assert.Equal(AutomationPublishLifecycleRepairCommand.ExclusionForeignRepository, xExcluded.Reason);
        Assert.Equal(RepoY, xExcluded.BoundRepository);
        Assert.Equal(1, x.ExcludedCount);

        var y = host.Run(RepoY, "--issue", "133");
        Assert.Equal(["SWR-G010"], y.Entries.Select(e => e.ExecutionUnit));
        var yExcluded = Assert.Single(y.Excluded);
        Assert.Equal("SDT-G69", yExcluded.ExecutionUnit);
        Assert.Equal(RepoX, yExcluded.BoundRepository);

        Assert.DoesNotContain(lookups.Calls, call => call.Repo != RepoX && call.Repo != RepoY);
    }

    [Fact]
    public void IssueScopeWrite_RepairsTheBoundUnit_AndLeavesTheForeignUnitBytesUnchanged()
    {
        using var host = new FixtureHost();
        host.WriteArtifact("SDT-G69", 133, $"https://github.com/{RepoX}/issues/133", "issue-created");
        host.WriteArtifact("SWR-G010", 133, $"https://github.com/{RepoY}/issues/133", "issue-created");
        host.InstallTargetIssue(133);
        var foreignBefore = File.ReadAllBytes(host.ArtifactPath("SWR-G010"));

        var result = host.Run(RepoX, "--issue", "133", "--write");

        Assert.Equal(["SDT-G69"], result.AppliedUnits);
        Assert.Equal("published",
            IssuePublishArtifactYaml.Deserialize(File.ReadAllText(host.ArtifactPath("SDT-G69"))).LifecycleState);
        Assert.Equal(foreignBefore, File.ReadAllBytes(host.ArtifactPath("SWR-G010")));
    }

    [Fact]
    public void UnscopedSweep_ExcludesForeignAndUnprovenUnits_BeforeAnyLookup_AndWritesNeither()
    {
        using var host = new FixtureHost();
        host.WriteArtifact("SDT-G69", 133, $"https://github.com/{RepoX}/issues/133", "issue-created");
        host.WriteArtifact("SWR-G010", 133, $"https://github.com/{RepoY}/issues/133", "issue-created");
        host.WriteArtifact("SWR-G011", 140, $"https://github.com/{RepoY}/issues/140", "issue-created");
        // Number without URL: the caller repository's #150 carries intent-target, which
        // would make this unit stale-issue-created if it were judged on that issue.
        host.WriteArtifact("SKS-G668", 150, createdIssueUrl: null, "issue-created");
        var lookups = host.InstallTargetIssue(133, 140, 150);
        var foreign140 = File.ReadAllBytes(host.ArtifactPath("SWR-G011"));
        var foreign133 = File.ReadAllBytes(host.ArtifactPath("SWR-G010"));
        var unproven = File.ReadAllBytes(host.ArtifactPath("SKS-G668"));

        var result = host.Run(RepoX, "--write");

        Assert.Equal(["SDT-G69"], result.Entries.Select(e => e.ExecutionUnit));
        Assert.Equal(["SDT-G69"], result.AppliedUnits);
        Assert.Equal(
            [
                ("SKS-G668", AutomationPublishLifecycleRepairCommand.ExclusionIssueRepositoryUnproven),
                ("SWR-G010", AutomationPublishLifecycleRepairCommand.ExclusionForeignRepository),
                ("SWR-G011", AutomationPublishLifecycleRepairCommand.ExclusionForeignRepository),
            ],
            result.Excluded.Select(e => (e.ExecutionUnit, e.Reason)).OrderBy(pair => pair.ExecutionUnit, StringComparer.Ordinal));
        Assert.Equal(3, result.ExcludedCount);

        // No lookup is made for 140 or 150: only the bound unit's issue is ever read.
        Assert.All(lookups.Calls, call => Assert.Equal((RepoX, 133), (call.Repo, call.Number)));
        Assert.Equal(foreign140, File.ReadAllBytes(host.ArtifactPath("SWR-G011")));
        Assert.Equal(foreign133, File.ReadAllBytes(host.ArtifactPath("SWR-G010")));
        Assert.Equal(unproven, File.ReadAllBytes(host.ArtifactPath("SKS-G668")));
    }

    [Fact]
    public void ExecutionUnitScope_ForeignUnit_IsExcludedAndNothingIsApplied()
    {
        using var host = new FixtureHost();
        host.WriteArtifact("SWR-G010", 133, $"https://github.com/{RepoY}/issues/133", "issue-created");
        var lookups = host.InstallTargetIssue(133);
        var before = File.ReadAllBytes(host.ArtifactPath("SWR-G010"));

        var result = host.Run(RepoX, "--execution-unit", "SWR-G010", "--write");

        Assert.Empty(result.Entries);
        Assert.Empty(result.AppliedUnits);
        var excluded = Assert.Single(result.Excluded);
        Assert.Equal(AutomationPublishLifecycleRepairCommand.ExclusionForeignRepository, excluded.Reason);
        Assert.Empty(lookups.Calls);
        Assert.Equal(before, File.ReadAllBytes(host.ArtifactPath("SWR-G010")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("https://ghe.example.com/J-Tech-Japan/sekiban-dcb-ts/issues/133")]
    [InlineData("https://github.com/J-Tech-Japan/sekiban-dcb-ts/issues/134")]
    [InlineData("https://github.com/J-Tech-Japan/sekiban-dcb-ts/pull/133")]
    [InlineData("http://github.com/J-Tech-Japan/sekiban-dcb-ts/issues/133")]
    public void UnprovableRepository_IsExcluded_NotAssumedToBeTheCallerRepository(string? url)
    {
        using var host = new FixtureHost();
        host.WriteArtifact("SDT-G70", 133, url, "issue-created");
        var lookups = host.InstallTargetIssue(133);

        var result = host.Run(RepoX, "--write");

        Assert.Empty(result.Entries);
        Assert.Empty(result.AppliedUnits);
        Assert.Equal(AutomationPublishLifecycleRepairCommand.ExclusionIssueRepositoryUnproven, Assert.Single(result.Excluded).Reason);
        Assert.Empty(lookups.Calls);
    }

    [Theory]
    [InlineData("J-Tech-Japan/sekiban-dcb-ts")]
    [InlineData("j-tech-japan/SEKIBAN-DCB-TS")]
    [InlineData("J-Tech-Japan/sekiban-dcb-ts.git")]
    public void RepositoryComparison_IsCaseInsensitive_AndIgnoresGitSuffix(string callerRepo)
    {
        using var host = new FixtureHost();
        host.WriteArtifact("SDT-G69", 133, "https://github.com/J-Tech-Japan/sekiban-dcb-ts/issues/133", "issue-created");
        host.InstallTargetIssue(133);

        var result = host.Run(callerRepo);

        Assert.Equal(["SDT-G69"], result.Entries.Select(e => e.ExecutionUnit));
        Assert.Empty(result.Excluded);
    }

    [Fact]
    public void UnitWithoutIssue_KeepsTodaysClassification_AndIsNotExcluded()
    {
        using var host = new FixtureHost();
        host.WriteArtifact("G900", createdIssueNumber: null, createdIssueUrl: null, "issue-created");
        host.InstallTargetIssue();

        var result = host.Run(RepoX);

        Assert.Equal(["G900"], result.Entries.Select(e => e.ExecutionUnit));
        Assert.Empty(result.Excluded);
    }

    [Fact]
    public void WritePath_RefusesAFileBoundToAnotherRepository_WithoutChangingIt()
    {
        using var host = new FixtureHost();
        host.WriteArtifact("SWR-G010", 133, $"https://github.com/{RepoY}/issues/133", "issue-created");
        var path = host.ArtifactPath("SWR-G010");
        var before = File.ReadAllBytes(path);
        var entry = new PublishLifecycleEntry
        {
            ExecutionUnit = "SWR-G010",
            ArtifactPath = path,
            CurrentLifecycleState = "issue-created",
            RecommendedLifecycleState = "published",
            Classification = PublishLifecycleAnalyzer.ClassificationStaleIssueCreated,
            Evidence = Array.Empty<string>(),
            RecommendedLinkedPrNumber = null,
            RecommendedLinkedPrUrl = null,
            RecommendedClosedOutAt = null,
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            AutomationPublishLifecycleRepairCommand.ApplyRepair(entry, RepoX));

        Assert.Contains(RepoX, error.Message, StringComparison.Ordinal);
        Assert.Contains(RepoY, error.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static void ResetSeams()
    {
        AutomationPublishLifecycleRepairCommand.CandidateListerFactory = null;
        AutomationPublishLifecycleRepairCommand.IssueLookupFactory = null;
        AutomationPublishLifecycleRepairCommand.UtcNowFactory = null;
    }

    private sealed class FixtureHost : IDisposable
    {
        private readonly string root;
        private readonly CliContext context;

        public FixtureHost()
        {
            root = Directory.CreateTempSubdirectory("g823-host-").FullName;
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "issues"));
            context = new CliContext
            {
                RepoRoot = root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" }
                }
            };
        }

        public string ArtifactPath(string unit) =>
            Path.Combine(root, IssuePublishArtifactPathResolver.Resolve(unit));

        public void WriteArtifact(string unit, int? createdIssueNumber, string? createdIssueUrl, string lifecycleState)
        {
            var path = ArtifactPath(unit);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
            {
                ExecutionUnit = unit,
                PublishStatus = "issue-created",
                PacketPath = $".intent-cli/issues/{unit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{unit}/github-body.md",
                CreatedIssueNumber = createdIssueNumber,
                CreatedIssueUrl = createdIssueUrl,
                PublishedLabelName = "intent-target",
                LifecycleState = lifecycleState
            }));
        }

        /// <summary>
        /// The caller repository's issue list is empty so every evidence read goes
        /// through the recording lookup; each listed number carries intent-target,
        /// which makes an issue-created unit judged on it stale-issue-created.
        /// </summary>
        public RecordingLookup InstallTargetIssue(params int[] numbers)
        {
            var lookup = new RecordingLookup(numbers);
            AutomationPublishLifecycleRepairCommand.CandidateListerFactory = () => new EmptyLister();
            AutomationPublishLifecycleRepairCommand.IssueLookupFactory = () => lookup;
            return lookup;
        }

        public PublishLifecycleRepairResult Run(string repo, params string[] extra)
        {
            using var writer = new StringWriter();
            var args = new List<string> { "--repo", repo };
            args.AddRange(extra);
            args.AddRange(["--format", "json"]);
            var exit = AutomationPublishLifecycleRepairCommand.Execute(context, args.ToArray(), writer);
            Assert.True(exit == 0, writer.ToString());
            return JsonSerializer.Deserialize<PublishLifecycleRepairResult>(writer.ToString(), JsonOptions)!;
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class EmptyLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class RecordingLookup : IGitHubAutomationIssueLookup
    {
        private readonly HashSet<int> numbers;

        public RecordingLookup(IEnumerable<int> numbers) => this.numbers = numbers.ToHashSet();

        public List<(string Repo, int Number)> Calls { get; } = [];

        public GitHubAutomationIssueCandidate GetIssue(string repo, int issueNumber)
        {
            Calls.Add((repo, issueNumber));
            if (!numbers.Contains(issueNumber))
            {
                throw new InvalidOperationException($"no fixture issue #{issueNumber}");
            }

            return new GitHubAutomationIssueCandidate
            {
                Number = issueNumber,
                State = "OPEN",
                Labels = new[] { new GitHubAutomationLabel { Name = "intent-target" } }
            };
        }
    }
}
