using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G331: end-to-end tests for <c>intent-cli migrate host-state</c>.
/// Covers dry-run reporting, write idempotency, the legacy-archive
/// step, and the AC-required dual coverage of a design-host
/// migration and a review-runtime-host migration.
/// </summary>
public sealed class MigrateHostStateCommandTests : IDisposable
{
    public MigrateHostStateCommandTests()
    {
        MigrateHostStateCommand.UtcNowFactory =
            () => new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        MigrateHostStateCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_DryRun_ReportsPlanWithoutTouchingFilesystem()
    {
        using var workspace = new MigrateWorkspace();
        workspace.WriteLegacyQueueState(BuildQueueState(
            ("G331", "review", linkedIssue: ("J-Tech-Japan/intent-system", 765)),
            ("OTHER", "queued", linkedIssue: ("J-Tech-Japan/Sekiban", 1))));
        workspace.WriteLegacyRuns(new[]
        {
            BuildRunLine("G331", "pr-merged", "J-Tech-Japan/intent-system")
        });
        var legacyQueueBefore = File.ReadAllText(workspace.LegacyQueuePath);
        var legacyRunsBefore = File.ReadAllText(workspace.LegacyRunsPath);

        using var writer = new StringWriter();
        var exit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "review-runtime",
                "--dry-run",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.False(root.GetProperty("applied").GetBoolean());
        var plan = root.GetProperty("plan");
        Assert.Single(plan.GetProperty("matching_items").EnumerateArray());
        Assert.Single(plan.GetProperty("items_to_add").EnumerateArray());

        // Dry-run must not touch any file.
        Assert.Equal(legacyQueueBefore, File.ReadAllText(workspace.LegacyQueuePath));
        Assert.Equal(legacyRunsBefore, File.ReadAllText(workspace.LegacyRunsPath));
        Assert.False(File.Exists(workspace.ScopedQueuePath("intent-cli", "J-Tech-Japan__intent-system")));
    }

    [Fact]
    public void Execute_Write_CreatesScopedStateAndArchivesLegacy_ReviewRuntimeHost()
    {
        // G331 AC: review-runtime host migration. The IntentSystemReview
        // host owns J-Tech-Japan/intent-system review runtime state.
        using var workspace = new MigrateWorkspace();
        workspace.WriteLegacyQueueState(BuildQueueState(
            ("G331", "review", linkedIssue: ("J-Tech-Japan/intent-system", 765)),
            ("OTHER", "queued", linkedIssue: ("J-Tech-Japan/Sekiban", 1))));
        workspace.WriteLegacyRuns(new[]
        {
            BuildRunLine("G331", "pr-merged", "J-Tech-Japan/intent-system"),
            BuildRunLine("OTHER", "pr-merged", "J-Tech-Japan/Sekiban")
        });

        using var writer = new StringWriter();
        var exit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "review-runtime",
                "--write",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exit);

        // Scoped queue-state has the matching item only.
        var scopedQueuePath = workspace.ScopedQueuePath("intent-cli", "J-Tech-Japan__intent-system");
        Assert.True(File.Exists(scopedQueuePath));
        var scoped = QueueStateSerializer.Deserialize(File.ReadAllText(scopedQueuePath));
        Assert.Single(scoped.Items);
        Assert.Equal("G331", scoped.Items[0].ExecutionUnit);

        // Scoped runs.jsonl has the matching run only.
        var scopedRunsPath = workspace.ScopedRunsPath("intent-cli", "J-Tech-Japan__intent-system");
        var scopedRuns = File.ReadAllLines(scopedRunsPath);
        Assert.Single(scopedRuns);
        Assert.Contains("\"execution_unit\":\"G331\"", scopedRuns[0], StringComparison.Ordinal);

        // Legacy files were archived under the scoped runtime tree.
        var archiveDir = Path.Combine(workspace.Context.RepoRoot, ".intent-cli", "runtime",
            "intent-cli", "J-Tech-Japan__intent-system", "legacy-archive");
        Assert.True(Directory.Exists(archiveDir));
        Assert.True(Directory.EnumerateFiles(archiveDir, "queue-state-*.json").Any());
        Assert.True(Directory.EnumerateFiles(archiveDir, "runs-*.jsonl").Any());
    }

