using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationQueueSeedFromPacketCommandTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void Execute_ValidatedPacket_DryRun_ReturnsQueueSeedReady_AndDoesNotWriteFiles()
    {
        // G363 AC1: validated complete packet directory reports a
        // deterministic queue seed repair (no --write → no
        // filesystem mutation).
        workspace.WritePreparedPacket("Z4R-G10", targetRepo: "J-Tech-Creations/Zero4Racer");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationReady,
            doc.RootElement.GetProperty("classification").GetString());
        Assert.False(doc.RootElement.GetProperty("write").GetBoolean());
        // queue-state.json must NOT be created on dry-run.
        Assert.False(File.Exists(workspace.QueueStatePath));
        // runs.jsonl must NOT be appended.
        Assert.False(File.Exists(workspace.RunsPath));
    }

    [Fact]
    public void Execute_ValidatedPacket_Write_InsertsQueuedItem_AndAppendsRunsEvent()
    {
        // G363 AC2: --write inserts a queued item with packet-derived
        // metadata and appends a runs.jsonl event. Verifies both
        // durable artifacts end up on disk.
        workspace.WritePreparedPacket("Z4R-G10", targetRepo: "J-Tech-Creations/Zero4Racer");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationApplied,
            doc.RootElement.GetProperty("classification").GetString());

        // queue-state.json now contains the seeded item.
        Assert.True(File.Exists(workspace.QueueStatePath));
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var seeded = queueState.Items.Single(i => i.ExecutionUnit == "Z4R-G10");
        Assert.Equal(QueueItemState.Queued, seeded.State);
        Assert.Equal(".intent-cli/issues/Z4R-G10/packet.yaml", seeded.PacketPaths.Yaml);
        Assert.Equal(".intent-cli/issues/Z4R-G10/implementation.md", seeded.PacketPaths.Implementation);
        Assert.Equal(".intent-cli/issues/Z4R-G10/review-context.md", seeded.PacketPaths.ReviewContext);
        Assert.Equal("Demo", seeded.Title); // From packet.yaml issue_title.
        Assert.Equal("coder", seeded.WorkerRole);
        Assert.Equal("reviewer", seeded.ReviewRole);
        Assert.Equal("normal", seeded.Priority);

        // runs.jsonl appended with the seed event.
        Assert.True(File.Exists(workspace.RunsPath));
        var runsLine = File.ReadAllLines(workspace.RunsPath).Single();
        var runEvent = RunLogSerializer.DeserializeLine(runsLine);
        Assert.Equal("Z4R-G10", runEvent.ExecutionUnit);
        Assert.Equal(AutomationQueueSeedFromPacketCommand.SeedEventName, runEvent.Event);
        Assert.Equal(".intent-cli/issues/Z4R-G10/", runEvent.PacketRef);
    }

    [Fact]
    public void Execute_AlreadySeededExecutionUnit_ReturnsAlreadySeeded_AndExitZero_AndNoDoubleAppend()
    {
        // Re-running on a unit that's already in the queue returns
        // already-seeded (no-op signal) and does NOT double-append
        // to runs.jsonl or duplicate the queue item.
        workspace.WritePreparedPacket("Z4R-G10", targetRepo: "J-Tech-Creations/Zero4Racer");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");
        // First pass — seed.
        using (var w1 = new StringWriter())
        {
            var exit1 = AutomationQueueSeedFromPacketCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--execution-unit", "Z4R-G10",
                    "--domain", "intent-cli",
                    "--target-repo", "J-Tech-Creations/Zero4Racer",
                    "--write",
                    "--format", "json",
                },
                w1);
            Assert.Equal(0, exit1);
        }
        var runsLinesAfterFirst = File.ReadAllLines(workspace.RunsPath).Length;

        // Second pass — already-seeded.
        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationAlreadySeeded,
            doc.RootElement.GetProperty("classification").GetString());
        // Same number of items, same number of runs.jsonl lines.
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(1, queueState.Items.Count(i => i.ExecutionUnit == "Z4R-G10"));
        Assert.Equal(runsLinesAfterFirst, File.ReadAllLines(workspace.RunsPath).Length);
    }

    [Fact]
    public void Execute_WrongDomainPacket_RefusesSeed()
    {
        // G363 AC4: wrong-domain packet (SKS-G42 under
        // ^Z4R-G[0-9]+$) is refused with structured unsafe stop.
        workspace.WritePreparedPacket("SKS-G42", targetRepo: "J-Tech-Creations/Zero4Racer");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "SKS-G42",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationUnsafe,
            doc.RootElement.GetProperty("classification").GetString());
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.ReasonWrongDomain,
            doc.RootElement.GetProperty("unsafe_reason").GetString());
        // No queue-state, no runs.jsonl on refusal.
        Assert.False(File.Exists(workspace.QueueStatePath));
        Assert.False(File.Exists(workspace.RunsPath));
    }

    [Fact]
    public void Execute_WrongTargetRepoPacket_RefusesSeed()
    {
        // G363 AC4: packet declares a different child repo than
        // the requested target — refused.
        workspace.WritePreparedPacket("Z4R-G10", targetRepo: "J-Tech-Creations/Zero4Racer");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "Other/Repo",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationUnsafe,
            doc.RootElement.GetProperty("classification").GetString());
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.ReasonWrongTargetRepo,
            doc.RootElement.GetProperty("unsafe_reason").GetString());
    }

    [Fact]
    public void Execute_MissingCanonicalFile_RefusesSeed()
    {
        // G363 AC4: packet missing github-body.md → refused
        // (missing-canonical-file).
        workspace.WritePreparedPacket("Z4R-G10", targetRepo: "J-Tech-Creations/Zero4Racer");
        File.Delete(Path.Combine(workspace.RootPath, ".intent-cli", "issues", "Z4R-G10", "github-body.md"));
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationUnsafe,
            doc.RootElement.GetProperty("classification").GetString());
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.ReasonMissingCanonicalFile,
            doc.RootElement.GetProperty("unsafe_reason").GetString());
    }

    [Fact]
    public void Execute_MalformedPacketYaml_RefusesSeed()
    {
        // G363 AC4: malformed packet.yaml (tab indentation) →
        // refused (packet-yaml-unparseable).
        workspace.WritePreparedPacket("Z4R-G10", targetRepo: "J-Tech-Creations/Zero4Racer");
        // Overwrite packet.yaml with a tab-indented malformed body.
        File.WriteAllText(
            Path.Combine(workspace.RootPath, ".intent-cli", "issues", "Z4R-G10", "packet.yaml"),
            "implementation_issue_packet:\n\tsource_execution_unit: Z4R-G10\n\ttarget_repo: J-Tech-Creations/Zero4Racer\n");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationUnsafe,
            doc.RootElement.GetProperty("classification").GetString());
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable,
            doc.RootElement.GetProperty("unsafe_reason").GetString());
    }

    [Fact]
    public void Execute_PacketYamlDeclaresDependenciesAndBlockedBy_PreservesThemInSeed()
    {
        // PR #830 review repair: when packet.yaml carries
        // `dependencies` and `blocked_by` arrays (and explicit
        // role/priority overrides), the seeded QueueItem MUST
        // preserve them rather than silently hardcoding empties /
        // defaults. The G361 scalar parser stores list values as
        // raw bracketed text; the new `ParsePacketArrayField`
        // expands them into the QueueItem arrays.
        var dir = Path.Combine(workspace.RootPath, ".intent-cli", "issues", "Z4R-G10");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "packet.yaml"),
            "implementation_issue_packet:\n"
            + "  source_execution_unit: Z4R-G10\n"
            + "  issue_title: Demo\n"
            + "  target_repo: J-Tech-Creations/Zero4Racer\n"
            + "  dependencies: [Z4R-G8, Z4R-G9]\n"
            + "  blocked_by: [Z4R-G7]\n"
            + "  worker_role: implementor\n"
            + "  review_role: reviewer-bot\n"
            + "  priority: high\n");
        File.WriteAllText(Path.Combine(dir, "implementation.md"), "# impl\n");
        File.WriteAllText(Path.Combine(dir, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(dir, "github-body.md"),
            "# Title\n## Goal\nx\n## Why This Slice Exists Now\nx\n## Current Observed State\nx\n## Accepted Baseline You May Assume\nx\n## Target Repo / Path / Part\nx\n## In Scope\nx\n## Out Of Scope\nx\n## Acceptance Criteria\nx\n## Verification\nx\n## Related Links\nx\n");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var seeded = queueState.Items.Single(i => i.ExecutionUnit == "Z4R-G10");
        Assert.Equal(new[] { "Z4R-G8", "Z4R-G9" }, seeded.Dependencies);
        Assert.Equal(new[] { "Z4R-G7" }, seeded.BlockedBy);
        Assert.Equal("implementor", seeded.WorkerRole);
        Assert.Equal("reviewer-bot", seeded.ReviewRole);
        Assert.Equal("high", seeded.Priority);
    }

    [Fact]
    public void Execute_PacketYamlOmitsDependencies_LeavesEmptyArrays_NoGuessing()
    {
        // Backward compat: packets that legitimately carry no
        // dependency metadata still produce queue items with empty
        // arrays — the loader MUST NEVER guess.
        workspace.WritePreparedPacket("Z4R-G10", targetRepo: "J-Tech-Creations/Zero4Racer");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var seeded = queueState.Items.Single(i => i.ExecutionUnit == "Z4R-G10");
        Assert.Empty(seeded.Dependencies);
        Assert.Empty(seeded.BlockedBy);
    }

    [Fact]
    public void Execute_PacketOmitsClarificationReturnPath_FallsBackToDomainDefault()
    {
        // PR #830 review repair #2: when packet.yaml does not declare
        // `clarification_return_path`, the seeded queue item MUST
        // fall back to the canonical per-domain default
        // (`intents/<domain>/clarifications/open.md`). An empty path
        // would silently break the packet ↔ queue-item clarification
        // path contract enforced by ClarifyOpenCommand and
        // MetadataValidateAnalyzer.
        workspace.WritePreparedPacket("Z4R-G10", targetRepo: "J-Tech-Creations/Zero4Racer");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G10",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var seeded = queueState.Items.Single(i => i.ExecutionUnit == "Z4R-G10");
        Assert.Equal("intents/intent-cli/clarifications/open.md", seeded.ClarificationReturnPath);
    }

    [Fact]
    public void Execute_PacketDeclaresClarificationReturnPath_PreservesIt()
    {
        // PR #830 review repair #2 (companion): when packet.yaml
        // DOES declare `clarification_return_path`, the seed MUST
        // honor that value (no silent override by the per-domain
        // default). This locks the LookupScalar precedence.
        var dir = Path.Combine(workspace.RootPath, ".intent-cli", "issues", "Z4R-G11");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "packet.yaml"),
            "implementation_issue_packet:\n"
            + "  source_execution_unit: Z4R-G11\n"
            + "  issue_title: Demo with explicit clarification\n"
            + "  target_repo: J-Tech-Creations/Zero4Racer\n"
            + "  clarification_return_path: intents/custom-domain/clarifications/open.md\n");
        File.WriteAllText(Path.Combine(dir, "implementation.md"), "# impl\n");
        File.WriteAllText(Path.Combine(dir, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(dir, "github-body.md"),
            "# Title\n## Goal\nx\n## Why This Slice Exists Now\nx\n## Current Observed State\nx\n## Accepted Baseline You May Assume\nx\n## Target Repo / Path / Part\nx\n## In Scope\nx\n## Out Of Scope\nx\n## Acceptance Criteria\nx\n## Verification\nx\n## Related Links\nx\n");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G11",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var seeded = queueState.Items.Single(i => i.ExecutionUnit == "Z4R-G11");
        Assert.Equal("intents/custom-domain/clarifications/open.md", seeded.ClarificationReturnPath);
    }

    [Fact]
    public void Execute_FullyHydratedPacket_PreservesAllMetadataInOneSeed()
    {
        // PR #830 review repair #3 (08:17 comment, integrated case):
        // when packet.yaml carries the FULL payload — non-empty
        // clarification path, dependencies, blocked_by, role
        // overrides, priority, and title — the seeded queue item MUST
        // preserve ALL fields in a single round-trip. This locks the
        // complete preservation chain in one assertion so future
        // refactors of BuildSeedItem cannot silently regress any
        // single field without flipping this test.
        var dir = Path.Combine(workspace.RootPath, ".intent-cli", "issues", "Z4R-G42");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "packet.yaml"),
            "implementation_issue_packet:\n"
            + "  source_execution_unit: Z4R-G42\n"
            + "  issue_title: Fully hydrated demo\n"
            + "  target_repo: J-Tech-Creations/Zero4Racer\n"
            + "  clarification_return_path: intents/zero4racer/clarifications/open.md\n"
            + "  dependencies: [Z4R-G40, Z4R-G41]\n"
            + "  blocked_by: [Z4R-G39]\n"
            + "  worker_role: implementor\n"
            + "  review_role: reviewer-bot\n"
            + "  priority: high\n");
        File.WriteAllText(Path.Combine(dir, "implementation.md"), "# impl\n");
        File.WriteAllText(Path.Combine(dir, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(dir, "github-body.md"),
            "# Title\n## Goal\nx\n## Why This Slice Exists Now\nx\n## Current Observed State\nx\n## Accepted Baseline You May Assume\nx\n## Target Repo / Path / Part\nx\n## In Scope\nx\n## Out Of Scope\nx\n## Acceptance Criteria\nx\n## Verification\nx\n## Related Links\nx\n");
        workspace.WriteBindings("intent-cli", "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G42",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var seeded = queueState.Items.Single(i => i.ExecutionUnit == "Z4R-G42");

        // Title + state.
        Assert.Equal("Fully hydrated demo", seeded.Title);
        Assert.Equal(QueueItemState.Queued, seeded.State);
        // Clarification path honored verbatim from the packet (NOT
        // overridden by the per-domain default).
        Assert.Equal("intents/zero4racer/clarifications/open.md", seeded.ClarificationReturnPath);
        // Dependencies + blocked_by preserved as authored.
        Assert.Equal(new[] { "Z4R-G40", "Z4R-G41" }, seeded.Dependencies);
        Assert.Equal(new[] { "Z4R-G39" }, seeded.BlockedBy);
        // Role / priority overrides preserved.
        Assert.Equal("implementor", seeded.WorkerRole);
        Assert.Equal("reviewer-bot", seeded.ReviewRole);
        Assert.Equal("high", seeded.Priority);
        // Packet paths derived from execution unit.
        Assert.Equal(".intent-cli/issues/Z4R-G42/packet.yaml", seeded.PacketPaths.Yaml);
        Assert.Equal(".intent-cli/issues/Z4R-G42/implementation.md", seeded.PacketPaths.Implementation);
        Assert.Equal(".intent-cli/issues/Z4R-G42/review-context.md", seeded.PacketPaths.ReviewContext);
    }

    [Fact]
    public void Execute_PacketDirectoryMissing_ReturnsStructuredStop()
    {
        // Defensive: command run on an EU whose packet directory
        // doesn't exist returns packet-directory-missing rather than
        // crashing.
        using var writer = new StringWriter();
        var exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "Z4R-G999",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Creations/Zero4Racer",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationQueueSeedFromPacketCommand.ClassificationPacketDirectoryMissing,
            doc.RootElement.GetProperty("classification").GetString());
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("g363-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }
        public string QueueStatePath => Path.Combine(RootPath, ".intent-cli", "queue-state.json");
        public string RunsPath => Path.Combine(RootPath, ".intent-cli", "runs.jsonl");

        public void WritePreparedPacket(string executionUnit, string targetRepo)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "packet.yaml"),
                $"implementation_issue_packet:\n  source_execution_unit: {executionUnit}\n  issue_title: Demo\n  target_repo: {targetRepo}\n");
            File.WriteAllText(Path.Combine(dir, "implementation.md"), "# impl\n");
            File.WriteAllText(Path.Combine(dir, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(dir, "github-body.md"),
                "# Title\n## Goal\nx\n## Why This Slice Exists Now\nx\n## Current Observed State\nx\n## Accepted Baseline You May Assume\nx\n## Target Repo / Path / Part\nx\n## In Scope\nx\n## Out Of Scope\nx\n## Acceptance Criteria\nx\n## Verification\nx\n## Related Links\nx\n");
        }

        public void WriteBindings(string domain, string executionUnitRegex)
        {
            var dir = Path.Combine(RootPath, "intents", domain, "automation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "bindings.md"),
                $"---\nexecution_unit_regex: '{executionUnitRegex}'\n---\n");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }
}
