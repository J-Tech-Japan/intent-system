using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationHostReviewDiagnosticsCommandTests : IDisposable
{
    public AutomationHostReviewDiagnosticsCommandTests()
    {
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = null;
        AutomationHostReviewDiagnosticsCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = null;
        AutomationHostReviewDiagnosticsCommand.NestedProviderLauncher = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
    }

    [Fact]
    public void Execute_NoPrsNoIssues_ClassifiesTrueIdle()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("true-idle", result.Classification);
        Assert.True(result.ReadOnly);
        Assert.Null(result.RecommendedNextCommand);
    }

    [Fact]
    public void Execute_StuckIntentPrReviewingWithoutExitTransition_ClassifiesStuckReviewingAndRecommendsTransition()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(490, "stuck reviewing", "https://github.com/J-Tech-Japan/intent-system/pull/490",
                    body: "Closes #559", labels: ["intent-target", "intent-pr-reviewing"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("stuck-reviewing", result.Classification);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("pr-transition", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains(result.Details, d => d.TargetNumber == 490);
    }

    [Fact]
    public void Execute_PrLinksPublishedIssueWithoutIntentTarget_ClassifiesMissingTargetOnPr()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(420, "missing target", "https://github.com/J-Tech-Japan/intent-system/pull/420",
                    body: "Closes #559", labels: Array.Empty<string>()),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    labels: ["intent-target"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("missing-target-on-pr", result.Classification);
        Assert.Contains("automation reconcile", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrCarriesBothRequestUpdateAndRereviewReady_ClassifiesConflictWithStructuredClarification()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(500, "conflict", "https://github.com/J-Tech-Japan/intent-system/pull/500",
                    body: "Closes #560", labels: ["intent-target", "intent-pr-request-update", "intent-pr-rereview-ready"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("request-update-rereview-conflict", result.Classification);
        Assert.NotNull(result.StructuredClarification);
        Assert.Equal(2, result.StructuredClarification!.Options.Count);
    }

    [Fact]
    public void Execute_OpenIntentTargetIssueButNoActionablePr_ClassifiesWipCapBlocked()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            PublishedIssues =
            [
                BuildIssue(700, "G300 in flight", "https://github.com/J-Tech-Japan/intent-system/issues/700",
                    labels: ["intent-target", "intent-issue-in-progress"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("wip-cap-blocked", result.Classification);
    }

    [Fact]
    public void Execute_ClarificationRequiredFlag_ClassifiesClarificationRequired()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--clarification-required", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("clarification-required", result.Classification);
    }

    [Fact]
    public void Execute_StaleHostCli_ClassifiesStaleHostCliWithoutCallingLister()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        workspace.WriteInstalledCliScript(stalePrTransition: true);
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("stale-host-cli", result.Classification);
        Assert.Contains("automation doctor", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ActionableReviewPrPresent_ClassifiesReviewPrActionable()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(800, "ready review", "https://github.com/J-Tech-Japan/intent-system/pull/800",
                    body: "Closes #100", labels: ["intent-target"]),
            ],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("review-pr-actionable", result.Classification);
    }

    [Fact]
    public void Execute_CandidateProvidedWithoutWipOrPr_ClassifiesCandidateReady()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G99", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal("candidate-ready", result.Classification);
        Assert.Contains("G99", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesAnyFile()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        workspace.WriteSentinel();
        var snapshotBefore = workspace.SnapshotFiles();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister
        {
            AllPrs =
            [
                BuildPr(490, "stuck", "https://github.com/J-Tech-Japan/intent-system/pull/490",
                    body: "Closes #559", labels: ["intent-target", "intent-pr-reviewing"]),
            ],
        };

        using var writer = new StringWriter();
        AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        var snapshotAfter = workspace.SnapshotFiles();
        Assert.Equal(snapshotBefore, snapshotAfter);
    }

    [Fact]
    public void CommandRouter_RegistersAutomationHostReviewDiagnostics()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "host-review-diagnostics", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.True(result.ReadOnly);
    }

    [Fact]
    public void CommandRouter_HelpListsAutomationHostReviewDiagnostics()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute([], workspace.Context, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation host-review-diagnostics", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuidePromptMatrix_HostLoopMentionsHostReviewDiagnostics()
    {
        using var workspace = new HostReviewDiagnosticsWorkspace();
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            workspace.Context,
            ["--mode", "host-loop", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("automation host-review-diagnostics", output, StringComparison.Ordinal);
        Assert.Contains("Stage 4", output, StringComparison.Ordinal);
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
            CreatedAt = "2026-05-07T00:00:00Z",
            UpdatedAt = "2026-05-07T00:00:00Z",
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
            CreatedAt = "2026-05-07T00:00:00Z",
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> AllPrs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> PublishedIssues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => AllPrs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => PublishedIssues;
    }

    private sealed class ThrowingLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("lister should not be invoked when surface probe rejects");

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("lister should not be invoked when surface probe rejects");
    }

    private sealed class HostReviewDiagnosticsWorkspace : IDisposable
    {
        public HostReviewDiagnosticsWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-host-review-diagnostics-tests-").FullName;
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

        public void WriteSentinel() =>
            File.WriteAllText(Path.Combine(RootPath, "sentinel.txt"), "unchanged");

        public IReadOnlyDictionary<string, string> SnapshotFiles()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                snapshot[path] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            }
            return snapshot;
        }

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
