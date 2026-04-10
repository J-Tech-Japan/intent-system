using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class DirectRunLauncherTests
{
    [Fact]
    public void Launch_GivenConfiguredCommandPolicy_StartsConfiguredExecutableAndCapturesProviderEvents()
    {
        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G19.provider.jsonl");
        var runner = new FakeDirectRunProcessRunner
        {
            StdOutLines = ["""{"type":"ready","step":"bootstrap"}"""],
            StdErrLines = ["""{"level":"warn","message":"slow-start"}"""],
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 4321,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G19",
            "implement",
            ".intent-cli/runs/G19.request.json",
            ".intent-cli/runs/G19.provider.jsonl",
            "ReviewBot",
            "gpt-5.4",
            "grpc",
            "review-runner",
            ["launch", "--entry", "{entry_kind}", "--unit", "{execution_unit}", "--model", "{model}", "--artifact", "{request_artifact_path}", "--run-artifact", "{direct_run_artifact_path}", "{prompt}"],
            DateTimeOffset.Parse("2026-04-09T10:15:00Z"),
            "/repo/.intent-cli/worktrees/G19",
            "/repo/.intent-cli/implement/G19.request.md",
            providerEventLogPath);

        Assert.Equal("review-runner", runner.FileName);
        Assert.Equal("/repo/.intent-cli/worktrees/G19", runner.WorkingDirectory);
        Assert.Equal(
            [
                "launch",
                "--entry",
                "implement",
                "--unit",
                "G19",
                "--model",
                "gpt-5.4",
                "--artifact",
                "/repo/.intent-cli/implement/G19.request.md",
                "--run-artifact",
                ".intent-cli/runs/G19.request.json",
                "Use the request artifact at '/repo/.intent-cli/implement/G19.request.md' as the bounded source of truth for this direct run."
            ],
            runner.Arguments);
        Assert.Equal("pid:4321", result.ProviderSessionId);
        Assert.Equal(".intent-cli/runs/G19.provider.jsonl", result.ProviderEventLogPath);
        Assert.Contains("grpc transport launched via 'review-runner'", result.TransportSummary, StringComparison.Ordinal);

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Collection(
            events,
            providerEvent =>
            {
                Assert.Equal("2026-04-09T10:15:00.0000000+00:00", providerEvent.Timestamp);
                Assert.Equal("G19", providerEvent.ExecutionUnit);
                Assert.Equal("ReviewBot", providerEvent.Provider);
                Assert.Equal("implement", providerEvent.EntryKind);
                Assert.Equal("pid:4321", providerEvent.SessionId);
                Assert.Equal("session-metadata", providerEvent.Kind);
                Assert.Equal("gpt-5.4", providerEvent.Payload.GetProperty("model").GetString());
                Assert.Equal("grpc", providerEvent.Payload.GetProperty("transport").GetString());
                Assert.Equal("review-runner", providerEvent.Payload.GetProperty("command").GetString());
            },
            providerEvent =>
            {
                Assert.Equal("G19", providerEvent.ExecutionUnit);
                Assert.Equal("ReviewBot", providerEvent.Provider);
                Assert.Equal("implement", providerEvent.EntryKind);
                Assert.Equal("pid:4321", providerEvent.SessionId);
                Assert.Equal("provider-event", providerEvent.Kind);
                Assert.Equal("ready", providerEvent.Payload.GetProperty("type").GetString());
                Assert.Equal("bootstrap", providerEvent.Payload.GetProperty("step").GetString());
            },
            providerEvent =>
            {
                Assert.Equal("G19", providerEvent.ExecutionUnit);
                Assert.Equal("ReviewBot", providerEvent.Provider);
                Assert.Equal("implement", providerEvent.EntryKind);
                Assert.Equal("pid:4321", providerEvent.SessionId);
                Assert.Equal("provider-event", providerEvent.Kind);
                Assert.Equal("warn", providerEvent.Payload.GetProperty("level").GetString());
                Assert.Equal("slow-start", providerEvent.Payload.GetProperty("message").GetString());
            });
    }

    [Fact]
    public void Launch_GivenUpstreamRequestArtifactPlaceholder_ExpandsAlias()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 8765,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G20",
            "fix",
            ".intent-cli/runs/G20.request.json",
            ".intent-cli/runs/G20.provider.jsonl",
            "Claude",
            "sonnet",
            "stdio",
            "claude",
            ["--artifact", "{upstream_request_artifact_path}", "--model", "{model}"],
            DateTimeOffset.Parse("2026-04-09T10:25:00Z"),
            "/repo/.intent-cli/worktrees/G20",
            "/repo/.intent-cli/fix/G20.request.md",
            tempDirectory.GetPath(".intent-cli/runs/G20.provider.jsonl"));

        Assert.Equal("claude", runner.FileName);
        Assert.Equal(
            ["--artifact", "/repo/.intent-cli/fix/G20.request.md", "--model", "sonnet"],
            runner.Arguments);
        Assert.Equal("pid:8765", result.ProviderSessionId);
    }

    [Fact]
    public void Launch_GivenEarlyNonZeroExit_Throws()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 999,
                ExitedEarly = true,
                ExitCode = 17
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var exception = Assert.Throws<InvalidOperationException>(() => launcher.Launch(
            "G9",
            "review",
            ".intent-cli/runs/G9.request.json",
            ".intent-cli/runs/G9.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "grpc",
            "codex",
            ["exec", "{prompt}"],
            DateTimeOffset.Parse("2026-04-09T10:35:00Z"),
            "/repo",
            "/repo/.intent-cli/reviews/G9.request.json",
            tempDirectory.GetPath(".intent-cli/runs/G9.provider.jsonl")));

        Assert.Contains("exit code 17", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeDirectRunProcessRunner : IDirectRunProcessRunner
    {
        public string WorkingDirectory { get; private set; } = string.Empty;

        public string FileName { get; private set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public IReadOnlyList<string> StdOutLines { get; init; } = [];

        public IReadOnlyList<string> StdErrLines { get; init; } = [];

        public DirectRunProcessLaunchResult Result { get; set; } = new()
        {
            ProcessId = 1,
            ExitedEarly = false,
            ExitCode = 0
        };

        public DirectRunProcessLaunchResult Start(
            string workingDirectory,
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan earlyExitWindow,
            Action<int> onStarted,
            Action<string> onStdOutLine,
            Action<string> onStdErrLine)
        {
            WorkingDirectory = workingDirectory;
            FileName = fileName;
            Arguments = arguments.ToArray();
            onStarted(Result.ProcessId);
            foreach (var line in StdOutLines)
            {
                onStdOutLine(line);
            }

            foreach (var line in StdErrLines)
            {
                onStdErrLine(line);
            }

            return Result;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

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