    [Fact]
    public void Execute_Write_DesignHost_MovesIntentCliDomain_LeavesSekibanInLegacy()
    {
        // G331 AC: design host migration. MyIntentHost is the design
        // workspace for both intent-cli AND sekiban-as-a-service
        // domains. Migrating the intent-cli scope must NOT touch
        // Sekiban items in the legacy file.
        using var workspace = new MigrateWorkspace();
        workspace.WriteLegacyQueueState(BuildQueueState(
            ("G331", "queued", linkedIssue: ("J-Tech-Japan/intent-system", 765)),
            ("SEKI-1", "queued", linkedIssue: ("J-Tech-Japan/SekibanAsAService", 99))));
        var legacyBefore = File.ReadAllText(workspace.LegacyQueuePath);

        using var writer = new StringWriter();
        var exit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "design",
                "--write",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exit);

        // The migrate command does NOT delete legacy items in this slice
        // (out of scope: long-lived dual-write or destructive cleanup).
        // The legacy file is preserved for audit; the scoped tree gets
        // a copy plus the archive.
        Assert.Equal(legacyBefore, File.ReadAllText(workspace.LegacyQueuePath));

        // Scoped queue-state for intent-cli has only the G331 item.
        var scoped = QueueStateSerializer.Deserialize(File.ReadAllText(
            workspace.ScopedQueuePath("intent-cli", "J-Tech-Japan__intent-system")));
        Assert.Single(scoped.Items);
        Assert.Equal("G331", scoped.Items[0].ExecutionUnit);

