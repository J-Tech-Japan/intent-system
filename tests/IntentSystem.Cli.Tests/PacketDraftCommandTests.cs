using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class PacketDraftCommandTests
{
    [Fact]
    public void Execute_GivenWriteMode_CreatesAllFourPacketFilesWithRequiredHeadings()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Packet draft — G244", output, StringComparison.Ordinal);
        Assert.Contains("mode: write", output, StringComparison.Ordinal);
        Assert.Contains("packet.yaml: created", output, StringComparison.Ordinal);
        Assert.Contains("implementation.md: created", output, StringComparison.Ordinal);
        Assert.Contains("review-context.md: created", output, StringComparison.Ordinal);
        Assert.Contains("github-body.md: created", output, StringComparison.Ordinal);
        Assert.Contains("missing sections: none", output, StringComparison.Ordinal);

        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G244");
        Assert.True(File.Exists(Path.Combine(packetDir, "packet.yaml")));
        Assert.True(File.Exists(Path.Combine(packetDir, "implementation.md")));
        Assert.True(File.Exists(Path.Combine(packetDir, "review-context.md")));
        var githubBody = File.ReadAllText(Path.Combine(packetDir, "github-body.md"));
        foreach (var section in PacketDraftCommand.RequiredContractSections)
        {
            Assert.Contains($"## {section}", githubBody, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_GivenExistingFiles_DoesNotOverwriteAndReportsSkipped()
    {
        using var workspace = new PacketDraftWorkspace();
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G244");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "packet.yaml"), "existing content");

        using var writer = new StringWriter();
        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("packet.yaml: skipped", output, StringComparison.Ordinal);
        Assert.Equal("existing content", File.ReadAllText(Path.Combine(packetDir, "packet.yaml")));
    }

    [Fact]
    public void Execute_GivenDryRun_DoesNotWriteFilesAndReportsPlanned()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        var files = root.GetProperty("files");
        Assert.Equal(4, files.GetArrayLength());
        foreach (var file in files.EnumerateArray())
        {
            Assert.Equal("planned", file.GetProperty("status").GetString());
        }

        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G244");
        Assert.False(Directory.Exists(packetDir));
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsStructuredResult()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G244", root.GetProperty("execution_unit").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.Equal(0, root.GetProperty("missing_contract_sections").GetArrayLength());
    }

    [Fact]
    public void Execute_MissingExecutionUnit_ReturnsUsageError()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--execution-unit is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidExecutionUnitId_ReturnsUsageError()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "bad/id"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid execution-unit id", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G244", "--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("packet draft", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("--execution-unit", writer.ToString(), StringComparison.Ordinal);
    }

    // ----- G347: Base Branch Policy section in generated github-body.md -----

    [Fact]
    public void Execute_G347_GeneratedGithubBodyIncludesBaseBranchPolicySection()
    {
        // G347 AC: the generated github-body.md must contain a non-empty
        // `## Base Branch Policy` section with a parseable
        // `Expected PR base branch:` line. This locks the section shape so
        // downstream consumers (worker complete G347 check) can always parse it.
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G347", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G347");
        var githubBody = File.ReadAllText(Path.Combine(packetDir, "github-body.md"));

        Assert.Contains("## Base Branch Policy", githubBody, StringComparison.Ordinal);
        Assert.Contains("Expected PR base branch:", githubBody, StringComparison.Ordinal);

        // The parser used by worker-complete must be able to extract the value.
        var extracted = WorkerCompleteCommand.ParseExpectedBaseBranchFromIssueBody(githubBody);
        Assert.NotNull(extracted);
        Assert.False(string.IsNullOrWhiteSpace(extracted));
    }

    [Fact]
    public void Execute_G347_GeneratedGithubBodyContainsDirectMainPolicyByDefault()
    {
        // G347 AC: when the project config has no BaseBranchPolicy the default
        // (`direct-main` → `main`) is stamped into the generated body.
        using var workspace = new PacketDraftWorkspace();
        using var writer = new StringWriter();

        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G347B"],
            writer);

        Assert.Equal(0, exitCode);
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G347B");
        var githubBody = File.ReadAllText(Path.Combine(packetDir, "github-body.md"));

        Assert.Contains("Policy: `direct-main`", githubBody, StringComparison.Ordinal);
        Assert.Contains("Expected PR base branch: `main`", githubBody, StringComparison.Ordinal);
    }

    private sealed class PacketDraftWorkspace : IDisposable
    {
        public PacketDraftWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("packet-draft-tests-").FullName;
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

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot))
            {
                Directory.Delete(RepoRoot, recursive: true);
            }
        }
    }
}
