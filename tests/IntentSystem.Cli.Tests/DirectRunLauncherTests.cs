using System.Diagnostics;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
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
    public void Launch_GivenProcessExit_AppendsBackendExitProviderEvent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G9.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var codexPath = tempDirectory.CreateExecutableFile(
            "repo/codex",
            """
            #!/bin/sh
            printf '%s\n' '{"type":"ready"}'
            """);
        var runner = new FakeDirectRunProcessRunner
        {
            ExecuteReceivedProcess = true
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G9",
            "review",
            ".intent-cli/runs/G9.request.json",
            ".intent-cli/runs/G9.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T10:35:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/reviews/G9.request.json"),
            providerEventLogPath);

        Assert.Equal("pid:", result.ProviderSessionId[..4]);
        Assert.Equal("/bin/sh", runner.FileName);
        Assert.Equal("-c", runner.Arguments[0]);
        Assert.Contains("exec \"$@\"", runner.Arguments[1], StringComparison.Ordinal);
        Assert.Contains(codexPath, runner.Arguments, StringComparer.Ordinal);

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "session-metadata"
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "ready", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        var backendExitEvent = Assert.Single(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
        Assert.Equal("G9", backendExitEvent.ExecutionUnit);
        Assert.Equal("Codex", backendExitEvent.Provider);
        Assert.Equal("review", backendExitEvent.EntryKind);
        Assert.Equal(result.ProviderSessionId, backendExitEvent.SessionId);
        Assert.Equal(0, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public void Launch_GivenWrappedCodexExitWithoutWrapperWrite_AppendsFallbackBackendExitProviderEvent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G10.provider.jsonl");
        var runner = new FakeDirectRunProcessRunner
        {
            ExitCodeEvent = 0,
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 2468,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G10",
            "review",
            ".intent-cli/runs/G10.request.json",
            ".intent-cli/runs/G10.provider.jsonl",
            "OpenAI",
            "gpt-5.4-mini",
            "responses",
            "/tmp/codex-experimental",
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T10:45:00Z"),
            "/repo",
            "/repo/.intent-cli/reviews/G10.request.json",
            providerEventLogPath);

        Assert.Equal("/bin/sh", runner.FileName);
        Assert.Equal("pid:2468", result.ProviderSessionId);

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        var backendExitEvent = Assert.Single(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
        Assert.Equal("G10", backendExitEvent.ExecutionUnit);
        Assert.Equal("OpenAI", backendExitEvent.Provider);
        Assert.Equal("review", backendExitEvent.EntryKind);
        Assert.Equal("pid:2468", backendExitEvent.SessionId);
        Assert.Equal(0, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task Launch_GivenRealProcessRunnerAndAbsoluteCodexCommand_AppendsBackendExitProviderEvent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G12.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var codexPath = tempDirectory.CreateExecutableFile(
            "repo/codex-experimental",
            """
            #!/bin/sh
            printf '%s\n' '{"type":"ready"}'
            sleep 1
            """);
        var launcher = new DirectRunLauncher();

        var result = launcher.Launch(
            "G12",
            "review",
            ".intent-cli/runs/G12.request.json",
            ".intent-cli/runs/G12.provider.jsonl",
            "OpenAI",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T11:05:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/reviews/G12.request.json"),
            providerEventLogPath);

        var initialEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Contains(initialEvents, providerEvent =>
            providerEvent.Kind == "session-metadata"
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));

        await TemporaryDirectory.WaitForConditionAsync(
            () =>
            {
                var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                return events.Any(providerEvent =>
                    providerEvent.Kind == "provider-event"
                    && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                    && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                    && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                    && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(5));

        var finalEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Contains(finalEvents, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "ready", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        var backendExitEvent = Assert.Single(finalEvents, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Equal(0, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task Launch_GivenRealProcessRunnerAndNonWrappedCommand_PreservesExitCallbackAfterGarbageCollection()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G12a.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var runnerPath = tempDirectory.CreateExecutableFile(
            "repo/review-runner",
            """
            #!/bin/sh
            printf '%s\n' '{"type":"ready"}'
            sleep 1
            """);
        var launcher = new DirectRunLauncher();

        var result = launcher.Launch(
            "G12a",
            "review",
            ".intent-cli/runs/G12a.request.json",
            ".intent-cli/runs/G12a.provider.jsonl",
            "ReviewBot",
            "gpt-5.4-mini",
            "responses",
            runnerPath,
            ["test prompt"],
            DateTimeOffset.Parse("2026-04-09T11:06:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/reviews/G12a.request.json"),
            providerEventLogPath);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await TemporaryDirectory.WaitForConditionAsync(
            () =>
            {
                var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                return events.Any(providerEvent =>
                    providerEvent.Kind == "provider-event"
                    && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                    && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                    && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                    && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(5));

        var finalEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        var backendExitEvent = Assert.Single(finalEvents, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Equal(0, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task Launch_GivenWrappedCodexProcessEndsWithoutExitCallback_MonitorAppendsBackendExitProviderEvent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var externalProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            ArgumentList = { "-c", "sleep 1" }
        });
        Assert.NotNull(externalProcess);

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14.provider.jsonl");
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = externalProcess!.Id,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G14",
            "review",
            ".intent-cli/runs/G14.request.json",
            ".intent-cli/runs/G14.provider.jsonl",
            "OpenAI",
            "gpt-5.4-mini",
            "responses",
            "/opt/homebrew/bin/codex",
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T11:15:00Z"),
            "/repo",
            "/repo/.intent-cli/reviews/G14.request.json",
            providerEventLogPath);

        await TemporaryDirectory.WaitForConditionAsync(
            () =>
            {
                if (!File.Exists(providerEventLogPath))
                {
                    return false;
                }

                var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                return events.Any(providerEvent =>
                    providerEvent.Kind == "provider-event"
                    && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                    && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                    && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                    && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(5));

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        var backendExitEvent = Assert.Single(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Equal(1, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task DirectRunExitMonitorCommand_GivenDetachedProcess_AppendsBackendExitForCurrentSession()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var externalProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            ArgumentList = { "-c", "sleep 1" }
        });
        Assert.NotNull(externalProcess);

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14b.provider.jsonl");
        var providerSessionId = $"pid:{externalProcess!.Id}";
        var writer = new DirectRunProviderEventWriter(providerEventLogPath);
        writer.Append(new DirectRunProviderEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            ExecutionUnit = "G14b",
            Provider = "OpenAI",
            EntryKind = "review",
            SessionId = providerSessionId,
            Kind = "provider-event",
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { type = "ready" })
        });

        using var monitor = Process.Start(DirectRunExitMonitorCommand.CreateDetachedStartInfo(
            externalProcess.Id,
            providerEventLogPath,
            "G14b",
            "review",
            "OpenAI",
            providerSessionId));
        Assert.NotNull(monitor);
        monitor!.StandardInput.Close();
        monitor.StandardOutput.Dispose();
        monitor.StandardError.Dispose();

        await TemporaryDirectory.WaitForConditionAsync(
            () =>
            {
                if (!File.Exists(providerEventLogPath))
                {
                    return false;
                }

                var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                return events.Any(providerEvent =>
                    providerEvent.Kind == "provider-event"
                    && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                    && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                    && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                    && string.Equals(providerEvent.SessionId, providerSessionId, StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(5));

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        var backendExitEvent = Assert.Single(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, providerSessionId, StringComparison.Ordinal));
        Assert.Equal(1, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public void CreateDetachedStartInfo_UsesExecutableHostPath()
    {
        var startInfo = DirectRunExitMonitorCommand.CreateDetachedStartInfo(
            1234,
            "/tmp/provider.jsonl",
            "G14c",
            "review",
            "Codex",
            "pid:1234");

        Assert.True(Path.IsPathRooted(startInfo.FileName));
        Assert.True(File.Exists(startInfo.FileName));

        if (string.Equals(Path.GetFileNameWithoutExtension(startInfo.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(Path.IsPathRooted(startInfo.ArgumentList[0]));
            Assert.EndsWith("IntentSystem.Cli.dll", startInfo.ArgumentList[0], StringComparison.Ordinal);
            Assert.Equal("__direct-run-exit-monitor", startInfo.ArgumentList[1]);
        }
        else
        {
            Assert.Equal("__direct-run-exit-monitor", startInfo.ArgumentList[0]);
        }
    }

    [Fact]
    public void Launch_GivenWrappedCodexProcessExitsShortlyAfterLaunch_PersistsBackendExitBeforeReturning()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var externalProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            ArgumentList = { "-c", "sleep 1" }
        });
        Assert.NotNull(externalProcess);

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14a.provider.jsonl");
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = externalProcess!.Id,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G14a",
            "review",
            ".intent-cli/runs/G14a.request.json",
            ".intent-cli/runs/G14a.provider.jsonl",
            "OpenAI",
            "gpt-5.4-mini",
            "responses",
            "/opt/homebrew/bin/codex",
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T11:17:00Z"),
            "/repo",
            "/repo/.intent-cli/reviews/G14a.request.json",
            providerEventLogPath);

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
    }

    [Fact]
    public void Launch_GivenWrappedCodexProcessReceivesTerminationSignal_AppendsBackendExitProviderEvent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G13.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var codexPath = tempDirectory.CreateExecutableFile(
            "repo/codex-experimental",
            """
            #!/bin/sh
            trap 'exit 0' TERM HUP INT
            printf '%s\n' '{"type":"ready"}'
            sleep 10
            """);
        var runner = new FakeDirectRunProcessRunner
        {
            ExecuteReceivedProcess = true,
            OnStartedProcess = processId =>
            {
                Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(800));
                    SignalProcess(processId, "TERM");
                });
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G13",
            "review",
            ".intent-cli/runs/G13.request.json",
            ".intent-cli/runs/G13.provider.jsonl",
            "OpenAI",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T11:10:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/reviews/G13.request.json"),
            providerEventLogPath);

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "ready", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
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

    private static void SignalProcess(int processId, string signalName)
    {
        using var signalProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            ArgumentList =
            {
                "-c",
                $"kill -s {signalName} {processId}"
            }
        });

        signalProcess?.WaitForExit();
    }

    private sealed class FakeDirectRunProcessRunner : IDirectRunProcessRunner
    {
        public string WorkingDirectory { get; private set; } = string.Empty;

        public string FileName { get; private set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public IReadOnlyList<string> StdOutLines { get; init; } = [];

        public IReadOnlyList<string> StdErrLines { get; init; } = [];

        public int? ExitCodeEvent { get; init; }

        public bool ExecuteReceivedProcess { get; init; }

        public Action<int>? OnStartedProcess { get; init; }

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
            Action<int> onExited,
            Action<string> onStdOutLine,
            Action<string> onStdErrLine)
        {
            WorkingDirectory = workingDirectory;
            FileName = fileName;
            Arguments = arguments.ToArray();

            if (ExecuteReceivedProcess)
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                var process = System.Diagnostics.Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start wrapper process.");
                onStarted(process.Id);
                OnStartedProcess?.Invoke(process.Id);

                using (process)
                {
                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    foreach (var line in SplitLines(stdout))
                    {
                        onStdOutLine(line);
                    }

                    foreach (var line in SplitLines(stderr))
                    {
                        onStdErrLine(line);
                    }

                    onExited(process.ExitCode);
                    return new DirectRunProcessLaunchResult
                    {
                        ProcessId = process.Id,
                        ExitedEarly = true,
                        ExitCode = process.ExitCode
                    };
                }
            }

            onStarted(Result.ProcessId);
            foreach (var line in StdOutLines)
            {
                onStdOutLine(line);
            }

            foreach (var line in StdErrLines)
            {
                onStdErrLine(line);
            }

            if (ExitCodeEvent is { } exitCode)
            {
                onExited(exitCode);
            }

            return Result;
        }

        private static IReadOnlyList<string> SplitLines(string content)
        {
            return content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

        public string GetPath(string relativePath)
        {
            return Path.Combine(rootPath, relativePath);
        }

        public string CreateExecutableFile(string relativePath, string content)
        {
            var path = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }

            return path;
        }

        public static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
        {
            var startedAt = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - startedAt < timeout)
            {
                if (predicate())
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100));
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
}
