using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G835: <c>issue publish-flow</c> design-review gate on gated repositories
/// for declared teams; ungated repos and undeclared teams stay byte-identical.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G835PublishFlowTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G835PF";
    private const string OtherRepo = "J-Tech-Japan/other";
    private const string UndeclaredTeam = "other-team";

    private readonly ThrowingIssueCreator throwingCreator = new();
    private readonly StubExistingIssueChecker defaultChecker =
        new(GitHubExistingIssueClassification.None);

    public G835PublishFlowTests()
    {
        IssuePublishFlowCommand.CreatorFactory = () => throwingCreator;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.AfterGateHook = null;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.AfterGateHook = null;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
    }

    // ── byte identity ──────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishFlow_NoDeclaration_IsByteIdenticalToDeclaringHostWithUngatedRepo(bool write)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false);
        workspace.WriteMinimalPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        var baseline = NormalizePublishOutput(Run(workspace, Unit, OtherRepo, write));
        using var declaring = new G835PublishFlowWorkspace(declare: true);
        declaring.WriteMinimalPacket(Unit, Repo);
        declaring.SeedQueueState(Unit, Title());
        var gated = NormalizePublishOutput(Run(declaring, Unit, OtherRepo, write));

        Assert.Equal(baseline.Json, gated.Json);
        Assert.Equal(baseline.ExitCode, gated.ExitCode);
        Assert.DoesNotContain("cross_runtime_design_review", gated.Json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishFlow_GatedRepo_UndeclaredTeam_IsByteIdentical(bool write)
    {
        using var workspace = new G835PublishFlowWorkspace(declare: false, heldTeam: UndeclaredTeam);
        workspace.WriteMinimalPacket(Unit, Repo, targetRepoOverride: "submodules/intent-system");
        workspace.SeedQueueState(Unit, Title());
        var stub = new StubIssueCreator($"https://github.com/{Repo}/issues/8350");
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var baseline = NormalizePublishOutput(Run(workspace, Unit, Repo, write, team: UndeclaredTeam));
        using var gated = new G835PublishFlowWorkspace(declare: true, heldTeam: UndeclaredTeam);
        gated.WriteMinimalPacket(Unit, Repo, targetRepoOverride: "submodules/intent-system");
        gated.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;
        var declaring = NormalizePublishOutput(Run(gated, Unit, Repo, write, team: UndeclaredTeam));

        Assert.Equal(baseline.Json, declaring.Json);
        Assert.Equal(0, declaring.ExitCode);
    }

    // ── resolution ─────────────────────────────────────────────────────

    [Fact]
    public void PublishFlow_Resolution_UsesPacketDomain_NotDomainFlag()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo, domain: Domain);
        workspace.SeedQueueState(Unit, Title());

        var (exit, output) = Run(workspace, Unit, Repo, write: false, domainOverride: "sekiban");
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        var review = result.RootElement.GetProperty("cross_runtime_design_review");
        Assert.Equal("intent-cli-dev", review.GetProperty("team").GetString());
        Assert.Equal(Domain, review.GetProperty("domain").GetString());
    }

    [Fact]
    public void PublishFlow_DomainMismatch_RefusesWhenClaimTeamDeclaredUnderAnotherDomain()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, extraTeams:
        [
            new CrossRuntimeReviewTeamDeclaration
            {
                Team = "sekiban/sekiban-dev",
                ConductorRuntime = "codex",
                Repos = [Repo],
            },
        ]);
        workspace.WriteFullPacket(Unit, Repo, domain: "wrong-domain");
        workspace.SeedQueueState(Unit, Title());
        workspace.WriteClaim(Unit, "sekiban-dev");

        workspace.CaptureDurableBaseline();
        var (exit, output) = Run(workspace, Unit, Repo, write: true, team: "sekiban-dev");
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DomainMismatch, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_TargetRepoMismatch_RefusesForDeclaredTeam_NamesFix()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, "J-Tech-Japan/wrong");
        workspace.SeedQueueState(Unit, Title());

        workspace.CaptureDurableBaseline();
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.TargetRepoMismatch, result.RootElement.GetProperty("cause").GetString());
        Assert.Contains(Repo, result.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DryRun_TargetRepoMismatch_ReportsResolutionRefusal()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, "J-Tech-Japan/wrong");
        workspace.SeedQueueState(Unit, Title());

        var (exit, output) = Run(workspace, Unit, Repo, write: false);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        var review = result.RootElement.GetProperty("cross_runtime_design_review");
        Assert.Equal(CrossRuntimeReviewCauses.TargetRepoMismatch, review.GetProperty("reasons")[0].GetProperty("cause").GetString());
    }

    [Fact]
    public void PublishFlow_UndeclaredTeam_IncompleteTwoFilePacket_PublishesAsToday()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: UndeclaredTeam);
        workspace.WriteTwoFilePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/9001");
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var (exit, output) = Run(workspace, Unit, Repo, write: true, team: UndeclaredTeam);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("created").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("cross_runtime_design_review", out _));
    }

    [Fact]
    public void PublishFlow_TeamUnresolved_RefusesOnWrite()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: null);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        workspace.CaptureDurableBaseline();
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        Assert.Contains(CrossRuntimeReviewCauses.TeamUnresolved, output, StringComparison.Ordinal);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    // ── body swap seams ────────────────────────────────────────────────

    [Fact]
    public void PublishFlow_CreatorFactoryMutatesPacketDuringConstruction_RefusesDigestStale()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        workspace.CaptureDurableBaseline();
        var bodyPath = workspace.GithubBodyPath(Unit);
        IssuePublishFlowCommand.CreatorFactory = () =>
        {
            File.AppendAllText(bodyPath, "\nmutated-during-creator-factory\n");
            return throwingCreator;
        };

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DigestStale, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_BodySwap_AfterGate_RefusesDigestStale()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        workspace.CaptureDurableBaseline();
        IssuePublishFlowCommand.AfterGateHook = () =>
            File.AppendAllText(workspace.GithubBodyPath(Unit), "\n");

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DigestStale, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_PacketSwapDuringLookup_RefusesDigestStale()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo, bodyTitle: Title());
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        workspace.CaptureDurableBaseline();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            new PacketSwapDuringLookupChecker(workspace, Unit, Repo, Title("B"));

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DigestStale, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_BodySwap_BetweenLookupAndRecovery_RefusesLookupInputChanged()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        var bodyPath = workspace.GithubBodyPath(Unit);
        workspace.CaptureDurableBaseline();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new MutatingExistingIssueChecker(bodyPath);

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.LookupInputChanged, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    // ── lookup and recovery ────────────────────────────────────────────

    [Fact]
    public void PublishFlow_Lookup_ReceivesInMemoryTitleAndBody()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo, bodyTitle: Title());
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var packetYamlPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
        var bodyPath = workspace.GithubBodyPath(Unit);
        var checker = new PostSnapshotMutatingChecker(
            packetYamlPath,
            bodyPath,
            GitHubExistingIssueClassification.None);
        workspace.CaptureDurableBaseline();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.DigestStale, result.RootElement.GetProperty("cause").GetString());

        Assert.Equal(1, checker.CallCount);
        Assert.Equal(IssuePublishFlowCommand.FormatIssueTitle(Unit, Title()), checker.LastTitle);
        Assert.DoesNotContain("mutated-on-disk", checker.LastBody, StringComparison.Ordinal);
        Assert.Contains("## Goal", checker.LastBody, StringComparison.Ordinal);
        Assert.Contains("mutated-on-disk", File.ReadAllText(packetYamlPath), StringComparison.Ordinal);
        Assert.Contains("mutated-on-disk", File.ReadAllText(bodyPath), StringComparison.Ordinal);
    }

    [Fact]
    public void PublishFlow_ExistingMarker_ShortCircuits_EvenWhenResolutionWouldFail()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: null);
        workspace.WriteTwoFilePacket(Unit, Repo, targetRepoOverride: "wrong/repo");
        workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), Repo, 42, $"https://github.com/{Repo}/issues/42");

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("idempotent").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("cross_runtime_design_review", out _));
    }

    [Fact]
    public void PublishFlow_ExactlyOneRecovery_ProceedsWithoutReviewGate_ForTwoFilePacket()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteTwoFilePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            new StubExistingIssueChecker(GitHubExistingIssueClassification.Unique, 55, $"https://github.com/{Repo}/issues/55");
        IssuePublishFlowCommand.CreatorFactory = () => throwingCreator;

        var queueBefore = File.ReadAllBytes(workspace.QueueStatePath);
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
        Assert.NotEqual(queueBefore, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void PublishFlow_ExactlyOneRecovery_ProceedsWhenResolutionWouldFail()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: null);
        workspace.WriteTwoFilePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            new StubExistingIssueChecker(GitHubExistingIssueClassification.Unique, 88, $"https://github.com/{Repo}/issues/88");
        IssuePublishFlowCommand.CreatorFactory = () => throwingCreator;

        var queueBefore = File.ReadAllBytes(workspace.QueueStatePath);
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(0, throwingCreator.CallCount);
        Assert.NotEqual(queueBefore, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void PublishFlow_DryRun_ReportsGateDecision_ForDeclaredTeam()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        var (exit, output) = Run(workspace, Unit, Repo, write: false);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        var review = result.RootElement.GetProperty("cross_runtime_design_review");
        Assert.Equal("missing", review.GetProperty("decision").GetString());
        Assert.NotNull(review.GetProperty("digest").GetString());
    }

    [Fact]
    public void PublishFlow_DryRun_IdempotentNotGated_WhenIssueAlreadyExists_ForDeclaredTeamOnly()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueStateWithLinkedIssue(Unit, Title(), Repo, 99, $"https://github.com/{Repo}/issues/99");

        var (exit, output) = Run(workspace, Unit, Repo, write: false);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("idempotent-not-gated", result.RootElement.GetProperty("cross_runtime_design_review").GetProperty("decision").GetString());
    }

    [Fact]
    public void PublishFlow_DryRun_UndeclaredTeam_EmitsNoDesignReviewField()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true, heldTeam: UndeclaredTeam);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        var (exit, output) = Run(workspace, Unit, Repo, write: false, team: UndeclaredTeam);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.TryGetProperty("cross_runtime_design_review", out _));
    }

    // ── gate refusals and success ──────────────────────────────────────

    [Fact]
    public void PublishFlow_DeclaredTeam_GateMissing_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());

        workspace.CaptureDurableBaseline();
        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Contains("cross-runtime-review-missing", result.RootElement.GetProperty("cause").GetString(), StringComparison.Ordinal);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateBlocked_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordDesignReview(Unit, "claude", "request-changes");
        workspace.RecordDesignReview(Unit, "cursor", "approve");
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.Blocked, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateRereviewMissing_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WritePacketVariant(Unit, Repo, "variant-a");
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordDesignReview(Unit, "claude", "approve");
        workspace.RecordDesignReview(Unit, "codex", "request-changes");
        workspace.WritePacketVariant(Unit, Repo, "variant-b");
        workspace.RecordDesignReview(Unit, "claude", "approve");
        workspace.RecordDesignReview(Unit, "cursor", "approve");
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.RereviewMissing, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateStaleEpoch_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WritePacketVariant(Unit, Repo, "epoch-a");
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordDesignReview(Unit, "claude", "approve");
        workspace.RecordDesignReview(Unit, "cursor", "approve");
        workspace.WritePacketVariant(Unit, Repo, "epoch-b");
        workspace.RecordDesignReview(Unit, "codex", "request-changes");
        workspace.WritePacketVariant(Unit, Repo, "epoch-a");
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("missing", result.RootElement.GetProperty("cross_runtime_design_review").GetProperty("decision").GetString());
        var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(workspace.PacketDirectory(Unit));
        var designResolution = CrossRuntimeReviewDesignTeamResolver.Resolve(workspace.Context.RepoRoot, Unit);
        var declaration = workspace.Context.Config.CrossRuntimeReview.Teams.Single(t => t.Team == $"{Domain}/{Team}");
        var gate = CrossRuntimeReviewGate.EvaluateDesign(
            declaration,
            CrossRuntimeReviewDesignTeamResolver.ToCrossRuntimeResolution(designResolution),
            digest,
            CrossRuntimeDesignReviewStore.Read(workspace.Context.RepoRoot, Unit));
        Assert.Contains(gate.Records, entry => entry.Status == CrossRuntimeReviewGate.StatusStaleEpoch);
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateRecordUnreadable_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordDesignReview(Unit, "claude", "approve");
        workspace.RecordDesignReview(Unit, "cursor", "approve");
        var directory = CrossRuntimeReviewPaths.DesignDirectory(workspace.Context.RepoRoot, Unit);
        File.WriteAllText(Path.Combine(directory, "20990101T000000Z-codex-zzzzzzz.json"), "{\"artifact_kind\":\"nope\"}");
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.RecordUnreadable, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_PacketMissing_RefusesAndLeavesDurableStateUntouched()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteTwoFilePacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.CaptureDurableBaseline();

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.PacketMissing, result.RootElement.GetProperty("cause").GetString());
        AssertZeroCreates();
        AssertDurableStateUntouched(workspace);
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_GateSatisfied_CreatesIssue()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        workspace.RecordSatisfiedDesignReviews(Unit);
        var packetYamlPath = Path.Combine(workspace.PacketDirectory(Unit), "packet.yaml");
        var expectedBodyBytes = File.ReadAllBytes(workspace.GithubBodyPath(Unit));
        var expectedTitle = IssuePublishFlowCommand.ResolveLookupTitle(
            Unit,
            File.ReadAllBytes(packetYamlPath),
            expectedBodyBytes);
        var recorder = new RecordingIssueCreator($"https://github.com/{Repo}/issues/835");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("created").GetBoolean());
        Assert.Equal(1, recorder.CallCount);
        Assert.Equal(expectedTitle, recorder.LastTitle);
        Assert.Equal(expectedBodyBytes, recorder.LastBodyBytes);
        Assert.True(File.Exists(workspace.PublishYamlPath(Unit)));
    }

    [Fact]
    public void PublishFlow_DeclaredTeam_CreatesWithTheSnapshotTitle_NotTheAnalysisTimeTitle()
    {
        using var workspace = new G835PublishFlowWorkspace(declare: true);
        var directory = workspace.PacketDirectory(Unit);
        string[] names = ["packet.yaml", "github-body.md", "review-context.md", "implementation.md"];

        // The reviewed packet is the renamed one; approvals are recorded on its digest.
        workspace.WriteFullPacket(Unit, Repo, bodyTitle: Title("renamed"));
        workspace.RecordSatisfiedDesignReviews(Unit);
        var reviewed = names.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(directory, name)));
        var expectedTitle = IssuePublishFlowCommand.ResolveLookupTitle(Unit, reviewed["packet.yaml"], reviewed["github-body.md"]);

        // Analysis reads the original title; the reviewed bytes land before the lookup snapshot.
        workspace.WriteFullPacket(Unit, Repo);
        workspace.SeedQueueState(Unit, Title());
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = () =>
        {
            foreach (var (name, bytes) in reviewed)
            {
                File.WriteAllBytes(Path.Combine(directory, name), bytes);
            }
        };
        var recorder = new RecordingIssueCreator($"https://github.com/{Repo}/issues/836");
        IssuePublishFlowCommand.CreatorFactory = () => recorder;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => defaultChecker;

        var (exit, output) = Run(workspace, Unit, Repo, write: true);
        Assert.True(exit == 0, output);
        Assert.Equal(1, recorder.CallCount);
        Assert.Contains("renamed", expectedTitle, StringComparison.Ordinal);
        Assert.Equal(expectedTitle, recorder.LastTitle);
        Assert.Equal(reviewed["github-body.md"], recorder.LastBodyBytes);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static string Title() => "G835PF Publish-flow design gate";

    private static string Title(string suffix) => $"G835PF Publish-flow design gate {suffix}";

    private static (int ExitCode, string Output) Run(
        G835PublishFlowWorkspace workspace,
        string unit,
        string repo,
        bool write,
        string? team = Team,
        string? domainOverride = null)
    {
        var args = new List<string> { unit, "--repo", repo, "--domain", domainOverride ?? Domain, "--format", "json" };
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

    private static void AssertDurableStateUntouched(G835PublishFlowWorkspace workspace) =>
        workspace.AssertDurableBaselineUntouched(Unit);

    private sealed class G835PublishFlowWorkspace : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("g835-publish-flow-").FullName;
        private byte[]? queueStateBaseline;
        private byte[]? runsBaseline;
        private IReadOnlyDictionary<string, byte[]>? claimsBaseline;
        private IReadOnlyDictionary<string, byte[]>? handoffBaseline;
        private int designReviewRecordedAtStep;

        public G835PublishFlowWorkspace(bool declare = false, string? heldTeam = Team, CrossRuntimeReviewTeamDeclaration[]? extraTeams = null)
        {
            var teams = new List<CrossRuntimeReviewTeamDeclaration>();
            if (declare)
            {
                teams.Add(new CrossRuntimeReviewTeamDeclaration
                {
                    Team = $"{Domain}/{Team}",
                    ConductorRuntime = "claude",
                    Repos = [Repo],
                });
                if (extraTeams is not null)
                {
                    teams.AddRange(extraTeams);
                }
            }

            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                    CrossRuntimeReview = new CrossRuntimeReviewConfig { Teams = teams },
                },
            };

            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            File.WriteAllText(Path.Combine(rootPath, ".intent-cli", "config.toml"), "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
            if (heldTeam is not null)
            {
                WriteClaim(Unit, heldTeam);
            }
        }

        public CliContext Context { get; }

        public string QueueStatePath => Path.Combine(rootPath, ".intent-cli", "queue-state.json");

        public string RunsLogPath => Path.Combine(rootPath, ".intent-cli", "runs.jsonl");

        public string GithubBodyPath(string unit) =>
            Path.Combine(rootPath, ".intent-cli", "issues", unit, "github-body.md");

        public string PublishYamlPath(string unit) =>
            Path.Combine(rootPath, ".intent-cli", "issues", unit, "publish.yaml");

        public string PacketDirectory(string unit) =>
            Path.Combine(rootPath, ".intent-cli", "issues", unit);

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

        public void WriteClaim(string unit, string team)
        {
            var claimPath = Path.Combine(rootPath, ClaimCommand.ClaimPath($"execution-unit:{unit}").Replace('/', Path.DirectorySeparatorChar));
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

        public void WriteMinimalPacket(string unit, string targetRepo, string domain = Domain, string? targetRepoOverride = null)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"),
                $"implementation_issue_packet:\n  issue_title: \"{Title()}\"\n  domain: {domain}\n  target_repo: {targetRepoOverride ?? targetRepo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildCompleteContractBody(Title()));
        }

        public void WriteTwoFilePacket(string unit, string targetRepo, string domain = Domain, string? targetRepoOverride = null)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"),
                $"implementation_issue_packet:\n  issue_title: \"{Title()}\"\n  domain: {domain}\n  target_repo: {targetRepoOverride ?? targetRepo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildCompleteContractBody(Title()));
        }

        public void WriteFullPacket(string unit, string targetRepo, string domain = Domain, string? bodyTitle = null)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"),
                $"implementation_issue_packet:\n  issue_title: \"{bodyTitle ?? Title()}\"\n  domain: {domain}\n  target_repo: {targetRepo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildCompleteContractBody(bodyTitle ?? Title()));
            File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
        }

        public void WritePacketVariant(string unit, string targetRepo, string variant)
        {
            var directory = PacketDirectory(unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"),
                $"implementation_issue_packet:\n  issue_title: \"{Title()} {variant}\"\n  domain: {Domain}\n  target_repo: {targetRepo}\n");
            File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildCompleteContractBody($"{Title()} {variant}"));
            File.WriteAllText(Path.Combine(directory, "review-context.md"), $"# review {variant}\n");
            File.WriteAllText(Path.Combine(directory, "implementation.md"), $"# notes {variant}\n");
        }

        public void RecordDesignReview(string unit, string runtime, string verdict)
        {
            var recordedAt = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero).AddMinutes(designReviewRecordedAtStep++);
            var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(PacketDirectory(unit));
            var verdictJson = JsonSerializer.Serialize(new
            {
                verdict,
                packet_digest = digest,
                blocking_findings = verdict == "request-changes"
                    ? new[] { new { file = "x", line = 1, scenario = "x", finding = "x" } }
                    : Array.Empty<object>(),
                notes = new[] { "ok" },
            });
            var raw = Encoding.UTF8.GetBytes(runtime == "claude" ? ClaudeEnvelope(verdictJson) : CursorEnvelope(verdictJson));
            var draft = new CrossRuntimeDesignReviewRecord
            {
                ArtifactKind = CrossRuntimeDesignReviewRecord.ArtifactKindValue,
                PacketDigest = digest,
                TargetRepo = Repo,
                ExecutionUnit = unit,
                Domain = Domain,
                Team = Team,
                Kind = CrossRuntimeReviewRecord.KindDesign,
                Runtime = runtime,
                RuntimeVersion = "2.1.269",
                ConductorRuntime = "claude",
                Relation = CrossRuntimeReviewRecord.RelationFor(runtime, "claude"),
                Verdict = verdict,
                BlockingFindings = verdict == "request-changes" ? [new CrossRuntimeReviewFinding { File = "x", Line = 1, Scenario = "x" }] : [],
                Notes = ["ok"],
                RecordedAt = recordedAt,
                RawVerdictFile = string.Empty,
                RawVerdictSha256 = CrossRuntimeReviewStore.Sha256Hex(raw),
            };
            var record = draft with { RawVerdictFile = CrossRuntimeDesignReviewStore.RawRelativePath(draft) };
            Assert.True(CrossRuntimeDesignReviewStore.Write(rootPath, record, raw).Written);
        }

        public void RecordSatisfiedDesignReviews(string unit)
        {
            var digest = CrossRuntimeDesignReviewDigest.ComputeFromDirectory(Path.Combine(rootPath, ".intent-cli", "issues", unit));
            foreach (var runtime in new[] { "claude", "cursor" })
            {
                var at = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero).AddMinutes(designReviewRecordedAtStep++);
                var verdict = JsonSerializer.Serialize(new
                {
                    verdict = "approve",
                    packet_digest = digest,
                    blocking_findings = Array.Empty<object>(),
                    notes = new[] { "ok" },
                });
                var raw = Encoding.UTF8.GetBytes(runtime == "claude" ? ClaudeEnvelope(verdict) : CursorEnvelope(verdict));
                var draft = new CrossRuntimeDesignReviewRecord
                {
                    ArtifactKind = CrossRuntimeDesignReviewRecord.ArtifactKindValue,
                    PacketDigest = digest,
                    TargetRepo = Repo,
                    ExecutionUnit = unit,
                    Domain = Domain,
                    Team = Team,
                    Kind = CrossRuntimeReviewRecord.KindDesign,
                    Runtime = runtime,
                    RuntimeVersion = "2.1.269",
                    ConductorRuntime = "claude",
                    Relation = CrossRuntimeReviewRecord.RelationFor(runtime, "claude"),
                    Verdict = "approve",
                    BlockingFindings = [],
                    Notes = ["ok"],
                    RecordedAt = at,
                    RawVerdictFile = string.Empty,
                    RawVerdictSha256 = CrossRuntimeReviewStore.Sha256Hex(raw),
                };
                var record = draft with { RawVerdictFile = CrossRuntimeDesignReviewStore.RawRelativePath(draft) };
                Assert.True(CrossRuntimeDesignReviewStore.Write(rootPath, record, raw).Written);
            }
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
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }

        private static string BuildCompleteContractBody(string title) =>
            $"""
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

            ## Base Branch Policy
            Policy: `direct-main`
            Expected PR base branch: `main`
            Open all child PRs against `main` directly.
            """;

        private static string ClaudeEnvelope(string verdict)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Fixture("claude-envelope.json")))!.AsObject();
            node["structured_output"] = System.Text.Json.Nodes.JsonNode.Parse(verdict);
            node["result"] = verdict;
            return node.ToJsonString();
        }

        private static string CursorEnvelope(string resultText)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Fixture("cursor-envelope.json")))!.AsObject();
            node["result"] = resultText;
            return node.ToJsonString();
        }

        private static string Fixture(string name) =>
            Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", name);

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
            throw new InvalidOperationException("tests must not reach real gh issue create");
        }
    }

    private sealed class StubIssueCreator : IIssueCreator
    {
        private readonly string url;

        public StubIssueCreator(string url) => this.url = url;

        public int CallCount { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            CallCount++;
            return new IssueCreateOutcome(url);
        }
    }

    private sealed class StubExistingIssueChecker : IGitHubExistingIssueChecker
    {
        private readonly GitHubExistingIssueLookupResult result;

        public StubExistingIssueChecker(GitHubExistingIssueClassification classification, int? issueNumber = null, string? issueUrl = null)
        {
            result = new GitHubExistingIssueLookupResult
            {
                Classification = classification,
                IssueNumber = issueNumber,
                IssueUrl = issueUrl,
            };
        }

        public int CallCount { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class CapturingExistingIssueChecker : IGitHubExistingIssueChecker
    {
        public CapturingExistingIssueChecker(GitHubExistingIssueClassification classification) =>
            Inner = new StubExistingIssueChecker(classification);

        private StubExistingIssueChecker Inner { get; }

        public int CallCount => Inner.CallCount;

        public string? LastTitle { get; private set; }

        public string? LastBody { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            LastTitle = expectedTitle;
            LastBody = expectedBody;
            return Inner.FindExistingIssue(repo, executionUnit, expectedTitle, expectedBody);
        }
    }

    private sealed class MutatingExistingIssueChecker : IGitHubExistingIssueChecker
    {
        private readonly string bodyPath;

        public MutatingExistingIssueChecker(string bodyPath) => this.bodyPath = bodyPath;

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            File.AppendAllText(bodyPath, "\n");
            return new GitHubExistingIssueLookupResult
            {
                Classification = GitHubExistingIssueClassification.Unique,
                IssueNumber = 77,
                IssueUrl = $"https://github.com/{repo}/issues/77",
            };
        }
    }

    private sealed class PostSnapshotMutatingChecker : IGitHubExistingIssueChecker
    {
        private readonly string packetYamlPath;
        private readonly string bodyPath;
        private readonly StubExistingIssueChecker inner;

        public PostSnapshotMutatingChecker(string packetYamlPath, string bodyPath, GitHubExistingIssueClassification classification)
        {
            this.packetYamlPath = packetYamlPath;
            this.bodyPath = bodyPath;
            inner = new StubExistingIssueChecker(classification);
        }

        public int CallCount => inner.CallCount;

        public string? LastTitle { get; private set; }

        public string? LastBody { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            File.AppendAllText(packetYamlPath, "\nmutated-on-disk: true\n");
            File.AppendAllText(bodyPath, "\nmutated-on-disk\n");
            LastTitle = expectedTitle;
            LastBody = expectedBody;
            return inner.FindExistingIssue(repo, executionUnit, expectedTitle, expectedBody);
        }
    }

    private sealed class RecordingIssueCreator : IIssueCreator
    {
        private readonly string url;

        public RecordingIssueCreator(string url) => this.url = url;

        public int CallCount { get; private set; }

        public string? LastTitle { get; private set; }

        public byte[]? LastBodyBytes { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            CallCount++;
            LastTitle = title;
            LastBodyBytes = File.ReadAllBytes(bodyFilePath);
            return new IssueCreateOutcome(url);
        }
    }

    private sealed class PacketSwapDuringLookupChecker : IGitHubExistingIssueChecker
    {
        private readonly G835PublishFlowWorkspace workspace;
        private readonly string unit;
        private readonly string targetRepo;
        private readonly string alternateTitle;

        public PacketSwapDuringLookupChecker(G835PublishFlowWorkspace workspace, string unit, string targetRepo, string alternateTitle)
        {
            this.workspace = workspace;
            this.unit = unit;
            this.targetRepo = targetRepo;
            this.alternateTitle = alternateTitle;
        }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            workspace.WriteFullPacket(unit, targetRepo, bodyTitle: alternateTitle);
            return new GitHubExistingIssueLookupResult
            {
                Classification = GitHubExistingIssueClassification.None,
            };
        }
    }
}
