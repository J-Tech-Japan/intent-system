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
    public void ExecuteCore_GivenCompletedQueueAndLaunchableIntakeSlice_AutoContinuesSingleExecutionUnitOnly()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Completed))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateIntakeExecutionArtifactMarkdown("auth", "AUTH-01", "AUTH-02"));
        var originalIntakeIssueExecutor = RunCommand.IntakeIssueExecutor;
        var originalQueueEnqueueExecutor = RunCommand.QueueEnqueueExecutor;
        var originalQueueDispatchExecutor = RunCommand.QueueDispatchExecutor;
        var originalRunStartExecutor = RunCommand.RunStartExecutor;
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalIssueLifecycleExecutors = CaptureIssueLifecycleExecutors();
        var invokedSteps = new List<string>();

        try
        {
            ConfigureFakeIssueLifecycleExecutors(invokedSteps);
            RunCommand.IntakeIssueExecutor = (_, domain, executionUnit) =>
            {
                invokedSteps.Add($"issue:{domain}:{executionUnit}");
                return new IntakeIssueResult
                {
                    Domain = domain,
                    GeneratedExecutionUnits = [executionUnit],
                    ArtifactPaths = [],
                    SkippedUnits = []
                };
            };
            RunCommand.QueueEnqueueExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"enqueue:{executionUnit}");
                AppendQueueItem(context.RepoRoot, CreateQueueItem(QueueItemState.Queued, executionUnit: executionUnit, withLinkedIssue: false));
                return 0;
            };
            RunCommand.QueueDispatchExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"dispatch:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with
                        {
                            LinkedIssue = new LinkedIssue
                            {
                                Repo = "J-Tech-Japan/intent-system",
                                Number = 401,
                                Url = "https://github.com/J-Tech-Japan/intent-system/issues/401"
                            }
                        }
                        : queueItem);

                return new QueueDispatchCommandResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/401",
                    ReusedExistingIssue = false
                };
            };
            RunCommand.RunStartExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"start:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Active }
                        : queueItem);

                return new RunStartResult
                {
                    ExecutionUnit = executionUnit,
                    WorktreePath = Path.Combine(context.RepoRoot, ".intent-cli", "worktrees", executionUnit),
                    BranchName = $"issue-401-{executionUnit.ToLowerInvariant()}"
                };
            };
            RunCommand.RunImplementExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"implement:{executionUnit}");
                tempDirectory.CreateFile(
                    Path.Combine("repo", ".intent-cli", "implement", $"{executionUnit}.request.md"),
                    "# Execution Worker Handoff");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };
            RunCommand.RunSuperviseExecutor = (_, executionUnit) =>
            {
                invokedSteps.Add($"supervise:{executionUnit}");
                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("AUTH-01", result.ExecutionUnit);
            Assert.Equal(
                [
                    "issue:auth:AUTH-01",
                    "enqueue:AUTH-01",
                    "draft:AUTH-01",
                    "create:AUTH-01",
                    "publish:AUTH-01",
                    "start:AUTH-01",
                    "implement:AUTH-01",
                    "supervise:AUTH-01"
                ],
                invokedSteps);
            Assert.Collection(
                result.Actions,
                action =>
                {
                    Assert.Equal("intake issue", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("queue enqueue", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue draft", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue create", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue publish", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run start", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run implement", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run supervise", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                });
            Assert.Contains("issue draft", result.ReusedChildCommandRefs);
            Assert.Contains("issue create", result.ReusedChildCommandRefs);
            Assert.Contains("issue publish", result.ReusedChildCommandRefs);
            Assert.DoesNotContain("queue dispatch", result.ReusedChildCommandRefs);
            var publishArtifact = IssuePublishArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "publish.yaml")));
            Assert.Equal("published", publishArtifact.PublishStatus);
            Assert.Equal("intent-target", publishArtifact.PublishedLabelName);
            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Contains(runEvents, runEvent => runEvent.Event == "issue-drafted" && runEvent.ExecutionUnit == "AUTH-01");
            Assert.Contains(runEvents, runEvent => runEvent.Event == "issue-created" && runEvent.ExecutionUnit == "AUTH-01");
            Assert.Contains(runEvents, runEvent => runEvent.Event == "issue-published" && runEvent.ExecutionUnit == "AUTH-01");
            Assert.DoesNotContain(invokedSteps, step => step.StartsWith("dispatch:", StringComparison.Ordinal));
            Assert.DoesNotContain(invokedSteps, step => step.Contains("AUTH-02", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.IntakeIssueExecutor = originalIntakeIssueExecutor;
            RunCommand.QueueEnqueueExecutor = originalQueueEnqueueExecutor;
            RunCommand.QueueDispatchExecutor = originalQueueDispatchExecutor;
            RunCommand.RunStartExecutor = originalRunStartExecutor;
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RestoreIssueLifecycleExecutors(originalIssueLifecycleExecutors);
        }
    }

    [Fact]
    public void ExecuteCore_GivenEmptyQueueAndLaunchableIntakeSlice_BootstrapsFirstExecutionUnit()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateIntakeExecutionArtifactMarkdown("auth", "AUTH-01", "AUTH-02"));
        var originalIntakeIssueExecutor = RunCommand.IntakeIssueExecutor;
        var originalQueueEnqueueExecutor = RunCommand.QueueEnqueueExecutor;
        var originalQueueDispatchExecutor = RunCommand.QueueDispatchExecutor;
        var originalRunStartExecutor = RunCommand.RunStartExecutor;
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalIssueLifecycleExecutors = CaptureIssueLifecycleExecutors();
        var invokedSteps = new List<string>();

        try
        {
            ConfigureFakeIssueLifecycleExecutors(invokedSteps);
            RunCommand.IntakeIssueExecutor = (_, domain, executionUnit) =>
            {
                invokedSteps.Add($"issue:{domain}:{executionUnit}");
                return new IntakeIssueResult
                {
                    Domain = domain,
                    GeneratedExecutionUnits = [executionUnit],
                    ArtifactPaths = [],
                    SkippedUnits = []
                };
            };
            RunCommand.QueueEnqueueExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"enqueue:{executionUnit}");
                AppendQueueItem(context.RepoRoot, CreateQueueItem(QueueItemState.Queued, executionUnit: executionUnit, withLinkedIssue: false));
                return 0;
            };
            RunCommand.QueueDispatchExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"dispatch:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with
                        {
                            LinkedIssue = new LinkedIssue
                            {
                                Repo = "J-Tech-Japan/intent-system",
                                Number = 401,
                                Url = "https://github.com/J-Tech-Japan/intent-system/issues/401"
                            }
                        }
                        : queueItem);

                return new QueueDispatchCommandResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/401",
                    ReusedExistingIssue = false
                };
            };
            RunCommand.RunStartExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"start:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Active }
                        : queueItem);

                return new RunStartResult
                {
                    ExecutionUnit = executionUnit,
                    WorktreePath = Path.Combine(context.RepoRoot, ".intent-cli", "worktrees", executionUnit),
                    BranchName = $"issue-401-{executionUnit.ToLowerInvariant()}"
                };
            };
            RunCommand.RunImplementExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"implement:{executionUnit}");
                tempDirectory.CreateFile(
                    Path.Combine("repo", ".intent-cli", "implement", $"{executionUnit}.request.md"),
                    "# Execution Worker Handoff");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };
            RunCommand.RunSuperviseExecutor = (_, executionUnit) =>
            {
                invokedSteps.Add($"supervise:{executionUnit}");
                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("AUTH-01", result.ExecutionUnit);
            Assert.Equal(
                [
                    "issue:auth:AUTH-01",
                    "enqueue:AUTH-01",
                    "draft:AUTH-01",
                    "create:AUTH-01",
                    "publish:AUTH-01",
                    "start:AUTH-01",
                    "implement:AUTH-01",
                    "supervise:AUTH-01"
                ],
                invokedSteps);
            Assert.Collection(
                result.Actions,
                action =>
                {
                    Assert.Equal("intake issue", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("queue enqueue", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue draft", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue create", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue publish", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run start", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run implement", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run supervise", action.Name);
                    Assert.Equal("AUTH-01", action.ExecutionUnit);
                });
            Assert.DoesNotContain(invokedSteps, step => step.Contains("AUTH-02", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.IntakeIssueExecutor = originalIntakeIssueExecutor;
            RunCommand.QueueEnqueueExecutor = originalQueueEnqueueExecutor;
            RunCommand.QueueDispatchExecutor = originalQueueDispatchExecutor;
            RunCommand.RunStartExecutor = originalRunStartExecutor;
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RestoreIssueLifecycleExecutors(originalIssueLifecycleExecutors);
        }
    }

    [Fact]
    public void ExecuteCore_GivenCompletedQueueAndOnlyCompletedIntakeUnits_DoesNotLoopBackIntoIntake()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(
                CreateQueueState(
                    CreateQueueItem(QueueItemState.Completed),
                    CreateQueueItem(QueueItemState.Completed, executionUnit: "AUTH-01"))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateIntakeExecutionArtifactMarkdown("auth", "AUTH-01"));

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Empty(result.Actions);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void ExecuteCore_GivenCompletedQueueAndOnlyRuntimeOnlyBootstrapIntakeUnit_DoesNotLoopBackIntoIntake()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(
                CreateQueueState(
                    CreateQueueItem(QueueItemState.Completed),
                    CreateQueueItem(QueueItemState.Completed, executionUnit: "TOY-CALC-V0-05"))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.execution.md"),
            CreateIntakeExecutionArtifactMarkdown(
                "toy-calc",
                ("TOY-CALC-01", ".intent-cli/intake")));

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("no-actionable-item", result.StopReason);
        Assert.Empty(result.Actions);
        Assert.Null(result.ExecutionUnit);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void ExecuteCore_GivenCompletedQueueAndBootstrapIntakeCandidateWithLaterChildFacingSlice_SkipsBootstrapCandidate()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(
                CreateQueueState(
                    CreateQueueItem(QueueItemState.Completed),
                    CreateQueueItem(QueueItemState.Completed, executionUnit: "TOY-CALC-V0-05"))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.execution.md"),
            CreateIntakeExecutionArtifactMarkdown(
                "toy-calc",
                ("TOY-CALC-01", ".intent-cli/intake"),
                ("TOY-CALC-V0-01", "concepts")));
        var originalIntakeIssueExecutor = RunCommand.IntakeIssueExecutor;
        var originalQueueEnqueueExecutor = RunCommand.QueueEnqueueExecutor;
        var originalQueueDispatchExecutor = RunCommand.QueueDispatchExecutor;
        var originalRunStartExecutor = RunCommand.RunStartExecutor;
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalIssueLifecycleExecutors = CaptureIssueLifecycleExecutors();
        var invokedSteps = new List<string>();

        try
        {
            ConfigureFakeIssueLifecycleExecutors(invokedSteps);
            RunCommand.IntakeIssueExecutor = (_, domain, executionUnit) =>
            {
                invokedSteps.Add($"issue:{domain}:{executionUnit}");
                return new IntakeIssueResult
                {
                    Domain = domain,
                    GeneratedExecutionUnits = [executionUnit],
                    ArtifactPaths = [],
                    SkippedUnits = []
                };
            };
            RunCommand.QueueEnqueueExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"enqueue:{executionUnit}");
                AppendQueueItem(context.RepoRoot, CreateQueueItem(QueueItemState.Queued, executionUnit: executionUnit, withLinkedIssue: false));
                return 0;
            };
            RunCommand.QueueDispatchExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"dispatch:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with
                        {
                            LinkedIssue = new LinkedIssue
                            {
                                Repo = "J-Tech-Japan/intent-system",
                                Number = 401,
                                Url = "https://github.com/J-Tech-Japan/intent-system/issues/401"
                            }
                        }
                        : queueItem);

                return new QueueDispatchCommandResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/401",
                    ReusedExistingIssue = false
                };
            };
            RunCommand.RunStartExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"start:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Active }
                        : queueItem);

                return new RunStartResult
                {
                    ExecutionUnit = executionUnit,
                    WorktreePath = Path.Combine(context.RepoRoot, ".intent-cli", "worktrees", executionUnit),
                    BranchName = $"issue-401-{executionUnit.ToLowerInvariant()}"
                };
            };
            RunCommand.RunImplementExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"implement:{executionUnit}");
                tempDirectory.CreateFile(
                    Path.Combine("repo", ".intent-cli", "implement", $"{executionUnit}.request.md"),
                    "# Execution Worker Handoff");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };
            RunCommand.RunSuperviseExecutor = (_, executionUnit) =>
            {
                invokedSteps.Add($"supervise:{executionUnit}");
                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("TOY-CALC-V0-01", result.ExecutionUnit);
            Assert.Equal(
                [
                    "issue:toy-calc:TOY-CALC-V0-01",
                    "enqueue:TOY-CALC-V0-01",
                    "draft:TOY-CALC-V0-01",
                    "create:TOY-CALC-V0-01",
                    "publish:TOY-CALC-V0-01",
                    "start:TOY-CALC-V0-01",
                    "implement:TOY-CALC-V0-01",
                    "supervise:TOY-CALC-V0-01"
                ],
                invokedSteps);
            Assert.DoesNotContain(invokedSteps, step => step.Contains("TOY-CALC-01", StringComparison.Ordinal));
            Assert.Collection(
                result.Actions,
                action =>
                {
                    Assert.Equal("intake issue", action.Name);
                    Assert.Equal("TOY-CALC-V0-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("queue enqueue", action.Name);
                    Assert.Equal("TOY-CALC-V0-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue draft", action.Name);
                    Assert.Equal("TOY-CALC-V0-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue create", action.Name);
                    Assert.Equal("TOY-CALC-V0-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue publish", action.Name);
                    Assert.Equal("TOY-CALC-V0-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run start", action.Name);
                    Assert.Equal("TOY-CALC-V0-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run implement", action.Name);
                    Assert.Equal("TOY-CALC-V0-01", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run supervise", action.Name);
                    Assert.Equal("TOY-CALC-V0-01", action.ExecutionUnit);
                });
        }
        finally
        {
            RunCommand.IntakeIssueExecutor = originalIntakeIssueExecutor;
            RunCommand.QueueEnqueueExecutor = originalQueueEnqueueExecutor;
            RunCommand.QueueDispatchExecutor = originalQueueDispatchExecutor;
            RunCommand.RunStartExecutor = originalRunStartExecutor;
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RestoreIssueLifecycleExecutors(originalIssueLifecycleExecutors);
        }
    }

    [Fact]
    public void ExecuteCore_GivenCompletedQueueAndStaleIntakeExecutionArtifact_RefreshesIntakeBeforeLaunchingNextSlice()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(
                CreateQueueState(
                    CreateQueueItem(QueueItemState.Completed, executionUnit: "TOY-CALC-V0-06"))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.execution.md"),
            CreateIntakeExecutionArtifactMarkdown("toy-calc", "TOY-CALC-V0-06"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.concept.yaml"),
            """
            domain_slug: toy-calc
            concept_source: test
            concept_text: "Toy calc"
            upstream_paths: []
            initial_goal: "Toy calc"
            constraints: []
            known_unknowns: []
            """);
        var originalIntakeAdvanceExecutor = RunCommand.IntakeAdvanceExecutor;
        var originalIntakeIssueExecutor = RunCommand.IntakeIssueExecutor;
        var originalQueueEnqueueExecutor = RunCommand.QueueEnqueueExecutor;
        var originalQueueDispatchExecutor = RunCommand.QueueDispatchExecutor;
        var originalRunStartExecutor = RunCommand.RunStartExecutor;
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalIssueLifecycleExecutors = CaptureIssueLifecycleExecutors();
        var invokedSteps = new List<string>();

        try
        {
            ConfigureFakeIssueLifecycleExecutors(invokedSteps);
            RunCommand.IntakeAdvanceExecutor = (context, domain) =>
            {
                invokedSteps.Add($"advance:{domain}");
                File.WriteAllText(
                    Path.Combine(context.RepoRoot, ".intent-cli", "intake", "toy-calc.execution.md"),
                    CreateIntakeExecutionArtifactMarkdown(
                        "toy-calc",
                        ("TOY-CALC-V0-06", "specs"),
                        ("TOY-CALC-V0-07", "specs")));

                return new IntakeAdvanceResult
                {
                    Domain = domain,
                    ReadinessStatus = "ready",
                    UpdatedSourceFilePaths = ["intents/toy-calc/specs/07-min-command.md"],
                    UpdatedExecutionFilePaths = ["intents/toy-calc/execution/01-issue-ready-slices.md"],
                    RegeneratedArtifactPaths = [".intent-cli/intake/toy-calc.execution.md"],
                    SkippedStages = []
                };
            };
            RunCommand.IntakeIssueExecutor = (_, domain, executionUnit) =>
            {
                invokedSteps.Add($"issue:{domain}:{executionUnit}");
                return new IntakeIssueResult
                {
                    Domain = domain,
                    GeneratedExecutionUnits = [executionUnit],
                    ArtifactPaths = [],
                    SkippedUnits = []
                };
            };
            RunCommand.QueueEnqueueExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"enqueue:{executionUnit}");
                AppendQueueItem(context.RepoRoot, CreateQueueItem(QueueItemState.Queued, executionUnit: executionUnit, withLinkedIssue: false));
                return 0;
            };
            RunCommand.QueueDispatchExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"dispatch:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with
                        {
                            LinkedIssue = new LinkedIssue
                            {
                                Repo = "J-Tech-Japan/intent-system",
                                Number = 407,
                                Url = "https://github.com/J-Tech-Japan/intent-system/issues/407"
                            }
                        }
                        : queueItem);

                return new QueueDispatchCommandResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/407",
                    ReusedExistingIssue = false
                };
            };
            RunCommand.RunStartExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"start:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Active }
                        : queueItem);

                return new RunStartResult
                {
                    ExecutionUnit = executionUnit,
                    WorktreePath = Path.Combine(context.RepoRoot, ".intent-cli", "worktrees", executionUnit),
                    BranchName = $"issue-407-{executionUnit.ToLowerInvariant()}"
                };
            };
            RunCommand.RunImplementExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"implement:{executionUnit}");
                tempDirectory.CreateFile(
                    Path.Combine("repo", ".intent-cli", "implement", $"{executionUnit}.request.md"),
                    "# Execution Worker Handoff");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };
            RunCommand.RunSuperviseExecutor = (_, executionUnit) =>
            {
                invokedSteps.Add($"supervise:{executionUnit}");
                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("TOY-CALC-V0-07", result.ExecutionUnit);
            Assert.Equal(
                [
                    "advance:toy-calc",
                    "issue:toy-calc:TOY-CALC-V0-07",
                    "enqueue:TOY-CALC-V0-07",
                    "draft:TOY-CALC-V0-07",
                    "create:TOY-CALC-V0-07",
                    "publish:TOY-CALC-V0-07",
                    "start:TOY-CALC-V0-07",
                    "implement:TOY-CALC-V0-07",
                    "supervise:TOY-CALC-V0-07"
                ],
                invokedSteps);
            Assert.Collection(
                result.Actions,
                action =>
                {
                    Assert.Equal("intake issue", action.Name);
                    Assert.Equal("TOY-CALC-V0-07", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("queue enqueue", action.Name);
                    Assert.Equal("TOY-CALC-V0-07", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue draft", action.Name);
                    Assert.Equal("TOY-CALC-V0-07", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue create", action.Name);
                    Assert.Equal("TOY-CALC-V0-07", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("issue publish", action.Name);
                    Assert.Equal("TOY-CALC-V0-07", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run start", action.Name);
                    Assert.Equal("TOY-CALC-V0-07", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run implement", action.Name);
                    Assert.Equal("TOY-CALC-V0-07", action.ExecutionUnit);
                },
                action =>
                {
                    Assert.Equal("run supervise", action.Name);
                    Assert.Equal("TOY-CALC-V0-07", action.ExecutionUnit);
                });
        }
        finally
        {
            RunCommand.IntakeAdvanceExecutor = originalIntakeAdvanceExecutor;
            RunCommand.IntakeIssueExecutor = originalIntakeIssueExecutor;
            RunCommand.QueueEnqueueExecutor = originalQueueEnqueueExecutor;
            RunCommand.QueueDispatchExecutor = originalQueueDispatchExecutor;
            RunCommand.RunStartExecutor = originalRunStartExecutor;
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RestoreIssueLifecycleExecutors(originalIssueLifecycleExecutors);
        }
    }

    [Fact]
    public void ExecuteCore_GivenCompletedQueueAndBrokenUnrelatedIntakeDomain_RefreshesOnlyContinuationDomain()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(
                CreateQueueState(
                    CreateQueueItem(QueueItemState.Completed, executionUnit: "TOY-CALC-V0-06"))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.execution.md"),
            CreateIntakeExecutionArtifactMarkdown("toy-calc", "TOY-CALC-V0-06"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.concept.yaml"),
            """
            domain_slug: toy-calc
            concept_source: test
            concept_text: "Toy calc"
            upstream_paths: []
            initial_goal: "Toy calc"
            constraints: []
            known_unknowns: []
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "broken.concept.yaml"),
            """
            domain_slug: broken
            concept_source: test
            concept_text: "Broken"
            upstream_paths: []
            initial_goal: "Broken"
            constraints: []
            known_unknowns: []
            """);
        var originalIntakeAdvanceExecutor = RunCommand.IntakeAdvanceExecutor;
        var originalIntakeIssueExecutor = RunCommand.IntakeIssueExecutor;
        var originalQueueEnqueueExecutor = RunCommand.QueueEnqueueExecutor;
        var originalQueueDispatchExecutor = RunCommand.QueueDispatchExecutor;
        var originalRunStartExecutor = RunCommand.RunStartExecutor;
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalIssueLifecycleExecutors = CaptureIssueLifecycleExecutors();
        var invokedSteps = new List<string>();

        try
        {
            ConfigureFakeIssueLifecycleExecutors(invokedSteps);
            RunCommand.IntakeAdvanceExecutor = (context, domain) =>
            {
                invokedSteps.Add($"advance:{domain}");
                if (string.Equals(domain, "broken", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Broken intake domain should not be refreshed.");
                }

                File.WriteAllText(
                    Path.Combine(context.RepoRoot, ".intent-cli", "intake", "toy-calc.execution.md"),
                    CreateIntakeExecutionArtifactMarkdown(
                        "toy-calc",
                        ("TOY-CALC-V0-06", "specs"),
                        ("TOY-CALC-V0-07", "specs")));

                return new IntakeAdvanceResult
                {
                    Domain = domain,
                    ReadinessStatus = "ready",
                    UpdatedSourceFilePaths = ["intents/toy-calc/specs/07-min-command.md"],
                    UpdatedExecutionFilePaths = ["intents/toy-calc/execution/01-issue-ready-slices.md"],
                    RegeneratedArtifactPaths = [".intent-cli/intake/toy-calc.execution.md"],
                    SkippedStages = []
                };
            };
            RunCommand.IntakeIssueExecutor = (_, domain, executionUnit) =>
            {
                invokedSteps.Add($"issue:{domain}:{executionUnit}");
                return new IntakeIssueResult
                {
                    Domain = domain,
                    GeneratedExecutionUnits = [executionUnit],
                    ArtifactPaths = [],
                    SkippedUnits = []
                };
            };
            RunCommand.QueueEnqueueExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"enqueue:{executionUnit}");
                AppendQueueItem(context.RepoRoot, CreateQueueItem(QueueItemState.Queued, executionUnit: executionUnit, withLinkedIssue: false));
                return 0;
            };
            RunCommand.QueueDispatchExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"dispatch:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with
                        {
                            LinkedIssue = new LinkedIssue
                            {
                                Repo = "J-Tech-Japan/intent-system",
                                Number = 407,
                                Url = "https://github.com/J-Tech-Japan/intent-system/issues/407"
                            }
                        }
                        : queueItem);

                return new QueueDispatchCommandResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/407",
                    ReusedExistingIssue = false
                };
            };
            RunCommand.RunStartExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"start:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Active }
                        : queueItem);

                return new RunStartResult
                {
                    ExecutionUnit = executionUnit,
                    WorktreePath = Path.Combine(context.RepoRoot, ".intent-cli", "worktrees", executionUnit),
                    BranchName = $"issue-407-{executionUnit.ToLowerInvariant()}"
                };
            };
            RunCommand.RunImplementExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"implement:{executionUnit}");
                tempDirectory.CreateFile(
                    Path.Combine("repo", ".intent-cli", "implement", $"{executionUnit}.request.md"),
                    "# Execution Worker Handoff");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };
            RunCommand.RunSuperviseExecutor = (_, executionUnit) =>
            {
                invokedSteps.Add($"supervise:{executionUnit}");
                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("TOY-CALC-V0-07", result.ExecutionUnit);
            Assert.Equal(
                [
                    "advance:toy-calc",
                    "issue:toy-calc:TOY-CALC-V0-07",
                    "enqueue:TOY-CALC-V0-07",
                    "draft:TOY-CALC-V0-07",
                    "create:TOY-CALC-V0-07",
                    "publish:TOY-CALC-V0-07",
                    "start:TOY-CALC-V0-07",
                    "implement:TOY-CALC-V0-07",
                    "supervise:TOY-CALC-V0-07"
                ],
                invokedSteps);
            Assert.DoesNotContain("advance:broken", invokedSteps);
        }
        finally
        {
            RunCommand.IntakeAdvanceExecutor = originalIntakeAdvanceExecutor;
            RunCommand.IntakeIssueExecutor = originalIntakeIssueExecutor;
            RunCommand.QueueEnqueueExecutor = originalQueueEnqueueExecutor;
            RunCommand.QueueDispatchExecutor = originalQueueDispatchExecutor;
            RunCommand.RunStartExecutor = originalRunStartExecutor;
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RestoreIssueLifecycleExecutors(originalIssueLifecycleExecutors);
        }
    }

    [Fact]
    public void ExecuteCore_GivenCompletedQueueAndBrokenUnrelatedExecutionDraft_RefreshesOnlyContinuationDomain()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(
                CreateQueueState(
                    CreateQueueItem(QueueItemState.Completed, executionUnit: "TOY-CALC-V0-06"))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.execution.md"),
            CreateIntakeExecutionArtifactMarkdown("toy-calc", "TOY-CALC-V0-06"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.concept.yaml"),
            """
            domain_slug: toy-calc
            concept_source: test
            concept_text: "Toy calc"
            upstream_paths: []
            initial_goal: "Toy calc"
            constraints: []
            known_unknowns: []
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "broken.execution.md"),
            """
            # Intake Execution Draft

            ## Domain
            `broken`

            ## Proposed Execution Units

            ### `BROKEN-01`
            source_file_path:
            """);
        var originalIntakeAdvanceExecutor = RunCommand.IntakeAdvanceExecutor;
        var originalIntakeIssueExecutor = RunCommand.IntakeIssueExecutor;
        var originalQueueEnqueueExecutor = RunCommand.QueueEnqueueExecutor;
        var originalQueueDispatchExecutor = RunCommand.QueueDispatchExecutor;
        var originalRunStartExecutor = RunCommand.RunStartExecutor;
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalIssueLifecycleExecutors = CaptureIssueLifecycleExecutors();
        var invokedSteps = new List<string>();

        try
        {
            ConfigureFakeIssueLifecycleExecutors(invokedSteps);
            RunCommand.IntakeAdvanceExecutor = (context, domain) =>
            {
                invokedSteps.Add($"advance:{domain}");
                File.WriteAllText(
                    Path.Combine(context.RepoRoot, ".intent-cli", "intake", "toy-calc.execution.md"),
                    CreateIntakeExecutionArtifactMarkdown(
                        "toy-calc",
                        ("TOY-CALC-V0-06", "specs"),
                        ("TOY-CALC-V0-07", "specs")));

                return new IntakeAdvanceResult
                {
                    Domain = domain,
                    ReadinessStatus = "ready",
                    UpdatedSourceFilePaths = ["intents/toy-calc/specs/07-min-command.md"],
                    UpdatedExecutionFilePaths = ["intents/toy-calc/execution/01-issue-ready-slices.md"],
                    RegeneratedArtifactPaths = [".intent-cli/intake/toy-calc.execution.md"],
                    SkippedStages = []
                };
            };
            RunCommand.IntakeIssueExecutor = (_, domain, executionUnit) =>
            {
                invokedSteps.Add($"issue:{domain}:{executionUnit}");
                return new IntakeIssueResult
                {
                    Domain = domain,
                    GeneratedExecutionUnits = [executionUnit],
                    ArtifactPaths = [],
                    SkippedUnits = []
                };
            };
            RunCommand.QueueEnqueueExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"enqueue:{executionUnit}");
                AppendQueueItem(context.RepoRoot, CreateQueueItem(QueueItemState.Queued, executionUnit: executionUnit, withLinkedIssue: false));
                return 0;
            };
            RunCommand.QueueDispatchExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"dispatch:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with
                        {
                            LinkedIssue = new LinkedIssue
                            {
                                Repo = "J-Tech-Japan/intent-system",
                                Number = 407,
                                Url = "https://github.com/J-Tech-Japan/intent-system/issues/407"
                            }
                        }
                        : queueItem);

                return new QueueDispatchCommandResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/407",
                    ReusedExistingIssue = false
                };
            };
            RunCommand.RunStartExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"start:{executionUnit}");
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Active }
                        : queueItem);

                return new RunStartResult
                {
                    ExecutionUnit = executionUnit,
                    WorktreePath = Path.Combine(context.RepoRoot, ".intent-cli", "worktrees", executionUnit),
                    BranchName = $"issue-407-{executionUnit.ToLowerInvariant()}"
                };
            };
            RunCommand.RunImplementExecutor = (context, executionUnit) =>
            {
                invokedSteps.Add($"implement:{executionUnit}");
                tempDirectory.CreateFile(
                    Path.Combine("repo", ".intent-cli", "implement", $"{executionUnit}.request.md"),
                    "# Execution Worker Handoff");

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };
            RunCommand.RunSuperviseExecutor = (_, executionUnit) =>
            {
                invokedSteps.Add($"supervise:{executionUnit}");
                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("TOY-CALC-V0-07", result.ExecutionUnit);
            Assert.Equal(
                [
                    "advance:toy-calc",
                    "issue:toy-calc:TOY-CALC-V0-07",
                    "enqueue:TOY-CALC-V0-07",
                    "draft:TOY-CALC-V0-07",
                    "create:TOY-CALC-V0-07",
                    "publish:TOY-CALC-V0-07",
                    "start:TOY-CALC-V0-07",
                    "implement:TOY-CALC-V0-07",
                    "supervise:TOY-CALC-V0-07"
                ],
                invokedSteps);
            Assert.DoesNotContain(invokedSteps, step => step.Contains("broken", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.IntakeAdvanceExecutor = originalIntakeAdvanceExecutor;
            RunCommand.IntakeIssueExecutor = originalIntakeIssueExecutor;
            RunCommand.QueueEnqueueExecutor = originalQueueEnqueueExecutor;
            RunCommand.QueueDispatchExecutor = originalQueueDispatchExecutor;
            RunCommand.RunStartExecutor = originalRunStartExecutor;
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RestoreIssueLifecycleExecutors(originalIssueLifecycleExecutors);
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
    public void ExecuteCore_GivenAutoContinuePostFixProgressAndRereviewWithStaleReviewRequest_LaunchesFreshReviewRun()
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
            Path.Combine("repo", ".intent-cli", "issues", "G226", "review-context.md"),
            "# Review Context");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.comment.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "fix", "G226.request.md"),
            "# Repair Worker Handoff");
        WriteDirectRunRequest(repoRoot, "G226", "review", "stale-review-session");
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999999", provider: "Claude");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "succeeded",
            providerEvents: [],
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRunResubmitExecutor = RunCommand.RunResubmitExecutor;
        var originalRunRereviewExecutor = RunCommand.RunRereviewExecutor;
        var originalReviewRunExecutor = RunCommand.ReviewRunExecutor;

        try
        {
            RunCommand.RunResubmitExecutor = (_, executionUnit) => new RunResubmitResult
            {
                ExecutionUnit = executionUnit,
                Branch = "issue-226-g226",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
            };
            RunCommand.RunRereviewExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Review }
                        : queueItem);
                File.AppendAllText(
                    Path.Combine(context.RepoRoot, ".intent-cli", "runs.jsonl"),
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                        ExecutionUnit = executionUnit,
                        Event = "rereview",
                        By = "intent-cli",
                        LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                    }) + Environment.NewLine);

                return new RunRereviewResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                };
            };
            RunCommand.ReviewRunExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "review", "review-session");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "review",
                    "running",
                    providerEvents: [],
                    sessionId: "review-session");

                return new ReviewRunResult
                {
                    ExecutionUnit = executionUnit,
                    ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json",
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, "review-session")
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(
                repoRoot,
                postFixWorktreeProgressPolicy: CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("Review direct run for 'G226' is 'running'.", result.Detail, StringComparison.Ordinal);
            Assert.Contains(result.Actions, action => action.Name == "run resubmit" && action.ExecutionUnit == "G226");
            Assert.Contains(result.Actions, action => action.Name == "run rereview" && action.ExecutionUnit == "G226");
            Assert.Contains(result.Actions, action => action.Name == "review run" && action.ExecutionUnit == "G226");
        }
        finally
        {
            RunCommand.RunResubmitExecutor = originalRunResubmitExecutor;
            RunCommand.RunRereviewExecutor = originalRunRereviewExecutor;
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
    public void Execute_GivenSucceededImplementRunWithRealLinkedWorktreeCarryForward_PersistsReviewArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var childRepoPath = tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        var worktreeRoot = tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees"));
        var worktreePath = Path.Combine(worktreeRoot, "G226");
        var originPath = tempDirectory.CreateDirectory("origin.git");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            implementation_issue_packet:
              issue_title: "[G226] Root Run Orchestration Command"
              issue_kind: "feature"
              source_execution_unit: "G226"
              goal: "Coordinate the root run loop."
              in_scope:
                - "run orchestration"
              out_of_scope:
                - "review implementation details"
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []
              technical_baseline:
                - "C# / .NET"
              project_local_guide:
                - "AGENTS.md"
              intent_baseline:
                - "run remains the coordinator"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
              rules_and_specs:
                - "intents/intent-cli/specs/08-config-and-run-model.md"
              acceptance_criteria:
                - "successful submit transitions to review"
              verification_evidence:
                - "tests-passing"
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "G226"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
              rules_and_specs:
                - "intents/intent-cli/specs/08-config-and-run-model.md"
              acceptance_criteria:
                - "successful submit transitions to review"
              deterministic_review_checks:
                - "run submit remains thin"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "G226.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        WriteDirectRunResult(repoRoot, "G226", "implement", "succeeded");

        InitializeRealRunSubmitTestRepo(childRepoPath, worktreePath, originPath, "issue-226-g226");
        File.AppendAllText(
            Path.Combine(worktreePath, "tests", "ToyCalc.Tests", "CalculatorTests.cs"),
            Environment.NewLine + "// root-run carry-forward");

        var originalRunSubmitGitFactory = RunSubmitCommand.GitCommandRunnerFactory;
        var originalRunSubmitPublisherFactory = RunSubmitCommand.PublisherFactory;
        var originalRunSubmitTimestampFactory = RunSubmitCommand.TimestampFactory;
        var originalReviewRunExecutor = RunCommand.ReviewRunExecutor;
        using var writer = new StringWriter();

        try
        {
            RunSubmitCommand.GitCommandRunnerFactory = () => new RealGitRunnerWithRemoteOriginOverride(
                childRepoPath,
                "git@github.com:J-Tech-Japan/intent-system.git");
            RunSubmitCommand.PublisherFactory = () => new FakeRunSubmitPublisher();
            RunSubmitCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-10T12:30:00Z");
            RunCommand.ReviewRunExecutor = (_, executionUnit) =>
            {
                WriteDirectRunResult(repoRoot, executionUnit, "review", "running", sessionId: "pid:review");

                return new ReviewRunResult
                {
                    ExecutionUnit = executionUnit,
                    ArtifactPath = $".intent-cli/reviews/{executionUnit}.request.json",
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, "pid:review")
                };
            };

            var exitCode = RunCommand.Execute(CreateContext(repoRoot), [], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Touched execution units: G226", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Reused child command refs: run submit, review run", writer.ToString(), StringComparison.Ordinal);
            Assert.Equal(
                "Carry forward succeeded implement progress for G226",
                RunRealGitStdOut(worktreePath, "log", "-1", "--pretty=%s"));

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Review, Assert.Single(queueState.Items).State);

            var rootArtifact = RunRootResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "run.result.json")));
            Assert.Equal("no-actionable-item", rootArtifact.StopReason);
            Assert.Equal(["G226"], rootArtifact.TouchedExecutionUnits);
            Assert.Equal(["run submit", "review run"], rootArtifact.ReusedChildCommandRefs);

            var currentResult = DirectRunResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("review", currentResult.EntryKind);
            Assert.Equal("running", currentResult.RunStatus);
            Assert.Equal("pid:review", currentResult.SessionId);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var reviewEvent = Assert.Single(runEvents);
            Assert.Equal("review", reviewEvent.Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/226", reviewEvent.LinkedPr);
        }
        finally
        {
            RunSubmitCommand.GitCommandRunnerFactory = originalRunSubmitGitFactory;
            RunSubmitCommand.PublisherFactory = originalRunSubmitPublisherFactory;
            RunSubmitCommand.TimestampFactory = originalRunSubmitTimestampFactory;
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
    public void ExecuteCore_GivenAcceptedReviewDecisionWithDraftLinkedPr_ClosesOutWithoutDeterministicContractGap()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            implementation_issue_packet:
              issue_title: "[G226] Review Accept"
              issue_kind: "feature"
              source_execution_unit: "G226"
              goal: "Close out accepted review."
              in_scope:
                - "review accept command"
              out_of_scope:
                - "review comment"
              target_repo: "submodules/child-repo"
              target_path: "."
              target_part: "cli review accept command"
              dependencies: []
              technical_baseline:
                - "C# / .NET"
              project_local_guide:
                - "AGENTS.md"
              intent_baseline:
                - "closeout stays thin"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
              rules_and_specs:
                - "intents/rules/issue-lifecycle-and-landing.md"
              acceptance_criteria:
                - "review accept merges and closes"
              verification_evidence:
                - "tests-passing"
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "G226"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
              rules_and_specs:
                - "intents/rules/issue-lifecycle-and-landing.md"
              acceptance_criteria:
                - "review accept merges and closes"
              deterministic_review_checks:
                - "selected item only"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-03T10:00:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/tomohisa/toy-calc-sample/issues/3"}
            {"ts":"2026-04-03T10:10:00Z","execution_unit":"G226","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/tomohisa/toy-calc-sample/pull/4"}
            """ + Environment.NewLine);
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(repoRoot, "G226", "review", "accepted");
        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;
        var originalGitFactory = ReviewAcceptCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = ReviewAcceptCommand.TimestampFactory;
        var client = new FakeReviewAcceptClient
        {
            RequireReadyBeforeMerge = true
        };

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => client;
            ReviewAcceptCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                new Dictionary<string, GitCommandResult>
                {
                    [FakeGitRunner.CreateCommandKey(["fetch", "origin", "main"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["switch", "main"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["merge", "--ff-only", "origin/main"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["rev-parse", "HEAD"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = "abc123" + Environment.NewLine,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["add", "submodules/child-repo"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    }
                });
            ReviewAcceptCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T01:02:03Z");

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Null(result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review accept", action.Name);
            Assert.Equal("G226", action.ExecutionUnit);
            Assert.DoesNotContain("Pull Request is still a draft", result.Detail ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal(2, client.MergeAttempts);
            Assert.Equal(["https://github.com/tomohisa/toy-calc-sample/pull/4"], client.ReadyMarkedPrs);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Completed, queueState.Items.Single(item => item.ExecutionUnit == "G226").State);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal("pr-merged", runEvents[^3].Event);
            Assert.Equal("issue-closed", runEvents[^2].Event);
            Assert.Equal("completed", runEvents[^1].Event);
        }
        finally
        {
            ReviewAcceptCommand.AcceptClientFactory = originalClientFactory;
            ReviewAcceptCommand.GitCommandRunnerFactory = originalGitFactory;
            ReviewAcceptCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenAcceptedReviewDecisionWithAcceptedWorktreeChangesThatWouldBeOverwrittenByMerge_ClosesOutWithoutDeterministicContractGap()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            implementation_issue_packet:
              issue_title: "[G226] Review Accept"
              issue_kind: "feature"
              source_execution_unit: "G226"
              goal: "Close out accepted review."
              in_scope:
                - "review accept command"
              out_of_scope:
                - "review comment"
              target_repo: "submodules/child-repo"
              target_path: "."
              target_part: "cli review accept command"
              dependencies: []
              technical_baseline:
                - "C# / .NET"
              project_local_guide:
                - "AGENTS.md"
              intent_baseline:
                - "closeout stays thin"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
              rules_and_specs:
                - "intents/rules/issue-lifecycle-and-landing.md"
              acceptance_criteria:
                - "review accept merges and closes"
              verification_evidence:
                - "tests-passing"
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "G226"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
              rules_and_specs:
                - "intents/rules/issue-lifecycle-and-landing.md"
              acceptance_criteria:
                - "review accept merges and closes"
              deterministic_review_checks:
                - "selected item only"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-03T10:00:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/tomohisa/toy-calc-sample/issues/5"}
            {"ts":"2026-04-03T10:10:00Z","execution_unit":"G226","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/tomohisa/toy-calc-sample/pull/6"}
            """ + Environment.NewLine);
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(repoRoot, "G226", "review", "accepted");
        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;
        var originalGitFactory = ReviewAcceptCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = ReviewAcceptCommand.TimestampFactory;
        var client = new FakeReviewAcceptClient();

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => client;
            ReviewAcceptCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                new Dictionary<string, GitCommandResult>
                {
                    [FakeGitRunner.CreateCommandKey(["fetch", "origin", "main"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["switch", "main"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["merge", "--ff-only", "origin/main"])] = new GitCommandResult
                    {
                        ExitCode = 1,
                        StdOut = string.Empty,
                        StdErr =
                            """
                            error: Your local changes to the following files would be overwritten by merge:
                              intents/toy-calc/specs/01-cli-surface.md
                            error: The following untracked working tree files would be overwritten by merge:
                              intents/toy-calc/specs/02-invalid-usage-contract.md
                            Please move or remove them before you merge.
                            Aborting
                            """
                    },
                    [FakeGitRunner.CreateCommandKey(["status", "--porcelain=v1", "--untracked-files=all"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut =
                            """
                             M intents/toy-calc/specs/01-cli-surface.md
                            ?? intents/toy-calc/specs/02-invalid-usage-contract.md
                            """,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["clean", "-fd", "--", "intents/toy-calc/specs/02-invalid-usage-contract.md"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["reset", "--hard", "abc123"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = "HEAD is now at abc123 closeout" + Environment.NewLine,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["rev-parse", "HEAD"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = "abc123" + Environment.NewLine,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["add", "submodules/child-repo"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    }
                },
                statusSequence:
                [
                    """
                     M intents/toy-calc/specs/01-cli-surface.md
                    ?? intents/toy-calc/specs/02-invalid-usage-contract.md
                    """,
                    string.Empty
                ]);
            ReviewAcceptCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-21T02:30:00Z");

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Null(result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review accept", action.Name);
            Assert.Equal("G226", action.ExecutionUnit);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Completed, queueState.Items.Single(item => item.ExecutionUnit == "G226").State);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal("pr-merged", runEvents[^3].Event);
            Assert.Equal("issue-closed", runEvents[^2].Event);
            Assert.Equal("completed", runEvents[^1].Event);
        }
        finally
        {
            ReviewAcceptCommand.AcceptClientFactory = originalClientFactory;
            ReviewAcceptCommand.GitCommandRunnerFactory = originalGitFactory;
            ReviewAcceptCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenAcceptedReviewDecisionWithDraftLinkedPrAndReadyApi404_ClosesOutWithoutDeterministicContractGap()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Review))));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "packet.yaml"),
            """
            implementation_issue_packet:
              issue_title: "[G226] Review Accept"
              issue_kind: "feature"
              source_execution_unit: "G226"
              goal: "Close out accepted review."
              in_scope:
                - "review accept command"
              out_of_scope:
                - "review comment"
              target_repo: "submodules/child-repo"
              target_path: "."
              target_part: "cli review accept command"
              dependencies: []
              technical_baseline:
                - "C# / .NET"
              project_local_guide:
                - "AGENTS.md"
              intent_baseline:
                - "closeout stays thin"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
              rules_and_specs:
                - "intents/rules/issue-lifecycle-and-landing.md"
              acceptance_criteria:
                - "review accept merges and closes"
              verification_evidence:
                - "tests-passing"
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "G226"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
              rules_and_specs:
                - "intents/rules/issue-lifecycle-and-landing.md"
              acceptance_criteria:
                - "review accept merges and closes"
              deterministic_review_checks:
                - "selected item only"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-03T10:00:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/tomohisa/toy-calc-sample/issues/3"}
            {"ts":"2026-04-03T10:10:00Z","execution_unit":"G226","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/tomohisa/toy-calc-sample/pull/4"}
            """ + Environment.NewLine);
        WriteDirectRunRequest(repoRoot, "G226", "review", "pid:226");
        WriteDirectRunResult(repoRoot, "G226", "review", "accepted");
        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;
        var originalGitFactory = ReviewAcceptCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = ReviewAcceptCommand.TimestampFactory;
        var reviewRunner = new ScriptedReviewCommandRunner(
        [
            new ExpectedReviewCommand(
                [
                    "api",
                    "repos/tomohisa/toy-calc-sample/pulls/4/merge",
                    "--method",
                    "PUT",
                    "-f",
                    "merge_method=merge"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr = "gh: Pull Request is still a draft (HTTP 405)"
                }),
            new ExpectedReviewCommand(
                [
                    "api",
                    "repos/tomohisa/toy-calc-sample/pulls/4/ready_for_review",
                    "--method",
                    "POST"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 1,
                    StdOut = string.Empty,
                    StdErr = "gh: Not Found (HTTP 404)"
                }),
            new ExpectedReviewCommand(
                [
                    "pr",
                    "ready",
                    "4",
                    "--repo",
                    "tomohisa/toy-calc-sample"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 0,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                }),
            new ExpectedReviewCommand(
                [
                    "api",
                    "repos/tomohisa/toy-calc-sample/pulls/4/merge",
                    "--method",
                    "PUT",
                    "-f",
                    "merge_method=merge"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 0,
                    StdOut = """{"sha":"abc123"}""",
                    StdErr = string.Empty
                }),
            new ExpectedReviewCommand(
                [
                    "api",
                    "repos/tomohisa/toy-calc-sample/issues/3",
                    "--method",
                    "PATCH",
                    "-f",
                    "state=closed"
                ],
                new ReviewCommandResult
                {
                    ExitCode = 0,
                    StdOut = """{"state":"closed"}""",
                    StdErr = string.Empty
                })
        ]);

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => new GhReviewAcceptClient(reviewRunner);
            ReviewAcceptCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                new Dictionary<string, GitCommandResult>
                {
                    [FakeGitRunner.CreateCommandKey(["fetch", "origin", "main"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["switch", "main"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["merge", "--ff-only", "origin/main"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["rev-parse", "HEAD"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = "abc123" + Environment.NewLine,
                        StdErr = string.Empty
                    },
                    [FakeGitRunner.CreateCommandKey(["add", "submodules/child-repo"])] = new GitCommandResult
                    {
                        ExitCode = 0,
                        StdOut = string.Empty,
                        StdErr = string.Empty
                    }
                });
            ReviewAcceptCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T01:02:03Z");

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Null(result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("review accept", action.Name);
            Assert.Equal("G226", action.ExecutionUnit);
            Assert.DoesNotContain("Not Found", result.Detail ?? string.Empty, StringComparison.Ordinal);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Completed, queueState.Items.Single(item => item.ExecutionUnit == "G226").State);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal("pr-merged", runEvents[^3].Event);
            Assert.Equal("issue-closed", runEvents[^2].Event);
            Assert.Equal("completed", runEvents[^1].Event);
            Assert.Equal(5, reviewRunner.Calls.Count);
        }
        finally
        {
            ReviewAcceptCommand.AcceptClientFactory = originalClientFactory;
            ReviewAcceptCommand.GitCommandRunnerFactory = originalGitFactory;
            ReviewAcceptCommand.TimestampFactory = originalTimestampFactory;
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
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
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
    public void ExecuteCore_GivenQueueTransitionRetryAfterBlockedFixSession_LaunchesFreshFixAttempt()
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
            {"ts":"2026-04-10T12:20:00Z","execution_unit":"G226","event":"blocked","by":"intent-cli","reason":"backend exit code 1"}
            {"ts":"2026-04-10T12:23:47.0973580+00:00","execution_unit":"G226","event":"fix-requested","by":"intent-cli"}
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
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
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
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
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
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:20:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:20:00Z"),
                LastInterruptionReason = "backend exit code 1"
            }));
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:57021", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "failed",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:57021",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ],
            sessionId: "pid:57021",
            provider: "Codex");
        var originalRunFixExecutor = RunCommand.RunFixExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;

        try
        {
            RunCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "fix", "pid:4242", provider: "Codex");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:23:48.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Codex",
                            EntryKind = "fix",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "gpt-5.4-mini",
                                transport = "responses",
                                command = "codex"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Codex");

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
                        Provider = "Codex",
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
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "review-context.md"),
            "# Review Context");
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
    public void ExecuteCore_GivenFreshFixWorkerProgressesPastInitialContinuationWindow_ContinuesSupervisionAndCapturesRuntimeArtifactBoundary()
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
            {"ts":"2026-04-10T12:20:00Z","execution_unit":"G226","event":"blocked","by":"intent-cli","reason":"backend exit code 1"}
            {"ts":"2026-04-18T04:46:59.7953570+00:00","execution_unit":"G226","event":"fix-requested","by":"intent-cli"}
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
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
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
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                HandoffArtifactRef = ".intent-cli/fix/G226.request.md",
                RetryCount = 2,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:20:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:20:00Z"),
                LastInterruptionReason = "backend exit code 1"
            }));
        WriteDirectRunRequest(
            repoRoot,
            "G226",
            "fix",
            "pid:57021",
            provider: "Codex",
            launchedAt: "2026-04-10T12:00:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "failed",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:57021",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ],
            sessionId: "pid:57021",
            provider: "Codex");
        var originalRunFixExecutor = RunCommand.RunFixExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalTimestampFactory = RunCommand.TimestampFactory;
        var originalFreshFixContinuationPollInterval = RunCommand.FreshFixContinuationPollInterval;
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;
        var superviseCallCount = 0;
        var timestampCallCount = 0;

        try
        {
            RunCommand.TimestampFactory = () =>
            {
                timestampCallCount++;
                return timestampCallCount == 1
                    ? DateTimeOffset.Parse("2026-04-18T04:46:59.9000000+00:00")
                    : DateTimeOffset.Parse("2026-04-18T04:47:31.2000000+00:00");
            };
            RunCommand.FreshFixContinuationPollInterval = TimeSpan.Zero;
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M .intent-cli/intake/toy-calc.concept.yaml
                 M .intent-cli/intake/toy-calc.execution.md
                 M .intent-cli/intake/toy-calc.patch.md
                """);
            RunCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "pid:999999",
                    provider: "Codex",
                    launchedAt: "2026-04-18T04:46:59.7953570+00:00");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-18T04:46:59.7953570+00:00",
                            ExecutionUnit = executionUnit,
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
                        }
                    ],
                    sessionId: "pid:999999",
                    provider: "Codex");

                File.WriteAllText(
                    Path.Combine(repoRoot, ".intent-cli", "supervision", $"{executionUnit}.session.json"),
                    RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
                    {
                        ExecutionUnit = executionUnit,
                        WorkerEntry = RunSupervisionWorkerEntry.Fix,
                        Status = RunSupervisionSessionStatus.Monitoring,
                        QueueState = "fixing",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = $"issue-226-{executionUnit.ToLowerInvariant()}",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                        CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                        HandoffArtifactRef = $".intent-cli/fix/{executionUnit}.request.md",
                        RetryCount = 2,
                        RetryBudget = 3,
                        CreatedAt = DateTimeOffset.Parse("2026-04-18T04:46:59.7953570+00:00"),
                        UpdatedAt = DateTimeOffset.Parse("2026-04-18T04:46:59.7953570+00:00"),
                        LastHeartbeatAt = DateTimeOffset.Parse("2026-04-18T04:46:59.7953570+00:00")
                    }));
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
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        Transport = "responses",
                        ProviderSessionId = "pid:999999",
                        RunStatus = "running",
                        TransportSummary = "launched"
                    }
                };
            };
            RunCommand.RunSuperviseExecutor = (context, executionUnit) =>
            {
                superviseCallCount++;
                if (superviseCallCount == 1)
                {
                    WriteDirectRunResult(
                        repoRoot,
                        executionUnit,
                        "fix",
                        "running",
                        providerEvents: CreateToyCalcLongerLivedRuntimeArtifactOnlyFixProgressProviderEvents(
                            executionUnit,
                            "pid:999999",
                            includeBackendExit: false),
                        sessionId: "pid:999999",
                        provider: "Codex");

                    return new RunSuperviseResult
                    {
                        ExecutionUnit = executionUnit,
                        SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                        WorkerEntry = RunSupervisionWorkerEntry.Fix,
                        SessionStatus = RunSupervisionSessionStatus.Monitoring,
                        RetryCount = 2,
                        RetryBudget = 3,
                        HandoffArtifactRef = $".intent-cli/fix/{executionUnit}.request.md"
                    };
                }

                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "running",
                    providerEvents: CreateToyCalcLongerLivedRuntimeArtifactOnlyFixProgressProviderEvents(
                        executionUnit,
                        "pid:999999",
                        includeBackendExit: true),
                    sessionId: "pid:999999",
                    provider: "Codex");
                return RunSuperviseCommand.ExecuteCore(context, executionUnit);
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Equal(3, result.Actions.Count);
            Assert.Equal("run fix", result.Actions[0].Name);
            Assert.Equal("run supervise", result.Actions[1].Name);
            Assert.Equal("run supervise", result.Actions[2].Name);
            Assert.Contains("out-of-scope runtime-artifact drift", result.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("Worker remains under supervision.", result.Detail, StringComparison.Ordinal);
            Assert.Equal(2, superviseCallCount);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("out-of-scope runtime-artifact drift", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(2, session.RetryCount);
            Assert.Contains("out-of-scope runtime-artifact drift", session.LastInterruptionReason, StringComparison.Ordinal);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("failed", resultArtifact.RunStatus);
            Assert.Equal("pid:999999", resultArtifact.SessionId);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("out-of-scope runtime-artifact drift", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.RunFixExecutor = originalRunFixExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RunCommand.TimestampFactory = originalTimestampFactory;
            RunCommand.FreshFixContinuationPollInterval = originalFreshFixContinuationPollInterval;
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithSucceededFixResultAndExplicitContractGapRefusal_DoesNotAdvanceIntoReview()
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
            "succeeded",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:999999",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                        "I stopped with a contract-gap explanation rather than inventing a repair target because the deterministic review contract points at `intents/toy-calc/specs/01-cli-surface.md`, and that spec file does not exist in this worktree.")
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
                        "2. Close this run as a completed contract-gap refusal.")
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
                        exit_code = 0
                    })
                }
            ],
            sessionId: "pid:999999",
            provider: "Codex");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("deterministic-contract-gap", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("contract-gap explanation", result.Detail, StringComparison.OrdinalIgnoreCase);

        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
        Assert.Equal("failed", resultArtifact.RunStatus);
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithEvidenceOnlyReviewFollowUpAmbiguityAfterSuccessfulExit_DoesNotSilentlyAdvanceIntoReview()
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
        WriteDirectRunRequest(
            repoRoot,
            "G226",
            "fix",
            "pid:11911",
            provider: "Codex",
            launchedAt: "2026-04-20T23:40:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "running",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-20T23:40:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:11911",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/G226.request.md' succeeded in 0ms")
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-20T23:40:00.0500000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:11911",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'pwd && rg --files . | sed -n ''1,200p''' succeeded in 0ms")
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-20T23:40:00.1000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:11911",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement("I cannot tell whether repo-local intent/spec artifacts lag implementation or whether the review comment asks for a narrower contract detail, but the comment asks for stronger verification: add a real process-boundary test and tighten invalid-usage assertions to exact exit code == 1, empty stdout, and canonical stderr.")
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-20T23:40:00.2000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:11911",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 0
                    })
                }
            ],
            sessionId: "pid:11911",
            provider: "Codex");

        DirectRunTerminalArtifactUpdater.PersistTerminalRunStatusIfCurrent(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl"),
            "pid:11911",
            DateTimeOffset.Parse("2026-04-20T23:40:00.0000000+00:00"),
            exitCode: 0);

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("deterministic-contract-gap", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("bounded repair outcome", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fix direct run failed for 'G226'.", result.Detail, StringComparison.Ordinal);

        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
        Assert.Equal("failed", resultArtifact.RunStatus);

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl")));
        Assert.Contains(providerEvents, providerEvent =>
            providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "contract-gap", StringComparison.Ordinal)
            && providerEvent.Payload.TryGetProperty("reason", out var reasonElement)
            && string.Equals(reasonElement.GetString(), "fix-evidence-only-review-follow-up-ended-without-bounded-repair-outcome", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithFollowUpWorkAfterInitialInspection_DoesNotClassifyInspectionOnlyContractGap()
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
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999998", provider: "Codex");
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
                    SessionId = "pid:999998",
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
                    SessionId = "pid:999998",
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
                    SessionId = "pid:999998",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                        "exec /bin/zsh -lc 'sed -n ''1,120p'' src/Program.cs' succeeded in 0ms")
                },
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:03.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:999998",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ],
            sessionId: "pid:999998",
            provider: "Codex");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("non-retryable-failure", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("Fix direct run failed for 'G226'.", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("initial repo-inspection command", result.Detail, StringComparison.Ordinal);
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
    public void ExecuteCore_GivenAutoResumedImplementSessionThatDiesAfterInitialInventory_StopsWithNonRetryableFailure()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            queueStatePath,
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active))));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            runLogPath,
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

        try
        {
            RunSuperviseCommand.TerminalFailureRaceWindow = TimeSpan.Zero;
            RunSuperviseCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(repoRoot, executionUnit, "implement", "pid:4242", provider: "Claude");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents: CreateInitialInventoryImplementProviderEvents(executionUnit, "pid:4242", includeBackendExit: false),
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

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            var action = Assert.Single(result.Actions);
            Assert.Equal("run supervise", action.Name);
            Assert.Contains("initial repo-inspection command completed", result.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("auto-resumed", result.Detail, StringComparison.OrdinalIgnoreCase);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("initial repo-inspection command completed", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("failed", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-attempted", runEvents[^2].Event);
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "auto-resumed", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
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
    public void ExecuteCore_GivenStartupOnlyDeadFixWorkerSession_StopsWithNonRetryableFailureDetail()
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
            providerEvents: CreateStartupOnlyFixProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Claude");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("non-retryable-failure", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        var action = Assert.Single(result.Actions);
        Assert.Equal("run supervise", action.Name);
        Assert.Contains("during provider startup", result.Detail, StringComparison.Ordinal);
        Assert.Contains("startup warnings or noise", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Supervisor blocked 'G226' after non-retryable failure.", result.Detail, StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
        Assert.Equal(QueueItemState.Blocked, selectedItem.State);
        Assert.Contains("during provider startup", selectedItem.BlockedBy[0], StringComparison.Ordinal);

        var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
        Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
        Assert.Equal(0, session.RetryCount);
        Assert.Contains("during provider startup", session.LastInterruptionReason, StringComparison.Ordinal);

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        Assert.Equal("blocked", runEvents[^1].Event);
        Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
        Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecuteCore_GivenImplementSessionThatDiesAfterInitialInventory_StopsWithNonRetryableFailureDetail()
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
                HandoffArtifactRef = ".intent-cli/implement/G226.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "implement", "pid:45803", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "implement",
            "running",
            providerEvents: CreateInitialInventoryImplementProviderEvents("G226", "pid:45803"),
            sessionId: "pid:45803",
            provider: "Codex");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("non-retryable-failure", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        var action = Assert.Single(result.Actions);
        Assert.Equal("run supervise", action.Name);
        Assert.Contains("initial repo-inspection command completed", result.Detail, StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
        Assert.Equal(QueueItemState.Blocked, selectedItem.State);
        Assert.Contains("initial repo-inspection command completed", selectedItem.BlockedBy[0], StringComparison.Ordinal);

        var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
        Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
        Assert.Equal(0, session.RetryCount);
        Assert.Contains("initial repo-inspection command completed", session.LastInterruptionReason, StringComparison.Ordinal);

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        Assert.Equal("blocked", runEvents[^1].Event);
        Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
        Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecuteCore_GivenReactivatedImplementWithStaleFailedResult_ReclassifiesToInspectionBoundary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "TOY-CALC-V0-02"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            queueStatePath,
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Active, executionUnit: "TOY-CALC-V0-02"))));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            runLogPath,
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"TOY-CALC-V0-02","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"TOY-CALC-V0-02","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T11:55:00Z","execution_unit":"TOY-CALC-V0-02","event":"blocked","by":"intent-cli","reason":"Worker session 'pid:45803' for 'TOY-CALC-V0-02' exited with backend exit code 1."}
            {"ts":"2026-04-10T12:15:00Z","execution_unit":"TOY-CALC-V0-02","event":"activated","by":"intent-cli"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "TOY-CALC-V0-02", "packet.yaml"),
            """
            execution_unit: "TOY-CALC-V0-02"

            implementation_issue:
              issue_title: "[G129] Preserve Detached Implement Bounded Progress After Initial Inventory"
              goal: "Preserve detached implement progress boundaries."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/TOY-CALC-V0-02/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "TOY-CALC-V0-02.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "TOY-CALC-V0-02.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "TOY-CALC-V0-02",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "TOY-CALC-V0-02"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-129-toy-calc-v0-02",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                HandoffArtifactRef = ".intent-cli/implement/TOY-CALC-V0-02.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T11:55:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T11:55:00Z"),
                LastInterruptionReason = "Worker session 'pid:45803' for 'TOY-CALC-V0-02' exited with backend exit code 1."
            }));
        WriteDirectRunRequest(
            repoRoot,
            "TOY-CALC-V0-02",
            "implement",
            "pid:45803",
            provider: "Codex",
            launchedAt: "2026-04-10T12:20:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "TOY-CALC-V0-02",
            "implement",
            "failed",
            providerEvents: CreateInitialInventoryImplementProviderEvents("TOY-CALC-V0-02", "pid:45803"),
            sessionId: "pid:45803",
            provider: "Codex");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("non-retryable-failure", result.StopReason);
        Assert.Equal("TOY-CALC-V0-02", result.ExecutionUnit);
        var action = Assert.Single(result.Actions);
        Assert.Equal("run supervise", action.Name);
        Assert.Contains("initial repo-inspection command completed", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Implement direct run failed for 'TOY-CALC-V0-02'.", result.Detail, StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "TOY-CALC-V0-02");
        Assert.Equal(QueueItemState.Blocked, selectedItem.State);
        Assert.Contains("initial repo-inspection command completed", selectedItem.BlockedBy[0], StringComparison.Ordinal);

        var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "supervision", "TOY-CALC-V0-02.session.json")));
        Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
        Assert.Contains("initial repo-inspection command completed", session.LastInterruptionReason, StringComparison.Ordinal);

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "runs", "TOY-CALC-V0-02.provider.jsonl")));
        Assert.Contains(
            providerEvents,
            providerEvent => providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "contract-gap", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("reason", out var reasonElement)
                && string.Equals(reasonElement.GetString(), "implement-session-ended-after-initial-inspection", StringComparison.Ordinal));

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        Assert.Equal("blocked", runEvents[^1].Event);
        Assert.Contains("initial repo-inspection command completed", runEvents[^1].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteCore_GivenReactivatedImplementWithStaleFailedResultFromOlderSession_LaunchesFreshImplement()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "TOY-CALC-V0-02"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            queueStatePath,
            QueueStateSerializer.Serialize(CreateQueueState(
                CreateQueueItem(QueueItemState.Active, executionUnit: "TOY-CALC-V0-02") with
                {
                    BlockedBy = ["Worker session 'pid:45803' for 'TOY-CALC-V0-02' exited with backend exit code 1."]
                })));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            runLogPath,
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"TOY-CALC-V0-02","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"TOY-CALC-V0-02","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T12:05:00Z","execution_unit":"TOY-CALC-V0-02","event":"blocked","by":"intent-cli","reason":"Worker session 'pid:45803' for 'TOY-CALC-V0-02' exited with backend exit code 1."}
            {"ts":"2026-04-10T12:15:00Z","execution_unit":"TOY-CALC-V0-02","event":"activated","by":"intent-cli"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "TOY-CALC-V0-02", "packet.yaml"),
            """
            execution_unit: "TOY-CALC-V0-02"

            implementation_issue:
              issue_title: "[G130] Re-Enter Fresh Implement Launch Instead Of Reusing Stale Failed Result"
              goal: "Launch a fresh implement session after active re-entry."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/TOY-CALC-V0-02/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "TOY-CALC-V0-02.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "TOY-CALC-V0-02.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "TOY-CALC-V0-02",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "TOY-CALC-V0-02"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-130-toy-calc-v0-02",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                HandoffArtifactRef = ".intent-cli/implement/TOY-CALC-V0-02.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:05:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:05:00Z"),
                LastInterruptionReason = "Worker session 'pid:45803' for 'TOY-CALC-V0-02' exited with backend exit code 1."
            }));
        WriteDirectRunRequest(
            repoRoot,
            "TOY-CALC-V0-02",
            "implement",
            "pid:45803",
            provider: "Codex",
            launchedAt: "2026-04-10T12:00:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "TOY-CALC-V0-02",
            "implement",
            "failed",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:05:00.0000000+00:00",
                    ExecutionUnit = "TOY-CALC-V0-02",
                    Provider = "Codex",
                    EntryKind = "implement",
                    SessionId = "pid:45803",
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ],
            sessionId: "pid:45803",
            provider: "Codex");
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;

        try
        {
            RunCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "pid:4242",
                    provider: "Codex",
                    launchedAt: "2026-04-10T12:21:00.0000000+00:00");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:21:00.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Codex",
                            EntryKind = "implement",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "gpt-5.4-mini",
                                transport = "responses",
                                command = "codex"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Codex");
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:21:00Z"),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        EntryKind = "implement",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = "pid:4242",
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md",
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, "pid:4242")
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
            Assert.Equal("TOY-CALC-V0-02", result.ExecutionUnit);
            Assert.Equal(2, result.Actions.Count);
            Assert.Equal("run implement", result.Actions[0].Name);
            Assert.Equal("run supervise", result.Actions[1].Name);
            Assert.Contains("under supervision", result.Detail, StringComparison.Ordinal);

            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "TOY-CALC-V0-02.request.json")));
            Assert.Equal("pid:4242", requestArtifact.ProviderSessionId);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "TOY-CALC-V0-02.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "TOY-CALC-V0-02");
            Assert.Equal(QueueItemState.Active, selectedItem.State);
            Assert.Empty(selectedItem.BlockedBy);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "provider-lifecycle", StringComparison.Ordinal)
                && string.Equals(runEvent.SessionId, "pid:4242", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
        }
    }

    [Fact]
    public async Task ExecuteCore_GivenStartupOnlyDeadFixWorkerSessionWhenBackendExitLandsDuringRaceWindow_StopsWithNonRetryableFailureDetail()
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
            providerEvents: CreateStartupOnlyFixProviderEvents("G226", "pid:999999", includeBackendExit: false),
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRaceWindow = RunSuperviseCommand.TerminalFailureRaceWindow;
        var originalRacePollInterval = RunSuperviseCommand.TerminalFailureRacePollInterval;

        try
        {
            RunSuperviseCommand.TerminalFailureRaceWindow = TimeSpan.FromMilliseconds(700);
            RunSuperviseCommand.TerminalFailureRacePollInterval = TimeSpan.FromMilliseconds(5);

            var appendTask = Task.Run(async () =>
            {
                await Task.Delay(600);
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

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Contains("during provider startup", result.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("during provider startup", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
            RunSuperviseCommand.TerminalFailureRacePollInterval = originalRacePollInterval;
        }
    }

    [Fact]
    public void ExecuteCore_GivenStartupOnlyDeadFixWorkerSessionWithoutCapturedTerminalEvent_StopsWithNonRetryableFailureDetail()
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
            providerEvents: CreateStartupOnlyFixProviderEvents("G226", "pid:999999", includeBackendExit: false),
            sessionId: "pid:999999",
            provider: "Claude");
        var originalRaceWindow = RunSuperviseCommand.TerminalFailureRaceWindow;

        try
        {
            RunSuperviseCommand.TerminalFailureRaceWindow = TimeSpan.Zero;

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Contains("during provider startup", result.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("during provider startup", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "runs", "G226.provider.jsonl")));
            Assert.Contains(providerEvents, providerEvent =>
                providerEvent.Kind == "provider-event"
                && string.Equals(providerEvent.SessionId, "pid:999999", StringComparison.Ordinal)
                && providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal)
                && providerEvent.Payload.TryGetProperty("exit_code", out var exitCodeElement)
                && exitCodeElement.GetInt32() == 1);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.TerminalFailureRaceWindow = originalRaceWindow;
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

        Assert.Equal("non-retryable-failure", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Contains("Fix retry budget exhausted", result.Detail, StringComparison.Ordinal);
        Assert.Contains("after 3 failed attempts", result.Detail, StringComparison.Ordinal);
        Assert.Contains("pid:999999", result.Detail, StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
        Assert.Equal(QueueItemState.Blocked, selectedItem.State);
        Assert.Contains("Fix retry budget exhausted", selectedItem.BlockedBy[0], StringComparison.Ordinal);
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
    public void ExecuteCore_GivenDeadFixWorkerSessionAtRetryExhaustionWithMeaningfulWorktreeDiff_StopsWithClarificationRequired()
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
            providerEvents: CreateMeaningfulFixWorktreeProgressProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Claude");
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;

        try
        {
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M src/ToyCalc/Calculator.cs
                 M src/ToyCalc/CommandLine.cs
                 M tests/ToyCalc.Tests/CalculatorTests.cs
                """);

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));
            var rerunResult = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("clarification-required", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("meaningful execution-unit worktree changes", result.Detail, StringComparison.Ordinal);
            Assert.Contains("src/ToyCalc/Calculator.cs", result.Detail, StringComparison.Ordinal);
            Assert.Contains("post_fix_worktree_progress_policy", result.Detail, StringComparison.Ordinal);
            Assert.Equal("clarification-required", rerunResult.StopReason);
            Assert.Contains("carry this progress forward", rerunResult.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("meaningful execution-unit worktree changes", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Contains("meaningful execution-unit worktree changes", session.LastInterruptionReason, StringComparison.Ordinal);
            Assert.True(session.RequiresPostFixWorktreeProgressDecision);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("meaningful execution-unit worktree changes", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenAutoContinuePolicyForMeaningfulFixWorktreeDiff_CommitsProgressAndResubmits()
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
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "review-context.md"),
            "# Review Context");
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
            providerEvents: CreateMeaningfulFixWorktreeProgressProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Claude");
        var gitRunner = new FakeGitRunner(new Dictionary<string, GitCommandResult>
        {
            [FakeGitRunner.CreateCommandKey(["status", "--porcelain=v1", "--untracked-files=all"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut =
                    """
                     M src/ToyCalc/Calculator.cs
                     M src/ToyCalc/CommandLine.cs
                     M tests/ToyCalc.Tests/CalculatorTests.cs
                    """,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["rev-parse", "--abbrev-ref", "HEAD"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "issue-226-g226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["rev-parse", "HEAD"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "base-commit-226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["add", "--", "src/ToyCalc/Calculator.cs", "src/ToyCalc/CommandLine.cs", "tests/ToyCalc.Tests/CalculatorTests.cs"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["diff", "--cached", "--quiet"])] = new GitCommandResult
            {
                ExitCode = 1,
                StdOut = string.Empty,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["commit", "-m", "Carry forward post-fix progress for G226"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "[issue-226-g226 abc123] Carry forward post-fix progress for G226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["reset", "--mixed", "base-commit-226"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            }
        });
        var originalSuperviseGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;
        var originalRunGitCommandRunnerFactory = RunCommand.GitCommandRunnerFactory;
        var originalRunResubmitExecutor = RunCommand.RunResubmitExecutor;
        var originalRunRereviewExecutor = RunCommand.RunRereviewExecutor;

        try
        {
            RunSuperviseCommand.GitCommandRunnerFactory = () => gitRunner;
            RunCommand.GitCommandRunnerFactory = () => gitRunner;
            RunCommand.RunResubmitExecutor = (_, executionUnit) =>
            {
                Assert.Equal("G226", executionUnit);

                return new RunResubmitResult
                {
                    ExecutionUnit = executionUnit,
                    Branch = "issue-226-g226",
                    WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                };
            };
            RunCommand.RunRereviewExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Review }
                        : queueItem);
                WriteDirectRunRequest(context.RepoRoot, executionUnit, "review", "review-session");
                WriteDirectRunResult(
                    context.RepoRoot,
                    executionUnit,
                    "review",
                    "running",
                    providerEvents: [],
                    sessionId: "review-session");

                return new RunRereviewResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(
                repoRoot,
                postFixWorktreeProgressPolicy: CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("Review direct run for 'G226' is 'running'.", result.Detail, StringComparison.Ordinal);
            Assert.Contains(result.Actions, action => action.Name == "run supervise" && action.ExecutionUnit == "G226");
            Assert.Contains(result.Actions, action => action.Name == "run resubmit" && action.ExecutionUnit == "G226");
            Assert.Contains(result.Actions, action => action.Name == "run rereview" && action.ExecutionUnit == "G226");

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Review, selectedItem.State);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.False(session.RequiresPostFixWorktreeProgressDecision);

            Assert.Contains(gitRunner.Commands, command =>
                command.SequenceEqual(["status", "--porcelain=v1", "--untracked-files=all"]));
            Assert.Contains(gitRunner.Commands, command =>
                command.SequenceEqual(["rev-parse", "--abbrev-ref", "HEAD"]));
            Assert.Contains(gitRunner.Commands, command =>
                command.SequenceEqual(["add", "--", "src/ToyCalc/Calculator.cs", "src/ToyCalc/CommandLine.cs", "tests/ToyCalc.Tests/CalculatorTests.cs"]));
            Assert.Contains(gitRunner.Commands, command =>
                command.SequenceEqual(["diff", "--cached", "--quiet"]));
            Assert.Contains(gitRunner.Commands, command =>
                command.SequenceEqual(["commit", "-m", "Carry forward post-fix progress for G226"]));

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Contains(runEvents, runEvent => string.Equals(runEvent.Event, "post-fix-progress-accepted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.GitCommandRunnerFactory = originalSuperviseGitCommandRunnerFactory;
            RunCommand.GitCommandRunnerFactory = originalRunGitCommandRunnerFactory;
            RunCommand.RunResubmitExecutor = originalRunResubmitExecutor;
            RunCommand.RunRereviewExecutor = originalRunRereviewExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenAutoContinueResubmitFailure_RollsBackConfirmationBoundaryState()
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
            providerEvents: CreateMeaningfulFixWorktreeProgressProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Claude");
        var gitRunner = new FakeGitRunner(new Dictionary<string, GitCommandResult>
        {
            [FakeGitRunner.CreateCommandKey(["status", "--porcelain=v1", "--untracked-files=all"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut =
                    """
                     M src/ToyCalc/Calculator.cs
                     M src/ToyCalc/CommandLine.cs
                     M tests/ToyCalc.Tests/CalculatorTests.cs
                    """,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["rev-parse", "--abbrev-ref", "HEAD"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "issue-226-g226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["rev-parse", "HEAD"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "base-commit-226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["add", "--", "src/ToyCalc/Calculator.cs", "src/ToyCalc/CommandLine.cs", "tests/ToyCalc.Tests/CalculatorTests.cs"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["diff", "--cached", "--quiet"])] = new GitCommandResult
            {
                ExitCode = 1,
                StdOut = string.Empty,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["commit", "-m", "Carry forward post-fix progress for G226"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "[issue-226-g226 abc123] Carry forward post-fix progress for G226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["reset", "--mixed", "base-commit-226"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            }
        });
        var originalSuperviseGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;
        var originalRunGitCommandRunnerFactory = RunCommand.GitCommandRunnerFactory;
        var originalRunResubmitExecutor = RunCommand.RunResubmitExecutor;

        try
        {
            RunSuperviseCommand.GitCommandRunnerFactory = () => gitRunner;
            RunCommand.GitCommandRunnerFactory = () => gitRunner;
            RunCommand.RunResubmitExecutor = (_, _) => throw new InvalidOperationException("git push failed.");

            var result = RunCommand.ExecuteCore(CreateContext(
                repoRoot,
                postFixWorktreeProgressPolicy: CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy));

            Assert.Equal("deterministic-contract-gap", result.StopReason);
            Assert.Contains("run resubmit", result.Detail, StringComparison.Ordinal);
            Assert.Contains("git push failed.", result.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("meaningful execution-unit worktree changes", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.True(session.RequiresPostFixWorktreeProgressDecision);
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "post-fix-progress-accepted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "resubmitted", StringComparison.Ordinal));
            Assert.Contains(gitRunner.Commands, command =>
                command.SequenceEqual(["reset", "--mixed", "base-commit-226"]));
        }
        finally
        {
            RunSuperviseCommand.GitCommandRunnerFactory = originalSuperviseGitCommandRunnerFactory;
            RunCommand.GitCommandRunnerFactory = originalRunGitCommandRunnerFactory;
            RunCommand.RunResubmitExecutor = originalRunResubmitExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenRetryAfterAutoContinueResubmitFailure_ReplaysSameBoundarySuccessfully()
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
            Path.Combine("repo", ".intent-cli", "reviews", "G226.request.json"),
            "{}");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G226", "review-context.md"),
            "# Review Context");
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
            providerEvents: CreateMeaningfulFixWorktreeProgressProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Claude");
        var gitRunner = new FakeGitRunner(new Dictionary<string, GitCommandResult>
        {
            [FakeGitRunner.CreateCommandKey(["status", "--porcelain=v1", "--untracked-files=all"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut =
                    """
                     M src/ToyCalc/Calculator.cs
                     M src/ToyCalc/CommandLine.cs
                     M tests/ToyCalc.Tests/CalculatorTests.cs
                    """,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["rev-parse", "--abbrev-ref", "HEAD"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "issue-226-g226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["rev-parse", "HEAD"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "base-commit-226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["add", "--", "src/ToyCalc/Calculator.cs", "src/ToyCalc/CommandLine.cs", "tests/ToyCalc.Tests/CalculatorTests.cs"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["diff", "--cached", "--quiet"])] = new GitCommandResult
            {
                ExitCode = 1,
                StdOut = string.Empty,
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["commit", "-m", "Carry forward post-fix progress for G226"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = "[issue-226-g226 abc123] Carry forward post-fix progress for G226\n",
                StdErr = string.Empty
            },
            [FakeGitRunner.CreateCommandKey(["reset", "--mixed", "base-commit-226"])] = new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            }
        });
        var originalSuperviseGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;
        var originalRunGitCommandRunnerFactory = RunCommand.GitCommandRunnerFactory;
        var originalRunResubmitExecutor = RunCommand.RunResubmitExecutor;
        var originalRunRereviewExecutor = RunCommand.RunRereviewExecutor;
        var resubmitCalls = 0;

        try
        {
            RunSuperviseCommand.GitCommandRunnerFactory = () => gitRunner;
            RunCommand.GitCommandRunnerFactory = () => gitRunner;
            RunCommand.RunResubmitExecutor = (_, executionUnit) =>
            {
                resubmitCalls++;
                if (resubmitCalls == 1)
                {
                    throw new InvalidOperationException("git push failed.");
                }

                return new RunResubmitResult
                {
                    ExecutionUnit = executionUnit,
                    Branch = "issue-226-g226",
                    WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                };
            };
            RunCommand.RunRereviewExecutor = (context, executionUnit) =>
            {
                PersistQueueState(
                    context.RepoRoot,
                    queueItem => string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                        ? queueItem with { State = QueueItemState.Review }
                        : queueItem);
                WriteDirectRunRequest(context.RepoRoot, executionUnit, "review", "review-session");
                WriteDirectRunResult(
                    context.RepoRoot,
                    executionUnit,
                    "review",
                    "running",
                    providerEvents: [],
                    sessionId: "review-session");

                return new RunRereviewResult
                {
                    ExecutionUnit = executionUnit,
                    LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226"
                };
            };

            var firstResult = RunCommand.ExecuteCore(CreateContext(
                repoRoot,
                postFixWorktreeProgressPolicy: CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy));
            var secondResult = RunCommand.ExecuteCore(CreateContext(
                repoRoot,
                postFixWorktreeProgressPolicy: CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy));

            Assert.Equal("deterministic-contract-gap", firstResult.StopReason);
            Assert.Equal("no-actionable-item", secondResult.StopReason);
            Assert.Equal("G226", secondResult.ExecutionUnit);
            Assert.Contains(secondResult.Actions, action => action.Name == "run resubmit" && action.ExecutionUnit == "G226");
            Assert.Contains(secondResult.Actions, action => action.Name == "run rereview" && action.ExecutionUnit == "G226");

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Review, selectedItem.State);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.False(session.RequiresPostFixWorktreeProgressDecision);

            Assert.Equal(2, gitRunner.Commands.Count(command =>
                command.SequenceEqual(["commit", "-m", "Carry forward post-fix progress for G226"])));
            Assert.Equal(1, gitRunner.Commands.Count(command =>
                command.SequenceEqual(["reset", "--mixed", "base-commit-226"])));

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal(1, runEvents.Count(runEvent =>
                string.Equals(runEvent.Event, "post-fix-progress-accepted", StringComparison.Ordinal)));
        }
        finally
        {
            RunSuperviseCommand.GitCommandRunnerFactory = originalSuperviseGitCommandRunnerFactory;
            RunCommand.GitCommandRunnerFactory = originalRunGitCommandRunnerFactory;
            RunCommand.RunResubmitExecutor = originalRunResubmitExecutor;
            RunCommand.RunRereviewExecutor = originalRunRereviewExecutor;
        }
    }

    [Fact]
    public void ExecuteCore_GivenDeadFixWorkerSessionAtRetryExhaustionWithBuildOutputOnlyDiff_StaysOnBackendExitPath()
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
            providerEvents: CreateMeaningfulFixWorktreeProgressProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Claude");
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;

        try
        {
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M src/ToyCalc/bin/Debug/net10.0/ToyCalc.dll
                 M src/ToyCalc/obj/Debug/net10.0/ToyCalc.GeneratedMSBuildEditorConfig.editorconfig
                 M tests/ToyCalc.Tests/TestResults/result.trx
                """);

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("Fix retry budget exhausted", result.Detail, StringComparison.Ordinal);
            Assert.Contains("after 3 failed attempts", result.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("meaningful execution-unit worktree changes", result.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("Fix retry budget exhausted", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.Contains("backend exit code 1", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("meaningful execution-unit worktree changes", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Contains("backend exit code 1", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("retry-exhausted", runEvents[^2].Event);
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("backend exit code 1", runEvents[^2].Reason, StringComparison.Ordinal);
            Assert.Contains("backend exit code 1", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("meaningful execution-unit worktree changes", runEvents[^1].Reason, StringComparison.Ordinal);
        }
        finally
        {
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
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

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("Fix retry budget exhausted", result.Detail, StringComparison.Ordinal);
            Assert.Contains("after 3 failed attempts", result.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("Fix retry budget exhausted", selectedItem.BlockedBy[0], StringComparison.Ordinal);
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
    public void ExecuteCore_GivenFixingItemWithOnlyOutOfScopeRuntimeArtifactDiff_StopsWithoutRetryExhaustion()
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
                RetryCount = 2,
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
            providerEvents: CreateRuntimeArtifactOnlyFixProgressProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Claude");
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;

        try
        {
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M .intent-cli/intake/toy-calc.concept.yaml
                 M .intent-cli/intake/toy-calc.execution.md
                 M .intent-cli/intake/toy-calc.patch.md
                """);

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("out-of-scope runtime-artifact drift", result.Detail, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/toy-calc.concept.yaml", result.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("Fix retry budget exhausted", result.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("out-of-scope runtime-artifact drift", selectedItem.BlockedBy[0], StringComparison.Ordinal);
            Assert.DoesNotContain("Fix retry budget exhausted", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(2, session.RetryCount);
            Assert.False(session.RequiresPostFixWorktreeProgressDecision);
            Assert.Contains("out-of-scope runtime-artifact drift", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("out-of-scope runtime-artifact drift", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithToyCalcReplayShapeAndOnlyRuntimeArtifactDiff_StopsWithoutRetryExhaustion()
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
                RetryCount = 2,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999999", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "running",
            providerEvents: CreateToyCalcReplayRuntimeArtifactOnlyFixProgressProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Codex");
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;

        try
        {
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M .intent-cli/intake/toy-calc.concept.yaml
                 M .intent-cli/intake/toy-calc.execution.md
                 M .intent-cli/intake/toy-calc.patch.md
                """);

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("out-of-scope runtime-artifact drift", result.Detail, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/toy-calc.concept.yaml", result.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("Fix retry budget exhausted", result.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("out-of-scope runtime-artifact drift", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(2, session.RetryCount);
            Assert.Contains("out-of-scope runtime-artifact drift", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("out-of-scope runtime-artifact drift", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithToyCalcMixedReplayShapeAndOnlyRuntimeArtifactDiff_StopsWithoutRetryExhaustion()
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
                RetryCount = 2,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T10:00:00Z")
            }));
        WriteDirectRunRequest(repoRoot, "G226", "fix", "pid:999999", provider: "Codex");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "running",
            providerEvents: CreateToyCalcMixedReplayRuntimeArtifactOnlyFixProgressProviderEvents("G226", "pid:999999"),
            sessionId: "pid:999999",
            provider: "Codex");
        var originalGitCommandRunnerFactory = RunSuperviseCommand.GitCommandRunnerFactory;

        try
        {
            RunSuperviseCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M .intent-cli/intake/toy-calc.concept.yaml
                 M .intent-cli/intake/toy-calc.execution.md
                 M .intent-cli/intake/toy-calc.patch.md
                """);

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("non-retryable-failure", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("out-of-scope runtime-artifact drift", result.Detail, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/intake/toy-calc.concept.yaml", result.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("retry-exhausted", result.Detail, StringComparison.OrdinalIgnoreCase);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Blocked, selectedItem.State);
            Assert.Contains("out-of-scope runtime-artifact drift", selectedItem.BlockedBy[0], StringComparison.Ordinal);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.Equal(RunSupervisionSessionStatus.Blocked, session.Status);
            Assert.Equal(2, session.RetryCount);
            Assert.Contains("out-of-scope runtime-artifact drift", session.LastInterruptionReason, StringComparison.Ordinal);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal("blocked", runEvents[^1].Event);
            Assert.Contains("out-of-scope runtime-artifact drift", runEvents[^1].Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal));
            Assert.DoesNotContain(runEvents, runEvent => string.Equals(runEvent.Event, "retry-exhausted", StringComparison.Ordinal));
        }
        finally
        {
            RunSuperviseCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
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
    public void ExecuteCore_GivenFixingItemWithStaleBlockedSupervisionAndTerminalFailedFixResult_ReconcilesGenericFailureWithoutReusingPlanningSentence()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            queueStatePath,
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            runLogPath,
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            {"ts":"2026-04-10T11:55:00Z","execution_unit":"G226","event":"blocked","by":"intent-cli","reason":"backend exit code 1"}
            {"ts":"2026-04-10T12:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"retry after preserved failure"}
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
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "G226"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-226-g226",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                HandoffArtifactRef = ".intent-cli/fix/G226.request.md",
                RetryCount = 2,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T11:55:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T11:55:00Z"),
                LastInterruptionReason = "Worker session 'pid:2750' for 'G226' exited with backend exit code 1."
            }));
        WriteDirectRunRequest(
            repoRoot,
            "G226",
            "fix",
            "pid:29569",
            provider: "Codex",
            launchedAt: "2026-04-10T12:20:00.0000000+00:00");
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
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:29569",
                    Kind = "assistant-message",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        role = "assistant",
                        content = "I’m opening the request artifact and the review context to decide whether this is a repair or a contract-gap refusal."
                    })
                }
            ],
            sessionId: "pid:29569",
            provider: "Codex");

        var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

        Assert.Equal("non-retryable-failure", result.StopReason);
        Assert.Equal("G226", result.ExecutionUnit);
        Assert.Empty(result.Actions);
        Assert.Contains("Fix direct run failed for 'G226'.", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("I’m opening the request artifact", result.Detail, StringComparison.Ordinal);

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "G226");
        Assert.Equal(QueueItemState.Blocked, selectedItem.State);
        Assert.Contains("Fix direct run failed for 'G226'.", selectedItem.BlockedBy[0], StringComparison.Ordinal);

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        Assert.Equal("blocked", runEvents[^1].Event);
        var lastRunEventReason = Assert.IsType<string>(runEvents[^1].Reason);
        Assert.Contains("Fix direct run failed for 'G226'.", lastRunEventReason, StringComparison.Ordinal);
        Assert.DoesNotContain("I’m opening the request artifact", lastRunEventReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldLaunchFreshFixAttempt_GivenStaleMonitoringFixSupervisionSessionWithNewerFixRequested_ReturnsTrue()
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
            {"ts":"2026-04-10T12:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"manual retry after preserved failure"}
            """ + Environment.NewLine);
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
                CreatedAt = DateTimeOffset.Parse("2026-04-10T12:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:14:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:14:00Z")
            }));
        WriteDirectRunRequest(
            repoRoot,
            "G226",
            "fix",
            "pid:29569",
            provider: "Codex",
            launchedAt: "2026-04-10T12:20:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "failed",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:14:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:29569",
                    Kind = "assistant-message",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        role = "assistant",
                        content = "I’m reading the request artifact first, then I’ll trace the relevant code path and either make the bounded repair or give a concrete contract-gap refusal."
                    })
                }
            ],
            sessionId: "pid:29569",
            provider: "Codex");

        var requestArtifact = DirectRunRequestArtifactJson.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.request.json")));
        var resultArtifact = DirectRunResultArtifactJson.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));

        var shouldLaunch = RunCommand.ShouldLaunchFreshFixAttempt(
            CreateContext(repoRoot),
            "G226",
            requestArtifact,
            resultArtifact);

        Assert.True(shouldLaunch);
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithStaleFailedFixResultWhoseLatestActivityPredatesManualFixRequest_LaunchesFreshFix()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        tempDirectory.CreateFile(
            runLogPath,
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            {"ts":"2026-04-10T12:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"manual retry after preserved failure"}
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
        WriteDirectRunRequest(
            repoRoot,
            "G226",
            "fix",
            "pid:29569",
            provider: "Codex",
            launchedAt: "2026-04-10T12:20:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "failed",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:14:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:29569",
                    Kind = "assistant-message",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        role = "assistant",
                        content = "I’m reading the request artifact first, then I’ll trace the relevant code path and either make the bounded repair or give a concrete contract-gap refusal."
                    })
                }
            ],
            sessionId: "pid:29569",
            provider: "Codex");
        var originalRunFixExecutor = RunCommand.RunFixExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalFreshFixContinuationWindow = RunCommand.FreshFixContinuationWindow;
        var originalFreshFixContinuationTotalWindow = RunCommand.FreshFixContinuationTotalWindow;

        try
        {
            RunCommand.FreshFixContinuationWindow = TimeSpan.Zero;
            RunCommand.FreshFixContinuationTotalWindow = TimeSpan.Zero;
            RunCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "pid:4242",
                    provider: "Codex",
                    launchedAt: "2026-04-10T12:21:00.0000000+00:00");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:21:00.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Codex",
                            EntryKind = "fix",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "gpt-5.4-mini",
                                transport = "responses",
                                command = "codex"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Codex");
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:21:00Z"),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                        CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                        EntryKind = "fix",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = "pid:4242",
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

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
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, "pid:4242")
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
            Assert.DoesNotContain("I’m reading the request artifact first", result.Detail, StringComparison.Ordinal);

            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.request.json")));
            Assert.Equal("pid:4242", requestArtifact.ProviderSessionId);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "provider-lifecycle", StringComparison.Ordinal)
                && string.Equals(runEvent.SessionId, "pid:4242", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.RunFixExecutor = originalRunFixExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RunCommand.FreshFixContinuationWindow = originalFreshFixContinuationWindow;
            RunCommand.FreshFixContinuationTotalWindow = originalFreshFixContinuationTotalWindow;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithStaleMonitoringFixSessionAndFailedFixResultWhoseLatestActivityPredatesManualFixRequest_LaunchesFreshFix()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(CreateQueueItem(QueueItemState.Fixing))));
        tempDirectory.CreateFile(
            runLogPath,
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"G226","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/226"}
            {"ts":"2026-04-10T10:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"contract mismatch"}
            {"ts":"2026-04-10T12:15:00Z","execution_unit":"G226","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2","reason":"manual retry after preserved failure"}
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
                CreatedAt = DateTimeOffset.Parse("2026-04-10T12:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:14:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:14:00Z")
            }));
        WriteDirectRunRequest(
            repoRoot,
            "G226",
            "fix",
            "pid:29569",
            provider: "Codex",
            launchedAt: "2026-04-10T12:20:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "G226",
            "fix",
            "failed",
            providerEvents:
            [
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:14:00.0000000+00:00",
                    ExecutionUnit = "G226",
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = "pid:29569",
                    Kind = "assistant-message",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        role = "assistant",
                        content = "I’m reading the request artifact first, then I’ll trace the relevant code path and either make the bounded repair or give a concrete contract-gap refusal."
                    })
                }
            ],
            sessionId: "pid:29569",
            provider: "Codex");
        var originalRunFixExecutor = RunCommand.RunFixExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalFreshFixContinuationWindow = RunCommand.FreshFixContinuationWindow;
        var originalFreshFixContinuationTotalWindow = RunCommand.FreshFixContinuationTotalWindow;

        try
        {
            RunCommand.FreshFixContinuationWindow = TimeSpan.Zero;
            RunCommand.FreshFixContinuationTotalWindow = TimeSpan.Zero;
            RunCommand.RunFixExecutor = (_, executionUnit) =>
            {
                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "pid:4242",
                    provider: "Codex",
                    launchedAt: "2026-04-10T12:21:00.0000000+00:00");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "fix",
                    "running",
                    providerEvents:
                    [
                        new DirectRunProviderEvent
                        {
                            Timestamp = "2026-04-10T12:21:00.0000000+00:00",
                            ExecutionUnit = executionUnit,
                            Provider = "Codex",
                            EntryKind = "fix",
                            SessionId = "pid:4242",
                            Kind = "session-metadata",
                            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                model = "gpt-5.4-mini",
                                transport = "responses",
                                command = "codex"
                            })
                        }
                    ],
                    sessionId: "pid:4242",
                    provider: "Codex");
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:21:00Z"),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/226",
                        CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/226#issuecomment-2",
                        EntryKind = "fix",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = "pid:4242",
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

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
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, "pid:4242")
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
            Assert.DoesNotContain("I’m reading the request artifact first", result.Detail, StringComparison.Ordinal);

            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.request.json")));
            Assert.Equal("pid:4242", requestArtifact.ProviderSessionId);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G226.result.json")));
            Assert.Equal("pid:4242", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "provider-lifecycle", StringComparison.Ordinal)
                && string.Equals(runEvent.SessionId, "pid:4242", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.RunFixExecutor = originalRunFixExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RunCommand.FreshFixContinuationWindow = originalFreshFixContinuationWindow;
            RunCommand.FreshFixContinuationTotalWindow = originalFreshFixContinuationTotalWindow;
        }
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
    public void ExecuteCore_GivenLegacyBlockedFixSessionWithMeaningfulWorktreeProgress_RehydratesClarificationRequired()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G226"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(
                CreateQueueState(
                    CreateQueueItem(QueueItemState.Blocked) with
                    {
                        BlockedBy =
                        [
                            "Worker session 'pid:76095' for 'G226' exited with backend exit code 1 after bounded fix progress and left meaningful execution-unit worktree changes. Changed paths: src/ToyCalc/Calculator.cs, src/ToyCalc/CommandLine.cs, tests/ToyCalc.Tests/CalculatorTests.cs."
                        ]
                    })));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "G226.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "G226",
                WorkerEntry = RunSupervisionWorkerEntry.Fix,
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
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
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:20:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:20:00Z"),
                LastInterruptionReason = "backend exit code 1"
            }));
        var originalGitCommandRunnerFactory = RunCommand.GitCommandRunnerFactory;

        try
        {
            RunCommand.GitCommandRunnerFactory = () => new FakeGitRunner(
                """
                 M src/ToyCalc/Calculator.cs
                 M src/ToyCalc/CommandLine.cs
                 M tests/ToyCalc.Tests/CalculatorTests.cs
                """);

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("clarification-required", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("meaningful execution-unit worktree changes", result.Detail, StringComparison.Ordinal);
            Assert.Contains("src/ToyCalc/Calculator.cs", result.Detail, StringComparison.Ordinal);
            Assert.Contains("post_fix_worktree_progress_policy", result.Detail, StringComparison.Ordinal);
            Assert.Empty(result.Actions);

            var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(
                Path.Combine(repoRoot, ".intent-cli", "supervision", "G226.session.json")));
            Assert.True(session.RequiresPostFixWorktreeProgressDecision);
            Assert.Contains("meaningful execution-unit worktree changes", session.LastInterruptionReason, StringComparison.Ordinal);
            Assert.Contains("src/ToyCalc/Calculator.cs", session.LastInterruptionReason, StringComparison.Ordinal);
        }
        finally
        {
            RunCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
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

    [Fact]
    public void ExecuteCore_GivenBlockedItemWithExternallyCompletedLinkedChildState_ReconcilesCompletedWithoutParentPlanning()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(
                CreateQueueItem(QueueItemState.Blocked) with
                {
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "tomohisa/toy-calc-sample",
                        Number = 3,
                        Url = "https://github.com/tomohisa/toy-calc-sample/issues/3"
                    }
                })));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/tomohisa/toy-calc-sample/issues/3"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/tomohisa/toy-calc-sample/pull/4"}
            """ + Environment.NewLine);
        var originalGitHubCommandRunnerFactory = RunCommand.GitHubCommandRunnerFactory;
        var originalTimestampFactory = RunCommand.TimestampFactory;

        try
        {
            RunCommand.GitHubCommandRunnerFactory = () => new ScriptedGitHubCommandRunner(
            [
                new ExpectedGitHubCommand(
                    ["issue", "view", "3", "--repo", "tomohisa/toy-calc-sample", "--json", "state"],
                    Success("""{"state":"CLOSED"}""")),
                new ExpectedGitHubCommand(
                    ["pr", "view", "4", "--repo", "tomohisa/toy-calc-sample", "--json", "state,mergeCommit"],
                    Success("""{"state":"MERGED","mergeCommit":{"oid":"ccfef2c122b39f37ca5fe70744b72403b9e24234"}}"""))
            ]);
            RunCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-19T08:00:00Z");

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("externally completed linked child state", result.Detail, StringComparison.Ordinal);
            Assert.Empty(result.Actions);

            var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            var selectedItem = queueState.Items.Single(item => item.ExecutionUnit == "G226");
            Assert.Equal(QueueItemState.Completed, selectedItem.State);
            Assert.Empty(selectedItem.BlockedBy);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal("pr-merged", runEvents[^3].Event);
            Assert.Equal("issue-closed", runEvents[^2].Event);
            Assert.Equal("completed", runEvents[^1].Event);
        }
        finally
        {
            RunCommand.GitHubCommandRunnerFactory = originalGitHubCommandRunnerFactory;
            RunCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenBlockedItemWithClosedIssueButUnmergedPullRequest_StopsWithParentIntentUpdateRequired()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(
                CreateQueueItem(QueueItemState.Blocked) with
                {
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "tomohisa/toy-calc-sample",
                        Number = 3,
                        Url = "https://github.com/tomohisa/toy-calc-sample/issues/3"
                    }
                })));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"G226","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/tomohisa/toy-calc-sample/issues/3"}
            {"ts":"2026-04-10T10:10:00Z","execution_unit":"G226","event":"review","by":"intent-cli","linked_pr":"https://github.com/tomohisa/toy-calc-sample/pull/4"}
            """ + Environment.NewLine);
        var originalGitHubCommandRunnerFactory = RunCommand.GitHubCommandRunnerFactory;

        try
        {
            RunCommand.GitHubCommandRunnerFactory = () => new ScriptedGitHubCommandRunner(
            [
                new ExpectedGitHubCommand(
                    ["issue", "view", "3", "--repo", "tomohisa/toy-calc-sample", "--json", "state"],
                    Success("""{"state":"CLOSED"}""")),
                new ExpectedGitHubCommand(
                    ["pr", "view", "4", "--repo", "tomohisa/toy-calc-sample", "--json", "state,mergeCommit"],
                    Success("""{"state":"OPEN","mergeCommit":null}"""))
            ]);

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("parent-intent-update-required", result.StopReason);
            Assert.Equal("G226", result.ExecutionUnit);
            Assert.Contains("requires parent-side planning", result.Detail, StringComparison.Ordinal);
            Assert.Empty(result.Actions);
        }
        finally
        {
            RunCommand.GitHubCommandRunnerFactory = originalGitHubCommandRunnerFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenBlockedImplementSessionWithFallbackSpecRecovery_LaunchesFreshImplementContinuation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "TOY-CALC-V0-04"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            queueStatePath,
            QueueStateSerializer.Serialize(CreateQueueState(
                CreateQueueItem(QueueItemState.Blocked, executionUnit: "TOY-CALC-V0-04") with
                {
                    BlockedBy = ["Blocked item 'TOY-CALC-V0-04' requires parent-side planning."]
                })));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            runLogPath,
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"TOY-CALC-V0-04","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"TOY-CALC-V0-04","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T12:30:00Z","execution_unit":"TOY-CALC-V0-04","event":"blocked","by":"intent-cli","reason":"Blocked item 'TOY-CALC-V0-04' requires parent-side planning."}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "TOY-CALC-V0-04", "packet.yaml"),
            """
            execution_unit: "TOY-CALC-V0-04"

            implementation_issue:
              issue_title: "[G136] Repair Implement Progress After Nested-Worktree Handoff Fallback"
              goal: "Repair implement progression after nested-worktree handoff fallback."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/TOY-CALC-V0-04/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "TOY-CALC-V0-04.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "TOY-CALC-V0-04.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "TOY-CALC-V0-04",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "TOY-CALC-V0-04"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-136-toy-calc-v0-04",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                HandoffArtifactRef = ".intent-cli/implement/TOY-CALC-V0-04.request.md",
                RetryCount = 0,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:30:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:30:00Z"),
                LastInterruptionReason = "Blocked item 'TOY-CALC-V0-04' requires parent-side planning."
            }));
        WriteDirectRunRequest(
            repoRoot,
            "TOY-CALC-V0-04",
            "implement",
            "pid:27654",
            provider: "Codex",
            launchedAt: "2026-04-10T12:20:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "TOY-CALC-V0-04",
            "implement",
            "failed",
            providerEvents: CreateFallbackSpecRecoveryImplementProviderEvents("TOY-CALC-V0-04", "pid:27654"),
            sessionId: "pid:27654",
            provider: "Codex");
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalTimestampFactory = RunCommand.TimestampFactory;
        var originalFreshFixContinuationPollInterval = RunCommand.FreshFixContinuationPollInterval;
        var superviseCallCount = 0;

        try
        {
            RunCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-10T12:31:00.5000000+00:00");
            RunCommand.FreshFixContinuationPollInterval = TimeSpan.Zero;
            RunCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                const string sessionId = "pid:17682";
                const string launchedAt = "2026-04-10T12:31:00.0000000+00:00";
                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "implement",
                    sessionId,
                    provider: "Codex",
                    launchedAt: launchedAt);
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents: CreateSingleSearchImplementProviderEvents(executionUnit, sessionId),
                    sessionId: sessionId,
                    provider: "Codex");
                File.WriteAllText(
                    Path.Combine(repoRoot, ".intent-cli", "supervision", $"{executionUnit}.session.json"),
                    RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
                    {
                        ExecutionUnit = executionUnit,
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        Status = RunSupervisionSessionStatus.Monitoring,
                        QueueState = "active",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-136-toy-calc-v0-04",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                        RetryCount = 0,
                        RetryBudget = 3,
                        CreatedAt = DateTimeOffset.Parse(launchedAt),
                        UpdatedAt = DateTimeOffset.Parse(launchedAt),
                        LastHeartbeatAt = DateTimeOffset.Parse(launchedAt)
                    }));
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse(launchedAt),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        EntryKind = "implement",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = sessionId,
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md",
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, sessionId)
                };
            };
            RunCommand.RunSuperviseExecutor = (context, executionUnit) =>
            {
                superviseCallCount++;
                if (superviseCallCount == 1)
                {
                    return new RunSuperviseResult
                    {
                        ExecutionUnit = executionUnit,
                        SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        SessionStatus = RunSupervisionSessionStatus.Monitoring,
                        RetryCount = 0,
                        RetryBudget = 3,
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                    };
                }

                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "pid:17683",
                    provider: "Codex",
                    launchedAt: "2026-04-10T12:31:01.0000000+00:00");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents: CreateImplementResumedSessionProviderEvents(executionUnit, "pid:17683"),
                    sessionId: "pid:17683",
                    provider: "Codex");
                File.WriteAllText(
                    Path.Combine(repoRoot, ".intent-cli", "supervision", $"{executionUnit}.session.json"),
                    RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
                    {
                        ExecutionUnit = executionUnit,
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        Status = RunSupervisionSessionStatus.Monitoring,
                        QueueState = "active",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-136-toy-calc-v0-04",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                        RetryCount = 0,
                        RetryBudget = 3,
                        CreatedAt = DateTimeOffset.Parse("2026-04-10T12:31:01.0000000+00:00"),
                        UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:31:01.0000000+00:00"),
                        LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:31:01.0000000+00:00")
                    }));
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:31:01Z"),
                        ExecutionUnit = executionUnit,
                        Event = "retry-attempted",
                        By = "intent-cli",
                        Reason = "Worker session 'pid:17682' for 'TOY-CALC-V0-04' exited with backend exit code 1."
                    }) + Environment.NewLine);
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:31:01Z"),
                        ExecutionUnit = executionUnit,
                        Event = "auto-resumed",
                        By = "intent-cli",
                        Reason = "run implement"
                    }) + Environment.NewLine);
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:31:01Z"),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        EntryKind = "implement",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = "pid:17683",
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                    AutoResumed = true
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("TOY-CALC-V0-04", result.ExecutionUnit);
            Assert.Equal(3, result.Actions.Count);
            Assert.Equal("run implement", result.Actions[0].Name);
            Assert.Equal("run supervise", result.Actions[1].Name);
            Assert.Equal("run supervise", result.Actions[2].Name);
            Assert.Contains("auto-resumed", result.Detail, StringComparison.Ordinal);

            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "TOY-CALC-V0-04.request.json")));
            Assert.Equal("pid:17683", requestArtifact.ProviderSessionId);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "TOY-CALC-V0-04.result.json")));
            Assert.Equal("pid:17683", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "TOY-CALC-V0-04");
            Assert.Equal(QueueItemState.Active, selectedItem.State);
            Assert.Empty(selectedItem.BlockedBy);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal(
                2,
                runEvents.Count(runEvent =>
                    string.Equals(runEvent.Event, "activated", StringComparison.Ordinal)
                    && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-04", StringComparison.Ordinal)));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal)
                && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-04", StringComparison.Ordinal));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "auto-resumed", StringComparison.Ordinal)
                && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-04", StringComparison.Ordinal));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "provider-lifecycle", StringComparison.Ordinal)
                && string.Equals(runEvent.SessionId, "pid:17683", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RunCommand.TimestampFactory = originalTimestampFactory;
            RunCommand.FreshFixContinuationPollInterval = originalFreshFixContinuationPollInterval;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithoutReviewCommentButWithBlockedImplementRecovery_ReactivatesImplementContinuation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "TOY-CALC-V0-04"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            queueStatePath,
            QueueStateSerializer.Serialize(CreateQueueState(
                CreateQueueItem(QueueItemState.Fixing, executionUnit: "TOY-CALC-V0-04"))));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            runLogPath,
            """
            {"ts":"2026-04-10T09:50:00Z","execution_unit":"TOY-CALC-V0-04","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/226"}
            {"ts":"2026-04-10T10:00:00Z","execution_unit":"TOY-CALC-V0-04","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-10T12:30:00Z","execution_unit":"TOY-CALC-V0-04","event":"blocked","by":"intent-cli","reason":"Worker session 'pid:27654' for 'TOY-CALC-V0-04' exited with backend exit code 1."}
            {"ts":"2026-04-10T12:31:00Z","execution_unit":"TOY-CALC-V0-04","event":"fix-requested","by":"intent-cli","reason":"manual retry after preserved failure"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "TOY-CALC-V0-04", "packet.yaml"),
            """
            execution_unit: "TOY-CALC-V0-04"

            implementation_issue:
              issue_title: "[G136] Repair Implement Progress After Nested-Worktree Handoff Fallback"
              goal: "Repair implement progression after nested-worktree handoff fallback."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/TOY-CALC-V0-04/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "TOY-CALC-V0-04.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "TOY-CALC-V0-04.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "TOY-CALC-V0-04",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "TOY-CALC-V0-04"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-136-toy-calc-v0-04",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                HandoffArtifactRef = ".intent-cli/implement/TOY-CALC-V0-04.request.md",
                RetryCount = 3,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-10T09:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:30:00Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:30:00Z"),
                LastInterruptionReason = "Worker session 'pid:27654' for 'TOY-CALC-V0-04' exited with backend exit code 1."
            }));
        WriteDirectRunRequest(
            repoRoot,
            "TOY-CALC-V0-04",
            "implement",
            "pid:27654",
            provider: "Codex",
            launchedAt: "2026-04-10T12:20:00.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "TOY-CALC-V0-04",
            "implement",
            "failed",
            providerEvents: CreateFallbackSpecRecoveryImplementProviderEvents("TOY-CALC-V0-04", "pid:27654"),
            sessionId: "pid:27654",
            provider: "Codex");
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalTimestampFactory = RunCommand.TimestampFactory;
        var originalFreshFixContinuationPollInterval = RunCommand.FreshFixContinuationPollInterval;
        var superviseCallCount = 0;

        try
        {
            RunCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-10T12:31:00.5000000+00:00");
            RunCommand.FreshFixContinuationPollInterval = TimeSpan.Zero;
            RunCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                const string sessionId = "pid:17682";
                const string launchedAt = "2026-04-10T12:31:00.0000000+00:00";
                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "implement",
                    sessionId,
                    provider: "Codex",
                    launchedAt: launchedAt);
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents: CreateSingleSearchImplementProviderEvents(executionUnit, sessionId),
                    sessionId: sessionId,
                    provider: "Codex");
                File.WriteAllText(
                    Path.Combine(repoRoot, ".intent-cli", "supervision", $"{executionUnit}.session.json"),
                    RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
                    {
                        ExecutionUnit = executionUnit,
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        Status = RunSupervisionSessionStatus.Monitoring,
                        QueueState = "active",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-136-toy-calc-v0-04",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                        RetryCount = 0,
                        RetryBudget = 3,
                        CreatedAt = DateTimeOffset.Parse(launchedAt),
                        UpdatedAt = DateTimeOffset.Parse(launchedAt),
                        LastHeartbeatAt = DateTimeOffset.Parse(launchedAt)
                    }));
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse(launchedAt),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        EntryKind = "implement",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = sessionId,
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md",
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, sessionId)
                };
            };
            RunCommand.RunSuperviseExecutor = (context, executionUnit) =>
            {
                superviseCallCount++;
                if (superviseCallCount == 1)
                {
                    return new RunSuperviseResult
                    {
                        ExecutionUnit = executionUnit,
                        SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        SessionStatus = RunSupervisionSessionStatus.Monitoring,
                        RetryCount = 0,
                        RetryBudget = 3,
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                    };
                }

                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "pid:17683",
                    provider: "Codex",
                    launchedAt: "2026-04-10T12:31:01.0000000+00:00");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents: CreateImplementResumedSessionProviderEvents(executionUnit, "pid:17683"),
                    sessionId: "pid:17683",
                    provider: "Codex");
                File.WriteAllText(
                    Path.Combine(repoRoot, ".intent-cli", "supervision", $"{executionUnit}.session.json"),
                    RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
                    {
                        ExecutionUnit = executionUnit,
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        Status = RunSupervisionSessionStatus.Monitoring,
                        QueueState = "active",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-136-toy-calc-v0-04",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                        RetryCount = 0,
                        RetryBudget = 3,
                        CreatedAt = DateTimeOffset.Parse("2026-04-10T12:31:01.0000000+00:00"),
                        UpdatedAt = DateTimeOffset.Parse("2026-04-10T12:31:01.0000000+00:00"),
                        LastHeartbeatAt = DateTimeOffset.Parse("2026-04-10T12:31:01.0000000+00:00")
                    }));
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:31:01Z"),
                        ExecutionUnit = executionUnit,
                        Event = "retry-attempted",
                        By = "intent-cli",
                        Reason = "Worker session 'pid:17682' for 'TOY-CALC-V0-04' exited with backend exit code 1."
                    }) + Environment.NewLine);
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:31:01Z"),
                        ExecutionUnit = executionUnit,
                        Event = "auto-resumed",
                        By = "intent-cli",
                        Reason = "run implement"
                    }) + Environment.NewLine);
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-10T12:31:01Z"),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/226",
                        EntryKind = "implement",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = "pid:17683",
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                    AutoResumed = true
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("TOY-CALC-V0-04", result.ExecutionUnit);
            Assert.Equal(3, result.Actions.Count);
            Assert.Equal("run implement", result.Actions[0].Name);
            Assert.Equal("run supervise", result.Actions[1].Name);
            Assert.Equal("run supervise", result.Actions[2].Name);
            Assert.Contains("auto-resumed", result.Detail, StringComparison.Ordinal);

            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "TOY-CALC-V0-04.request.json")));
            Assert.Equal("pid:17683", requestArtifact.ProviderSessionId);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "TOY-CALC-V0-04.result.json")));
            Assert.Equal("pid:17683", resultArtifact.SessionId);
            Assert.Equal("running", resultArtifact.RunStatus);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "TOY-CALC-V0-04");
            Assert.Equal(QueueItemState.Active, selectedItem.State);
            Assert.Empty(selectedItem.BlockedBy);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Equal(
                2,
                runEvents.Count(runEvent =>
                    string.Equals(runEvent.Event, "activated", StringComparison.Ordinal)
                    && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-04", StringComparison.Ordinal)));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "fix-requested", StringComparison.Ordinal)
                && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-04", StringComparison.Ordinal));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "retry-attempted", StringComparison.Ordinal)
                && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-04", StringComparison.Ordinal));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "auto-resumed", StringComparison.Ordinal)
                && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-04", StringComparison.Ordinal));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "provider-lifecycle", StringComparison.Ordinal)
                && string.Equals(runEvent.SessionId, "pid:17683", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RunCommand.TimestampFactory = originalTimestampFactory;
            RunCommand.FreshFixContinuationPollInterval = originalFreshFixContinuationPollInterval;
        }
    }

    [Fact]
    public void ExecuteCore_GivenFixingItemWithoutReviewCommentButWithBlockedImplementLineage_ReactivatesImplementRetry()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "TOY-CALC-V0-05"));
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        tempDirectory.CreateFile(
            queueStatePath,
            QueueStateSerializer.Serialize(CreateQueueState(
                CreateQueueItem(QueueItemState.Fixing, executionUnit: "TOY-CALC-V0-05"))));
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(
            runLogPath,
            """
            {"ts":"2026-04-21T23:20:00Z","execution_unit":"TOY-CALC-V0-05","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/375"}
            {"ts":"2026-04-21T23:25:00Z","execution_unit":"TOY-CALC-V0-05","event":"activated","by":"intent-cli"}
            {"ts":"2026-04-21T23:31:52Z","execution_unit":"TOY-CALC-V0-05","event":"blocked","by":"intent-cli","reason":"Worker session 'pid:13345' for 'TOY-CALC-V0-05' exited with backend exit code 1."}
            {"ts":"2026-04-21T23:35:00Z","execution_unit":"TOY-CALC-V0-05","event":"fix-requested","by":"intent-cli","reason":"manual retry after blocked implement failure"}
            """ + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "TOY-CALC-V0-05", "packet.yaml"),
            """
            execution_unit: "TOY-CALC-V0-05"

            implementation_issue:
              issue_title: "[G139] Repair Implement Retry Routing After Blocked-Item Queue Transition To Fixing"
              goal: "Repair root retry routing after blocked implement retry transition."
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "run command"
              dependencies: []

            review:
              review_context_path: ".intent-cli/issues/TOY-CALC-V0-05/review-context.md"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "implement", "TOY-CALC-V0-05.request.md"),
            "# Execution Worker Handoff");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "supervision", "TOY-CALC-V0-05.session.json"),
            RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
            {
                ExecutionUnit = "TOY-CALC-V0-05",
                WorkerEntry = RunSupervisionWorkerEntry.Implement,
                Status = RunSupervisionSessionStatus.Blocked,
                QueueState = "blocked",
                WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", "TOY-CALC-V0-05"),
                ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                Branch = "issue-375-toy-calc-v0-05",
                LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/375",
                HandoffArtifactRef = ".intent-cli/implement/TOY-CALC-V0-05.request.md",
                RetryCount = 1,
                RetryBudget = 3,
                CreatedAt = DateTimeOffset.Parse("2026-04-21T23:20:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-04-21T23:31:52Z"),
                LastHeartbeatAt = DateTimeOffset.Parse("2026-04-21T23:31:52Z"),
                LastInterruptionReason = "Worker session 'pid:13345' for 'TOY-CALC-V0-05' exited with backend exit code 1."
            }));
        WriteDirectRunRequest(
            repoRoot,
            "TOY-CALC-V0-05",
            "implement",
            "pid:13345",
            provider: "Codex",
            launchedAt: "2026-04-21T23:30:10.0000000+00:00");
        WriteDirectRunResult(
            repoRoot,
            "TOY-CALC-V0-05",
            "implement",
            "failed",
            providerEvents: CreateIssue373LiveImplementProviderEvents("TOY-CALC-V0-05", "pid:13345"),
            sessionId: "pid:13345",
            provider: "Codex");
        var originalRunImplementExecutor = RunCommand.RunImplementExecutor;
        var originalRunSuperviseExecutor = RunCommand.RunSuperviseExecutor;
        var originalTimestampFactory = RunCommand.TimestampFactory;
        var originalFreshFixContinuationPollInterval = RunCommand.FreshFixContinuationPollInterval;
        var superviseCallCount = 0;

        try
        {
            RunCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-21T23:35:01.0000000+00:00");
            RunCommand.FreshFixContinuationPollInterval = TimeSpan.Zero;
            RunCommand.RunImplementExecutor = (_, executionUnit) =>
            {
                const string sessionId = "pid:14400";
                const string launchedAt = "2026-04-21T23:35:01.0000000+00:00";
                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "implement",
                    sessionId,
                    provider: "Codex",
                    launchedAt: launchedAt);
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents: CreateSingleSearchImplementProviderEvents(executionUnit, sessionId),
                    sessionId: sessionId,
                    provider: "Codex");
                File.WriteAllText(
                    Path.Combine(repoRoot, ".intent-cli", "supervision", $"{executionUnit}.session.json"),
                    RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
                    {
                        ExecutionUnit = executionUnit,
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        Status = RunSupervisionSessionStatus.Monitoring,
                        QueueState = "active",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-375-toy-calc-v0-05",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/375",
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                        RetryCount = 0,
                        RetryBudget = 3,
                        CreatedAt = DateTimeOffset.Parse(launchedAt),
                        UpdatedAt = DateTimeOffset.Parse(launchedAt),
                        LastHeartbeatAt = DateTimeOffset.Parse(launchedAt)
                    }));
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse(launchedAt),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/375",
                        EntryKind = "implement",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = sessionId,
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

                return new RunImplementResult
                {
                    Request = CreateRunImplementRequest(repoRoot, executionUnit),
                    ArtifactPath = $".intent-cli/implement/{executionUnit}.request.md",
                    DirectRun = CreateDirectRunLaunchResult(executionUnit, sessionId)
                };
            };
            RunCommand.RunSuperviseExecutor = (context, executionUnit) =>
            {
                superviseCallCount++;
                if (superviseCallCount == 1)
                {
                    return new RunSuperviseResult
                    {
                        ExecutionUnit = executionUnit,
                        SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        SessionStatus = RunSupervisionSessionStatus.Monitoring,
                        RetryCount = 0,
                        RetryBudget = 3,
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md"
                    };
                }

                WriteDirectRunRequest(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "pid:14401",
                    provider: "Codex",
                    launchedAt: "2026-04-21T23:35:02.0000000+00:00");
                WriteDirectRunResult(
                    repoRoot,
                    executionUnit,
                    "implement",
                    "running",
                    providerEvents: CreateImplementResumedSessionProviderEvents(executionUnit, "pid:14401"),
                    sessionId: "pid:14401",
                    provider: "Codex");
                File.WriteAllText(
                    Path.Combine(repoRoot, ".intent-cli", "supervision", $"{executionUnit}.session.json"),
                    RunSupervisionSessionArtifactJson.Serialize(new RunSupervisionSession
                    {
                        ExecutionUnit = executionUnit,
                        WorkerEntry = RunSupervisionWorkerEntry.Implement,
                        Status = RunSupervisionSessionStatus.Monitoring,
                        QueueState = "active",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit),
                        ChildRepoPath = Path.Combine(repoRoot, "submodules", "intent-system"),
                        Branch = "issue-375-toy-calc-v0-05",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/375",
                        HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                        RetryCount = 0,
                        RetryBudget = 3,
                        CreatedAt = DateTimeOffset.Parse("2026-04-21T23:35:02.0000000+00:00"),
                        UpdatedAt = DateTimeOffset.Parse("2026-04-21T23:35:02.0000000+00:00"),
                        LastHeartbeatAt = DateTimeOffset.Parse("2026-04-21T23:35:02.0000000+00:00")
                    }));
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-21T23:35:02Z"),
                        ExecutionUnit = executionUnit,
                        Event = "retry-attempted",
                        By = "intent-cli",
                        Reason = "Worker session 'pid:14400' for 'TOY-CALC-V0-05' exited with backend exit code 1."
                    }) + Environment.NewLine);
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-21T23:35:02Z"),
                        ExecutionUnit = executionUnit,
                        Event = "auto-resumed",
                        By = "intent-cli",
                        Reason = "run implement"
                    }) + Environment.NewLine);
                File.AppendAllText(
                    runLogPath,
                    RunLogSerializer.SerializeLine(new RunEvent
                    {
                        Ts = DateTimeOffset.Parse("2026-04-21T23:35:02Z"),
                        ExecutionUnit = executionUnit,
                        Event = "provider-lifecycle",
                        By = "intent-cli",
                        LinkedIssue = "https://github.com/J-Tech-Japan/intent-system/issues/375",
                        EntryKind = "implement",
                        Provider = "Codex",
                        Model = "gpt-5.4-mini",
                        SessionId = "pid:14401",
                        RunStatus = "running",
                        RawLogRef = $".intent-cli/runs/{executionUnit}.provider.jsonl",
                        ResultRef = $".intent-cli/runs/{executionUnit}.result.json",
                        PacketRef = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        ReviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md",
                        WorktreePath = Path.Combine(repoRoot, ".intent-cli", "worktrees", executionUnit)
                    }) + Environment.NewLine);

                return new RunSuperviseResult
                {
                    ExecutionUnit = executionUnit,
                    SessionArtifactPath = $".intent-cli/supervision/{executionUnit}.session.json",
                    WorkerEntry = RunSupervisionWorkerEntry.Implement,
                    SessionStatus = RunSupervisionSessionStatus.Monitoring,
                    RetryCount = 0,
                    RetryBudget = 3,
                    HandoffArtifactRef = $".intent-cli/implement/{executionUnit}.request.md",
                    AutoResumed = true
                };
            };

            var result = RunCommand.ExecuteCore(CreateContext(repoRoot));

            Assert.Equal("no-actionable-item", result.StopReason);
            Assert.Equal("TOY-CALC-V0-05", result.ExecutionUnit);
            Assert.Equal(3, result.Actions.Count);
            Assert.Equal("run implement", result.Actions[0].Name);
            Assert.Equal("run supervise", result.Actions[1].Name);
            Assert.Equal("run supervise", result.Actions[2].Name);
            Assert.Contains("auto-resumed", result.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(".intent-cli/reviews/TOY-CALC-V0-05.comment.json", result.Detail, StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            var selectedItem = Assert.Single(updatedState.Items, item => item.ExecutionUnit == "TOY-CALC-V0-05");
            Assert.Equal(QueueItemState.Active, selectedItem.State);
            Assert.Empty(selectedItem.BlockedBy);

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "fix-requested", StringComparison.Ordinal)
                && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-05", StringComparison.Ordinal));
            Assert.Contains(runEvents, runEvent =>
                string.Equals(runEvent.Event, "auto-resumed", StringComparison.Ordinal)
                && string.Equals(runEvent.ExecutionUnit, "TOY-CALC-V0-05", StringComparison.Ordinal));
        }
        finally
        {
            RunCommand.RunImplementExecutor = originalRunImplementExecutor;
            RunCommand.RunSuperviseExecutor = originalRunSuperviseExecutor;
            RunCommand.TimestampFactory = originalTimestampFactory;
            RunCommand.FreshFixContinuationPollInterval = originalFreshFixContinuationPollInterval;
        }
    }

    private static CliContext CreateContext(
        string repoRoot,
        string postFixWorktreeProgressPolicy = CliRuntimeContracts.DefaultPostFixWorktreeProgressPolicy)
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
                Run = new RunConfig
                {
                    PostFixWorktreeProgressPolicy = postFixWorktreeProgressPolicy
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

    private static string CreateIntakeExecutionArtifactMarkdown(string domain, params string[] executionUnits)
    {
        var sections = executionUnits.Select((executionUnit, index) => $$"""
            ### `{{executionUnit}}`
            source_file_path: intents/intent-cli/concepts/{{executionUnit.ToLowerInvariant()}}.md
            target_part: concepts
            dependencies:
            - {{(index == 0 ? "none" : executionUnits[index - 1])}}
            readiness_notes:
            - Ready for issue cut
            verification_hints:
            - dotnet test IntentSystem.sln
            """);

        return $$"""
            # Intake Execution Draft

            ## Domain
            `{{domain}}`

            ## Proposed Execution Units

            {{string.Join(Environment.NewLine + Environment.NewLine, sections)}}
            """;
    }

    private static string CreateIntakeExecutionArtifactMarkdown(
        string domain,
        params (string ExecutionUnit, string TargetPart)[] executionUnits)
    {
        var sections = executionUnits.Select((executionUnit, index) => $$"""
            ### `{{executionUnit.ExecutionUnit}}`
            source_file_path: intents/intent-cli/concepts/{{executionUnit.ExecutionUnit.ToLowerInvariant()}}.md
            target_part: {{executionUnit.TargetPart}}
            dependencies:
            - {{(index == 0 ? "none" : executionUnits[index - 1].ExecutionUnit)}}
            readiness_notes:
            - Ready for issue cut
            verification_hints:
            - dotnet test IntentSystem.sln
            """);

        return $$"""
            # Intake Execution Draft

            ## Domain
            `{{domain}}`

            ## Proposed Execution Units

            {{string.Join(Environment.NewLine + Environment.NewLine, sections)}}
            """;
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

    private static void AppendQueueItem(string repoRoot, QueueItem item)
    {
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var updatedState = queueState with
        {
            Items = [.. queueState.Items, item]
        };

        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));
    }

    private static IssueLifecycleExecutorSnapshot CaptureIssueLifecycleExecutors()
    {
        return new IssueLifecycleExecutorSnapshot(
            RunCommand.IssueDraftExecutor,
            RunCommand.IssueCreateExecutor,
            RunCommand.IssuePublishExecutor);
    }

    private static void RestoreIssueLifecycleExecutors(IssueLifecycleExecutorSnapshot snapshot)
    {
        RunCommand.IssueDraftExecutor = snapshot.IssueDraftExecutor;
        RunCommand.IssueCreateExecutor = snapshot.IssueCreateExecutor;
        RunCommand.IssuePublishExecutor = snapshot.IssuePublishExecutor;
    }

    private static void ConfigureFakeIssueLifecycleExecutors(ICollection<string> invokedSteps)
    {
        RunCommand.IssueDraftExecutor = (context, executionUnit) =>
        {
            invokedSteps.Add($"draft:{executionUnit}");
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = executionUnit,
                PublishStatus = "drafted",
                PacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{executionUnit}/github-body.md",
                CreatedIssueNumber = null,
                CreatedIssueUrl = null,
                PublishedLabelName = null
            };
            var artifactPath = IssuePublishArtifactPathResolver.Resolve(executionUnit);
            WriteIssuePublishArtifact(context.RepoRoot, artifactPath, artifact);
            AppendRunEvent(context.RepoRoot, "issue-drafted", executionUnit, linkedIssue: null);
            return new IssueDraftCommandResult
            {
                Artifact = artifact,
                ArtifactPath = artifactPath
            };
        };

        RunCommand.IssueCreateExecutor = (context, executionUnit) =>
        {
            invokedSteps.Add($"create:{executionUnit}");
            var artifactPath = IssuePublishArtifactPathResolver.Resolve(executionUnit);
            var artifact = IssuePublishArtifactYaml.Deserialize(ReadRepoFile(context.RepoRoot, artifactPath));
            var linkedIssue = new LinkedIssue
            {
                Repo = "J-Tech-Japan/intent-system",
                Number = 401,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/401"
            };
            var updatedArtifact = artifact with
            {
                PublishStatus = "issue-created",
                CreatedIssueNumber = linkedIssue.Number,
                CreatedIssueUrl = linkedIssue.Url,
                PublishedLabelName = null
            };
            WriteIssuePublishArtifact(context.RepoRoot, artifactPath, updatedArtifact);
            AppendRunEvent(context.RepoRoot, "issue-created", executionUnit, linkedIssue.Url);
            return new IssueCreateCommandResult
            {
                Artifact = updatedArtifact,
                ArtifactPath = artifactPath,
                LinkedIssue = linkedIssue
            };
        };

        RunCommand.IssuePublishExecutor = (context, executionUnit) =>
        {
            invokedSteps.Add($"publish:{executionUnit}");
            var artifactPath = IssuePublishArtifactPathResolver.Resolve(executionUnit);
            var artifact = IssuePublishArtifactYaml.Deserialize(ReadRepoFile(context.RepoRoot, artifactPath));
            var updatedArtifact = artifact with
            {
                PublishStatus = "published",
                PublishedLabelName = "intent-target"
            };
            WriteIssuePublishArtifact(context.RepoRoot, artifactPath, updatedArtifact);
            AppendRunEvent(context.RepoRoot, "issue-published", executionUnit, artifact.CreatedIssueUrl);
            return new IssuePublishCommandResult
            {
                Artifact = updatedArtifact,
                ArtifactPath = artifactPath,
                LinkedIssue = new LinkedIssue
                {
                    Repo = "J-Tech-Japan/intent-system",
                    Number = artifact.CreatedIssueNumber ?? 401,
                    Url = artifact.CreatedIssueUrl ?? "https://github.com/J-Tech-Japan/intent-system/issues/401"
                }
            };
        };
    }

    private static string ReadRepoFile(string repoRoot, string artifactPath)
    {
        return File.ReadAllText(Path.Combine(repoRoot, artifactPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void WriteIssuePublishArtifact(string repoRoot, string artifactPath, IssuePublishArtifact artifact)
    {
        var absolutePath = Path.Combine(repoRoot, artifactPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Artifact path did not contain a directory."));
        File.WriteAllText(absolutePath, IssuePublishArtifactYaml.Serialize(artifact));
    }

    private static void AppendRunEvent(string repoRoot, string eventName, string executionUnit, string? linkedIssue)
    {
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory."));
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = DateTimeOffset.Parse("2026-04-23T01:00:00Z"),
                ExecutionUnit = executionUnit,
                Event = eventName,
                By = "intent-cli",
                LinkedIssue = linkedIssue
            }) + Environment.NewLine);
    }

    private sealed record IssueLifecycleExecutorSnapshot(
        Func<CliContext, string, IssueDraftCommandResult> IssueDraftExecutor,
        Func<CliContext, string, IssueCreateCommandResult> IssueCreateExecutor,
        Func<CliContext, string, IssuePublishCommandResult> IssuePublishExecutor);

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

    private static void InitializeRealRunSubmitTestRepo(
        string childRepoPath,
        string worktreePath,
        string originPath,
        string branchName)
    {
        RunRealGit(childRepoPath, "init", "--initial-branch=main");
        RunRealGit(childRepoPath, "config", "user.name", "Intent System Tests");
        RunRealGit(childRepoPath, "config", "user.email", "intent-system-tests@example.com");

        Directory.CreateDirectory(Path.Combine(childRepoPath, "tests", "ToyCalc.Tests"));
        File.WriteAllText(
            Path.Combine(childRepoPath, "tests", "ToyCalc.Tests", "CalculatorTests.cs"),
            """
            namespace ToyCalc.Tests;

            public sealed class CalculatorTests
            {
            }
            """);

        RunRealGit(childRepoPath, "add", "--all");
        RunRealGit(childRepoPath, "commit", "-m", "Initial commit");

        RunRealGit(originPath, "init", "--bare", "--initial-branch=main");
        RunRealGit(childRepoPath, "remote", "add", "origin", originPath);
        RunRealGit(childRepoPath, "push", "-u", "origin", "main");
        RunRealGit(childRepoPath, "branch", branchName);
        RunRealGit(childRepoPath, "push", "-u", "origin", branchName);
        RunRealGit(childRepoPath, "worktree", "add", worktreePath, branchName);
    }

    private static void RunRealGit(string workingDirectory, params string[] arguments)
    {
        var result = new GitCommandRunner().Run(workingDirectory, arguments);
        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed in '{workingDirectory}' with stderr: {result.StdErr}");
    }

    private static string RunRealGitStdOut(string workingDirectory, params string[] arguments)
    {
        var result = new GitCommandRunner().Run(workingDirectory, arguments);
        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed in '{workingDirectory}' with stderr: {result.StdErr}");
        return result.StdOut.Trim();
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateStartupOnlyFixProviderEvents(
        string executionUnit,
        string sessionId,
        bool includeBackendExit = true)
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
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
                Timestamp = "2026-04-10T12:00:00.2000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("OpenAI Codex v0.118.0 (research preview)")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.2500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("--------")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.3000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("workdir: /repo/.intent-cli/worktrees/G226")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.3500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("model: gpt-5.4")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.4000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("reasoning summaries: none")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.4500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("session id: sess_123")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.5000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("user")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.6000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("provider: openai")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.6500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("approval: never")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.7000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("sandbox: danger-full-access")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.7500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("reasoning effort: high")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.8000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("Please diagnose the startup-only backend exit reproduction for issue #295.")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.9000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("warn plugin manifest falling_back after state db discrepancy on slow path")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.9500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "2026-04-10T12:00:00.9500000Z  WARN codex_core::shell_snapshot: Failed to delete shell snapshot at \"/tmp/snapshot\"")
            }
        ];

        if (includeBackendExit)
        {
            providerEvents =
            [
                .. providerEvents,
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                    ExecutionUnit = executionUnit,
                    Provider = "Claude",
                    EntryKind = "fix",
                    SessionId = sessionId,
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ];
        }

        return providerEvents;
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateInitialInventoryImplementProviderEvents(
        string executionUnit,
        string sessionId,
        bool includeBackendExit = true)
    {
        var providerEvents = new List<DirectRunProviderEvent>
        {
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "session-metadata",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    model = "gpt-5.4-mini",
                    transport = "sdk",
                    command = "codex"
                })
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.3000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("Use the request artifact at '/repo/.intent-cli/implement/G226.request.md' as the bounded source of truth for this direct run.")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.6000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'rg --files' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.8000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("warn plugin manifest falling_back after state db discrepancy on slow path")
            }
        };

        if (includeBackendExit)
        {
            providerEvents.Add(new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            });
        }

        return providerEvents;
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateFallbackSpecRecoveryImplementProviderEvents(
        string executionUnit,
        string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
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
                Timestamp = "2026-04-10T12:20:00.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.2000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"sed -n '1,220p' /repo/.intent-cli/implement/TOY-CALC-V0-04.request.md\" in /repo/.intent-cli/worktrees/TOY-CALC-V0-04")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.3000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(" succeeded in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.4000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("# Execution Worker Handoff")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.5000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.6000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"rg --files . | rg 'packet.yaml|04-max-command.md|intents/toy-calc|cli|max'\" in /repo/.intent-cli/worktrees/TOY-CALC-V0-04")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.7000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(" succeeded in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.8000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("intents/toy-calc/specs/04-max-command.md")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:00.9000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:01.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"sed -n '1,240p' /repo/intents/toy-calc/specs/04-max-command.md\" in /repo/.intent-cli/worktrees/TOY-CALC-V0-04")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:01.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(" succeeded in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:01.2000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("## Max command")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:20:02.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateIssue373LiveImplementProviderEvents(
        string executionUnit,
        string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:10.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
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
                Timestamp = "2026-04-21T23:30:11.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "Use the request artifact at '/repo/.intent-cli/implement/TOY-CALC-V0-05.request.md' as the bounded source of truth for this direct run.")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:12.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "/bin/zsh -lc \"pwd && ls -la && sed -n '1,220p' /repo/.intent-cli/implement/TOY-CALC-V0-05.request.md\" in /repo/.intent-cli/worktrees/TOY-CALC-V0-05")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:13.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "/bin/zsh -lc \"rg --files -g 'README*' -g 'src/**' -g 'tests/**' -g '.intent-cli/**' | sed -n '1,220p'\" in /repo/.intent-cli/worktrees/TOY-CALC-V0-05")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:14.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("src/ToyCalc/Program.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:14.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("src/ToyCalc/Calculator.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:14.2000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("src/ToyCalc/CommandLine.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:14.3000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("tests/ToyCalc.Tests/CalculatorTests.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:15.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "/bin/zsh -lc \"sed -n '1,220p' intents/toy-calc/specs/05-division-command.md\" in /repo/.intent-cli/worktrees/TOY-CALC-V0-05")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:15.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "sed: intents/toy-calc/specs/05-division-command.md: No such file or directory")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:16.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "/bin/zsh -lc \"sed -n '1,220p' tests/ToyCalc.Tests/CalculatorTests.cs\" in /repo/.intent-cli/worktrees/TOY-CALC-V0-05")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:30:17.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "/bin/zsh -lc \"sed -n '1,220p' src/ToyCalc/Program.cs && printf '\\n---\\n' && sed -n '1,220p' src/ToyCalc/CommandLine.cs && printf '\\n---\\n' && sed -n '1,220p' src/ToyCalc/Calculator.cs\" in /repo/.intent-cli/worktrees/TOY-CALC-V0-05")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-21T23:31:51.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateSingleSearchImplementProviderEvents(
        string executionUnit,
        string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:31:00.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
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
                Timestamp = "2026-04-10T12:31:00.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'rg -n \"max command|max|Max Command|maximum\" -S .' succeeded in 0ms")
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateImplementResumedSessionProviderEvents(
        string executionUnit,
        string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:31:01.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
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
                Timestamp = "2026-04-10T12:31:01.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "implement",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'sed -n ''1,220p'' src/IntentSystem.Cli/Commands/RunCommand.cs' succeeded in 0ms")
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateMeaningfulFixWorktreeProgressProviderEvents(
        string executionUnit,
        string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
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
                Timestamp = "2026-04-10T12:00:00.5000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec /bin/zsh -lc 'rg --files' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.7500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("git status --short")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateRuntimeArtifactOnlyFixProgressProviderEvents(
        string executionUnit,
        string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
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
                Timestamp = "2026-04-10T12:00:00.0500000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "exec /bin/zsh -lc 'sed -n ''1,220p'' /repo/.intent-cli/fix/G226.request.md' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.1000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "exec /bin/zsh -lc 'sed -n ''1,220p'' .intent-cli/fix/G226.request.md' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.2000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "exec /bin/zsh -lc 'sed -n ''1,220p'' intents/toy-calc/README.md' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.3000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "exec /bin/zsh -lc 'sed -n ''1,220p'' src/ToyCalc/Calculator.cs' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.4000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "exec /bin/zsh -lc 'dotnet test' succeeded in 0ms")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:00.5000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    "Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-10T12:00:01.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Claude",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateToyCalcReplayRuntimeArtifactOnlyFixProgressProviderEvents(
        string executionUnit,
        string sessionId)
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
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("## Execution Contract")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2370090+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("- Continue beyond initial repository inspection; do not stop after a single listing/read-only command.")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2427050+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2428840+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"pwd && rg --files -g '!node_modules*' -g '!dist*' -g '!build*' | sed -n '1,220p'\" in /repo/.intent-cli/worktrees/G226")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2429790+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(" succeeded in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2431360+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("src/ToyCalc/Program.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2431740+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("src/ToyCalc/Calculator.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2432870+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("intents/toy-calc/clarifications/open.md")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2434640+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("tests/ToyCalc.Tests/CalculatorTests.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:29:47.2463570+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("2026-04-17T05:29:47.246315Z  WARN codex_core::plugins::manifest: ignoring interface.defaultPrompt: prompt must be at most 128 characters")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-17T05:30:36.3739630+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateToyCalcLongerLivedRuntimeArtifactOnlyFixProgressProviderEvents(
        string executionUnit,
        string sessionId,
        bool includeBackendExit)
    {
        IReadOnlyList<DirectRunProviderEvent> providerEvents =
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-18T04:46:59.7953570+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
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
                Timestamp = "2026-04-18T04:47:29.8899000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-18T04:47:29.8901000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"sed -n '1,220p' tests/ToyCalc.Tests/CalculatorTests.cs && printf '\\n---\\n' && sed -n '1,220p' src/ToyCalc/Calculator.cs\" in /repo/.intent-cli/worktrees/G226")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-18T04:47:29.8903000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(" succeeded in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-18T04:47:29.8904000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("tests/ToyCalc.Tests/CalculatorTests.cs")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-18T04:47:29.8905140+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("src/ToyCalc/Calculator.cs")
            }
        ];

        if (includeBackendExit)
        {
            providerEvents =
            [
                .. providerEvents,
                new DirectRunProviderEvent
                {
                    Timestamp = "2026-04-18T04:48:05.0865860+00:00",
                    ExecutionUnit = executionUnit,
                    Provider = "Codex",
                    EntryKind = "fix",
                    SessionId = sessionId,
                    Kind = "provider-event",
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        type = "backend-exit",
                        exit_code = 1
                    })
                }
            ];
        }

        return providerEvents;
    }

    private static IReadOnlyList<DirectRunProviderEvent> CreateToyCalcMixedReplayRuntimeArtifactOnlyFixProgressProviderEvents(
        string executionUnit,
        string sessionId)
    {
        return
        [
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:18.4211970+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"sed -n '1,220p' '/repo/.intent-cli/fix/G226.request.md'\" in /repo/.intent-cli/worktrees/G226")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:18.4212630+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(" succeeded in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:18.4213240+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("# Repair Worker Handoff")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9131420+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9134060+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"sed -n '1,220p' intents/toy-calc/specs/01-cli-surface.md\" in /repo/.intent-cli/worktrees/G226")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9135080+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(" exited 1 in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9135740+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("sed: intents/toy-calc/specs/01-cli-surface.md: No such file or directory")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9167470+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9168690+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"sed -n '1,220p' src/ToyCalc/Program.cs && printf '\\n---\\n' && sed -n '1,220p' src/ToyCalc/CommandLine.cs && printf '\\n---\\n' && sed -n '1,220p' src/ToyCalc/Calculator.cs\" in /repo/.intent-cli/worktrees/G226")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9169560+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9170390+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc \"sed -n '1,220p' tests/ToyCalc.Tests/CalculatorTests.cs && printf '\\n---\\n' && sed -n '1,220p' tests/ToyCalc.Tests/ToyCalc.Tests.csproj && printf '\\n---\\n' && sed -n '1,220p' src/ToyCalc/ToyCalc.csproj\" in /repo/.intent-cli/worktrees/G226")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:25.9171110+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(" succeeded in 0ms:")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:35.4885360+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("I've found a likely contract-gap: the canonical spec file referenced by the artifact is not present in this worktree, but the actual code currently uses `int` parsing and arithmetic already, which matches the review comment's requested fix. I'm running the test suite now to distinguish \"already fixed\" from \"needs a small local repair.\"")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:36.0546370+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("exec")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:36.0548610+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement("/bin/zsh -lc 'dotnet test ToyCalc.sln' in /repo/.intent-cli/worktrees/G226")
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-16T20:43:40.0000000+00:00",
                ExecutionUnit = executionUnit,
                Provider = "Codex",
                EntryKind = "fix",
                SessionId = sessionId,
                Kind = "provider-event",
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    type = "backend-exit",
                    exit_code = 1
                })
            }
        ];
    }

    private static void WriteDirectRunRequest(
        string repoRoot,
        string executionUnit,
        string entryKind,
        string providerSessionId,
        string provider = "ReviewBot",
        string launchedAt = "2026-04-10T12:00:00.0000000+00:00")
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
                    LaunchedAt = launchedAt,
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

    private static GitHubCommandResult Success(string stdOut)
    {
        return new GitHubCommandResult
        {
            ExitCode = 0,
            StdOut = stdOut,
            StdErr = string.Empty
        };
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

    private sealed class FakeReviewAcceptClient : IReviewAcceptClient
    {
        public bool RequireReadyBeforeMerge { get; init; }

        public int MergeAttempts { get; private set; }

        public List<string> ReadyMarkedPrs { get; } = [];

        public void MarkPullRequestReady(string linkedPr)
        {
            ReadyMarkedPrs.Add(linkedPr);
        }

        public string MergePullRequest(string linkedPr)
        {
            MergeAttempts++;
            if (RequireReadyBeforeMerge && ReadyMarkedPrs.Count == 0)
            {
                throw new InvalidOperationException("gh: Pull Request is still a draft (HTTP 405)");
            }

            return "abc123";
        }

        public void CloseIssue(string linkedIssue)
        {
        }
    }

    private sealed class FakeRunSubmitPublisher : IRunSubmitPublisher
    {
        public string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body)
        {
            return "https://github.com/J-Tech-Japan/intent-system/pull/226";
        }
    }

    private sealed record ExpectedReviewCommand(IReadOnlyList<string> Arguments, ReviewCommandResult Result);

    private sealed record ExpectedGitHubCommand(IReadOnlyList<string> Arguments, GitHubCommandResult Result);

    private sealed class ScriptedReviewCommandRunner(IReadOnlyList<ExpectedReviewCommand> expectedCalls) : IReviewCommandRunner
    {
        private readonly Queue<ExpectedReviewCommand> expectedCalls = new(expectedCalls);

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public ReviewCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());

            Assert.NotEmpty(expectedCalls);
            var expected = expectedCalls.Dequeue();
            Assert.Equal(expected.Arguments, arguments);
            return expected.Result;
        }
    }

    private sealed class ScriptedGitHubCommandRunner(IReadOnlyList<ExpectedGitHubCommand> expectedCalls) : IGitHubCommandRunner
    {
        private readonly Queue<ExpectedGitHubCommand> expectedCalls = new(expectedCalls);

        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            Assert.NotEmpty(expectedCalls);
            var expected = expectedCalls.Dequeue();
            Assert.Equal(expected.Arguments, arguments);
            return expected.Result;
        }
    }

    private sealed class RealGitRunnerWithRemoteOriginOverride(string childRepoPath, string overriddenOriginUrl) : IGitCommandRunner
    {
        private readonly GitCommandRunner innerRunner = new();

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            if (string.Equals(workingDirectory, childRepoPath, StringComparison.Ordinal)
                && arguments.SequenceEqual(["remote", "get-url", "origin"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = overriddenOriginUrl + Environment.NewLine,
                    StdErr = string.Empty
                };
            }

            return innerRunner.Run(workingDirectory, arguments);
        }
    }

    private sealed class FakeGitRunner : IGitCommandRunner
    {
        private readonly string? statusOutput;
        private readonly IReadOnlyDictionary<string, GitCommandResult>? scriptedResults;
        private readonly Queue<string>? statusSequence;

        public FakeGitRunner(string statusOutput)
        {
            this.statusOutput = statusOutput;
        }

        public FakeGitRunner(IReadOnlyDictionary<string, GitCommandResult> scriptedResults)
        {
            this.scriptedResults = scriptedResults;
        }

        public FakeGitRunner(IReadOnlyDictionary<string, GitCommandResult> scriptedResults, IReadOnlyList<string> statusSequence)
        {
            this.scriptedResults = scriptedResults;
            this.statusSequence = new Queue<string>(statusSequence);
        }

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Commands.Add(arguments.ToArray());

            if (scriptedResults is not null)
            {
                var key = CreateCommandKey(arguments);
                if (!scriptedResults.TryGetValue(key, out var result))
                {
                    throw new Xunit.Sdk.XunitException($"Unexpected git command: {string.Join(" ", arguments)}");
                }

                if (arguments.SequenceEqual(["status", "--porcelain=v1", "--untracked-files=all"])
                    && statusSequence is not null)
                {
                    return result with
                    {
                        StdOut = statusSequence.Count > 0
                            ? statusSequence.Dequeue()
                            : result.StdOut
                    };
                }

                return result;
            }

            Assert.Equal(["status", "--porcelain=v1", "--untracked-files=all"], arguments);
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = statusOutput ?? string.Empty,
                StdErr = string.Empty
            };
        }

        public static string CreateCommandKey(IReadOnlyList<string> arguments)
        {
            return string.Join("\u001f", arguments);
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
