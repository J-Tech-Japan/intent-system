using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IssuePublishFlowCommandTests : IDisposable
{
    public IssuePublishFlowCommandTests()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
    }

    [Fact]
    public void Execute_GivenCompletePacketDryRun_ReportsValidationOk()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245", BuildCompleteContractBody("G245 Add intent-cli issue publish-flow command"));

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("github_body_present").GetBoolean());
        Assert.Equal(0, root.GetProperty("missing_contract_sections").GetArrayLength());
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.False(root.GetProperty("intent_target_applied").GetBoolean());
        Assert.Equal("G245 Add intent-cli issue publish-flow command", root.GetProperty("title").GetString());
    }

    [Fact]
    public void Execute_GivenIncompleteContract_ReportsMissingSectionsAndExitsNonZero()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245",
            """
            # G245 short body

            ## Goal
            x

            ## In Scope
            - x
            """);

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var missing = root.GetProperty("missing_contract_sections");
        Assert.True(missing.GetArrayLength() > 0);
        var names = missing.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Verification", names);
        Assert.Contains("Acceptance Criteria", names);
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.Contains("incomplete", root.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPacketDirectory_ReportsErrorAndExitsNonZero()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G999", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("packet directory not found", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenWriteWithCompletePacket_CreatesIssueAndReportsNextSteps()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245", BuildCompleteContractBody("G245 Add intent-cli issue publish-flow command"));

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/593");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("created").GetBoolean());
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/593", root.GetProperty("issue_url").GetString());
        Assert.False(root.GetProperty("intent_target_applied").GetBoolean());

        var nextSteps = root.GetProperty("next_steps");
        Assert.True(nextSteps.GetArrayLength() >= 2);
        var stepText = string.Join('|', nextSteps.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("automation issue-publish", stepText, StringComparison.Ordinal);

        Assert.Equal("J-Tech-Japan/intent-system", stub.LastRepo);
        Assert.Equal("G245 Add intent-cli issue publish-flow command", stub.LastTitle);
        Assert.EndsWith("github-body.md", stub.LastBodyFile!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenCreatorFailure_ReportsErrorAndExitsNonZero()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245", BuildCompleteContractBody("G245 Add intent-cli issue publish-flow command"));

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("gh issue create failed", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.False(document.RootElement.GetProperty("created").GetBoolean());
    }

    [Fact]
    public void Execute_MissingExecutionUnit_ReturnsUsageError()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("execution-unit id is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidExecutionUnitId_ReturnsUsageError()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["bad/id", "--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid execution-unit id", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("issue publish-flow", writer.ToString(), StringComparison.Ordinal);
    }

    private static string BuildCompleteContractBody(string title)
    {
        return $"""
            # {title}

            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            - x

            ## Out Of Scope
            - x

            ## Acceptance Criteria
            - x

            ## Verification
            x

            ## Related Links
            - x
            """;
    }

    private sealed class StubIssueCreator : IIssueCreator
    {
        private readonly string url;

        public StubIssueCreator(string url)
        {
            this.url = url;
        }

        public string? LastRepo { get; private set; }

        public string? LastTitle { get; private set; }

        public string? LastBodyFile { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            LastRepo = repo;
            LastTitle = title;
            LastBodyFile = bodyFilePath;
            return new IssueCreateOutcome(url);
        }
    }

    private sealed class ThrowingIssueCreator : IIssueCreator
    {
        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            throw new InvalidOperationException("simulated gh failure");
        }
    }

    private sealed class IssuePublishFlowWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("issue-publish-flow-tests-")
            .FullName;

        public IssuePublishFlowWorkspace()
        {
            Context = new CliContext
            {
                RepoRoot = rootPath,
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

        public CliContext Context { get; }

        public void WriteGithubBody(string executionUnit, string content)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "github-body.md"), content);
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
