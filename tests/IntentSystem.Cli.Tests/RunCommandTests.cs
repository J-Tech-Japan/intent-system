using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class RunCommandTests
{
    [Fact]
    public void Execute_GivenNoActionableQueue_PersistsRootRunArtifactAndWritesSummary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = RunCommand.Execute(CreateContext(repoRoot), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Run orchestration processed.", output, StringComparison.Ordinal);
        Assert.Contains("Stop reason: no-actionable-item", output, StringComparison.Ordinal);
        Assert.Contains("Touched execution units: none", output, StringComparison.Ordinal);
        Assert.Contains("Reused child command refs: none", output, StringComparison.Ordinal);
        Assert.Contains("Root run result artifact: .intent-cli/run.result.json", output, StringComparison.Ordinal);

        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "run.result.json");
        Assert.True(File.Exists(artifactPath));
        var artifact = RunRootResultArtifactJson.Deserialize(File.ReadAllText(artifactPath));
        Assert.Equal("no-actionable-item", artifact.StopReason);
        Assert.Empty(artifact.TouchedExecutionUnits);
        Assert.Empty(artifact.ReusedChildCommandRefs);
        Assert.Null(artifact.ExecutionUnit);
    }

    [Fact]
    public void Execute_GivenReusedChildCommands_PersistsTouchedUnitsAndCommandRefs()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G226.request.md"),
            "# Execution Worker Handoff");
        WriteDirectRunResult(repoRoot, "G226", "implement", "succeeded");
        var originalRunSubmitExecutor = RunCommand.RunSubmitExecutor;
        var originalReviewRunExecutor = RunCommand.ReviewRunExecutor;
        using var writer = new StringWriter();

        try
        {
            RunCommand.RunSubmitExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Review
                    });

                return new RunSubmitResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                };
            };
            RunCommand.ReviewRunExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "review", "pid:226");
                WriteDirectRunResult(repoRoot, executionUnit, "review", "running");

                return new ReviewRunResult
                {
                    ExecutionUnit = executionUnit,
                    ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json",
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, "pid:226")
                };
            };

            var exitCode = RunCommand.Execute(CreateContext(repoRoot), [], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Touched execution units: G226", output, StringComparison.Ordinal);
            Assert.Contains("Reused child command refs: run submit, review run", output, StringComparison.Ordinal);

            var artifact = RunRootResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "run.result.json")));
            Assert.Equal("no-actionable-item", artifact.StopReason);
            Assert.Equal(["G226"], artifact.TouchedExecutionUnits);
            Assert.Equal(["run submit", "review run"], artifact.ReusedChildCommandRefs);
            Assert.Equal("G226", artifact.ExecutionUnit);
            Assert.Contains("Review direct run for 'G226' is 'running'.", artifact.Detail, StringComparison.Ordinal);
        }
        finally
        {
            RunCommand.RunSubmitExecutor = originalRunSubmitExecutor;
            RunCommand.ReviewRunExecutor = originalReviewRunExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenQueuedItem_ChainsDispatchStartImplementAndStopsAtWorkerMonitoring()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Queued, withLinkedIssue: false))));
        var originalQueueDispatchExecutor = RunCommand.QueueDispatchExecutor;
        var originalRunStartExecutor = RunCommand.RunStartExecutor;
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;

        try
        {
            RunCommand.QueueDispatchExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        LinkedIssue = new LinkedIssue
                        {
                            Repo = "J-Tech-Japan/intent-system",
                            Number = 226,
                            Url = "https://github.com/J-Tech-Japan/intent-system/issues/226"
                        }
                    });

                return new QueueDispatchCommandResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                    ReusedExistingIssue = false
                };
            };

            RunCommand.RunStartExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Active
                    });

                return new RunStartResult
                {
                    ExecutionUnit = executionUnit,
                    WorktreePath = Path.Combine(context.RepoRoot, ".intent-cli", "worktrees", executionUnit),
                    BranchName = $"issue-226-{executionUnit.ToLowerInvariant()}"
                };
            };

            RunCommand.RunImplementExecutor = (context, executionUnit) =>
            {
                tempDirectory.CreateFile(
                    Path.Combine("repo", ".intent-cli", "implement", $"{executionUnit}.request.md"),
                    "# Execution Worker Handoff");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            RunCommand.RunSuperviseExecutor = (_, executionUnit) => new RunSuperviseResult
            {
                ExecutionUnit = executionUnit,
                SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                SessionStatus = RunSupervisionSessionStatus.Monitoring,
                RetryCount = 0,
                RetryBudget = 3,
                HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Collection(
                result.Actions,
                action =>
                {
                    Assert.Equal("queue dispatch", action.Name);
                    Assert.Equal("G226", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run start", action.Name);
                    Assert.Equal("G226", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run implement", action.Name);
                    Assert.Equal("G226", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run supervise", action.Name);
                    Assert.Equal("G226", action.ExecutionUnit);
                });
        }
        finally
        {
            RunCommand.QueueDispatchExecutor = originalQueueDispatchExecutor;
            RunCommand.RunStartExecutor = originalRunStartExecutor;
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenReviewItemWithoutRequest_GeneratesReviewRequestAndStopsForReviewDecision()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        var originalReviewRunExecutor = RunCommand.ReviewRunExecutor;

        try
        {
            RunCommand.ReviewRunExecutor = (_, executionUnit) => new ReviewRunResult
            {
                ExecutionUnit = executionUnit,
                ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json",
                DirectRun = CreateDirectRunLaunchResult(executionUnit, "pid:226")
            };
            WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review run", action.Name);
            Assert.Equal("G226", action.ExecutionUnit);
            Assert.Contains("no direct run result is available yet", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            RunCommand.ReviewRunExecutor = originalReviewRunExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenReviewLaunchAndStaleImplementResult_IgnoresStaleArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        WriteDirectRunResult(repoRoot, "G226", "implement", "succeeded");
        var originalReviewRunExecutor = RunCommand.ReviewRunExecutor;

        try
        {
            RunCommand.ReviewRunExecutor = (_, executionUnit) => new ReviewRunResult
            {
                ExecutionUnit = executionUnit,
                ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json",
                DirectRun = CreateDirectRunLaunchResult(executionUnit, "pid:226")
            };
            WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review run", action.Name);
            Assert.Contains("no direct run result is available yet", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            RunCommand.ReviewRunExecutor = originalReviewRunExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenSucceededImplementRun_ReusesRunSubmitBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G226.request.md"),
            "# Execution Worker Handoff");
        WriteDirectRunResult(repoRoot, "G226", "implement", "succeeded");
        var originalRunSubmitExecutor = RunCommand.RunSubmitExecutor;
        var originalReviewRunExecutor = RunCommand.ReviewRunExecutor;

        try
        {
            RunCommand.RunSubmitExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Review
                    });

                return new RunSubmitResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                };
            };
            RunCommand.ReviewRunExecutor = (_, executionUnit) => new ReviewRunResult
            {
                ExecutionUnit = executionUnit,
                ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json"
            };
            RunCommand.ReviewRunExecutor = (_, executionUnit) =>
            {
                WriteDirectRunResult(repoRoot, executionUnit, "review", "running");

                return new ReviewRunResult
                {
                    ExecutionUnit = executionUnit,
                    ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Collection(
                result.Actions,
                action =>
                {
                    Assert.Equal("run submit", action.Name);
                    Assert.Equal("G226", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("review run", action.Name);
                    Assert.Equal("G226", action.ExecutionUnit);
                });
        }
        finally
        {
            RunCommand.RunSubmitExecutor = originalRunSubmitExecutor;
            RunCommand.ReviewRunExecutor = originalReviewRunExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenAcceptedReviewDecision_ReusesReviewAcceptBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(repoRoot, "G226", "review", "accepted");
        var originalReviewAcceptExecutor = RunCommand.ReviewAcceptExecutor;

        try
        {
            RunCommand.ReviewAcceptExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Completed
                    });

                return new ReviewAcceptResult
                {
                    ExecutionUnit = executionUnit,
                    MergedPrRef = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                    ClosedIssueRef = "https://github.com/J-Tech-Japan/intent-system/issues/226"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Null(result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review accept", action.Name);
            Assert.Equal("G226", action.ExecutionUnit);
        }
        finally
        {
            RunCommand.ReviewAcceptExecutor = originalReviewAcceptExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenSucceededReviewDecisionWithoutExplicitOutcome_Waits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "succeeded",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "ReviewBot",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 0
                    })
                }
            ]);

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("Review direct run for 'G226' is 'succeeded'.", result.Detail, StringComparison.Ordinal);

        var artifact = DirectRunResultArtifactJson.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
        Assert.Null(artifact.ReviewOutcome);
    }

    [Fact]
    public void ExecuteCore_GivenSucceededReviewDecisionWithExplicitAcceptedOutcome_ReusesReviewAcceptBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "succeeded",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "ReviewBot",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        disposition = "accepted"
                    })
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "ReviewBot",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 0
                    })
                }
            ]);
        var originalReviewAcceptExecutor = RunCommand.ReviewAcceptExecutor;

        try
        {
            RunCommand.ReviewAcceptExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Completed
                    });

                return new ReviewAcceptResult
                {
                    ExecutionUnit = executionUnit,
                    MergedPrRef = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                    ClosedIssueRef = "https://github.com/J-Tech-Japan/intent-system/issues/226"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Null(result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review accept", action.Name);
            Assert.Equal("G226", action.ExecutionUnit);
        }
        finally
        {
            RunCommand.ReviewAcceptExecutor = originalReviewAcceptExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenSucceededReviewDecisionWithOnlyStaleAcceptedOutcome_Waits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "succeeded",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T11:59:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "ReviewBot",
                    EntryKind = "review",
                    SessionId = "pid:stale",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        disposition = "accepted"
                    })
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "ReviewBot",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 0
                    })
                }
            ]);

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("Review direct run for 'G226' is 'succeeded'.", result.Detail, StringComparison.Ordinal);

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl")));
        Assert.DoesNotContain(providerEvents, providerEvent =>
            string.Equals(providerEvent.SessionId, "pid:226", StringComparison.Ordinal)
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("disposition", out _));
    }

    [Fact]
    public void ExecuteCore_GivenRunningReviewDecisionWithBackendExitFailure_FailsAndPersistsNormalizedResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "ReviewBot",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ]);

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("non-retryable-failure", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("Review direct run failed for 'G226'.", result.Detail, StringComparison.Ordinal);

        var artifact = DirectRunResultArtifactJson.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
        Assert.Equal("failed", artifact.RunStatus);
    }

    [Fact]
    public void ExecuteCore_GivenRunningReviewDecisionWithExitedCurrentSession_AppendsBackendExitAndWaits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:999999", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "review",
                    SessionId = "pid:999999",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "ready"
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Codex");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("Review direct run for 'G226' is 'succeeded'.", result.Detail, StringComparison.Ordinal);

        var events = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl")));
        Assert.Contains(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal));
        Assert.DoesNotContain(events, providerEvent =>
            providerEvent.Kind == "provider-event"
            && string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("disposition", out _));
        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
        Assert.Equal("succeeded", resultArtifact.RunStatus);
        Assert.Null(resultArtifact.ReviewOutcome);
    }

    [Fact]
    public void ExecuteCore_GivenSucceededReviewDecisionWithPersistedCommentOutcome_ReusesReviewCommentBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.md"),
            "Please cover the deterministic submit path.");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "succeeded",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "ReviewBot",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 0
                    })
                }
            ],
            reviewOutcome: "fix-requested",
            reviewCommentBodyPath: ".intent-cli/reviews/G226.comment.md");
        var originalReviewCommentExecutor = RunCommand.ReviewCommentExecutor;

        try
        {
            RunCommand.ReviewCommentExecutor = (context, executionUnit, bodyPath) =>
            {
                Assert.Equal(".intent-cli/reviews/G226.comment.md", bodyPath);
                Assert.Equal(
                    "Please cover the deterministic submit path.",
                    File.ReadAllText(Path.Combine(context.RepoRoot, bodyPath.Replace('/', Path.DirectorySeparatorChar))));

                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Fixing
                    });

                return new ReviewCommentResult
                {
                    ExecutionUnit = executionUnit,
                    ArtifactPath = ".intent-cli/reviews/G226.comment.json",
                    CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("deterministic-contract-gap", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review comment", action.Name);
            Assert.Equal("G226", action.ExecutionUnit);
            Assert.Contains("requires .intent-cli/reviews/G226.comment.json", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            RunCommand.ReviewCommentExecutor = originalReviewCommentExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenSucceededReviewDecisionWithCapturedAcceptedLastMessage_ReusesReviewAcceptBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", "G226.last-message.json"),
            """{"disposition":"accepted"}""");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "succeeded",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 0
                    })
                }
            ],
            provider: "Codex");
        var originalReviewAcceptExecutor = RunCommand.ReviewAcceptExecutor;

        try
        {
            RunCommand.ReviewAcceptExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Completed
                    });

                return new ReviewAcceptResult
                {
                    ExecutionUnit = executionUnit,
                    MergedPrRef = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                    ClosedIssueRef = "https://github.com/J-Tech-Japan/intent-system/issues/226"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Null(result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review accept", action.Name);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("accepted", resultArtifact.ReviewOutcome);
        }
        finally
        {
            RunCommand.ReviewAcceptExecutor = originalReviewAcceptExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenSucceededReviewDecisionWithCapturedCommentLastMessage_ReusesReviewCommentBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", "G226.last-message.json"),
            """{"disposition":"fix-requested","comment_body":"Please cover the deterministic submit path."}""");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "succeeded",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 0
                    })
                }
            ],
            provider: "Codex");
        var originalReviewCommentExecutor = RunCommand.ReviewCommentExecutor;

        try
        {
            RunCommand.ReviewCommentExecutor = (context, executionUnit, bodyPath) =>
            {
                Assert.Equal(".intent-cli/reviews/G226.comment.md", bodyPath);
                Assert.Equal(
                    "Please cover the deterministic submit path.",
                    File.ReadAllText(Path.Combine(context.RepoRoot, bodyPath.Replace('/', Path.DirectorySeparatorChar))));

                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Fixing
                    });

                return new ReviewCommentResult
                {
                    ExecutionUnit = executionUnit,
                    ArtifactPath = ".intent-cli/reviews/G226.comment.json",
                    CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("deterministic-contract-gap", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review comment", action.Name);
        }
        finally
        {
            RunCommand.ReviewCommentExecutor = originalReviewCommentExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenCommentReviewDecisionWithCommentBody_ReusesReviewCommentBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "fix-requested",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "ReviewBot",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        disposition = "fix-requested",
                        comment_body = "Please cover the deterministic submit path."
                    })
                }
            ]);
        var originalReviewCommentExecutor = RunCommand.ReviewCommentExecutor;

        try
        {
            RunCommand.ReviewCommentExecutor = (context, executionUnit, bodyPath) =>
            {
                Assert.Equal(".intent-cli/reviews/G226.comment.md", bodyPath);
                Assert.Equal(
                    "Please cover the deterministic submit path.",
                    File.ReadAllText(Path.Combine(context.RepoRoot, bodyPath.Replace('/', Path.DirectorySeparatorChar))));

                PersistQueueState(
                    context.RepoRoot,
                    queueItem => queueItem with
                    {
                        State = QueueItemState.Fixing
                    });

                return new ReviewCommentResult
                {
                    ExecutionUnit = executionUnit,
                    ArtifactPath = ".intent-cli/reviews/G226.comment.json",
                    CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("deterministic-contract-gap", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review comment", action.Name);
            Assert.Equal("G226", action.ExecutionUnit);
            Assert.Contains("requires .intent-cli/reviews/G226.comment.json", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            RunCommand.ReviewCommentExecutor = originalReviewCommentExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenStaleReviewResultForDifferentSession_WaitsForCurrentBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:current");
        WriteDirectRunResult(repoRoot, "G226", "review", "accepted", sessionId: "pid:stale");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("does not match the current launched request boundary", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteCore_GivenLegacyReviewResultWithoutUpstreamRequestRef_WaitsForCurrentBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:current");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", "G226.result.json"),
            """
            {
              "schema_version": "1",
              "execution_unit": "G226",
              "entry_kind": "review",
              "provider": "ReviewBot",
              "model": "gpt-5.4-mini",
              "session_id": "pid:current",
              "run_status": "accepted",
              "raw_log_ref": ".intent-cli/runs/G226.provider.jsonl",
              "packet_ref": ".intent-cli/issues/G226/packet.yaml",
              "review_context_ref": ".intent-cli/issues/G226/review-context.md",
              "linked_pr": {
                "repo": "J-Tech-Japan/intent-system",
                "number": 226,
                "url": "https://github.com/J-Tech-Japan/intent-system/pull/226"
              },
              "worktree": {
                "path": "/repo/.intent-cli/worktrees/G226"
              }
            }
            """);

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("does not match the current launched request boundary", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteCore_GivenClarifyBlockedItem_StopsWithClarificationRequired()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.ClarifyBlocked))));

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("clarification-required", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Contains("intents/intent-cli/clarifications/open.md", result.Detail, StringComparison.Ordinal);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void ExecuteCore_GivenMultipleInProgressItems_StopsWithParallelWorkDetected()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(
                new QueueState
                {
                    SchemaVersion = "intent-cli/queue-state/v1",
                    UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:00:00Z"),
                    Items =
                    [
                        CreateQueueItem(QueueItemState.Active, executionUnit: "G226"),
                        CreateQueueItem(QueueItemState.Review, executionUnit: "G227")
                    ]
                }));

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("deterministic-contract-gap", result.StopReason);
        Assert.Contains("G226", result.Detail, StringComparison.Ordinal);
        Assert.Contains("G227", result.Detail, StringComparison.Ordinal);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void ExecuteCore_GivenBlockedItem_StopsWithParentIntentUpdateRequired()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Blocked))));

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("parent-intent-update-required", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
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
                    Implement = "Codex",
                    Review = "Codex"
                },
                Supervision = new SupervisionConfig
                {
                    ArtifactRoot = ".intent-cli/supervision",
                    StaleHeartbeatTimeoutMinutes = 15,
                    RetryDelayMinutes = 5,
                    RetryBudget = 3
                },
                DirectRun = new DirectRunConfig
                {
                    Provider = "openai",
                    Model = "gpt-5.4",
                    Transport = "responses",
                    Implement = new DirectRunEntryConfig
                    {
                        Command = "codex",
                        Args = []
                    },
                    Fix = new DirectRunEntryConfig
                    {
                        Command = "codex",
                        Args = []
                    },
                    Review = new DirectRunEntryConfig
                    {
                        Command = "codex",
                        Args = []
                    }
                }
            }
        };
    }

    private static QueueState CreateQueueState(params QueueItem[] items)
    {
        return new QueueState
        {
            SchemaVersion = "intent-cli/queue-state/v1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:00:00Z"),
            Items = items
        };
    }

    private static QueueItem CreateQueueItem(
        QueueItemState state,
        string executionUnit = "G226",
        LinkedIssue? linkedIssue = null,
        bool withLinkedIssue = true)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = "[G226] Root Run Orchestration Command",
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
                ? linkedIssue ?? new LinkedIssue
                {
                    Repo = "J-Tech-Japan/intent-system",
                    Number = 226,
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/226"
                }
                : null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "P1"
        };
    }

    private static void PersistQueueState(string repoRoot, Func<QueueItem, QueueItem> update)
    {
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var updatedState = queueState with
        {
            Items = queueState.Items.Select(update).ToArray()
        };

        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));
    }

    private static RunImplementRequest CreateRunImplementRequest(string repoRoot, string executionUnit)
    {
        return new RunImplementRequest
        {
            ExecutionUnit = executionUnit,
            State = "active",
            ImplementRole = "Codex",
            QueueWorkerRole = "coder",
            QueueReviewRole = "reviewer",
            WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
            ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
            Branch = $"issue-226-{executionUnit.ToLowerInvariant()}",
            LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
            LatestLinkedPr = null,
            PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
            ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
            IssueTitle = "[G226] Root Run Orchestration Command",
            Goal = "Coordinate the root run loop.",
            TargetPart = "run command",
            TargetRepo = "submodules/intent-system",
            TargetPath = ".",
            InScope = [],
            OutOfScope = [],
            AcceptanceCriteria = [],
            DeterministicReviewChecks = [],
            ExpectedEvidence = []
        };
    }

    private static void WriteDirectRunResult(
        string repoRoot,
        string executionUnit,
        string entryKind,
        string runStatus,
        IReadOnlyList<DirectRunProviderEvent>? providerEvents = null,
        string sessionId = "pid:226",
        string provider = "ReviewBot",
        string? reviewOutcome = null,
        string? reviewCommentBodyPath = null)
    {
        var runsDirectory = Path.Combine(repoRoot, ".intent-cli", "runs");
        Directory.CreateDirectory(runsDirectory);

        File.WriteAllText(
            Path.Combine(runsDirectory, $"{executionUnit}.result.json"),
            DirectRunResultArtifactJson.Serialize(
                new DirectRunResultArtifact
                {
                    SchemaVersion = "1",
                    ExecutionUnit = executionUnit,
                    EntryKind = entryKind,
                    UpstreamRequestRef = ResolveUpstreamRequestRef(executionUnit, entryKind),
                    Provider = provider,
                    Model = "gpt-5.4-mini",
                    SessionId = sessionId,
                    RunStatus = runStatus,
                    ReviewOutcome = reviewOutcome,
                    ReviewCommentBodyPath = reviewCommentBodyPath,
                    RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                    PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                    ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                    LinkedIssue = new DirectRunLinkedIssueContext
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 226,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/226"
                    },
                    LinkedPr = new DirectRunLinkedPullRequestContext
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 226,
                        Url = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                    },
                    Worktree = new DirectRunWorktreeContext
                    {
                        Path = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }
                }));

        if (providerEvents is null)
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(runsDirectory, $"{executionUnit}.provider.jsonl"),
            string.Join(Environment.NewLine, providerEvents.Select(DirectRunProviderEventJsonl.SerializeLine)) + Environment.NewLine);
    }

    private static void WriteDirectRunRequest(
        string repoRoot,
        string executionUnit,
        string entryKind,
        string providerSessionId,
        string provider = "ReviewBot")
    {
        var runsDirectory = Path.Combine(repoRoot, ".intent-cli", "runs");
        Directory.CreateDirectory(runsDirectory);

        File.WriteAllText(
            Path.Combine(runsDirectory, $"{executionUnit}.request.json"),
            DirectRunRequestArtifactJson.Serialize(
                new DirectRunRequestArtifact
                {
                    SchemaVersion = "1",
                    ExecutionUnit = executionUnit,
                    EntryKind = entryKind,
                    UpstreamRequestRef = ResolveUpstreamRequestRef(executionUnit, entryKind),
                    Provider = provider,
                    Model = "gpt-5.4-mini",
                    Transport = "responses",
                    LaunchedAt = "2026-04-10T12:00:00.0000000+00:00",
                    ProviderSessionId = providerSessionId,
                    TransportSummary = "launched"
                }));
    }

    private static DirectRunLaunchResult CreateDirectRunLaunchResult(string executionUnit, string providerSessionId)
    {
        return new DirectRunLaunchResult
        {
            RequestArtifactPath = $".intent-cli/runs/{executionUnit}.request.json",
            ProviderEventLogPath = $".intent-cli/runs/{executionUnit}.provider.jsonl",
            ResultArtifactPath = $".intent-cli/runs/{executionUnit}.result.json",
            Provider = "ReviewBot",
            Model = "gpt-5.4-mini",
            Transport = "responses",
            ProviderSessionId = providerSessionId,
            TransportSummary = "launched",
            RunStatus = "running"
        };
    }

    private static string ResolveUpstreamRequestRef(string executionUnit, string entryKind)
    {
        return entryKind switch
        {
            "implement" => $".intent-cli/implement/{executionUnit}.request.md",
            "fix" => $".intent-cli/fix/{executionUnit}.request.md",
            "review" => $".intent-cli/reviews/{executionUnit}.request.json",
            _ => throw new InvalidOperationException($"Unsupported entry kind '{entryKind}'.")
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
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
