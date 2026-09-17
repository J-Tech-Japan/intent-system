using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class G841PublishFlowTests : IDisposable
{
    private const string Unit = "G841PF";
    private const string Repo = "J-Tech-Japan/other";
    private readonly string root = Directory.CreateTempSubdirectory("g841-publish-flow-").FullName;

    public G841PublishFlowTests()
    {
        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"), "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
        WriteClaim(Unit, "intent-cli-dev");
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PublishFlow_Ungated_UnparseablePacket_RefusesWithPacketYamlUnparseable()
    {
        WritePacket(
            """
            implementation_issue_packet:
              issue_title: "G841PF Real title"
              domain: intent-cli
              domain: other-domain
              target_repo: J-Tech-Japan/other
            """);
        File.WriteAllText(PacketPath("github-body.md"), "# Borrowed H1\n\n## Goal\nx\n\n## Why This Slice Exists Now\nx\n\n## Current Observed State\nx\n\n## Accepted Baseline You May Assume\nx\n\n## Target Repo / Path / Part\nx\n\n## In Scope\nx\n\n## Out Of Scope\nx\n\n## Acceptance Criteria\nx\n\n## Related Links\nx\n");

        var (exit, output) = Run(false);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.TryGetProperty("cause", out var causeNode), output);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, causeNode.GetString());
        Assert.False(json.RootElement.TryGetProperty("title", out var title) && title.ValueKind is not JsonValueKind.Null);
        Assert.False(json.RootElement.TryGetProperty("title_source", out var titleSource) && titleSource.ValueKind is not JsonValueKind.Null);
        Assert.False(json.RootElement.GetProperty("created").GetBoolean());
    }

    [Fact]
    public void PublishFlow_Ungated_QuotedHashPacket_UsesPacketYamlTitle()
    {
        WritePacket(
            """
            implementation_issue_packet:
              issue_title: "G841PF Real title"
              domain: intent-cli
              target_repo: J-Tech-Japan/other
              source_artifact: "review of PR #1823"
            """);
        WriteContractBody();

        var (exit, output) = Run(false);
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(IssuePublishFlowCommand.TitleSourcePacketYaml, json.RootElement.GetProperty("title_source").GetString());
    }

    private (int ExitCode, string Output) Run(bool write)
    {
        using var writer = new StringWriter();
        var args = new List<string> { Unit, "--repo", Repo, "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json" };
        if (write)
        {
            args.Add("--write");
        }

        var exit = IssuePublishFlowCommand.Execute(Context(), args.ToArray(), writer);
        return (exit, writer.ToString());
    }

    private CliContext Context() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            CrossRuntimeReview = new CrossRuntimeReviewConfig(),
        },
    };

    private string PacketDir() => Path.Combine(root, ".intent-cli", "issues", Unit);

    private string PacketPath(string name) => Path.Combine(PacketDir(), name);

    private void WritePacket(string yaml)
    {
        Directory.CreateDirectory(PacketDir());
        File.WriteAllText(PacketPath("packet.yaml"), yaml);
        File.WriteAllText(PacketPath("review-context.md"), "# review\n");
        File.WriteAllText(PacketPath("implementation.md"), "# impl\n");
    }

    private void WriteClaim(string unit, string team)
    {
        var claimPath = Path.Combine(root, ClaimCommand.ClaimPath($"execution-unit:{unit}").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
        File.WriteAllText(claimPath, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            scope = $"execution-unit:{unit}",
            actor = "design",
            team,
            claimed_at = DateTimeOffset.UtcNow,
            base_commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        }));
    }

    private void WriteContractBody()
    {
        File.WriteAllText(PacketPath("github-body.md"),
            """
            # G841PF Real title

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

            ## Base Branch Policy
            Policy: `direct-main`
            Expected PR base branch: `main`
            Open all child PRs against `main` directly.
            """);
    }

    private sealed class ThrowingIssueCreator : IIssueCreator
    {
        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath) =>
            throw new InvalidOperationException("must not create");
    }

    private sealed class StubExistingIssueChecker(GitHubExistingIssueClassification classification) : IGitHubExistingIssueChecker
    {
        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string title, string body) =>
            new() { Classification = classification };
    }
}
