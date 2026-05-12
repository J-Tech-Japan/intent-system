using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class RunLogCommandTests
{
    [Fact]
    public void Execute_GivenQueueItemAndRunLog_WritesSelectedExecutionUnitHistoryWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunLogCommand.Execute(CreateContext(repoRoot), ["G18"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Execution unit: G18", output, StringComparison.Ordinal);
        Assert.Contains("Current state: fixing", output, StringComparison.Ordinal);
        Assert.Contains("event=issue-created", output, StringComparison.Ordinal);
        Assert.Contains("event=activated", output, StringComparison.Ordinal);
        Assert.Contains("event=review", output, StringComparison.Ordinal);
        Assert.Contains("event=fix-requested", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Execution unit: A1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("https://github.com/J-Tech-Japan/intent-system/pull/12", output, StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenNoMatchingRunEvents_WritesNoneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-07T08:00:00Z","execution_unit":"A1","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/12"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var originalRunLog = File.ReadAllText(runLogPath);

        var exitCode = RunLogCommand.Execute(CreateContext(repoRoot), ["G18"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("- none", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Equal(originalRunLog, File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = RunLogCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenMissingQueueItem_ReturnsExitCodeOne()
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

        var exitCode = RunLogCommand.Execute(CreateContext(repoRoot), ["G99"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("was not found in queue state", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenTargetRepoAndScopedRunLog_ReadsScopedRunLogAndPreservesPacketLookup()
    {
        // G327: review-runtime read-path integration. When the caller
        // names the (domain, owner/repo) explicitly, RunLogCommand
        // resolves the per-scope `.intent-cli/runtime/<domain>/<owner>__<repo>/runs.jsonl`
        // instead of the shared root file. Packet lookup is unchanged —
        // queueItem.PacketPaths.Yaml still points at the design-owned
        // `.intent-cli/issues/<execution-unit>/packet.yaml`.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        // Seed a DIFFERENT legacy runs.jsonl so the test can detect
        // accidental fallback to the root path.
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-07T08:00:00Z","execution_unit":"G18","event":"issue-created","by":"legacy-root","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/64"}""" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runtime", "intent-system",
                "J-Tech-Japan__intent-system", "runs.jsonl"),
            """{"ts":"2026-04-07T09:00:00Z","execution_unit":"G18","event":"review","by":"scoped-runtime","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/65"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = RunLogCommand.Execute(
            CreateContext(repoRoot),
            ["G18", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Run log source: scoped", output, StringComparison.Ordinal);
        Assert.Contains(
            Path.Combine("runtime", "intent-system", "J-Tech-Japan__intent-system", "runs.jsonl"),
            output,
            StringComparison.Ordinal);
        Assert.Contains("event=review", output, StringComparison.Ordinal);
        // The scoped runs.jsonl had `by=scoped-runtime`; the legacy seed
        // used `by=legacy-root`. The scoped row must win.
        Assert.DoesNotContain("legacy-root", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithTargetRepo_NeverRoutesPacketLookupThroughRuntimeScopedTree()
    {
        // G327 acceptance: even when the run-log read is scoped, the
        // packet backlog under `.intent-cli/issues/<execution-unit>/`
        // remains design-owned and MUST NOT be rerouted into
        // `.intent-cli/runtime/<domain>/<owner>__<repo>/`. The packet
        // file path comes from QueueItem.PacketPaths.Yaml and is
        // verified here against the canonical legacy location — the
        // resolver only redirects runtime audit state.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        // Seed the design-owned packet at its canonical legacy path.
        var packetPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G18", "packet.yaml"),
            "execution_unit: G18\n");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runtime", "intent-system",
                "J-Tech-Japan__intent-system", "runs.jsonl"),
            """{"ts":"2026-04-07T09:00:00Z","execution_unit":"G18","event":"review","by":"scoped-runtime"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = RunLogCommand.Execute(
            CreateContext(repoRoot),
            ["G18", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        // The original packet path under `.intent-cli/issues/G18/` is
        // still there and untouched by the scoped read.
        Assert.True(File.Exists(packetPath));
        Assert.DoesNotContain(
            Path.Combine("runtime", "intent-system", "J-Tech-Japan__intent-system", "issues"),
            writer.ToString(),
            StringComparison.Ordinal);
        // And the scoped runtime directory does NOT contain a packet
        // copy — the migration only owns runtime audit state.
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runtime",
            "intent-system", "J-Tech-Japan__intent-system", "issues", "G18", "packet.yaml")));
    }

    [Fact]
    public void Execute_GivenTargetRepoButOnlyLegacyRunLog_FallsBackToLegacyWithDiagnostic()
    {
        // G327: legacy-fallback. When the operator names a target repo
        // but the scoped runs.jsonl is not yet on disk (mid-migration),
        // the resolver falls back to the legacy root path and surfaces
        // the chosen layout so operators can see they read legacy.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var exitCode = RunLogCommand.Execute(
            CreateContext(repoRoot),
            ["G18", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Run log source: legacy", output, StringComparison.Ordinal);
        Assert.Contains("event=issue-created", output, StringComparison.Ordinal);
        Assert.Contains("event=activated", output, StringComparison.Ordinal);
        Assert.Contains("event=review", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenTargetRepoFullGithubUrl_NormalizesToSameScopedDirectory()
    {
        // G327: NormalizeOwnerRepo strips `https://github.com/` and
        // collapses separators. Pasted URLs and bare owner/repo must
        // resolve to the same scoped runs.jsonl.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runtime", "intent-system",
                "J-Tech-Japan__intent-system", "runs.jsonl"),
            """{"ts":"2026-04-07T09:00:00Z","execution_unit":"G18","event":"review","by":"scoped-runtime","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/65"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = RunLogCommand.Execute(
            CreateContext(repoRoot),
            ["G18", "--target-repo", "https://github.com/J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Run log source: scoped", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            Path.Combine("J-Tech-Japan__intent-system", "runs.jsonl"),
            writer.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoTargetRepo_PreservesLegacyRootBehaviorWithoutScopeDiagnostic()
    {
        // G327: opt-in migration. Callers that don't name a target
        // repo continue to read the legacy root runs.jsonl exactly as
        // before — no `Run log source:` banner is emitted.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var exitCode = RunLogCommand.Execute(CreateContext(repoRoot), ["G18"], writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Run log source:", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenTargetRepoMissingValue_ReturnsUsageError()
    {
        using var writer = new StringWriter();

        var exitCode = RunLogCommand.Execute(
            CreateContext("/tmp/intent-system"),
            ["G18", "--target-repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--target-repo requires a value", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRunLog_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = RunLogCommand.Execute(CreateContext(repoRoot), ["G18"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Run log was not found", writer.ToString(), StringComparison.Ordinal);
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
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-07T08:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G18",
                    Title = "Run log command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G17"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G18/implementation.md",
                        ReviewContext = ".intent-cli/issues/G18/review-context.md",
                        Yaml = ".intent-cli/issues/G18/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 64,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/64"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                },
                new QueueItem
                {
                    ExecutionUnit = "A1",
                    Title = "Unrelated execution unit",
                    State = QueueItemState.Completed,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/A1/implementation.md",
                        ReviewContext = ".intent-cli/issues/A1/review-context.md",
                        Yaml = ".intent-cli/issues/A1/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "normal"
                }
            ]
        };
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-07T08:00:00Z","execution_unit":"G18","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/64"}
        {"ts":"2026-04-07T08:05:00Z","execution_unit":"A1","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/12"}
        {"ts":"2026-04-07T08:10:00Z","execution_unit":"G18","event":"activated","by":"intent-cli"}
        {"ts":"2026-04-07T08:20:00Z","execution_unit":"G18","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/65"}
        {"ts":"2026-04-07T08:30:00Z","execution_unit":"G18","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/65#issuecomment-1","reason":"contract mismatch"}
        """ + Environment.NewLine;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-run-log-tests-").FullName;

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
