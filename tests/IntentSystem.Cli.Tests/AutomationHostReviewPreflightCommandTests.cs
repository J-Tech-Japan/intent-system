using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class AutomationHostReviewPreflightCommandTests : IDisposable
{
    public AutomationHostReviewPreflightCommandTests()
    {
        AutomationHostReviewPreflightCommand.CandidateListerFactory = null;
        AutomationHostReviewPreflightCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        AutomationHostReviewPreflightCommand.CandidateListerFactory = null;
        AutomationHostReviewPreflightCommand.NestedProviderLauncher = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
    }

    [Fact]
    public void Execute_EmptyQueueReturnsNoActionableItem()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("no-actionable-item", result.Action);
        Assert.Null(result.TargetPr);
        Assert.Empty(result.InFlightPrs);
        Assert.Empty(result.InFlightIssues);
    }

    [Fact]
    public void Execute_PrReadySelectsOldestUpdatedReviewPr()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            Prs =
            [
                BuildPr(30, "created earlier", "https://github.com/J-Tech-Japan/intent-system/pull/30",
                    "2026-05-02T01:00:00Z", ["intent-target"],
                    updatedAt: "2026-05-02T03:00:00Z"),
                BuildPr(20, "updated earlier", "https://github.com/J-Tech-Japan/intent-system/pull/20",
                    "2026-05-02T02:00:00Z", ["intent-target", "intent-pr-rereview-ready"],
                    updatedAt: "2026-05-02T02:30:00Z"),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("review-pr", result.Action);
        Assert.Equal(20, result.TargetPr);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/20", result.TargetPrUrl);
        Assert.Equal([20, 30], result.InFlightPrs);
    }

    [Fact]
    public void Execute_PrAlreadyMarkedReviewingStillSelectsReviewPr()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            Prs =
            [
                BuildPr(490, "reviewing", "https://github.com/J-Tech-Japan/SekibanAsAService/pull/490",
                    "2026-05-06T02:00:00Z", ["intent-target", "intent-pr-reviewing"]),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("review-pr", result.Action);
        Assert.Equal(490, result.TargetPr);
        Assert.Equal([490], result.InFlightPrs);
    }

    [Fact]
    public void Execute_PrimaryIntentTargetPrWinsOverOlderIssueLinkedFallback()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            Prs =
            [
                BuildPr(30, "primary", "https://github.com/J-Tech-Japan/intent-system/pull/30",
                    "2026-05-02T02:00:00Z", ["intent-target"],
                    updatedAt: "2026-05-02T03:00:00Z"),
            ],
            AllPrs =
            [
                BuildPr(20, "fallback", "https://github.com/J-Tech-Japan/intent-system/pull/20",
                    "2026-05-02T01:00:00Z", [], body: "Closes #559",
                    updatedAt: "2026-05-02T02:00:00Z"),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    "2026-05-02T01:00:00Z", ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("review-pr", result.Action);
        Assert.Equal(30, result.TargetPr);
        Assert.Equal([30], result.InFlightPrs);
    }

    [Fact]
    public void Execute_BlockedPrimaryFallsBackToIssueLinkedPr()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            Prs =
            [
                BuildPr(30, "blocked primary", "https://github.com/J-Tech-Japan/intent-system/pull/30",
                    "2026-05-02T02:00:00Z", ["intent-target", "intent-pr-request-update"],
                    updatedAt: "2026-05-02T03:00:00Z"),
            ],
            AllPrs =
            [
                BuildPr(20, "fallback", "https://github.com/J-Tech-Japan/intent-system/pull/20",
                    "2026-05-02T01:00:00Z", [], body: "Closes #559",
                    updatedAt: "2026-05-02T02:00:00Z"),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    "2026-05-02T01:00:00Z", ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("review-pr", result.Action);
        Assert.Equal(20, result.TargetPr);
        Assert.Equal([20, 30], result.InFlightPrs);
    }

    [Fact]
    public void Execute_IssueLinkedPrWithoutIntentTargetFallsBackToReviewPr()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            AllPrs =
            [
                BuildPr(560, "G227", "https://github.com/J-Tech-Japan/intent-system/pull/560",
                    "2026-05-02T02:00:00Z", [], body: "Closes #559"),
            ],
            PublishedIssues =
            [
                BuildIssue(559, "G227", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    "2026-05-02T01:00:00Z", ["intent-target", "intent-pr-created"]),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("review-pr", result.Action);
        Assert.Equal(560, result.TargetPr);
        Assert.Equal([560], result.InFlightPrs);
    }

    [Fact]
    public void Execute_RereviewReadyDoesNotOverrideBlockingReviewState()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            Prs =
            [
                BuildPr(560, "blocked", "https://github.com/J-Tech-Japan/intent-system/pull/560",
                    "2026-05-02T02:00:00Z",
                    ["intent-target", "intent-pr-rereview-ready", "intent-pr-request-update"]),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G228", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("skip-next-slice-due-to-wip", result.Action);
        Assert.Null(result.TargetPr);
        Assert.Equal([560], result.InFlightPrs);
    }

    [Fact]
    public void Execute_WipIssueBlocksCandidate()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister
        {
            Issues =
            [
                BuildIssue(559, "wip", "https://github.com/J-Tech-Japan/intent-system/issues/559",
                    "2026-05-02T01:00:00Z", ["intent-target"]),
            ],
        };
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G228", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("skip-next-slice-due-to-wip", result.Action);
        Assert.Equal([559], result.InFlightIssues);
        Assert.Equal("G228", result.CandidateExecutionUnit);
    }

    [Fact]
    public void Execute_CandidateReadyWhenNoWip()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--candidate", "G228", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("candidate-ready", result.Action);
        Assert.Equal("G228", result.CandidateExecutionUnit);
    }

    [Fact]
    public void Execute_ClarificationRequiredWins()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new FakeLister
        {
            Prs = [BuildPr(20, "ready", "https://github.com/J-Tech-Japan/intent-system/pull/20", "2026-05-02T01:00:00Z", ["intent-target"])],
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--clarification-required", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("clarification-required", result.Action);
        Assert.Null(result.TargetPr);
    }

    [Fact]
    public void Execute_StaleInstalledCliStopsBeforeListingCandidates()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new ThrowingLister();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;
        workspace.WriteInstalledCliScript(stalePrTransition: true);

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("stale-host-cli", result.Action);
        Assert.Contains(".intent-cli", result.InstalledCliPath, StringComparison.Ordinal);
        Assert.NotEmpty(result.MissingCommandSurfaces);
        Assert.Contains(result.MissingCommandSurfaces, surface =>
            string.Equals(surface.Command, "intent-cli automation pr-transition", StringComparison.Ordinal)
            && string.Equals(surface.Transition, "review-start", StringComparison.Ordinal)
            && !surface.Available);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("automation pr-transition", StringComparison.Ordinal)
            && warning.Contains("not yet implemented", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_HelpSurfacesHostReviewPreflightWithoutCandidateListing()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new ThrowingLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation host-review-preflight", writer.ToString(), StringComparison.Ordinal);
    }

    // ── focused-guide-automation-setup-same-thread-and-wrapper-help-tests ───────────

    [Fact]
    public void Execute_HelpInterceptedByWrapper_SurfacesStillAvailableWhenCommandWorks()
    {
        // Simulate a wrapper that intercepts --help (returns dotnet help instead of intent-cli help)
        // but allows direct command invocation to reach intent-cli.
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        var lister = new FakeLister();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => lister;

        // Override ProbeRunner to simulate help interception: direct invocations return
        // intent-cli's own error (missing required args), not dotnet's help output.
        AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, args) =>
        {
            var joined = string.Join(" ", args);
            if (joined.Contains("--help", StringComparison.Ordinal))
            {
                // Simulate dotnet tool exec intercepting --help (would have happened with old probes)
                return new InstalledCliProbeResult(0, "dotnet tool exec help — not intent-cli", string.Empty);
            }
            return joined switch
            {
                "automation summary" => new InstalledCliProbeResult(1, string.Empty, "--domain is required."),
                "automation host-review-preflight" => new InstalledCliProbeResult(1, string.Empty, "--repo is required."),
                "automation issue-publish" => new InstalledCliProbeResult(1, string.Empty, "--issue is required."),
                "automation pr-transition" => new InstalledCliProbeResult(
                    1, string.Empty,
                    "--transition is required (review-start, request-update, or approved)."),
                _ => new InstalledCliProbeResult(1, $"unexpected probe: {joined}", string.Empty)
            };
        };

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("no-actionable-item", result.Action);
        Assert.NotEqual("stale-host-cli", result.Action);
    }

    [Fact]
    public void Execute_ProbeDoesNotUseHelpFlag_NoHelpInProbedArgs()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new FakeLister();

        var probedArgSets = new List<IReadOnlyList<string>>();
        AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, args) =>
        {
            probedArgSets.Add(args);
            return new InstalledCliProbeResult(1, string.Empty,
                "--transition is required (review-start, request-update, or approved).");
        };

        using var writer = new StringWriter();
        AutomationHostReviewPreflightCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.NotEmpty(probedArgSets);
        Assert.All(probedArgSets, args =>
            Assert.DoesNotContain("--help", args, StringComparer.Ordinal));
    }

    [Fact]
    public void CommandRouter_RegistersAutomationHostReviewPreflight()
    {
        using var workspace = new AutomationHostReviewPreflightWorkspace();
        AutomationHostReviewPreflightCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "host-review-preflight", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewPreflightResult>(writer.ToString())!;
        Assert.Equal("no-actionable-item", result.Action);
    }

    // ── G276-regression-analyze-unit-tests ───────────────────────────────

    [Fact]
    public void Analyze_NoPrWipClearCandidateProvided_ReturnsCandidateReady()
    {
        var result = AutomationHostReviewPreflightCommand.Analyze(
            "test-owner/test-repo",
            reviewCandidatePrs: Array.Empty<GitHubAutomationPrCandidate>(),
            inFlightPrCandidates: Array.Empty<GitHubAutomationPrCandidate>(),
            intentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            candidateExecutionUnit: "G99",
            clarificationRequired: false);

        Assert.Equal("candidate-ready", result.Action);
        Assert.Equal("G99", result.CandidateExecutionUnit);
        Assert.Empty(result.InFlightPrs);
        Assert.Empty(result.InFlightIssues);
    }

    [Fact]
    public void Analyze_NoPrWipClearNoCandidate_ReturnsNoActionableItemNotStaleCli()
    {
        // no-actionable-item from Analyze() is NOT the same as stale-host-cli.
        // stale-host-cli is a surface-probe result emitted before Analyze() is called.
        // The guide (G276) requires Stage 2 next-slice dry-run before treating
        // no-actionable-item as a truly idle wake.
        var result = AutomationHostReviewPreflightCommand.Analyze(
            "test-owner/test-repo",
            reviewCandidatePrs: Array.Empty<GitHubAutomationPrCandidate>(),
            inFlightPrCandidates: Array.Empty<GitHubAutomationPrCandidate>(),
            intentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            candidateExecutionUnit: null,
            clarificationRequired: false);

        Assert.Equal("no-actionable-item", result.Action);
        Assert.NotEqual("stale-host-cli", result.Action);
        Assert.Null(result.CandidateExecutionUnit);
        Assert.Empty(result.InFlightPrs);
        Assert.Empty(result.InFlightIssues);
    }

    [Fact]
    public void Analyze_NoPrInFlightIssueBlocking_ReturnsSkipNextSliceDueToWip()
    {
        var inFlightIssue = BuildIssue(42, "G42", "https://github.com/t/r/issues/42", "2024-01-01", ["intent-target"]);

        var result = AutomationHostReviewPreflightCommand.Analyze(
            "test-owner/test-repo",
            reviewCandidatePrs: Array.Empty<GitHubAutomationPrCandidate>(),
            inFlightPrCandidates: Array.Empty<GitHubAutomationPrCandidate>(),
            intentTargetIssues: [inFlightIssue],
            candidateExecutionUnit: "G99",
            clarificationRequired: false);

        Assert.Equal("skip-next-slice-due-to-wip", result.Action);
        Assert.Contains(42, result.InFlightIssues);
    }

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        string url,
        string createdAt,
        IReadOnlyList<string> labels,
        string body = "",
        string? updatedAt = null) =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            Body = body,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt ?? createdAt,
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number,
        string title,
        string url,
        string createdAt,
        IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = title,
            Url = url,
            CreatedAt = createdAt,
            Labels = labels.Select(label => new GitHubAutomationLabel { Name = label }).ToArray(),
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> Prs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> AllPrs { get; init; } = Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> Issues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> PublishedIssues { get; init; } = Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            requiredLabels.Count == 0 ? AllPrs : Prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            requiredLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal)
                ? PublishedIssues
                : Issues;
    }

    private sealed class ThrowingLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("candidate listing should not run");

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("candidate listing should not run");
    }

    private sealed class AutomationHostReviewPreflightWorkspace : IDisposable
    {
        public AutomationHostReviewPreflightWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("automation-host-review-preflight-tests-").FullName;
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
            // Probes omit --help to avoid wrapper-layer interception. Each surface is probed
            // without --help; the script returns intent-cli-style usage errors (not "not yet
            // implemented") so the surface is detected as available.
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
