using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationPublishLifecycleRepairCommandTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public AutomationPublishLifecycleRepairCommandTests()
    {
        AutomationPublishLifecycleRepairCommand.CandidateListerFactory = null;
        AutomationPublishLifecycleRepairCommand.IssueLookupFactory = null;
        AutomationPublishLifecycleRepairCommand.UtcNowFactory = null;
    }

    public void Dispose()
    {
        AutomationPublishLifecycleRepairCommand.CandidateListerFactory = null;
        AutomationPublishLifecycleRepairCommand.IssueLookupFactory = null;
        AutomationPublishLifecycleRepairCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_IssueScopeRepairsOnlySelectedUnit_G717()
    {
        using var workspace = new Workspace();
        workspace.WriteArtifact("G717", 717, "issue-created");
        workspace.WriteArtifact("G718", 718, "issue-created");
        var untouchedBefore = File.ReadAllText(workspace.ArtifactPath("G718"));

        AutomationPublishLifecycleRepairCommand.CandidateListerFactory = () =>
            new FakeLister(
                new GitHubAutomationIssueCandidate
                {
                    Number = 717,
                    State = "OPEN",
                    Labels = new[] { new GitHubAutomationLabel { Name = "intent-target" } }
                },
                new GitHubAutomationIssueCandidate
                {
                    Number = 718,
                    State = "OPEN",
                    Labels = new[] { new GitHubAutomationLabel { Name = "intent-target" } }
                });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishLifecycleRepairCommand.Execute(
            workspace.Context,
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "717",
                "--write",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<PublishLifecycleRepairResult>(writer.ToString(), JsonOptions)!;
        Assert.Equal("issue:717", result.Scope);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(new[] { "G717" }, result.AppliedUnits);
        Assert.Single(result.Entries);
        Assert.Equal("G717", result.Entries[0].ExecutionUnit);
        Assert.Equal(
            "published",
            IssuePublishArtifactYaml.Deserialize(File.ReadAllText(workspace.ArtifactPath("G717"))).LifecycleState);
        Assert.Equal(untouchedBefore, File.ReadAllText(workspace.ArtifactPath("G718")));
    }

    [Fact]
    public void Execute_ExecutionUnitScopeRepairsOnlyNamedArtifact_G717()
    {
        using var workspace = new Workspace();
        workspace.WriteArtifact("G717", 717, "issue-created");
        workspace.WriteArtifact("G718", 718, "issue-created");

        AutomationPublishLifecycleRepairCommand.CandidateListerFactory = () =>
            new FakeLister(
                new GitHubAutomationIssueCandidate
                {
                    Number = 717,
                    State = "OPEN",
                    Labels = new[] { new GitHubAutomationLabel { Name = "intent-target" } }
                },
                new GitHubAutomationIssueCandidate
                {
                    Number = 718,
                    State = "OPEN",
                    Labels = new[] { new GitHubAutomationLabel { Name = "intent-target" } }
                });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishLifecycleRepairCommand.Execute(
            workspace.Context,
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--execution-unit", "G717",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<PublishLifecycleRepairResult>(writer.ToString(), JsonOptions)!;
        Assert.Equal("execution-unit:G717", result.Scope);
        Assert.Single(result.Entries);
        Assert.Equal("G717", result.Entries[0].ExecutionUnit);
        Assert.Equal("published", result.Entries[0].RecommendedLifecycleState);
        Assert.Equal(
            "issue-created",
            IssuePublishArtifactYaml.Deserialize(File.ReadAllText(workspace.ArtifactPath("G718"))).LifecycleState);
    }

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;

        public FakeLister(params GitHubAutomationIssueCandidate[] issues) => this.issues = issues;

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => issues;
    }

    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("publish-lifecycle-repair-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli", "issues"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public string ArtifactPath(string executionUnit) =>
            Path.Combine(RootPath, IssuePublishArtifactPathResolver.Resolve(executionUnit));

        public void WriteArtifact(string executionUnit, int issueNumber, string lifecycleState)
        {
            var path = ArtifactPath(executionUnit);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
            {
                ExecutionUnit = executionUnit,
                PublishStatus = "issue-created",
                PacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{executionUnit}/github-body.md",
                CreatedIssueNumber = issueNumber,
                CreatedIssueUrl = $"https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}",
                PublishedLabelName = "intent-target",
                LifecycleState = lifecycleState
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
