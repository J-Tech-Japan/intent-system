using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class IntentInitCommandTests
{
    [Fact]
    public void Execute_WithoutWrite_PerformsDryRun_AndCreatesNoFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--target-repo", "owner/repo"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Path.Combine(hostRoot, ".intent-cli", "config.toml")));
        Assert.False(File.Exists(Path.Combine(hostRoot, "intents", "auth", "README.md")));

        var output = writer.ToString();
        Assert.Contains("Mode: dry-run", output, StringComparison.Ordinal);
        Assert.Contains("`.intent-cli/config.toml`", output, StringComparison.Ordinal);
        Assert.Contains("`intents/auth/README.md`", output, StringComparison.Ordinal);
        Assert.Contains("--write", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithWrite_CreatesScaffold_AndIsIdempotentOnSecondRun()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var firstWriter = new StringWriter();

        var firstExitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            firstWriter);

        Assert.Equal(0, firstExitCode);
        var configPath = Path.Combine(hostRoot, ".intent-cli", "config.toml");
        var readmePath = Path.Combine(hostRoot, "intents", "auth", "README.md");
        var clarificationsPath = Path.Combine(hostRoot, "intents", "auth", "clarifications", "open.md");
        var intentMapPath = Path.Combine(hostRoot, "intents", "auth", "intent-tree", "00-map.md");
        Assert.True(File.Exists(configPath));
        Assert.True(File.Exists(readmePath));
        Assert.True(File.Exists(clarificationsPath));
        Assert.True(File.Exists(intentMapPath));

        var configContent = File.ReadAllText(configPath);
        Assert.Contains("domain = \"auth\"", configContent, StringComparison.Ordinal);
        Assert.Contains("artifact_root = \".intent-cli\"", configContent, StringComparison.Ordinal);

        File.WriteAllText(readmePath, "operator override\n");

        using var secondWriter = new StringWriter();
        var secondExitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            secondWriter);

        Assert.Equal(0, secondExitCode);
        Assert.Equal("operator override\n", File.ReadAllText(readmePath));
        var secondOutput = secondWriter.ToString();
        Assert.Contains("Already initialized", secondOutput, StringComparison.Ordinal);
        Assert.Contains("Existing paths", secondOutput, StringComparison.Ordinal);
        Assert.Contains("`intents/auth/README.md`", secondOutput, StringComparison.Ordinal);
    }

    // ── G301 host bootstrap (AGENTS.md / CLAUDE.md / host-binding.toml) ──

    [Fact]
    public void Execute_WithWrite_AlsoBootstrapsAgentsClaudeAndHostBinding()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "trace-forge-poc", "--target-repo", "J-Tech-Japan/TraceForge", "--host-repo", "J-Tech-Japan/TraceForgeHost", "--write"],
            writer);

        Assert.Equal(0, exitCode);
        var agents = Path.Combine(hostRoot, "AGENTS.md");
        var claude = Path.Combine(hostRoot, "CLAUDE.md");
        var binding = Path.Combine(hostRoot, ".intent-cli", "host-binding.toml");
        Assert.True(File.Exists(agents));
        Assert.True(File.Exists(claude));
        Assert.True(File.Exists(binding));

        var agentsText = File.ReadAllText(agents);
        Assert.Contains("intent host repository", agentsText, StringComparison.Ordinal);
        Assert.Contains("Work directly on `main`", agentsText, StringComparison.Ordinal);
        Assert.Contains("Wrong-host detection (G301)", agentsText, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/TraceForgeHost", agentsText, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/TraceForge", agentsText, StringComparison.Ordinal);
        Assert.Contains("trace-forge-poc", agentsText, StringComparison.Ordinal);

        var claudeText = File.ReadAllText(claude);
        Assert.Contains("CLAUDE.md", claudeText, StringComparison.Ordinal);
        Assert.Contains("trace-forge-poc", claudeText, StringComparison.Ordinal);
        Assert.Contains("Hard rules (host repo)", claudeText, StringComparison.Ordinal);
        Assert.Contains("G299", claudeText, StringComparison.Ordinal);

        var bindingText = File.ReadAllText(binding);
        Assert.Contains("[host]", bindingText, StringComparison.Ordinal);
        Assert.Contains("domain = \"trace-forge-poc\"", bindingText, StringComparison.Ordinal);
        Assert.Contains("host_repo = \"J-Tech-Japan/TraceForgeHost\"", bindingText, StringComparison.Ordinal);
        Assert.Contains("target_repo = \"J-Tech-Japan/TraceForge\"", bindingText, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithWrite_HostBindingHasEmptyValuesWhenFlagsOmitted()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write"],
            writer);

        Assert.Equal(0, exitCode);
        var binding = File.ReadAllText(Path.Combine(hostRoot, ".intent-cli", "host-binding.toml"));
        Assert.Contains("domain = \"auth\"", binding, StringComparison.Ordinal);
        Assert.Contains("host_repo = \"\"", binding, StringComparison.Ordinal);
        Assert.Contains("target_repo = \"\"", binding, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DryRun_ReportsAgentsClaudeAndHostBindingAsPlanned()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--target-repo", "owner/repo", "--host-repo", "owner/host", "--format", "json"],
            writer);

        var json = writer.ToString();
        Assert.Contains("\"AGENTS.md\"", json, StringComparison.Ordinal);
        Assert.Contains("\"CLAUDE.md\"", json, StringComparison.Ordinal);
        Assert.Contains("\".intent-cli/host-binding.toml\"", json, StringComparison.Ordinal);
        // No write applied → none of those files should exist on disk.
        Assert.False(File.Exists(Path.Combine(hostRoot, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(hostRoot, "CLAUDE.md")));
        Assert.False(File.Exists(Path.Combine(hostRoot, ".intent-cli", "host-binding.toml")));
    }

    [Fact]
    public void Execute_RejectsMissingHostRepoValue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--host-repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--host-repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FromChildWorktree_ReturnsExitCodeOne_WithGuidance()
    {
        using var tempDirectory = new TemporaryDirectory();
        var childRoot = tempDirectory.CreateDirectory(Path.Combine("host", ".intent-cli", "worktrees", "auth-2026"));
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(childRoot),
            ["--domain", "auth", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("automation child worktree", output, StringComparison.Ordinal);
        Assert.Contains("parent host repository", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(childRoot, ".intent-cli", "config.toml")));
    }

    [Fact]
    public void Execute_MissingDomain_ReturnsExitCodeOne_WithUsageHint()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--write"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("--domain", output, StringComparison.Ordinal);
        Assert.Contains("Usage: intent-cli intent init", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsInvalidDomainSlug()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "not a slug", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be a slug", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_ProducesParseableJson()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "auth", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var json = writer.ToString().Trim();
        Assert.StartsWith("{", json, StringComparison.Ordinal);
        Assert.Contains("\"domain\": \"auth\"", json, StringComparison.Ordinal);
        Assert.Contains("\"write_applied\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"planned_paths\"", json, StringComparison.Ordinal);
        Assert.Contains("\"written_paths\"", json, StringComparison.Ordinal);
        Assert.Contains("\"existing_paths\"", json, StringComparison.Ordinal);
        Assert.Contains("\"next_steps\"", json, StringComparison.Ordinal);
    }

    // ── G346 base branch policy in intent init ───────────────────────────────
    [Fact]
    public void Execute_NoBaseBranchPolicy_WritesDirectMainAsDefault()
    {
        // G346: when --base-branch-policy is omitted, intent init must write
        // base_branch_policy = "direct-main" into the generated config.toml.
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "myapp", "--write"],
            writer);

        Assert.Equal(0, exitCode);
        var configContent = File.ReadAllText(Path.Combine(hostRoot, ".intent-cli", "config.toml"));
        Assert.Contains("base_branch_policy = \"direct-main\"", configContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MainAiBaseBranchPolicy_WritesMainAiInConfig()
    {
        // G346: when --base-branch-policy main-ai is supplied, the generated
        // config.toml must persist base_branch_policy = "main-ai".
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "myapp", "--base-branch-policy", "main-ai", "--write"],
            writer);

        Assert.Equal(0, exitCode);
        var configContent = File.ReadAllText(Path.Combine(hostRoot, ".intent-cli", "config.toml"));
        Assert.Contains("base_branch_policy = \"main-ai\"", configContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsUnknownBaseBranchPolicyInInit()
    {
        // G346: unknown policy values must fail before any file mutation.
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "myapp", "--base-branch-policy", "squash-main", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--base-branch-policy must be", writer.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // G441 durable-state skeletons (first-run host-check)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_G441_WithWrite_CreatesDurableStateSkeletons()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        var exitCode = IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "demo", "--target-repo", "owner/repo", "--write"],
            writer);

        Assert.Equal(0, exitCode);
        var queueStatePath = Path.Combine(hostRoot, ".intent-cli", "queue-state.json");
        var runsPath = Path.Combine(hostRoot, ".intent-cli", "runs.jsonl");
        Assert.True(File.Exists(queueStatePath), "init must scaffold .intent-cli/queue-state.json");
        Assert.True(File.Exists(runsPath), "init must scaffold .intent-cli/runs.jsonl");

        // queue-state must be a valid, round-trippable empty skeleton.
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        Assert.Empty(queueState.Items);
        Assert.Equal("1", queueState.SchemaVersion);

        // runs.jsonl is an empty append-only log skeleton.
        Assert.Equal(string.Empty, File.ReadAllText(runsPath));
    }

    [Fact]
    public void Execute_G441_FreshHost_AllHostCheckArtifactsPresent_AnalyzerReportsOk()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        using var writer = new StringWriter();

        IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "demo", "--target-repo", "owner/repo", "--host-repo", "owner/host", "--write"],
            writer);

        // The four artifacts host-check keys off must all exist after a
        // single `intent init --write`, so the analyzer no longer returns
        // `partially-initialized` solely due to missing scaffolds (AC #3).
        bool Present(params string[] parts) => File.Exists(Path.Combine(new[] { hostRoot }.Concat(parts).ToArray()));
        var input = new IntentHostCheckInput
        {
            RepoRoot = hostRoot,
            Domain = "demo",
            TargetRepo = "owner/repo",
            ObservedRemoteUrl = null,
            BoundHostRepo = "owner/host",
            ConfigTomlPresent = Present(".intent-cli", "config.toml"),
            HostBindingPresent = Present(".intent-cli", "host-binding.toml"),
            IntentsDomainDirectoryPresent = Directory.Exists(Path.Combine(hostRoot, "intents", "demo")),
            AgentsMarkdownPresent = Present("AGENTS.md"),
            ClaudeMarkdownPresent = Present("CLAUDE.md"),
            QueueStateJsonPresent = Present(".intent-cli", "queue-state.json"),
            RunsJsonlPresent = Present(".intent-cli", "runs.jsonl"),
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);
        Assert.Equal(IntentHostCheckAnalyzer.ClassificationOk, result.Classification);
        Assert.True(result.Ok);
    }

    [Fact]
    public void Execute_G441_DurableStateSkeleton_IsIdempotent_NotOverwritten()
    {
        using var tempDirectory = new TemporaryDirectory();
        var hostRoot = tempDirectory.CreateDirectory("host");
        var queueStatePath = Path.Combine(hostRoot, ".intent-cli", "queue-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        const string existing = "{\"schema_version\":\"1\",\"updated_at\":\"2026-01-01T00:00:00+00:00\",\"items\":[]}";
        File.WriteAllText(queueStatePath, existing);

        using var writer = new StringWriter();
        IntentInitCommand.Execute(
            CreateContext(hostRoot),
            ["--domain", "demo", "--write"],
            writer);

        // An existing queue-state (e.g. an active host) must be preserved.
        Assert.Equal(existing, File.ReadAllText(queueStatePath));
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
                    Domain = "bootstrap",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intent-init-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
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