        // No scoped tree was created for the Sekiban scope (that
        // migration is a separate invocation).
        Assert.False(Directory.Exists(Path.Combine(workspace.Context.RepoRoot,
            ".intent-cli", "runtime", "intent-cli", "J-Tech-Japan__SekibanAsAService")));
    }

    [Fact]
    public void Execute_Write_RunningTwice_IsIdempotent()
    {
        // G331 AC: running write twice is safe. The second invocation
        // detects all matching items already in scoped state and does
        // not duplicate them.
        using var workspace = new MigrateWorkspace();
        workspace.WriteLegacyQueueState(BuildQueueState(
            ("G331", "review", linkedIssue: ("J-Tech-Japan/intent-system", 765))));
        workspace.WriteLegacyRuns(new[]
        {
            BuildRunLine("G331", "pr-merged", "J-Tech-Japan/intent-system")
        });

        var firstWriter = new StringWriter();
        var firstExit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "review-runtime",
                "--write",
                "--format", "json"
            },
            firstWriter);
        Assert.Equal(0, firstExit);

        var scopedQueuePath = workspace.ScopedQueuePath("intent-cli", "J-Tech-Japan__intent-system");
        var scopedRunsPath = workspace.ScopedRunsPath("intent-cli", "J-Tech-Japan__intent-system");
        var firstScopedQueue = File.ReadAllText(scopedQueuePath);
        var firstScopedRuns = File.ReadAllText(scopedRunsPath);
        var firstScopedRunsLineCount = File.ReadAllLines(scopedRunsPath).Length;

        // Second invocation — already migrated.
        var secondWriter = new StringWriter();
        var secondExit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "review-runtime",
                "--write",
                "--format", "json"
            },
            secondWriter);
        Assert.Equal(0, secondExit);

        var secondDoc = JsonDocument.Parse(secondWriter.ToString());
        var plan = secondDoc.RootElement.GetProperty("plan");
        Assert.True(plan.GetProperty("already_migrated").GetBoolean());
        Assert.Empty(plan.GetProperty("items_to_add").EnumerateArray());
        Assert.Empty(plan.GetProperty("runs_to_add").EnumerateArray());
        // applied=false on the second run because there was nothing to add.
        Assert.False(secondDoc.RootElement.GetProperty("applied").GetBoolean());

        // Scoped files are NOT duplicated.
        var secondScopedRunsLineCount = File.ReadAllLines(scopedRunsPath).Length;
        Assert.Equal(firstScopedRunsLineCount, secondScopedRunsLineCount);
        // Queue-state items are not duplicated either (the writer
        // didn't fire because items_to_add was empty).
        var second = QueueStateSerializer.Deserialize(File.ReadAllText(scopedQueuePath));
        Assert.Single(second.Items);
    }

    [Fact]
    public void Execute_Write_PreservesExistingPacketDirectoriesUnderIntentCliIssues()
    {
        // G331 invariant: the migration MUST NOT touch packets under
        // `.intent-cli/issues/<execution-unit>/` (G300 / packet
        // authoring out of scope).
        using var workspace = new MigrateWorkspace();
        workspace.WriteLegacyQueueState(BuildQueueState(
            ("G331", "review", linkedIssue: ("J-Tech-Japan/intent-system", 765))));
        var packetPath = Path.Combine(workspace.Context.RepoRoot,
            ".intent-cli", "issues", "G331", "packet.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(packetPath)!);
        File.WriteAllText(packetPath, "execution_unit: G331\n");
        var packetBefore = File.ReadAllText(packetPath);

        using var writer = new StringWriter();
        var exit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "review-runtime",
                "--write",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exit);
        Assert.Equal(packetBefore, File.ReadAllText(packetPath));
        // Scoped runtime tree must NOT contain a packet copy.
        Assert.False(Directory.Exists(Path.Combine(workspace.Context.RepoRoot,
            ".intent-cli", "runtime", "intent-cli", "J-Tech-Japan__intent-system", "issues")));
    }

    [Fact]
    public void Execute_AmbiguousItem_ExitsTwo_AndReportsStructuredGap()
    {
        // G331 AC: ambiguous items produce structured unsafe metadata,
        // not guesses. Conflicting linked_issue + linked_pr →
        // ambiguity gap, exit code 2 so automation notices.
        using var workspace = new MigrateWorkspace();
        workspace.WriteLegacyQueueState(
            $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G331",
                  "title": "ambiguous",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/Sekiban", "number": 1},
                  "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/766",
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "review-runtime",
                "--write",
                "--format", "json"
            },
            writer);

        Assert.Equal(2, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var plan = doc.RootElement.GetProperty("plan");
        Assert.Empty(plan.GetProperty("matching_items").EnumerateArray());
        Assert.Single(plan.GetProperty("ambiguities").EnumerateArray());
        // The ambiguous item must NOT have been written to scoped state.
        Assert.False(File.Exists(workspace.ScopedQueuePath("intent-cli", "J-Tech-Japan__intent-system")));
    }

    [Fact]
    public void Execute_Write_MixedAmbiguityAndMatch_RefusesAllMutation()
    {
        // G331 review fix: ambiguous matches MUST refuse the whole
        // migration, even when other items in the same legacy file
        // are deterministically matched. Partial application would
        // leave half-migrated scoped state alongside the ambiguity
        // gap and force the operator to clean up both halves.
        using var workspace = new MigrateWorkspace();
        workspace.WriteLegacyQueueState(
            $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G331",
                  "title": "deterministic match",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": 765, "url": "https://github.com/J-Tech-Japan/intent-system/issues/765"},
                  "linked_pr": null,
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G331-AMBIG",
                  "title": "conflicting linkage",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/Sekiban", "number": 1, "url": "https://github.com/J-Tech-Japan/Sekiban/issues/1"},
                  "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/770",
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        workspace.WriteLegacyRuns(new[]
        {
            BuildRunLine("G331", "pr-merged", "J-Tech-Japan/intent-system")
        });
        var legacyQueueBefore = File.ReadAllText(workspace.LegacyQueuePath);
        var legacyRunsBefore = File.ReadAllText(workspace.LegacyRunsPath);

        using var writer = new StringWriter();
        var exit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "review-runtime",
                "--write",
                "--format", "json"
            },
            writer);

        // Exit 2 (ambiguity present).
        Assert.Equal(2, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("applied").GetBoolean());

        var plan = root.GetProperty("plan");
        // The deterministic match is still REPORTED in matching_items
        // (so the operator sees what would have moved) but NOT applied.
        var matched = plan.GetProperty("matching_items").EnumerateArray()
            .Select(e => e.GetProperty("execution_unit").GetString())
            .ToArray();
        Assert.Contains("G331", matched);
        Assert.Single(plan.GetProperty("ambiguities").EnumerateArray());

        // No scoped state was created, no archive was made, and the
        // legacy files are byte-identical.
        Assert.False(File.Exists(workspace.ScopedQueuePath(
            "intent-cli", "J-Tech-Japan__intent-system")));
        Assert.False(File.Exists(workspace.ScopedRunsPath(
            "intent-cli", "J-Tech-Japan__intent-system")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Context.RepoRoot,
            ".intent-cli", "runtime", "intent-cli", "J-Tech-Japan__intent-system",
            "legacy-archive")));
        Assert.Equal(legacyQueueBefore, File.ReadAllText(workspace.LegacyQueuePath));
        Assert.Equal(legacyRunsBefore, File.ReadAllText(workspace.LegacyRunsPath));
    }

    [Fact]
    public void Execute_MissingDomainArgument_ReturnsUsageError()
    {
        using var workspace = new MigrateWorkspace();
        using var writer = new StringWriter();
        var exit = MigrateHostStateCommand.Execute(
            workspace.Context,
            new[]
            {
                "host-state",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--role", "review-runtime",
                "--write"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--domain is required.", writer.ToString(), StringComparison.Ordinal);
    }

    private static string BuildQueueState(
        params (string ExecutionUnit, string State, (string Repo, int Number)? LinkedIssue)[] items)
    {
        var entries = string.Join(",", items.Select(item =>
        {
            var li = item.LinkedIssue is null
                ? "null"
                : $$"""{ "repo": "{{item.LinkedIssue.Value.Repo}}", "number": {{item.LinkedIssue.Value.Number}}, "url": "https://github.com/{{item.LinkedIssue.Value.Repo}}/issues/{{item.LinkedIssue.Value.Number}}" }""";
            return $$"""
                {
                  "execution_unit": "{{item.ExecutionUnit}}",
                  "title": "title",
                  "state": "{{item.State}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {{li}},
                  "linked_pr": null,
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
                """;
        }));
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": [{{entries}}]
            }
            """;
    }

    private static string BuildRunLine(string executionUnit, string @event, string repo)
    {
        // Use RunLogSerializer so the line is byte-for-byte what the
        // production code emits.
        var runEvent = new RunEvent
        {
            Ts = DateTimeOffset.Parse("2026-05-12T00:00:00Z"),
            ExecutionUnit = executionUnit,
            Event = @event,
            By = "intent-cli closeout pr",
            Repo = repo
        };
        return RunLogSerializer.SerializeLine(runEvent);
    }

    private sealed class MigrateWorkspace : IDisposable
    {
        public MigrateWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("migrate-host-state-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RepoRoot, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public string RepoRoot { get; }
        public CliContext Context { get; }
        public string LegacyQueuePath => Path.Combine(RepoRoot, ".intent-cli", "queue-state.json");
        public string LegacyRunsPath => Path.Combine(RepoRoot, ".intent-cli", "runs.jsonl");

        public string ScopedQueuePath(string domain, string ownerRepoDir) =>
            Path.Combine(RepoRoot, ".intent-cli", "runtime", domain, ownerRepoDir, "queue-state.json");
        public string ScopedRunsPath(string domain, string ownerRepoDir) =>
            Path.Combine(RepoRoot, ".intent-cli", "runtime", domain, ownerRepoDir, "runs.jsonl");

        public void WriteLegacyQueueState(string content) =>
            File.WriteAllText(LegacyQueuePath, content);

        public void WriteLegacyRuns(IEnumerable<string> lines)
        {
            using var stream = new FileStream(LegacyRunsPath, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            foreach (var line in lines)
            {
                writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot))
            {
                Directory.Delete(RepoRoot, recursive: true);
            }
        }
    }
}
