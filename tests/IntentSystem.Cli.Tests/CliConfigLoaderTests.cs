using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

public sealed class CliConfigLoaderTests
{
    [Fact]
    public void Load_GivenCanonicalRootKeys_RestoresRuntimeConfig()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"
        worktree_root = ".intent-cli/worktrees"
        work_repo_path = "../Sekiban-dcb/dcb"
        parent_intent_repo_root = "../MyIntentHost"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("intent-cli", config.Project.Domain);
        Assert.Equal(".intent-cli", config.Project.ArtifactRoot);
        Assert.Equal(".intent-cli/worktrees", config.Project.WorktreeRoot);
        Assert.Equal("../Sekiban-dcb/dcb", config.Project.WorkRepoPath);
        Assert.Equal("../MyIntentHost", config.Project.ParentIntentRepoRoot);
        Assert.Equal("Claude", config.Roles.Implement);
        Assert.Equal("Codex", config.Roles.Review);
        Assert.Equal(".intent-cli/supervision", config.Supervision.ArtifactRoot);
        Assert.Equal(15, config.Supervision.StaleHeartbeatTimeoutMinutes);
        Assert.Equal(5, config.Supervision.RetryDelayMinutes);
        Assert.Equal(3, config.Supervision.RetryBudget);
        Assert.Equal(".intent-cli/runs", config.DirectRun.ArtifactRoot);
        Assert.Equal(string.Empty, config.DirectRun.Provider);
        Assert.Equal("default", config.DirectRun.Model);
        Assert.Equal("stdio", config.DirectRun.Transport);
        Assert.Equal(string.Empty, config.DirectRun.Command);
        Assert.Empty(config.DirectRun.Args);
    }

    [Fact]
    public void LoadFromFile_GivenConfigTomlPath_RestoresProjectSettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var configPath = tempDirectory.CreateFile(
            ".intent-cli/config.toml",
            """
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"
            worktree_root = ".intent-cli/worktrees"
            work_repo_path = "../Sekiban-dcb/dcb"
            parent_intent_repo_root = "../MyIntentHost"
            """);

        var config = CliConfigLoader.LoadFromFile(configPath);

        Assert.Equal("intent-cli", config.Project.Domain);
        Assert.Equal(".intent-cli", config.Project.ArtifactRoot);
        Assert.Equal(".intent-cli/worktrees", config.Project.WorktreeRoot);
        Assert.Equal("../Sekiban-dcb/dcb", config.Project.WorkRepoPath);
        Assert.Equal("../MyIntentHost", config.Project.ParentIntentRepoRoot);
        Assert.Equal("Claude", config.Roles.Implement);
    }

    [Fact]
    public void Load_GivenLegacyProjectSection_StillSupportsFutureRicherShape()
    {
        var toml = """
        [project]
        domain = "intent-cli"
        artifact_root = ".intent-cli"
        worktree_root = ".intent-cli/worktrees"
        work_repo_path = "../Sekiban-dcb/dcb"
        parent_intent_repo_root = "../MyIntentHost"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("intent-cli", config.Project.Domain);
        Assert.Equal(".intent-cli", config.Project.ArtifactRoot);
        Assert.Equal(".intent-cli/worktrees", config.Project.WorktreeRoot);
        Assert.Equal("../Sekiban-dcb/dcb", config.Project.WorkRepoPath);
        Assert.Equal("../MyIntentHost", config.Project.ParentIntentRepoRoot);
        Assert.Equal("Claude", config.Roles.Implement);
    }

    [Fact]
    public void Load_GivenRolesSection_RestoresRoleMappings()
    {
        var toml = """
        default_domain = "intent-cli"
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
    public void Load_GivenRunSection_RestoresPostFixWorktreeProgressPolicy()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"

        [run]
        post_fix_worktree_progress_policy = "auto-continue"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("auto-continue", config.Run.PostFixWorktreeProgressPolicy);
    }

    [Fact]
    public void Load_GivenDirectBackendSection_RestoresDefaultAndEntrySpecificPolicies()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"

        [direct_backend]
        artifact_root = ".intent-cli/runtime-runs"
        provider = "Codex"
        model = "gpt-5.4"
        transport = "stdio"
        command = "codex"
        args = ["exec", "--model", "{model}", "{prompt}"]

        [direct_backend.implement]
        model = "gpt-5.4-codex"
        command = "codex-experimental"
        args = ["run", "--input", "{request_artifact_path}"]

        [direct_backend.fix]
        provider = "Claude"
        transport = "http"
        command = "claude"

        [direct_backend.review]
        provider = "ReviewBot"
        model = "gpt-5.4-mini"
        transport = "grpc"
        command = "reviewbot"
        args = ["launch", "--model", "{model}", "--artifact", "{request_artifact_path}"]
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal(".intent-cli/runtime-runs", config.DirectRun.ArtifactRoot);
        Assert.Equal("Codex", config.DirectRun.Provider);
        Assert.Equal("gpt-5.4", config.DirectRun.Model);
        Assert.Equal("stdio", config.DirectRun.Transport);
        Assert.Equal("codex", config.DirectRun.Command);
        Assert.Equal(["exec", "--model", "{model}", "{prompt}"], config.DirectRun.Args);
        Assert.Equal(string.Empty, config.DirectRun.Implement.Provider);
        Assert.Equal("gpt-5.4-codex", config.DirectRun.Implement.Model);
        Assert.Equal(string.Empty, config.DirectRun.Implement.Transport);
        Assert.Equal("codex-experimental", config.DirectRun.Implement.Command);
        Assert.Equal(["run", "--input", "{request_artifact_path}"], config.DirectRun.Implement.Args);
        Assert.Equal("Claude", config.DirectRun.Fix.Provider);
        Assert.Equal(string.Empty, config.DirectRun.Fix.Model);
        Assert.Equal("http", config.DirectRun.Fix.Transport);
        Assert.Equal("claude", config.DirectRun.Fix.Command);
        Assert.Empty(config.DirectRun.Fix.Args);
        Assert.Equal("ReviewBot", config.DirectRun.Review.Provider);
        Assert.Equal("gpt-5.4-mini", config.DirectRun.Review.Model);
        Assert.Equal("grpc", config.DirectRun.Review.Transport);
        Assert.Equal("reviewbot", config.DirectRun.Review.Command);
        Assert.Equal(["launch", "--model", "{model}", "--artifact", "{request_artifact_path}"], config.DirectRun.Review.Args);
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
        """;

        Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(toml));
    }

    [Fact]
    public void Load_GivenObsoleteWorkflowEngineKey_ThrowsInvalidOperationException()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"
        workflow_engine = "takt"
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(toml));

        Assert.Contains("workflow_engine", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_GivenInvalidPostFixWorktreeProgressPolicy_ThrowsInvalidOperationException()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"

        [run]
        post_fix_worktree_progress_policy = "always"
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(toml));

        Assert.Contains("post_fix_worktree_progress_policy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_BaseBranchPolicy_DefaultsToDirectMain_WhenAbsent()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("direct-main", config.Project.BaseBranchPolicy);
    }

    [Fact]
    public void Load_BaseBranchPolicy_AcceptsMainAi_AtRoot()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"
        base_branch_policy = "main-ai"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("main-ai", config.Project.BaseBranchPolicy);
    }

    [Fact]
    public void Load_BaseBranchPolicy_AcceptsMainAi_InProjectSection()
    {
        var toml = """
        [project]
        domain = "intent-cli"
        artifact_root = ".intent-cli"
        base_branch_policy = "main-ai"
        """;

        var config = CliConfigLoader.Load(toml);

        Assert.Equal("main-ai", config.Project.BaseBranchPolicy);
    }

    [Fact]
    public void Load_BaseBranchPolicy_RejectsUnknownValue()
    {
        var toml = """
        default_domain = "intent-cli"
        artifact_root = ".intent-cli"
        base_branch_policy = "trunk"
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load(toml));
        Assert.Contains("base_branch_policy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("trunk", exception.Message, StringComparison.Ordinal);
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
