using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G698: closeout evidence is attributed to the recorder, not collapsed into
/// one append-only slot. These fixtures deliberately leave their unique temp
/// directories in place: the task contract forbids deleting /tmp paths.
/// </summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class RoleScopedCloseoutG698Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string HostCommit = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private static readonly List<JsonDocument> documents = new();

    public RoleScopedCloseoutG698Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyCandidateLister();
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
        AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory = () => FixedNow;
        AutomationGuideReachabilityRecordCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        foreach (var document in documents)
        {
            document.Dispose();
        }
        documents.Clear();

        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory = null;
        AutomationGuideReachabilityRecordCommand.UtcNowFactory = null;
    }

    [Fact]
    public void KnowledgeRecords_CoexistByRole_AndWrongOrMissingRolesRefuse_G698()
    {
        var workspace = NewWorkspace("knowledge");
        workspace.WriteKnowledgePacket("G698");

        var design = workspace.RunKnowledge(
            "--execution-unit", "G698", "--commit", HostCommit, "--role", "design", "--write", "--format", "json");
        Assert.Equal(0, design.ExitCode);
        Assert.Equal("design", design.Json.GetProperty("recording_role").GetString());
        Assert.True(design.Json.GetProperty("applied").GetBoolean());
        Assert.True(File.Exists(workspace.KnowledgeRolePath("G698", "design")));

        var orchestration = workspace.RunKnowledge(
            "--execution-unit", "G698", "--commit", HostCommit, "--role", "orchestration", "--write", "--format", "json");
        Assert.Equal(0, orchestration.ExitCode);
        Assert.Equal(2, orchestration.Json.GetProperty("records").GetArrayLength());
        Assert.Contains(
            "design",
            orchestration.Json.GetProperty("recorded_roles").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(
            "orchestration",
            orchestration.Json.GetProperty("recorded_roles").EnumerateArray().Select(value => value.GetString()));
        Assert.True(File.Exists(workspace.KnowledgeRolePath("G698", "orchestration")));

        var conflict = workspace.RunKnowledge(
            "--execution-unit", "G698", "--commit", "0f0f0f0f0f0f0f0f", "--role", "design", "--write", "--format", "json");
        Assert.Equal(1, conflict.ExitCode);
        Assert.Contains("refusing to replace", conflict.Json.GetProperty("error").GetString(), StringComparison.Ordinal);

        var missing = workspace.RunKnowledgeText(
            "--execution-unit", "G698", "--commit", HostCommit, "--role");
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("--role requires", missing.Output, StringComparison.Ordinal);

        var wrong = workspace.RunKnowledgeText(
            "--execution-unit", "G698", "--commit", HostCommit, "--role", "review");
        Assert.Equal(1, wrong.ExitCode);
        Assert.Contains("not supported", wrong.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyKnowledgeRecord_LoadsUnattributed_AndRoleScopedDebtDoesNotSilentlyClear_G698()
    {
        var workspace = NewWorkspace("legacy-knowledge");
        workspace.WriteKnowledgePacket("G698");
        workspace.WriteCloseout("G698", FixedNow.AddMinutes(-180));
        workspace.WriteRawKnowledgeRecord("G698", role: null);

        var legacy = KnowledgeWriteBackRecord.Deserialize(
            File.ReadAllText(workspace.KnowledgeLegacyPath("G698")),
            "G698");
        Assert.Null(legacy.Role);

        // A legacy unattributed record still clears the compatibility scan.
        Assert.Empty(workspace.RunStalled().GetProperty("items").EnumerateArray());

        // It must not clear an explicitly selected role.
        var designDebt = workspace.RunStalled("--role", "design");
        var item = Assert.Single(designDebt.GetProperty("items").EnumerateArray());
        Assert.Equal("design", item.GetProperty("recording_role").GetString());
        Assert.Contains("unattributed", item.GetProperty("recorded_roles").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("--role design", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RoleSpecificKnowledgeDebt_ClearsOnlyAfterSelectedRoleRecords_G698()
    {
        var workspace = NewWorkspace("role-debt");
        workspace.WriteKnowledgePacket("G698");
        workspace.WriteCloseout("G698", FixedNow.AddMinutes(-180));

        var orchestration = workspace.RunKnowledge(
            "--execution-unit", "G698", "--commit", HostCommit, "--role", "orchestration", "--write", "--format", "json");
        Assert.Equal(0, orchestration.ExitCode);

        var designDebt = workspace.RunStalled("--role", "design");
        var pending = Assert.Single(designDebt.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackPending, pending.GetProperty("kind").GetString());
        Assert.Equal("orchestration", Assert.Single(pending.GetProperty("recorded_roles").EnumerateArray()).GetString());

        var design = workspace.RunKnowledge(
            "--execution-unit", "G698", "--commit", HostCommit, "--role", "design", "--write", "--format", "json");
        Assert.Equal(0, design.ExitCode);
        Assert.Empty(workspace.RunStalled("--role", "design").GetProperty("items").EnumerateArray());
        Assert.Empty(workspace.RunStalled("--role", "orchestration").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void GuideRecords_CoexistByRole_AndLegacyGuideRecordRemainsReadable_G698()
    {
        var workspace = NewWorkspace("guide");
        workspace.WriteGuidePacket("G698");
        workspace.WriteCloseout("G698", FixedNow.AddMinutes(-180));

        var design = workspace.RunGuide(
            "--execution-unit", "G698", "--commit", HostCommit, "--role", "design", "--write", "--format", "json");
        Assert.Equal(0, design.ExitCode);
        var orchestrationDebt = workspace.RunStalled("--role", "orchestration");
        var pending = Assert.Single(orchestrationDebt.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindGuideReachabilityPending, pending.GetProperty("kind").GetString());
        Assert.Equal("orchestration", pending.GetProperty("recording_role").GetString());
        Assert.Contains("design", pending.GetProperty("recorded_roles").EnumerateArray().Select(value => value.GetString()));

        var orchestration = workspace.RunGuide(
            "--execution-unit", "G698", "--commit", HostCommit, "--role", "orchestration", "--write", "--format", "json");
        Assert.Equal(0, orchestration.ExitCode);
        Assert.Equal(2, orchestration.Json.GetProperty("records").GetArrayLength());
        Assert.Contains("design", orchestration.Json.GetProperty("recorded_roles").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("orchestration", orchestration.Json.GetProperty("recorded_roles").EnumerateArray().Select(value => value.GetString()));
        Assert.Empty(workspace.RunStalled("--role", "orchestration").GetProperty("items").EnumerateArray());

        var wrong = workspace.RunGuideText(
            "--execution-unit", "G698", "--commit", HostCommit, "--role", "review");
        Assert.Equal(1, wrong.ExitCode);
        Assert.Contains("not supported", wrong.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void OrchestrationGuides_AreExecutableFromBareDirectory_AndNameRoleSyntax_G698()
    {
        var cliDll = ResolveBuiltCliFromActiveTestOutput();

        var bareDirectory = Path.Combine(Path.GetTempPath(), $"g698-bare-guide-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bareDirectory);
        var closeout = RunBuiltCli(
            cliDll,
            bareDirectory,
            "guide", "closeout", "run", "--domain", "intent-cli", "--repo", Repo, "--format", "json");
        Assert.Equal(0, closeout.ExitCode);
        Assert.Contains("--role design", closeout.Output, StringComparison.Ordinal);
        Assert.Contains("--role orchestration", closeout.Output, StringComparison.Ordinal);
        Assert.Contains("G698 role split", closeout.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(bareDirectory, ".intent-cli", "config.toml")));

        var orchestrator = RunBuiltCli(
            cliDll,
            bareDirectory,
            "guide", "orchestrator-thread", "--domain", "intent-cli", "--target-repo", Repo, "--agent", "claude", "--format", "json");
        Assert.Equal(0, orchestrator.ExitCode);
        Assert.Contains("--role design", orchestrator.Output, StringComparison.Ordinal);
        Assert.Contains("--role orchestration", orchestrator.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveTestOutput_ContainsRunnableCliAndRuntimeFiles_G698()
    {
        var cliDll = ResolveBuiltCliFromActiveTestOutput();
        Assert.True(File.Exists(cliDll), $"built CLI not found in active test output: {cliDll}");
        Assert.True(
            File.Exists(Path.ChangeExtension(cliDll, ".runtimeconfig.json")),
            $"CLI runtimeconfig not found beside active test output: {cliDll}");
        Assert.True(
            File.Exists(Path.ChangeExtension(cliDll, ".deps.json")),
            $"CLI deps file not found beside active test output: {cliDll}");
    }

    [Fact]
    public void EnglishAndJapaneseOperationalGuides_NameTheSameRoleScopedCommands_G698()
    {
        var repoRoot = FindRepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var docs = Directory.EnumerateFiles(Path.Combine(repoRoot, "docs", language), "*.md", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText)
                .ToArray();
            var combined = string.Join("\n", docs);
            Assert.Contains("knowledge-writeback-record", combined, StringComparison.Ordinal);
            Assert.Contains("guide-reachability-record", combined, StringComparison.Ordinal);
            Assert.Contains("--role design", combined, StringComparison.Ordinal);
            Assert.Contains("--role orchestration", combined, StringComparison.Ordinal);
            Assert.Contains("legacy", combined, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Workspace NewWorkspace(string suffix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"g698-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        return new Workspace(root);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }

    private static string ResolveBuiltCliFromActiveTestOutput()
    {
        // The test assembly is copied into the active configuration/output
        // directory by the project reference. Resolving beside that assembly
        // keeps Release CI, Debug runs, and custom output layouts on the same
        // path without guessing a configuration or target framework.
        var outputDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var cliDll = Path.Combine(outputDirectory, "IntentSystem.Cli.dll");
        Assert.True(
            File.Exists(cliDll),
            $"built CLI not found in active test output '{outputDirectory}'; expected {cliDll}");
        return cliDll;
    }

    private static ProcessResult RunBuiltCli(string cliDll, string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add(cliDll);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output + error);
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed record RecordRun(int ExitCode, JsonElement Json);

    private sealed record TextRun(int ExitCode, string Output);

    private sealed class Workspace
    {
        public Workspace(string rootPath)
        {
            RootPath = rootPath;
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

        public string KnowledgeLegacyPath(string unit) =>
            KnowledgeWriteBackRecord.ResolveFullPath(RootPath, unit);

        public string KnowledgeRolePath(string unit, string role) =>
            RoleScopedCloseoutRecordStore.ResolveRoleFullPath(
                RootPath, KnowledgeWriteBackRecord.RecordRootRelativePath, unit, role);

        public void WriteKnowledgePacket(string unit)
        {
            WritePacket(unit, $"""
                implementation_issue_packet:
                  source_execution_unit: {unit}
                  domain: intent-cli
                knowledge_updates:
                  intent_tree:
                    required: true
                    target_paths:
                      - intents/intent-cli/intent-tree/means/role-scoped-closeout.md
                    summary: "role-scoped closeout evidence"
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

        public void WriteGuidePacket(string unit)
        {
            WritePacket(unit, $"""
                implementation_issue_packet:
                  source_execution_unit: {unit}
                  domain: intent-cli
                guide_reachability:
                  no_role_facing_surface: false
                  routes:
                    - guide_surface: guide closeout
                      role: orchestration
                      target_surface: role-scoped closeout recorder
                """);
        }

        public void WritePacket(string unit, string yaml)
        {
            var directory = Path.Combine(RootPath, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"), yaml);
        }

        public void WriteRawKnowledgeRecord(string unit, string? role)
        {
            var path = KnowledgeLegacyPath(unit);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var roleProperty = role is null ? string.Empty : $"\n  \"role\": \"{role}\",";
            File.WriteAllText(path, $$"""
                {
                  "artifact_kind": "knowledge-writeback-record",
                  "execution_unit": "{{unit}}",
                  "host_commit": "{{HostCommit}}",
                  "recorded_at": "2026-08-15T12:00:00+00:00",
                  "targets": [],{{roleProperty}}
                  "note": null
                }
                """);
        }

        public void WriteCloseout(string unit, DateTimeOffset at)
        {
            var path = Context.GetRunLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = IntentSystem.Supervisor.Serialization.RunLogSerializer.SerializeLine(
                new IntentSystem.Supervisor.Models.RunEvent
                {
                    Ts = at,
                    ExecutionUnit = unit,
                    Event = "closeout-recorded",
                    By = "intent-cli closeout pr",
                });
            File.AppendAllText(path, line + Environment.NewLine);
        }

        public RecordRun RunKnowledge(params string[] args) => RunJson(
            (writer, commandArgs) => AutomationKnowledgeWriteBackRecordCommand.Execute(Context, commandArgs, writer), args);

        public TextRun RunKnowledgeText(params string[] args) => RunText(
            (writer, commandArgs) => AutomationKnowledgeWriteBackRecordCommand.Execute(Context, commandArgs, writer), args);

        public RecordRun RunGuide(params string[] args) => RunJson(
            (writer, commandArgs) => AutomationGuideReachabilityRecordCommand.Execute(Context, commandArgs, writer), args);

        public TextRun RunGuideText(params string[] args) => RunText(
            (writer, commandArgs) => AutomationGuideReachabilityRecordCommand.Execute(Context, commandArgs, writer), args);

        public JsonElement RunStalled(params string[] extraArgs)
        {
            var args = new List<string> { "--domain", "intent-cli", "--repo", Repo, "--format", "json" };
            args.AddRange(extraArgs);
            using var writer = new StringWriter();
            var exit = AutomationStalledWorkCommand.Execute(Context, args.ToArray(), writer);
            Assert.Equal(0, exit);
            var document = JsonDocument.Parse(writer.ToString());
            // The owning test class holds the document until Dispose; this
            // keeps returned JsonElement values valid without deleting the
            // unique fixture directory.
            documents.Add(document);
            return document.RootElement;
        }

        private RecordRun RunJson(Func<TextWriter, string[], int> execute, string[] args)
        {
            using var writer = new StringWriter();
            var exit = execute(writer, args);
            var document = JsonDocument.Parse(writer.ToString());
            documents.Add(document);
            return new RecordRun(exit, document.RootElement);
        }

        private static TextRun RunText(Func<TextWriter, string[], int> execute, string[] args)
        {
            using var writer = new StringWriter();
            var exit = execute(writer, args);
            return new TextRun(exit, writer.ToString());
        }
    }

    private sealed class EmptyCandidateLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();
    }
}
