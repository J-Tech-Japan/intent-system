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
        Assert.False(runner.InheritStandardInput);
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
    public void Launch_GivenReviewOutputSchemaPlaceholder_ExpandsSchemaPath()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 4321,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);
        var launchedAt = DateTimeOffset.Parse("2026-04-09T10:15:00Z");
        var suffix = DirectRunCommandSupport.CreateCapturedMessageSuffix(launchedAt);

        var result = launcher.Launch(
            "G19",
            "review",
            ".intent-cli/runs/G19.request.json",
            ".intent-cli/runs/G19.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "responses",
            "codex",
            [
                "exec",
                "--json",
                "--model",
                "{model}",
                "--output-schema",
                "{output_schema_path}",
                "--output-last-message",
                "{output_last_message_path}",
                "{prompt}"
            ],
            launchedAt,
            "/repo/.intent-cli/worktrees/G19",
            "/repo/.intent-cli/reviews/G19.request.json",
            tempDirectory.GetPath(".intent-cli/runs/G19.provider.jsonl"));

        Assert.Equal("pid:4321", result.ProviderSessionId);
        Assert.Contains(DirectRunDetachedCaptureCommand.CommandName, runner.Arguments);
        Assert.Contains("--output-schema", runner.Arguments);
        Assert.Contains($".intent-cli/runs/G19.{suffix}.review-output-schema.json", runner.Arguments);
        Assert.Contains("--output-last-message", runner.Arguments);
        Assert.Contains($".intent-cli/runs/G19.{suffix}.last-message.json", runner.Arguments);
    }

    [Fact]
    public void Launch_GivenReviewEntry_InjectsPromptThatKeepsPrCommentPublicationCanonical()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 4321,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        launcher.Launch(
            "G19",
            "review",
            ".intent-cli/runs/G19.request.json",
            ".intent-cli/runs/G19.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "responses",
            "codex",
            [
                "exec",
                "--json",
                "--model",
                "{model}",
                "{prompt}"
            ],
            DateTimeOffset.Parse("2026-04-09T10:15:00Z"),
            "/repo/.intent-cli/worktrees/G19",
            "/repo/.intent-cli/reviews/G19.request.json",
            tempDirectory.GetPath(".intent-cli/runs/G19.provider.jsonl"));

        var prompt = Assert.Single(runner.Arguments, argument => argument.Contains("bounded source of truth", StringComparison.Ordinal));
        Assert.Contains("Do not post GitHub or pull request comments", prompt, StringComparison.Ordinal);
        Assert.Contains("do not run 'gh pr comment'", prompt, StringComparison.Ordinal);
        Assert.Contains("the separate 'review comment' step owns PR comment publication", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_GivenFixEntry_InjectsPromptThatRequiresRepairOrDeterministicRefusal()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 4321,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        launcher.Launch(
            "G19",
            "fix",
            ".intent-cli/runs/G19.request.json",
            ".intent-cli/runs/G19.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "responses",
            "codex",
            [
                "exec",
                "--model",
                "{model}",
                "{prompt}"
            ],
            DateTimeOffset.Parse("2026-04-09T10:15:00Z"),
            "/repo/.intent-cli/worktrees/G19",
            "/repo/.intent-cli/fix/G19.request.md",
            tempDirectory.GetPath(".intent-cli/runs/G19.provider.jsonl"));

        var prompt = Assert.Single(runner.Arguments, argument => argument.Contains("bounded source of truth", StringComparison.Ordinal));
        Assert.Contains("Continue beyond initial repository inspection", prompt, StringComparison.Ordinal);
        Assert.Contains("complete the bounded repair attempt", prompt, StringComparison.Ordinal);
        Assert.Contains("deterministic refusal or contract-gap explanation", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not stop after a single inspection command", prompt, StringComparison.Ordinal);
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
        Assert.True(Path.IsPathRooted(runner.FileName));
        Assert.False(runner.InheritStandardInput);
        Assert.Contains(DirectRunDetachedCaptureCommand.CommandName, runner.Arguments);
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
        var backendExitEvent = events
            .Where(providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal))
            .Last();
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
        Directory.CreateDirectory(Path.GetDirectoryName(providerEventLogPath)!);
        File.WriteAllText(
            providerEventLogPath,
            string.Join(
                Environment.NewLine,
                DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-09T10:44:59.0000000+00:00",
                    ExecutionUnit = "G10",
                    Provider = "OpenAI",
                    EntryKind = "review",
                    SessionId = "pid:2468",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }),
                string.Empty));
        var runner = new FakeDirectRunProcessRunner
        {
            StdOutLines = ["pid:2468"],
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

        Assert.True(Path.IsPathRooted(runner.FileName));
        Assert.False(runner.InheritStandardInput);
        Assert.Contains(DirectRunDetachedCaptureCommand.CommandName, runner.Arguments);
        Assert.Equal("pid:2468", result.ProviderSessionId);

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        var backendExitEvent = events
            .Where(providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal))
            .Last();
        Assert.Equal("G10", backendExitEvent.ExecutionUnit);
        Assert.Equal("OpenAI", backendExitEvent.Provider);
        Assert.Equal("review", backendExitEvent.EntryKind);
        Assert.Equal("pid:2468", backendExitEvent.SessionId);
        Assert.Equal(0, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
        Assert.Equal(
            2,
            events.Count(providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)));
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
    public async Task Launch_GivenRepeatedRealWrappedCodexRuns_EachSessionAppendsOwnBackendExitProviderEvent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
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
        var observedSessions = new List<string>();

        for (var iteration = 1; iteration <= 5; iteration++)
        {
            var executionUnit = $"G12R{iteration}";
            var providerEventLogPath = tempDirectory.GetPath($".intent-cli/runs/{executionUnit}.provider.jsonl");
            var result = launcher.Launch(
                executionUnit,
                "review",
                $".intent-cli/runs/{executionUnit}.request.json",
                $".intent-cli/runs/{executionUnit}.provider.jsonl",
                "OpenAI",
                "gpt-5.4-mini",
                "responses",
                codexPath,
                ["exec", "test prompt"],
                DateTimeOffset.Parse("2026-04-09T11:05:00Z").AddMinutes(iteration),
                worktreePath,
                tempDirectory.GetPath($".intent-cli/reviews/{executionUnit}.request.json"),
                providerEventLogPath);

            observedSessions.Add(result.ProviderSessionId);

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

        Assert.Equal(observedSessions.Count, observedSessions.Distinct(StringComparer.Ordinal).Count());
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
    public async Task Launch_GivenWrappedCodexProcessEndsAfterCliReturns_DetachedCaptureAppendsBackendExitProviderEvent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14.provider.jsonl");
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
            "G14",
            "review",
            ".intent-cli/runs/G14.request.json",
            ".intent-cli/runs/G14.provider.jsonl",
            "OpenAI",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T11:15:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/reviews/G14.request.json"),
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
        Assert.Equal(0, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task Launch_GivenWrappedCodexProcess_DetachedCaptureClosesProviderStandardInput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14stdin.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var codexPath = tempDirectory.CreateExecutableFile(
            "repo/codex-experimental",
            """
            #!/bin/sh
            printf '%s\n' '{"type":"ready"}'
            if IFS= read -r _; then
                printf '%s\n' '{"type":"stdin-open"}'
            else
                printf '%s\n' '{"type":"stdin-eof"}'
            fi
            """);
        var launcher = new DirectRunLauncher();

        var result = launcher.Launch(
            "G14stdin",
            "review",
            ".intent-cli/runs/G14stdin.request.json",
            ".intent-cli/runs/G14stdin.provider.jsonl",
            "OpenAI",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T11:16:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/reviews/G14stdin.request.json"),
            providerEventLogPath);

        try
        {
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
                               && string.Equals(typeElement.GetString(), "stdin-eof", StringComparison.Ordinal)
                               && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal))
                           && events.Any(providerEvent =>
                               providerEvent.Kind == "provider-event"
                               && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                               && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                               && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                               && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
                },
                TimeSpan.FromSeconds(5));

            var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            Assert.DoesNotContain(events, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "stdin-open", StringComparison.Ordinal)
                && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            Assert.Contains(events, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "stdin-eof", StringComparison.Ordinal)
                && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            var backendExitEvent = Assert.Single(events, providerEvent =>
                providerEvent.Kind == "provider-event"
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            Assert.Equal(0, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
        }
        finally
        {
            if (TryParseProcessId(result.ProviderSessionId, out var processId) && IsProcessAlive(processId))
            {
                SignalProcess(processId, "TERM");
                TemporaryDirectory.WaitForCondition(() => !IsProcessAlive(processId), TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public async Task Launch_GivenWrappedCodexImplementProcess_DetachedCaptureProvidesTerminalToProvider()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14implement.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var codexPath = tempDirectory.CreateExecutableFile(
            "repo/codex-experimental",
            """
            #!/bin/sh
            if [ -t 0 ]; then
                printf '%s\n' '{"type":"tty-present"}'
            else
                printf '%s\n' '{"type":"tty-missing"}'
            fi
            sleep 1
            """);
        var launcher = new DirectRunLauncher();

        var result = launcher.Launch(
            "G14implement",
            "implement",
            ".intent-cli/runs/G14implement.request.json",
            ".intent-cli/runs/G14implement.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-09T11:17:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/implement/G14implement.request.md"),
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
                           && string.Equals(typeElement.GetString(), "tty-present", StringComparison.Ordinal)
                           && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal))
                       && events.Any(providerEvent =>
                           providerEvent.Kind == "provider-event"
                           && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                           && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                           && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                           && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(8));

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.DoesNotContain(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "tty-missing", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "tty-present", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        var backendExitEvent = Assert.Single(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Equal(0, backendExitEvent.Payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task Launch_GivenWrappedCodexImplementProcess_PreservesStandardInputLongEnoughForStartupActivity()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14implement-stdin.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var codexPath = tempDirectory.CreateExecutableFile(
            "repo/codex-experimental",
            """
            #!/bin/sh
            printf '%s\n' 'OpenAI Codex v0.118.0 (research preview)'
            printf '%s\n' '--------'
            printf '%s\n' "workdir: $PWD"
            printf '%s\n' 'user'
            /usr/bin/python3 -c 'import os,select,sys; fd = sys.stdin.fileno(); os.isatty(fd) or (print("tty-missing", flush=True), sys.exit(1)); readable, _, _ = select.select([sys.stdin], [], [], 0.2); data = os.read(fd, 1) if readable else None; data != b"" or (print("stdin-eof", flush=True), sys.exit(1)); print("pwd && rg --files .", flush=True); print("git status --short", flush=True)'
            sleep 1
            """);
        var launcher = new DirectRunLauncher();

        var result = launcher.Launch(
            "G14implement-stdin",
            "implement",
            ".intent-cli/runs/G14implement-stdin.request.json",
            ".intent-cli/runs/G14implement-stdin.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-16T21:00:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/implement/G14implement-stdin.request.md"),
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
                           && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                           && string.Equals(providerEvent.Payload.GetString(), "pwd && rg --files .", StringComparison.Ordinal)
                           && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal))
                       && events.Any(providerEvent =>
                           providerEvent.Kind == "provider-event"
                           && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                           && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                           && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                           && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(8));

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.DoesNotContain(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "stdin-eof", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.DoesNotContain(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "tty-missing", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "pwd && rg --files .", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "git status --short", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Launch_GivenWrappedCodexImplementProcess_PreservesStandardInputThroughDelayedStartup()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14implement-delayed.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var codexPath = tempDirectory.CreateExecutableFile(
            "repo/codex-experimental",
            """
            #!/bin/sh
            sleep 2
            /usr/bin/python3 -c 'import os,select,sys; fd = sys.stdin.fileno(); os.isatty(fd) or (print("tty-missing", flush=True), sys.exit(1)); readable, _, _ = select.select([sys.stdin], [], [], 0.2); data = os.read(fd, 1) if readable else None; data != b"" or (print("stdin-eof", flush=True), sys.exit(1)); print("delayed-startup-ok", flush=True)'
            sleep 1
            """);
        var launcher = new DirectRunLauncher();

        var result = launcher.Launch(
            "G14implement-delayed",
            "implement",
            ".intent-cli/runs/G14implement-delayed.request.json",
            ".intent-cli/runs/G14implement-delayed.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-17T02:10:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/implement/G14implement-delayed.request.md"),
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
                           && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                           && string.Equals(providerEvent.Payload.GetString(), "delayed-startup-ok", StringComparison.Ordinal)
                           && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal))
                       && events.Any(providerEvent =>
                           providerEvent.Kind == "provider-event"
                           && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                           && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                           && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                           && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(10));

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.DoesNotContain(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "stdin-eof", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.DoesNotContain(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "tty-missing", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "delayed-startup-ok", StringComparison.Ordinal)
            && string.Equals(providerEvent.SessionId, result.ProviderSessionId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Launch_GivenWrappedCodexFixProcess_PreservesStandardInputLongEnoughForBoundedRepoActivity()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14fix.provider.jsonl");
        var worktreePath = tempDirectory.GetPath("repo");
        Directory.CreateDirectory(worktreePath);
        var codexPath = tempDirectory.CreateExecutableFile(
            "repo/codex-experimental",
            """
            #!/bin/sh
            printf '%s\n' 'OpenAI Codex v0.118.0 (research preview)'
            printf '%s\n' '--------'
            printf '%s\n' "workdir: $PWD"
            printf '%s\n' 'user'
            /usr/bin/python3 -c 'import os,select,sys; fd = sys.stdin.fileno(); os.isatty(fd) or (print("tty-missing", flush=True), sys.exit(1)); readable, _, _ = select.select([sys.stdin], [], [], 0.2); data = os.read(fd, 1) if readable else None; data != b"" or (print("stdin-eof", flush=True), sys.exit(1)); print("pwd && rg --files .", flush=True); print("git status --short", flush=True); print("dotnet test", flush=True)'
            sleep 1
            """);
        var launcher = new DirectRunLauncher();

        var result = launcher.Launch(
            "G14fix",
            "fix",
            ".intent-cli/runs/G14fix.request.json",
            ".intent-cli/runs/G14fix.provider.jsonl",
            "Codex",
            "gpt-5.4-mini",
            "responses",
            codexPath,
            ["exec", "test prompt"],
            DateTimeOffset.Parse("2026-04-16T18:15:00Z"),
            worktreePath,
            tempDirectory.GetPath(".intent-cli/fix/G14fix.request.md"),
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
                           && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
                           && string.Equals(providerEvent.Payload.GetString(), "pwd && rg --files .", StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(8));

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.DoesNotContain(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "stdin-eof", StringComparison.Ordinal));
        Assert.DoesNotContain(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "tty-missing", StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "pwd && rg --files .", StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "git status --short", StringComparison.Ordinal));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(providerEvent.Payload.GetString(), "dotnet test", StringComparison.Ordinal));
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
            providerSessionId,
            DateTimeOffset.UtcNow));
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
    public async Task DirectRunExitMonitorCommand_GivenCurrentRunningResultArtifact_UpdatesRunStatusToFailed()
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
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14b-result.provider.jsonl");
        var requestArtifactPath = tempDirectory.GetPath(".intent-cli/runs/G14b-result.request.json");
        var resultArtifactPath = tempDirectory.GetPath(".intent-cli/runs/G14b-result.result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(providerEventLogPath)!);

        var launchedAt = DateTimeOffset.UtcNow;
        var providerSessionId = $"pid:{externalProcess!.Id}";
        File.WriteAllText(
            requestArtifactPath,
            DirectRunRequestArtifactJson.Serialize(new DirectRunRequestArtifact
            {
                SchemaVersion = "1",
                ExecutionUnit = "G14b-result",
                EntryKind = "fix",
                UpstreamRequestRef = ".intent-cli/fix/G14b-result.request.md",
                Provider = "Codex",
                Model = "gpt-5.4-mini",
                Transport = "responses",
                LaunchedAt = launchedAt.ToString("O"),
                ProviderSessionId = providerSessionId,
                TransportSummary = "launched via wrapper"
            }));
        File.WriteAllText(
            resultArtifactPath,
            DirectRunResultArtifactJson.Serialize(new DirectRunResultArtifact
            {
                SchemaVersion = "1",
                ExecutionUnit = "G14b-result",
                EntryKind = "fix",
                UpstreamRequestRef = ".intent-cli/fix/G14b-result.request.md",
                Provider = "Codex",
                Model = "gpt-5.4-mini",
                SessionId = providerSessionId,
                RunStatus = "running",
                RawLogRef = ".intent-cli/runs/G14b-result.provider.jsonl",
                PacketRef = ".intent-cli/issues/G14b-result/packet.yaml",
                ReviewContextRef = ".intent-cli/issues/G14b-result/review-context.md",
                Worktree = new DirectRunWorktreeContext
                {
                    Path = "/repo/.intent-cli/worktrees/G14b-result"
                }
            }));

        using var monitor = Process.Start(DirectRunExitMonitorCommand.CreateDetachedStartInfo(
            externalProcess.Id,
            providerEventLogPath,
            "G14b-result",
            "fix",
            "Codex",
            providerSessionId,
            launchedAt));
        Assert.NotNull(monitor);
        monitor!.StandardInput.Close();
        monitor.StandardOutput.Dispose();
        monitor.StandardError.Dispose();

        await TemporaryDirectory.WaitForConditionAsync(
            () =>
            {
                if (!File.Exists(resultArtifactPath))
                {
                    return false;
                }

                var artifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
                return string.Equals(artifact.RunStatus, "failed", StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
        Assert.Equal(providerSessionId, resultArtifact.SessionId);
        Assert.Equal("failed", resultArtifact.RunStatus);
    }

    [Fact]
    public void FinalizeDeadFixSessionIfCurrent_GivenStartupOnlyDeadSession_AppendsBackendExitAndFailsResult()
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
        externalProcess!.WaitForExit();

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14b-rescue.provider.jsonl");
        var requestArtifactPath = tempDirectory.GetPath(".intent-cli/runs/G14b-rescue.request.json");
        var resultArtifactPath = tempDirectory.GetPath(".intent-cli/runs/G14b-rescue.result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(providerEventLogPath)!);

        var launchedAt = DateTimeOffset.UtcNow;
        var providerSessionId = $"pid:{externalProcess.Id}";
        File.WriteAllText(
            requestArtifactPath,
            DirectRunRequestArtifactJson.Serialize(new DirectRunRequestArtifact
            {
                SchemaVersion = "1",
                ExecutionUnit = "G14b-rescue",
                EntryKind = "fix",
                UpstreamRequestRef = ".intent-cli/fix/G14b-rescue.request.md",
                Provider = "Codex",
                Model = "gpt-5.4-mini",
                Transport = "responses",
                LaunchedAt = launchedAt.ToString("O"),
                ProviderSessionId = providerSessionId,
                TransportSummary = "launched via wrapper"
            }));
        File.WriteAllText(
            resultArtifactPath,
            DirectRunResultArtifactJson.Serialize(new DirectRunResultArtifact
            {
                SchemaVersion = "1",
                ExecutionUnit = "G14b-rescue",
                EntryKind = "fix",
                UpstreamRequestRef = ".intent-cli/fix/G14b-rescue.request.md",
                Provider = "Codex",
                Model = "gpt-5.4-mini",
                SessionId = providerSessionId,
                RunStatus = "running",
                RawLogRef = ".intent-cli/runs/G14b-rescue.provider.jsonl",
                PacketRef = ".intent-cli/issues/G14b-rescue/packet.yaml",
                ReviewContextRef = ".intent-cli/issues/G14b-rescue/review-context.md",
                Worktree = new DirectRunWorktreeContext
                {
                    Path = "/repo/.intent-cli/worktrees/G14b-rescue"
                }
            }));

        var writer = new DirectRunProviderEventWriter(providerEventLogPath);
        writer.Append(new DirectRunProviderEvent
        {
            Timestamp = launchedAt.ToString("O"),
            ExecutionUnit = "G14b-rescue",
            Provider = "Codex",
            EntryKind = "fix",
            SessionId = providerSessionId,
            Kind = "provider-event",
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                "2026-04-17T08:10:00.000000Z  WARN codex_core::plugins::manifest: ignoring interface.defaultPrompt")
        });

        var updatedRunStatus = DirectRunTerminalArtifactUpdater.FinalizeDeadFixSessionIfCurrent(
            providerEventLogPath,
            "G14b-rescue",
            "fix",
            "Codex",
            providerSessionId,
            launchedAt,
            "running");

        Assert.Equal("failed", updatedRunStatus);

        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
        Assert.Equal("failed", resultArtifact.RunStatus);

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Contains(providerEvents, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
            && exitCodeElement.GetInt32() == 1);
    }

    [Fact]
    public void FinalizeDeadFixSessionIfCurrent_GivenDeepProgressDeadSession_AppendsContractGapAndFailsResult()
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
        externalProcess!.WaitForExit();

        using var tempDirectory = new TemporaryDirectory();
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14b-rescue-deep.provider.jsonl");
        var requestArtifactPath = tempDirectory.GetPath(".intent-cli/runs/G14b-rescue-deep.request.json");
        var resultArtifactPath = tempDirectory.GetPath(".intent-cli/runs/G14b-rescue-deep.result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(providerEventLogPath)!);

        var launchedAt = DateTimeOffset.UtcNow;
        var providerSessionId = $"pid:{externalProcess.Id}";
        File.WriteAllText(
            requestArtifactPath,
            DirectRunRequestArtifactJson.Serialize(new DirectRunRequestArtifact
            {
                SchemaVersion = "1",
                ExecutionUnit = "G14b-rescue-deep",
                EntryKind = "fix",
                UpstreamRequestRef = ".intent-cli/fix/G14b-rescue-deep.request.md",
                Provider = "Codex",
                Model = "gpt-5.4-mini",
                Transport = "responses",
                LaunchedAt = launchedAt.ToString("O"),
                ProviderSessionId = providerSessionId,
                TransportSummary = "launched via wrapper"
            }));
        File.WriteAllText(
            resultArtifactPath,
            DirectRunResultArtifactJson.Serialize(new DirectRunResultArtifact
            {
                SchemaVersion = "1",
                ExecutionUnit = "G14b-rescue-deep",
                EntryKind = "fix",
                UpstreamRequestRef = ".intent-cli/fix/G14b-rescue-deep.request.md",
                Provider = "Codex",
                Model = "gpt-5.4-mini",
                SessionId = providerSessionId,
                RunStatus = "running",
                RawLogRef = ".intent-cli/runs/G14b-rescue-deep.provider.jsonl",
                PacketRef = ".intent-cli/issues/G14b-rescue-deep/packet.yaml",
                ReviewContextRef = ".intent-cli/issues/G14b-rescue-deep/review-context.md",
                Worktree = new DirectRunWorktreeContext
                {
                    Path = "/repo/.intent-cli/worktrees/G14b-rescue-deep"
                }
            }));

        var writer = new DirectRunProviderEventWriter(providerEventLogPath);
        foreach (var payload in new[]
                 {
                     "exec",
                     "/bin/zsh -lc \"sed -n '1,220p' '/repo/.intent-cli/fix/G14b-rescue-deep.request.md'\"",
                     "exec",
                     "/bin/zsh -lc \"pwd && rg --files . | sed -n '1,200p'\"",
                     "exec",
                     "/bin/zsh -lc \"sed -n '1,220p' 'src/ToyCalc/Program.cs'\"",
                     "exec",
                     "/bin/zsh -lc \"dotnet test\""
                 })
        {
            writer.Append(new DirectRunProviderEvent
            {
                Timestamp = launchedAt.ToString("O"),
                ExecutionUnit = "G14b-rescue-deep",
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = providerSessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload)
            });
        }

        var updatedRunStatus = DirectRunTerminalArtifactUpdater.FinalizeDeadFixSessionIfCurrent(
            providerEventLogPath,
            "G14b-rescue-deep",
            "fix",
            "Codex",
            providerSessionId,
            launchedAt,
            "running");

        Assert.Equal("failed", updatedRunStatus);

        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
        Assert.Equal("failed", resultArtifact.RunStatus);

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        Assert.Contains(providerEvents, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "contract-gap", StringComparison.Ordinal));
        Assert.Contains(providerEvents, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DirectRunExitMonitorCommand_GivenStaleBackendExitForSamePid_AppendsFreshBackendExit()
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
        var providerEventLogPath = tempDirectory.GetPath(".intent-cli/runs/G14c.provider.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(providerEventLogPath)!);
        var providerSessionId = $"pid:{externalProcess!.Id}";
        var launchedAt = DateTimeOffset.UtcNow;
        File.WriteAllText(
            providerEventLogPath,
            string.Join(
                Environment.NewLine,
                DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                {
                    Timestamp = launchedAt.AddMinutes(-1).ToString("O"),
                    ExecutionUnit = "G14c",
                    Provider = "OpenAI",
                    EntryKind = "review",
                    SessionId = providerSessionId,
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }),
                string.Empty));

        using var monitor = Process.Start(DirectRunExitMonitorCommand.CreateDetachedStartInfo(
            externalProcess.Id,
            providerEventLogPath,
            "G14c",
            "review",
            "OpenAI",
            providerSessionId,
            launchedAt));
        Assert.NotNull(monitor);
        monitor!.StandardInput.Close();
        monitor.StandardOutput.Dispose();
        monitor.StandardError.Dispose();

        await TemporaryDirectory.WaitForConditionAsync(
            () =>
            {
                var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
                return events.Count(providerEvent =>
                           providerEvent.Kind == "provider-event"
                           && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                           && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                           && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                           && string.Equals(providerEvent.SessionId, providerSessionId, StringComparison.Ordinal)) == 2;
            },
            TimeSpan.FromSeconds(5));
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
            "pid:1234",
            DateTimeOffset.Parse("2026-04-09T11:18:00Z"));

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
    public void ShouldStartDetachedProviderExitMonitor_GivenHelperAndDifferentResolvedProviderPid_ReturnsTrue()
    {
        var shouldStart = DirectRunLauncher.ShouldStartDetachedProviderExitMonitor(
            usesDetachedCaptureHelper: true,
            startedProcessId: 1234,
            providerSessionId: "pid:5678",
            out var providerProcessId);

        Assert.True(shouldStart);
        Assert.Equal(5678, providerProcessId);
    }

    [Fact]
    public void ShouldStartDetachedProviderExitMonitor_GivenResolvedProviderPidMatchesHelperPid_ReturnsFalse()
    {
        var shouldStart = DirectRunLauncher.ShouldStartDetachedProviderExitMonitor(
            usesDetachedCaptureHelper: true,
            startedProcessId: 1234,
            providerSessionId: "pid:1234",
            out var providerProcessId);

        Assert.False(shouldStart);
        Assert.Equal(1234, providerProcessId);
    }

    [Fact]
    public void Launch_GivenWrappedCodexProcessTerminates_AppendsBackendExitProviderEvent()
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
            printf '%s\n' '{"type":"ready"}'
            sleep 1
            """);
        var launcher = new DirectRunLauncher();

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

        TemporaryDirectory.WaitForCondition(
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

    private static bool TryParseProcessId(string providerSessionId, out int processId)
    {
        processId = default;
        const string prefix = "pid:";
        return providerSessionId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(providerSessionId[prefix.Length..], out processId);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class FakeDirectRunProcessRunner : IDirectRunProcessRunner
    {
        public string WorkingDirectory { get; private set; } = string.Empty;

        public string FileName { get; private set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public bool InheritStandardInput { get; private set; }

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
            bool inheritStandardInput,
            bool keepStandardInputOpen,
            TimeSpan earlyExitWindow,
            Action<int> onStarted,
            Action<int> onExited,
            Action<string> onStdOutLine,
            Action<string> onStdErrLine)
        {
            WorkingDirectory = workingDirectory;
            FileName = fileName;
            Arguments = arguments.ToArray();
            InheritStandardInput = inheritStandardInput;

            if (ExecuteReceivedProcess)
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = !inheritStandardInput || keepStandardInputOpen,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                var process = System.Diagnostics.Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start wrapper process.");

                if (!inheritStandardInput && !keepStandardInputOpen)
                {
                    process.StandardInput.Close();
                }

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

        public static void WaitForCondition(Func<bool> predicate, TimeSpan timeout)
        {
            WaitForConditionAsync(predicate, timeout).GetAwaiter().GetResult();
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
