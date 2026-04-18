using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using System.Text.Json;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class RunSuperviseCommandTests
{
    [Fact]
    public void Execute_GivenActiveItemWithoutExistingSession_CreatesMonitoringSessionWithoutMutatingQueueOrRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:15:00Z");
            var originalQueueState = File.ReadAllText(queueStatePath);
            var originalRunLog = File.ReadAllText(runLogPath);

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Run supervision updated for G25", output, StringComparison.Ordinal);
            Assert.Contains("Worker entry: run implement", output, StringComparison.Ordinal);
            Assert.Contains("Session status: monitoring", output, StringComparison.Ordinal);
            Assert.Contains("Retry count: 0/3", output, StringComparison.Ordinal);

            var sessionPath = Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json");
            Assert.True(File.Exists(sessionPath));
            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionPath));
            Assert.Equal("G25", session.ExecutionUnit);
            Assert.Equal(RunSupervisionWorkerEntry.Implement, session.WorkerEntry);
            Assert.Equal(RunSupervisionSessionStatus.Monitoring, session.Status);
            Assert.Equal(".intent-cli/implement/G25.request.md", session.HandoffArtifactRef);
            Assert.Equal(Path.Combine(repoRoot, ".intent-cli", "worktrees", "G25"), session.WorktreePath);
            Assert.Equal(Path.Combine(repoRoot, "submodules", "intent-system"), session.ChildRepoPath);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/178", session.LinkedIssue);
            Assert.Equal("2026-04-08T10:15:00.0000000+00:00", session.LastHeartbeatAt.ToString("O"));
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenFixingItemWithStaleImplementSession_RealignsSessionToFixLoop()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession()));
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:15:00Z");
            var originalQueueState = File.ReadAllText(queueStatePath);
            var originalRunLog = File.ReadAllText(runLogPath);

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Worker entry: run fix", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Session status: monitoring", writer.ToString(), StringComparison.Ordinal);

            var sessionPath = Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json");
            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionPath));
            Assert.Equal(RunSupervisionWorkerEntry.Fix, session.WorkerEntry);
            Assert.Equal(RunSupervisionSessionStatus.Monitoring, session.Status);
            Assert.Equal("fixing", session.QueueState);
            Assert.Equal(".intent-cli/fix/G25.request.md", session.HandoffArtifactRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/180", session.LinkedPr);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1", session.CommentRef);
            Assert.Equal("2026-04-08T09:00:00.0000000+00:00", session.CreatedAt.ToString("O"));
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenExpiredHeartbeat_SchedulesRetryAndAppendsRetryScheduledEvent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession()));
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:20:00Z");
            var originalQueueState = File.ReadAllText(queueStatePath);

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Retry scheduled: yes", writer.ToString(), StringComparison.Ordinal);

            var sessionPath = Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json");
            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionPath));
            Assert.Equal(RunSupervisionSessionStatus.RetryScheduled, session.Status);
            Assert.Equal("2026-04-08T10:25:00.0000000+00:00", session.NextRetryAt?.ToString("O"));

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-scheduled", runEvents[^1].Event);
            Assert.Contains("Heartbeat expired after 15 minutes", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenConfiguredSupervisionPolicy_UsesConfiguredRetryDelayAndArtifactRoot()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runtime-supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession()));
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:20:00Z");

            var exitCode = RunSuperviseCommand.Execute(CreateContext(
                repoRoot,
                supervisionArtifactRoot: ".intent-cli/runtime-supervision",
                staleHeartbeatTimeoutMinutes: 10,
                retryDelayMinutes: 12,
                retryBudget: 5), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains(".intent-cli/runtime-supervision/G25.session.json", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Retry count: 0/5", writer.ToString(), StringComparison.Ordinal);

            var sessionPath = Path.Combine(repoRoot, ".intent-cli", "runtime-supervision", "G25.session.json");
            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionPath));
            Assert.Equal(RunSupervisionSessionStatus.RetryScheduled, session.Status);
            Assert.Equal(5, session.RetryBudget);
            Assert.Equal("2026-04-08T10:32:00.0000000+00:00", session.NextRetryAt?.ToString("O"));

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-scheduled", runEvents[^1].Event);
            Assert.Contains("Heartbeat expired after 10 minutes", runEvents[^1].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenDueRetrySchedule_AutoResumesSameWorkerEntryAndAppendsRunEvents()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateRetryScheduledSession()));
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunImplementExecutor = RunSuperviseCommand.RunImplementExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) => new RunImplementResult
            {
                Request = new RunImplementRequest
                {
                    ExecutionUnit = executionUnit,
                    State = "active",
                    ImplementRole = "Claude",
                    QueueWorkerRole = "coder",
                    QueueReviewRole = "reviewer",
                    WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                    ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                    Branch = "issue-178-g25",
                    LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
                    LatestLinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/180",
                    PacketRef = ".intent-cli/issues/G25/packet.yaml",
                    ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
                    IssueTitle = "[G25] Run Supervise Command",
                    Goal = "Supervise the active execution loop.",
                    TargetPart = "cli run supervise command",
                    TargetRepo = "submodules/intent-system",
                    TargetPath = ".",
                    InScope = [],
                    OutOfScope = [],
                    AcceptanceCriteria = [],
                    DeterministicReviewChecks = [],
                    ExpectedEvidence = []
                },
                ArtifactPath = ".intent-cli/implement/G25.request.md"
            };
            var originalQueueState = File.ReadAllText(queueStatePath);

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var sessionPath = Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json");
            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionPath));
            Assert.Equal(RunSupervisionSessionStatus.Monitoring, session.Status);
            Assert.Equal(1, session.RetryCount);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/180", session.LinkedPr);
            Assert.Null(session.NextRetryAt);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^2].Event);
            Assert.Equal("auto-resumed", runEvents[^1].Event);
            Assert.Equal("run implement", runEvents[^1].Reason);
            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
        }
    }

    [Fact]
    public void Execute_GivenDeadImplementWorkerSession_CapturesFailureAndAutoResumesImplementLoop()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession()));
        WriteDeadImplementDirectRunArtifacts(repoRoot, "pid:999999");
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunImplementExecutor = RunSuperviseCommand.RunImplementExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteLiveImplementDirectRunArtifacts(repoRoot, executionUnit, "pid:4242");

                return new RunImplementResult
                {
                    Request = new RunImplementRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "active",
                        ImplementRole = "Claude",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-178-g25",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
                        LatestLinkedPr = null,
                        PacketRef = ".intent-cli/issues/G25/packet.yaml",
                        ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
                        IssueTitle = "[G25] Run Supervise Command",
                        Goal = "Supervise retryable run interruptions.",
                        TargetPart = "cli run supervise command",
                        TargetRepo = "submodules/intent-system",
                        TargetPath = ".",
                        InScope = [],
                        OutOfScope = [],
                        AcceptanceCriteria = [],
                        DeterministicReviewChecks = [],
                        ExpectedEvidence = []
                    },
                    ArtifactPath = ".intent-cli/implement/G25.request.md"
                };
            };

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionWorkerEntry.Implement, session.WorkerEntry);
            Assert.Equal(RunSupervisionSessionStatus.Monitoring, session.Status);
            Assert.Equal(1, session.RetryCount);
            Assert.Null(session.NextRetryAt);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl")));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
                && providerEvent.Payload.ValueKind == JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
                && exitCodeElement.GetInt32() == 1);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^2].Event);
            Assert.Equal("auto-resumed", runEvents[^1].Event);
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
        }
    }

    [Fact]
    public void Execute_GivenDeadImplementWorkerSessionWithCapturedBackendExit_AutoResumesUsingBackendExitReason()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession()));
        WriteDeadImplementDirectRunArtifacts(repoRoot, "pid:999999", includeBackendExit: true);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunImplementExecutor = RunSuperviseCommand.RunImplementExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteLiveImplementDirectRunArtifacts(repoRoot, executionUnit, "pid:4242");

                return new RunImplementResult
                {
                    Request = new RunImplementRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "active",
                        ImplementRole = "Claude",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-178-g25",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
                        LatestLinkedPr = null,
                        PacketRef = ".intent-cli/issues/G25/packet.yaml",
                        ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
                        IssueTitle = "[G25] Run Supervise Command",
                        Goal = "Supervise retryable run interruptions.",
                        TargetPart = "cli run supervise command",
                        TargetRepo = "submodules/intent-system",
                        TargetPath = ".",
                        InScope = [],
                        OutOfScope = [],
                        AcceptanceCriteria = [],
                        DeterministicReviewChecks = [],
                        ExpectedEvidence = []
                    },
                    ArtifactPath = ".intent-cli/implement/G25.request.md"
                };
            };

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^2].Event);
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
        }
    }

    [Fact]
    public async Task Execute_GivenDeadImplementWorkerSessionWhenBackendExitLandsDuringRaceWindow_AutoResumesUsingBackendExitReason()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession()));
        WriteDeadImplementDirectRunArtifacts(repoRoot, "pid:999999");
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunImplementExecutor = RunSuperviseCommand.RunImplementExecutor;
        var originalRaceWindow = RunSuperviseCommand.TerminalFailureRaceWindow;
        var originalRacePollInterval = RunSuperviseCommand.TerminalFailureRacePollInterval;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.TerminalFailureRaceWindow = TimeSpan.FromMilliseconds(200);
            RunSuperviseCommand.TerminalFailureRacePollInterval = TimeSpan.FromMilliseconds(5);
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteLiveImplementDirectRunArtifacts(repoRoot, executionUnit, "pid:4242");

                return new RunImplementResult
                {
                    Request = new RunImplementRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "active",
                        ImplementRole = "Claude",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-178-g25",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
                        LatestLinkedPr = null,
                        PacketRef = ".intent-cli/issues/G25/packet.yaml",
                        ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
                        IssueTitle = "[G25] Run Supervise Command",
                        Goal = "Supervise retryable run interruptions.",
                        TargetPart = "cli run supervise command",
                        TargetRepo = "submodules/intent-system",
                        TargetPath = ".",
                        InScope = [],
                        OutOfScope = [],
                        AcceptanceCriteria = [],
                        DeterministicReviewChecks = [],
                        ExpectedEvidence = []
                    },
                    ArtifactPath = ".intent-cli/implement/G25.request.md"
                };
            };

            var appendTask = Task.Run(async () =>
            {
                await Task.Delay(20);
                new DirectRunProviderEventWriter(Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl"))
                    .Append(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-08T10:20:01.0000000+00:00",
                        ExecutionUnit = "G25",
                        Provider = "Claude",
                        EntryKind = "implement",
                        SessionId = "pid:999999",
                        Kind = "provider-event",
                        Payload = JsonSerializer.SerializeToElement(new
                        {
                            type = "backend-exit",
                            exit_code = 1
                        })
                    });
            });

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);
            await appendTask;

            Assert.Equal(0, exitCode);
            Assert.Contains("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^2].Event);
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
            RunSuperviseCommand.TerminalFailureRacePollInterval = originalRacePollInterval;
        }
    }

    [Fact]
    public async Task Execute_GivenDeadImplementWorkerSessionAtRetryExhaustionWhenBackendExitLandsAfterPreviousRaceWindow_BlocksUsingBackendExitReason()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession() with
            {
                RetryCount = 3,
                RetryBudget = 3
            }));
        WriteDeadImplementDirectRunArtifacts(repoRoot, "pid:999999");
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRaceWindow = RunSuperviseCommand.TerminalFailureRaceWindow;
        var originalRacePollInterval = RunSuperviseCommand.TerminalFailureRacePollInterval;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.TerminalFailureRacePollInterval = TimeSpan.FromMilliseconds(5);

            var appendTask = Task.Run(async () =>
            {
                await Task.Delay(150);
                new DirectRunProviderEventWriter(Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl"))
                    .Append(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-08T10:20:01.0000000+00:00",
                        ExecutionUnit = "G25",
                        Provider = "Claude",
                        EntryKind = "implement",
                        SessionId = "pid:999999",
                        Kind = "provider-event",
                        Payload = JsonSerializer.SerializeToElement(new
                        {
                            type = "backend-exit",
                            exit_code = 1
                        })
                    });
            });

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);
            await appendTask;

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G25");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("backend exit code 1", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal("blocked", session.QueueState);
            Assert.Contains("backend exit code 1", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-exhausted", runEvents[^2].Event);
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.Contains("backend exit code 1", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^1].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
            RunSuperviseCommand.TerminalFailureRacePollInterval = originalRacePollInterval;
        }
    }

    [Fact]
    public void Execute_GivenActiveItemWithoutExistingSessionAndDeadImplementWorkerSession_CapturesFailureAndAutoResumesImplementLoop()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Active)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateActiveRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G25.request.md"),
            "# Execution Worker Handoff");
        WriteDeadImplementDirectRunArtifacts(repoRoot, "pid:999999");
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunImplementExecutor = RunSuperviseCommand.RunImplementExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteLiveImplementDirectRunArtifacts(repoRoot, executionUnit, "pid:4242");

                return new RunImplementResult
                {
                    Request = new RunImplementRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "active",
                        ImplementRole = "Claude",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-178-g25",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
                        LatestLinkedPr = null,
                        PacketRef = ".intent-cli/issues/G25/packet.yaml",
                        ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
                        IssueTitle = "[G25] Run Supervise Command",
                        Goal = "Supervise retryable run interruptions.",
                        TargetPart = "cli run supervise command",
                        TargetRepo = "submodules/intent-system",
                        TargetPath = ".",
                        InScope = [],
                        OutOfScope = [],
                        AcceptanceCriteria = [],
                        DeterministicReviewChecks = [],
                        ExpectedEvidence = []
                    },
                    ArtifactPath = ".intent-cli/implement/G25.request.md"
                };
            };

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionWorkerEntry.Implement, session.WorkerEntry);
            Assert.Equal(RunSupervisionSessionStatus.Monitoring, session.Status);
            Assert.Equal(1, session.RetryCount);
            Assert.Null(session.NextRetryAt);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl")));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
                && providerEvent.Payload.ValueKind == JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
                && exitCodeElement.GetInt32() == 1);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^2].Event);
            Assert.Equal("auto-resumed", runEvents[^1].Event);
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
        }
    }

    [Fact]
    public void Execute_GivenDeadFixWorkerSession_CapturesFailureAndAutoResumesFixLoop()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession(workerEntry: RunSupervisionWorkerEntry.Fix)));
        WriteDeadFixDirectRunArtifacts(repoRoot, "pid:999999");
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunFixExecutor = RunSuperviseCommand.RunFixExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteLiveFixDirectRunArtifacts(repoRoot, executionUnit, "pid:4242");

                return new RunFixResult
                {
                    Request = new RunFixRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "fixing",
                        ImplementRole = "Claude",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-178-g25",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
                        LatestLinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/180",
                        LatestCommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1",
                        PacketRef = ".intent-cli/issues/G25/packet.yaml",
                        ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
                        ReviewCommentArtifactRef = ".intent-cli/reviews/G25.comment.json",
                        ReviewRequestRef = ".intent-cli/reviews/G25.request.json",
                        ReviewCommentBodyPath = ".intent-cli/reviews/G25.comment.md",
                        IssueTitle = "[G25] Run Supervise Command",
                        Goal = "Supervise retryable run interruptions.",
                        TargetPart = "cli run supervise command",
                        TargetRepo = "submodules/intent-system",
                        TargetPath = ".",
                        InScope = [],
                        OutOfScope = [],
                        AcceptanceCriteria = [],
                        DeterministicReviewChecks = [],
                        ExpectedEvidence = []
                    },
                    ArtifactPath = ".intent-cli/fix/G25.request.md"
                };
            };

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionWorkerEntry.Fix, session.WorkerEntry);
            Assert.Equal(RunSupervisionSessionStatus.Monitoring, session.Status);
            Assert.Equal(1, session.RetryCount);
            Assert.Null(session.NextRetryAt);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl")));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
                && providerEvent.Payload.ValueKind == JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
                && exitCodeElement.GetInt32() == 1);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^2].Event);
            Assert.Equal("auto-resumed", runEvents[^1].Event);
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenStartupOnlyDeadFixWorkerSession_BlocksWithoutConsumingRetryBudget()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession(workerEntry: RunSupervisionWorkerEntry.Fix)));
        WriteStartupOnlyDeadFixDirectRunArtifacts(repoRoot, "pid:999999");
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            var selectedItem = Assert.Single(queueState.Items, item => item.ExecutionUnit == "G25");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("during provider startup", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionWorkerEntry.Fix, session.WorkerEntry);
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(0, session.RetryCount);
            Assert.Null(session.NextRetryAt);
            Assert.Contains("during provider startup", session.LastInterruptionReason, StringComparison.Ordinal);
            Assert.Contains("startup warnings or noise", session.LastInterruptionReason, StringComparison.Ordinal);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.result.json")));
            Assert.Equal("pid:999999", resultArtifact.SessionId);
            Assert.Equal("failed", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("during provider startup", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public async Task Execute_GivenStartupOnlyDeadFixWorkerSessionWhenBackendExitLandsDuringRaceWindow_BlocksWithoutAutoResume()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession(workerEntry: RunSupervisionWorkerEntry.Fix)));
        WriteStartupOnlyDeadFixDirectRunArtifacts(repoRoot, "pid:999999", includeBackendExit: false);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRaceWindow = RunSuperviseCommand.TerminalFailureRaceWindow;
        var originalRacePollInterval = RunSuperviseCommand.TerminalFailureRacePollInterval;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.TerminalFailureRaceWindow = TimeSpan.FromMilliseconds(700);
            RunSuperviseCommand.TerminalFailureRacePollInterval = TimeSpan.FromMilliseconds(5);

            var appendTask = Task.Run(async () =>
            {
                await Task.Delay(600);
                new DirectRunProviderEventWriter(Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl"))
                    .Append(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-08T10:20:01.0000000+00:00",
                        ExecutionUnit = "G25",
                        Provider = "Claude",
                        EntryKind = "fix",
                        SessionId = "pid:999999",
                        Kind = "provider-event",
                        Payload = JsonSerializer.SerializeToElement(new
                        {
                            type = "backend-exit",
                            exit_code = 1
                        })
                    });
            });

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);
            await appendTask;

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(0, session.RetryCount);
            Assert.Contains("during provider startup", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
            RunSuperviseCommand.TerminalFailureRacePollInterval = originalRacePollInterval;
        }
    }

    [Fact]
    public void Execute_GivenStartupOnlyDeadFixWorkerSessionWithoutCapturedTerminalEvent_BlocksUsingSyntheticStartupOnlyReason()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession(workerEntry: RunSupervisionWorkerEntry.Fix)));
        WriteStartupOnlyDeadFixDirectRunArtifacts(repoRoot, "pid:999999", includeBackendExit: false);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRaceWindow = RunSuperviseCommand.TerminalFailureRaceWindow;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.TerminalFailureRaceWindow = TimeSpan.Zero;

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(0, session.RetryCount);
            Assert.Contains("during provider startup", session.LastInterruptionReason, StringComparison.Ordinal);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl")));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
                && providerEvent.Payload.ValueKind == JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
                && exitCodeElement.GetInt32() == 1);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("during provider startup", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
        }
    }

    [Fact]
    public void Execute_GivenFixingItemWithoutExistingSessionAndDeadFixWorkerSession_CapturesFailureAndAutoResumesFixLoop()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        WriteDeadFixDirectRunArtifacts(repoRoot, "pid:999999");
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunFixExecutor = RunSuperviseCommand.RunFixExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteLiveFixDirectRunArtifacts(repoRoot, executionUnit, "pid:4242");

                return new RunFixResult
                {
                    Request = new RunFixRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "fixing",
                        ImplementRole = "Claude",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-178-g25",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
                        LatestLinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/180",
                        LatestCommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1",
                        PacketRef = ".intent-cli/issues/G25/packet.yaml",
                        ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
                        ReviewCommentArtifactRef = ".intent-cli/reviews/G25.comment.json",
                        ReviewRequestRef = ".intent-cli/reviews/G25.request.json",
                        ReviewCommentBodyPath = ".intent-cli/reviews/G25.comment.md",
                        IssueTitle = "[G25] Run Supervise Command",
                        Goal = "Supervise retryable run interruptions.",
                        TargetPart = "cli run supervise command",
                        TargetRepo = "submodules/intent-system",
                        TargetPath = ".",
                        InScope = [],
                        OutOfScope = [],
                        AcceptanceCriteria = [],
                        DeterministicReviewChecks = [],
                        ExpectedEvidence = []
                    },
                    ArtifactPath = ".intent-cli/fix/G25.request.md"
                };
            };

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionWorkerEntry.Fix, session.WorkerEntry);
            Assert.Equal(RunSupervisionSessionStatus.Monitoring, session.Status);
            Assert.Equal(1, session.RetryCount);
            Assert.Null(session.NextRetryAt);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl")));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
                && exitCodeElement.GetInt32() == 1);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^2].Event);
            Assert.Equal("auto-resumed", runEvents[^1].Event);
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenDeadFixWorkerSessionOneFailureBeforeRetryBudget_BlocksWithoutLaunchingAnotherWorker()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog() + string.Join(
                Environment.NewLine,
                [
                    """{"ts":"2026-04-08T10:25:00Z","execution_unit":"G25","event":"retry-attempted","by":"intent-cli","reason":"Worker session 'pid:82625' for 'G25' exited with backend exit code 1."}""",
                    """{"ts":"2026-04-08T10:25:00Z","execution_unit":"G25","event":"auto-resumed","by":"intent-cli","reason":"run fix"}""",
                    """{"ts":"2026-04-08T10:27:00Z","execution_unit":"G25","event":"retry-attempted","by":"intent-cli","reason":"Worker session 'pid:83683' for 'G25' exited with backend exit code 1."}""",
                    """{"ts":"2026-04-08T10:27:00Z","execution_unit":"G25","event":"auto-resumed","by":"intent-cli","reason":"run fix"}"""
                ]) + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession(workerEntry: RunSupervisionWorkerEntry.Fix) with
            {
                RetryCount = 2,
                RetryBudget = 3
            }));
        WriteDeadFixDirectRunArtifacts(repoRoot, "pid:84643");
        File.WriteAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl"),
            string.Join(
                Environment.NewLine,
                new[]
                {
                    CreateProviderEvent("2026-04-08T10:24:00.0000000+00:00", "G25", "fix", "pid:82625", "session-metadata", new { model = "sonnet", transport = "sdk", command = "claude" }),
                    CreateProviderEvent("2026-04-08T10:24:01.0000000+00:00", "G25", "fix", "pid:82625", "provider-event", new { type = "backend-exit", exit_code = 1 }),
                    CreateProviderEvent("2026-04-08T10:26:00.0000000+00:00", "G25", "fix", "pid:83683", "session-metadata", new { model = "sonnet", transport = "sdk", command = "claude" }),
                    CreateProviderEvent("2026-04-08T10:26:01.0000000+00:00", "G25", "fix", "pid:83683", "provider-event", new { type = "backend-exit", exit_code = 1 }),
                    CreateProviderEvent("2026-04-08T10:28:00.0000000+00:00", "G25", "fix", "pid:84643", "session-metadata", new { model = "sonnet", transport = "sdk", command = "claude" }),
                    CreateProviderEvent("2026-04-08T10:28:00.5000000+00:00", "G25", "fix", "pid:84643", "provider-event", "checked current review comment context before fix planning"),
                    CreateProviderEvent("2026-04-08T10:28:00.7500000+00:00", "G25", "fix", "pid:84643", "provider-event", "warn plugin manifest falling_back after state db discrepancy on slow path"),
                    CreateProviderEvent("2026-04-08T10:28:01.0000000+00:00", "G25", "fix", "pid:84643", "provider-event", new { type = "backend-exit", exit_code = 1 })
                }
                .Select(DirectRunProviderEventJsonl.SerializeLine)) + Environment.NewLine);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunFixExecutor = RunSuperviseCommand.RunFixExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.RunFixExecutor = (_, _) =>
                throw new InvalidOperationException("unexpected extra fix worker launch");

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G25");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("Fix retry budget exhausted", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("after 3 failed attempts", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("pid:82625", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("pid:83683", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("pid:84643", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("Representative latest output", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("warn plugin manifest", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(3, session.RetryCount);
            Assert.Contains("Fix retry budget exhausted", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^3].Event);
            Assert.Equal("retry-exhausted", runEvents[^2].Event);
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("pid:84643", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent =>
                string.Equals(runEvent.Event, "auto-resumed", StringComparison.Ordinal)
                && runEvent.Ts == DateTimeOffset.Parse("2026-04-08T10:30:00Z"));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenRetryExhaustionWithOlderFixLoopHistory_ExcludesStaleSessionsFromSummary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog() + string.Join(
                Environment.NewLine,
                [
                    """{"ts":"2026-04-08T10:24:00Z","execution_unit":"G25","event":"retry-attempted","by":"intent-cli","reason":"Worker session 'pid:82625' for 'G25' exited with backend exit code 1."}""",
                    """{"ts":"2026-04-08T10:24:00Z","execution_unit":"G25","event":"auto-resumed","by":"intent-cli","reason":"run fix"}""",
                    """{"ts":"2026-04-08T10:25:00Z","execution_unit":"G25","event":"retry-attempted","by":"intent-cli","reason":"Worker session 'pid:83683' for 'G25' exited with backend exit code 1."}""",
                    """{"ts":"2026-04-08T10:25:00Z","execution_unit":"G25","event":"auto-resumed","by":"intent-cli","reason":"run fix"}""",
                    """{"ts":"2026-04-08T10:28:00Z","execution_unit":"G25","event":"retry-attempted","by":"intent-cli","reason":"Worker session 'pid:90001' for 'G25' exited with backend exit code 1."}""",
                    """{"ts":"2026-04-08T10:28:00Z","execution_unit":"G25","event":"auto-resumed","by":"intent-cli","reason":"run fix"}""",
                    """{"ts":"2026-04-08T10:29:00Z","execution_unit":"G25","event":"retry-attempted","by":"intent-cli","reason":"Worker session 'pid:90002' for 'G25' exited with backend exit code 1."}""",
                    """{"ts":"2026-04-08T10:29:00Z","execution_unit":"G25","event":"auto-resumed","by":"intent-cli","reason":"run fix"}"""
                ]) + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateMonitoringSession(workerEntry: RunSupervisionWorkerEntry.Fix) with
            {
                RetryCount = 2,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-08T10:27:30Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-08T10:29:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-08T10:29:00Z")
            }));
        WriteDeadFixDirectRunArtifacts(repoRoot, "pid:90003");
        File.WriteAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl"),
            string.Join(
                Environment.NewLine,
                new[]
                {
                    CreateProviderEvent("2026-04-08T10:24:00.0000000+00:00", "G25", "fix", "pid:82625", "session-metadata", new { model = "sonnet", transport = "sdk", command = "claude" }),
                    CreateProviderEvent("2026-04-08T10:24:01.0000000+00:00", "G25", "fix", "pid:82625", "provider-event", new { type = "backend-exit", exit_code = 1 }),
                    CreateProviderEvent("2026-04-08T10:25:00.0000000+00:00", "G25", "fix", "pid:83683", "session-metadata", new { model = "sonnet", transport = "sdk", command = "claude" }),
                    CreateProviderEvent("2026-04-08T10:25:01.0000000+00:00", "G25", "fix", "pid:83683", "provider-event", new { type = "backend-exit", exit_code = 1 }),
                    CreateProviderEvent("2026-04-08T10:28:00.0000000+00:00", "G25", "fix", "pid:90001", "session-metadata", new { model = "sonnet", transport = "sdk", command = "claude" }),
                    CreateProviderEvent("2026-04-08T10:28:01.0000000+00:00", "G25", "fix", "pid:90001", "provider-event", new { type = "backend-exit", exit_code = 1 }),
                    CreateProviderEvent("2026-04-08T10:29:00.0000000+00:00", "G25", "fix", "pid:90002", "session-metadata", new { model = "sonnet", transport = "sdk", command = "claude" }),
                    CreateProviderEvent("2026-04-08T10:29:01.0000000+00:00", "G25", "fix", "pid:90002", "provider-event", new { type = "backend-exit", exit_code = 1 }),
                    CreateProviderEvent("2026-04-08T10:30:00.0000000+00:00", "G25", "fix", "pid:90003", "session-metadata", new { model = "sonnet", transport = "sdk", command = "claude" }),
                    CreateProviderEvent("2026-04-08T10:30:00.5000000+00:00", "G25", "fix", "pid:90003", "provider-event", "checked current review comment context before fix planning"),
                    CreateProviderEvent("2026-04-08T10:30:00.7500000+00:00", "G25", "fix", "pid:90003", "provider-event", "warn plugin manifest falling_back after state db discrepancy on slow path"),
                    CreateProviderEvent("2026-04-08T10:30:01.0000000+00:00", "G25", "fix", "pid:90003", "provider-event", new { type = "backend-exit", exit_code = 1 })
                }
                .Select(DirectRunProviderEventJsonl.SerializeLine)) + Environment.NewLine);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunFixExecutor = RunSuperviseCommand.RunFixExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:31:00Z");
            RunSuperviseCommand.RunFixExecutor = (_, _) =>
                throw new InvalidOperationException("unexpected extra fix worker launch");

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G25");
            Assert.Contains("pid:90001", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("pid:90002", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("pid:90003", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("pid:82625", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("pid:83683", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("10:24", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("10:25", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("Worker session 'pid:90001'", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("Worker session 'pid:90002'", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("Worker session 'pid:90003'", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("Worker session 'pid:82625'", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("Worker session 'pid:83683'", selectedItem.BlockedBy[0], StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void Execute_GivenDeadFixWorkerSessionWithMeaningfulWorktreeDiff_BlocksAsNonRetryableFailure()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        WriteDeadFixDirectRunArtifacts(repoRoot, "pid:999999");
        File.AppendAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl"),
            string.Join(
                Environment.NewLine,
                CreateMeaningfulFixWorktreeProgressEvents("G25", "pid:999999")
                    .Select(DirectRunProviderEventJsonl.SerializeLine)) + Environment.NewLine);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M src/ToyCalc/Calculator.cs
                 M src/ToyCalc/CommandLine.cs
                 M tests/ToyCalc.Tests/CalculatorTests.cs
                """);

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G25");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("meaningful execution-unit worktree changes", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("src/ToyCalc/Calculator.cs", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Contains("meaningful execution-unit worktree changes", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("meaningful execution-unit worktree changes", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void Execute_GivenDeadFixWorkerSessionWithOnlyOutOfScopeRuntimeArtifactDiff_BlocksWithoutConsumingRetryBudget()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        WriteDeadFixDirectRunArtifacts(repoRoot, "pid:999999");
        File.AppendAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl"),
            string.Join(
                Environment.NewLine,
                CreateRuntimeArtifactOnlyFixProgressEvents("G25", "pid:999999")
                    .Select(DirectRunProviderEventJsonl.SerializeLine)) + Environment.NewLine);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M .intent-cli/intake/toy-calc.concept.yaml
                 M .intent-cli/intake/toy-calc.execution.md
                 M .intent-cli/intake/toy-calc.patch.md
                """);

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G25");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("out-of-scope runtime-artifact drift", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/toy-calc.concept.yaml", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("Fix retry budget exhausted", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(0, session.RetryCount);
            Assert.False(session.RequiresPostFixWorktreeProgressDecision);
            Assert.Contains("out-of-scope runtime-artifact drift", session.LastInterruptionReason, StringComparison.Ordinal);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G25.result.json")));
            Assert.Equal("failed", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("out-of-scope runtime-artifact drift", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void Execute_GivenDeadFixWorkerSessionWithToyCalcReplayShapeAndOnlyRuntimeArtifactDiff_BlocksWithoutConsumingRetryBudget()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        WriteDeadFixDirectRunArtifacts(repoRoot, "pid:999999");
        File.AppendAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G25.provider.jsonl"),
            string.Join(
                Environment.NewLine,
                CreateToyCalcReplayRuntimeArtifactOnlyFixProgressEvents("G25", "pid:999999")
                    .Select(DirectRunProviderEventJsonl.SerializeLine)) + Environment.NewLine);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M .intent-cli/intake/toy-calc.concept.yaml
                 M .intent-cli/intake/toy-calc.execution.md
                 M .intent-cli/intake/toy-calc.patch.md
                """);

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Auto-resumed: yes", writer.ToString(), StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G25");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("out-of-scope runtime-artifact drift", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/toy-calc.concept.yaml", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(0, session.RetryCount);
            Assert.Contains("out-of-scope runtime-artifact drift", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("out-of-scope runtime-artifact drift", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void Execute_GivenNonRetryableAutoResumeFailure_BlocksSelectedItemAndAppendsTerminalEvents()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G25"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateFixingRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G25.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G25.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(CreateRetryScheduledSession(workerEntry: RunSupervisionWorkerEntry.Fix, commentRef: "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1")));
        using var writer = new StringWriter();
        var originalTimestampFactory = RunSuperviseCommand.TimestampFactory;
        var originalRunFixExecutor = RunSuperviseCommand.RunFixExecutor;

        try
        {
            RunSuperviseCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T10:30:00Z");
            RunSuperviseCommand.RunFixExecutor = (_, _) =>
                throw new InvalidOperationException("review comment artifact was not found");

            var exitCode = RunSuperviseCommand.Execute(CreateContext(repoRoot), ["G25"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Blocked transition applied: yes", writer.ToString(), StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G25");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("Non-retryable auto-resume failure", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var sessionPath = Path.Combine(repoRoot, ".intent-cli", "supervision", "G25.session.json");
            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionPath));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(1, session.RetryCount);
            Assert.Equal("blocked", session.QueueState);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^3].Event);
            Assert.Equal("retry-exhausted", runEvents[^2].Event);
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("review comment artifact was not found", runEvents[^1].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.TimestampFactory = originalTimestampFactory;
            RunSuperviseCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    private static CliContext CreateContext(
        string repoRoot,
        string supervisionArtifactRoot = ".intent-cli/supervision",
        int staleHeartbeatTimeoutMinutes = 15,
        int retryDelayMinutes = 5,
        int retryBudget = 3)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-system",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                },
                Roles = new RoleMappings
                {
                    Implement = "Claude",
                    Review = "Codex",
                    Interview = "Claude",
                    Clarify = "Codex"
                },
                Supervision = new SupervisionConfig
                {
                    ArtifactRoot = supervisionArtifactRoot,
                    StaleHeartbeatTimeoutMinutes = staleHeartbeatTimeoutMinutes,
                    RetryDelayMinutes = retryDelayMinutes,
                    RetryBudget = retryBudget
                }
            }
        };
    }

    private static QueueState CreateQueueState(QueueItemState selectedState)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T10:00:00Z"),
            Items =
            [
                CreateItem("G25", selectedState),
                CreateItem("G26", QueueItemState.Queued) with
                {
                    Title = "[G26] Unrelated queued item"
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Run Supervise Command",
            State = state,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            LinkedIssue = new LinkedIssue
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = 178,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/178"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "P1"
        };
    }

    private static RunSupervisionSession CreateMonitoringSession(
        RunSupervisionWorkerEntry workerEntry = RunSupervisionWorkerEntry.Implement)
    {
        return new RunSupervisionSession
        {
            ExecutionUnit = "G25",
            WorkerEntry = workerEntry,
            Status = RunSupervisionSessionStatus.Monitoring,
            QueueState = workerEntry == RunSupervisionWorkerEntry.Fix ? "fixing" : "active",
            WorktreePath = "/repo/.intent-cli/worktrees/G25",
            ChildRepoPath = "/repo/submodules/intent-system",
            Branch = "issue-178-g25",
            LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
            LinkedPr = workerEntry == RunSupervisionWorkerEntry.Fix
                ? "https://github.com/J-Tech-Japan/intent-system/pull/180"
                : null,
            CommentRef = workerEntry == RunSupervisionWorkerEntry.Fix
                ? "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"
                : null,
            HandoffArtifactRef = workerEntry == RunSupervisionWorkerEntry.Fix
                ? ".intent-cli/fix/G25.request.md"
                : ".intent-cli/implement/G25.request.md",
            RetryCount = 0,
            RetryBudget = 3,
            CreatedAt = DateTimeOffset.Parse("2026-04-08T09:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T10:00:00Z"),
            LastHeartbeatAt = DateTimeOffset.Parse("2026-04-08T10:00:00Z")
        };
    }

    private static RunSupervisionSession CreateRetryScheduledSession(
        RunSupervisionWorkerEntry workerEntry = RunSupervisionWorkerEntry.Implement,
        string? commentRef = null)
    {
        return new RunSupervisionSession
        {
            ExecutionUnit = "G25",
            WorkerEntry = workerEntry,
            Status = RunSupervisionSessionStatus.RetryScheduled,
            QueueState = workerEntry == RunSupervisionWorkerEntry.Fix ? "fixing" : "active",
            WorktreePath = "/repo/.intent-cli/worktrees/G25",
            ChildRepoPath = "/repo/submodules/intent-system",
            Branch = "issue-178-g25",
            LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/178",
            LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/180",
            CommentRef = commentRef,
            HandoffArtifactRef = workerEntry == RunSupervisionWorkerEntry.Fix
                ? ".intent-cli/fix/G25.request.md"
                : ".intent-cli/implement/G25.request.md",
            RetryCount = 0,
            RetryBudget = 3,
            CreatedAt = DateTimeOffset.Parse("2026-04-08T09:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T10:20:00Z"),
            LastHeartbeatAt = DateTimeOffset.Parse("2026-04-08T10:00:00Z"),
            NextRetryAt = DateTimeOffset.Parse("2026-04-08T10:25:00Z"),
            LastInterruptionReason = "Heartbeat expired after 15 minutes while supervising 'G25'."
        };
    }

    private static string CreatePacketYaml()
    {
        return """
        execution_unit: "G25"

        implementation_issue:
          issue_title: "[G25] Run Supervise Command"
          goal: "Supervise retryable run interruptions."
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run supervise command"
          dependencies: []

        review:
          review_context_path: ".intent-cli/issues/G25/review-context.md"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateActiveRunLog()
    {
        return """
        {"ts":"2026-04-08T09:50:00Z","execution_unit":"G25","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/178"}
        {"ts":"2026-04-08T10:00:00Z","execution_unit":"G25","event":"activated","by":"intent-cli"}
        """ + Environment.NewLine;
    }

    private static string CreateFixingRunLog()
    {
        return """
        {"ts":"2026-04-08T09:50:00Z","execution_unit":"G25","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/178"}
        {"ts":"2026-04-08T10:00:00Z","execution_unit":"G25","event":"activated","by":"intent-cli"}
        {"ts":"2026-04-08T10:10:00Z","execution_unit":"G25","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/180"}
        {"ts":"2026-04-08T10:15:00Z","execution_unit":"G25","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1","reason":"contract mismatch"}
        """ + Environment.NewLine;
    }

    private static DirectRunProviderEvent CreateProviderEvent(
        string timestamp,
        string executionUnit,
        string entryKind,
        string sessionId,
        string kind,
        object payload)
    {
        return new DirectRunProviderEvent
        {
            Timestamp = timestamp,
            ExecutionUnit = executionUnit,
            Provider = "Claude",
            EntryKind = entryKind,
            SessionId = sessionId,
            Kind = kind,
            Payload = JsonSerializer.SerializeToElement(payload)
        };
    }

    private static void WriteDeadFixDirectRunArtifacts(string repoRoot, string sessionId)
    {
        var requestArtifact = new DirectRunRequestArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = "G25",
            EntryKind = "fix",
            UpstreamRequestRef = ".intent-cli/fix/G25.request.md",
            Provider = "Claude",
            Model = "sonnet",
            Transport = "sdk",
            LaunchedAt = "2026-04-08T10:20:00.0000000+00:00",
            ProviderSessionId = sessionId,
            TransportSummary = "sdk transport"
        };
        var resultArtifact = new DirectRunResultArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = "G25",
            EntryKind = "fix",
            UpstreamRequestRef = ".intent-cli/fix/G25.request.md",
            Provider = "Claude",
            Model = "sonnet",
            SessionId = sessionId,
            RunStatus = "running",
            RawLogRef = ".intent-cli/runs/G25.provider.jsonl",
            PacketRef = ".intent-cli/issues/G25/packet.yaml",
            ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
            Worktree = new DirectRunWorktreeContext
            {
                Path = "/repo/.intent-cli/worktrees/G25"
            }
        };
        var providerEvents = new[]
        {
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.0000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "session-metadata",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    model = "sonnet",
                    transport = "sdk",
                    command = "claude"
                })
            })
        };

        var runsPath = Path.Combine(repoRoot, ".intent-cli", "runs");
        Directory.CreateDirectory(runsPath);
        File.WriteAllText(Path.Combine(runsPath, "G25.request.json"), DirectRunRequestArtifactJson.Serialize(requestArtifact));
        File.WriteAllText(Path.Combine(runsPath, "G25.result.json"), DirectRunResultArtifactJson.Serialize(resultArtifact));
        File.WriteAllText(Path.Combine(runsPath, "G25.provider.jsonl"), string.Join(Environment.NewLine, providerEvents) + Environment.NewLine);
    }

    private static void WriteStartupOnlyDeadFixDirectRunArtifacts(string repoRoot, string sessionId, bool includeBackendExit = true)
    {
        var requestArtifact = new DirectRunRequestArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = "G25",
            EntryKind = "fix",
            UpstreamRequestRef = ".intent-cli/fix/G25.request.md",
            Provider = "Claude",
            Model = "sonnet",
            Transport = "sdk",
            LaunchedAt = "2026-04-08T10:20:00.0000000+00:00",
            ProviderSessionId = sessionId,
            TransportSummary = "sdk transport"
        };
        var resultArtifact = new DirectRunResultArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = "G25",
            EntryKind = "fix",
            UpstreamRequestRef = ".intent-cli/fix/G25.request.md",
            Provider = "Claude",
            Model = "sonnet",
            SessionId = sessionId,
            RunStatus = "running",
            RawLogRef = ".intent-cli/runs/G25.provider.jsonl",
            PacketRef = ".intent-cli/issues/G25/packet.yaml",
            ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
            Worktree = new DirectRunWorktreeContext
            {
                Path = "/repo/.intent-cli/worktrees/G25"
            }
        };
        var providerEvents = new[]
        {
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.0000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "session-metadata",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    model = "sonnet",
                    transport = "sdk",
                    command = "claude"
                })
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.2000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("OpenAI Codex v0.118.0 (research preview)")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.2500000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("--------")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.3000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("workdir: /repo/.intent-cli/worktrees/G25")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.3500000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("model: gpt-5.4")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.4000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("reasoning summaries: none")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.4500000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("session id: sess_123")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.5000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("user")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.6000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("provider: openai")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.6500000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("approval: never")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.7000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("sandbox: danger-full-access")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.7500000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("reasoning effort: high")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.8000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("Please diagnose the startup-only backend exit reproduction for issue #295.")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.9000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("warn state db discrepancy detected on slow path while reconcile_rollout started")
            }),
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.9500000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement(
                    "2026-04-08T10:20:00.9500000Z  WARN codex_core::shell_snapshot: Failed to delete shell snapshot at \"/tmp/snapshot\"")
            }),
        };
        if (includeBackendExit)
        {
            providerEvents =
            [
                .. providerEvents,
                DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-08T10:20:01.0000000+00:00",
                    ExecutionUnit = "G25",
                    Provider = "Claude",
                    EntryKind = "fix",
                    SessionId = sessionId,
                    Kind = "provider-event",
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                })
            ];
        }

        var runsPath = Path.Combine(repoRoot, ".intent-cli", "runs");
        Directory.CreateDirectory(runsPath);
        File.WriteAllText(Path.Combine(runsPath, "G25.request.json"), DirectRunRequestArtifactJson.Serialize(requestArtifact));
        File.WriteAllText(Path.Combine(runsPath, "G25.result.json"), DirectRunResultArtifactJson.Serialize(resultArtifact));
        File.WriteAllText(Path.Combine(runsPath, "G25.provider.jsonl"), string.Join(Environment.NewLine, providerEvents) + Environment.NewLine);
    }

    private static void WriteDeadImplementDirectRunArtifacts(string repoRoot, string sessionId, bool includeBackendExit = false)
    {
        var requestArtifact = new DirectRunRequestArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = "G25",
            EntryKind = "implement",
            UpstreamRequestRef = ".intent-cli/implement/G25.request.md",
            Provider = "Claude",
            Model = "sonnet",
            Transport = "sdk",
            LaunchedAt = "2026-04-08T10:20:00.0000000+00:00",
            ProviderSessionId = sessionId,
            TransportSummary = "sdk transport"
        };
        var resultArtifact = new DirectRunResultArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = "G25",
            EntryKind = "implement",
            UpstreamRequestRef = ".intent-cli/implement/G25.request.md",
            Provider = "Claude",
            Model = "sonnet",
            SessionId = sessionId,
            RunStatus = "running",
            RawLogRef = ".intent-cli/runs/G25.provider.jsonl",
            PacketRef = ".intent-cli/issues/G25/packet.yaml",
            ReviewContextRef = ".intent-cli/issues/G25/review-context.md",
            Worktree = new DirectRunWorktreeContext
            {
                Path = "/repo/.intent-cli/worktrees/G25"
            }
        };
        var providerEvents = new List<string>
        {
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.0000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "session-metadata",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    model = "sonnet",
                    transport = "sdk",
                    command = "claude"
                })
            })
        };
        if (includeBackendExit)
        {
            providerEvents.Add(DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:01.0000000+00:00",
                ExecutionUnit = "G25",
                Provider = "Claude",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }));
        }

        var runsPath = Path.Combine(repoRoot, ".intent-cli", "runs");
        Directory.CreateDirectory(runsPath);
        File.WriteAllText(Path.Combine(runsPath, "G25.request.json"), DirectRunRequestArtifactJson.Serialize(requestArtifact));
        File.WriteAllText(Path.Combine(runsPath, "G25.result.json"), DirectRunResultArtifactJson.Serialize(resultArtifact));
        File.WriteAllText(Path.Combine(runsPath, "G25.provider.jsonl"), string.Join(Environment.NewLine, providerEvents) + Environment.NewLine);
    }

    private static void WriteLiveFixDirectRunArtifacts(string repoRoot, string executionUnit, string sessionId)
    {
        var requestArtifact = new DirectRunRequestArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = executionUnit,
            EntryKind = "fix",
            UpstreamRequestRef = $".intent-cli/fix/{executionUnit}.request.md",
            Provider = "Claude",
            Model = "sonnet",
            Transport = "sdk",
            LaunchedAt = "2026-04-08T10:30:00.0000000+00:00",
            ProviderSessionId = sessionId,
            TransportSummary = "sdk transport"
        };
        var resultArtifact = new DirectRunResultArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = executionUnit,
            EntryKind = "fix",
            UpstreamRequestRef = $".intent-cli/fix/{executionUnit}.request.md",
            Provider = "Claude",
            Model = "sonnet",
            SessionId = sessionId,
            RunStatus = "running",
            RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
            PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
            ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
            Worktree = new DirectRunWorktreeContext
            {
                Path = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
            }
        };
        var providerLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", $"{executionUnit}.provider.jsonl");
        File.AppendAllText(
            providerLogPath,
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:30:00.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "session-metadata",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    model = "sonnet",
                    transport = "sdk",
                    command = "claude"
                })
            }) + Environment.NewLine);
        File.WriteAllText(Path.Combine(repoRoot, ".intent-cli", "runs", $"{executionUnit}.request.json"), DirectRunRequestArtifactJson.Serialize(requestArtifact));
        File.WriteAllText(Path.Combine(repoRoot, ".intent-cli", "runs", $"{executionUnit}.result.json"), DirectRunResultArtifactJson.Serialize(resultArtifact));
    }

    private static void WriteLiveImplementDirectRunArtifacts(string repoRoot, string executionUnit, string sessionId)
    {
        var requestArtifact = new DirectRunRequestArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = executionUnit,
            EntryKind = "implement",
            UpstreamRequestRef = $".intent-cli/implement/{executionUnit}.request.md",
            Provider = "Claude",
            Model = "sonnet",
            Transport = "sdk",
            LaunchedAt = "2026-04-08T10:30:00.0000000+00:00",
            ProviderSessionId = sessionId,
            TransportSummary = "sdk transport"
        };
        var resultArtifact = new DirectRunResultArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = executionUnit,
            EntryKind = "implement",
            UpstreamRequestRef = $".intent-cli/implement/{executionUnit}.request.md",
            Provider = "Claude",
            Model = "sonnet",
            SessionId = sessionId,
            RunStatus = "running",
            RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
            PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
            ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
            Worktree = new DirectRunWorktreeContext
            {
                Path = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
            }
        };
        var providerLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", $"{executionUnit}.provider.jsonl");
        File.AppendAllText(
            providerLogPath,
            DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:30:00.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "session-metadata",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    model = "sonnet",
                    transport = "sdk",
                    command = "claude"
                })
            }) + Environment.NewLine);
        File.WriteAllText(Path.Combine(repoRoot, ".intent-cli", "runs", $"{executionUnit}.request.json"), DirectRunRequestArtifactJson.Serialize(requestArtifact));
        File.WriteAllText(Path.Combine(repoRoot, ".intent-cli", "runs", $"{executionUnit}.result.json"), DirectRunResultArtifactJson.Serialize(resultArtifact));
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateMeaningfulFixWorktreeProgressEvents(string executionUnit, string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.0500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/G25.request.md' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'rg --files' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.2000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("git status --short")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:01.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateRuntimeArtifactOnlyFixProgressEvents(string executionUnit, string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'sed -n ''1,220p'' .intent-cli/fix/G25.request.md' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.2000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/README.md' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.3000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'sed -n ''1,220p'' src/ToyCalc/Calculator.cs' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.4000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'dotnet test' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:00.5000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-08T10:20:01.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateToyCalcReplayRuntimeArtifactOnlyFixProgressEvents(string executionUnit, string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2369770+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("## Execution Contract")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2370090+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("- Continue beyond initial repository inspection; do not stop after a single listing/read-only command.")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2427050+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2428840+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("/bin/zsh -lc \"pwd && rg --files -g '!node_modules*' -g '!dist*' -g '!build*' | sed -n '1,220p'\" in /repo/.intent-cli/worktrees/G25")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2429790+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement(" succeeded in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2431360+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("src/ToyCalc/Program.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2431740+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("src/ToyCalc/Calculator.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2432870+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("intents/toy-calc/clarifications/open.md")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2434640+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("tests/ToyCalc.Tests/CalculatorTests.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2463570+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement("2026-04-17T05:29:47.246315Z  WARN codex_core::plugins::manifest: ignoring interface.defaultPrompt: prompt must be at most 128 characters")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:30:36.3739630+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private sealed class FakeGitRunner(string statusOutput) : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Assert.Equal(["status", "--short", "--untracked-files=all"], arguments);
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = statusOutput,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-supervise-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
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
