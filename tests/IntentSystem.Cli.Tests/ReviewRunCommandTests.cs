using System.Diagnostics;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class ReviewRunCommandTests
{
    [Fact]
    public void Execute_GivenQueueItemReviewContextAndRunLog_WritesReviewRequestArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();
        var originalTimestampFactory = ReviewRunCommand.TimestampFactory;
        var originalLauncherFactory = ReviewRunCommand.DirectRunLauncherFactory;

        try
        {
            ReviewRunCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:35:00Z");
            ReviewRunCommand.DirectRunLauncherFactory = () => new FakeDirectRunLauncher(
                "pid:9999",
                "ReviewBot",
                "gpt-5.4-mini",
                "grpc",
                "reviewbot",
                ["launch", "--model", "{model}", "--artifact", "{request_artifact_path}"],
                "grpc transport launched via 'reviewbot' in '/repo' for provider 'ReviewBot'.");

            var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Review request artifact generated for G9", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Direct run request artifact: .intent-cli/runtime-runs/G9.request.json", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Provider raw event log: .intent-cli/runtime-runs/G9.provider.jsonl", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Normalized run result: .intent-cli/runtime-runs/G9.result.json", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Direct provider: ReviewBot", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Direct model: gpt-5.4-mini", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Direct transport: grpc", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Provider session: pid:9999", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Run status: running", writer.ToString(), StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "reviews", "G9.request.json");
            Assert.True(File.Exists(artifactPath));
            var request = ReviewRequestSerializer.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal("G9", request.ExecutionUnit);
            Assert.Equal(".intent-cli/issues/G9/review-context.md", request.ReviewContextRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/45", request.LinkedPr);
            Assert.Equal(
                ["review run command が PR comment 投稿や closeout の責務へ広がっていない"],
                request.DeterministicReviewChecks);
            Assert.Empty(request.AcceptanceCriteria);
            Assert.Equal(
                ["dotnet test IntentSystem.sln", "review run command tests"],
                request.ExpectedEvidence);

            var directRunArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runtime-runs", "G9.request.json");
            Assert.True(File.Exists(directRunArtifactPath));
            var directRunArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(directRunArtifactPath));
            Assert.Equal("G9", directRunArtifact.ExecutionUnit);
            Assert.Equal("review", directRunArtifact.EntryKind);
            Assert.Equal(".intent-cli/reviews/G9.request.json", directRunArtifact.UpstreamRequestRef);
            Assert.Equal("ReviewBot", directRunArtifact.Provider);
            Assert.Equal("gpt-5.4-mini", directRunArtifact.Model);
            Assert.Equal("grpc", directRunArtifact.Transport);
            Assert.Equal("pid:9999", directRunArtifact.ProviderSessionId);

            var resultArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runtime-runs", "G9.result.json");
            Assert.True(File.Exists(resultArtifactPath));
            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
            Assert.Equal("G9", resultArtifact.ExecutionUnit);
            Assert.Equal("review", resultArtifact.EntryKind);
            Assert.Equal(".intent-cli/reviews/G9.request.json", resultArtifact.UpstreamRequestRef);
            Assert.Equal("ReviewBot", resultArtifact.Provider);
            Assert.Equal("gpt-5.4-mini", resultArtifact.Model);
            Assert.Equal("pid:9999", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);
            Assert.Equal(".intent-cli/runtime-runs/G9.provider.jsonl", resultArtifact.RawLogRef);
            Assert.Equal(".intent-cli/issues/G9/review-context.md", resultArtifact.ReviewContextRef);
            Assert.Equal(".intent-cli/issues/G9/packet.yaml", resultArtifact.PacketRef);
            Assert.Null(resultArtifact.LinkedIssue);
            Assert.Equal("J-Tech-Japan/intent-system", resultArtifact.LinkedPr?.Repo);
            Assert.Equal(45, resultArtifact.LinkedPr?.Number);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/45", resultArtifact.LinkedPr?.Url);
            Assert.EndsWith("/.intent-cli/worktrees/G9", resultArtifact.Worktree.Path, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var lifecycleEvent = Assert.Single(runEvents, runEvent => runEvent.Event == "provider-lifecycle");
            Assert.Equal("G9", lifecycleEvent.ExecutionUnit);
            Assert.Equal("review", lifecycleEvent.EntryKind);
            Assert.Equal("ReviewBot", lifecycleEvent.Provider);
            Assert.Equal("gpt-5.4-mini", lifecycleEvent.Model);
            Assert.Equal("pid:9999", lifecycleEvent.SessionId);
            Assert.Equal("running", lifecycleEvent.RunStatus);
            Assert.Equal(".intent-cli/runtime-runs/G9.provider.jsonl", lifecycleEvent.RawLogRef);
            Assert.Equal(".intent-cli/runtime-runs/G9.result.json", lifecycleEvent.ResultRef);
            Assert.Equal(".intent-cli/issues/G9/packet.yaml", lifecycleEvent.PacketRef);
            Assert.Equal(".intent-cli/issues/G9/review-context.md", lifecycleEvent.ReviewContextRef);
            Assert.Null(lifecycleEvent.LinkedIssue);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/45", lifecycleEvent.LinkedPr);
            Assert.EndsWith("/.intent-cli/worktrees/G9", lifecycleEvent.WorktreePath, StringComparison.Ordinal);
        }
        finally
        {
            ReviewRunCommand.TimestampFactory = originalTimestampFactory;
            ReviewRunCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenMissingReviewContext_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review context artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRunLog_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Run log was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewContextMismatch_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown("G8"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match queue item execution unit", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingLinkedPr_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-03T10:00:00Z","execution_unit":"G9","event":"queued","by":"intent-cli"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No linked PR found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenCumulativeProviderLog_PrefersCurrentLaunchedSession()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runtime-runs", "G9.provider.jsonl"),
            string.Join(
                Environment.NewLine,
                new[]
                {
                    DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-09T10:00:00.0000000+00:00",
                        ExecutionUnit = "G9",
                        Provider = "ReviewBot",
                        EntryKind = "review",
                        SessionId = "pid:stale",
                        Kind = "session-metadata",
                        Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                        {
                            model = "gpt-5.4-stale",
                            transport = "grpc",
                            command = "reviewbot"
                        })
                    }),
                    DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-09T10:00:01.0000000+00:00",
                        ExecutionUnit = "G9",
                        Provider = "ReviewBot",
                        EntryKind = "review",
                        SessionId = "pid:stale",
                        Kind = "provider-event",
                        Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                        {
                            status = "accepted"
                        })
                    })
                }) + Environment.NewLine);
        using var writer = new StringWriter();
        var originalTimestampFactory = ReviewRunCommand.TimestampFactory;
        var originalLauncherFactory = ReviewRunCommand.DirectRunLauncherFactory;

        try
        {
            ReviewRunCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:35:00Z");
            ReviewRunCommand.DirectRunLauncherFactory = () => new AppendingFakeDirectRunLauncher(
                "pid:9999",
                "ReviewBot",
                "gpt-5.4-mini",
                "grpc",
                "reviewbot",
                ["launch", "--model", "{model}", "--artifact", "{request_artifact_path}"],
                "grpc transport launched via 'reviewbot' in '/repo' for provider 'ReviewBot'.");

            var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

            Assert.Equal(0, exitCode);
            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runtime-runs", "G9.result.json")));
            Assert.Equal("pid:9999", resultArtifact.SessionId);
            Assert.Equal("gpt-5.4-mini", resultArtifact.Model);
            Assert.Equal("running", resultArtifact.RunStatus);
        }
        finally
        {
            ReviewRunCommand.TimestampFactory = originalTimestampFactory;
            ReviewRunCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenBackendExitEvent_NormalizesSucceededRunStatus()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();
        var originalTimestampFactory = ReviewRunCommand.TimestampFactory;
        var originalLauncherFactory = ReviewRunCommand.DirectRunLauncherFactory;

        try
        {
            ReviewRunCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:35:00Z");
            ReviewRunCommand.DirectRunLauncherFactory = () => new ExitCodeFakeDirectRunLauncher(
                "pid:9999",
                "ReviewBot",
                "gpt-5.4-mini",
                "grpc",
                "reviewbot",
                ["launch", "--model", "{model}", "--artifact", "{request_artifact_path}"],
                "grpc transport launched via 'reviewbot' in '/repo' for provider 'ReviewBot'.",
                0);

            var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run status: succeeded", writer.ToString(), StringComparison.Ordinal);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runtime-runs", "G9.result.json")));
            Assert.Equal("succeeded", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var lifecycleEvent = Assert.Single(runEvents, runEvent => runEvent.Event == "provider-lifecycle");
            Assert.Equal("succeeded", lifecycleEvent.RunStatus);
        }
        finally
        {
            ReviewRunCommand.TimestampFactory = originalTimestampFactory;
            ReviewRunCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
    }

    [Fact]
    public void Execute_GivenCliProcessExitsBeforeAbsoluteCodexReviewCompletes_PersistsBackendExitToRawLog()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var codexPath = tempDirectory.CreateExecutableFile(
            Path.Combine("bin", "codex-experimental"),
            """
            #!/bin/sh
            printf '%s\n' '{"type":"ready"}'
            sleep 1
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "config.toml"),
            $$"""
            default_domain = "intent-system"
            artifact_root = ".intent-cli"
            worktree_root = ".intent-cli/worktrees"

            [direct_backend]
            artifact_root = ".intent-cli/runtime-runs"

            [direct_backend.review]
            provider = "OpenAI"
            model = "gpt-5.4-mini"
            transport = "responses"
            command = "{{codexPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
            args = ["exec", "{prompt}"]
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "packet.yaml"),
            "execution_unit: G9" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());

        var process = StartCliProcess(repoRoot, "review run G9");
        Assert.True(process.WaitForExit(120000), "CLI process did not exit within the timeout.");
        Assert.Equal(0, process.ExitCode);

        var providerEventLogPath = Path.Combine(repoRoot, ".intent-cli", "runtime-runs", "G9.provider.jsonl");
        TemporaryDirectory.WaitForCondition(
            () => File.Exists(providerEventLogPath)
                && DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath)).Any(providerEvent =>
                    providerEvent.Kind == "provider-event"
                    && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                    && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                    && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5));

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "ready", StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
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
                    ArtifactRoot = ".intent-cli"
                },
                DirectRun = new DirectRunConfig
                {
                    ArtifactRoot = ".intent-cli/runtime-runs",
                    Command = "fallback-review-launcher",
                    Args = ["--prompt", "{prompt}"],
                    Review = new DirectRunEntryConfig
                    {
                        Provider = "ReviewBot",
                        Model = "gpt-5.4-mini",
                        Transport = "grpc",
                        Command = "reviewbot",
                        Args = ["launch", "--model", "{model}", "--artifact", "{request_artifact_path}"]
                    }
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G9",
                    Title = "Review run command",
                    State = QueueItemState.Review,
                    Dependencies = ["G7"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G9/implementation.md",
                        ReviewContext = ".intent-cli/issues/G9/review-context.md",
                        Yaml = ".intent-cli/issues/G9/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static string CreateReviewContextMarkdown(string executionUnit = "G9")
    {
        return $$"""
        # Execution Unit

        `{{executionUnit}}`

        # Goal

        `intent-cli review run <execution-unit>` を working command として実装し、
        review context packet と latest linked PR をもとに
        deterministic review request artifact を `.intent-cli/reviews/<execution-unit>.request.json` へ生成できるようにする。

        # Parent References

        - [Intent CLI Surface](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/05-intent-cli-surface.md)
        - [Config And Run Model](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/08-config-and-run-model.md)

        # Deterministic Review Checks

        - review run command が PR comment 投稿や closeout の責務へ広がっていない

        # Expected Evidence

        - dotnet test IntentSystem.sln
        - review run command tests
        """;
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/44"}
        {"ts":"2026-04-03T10:10:00Z","execution_unit":"A1","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/12"}
        {"ts":"2026-04-03T10:20:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/45"}
        """ + Environment.NewLine;
    }

    private static Process StartCliProcess(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/zsh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(
            $"dotnet run --project {QuoteForShell(Path.Combine(GetSolutionRoot(), "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj"))} -- {arguments}");

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start CLI process.");
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string QuoteForShell(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-review-run-tests-").FullName;

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

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    fullPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }

            return fullPath;
        }

        public static void WaitForCondition(Func<bool> predicate, TimeSpan timeout)
        {
            var startedAt = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - startedAt < timeout)
            {
                if (predicate())
                {
                    return;
                }

                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }

            Assert.True(predicate(), $"Condition was not satisfied within {timeout}.");
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class FakeDirectRunLauncher(
        string providerSessionId,
        string provider,
        string model,
        string transport,
        string command,
        IReadOnlyList<string> argsTemplate,
        string transportSummary) : IDirectRunLauncher
    {
        public DirectRunLaunchResult Launch(
            string executionUnit,
            string entryKind,
            string requestArtifactPath,
            string providerEventLogPath,
            string providerArg,
            string modelArg,
            string transportArg,
            string commandArg,
            IReadOnlyList<string> argsTemplateArg,
            DateTimeOffset launchedAt,
            string workingDirectory,
            string absoluteRequestArtifactPath,
            string absoluteProviderEventLogPath)
        {
            Assert.Equal("G9", executionUnit);
            Assert.Equal("review", entryKind);
            Assert.Equal(".intent-cli/runtime-runs/G9.request.json", requestArtifactPath);
            Assert.Equal(".intent-cli/runtime-runs/G9.provider.jsonl", providerEventLogPath);
            Assert.Equal(provider, providerArg);
            Assert.Equal(model, modelArg);
            Assert.Equal(transport, transportArg);
            Assert.Equal(command, commandArg);
            Assert.Equal(argsTemplate, argsTemplateArg);
            Assert.EndsWith("/repo", workingDirectory, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/reviews/G9.request.json", absoluteRequestArtifactPath, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/runtime-runs/G9.provider.jsonl", absoluteProviderEventLogPath, StringComparison.Ordinal);

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
                RequestArtifactPath = ".intent-cli/runtime-runs/G9.request.json",
                ProviderEventLogPath = ".intent-cli/runtime-runs/G9.provider.jsonl",
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = providerSessionId,
                TransportSummary = transportSummary
            };
        }
    }

    private sealed class AppendingFakeDirectRunLauncher(
        string providerSessionId,
        string provider,
        string model,
        string transport,
        string command,
        IReadOnlyList<string> argsTemplate,
        string transportSummary) : IDirectRunLauncher
    {
        public DirectRunLaunchResult Launch(
            string executionUnit,
            string entryKind,
            string requestArtifactPath,
            string providerEventLogPath,
            string providerArg,
            string modelArg,
            string transportArg,
            string commandArg,
            IReadOnlyList<string> argsTemplateArg,
            DateTimeOffset launchedAt,
            string workingDirectory,
            string absoluteRequestArtifactPath,
            string absoluteProviderEventLogPath)
        {
            Assert.Equal("G9", executionUnit);
            Assert.Equal("review", entryKind);
            Assert.Equal(".intent-cli/runtime-runs/G9.request.json", requestArtifactPath);
            Assert.Equal(".intent-cli/runtime-runs/G9.provider.jsonl", providerEventLogPath);
            Assert.Equal(provider, providerArg);
            Assert.Equal(model, modelArg);
            Assert.Equal(transport, transportArg);
            Assert.Equal(command, commandArg);
            Assert.Equal(argsTemplate, argsTemplateArg);
            Assert.EndsWith("/repo", workingDirectory, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/reviews/G9.request.json", absoluteRequestArtifactPath, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/runtime-runs/G9.provider.jsonl", absoluteProviderEventLogPath, StringComparison.Ordinal);

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
            File.AppendAllText(absoluteProviderEventLogPath, providerEvents);

            return new DirectRunLaunchResult
            {
                RequestArtifactPath = ".intent-cli/runtime-runs/G9.request.json",
                ProviderEventLogPath = ".intent-cli/runtime-runs/G9.provider.jsonl",
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = providerSessionId,
                TransportSummary = transportSummary
            };
        }
    }

    private sealed class ExitCodeFakeDirectRunLauncher(
        string providerSessionId,
        string provider,
        string model,
        string transport,
        string command,
        IReadOnlyList<string> argsTemplate,
        string transportSummary,
        int exitCode) : IDirectRunLauncher
    {
        public DirectRunLaunchResult Launch(
            string executionUnit,
            string entryKind,
            string requestArtifactPath,
            string providerEventLogPath,
            string providerArg,
            string modelArg,
            string transportArg,
            string commandArg,
            IReadOnlyList<string> argsTemplateArg,
            DateTimeOffset launchedAt,
            string workingDirectory,
            string absoluteRequestArtifactPath,
            string absoluteProviderEventLogPath)
        {
            Assert.Equal("G9", executionUnit);
            Assert.Equal("review", entryKind);
            Assert.Equal(".intent-cli/runtime-runs/G9.request.json", requestArtifactPath);
            Assert.Equal(".intent-cli/runtime-runs/G9.provider.jsonl", providerEventLogPath);
            Assert.Equal(provider, providerArg);
            Assert.Equal(model, modelArg);
            Assert.Equal(transport, transportArg);
            Assert.Equal(command, commandArg);
            Assert.Equal(argsTemplate, argsTemplateArg);
            Assert.EndsWith("/repo", workingDirectory, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/reviews/G9.request.json", absoluteRequestArtifactPath, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/runtime-runs/G9.provider.jsonl", absoluteProviderEventLogPath, StringComparison.Ordinal);

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
                                                 type = "backend-exit",
                                                 exit_code = exitCode
                                             })
                                         })
                                     }) + Environment.NewLine;
            File.WriteAllText(absoluteProviderEventLogPath, providerEvents);

            return new DirectRunLaunchResult
            {
                RequestArtifactPath = ".intent-cli/runtime-runs/G9.request.json",
                ProviderEventLogPath = ".intent-cli/runtime-runs/G9.provider.jsonl",
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = providerSessionId,
                TransportSummary = transportSummary
            };
        }
    }
}
