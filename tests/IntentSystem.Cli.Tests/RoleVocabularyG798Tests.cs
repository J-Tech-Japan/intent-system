using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G798 contract coverage: config values, queue writer vocabulary, legacy
/// display/duplicate compatibility, and the deliberate absence of a runtime
/// field in queue-state.
/// </summary>
public sealed class RoleVocabularyG798Tests
{
    private readonly ITestOutputHelper output;

    public RoleVocabularyG798Tests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Load_ExactFourLineVendorConfig_IsUnchangedAndReportsEveryLegacyKey()
    {
        using var fixture = new Fixture();
        var configText = """
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"
            [roles]
            implement = "Claude"
            review = "Codex"
            interview = "Claude"
            clarify = "Codex"
            """;
        var configPath = fixture.WriteFile(".intent-cli/config.toml", configText);
        var before = File.ReadAllBytes(configPath);

        var config = CliConfigLoader.LoadFromFile(configPath);
        Assert.Equal("Claude", config.Roles.Implement);
        Assert.Equal("Codex", config.Roles.Review);
        Assert.Equal("Claude", config.Roles.Interview);
        Assert.Equal("Codex", config.Roles.Clarify);
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["implement"] = "Claude",
                ["review"] = "Codex",
                ["interview"] = "Claude",
                ["clarify"] = "Codex",
            },
            config.Roles.LegacyValues);

        var context = fixture.CreateContext(config);
        using var writer = new StringWriter();
        var exitCode = AutomationSummaryCommand.Execute(context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var report = document.RootElement.GetProperty("legacy_role_mappings");
        Assert.Equal("Claude", report.GetProperty("implement").GetString());
        Assert.Equal("Codex", report.GetProperty("review").GetString());
        Assert.Equal("Claude", report.GetProperty("interview").GetString());
        Assert.Equal("Codex", report.GetProperty("clarify").GetString());
        Assert.Equal(before, File.ReadAllBytes(configPath));
        output.WriteLine("AC1/AC2 legacy_role_mappings JSON:");
        output.WriteLine(writer.ToString());
        output.WriteLine($"AC2 exit_code={exitCode}; config_bytes_unchanged={before.SequenceEqual(File.ReadAllBytes(configPath))}");
    }

    [Fact]
    public void Load_CanonicalAndAliasValues_UsesLogicalRoleNormalizer()
    {
        var config = CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"
            [roles]
            implement = "IMPLEMENTATION"
            review = "review"
            interview = "architect"
            clarify = "steward"
            """);

        Assert.Equal(LogicalRoleNormalizer.Builder, config.Roles.Implement);
        Assert.Equal(LogicalRoleNormalizer.Reviewer, config.Roles.Review);
        Assert.Equal(LogicalRoleNormalizer.Architect, config.Roles.Interview);
        Assert.Equal(LogicalRoleNormalizer.Steward, config.Roles.Clarify);
        Assert.Empty(config.Roles.LegacyValues);
        Assert.Equal(LogicalRoleNormalizer.Builder, config.Roles.WorkerRoleForQueue);
        Assert.Equal(LogicalRoleNormalizer.Reviewer, config.Roles.ReviewRoleForQueue);
    }

    [Fact]
    public void SeedAndQueueEnqueue_NormalizeKnownValuesAndVendorFallbacks()
    {
        Assert.True(
            PacketYamlDocument.TryParse(
                "implementation_issue_packet:\n  issue_title: G798\n  worker_role: implementation\n  review_role: review\n",
                out var packet,
                out var parseError),
            parseError);
        Assert.NotNull(packet);

        var seeded = AutomationQueueSeedFromPacketCommand.BuildSeedItem(
            "G798",
            packet!,
            "intents/intent-cli/clarifications/open.md",
            LogicalRoleNormalizer.Builder,
            LogicalRoleNormalizer.Reviewer,
            "high");
        Assert.Equal(LogicalRoleNormalizer.Builder, seeded.WorkerRole);
        Assert.Equal(LogicalRoleNormalizer.Reviewer, seeded.ReviewRole);

        var context = new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
                Roles = new RoleMappings
                {
                    Implement = "Claude",
                    Review = "Codex",
                    LegacyValues = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["implement"] = "Claude",
                        ["review"] = "Codex",
                    },
                },
            },
        };
        var queueItem = QueueEnqueueCommand.CreateQueueItem(
            context,
            new QueueEnqueueCommand.ResolvedQueuePacket
            {
                ExecutionUnit = "G798",
                IssueTitle = "G798",
                Dependencies = [],
                ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            },
            new PacketPaths
            {
                Implementation = ".intent-cli/issues/G798/implementation.md",
                ReviewContext = ".intent-cli/issues/G798/review-context.md",
                Yaml = ".intent-cli/issues/G798/packet.yaml",
            });
        Assert.Equal(LogicalRoleNormalizer.Builder, queueItem.WorkerRole);
        Assert.Equal(LogicalRoleNormalizer.Reviewer, queueItem.ReviewRole);
        output.WriteLine($"AC3 seed worker_role={seeded.WorkerRole}; review_role={seeded.ReviewRole}; queue-enqueue worker_role={queueItem.WorkerRole}; review_role={queueItem.ReviewRole}; coder_written=false");
    }

    [Fact]
    public void IntentExplain_RendersEveryLegacyValue()
    {
        using var fixture = new Fixture();
        var items = new[]
        {
            CreateItem("G798-claude-codex", "Claude", "Codex"),
            CreateItem("G798-implement-intake", "implement", "intake"),
            CreateItem("G798-clarify-interview", "clarify", "interview"),
            CreateItem("G798-fix-queue", "fix", "queue"),
            CreateItem("G798-review-coder", "review", "coder"),
        };
        fixture.WriteQueueState(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-09-04T00:00:00Z"),
            Items = items,
        });

        var rendered = new StringBuilder();
        var context = fixture.CreateContext();
        foreach (var item in items)
        {
            using var writer = new StringWriter();
            Assert.Equal(0, IntentExplainCommand.Execute(context, [item.ExecutionUnit], writer));
            rendered.Append(writer);
        }

        foreach (var value in new[]
        {
            "Claude", "Codex", "implement", "intake", "clarify",
            "interview", "fix", "queue", "review", "coder",
        })
        {
            Assert.True(
                rendered.ToString().Contains($"worker role: {value}", StringComparison.Ordinal)
                    || rendered.ToString().Contains($"review role: {value}", StringComparison.Ordinal),
                $"legacy value '{value}' was not rendered by intent explain");
        }
        output.WriteLine("AC4 intent explain output:");
        output.WriteLine(rendered.ToString());
    }

    [Fact]
    public void DuplicateDetection_RemainsDuplicateAcrossCanonicalAndLegacyVocabulary()
    {
        var legacy = CreateItem("G798", "implement", "review") with { State = QueueItemState.Queued };
        var canonical = CreateItem("G798", LogicalRoleNormalizer.Builder, LogicalRoleNormalizer.Reviewer) with
        {
            State = QueueItemState.Queued,
        };

        var groups = DuplicateQueueItemRules.Analyze(
        [
            DuplicateQueueItemRules.FromQueueItem(legacy, 0),
            DuplicateQueueItemRules.FromQueueItem(canonical, 1),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal("G798", group.ExecutionUnit);
        Assert.Equal(2, group.Entries.Count);
        Assert.Null(group.Winner);
        output.WriteLine($"AC5 duplicate_groups={groups.Count}; execution_unit={group.ExecutionUnit}; entries={group.Entries.Count}; winner={(group.Winner is null ? "unresolved" : "selected")}");
    }

    [Fact]
    public void QueueStateSchema_HasNoRuntimeField_AndOpenCodeRoundTrips()
    {
        var config = CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"
            [roles]
            implement = "opencode"
            review = "opencode"
            """);
        Assert.Equal("opencode", config.Roles.Implement);
        Assert.Equal("opencode", config.Roles.Review);
        Assert.Equal("opencode", config.Roles.LegacyValues["implement"]);
        Assert.Equal("opencode", config.Roles.LegacyValues["review"]);
        Assert.Equal(LogicalRoleNormalizer.Builder, config.Roles.WorkerRoleForQueue);
        Assert.Equal(LogicalRoleNormalizer.Reviewer, config.Roles.ReviewRoleForQueue);

        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-09-04T00:00:00Z"),
            Items = [CreateItem("G798-opencode", "opencode", "opencode")],
        };
        var serialized = QueueStateSerializer.Serialize(state);
        using var document = JsonDocument.Parse(serialized);
        var item = document.RootElement.GetProperty("items")[0];
        Assert.False(item.TryGetProperty("runtime", out _));
        Assert.Equal("opencode", item.GetProperty("worker_role").GetString());
        Assert.Equal("opencode", item.GetProperty("review_role").GetString());
        var roundTrip = QueueStateSerializer.Deserialize(serialized).Items.Single();
        Assert.Equal("opencode", roundTrip.WorkerRole);
        Assert.Equal("opencode", roundTrip.ReviewRole);
        output.WriteLine("AC6/AC7 queue-state JSON:");
        output.WriteLine(serialized);
        output.WriteLine($"AC7 config_worker_role={config.Roles.Implement}; config_review_role={config.Roles.Review}; queue_roundtrip_worker_role={roundTrip.WorkerRole}; queue_roundtrip_review_role={roundTrip.ReviewRole}; runtime_field_present={item.TryGetProperty("runtime", out _)}");
    }

    private static QueueItem CreateItem(string executionUnit, string workerRole, string reviewRole)
        => new()
        {
            ExecutionUnit = executionUnit,
            Title = executionUnit,
            State = QueueItemState.Completed,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
            },
            WorkerRole = workerRole,
            ReviewRole = reviewRole,
            Priority = "normal",
        };

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("intent-g798-").FullName;

        public string WriteFile(string relativePath, string content)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        public void WriteQueueState(QueueState state)
        {
            WriteFile(".intent-cli/queue-state.json", QueueStateSerializer.Serialize(state));
        }

        public CliContext CreateContext(CliConfig? config = null)
        {
            return new CliContext
            {
                RepoRoot = root,
                Config = config ?? new CliConfig
                {
                    Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
                },
            };
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
