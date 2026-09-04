using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G795: the role vocabulary is one shared normalization boundary. These
/// fixtures exercise the three role-scoped automation commands together, and
/// deliberately keep runtime/model values independent from logical roles.
/// </summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class LogicalRoleNormalizerG795Tests : IDisposable
{
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G795";
    private const string Commit = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    private static readonly string[] CanonicalRoles =
    [
        "architect",
        "orchestrator",
        "builder",
        "reviewer",
        "steward",
    ];

    private static readonly string[] LegacyAliases =
    [
        "design",
        "orchestration",
        "implementation",
        "review",
    ];

    private static readonly (string Alias, string Canonical)[] AliasPairs =
    [
        ("design", "architect"),
        ("orchestration", "orchestrator"),
        ("implementation", "builder"),
        ("review", "reviewer"),
    ];

    private readonly ITestOutputHelper testOutput;

    public LogicalRoleNormalizerG795Tests(ITestOutputHelper testOutput)
    {
        this.testOutput = testOutput;
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyCandidateLister();
        AutomationStalledWorkCommand.UtcNowFactory = () => new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
    }

    [Fact]
    public void SharedNormalizer_ExposesExactlyFiveCanonicalRolesAndFourAliases_G795()
    {
        Assert.Equal(CanonicalRoles, LogicalRoleNormalizer.CanonicalRoles);
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["design"] = "architect",
                ["orchestration"] = "orchestrator",
                ["implementation"] = "builder",
                ["review"] = "reviewer",
            },
            LogicalRoleNormalizer.Aliases);

        foreach (var role in CanonicalRoles.Concat(LegacyAliases))
        {
            Assert.True(LogicalRoleNormalizer.TryNormalize($"  {role.ToUpperInvariant()}  ", out var normalized, out var error), error);
            Assert.Equal(
                role switch
                {
                    "design" => "architect",
                    "orchestration" => "orchestrator",
                    "implementation" => "builder",
                    "review" => "reviewer",
                    _ => role,
                },
                normalized);
        }
    }

    [Fact]
    public void EveryCanonicalAndLegacyRoleIsAcceptedByAllThreeCommands_G795()
    {
        foreach (var input in CanonicalRoles.Concat(LegacyAliases))
        {
            var expected = Normalize(input);

            using var knowledge = NewWorkspace("knowledge-" + input).RunKnowledge(input);
            Assert.Equal(0, knowledge.ExitCode);
            Assert.Equal(expected, knowledge.Json.GetProperty("recording_role").GetString());

            using var guide = NewWorkspace("guide-" + input).RunGuide(input);
            Assert.Equal(0, guide.ExitCode);
            Assert.Equal(expected, guide.Json.GetProperty("recording_role").GetString());

            using var stalled = NewWorkspace("stalled-" + input).RunStalled(input);
            Assert.Equal(0, stalled.ExitCode);
            Assert.Equal(expected, stalled.Json.GetProperty("recording_role").GetString());
            testOutput.WriteLine($"input={input}; normalized={expected}; knowledge.exit={knowledge.ExitCode}; guide.exit={guide.ExitCode}; stalled.exit={stalled.ExitCode}; knowledge.recording_role={knowledge.Json.GetProperty("recording_role").GetString()}; guide.recording_role={guide.Json.GetProperty("recording_role").GetString()}; stalled.recording_role={stalled.Json.GetProperty("recording_role").GetString()}");
        }
    }

    [Fact]
    public void AliasAndCanonicalInputsHaveByteIdenticalStableProjections_G795()
    {
        foreach (var (alias, canonical) in AliasPairs)
        {
            using var aliasKnowledge = NewWorkspace("projection-knowledge-alias-" + alias).RunKnowledge(alias);
            using var canonicalKnowledge = NewWorkspace("projection-knowledge-canonical-" + canonical).RunKnowledge(canonical);
            Assert.Equal(
                StableProjection(aliasKnowledge.Json, "knowledge"),
                StableProjection(canonicalKnowledge.Json, "knowledge"));

            using var aliasGuide = NewWorkspace("projection-guide-alias-" + alias).RunGuide(alias);
            using var canonicalGuide = NewWorkspace("projection-guide-canonical-" + canonical).RunGuide(canonical);
            Assert.Equal(
                StableProjection(aliasGuide.Json, "guide"),
                StableProjection(canonicalGuide.Json, "guide"));

            using var aliasStalled = NewWorkspace("projection-stalled-alias-" + alias).RunStalled(alias);
            using var canonicalStalled = NewWorkspace("projection-stalled-canonical-" + canonical).RunStalled(canonical);
            Assert.Equal(
                StableProjection(aliasStalled.Json, "stalled"),
                StableProjection(canonicalStalled.Json, "stalled"));
        }
    }

    [Fact]
    public void LegacySpellingPersistsCanonicalBytesAndLegacyRecordReadsCanonically_G795()
    {
        using var workspace = NewWorkspace("persistence");
        using var written = workspace.RunKnowledge("design", write: true);
        Assert.Equal(0, written.ExitCode);
        Assert.Equal("architect", written.Json.GetProperty("recording_role").GetString());

        var canonicalPath = RoleScopedCloseoutRecordStore.ResolveRoleFullPath(
            workspace.RootPath,
            KnowledgeWriteBackRecord.RecordRootRelativePath,
            Unit,
            "architect");
        Assert.True(File.Exists(canonicalPath));
        var persisted = File.ReadAllText(canonicalPath);
        Assert.Contains("\"role\": \"architect\"", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("\"role\": \"design\"", persisted, StringComparison.Ordinal);
        testOutput.WriteLine($"legacy-input=design; canonical-recording-role={written.Json.GetProperty("recording_role").GetString()}; persisted-role-bytes=\"role\": \"architect\"; canonical-path={Path.GetRelativePath(workspace.RootPath, canonicalPath).Replace(Path.DirectorySeparatorChar, '/')}");

        using var legacyWorkspace = NewWorkspace("legacy-read");
        var legacyPath = Path.Combine(
            legacyWorkspace.RootPath,
            KnowledgeWriteBackRecord.RecordRootRelativePath.Replace('/', Path.DirectorySeparatorChar),
            Unit,
            RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName,
            "design.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, $$"""
            {
              "artifact_kind": "knowledge-writeback-record",
              "execution_unit": "{{Unit}}",
              "role": "design",
              "host_commit": "{{Commit}}",
              "recorded_at": "2026-09-04T12:00:00+00:00",
              "targets": [],
              "note": null
            }
            """);

        var legacy = KnowledgeWriteBackRecord.Deserialize(File.ReadAllText(legacyPath), Unit);
        Assert.Equal("architect", legacy.Role);
        Assert.Contains("\"role\": \"architect\"", KnowledgeWriteBackRecord.Serialize(legacy), StringComparison.Ordinal);
        using var reemitted = legacyWorkspace.RunKnowledge("architect");
        Assert.Equal(0, reemitted.ExitCode);
        Assert.Equal("architect", reemitted.Json.GetProperty("recording_role").GetString());
        Assert.Equal("architect", reemitted.Json.GetProperty("records")[0].GetProperty("role").GetString());
        testOutput.WriteLine($"legacy-read-path={Path.GetRelativePath(legacyWorkspace.RootPath, legacyPath).Replace(Path.DirectorySeparatorChar, '/')}; reemitted-role={reemitted.Json.GetProperty("records")[0].GetProperty("role").GetString()}");
    }

    [Fact]
    public void UnknownRoleIsRefusedAndNamesCanonicalSetAndAliases_G795()
    {
        const string unknown = "maintainer";
        var expectedMessage = LogicalRoleNormalizer.BuildUnknownRoleMessage(unknown);

        using var knowledge = NewWorkspace("unknown-knowledge").RunKnowledgeText(unknown);
        Assert.Equal(1, knowledge.ExitCode);
        Assert.Contains(expectedMessage, knowledge.Output, StringComparison.Ordinal);

        using var guide = NewWorkspace("unknown-guide").RunGuideText(unknown);
        Assert.Equal(1, guide.ExitCode);
        Assert.Contains(expectedMessage, guide.Output, StringComparison.Ordinal);

        using var stalled = NewWorkspace("unknown-stalled").RunStalledText(unknown);
        Assert.Equal(1, stalled.ExitCode);
        Assert.Contains(expectedMessage, stalled.Output, StringComparison.Ordinal);
        testOutput.WriteLine($"unknown-role={unknown}; all-three-refused=true; message={expectedMessage}");
    }

    [Fact]
    public void CrossSurfaceAgreementAndStewardAcceptanceUseTheSameCanonicalValue_G795()
    {
        using var workspace = NewWorkspace("agreement");
        using var knowledge = workspace.RunKnowledge("implementation");
        using var guide = workspace.RunGuide("builder");
        using var stalled = workspace.RunStalled("IMPLEMENTATION");

        Assert.Equal("builder", knowledge.Json.GetProperty("recording_role").GetString());
        Assert.Equal("builder", guide.Json.GetProperty("recording_role").GetString());
        Assert.Equal("builder", stalled.Json.GetProperty("recording_role").GetString());

        foreach (var command in new[] { "knowledge", "guide", "stalled" })
        {
            using var result = command switch
            {
                "knowledge" => NewWorkspace("steward-knowledge").RunKnowledge("steward"),
                "guide" => NewWorkspace("steward-guide").RunGuide("steward"),
                _ => NewWorkspace("steward-stalled").RunStalled("steward"),
            };
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("steward", result.Json.GetProperty("recording_role").GetString());
        }
    }

    [Fact]
    public void OpencodeRuntimeRemainsFreeFormAndRoundTripsOnRoleRecord_G795()
    {
        using var workspace = NewWorkspace("opencode");
        using var writer = new StringWriter();
        var exitCode = SessionLayerTopologyCommand.Execute(
            workspace.Context,
            [
                "record", "--domain", "intent-cli", "--team", "g795-opencode",
                "--role", "architect", "--resident", "herdr", "--workspace-id", "ws-g795",
                "--pane-id", "ws-g795:1", "--cwd", "/tmp", "--kind", "opencode",
                "--write", "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.Equal("architect", result.RootElement.GetProperty("role").GetString());

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(workspace.RootPath, "intent-cli", "g795-opencode");
        using var topology = JsonDocument.Parse(File.ReadAllText(topologyPath));
        var role = topology.RootElement.GetProperty("roles").GetProperty("architect");
        Assert.Equal("opencode", role.GetProperty("kind").GetString());
        Assert.Equal("herdr", role.GetProperty("resident").GetString());
        testOutput.WriteLine("runtime=opencode; role=architect; round_trip=true; kind-preserved=true");
    }

    [Fact]
    public void DocumentationCarriesTheSameConsumerInventoryInEnglishAndJapanese_G795()
    {
        var repoRoot = FindRepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var content = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "08-command-reference.md"));
            Assert.Contains("G795", content, StringComparison.Ordinal);
            Assert.Contains("role-consumer-inventory-entries: 24", content, StringComparison.Ordinal);
            Assert.Contains("LogicalRoleNormalizer", content, StringComparison.Ordinal);
            Assert.Contains("worker_role", content, StringComparison.Ordinal);
            Assert.Contains("review_role", content, StringComparison.Ordinal);
            Assert.Contains("runtime", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("action verb", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Normalize(string input) => input switch
    {
        "design" => "architect",
        "orchestration" => "orchestrator",
        "implementation" => "builder",
        "review" => "reviewer",
        _ => input,
    };

    private static string StableProjection(JsonElement json, string command) =>
        command switch
        {
            "knowledge" => string.Join('|',
                json.GetProperty("mode").GetString(),
                json.GetProperty("applied").GetBoolean(),
                json.GetProperty("recording_role").GetString(),
                json.GetProperty("role_source").GetString(),
                json.GetProperty("record_path").GetString(),
                json.GetProperty("declaration_required").GetBoolean()),
            "guide" => string.Join('|',
                json.GetProperty("mode").GetString(),
                json.GetProperty("applied").GetBoolean(),
                json.GetProperty("recording_role").GetString(),
                json.GetProperty("role_source").GetString(),
                json.GetProperty("record_path").GetString(),
                json.GetProperty("declaration_present").GetBoolean()),
            _ => string.Join('|',
                json.GetProperty("repo").GetString(),
                json.GetProperty("recording_role").GetString(),
                json.GetProperty("stalled").GetBoolean(),
                json.GetProperty("open_pending_delegations").GetInt32(),
                json.GetProperty("items").GetArrayLength()),
        };

    private static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.NotNull(directory);
        return directory!;
    }

    private Workspace NewWorkspace(string suffix) => new(Path.Combine(Path.GetTempPath(), $"g795-{suffix}-{Guid.NewGuid():N}"));

    private sealed class Workspace : IDisposable
    {
        public Workspace(string rootPath)
        {
            RootPath = rootPath;
            Directory.CreateDirectory(rootPath);
            Context = new CliContext
            {
                RepoRoot = rootPath,
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

        public CommandRun RunKnowledge(string role, bool write = false)
        {
            WriteKnowledgePacket();
            return RunJson((writer, args) => AutomationKnowledgeWriteBackRecordCommand.Execute(Context, args, writer),
                ["--execution-unit", Unit, "--commit", Commit, "--role", role, write ? "--write" : "--dry-run", "--format", "json"]);
        }

        public TextRun RunKnowledgeText(string role)
        {
            WriteKnowledgePacket();
            using var writer = new StringWriter();
            var exitCode = AutomationKnowledgeWriteBackRecordCommand.Execute(
                Context,
                ["--execution-unit", Unit, "--commit", Commit, "--role", role, "--dry-run", "--format", "json"],
                writer);
            return new TextRun(exitCode, writer.ToString());
        }

        public CommandRun RunGuide(string role, bool write = false)
        {
            WriteGuidePacket();
            return RunJson((writer, args) => AutomationGuideReachabilityRecordCommand.Execute(Context, args, writer),
                ["--execution-unit", Unit, "--commit", Commit, "--role", role, write ? "--write" : "--dry-run", "--format", "json"]);
        }

        public TextRun RunGuideText(string role)
        {
            WriteGuidePacket();
            using var writer = new StringWriter();
            var exitCode = AutomationGuideReachabilityRecordCommand.Execute(
                Context,
                ["--execution-unit", Unit, "--commit", Commit, "--role", role, "--dry-run", "--format", "json"],
                writer);
            return new TextRun(exitCode, writer.ToString());
        }

        public CommandRun RunStalled(string role)
        {
            using var writer = new StringWriter();
            var exitCode = AutomationStalledWorkCommand.Execute(
                Context,
                ["--domain", "intent-cli", "--repo", Repo, "--role", role, "--format", "json"],
                writer);
            var document = JsonDocument.Parse(writer.ToString());
            return new CommandRun(exitCode, document);
        }

        public TextRun RunStalledText(string role)
        {
            using var writer = new StringWriter();
            var exitCode = AutomationStalledWorkCommand.Execute(
                Context,
                ["--domain", "intent-cli", "--repo", Repo, "--role", role, "--format", "json"],
                writer);
            return new TextRun(exitCode, writer.ToString());
        }

        private CommandRun RunJson(Func<TextWriter, string[], int> execute, string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = execute(writer, args);
            return new CommandRun(exitCode, JsonDocument.Parse(writer.ToString()));
        }

        private void WriteKnowledgePacket()
        {
            WritePacket($"""
                implementation_issue_packet:
                  source_execution_unit: {Unit}
                  domain: intent-cli
                knowledge_updates:
                  intent_tree:
                    required: true
                    target_paths:
                      - intents/intent-cli/intent-tree/means/role-normalizer.md
                    summary: "canonical role vocabulary"
                  adr:
                    required: false
                    target_paths: []
                  diagram:
                    required: false
                    target_paths: []
                  docs:
                    required: false
                    target_paths: []
                closeout_learning:
                  expected: ""
                  write_back_required: false
                  write_back_targets: []
                """);
        }

        private void WriteGuidePacket()
        {
            WritePacket($"""
                implementation_issue_packet:
                  source_execution_unit: {Unit}
                  domain: intent-cli
                guide_reachability:
                  no_role_facing_surface: false
                  routes:
                    - guide_surface: guide role-normalizer
                      role: builder
                      target_surface: role vocabulary inventory
                """);
        }

        private void WritePacket(string yaml)
        {
            var packetDirectory = Path.Combine(RootPath, ".intent-cli", "issues", Unit);
            Directory.CreateDirectory(packetDirectory);
            File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), yaml);
        }

        public void Dispose()
        {
            // The fixtures are intentionally retained under /tmp so the
            // durable test output can be inspected after a run.
        }
    }

    private sealed class CommandRun : IDisposable
    {
        public CommandRun(int exitCode, JsonDocument document)
        {
            ExitCode = exitCode;
            Json = document.RootElement.Clone();
            Document = document;
        }

        public int ExitCode { get; }
        public JsonElement Json { get; }
        private JsonDocument Document { get; }
        public void Dispose() => Document.Dispose();
    }

    private sealed record TextRun(int ExitCode, string Output) : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class EmptyCandidateLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => [];

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => [];

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => [];

        public IReadOnlyList<GitHubAutomationReleaseCandidate> ListPublishedReleases(string repo) => [];
    }
}
