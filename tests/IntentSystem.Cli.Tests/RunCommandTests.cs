using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Review.Serialization;
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
        var launchedAt = DateTimeOffset.Parse("2026-04-10T12:00:00.0000000+00:00");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", CreateCapturedLastMessageFileName("G226", launchedAt)),
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
        var launchedAt = DateTimeOffset.Parse("2026-04-10T12:00:00.0000000+00:00");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", CreateCapturedLastMessageFileName("G226", launchedAt)),
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
    public void ExecuteCore_GivenCurrentReviewSessionAlreadyPublishedComment_DoesNotDuplicatePublicationDuringRootRun()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            """
            {
              "execution_unit": "G226",
              "review_context_ref": ".intent-cli/issues/G226/review-context.md",
              "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/226",
              "deterministic_review_checks": [],
              "acceptance_criteria": [],
              "expected_evidence": []
            }
            """);
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
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                        "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2")
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:02.0000000+00:00",
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
        var originalPublisherFactory = ReviewCommentCommand.PublisherFactory;
        var publisher = new FakeReviewCommentPublisher();

        try
        {
            ReviewCommentCommand.PublisherFactory = () => publisher;

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("deterministic-contract-gap", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Equal(0, publisher.CallCount);

            var reviewCommentArtifact = ReviewCommentArtifactSerializer.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "reviews", "G226.comment.json")));
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2", reviewCommentArtifact.CommentRef);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2", resultArtifact.ReviewCommentRef);
        }
        finally
        {
            ReviewCommentCommand.PublisherFactory = originalPublisherFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFailedReviewResultWithRawNoActionableItem_Accepts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "failed",
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
                        stop_reason = "no-actionable-item"
                    })
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:02.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 137
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
            Assert.Equal("succeeded", resultArtifact.RunStatus);
            Assert.Equal("accepted", resultArtifact.ReviewOutcome);
        }
        finally
        {
            RunCommand.ReviewAcceptExecutor = originalReviewAcceptExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFailedReviewResultWithRawDeterministicContractGap_Comments()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "review",
            "failed",
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
                        stop_reason = "deterministic-contract-gap",
                        detail = "Please cover the deterministic submit path."
                    })
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:02.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "review",
                    SessionId = "pid:226",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 137
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

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("succeeded", resultArtifact.RunStatus);
            Assert.Equal("fix-requested", resultArtifact.ReviewOutcome);
            Assert.Equal(".intent-cli/reviews/G226.comment.md", resultArtifact.ReviewCommentBodyPath);
        }
        finally
        {
            RunCommand.ReviewCommentExecutor = originalReviewCommentExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenSucceededReviewDecisionWithOnlyStaleCapturedAcceptedLastMessage_Waits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var staleLaunchedAt = DateTimeOffset.Parse("2026-04-10T11:59:00.0000000+00:00");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", CreateCapturedLastMessageFileName("G226", staleLaunchedAt)),
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

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("Review direct run for 'G226' is 'succeeded'.", result.Detail, StringComparison.Ordinal);

        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
        Assert.Null(resultArtifact.ReviewOutcome);
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
    public void ExecuteCore_GivenFixingItemWithFailedFixResultBeforeOperatorRetry_LaunchesFreshFixAttempt()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            {"ts":"2026-04-10T12:05:00Z","execution_unit":"G226","event":"blocked","by":"intent-cli","reason":"backend exit code 1"}
            {"ts":"2026-04-10T12:10:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:94914", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "failed",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:01:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "fix",
                    SessionId = "pid:94914",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ],
            sessionId: "pid:94914",
            provider: "Claude");
        var originalRunFixExecutor = RunCommand.RunFixExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;

        try
        {
            RunCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "fix", "pid:4242", provider: "Claude");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:10:01.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Claude",
                            EntryKind = "fix",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "gpt-5.4-mini",
                                transport = "responses",
                                command = "claude"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Claude");

                return new RunFixResult
                {
                    Request = new RunFixRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "fixing",
                        ImplementRole = "Codex",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = $"issue-226-{executionUnit.ToLowerInvariant()}",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        LatestLinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                        LatestCommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        ReviewCommentArtifactRef = $".intent-cli/reviews/{executionUnit}.comment.json",
                        ReviewRequestRef = $".intent-cli/reviews/{executionUnit}.request.json",
                        ReviewCommentBodyPath = $".intent-cli/reviews/{executionUnit}.comment.md",
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
                    },
                    ArtifactPath = $".intent-cli/fix/{executionUnit}.request.md",
                    DirectRun = new DirectRunLaunchResult
                    {
                        RequestArtifactPath = $".intent-cli/runs/{executionUnit}.request.json",
                        ProviderEventLogPath = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultArtifactPath = $".intent-cli/runs/{executionUnit}.result.json",
                        Provider = "Claude",
                        Model = "gpt-5.4-mini",
                        Transport = "responses",
                        ProviderSessionId = "pid:4242",
                        TransportSummary = "launched"
                    }
                };
            };
            RunCommand.RunSuperviseExecutor = (_, executionUnit) => new RunSuperviseResult
            {
                ExecutionUnit = executionUnit,
                SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                WorkerEntry = RunSupervisionWorkerEntry.Fix,
                SessionStatus = RunSupervisionSessionStatus.Monitoring,
                RetryCount = 0,
                RetryBudget = 3,
                HandoffArtifactRef = $".intent-cli/fix/{executionUnit}.request.md"
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Equal(2, result.Actions.Count);
            Assert.Equal("run fix", result.Actions[0].Name);
            Assert.Equal("run supervise", result.Actions[1].Name);
            Assert.Contains("under supervision", result.Detail, StringComparison.Ordinal);

            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.request.json")));
            Assert.Equal("pid:4242", requestArtifact.ProviderSessionId);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);
        }
        finally
        {
            RunCommand.RunFixExecutor = originalRunFixExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithInspectionOnlyBackendExitFailure_StopsWithDeterministicContractGap()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999999", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "failed",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "gpt-5.4-mini",
                        transport = "responses",
                        command = "codex"
                    })
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:999999",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                        "exec /bin/zsh -lc 'rg --files' succeeded in 0ms")
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:02.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:999999",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Codex");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("deterministic-contract-gap", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("initial repo-inspection command", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Fix direct run failed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithStaleImplementSupervisionSession_RealignsAndContinuesMonitoring()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Monitoring,
                QueueState = "active",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                CommentRef = null,
                HandoffArtifactRef = ".intent-cli/implement/G226.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunResult(repoRoot, "G226", "fix", "running");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        var action = Assert.Single(result.Actions);
        Assert.Equal("run supervise", action.Name);
        Assert.Equal("G226", action.ExecutionUnit);
        Assert.Contains("Worker remains under supervision.", result.Detail, StringComparison.Ordinal);

        var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
        Assert.Equal(RunSupervisionWorkerEntry.Fix, session.WorkerEntry);
        Assert.Equal("fixing", session.QueueState);
        Assert.Equal(".intent-cli/fix/G226.request.md", session.HandoffArtifactRef);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/226", session.LinkedPr);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2", session.CommentRef);
    }

    [Fact]
    public void ExecuteCore_GivenDeadFixWorkerSession_DoesNotRemainUnderSupervision()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Fix,
                Status = RunSupervisionSessionStatus.Monitoring,
                QueueState = "fixing",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                HandoffArtifactRef = ".intent-cli/fix/G226.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "fix",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "sonnet",
                        transport = "sdk",
                        command = "claude"
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRunFixExecutor = RunSuperviseCommand.RunFixExecutor;

        try
        {
            RunSuperviseCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "fix", "pid:4242", provider: "Claude");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:01:00.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Claude",
                            EntryKind = "fix",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "sonnet",
                                transport = "sdk",
                                command = "claude"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Claude");

                return new RunFixResult
                {
                    Request = new RunFixRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "fixing",
                        ImplementRole = "Codex",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = $"issue-226-{executionUnit.ToLowerInvariant()}",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        LatestLinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                        LatestCommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        ReviewCommentArtifactRef = $".intent-cli/reviews/{executionUnit}.comment.json",
                        ReviewRequestRef = $".intent-cli/reviews/{executionUnit}.request.json",
                        ReviewCommentBodyPath = $".intent-cli/reviews/{executionUnit}.comment.md",
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
                    },
                    ArtifactPath = $".intent-cli/fix/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("run supervise", action.Name);
            Assert.DoesNotContain("Worker remains under supervision.", result.Detail, StringComparison.Ordinal);
            Assert.Contains("auto-resumed", result.Detail, StringComparison.OrdinalIgnoreCase);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);
        }
        finally
        {
            RunSuperviseCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenDeadImplementWorkerSession_DoesNotRemainUnderSupervision()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G226.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Monitoring,
                QueueState = "active",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = null,
                CommentRef = null,
                HandoffArtifactRef = ".intent-cli/implement/G226.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "implement", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "implement",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "implement",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "sonnet",
                        transport = "sdk",
                        command = "claude"
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRunImplementExecutor = RunSuperviseCommand.RunImplementExecutor;

        try
        {
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "implement", "pid:4242", provider: "Claude");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:01:00.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Claude",
                            EntryKind = "implement",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "sonnet",
                                transport = "sdk",
                                command = "claude"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Claude");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit) with
                    {
                        ImplementRole = "Claude"
                    },
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("run supervise", action.Name);
            Assert.DoesNotContain("Worker remains under supervision.", result.Detail, StringComparison.Ordinal);
            Assert.Contains("auto-resumed", result.Detail, StringComparison.OrdinalIgnoreCase);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);
        }
        finally
        {
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenDeadImplementWorkerSessionWithCapturedBackendExit_DoesNotReportMissingTerminalEvent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G226.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Monitoring,
                QueueState = "active",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = null,
                CommentRef = null,
                HandoffArtifactRef = ".intent-cli/implement/G226.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "implement", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "implement",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "implement",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "sonnet",
                        transport = "sdk",
                        command = "claude"
                    })
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "implement",
                    SessionId = "pid:999999",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRunImplementExecutor = RunSuperviseCommand.RunImplementExecutor;

        try
        {
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "implement", "pid:4242", provider: "Claude");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:01:00.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Claude",
                            EntryKind = "implement",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "sonnet",
                                transport = "sdk",
                                command = "claude"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Claude");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit) with
                    {
                        ImplementRole = "Claude"
                    },
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Contains("auto-resumed", result.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("no terminal provider event was captured", result.Detail, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
        }
    }

    [Fact]
    public async Task ExecuteCore_GivenDeadImplementWorkerSessionWhenBackendExitLandsDuringRaceWindow_DoesNotReportMissingTerminalEvent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G226.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Monitoring,
                QueueState = "active",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = null,
                CommentRef = null,
                HandoffArtifactRef = ".intent-cli/implement/G226.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "implement", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "implement",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "implement",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "sonnet",
                        transport = "sdk",
                        command = "claude"
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRunImplementExecutor = RunSuperviseCommand.RunImplementExecutor;
        var originalRaceWindow = RunSuperviseCommand.TerminalFailureRaceWindow;
        var originalRacePollInterval = RunSuperviseCommand.TerminalFailureRacePollInterval;

        try
        {
            RunSuperviseCommand.TerminalFailureRaceWindow = TimeSpan.FromMilliseconds(200);
            RunSuperviseCommand.TerminalFailureRacePollInterval = TimeSpan.FromMilliseconds(5);
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "implement", "pid:4242", provider: "Claude");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:01:00.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Claude",
                            EntryKind = "implement",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "sonnet",
                                transport = "sdk",
                                command = "claude"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Claude");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit) with
                    {
                        ImplementRole = "Claude"
                    },
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var appendTask = Task.Run(async () =>
            {
                await Task.Delay(20);
                new DirectRunProviderEventWriter(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl"))
                    .Append(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                        ExecutionUnit = "G226",
                        Provider = "Claude",
                        EntryKind = "implement",
                        SessionId = "pid:999999",
                        Kind = "provider-event",
                        Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                        {
                            type = "backend-exit",
                            exit_code = 1
                        })
                    });
            });

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));
            await appendTask;

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Contains("auto-resumed", result.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("no terminal provider event was captured", result.Detail, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
            RunSuperviseCommand.TerminalFailureRacePollInterval = originalRacePollInterval;
        }
    }

    [Fact]
    public async Task ExecuteCore_GivenDeadImplementWorkerSessionAtRetryExhaustionWhenBackendExitLandsAfterPreviousRaceWindow_BlocksUsingBackendExitReason()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active))));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G226.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Monitoring,
                QueueState = "active",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = null,
                CommentRef = null,
                HandoffArtifactRef = ".intent-cli/implement/G226.request.md",
                RetryCount = 3,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "implement", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "implement",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "implement",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "sonnet",
                        transport = "sdk",
                        command = "claude"
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRaceWindow = RunSuperviseCommand.TerminalFailureRaceWindow;
        var originalRacePollInterval = RunSuperviseCommand.TerminalFailureRacePollInterval;

        try
        {
            RunSuperviseCommand.TerminalFailureRacePollInterval = TimeSpan.FromMilliseconds(5);

            var appendTask = Task.Run(async () =>
            {
                await Task.Delay(150);
                new DirectRunProviderEventWriter(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl"))
                    .Append(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                        ExecutionUnit = "G226",
                        Provider = "Claude",
                        EntryKind = "implement",
                        SessionId = "pid:999999",
                        Kind = "provider-event",
                        Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                        {
                            type = "backend-exit",
                            exit_code = 1
                        })
                    });
            });

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));
            await appendTask;

            Assert.Equal("parent-intent-update-required", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("backend exit code 1", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
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
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
            RunSuperviseCommand.TerminalFailureRacePollInterval = originalRacePollInterval;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithoutExistingSessionAndDeadFixWorkerSession_DoesNotRemainUnderSupervision()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "fix",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "sonnet",
                        transport = "sdk",
                        command = "claude"
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRunFixExecutor = RunSuperviseCommand.RunFixExecutor;

        try
        {
            RunSuperviseCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "fix", "pid:4242", provider: "Claude");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:01:00.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Claude",
                            EntryKind = "fix",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "sonnet",
                                transport = "sdk",
                                command = "claude"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Claude");

                return new RunFixResult
                {
                    Request = new RunFixRequest
                    {
                        ExecutionUnit = executionUnit,
                        State = "fixing",
                        ImplementRole = "Codex",
                        QueueWorkerRole = "coder",
                        QueueReviewRole = "reviewer",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = $"issue-226-{executionUnit.ToLowerInvariant()}",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        LatestLinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                        LatestCommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        ReviewCommentArtifactRef = $".intent-cli/reviews/{executionUnit}.comment.json",
                        ReviewRequestRef = $".intent-cli/reviews/{executionUnit}.request.json",
                        ReviewCommentBodyPath = $".intent-cli/reviews/{executionUnit}.comment.md",
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
                    },
                    ArtifactPath = $".intent-cli/fix/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("run supervise", action.Name);
            Assert.DoesNotContain("Worker remains under supervision.", result.Detail, StringComparison.Ordinal);
            Assert.Contains("auto-resumed", result.Detail, StringComparison.OrdinalIgnoreCase);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionWorkerEntry.Fix, session.WorkerEntry);
            Assert.Equal(RunSupervisionSessionStatus.Monitoring, session.Status);
            Assert.Equal(1, session.RetryCount);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.RunFixExecutor = originalRunFixExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenDeadFixWorkerSessionAtRetryExhaustionWithoutCapturedTerminalEvent_BlocksUsingSyntheticBackendExitReason()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Fix,
                Status = RunSupervisionSessionStatus.Monitoring,
                QueueState = "fixing",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                HandoffArtifactRef = ".intent-cli/fix/G226.request.md",
                RetryCount = 3,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "fix",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "sonnet",
                        transport = "sdk",
                        command = "claude"
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Claude");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("parent-intent-update-required", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
        Assert.Equal(QueueItemState.Blocked, selectedItem.State);
        Assert.Contains("backend exit code 1", selectedItem.BlockedBy[0], StringComparison.Ordinal);
        Assert.DoesNotContain("no terminal provider event was captured", selectedItem.BlockedBy[0], StringComparison.Ordinal);

        var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
        Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
        Assert.Contains("backend exit code 1", session.LastInterruptionReason, StringComparison.Ordinal);

        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
        Assert.Equal("failed", resultArtifact.RunStatus);

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl")));
        Assert.Contains(providerEvents, providerEvent =>
            string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
            && providerEvent.Kind == "provider-event"
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
            && providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
            && exitCodeElement.GetInt32() == 1);

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        Assert.Equal("retry-exhausted", runEvents[^2].Event);
        Assert.Equal("blocked", runEvents[^1].Event);
        Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
        Assert.Contains("backend exit code 1", runEvents[^1].Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("no terminal provider event was captured", runEvents[^2].Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("no terminal provider event was captured", runEvents[^1].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteCore_GivenDeadFixWorkerSessionAtRetryExhaustionWhenBackendExitLandsAfterObservedDelay_BlocksUsingBackendExitReason()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Fix,
                Status = RunSupervisionSessionStatus.Monitoring,
                QueueState = "fixing",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                HandoffArtifactRef = ".intent-cli/fix/G226.request.md",
                RetryCount = 3,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Claude",
                    EntryKind = "fix",
                    SessionId = "pid:999999",
                    Kind = "session-metadata",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        model = "sonnet",
                        transport = "sdk",
                        command = "claude"
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRacePollInterval = RunSuperviseCommand.TerminalFailureRacePollInterval;

        try
        {
            RunSuperviseCommand.TerminalFailureRacePollInterval = TimeSpan.FromMilliseconds(5);

            var appendTask = Task.Run(async () =>
            {
                await Task.Delay(300);
                new DirectRunProviderEventWriter(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl"))
                    .Append(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                        ExecutionUnit = "G226",
                        Provider = "Claude",
                        EntryKind = "fix",
                        SessionId = "pid:999999",
                        Kind = "provider-event",
                        Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                        {
                            type = "backend-exit",
                            exit_code = 1
                        })
                    });
            });

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));
            await appendTask;

            Assert.Equal("parent-intent-update-required", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("backend exit code 1", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("no terminal provider event was captured", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
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
            RunSuperviseCommand.TerminalFailureRacePollInterval = originalRacePollInterval;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithStaleFailedFixResultAndRuntimeOnlyTarget_StopsWithDeterministicContractGap()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            execution_unit: "G226"

            implementation_issue:
              issue_title: "[G226] Root Run Orchestration Command"
              goal: "Coordinate the root run loop."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: ".intent-cli/intake"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/G226/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        WriteDirectRunResult(repoRoot, "G226", "fix", "failed");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("deterministic-contract-gap", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("host runtime-only '.intent-cli/**' content", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Fix direct run failed", result.Detail, StringComparison.Ordinal);
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

    private static string CreateCapturedLastMessageFileName(string executionUnit, DateTimeOffset launchedAt)
    {
        return $"{executionUnit}.{DirectRunCommandSupport.CreateCapturedMessageSuffix(launchedAt)}.last-message.json";
    }

    private sealed class FakeReviewCommentPublisher : IReviewCommentPublisher
    {
        public int CallCount { get; private set; }

        public string PostComment(string linkedPr, string body)
        {
            CallCount++;
            return $"{linkedPr}#issuecomment-generated";
        }
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
