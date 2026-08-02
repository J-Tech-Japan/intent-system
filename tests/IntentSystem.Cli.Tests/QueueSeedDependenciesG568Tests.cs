using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G568: queue seeding preserves <c>dependencies</c> faithfully, and already
/// seeded items can be reconciled.
///
/// G567 pinned the loss it inherited: a FLOW sequence survived as bracket text
/// and a BLOCK sequence was dropped entirely. The severity is not cosmetic —
/// dependency-aware selection reads the seeded list, so a dropped dependency
/// makes a dependent unit look publish-ready while its root is still open.
/// That is precisely the failure the ordering taxonomy exists to prevent,
/// happening silently at the surface furthest upstream.
///
/// So the fixtures below assert the property that matters — how an author
/// happened to write the sequence is not a semantic difference — and then
/// follow it through to the selection gate that consumes it.
/// </summary>
public sealed class QueueSeedDependenciesG568Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly SeedWorkspace workspace = new();

    public QueueSeedDependenciesG568Tests()
    {
        AutomationQueueDependencyReconcileCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationQueueDependencyReconcileCommand.UtcNowFactory = null;
        workspace.Dispose();
    }

    /// <summary>
    /// The equivalence classes. Each pair is the SAME declaration written two
    /// ways; the seeded item must not be able to tell them apart.
    /// </summary>
    public static TheoryData<string, string, string[]> EquivalentDeclarations() => new()
    {
        { "empty", "  dependencies: []", Array.Empty<string>() },
        { "single", "  dependencies: [G565]", ["G565"] },
        { "multiple", "  dependencies: [G565, G566]", ["G565", "G566"] },
        { "block single", "  dependencies:\n    - G565", ["G565"] },
        { "block multiple", "  dependencies:\n    - G565\n    - G566", ["G565", "G566"] },
        { "block at parent indent", "  dependencies:\n  - G565\n  - G566", ["G565", "G566"] },
        { "quoted block items", "  dependencies:\n    - \"G565\"\n    - 'G566'", ["G565", "G566"] },
        { "absent key", "  target_part: no dependencies declared", Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(EquivalentDeclarations))]
    public void SeededDependencies_AreTheSameStructuredList_ForEveryDeclarationStyle_G568(
        string description,
        string dependenciesYaml,
        string[] expected)
    {
        workspace.WritePacket(SeedWorkspace.BuildPacket(dependenciesYaml));

        var (exitCode, result) = workspace.RunSeed(write: false);

        Assert.Equal(0, exitCode);
        var seeded = result.GetProperty("seeded_item").GetProperty("dependencies")
            .EnumerateArray().Select(entry => entry.GetString()).ToArray();
        Assert.Equal(expected, seeded);

        // And no bracket text anywhere in the seeded state — the representation
        // is a list, not a string that happens to look like one.
        Assert.DoesNotContain("[G565", result.GetRawText(), StringComparison.Ordinal);
        _ = description;
    }

    [Fact]
    public void BlockDeclaredDependencies_SurviveTheWritePath_G568()
    {
        // Dry-run planning and the persisted item must agree; the queue file is
        // what selection actually reads.
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies:\n    - G565\n    - G566"));

        var (exitCode, _) = workspace.RunSeed(write: true);
        Assert.Equal(0, exitCode);

        var state = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var item = Assert.Single(state.Items);
        Assert.Equal(["G565", "G566"], item.Dependencies);
    }

    [Fact]
    public void BlockDeclaredBlockedBy_IsAlsoPreserved_G568()
    {
        workspace.WritePacket(SeedWorkspace.BuildPacket("  blocked_by:\n    - G999"));

        var (exitCode, result) = workspace.RunSeed(write: false);

        Assert.Equal(0, exitCode);
        Assert.Collection(
            result.GetProperty("seeded_item").GetProperty("blocked_by")
                .EnumerateArray().Select(entry => entry.GetString()),
            dependency => Assert.Equal("G999", dependency));
    }

    // ------------------------------------------------------- selection gating

    [Fact]
    public void ABlockDeclaredDependency_ActuallyGatesSelection_G568()
    {
        // The point of the fix, end to end: seed a unit whose dependency is
        // declared in block style, and the shared selector must NOT hand it out
        // while that root is unfinished. Before G568 the dependency never
        // reached the queue, so this returned the dependent immediately.
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies:\n    - G565"));
        Assert.Equal(0, workspace.RunSeed(write: true).ExitCode);

        var seeded = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var withOpenRoot = seeded with
        {
            Items = seeded.Items.Append(SeedWorkspace.BuildQueueItem("G565", QueueItemState.Queued)).ToArray(),
        };

        // Root is queued (not completed) → the dependent is not selectable.
        var selected = QueueSelection.SelectNext(withOpenRoot);
        Assert.NotNull(selected);
        Assert.Equal("G565", selected!.ExecutionUnit);

        // Complete the root → the dependent becomes selectable.
        var withCompletedRoot = seeded with
        {
            Items = seeded.Items.Append(SeedWorkspace.BuildQueueItem("G565", QueueItemState.Completed)).ToArray(),
        };
        Assert.Equal(SeedWorkspace.Unit, QueueSelection.SelectNext(withCompletedRoot)!.ExecutionUnit);
    }

    // ------------------------------------------------------------- reconcile

    [Fact]
    public void Reconcile_DiagnosesALegacyItemWhoseDependenciesWereDropped_G568()
    {
        // The historical shape: the packet declared a block dependency, the
        // lossy seed recorded none.
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies:\n    - G565"));
        workspace.WriteQueueStateWithDependencies([]);

        var (exitCode, result) = workspace.RunReconcile(write: false);

        Assert.Equal(0, exitCode);
        var finding = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationQueueDependencyReconcileCommand.StatusDrifted, finding.GetProperty("status").GetString());
        Assert.Collection(
            finding.GetProperty("packet_dependencies").EnumerateArray().Select(e => e.GetString()),
            dependency => Assert.Equal("G565", dependency));
        Assert.Empty(finding.GetProperty("queue_dependencies").EnumerateArray());

        // Read-only really means read-only.
        Assert.False(result.GetProperty("applied").GetBoolean());
        var state = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Empty(Assert.Single(state.Items).Dependencies);
    }

    [Fact]
    public void Reconcile_Write_RederivesFromThePacket_AndIsIdempotent_G568()
    {
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies:\n    - G565\n    - G566"));
        workspace.WriteQueueStateWithDependencies([]);

        var (exitCode, result) = workspace.RunReconcile(write: true);
        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("applied").GetBoolean());
        Assert.Equal(
            ["G565", "G566"],
            QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath))
                .Items.Single().Dependencies);

        // Second run: nothing left to do, and nothing done.
        var (secondExit, second) = workspace.RunReconcile(write: true);
        Assert.Equal(0, secondExit);
        Assert.False(second.GetProperty("applied").GetBoolean());
        Assert.Equal(0, second.GetProperty("drifted").GetInt32());
        Assert.Equal(
            AutomationQueueDependencyReconcileCommand.StatusInSync,
            Assert.Single(second.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public void Reconcile_RederivesRatherThanMerges_G568()
    {
        // A queue entry that has dependencies the packet no longer declares is
        // corrected DOWN. Merging would make the queue a second source of truth
        // that no packet can ever contradict.
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies:\n    - G565"));
        workspace.WriteQueueStateWithDependencies(["G001", "G002"]);

        Assert.Equal(0, workspace.RunReconcile(write: true).ExitCode);

        Assert.Equal(
            ["G565"],
            QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath))
                .Items.Single().Dependencies);
    }

    [Fact]
    public void Reconcile_TouchesOnlyTheDependenciesField_G568()
    {
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies:\n    - G565"));
        workspace.WriteQueueStateWithDependencies([]);
        var before = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath)).Items.Single();

        Assert.Equal(0, workspace.RunReconcile(write: true).ExitCode);

        var after = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath)).Items.Single();
        Assert.Equal(["G565"], after.Dependencies);

        // Everything else is unchanged: the repair is not a re-seed. Compared
        // field by field rather than with record equality, because QueueItem's
        // list members compare by reference and a JSON round-trip always yields
        // fresh instances — record equality would pass or fail for reasons that
        // have nothing to do with what this test is asserting.
        Assert.Equal(before.ExecutionUnit, after.ExecutionUnit);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.BlockedBy, after.BlockedBy);
        Assert.Equal(before.ClarificationReturnPath, after.ClarificationReturnPath);
        Assert.Equal(before.PacketPaths, after.PacketPaths);
        Assert.Equal(before.LinkedIssue, after.LinkedIssue);
        Assert.Equal(before.LinkedPr, after.LinkedPr);
        Assert.Equal(before.WorkerRole, after.WorkerRole);
        Assert.Equal(before.ReviewRole, after.ReviewRole);
        Assert.Equal(before.Priority, after.Priority);
        Assert.Equal(before.RetirementReason, after.RetirementReason);
        Assert.Equal(before.PriorityRevision, after.PriorityRevision);
    }

    [Fact]
    public void Reconcile_PreservesUnrelatedItems_G548_G568()
    {
        // G548's no-item-loss invariant: a targeted repair must not drop the
        // rest of the queue.
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies:\n    - G565"));
        workspace.WriteQueueStateWithDependencies([], extraUnits: ["G900", "G901"]);

        Assert.Equal(0, workspace.RunReconcile(write: true).ExitCode);

        var state = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(3, state.Items.Count);
        Assert.Contains(state.Items, item => item.ExecutionUnit == "G900");
        Assert.Contains(state.Items, item => item.ExecutionUnit == "G901");
    }

    [Fact]
    public void Reconcile_FailsClosedOnAnUnknownUnit_G568()
    {
        workspace.WriteQueueStateWithDependencies([]);

        using var writer = new StringWriter();
        var exitCode = AutomationQueueDependencyReconcileCommand.Execute(
            workspace.Context, ["--execution-unit", "G999", "--write"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("is not in queue-state", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reconcile_FailsClosedOnAnUnparseablePacket_AndWritesNothing_G568()
    {
        // "I cannot read the declaration" must never become "the declaration is
        // empty" — that would repair a dropped dependency into a confirmed
        // absence.
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies: [G565, G566"));
        workspace.WriteQueueStateWithDependencies([]);
        var before = File.ReadAllBytes(workspace.QueueStatePath);

        var (exitCode, result) = workspace.RunReconcile(write: true, unit: SeedWorkspace.Unit);

        Assert.Equal(1, exitCode);
        Assert.False(result.GetProperty("applied").GetBoolean());
        Assert.Equal(
            AutomationQueueDependencyReconcileCommand.StatusPacketUnparseable,
            Assert.Single(result.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
        Assert.Equal(before, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void Reconcile_SkipsAnItemWithNoPacket_WithoutFailingASweep_G568()
    {
        // A sweep across the whole queue meets items whose packet is not in
        // this checkout. That is a skip with a reason, not a failure — but it
        // is never silent.
        workspace.WriteQueueStateWithDependencies([], extraUnits: ["G900"]);

        var (exitCode, result) = workspace.RunReconcile(write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, result.GetProperty("skipped").GetInt32());
        Assert.All(
            result.GetProperty("items").EnumerateArray(),
            finding => Assert.Equal(
                AutomationQueueDependencyReconcileCommand.StatusPacketMissing,
                finding.GetProperty("status").GetString()));
    }

    [Fact]
    public void Reconcile_RecordsAnAuditEventForEachRepair_G568()
    {
        workspace.WritePacket(SeedWorkspace.BuildPacket("  dependencies:\n    - G565"));
        workspace.WriteQueueStateWithDependencies([]);

        Assert.Equal(0, workspace.RunReconcile(write: true).ExitCode);

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsPath));
        var reconciled = Assert.Single(events, e =>
            e.Event == AutomationQueueDependencyReconcileCommand.ReconcileEventName);
        Assert.Equal(SeedWorkspace.Unit, reconciled.ExecutionUnit);
        Assert.Contains("G565", reconciled.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_DispatchesTheReconcileCommand_G568()
    {
        var router = typeof(CommandRouter);
        var commandsField = router.GetField("ImplementedCommands",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var outer = (System.Collections.IDictionary)commandsField!.GetValue(null)!;
        var automation = (System.Collections.IDictionary)outer["automation"]!;
        Assert.True(automation.Contains("queue-dependency-reconcile"));

        var helpField = router.GetField("AutomationCommandHelp",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var lines = (IReadOnlyList<string>?)helpField!.GetValue(null);
        Assert.Contains(lines!, line => line.Contains("queue-dependency-reconcile", StringComparison.Ordinal));
    }

    private sealed class SeedWorkspace : IDisposable
    {
        public const string Unit = "G568";
        public const string Domain = "intent-cli";
        public const string TargetRepo = "J-Tech-Japan/intent-system";

        public SeedWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("queue-seed-dependencies-g568-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };

            var bindings = Path.Combine(RootPath, "intents", Domain, "automation");
            Directory.CreateDirectory(bindings);
            File.WriteAllText(Path.Combine(bindings, "bindings.md"), "---\nexecution_unit_regex: '^G[0-9]+$'\n---\n");
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public string QueueStatePath => Context.GetQueueStatePath();

        public string RunsPath => Context.GetRunLogPath();

        public void WritePacket(string yaml)
        {
            var directory = Path.Combine(RootPath, ".intent-cli", "issues", Unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"), yaml);
            File.WriteAllText(Path.Combine(directory, "implementation.md"), "# impl\n");
            File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"),
                "# Title\n## Goal\nx\n## Why This Slice Exists Now\nx\n## Current Observed State\nx\n"
                + "## Accepted Baseline You May Assume\nx\n## Target Repo / Path / Part\nx\n## In Scope\nx\n"
                + "## Out Of Scope\nx\n## Acceptance Criteria\nx\n## Verification\nx\n## Related Links\nx\n");
        }

        public void WriteQueueStateWithDependencies(string[] dependencies, string[]? extraUnits = null)
        {
            var items = new List<QueueItem> { BuildQueueItem(Unit, QueueItemState.Queued, dependencies) };
            foreach (var extra in extraUnits ?? Array.Empty<string>())
            {
                items.Add(BuildQueueItem(extra, QueueItemState.Queued));
            }

            File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                Items = items,
            }));
        }

        public static QueueItem BuildQueueItem(
            string executionUnit,
            QueueItemState state,
            string[]? dependencies = null) => new()
            {
                ExecutionUnit = executionUnit,
                Title = $"[{executionUnit}] fixture",
                State = state,
                Dependencies = dependencies ?? Array.Empty<string>(),
                BlockedBy = Array.Empty<string>(),
                ClarificationReturnPath = $"intents/{Domain}/clarifications/open.md",
                PacketPaths = new PacketPaths
                {
                    Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                    ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                    Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                },
                WorkerRole = "coder",
                ReviewRole = "reviewer",
                Priority = "high",
            };

        public (int ExitCode, JsonElement Result) RunSeed(bool write)
        {
            using var writer = new StringWriter();
            var args = new List<string>
            {
                "--execution-unit", Unit, "--domain", Domain, "--target-repo", TargetRepo, "--format", "json",
            };
            if (write)
            {
                args.Add("--write");
            }

            var exitCode = AutomationQueueSeedFromPacketCommand.Execute(Context, args.ToArray(), writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public (int ExitCode, JsonElement Result) RunReconcile(bool write, string? unit = null)
        {
            using var writer = new StringWriter();
            var args = new List<string> { "--format", "json" };
            if (unit is not null)
            {
                args.AddRange(["--execution-unit", unit]);
            }
            if (write)
            {
                args.Add("--write");
            }

            var exitCode = AutomationQueueDependencyReconcileCommand.Execute(Context, args.ToArray(), writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public static string BuildPacket(string listYaml) => $"""
            domain: {Domain}
            implementation_issue_packet:
              source_execution_unit: {Unit}
              issue_title: "Dependency fixture"
              target_repo: {TargetRepo}
            {listYaml}
            """;

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
