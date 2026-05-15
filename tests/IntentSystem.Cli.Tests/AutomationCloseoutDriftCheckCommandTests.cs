using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G356: Tests for <c>intent-cli automation closeout-drift-check</c>. Covers:
/// - Merged PR with non-Completed queue item → safe-repair.
/// - Unmerged PR → skipped (no drift).
/// - Multiple queue items sharing the same PR number → unsafe-stop.
/// - No queue-state present → empty result.
/// - --write: marks item Completed and appends pr-merged/closeout-recorded events.
/// - Diagnostics returns closeout-drift-repair instead of true-idle when count > 0.
/// </summary>
public sealed class AutomationCloseoutDriftCheckCommandTests : IDisposable
{
    public AutomationCloseoutDriftCheckCommandTests()
    {
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = null;
        AutomationCloseoutDriftCheckCommand.UtcNowFactory =
            () => new DateTimeOffset(2026, 5, 15, 10, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = null;
        AutomationCloseoutDriftCheckCommand.UtcNowFactory = null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Basic drift detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_MergedPrWithNonCompletedItem_ReturnsSafeRepair()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal("dry-run", result.Mode);
        Assert.Equal(1, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        Assert.Equal(0, result.AppliedCount);

        var record = Assert.Single(result.Records);
        Assert.Equal("G330", record.ExecutionUnit);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSafeRepair, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonPrMerged, record.ReasonCode);
        Assert.Equal(780, record.LinkedPrNumber);
    }

    [Fact]
    public void Execute_UnmergedPr_ReturnsSkipped()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: false, state: "OPEN");

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);

        var record = Assert.Single(result.Records);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSkipped, record.Result);
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonPrNotMerged, record.ReasonCode);
    }

    [Fact]
    public void Execute_AlreadyCompletedItem_NotIncludedInCandidates()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "completed",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        // PrLookup should never be called because completed items are excluded.
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Execute_ItemWithNullLinkedPr_ReturnsEmpty()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review", linkedPr: null));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(0, merged: false);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        // No candidates because linked_pr is null.
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Empty(result.Records);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Ambiguity guard (G356 unsafe-stop for multiple items sharing one PR)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_TwoItemsShareSamePrNumber_ReturnsUnsafeStop()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithTwoItems(
            "G330", "review", "https://github.com/J-Tech-Japan/intent-system/pull/780",
            "G331", "active", "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode); // unsafe-stop causes non-zero exit
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(2, result.UnsafeStopCount);

        Assert.All(result.Records, r =>
        {
            Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultUnsafeStop, r.Result);
            Assert.Equal(AutomationCloseoutDriftCheckCommand.ReasonAmbiguousMapping, r.ReasonCode);
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // No queue-state
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_NoQueueState_ReturnsEmptyAndExitsZero()
    {
        using var workspace = new DriftWorkspace();
        // No queue-state written.
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(0, merged: false);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.SafeRepairCount);
        Assert.Equal(0, result.UnsafeStopCount);
        Assert.Empty(result.Records);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Write path: marks queue item Completed + appends run events
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_WriteWithMergedPr_MarksQueueItemCompleted()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal("write", result.Mode);
        Assert.Equal(1, result.SafeRepairCount);
        Assert.Equal(1, result.AppliedCount);

        // Queue item must now be Completed.
        var queueOnDisk = workspace.QueueStateOnDisk();
        Assert.Contains("\"state\": \"completed\"", queueOnDisk, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WriteWithMergedPr_AppendsPrMergedAndCloseoutRecordedEvents()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);

        var runsLines = workspace.RunsLines();
        Assert.Equal(2, runsLines.Length);

        // Both events must be deserializable via RunLogSerializer.
        var event0 = RunLogSerializer.DeserializeLine(runsLines[0]);
        var event1 = RunLogSerializer.DeserializeLine(runsLines[1]);

        Assert.Equal("pr-merged", event0.Event);
        Assert.Equal("closeout-recorded", event1.Event);

        Assert.Equal("G330", event0.ExecutionUnit);
        Assert.Equal("G330", event1.ExecutionUnit);

        Assert.Equal("J-Tech-Japan/intent-system", event0.Repo);
        Assert.Equal(780, event0.Pr);

        Assert.Contains("closeout-drift-check", event0.By, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WriteWithUnmergedPr_DoesNotMutateQueueOrRunsLog()
    {
        using var workspace = new DriftWorkspace();
        var originalQueueText = BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780");
        workspace.WriteQueueState(originalQueueText);
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: false, state: "OPEN");

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(0, result.AppliedCount);

        // Queue-state must be unchanged.
        Assert.Equal(originalQueueText, workspace.QueueStateOnDisk());
        // No runs.jsonl created.
        Assert.Empty(workspace.RunsLines());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Diagnostics integration: closeout-drift-repair classification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyzer_WithCloseoutDriftRepairsAvailable_ClassifiesCloseoutDriftRepairNotTrueIdle()
    {
        // Snapshot: diagnostics returns closeout-drift-repair instead of
        // true-idle when closeoutDriftRepairsAvailable > 0 and no other
        // terminal class applies.
        var result = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo: "J-Tech-Japan/intent-system",
            openPrs: Array.Empty<GitHubAutomationPrCandidate>(),
            publishedIntentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            clarificationRequired: false,
            candidateExecutionUnit: null,
            closeoutDriftRepairsAvailable: 1);

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair,
            result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal(SafeRepairCategories.CloseoutDriftRepair, result.SafeRepairCategory);
        Assert.NotNull(result.RecommendedNextCommand);
        Assert.Contains("closeout-drift-check", result.RecommendedNextCommand!, StringComparison.Ordinal);
        Assert.Contains("--write", result.RecommendedNextCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyzer_CloseoutDriftRepairPrecedesTrueIdle_WhenNoOtherBlockers()
    {
        // With zero drift repairs, should return true-idle.
        var idleResult = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo: "J-Tech-Japan/intent-system",
            openPrs: Array.Empty<GitHubAutomationPrCandidate>(),
            publishedIntentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            clarificationRequired: false,
            candidateExecutionUnit: null,
            closeoutDriftRepairsAvailable: 0);
        Assert.Equal(AutomationHostReviewDiagnosticsClassifications.TrueIdle, idleResult.Classification);

        // With one drift repair, should return closeout-drift-repair.
        var driftResult = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo: "J-Tech-Japan/intent-system",
            openPrs: Array.Empty<GitHubAutomationPrCandidate>(),
            publishedIntentTargetIssues: Array.Empty<GitHubAutomationIssueCandidate>(),
            clarificationRequired: false,
            candidateExecutionUnit: null,
            closeoutDriftRepairsAvailable: 1);
        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair,
            driftResult.Classification);
    }

    [Fact]
    public void DiagnosticsCommand_WithCloseoutDriftRepairsAvailableFlag_ClassifiesCloseoutDriftRepair()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteInstalledCliScript();
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null; // use real check or stub
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () => new FakeEmptyLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHostReviewDiagnosticsCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system",
             "--closeout-drift-repairs-available", "2",
             "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationHostReviewDiagnosticsResult>(writer.ToString())!;
        Assert.Equal(AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair, result.Classification);
        Assert.True(result.SafeRepairAvailable);
        Assert.Equal(SafeRepairCategories.CloseoutDriftRepair, result.SafeRepairCategory);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Argument validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new DriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context, [], writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var workspace = new DriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--unknown-flag"],
            writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new DriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context, ["--help"], writer);
        Assert.Equal(0, exitCode);
        Assert.Contains("closeout-drift-check", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidFormatValue_ReturnsError()
    {
        using var workspace = new DriftWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "xml"],
            writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'json' or 'text'", writer.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Text format smoke test
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_TextFormat_PrintsReadableOutput()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review",
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/780"));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("closeout-drift-check", output, StringComparison.Ordinal);
        Assert.Contains("Safe repairs", output, StringComparison.Ordinal);
        Assert.Contains("G330", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Negative test: PR number in URL as numeric string
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_LinkedPrAsRawNumber_ParsesAndChecks()
    {
        using var workspace = new DriftWorkspace();
        workspace.WriteQueueState(BuildQueueState("G330", "review", linkedPr: "780"));
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () =>
            new FakePrLookup(780, merged: true);

        using var writer = new StringWriter();
        var exitCode = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(1, result.SafeRepairCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SafeRepairCategories constant
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SafeRepairCategories_CloseoutDriftRepair_HasExpectedValue()
    {
        Assert.Equal("closeout-drift-repair", SafeRepairCategories.CloseoutDriftRepair);
    }

    [Fact]
    public void AutomationHostReviewDiagnosticsClassifications_CloseoutDriftRepair_HasExpectedValue()
    {
        Assert.Equal("closeout-drift-repair",
            AutomationHostReviewDiagnosticsClassifications.CloseoutDriftRepair);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper types
    // ──────────────────────────────────────────────────────────────────────────

    private static string BuildQueueState(string executionUnit, string state, string? linkedPr)
    {
        var linked = linkedPr is null ? "null" : $"\"{linkedPr}\"";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-15T10:00:00Z",
              "items": [
                {
                  "execution_unit": "{{executionUnit}}",
                  "title": "title",
                  "state": "{{state}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{linked}},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private static string BuildQueueStateWithTwoItems(
        string unit1, string state1, string linkedPr1,
        string unit2, string state2, string linkedPr2)
    {
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-15T10:00:00Z",
              "items": [
                {
                  "execution_unit": "{{unit1}}",
                  "title": "first",
                  "state": "{{state1}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": "{{linkedPr1}}",
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "{{unit2}}",
                  "title": "second",
                  "state": "{{state2}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": "{{linkedPr2}}",
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private sealed class FakePrLookup : IGitHubPrLookup
    {
        private readonly int prNumber;
        private readonly bool merged;
        private readonly string state;

        public FakePrLookup(int prNumber, bool merged, string state = "MERGED")
        {
            this.prNumber = prNumber;
            this.merged = merged;
            this.state = merged ? "MERGED" : state;
        }

        public GitHubPrLookupResult Lookup(string repo, int number)
        {
            if (number != prNumber)
            {
                throw new InvalidOperationException(
                    $"FakePrLookup: unexpected PR number {number} (expected {prNumber})");
            }
            return new GitHubPrLookupResult
            {
                Number = number,
                State = this.state,
                Merged = merged,
            };
        }
    }

    private sealed class FakeEmptyLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels)
            => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels)
            => Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class DriftWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("closeout-drift-check-tests-")
            .FullName;

        public DriftWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    }
                }
            };
        }

        public CliContext Context { get; }

        public void WriteQueueState(string content) =>
            File.WriteAllText(Context.GetQueueStatePath(), content);

        public string QueueStateOnDisk() =>
            File.ReadAllText(Context.GetQueueStatePath());

        public string[] RunsLines() => File.Exists(Context.GetRunLogPath())
            ? File.ReadAllLines(Context.GetRunLogPath())
            : Array.Empty<string>();

        /// <summary>
        /// Write a minimal installed-cli script so
        /// <see cref="AutomationInstalledCliSurfaceProbe"/> passes for tests
        /// that exercise the full diagnostics command.
        /// </summary>
        public void WriteInstalledCliScript()
        {
            var binPath = Path.Combine(rootPath, ".intent-cli", "bin");
            Directory.CreateDirectory(binPath);
            var scriptPath = Path.Combine(binPath, "intent-cli");
            File.WriteAllText(
                scriptPath,
                "#!/bin/sh\n"
                + "case \"$*\" in\n"
                + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
                + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
                + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
                + "  'automation pr-transition') echo '--transition is required (review-start, request-update, or approved).'; exit 1 ;;\n"
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
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
