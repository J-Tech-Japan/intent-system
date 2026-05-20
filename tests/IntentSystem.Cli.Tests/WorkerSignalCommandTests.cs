using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G374: tests for <c>intent-cli worker signal &lt;kind&gt;</c> — the
/// dry-run / write split (no comment posted and no label applied unless
/// --write), kind/target routing, body-file validation, and the
/// no-Process.Start / no-nested-provider invariants.
/// </summary>
public sealed class WorkerSignalCommandTests : IDisposable
{
    public WorkerSignalCommandTests()
    {
        Reset();
    }

    public void Dispose()
    {
        Reset();
    }

    private static void Reset()
    {
        WorkerSignalCommand.GatewayFactory = null;
        WorkerSignalCommand.MutatorFactory = null;
        WorkerSignalCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_DryRunBlockerOnIssue_PlansSentLabel_PostsNothing()
    {
        using var workspace = new SignalWorkspace();
        var bodyPath = workspace.WriteBody("cannot build: missing contract");
        var gateway = new FakeSignalGateway();
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerSignalCommand.GatewayFactory = () => gateway;
        WorkerSignalCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "blocker", "--repo", "J-Tech-Japan/intent-system", "--issue", "851", "--from-file", bodyPath, "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<WorkerSignalResult>(writer.ToString())!;
        Assert.Equal("blocker", result.SignalKind);
        Assert.Equal("issue", result.Target);
        Assert.Equal(851, result.Number);
        Assert.False(result.Posted);
        Assert.False(result.Applied);
        Assert.Contains("intent-signal-sent", result.AddLabels);
        Assert.Empty(gateway.Posted);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_WriteBlockerOnIssue_PostsMarkerComment_AddsSentLabel()
    {
        using var workspace = new SignalWorkspace();
        var bodyPath = workspace.WriteBody("cannot build: missing contract");
        var gateway = new FakeSignalGateway();
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerSignalCommand.GatewayFactory = () => gateway;
        WorkerSignalCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "blocker", "--repo", "J-Tech-Japan/intent-system", "--issue", "851", "--from-file", bodyPath, "--write", "--github-only", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<WorkerSignalResult>(writer.ToString())!;
        Assert.True(result.Posted);
        Assert.True(result.Applied);
        Assert.True(result.GithubOnly);
        Assert.Equal(gateway.Posted[0].CommentRef, result.CommentRef);

        var posted = Assert.Single(gateway.Posted);
        Assert.Equal("issue", posted.Kind);
        Assert.Equal(851, posted.Number);
        Assert.StartsWith(WorkerSignalContract.MarkerPrefix, posted.Body.Split('\n')[0]);
        Assert.Contains("cannot build: missing contract", posted.Body, StringComparison.Ordinal);

        var applied = Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("issue", applied.Kind);
        Assert.Contains("intent-signal-sent", applied.AddLabels);
    }

    [Fact]
    public void Execute_WriteFollowUpOnPr_Posts_AndLabels()
    {
        using var workspace = new SignalWorkspace();
        var bodyPath = workspace.WriteBody("found a follow-up defect in adjacent module");
        var gateway = new FakeSignalGateway();
        var mutator = new FakeMutator { Labels = new[] { "intent-pr-reviewing" } };
        WorkerSignalCommand.GatewayFactory = () => gateway;
        WorkerSignalCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "follow-up", "--repo", "J-Tech-Japan/intent-system", "--pr", "860", "--from-file", bodyPath, "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var posted = Assert.Single(gateway.Posted);
        Assert.Equal("pr", posted.Kind);
        Assert.Equal(860, posted.Number);
    }

    [Fact]
    public void Execute_AlreadySent_WarnsAndPostsButPlansNoDuplicateLabel()
    {
        using var workspace = new SignalWorkspace();
        var bodyPath = workspace.WriteBody("another blocker observation");
        var gateway = new FakeSignalGateway();
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-signal-sent" } };
        WorkerSignalCommand.GatewayFactory = () => gateway;
        WorkerSignalCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "blocker", "--repo", "J-Tech-Japan/intent-system", "--issue", "851", "--from-file", bodyPath, "--write", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<WorkerSignalResult>(writer.ToString())!;
        Assert.True(result.Posted);
        Assert.False(result.Applied);
        Assert.Empty(result.AddLabels);
        Assert.NotEmpty(result.Warnings);
        Assert.Single(gateway.Posted);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_BlockerOnPr_RejectsDisallowedTarget()
    {
        using var workspace = new SignalWorkspace();
        var bodyPath = workspace.WriteBody("x");
        WorkerSignalCommand.MutatorFactory = () => new FakeMutator();

        using var writer = new StringWriter();
        var exit = WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "blocker", "--repo", "J-Tech-Japan/intent-system", "--pr", "860", "--from-file", bodyPath },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("cannot target a pr", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownKind_ReturnsError()
    {
        using var workspace = new SignalWorkspace();
        using var writer = new StringWriter();
        var exit = WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "bogus", "--repo", "J-Tech-Japan/intent-system", "--issue", "1", "--from-file", "x" },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("Unknown signal kind", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingFromFile_ReturnsError()
    {
        using var workspace = new SignalWorkspace();
        WorkerSignalCommand.MutatorFactory = () => new FakeMutator();
        using var writer = new StringWriter();
        var exit = WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "blocker", "--repo", "J-Tech-Japan/intent-system", "--issue", "1" },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--from-file", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_EmptyBodyFile_ReturnsError()
    {
        using var workspace = new SignalWorkspace();
        var bodyPath = workspace.WriteBody("   ");
        WorkerSignalCommand.MutatorFactory = () => new FakeMutator();
        using var writer = new StringWriter();
        var exit = WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "blocker", "--repo", "J-Tech-Japan/intent-system", "--issue", "1", "--from-file", bodyPath },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("must not be empty", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new SignalWorkspace();
        var bodyPath = workspace.WriteBody("body");
        var invoked = false;
        WorkerSignalCommand.NestedProviderLauncher = () => { invoked = true; return true; };
        WorkerSignalCommand.GatewayFactory = () => new FakeSignalGateway();
        WorkerSignalCommand.MutatorFactory = () => new FakeMutator { Labels = new[] { "intent-target" } };

        using var writer = new StringWriter();
        Assert.Equal(0, WorkerSignalCommand.Execute(
            workspace.Context,
            new[] { "blocker", "--repo", "J-Tech-Japan/intent-system", "--issue", "851", "--from-file", bodyPath, "--write", "--format", "json" },
            writer));

        Assert.False(invoked, "WorkerSignalCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void SourceScan_CommandContractAndAnalyzer_ContainNoProcessStart()
    {
        var command = File.ReadAllText(LocateSourceFile("WorkerSignalCommand.cs"));
        var contract = File.ReadAllText(LocateSourceFile("WorkerSignalContract.cs"));
        var analyzer = File.ReadAllText(LocateSourceFile("ReviewCollectSignalsAnalyzer.cs"));
        var combined = command + "\n" + contract + "\n" + analyzer;

        Assert.DoesNotContain("Process.Start(", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue comment", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr comment", combined, StringComparison.Ordinal);
    }

    private static string LocateSourceFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "IntentSystem.Cli", "Commands", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate source file {fileName}");
    }

    internal sealed record PostedComment
    {
        public required string Repo { get; init; }
        public required string Kind { get; init; }
        public required int Number { get; init; }
        public required string Body { get; init; }
        public required string CommentRef { get; init; }
    }

    internal sealed class FakeSignalGateway : IGitHubSignalGateway
    {
        public List<PostedComment> Posted { get; } = new();
        public IReadOnlyList<GitHubSignalComment> CommentsToReturn { get; init; } = Array.Empty<GitHubSignalComment>();

        public string PostComment(string repo, string kind, int number, string body)
        {
            var commentRef = $"https://github.com/{repo}/{kind}/{number}#issuecomment-{Posted.Count + 1}";
            Posted.Add(new PostedComment { Repo = repo, Kind = kind, Number = number, Body = body, CommentRef = commentRef });
            return commentRef;
        }

        public IReadOnlyList<GitHubSignalComment> ListComments(string repo, string kind, int number) => CommentsToReturn;
    }

    internal sealed class FakeMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            AppliedTransitions.Add(new AppliedTransition
            {
                Repo = repo,
                Kind = kind,
                Number = number,
                AddLabels = addLabels.ToArray(),
                RemoveLabels = removeLabels.ToArray(),
            });
        }

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    internal sealed record AppliedTransition
    {
        public required string Repo { get; init; }
        public required string Kind { get; init; }
        public required int Number { get; init; }
        public required IReadOnlyList<string> AddLabels { get; init; }
        public required IReadOnlyList<string> RemoveLabels { get; init; }
    }

    private sealed class SignalWorkspace : IDisposable
    {
        public SignalWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-signal-tests-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public string WriteBody(string content)
        {
            var path = Path.Combine(RootPath, $"signal-{Guid.NewGuid():N}.md");
            File.WriteAllText(path, content);
            return path;
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
