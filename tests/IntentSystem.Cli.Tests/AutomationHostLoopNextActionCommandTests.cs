using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G319: command-level regression tests for the approved-PR
/// continuation lane. Verifies the end-to-end path — the command layer
/// detects an approved PR from GitHub labels, plumbs it into the
/// analyzer with the correct `is_draft` + merge-state + metadata flags,
/// and the analyzer surfaces it BEFORE the wip-cap-blocked stop.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class AutomationHostLoopNextActionCommandTests : IDisposable
{
    public AutomationHostLoopNextActionCommandTests()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
    }

    public void Dispose()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
    }

    [Fact]
    public void Execute_ApprovedPrOpenAndNonDraft_SelectsApprovedContinuation_NotWipCapBlocked()
    {
        // Mirrors SKS PR #571: open, non-draft, carries
        // intent-target + intent-pr-approved; the host loop previously
        // stopped at wip-cap-blocked for the open intent-target.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" },
                      closingIssue: 570)
            },
            issues: new[] { NewIssue(570, labels: new[] { "intent-target", "intent-pr-created" }) });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("approved-pr-merge-closeout", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
        Assert.Contains("--pr 571", root.GetProperty("recommended_command").GetString()!, StringComparison.Ordinal);
        var evidence = root.GetProperty("evidence").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains(evidence, e => e.Contains("#571", StringComparison.Ordinal));
        Assert.Contains(evidence, e => e.Contains("Closes #570", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Lane: `main-hotfix`\nLanding mode: `direct`\n")]
    public void Execute_ImmutableOperatorMergeSnapshotOverridesAbsentOrMismatchedIssueProjection_G678(
        string? issueBody)
    {
        var root = Directory.CreateTempSubdirectory("host-loop-g678-operator-").FullName;
        try
        {
            var context = CreateContext(root);
            WriteRoutingState(context, BranchLaneLandingModes.OperatorMerge);
            AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
                prs:
                [
                    NewPr(1468, isDraft: false, state: "OPEN",
                        labels: ["intent-target", "intent-pr-approved"],
                        closingIssue: 1467,
                        checksGreen: true),
                ],
                issues: [NewIssue(1467, ["intent-target", "intent-pr-created"], issueBody, "G678 operator merge")]);
            AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ =>
                new FakeNextSliceProbe(new NextSliceProbeResult
                {
                    RecommendedOutcome = "skip-next-slice-due-to-wip",
                    ExecutionUnit = null,
                });

            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                context,
                ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(writer.ToString());
            var result = doc.RootElement;
            Assert.Equal("awaiting-operator-merge", result.GetProperty("classification").GetString());
            Assert.False(result.GetProperty("mutation_allowed").GetBoolean());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("recommended_command").ValueKind);
            Assert.Contains(result.GetProperty("evidence").EnumerateArray(), item =>
                item.GetString()!.Contains("immutable queue+packet routing snapshot", StringComparison.Ordinal));
            Assert.DoesNotContain("merge via", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Execute_ImmutableDirectSnapshotPreservesDirectContinuationDespiteMutableProjection_G678()
    {
        var root = Directory.CreateTempSubdirectory("host-loop-g678-direct-").FullName;
        try
        {
            var context = CreateContext(root);
            WriteRoutingState(context, BranchLaneLandingModes.Direct);
            AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
                prs:
                [
                    NewPr(1468, isDraft: false, state: "OPEN",
                        labels: ["intent-target", "intent-pr-approved"],
                        closingIssue: 1467,
                        checksGreen: true),
                ],
                issues:
                [
                    NewIssue(1467, ["intent-target", "intent-pr-created"],
                        "Lane: `main-hotfix`\nLanding mode: `operator-merge`\n",
                        "G678 operator merge"),
                ]);

            using var writer = new StringWriter();
            Assert.Equal(0, AutomationHostLoopNextActionCommand.Execute(
                context,
                ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
                writer));

            using var doc = JsonDocument.Parse(writer.ToString());
            Assert.Equal("approved-pr-merge-closeout", doc.RootElement.GetProperty("classification").GetString());
            Assert.True(doc.RootElement.GetProperty("mutation_allowed").GetBoolean());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Execute_NoImmutableSnapshotOrIssueProjectionPreservesUndeclaredDirectContinuation_G678()
    {
        var root = Directory.CreateTempSubdirectory("host-loop-g678-undeclared-").FullName;
        try
        {
            var context = CreateContext(root);
            AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
                prs:
                [
                    NewPr(1468, isDraft: false, state: "OPEN",
                        labels: ["intent-target", "intent-pr-approved"],
                        closingIssue: 1467,
                        checksGreen: true),
                ],
                issues: [NewIssue(1467, ["intent-target", "intent-pr-created"], null, "G678 operator merge")]);

            using var writer = new StringWriter();
            Assert.Equal(0, AutomationHostLoopNextActionCommand.Execute(
                context,
                ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
                writer));

            using var doc = JsonDocument.Parse(writer.ToString());
            Assert.Equal("approved-pr-merge-closeout", doc.RootElement.GetProperty("classification").GetString());
            Assert.True(doc.RootElement.GetProperty("mutation_allowed").GetBoolean());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Execute_ConflictingImmutableSnapshotsFailClosedWithoutMergeCommand_G678()
    {
        var root = Directory.CreateTempSubdirectory("host-loop-g678-conflict-").FullName;
        try
        {
            var context = CreateContext(root);
            WriteRoutingState(
                context,
                BranchLaneLandingModes.OperatorMerge,
                packetLandingMode: BranchLaneLandingModes.Direct);
            AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
                prs:
                [
                    NewPr(1468, isDraft: false, state: "OPEN",
                        labels: ["intent-target", "intent-pr-approved"],
                        closingIssue: 1467,
                        checksGreen: true),
                ],
                issues: [NewIssue(1467, ["intent-target", "intent-pr-created"], null, "G678 operator merge")]);

            using var writer = new StringWriter();
            Assert.Equal(0, AutomationHostLoopNextActionCommand.Execute(
                context,
                ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
                writer));

            using var doc = JsonDocument.Parse(writer.ToString());
            var result = doc.RootElement;
            Assert.Equal("approved-pr-metadata-blocked", result.GetProperty("classification").GetString());
            Assert.False(result.GetProperty("mutation_allowed").GetBoolean());
            Assert.DoesNotContain("merge", result.GetProperty("recommended_command").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.GetProperty("evidence").EnumerateArray(), item =>
                item.GetString()!.Contains("snapshots disagree", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Execute_ApprovedPrIsDraft_MapsToApprovedPrDraftBlocked()
    {
        // G297: an approved PR that is currently a draft cannot be
        // merged; the analyzer must classify draft-blocked, not the
        // happy path or generic wip-cap-blocked.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: true, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-draft-blocked", doc.RootElement.GetProperty("classification").GetString());
        Assert.False(doc.RootElement.GetProperty("mutation_allowed").GetBoolean());
    }

    [Fact]
    public void Execute_ApprovedPrMergeStateOverride_MapsToApprovedPrMergeBlocked()
    {
        // The command exposes `--approved-pr-merge-state <state>` so
        // host-loop guidance can pre-fetch `gh pr view --json
        // mergeStateStatus` once and pipe it in. CONFLICTING → blocked.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--approved-pr-merge-state", "CONFLICTING", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-merge-blocked", doc.RootElement.GetProperty("classification").GetString());
        Assert.False(doc.RootElement.GetProperty("mutation_allowed").GetBoolean());
        Assert.Contains("CONFLICTING", doc.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ApprovedPrMetadataBlockedFlag_MapsToApprovedPrMetadataBlocked()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN", labels: new[] { "intent-target", "intent-pr-approved" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--approved-pr-metadata-blocked", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("approved-pr-metadata-blocked", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_OpenIntentTargetPrWithoutApproved_FallsThroughToWipCapBlocked()
    {
        // Acceptance: when there is open intent-target work but NO
        // approved continuation pending, the existing wip-cap-blocked
        // classification is unchanged.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: new[] { NewIssue(700, labels: new[] { "intent-target" }) });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wip-cap-blocked", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_ApprovedPrWithUpdateInProgress_NotSelectedForContinuation()
    {
        // An approved PR that is also `intent-pr-update-in-progress`
        // means a child worker is repairing it. The continuation lane
        // skips it; the existing wip-cap / wait-for-child lanes handle
        // the in-flight state.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                NewPr(571, isDraft: false, state: "OPEN",
                    labels: new[] { "intent-target", "intent-pr-approved", "intent-pr-update-in-progress" })
            },
            issues: Array.Empty<GitHubAutomationIssueCandidate>());

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var classification = doc.RootElement.GetProperty("classification").GetString();
        Assert.NotEqual("approved-pr-merge-closeout", classification);
        // Open intent-target + lease held → wait-for-child.
        Assert.Equal("wait-for-child", classification);
    }

    // --- helpers --------------------------------------------------------------

    private static GitHubAutomationPrCandidate NewPr(
        int number,
        bool isDraft,
        string state,
        IReadOnlyList<string> labels,
        int? closingIssue = null,
        bool checksGreen = false)
    {
        var refs = closingIssue is int issueNumber
            ? new[]
            {
                new GitHubPrClosingIssueReference { Number = issueNumber }
            }
            : Array.Empty<GitHubPrClosingIssueReference>();
        return new GitHubAutomationPrCandidate
        {
            Number = number,
            Title = $"PR {number}",
            Url = $"https://github.com/J-Tech-Japan/SekibanAsAService/pull/{number}",
            Body = "stub",
            CreatedAt = "2026-05-10T00:00:00Z",
            UpdatedAt = "2026-05-10T00:00:00Z",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            ClosingIssuesReferences = refs,
            State = state,
            IsDraft = isDraft,
            HeadRefOid = checksGreen ? "g678-head" : string.Empty,
            StatusCheckRollup = checksGreen
                ?
                [
                    new GitHubAutomationStatusCheckCandidate
                    {
                        TypeName = "CheckRun",
                        Status = "COMPLETED",
                        Conclusion = "SUCCESS",
                    },
                ]
                : [],
        };
    }

    private static GitHubAutomationIssueCandidate NewIssue(
        int number,
        IReadOnlyList<string> labels,
        string? body = null,
        string? title = null) =>
        new()
        {
            Number = number,
            Title = title ?? $"issue {number}",
            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
            CreatedAt = "2026-05-10T00:00:00Z",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            State = "OPEN",
            Body = body ?? string.Empty,
        };

    private static void WriteRoutingState(
        CliContext context,
        string queueLandingMode,
        string? packetLandingMode = null)
    {
        packetLandingMode ??= queueLandingMode;
        var queuePath = context.GetQueueStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(queuePath)!);
        var queue = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 8, 12, 18, 0, 0, TimeSpan.Zero),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G678",
                    Title = "G678 operator merge",
                    State = QueueItemState.Review,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = ".intent-cli/issues/G678/packet.yaml",
                        Implementation = ".intent-cli/issues/G678/implementation.md",
                        ReviewContext = ".intent-cli/issues/G678/review-context.md",
                    },
                    RoutingSnapshot = QueueSnapshot(queueLandingMode),
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 1467,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/1467",
                    },
                    LinkedPr = "1468",
                    WorkerRole = "implementation",
                    ReviewRole = "review",
                    Priority = "normal",
                },
            ],
        };
        File.WriteAllText(queuePath, QueueStateSerializer.Serialize(queue));

        var packetPath = Path.Combine(context.RepoRoot, ".intent-cli", "issues", "G678", "packet.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(packetPath)!);
        File.WriteAllText(packetPath, $$"""
            implementation_issue_packet:
              source_execution_unit: G678
              branch_lane: main-hotfix
              routing_snapshot:
                lane_id: main-hotfix
                definition_revision: registry-g678
                start_branch: main
                pr_base_branch: main
                landing_mode: {{packetLandingMode}}
            """);
    }

    private static QueueRoutingSnapshot QueueSnapshot(string landingMode) => new()
    {
        LaneId = "main-hotfix",
        DefinitionRevision = "registry-g678",
        StartBranch = "main",
        PrBaseBranch = "main",
        LandingMode = landingMode,
    };

    // --- G318: automatic intent next-slice --dry-run probe ----------------

    [Fact]
    public void Execute_NextSliceProbeReturnsIssueCutReady_SelectsPublishNextIssue_NotTrueIdle()
    {
        // Mirrors SKS-G224: empty repo (no review PR, no open intent-target,
        // no host-metadata drift), `intent next-slice --dry-run` reports
        // `issue-cut-ready`. Before G318 the host loop reported true-idle;
        // the command now auto-probes next-slice and surfaces the publish
        // action.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G224" });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("publish-next-issue", root.GetProperty("classification").GetString());
        Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
        var recommended = root.GetProperty("recommended_command").GetString()!;
        Assert.Contains("--execution-unit SKS-G224", recommended, StringComparison.Ordinal);
        Assert.Contains("--target-repo J-Tech-Japan/SekibanAsAService", recommended, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NextSliceIssueCutReady_ButUnitAlreadyOnGitHub_ReconcilesInsteadOfDuplicatePublish_G444()
    {
        // G444 / Zero4Racer Z4R-G329: stale queue-state makes next-slice
        // report `issue-cut-ready` even though GitHub already has an open
        // issue (#661) and PR (#662) for the same execution unit. The host
        // loop must NOT publish a duplicate; it must reconcile.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: new[]
            {
                new GitHubAutomationPrCandidate
                {
                    Number = 662,
                    Title = "Z4R-G329 implement thing",
                    Url = "https://github.com/J-Tech-Japan/intent-system/pull/662",
                    Body = "Closes #661",
                    CreatedAt = "2026-06-01T00:00:00Z",
                    UpdatedAt = "2026-06-01T00:00:00Z",
                    Labels = Array.Empty<GitHubAutomationLabel>(),
                    ClosingIssuesReferences = new[] { new GitHubPrClosingIssueReference { Number = 661 } },
                    State = "OPEN",
                    IsDraft = false
                }
            },
            issues: new[]
            {
                new GitHubAutomationIssueCandidate
                {
                    Number = 661,
                    Title = "Z4R-G329 implement thing",
                    Url = "https://github.com/J-Tech-Japan/intent-system/issues/661",
                    CreatedAt = "2026-06-01T00:00:00Z",
                    Labels = new[]
                    {
                        new GitHubAutomationLabel { Name = "intent-target" },
                        new GitHubAutomationLabel { Name = "intent-pr-created" }
                    },
                    State = "OPEN"
                }
            });
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "Z4R-G329" });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system",
             "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("stale-next-slice-reconcile", root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("mutation_allowed").GetBoolean());
        var recommended = root.GetProperty("recommended_command").GetString()!;
        Assert.Contains("automation reconcile", recommended, StringComparison.Ordinal);
        Assert.Contains("--lane next-slice", recommended, StringComparison.Ordinal);
        // Must NOT recommend the publish chain.
        Assert.DoesNotContain("packet draft", recommended, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NextSliceIssueCutReady_AdjacentLongerIdOnGitHub_DoesNotFalseMatch_StillPublishes_G444()
    {
        // G444 review fix: the duplicate guard must be token-boundary aware,
        // not a plain substring. A candidate `SKS-G67` must NOT be treated as
        // already-published just because an unrelated open issue is titled
        // `SKS-G670 ...`. The guard must fall through to the normal publish
        // lane (no real duplicate, no open intent-target work).
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: new[]
            {
                new GitHubAutomationIssueCandidate
                {
                    Number = 700,
                    Title = "SKS-G670 a different, longer execution unit",
                    Url = "https://github.com/J-Tech-Japan/SekibanAsAService/issues/700",
                    CreatedAt = "2026-06-01T00:00:00Z",
                    // No intent-target label: unrelated open issue, must not
                    // block the WIP cap either.
                    Labels = Array.Empty<GitHubAutomationLabel>(),
                    State = "OPEN"
                }
            });
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G67" });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        // Adjacent longer id must NOT trigger the stale-reconcile guard.
        Assert.NotEqual("stale-next-slice-reconcile", root.GetProperty("classification").GetString());
        Assert.Equal("publish-next-issue", root.GetProperty("classification").GetString());
        Assert.Contains("--execution-unit SKS-G67", root.GetProperty("recommended_command").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NextSliceProbeReturnsNoActionableItem_FallsThroughToTrueIdle()
    {
        // G318 acceptance: when next-slice has no candidate AND no other
        // host signal exists, true-idle remains a valid outcome.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("true-idle", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbe_IsNotInvoked_WhenOperatorPrePipesFlag()
    {
        // G318 backward-compat: pre-piped `--next-slice-issue-cut-ready`
        // wins; the probe is not invoked even when --domain is present.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        var probeWasInvoked = false;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "ignored", ExecutionUnit = "ignored" },
            onProbe: () => probeWasInvoked = true);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service",
             "--next-slice-issue-cut-ready",
             "--publish-next-execution-unit", "OPERATOR-PIPED",
             "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.False(probeWasInvoked, "operator-supplied next-slice flag must short-circuit the probe");
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("publish-next-issue", doc.RootElement.GetProperty("classification").GetString());
        Assert.Contains("--execution-unit OPERATOR-PIPED",
            doc.RootElement.GetProperty("recommended_command").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NextSliceProbeReturnsIssueCutReady_ButWipCapActive_BlocksPublish()
    {
        // G318 acceptance: even when next-slice is `issue-cut-ready`, an
        // open intent-target issue/PR must keep the WIP-cap-blocked
        // classification (publish-next-issue requires WIP cap empty).
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: new[] { NewIssue(900, labels: new[] { "intent-target" }) });
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G225" });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.Equal("wip-cap-blocked",
            JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbeReturnsClarificationRequired_SurfacesHardClarification_NotTrueIdle()
    {
        // G318 review fix (PR #744): a blocked next-slice outcome must not
        // collapse into `true-idle`. `clarification-required` from
        // `intent next-slice --dry-run` maps to the analyzer's
        // `hard-clarification` lane so the operator gets a precise stop
        // and a clarification-next pointer instead of an idle wake.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "clarification-required", ExecutionUnit = null });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("hard-clarification", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbeReturnsSkipDueToWip_SurfacesWipCapBlocked_NotTrueIdle()
    {
        // G318 review fix (PR #744): when queue-state authoritatively
        // reports `skip-next-slice-due-to-wip` (e.g. labels and queue
        // diverge), the command MUST surface `wip-cap-blocked` instead of
        // collapsing into `true-idle` because the GitHub label listing
        // happens to look empty.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "skip-next-slice-due-to-wip", ExecutionUnit = null });

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/SekibanAsAService",
             "--domain", "sekiban-as-a-service", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wip-cap-blocked", doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbe_IsNotInvoked_WhenDomainMissingAndConfigEmpty()
    {
        // G318 safety rail + G341 update: `intent next-slice --dry-run`
        // requires a domain. The probe is skipped only when neither
        // `--domain` is supplied nor the CliContext config carries a
        // configured domain. G341 added the config-domain fallback so
        // operators can run `automation host-loop-next-action --repo X`
        // without re-typing the domain every time.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        var probeWasInvoked = false;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "IGNORED" },
            onProbe: () => probeWasInvoked = true);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContextWithoutDomain(),
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.False(probeWasInvoked, "probe must not run when neither --domain nor config domain is available");
        Assert.Equal("true-idle",
            JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_NextSliceProbe_FallsBackToConfiguredDomain_WhenDomainFlagMissing()
    {
        // G341: when the operator omits `--domain`, the command falls
        // back to `context.Config.Project.Domain` so a real
        // `issue-cut-ready` next-slice candidate is surfaced as
        // `publish-next-issue` instead of `true-idle`. Mirrors the
        // SekibanAsAService SKS-G239 case in the G341 packet.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        var probeWasInvoked = false;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G239" },
            onProbe: () => probeWasInvoked = true);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopNextActionCommand.Execute(
            CreateContext(), // configured domain = "intent-cli"
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.True(probeWasInvoked, "probe must run when --domain is omitted but config carries a domain");
        var root = JsonDocument.Parse(writer.ToString()).RootElement;
        Assert.Equal("publish-next-issue", root.GetProperty("classification").GetString());
        Assert.Contains("SKS-G239", root.GetProperty("recommended_command").GetString(), StringComparison.Ordinal);
        Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
        // The emitted domain field should carry the fallback so the
        // operator can see which domain drove the classification.
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
    }

    [Fact]
    public void Execute_G364_PublishNextIssue_SurfacesCandidateExecutionUnitField()
    {
        // G364 AC: when the analyzer selects the `publish-next-issue`
        // lane, the JSON output must expose the chosen execution unit
        // as a top-level `candidate_execution_unit` field — not only
        // inside `recommended_command` / `evidence`. This lets host
        // loop wake scripts and dashboards read the next-slice target
        // deterministically without parsing the command string.
        // Captures the observed SekibanAsAService SKS-G403 shape.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G403" });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal("publish-next-issue", root.GetProperty("classification").GetString());
            Assert.Equal("SKS-G403", root.GetProperty("candidate_execution_unit").GetString());
            Assert.Contains("SKS-G403", root.GetProperty("recommended_command").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G364_NonPublishLane_CandidateExecutionUnitFieldIsNull()
    {
        // G364: `candidate_execution_unit` is only meaningful for lanes
        // where a specific next-slice / prepared-packet unit was chosen.
        // For all other classifications (e.g. true-idle, wip-cap-blocked)
        // the field must be null so consumers can deterministically
        // detect "no specific candidate selected".
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal(JsonValueKind.Null, root.GetProperty("candidate_execution_unit").ValueKind);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_PublishRecoveryProbe_SurfacesRepairHostMetadata_WhenSafeRepairsAvailable()
    {
        // G342: when `automation publish-recovery --dry-run` reports
        // safe_repairs > 0 (the deterministic `linked_pr` recovery
        // case), the host loop must surface `repair-host-metadata`
        // instead of falling through to `true-idle`.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 1, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationClean });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal("repair-host-metadata", root.GetProperty("classification").GetString());
            Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
            Assert.Contains("publish-recovery", root.GetProperty("recommended_command").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_PublishRecoveryProbe_LaneSuppressed_WhenUnsafeStopsPresent()
    {
        // G342 review fix: when the probe returns safe_repair_count > 0 but
        // unsafe_stop_count > 0, publish-recovery --write refuses all mutations,
        // so the repair-host-metadata lane must be suppressed and the host loop
        // must fall through to true-idle (or the next applicable lane).
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        // Mixed: 1 safe repair available BUT 1 unsafe stop also present →
        // publish-recovery --write would refuse to run → lane suppressed.
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 1, UnsafeStopCount = 1 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationClean });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var classification = JsonDocument.Parse(writer.ToString()).RootElement
                .GetProperty("classification").GetString();
            // Must NOT recommend repair-host-metadata — the write would be a no-op.
            Assert.NotEqual("repair-host-metadata", classification);
            // With no other signals, falls through to true-idle.
            Assert.Equal("true-idle", classification);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_HostSyncPreflightProbe_SurfacesSafeStash_WhenDirtyUnrelatedSubmodule()
    {
        // G342: when host-sync-preflight reports
        // `dirty-unrelated-submodule` (the recoverable workspace
        // case), the host loop must surface `safe-stash` with the
        // `workspace-guard --mode begin --write` recommendation
        // instead of falling through to `true-idle`.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationDirtyUnrelatedSubmodule });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal("safe-stash", root.GetProperty("classification").GetString());
            Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
            Assert.Contains("workspace-guard", root.GetProperty("recommended_command").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_HostSyncPreflightProbe_SurfacesDirtyHostState_WhenDurableStateDirty()
    {
        // G342: when host-sync-preflight reports
        // `dirty-host-durable-state` (NOT recoverable — operator
        // must commit/revert), the host loop must surface
        // `dirty-host-state` with `mutation_allowed: false` and a
        // structured stop. This is the unambiguous-block lane that
        // protects against publishing on top of dirty durable state.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationDirtyDurableState });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal("dirty-host-state", root.GetProperty("classification").GetString());
            Assert.False(root.GetProperty("mutation_allowed").GetBoolean());
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_TrueIdle_RemainsPossible_WhenAllSafeRepairProbesIdle()
    {
        // G342 acceptance: `true-idle` is still emitted when every
        // safe-repair probe (next-slice, publish-recovery,
        // host-sync-preflight) reports no actionable signal.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationClean });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.Equal("true-idle",
                JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_OperatorSupplied_PublishRecoveryRepairs_BypassesProbe()
    {
        // G342: when the operator pre-supplies
        // `--publish-recovery-repairs <N>`, the probe is skipped so
        // upstream tooling that already computed the count routes
        // through the operator value.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        var probeInvoked = false;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            canned: null) { };
        // Use a probe that records invocation so we can assert it was bypassed.
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ =>
        {
            probeInvoked = true;
            return new FakePublishRecoveryProbe(new PublishRecoveryProbeResult { SafeRepairCount = 99, UnsafeStopCount = 0 });
        };

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--publish-recovery-repairs", "1", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.False(probeInvoked, "publish-recovery probe must not run when --publish-recovery-repairs is operator-supplied");
            // The operator-supplied value (1) wins, surfacing
            // repair-host-metadata regardless of probe.
            Assert.Equal("repair-host-metadata",
                JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
        }
        finally
        {
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G358_CloseoutDriftCheckProbe_SurfacesRepairHostMetadata_WhenSafeRepairsAvailable()
    {
        // G358: when `automation closeout-drift-check --dry-run` reports
        // safe_repair_count > 0 (items with linked_issue but no linked_pr where
        // GitHub confirms a single merged closing PR), the host loop must surface
        // `repair-host-metadata` with the closeout-drift-check --write recommendation
        // instead of falling through to `true-idle`.
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationClean });
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = _ => new FakeCloseoutDriftCheckProbe(
            new CloseoutDriftCheckProbeResult { SafeRepairCount = 1, UnsafeStopCount = 0 });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var root = JsonDocument.Parse(writer.ToString()).RootElement;
            Assert.Equal("repair-host-metadata", root.GetProperty("classification").GetString());
            Assert.True(root.GetProperty("mutation_allowed").GetBoolean());
            Assert.Contains(
                "closeout-drift-check",
                root.GetProperty("recommended_command").GetString() ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Contains(
                "--write",
                root.GetProperty("recommended_command").GetString() ?? string.Empty,
                StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G358_CloseoutDriftCheckProbe_TrueIdleWhenNoRepairs()
    {
        // G358: closeout-drift-check probe returning 0 repairs must not prevent
        // the host loop from reaching true-idle (regression guard).
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationClean });
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = _ => new FakeCloseoutDriftCheckProbe(
            new CloseoutDriftCheckProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.Equal(
                "true-idle",
                JsonDocument.Parse(writer.ToString()).RootElement.GetProperty("classification").GetString());
        }
        finally
        {
            AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G358_CloseoutDriftCheckProbe_LaneSuppressed_WhenUnsafeStopsPresent()
    {
        // G358 review fix: when the probe returns safe_repair_count > 0 but
        // unsafe_stop_count > 0, closeout-drift-check --write will refuse all
        // mutations, so the repair-host-metadata lane must be suppressed and the
        // host loop must fall through to true-idle (or the next applicable lane).
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FakePublishRecoveryProbe(
            new PublishRecoveryProbeResult { SafeRepairCount = 0, UnsafeStopCount = 0 });
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = _ => new FakeHostSyncPreflightProbe(
            new HostSyncPreflightProbeResult { Classification = HostSyncPreflightAnalyzer.ClassificationClean });
        // Mixed: 1 safe repair available BUT 1 unsafe stop also present →
        // closeout-drift-check --write would refuse to run → lane suppressed.
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = _ => new FakeCloseoutDriftCheckProbe(
            new CloseoutDriftCheckProbeResult { SafeRepairCount = 1, UnsafeStopCount = 1 });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(),
                ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            var classification = JsonDocument.Parse(writer.ToString()).RootElement
                .GetProperty("classification").GetString();
            // Must NOT recommend repair-host-metadata — the write would be a no-op.
            Assert.NotEqual("repair-host-metadata", classification);
            // With no other signals, falls through to true-idle.
            Assert.Equal("true-idle", classification);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
            AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
            AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G365_HostBinding_Match_DerivesDomainAndPublishesNextIssue()
    {
        // G365: when --domain is omitted but --repo matches the host's
        // host-binding.toml target_repo, the binding's domain is used
        // for the next-slice probe so a real `issue-cut-ready` candidate
        // surfaces as `publish-next-issue` instead of `design-needed`.
        // Mirrors the observed SekibanAsAService SKS-G406 case where the
        // operator runs the host loop without typing the domain.
        string? probedDomain = null;
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = (_, repo) =>
            HostBindingDomainResolution.Match("sekiban-as-a-service", "/host/.intent-cli/host-binding.toml");
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeProbeRecorder(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "SKS-G406" },
            (_, d) => probedDomain = d);

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContextWithoutDomain(),
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.Equal("sekiban-as-a-service", probedDomain);
            using var doc = JsonDocument.Parse(writer.ToString());
            var root = doc.RootElement;
            Assert.Equal("publish-next-issue", root.GetProperty("classification").GetString());
            Assert.Equal("SKS-G406", root.GetProperty("candidate_execution_unit").GetString());
            var recommended = root.GetProperty("recommended_command").GetString()!;
            Assert.Contains("--execution-unit SKS-G406", recommended, StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = null;
            AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G365_HostBinding_Mismatch_EmitsMissingDomainBindingClassification()
    {
        // G365: when --domain is omitted and the host-binding records a
        // different target_repo than --repo, the command MUST surface
        // `missing-domain-binding` rather than silently fall back to the
        // configured domain. The probe MUST NOT be invoked (it would
        // run against the wrong domain).
        var probeWasInvoked = false;
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = (_, _) =>
            HostBindingDomainResolution.Mismatch(
                domain: "intent-cli",
                boundTargetRepo: "J-Tech-Japan/intent-system",
                bindingPath: "/host/.intent-cli/host-binding.toml");
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "issue-cut-ready", ExecutionUnit = "WRONG-DOMAIN" },
            onProbe: () => probeWasInvoked = true);

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContextWithoutDomain(),
                ["--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.False(probeWasInvoked, "next-slice probe must not run when host-binding records a different target_repo");
            using var doc = JsonDocument.Parse(writer.ToString());
            var root = doc.RootElement;
            Assert.Equal("missing-domain-binding", root.GetProperty("classification").GetString());
            Assert.False(root.GetProperty("mutation_allowed").GetBoolean());
            var recommended = root.GetProperty("recommended_command").GetString()!;
            Assert.Contains("--domain <DOMAIN>", recommended, StringComparison.Ordinal);
            var evidence = string.Join(' ',
                root.GetProperty("evidence").EnumerateArray().Select(e => e.GetString()));
            Assert.Contains("J-Tech-Japan/intent-system", evidence, StringComparison.Ordinal);
            Assert.Contains("J-Tech-Japan/SekibanAsAService", evidence, StringComparison.Ordinal);
        }
        finally
        {
            AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = null;
            AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G365_HostBinding_Missing_FallsBackToConfiguredDomain()
    {
        // G365: when no host-binding is present, the existing G341
        // fallback to `context.Config.Project.Domain` MUST remain
        // byte-identical. Pre-G365 hosts (no binding file) keep
        // working exactly as they did under G341.
        string? probedDomain = null;
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = (_, _) =>
            HostBindingDomainResolution.Missing("(no binding file)");
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeProbeRecorder(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null },
            (_, d) => probedDomain = d);

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContext(), // configured domain = "intent-cli"
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.Equal("intent-cli", probedDomain);
            using var doc = JsonDocument.Parse(writer.ToString());
            Assert.Equal("true-idle", doc.RootElement.GetProperty("classification").GetString());
        }
        finally
        {
            AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = null;
            AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    [Fact]
    public void Execute_G365_ExplicitDomainFlag_BypassesHostBindingLookup()
    {
        // G365: an operator-supplied `--domain` is authoritative; the
        // host-binding resolver MUST NOT be invoked. This preserves the
        // pre-G365 contract that an explicit domain flag wins.
        var resolverInvoked = false;
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => new FakeLister(
            prs: Array.Empty<GitHubAutomationPrCandidate>(),
            issues: Array.Empty<GitHubAutomationIssueCandidate>());
        AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = (_, _) =>
        {
            resolverInvoked = true;
            return HostBindingDomainResolution.Mismatch(
                "wrong-domain", "wrong-repo", "/host/.intent-cli/host-binding.toml");
        };
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FakeNextSliceProbe(
            new NextSliceProbeResult { RecommendedOutcome = "no-actionable-item", ExecutionUnit = null });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(
                CreateContextWithoutDomain(),
                ["--repo", "owner/repo",
                 "--domain", "operator-supplied",
                 "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.False(resolverInvoked, "host-binding resolver must not run when --domain is supplied");
        }
        finally
        {
            AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = null;
            AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        }
    }

    /// <summary>
    /// G365: stand-in for the next-slice probe that records the
    /// (repo, domain) pair the command supplied. Used by tests that
    /// assert the derived domain reaches the probe.
    /// </summary>
    private sealed class FakeProbeRecorder : INextSliceDryRunProbe
    {
        private readonly NextSliceProbeResult? _canned;
        private readonly Action<string, string> _onProbeArgs;
        public FakeProbeRecorder(NextSliceProbeResult? canned, Action<string, string> onProbeArgs)
        {
            _canned = canned;
            _onProbeArgs = onProbeArgs;
        }
        public NextSliceProbeResult? Probe(string repo, string domain)
        {
            _onProbeArgs(repo, domain);
            return _canned;
        }
    }

    private static CliContext CreateContextWithoutDomain() =>
        new()
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = string.Empty,
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };

    /// <summary>
    /// G342: deterministic stand-in for the publish-recovery probe.
    /// Returns the canned safe-repair / unsafe-stop counts the host
    /// loop tests need to drive the analyzer's `repair-host-metadata`
    /// lane without touching live queue-state.
    /// </summary>
    private sealed class FakePublishRecoveryProbe : IPublishRecoveryProbe
    {
        private readonly PublishRecoveryProbeResult? _canned;
        public FakePublishRecoveryProbe(PublishRecoveryProbeResult? canned) { _canned = canned; }
        public PublishRecoveryProbeResult? Probe(string repo) => _canned;
    }

    /// <summary>
    /// G342: deterministic stand-in for the host-sync-preflight probe.
    /// Tests drive `safe-stash` / `dirty-host-state` lanes by canning
    /// classifications without touching the local git working tree.
    /// </summary>
    private sealed class FakeHostSyncPreflightProbe : IHostSyncPreflightProbe
    {
        private readonly HostSyncPreflightProbeResult? _canned;
        public FakeHostSyncPreflightProbe(HostSyncPreflightProbeResult? canned) { _canned = canned; }
        public HostSyncPreflightProbeResult? Probe() => _canned;
    }

    private sealed class FakeNextSliceProbe : INextSliceDryRunProbe
    {
        private readonly NextSliceProbeResult? _canned;
        private readonly Action? _onProbe;

        public FakeNextSliceProbe(NextSliceProbeResult? canned, Action? onProbe = null)
        {
            _canned = canned;
            _onProbe = onProbe;
        }

        public NextSliceProbeResult? Probe(string repo, string domain)
        {
            _onProbe?.Invoke();
            return _canned;
        }
    }

    /// <summary>
    /// G358: deterministic stand-in for the closeout-drift-check probe.
    /// Returns a canned safe-repair count so the host-loop tests can drive
    /// the `repair-host-metadata` lane without touching live queue-state or network.
    /// </summary>
    private sealed class FakeCloseoutDriftCheckProbe : ICloseoutDriftCheckProbe
    {
        private readonly CloseoutDriftCheckProbeResult? _canned;
        public FakeCloseoutDriftCheckProbe(CloseoutDriftCheckProbeResult? canned) { _canned = canned; }
        public CloseoutDriftCheckProbeResult? Probe(string repo) => _canned;
    }

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;

        public FakeLister(
            IReadOnlyList<GitHubAutomationPrCandidate> prs,
            IReadOnlyList<GitHubAutomationIssueCandidate> issues)
        {
            this.prs = prs;
            this.issues = issues;
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }

    private static CliContext CreateContext() =>
        CreateContext(Path.GetTempPath());

    private static CliContext CreateContext(string repoRoot) =>
        new()
        {
            RepoRoot = repoRoot,
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
