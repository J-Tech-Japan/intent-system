using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationReconcileCommandTests : IDisposable
{
    public AutomationReconcileCommandTests()
    {
        AutomationReconcileCommand.CandidateListerFactory = null;
        AutomationReconcileCommand.MutatorFactory = null;
        AutomationReconcileCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationReconcileCommand.CandidateListerFactory = null;
        AutomationReconcileCommand.MutatorFactory = null;
        AutomationReconcileCommand.NestedProviderLauncher = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
    }

    [Fact]
    public void Execute_DryRun_DetectsMissingPrIntentTargetAndMissingIssueIntentPrCreated()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(420, "child impl", "https://github.com/J-Tech-Japan/intent-system/pull/420",
                    body: "Closes #559", labels: Array.Empty<string>()),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227 some unit", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    labels: ["intent-target"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Equal("host-review", result.Lane);
        Assert.Equal("dry-run", result.Mode);
        Assert.True(result.HostOnly);
        Assert.Empty(result.UnsafeStops);

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.MissingPrIntentTarget, StringComparison.Ordinal)
            && repair.TargetNumber == 420
            && repair.AddLabels.Contains("intent-target", StringComparer.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal)
            && !repair.Applied);

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.MissingIssueIntentPrCreated, StringComparison.Ordinal)
            && repair.TargetNumber == 559
            && repair.AddLabels.Contains("intent-pr-created", StringComparer.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal)
            && !repair.Applied);

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.MissingLinkedPrMetadata, StringComparison.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.Advisory, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(repair.RequiresFollowupCommand));
    }

    [Fact]
    public void Execute_DryRun_DetectsMisplacedIntentPrCreatedOnPr()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(421, "should not have intent-pr-created",
                    "https://github.com/J-Tech-Japan/intent-system/pull/421",
                    body: "Closes #560",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
            PublishedIssues =
            [
                BuildIssue(560, "G228", "https://github.com/J-Tech-Japan/intent-system/issues/560",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.MisplacedPrIntentPrCreated, StringComparison.Ordinal)
            && repair.TargetNumber == 421
            && repair.RemoveLabels.Contains("intent-pr-created", StringComparer.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_NoDriftReturnsCleanPlanWithSummaryAndZeroExit()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(500, "clean", "https://github.com/J-Tech-Japan/intent-system/pull/500",
                    body: "Closes #999",
                    labels: ["intent-target"]),
            ],
            PublishedIssues =
            [
                BuildIssue(999, "G300", "https://github.com/J-Tech-Japan/intent-system/issues/999",
                    labels: ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;

        Assert.DoesNotContain(result.SafeRepairs, repair =>
            string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal));
        Assert.Empty(result.UnsafeStops);
    }

    [Fact]
    public void Execute_DryRunNeverInvokesMutator()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(600, "needs intent-target", "https://github.com/J-Tech-Japan/intent-system/pull/600",
                    body: "Closes #559", labels: Array.Empty<string>()),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    labels: ["intent-target"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;
        var mutator = new RecordingMutator();
        AutomationReconcileCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Empty(mutator.Reconciles);
    }

    [Fact]
    public void Execute_WriteAppliesOnlyHighConfidenceRepairs()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(700, "missing target", "https://github.com/J-Tech-Japan/intent-system/pull/700",
                    body: "Closes #559", labels: Array.Empty<string>()),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    labels: ["intent-target"]),
            ],
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;
        var mutator = new RecordingMutator();
        AutomationReconcileCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, mutator.Reconciles.Count);
        Assert.Contains(mutator.Reconciles, t =>
            t.Kind == "pr" && t.Number == 700
            && t.AddLabels.Contains("intent-target", StringComparer.Ordinal));
        Assert.Contains(mutator.Reconciles, t =>
            t.Kind == "issue" && t.Number == 559
            && t.AddLabels.Contains("intent-pr-created", StringComparer.Ordinal));

        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Equal("write", result.Mode);
        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal)
            && repair.Applied);
        Assert.DoesNotContain(result.SafeRepairs, repair =>
            string.Equals(repair.Confidence, AutomationReconcileConfidence.Advisory, StringComparison.Ordinal)
            && repair.Applied);
    }

    [Fact]
    public void Execute_AmbiguousIssueLinkProducesUnsafeStop()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(800, "no closes keyword",
                    "https://github.com/J-Tech-Japan/intent-system/pull/800",
                    body: "free-form notes — no Closes keyword",
                    labels: ["intent-target"]),
            ],
            PublishedIssues = Array.Empty<GitHubAutomationIssueCandidate>(),
        };
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Contains(result.UnsafeStops, stop =>
            string.Equals(stop.Kind, AutomationReconcileUnsafeStopKinds.AmbiguousIssueLink, StringComparison.Ordinal)
            && stop.TargetNumber == 800);
    }

    [Fact]
    public void Execute_ChildLoopContextRefusesEarlyAndExitsTwo()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new ThrowingLister();
        AutomationReconcileCommand.CandidateListerFactory = () => lister;
        var mutator = new RecordingMutator();
        AutomationReconcileCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--child-loop-context", "--write", "--format", "json"],
            writer);

        Assert.Equal(2, exitCode);
        Assert.Empty(mutator.Reconciles);

        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Empty(result.SafeRepairs);
        Assert.Contains(result.UnsafeStops, stop =>
            string.Equals(stop.Kind, AutomationReconcileUnsafeStopKinds.ChildLoopProhibited, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_NextSliceLane_StaleCacheClassifiedAsAdvisory()
    {
        using var workspace = new ReconcileWorkspace();
        AutomationReconcileCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            [
                "--lane", "next-slice",
                "--repo", "J-Tech-Japan/intent-system",
                "--next-slice-clarification-required",
                "--clarifications-all-resolved",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Empty(result.UnsafeStops);
        Assert.Contains(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.StaleNextSliceCandidateCache, StringComparison.Ordinal)
            && string.Equals(repair.Confidence, AutomationReconcileConfidence.Advisory, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(repair.RequiresFollowupCommand));
    }

    [Fact]
    public void Execute_NextSliceLane_OpenClarificationProducesUnsafeStop()
    {
        using var workspace = new ReconcileWorkspace();
        AutomationReconcileCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            [
                "--lane", "next-slice",
                "--repo", "J-Tech-Japan/intent-system",
                "--next-slice-clarification-required",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.DoesNotContain(result.SafeRepairs, repair =>
            string.Equals(repair.Type, AutomationReconcileRepairTypes.StaleNextSliceCandidateCache, StringComparison.Ordinal));
        Assert.Contains(result.UnsafeStops, stop =>
            string.Equals(stop.Kind, "open-clarification-present", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_StaleHostCliReturnsStructuredStopAndDoesNotRunLister()
    {
        using var workspace = new ReconcileWorkspace();
        workspace.WriteInstalledCliScript(stalePrTransition: true);
        AutomationReconcileCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationReconcileCommand.Execute(
            workspace.Context,
            ["--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.Contains(result.UnsafeStops, stop =>
            string.Equals(stop.Kind, "stale-host-cli", StringComparison.Ordinal));
        Assert.Empty(result.SafeRepairs);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationReconcile()
    {
        using var workspace = new ReconcileWorkspace();
        var lister = new FakeLister();
        AutomationReconcileCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "reconcile", "--lane", "host-review", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationReconcileResult>(writer.ToString())!;
        Assert.True(result.HostOnly);
    }

    [Fact]
    public void CommandRouter_HelpListsAutomationReconcile()
    {
        using var workspace = new ReconcileWorkspace();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute([], workspace.Context, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation reconcile", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_ChildLoopDoesNotMentionReconcile()
    {
        using var workspace = new ReconcileWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "child-loop", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.DoesNotContain("automation reconcile", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_ChildOneshotDoesNotMentionReconcile()
    {
        using var workspace = new ReconcileWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "child-oneshot", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("automation reconcile", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_ApplyReconcileTransitions_RejectsAddingIntentPrCreatedToPr()
    {
        var mutator = new GhCliGitHubLabelMutator();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            mutator.ApplyReconcileTransitions(
                "J-Tech-Japan/intent-system", "pr", 421,
                new[] { "intent-pr-created" },
                Array.Empty<string>()));
        Assert.Contains("issue-only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_HostLoopMentionsReconcile()
    {
        using var workspace = new ReconcileWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "host-loop", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation reconcile", writer.ToString(), StringComparison.Ordinal);
    }

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        string url,
        string body,
        IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            Body = body,
            CreatedAt = "2026-05-06T00:00:00Z",
            UpdatedAt = "2026-05-06T00:00:00Z",
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number,
        string title,
        string url,
        IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = "2026-05-06T00:00:00Z",
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> AllPrs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> PublishedIssues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            AllPrs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            PublishedIssues;
    }

    private sealed class ThrowingLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("lister should not be invoked in this test");

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("lister should not be invoked in this test");
    }

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        public List<RecordedTransition> Transitions { get; } = new();
        public List<RecordedTransition> Reconciles { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Array.Empty<GitHubAutomationLabel>();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            Transitions.Add(new RecordedTransition(repo, kind, number, addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            Reconciles.Add(new RecordedTransition(repo, kind, number, addLabels.ToArray(), removeLabels.ToArray()));
    }

    private sealed record RecordedTransition(
        string Repo,
        string Kind,
        int Number,
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);

    private sealed class ReconcileWorkspace : IDisposable
    {
        public ReconcileWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-reconcile-tests-").FullName;
            WriteInstalledCliScript(stalePrTransition: false);
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

        public void WriteInstalledCliScript(bool stalePrTransition)
        {
            var binPath = Path.Combine(RootPath, ".intent-cli", "bin");
            Directory.CreateDirectory(binPath);
            var scriptPath = Path.Combine(binPath, "intent-cli");
            var prTransitionBlock = stalePrTransition
                ? "  echo \"Command 'automation pr-transition' is not yet implemented.\"\n  exit 1\n"
                : "  echo '--transition is required (review-start, request-update, or approved).'\n  exit 1\n";
            File.WriteAllText(
                scriptPath,
                "#!/bin/sh\n"
                + "case \"$*\" in\n"
                + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
                + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
                + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
                + "  'automation pr-transition')\n"
                + prTransitionBlock
                + "    ;;\n"
                + "  *) echo \"unexpected probe: $*\"; exit 1 ;;\n"
                + "esac\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    scriptPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }
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
