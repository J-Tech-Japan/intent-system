using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC9: equal-root event-append-failed reconciliation command exposure.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifyEventAppendReconcileHintTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "g837-notify";
    private const string TaskId = "G837-report";
    private readonly string root = Directory.CreateTempSubdirectory("g837-notify-").FullName;
    private readonly OrcaRunTestSupport.FakeBinFixture fakeBin;
    private readonly DateTimeOffset now = new(2026, 9, 15, 18, 0, 0, TimeSpan.Zero);
    private readonly string? previousPath;
    private readonly string? previousFakeOrcaLog;

    public NotifyEventAppendReconcileHintTests()
    {
        previousPath = Environment.GetEnvironmentVariable("PATH");
        OrcaRunTestSupport.ResetSeams();
        fakeBin = OrcaRunTestSupport.CreateFakeBinFixture(root);
        var existing = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        previousFakeOrcaLog = Environment.GetEnvironmentVariable("FAKE_ORCA_LOG");
        Environment.SetEnvironmentVariable("PATH", $"{fakeBin.BinDirectory}:{existing}");
        Environment.SetEnvironmentVariable("FAKE_ORCA_LOG", fakeBin.OrcaLogPath);
        OrcaRunTestSupport.ClearFakeLogs(fakeBin);
        NotifyCommand.UtcNowFactory = () => now;
        NotifyCommand.ProcessRunnerFactory = () => new NotifyProcessRunnerStub();
        WriteTopology();
        WriteTeamMode();
        WriteSessionLayerMode();
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        Environment.SetEnvironmentVariable("PATH", previousPath);
        Environment.SetEnvironmentVariable("FAKE_ORCA_LOG", previousFakeOrcaLog);
        OrcaRunTestSupport.ResetSeams();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EqualRootReport_UnwritableReader_EmitsReconciliationCommandAndSummary_G837()
    {
        var readerPath = Path.Combine(root, ".intent-cli", "events", Domain, $"{Team}-orchestration.jsonl");
        GuardedFileWrite.AppendLineFactory = (path, _) =>
        {
            if (string.Equals(path, readerPath, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("denied");
            }
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, Pending()).Written);

        var (exitCode, result) = RunReport();
        Assert.Equal(1, exitCode);
        Assert.Equal("event-append-failed", result.GetProperty("cause").GetString());
        var reconciliation = result.GetProperty("reconciliation_command").GetString()!;
        Assert.Equal(
            $"intent-cli notify reconcile --domain {Domain} --team {Team} --task-id {TaskId} --routing-root '{root}' --report-root '{root}' --write --format json",
            reconciliation);
        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains("The report is retained at '", summary, StringComparison.Ordinal);
        Assert.Contains("' and marked undelivered; orchestration reconciles with '", summary, StringComparison.Ordinal);
        Assert.Contains(reconciliation, summary, StringComparison.Ordinal);
        Assert.Contains("which names the notify collect recovery while the report is undelivered.", summary, StringComparison.Ordinal);
        OrcaRunTestSupport.AssertOrcaLogEmpty(fakeBin);
    }

    [Fact]
    public void Delegate_UnwritableReader_KeepsBaseSummary_G837()
    {
        var readerPath = Path.Combine(root, ".intent-cli", "events", Domain, $"{Team}-impl.jsonl");
        GuardedFileWrite.AppendLineFactory = (path, _) =>
        {
            if (string.Equals(path, readerPath, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("denied");
            }
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G837-delegate",
            ResultNonce = "delegate-nonce",
            DelegatingRole = "orchestration",
            RecipientRole = "implementation",
            ReportToRole = "orchestration",
            RecipientIdentity = $"role=implementation;reader=.intent-cli/events/{Domain}/{Team}-impl.jsonl",
            ExpectedArtifact = "artifact",
            DispatchedAt = now.AddMinutes(-1),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.ExternalResident,
            Reader = $".intent-cli/events/{Domain}/{Team}-impl.jsonl",
        }).Written);
        var (exitCode, result) = Run([
            "notify", "delegate",
            "--domain", Domain, "--team", Team,
            "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
            "--task-id", "G837-delegate",
            "--objective", "do work",
            "--expected-artifact", "artifact",
            "--result-nonce", "delegate-nonce",
            "--routing-root", root,
            "--write", "--format", "json",
        ]);
        Assert.Equal(1, exitCode);
        Assert.Equal("event-append-failed", result.GetProperty("cause").GetString());
        Assert.False(result.TryGetProperty("reconciliation_command", out _));
        Assert.EndsWith("Fix reader access and retry notify.", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReconcileCollectChain_ConvergesWithoutDuplicateEvent_G837()
    {
        var readerPath = Path.Combine(root, ".intent-cli", "events", Domain, $"{Team}-orchestration.jsonl");
        var appendBlocked = true;
        GuardedFileWrite.AppendLineFactory = (path, line) =>
        {
            if (appendBlocked && string.Equals(path, readerPath, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("denied");
            }

            GuardedFileWrite.AppendLineFactory = null;
            GuardedFileWrite.AppendLine(path, line);
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, Pending()).Written);
        var (reportExit, report) = RunReport();
        Assert.Equal(1, reportExit);
        Assert.Equal("event-append-failed", report.GetProperty("cause").GetString());

        var reconcileArgs = new[]
        {
            "notify", "reconcile", "--domain", Domain, "--team", Team, "--task-id", TaskId,
            "--routing-root", root, "--report-root", root, "--write", "--format", "json",
        };
        var (firstExit, first) = Run(reconcileArgs);
        Assert.Equal(1, firstExit);
        Assert.Equal("sender-local-report-not-delivered", first.GetProperty("cause").GetString());

        appendBlocked = false;
        var (collectExit, collect) = Run([
            "notify", "collect", "--domain", Domain, "--team", Team, "--task-id", TaskId,
            "--routing-root", root, "--report-root", root, "--write", "--format", "json",
        ]);
        Assert.Equal(0, collectExit);
        Assert.True(collect.GetProperty("delivered").GetBoolean());

        Assert.Single(File.ReadAllLines(readerPath));

        var (secondExit, second) = Run(reconcileArgs);
        Assert.Equal(0, secondExit);
        Assert.True(
            second.GetProperty("reconciled").GetBoolean()
            || second.GetProperty("already_converged").GetBoolean());
        Assert.Single(File.ReadAllLines(readerPath));
    }

    [Fact]
    public void EscalateFailure_OutputStableAcrossRoots_G837()
    {
        var baselineRoot = Directory.CreateTempSubdirectory("g837-notify-base-").FullName;
        try
        {
            WriteTopology(baselineRoot);
            WriteTeamMode(baselineRoot);
            WriteSessionLayerMode(baselineRoot);
            var baselineResult = RunOnRoot(baselineRoot, EscalateArgs());
            var current = RunOnRoot(root, EscalateArgs());
            Assert.Equal(baselineResult.Output, current.Output);
            Assert.Equal(baselineResult.ExitCode, current.ExitCode);
        }
        finally
        {
            Directory.Delete(baselineRoot, recursive: true);
        }
    }

    private (int ExitCode, JsonElement Result) RunReport() => Run(ReportArgs());

    private (int ExitCode, JsonElement Result) Run(string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, CreateContext(), writer);
        return (exitCode, OrcaRunTestSupport.Parse(writer.ToString()));
    }

    private (int ExitCode, string Output) RunOnRoot(string scenarioRoot, string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, ContextFor(scenarioRoot), writer);
        return (exitCode, writer.ToString());
    }

    private string[] ReportArgs() =>
    [
        "notify", "report",
        "--domain", Domain, "--team", Team,
        "--from", "design", "--to", "orchestration",
        "--task-id", TaskId,
        "--status", "completed",
        "--artifact", "ready PR",
        "--summary", "done",
        "--routing-root", root,
        "--report-root", root,
        "--write", "--format", "json",
    ];

    private string[] EscalateArgs() =>
    [
        "notify", "escalate",
        "--domain", Domain, "--team", Team,
        "--task-id", "G837-escalate",
        "--summary", "boundary",
        "--routing-root", root,
        "--write", "--format", "json",
    ];

    private NotifyPendingDelegation Pending() => new()
    {
        Domain = Domain,
        Team = Team,
        TaskId = TaskId,
        ResultNonce = "g837-report-nonce",
        DelegatingRole = "orchestration",
        RecipientRole = "design",
        ReportToRole = "orchestration",
        RecipientIdentity = $"role=design;reader=.intent-cli/events/{Domain}/{Team}.jsonl",
        ExpectedArtifact = "ready PR",
        DispatchedAt = now.AddMinutes(-1),
        TransportMode = SessionLayerMode.Agmsg,
        Resident = NotifyRecordedRole.ExternalResident,
        Reader = $".intent-cli/events/{Domain}/{Team}.jsonl",
    };

    private void WriteSessionLayerMode(string? scenarioRoot = null)
    {
        var targetRoot = scenarioRoot ?? root;
        var path = SessionLayerModeStore.ResolvePath(targetRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            entries = new[]
            {
                new
                {
                    domain = Domain,
                    team = Team,
                    mode = SessionLayerMode.HerdrOnly,
                    updated_at = "2026-09-15T00:00:00Z",
                    transitions = new[]
                    {
                        new
                        {
                            from = SessionLayerMode.Agmsg,
                            to = SessionLayerMode.HerdrOnly,
                            at = "2026-09-15T00:00:00Z",
                            reason = "g837-fixture",
                        },
                    },
                },
            },
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void WriteTeamMode(string? scenarioRoot = null)
    {
        var targetRoot = scenarioRoot ?? root;
        var path = TeamModeStore.ResolvePath(targetRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            $$"""
            {
              "schema_version": "1",
              "entries": [
                {
                  "domain": "{{Domain}}",
                  "team": "{{Team}}",
                  "mode": "delivery",
                  "updated_at": "2026-09-15T00:00:00Z",
                  "transitions": [
                    {
                      "from": "delivery",
                      "to": "delivery",
                      "at": "2026-09-15T00:00:00Z",
                      "reason": "g837-fixture"
                    }
                  ]
                }
              ]
            }
            """);
    }

    private void WriteTopology(string? scenarioRoot = null)
    {
        var targetRoot = scenarioRoot ?? root;
        var path = NotifyRoleTopologyStore.ResolvePath(targetRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            domain = Domain,
            team = Team,
            workspace_id = "w1",
            host_state = new { role = "design", envelope = OrcaRunTestSupport.HostStateEnvelope },
            roles = new Dictionary<string, object>
            {
                ["design"] = new
                {
                    resident = "external",
                    reader = $".intent-cli/events/{Domain}/{Team}.jsonl",
                    frontend = "claude-app",
                },
                ["orchestration"] = new
                {
                    resident = "external",
                    reader = $".intent-cli/events/{Domain}/{Team}-orchestration.jsonl",
                    frontend = "codex",
                },
                ["implementation"] = new
                {
                    resident = "external",
                    reader = $".intent-cli/events/{Domain}/{Team}-impl.jsonl",
                    frontend = "codex",
                },
            },
        }));
    }

    private CliContext CreateContext() => ContextFor(root);

    private static CliContext ContextFor(string scenarioRoot) => new()
    {
        RepoRoot = scenarioRoot,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

    private sealed class NotifyProcessRunnerStub : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            new(0, string.Empty, string.Empty);
    }
}
