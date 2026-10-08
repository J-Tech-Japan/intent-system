using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class SoloConductorPrTransitionTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G856";
    private const int Pr = 1870;
    private const string Head = "2222222222222222222222222222222222222222";
    private readonly string root = Directory.CreateTempSubdirectory("g856-solo-host-").FullName;
    private readonly RecordingMutator mutator = new();
    private int headReads;
    private Func<int, string>? headResult;
    private Func<SoloConductorReviewReadRequest, SoloConductorReviewReadResult>? reviewResult;
    private int reviewReads;
    private int claimReads;

    public SoloConductorPrTransitionTests()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        WriteQueue();
        WritePacket();
        WriteSoloMode();
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            claimReads++;
            return new ClaimOwnershipVerification(
                false,
                ClaimOwnershipVerification.StatusTeamRequired,
                scope,
                true,
                null,
                "implementation",
                Team,
                "held");
        };
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;
        AutomationPrTransitionCommand.PrHeadReader = (_, _) =>
        {
            headReads++;
            return headResult?.Invoke(headReads) ?? Head;
        };
        AutomationPrTransitionCommand.SoloReviewReader = request =>
        {
            reviewReads++;
            return reviewResult?.Invoke(request) ?? CompleteRead(request, GenericBody(Unit, Head), 900 + reviewReads);
        };
    }

    public void Dispose()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        AutomationPrTransitionCommand.MutatorFactory = null;
        AutomationPrTransitionCommand.PrHeadReader = null;
        AutomationPrTransitionCommand.SoloReviewReader = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void GenericSoloApproval_WritesOnlyAfterTwoMatchingHeadReads_AndClearsCiWait()
    {
        RecordCiWait();

        var (exit, output) = Run(write: true);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal("satisfied", result.RootElement.GetProperty("solo_conductor_review").GetProperty("decision").GetString());
        Assert.Equal(2, headReads);
        Assert.Equal(1, reviewReads);
        Assert.Single(mutator.Applied);
        Assert.Empty(CiWaitStore.ReadOpen(root, repo: Repo).Records);
    }

    [Fact]
    public void MissingHeadRefusesBeforeHeadOrReviewReads()
    {
        var (exit, output) = Run(write: true, head: null);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        var solo = result.RootElement.GetProperty("solo_conductor_review");
        Assert.Equal(SoloConductorApprovalGate.CauseHeadRequired, solo.GetProperty("cause").GetString());
        Assert.False(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.Empty(result.RootElement.GetProperty("add_labels").EnumerateArray());
        Assert.Empty(result.RootElement.GetProperty("remove_labels").EnumerateArray());
        Assert.Equal(0, headReads);
        Assert.Equal(0, reviewReads);
        Assert.Empty(mutator.Applied);
    }

    [Fact]
    public void ReviewReadFailureLeavesLabelsAndCiWaitUntouched()
    {
        RecordCiWait();
        reviewResult = _ => SoloConductorReviewReadResult.Failed(
            SoloConductorReviewReader.CauseReadUnavailable,
            "scripted GitHub 403");

        var (exit, output) = Run(write: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(SoloConductorReviewReader.CauseReadUnavailable,
            result.RootElement.GetProperty("solo_conductor_review").GetProperty("cause").GetString());
        Assert.False(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.Empty(result.RootElement.GetProperty("add_labels").EnumerateArray());
        Assert.Empty(result.RootElement.GetProperty("remove_labels").EnumerateArray());
        Assert.Empty(mutator.Applied);
        Assert.Single(CiWaitStore.ReadOpen(root, repo: Repo).Records);
    }

    [Fact]
    public void HeadRaceAfterReviewEvaluationRefusesBeforeLabelMutation()
    {
        headResult = read => read == 1 ? Head : "3333333333333333333333333333333333333333";
        RecordCiWait();

        var (exit, output) = Run(write: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        var solo = result.RootElement.GetProperty("solo_conductor_review");
        Assert.Equal(SoloConductorApprovalGate.CauseHeadChanged, solo.GetProperty("cause").GetString());
        Assert.Equal("3333333333333333333333333333333333333333", solo.GetProperty("observed_head_sha").GetString());
        Assert.Equal(2, headReads);
        Assert.Equal(1, reviewReads);
        Assert.Empty(mutator.Applied);
        Assert.Single(CiWaitStore.ReadOpen(root, repo: Repo).Records);
    }

    [Fact]
    public void HeadRaceBeforeReviewInventoryRefusesWithoutReviewRead()
    {
        headResult = _ => "3333333333333333333333333333333333333333";

        var (exit, output) = Run(write: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(SoloConductorApprovalGate.CauseHeadChanged,
            result.RootElement.GetProperty("solo_conductor_review").GetProperty("cause").GetString());
        Assert.Equal(1, headReads);
        Assert.Equal(0, reviewReads);
        Assert.Empty(mutator.Applied);
    }

    [Fact]
    public void MalformedTeamModeRefusesBeforeReviewOrHeadRead()
    {
        File.WriteAllText(TeamModeStore.ResolvePath(root), "{");

        var (exit, output) = Run(write: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(SoloConductorApprovalGate.CauseApplicabilityUnresolved,
            result.RootElement.GetProperty("solo_conductor_review").GetProperty("cause").GetString());
        Assert.Equal(0, headReads);
        Assert.Equal(0, reviewReads);
        Assert.Empty(mutator.Applied);
    }

    [Fact]
    public void SoloConfiguredHostWithUnresolvedPrIdentityRefusesConservatively()
    {
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), JsonSerializer.Serialize(new
        {
            schema_version = "1",
            updated_at = "2026-10-08T00:00:00+00:00",
            items = Array.Empty<object>(),
        }));

        var (exit, output) = Run(write: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(SoloConductorApprovalGate.CauseApplicabilityUnresolved,
            result.RootElement.GetProperty("solo_conductor_review").GetProperty("cause").GetString());
        Assert.Contains("could not be authoritatively resolved", result.RootElement.GetProperty("solo_conductor_review").GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, headReads);
        Assert.Equal(0, reviewReads);
        Assert.Empty(mutator.Applied);
    }

    [Fact]
    public void SoloHostResolvedNonSoloTeamUsesExistingPathWithoutReviewRead()
    {
        WriteMode(
            Entry("intent-cli-dev", TeamMode.AuthoringOnly),
            Entry("intent-cli-other", TeamMode.SoloConductor));

        var (exit, output) = Run(write: false);

        Assert.Equal(0, exit);
        Assert.Equal(0, reviewReads);
        Assert.Equal(0, headReads);
        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.TryGetProperty("solo_conductor_review", out _));
    }

    [Fact]
    public void DeclaredNonSoloG834SuccessResolvesClaimExactlyOnce()
    {
        WriteMode(Entry(Team, TeamMode.Delivery));
        WriteDeclaredRecords();

        var (exit, output) = Run(write: false, declareG834: true);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("satisfied", result.RootElement.GetProperty("cross_runtime_review").GetProperty("decision").GetString());
        Assert.False(result.RootElement.TryGetProperty("solo_conductor_review", out _));
        Assert.Equal(1, claimReads);
        Assert.Equal(1, headReads);
        Assert.Equal(0, reviewReads);
    }

    [Fact]
    public void DeclaredNonSoloG834RefusalResolvesClaimExactlyOnce()
    {
        WriteMode(Entry(Team, TeamMode.Delivery));

        var (exit, output) = Run(write: false, declareG834: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.Missing, result.RootElement.GetProperty("cross_runtime_review").GetProperty("cause").GetString());
        Assert.False(result.RootElement.TryGetProperty("solo_conductor_review", out _));
        Assert.Equal(1, claimReads);
        Assert.Equal(1, headReads);
        Assert.Equal(0, reviewReads);
        Assert.Empty(mutator.Applied);
    }

    [Fact]
    public void DeclaredSoloTeamRequiresBothLocalGateAndCanonicalPostedRelationSlots()
    {
        WriteDeclaredRecords();
        reviewResult = request =>
        {
            var claude = ReadCanonicalBody(request, "claude", 910);
            var cursor = ReadCanonicalBody(request, "cursor", 911);
            return new SoloConductorReviewReadResult { Complete = true, Rows = [claude, cursor] };
        };

        var (exit, output) = Run(write: false, declareG834: true);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("satisfied", result.RootElement.GetProperty("cross_runtime_review").GetProperty("decision").GetString());
        Assert.Equal("satisfied", result.RootElement.GetProperty("solo_conductor_review").GetProperty("decision").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("solo_conductor_review").GetProperty("qualifying_reviews").GetArrayLength());
        Assert.Equal(2, headReads);
        Assert.Equal(1, reviewReads);
    }

    [Fact]
    public void DeclaredTeamOnRepoAbsentFromEveryDeclarationUsesGenericSoloRoute()
    {
        var (exit, output) = Run(write: false, declareG834: true, declarationOmitsRepo: true);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.TryGetProperty("cross_runtime_review", out _));
        Assert.Equal("satisfied", result.RootElement.GetProperty("solo_conductor_review").GetProperty("decision").GetString());
        Assert.Equal(1, reviewReads);
    }

    [Fact]
    public void GatedRepoListedOnlyByAnotherTeamRetainsGlobalExistingGatePredicate()
    {
        var (exit, output) = Run(write: false, declareG834: true, gateViaOtherTeam: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.Missing, result.RootElement.GetProperty("cross_runtime_review").GetProperty("cause").GetString());
        Assert.Equal(SoloConductorApprovalGate.CauseLocalGateMissing,
            result.RootElement.GetProperty("solo_conductor_review").GetProperty("cause").GetString());
        Assert.Equal(0, reviewReads);
    }

    private (int ExitCode, string Output) Run(
        bool write = false,
        string? head = Head,
        bool declareG834 = false,
        bool declarationOmitsRepo = false,
        bool gateViaOtherTeam = false)
    {
        var args = new List<string>
        {
            "--repo", Repo,
            "--pr", Pr.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--transition", "approved",
            "--format", "json",
        };
        if (head is not null) args.AddRange(["--head-sha", head]);
        if (write) args.Add("--write");
        using var writer = new StringWriter();
        var exit = AutomationPrTransitionCommand.Execute(Context(declareG834, declarationOmitsRepo, gateViaOtherTeam), args.ToArray(), writer);
        return (exit, writer.ToString());
    }

    private CliContext Context(bool declareG834, bool declarationOmitsRepo = false, bool gateViaOtherTeam = false) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            CrossRuntimeReview = !declareG834
                ? new CrossRuntimeReviewConfig()
                : new CrossRuntimeReviewConfig
                {
                    Teams = gateViaOtherTeam
                        ? [
                            new CrossRuntimeReviewTeamDeclaration { Team = $"{Domain}/{Team}", ConductorRuntime = "claude", Repos = ["J-Tech-Japan/other"] },
                            new CrossRuntimeReviewTeamDeclaration { Team = $"{Domain}/intent-cli-other", ConductorRuntime = "cursor", Repos = [Repo] },
                        ]
                        : [new CrossRuntimeReviewTeamDeclaration
                        {
                            Team = $"{Domain}/{Team}",
                            ConductorRuntime = "claude",
                            Repos = declarationOmitsRepo ? ["J-Tech-Japan/other"] : [Repo],
                        }],
                },
        },
    };

    private void WriteSoloMode() => WriteMode(Entry(Team, TeamMode.SoloConductor));

    private void WriteMode(params TeamModeEntry[] entries) => TeamModeStore.Write(root, new TeamModeState
    {
        SchemaVersion = TeamModeStore.SchemaVersion,
        Entries = entries.OrderBy(entry => entry.Domain, StringComparer.Ordinal).ThenBy(entry => entry.Team ?? string.Empty, StringComparer.Ordinal).ToArray(),
    });

    private static TeamModeEntry Entry(string team, string mode)
    {
        var at = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        return new TeamModeEntry
        {
            Domain = Domain,
            Team = team,
            Mode = mode,
            UpdatedAt = at,
            Transitions = [new TeamModeTransition { From = TeamMode.Default, To = mode, At = at }],
        };
    }

    private void WriteQueue()
    {
        var queue = new
        {
            schema_version = "1",
            updated_at = "2026-10-08T00:00:00+00:00",
            items = new[]
            {
                new Dictionary<string, object?>
                {
                    ["execution_unit"] = Unit,
                    ["title"] = Unit,
                    ["state"] = "active",
                    ["dependencies"] = Array.Empty<string>(),
                    ["blocked_by"] = Array.Empty<string>(),
                    ["clarification_return_path"] = "intents/intent-cli/clarifications/open.md",
                    ["packet_paths"] = new { implementation = $".intent-cli/issues/{Unit}/implementation.md", review_context = $".intent-cli/issues/{Unit}/review-context.md", yaml = $".intent-cli/issues/{Unit}/packet.yaml" },
                    ["linked_issue"] = new { repo = Repo, number = 1869, url = $"https://github.com/{Repo}/issues/1869" },
                    ["linked_pr"] = $"https://github.com/{Repo}/pull/{Pr}",
                    ["worker_role"] = "builder",
                    ["review_role"] = "reviewer",
                    ["priority"] = "high",
                },
            },
        };
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), JsonSerializer.Serialize(queue));
    }

    private void WritePacket()
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", Unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"),
            $"implementation_issue_packet:\n  issue_title: \"G856\"\n  domain: {Domain}\n  target_repo: {Repo}\n");
    }

    private void RecordCiWait() => Assert.True(CiWaitStore.Record(root, new CiWaitRecord
    {
        Domain = Domain,
        Repo = Repo,
        Pr = Pr,
        ObservedHead = Head,
        OwedTransition = "approved",
        RecordedAt = DateTimeOffset.UtcNow,
    }, write: true).Applied);

    private void WriteDeclaredRecords()
    {
        WriteCanonicalRecord("claude", "claude", DateTimeOffset.UtcNow.AddMinutes(-2));
        WriteCanonicalRecord("cursor", "claude", DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private void WriteCanonicalRecord(string runtime, string conductor, DateTimeOffset at)
    {
        var provisional = new CrossRuntimeReviewRecord
        {
            ArtifactKind = CrossRuntimeReviewRecord.ArtifactKindValue,
            Repo = Repo,
            Pr = Pr,
            HeadSha = Head,
            ExecutionUnit = Unit,
            Domain = Domain,
            Team = Team,
            Kind = CrossRuntimeReviewRecord.KindImplementation,
            Runtime = runtime,
            RuntimeVersion = "test-runtime-1",
            ConductorRuntime = conductor,
            Relation = CrossRuntimeReviewRecord.RelationFor(runtime, conductor),
            Verdict = CrossRuntimeReviewVerdict.Approve,
            BlockingFindings = [],
            Notes = ["actual fixture"],
            RecordedAt = at,
            RawVerdictFile = "pending",
            RawVerdictSha256 = "pending",
        };
        var raw = System.Text.Encoding.UTF8.GetBytes("{\"fixture\":true}");
        var record = provisional with
        {
            RawVerdictFile = CrossRuntimeReviewStore.RawRelativePath(provisional),
            RawVerdictSha256 = CrossRuntimeReviewStore.Sha256Hex(raw),
        };
        var written = CrossRuntimeReviewStore.Write(root, record, raw);
        Assert.True(written.Written, written.Error);
    }

    private static IndependentReviewRowEvidence ReadCanonicalBody(SoloConductorReviewReadRequest request, string runtime, long id)
    {
        var stored = request.LocalReviews.Records.Single(item => item.Record.Runtime == runtime);
        var body = ReviewCrossRuntimeCommand.RenderCommentBody(stored.Record, stored.RelativePath);
        return ParseRow(request, id, body, runtime + "-reviewer");
    }

    private static SoloConductorReviewReadResult CompleteRead(SoloConductorReviewReadRequest request, string body, long id) => new()
    {
        Complete = true,
        Rows = [ParseRow(request, id, body, "reviewer")],
    };

    private static IndependentReviewRowEvidence ParseRow(SoloConductorReviewReadRequest request, long id, string body, string login)
    {
        var json = JsonSerializer.Serialize(new
        {
            id,
            state = "COMMENTED",
            commit_id = Head,
            body,
            html_url = $"https://github.com/{Repo}/pull/{Pr}#pullrequestreview-{id}",
            user = new { login },
            submitted_at = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero)
                .AddSeconds(id)
                .ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        });
        using var document = JsonDocument.Parse(json);
        return IndependentReviewEvidence.ParseRow(
            document.RootElement,
            request.ExecutionUnit,
            request.Domain,
            request.Team,
            request.Repo,
            request.PullRequest,
            request.LocalReviews);
    }

    private static string GenericBody(string unit, string head) => $"""
        ## Independent subagent review: approve

        - reviewer: independent subagent review
        - execution unit: {unit}
        - kind: implementation
        - head SHA: {head}
        - verdict: approve

        ### Blocking findings

        - none

        ### Notes

        actual reviewer notes
        """;

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        public List<(IReadOnlyCollection<string> Add, IReadOnlyCollection<string> Remove)> Applied { get; } = [];

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            [new GitHubAutomationLabel { Name = "intent-pr-reviewing" }];

        public void ApplyLabelTransitions(string repo, string kind, int number, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            Applied.Add((addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(string repo, string kind, int number, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new InvalidOperationException("unexpected reconcile path");
    }
}
