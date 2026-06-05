using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G343 (PR #790 repair): end-to-end coverage for the
/// <c>automation intent-target-gap-recovery</c> command surface that
/// wires <see cref="IntentTargetGapAnalyzer"/> into a host-callable
/// command. Reviewer flagged the analyzer was unreachable without
/// this surface.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class AutomationIntentTargetGapRecoveryCommandTests : IDisposable
{
    public AutomationIntentTargetGapRecoveryCommandTests()
    {
        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = null;
        AutomationIntentTargetGapRecoveryCommand.IssueLookupFactory = null;
        AutomationIntentTargetGapRecoveryCommand.IssuePublishExecutor = null;
    }

    public void Dispose()
    {
        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = null;
        AutomationIntentTargetGapRecoveryCommand.IssueLookupFactory = null;
        AutomationIntentTargetGapRecoveryCommand.IssuePublishExecutor = null;
    }

    [Fact]
    public void Execute_DryRun_ReportsSafeRepair_AndDoesNotCallIssuePublish()
    {
        using var workspace = new GapWorkspace();
        workspace.WritePublishArtifact("SKS-G343", createdIssueNumber: 1234);

        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(new[] { BuildIssue(1234, state: "OPEN", labels: Array.Empty<string>()) });

        var executorInvocations = 0;
        AutomationIntentTargetGapRecoveryCommand.IssuePublishExecutor = (_, _, _) =>
        {
            executorInvocations++;
            return 0;
        };

        using var writer = new StringWriter();
        var exitCode = AutomationIntentTargetGapRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, executorInvocations); // dry-run never invokes write
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());
    }

    [Fact]
    public void Execute_Write_InvokesIssuePublishForSafeRepair()
    {
        using var workspace = new GapWorkspace();
        workspace.WritePublishArtifact("SKS-G343", createdIssueNumber: 1234);

        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(new[] { BuildIssue(1234, state: "OPEN", labels: Array.Empty<string>()) });

        var capturedArgs = new List<string[]>();
        AutomationIntentTargetGapRecoveryCommand.IssuePublishExecutor = (_, args, _) =>
        {
            capturedArgs.Add(args);
            return 0;
        };

        using var writer = new StringWriter();
        var exitCode = AutomationIntentTargetGapRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Single(capturedArgs);
        Assert.Contains("--repo", capturedArgs[0]);
        Assert.Contains("owner/repo", capturedArgs[0]);
        Assert.Contains("--issue", capturedArgs[0]);
        Assert.Contains("1234", capturedArgs[0]);
        Assert.Contains("--write", capturedArgs[0]);

        using var doc = JsonDocument.Parse(SplitJsonPrefix(writer.ToString()));
        Assert.Equal("write", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("applied_count").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("applied_issues").GetArrayLength());
    }

    [Fact]
    public void Execute_NonCanonicalArtifact_ReportsUnsafeStop_NoIssuePublish()
    {
        // Artifact with an unknown top-level key — recently-flagged
        // operator-edit signature. Recovery must refuse to apply
        // intent-target via issue-publish.
        using var workspace = new GapWorkspace();
        workspace.WritePublishArtifactWithExtraKey("SKS-G343", createdIssueNumber: 1234, extraKey: "operator_note");

        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(new[] { BuildIssue(1234, state: "OPEN", labels: Array.Empty<string>()) });

        var executorInvocations = 0;
        AutomationIntentTargetGapRecoveryCommand.IssuePublishExecutor = (_, _, _) =>
        {
            executorInvocations++;
            return 0;
        };

        using var writer = new StringWriter();
        var exitCode = AutomationIntentTargetGapRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode); // unsafe-stop -> non-zero exit
        Assert.Equal(0, executorInvocations);
        using var doc = JsonDocument.Parse(SplitJsonPrefix(writer.ToString()));
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("unsafe_stop_count").GetInt32() >= 1);
    }

    [Fact]
    public void Execute_IssueAlreadyHasIntentTarget_NoSafeRepair()
    {
        using var workspace = new GapWorkspace();
        workspace.WritePublishArtifact("SKS-G343", createdIssueNumber: 1234);

        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(new[] { BuildIssue(1234, state: "OPEN", labels: new[] { "intent-target" }) });

        using var writer = new StringWriter();
        var exitCode = AutomationIntentTargetGapRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("unsafe_stop_count").GetInt32());
    }

    [Fact]
    public void Execute_AmbiguousArtifacts_UnsafeStop_NoApplication()
    {
        using var workspace = new GapWorkspace();
        // Two execution units both claim the same issue number — the
        // analyzer must refuse to deterministically pick one.
        workspace.WritePublishArtifact("SKS-G343", createdIssueNumber: 1234);
        workspace.WritePublishArtifact("SKS-G999", createdIssueNumber: 1234);

        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(new[] { BuildIssue(1234, state: "OPEN", labels: Array.Empty<string>()) });

        var executorInvocations = 0;
        AutomationIntentTargetGapRecoveryCommand.IssuePublishExecutor = (_, _, _) =>
        {
            executorInvocations++;
            return 0;
        };

        using var writer = new StringWriter();
        var exitCode = AutomationIntentTargetGapRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, executorInvocations);
        using var doc = JsonDocument.Parse(SplitJsonPrefix(writer.ToString()));
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("unsafe_stop_count").GetInt32());
    }

    [Fact]
    public void Execute_NoIssuesDirectory_EmitsEmptyResult_ExitZero()
    {
        using var workspace = new GapWorkspace();
        // No publish artifacts written.

        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exitCode = AutomationIntentTargetGapRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
    }

    [Fact]
    public void Execute_IssuePublishExecutorFails_SurfacesAsWarning_NonZeroExit()
    {
        using var workspace = new GapWorkspace();
        workspace.WritePublishArtifact("SKS-G343", createdIssueNumber: 1234);

        AutomationIntentTargetGapRecoveryCommand.CandidateListerFactory = () =>
            new FakeLister(new[] { BuildIssue(1234, state: "OPEN", labels: Array.Empty<string>()) });

        AutomationIntentTargetGapRecoveryCommand.IssuePublishExecutor = (_, _, _) => 1;

        using var writer = new StringWriter();
        var exitCode = AutomationIntentTargetGapRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "owner/repo", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(SplitJsonPrefix(writer.ToString()));
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("warnings").GetArrayLength() >= 1);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsError()
    {
        using var workspace = new GapWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationIntentTargetGapRecoveryCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_HelpListsIntentTargetGapRecovery()
    {
        // G343 (PR #790 repair): the recovery analyzer must be
        // reachable through an installed command surface — pin the
        // help listing so the command stays operator-grep-able.
        var router = typeof(CommandRouter);
        var helpField = router.GetField("AutomationCommandHelp",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(helpField);
        var lines = (System.Collections.Generic.IReadOnlyList<string>?)helpField!.GetValue(null);
        Assert.NotNull(lines);
        Assert.Contains(lines!, l => l.Contains("intent-target-gap-recovery", StringComparison.Ordinal));
    }

    [Fact]
    public void CommandRouter_DispatchesIntentTargetGapRecoveryToNewCommand()
    {
        // Verifies the dispatcher actually wires the help-listed
        // command to AutomationIntentTargetGapRecoveryCommand.Execute,
        // not some other shim. The CommandHandler delegate is private,
        // so we reflect dynamically.
        var router = typeof(CommandRouter);
        var commandsField = router.GetField("ImplementedCommands",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(commandsField);
        var groups = commandsField!.GetValue(null);
        Assert.NotNull(groups);

        // The outer dictionary maps group name -> inner dictionary.
        // Use the non-generic IDictionary view that all
        // Dictionary<,> instances expose so we don't need to know the
        // exact generic types.
        var outer = (System.Collections.IDictionary)groups!;
        Assert.True(outer.Contains("automation"));
        var inner = (System.Collections.IDictionary)outer["automation"]!;
        Assert.True(inner.Contains("intent-target-gap-recovery"));
        var handler = (Delegate)inner["intent-target-gap-recovery"]!;
        Assert.Equal(
            typeof(AutomationIntentTargetGapRecoveryCommand).GetMethod(
                "Execute",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!,
            handler.Method);
    }

    /// <summary>
    /// Helper: skip any non-JSON prefix written by issue-publish
    /// execution (the parent command emits a "--- automation
    /// issue-publish #N ---" header when the executor produced output)
    /// so the test can parse the JSON tail.
    /// </summary>
    private static string SplitJsonPrefix(string text)
    {
        var brace = text.IndexOf('{');
        return brace >= 0 ? text[brace..] : text;
    }

    private static GitHubAutomationIssueCandidate BuildIssue(int number, string state, IReadOnlyList<string> labels) =>
        new()
        {
            Number = number,
            Title = $"Issue {number}",
            Url = $"https://github.com/owner/repo/issues/{number}",
            CreatedAt = "2026-05-12T00:00:00Z",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            State = state,
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        public FakeLister(IReadOnlyList<GitHubAutomationIssueCandidate> issues) => this.issues = issues;
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            issues;
    }

    private sealed class GapWorkspace : IDisposable
    {
        public GapWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("intent-target-gap-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void WritePublishArtifact(string executionUnit, int createdIssueNumber)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = executionUnit,
                PublishStatus = "issue-created",
                PacketPath = $"intents/packets/{executionUnit}/packet.md",
                IssueBodyPath = $"intents/packets/{executionUnit}/issue-body.md",
                CreatedIssueNumber = createdIssueNumber,
                CreatedIssueUrl = $"https://github.com/owner/repo/issues/{createdIssueNumber}",
                PublishedLabelName = null,
            };
            File.WriteAllText(Path.Combine(dir, "publish.yaml"), IssuePublishArtifactYaml.Serialize(artifact));
        }

        public void WritePublishArtifactWithExtraKey(string executionUnit, int createdIssueNumber, string extraKey)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = executionUnit,
                PublishStatus = "issue-created",
                PacketPath = $"intents/packets/{executionUnit}/packet.md",
                IssueBodyPath = $"intents/packets/{executionUnit}/issue-body.md",
                CreatedIssueNumber = createdIssueNumber,
                CreatedIssueUrl = $"https://github.com/owner/repo/issues/{createdIssueNumber}",
                PublishedLabelName = null,
            };
            var canonical = IssuePublishArtifactYaml.Serialize(artifact);
            var withExtra = canonical + extraKey + ": \"operator-authored\"" + Environment.NewLine;
            File.WriteAllText(Path.Combine(dir, "publish.yaml"), withExtra);
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
