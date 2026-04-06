using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeEnqueueCommandTests
{
    [Fact]
    public void Execute_GivenExecutionArtifactAndPackets_EnqueuesSelectedDomainUnitsAndAppendsRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifact("auth", ["AUTH-01", "AUTH-02"]));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "packet.yaml"),
            CreatePacketYaml("AUTH-01", ["G3"]));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-02", "packet.yaml"),
            CreatePacketYaml("AUTH-02", ["AUTH-01", "G4"]));
        var originalTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        using var writer = new StringWriter();

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:00:00Z");

            var exitCode = IntakeEnqueueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Intake enqueue processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-02", output, StringComparison.Ordinal);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            var auth01 = queueState.Items.Single(item => item.ExecutionUnit == "AUTH-01");
            var auth02 = queueState.Items.Single(item => item.ExecutionUnit == "AUTH-02");
            Assert.Equal(QueueItemState.Queued, auth01.State);
            Assert.Equal(["G3"], auth01.Dependencies);
            Assert.Empty(auth01.BlockedBy);
            Assert.Equal(".intent-cli/issues/AUTH-01/packet.yaml", auth01.PacketPaths.Yaml);
            Assert.Equal("Claude", auth01.WorkerRole);
            Assert.Equal("Codex", auth01.ReviewRole);
            Assert.Equal(QueueItemState.Queued, auth02.State);
            Assert.Equal(["AUTH-01", "G4"], auth02.Dependencies);
            Assert.Equal(["AUTH-01", "G4"], auth02.BlockedBy);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(2, runEvents.Count);
            Assert.All(runEvents, runEvent => Assert.Equal("queued", runEvent.Event));
            Assert.Equal(["AUTH-01", "AUTH-02"], runEvents.Select(runEvent => runEvent.ExecutionUnit).ToArray());
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenExistingQueueItem_SkipsWithoutDuplicateEnqueue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(includeExisting: true)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifact("auth", ["AUTH-01"]));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "packet.yaml"),
            CreatePacketYaml("AUTH-01", ["G3"]));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = IntakeEnqueueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Skipped units:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingExecutionArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = IntakeEnqueueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake execution artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPacketArtifact_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifact("auth", ["AUTH-01"]));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = IntakeEnqueueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
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
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static QueueState CreateQueueState(bool includeExisting = false)
    {
        var items = new List<QueueItem>
        {
            CreateItem("G3", QueueItemState.Completed),
            CreateItem("G4", QueueItemState.Queued)
        };

        if (includeExisting)
        {
            items.Add(CreateItem("AUTH-01", QueueItemState.Queued));
        }

        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            Items = items
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Existing Item",
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
            LinkedIssue = null,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreateExecutionArtifact(string domain, IReadOnlyList<string> executionUnits)
    {
        var sections = executionUnits.Select(executionUnit =>
            $$"""

            ### `{{executionUnit}}`
            source_file_path: intents/intent-cli/concepts/{{executionUnit.ToLowerInvariant()}}.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Current heading: # {{executionUnit}}
            verification_hints:
            - dotnet test IntentSystem.sln
            """);

        return $$"""
            # Intake Execution Draft

            ## Domain
            `{{domain}}`

            ## Proposed Execution Units{{string.Concat(sections)}}
            """;
    }

    private static string CreatePacketYaml(string executionUnit, IReadOnlyList<string> dependencies)
    {
        var dependencyLines = dependencies.Count == 0
            ? "    - \"none\""
            : string.Join(Environment.NewLine, dependencies.Select(value => $"    - \"{value}\""));

        return $$"""
            execution_unit: {{executionUnit}}
            implementation_issue:
              issue_title: "{{executionUnit}} Queue Item"
              goal: "Enqueue generated issue artifact into queue artifacts."
              in_scope:
                - "queue insertion"
              out_of_scope:
                - "workflow execution"
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "cli intake enqueue command"
              dependencies:
            {{dependencyLines}}
              technical_baseline:
                - "C# / .NET"
              project_local_guidance:
                - "AGENTS.md"
              intent_baseline:
                - "intake enqueue stays thin"
              acceptance_criteria:
                - "queue item inserted"
              verification:
                - "tests-passing"

            review:
              summarize_first: true
              require_explicit_diff_check: true
              require_explicit_scope_check: true
              require_explicit_contract_check: true
              required_checks:
                - "intake enqueue remains thin"
            """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-enqueue-command-tests-").FullName;

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
