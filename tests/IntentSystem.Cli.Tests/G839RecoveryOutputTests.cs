using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using static IntentSystem.Cli.Tests.G839RecoveryFakes;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G839: format-honouring named-cause output on the three pr-created-stale-recovery
/// audit-append failure paths.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G839RecoveryOutputTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G839";
    private const int Issue = 839;
    private const int Pr = 1823;
    private const string Team = "intent-cli-dev";
    private const string Ruling = "stale intent-pr-created after unmerged PR close";

    public G839RecoveryOutputTests()
    {
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () => new ThrowingIssueLookup();
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => new ThrowingPrLookup();
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () => new ThrowingLister();
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => new ThrowingLabelMutator();
        AutomationPrCreatedStaleRecoveryCommand.ClaimVerifierFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.ClaimVerifierFactory = null;
        AutomationPrCreatedStaleRecoveryCommand.UtcNowFactory = null;
    }

    [Fact]
    public void AuditOnlyRecoveredAppendFailure_EmitsJsonRefusal()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        var labelMutator = new RecordingLabelMutator("intent-target");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        SetRunLogReadOnly(workspace.Context, readOnly: true);

        var (exitCode, output) = Execute(workspace, write: true, format: "json");
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal(1, exitCode);
        Assert.Equal("refused", root.GetProperty("outcome").GetString());
        Assert.Equal("recovered-event-append-failed", root.GetProperty("cause").GetString());
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.False(root.GetProperty("applied").GetBoolean());
        Assert.Equal(Pr, root.GetProperty("linked_pr").GetInt32());
        Assert.Empty(root.GetProperty("planned_mutations").EnumerateArray());
        Assert.False(root.TryGetProperty("recheck_cause", out _));

        var message = AssertAppendFailureMessage(workspace.Context);
        Assert.Equal(
            $"audit-only completion: intent-pr-created was already absent and no GitHub mutation was made, but the recovered event could not be appended: {message}. Re-run `intent-cli automation pr-created-stale-recovery --write` to complete the audit trail.",
            root.GetProperty("summary").GetString());
        Assert.Empty(labelMutator.Transitions);
        workspace.AssertHostArtifactsUnchanged();
    }

    [Fact]
    public void CompletionPathRecoveredAppendFailure_EmitsJsonRefusal()
    {
        using var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target"));
        var labelMutator = new RecordingLabelMutator("intent-target");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        SetRunLogReadOnly(workspace.Context, readOnly: true);

        var (exitCode, output) = Execute(workspace, write: true, format: "json");
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal(1, exitCode);
        Assert.Equal("recovered-event-append-failed", root.GetProperty("cause").GetString());
        Assert.Equal(Pr, root.GetProperty("linked_pr").GetInt32());
        Assert.False(root.TryGetProperty("recheck_cause", out _));
        Assert.Contains(
            root.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString() == "the earlier run may have aborted.");

        var message = AssertAppendFailureMessage(workspace.Context);
        Assert.Equal(
            $"interrupted recovery not completed: no GitHub mutation was made and the started event for PR #{Pr} stays open, because the recovered event could not be appended: {message}. Re-run `intent-cli automation pr-created-stale-recovery --write` to complete the audit trail.",
            root.GetProperty("summary").GetString());
        Assert.Empty(labelMutator.Transitions);
        workspace.AssertHostArtifactsUnchanged();
    }

    [Theory]
    [InlineData("pr-merged")]
    [InlineData("issue-unavailable")]
    [InlineData("claim-held")]
    [InlineData("in-progress-present")]
    [InlineData("queue-item-ambiguous")]
    public void AbortedAppendFailure_EmitsJsonRefusalWithRecheckCause(string row)
    {
        using var workspace = CreateAbortAppendWorkspace(row);
        var labelMutator = new RecordingLabelMutator("intent-target", "intent-pr-created");
        AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () => labelMutator;
        ConfigureAbortAppendScenario(workspace, row);

        var (exitCode, output) = Execute(workspace, write: true, format: "json");
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal(1, exitCode);
        Assert.Equal("aborted-event-append-failed", root.GetProperty("cause").GetString());
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.False(root.GetProperty("applied").GetBoolean());
        Assert.Equal(Pr, root.GetProperty("linked_pr").GetInt32());
        Assert.Equal(ExpectedRecheckCause(row), root.GetProperty("recheck_cause").GetString());

        var message = AssertAppendFailureMessage(workspace.Context);
        Assert.Equal(ExpectedAbortedSummary(row, message), root.GetProperty("summary").GetString());
        Assert.Empty(labelMutator.Transitions);
        workspace.AssertHostArtifactsUnchanged();
    }

    [Fact]
    public void ThreePaths_MarkdownRendersStandardBlock()
    {
        foreach (var (scenario, expectedCause) in new (string Scenario, string Cause)[]
                 {
                     ("audit-only", "recovered-event-append-failed"),
                     ("completion", "recovered-event-append-failed"),
                     ("abort", "aborted-event-append-failed"),
                 })
        {
            using var workspace = scenario switch
            {
                "audit-only" => CreateProceedWorkspace(),
                "completion" => CreateCompletionWorkspace(),
                _ => CreateAbortAppendWorkspace("claim-held"),
            };

            AutomationPrCreatedStaleRecoveryCommand.LabelMutatorFactory = () =>
                scenario == "audit-only"
                    ? new RecordingLabelMutator("intent-target")
                    : new RecordingLabelMutator("intent-target", "intent-pr-created");
            if (scenario == "audit-only")
            {
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(OpenIssue("intent-target"));
            }
            else if (scenario == "completion")
            {
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(OpenIssue("intent-target"));
            }
            else
            {
                ConfigureAbortAppendScenario(workspace, "claim-held");
            }

            SetRunLogReadOnly(workspace.Context, readOnly: scenario != "abort");
            var (exitCode, output) = Execute(workspace, write: true, format: "markdown");

            Assert.Equal(1, exitCode);
            Assert.Contains("- outcome: refused", output, StringComparison.Ordinal);
            Assert.Contains("- mode: write", output, StringComparison.Ordinal);
            Assert.Contains("- applied: false", output, StringComparison.Ordinal);
            Assert.Contains($"- cause: {expectedCause}", output, StringComparison.Ordinal);
            Assert.Contains("- summary:", output, StringComparison.Ordinal);
            Assert.DoesNotContain("recheck_cause", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(RecoverySweepFixtureIds))]
    public void RecheckCause_AbsentFromEveryOtherResult(string fixtureId)
    {
        var output = G839RecoveryByteIdentityTests.CaptureRecovery(fixtureId);
        Assert.DoesNotContain("recheck_cause", output, StringComparison.Ordinal);
    }

    public static TheoryData<string> RecoverySweepFixtureIds() =>
        new(G839ByteIdentityHarness.RecoveryScenarioIds);

    [Fact]
    public void RecheckCause_AbsentFromRefusalPaths()
    {
        using var workspace = CreateProceedWorkspace();
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(ClosedIssue());
        foreach (var format in new[] { "json", "markdown" })
        {
            var (_, output) = Execute(workspace, write: false, format);
            Assert.DoesNotContain("recheck_cause", output, StringComparison.Ordinal);
        }
    }

    private static RecoveryWorkspace CreateCompletionWorkspace()
    {
        var workspace = CreateProceedWorkspace();
        workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
        return workspace;
    }

    private static RecoveryWorkspace CreateAbortAppendWorkspace(string row) =>
        row is "in-progress-present" or "queue-item-ambiguous"
            ? CreateSameKeyResumeWorkspace(row)
            : CreateProceedWorkspace();

    private static void ConfigureAbortAppendScenario(RecoveryWorkspace workspace, string row)
    {
        switch (row)
        {
            case "pr-merged":
            {
                var lookup = new ReadonlyBeforeNthPrLookup(
                    workspace.Context,
                    2,
                    new SequencedPrLookup(ClosedUnmerged(Pr), Merged(Pr)));
                AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => lookup;
                break;
            }
            case "issue-unavailable":
            {
                var lookup = new ReadonlyBeforeNthIssueLookup(
                    workspace.Context,
                    2,
                    new SequencedIssueLookup(
                        OpenIssue("intent-target", "intent-pr-created"),
                        new InvalidOperationException("issue read failed")));
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () => lookup;
                break;
            }
            case "claim-held":
                AutomationPrCreatedStaleRecoveryCommand.ClaimVerifierFactory =
                    new ReadonlyBeforeNthClaimVerifier(
                        workspace.Context,
                        2,
                        new SequencedClaimVerifier(UnheldClaim(), HeldClaim())).Verify;
                break;
            case "in-progress-present":
                workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
                AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
                    new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created", "intent-issue-in-progress"));
                AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () => new FakePrLookup(ClosedUnmerged(Pr));
                AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () => new FakeLister();
                SetRunLogReadOnly(workspace.Context, readOnly: true);
                break;
            case "queue-item-ambiguous":
                workspace.AppendRunEvent(BuildRecoveryEvent(AutomationPrCreatedStaleRecoveryCommand.EventStarted, Pr));
                SetRunLogReadOnly(workspace.Context, readOnly: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row), row, null);
        }
    }

    private static string ExpectedRecheckCause(string row) =>
        row switch
        {
            "pr-merged" => "pr-merged",
            "issue-unavailable" => "issue-unavailable",
            "claim-held" => "claim-held",
            "in-progress-present" => "in-progress-present",
            "queue-item-ambiguous" => "queue-item-ambiguous",
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
        };

    private static string ExpectedAbortedSummary(string row, string message) =>
        row switch
        {
            "pr-merged" =>
                $"re-check refused: linked PR state changed. No GitHub mutation was made, but the aborted event could not be appended: {message}; the started event for PR #{Pr} stays open. Re-run `intent-cli automation pr-created-stale-recovery --write` to close it.",
            "issue-unavailable" =>
                $"re-check failed closed: issue read failed. No GitHub mutation was made, but the aborted event could not be appended: {message}; the started event for PR #{Pr} stays open. Re-run `intent-cli automation pr-created-stale-recovery --write` to close it.",
            "claim-held" =>
                $"re-check refused: claim status '{ClaimOwnershipVerification.StatusOwned}'. No GitHub mutation was made, but the aborted event could not be appended: {message}; the started event for PR #{Pr} stays open. Re-run `intent-cli automation pr-created-stale-recovery --write` to close it.",
            "in-progress-present" =>
                $"refusing: issue carries intent-issue-in-progress. No GitHub mutation was made, but the aborted event could not be appended: {message}; the started event for PR #{Pr} stays open. Re-run `intent-cli automation pr-created-stale-recovery --write` to close it.",
            "queue-item-ambiguous" =>
                $"refusing: more than one queue item matches the requested issue. No GitHub mutation was made, but the aborted event could not be appended: {message}; the started event for PR #{Pr} stays open. Re-run `intent-cli automation pr-created-stale-recovery --write` to close it.",
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
        };

    private static string AssertAppendFailureMessage(CliContext context)
    {
        var runsPath = context.GetRunLogPath();
        try
        {
            File.AppendAllText(runsPath, "probe\n");
            throw new InvalidOperationException("append probe unexpectedly succeeded");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
        finally
        {
            SetRunLogReadOnly(context, readOnly: false);
        }
    }

    private static RecoveryWorkspace CreateProceedWorkspace(bool duplicateQueueItem = false)
    {
        var workspace = new RecoveryWorkspace(Unit, Issue);
        workspace.WriteProceedHostState(linkedPr: Pr, duplicateQueueItem: duplicateQueueItem);
        workspace.EnsureClaimsStore();
        SeedGitHubFakes(workspace, Pr);
        return workspace;
    }

    private static RecoveryWorkspace CreateSameKeyResumeWorkspace(string cause) =>
        cause switch
        {
            "queue-item-ambiguous" => CreateProceedWorkspace(duplicateQueueItem: true),
            _ => CreateProceedWorkspace(),
        };

    private static void SeedGitHubFakes(RecoveryWorkspace workspace, int linkedPr)
    {
        AutomationPrCreatedStaleRecoveryCommand.IssueLookupFactory = () =>
            new FakeIssueLookup(OpenIssue("intent-target", "intent-pr-created"));
        AutomationPrCreatedStaleRecoveryCommand.PrLookupFactory = () =>
            new FakePrLookup(ClosedUnmerged(linkedPr));
        AutomationPrCreatedStaleRecoveryCommand.CandidateListerFactory = () => new FakeLister();
    }

    private static (int ExitCode, string Output) Execute(RecoveryWorkspace workspace, bool write, string format)
    {
        var args = new List<string>
        {
            "--repo", Repo,
            "--issue", Issue.ToString(),
            "--execution-unit", Unit,
            "--team", Team,
            "--ruling", Ruling,
            "--format", format,
        };
        if (write)
        {
            args.Add("--write");
        }

        using var writer = new StringWriter();
        var exitCode = AutomationPrCreatedStaleRecoveryCommand.Execute(workspace.Context, args.ToArray(), writer);
        return (exitCode, writer.ToString());
    }

    private static void SetRunLogReadOnly(CliContext context, bool readOnly)
    {
        var runsPath = context.GetRunLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
        if (!File.Exists(runsPath))
        {
            File.WriteAllText(runsPath, string.Empty);
        }

        if (readOnly)
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(runsPath, FileAttributes.ReadOnly);
            }
            else
            {
                RunChmod(runsPath, "444");
            }
        }
        else
        {
            File.SetAttributes(runsPath, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows())
            {
                RunChmod(runsPath, "644");
            }
        }
    }

    private static void RunChmod(string path, string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "chmod",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add(path);
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }

    private static ClaimOwnershipVerification UnheldClaim() => new(
        Passed: true,
        Status: ClaimOwnershipVerification.StatusUnheldAvailable,
        Scope: $"execution-unit:{Unit}",
        StoreConfigured: true,
        InvokingTeam: Team,
        Holder: null,
        HolderTeam: null,
        Detail: "unheld");

    private static ClaimOwnershipVerification HeldClaim() => new(
        Passed: false,
        Status: ClaimOwnershipVerification.StatusOwned,
        Scope: $"execution-unit:{Unit}",
        StoreConfigured: true,
        InvokingTeam: Team,
        Holder: "builder",
        HolderTeam: Team,
        Detail: "owned");

    private static GitHubIssueLookupResult OpenIssue(params string[] labels) => new()
    {
        Number = Issue,
        State = "OPEN",
        Title = $"{Unit}: stale pr-created recovery fixture",
        Labels = labels.Select(name => new GitHubIssueLabel { Name = name }).ToArray(),
    };

    private static GitHubPrLookupResult ClosedUnmerged(int pr) => new()
    {
        Number = pr,
        State = "CLOSED",
        Merged = false,
        MergedAt = null,
    };

    private static GitHubIssueLookupResult ClosedIssue() => new()
    {
        Number = Issue,
        State = "CLOSED",
        Title = $"{Unit}: closed",
        Labels = Array.Empty<GitHubIssueLabel>(),
    };

    private static GitHubPrLookupResult Merged(int pr) => new()
    {
        Number = pr,
        State = "MERGED",
        Merged = true,
        MergedAt = "2026-01-01T00:00:00Z",
    };

    private static RunEvent BuildRecoveryEvent(string eventName, int pr) => new()
    {
        Ts = FixedNow.AddMinutes(-10),
        ExecutionUnit = Unit,
        Event = eventName,
        By = AutomationPrCreatedStaleRecoveryCommand.By,
        Repo = Repo,
        LinkedIssue = $"https://github.com/{Repo}/issues/{Issue}",
        LinkedPr = $"https://github.com/{Repo}/pull/{pr}",
        Pr = pr,
        Reason = $"ruling: {Ruling}; claim: unheld-available",
    };

    private sealed class ReadonlyBeforeNthIssueLookup : IGitHubIssueLookup
    {
        private readonly CliContext context;
        private readonly int nth;
        private readonly IGitHubIssueLookup inner;
        private int calls;

        public ReadonlyBeforeNthIssueLookup(CliContext context, int nth, IGitHubIssueLookup inner)
        {
            this.context = context;
            this.nth = nth;
            this.inner = inner;
        }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            calls++;
            if (calls == nth)
            {
                SetRunLogReadOnly(context, readOnly: true);
            }

            return inner.Lookup(repo, issueNumber);
        }
    }

    private sealed class ReadonlyBeforeNthPrLookup : IGitHubPrLookup
    {
        private readonly CliContext context;
        private readonly int nth;
        private readonly IGitHubPrLookup inner;
        private int calls;

        public ReadonlyBeforeNthPrLookup(CliContext context, int nth, IGitHubPrLookup inner)
        {
            this.context = context;
            this.nth = nth;
            this.inner = inner;
        }

        public GitHubPrLookupResult Lookup(string repo, int prNumber)
        {
            calls++;
            if (calls == nth)
            {
                SetRunLogReadOnly(context, readOnly: true);
            }

            return inner.Lookup(repo, prNumber);
        }
    }

    private sealed class ReadonlyBeforeNthClaimVerifier
    {
        private readonly CliContext context;
        private readonly int nth;
        private readonly SequencedClaimVerifier inner;
        private int calls;

        public ReadonlyBeforeNthClaimVerifier(CliContext context, int nth, SequencedClaimVerifier inner)
        {
            this.context = context;
            this.nth = nth;
            this.inner = inner;
        }

        public ClaimOwnershipVerification Verify(string repoRoot, string scope, string? team, bool allowUnheld)
        {
            calls++;
            if (calls == nth)
            {
                SetRunLogReadOnly(context, readOnly: true);
            }

            return inner.Verify(repoRoot, scope, team, allowUnheld);
        }
    }

    private sealed class RecoveryWorkspace : IDisposable
    {
        private string queueStateSnapshot = string.Empty;
        private string publishYamlSnapshot = string.Empty;

        public RecoveryWorkspace(string unit, int issue)
        {
            UnitName = unit;
            IssueNumber = issue;
            RootPath = Directory.CreateTempSubdirectory("g839-recovery-").FullName;
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
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string UnitName { get; }

        public int IssueNumber { get; }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WriteProceedHostState(int? linkedPr, bool duplicateQueueItem = false)
        {
            var linkedPrJson = linkedPr is null
                ? "null"
                : $"\"https://github.com/{Repo}/pull/{linkedPr}\"";
            var duplicateItem = duplicateQueueItem
                ? $$"""

                    ,{
                      "execution_unit": "{{UnitName}}-dup",
                      "title": "{{UnitName}}-dup title",
                      "state": "queued",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "",
                      "packet_paths": {
                        "yaml": ".intent-cli/issues/{{UnitName}}-dup/packet.yaml",
                        "implementation": ".intent-cli/issues/{{UnitName}}-dup/implementation.md",
                        "review_context": ".intent-cli/issues/{{UnitName}}-dup/review-context.md"
                      },
                      "linked_issue": {
                        "repo": "{{Repo}}",
                        "number": {{IssueNumber}},
                        "url": "https://github.com/{{Repo}}/issues/{{IssueNumber}}"
                      },
                      "linked_pr": {{linkedPrJson}},
                      "worker_role": "builder",
                      "review_role": "reviewer",
                      "priority": "normal"
                    }
                    """
                : string.Empty;
            File.WriteAllText(
                Context.GetQueueStatePath(),
                $$"""
                {
                  "schema_version": "1",
                  "updated_at": "{{FixedNow:O}}",
                  "items": [
                    {
                      "execution_unit": "{{UnitName}}",
                      "title": "{{UnitName}} title",
                      "state": "queued",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "",
                      "packet_paths": {
                        "yaml": ".intent-cli/issues/{{UnitName}}/packet.yaml",
                        "implementation": ".intent-cli/issues/{{UnitName}}/implementation.md",
                        "review_context": ".intent-cli/issues/{{UnitName}}/review-context.md"
                      },
                      "linked_issue": {
                        "repo": "{{Repo}}",
                        "number": {{IssueNumber}},
                        "url": "https://github.com/{{Repo}}/issues/{{IssueNumber}}"
                      },
                      "linked_pr": {{linkedPrJson}},
                      "worker_role": "builder",
                      "review_role": "reviewer",
                      "priority": "normal"
                    }{{duplicateItem}}
                  ]
                }
                """);

            var issueDir = Path.Combine(RootPath, ".intent-cli", "issues", UnitName);
            Directory.CreateDirectory(issueDir);
            File.WriteAllText(
                Path.Combine(issueDir, "publish.yaml"),
                IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
                {
                    ExecutionUnit = UnitName,
                    PublishStatus = "published",
                    PacketPath = $".intent-cli/issues/{UnitName}/packet.yaml",
                    IssueBodyPath = $".intent-cli/issues/{UnitName}/issue-body.md",
                    CreatedIssueNumber = IssueNumber,
                    CreatedIssueUrl = $"https://github.com/{Repo}/issues/{IssueNumber}",
                    PublishedLabelName = "intent-target",
                    LinkedPrNumber = linkedPr ?? Pr,
                    LinkedPrUrl = $"https://github.com/{Repo}/pull/{linkedPr ?? Pr}",
                }));

            queueStateSnapshot = File.ReadAllText(Context.GetQueueStatePath());
            publishYamlSnapshot = File.ReadAllText(Path.Combine(issueDir, "publish.yaml"));
        }

        public void EnsureClaimsStore() =>
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli", "claims"));

        public void AppendRunEvent(RunEvent runEvent)
        {
            var runsPath = Context.GetRunLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
            File.AppendAllText(runsPath, RunLogSerializer.SerializeLine(runEvent) + "\n");
        }

        public void AssertHostArtifactsUnchanged()
        {
            Assert.Equal(queueStateSnapshot, File.ReadAllText(Context.GetQueueStatePath()));
            var publishPath = Path.Combine(RootPath, ".intent-cli", "issues", UnitName, "publish.yaml");
            Assert.Equal(publishYamlSnapshot, File.ReadAllText(publishPath));
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
