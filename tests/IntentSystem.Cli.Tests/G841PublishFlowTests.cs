using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>G841 AC14a/14b: publish-flow packet parse/read refusal and snapshot races.</summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G841PublishFlowTests : IDisposable
{
    private const string Unit = "G841PF";
    private const string Title = "G841PF Real title";
    private const string UngatedRepo = "J-Tech-Japan/other";
    private const string UndeclaredTeam = "other-team";

    private readonly ThrowingIssueCreator throwingCreator = new();
    private readonly StubExistingIssueChecker defaultChecker =
        new(GitHubExistingIssueClassification.None);

    private readonly string root = Directory.CreateTempSubdirectory("g841-publish-flow-").FullName;

    public G841PublishFlowTests()
    {
        IssuePublishFlowCommand.CreatorFactory = () => throwingCreator;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.AfterGateHook = null;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = null;
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        G841TestHelpers.WriteHostConfig(root);
        G841TestHelpers.WriteClaim(root, Unit, G841TestHelpers.Team);
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.AfterGateHook = null;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = null;
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── AC14a live path ────────────────────────────────────────────────

    [Fact]
    public void PublishFlow_Ungated_UnparseablePacket_RefusesWithPacketYamlUnparseable()
    {
        WriteUngatedPacket(
            """
            implementation_issue_packet:
              issue_title: "G841PF Real title"
              domain: intent-cli
              domain: other-domain
              target_repo: J-Tech-Japan/other
            """);
        WriteBorrowedGithubBody();

        var (exit, output) = RunUngated(false);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.TryGetProperty("cause", out var causeNode), output);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, causeNode.GetString());
        Assert.False(json.RootElement.TryGetProperty("title", out var title) && title.ValueKind is not JsonValueKind.Null);
        Assert.False(json.RootElement.TryGetProperty("title_source", out var titleSource) && titleSource.ValueKind is not JsonValueKind.Null);
        Assert.False(json.RootElement.GetProperty("created").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("cross_runtime_design_review", out _));
    }

    [Fact]
    public void PublishFlow_Ungated_UnreadablePacket_RefusesWithPacketYamlUnreadable_G841Ac14a()
    {
        WriteUngatedPacket(G841TestHelpers.LegacyEquivalentPacket(extra: $"  target_repo: {UngatedRepo}\n"));
        WriteBorrowedGithubBody();
        var packetPath = G841TestHelpers.PacketPath(root, Unit);
        using var unreadable = G841TestHelpers.UnreadablePacket(
            packetPath,
            ArmDeniedPacketReader,
            DisarmDeniedPacketReader,
            out _);

        var (exit, output) = RunUngated(false);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnreadable, json.RootElement.GetProperty("cause").GetString());
        Assert.False(json.RootElement.TryGetProperty("cross_runtime_design_review", out _));
        Assert.False(json.RootElement.GetProperty("created").GetBoolean());
    }

    [Fact]
    public void PublishFlow_Ungated_QuotedHashPacket_UsesPacketYamlTitle()
    {
        WriteUngatedPacket(
            """
            implementation_issue_packet:
              issue_title: "G841PF Real title"
              domain: intent-cli
              target_repo: J-Tech-Japan/other
              source_artifact: "review of PR #1823"
            """);
        WriteContractBody();

        var (exit, output) = RunUngated(false);
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(IssuePublishFlowCommand.TitleSourcePacketYaml, json.RootElement.GetProperty("title_source").GetString());
    }

    [Fact]
    public void PublishFlow_Gated_UnparseablePacket_RefusesWithBlockedDesignReview_G841Ac14a()
    {
        using var workspace = new G841PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, G841TestHelpers.Repo, yaml: G841TestHelpers.UnparseableYaml);
        workspace.WriteIncompleteGithubBody();

        var (exit, output) = Run(workspace, Unit, G841TestHelpers.Repo, write: false);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, json.RootElement.GetProperty("cause").GetString());
        var review = json.RootElement.GetProperty("cross_runtime_design_review");
        Assert.Equal(CrossRuntimeReviewGate.DecisionBlocked, review.GetProperty("decision").GetString());
        Assert.Equal(
            CrossRuntimeReviewCauses.PacketInvalid,
            review.GetProperty("reasons")[0].GetProperty("cause").GetString());
        Assert.False(json.RootElement.GetProperty("created").GetBoolean());
    }

    [Fact]
    public void PublishFlow_Gated_UnreadablePacket_RefusesWithPacketUnreadableCause_G841Ac14a()
    {
        using var workspace = new G841PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, G841TestHelpers.Repo);
        var packetPath = workspace.PacketYamlPath(Unit);
        using var unreadable = G841TestHelpers.UnreadablePacket(
            packetPath,
            ArmDeniedPacketReader,
            DisarmDeniedPacketReader,
            out _);

        var (exit, output) = Run(workspace, Unit, G841TestHelpers.Repo, write: false);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnreadable, json.RootElement.GetProperty("cause").GetString());
        var review = json.RootElement.GetProperty("cross_runtime_design_review");
        Assert.Equal(CrossRuntimeReviewGate.DecisionBlocked, review.GetProperty("decision").GetString());
        Assert.Equal(
            CrossRuntimeReviewCauses.PacketUnreadable,
            review.GetProperty("reasons")[0].GetProperty("cause").GetString());
    }

    [Fact]
    public void PublishFlow_Gated_ReadinessWouldFail_StillShowsBlockedDesignReview_G841Ac14a()
    {
        using var workspace = new G841PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, G841TestHelpers.Repo, yaml: G841TestHelpers.UnparseableYaml);
        workspace.WriteIncompleteGithubBody();

        var (exit, output) = Run(workspace, Unit, G841TestHelpers.Repo, write: false);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.TryGetProperty("cross_runtime_design_review", out var reviewNode), output);
        Assert.Equal(CrossRuntimeReviewGate.DecisionBlocked, reviewNode.GetProperty("decision").GetString());
        Assert.Equal(
            CrossRuntimeReviewCauses.PacketInvalid,
            reviewNode.GetProperty("reasons")[0].GetProperty("cause").GetString());
    }

    [Fact]
    public void PublishFlow_Gated_AlreadyPublished_BrokenPacket_ShowsBlockedNotAbsent_G841Ac14a()
    {
        using var workspace = new G841PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, G841TestHelpers.Repo, yaml: G841TestHelpers.UnparseableYaml);
        workspace.WriteIncompleteGithubBody();
        workspace.SeedQueueStateWithLinkedIssue(
            Unit,
            Title,
            G841TestHelpers.Repo,
            841,
            $"https://github.com/{G841TestHelpers.Repo}/issues/841");

        var (exit, output) = Run(workspace, Unit, G841TestHelpers.Repo, write: false);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.TryGetProperty("cross_runtime_design_review", out var reviewNode), output);
        Assert.Equal(CrossRuntimeReviewGate.DecisionBlocked, reviewNode.GetProperty("decision").GetString());
        Assert.NotEqual(CrossRuntimeReviewGate.DecisionIdempotentNotGated, reviewNode.GetProperty("decision").GetString());
    }

    [Fact]
    public void PublishFlow_Gated_TeamUnresolved_BuildResolutionRefusalField_ByteIdentical_G841Ac14a()
    {
        using var workspace = new G841PublishFlowWorkspace(declare: true, heldTeam: null);
        workspace.WriteFullPacket(Unit, G841TestHelpers.Repo);
        workspace.SeedQueueState(Unit, Title);

        var dryRun = NormalizeDesignReview(Run(workspace, Unit, G841TestHelpers.Repo, write: false));
        workspace.CaptureDurableBaseline();
        var writeRun = NormalizeDesignReview(Run(workspace, Unit, G841TestHelpers.Repo, write: true));
        Assert.Equal(1, writeRun.ExitCode);
        AssertZeroCreates();
        workspace.AssertDurableBaselineUntouched(Unit);

        Assert.Equal(dryRun.DesignReviewJson, writeRun.DesignReviewJson);
        using var review = JsonDocument.Parse(dryRun.DesignReviewJson!);
        Assert.Equal(CrossRuntimeReviewGate.DecisionBlocked, review.RootElement.GetProperty("decision").GetString());
        Assert.Equal(
            G841TestHelpers.BaseTeamUnresolvedCause,
            review.RootElement.GetProperty("reasons")[0].GetProperty("cause").GetString());
    }

    [Fact]
    public void PublishFlow_MissingPacketYaml_UsesGithubBodyH1_G841Ac14a()
    {
        using var workspace = new G841PublishFlowWorkspace(declare: true);
        workspace.WriteBodyOnlyPacket(Unit, Title);

        var (exit, output) = Run(workspace, Unit, G841TestHelpers.Repo, write: false);
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(IssuePublishFlowCommand.TitleSourceGithubBodyH1, json.RootElement.GetProperty("title_source").GetString());
        Assert.Equal(Title, json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void PublishFlow_GithubBodyH1_GatedAndUngated_ByteIdentical_G841Ac14a()
    {
        using var ungated = new G841PublishFlowWorkspace(declare: false);
        ungated.WriteBodyOnlyPacket(Unit, Title);
        var ungatedResult = NormalizePublishOutput(Run(ungated, Unit, UngatedRepo, write: false, team: G841TestHelpers.Team));

        using var gated = new G841PublishFlowWorkspace(declare: true, heldTeam: UndeclaredTeam);
        gated.WriteBodyOnlyPacket(Unit, Title);
        var gatedResult = NormalizePublishOutput(Run(gated, Unit, UngatedRepo, write: false, team: UndeclaredTeam));

        Assert.Equal(ungatedResult.Json, gatedResult.Json);
        Assert.Equal(IssuePublishFlowCommand.TitleSourceGithubBodyH1, JsonDocument.Parse(ungatedResult.Json).RootElement.GetProperty("title_source").GetString());
    }

    [Fact]
    public void PublishFlow_FallbackUntitled_GatedAndUngated_ByteIdentical_G841Ac14a()
    {
        using var ungated = new G841PublishFlowWorkspace(declare: false);
        ungated.WriteBodyOnlyPacket(Unit, bodyWithoutH1: true);
        var ungatedResult = NormalizePublishOutput(Run(ungated, Unit, UngatedRepo, write: false, team: G841TestHelpers.Team));

        using var gated = new G841PublishFlowWorkspace(declare: true, heldTeam: UndeclaredTeam);
        gated.WriteBodyOnlyPacket(Unit, bodyWithoutH1: true);
        var gatedResult = NormalizePublishOutput(Run(gated, Unit, UngatedRepo, write: false, team: UndeclaredTeam));

        Assert.Equal(ungatedResult.Json, gatedResult.Json);
        using var json = JsonDocument.Parse(ungatedResult.Json);
        Assert.Equal(IssuePublishFlowCommand.TitleSourceFallbackUntitled, json.RootElement.GetProperty("title_source").GetString());
        Assert.Equal($"{Unit} (untitled)", json.RootElement.GetProperty("title").GetString());
    }

    // ── AC14b snapshot path ────────────────────────────────────────────

    [Fact]
    public void PublishFlow_Gated_SnapshotUnparseable_RefusesAfterFirstRead_G841Ac14b()
    {
        using var workspace = new G841PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, G841TestHelpers.Repo);
        workspace.SeedQueueState(Unit, Title);
        workspace.CaptureDurableBaseline();
        var packetYamlPath = workspace.PacketYamlPath(Unit);
        var checker = new RecordingExistingIssueChecker(defaultChecker);
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = () =>
            File.WriteAllBytes(packetYamlPath, Encoding.UTF8.GetBytes(G841TestHelpers.UnparseableYaml));

        var (exit, output) = Run(workspace, Unit, G841TestHelpers.Repo, write: true);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, json.RootElement.GetProperty("cause").GetString());
        Assert.Contains("changed after it was first read", json.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(json.RootElement.GetProperty("created").GetBoolean());
        Assert.Equal(Title, json.RootElement.GetProperty("title").GetString());
        Assert.Equal(IssuePublishFlowCommand.TitleSourcePacketYaml, json.RootElement.GetProperty("title_source").GetString());
        AssertZeroCreates();
        Assert.Equal(0, checker.CallCount);
        workspace.AssertDurableBaselineUntouched(Unit);
    }

    [Fact]
    public void PublishFlow_Gated_SnapshotUnreadable_RefusesAfterFirstRead_G841Ac14b()
    {
        using var workspace = new G841PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, G841TestHelpers.Repo);
        workspace.SeedQueueState(Unit, Title);
        workspace.CaptureDurableBaseline();
        var checker = new RecordingExistingIssueChecker(defaultChecker);
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        PacketFileReader.ReadAllBytes = _ => throw new UnauthorizedAccessException("Access to the path is denied.");

        var (exit, output) = Run(workspace, Unit, G841TestHelpers.Repo, write: true);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnreadable, json.RootElement.GetProperty("cause").GetString());
        Assert.Contains("changed after it was first read", json.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(json.RootElement.GetProperty("created").GetBoolean());
        Assert.Equal(Title, json.RootElement.GetProperty("title").GetString());
        Assert.Equal(IssuePublishFlowCommand.TitleSourcePacketYaml, json.RootElement.GetProperty("title_source").GetString());
        AssertZeroCreates();
        Assert.Equal(0, checker.CallCount);
        workspace.AssertDurableBaselineUntouched(Unit);
    }

    [Fact]
    public void ResolveTitleWithSourceFromSnapshot_UnparseableBytes_DoesNotReturnGithubBodyH1_G841Ac14b()
    {
        var packetBytes = Encoding.UTF8.GetBytes(G841TestHelpers.UnparseableYaml);
        var bodyBytes = Encoding.UTF8.GetBytes(G841TestHelpers.MinimalContractBody("Borrowed H1"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            IssuePublishFlowCommand.ResolveTitleWithSourceFromSnapshot(Unit, packetBytes, bodyBytes));

        Assert.Contains("changed after it was first read", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuePublishFlowCommand.TitleSourceGithubBodyH1, exception.Message, StringComparison.Ordinal);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private (int ExitCode, string Output) RunUngated(bool write) =>
        Run(new G841PublishFlowWorkspace(root, declare: false), Unit, UngatedRepo, write, team: G841TestHelpers.Team);

    private static (int ExitCode, string Output) Run(
        G841PublishFlowWorkspace workspace,
        string unit,
        string repo,
        bool write,
        string? team = G841TestHelpers.Team)
    {
        var args = new List<string> { unit, "--repo", repo, "--domain", G841TestHelpers.Domain, "--format", "json" };
        if (team is not null)
        {
            args.AddRange(["--team", team]);
        }

        if (write)
        {
            args.Add("--write");
        }

        using var writer = new StringWriter();
        var exit = IssuePublishFlowCommand.Execute(workspace.Context, args.ToArray(), writer);
        return (exit, writer.ToString());
    }

    private static (int ExitCode, string? DesignReviewJson) NormalizeDesignReview((int ExitCode, string Output) result)
    {
        using var json = JsonDocument.Parse(result.Output);
        if (!json.RootElement.TryGetProperty("cross_runtime_design_review", out var review))
        {
            return (result.ExitCode, null);
        }

        return (result.ExitCode, review.GetRawText());
    }

    private static (int ExitCode, string Json) NormalizePublishOutput((int ExitCode, string Output) result)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(result.Output)!.AsObject();
        foreach (var path in new[]
        {
            "packet_directory", "github_body_path", "publish_yaml_path", "issue_url", "issue_number",
        })
        {
            if (node.ContainsKey(path))
            {
                node[path] = path == "issue_number" ? 0 : "<normalized>";
            }
        }

        if (node.TryGetPropertyValue("cross_runtime_design_review", out var review) && review is null)
        {
            node.Remove("cross_runtime_design_review");
        }

        return (result.ExitCode, node.ToJsonString());
    }

    private void AssertZeroCreates() => Assert.Equal(0, throwingCreator.CallCount);

    private void WriteUngatedPacket(string yaml) =>
        G841TestHelpers.WritePacketFiles(root, Unit, yaml, withContractBody: false);

    private void WriteBorrowedGithubBody() =>
        File.WriteAllText(
            Path.Combine(G841TestHelpers.PacketDir(root, Unit), "github-body.md"),
            G841TestHelpers.MinimalContractBody("Borrowed H1"));

    private void WriteContractBody() =>
        File.WriteAllText(
            Path.Combine(G841TestHelpers.PacketDir(root, Unit), "github-body.md"),
            G841TestHelpers.MinimalContractBody(Title));

    private sealed class G841PublishFlowWorkspace : IDisposable
    {
        private readonly string rootPath;
        private readonly bool ownsRoot;
        private byte[]? queueStateBaseline;
        private byte[]? runsBaseline;
        private IReadOnlyDictionary<string, byte[]>? claimsBaseline;
        private IReadOnlyDictionary<string, byte[]>? handoffBaseline;

        public G841PublishFlowWorkspace(bool declare, string? heldTeam = G841TestHelpers.Team)
            : this(Directory.CreateTempSubdirectory("g841-publish-flow-").FullName, declare, heldTeam, ownsRoot: true)
        {
        }

        public G841PublishFlowWorkspace(string root, bool declare, string? heldTeam = G841TestHelpers.Team)
            : this(root, declare, heldTeam, ownsRoot: false)
        {
        }

        private G841PublishFlowWorkspace(string root, bool declare, string? heldTeam, bool ownsRoot)
        {
            rootPath = root;
            this.ownsRoot = ownsRoot;
            G841TestHelpers.WriteHostConfig(rootPath);
            Context = declare
                ? G841TestHelpers.GatedContext(rootPath, heldTeam)
                : G841TestHelpers.UngatedContext(rootPath);
            if (heldTeam is not null)
            {
                G841TestHelpers.WriteClaim(rootPath, Unit, heldTeam);
            }
        }

        public CliContext Context { get; }

        public string QueueStatePath => Path.Combine(rootPath, ".intent-cli", "queue-state.json");

        public string RunsLogPath => Path.Combine(rootPath, ".intent-cli", "runs.jsonl");

        public string PacketYamlPath(string unit) => G841TestHelpers.PacketPath(rootPath, unit);

        public string PublishYamlPath(string unit) =>
            Path.Combine(rootPath, ".intent-cli", "issues", unit, "publish.yaml");

        public string PacketDirectory(string unit) => G841TestHelpers.PacketDir(rootPath, unit);

        public string ClaimsDirectory => Path.Combine(rootPath, ".intent-cli", "claims");

        public string HandoffDirectory => Path.Combine(rootPath, PublishedExternalHandoffStore.RecordRootRelativePath);

        public void CaptureDurableBaseline()
        {
            queueStateBaseline = File.Exists(QueueStatePath) ? File.ReadAllBytes(QueueStatePath) : Array.Empty<byte>();
            runsBaseline = File.Exists(RunsLogPath) ? File.ReadAllBytes(RunsLogPath) : Array.Empty<byte>();
            claimsBaseline = SnapshotDirectory(ClaimsDirectory);
            handoffBaseline = SnapshotDirectory(HandoffDirectory);
        }

        public void AssertDurableBaselineUntouched(string unit)
        {
            Assert.False(File.Exists(PublishYamlPath(unit)));
            var queueState = File.Exists(QueueStatePath) ? File.ReadAllBytes(QueueStatePath) : Array.Empty<byte>();
            Assert.Equal(queueStateBaseline ?? Array.Empty<byte>(), queueState);
            var runs = File.Exists(RunsLogPath) ? File.ReadAllBytes(RunsLogPath) : Array.Empty<byte>();
            Assert.Equal(runsBaseline ?? Array.Empty<byte>(), runs);
            Assert.Equal(claimsBaseline ?? new Dictionary<string, byte[]>(), SnapshotDirectory(ClaimsDirectory));
            Assert.Equal(handoffBaseline ?? new Dictionary<string, byte[]>(), SnapshotDirectory(HandoffDirectory));
        }

        public void WriteFullPacket(string unit, string targetRepo, string? yaml = null, string domain = G841TestHelpers.Domain)
        {
            G841TestHelpers.WritePacketFiles(
                rootPath,
                unit,
                yaml ?? $"""
                    implementation_issue_packet:
                      issue_title: "{Title}"
                      domain: {domain}
                      target_repo: {targetRepo}
                    """,
                bodyTitle: Title);
        }

        public void WriteBodyOnlyPacket(string unit, string? title = null, bool bodyWithoutH1 = false)
        {
            var directory = PacketDirectory(unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
            File.WriteAllText(
                Path.Combine(directory, "github-body.md"),
                bodyWithoutH1
                    ? """
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
                      """
                    : G841TestHelpers.MinimalContractBody(title ?? Title));
        }

        public void WriteIncompleteGithubBody()
        {
            File.WriteAllText(
                Path.Combine(PacketDirectory(Unit), "github-body.md"),
                """
                # Incomplete

                ## Goal
                x
                """);
        }

        public void SeedQueueState(string unit, string title) => WriteQueueStateForUnit(unit, title, linkedIssue: null);

        public void SeedQueueStateWithLinkedIssue(string unit, string title, string repo, int issueNumber, string issueUrl) =>
            WriteQueueStateForUnit(unit, title, new LinkedIssue { Repo = repo, Number = issueNumber, Url = issueUrl });

        private void WriteQueueStateForUnit(string unit, string title, LinkedIssue? linkedIssue)
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            var state = new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 5, 6, 0, 0, 0, TimeSpan.Zero),
                Items =
                [
                    new QueueItem
                    {
                        ExecutionUnit = unit,
                        Title = title,
                        State = QueueItemState.Queued,
                        Dependencies = Array.Empty<string>(),
                        BlockedBy = Array.Empty<string>(),
                        ClarificationReturnPath = string.Empty,
                        PacketPaths = new PacketPaths
                        {
                            Implementation = $".intent-cli/issues/{unit}/implementation.md",
                            ReviewContext = $".intent-cli/issues/{unit}/review-context.md",
                            Yaml = $".intent-cli/issues/{unit}/packet.yaml",
                        },
                        LinkedIssue = linkedIssue,
                        WorkerRole = "child-impl",
                        ReviewRole = "host-review",
                        Priority = "normal",
                    },
                ],
            };
            File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(state));
        }

        public void Dispose()
        {
            if (ownsRoot && Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }

        private static Dictionary<string, byte[]> SnapshotDirectory(string directory)
        {
            var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (!Directory.Exists(directory))
            {
                return snapshot;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            {
                snapshot[path] = File.ReadAllBytes(path);
            }

            return snapshot;
        }
    }

    private sealed class ThrowingIssueCreator : IIssueCreator
    {
        public int CallCount { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            CallCount++;
            throw new InvalidOperationException("must not create");
        }
    }

    private sealed class StubExistingIssueChecker(GitHubExistingIssueClassification classification) : IGitHubExistingIssueChecker
    {
        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string title, string body) =>
            new() { Classification = classification };
    }

    private sealed class RecordingExistingIssueChecker(StubExistingIssueChecker inner) : IGitHubExistingIssueChecker
    {
        public int CallCount { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            CallCount++;
            return inner.FindExistingIssue(repo, executionUnit, expectedTitle, expectedBody);
        }
    }

    private static void ArmDeniedPacketReader()
    {
        PacketFileReader.ReadAllText = _ => throw new UnauthorizedAccessException("Access to the path is denied.");
        PacketFileReader.ReadAllBytes = _ => throw new UnauthorizedAccessException("Access to the path is denied.");
    }

    private static void DisarmDeniedPacketReader()
    {
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
    }
}
