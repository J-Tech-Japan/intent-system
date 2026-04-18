using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class RunFixCommandTests
{
    [Fact]
    public void Execute_GivenFixingItemWithInputs_GeneratesRepairHandoffArtifactAndNormalizedRunResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;
        var originalLauncherFactory = RunFixCommand.DirectRunLauncherFactory;

        var originalQueueState = File.ReadAllText(queueStatePath);

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:25:00Z");
            RunFixCommand.DirectRunLauncherFactory = () => new FakeDirectRunLauncher(
                "pid:8765",
                "Claude",
                "default",
                "stdio",
                "stdio transport launched via 'claude' in '/repo/.intent-cli/worktrees/G20' for provider 'Claude'.");

            var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Repair handoff artifact generated for G20", output, StringComparison.Ordinal);
            Assert.Contains("Implement role: Claude", output, StringComparison.Ordinal);
            Assert.Contains("Branch: issue-68-g20", output, StringComparison.Ordinal);
            Assert.Contains("Latest linked PR: https://github.com/J-Tech-Japan/intent-system/pull/69", output, StringComparison.Ordinal);
            Assert.Contains("Latest comment ref: https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2", output, StringComparison.Ordinal);
            Assert.Contains("Direct run request artifact: .intent-cli/runs/G20.request.json", output, StringComparison.Ordinal);
            Assert.Contains("Provider raw event log: .intent-cli/runs/G20.provider.jsonl", output, StringComparison.Ordinal);
            Assert.Contains("Normalized run result: .intent-cli/runs/G20.result.json", output, StringComparison.Ordinal);
            Assert.Contains("Direct provider: Claude", output, StringComparison.Ordinal);
            Assert.Contains("Direct model: default", output, StringComparison.Ordinal);
            Assert.Contains("Direct transport: stdio", output, StringComparison.Ordinal);
            Assert.Contains("Provider session: pid:8765", output, StringComparison.Ordinal);
            Assert.Contains("Run status: failed", output, StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "fix", "G20.request.md");
            Assert.True(File.Exists(artifactPath));
            var markdown = File.ReadAllText(artifactPath);
            Assert.Contains("- packet_ref: .intent-cli/issues/G20/packet.yaml", markdown, StringComparison.Ordinal);
            Assert.Contains("- review_context_ref: .intent-cli/issues/G20/review-context.md", markdown, StringComparison.Ordinal);
            Assert.Contains("- review_comment_artifact_ref: .intent-cli/reviews/G20.comment.json", markdown, StringComparison.Ordinal);
            Assert.Contains("- review_request_ref: .intent-cli/reviews/G20.request.json", markdown, StringComparison.Ordinal);
            Assert.Contains("- latest_linked_pr: https://github.com/J-Tech-Japan/intent-system/pull/69", markdown, StringComparison.Ordinal);
            Assert.Contains("- latest_comment_ref: https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2", markdown, StringComparison.Ordinal);

            var directRunArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.request.json");
            Assert.True(File.Exists(directRunArtifactPath));
            var directRunArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(directRunArtifactPath));
            Assert.Equal("G20", directRunArtifact.ExecutionUnit);
            Assert.Equal("fix", directRunArtifact.EntryKind);
            Assert.Equal(".intent-cli/fix/G20.request.md", directRunArtifact.UpstreamRequestRef);
            Assert.Equal("Claude", directRunArtifact.Provider);
            Assert.Equal("default", directRunArtifact.Model);
            Assert.Equal("stdio", directRunArtifact.Transport);
            Assert.Equal("pid:8765", directRunArtifact.ProviderSessionId);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json");
            Assert.True(File.Exists(resultArtifactPath));
            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal("G20", resultArtifact.ExecutionUnit);
            Assert.Equal("fix", resultArtifact.EntryKind);
            Assert.Equal(".intent-cli/fix/G20.request.md", resultArtifact.UpstreamRequestRef);
            Assert.Equal("Claude", resultArtifact.Provider);
            Assert.Equal("default", resultArtifact.Model);
            Assert.Equal("pid:8765", resultArtifact.SessionId);
            Assert.Equal("failed", resultArtifact.RunStatus);
            Assert.Equal(".intent-cli/runs/G20.provider.jsonl", resultArtifact.RawLogRef);
            Assert.Equal(".intent-cli/issues/G20/packet.yaml", resultArtifact.PacketRef);
            Assert.Equal(".intent-cli/issues/G20/review-context.md", resultArtifact.ReviewContextRef);
            Assert.Equal("J-Tech-Japan/intent-system", resultArtifact.LinkedIssue?.Repo);
            Assert.Equal(68, resultArtifact.LinkedIssue?.Number);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/68", resultArtifact.LinkedIssue?.Url);
            Assert.Equal("J-Tech-Japan/intent-system", resultArtifact.LinkedPr?.Repo);
            Assert.Equal(69, resultArtifact.LinkedPr?.Number);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/69", resultArtifact.LinkedPr?.Url);
            Assert.EndsWith("/.intent-cli/worktrees/G20", resultArtifact.Worktree.Path, StringComparison.Ordinal);

            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            var lifecycleEvent = Assert.Single(runEvents, runEvent => runEvent.Event == "provider-lifecycle");
            Assert.Equal("G20", lifecycleEvent.ExecutionUnit);
            Assert.Equal("intent-cli", lifecycleEvent.By);
            Assert.Equal("fix", lifecycleEvent.EntryKind);
            Assert.Equal("Claude", lifecycleEvent.Provider);
            Assert.Equal("default", lifecycleEvent.Model);
            Assert.Equal("pid:8765", lifecycleEvent.SessionId);
            Assert.Equal("running", lifecycleEvent.RunStatus);
            Assert.Equal(".intent-cli/runs/G20.provider.jsonl", lifecycleEvent.RawLogRef);
            Assert.Equal(".intent-cli/runs/G20.result.json", lifecycleEvent.ResultRef);
            Assert.Equal(".intent-cli/issues/G20/packet.yaml", lifecycleEvent.PacketRef);
            Assert.Equal(".intent-cli/issues/G20/review-context.md", lifecycleEvent.ReviewContextRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/68", lifecycleEvent.LinkedIssue);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/69", lifecycleEvent.LinkedPr);
            Assert.EndsWith("/.intent-cli/worktrees/G20", lifecycleEvent.WorktreePath, StringComparison.Ordinal);
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
            RunFixCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenStaleWorktreeRuntimeArtifacts_SyncsCurrentIssueArtifactsAndRemovesSupersededRunResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var worktreePath = tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "implementation.md"),
            "# Current implementation");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "github-body.md"),
            "# Current github body");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "worktrees", "G20", ".intent-cli", "issues", "OLD-01", "packet.yaml"),
            "stale-packet");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "worktrees", "G20", ".intent-cli", "run.result.json"),
            RunRootResultArtifactJson.Serialize(new RunRootResultArtifact
            {
                SchemaVersion = "1",
                StopReason = "deterministic-contract-gap",
                TouchedExecutionUnits = ["OLD-01"],
                ReusedChildCommandRefs = [],
                ExecutionUnit = "OLD-01",
                Detail = "stale worktree result"
            }));
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;
        var originalLauncherFactory = RunFixCommand.DirectRunLauncherFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:25:00Z");
            RunFixCommand.DirectRunLauncherFactory = () => new FakeDirectRunLauncher(
                "pid:8765",
                "Claude",
                "default",
                "stdio",
                "stdio transport launched via 'claude' in '/repo/.intent-cli/worktrees/G20' for provider 'Claude'.");

            var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(worktreePath, ".intent-cli", "issues", "G20", "packet.yaml")));
            Assert.True(File.Exists(Path.Combine(worktreePath, ".intent-cli", "issues", "G20", "review-context.md")));
            Assert.True(File.Exists(Path.Combine(worktreePath, ".intent-cli", "issues", "G20", "implementation.md")));
            Assert.True(File.Exists(Path.Combine(worktreePath, ".intent-cli", "issues", "G20", "github-body.md")));
            Assert.Equal(
                CreatePacketYaml(),
                File.ReadAllText(Path.Combine(worktreePath, ".intent-cli", "issues", "G20", "packet.yaml")));
            Assert.Equal(
                CreateReviewContextMarkdown(),
                File.ReadAllText(Path.Combine(worktreePath, ".intent-cli", "issues", "G20", "review-context.md")));
            Assert.False(File.Exists(Path.Combine(worktreePath, ".intent-cli", "run.result.json")));
            Assert.True(File.Exists(Path.Combine(worktreePath, ".intent-cli", "issues", "OLD-01", "packet.yaml")));
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
            RunFixCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenInspectionOnlyBackendExitFailure_AppendsDeterministicContractGapEvent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;
        var originalLauncherFactory = RunFixCommand.DirectRunLauncherFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-16T00:17:00Z");
            RunFixCommand.DirectRunLauncherFactory = () => new InspectionOnlyFailureDirectRunLauncher();

            var context = CreateContext(repoRoot) with
            {
                Config = CreateContext(repoRoot).Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json")));
            Assert.Equal("failed", resultArtifact.RunStatus);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl")));
            Assert.Contains(providerEvents, providerEvent =>
                string.Equals(providerEvent.SessionId, "pid:4321", StringComparison.Ordinal)
                && providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("stop_reason", out var stopReasonElement)
                && string.Equals(stopReasonElement.GetString(), "deterministic-contract-gap", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("reason", out var reasonElement)
                && string.Equals(reasonElement.GetString(), "fix-session-ended-after-initial-inspection", StringComparison.Ordinal));
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
            RunFixCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenFollowUpWorkAfterInitialInspection_DoesNotAppendInspectionOnlyContractGapEvent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;
        var originalLauncherFactory = RunFixCommand.DirectRunLauncherFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-16T00:17:00Z");
            RunFixCommand.DirectRunLauncherFactory = () => new FollowUpWorkFailureDirectRunLauncher();

            var context = CreateContext(repoRoot) with
            {
                Config = CreateContext(repoRoot).Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json")));
            Assert.Equal("failed", resultArtifact.RunStatus);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl")));
            Assert.DoesNotContain(providerEvents, providerEvent =>
                string.Equals(providerEvent.SessionId, "pid:5321", StringComparison.Ordinal)
                && providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("reason", out var reasonElement)
                && string.Equals(reasonElement.GetString(), "fix-session-ended-after-initial-inspection", StringComparison.Ordinal));
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
            RunFixCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenRuntimeOnlyTargetPart_ReturnsExitCodeOneWithoutWritingRepairArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml(targetPart: ".intent-cli/intake"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();
        var originalLauncherFactory = RunFixCommand.DirectRunLauncherFactory;

        try
        {
            RunFixCommand.DirectRunLauncherFactory = () => throw new InvalidOperationException("launcher should not be called");

            var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("host runtime-only '.intent-cli/**' content", writer.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "fix", "G20.request.md")));
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runs", "G20.request.json")));
        }
        finally
        {
            RunFixCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    private sealed class FakeDirectRunLauncher(
        string providerSessionId,
        string provider,
        string model,
        string transport,
        string transportSummary,
        string expectedCommand = "claude",
        IReadOnlyList<string>? expectedArgsTemplate = null) : IDirectRunLauncher
    {
        public DirectRunLaunchResult Launch(
            string executionUnit,
            string entryKind,
            string requestArtifactPath,
            string providerEventLogPath,
            string providerArg,
            string modelArg,
            string transportArg,
            string command,
            IReadOnlyList<string> argsTemplate,
            DateTimeOffset launchedAt,
            string workingDirectory,
            string absoluteRequestArtifactPath,
            string absoluteProviderEventLogPath)
        {
            Assert.Equal("G20", executionUnit);
            Assert.Equal("fix", entryKind);
            Assert.Equal(".intent-cli/runs/G20.request.json", requestArtifactPath);
            Assert.Equal(".intent-cli/runs/G20.provider.jsonl", providerEventLogPath);
            Assert.Equal(provider, providerArg);
            Assert.Equal(model, modelArg);
            Assert.Equal(transport, transportArg);
            Assert.Equal(expectedCommand, command);
            Assert.Equal(expectedArgsTemplate ?? ["--print", "--model", "{model}", "--output-format", "json", "{prompt}"], argsTemplate);
            Assert.EndsWith("/.intent-cli/worktrees/G20", workingDirectory, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/fix/G20.request.md", absoluteRequestArtifactPath, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/runs/G20.provider.jsonl", absoluteProviderEventLogPath, StringComparison.Ordinal);

            Directory.CreateDirectory(
                Path.GetDirectoryName(absoluteProviderEventLogPath)
                ?? throw new InvalidOperationException("Provider event log path did not contain a directory."));
            var providerEvents = string.Join(
                                     Environment.NewLine,
                                     new[]
                                     {
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = providerSessionId,
                                             Kind = "session-metadata",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                                             {
                                                 model = modelArg,
                                                 transport = transportArg,
                                                 command
                                             })
                                         }),
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.AddSeconds(1).ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = providerSessionId,
                                             Kind = "provider-event",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                                             {
                                                 type = "ready"
                                             })
                                         })
                                     }) + Environment.NewLine;
            File.WriteAllText(absoluteProviderEventLogPath, providerEvents);

            return new DirectRunLaunchResult
            {
                RequestArtifactPath = ".intent-cli/runs/G20.request.json",
                ProviderEventLogPath = ".intent-cli/runs/G20.provider.jsonl",
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = providerSessionId,
                TransportSummary = transportSummary
            };
        }
    }

    private sealed class InspectionOnlyFailureDirectRunLauncher : IDirectRunLauncher
    {
        public DirectRunLaunchResult Launch(
            string executionUnit,
            string entryKind,
            string requestArtifactPath,
            string providerEventLogPath,
            string providerArg,
            string modelArg,
            string transportArg,
            string command,
            IReadOnlyList<string> argsTemplate,
            DateTimeOffset launchedAt,
            string workingDirectory,
            string absoluteRequestArtifactPath,
            string absoluteProviderEventLogPath)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(absoluteProviderEventLogPath)
                ?? throw new InvalidOperationException("Provider event log path did not contain a directory."));
            var providerEvents = string.Join(
                                     Environment.NewLine,
                                     new[]
                                     {
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = "pid:4321",
                                             Kind = "session-metadata",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                                             {
                                                 model = modelArg,
                                                 transport = transportArg,
                                                 command
                                             })
                                         }),
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.AddSeconds(1).ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = "pid:4321",
                                             Kind = "provider-event",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                                                 "exec /bin/zsh -lc 'rg --files' succeeded in 0ms")
                                         }),
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.AddSeconds(2).ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = "pid:4321",
                                             Kind = "provider-event",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                                             {
                                                 type = "backend-exit",
                                                 exit_code = 1
                                             })
                                         })
                                     }) + Environment.NewLine;
            File.WriteAllText(absoluteProviderEventLogPath, providerEvents);

            return new DirectRunLaunchResult
            {
                RequestArtifactPath = requestArtifactPath,
                ProviderEventLogPath = providerEventLogPath,
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = "pid:4321",
                TransportSummary =
                    $"{transportArg} transport launched via '{command}' in '{workingDirectory}' for provider '{providerArg}'."
            };
        }
    }

    private sealed class FollowUpWorkFailureDirectRunLauncher : IDirectRunLauncher
    {
        public DirectRunLaunchResult Launch(
            string executionUnit,
            string entryKind,
            string requestArtifactPath,
            string providerEventLogPath,
            string providerArg,
            string modelArg,
            string transportArg,
            string command,
            IReadOnlyList<string> argsTemplate,
            DateTimeOffset launchedAt,
            string workingDirectory,
            string absoluteRequestArtifactPath,
            string absoluteProviderEventLogPath)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(absoluteProviderEventLogPath)
                ?? throw new InvalidOperationException("Provider event log path did not contain a directory."));
            var providerEvents = string.Join(
                                     Environment.NewLine,
                                     new[]
                                     {
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = "pid:5321",
                                             Kind = "session-metadata",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                                             {
                                                 model = modelArg,
                                                 transport = transportArg,
                                                 command
                                             })
                                         }),
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.AddSeconds(1).ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = "pid:5321",
                                             Kind = "provider-event",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                                                 "exec /bin/zsh -lc 'rg --files' succeeded in 0ms")
                                         }),
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.AddSeconds(2).ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = "pid:5321",
                                             Kind = "provider-event",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                                                 "exec /bin/zsh -lc 'sed -n ''1,120p'' src/Program.cs' succeeded in 0ms")
                                         }),
                                         DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                                         {
                                             Timestamp = launchedAt.AddSeconds(3).ToString("O"),
                                             ExecutionUnit = executionUnit,
                                             Provider = providerArg,
                                             EntryKind = entryKind,
                                             SessionId = "pid:5321",
                                             Kind = "provider-event",
                                             Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                                             {
                                                 type = "backend-exit",
                                                 exit_code = 1
                                             })
                                         })
                                     }) + Environment.NewLine;
            File.WriteAllText(absoluteProviderEventLogPath, providerEvents);

            return new DirectRunLaunchResult
            {
                RequestArtifactPath = requestArtifactPath,
                ProviderEventLogPath = providerEventLogPath,
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = "pid:5321",
                TransportSummary =
                    $"{transportArg} transport launched via '{command}' in '{workingDirectory}' for provider '{providerArg}'."
            };
        }
    }

    [Fact]
    public void Execute_GivenCodexCommandPathWithoutModelOverride_UsesRunnableCodexDefaultModel()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;
        var originalLauncherFactory = RunFixCommand.DirectRunLauncherFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:25:00Z");
            RunFixCommand.DirectRunLauncherFactory = () => new FakeDirectRunLauncher(
                "pid:8765",
                "Codex",
                CliRuntimeContracts.DefaultCodexDirectRunModel,
                "stdio",
                "stdio transport launched via '/opt/homebrew/bin/codex' in '/repo/.intent-cli/worktrees/G20' for provider 'Codex'.",
                "/opt/homebrew/bin/codex",
                ["exec", "--model", "{model}", "{prompt}"]);

            var context = CreateContext(repoRoot) with
            {
                Config = CreateContext(repoRoot).Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    },
                    DirectRun = new DirectRunConfig
                    {
                        Command = "/opt/homebrew/bin/codex"
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains($"Direct model: {CliRuntimeContracts.DefaultCodexDirectRunModel}", writer.ToString(), StringComparison.Ordinal);

            var directRunArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G20.request.json")));
            Assert.Equal(CliRuntimeContracts.DefaultCodexDirectRunModel, directRunArtifact.Model);
            Assert.Equal("Codex", directRunArtifact.Provider);
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
            RunFixCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public async Task Execute_GivenWrapperCodexCommand_AppendsTerminalBackendExitAndUpdatesResultArtifact()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        var providerBinaryPath = tempDirectory.CreateExecutableFile(
            "bin/codex",
            """
            #!/bin/sh
            sleep 1
            exit 1
            """);
        var wrapperPath = tempDirectory.CreateExecutableFile(
            "bin/codex-isolated",
            $$"""
            #!/bin/zsh
            export CODEX_HOME={{tempDirectory.GetPath(".codex-direct-backend")}}
            exec {{providerBinaryPath}} "$@"
            """);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-16T00:17:00Z");

            var context = CreateContext(repoRoot) with
            {
                Config = CreateContext(repoRoot).Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    },
                    DirectRun = new DirectRunConfig
                    {
                        Command = wrapperPath
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json");
            var providerEventLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl");
            var initialArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));

            await WaitForConditionAsync(
                () =>
                {
                    if (!File.Exists(providerEventLogPath))
                    {
                        return false;
                    }

                    var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                    var terminalEventExists = providerEvents.Any(providerEvent =>
                        string.Equals(providerEvent.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                        && providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                        && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                        && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
                    if (!terminalEventExists)
                    {
                        return false;
                    }

                    var updatedArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
                    return string.Equals(updatedArtifact.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                        && string.Equals(updatedArtifact.RunStatus, "failed", StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10));

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            var backendExitEvent = Assert.Single(providerEvents, providerEvent =>
                string.Equals(providerEvent.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                && providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
            Assert.Equal(1, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal(initialArtifact.SessionId, resultArtifact.SessionId);
            Assert.Equal("failed", resultArtifact.RunStatus);
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public async Task Execute_GivenWrapperCodexCommandWithPartialProviderOutput_AppendsTerminalBackendExitAndUpdatesResultArtifact()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        var providerBinaryPath = tempDirectory.CreateExecutableFile(
            "bin/codex",
            """
            #!/bin/sh
            printf '%s\n' '{"type":"ready"}'
            printf '%s\n' '{"type":"error","message":"AuthRequired(No access token was provided)"}' 1>&2
            exit 1
            """);
        var wrapperPath = tempDirectory.CreateExecutableFile(
            "bin/codex-isolated",
            $$"""
            #!/bin/zsh
            export CODEX_HOME={{tempDirectory.GetPath(".codex-direct-backend")}}
            exec {{providerBinaryPath}} "$@"
            """);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-16T00:18:00Z");

            var context = CreateContext(repoRoot) with
            {
                Config = CreateContext(repoRoot).Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    },
                    DirectRun = new DirectRunConfig
                    {
                        Command = wrapperPath
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json");
            var providerEventLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl");
            var initialArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));

            await WaitForConditionAsync(
                () =>
                {
                    if (!File.Exists(providerEventLogPath) || !File.Exists(resultArtifactPath))
                    {
                        return false;
                    }

                    var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                    var sawAuthRequired = providerEvents.Any(providerEvent =>
                        string.Equals(providerEvent.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                        && providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                        && providerEvent.Payload.TryGetProperty("message", out var messageElement)
                        && string.Equals(
                            messageElement.GetString(),
                            "AuthRequired(No access token was provided)",
                            StringComparison.Ordinal));
                    var terminalEventExists = providerEvents.Any(providerEvent =>
                        string.Equals(providerEvent.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                        && providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                        && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                        && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
                    if (!sawAuthRequired || !terminalEventExists)
                    {
                        return false;
                    }

                    var updatedArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
                    return string.Equals(updatedArtifact.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                        && string.Equals(updatedArtifact.RunStatus, "failed", StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10));

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            Assert.Contains(providerEvents, providerEvent =>
                string.Equals(providerEvent.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                && providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "ready", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                string.Equals(providerEvent.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                && providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("message", out var messageElement)
                && string.Equals(
                    messageElement.GetString(),
                    "AuthRequired(No access token was provided)",
                    StringComparison.Ordinal));
            var backendExitEvent = Assert.Single(providerEvents, providerEvent =>
                string.Equals(providerEvent.SessionId, initialArtifact.SessionId, StringComparison.Ordinal)
                && providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
            Assert.Equal(1, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal(initialArtifact.SessionId, resultArtifact.SessionId);
            Assert.Equal("failed", resultArtifact.RunStatus);
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public async Task Execute_GivenWrapperCodexFixCommandWithDeepProgress_FinalizesResultInsteadOfLeavingRunning()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        var providerBinaryPath = tempDirectory.CreateExecutableFile(
            "bin/codex",
            """
            #!/bin/sh
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "sed -n '\''1,220p'\'' '\''/repo/.intent-cli/fix/G20.request.md'\''"'
            printf '%s\n' ' succeeded in 0ms:'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "pwd && rg --files . | sed -n '\''1,200p'\''"'
            printf '%s\n' ' succeeded in 0ms:'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "sed -n '\''1,220p'\'' '\''intents/toy-calc/specs/01-cli-surface.md'\''"'
            printf '%s\n' ' exited 1 in 0ms:'
            printf '%s\n' 'sed: intents/toy-calc/specs/01-cli-surface.md: No such file or directory'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "sed -n '\''1,220p'\'' '\''src/ToyCalc/Program.cs'\''"'
            printf '%s\n' ' succeeded in 0ms:'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "sed -n '\''1,220p'\'' '\''tests/ToyCalc.Tests/CalculatorTests.cs'\''"'
            printf '%s\n' ' succeeded in 0ms:'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "dotnet test"'
            printf '%s\n' ' succeeded in 0ms:'
            sleep 12
            exit 1
            """);
        var wrapperPath = tempDirectory.CreateExecutableFile(
            "bin/codex-isolated",
            $$"""
            #!/bin/zsh
            export CODEX_HOME={{tempDirectory.GetPath(".codex-direct-backend")}}
            exec {{providerBinaryPath}} "$@"
            """);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-17T07:30:00Z");

            var context = CreateContext(repoRoot) with
            {
                Config = CreateContext(repoRoot).Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    },
                    DirectRun = new DirectRunConfig
                    {
                        Command = wrapperPath
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json");
            var providerEventLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl");

            await WaitForConditionAsync(
                () =>
                {
                    if (!File.Exists(providerEventLogPath) || !File.Exists(resultArtifactPath))
                    {
                        return false;
                    }

                    var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                    var hasTerminalFailureEvidence = providerEvents.Any(providerEvent =>
                        providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                        && ((providerEvent.Payload.TryGetProperty("type", out var typeElement)
                                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal))
                            || (providerEvent.Payload.TryGetProperty("type", out var contractGapTypeElement)
                                && string.Equals(contractGapTypeElement.GetString(), "contract-gap", StringComparison.Ordinal))));
                    if (!hasTerminalFailureEvidence)
                    {
                        return false;
                    }

                    var updatedArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
                    return string.Equals(updatedArtifact.RunStatus, "failed", StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(25));

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal("failed", resultArtifact.RunStatus);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && providerEvent.Payload.GetString()!.Contains("src/ToyCalc/Program.cs", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && providerEvent.Payload.GetString()!.Contains("tests/ToyCalc.Tests/CalculatorTests.cs", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && providerEvent.Payload.GetString()!.Contains("dotnet test", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public async Task Execute_GivenWrapperCodexFixCommandWithStartupWarningOnlyAndDeadSession_FinalizesResultInsteadOfLeavingRunning()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        var providerBinaryPath = tempDirectory.CreateExecutableFile(
            "bin/codex",
            """
            #!/bin/sh
            printf '%s\n' '2026-04-17T08:10:00.000000Z  WARN codex_core::plugins::manifest: ignoring interface.defaultPrompt: maximum of 3 prompts is supported'
            sleep 12
            exit 1
            """);
        var wrapperPath = tempDirectory.CreateExecutableFile(
            "bin/codex-isolated",
            $$"""
            #!/bin/zsh
            export CODEX_HOME={{tempDirectory.GetPath(".codex-direct-backend")}}
            exec {{providerBinaryPath}} "$@"
            """);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-17T08:10:00Z");

            var context = CreateContext(repoRoot) with
            {
                Config = CreateContext(repoRoot).Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    },
                    DirectRun = new DirectRunConfig
                    {
                        Command = wrapperPath
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json");
            var providerEventLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl");
            await WaitForConditionAsync(
                () =>
                {
                    if (!File.Exists(providerEventLogPath) || !File.Exists(resultArtifactPath))
                    {
                        return false;
                    }

                    var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                    var hasWarning = providerEvents.Any(providerEvent =>
                        providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                        && providerEvent.Payload.GetString()!.Contains("WARN codex_core::plugins::manifest", StringComparison.Ordinal));
                    var hasBackendExit = providerEvents.Any(providerEvent =>
                        providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                        && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                        && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
                    if (!hasWarning || !hasBackendExit)
                    {
                        return false;
                    }

                    var updatedArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
                    return string.Equals(updatedArtifact.RunStatus, "failed", StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(25));

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal("failed", resultArtifact.RunStatus);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && providerEvent.Payload.GetString()!.Contains("WARN codex_core::plugins::manifest", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public async Task Execute_GivenWrapperCodexFixCommandWithDeepProgressAndZeroExit_MissingTerminalKeepsFailedResult()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        var providerBinaryPath = tempDirectory.CreateExecutableFile(
            "bin/codex",
            """
            #!/bin/sh
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "sed -n '\''1,220p'\'' '\''/repo/.intent-cli/fix/G20.request.md'\''"'
            printf '%s\n' ' succeeded in 0ms:'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "pwd && rg --files . | sed -n '\''1,200p'\''"'
            printf '%s\n' ' succeeded in 0ms:'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "sed -n '\''1,220p'\'' '\''intents/toy-calc/specs/01-cli-surface.md'\''"'
            printf '%s\n' ' exited 1 in 0ms:'
            printf '%s\n' 'sed: intents/toy-calc/specs/01-cli-surface.md: No such file or directory'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "sed -n '\''1,220p'\'' '\''src/ToyCalc/Program.cs'\''"'
            printf '%s\n' ' succeeded in 0ms:'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "sed -n '\''1,220p'\'' '\''tests/ToyCalc.Tests/CalculatorTests.cs'\''"'
            printf '%s\n' ' succeeded in 0ms:'
            printf '%s\n' 'exec'
            printf '%s\n' '/bin/zsh -lc "dotnet test"'
            printf '%s\n' ' succeeded in 0ms:'
            sleep 12
            exit 0
            """);
        var wrapperPath = tempDirectory.CreateExecutableFile(
            "bin/codex-isolated",
            $$"""
            #!/bin/zsh
            export CODEX_HOME={{tempDirectory.GetPath(".codex-direct-backend")}}
            exec {{providerBinaryPath}} "$@"
            """);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-17T08:20:00Z");

            var context = CreateContext(repoRoot) with
            {
                Config = CreateContext(repoRoot).Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    },
                    DirectRun = new DirectRunConfig
                    {
                        Command = wrapperPath
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json");
            var providerEventLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl");

            await WaitForConditionAsync(
                () =>
                {
                    if (!File.Exists(providerEventLogPath) || !File.Exists(resultArtifactPath))
                    {
                        return false;
                    }

                    var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                    var hasContractGap = providerEvents.Any(providerEvent =>
                        providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                        && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                        && string.Equals(typeElement.GetString(), "contract-gap", StringComparison.Ordinal));
                    var hasBackendExit = providerEvents.Any(providerEvent =>
                        providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                        && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                        && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
                    if (!hasContractGap || !hasBackendExit)
                    {
                        return false;
                    }

                    var updatedArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
                    return string.Equals(updatedArtifact.RunStatus, "failed", StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(25));

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal("failed", resultArtifact.RunStatus);
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public async Task Execute_GivenWrapperCodexFixCommand_PersistsBoundedRepoActivityForCurrentSession()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        var providerBinaryPath = tempDirectory.CreateExecutableFile(
            "bin/codex",
            """
            #!/bin/sh
            printf '%s\n' 'OpenAI Codex v0.118.0 (research preview)'
            printf '%s\n' '--------'
            printf '%s\n' "workdir: $PWD"
            printf '%s\n' 'user'
            /usr/bin/python3 -c 'import os,select,sys; fd = sys.stdin.fileno(); os.isatty(fd) or (print("tty-missing", flush=True), sys.exit(1)); readable, _, _ = select.select([sys.stdin], [], [], 0.2); data = os.read(fd, 1) if readable else None; data != b"" or (print("stdin-eof", flush=True), sys.exit(1)); print("pwd && rg --files .", flush=True); print("git status --short", flush=True); print("dotnet test", flush=True)'
            exit 0
            """);
        var wrapperPath = tempDirectory.CreateExecutableFile(
            "bin/codex-isolated",
            $$"""
            #!/bin/zsh
            export CODEX_HOME={{tempDirectory.GetPath(".codex-direct-backend")}}
            exec {{providerBinaryPath}} "$@"
            """);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-16T18:16:00Z");

            var baseContext = CreateContext(repoRoot);
            var context = baseContext with
            {
                Config = baseContext.Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    },
                    DirectRun = new DirectRunConfig
                    {
                        Command = wrapperPath
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json");
            var providerEventLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl");
            var initialArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));

            await WaitForConditionAsync(
                () =>
                {
                    if (!File.Exists(providerEventLogPath) || !File.Exists(resultArtifactPath))
                    {
                        return false;
                    }

                    var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                    var sawBoundedProgress = providerEvents.Any(providerEvent =>
                        providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                        && string.Equals(providerEvent.Payload.GetString(), "pwd && rg --files .", StringComparison.Ordinal));
                    if (!sawBoundedProgress)
                    {
                        return false;
                    }

                    var updatedArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
                    return string.Equals(updatedArtifact.SessionId, initialArtifact.SessionId, StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10));

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            Assert.DoesNotContain(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "stdin-eof", StringComparison.Ordinal));
            Assert.DoesNotContain(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "tty-missing", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "pwd && rg --files .", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "git status --short", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "dotnet test", StringComparison.Ordinal));

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal(initialArtifact.SessionId, resultArtifact.SessionId);
        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public async Task Execute_GivenWrapperCodexFixCommand_KeepsStandardInputOpenUntilPostInventoryPlanningAppears()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        var providerBinaryPath = tempDirectory.CreateExecutableFile(
            "bin/codex",
            """
            #!/bin/sh
            printf '%s\n' 'OpenAI Codex v0.118.0 (research preview)'
            printf '%s\n' '--------'
            printf '%s\n' "workdir: $PWD"
            printf '%s\n' 'user'
            /usr/bin/python3 -c 'import os,select,sys,time; fd = sys.stdin.fileno(); os.isatty(fd) or (print("tty-missing", flush=True), sys.exit(1)); readable, _, _ = select.select([sys.stdin], [], [], 0.2); data = os.read(fd, 1) if readable else None; data != b"" or (print("stdin-eof", flush=True), sys.exit(1)); print("pwd && rg --files .", flush=True); time.sleep(1.4); readable, _, _ = select.select([sys.stdin], [], [], 0.2); data = os.read(fd, 1) if readable else None; data != b"" or (print("stdin-eof-after-inventory", flush=True), sys.exit(1)); print("sed -n 1,160p .intent-cli/fix/G20.request.md", flush=True); print("cat src/ToyCalc/Program.cs", flush=True)'
            exit 0
            """);
        var wrapperPath = tempDirectory.CreateExecutableFile(
            "bin/codex-isolated",
            $$"""
            #!/bin/zsh
            export CODEX_HOME={{tempDirectory.GetPath(".codex-direct-backend")}}
            exec {{providerBinaryPath}} "$@"
            """);
        using var writer = new StringWriter();
        var originalTimestampFactory = RunFixCommand.TimestampFactory;

        try
        {
            RunFixCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-16T18:18:00Z");

            var baseContext = CreateContext(repoRoot);
            var context = baseContext with
            {
                Config = baseContext.Config with
                {
                    Roles = new RoleMappings
                    {
                        Implement = "Codex",
                        Review = "Codex",
                        Interview = "Claude",
                        Clarify = "Codex"
                    },
                    DirectRun = new DirectRunConfig
                    {
                        Command = wrapperPath
                    }
                }
            };

            var exitCode = RunFixCommand.Execute(context, ["G20"], writer);

            Assert.Equal(0, exitCode);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.result.json");
            var providerEventLogPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G20.provider.jsonl");
            var initialArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));

            await WaitForConditionAsync(
                () =>
                {
                    if (!File.Exists(providerEventLogPath) || !File.Exists(resultArtifactPath))
                    {
                        return false;
                    }

                    var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                    var sawRequestRead = providerEvents.Any(providerEvent =>
                        providerEvent.Kind == "provider-event"
                        && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                        && string.Equals(
                            providerEvent.Payload.GetString(),
                            "sed -n 1,160p .intent-cli/fix/G20.request.md",
                            StringComparison.Ordinal));
                    if (!sawRequestRead)
                    {
                        return false;
                    }

                    return true;
                },
                TimeSpan.FromSeconds(10));

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            Assert.DoesNotContain(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "stdin-eof", StringComparison.Ordinal));
            Assert.DoesNotContain(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "stdin-eof-after-inventory", StringComparison.Ordinal));
            Assert.DoesNotContain(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "tty-missing", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "pwd && rg --files .", StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(
                    providerEvent.Payload.GetString(),
                    "sed -n 1,160p .intent-cli/fix/G20.request.md",
                    StringComparison.Ordinal));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(providerEvent.Payload.GetString(), "cat src/ToyCalc/Program.cs", StringComparison.Ordinal));

        }
        finally
        {
            RunFixCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingQueueItem_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G99"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("was not found in queue state", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenInvalidState_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Review)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be fixing", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingLinkedIssue_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(withLinkedIssue: false)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must have a linked issue", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
    }

    [Fact]
    public void Execute_GivenMissingPacketArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingReviewContextArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review context artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingReviewCommentArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review comment artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewContextMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown("G21"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match queue item execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenReviewCommentArtifactMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson(executionUnit: "G21"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review comment artifact execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingLatestLinkedPr_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-09T09:40:00Z","execution_unit":"G20","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2"}""" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No linked PR found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenCommentArtifactLinkedPrMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson(linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/68"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match latest linked PR", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingChildRepoPath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Child repo path was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingWorktreePath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunFixCommand.Execute(CreateContext(repoRoot), ["G20"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Worktree path was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    private static CliContext CreateContext(string repoRoot)
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
                }
            }
        };
    }

    private static QueueState CreateQueueState(
        QueueItemState selectedState = QueueItemState.Fixing,
        bool withLinkedIssue = true)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-09T09:42:34Z"),
            Items =
            [
                CreateItem("G20", selectedState, withLinkedIssue),
                CreateItem("G21", QueueItemState.Blocked, false) with
                {
                    Dependencies = ["G20"],
                    BlockedBy = ["G20"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state, bool withLinkedIssue)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Run Fix Command",
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
            LinkedIssue = withLinkedIssue
                ? new LinkedIssue
                {
                    Repo = "J-Tech-Japan/intent-system",
                    Number = 68,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/68"
                }
                : null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreatePacketYaml(string targetPart = "cli run fix command")
    {
        return """
        implementation_issue_packet:
          issue_title: "[G20] Run Fix Command"
          issue_kind: "feature"
          source_execution_unit: "G20"
          goal: "Generate a repair worker handoff artifact."
          in_scope:
            - "run fix command"
            - "repair handoff artifact generation"
          out_of_scope:
            - "queue mutation"
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "__TARGET_PART__"
          dependencies:
            - "G19"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run fix stays handoff-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/review-recovery-and-retry.md"
          acceptance_criteria:
            - "repair handoff artifact generated"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G20"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/review-recovery-and-retry.md"
          acceptance_criteria:
            - "repair handoff artifact generated"
          deterministic_review_checks:
            - "run fix command remains handoff-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """.Replace("__TARGET_PART__", targetPart, StringComparison.Ordinal);
    }

    private static string CreateReviewContextMarkdown(string executionUnit = "G20")
    {
        return $$"""
        # Execution Unit

        `{{executionUnit}}`

        # Goal

        `intent-cli run fix <execution-unit>` を working command にする。

        # Acceptance Criteria

        - repair handoff artifact generated

        # Deterministic Review Checks

        - run fix command remains handoff-only

        # Expected Evidence

        - dotnet test IntentSystem.sln
        """;
    }

    private static string CreateReviewCommentArtifactJson(
        string executionUnit = "G20",
        string linkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/69")
    {
        return $$"""
        {
          "execution_unit": "{{executionUnit}}",
          "review_request_ref": ".intent-cli/reviews/G20.request.json",
          "linked_pr": "{{linkedPr}}",
          "comment_ref": "https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2",
          "body_path": "/repo/prepared-comment.md"
        }
        """;
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-09T09:00:00Z","execution_unit":"G20","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/69"}
        {"ts":"2026-04-09T09:10:00Z","execution_unit":"A1","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/12"}
        {"ts":"2026-04-09T09:20:00Z","execution_unit":"G20","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2"}
        """ + Environment.NewLine;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.True(condition(), "Timed out waiting for condition.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-fix-tests-").FullName;

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

        public string CreateExecutableFile(string relativePath, string contents)
        {
            var fullPath = CreateFile(relativePath, contents);
            if (OperatingSystem.IsWindows())
            {
                return fullPath;
            }

            File.SetUnixFileMode(
                fullPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
            return fullPath;
        }

        public string GetPath(string relativePath)
        {
            return Path.Combine(rootPath, relativePath);
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
