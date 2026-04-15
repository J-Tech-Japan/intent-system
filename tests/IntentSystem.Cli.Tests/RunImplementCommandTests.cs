using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class RunImplementCommandTests
{
    [Fact]
    public void Execute_GivenActiveItemWithInputs_GeneratesHandoffArtifactAndNormalizedRunResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G19"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();
        var originalTimestampFactory = RunImplementCommand.TimestampFactory;
        var originalLauncherFactory = RunImplementCommand.DirectRunLauncherFactory;

        var originalQueueState = File.ReadAllText(queueStatePath);

        try
        {
            RunImplementCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:15:00Z");
            RunImplementCommand.DirectRunLauncherFactory = () => new FakeDirectRunLauncher(
                "pid:4321",
                "Claude",
                "default",
                "stdio",
                "stdio transport launched via 'claude' in '/repo/.intent-cli/worktrees/G19' for provider 'Claude'.");

            var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Implementation handoff artifact generated for G19", output, StringComparison.Ordinal);
            Assert.Contains("Implement role: Claude", output, StringComparison.Ordinal);
            Assert.Contains("Branch: issue-66-g19", output, StringComparison.Ordinal);
            Assert.Contains("Latest linked PR: https://github.com/J-Tech-Japan/intent-system/pull/67", output, StringComparison.Ordinal);
            Assert.Contains("Direct run request artifact: .intent-cli/runs/G19.request.json", output, StringComparison.Ordinal);
            Assert.Contains("Provider raw event log: .intent-cli/runs/G19.provider.jsonl", output, StringComparison.Ordinal);
            Assert.Contains("Normalized run result: .intent-cli/runs/G19.result.json", output, StringComparison.Ordinal);
            Assert.Contains("Direct provider: Claude", output, StringComparison.Ordinal);
            Assert.Contains("Direct model: default", output, StringComparison.Ordinal);
            Assert.Contains("Direct transport: stdio", output, StringComparison.Ordinal);
            Assert.Contains("Provider session: pid:4321", output, StringComparison.Ordinal);
            Assert.Contains("Run status: running", output, StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "implement", "G19.request.md");
            Assert.True(File.Exists(artifactPath));
            var markdown = File.ReadAllText(artifactPath);
            Assert.Contains("- packet_ref: .intent-cli/issues/G19/packet.yaml", markdown, StringComparison.Ordinal);
            Assert.Contains("- review_context_ref: .intent-cli/issues/G19/review-context.md", markdown, StringComparison.Ordinal);
            Assert.Contains("- latest_linked_pr: https://github.com/J-Tech-Japan/intent-system/pull/67", markdown, StringComparison.Ordinal);
            Assert.Contains("- implement: Claude", markdown, StringComparison.Ordinal);

            var directRunArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G19.request.json");
            Assert.True(File.Exists(directRunArtifactPath));
            var directRunArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(directRunArtifactPath));
            Assert.Equal("G19", directRunArtifact.ExecutionUnit);
            Assert.Equal("implement", directRunArtifact.EntryKind);
            Assert.Equal(".intent-cli/implement/G19.request.md", directRunArtifact.UpstreamRequestRef);
            Assert.Equal("Claude", directRunArtifact.Provider);
            Assert.Equal("default", directRunArtifact.Model);
            Assert.Equal("stdio", directRunArtifact.Transport);
            Assert.Equal("pid:4321", directRunArtifact.ProviderSessionId);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runs", "G19.result.json");
            Assert.True(File.Exists(resultArtifactPath));
            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal("G19", resultArtifact.ExecutionUnit);
            Assert.Equal("implement", resultArtifact.EntryKind);
            Assert.Equal(".intent-cli/implement/G19.request.md", resultArtifact.UpstreamRequestRef);
            Assert.Equal("Claude", resultArtifact.Provider);
            Assert.Equal("default", resultArtifact.Model);
            Assert.Equal("pid:4321", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);
            Assert.Equal(".intent-cli/runs/G19.provider.jsonl", resultArtifact.RawLogRef);
            Assert.Equal(".intent-cli/issues/G19/packet.yaml", resultArtifact.PacketRef);
            Assert.Equal(".intent-cli/issues/G19/review-context.md", resultArtifact.ReviewContextRef);
            Assert.Equal("J-Tech-Japan/intent-system", resultArtifact.LinkedIssue?.Repo);
            Assert.Equal(66, resultArtifact.LinkedIssue?.Number);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/66", resultArtifact.LinkedIssue?.Url);
            Assert.Equal("J-Tech-Japan/intent-system", resultArtifact.LinkedPr?.Repo);
            Assert.Equal(67, resultArtifact.LinkedPr?.Number);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/67", resultArtifact.LinkedPr?.Url);
            Assert.EndsWith("/.intent-cli/worktrees/G19", resultArtifact.Worktree.Path, StringComparison.Ordinal);

            Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            var lifecycleEvent = Assert.Single(runEvents, runEvent => runEvent.Event == "provider-lifecycle");
            Assert.Equal("G19", lifecycleEvent.ExecutionUnit);
            Assert.Equal("intent-cli", lifecycleEvent.By);
            Assert.Equal("implement", lifecycleEvent.EntryKind);
            Assert.Equal("Claude", lifecycleEvent.Provider);
            Assert.Equal("default", lifecycleEvent.Model);
            Assert.Equal("pid:4321", lifecycleEvent.SessionId);
            Assert.Equal("running", lifecycleEvent.RunStatus);
            Assert.Equal(".intent-cli/runs/G19.provider.jsonl", lifecycleEvent.RawLogRef);
            Assert.Equal(".intent-cli/runs/G19.result.json", lifecycleEvent.ResultRef);
            Assert.Equal(".intent-cli/issues/G19/packet.yaml", lifecycleEvent.PacketRef);
            Assert.Equal(".intent-cli/issues/G19/review-context.md", lifecycleEvent.ReviewContextRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/66", lifecycleEvent.LinkedIssue);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/67", lifecycleEvent.LinkedPr);
            Assert.EndsWith("/.intent-cli/worktrees/G19", lifecycleEvent.WorktreePath, StringComparison.Ordinal);
        }
        finally
        {
            RunImplementCommand.TimestampFactory = originalTimestampFactory;
            RunImplementCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenFixingItemWithoutLatestPr_GeneratesArtifactWithoutLatestPr()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G19"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(QueueItemState.Fixing)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-08T08:00:00Z","execution_unit":"G19","event":"fix-requested","by":"intent-cli"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Latest linked PR:", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("latest_linked_pr", File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "implement", "G19.request.md")), StringComparison.Ordinal);
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
            Assert.Equal("G19", executionUnit);
            Assert.Equal("implement", entryKind);
            Assert.Equal(".intent-cli/runs/G19.request.json", requestArtifactPath);
            Assert.Equal(".intent-cli/runs/G19.provider.jsonl", providerEventLogPath);
            Assert.Equal(provider, providerArg);
            Assert.Equal(model, modelArg);
            Assert.Equal(transport, transportArg);
            Assert.Equal(expectedCommand, command);
            Assert.Equal(expectedArgsTemplate ?? ["--print", "--model", "{model}", "--output-format", "json", "{prompt}"], argsTemplate);
            Assert.EndsWith("/.intent-cli/worktrees/G19", workingDirectory, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/implement/G19.request.md", absoluteRequestArtifactPath, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/runs/G19.provider.jsonl", absoluteProviderEventLogPath, StringComparison.Ordinal);

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
                RequestArtifactPath = ".intent-cli/runs/G19.request.json",
                ProviderEventLogPath = ".intent-cli/runs/G19.provider.jsonl",
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = providerSessionId,
                TransportSummary = transportSummary
            };
        }
    }

    [Fact]
    public void Execute_GivenCodexCommandPathWithoutModelOverride_UsesRunnableCodexDefaultModel()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G19"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();
        var originalTimestampFactory = RunImplementCommand.TimestampFactory;
        var originalLauncherFactory = RunImplementCommand.DirectRunLauncherFactory;

        try
        {
            RunImplementCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:15:00Z");
            RunImplementCommand.DirectRunLauncherFactory = () => new FakeDirectRunLauncher(
                "pid:4321",
                "Codex",
                CliRuntimeContracts.DefaultCodexDirectRunModel,
                "stdio",
                "stdio transport launched via '/opt/homebrew/bin/codex' in '/repo/.intent-cli/worktrees/G19' for provider 'Codex'.",
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

            var exitCode = RunImplementCommand.Execute(context, ["G19"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains($"Direct model: {CliRuntimeContracts.DefaultCodexDirectRunModel}", writer.ToString(), StringComparison.Ordinal);

            var directRunArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G19.request.json")));
            Assert.Equal(CliRuntimeContracts.DefaultCodexDirectRunModel, directRunArtifact.Model);
            Assert.Equal("Codex", directRunArtifact.Provider);
        }
        finally
        {
            RunImplementCommand.TimestampFactory = originalTimestampFactory;
            RunImplementCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = RunImplementCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenRuntimeOnlyTargetPart_ReturnsExitCodeOneWithoutWritingHandoffArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G19"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml(targetPart: ".intent-cli/intake"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();
        var originalLauncherFactory = RunImplementCommand.DirectRunLauncherFactory;

        try
        {
            RunImplementCommand.DirectRunLauncherFactory = () => throw new InvalidOperationException("launcher should not be called");

            var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("host runtime-only '.intent-cli/**' content", writer.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "implement", "G19.request.md")));
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runs", "G19.request.json")));
        }
        finally
        {
            RunImplementCommand.DirectRunLauncherFactory = originalLauncherFactory;
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

        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G99"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be active or fixing", writer.ToString(), StringComparison.Ordinal);
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
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review context artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewContextMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G19"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown("G20"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match queue item execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingChildRepoPath_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G19"));
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

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
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunImplementCommand.Execute(CreateContext(repoRoot), ["G19"], writer);

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
        QueueItemState selectedState = QueueItemState.Active,
        bool withLinkedIssue = true)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T08:12:34Z"),
            Items =
            [
                CreateItem("G19", selectedState, withLinkedIssue),
                CreateItem("G20", QueueItemState.Blocked, false) with
                {
                    Dependencies = ["G19"],
                    BlockedBy = ["G19"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state, bool withLinkedIssue)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Run Implement Command",
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
                    Number = 66,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/66"
                }
                : null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreatePacketYaml(string targetPart = "cli run implement command")
    {
        return """
        implementation_issue_packet:
          issue_title: "[G19] Run Implement Command"
          issue_kind: "feature"
          source_execution_unit: "G19"
          goal: "Generate an execution worker handoff artifact."
          in_scope:
            - "run implement command"
            - "handoff artifact generation"
          out_of_scope:
            - "queue mutation"
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "__TARGET_PART__"
          dependencies:
            - "G18"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run implement stays handoff-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "handoff artifact generated"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G19"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "handoff artifact generated"
          deterministic_review_checks:
            - "run implement command remains handoff-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """.Replace("__TARGET_PART__", targetPart, StringComparison.Ordinal);
    }

    private static string CreateReviewContextMarkdown(string executionUnit = "G19")
    {
        return $$"""
        # Execution Unit

        `{{executionUnit}}`

        # Goal

        `intent-cli run implement <execution-unit>` を working command にする。

        # Acceptance Criteria

        - handoff artifact generated

        # Deterministic Review Checks

        - run implement command remains handoff-only

        # Expected Evidence

        - dotnet test IntentSystem.sln
        """;
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-08T08:00:00Z","execution_unit":"G19","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/66"}
        {"ts":"2026-04-08T08:10:00Z","execution_unit":"A1","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/12"}
        {"ts":"2026-04-08T08:20:00Z","execution_unit":"G19","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/66#issuecomment-1"}
        {"ts":"2026-04-08T08:30:00Z","execution_unit":"G19","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/67"}
        """ + Environment.NewLine;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-implement-tests-").FullName;

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
