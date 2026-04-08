using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

public sealed class CliConfigLoaderTests
{
    [Fact]
    public void Load_GivenCanonicalRootKeys_RestoresRuntimeConfig()
    {
        var toml = """
        default_domain = "intent-cli"
        workflow_engine = "takt"
        artifact_root = ".intent-cli"
        worktree_root = ".intent-cli/worktrees"
        parent_intent_repo_root = "../MyIntentHost"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("intent-cli", config.Project.Domain);
        Assert.Equal("takt", config.Project.WorkflowEngine);
        Assert.Equal(".intent-cli", config.Project.ArtifactRoot);
        Assert.Equal(".intent-cli/worktrees", config.Project.WorktreeRoot);
        Assert.Equal("../MyIntentHost", config.Project.ParentIntentRepoRoot);
        Assert.Equal("Claude", config.Roles.Implement);
        Assert.Equal("Codex", config.Roles.Review);
        Assert.Equal(".intent-cli/supervision", config.Supervision.ArtifactRoot);
        Assert.Equal(15, config.Supervision.StaleHeartbeatTimeoutMinutes);
        Assert.Equal(5, config.Supervision.RetryDelayMinutes);
        Assert.Equal(3, config.Supervision.RetryBudget);
    }

    [Fact]
    public void LoadFromFile_GivenConfigTomlPath_RestoresProjectSettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var configPath = tempDirectory.CreateFile(
            ".intent-cli/config.toml",
            """
            default_domain = "intent-cli"
            workflow_engine = "takt"
            artifact_root = ".intent-cli"
            worktree_root = ".intent-cli/worktrees"
            parent_intent_repo_root = "../MyIntentHost"
            """);

        var config = CliConfigLoader.LoadFromFile(configPath);

        Assert.Equal("intent-cli", config.Project.Domain);
        Assert.Equal("takt", config.Project.WorkflowEngine);
        Assert.Equal(".intent-cli", config.Project.ArtifactRoot);
        Assert.Equal(".intent-cli/worktrees", config.Project.WorktreeRoot);
        Assert.Equal("../MyIntentHost", config.Project.ParentIntentRepoRoot);
        Assert.Equal("Claude", config.Roles.Implement);
    }

    [Fact]
    public void Load_GivenLegacyProjectSection_StillSupportsFutureRicherShape()
    {
        var toml = """
        [project]
        domain = "intent-cli"
        workflow_engine = "takt"
        artifact_root = ".intent-cli"
        worktree_root = ".intent-cli/worktrees"
        parent_intent_repo_root = "../MyIntentHost"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("intent-cli", config.Project.Domain);
        Assert.Equal("takt", config.Project.WorkflowEngine);
        Assert.Equal(".intent-cli", config.Project.ArtifactRoot);
        Assert.Equal(".intent-cli/worktrees", config.Project.WorktreeRoot);
        Assert.Equal("../MyIntentHost", config.Project.ParentIntentRepoRoot);
        Assert.Equal("Claude", config.Roles.Implement);
    }

    [Fact]
    public void Load_GivenRolesSection_RestoresRoleMappings()
    {
        var toml = """
        default_domain = "intent-cli"
        workflow_engine = "takt"
        artifact_root = ".intent-cli"

        [roles]
        implement = "Codex"
        review = "Claude"
        interview = "Codex"
        clarify = "Claude"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("Codex", config.Roles.Implement);
        Assert.Equal("Claude", config.Roles.Review);
        Assert.Equal("Codex", config.Roles.Interview);
        Assert.Equal("Claude", config.Roles.Clarify);
    }

    [Fact]
    public void Load_GivenSupervisionSection_RestoresRetryPolicyAndArtifactRoot()
    {
        var toml = """
        default_domain = "intent-cli"
        workflow_engine = "takt"
        artifact_root = ".intent-cli"

        [supervision]
        artifact_root = ".intent-cli/runtime-supervision"
        stale_heartbeat_timeout_minutes = 30
        retry_delay_minutes = 12
        retry_budget = 7
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal(".intent-cli/runtime-supervision", config.Supervision.ArtifactRoot);
        Assert.Equal(30, config.Supervision.StaleHeartbeatTimeoutMinutes);
        Assert.Equal(12, config.Supervision.RetryDelayMinutes);
        Assert.Equal(7, config.Supervision.RetryBudget);
    }

    [Fact]
    public void Load_GivenMissingCanonicalRootKeys_ThrowsInvalidOperationException()
    {
        var toml = """
        [queue]
        file = ".intent-cli/queue-state.json"
        """;

        Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(toml));
    }

    [Fact]
    public void Load_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"
        """;

        Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(toml));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

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
