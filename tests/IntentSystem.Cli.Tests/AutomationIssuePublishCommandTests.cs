using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class AutomationIssuePublishCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 2, 1, 54, 0, TimeSpan.Zero);

    public AutomationIssuePublishCommandTests()
    {
        AutomationIssuePublishCommand.MutatorFactory = null;
        AutomationIssuePublishCommand.UtcNowFactory = () => FixedNow;
        AutomationIssuePublishCommand.NestedProviderLauncher = null;
        // G462: default the host-only gate's issue-body lookup to a child-path
        // fake so existing publish tests neither hit live `gh` nor get refused.
        AutomationIssuePublishCommand.IssueLookupFactory =
            () => new FakeIssueLookup("Target paths: `src/Foo`, `docs`, `README.md`");
    }

    public void Dispose()
    {
        AutomationIssuePublishCommand.MutatorFactory = null;
        AutomationIssuePublishCommand.UtcNowFactory = null;
        AutomationIssuePublishCommand.NestedProviderLauncher = null;
        AutomationIssuePublishCommand.IssueLookupFactory = null;
    }

    [Fact]
    public void Execute_HostOnlyPacket_RefusesPublish_NonZero()
    {
        // G462 / G458 regression: a packet whose target paths are all
        // host-owned must NOT be published as a child intent-target issue.
        using var workspace = new AutomationIssuePublishWorkspace();
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;
        AutomationIssuePublishCommand.IssueLookupFactory = () => new FakeIssueLookup(
            "Target paths: `intents/intent-cli/intent-tree/purpose/04-product-goal.md`, `.intent-cli/issues/G458`");

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1018", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssuePublishResult>(writer.ToString())!;
        Assert.True(result.Refused);
        Assert.False(result.Applied);
        Assert.NotNull(result.RefusalReason);
        Assert.Contains("host-only packet", result.RefusalReason!, StringComparison.Ordinal);
        // The label was NEVER applied.
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_HostOnlyPacket_WithOverride_StillPublishes()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;
        AutomationIssuePublishCommand.IssueLookupFactory = () => new FakeIssueLookup(
            "Target paths: `intents/intent-cli/x.md`");

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "1018", "--write",
             "--allow-host-only-override", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssuePublishResult>(writer.ToString())!;
        Assert.False(result.Refused);
        Assert.True(result.Applied);
        Assert.Single(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_DryRunReportsPlannedIssuePublishLabelAction()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--issue", "557", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssuePublishResult>(writer.ToString())!;
        Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        Assert.Equal(557, result.Issue);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/557", result.IssueUrl);
        Assert.Equal("dry-run", result.Mode);
        Assert.False(result.Applied);
        Assert.Equal("intent-target", result.AppliedLabel);
        Assert.Contains("intent-target", result.AddLabels);
        Assert.Empty(result.RemoveLabels);
        Assert.Equal(FixedNow, result.PublishedAt);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_WriteAppliesIntentTargetToIssue()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "557",
                "--write",
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssuePublishResult>(writer.ToString())!;
        Assert.True(result.Applied);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("issue", transition.Kind);
        Assert.Equal(557, transition.Number);
        Assert.Contains("intent-target", transition.AddLabels);
        Assert.Empty(transition.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", transition.AddLabels);
    }

    [Fact]
    public void Execute_G783_ModeAwareSummaryMatchesJsonAndTextOutput()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;

        var dryRunJson = ExecutePublish(workspace.Context, "json");
        var writeJson = ExecutePublish(workspace.Context, "json", "--write");
        var dryRunText = ExecutePublish(workspace.Context, "text");
        var writeText = ExecutePublish(workspace.Context, "text", "--write");

        var dryRunResult = JsonSerializer.Deserialize<AutomationIssuePublishResult>(dryRunJson.Output)!;
        var writeResult = JsonSerializer.Deserialize<AutomationIssuePublishResult>(writeJson.Output)!;
        const string dryRunSummary = "Would publish issue #783 by adding intent-target.";
        const string writeSummary = "Published issue #783 by adding intent-target.";

        Assert.Equal(0, dryRunJson.ExitCode);
        Assert.Equal(0, writeJson.ExitCode);
        Assert.Equal(0, dryRunText.ExitCode);
        Assert.Equal(0, writeText.ExitCode);
        Assert.Equal(dryRunSummary, dryRunResult.Summary);
        Assert.Equal(writeSummary, writeResult.Summary);
        Assert.DoesNotContain("Would", writeResult.Summary, StringComparison.Ordinal);
        Assert.NotEqual(dryRunResult.Summary, writeResult.Summary);
        Assert.StartsWith(dryRunSummary, dryRunText.Output, StringComparison.Ordinal);
        Assert.StartsWith(writeSummary, writeText.Output, StringComparison.Ordinal);

        Console.WriteLine($"G783 dry-run JSON:{Environment.NewLine}{dryRunJson.Output}");
        Console.WriteLine($"G783 write JSON:{Environment.NewLine}{writeJson.Output}");
        Console.WriteLine($"G783 dry-run text:{Environment.NewLine}{dryRunText.Output}");
        Console.WriteLine($"G783 write text:{Environment.NewLine}{writeText.Output}");
    }

    [Fact]
    public void Execute_TextOutputIncludesPublishMetadata()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "557"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("issue: 557", output, StringComparison.Ordinal);
        Assert.Contains("issue_url: https://github.com/J-Tech-Japan/intent-system/issues/557", output, StringComparison.Ordinal);
        Assert.Contains("applied_label: intent-target", output, StringComparison.Ordinal);
        Assert.Contains("published_at: 2026-05-02T01:54:00.0000000+00:00", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsPrInput()
    {
        using var workspace = new AutomationIssuePublishWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "557", "--pr", "12"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr is not supported", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingIssueReturnsNonZero()
    {
        using var workspace = new AutomationIssuePublishWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--issue or --execution-unit is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ExecutionUnitResolvesRecordedIssueNumber_G625()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        workspace.WritePublishArtifact("G625", 557);
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--execution-unit", "G625", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssuePublishResult>(writer.ToString())!;
        Assert.Equal(557, result.Issue);
        Assert.Equal(557, Assert.Single(mutator.AppliedTransitions).Number);
    }

    [Fact]
    public void Execute_ExecutionUnitAndIssueDisagreementFailsClosed_G625()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        workspace.WritePublishArtifact("G625", 557);
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--execution-unit", "G625", "--issue", "558"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--issue 558", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("--execution-unit 'G625'", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("created_issue_number 557", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_ExecutionUnitWithoutRecordedIssueRefusesNamingMissingField_G625()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        workspace.WritePublishArtifact("G625", null);

        using var writer = new StringWriter();
        var exitCode = AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--execution-unit", "G625"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("G625", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("created_issue_number", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationIssuePublish()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "issue-publish", "--repo", "J-Tech-Japan/intent-system", "--issue", "557", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssuePublishResult>(writer.ToString())!;
        Assert.Equal(557, result.Issue);
    }

    [Fact]
    public void ProgramMain_AutomationIssuePublishDoesNotRequireIntentCliDirectory()
    {
        using var workspace = new AutomationIssuePublishWorkspace(createIntentCliDirectory: false);
        workspace.WriteOriginRemote("https://github.com/J-Tech-Japan/intent-system.git");
        var mutator = new FakeMutator();
        AutomationIssuePublishCommand.MutatorFactory = () => mutator;
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Directory.SetCurrentDirectory(workspace.RootPath);
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = Program.Main(["automation", "issue-publish", "--issue", "557", "--format", "json"]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());
            var result = JsonSerializer.Deserialize<AutomationIssuePublishResult>(stdout.ToString())!;
            Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
            Assert.Equal(557, result.Issue);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new AutomationIssuePublishWorkspace();
        var launcherInvoked = false;
        AutomationIssuePublishCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };
        AutomationIssuePublishCommand.MutatorFactory = () => new FakeMutator();

        using var writer = new StringWriter();
        Assert.Equal(0, AutomationIssuePublishCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--issue", "557", "--format", "json"],
            writer));

        Assert.False(launcherInvoked,
            "AutomationIssuePublishCommand must never invoke NestedProviderLauncher.");
    }

    private sealed class FakeIssueLookup : IGitHubIssueLookup
    {
        private readonly string body;
        public FakeIssueLookup(string body) => this.body = body;
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) =>
            new() { Number = issueNumber, State = "OPEN", Body = body };
    }

    private sealed class FakeMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            if (!string.Equals(kind, "issue", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("issue publish must only target issues.");
            }

            AppliedTransitions.Add(new AppliedTransition(
                repo,
                kind,
                number,
                addLabels.ToArray(),
                removeLabels.ToArray()));
        }

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException("reconcile path not exercised by these tests");
    }

    private static (int ExitCode, string Output) ExecutePublish(
        CliContext context,
        string format,
        params string[] modeArguments)
    {
        var arguments = new List<string>
        {
            "--repo", "J-Tech-Japan/intent-system",
            "--issue", "783"
        };
        arguments.AddRange(modeArguments);
        arguments.Add("--format");
        arguments.Add(format);
        using var writer = new StringWriter();

        var exitCode = AutomationIssuePublishCommand.Execute(context, [.. arguments], writer);
        return (exitCode, writer.ToString());
    }

    private sealed record AppliedTransition(
        string Repo,
        string Kind,
        int Number,
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);

    private sealed class AutomationIssuePublishWorkspace : IDisposable
    {
        public AutomationIssuePublishWorkspace(bool createIntentCliDirectory = true)
        {
            RootPath = Directory.CreateTempSubdirectory("automation-issue-publish-tests-").FullName;
            if (createIntentCliDirectory)
            {
                Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            }

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

        public void WriteOriginRemote(string remoteUrl)
        {
            var gitDirectory = Path.Combine(RootPath, ".git");
            Directory.CreateDirectory(gitDirectory);
            File.WriteAllText(
                Path.Combine(gitDirectory, "config"),
                $"""
                [remote "origin"]
                    url = {remoteUrl}
                """);
        }

        public void WritePublishArtifact(string executionUnit, int? createdIssueNumber)
        {
            var directory = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "publish.yaml"), IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
            {
                ExecutionUnit = executionUnit,
                PublishStatus = "issue-created",
                PacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{executionUnit}/github-body.md",
                CreatedIssueNumber = createdIssueNumber,
                CreatedIssueUrl = createdIssueNumber is null ? null : $"https://github.com/J-Tech-Japan/intent-system/issues/{createdIssueNumber}",
                PublishedLabelName = null,
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
