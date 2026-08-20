using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class G717WorkerPathTests : IDisposable
{
    public G717WorkerPathTests()
    {
        WorkerNextActionCommand.CandidateListerFactory = null;
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
        WorkerIssuePreflightCommand.IssueLookupFactory = null;
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new NoExistingIssueChecker();
    }

    public void Dispose()
    {
        WorkerNextActionCommand.CandidateListerFactory = null;
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
        WorkerIssuePreflightCommand.IssueLookupFactory = null;
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
    }

    [Fact]
    public void DraftingClaimPublishReleaseStaleLabelNextActionClaimPreflight_G717()
    {
        using var repository = new LocalClaimRepository();
        SeedPublishPacket(repository.Worker);
        var context = Context(repository.Worker);

        var acquired = ClaimCommand.RunTransaction(
            repository.Worker,
            new ClaimRequest(
                ClaimOperation.Acquire,
                "execution-unit:G717",
                "designer",
                "design",
                null,
                null,
                true,
                "json",
                ClaimCommand.DefaultMaxAttempts));
        Assert.Equal("acquired", acquired.Status);

        IssuePublishFlowCommand.CreatorFactory = () =>
            new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/717");
        using (var publishWriter = new StringWriter())
        {
            var publishExit = IssuePublishFlowCommand.Execute(
                context,
                new[]
                {
                    "G717", "--repo", "J-Tech-Japan/intent-system",
                    "--team", "design", "--write", "--format", "json",
                },
                publishWriter);
            Assert.Equal(0, publishExit);
            using var publish = JsonDocument.Parse(publishWriter.ToString());
            Assert.True(publish.RootElement.GetProperty("created").GetBoolean());
            Assert.True(publish.RootElement.GetProperty("durable_state_synced").GetBoolean());
        }

        var released = ClaimCommand.RunTransaction(
            repository.Worker,
            new ClaimRequest(
                ClaimOperation.Release,
                "execution-unit:G717",
                "designer",
                "design",
                null,
                "attributed drafter handed off after publish",
                true,
                "json",
                ClaimCommand.DefaultMaxAttempts));
        Assert.Equal("released", released.Status);

        var staleIssue = new GitHubAutomationIssueCandidate
        {
            Number = 717,
            Title = "G717 claim precedence",
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/717",
            CreatedAt = "2026-08-19T00:00:00Z",
            Labels = new[]
            {
                new GitHubAutomationLabel { Name = "intent-target" },
                new GitHubAutomationLabel { Name = "intent-issue-in-progress" },
            },
        };
        WorkerNextActionCommand.CandidateListerFactory = () => new SingleIssueLister(staleIssue);

        using (var nextWriter = new StringWriter())
        {
            var nextExit = WorkerNextActionCommand.Execute(
                context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--team", "implementation", "--github-only", "--format", "json",
                },
                nextWriter);
            Assert.Equal(0, nextExit);
            var next = JsonSerializer.Deserialize<WorkerNextActionResult>(nextWriter.ToString())!;
            Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, next.Action);
            Assert.Equal(717, next.Number);
            Assert.Contains("stale shadow state", next.Reason, StringComparison.Ordinal);
        }

        var mutator = new RecordingMutator(staleIssue.Labels.Select(label => label.Name).ToArray());
        WorkerClaimCommand.MutatorFactory = () => mutator;
        WorkerClaimCommand.IssueLookupFactory = () => new IssueLookup("G717 claim precedence", ValidIssueBody());
        using (var claimWriter = new StringWriter())
        {
            var claimExit = WorkerClaimCommand.Execute(
                context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--kind", "issue", "--number", "717", "--write", "--format", "json",
                },
                claimWriter);
            Assert.Equal(0, claimExit);
            var claim = JsonSerializer.Deserialize<WorkerClaimResult>(claimWriter.ToString())!;
            Assert.True(claim.Proceed);
            Assert.False(claim.Applied);
            Assert.Empty(mutator.Transitions);
        }

        WorkerIssuePreflightCommand.IssueLookupFactory = () => new IssueLookup(
            "G717 claim precedence",
            ValidIssueBody(),
            new[] { "intent-target", "intent-issue-in-progress" });
        using (var preflightWriter = new StringWriter())
        {
            var preflightExit = WorkerIssuePreflightCommand.Execute(
                context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--issue", "717", "--format", "json",
                },
                preflightWriter);
            Assert.Equal(0, preflightExit);
            var preflight = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(preflightWriter.ToString())!;
            Assert.True(preflight.Actionable);
            Assert.Equal(WorkerIssuePreflightConstants.Classifications.ReadyToImplement, preflight.Classification);
            Assert.Equal(ClaimOwnershipVerification.StatusUnheld, preflight.ClaimStatus);
            Assert.Contains(preflight.Reasons, reason => reason.Contains("disagrees", StringComparison.Ordinal));
        }

        // A later relabel is still stale shadow state. The selector reports it
        // again deterministically; it does not retry, back off, or mutate the
        // label to win a tug-of-war.
        using var laterRelabelWriter = new StringWriter();
        var laterRelabelExit = WorkerNextActionCommand.Execute(
            context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--team", "implementation", "--github-only", "--format", "json",
            },
            laterRelabelWriter);
        Assert.Equal(0, laterRelabelExit);
        var laterRelabel = JsonSerializer.Deserialize<WorkerNextActionResult>(laterRelabelWriter.ToString())!;
        Assert.Equal(WorkerNextActionConstants.Actions.IssueToPr, laterRelabel.Action);
        Assert.Contains("stale shadow state", laterRelabel.Reason, StringComparison.Ordinal);
    }

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

    private static void SeedPublishPacket(string root)
    {
        var packet = Path.Combine(root, ".intent-cli", "issues", "G717");
        Directory.CreateDirectory(packet);
        File.WriteAllText(Path.Combine(packet, "github-body.md"), ValidIssueBody());
        File.WriteAllText(Path.Combine(packet, "packet.yaml"), "execution_unit: G717\ntitle: G717 claim precedence\n");
        File.WriteAllText(Path.Combine(packet, "implementation.md"), "# implementation\n");
        File.WriteAllText(Path.Combine(packet, "review-context.md"), "# review\n");
        File.WriteAllText(
            Path.Combine(root, ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
                Items = new[]
                {
                    new QueueItem
                    {
                        ExecutionUnit = "G717",
                        Title = "G717 claim precedence",
                        State = QueueItemState.Queued,
                        Dependencies = Array.Empty<string>(),
                        BlockedBy = Array.Empty<string>(),
                        ClarificationReturnPath = string.Empty,
                        WorkerRole = "implementation",
                        ReviewRole = "review",
                        Priority = "normal",
                        PacketPaths = new PacketPaths
                        {
                            Implementation = ".intent-cli/issues/G717/implementation.md",
                            ReviewContext = ".intent-cli/issues/G717/review-context.md",
                            Yaml = ".intent-cli/issues/G717/packet.yaml",
                        },
                    },
                },
            }));
    }

    private static string ValidIssueBody() => """
        # G717 claim precedence

        ## Goal
        Keep claim and lifecycle state convergent.

        ## Why This Slice Exists Now
        A stale label must not deadlock the worker.

        ## Current Observed State
        The claim is released while the lifecycle label remains.

        ## Accepted Baseline You May Assume
        G679 claim transactions remain authoritative.

        ## Target Repo / Path / Part
        Repository: J-Tech-Japan/intent-system
        Target paths: `src/IntentSystem.Cli/Commands`, `tests/IntentSystem.Cli.Tests`

        ## In Scope
        Worker claim precedence.

        ## Out Of Scope
        Release and publish operations.

        ## Acceptance Criteria
        The stale label is reported and does not block.

        ## Verification
        Run the worker regression suite.

        ## Related Links
        - https://github.com/J-Tech-Japan/intent-system/issues/1556

        ## Base Branch Policy
        Policy: `direct-main`
        Expected PR base branch: `main`
        Open all child PRs against `main` directly.
        """;

    private sealed class SingleIssueLister : IGitHubAutomationCandidateLister
    {
        private readonly GitHubAutomationIssueCandidate issue;

        public SingleIssueLister(GitHubAutomationIssueCandidate issue)
        {
            this.issue = issue;
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => new[] { issue };
    }

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        public RecordingMutator(IReadOnlyList<string> labels)
        {
            Labels = labels;
        }

        public IReadOnlyList<string> Labels { get; }

        public List<(IReadOnlyList<string> Add, IReadOnlyList<string> Remove)> Transitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

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
        private readonly string body;
        private readonly IReadOnlyList<string> labels;

        public IssueLookup(string title, string body, IReadOnlyList<string>? labels = null)
        {
            this.title = title;
            this.body = body;
            this.labels = labels ?? new[] { "intent-target" };
        }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => new()
        {
            Number = issueNumber,
            State = "OPEN",
            Title = title,
            Body = body,
            Labels = labels.Select(name => new GitHubIssueLabel { Name = name }).ToArray(),
        };
    }

    private sealed class StubIssueCreator : IIssueCreator
    {
        private readonly string url;

        public StubIssueCreator(string url)
        {
            this.url = url;
        }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath) =>
            new(url);
    }

    private sealed class NoExistingIssueChecker : IGitHubExistingIssueChecker
    {
        public GitHubExistingIssueLookupResult FindExistingIssue(
            string repo,
            string executionUnit,
            string expectedTitle,
            string expectedBody) =>
            new() { Classification = GitHubExistingIssueClassification.None };
    }

    private sealed class LocalClaimRepository : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("g717-worker-path-").FullName;

        public LocalClaimRepository()
        {
            var bare = Path.Combine(root, "origin.git");
            var seed = Path.Combine(root, "seed");
            Worker = Path.Combine(root, "worker");
            RunWithDirectory(bare, "git", new[] { "init", "--bare", "--quiet" });
            RunWithDirectory(seed, "git", new[] { "init", "--quiet", "--initial-branch=main" });
            Run(seed, "git", "config", "user.name", "seed");
            Run(seed, "git", "config", "user.email", "seed@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "seed\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", bare);
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");
            Run(bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(root, "git", "clone", "--quiet", bare, Worker);
        }

        public string Worker { get; }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static void Run(
            string workingDirectory,
            string fileName,
            params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0,
                $"{fileName} {string.Join(' ', arguments)} failed: {output}\n{error}");
        }

        private static void RunWithDirectory(
            string workingDirectory,
            string fileName,
            IReadOnlyList<string> arguments)
        {
            Directory.CreateDirectory(workingDirectory);
            Run(workingDirectory, fileName, arguments.ToArray());
        }
    }
}
