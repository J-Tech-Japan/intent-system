using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class ApprovalPostingOverrideG861Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G861";
    private const int PullRequest = 1883;
    private const string Head = "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a";
    private const string OverrideId = "d2c07755-2db3-4ad5-8ee7-3c9ef7d17bce";
    private const string ExpectedUnflaggedMissingJson = """
{
  "repo": "J-Tech-Japan/intent-system",
  "pr": 1883,
  "transition": "approved",
  "mode": "dry-run",
  "applied": false,
  "add_labels": [],
  "addLabels": [],
  "remove_labels": [],
  "removeLabels": [],
  "current_labels": [
    "intent-pr-reviewing"
  ],
  "currentLabels": [
    "intent-pr-reviewing"
  ],
  "summary": "Refused host PR transition \u0027approved\u0027 on PR #1883 in J-Tech-Japan/intent-system: review-missing. No labels were changed.",
  "ci_wait_cleared": false,
  "may_have_applied": false,
  "mayHaveApplied": false,
  "intended_labels": null,
  "intendedLabels": null,
  "recovery_command": null,
  "recoveryCommand": null,
  "error": "review-missing: The existing G834 gate is satisfied, but a currently deciding canonical posted approve is missing for relation(s): cross-runtime. Post the rendered canonical review body for the deciding local record.",
  "cross_runtime_review": {
    "decision": "satisfied",
    "reasons": [],
    "execution_unit": "G861",
    "domain": "intent-cli",
    "team": "intent-cli-dev"
  },
  "solo_conductor_review": {
    "decision": "refused",
    "cause": "review-missing",
    "detail": "The existing G834 gate is satisfied, but a currently deciding canonical posted approve is missing for relation(s): cross-runtime. Post the rendered canonical review body for the deciding local record.",
    "execution_unit": "G861",
    "domain": "intent-cli",
    "team": "intent-cli-dev",
    "expected_head_sha": "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a",
    "observed_head_sha": "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a",
    "qualifying_reviews": [
      {
        "review_id": 99301,
        "url": "https://github.com/J-Tech-Japan/intent-system/pull/1883#pullrequestreview-99301",
        "login": "independent-reviewer",
        "runtime": "claude",
        "relation": "same-runtime",
        "cited_record_path": ".intent-cli/cross-runtime-reviews/j-tech-japan__intent-system/pr-1883/20261010T1200010000000Z-claude-1a1a1a1.json"
      }
    ],
    "obligations": [],
    "superseded_invalid_review_ids": [],
    "unscopable_review_ids": []
  }
}
""";
    private const string ExpectedUnflaggedMissingText = """
review-missing: The existing G834 gate is satisfied, but a currently deciding canonical posted approve is missing for relation(s): cross-runtime. Post the rendered canonical review body for the deciding local record.
mode: dry-run
repo: J-Tech-Japan/intent-system
pr: 1883
applied: false
ci_wait_cleared: false
cross_runtime_review: satisfied
solo_conductor_review: refused (review-missing)
solo_conductor_identity: execution_unit=G861 domain=intent-cli team=intent-cli-dev
solo_conductor_head: expected=<HEAD> observed=<HEAD>
solo_conductor_qualifying_review: review_id=99301 login=independent-reviewer url=https://github.com/J-Tech-Japan/intent-system/pull/1883#pullrequestreview-99301 runtime=claude relation=same-runtime cited_record_path=.intent-cli/cross-runtime-reviews/j-tech-japan__intent-system/pr-1883/20261010T1200010000000Z-claude-1a1a1a1.json
solo_conductor_superseded_invalid_review_ids: (none)
solo_conductor_unscopable_review_ids: (none)
""";

    private readonly string tempRoot = Directory.CreateTempSubdirectory("g861-approval-override-test-").FullName;
    private readonly Func<IGitHubLabelMutator>? previousMutatorFactory;
    private readonly Func<string, int, string>? previousHeadReader;
    private readonly Func<SoloConductorReviewReadRequest, SoloConductorReviewReadResult>? previousSoloReader;
    private readonly Func<string, string, ClaimOwnershipVerification>? previousClaimReader;
    private readonly Func<DateTimeOffset>? previousReviewClock;
    private readonly ApprovalPostingOverride.WriterOperationsHooks previousWriterOperations;
    private readonly RecordingMutator mutator = new();
    private string currentHead = Head;
    private int headReadCount;
    private int reviewClockSequence;
    private Func<CrossRuntimeReviewStoredRecord, bool> postedRecordSelector = item => item.Record.Runtime == "claude";
    private string? postedStateOverride { get; set; }
    private Exception? postedReaderException { get; set; }
    private SoloConductorReviewReadResult? postedReadResultOverride { get; set; }
    private Func<SoloConductorReviewReadRequest, IReadOnlyList<IndependentReviewRowEvidence>>? postedRowsOverride { get; set; }
    private Action<int>? headReadAction { get; set; }

    public ApprovalPostingOverrideG861Tests()
    {
        previousMutatorFactory = AutomationPrTransitionCommand.MutatorFactory;
        previousHeadReader = AutomationPrTransitionCommand.PrHeadReader;
        previousSoloReader = AutomationPrTransitionCommand.SoloReviewReader;
        previousClaimReader = CrossRuntimeReviewTeamResolver.ClaimReader;
        previousReviewClock = ReviewCrossRuntimeCommand.Clock;
        previousWriterOperations = ApprovalPostingOverride.WriterOperations;
        ApprovalPostingOverride.WriterOperations = ApprovalPostingOverride.WriterOperationsHooks.Default;
        AutomationPrTransitionCommand.MutatorFactory = () => mutator;
        AutomationPrTransitionCommand.PrHeadReader = (_, _) =>
        {
            var call = Interlocked.Increment(ref headReadCount);
            headReadAction?.Invoke(call);
            return currentHead;
        };
        AutomationPrTransitionCommand.SoloReviewReader = ReadCanonicalPostedReviews;
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        ReviewCrossRuntimeCommand.Clock = () => new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero)
            .AddSeconds(Interlocked.Increment(ref reviewClockSequence));
    }

    public void Dispose()
    {
        AutomationPrTransitionCommand.MutatorFactory = previousMutatorFactory;
        AutomationPrTransitionCommand.PrHeadReader = previousHeadReader;
        AutomationPrTransitionCommand.SoloReviewReader = previousSoloReader;
        CrossRuntimeReviewTeamResolver.ClaimReader = previousClaimReader;
        ReviewCrossRuntimeCommand.Clock = previousReviewClock;
        ApprovalPostingOverride.WriterOperations = previousWriterOperations;
        try { Directory.Delete(tempRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void ActualCanonicalWriters_PublishPreparedBeforeLabels_ThenPublishObservedPair_G861()
    {
        using var repos = CreateEligibleRepositories();
        var ciWaitPath = RecordCiWait(repos.Caller);
        var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
        var recordsBefore = PublishedRecordBytes(repos);
        var runPath = ScopedRunPath(repos.Caller);
        var path = AuditPath("prepared.json");
        var preparedChecksAtMutation = 0;
        mutator.BeforeApply = () =>
        {
            var remotePrepared = GitBytes(repos.Bare, "show", $"refs/heads/main:{path}");
            Assert.NotEmpty(remotePrepared);
            var events = RunLogSerializer.DeserializeAll(Encoding.UTF8.GetString(GitBytes(repos.Bare, "show", $"refs/heads/main:{runPath}")));
            var preparedEvent = Assert.Single(events, item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
            Assert.Equal(path, preparedEvent.ResultRef);
            Assert.Single(events, item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
            preparedChecksAtMutation++;
        };

        var (ordinaryExit, ordinaryOutput) = Run(repos, OverrideId, write: false, flagged: false);
        Assert.Equal(1, ordinaryExit);
        Assert.Equal(Encoding.UTF8.GetBytes(ExpectedUnflaggedMissingJson + Environment.NewLine), Encoding.UTF8.GetBytes(ordinaryOutput));
        var (ordinaryTextExit, ordinaryTextOutput) = Run(repos, OverrideId, write: false, flagged: false, format: "text");
        Assert.Equal(1, ordinaryTextExit);
        var expectedTextBytes = Encoding.UTF8.GetBytes(ExpectedUnflaggedMissingText.Replace("<HEAD>", Head, StringComparison.Ordinal) + Environment.NewLine);
        Assert.Equal(expectedTextBytes, Encoding.UTF8.GetBytes(ordinaryTextOutput));
        Assert.True(ordinaryOutput.TrimStart().StartsWith('{'), ordinaryOutput);
        using (var ordinary = JsonDocument.Parse(ordinaryOutput))
        {
            var solo = ordinary.RootElement.GetProperty("solo_conductor_review");
            Assert.Equal(SoloConductorApprovalGate.CauseReviewMissing, solo.GetProperty("cause").GetString());
            Assert.False(solo.TryGetProperty("missing_relations", out _));
            Assert.False(ordinary.RootElement.TryGetProperty("approval_posting_override", out _));
        }
        Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));

        var refBeforePreview = GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var (previewExit, previewOutput) = Run(repos, OverrideId, write: false, flagged: true);
        Assert.True(previewExit == 0, previewOutput);
        using (var preview = JsonDocument.Parse(previewOutput))
        {
            Assert.False(preview.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal(SoloConductorApprovalGate.CauseReviewMissing,
                preview.RootElement.GetProperty("solo_conductor_review").GetProperty("cause").GetString());
            var facts = preview.RootElement.GetProperty("approval_posting_override");
            Assert.Equal("eligible-preview", facts.GetProperty("disposition").GetString());
            Assert.False(facts.GetProperty("prepared_published").GetBoolean());
            Assert.False(facts.GetProperty("outcome_published").GetBoolean());
            Assert.Null(preview.RootElement.GetProperty("error").GetString());
            Assert.Contains("Eligible preview", preview.RootElement.GetProperty("summary").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Refused approved transition", preview.RootElement.GetProperty("summary").GetString(), StringComparison.Ordinal);
        }
        Assert.Equal(refBeforePreview, GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Empty(mutator.Applied);
        Assert.False(repos.HasPath("refs/heads/main", path));
        Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));

        var stagedSentinel = Path.Combine(repos.Caller, "g861-preview-staged-sentinel.txt");
        var untrackedSentinel = Path.Combine(repos.Caller, "g861-preview-untracked-sentinel.txt");
        File.WriteAllText(stagedSentinel, "staged caller bytes\n");
        File.WriteAllText(untrackedSentinel, "untracked caller bytes\n");
        Git(repos.Caller, "add", "--", Path.GetFileName(stagedSentinel));
        var staleMarker = Path.Combine(tempRoot, "preexisting-stale-g861-temp-marker");
        File.WriteAllText(staleMarker, "leave invocation-unrelated temp state alone\n");
        var callerHeadBeforePreview = GitText(repos.Caller, "rev-parse", "HEAD");
        var callerRefsBeforePreview = GitText(repos.Caller, "show-ref", "--head");
        var callerStatusBeforePreview = GitText(repos.Caller, "status", "--porcelain=v1", "--untracked-files=all");
        var callerIndexBeforePreview = File.ReadAllBytes(Path.Combine(repos.Caller, ".git", "index"));
        var (dirtyPreviewExit, dirtyPreviewOutput) = Run(repos, Guid.NewGuid().ToString("D"), write: false, flagged: true);
        Assert.True(dirtyPreviewExit == 0, dirtyPreviewOutput);
        AssertOverrideFacts(dirtyPreviewOutput, "eligible-missing-posting", "eligible-preview", prepared: false, outcome: false, mutationAttempted: false);
        Assert.Equal(callerHeadBeforePreview, GitText(repos.Caller, "rev-parse", "HEAD"));
        Assert.Equal(callerRefsBeforePreview, GitText(repos.Caller, "show-ref", "--head"));
        Assert.Equal(callerStatusBeforePreview, GitText(repos.Caller, "status", "--porcelain=v1", "--untracked-files=all"));
        Assert.Equal(callerIndexBeforePreview, File.ReadAllBytes(Path.Combine(repos.Caller, ".git", "index")));
        Assert.True(File.Exists(staleMarker));
        Assert.Equal(refBeforePreview, GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim());

        var (writeExit, writeOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.True(writeExit == 0, writeOutput);
        using var result = JsonDocument.Parse(writeOutput);
        Assert.True(result.RootElement.GetProperty("applied").GetBoolean());
        var finalFacts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("observed", finalFacts.GetProperty("disposition").GetString());
        Assert.True(finalFacts.GetProperty("prepared_published").GetBoolean());
        Assert.True(finalFacts.GetProperty("outcome_published").GetBoolean());
        Assert.True(finalFacts.GetProperty("mutation_attempted").GetBoolean());
        Assert.True(finalFacts.GetProperty("label_state_observed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, finalFacts.GetProperty("recovery_command").ValueKind);
        Assert.True(result.RootElement.GetProperty("ci_wait_cleared").GetBoolean());
        Assert.Empty(CiWaitStore.ReadOpen(repos.Caller, repo: Repo).Records);
        Assert.NotEqual(Convert.ToBase64String(ciWaitBefore), Convert.ToBase64String(File.ReadAllBytes(ciWaitPath)));
        Assert.Equal(1, preparedChecksAtMutation);
        Assert.Single(mutator.Applied);
        Assert.Contains("intent-pr-approved", mutator.Labels);
        Assert.Equal(recordsBefore.OrderBy(item => item.Key), PublishedRecordBytes(repos).OrderBy(item => item.Key));

        var preparedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{path}");
        var observedPath = AuditPath("observed.json");
        var observedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{observedPath}");
        using var prepared = JsonDocument.Parse(preparedBytes);
        using var observed = JsonDocument.Parse(observedBytes);
        Assert.Equal("approved-posting-exception", prepared.RootElement.GetProperty("operation").GetString());
        Assert.Equal(OverrideId, prepared.RootElement.GetProperty("override_id").GetString());
        Assert.Equal(Unit, prepared.RootElement.GetProperty("execution_unit").GetString());
        Assert.Equal("intent-cli", prepared.RootElement.GetProperty("domain").GetString());
        Assert.Equal(Team, prepared.RootElement.GetProperty("team").GetString());
        Assert.Equal(Repo, prepared.RootElement.GetProperty("target_repo").GetString());
        Assert.Equal(PullRequest, prepared.RootElement.GetProperty("pull_request").GetInt32());
        Assert.Equal(Head, prepared.RootElement.GetProperty("requested_head_sha").GetString());
        Assert.Equal("review-missing", prepared.RootElement.GetProperty("original_solo_result").GetProperty("cause").GetString());
        Assert.False(prepared.RootElement.GetProperty("original_solo_result").TryGetProperty("missing_relations", out _));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(preparedBytes)).ToLowerInvariant(),
            observed.RootElement.GetProperty("prepared_sha256").GetString());
        Assert.Equal(finalFacts.GetProperty("prepared_commit").GetString(),
            observed.RootElement.GetProperty("prepared_publication_commit").GetString());
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ExecutionUnit == Unit);
        Assert.True(repos.HasPath("refs/heads/main", ClaimCommand.ClaimPath($"execution-unit:{Unit}")));
        Assert.Equal("owned", ClaimOwnershipVerifier.Verify(repos.Caller, $"execution-unit:{Unit}", Team).Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullCanonicalQueueItemsRefuseWithoutLosingPreparedHistory_G861(bool nullItems, bool afterPrepared)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var ciWaitPath = RecordCiWait(repos.Caller);
        var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
        byte[]? preparedBefore = null;
        byte[]? runBefore = null;
        string? selectedRun = null;
        var actionCountBeforeInvalidQueue = 0;
        if (afterPrepared)
        {
            mutator.ThrowBeforeApply = true;
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, firstExit);
            AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
            preparedBefore = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}");
            using var prepared = JsonDocument.Parse(preparedBefore);
            selectedRun = prepared.RootElement.GetProperty("selected_run_log_path").GetString();
            runBefore = GitBytes(repos.Bare, "show", $"refs/heads/main:{selectedRun}");
            mutator.ThrowBeforeApply = false;
            actionCountBeforeInvalidQueue = mutator.Applied.Count;
        }

        var queueRelative = Path.GetRelativePath(repos.Caller,
            RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Caller, Domain, Repo));
        PublishCanonicalChange(repos, root =>
        {
            var path = Path.Combine(root, queueRelative);
            var queue = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (nullItems) queue["items"] = null;
            else queue["items"]!.AsArray().Add((JsonNode?)null);
            File.WriteAllText(path, queue.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        });

        var (exit, output) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, exit);
        AssertOverrideFacts(output, "canonical-queue-invalid", "refused", prepared: afterPrepared, outcome: false, mutationAttempted: false);
        Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));
        Assert.Equal(actionCountBeforeInvalidQueue, mutator.Applied.Count);
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
        if (afterPrepared)
        {
            Assert.Equal(preparedBefore, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
            Assert.Equal(runBefore, GitBytes(repos.Bare, "show", $"refs/heads/main:{selectedRun}"));
            Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        }
        else
        {
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
            Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("head_after_action", "null", "override-audit-unavailable")]
    [InlineData("head_after_action", "wrong-head", "override-binding-conflict")]
    [InlineData("labels_observed", "empty", "override-binding-conflict")]
    [InlineData("target_repo", "case-drift", "override-binding-conflict")]
    public void ObservedAuditMustMatchTheImmutableHeadAndConvergedLabels_G861(string field, string replacement, string cause)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.True(firstExit == 0, firstOutput);
        var ciWaitPath = RecordCiWait(repos.Caller);
        var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
        var preparedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}");
        var runPath = JsonDocument.Parse(preparedBytes).RootElement.GetProperty("selected_run_log_path").GetString()!;
        PublishCanonicalChange(repos, root =>
        {
            var path = Path.Combine(root, AuditPath("observed.json").Replace('/', Path.DirectorySeparatorChar));
            var observed = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            switch (replacement)
            {
                case "null": observed[field] = null; break;
                case "wrong-head": observed[field] = new string('e', 40); break;
                case "empty": observed[field] = new JsonArray(); break;
                case "case-drift":
                    var changedRepo = Repo.ToLowerInvariant();
                    observed[field] = changedRepo;
                    var runFile = Path.Combine(root, runPath.Replace('/', Path.DirectorySeparatorChar));
                    var events = RunLogSerializer.DeserializeAll(File.ReadAllText(runFile));
                    var changedEvents = events.Select(item => item.ResultRef == AuditPath("observed.json")
                        ? item with { Repo = changedRepo, LinkedPr = $"https://github.com/{changedRepo}/pull/{PullRequest}" }
                        : item);
                    File.WriteAllText(runFile, string.Concat(changedEvents.Select(item => RunLogSerializer.SerializeLine(item) + "\n")));
                    break;
                default: throw new InvalidOperationException($"Unknown fixture replacement '{replacement}'.");
            }
            File.WriteAllText(path, observed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        });
        var observedBefore = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("observed.json")}");
        var runBefore = GitBytes(repos.Bare, "show", $"refs/heads/main:{runPath}");
        var actionsBefore = mutator.Applied.Count;

        foreach (var write in new[] { false, true })
        {
            var (exit, output) = Run(repos, OverrideId, write, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, cause, "refused", prepared: true, outcome: true, mutationAttempted: false);
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("applied").GetBoolean());
            Assert.False(result.RootElement.GetProperty("ci_wait_cleared").GetBoolean());
            Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));
            Assert.Equal(actionsBefore, mutator.Applied.Count);
            Assert.Equal(preparedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
            Assert.Equal(observedBefore, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("observed.json")}"));
            Assert.Equal(runBefore, GitBytes(repos.Bare, "show", $"refs/heads/main:{runPath}"));
        }
    }

    [Fact]
    public void LegacyPreparedUuidUsesExactRepositorySpellingForResume_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            RemoveScopedQueueAndRun(repos);
            var ciWaitPath = RecordCiWait(repos.Caller);
            var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
            mutator.ThrowBeforeApply = true;
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, firstExit);
            AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
            var preparedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}");
            var prepared = JsonDocument.Parse(preparedBytes);
            var legacyRun = prepared.RootElement.GetProperty("selected_run_log_path").GetString()!;
            var runBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{legacyRun}");
            var actionsBefore = mutator.Applied.Count;
            mutator.ThrowBeforeApply = false;

            var (caseDriftExit, caseDriftOutput) = Run(repos, OverrideId, write: false, flagged: true,
                repo: Repo.ToLowerInvariant());
            Assert.Equal(1, caseDriftExit);
            AssertOverrideFacts(caseDriftOutput, "override-binding-conflict", "refused", prepared: true, outcome: false, mutationAttempted: false);
            Assert.Equal(actionsBefore, mutator.Applied.Count);
            Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));
            Assert.Equal(preparedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
            Assert.Equal(runBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{legacyRun}"));

            var (exactExit, exactOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.True(exactExit == 0, exactOutput);
            Assert.Equal(preparedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
            Assert.True(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            RemoveScopedQueueAndRun(repos);
            mutator.ThrowBeforeApply = true;
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, firstExit);
            AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
            var preparedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}");
            mutator.ThrowBeforeApply = false;
            var (exactExit, exactOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.True(exactExit == 0, exactOutput);
            Assert.Equal(preparedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
            Assert.True(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentObservedAuditConflictRemainsVisibleWithoutAuthorizingOrClearingCiWait_G861(bool failLiveReadOnRetry)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var ciWaitPath = RecordCiWait(repos.Caller);
        var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
        var observedPath = AuditPath("observed.json");
        var runPath = ScopedRunPath(repos.Caller);
        byte[]? intendedObservedBytes = null;
        byte[]? remoteObservedBytes = null;
        byte[]? remoteRunBytes = null;
        var injected = 0;
        var previous = ApprovalPostingOverride.WriterOperations;
        ApprovalPostingOverride.WriterOperations = previous with
        {
            SerializeAudit = (path, serialize) =>
            {
                var bytes = serialize();
                if (path == observedPath) intendedObservedBytes = bytes;
                return bytes;
            },
            AppendRunEvent = (selectedRun, eventName, append) =>
            {
                if (eventName == "approval-evidence-override-observed" && Interlocked.Exchange(ref injected, 1) == 0)
                {
                    Assert.NotNull(intendedObservedBytes);
                    var altered = JsonNode.Parse(Encoding.UTF8.GetString(intendedObservedBytes!))!.AsObject();
                    altered["labels_observed"] = new JsonArray();
                    var alteredBytes = Encoding.UTF8.GetBytes(
                        altered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                    var preparedEvent = Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared");
                    var competingEvent = preparedEvent with
                    {
                        Ts = DateTimeOffset.UtcNow,
                        Event = "approval-evidence-override-observed",
                        ResultRef = observedPath,
                    };
                    PublishCanonicalChange(repos, root =>
                    {
                        File.WriteAllBytes(Path.Combine(root, observedPath.Replace('/', Path.DirectorySeparatorChar)), alteredBytes);
                        var remoteRun = Path.Combine(root, selectedRun.Replace('/', Path.DirectorySeparatorChar));
                        File.AppendAllText(remoteRun, RunLogSerializer.SerializeLine(competingEvent) + "\n");
                    });
                    if (failLiveReadOnRetry) mutator.FailReadLabels = true;
                    remoteObservedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{observedPath}");
                    remoteRunBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{selectedRun}");
                }
                append();
            },
        };

        (int Exit, string Output) result;
        try { result = Run(repos, OverrideId, write: true, flagged: true); }
        finally { ApprovalPostingOverride.WriterOperations = previous; }

        Assert.Equal(1, result.Exit);
        Assert.Equal(1, Volatile.Read(ref injected));
        using var output = JsonDocument.Parse(result.Output);
        var facts = output.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("unresolved", facts.GetProperty("disposition").GetString());
        Assert.Equal("observed-binding-conflict", facts.GetProperty("cause").GetString());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.True(facts.GetProperty("outcome_published").GetBoolean());
        Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.False(output.RootElement.GetProperty("applied").GetBoolean());
        Assert.False(output.RootElement.GetProperty("ci_wait_cleared").GetBoolean());
        Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));
        Assert.Single(mutator.Applied);
        Assert.Equal(remoteObservedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{observedPath}"));
        Assert.Equal(remoteRunBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{runPath}"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ResultRef == observedPath);
    }

    [Fact]
    public void SameResultReferenceWrongUnitDuplicateCannotAuthorizeObservedPair_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.True(firstExit == 0, firstOutput);
        var ciWaitPath = RecordCiWait(repos.Caller);
        var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
        var runPath = ScopedRunPath(repos.Caller);
        PublishCanonicalChange(repos, root =>
        {
            var path = Path.Combine(root, runPath.Replace('/', Path.DirectorySeparatorChar));
            var events = RunLogSerializer.DeserializeAll(File.ReadAllText(path));
            var preparedEvent = Assert.Single(events, item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
            File.AppendAllText(path, RunLogSerializer.SerializeLine(preparedEvent with { ExecutionUnit = "G860" }) + "\n");
            File.AppendAllText(path, RunLogSerializer.SerializeLine(preparedEvent with
            {
                Event = "unrelated-same-unit-event",
                ResultRef = "other/unit/unrelated.json",
            }) + "\n");
        });
        var conflictingRunBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{runPath}");
        var actionsBefore = mutator.Applied.Count;

        foreach (var write in new[] { false, true })
        {
            var (exit, output) = Run(repos, OverrideId, write, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, "override-audit-unavailable", "refused", prepared: true, outcome: true, mutationAttempted: false);
            using var result = JsonDocument.Parse(output);
            Assert.False(result.RootElement.GetProperty("ci_wait_cleared").GetBoolean());
            Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));
            Assert.Equal(actionsBefore, mutator.Applied.Count);
            Assert.Equal(conflictingRunBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{runPath}"));
            var events = repos.ReadEvents();
            Assert.Equal(2, events.Count(item => item.ResultRef == AuditPath("prepared.json")));
            Assert.Contains(events, item => item.Event == "unrelated-same-unit-event" && item.ResultRef == "other/unit/unrelated.json");
        }
    }

    [Fact]
    public void PreparedAuditAndEventRemainVisibleWhenFreshInitialHolderGuardFails_G861()
    {
        using var repos = CreateEligibleRepositories();
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        using (var first = JsonDocument.Parse(firstOutput))
        {
            Assert.False(first.RootElement.GetProperty("applied").GetBoolean());
            var facts = first.RootElement.GetProperty("approval_posting_override");
            Assert.True(facts.GetProperty("prepared_published").GetBoolean());
            Assert.False(facts.GetProperty("outcome_published").GetBoolean());
            Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
            Assert.True(facts.GetProperty("may_have_applied").GetBoolean());
        }
        Assert.Single(mutator.Applied);
        mutator.ThrowBeforeApply = false;
        TakeOver(repos.Caller, displacedHolder: "implementation", newActor: "operator");
        Pull(repos.Caller);
        var beforeMutationCalls = mutator.Applied.Count;

        var (retryExit, retryOutput) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, retryExit);
        using var retry = JsonDocument.Parse(retryOutput);
        Assert.False(retry.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal("refused", retry.RootElement.GetProperty("approval_posting_override").GetProperty("disposition").GetString());
        Assert.Equal("claim-holder-mismatch", retry.RootElement.GetProperty("approval_posting_override").GetProperty("cause").GetString());
        Assert.True(retry.RootElement.GetProperty("approval_posting_override").GetProperty("prepared_published").GetBoolean());
        Assert.False(retry.RootElement.GetProperty("approval_posting_override").GetProperty("outcome_published").GetBoolean());
        Assert.Contains("exact canonical audit/run-event pair was verified", retry.RootElement.GetProperty("approval_posting_override").GetProperty("detail").GetString());
        Assert.Equal(beforeMutationCalls, mutator.Applied.Count);
    }

    [Fact]
    public void ConsumedOverridePreviewRefusesDivergentLabelsAndReportsExistingPair_G861()
    {
        using var repos = CreateEligibleRepositories();
        var initialWrite = Run(repos, OverrideId, write: true, flagged: true);
        Assert.True(initialWrite.Exit == 0, initialWrite.Output);
        var callsBeforePreview = mutator.Applied.Count;
        mutator.Labels = ["intent-pr-reviewing"];

        var (exit, output) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("applied").GetBoolean());
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("refused", facts.GetProperty("disposition").GetString());
        Assert.Equal("override-binding-conflict", facts.GetProperty("cause").GetString());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.True(facts.GetProperty("outcome_published").GetBoolean());
        Assert.False(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.Equal(callsBeforePreview, mutator.Applied.Count);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ExecutionUnit == Unit);
    }

    [Fact]
    public void NonzeroPushThatLandedPreparedPairIsVerifiedBeforeLabelWrite_G861()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = CreateEligibleRepositories();
        using var shim = new PushReturnsFailureAfterCommit(repos.Bare);

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.True(exit == 0, output);
        Assert.True(File.Exists(shim.Marker));
        Assert.Single(mutator.Applied);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("approval_posting_override").GetProperty("prepared_published").GetBoolean());
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
    }

    [Fact]
    public void NonzeroObservedPushThatLandedPairIsVerifiedWithoutRepeatingLabels_G861()
    {
        if (OperatingSystem.IsWindows()) return;
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        using var shim = new PushReturnsFailureAfterCommit(repos.Bare, phase: "observed");

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.True(exit == 0, output);
        Assert.True(File.Exists(shim.Marker), "the PATH wrapper proves the real observed push landed before its nonzero exit");
        using var result = JsonDocument.Parse(output);
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.True(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.True(facts.GetProperty("outcome_published").GetBoolean());
        Assert.Single(mutator.Applied);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ResultRef == AuditPath("observed.json"));
    }

    [Fact]
    public async Task CiWaitRemainsUntilAConfirmedObservedOutcome_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var path = RecordCiWait(repos.Caller);
            var before = File.ReadAllBytes(path);
            var (previewExit, previewOutput) = Run(repos, Guid.NewGuid().ToString("D"), write: false, flagged: true);
            Assert.Equal(0, previewExit);
            AssertOverrideFacts(previewOutput, "eligible-missing-posting", "eligible-preview", prepared: false, outcome: false, mutationAttempted: false);
            Assert.Equal(before, File.ReadAllBytes(path));

            postedStateOverride = "CHANGES_REQUESTED";
            var (refusalExit, refusalOutput) = Run(repos, Guid.NewGuid().ToString("D"), write: true, flagged: true);
            Assert.Equal(1, refusalExit);
            AssertOverrideFacts(refusalOutput, SoloConductorApprovalGate.CauseReviewBlocked, "refused", prepared: false, outcome: false, mutationAttempted: false);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(mutator.Applied);
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var path = RecordCiWait(repos.Caller);
            var before = File.ReadAllBytes(path);
            mutator.ThrowBeforeApply = true;
            var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var path = RecordCiWait(repos.Caller);
            var before = File.ReadAllBytes(path);
            var previous = ApprovalPostingOverride.WriterOperations;
            ApprovalPostingOverride.WriterOperations = previous with
            {
                AppendRunEvent = (runPath, eventName, append) =>
                {
                    if (eventName == "approval-evidence-override-observed")
                        throw new IOException("injected observed outcome append failure after label convergence");
                    append();
                },
            };
            (int Exit, string Output) result;
            try { result = Run(repos, OverrideId, write: true, flagged: true); }
            finally { ApprovalPostingOverride.WriterOperations = previous; }
            Assert.Equal(1, result.Exit);
            AssertOverrideFacts(result.Output, "observed-append-failed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
            Assert.Single(mutator.Applied);
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var path = RecordCiWait(repos.Caller);
            var before = File.ReadAllBytes(path);
            using var pause = new PauseBeforeAuditVerificationClone(repos.Bare, AuditPath("observed.json"));
            var failFinalRead = Task.Run(() =>
            {
                Assert.True(pause.WaitUntilVerificationCloneStarts(TimeSpan.FromSeconds(20)),
                    "the observed audit must be pushed before the verifier clone starts");
                try { headReadAction = _ => throw new IOException("injected final live-head read failure after the observed push"); }
                finally { pause.ReleaseVerificationClone(); }
            });
            var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);
            await failFinalRead;
            Assert.True(File.Exists(pause.Marker), "the PATH gate proves the observed pair reached canonical main before the injected final read failure");
            Assert.Equal(1, exit);
            using var result = JsonDocument.Parse(output);
            var facts = result.RootElement.GetProperty("approval_posting_override");
            Assert.Equal("unresolved", facts.GetProperty("disposition").GetString());
            Assert.True(facts.GetProperty("prepared_published").GetBoolean());
            Assert.True(facts.GetProperty("outcome_published").GetBoolean());
            Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
            Assert.False(result.RootElement.GetProperty("ci_wait_cleared").GetBoolean());
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.True(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
            Assert.Single(mutator.Applied);
        }
    }

    [Fact]
    public void PreparedHostStateWriteRetriesReachedIndexLockContention_G861()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = CreateEligibleRepositories();
        using var shim = new GitRetriesIndexLockOnce(repos.Bare);

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.True(exit == 0, output);
        Assert.True(File.Exists(shim.FailedFirstAdd), "the wrapper must prove that transaction staging reached the index-lock failure");
        // One retry makes three add calls total: two for prepared, then one
        // for the observed outcome. Without the reached lock failure there
        // are only two transaction adds.
        Assert.Equal("3", File.ReadAllText(shim.AddAttempts).Trim());
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        Assert.Single(mutator.Applied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedPushRacePreservesUnrelatedAdvanceAndRechecksFreshGuards_G861(bool introduceBlocker)
    {
        if (OperatingSystem.IsWindows()) return;
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var initial = GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var racer = repos.CloneCurrent();
        if (introduceBlocker) WriteTeamMode(racer, TeamMode.Delivery, DateTimeOffset.UtcNow);
        using var shim = new AdvanceOriginOnPreparedPush(repos.Bare, racer, introduceBlocker);

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.True(File.Exists(shim.Marker), "the real prepared transaction push must lose to the unrelated canonical advance");
        Assert.NotEqual(initial, GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.True(repos.HasPath("refs/heads/main", "g861-unrelated-racer-advance.txt"));
        Assert.Equal("unrelated canonical file from the concurrent writer\n",
            Encoding.UTF8.GetString(GitBytes(repos.Bare, "show", "refs/heads/main:g861-unrelated-racer-advance.txt")));
        if (introduceBlocker)
        {
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, "override-not-applicable", "refused", prepared: false, outcome: false, mutationAttempted: false);
            using var refused = JsonDocument.Parse(output);
            Assert.Equal(JsonValueKind.Null, refused.RootElement.GetProperty("approval_posting_override").GetProperty("recovery_command").ValueKind);
            Assert.Empty(mutator.Applied);
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
            Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(0, exit);
            using var result = JsonDocument.Parse(output);
            Assert.True(result.RootElement.GetProperty("applied").GetBoolean());
            Assert.True(result.RootElement.GetProperty("approval_posting_override").GetProperty("prepared_published").GetBoolean());
            Assert.True(result.RootElement.GetProperty("approval_posting_override").GetProperty("outcome_published").GetBoolean());
            Assert.Single(mutator.Applied);
            Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
            Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ResultRef == AuditPath("observed.json"));
        }
        Assert.True(repos.HasPath("refs/heads/main", "g861-unrelated-racer-advance.txt"));
    }

    [Fact]
    public void PreparedPushUncertaintySurvivesLaterGuardVeto_G861()
    {
        if (OperatingSystem.IsWindows()) return;
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var racer = repos.CloneCurrent();
        WriteTeamMode(racer, TeamMode.Delivery, DateTimeOffset.UtcNow);
        using var shim = new AdvanceOriginOnPreparedPush(repos.Bare, racer, hasBlocker: true,
            failPostAdvanceCloneOrdinals: [1, 3, 4]);

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(1, exit);
        Assert.True(File.Exists(shim.Marker), "the first prepared push must be reached after the racing canonical update");
        Assert.Equal("4", File.ReadAllText(shim.PostAdvanceCloneAttempts).Trim());
        Assert.True(repos.HasPath("refs/heads/main", "g861-unrelated-racer-advance.txt"));
        Assert.Equal(TeamMode.Delivery, TeamModeStore.TryRead(racer)!.Entries.Single().Mode);
        using var result = JsonDocument.Parse(output);
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("unresolved", facts.GetProperty("disposition").GetString());
        Assert.Equal("prepared-publication-unconfirmed", facts.GetProperty("cause").GetString());
        Assert.False(facts.GetProperty("prepared_published").GetBoolean());
        Assert.False(facts.GetProperty("outcome_published").GetBoolean());
        Assert.False(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.NotNull(facts.GetProperty("prepared_path").GetString());
        Assert.NotNull(facts.GetProperty("selected_run_log_path").GetString());
        Assert.NotNull(facts.GetProperty("prepared_commit").GetString());
        Assert.NotNull(facts.GetProperty("recovery_command").GetString());
        Assert.Empty(mutator.Applied);
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
    }

    [Fact]
    public void PreparedPushUncertaintySurvivesRetryCloneFailure_G861()
    {
        if (OperatingSystem.IsWindows()) return;
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var racer = repos.CloneCurrent();
        WriteTeamMode(racer, TeamMode.Delivery, DateTimeOffset.UtcNow);
        using var shim = new AdvanceOriginOnPreparedPush(repos.Bare, racer, hasBlocker: true,
            failPostAdvanceCloneOrdinals: [1, 2, 3, 4]);

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(1, exit);
        Assert.True(File.Exists(shim.Marker), "the first prepared push must be reached after the racing canonical update");
        Assert.Equal("4", File.ReadAllText(shim.PostAdvanceCloneAttempts).Trim());
        Assert.Equal("unrelated canonical file from the concurrent writer\n",
            Encoding.UTF8.GetString(GitBytes(repos.Bare, "show", "refs/heads/main:g861-unrelated-racer-advance.txt")));
        using var result = JsonDocument.Parse(output);
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("unresolved", facts.GetProperty("disposition").GetString());
        Assert.Equal("prepared-publication-unconfirmed", facts.GetProperty("cause").GetString());
        Assert.False(facts.GetProperty("prepared_published").GetBoolean());
        Assert.False(facts.GetProperty("outcome_published").GetBoolean());
        Assert.False(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.NotNull(facts.GetProperty("prepared_path").GetString());
        Assert.NotNull(facts.GetProperty("selected_run_log_path").GetString());
        Assert.NotNull(facts.GetProperty("prepared_commit").GetString());
        Assert.NotNull(facts.GetProperty("recovery_command").GetString());
        Assert.Empty(mutator.Applied);
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
    }

    [Fact]
    public void ObservedPushUncertaintySurvivesRetryCloneFailure_G861()
    {
        if (OperatingSystem.IsWindows()) return;
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var racer = repos.CloneCurrent();
        using var shim = new AdvanceOriginOnPreparedPush(repos.Bare, racer, hasBlocker: false,
            phase: "observed", failPostAdvanceCloneOrdinals: [1, 2, 3, 4]);

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(1, exit);
        Assert.True(File.Exists(shim.Marker), "the observed push must be reached after the racing canonical update");
        Assert.Equal("4", File.ReadAllText(shim.PostAdvanceCloneAttempts).Trim());
        Assert.Equal("unrelated canonical file from the concurrent writer\n",
            Encoding.UTF8.GetString(GitBytes(repos.Bare, "show", "refs/heads/main:g861-unrelated-racer-advance.txt")));
        using var result = JsonDocument.Parse(output);
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("unresolved", facts.GetProperty("disposition").GetString());
        Assert.Equal("observed-publication-unconfirmed", facts.GetProperty("cause").GetString());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.False(facts.GetProperty("outcome_published").GetBoolean());
        Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.True(facts.GetProperty("label_state_observed").GetBoolean());
        Assert.NotNull(facts.GetProperty("prepared_commit").GetString());
        Assert.NotNull(facts.GetProperty("outcome_commit").GetString());
        Assert.NotNull(facts.GetProperty("recovery_command").GetString());
        Assert.Single(mutator.Applied);
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
    }

    [Theory]
    [InlineData("prepared", "serialize")]
    [InlineData("prepared", "append")]
    [InlineData("observed", "serialize")]
    [InlineData("observed", "append")]
    public void PreparedAndObservedWriterFaultsRetainExactPhaseFactsAndResume_G861(string phase, string operation)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var targetPath = AuditPath(phase == "prepared" ? "prepared.json" : "observed.json");
        var before = GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var reached = 0;
        var previous = ApprovalPostingOverride.WriterOperations;
        ApprovalPostingOverride.WriterOperations = operation == "serialize"
            ? previous with
            {
                SerializeAudit = (path, serialize) =>
                {
                    if (string.Equals(path, targetPath, StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref reached);
                        throw new IOException($"injected {phase} serialization failure");
                    }
                    return serialize();
                },
            }
            : previous with
            {
                AppendRunEvent = (path, eventName, append) =>
                {
                    var expectedEvent = phase == "prepared"
                        ? "approval-evidence-override-prepared"
                        : "approval-evidence-override-observed";
                    if (string.Equals(eventName, expectedEvent, StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref reached);
                        throw new IOException($"injected {phase} append failure");
                    }
                    append();
                },
            };

        (int Exit, string Output) failed;
        try
        {
            failed = Run(repos, OverrideId, write: true, flagged: true);
        }
        finally
        {
            ApprovalPostingOverride.WriterOperations = previous;
        }

        Assert.True(Volatile.Read(ref reached) > 0, $"the actual {phase} {operation} boundary was not reached");
        Assert.Equal(1, failed.Exit);
        var preparedPublished = phase == "observed";
        using (var result = JsonDocument.Parse(failed.Output))
        {
            var facts = result.RootElement.GetProperty("approval_posting_override");
            Assert.Equal(preparedPublished, facts.GetProperty("prepared_published").GetBoolean());
            Assert.False(facts.GetProperty("outcome_published").GetBoolean());
            Assert.Equal(preparedPublished, facts.GetProperty("mutation_attempted").GetBoolean());
            Assert.Equal(preparedPublished, facts.GetProperty("label_state_observed").GetBoolean());
            var expectedDisposition = preparedPublished ? "unresolved" : "refused";
            Assert.Equal(expectedDisposition, facts.GetProperty("disposition").GetString());
        }

        if (!preparedPublished)
        {
            Assert.Empty(mutator.Applied);
            Assert.Equal(before, GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
            Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
            return;
        }

        Assert.Single(mutator.Applied);
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));

        var preparedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}");
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.True(retryExit == 0, retryOutput);
        Assert.Single(mutator.Applied);
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.Equal(preparedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ResultRef == AuditPath("observed.json"));
        using var retry = JsonDocument.Parse(retryOutput);
        Assert.Equal("observed", retry.RootElement.GetProperty("approval_posting_override").GetProperty("disposition").GetString());
    }

    [Theory]
    [InlineData("head")]
    [InlineData("reader")]
    public void PreparedPairRemainsPublishedWhenTheNextLiveEligibilityReadFails_G861(string failure)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        headReadAction = call =>
        {
            if (call != 5) return;
            if (failure == "head") currentHead = new string('e', 40);
            else postedReaderException = new IOException("injected late posted-review reader failure");
        };

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("refused", facts.GetProperty("disposition").GetString());
        Assert.Equal(failure == "head" ? SoloConductorApprovalGate.CauseHeadChanged : SoloConductorReviewReader.CauseReadUnavailable,
            facts.GetProperty("cause").GetString());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.False(facts.GetProperty("outcome_published").GetBoolean());
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.Empty(mutator.Applied);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("reader")]
    public async Task ObservedPairRemainsPublishedWhenTheFinalLiveEligibilityReadFails_G861(string failure)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        using var pause = new PauseBeforeAuditVerificationClone(repos.Bare, AuditPath("observed.json"));
        var failFinalRead = Task.Run(() =>
        {
            Assert.True(pause.WaitUntilVerificationCloneStarts(TimeSpan.FromSeconds(20)),
                "the observed audit must be pushed before the verifier clone starts");
            try
            {
                if (failure == "head") currentHead = new string('e', 40);
                else postedReaderException = new IOException("injected final posted-review reader failure");
            }
            finally { pause.ReleaseVerificationClone(); }
        });

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);
        await failFinalRead;

        Assert.True(File.Exists(pause.Marker), "the PATH gate proves the observed pair reached canonical main before the final live read");
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("unresolved", facts.GetProperty("disposition").GetString());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.True(facts.GetProperty("outcome_published").GetBoolean());
        Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ResultRef == AuditPath("observed.json"));
        Assert.Single(mutator.Applied);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("commit")]
    [InlineData("push")]
    public void ObservedGitPublicationFaultsAreReachedAndSameIdRetryDoesNotRepeatLabels_G861(string operation)
    {
        if (OperatingSystem.IsWindows()) return;
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var before = GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        (int Exit, string Output) failed;
        using (var shim = new FailObservedGitOperation(repos.Bare, operation))
        {
            failed = Run(repos, OverrideId, write: true, flagged: true);
            Assert.True(File.Exists(shim.Sentinel), $"the PATH wrapper must reach observed {operation}");
            Assert.True(int.Parse(File.ReadAllText(shim.Attempts).Trim(), System.Globalization.CultureInfo.InvariantCulture) >= 2,
                "both bounded publication attempts must be exhausted before returning unresolved");
        }

        Assert.Equal(1, failed.Exit);
        using (var result = JsonDocument.Parse(failed.Output))
        {
            var facts = result.RootElement.GetProperty("approval_posting_override");
            Assert.Equal("unresolved", facts.GetProperty("disposition").GetString());
            Assert.True(facts.GetProperty("prepared_published").GetBoolean());
            Assert.False(facts.GetProperty("outcome_published").GetBoolean());
            Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
            Assert.True(facts.GetProperty("label_state_observed").GetBoolean());
        }
        Assert.NotEqual(before, GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
        Assert.Single(mutator.Applied);

        var preparedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}");
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.True(retryExit == 0, retryOutput);
        Assert.Single(mutator.Applied);
        Assert.Equal(preparedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ResultRef == AuditPath("observed.json"));
        using var retry = JsonDocument.Parse(retryOutput);
        Assert.Equal("observed", retry.RootElement.GetProperty("approval_posting_override").GetProperty("disposition").GetString());
    }

    [Theory]
    [InlineData("add")]
    [InlineData("commit")]
    [InlineData("push")]
    public void PreparedGitPublicationFailuresExhaustOnlyTheHostPhase_G861(string operation)
    {
        if (OperatingSystem.IsWindows()) return;
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var before = GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        (int Exit, string Output) failed;
        using (var shim = new FailObservedGitOperation(repos.Bare, operation, "prepared"))
        {
            failed = Run(repos, OverrideId, write: true, flagged: true);
            Assert.True(File.Exists(shim.Sentinel), $"the PATH wrapper must reach prepared {operation}");
            Assert.True(int.Parse(File.ReadAllText(shim.Attempts).Trim(), System.Globalization.CultureInfo.InvariantCulture) >= 2,
                "both bounded prepared publication attempts must fail before label mutation");
        }

        Assert.Equal(1, failed.Exit);
        using (var result = JsonDocument.Parse(failed.Output))
        {
            var facts = result.RootElement.GetProperty("approval_posting_override");
            Assert.Equal("refused", facts.GetProperty("disposition").GetString());
            Assert.False(facts.GetProperty("prepared_published").GetBoolean());
            Assert.False(facts.GetProperty("outcome_published").GetBoolean());
            Assert.False(facts.GetProperty("mutation_attempted").GetBoolean());
        }
        Assert.Equal(before, GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
        Assert.Empty(mutator.Applied);
    }

    [Fact]
    public void MissingPostingSlots_AreTypedAndAnyDecidingCrossRuntimePostSatisfiesItsRelation_G861()
    {
        using var repos = CreateEligibleRepositories("cursor");
        var cases = new (Func<CrossRuntimeReviewStoredRecord, bool> Posted, string[] Missing)[]
        {
            (item => item.Record.Runtime is "codex" or "cursor", [CrossRuntimeReviewRecord.RelationSameRuntime]),
            (item => item.Record.Runtime == "claude", [CrossRuntimeReviewRecord.RelationCrossRuntime]),
            (_ => false, [CrossRuntimeReviewRecord.RelationSameRuntime, CrossRuntimeReviewRecord.RelationCrossRuntime]),
        };

        foreach (var (posted, expectedMissing) in cases)
        {
            postedRecordSelector = posted;
            var id = Guid.NewGuid().ToString("D");
            var (exit, output) = Run(repos, id, write: true, flagged: true);
            Assert.True(exit == 0, output);
            using var result = JsonDocument.Parse(output);
            Assert.Equal("observed", result.RootElement.GetProperty("approval_posting_override").GetProperty("disposition").GetString());
            var path = AuditPath("prepared.json", id);
            using var prepared = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{path}"));
            Assert.Equal(expectedMissing.OrderBy(item => item, StringComparer.Ordinal),
                prepared.RootElement.GetProperty("missing_relations").EnumerateArray()
                    .Select(item => item.GetString()!).OrderBy(item => item, StringComparer.Ordinal));
        }

        // Claude provides the same-runtime slot and Codex is one of two
        // locally deciding cross-runtime approvals. Cursor's unposted local
        // record does not create an all-record posting requirement.
        postedRecordSelector = item => item.Record.Runtime is "claude" or "codex";
        var satisfiedId = Guid.NewGuid().ToString("D");
        var (satisfiedExit, satisfiedOutput) = Run(repos, satisfiedId, write: false, flagged: true);
        Assert.Equal(1, satisfiedExit);
        using var satisfied = JsonDocument.Parse(satisfiedOutput);
        Assert.Equal("override-not-applicable", satisfied.RootElement.GetProperty("approval_posting_override").GetProperty("cause").GetString());
        Assert.Equal("satisfied", satisfied.RootElement.GetProperty("solo_conductor_review").GetProperty("decision").GetString());
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json", satisfiedId)));
    }

    [Fact]
    public void RequestIdentityVetoesAreDistinctAndDoNotPublish_G861()
    {
        var cases = new (string Name, Func<G861Repositories, string, (int Exit, string Output)> Invoke, string Cause)[]
        {
            ("actor alias is not exact stored holder", (repos, id) => Run(repos, id, false, true, actor: "builder"), "claim-holder-mismatch"),
            ("wrong team", (repos, id) => Run(repos, id, false, true, team: "another-team"), "claim-holder-mismatch"),
            ("other repository", (repos, id) => Run(repos, id, false, true, repo: "J-Tech-Japan/other"), "packet-repo-mismatch"),
            ("other PR", (repos, id) => Run(repos, id, false, true, pr: PullRequest + 1), "canonical-queue-pr-mismatch"),
            ("other requested head", (repos, id) => Run(repos, id, false, true, head: new string('b', 40)), SoloConductorApprovalGate.CauseHeadChanged),
        };

        foreach (var (name, invoke, expectedCause) in cases)
        {
            ResetSeams();
            using var repos = CreateEligibleRepositories();
            var id = Guid.NewGuid().ToString("D");
            AssertEligiblePreview(repos, id);
            var before = mutator.Applied.Count;
            var (exit, output) = invoke(repos, id);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, expectedCause, "refused", prepared: false, outcome: false, mutationAttempted: false);
            Assert.Empty(mutator.Applied.Skip(before));
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json", id)));
            Assert.True(output.Contains(expectedCause, StringComparison.Ordinal), name + ": " + output);
        }
    }

    [Fact]
    public void CanonicalSnapshotIdentityAndPolicyVetoesDoNotFallBack_G861()
    {
        var cases = new (string Name, string Cause, Action<G861Repositories> Change)[]
        {
            ("claim absent", "claim-unavailable", repos => PublishCanonicalChange(repos, root => File.Delete(Path.Combine(root, ClaimCommand.ClaimPath($"execution-unit:{Unit}"))))),
            ("claim malformed", "claim-invalid", repos => PublishCanonicalChange(repos, root => File.WriteAllText(Path.Combine(root, ClaimCommand.ClaimPath($"execution-unit:{Unit}")), "{"))),
            ("canonical config malformed", "canonical-config-invalid", repos => PublishCanonicalChange(repos, root =>
                File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"), "[cross_runtime_review\n"))),
            ("packet source unit conflicts", "packet-unit-mismatch", repos => PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(root, Unit), "packet.yaml");
                File.WriteAllText(path, $"implementation_issue_packet:\n  domain: {Domain}\n  target_repo: {Repo}\n  source_execution_unit: G999\n");
            })),
            ("malformed canonical packet YAML", "packet-identity-invalid", repos => PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(root, Unit), "packet.yaml");
                File.WriteAllText(path, "implementation_issue_packet: [\n");
            })),
            ("packet identity under unsupported metadata alias", "packet-identity-missing", repos => PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(root, Unit), "packet.yaml");
                File.WriteAllText(path, $"metadata:\n  domain: {Domain}\n  target_repo: {Repo}\n");
            })),
            ("conflicting root and nested packet domains", "packet-identity-invalid", repos => PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(root, Unit), "packet.yaml");
                File.WriteAllText(path, $"domain: foreign-domain\nimplementation_issue_packet:\n  domain: {Domain}\n  target_repo: {Repo}\n");
            })),
            ("conflicting root and nested packet repositories", "packet-identity-invalid", repos => PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(root, Unit), "packet.yaml");
                File.WriteAllText(path, $"target_repo: J-Tech-Japan/other\nimplementation_issue_packet:\n  domain: {Domain}\n  target_repo: {Repo}\n");
            })),
            ("well-formed foreign packet domain", "override-not-applicable", repos => PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(root, Unit), "packet.yaml");
                File.WriteAllText(path, $"implementation_issue_packet:\n  domain: foreign-domain\n  target_repo: {Repo}\n");
            })),
            ("packet target repository conflicts", "packet-repo-mismatch", repos => PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(root, Unit), "packet.yaml");
                File.WriteAllText(path, "implementation_issue_packet:\n  domain: intent-cli\n  target_repo: J-Tech-Japan/other\n");
            })),
            ("canonical config no longer declares gate", "override-not-applicable", repos => PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(root, ".intent-cli", "config.toml");
                var text = File.ReadAllText(path);
                var declaration = text.IndexOf("[[cross_runtime_review.teams]]", StringComparison.Ordinal);
                Assert.True(declaration > 0);
                File.WriteAllText(path, text[..declaration].TrimEnd() + "\n");
            })),
            ("canonical config unavailable", "canonical-config-unavailable", repos => PublishCanonicalChange(repos, root => File.Delete(Path.Combine(root, ".intent-cli", "config.toml")))),
            ("recorded non-solo mode", "override-not-applicable", repos => PublishCanonicalChange(repos, root =>
            {
                var at = DateTimeOffset.UtcNow;
                TeamModeStore.Write(root, new TeamModeState
                {
                    SchemaVersion = TeamModeStore.SchemaVersion,
                    Entries = [new TeamModeEntry
                    {
                        Domain = Domain, Team = Team, Mode = TeamMode.Delivery, UpdatedAt = at,
                        Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.Delivery, At = at }],
                    }],
                });
            })),
            ("malformed canonical team mode", "team-mode-unavailable", repos => PublishCanonicalChange(repos, root =>
                File.WriteAllText(Path.Combine(root, TeamModeStore.RelativePath.Replace('/', Path.DirectorySeparatorChar)), "{"))),
            ("canonical queue has no PR", "canonical-queue-pr-missing", repos => PublishCanonicalChange(repos, root =>
            {
                var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, Domain, Repo);
                var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                var row = document["items"]!.AsArray().Single()!.AsObject();
                row["linked_pr"] = null;
                File.WriteAllText(path, document.ToJsonString());
            })),
            ("canonical queue has no requested unit", "canonical-queue-item-missing", repos => PublishCanonicalChange(repos, root =>
            {
                var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, Domain, Repo);
                var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                document["items"] = new System.Text.Json.Nodes.JsonArray();
                File.WriteAllText(path, document.ToJsonString());
            })),
            ("unavailable scoped queue does not fall back to legacy", "canonical-queue-unavailable", repos => PublishCanonicalChange(repos, root =>
            {
                var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, Domain, Repo);
                var legacy = RuntimeScopedStateResolver.GetLegacyQueueStatePath(root);
                Assert.True(File.Exists(legacy), "the valid legacy queue proves fallback would hide the preferred-path failure");
                File.Delete(path);
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "sentinel"), "the scoped queue path is a directory");
            })),
            ("duplicate canonical unit rows", "canonical-queue-item-ambiguous", repos => PublishCanonicalChange(repos, root =>
            {
                var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, Domain, Repo);
                var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                var items = document["items"]!.AsArray();
                items.Add(items.Single()!.DeepClone());
                File.WriteAllText(path, document.ToJsonString());
            })),
            ("contradictory structured linked PR assertions", "canonical-queue-pr-identity-conflict", repos => PublishCanonicalChange(repos, root =>
            {
                var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, Domain, Repo);
                var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                var row = document["items"]!.AsArray().Single()!.AsObject();
                row["linked_pr"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["repo"] = Repo,
                    ["number"] = PullRequest + 1,
                    ["url"] = $"https://github.com/{Repo}/pull/{PullRequest}",
                };
                File.WriteAllText(path, document.ToJsonString());
            })),
        };

        foreach (var (name, expectedCause, change) in cases)
        {
            ResetSeams();
            using var repos = CreateEligibleRepositories();
            var id = Guid.NewGuid().ToString("D");
            AssertEligiblePreview(repos, id);
            change(repos);
            var before = mutator.Applied.Count;
            var (exit, output) = Run(repos, id, write: false, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, expectedCause, "refused", prepared: false, outcome: false, mutationAttempted: false);
            Assert.Equal(before, mutator.Applied.Count);
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json", id)));
            Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
            Assert.True(output.Contains(expectedCause, StringComparison.Ordinal), name + ": " + output);
        }

        ResetSeams();
        using (var wrongUnit = CreateEligibleRepositories())
        {
            const string otherUnit = "G999";
            Acquire(wrongUnit.Caller, otherUnit);
            PublishCanonicalChange(wrongUnit, root =>
            {
                var path = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(root, otherUnit), "packet.yaml");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $"implementation_issue_packet:\n  domain: {Domain}\n  target_repo: {Repo}\n");
            });
            var id = Guid.NewGuid().ToString("D");
            var (exit, output) = Run(wrongUnit, id, write: false, flagged: true, unit: otherUnit);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, "canonical-queue-item-missing", "refused", prepared: false, outcome: false, mutationAttempted: false);
            Assert.DoesNotContain(wrongUnit.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
            Assert.False(wrongUnit.HasPath("refs/heads/main", $".intent-cli/approval-evidence-overrides/{otherUnit}/{id}/prepared.json"));
            Assert.Empty(mutator.Applied);
        }

        ResetSeams();
        using (var invalidUnitRepos = CreateEligibleRepositories())
        {
            var id = Guid.NewGuid().ToString("D");
            using var output = new StringWriter();
            var invalidUnitRequest = new ApprovalPostingOverrideRequest
            {
                Repo = Repo, PullRequest = PullRequest, Transition = "approved", Mode = "dry-run", Format = "json",
                HeadSha = Head, ExecutionUnit = "../G861", Actor = "implementation", Team = Team,
                Reason = "invalid unit boundary", OverrideId = id,
            };
            var exit = ApprovalPostingOverride.Execute(Context(invalidUnitRepos.Caller), invalidUnitRequest, output);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output.ToString(), "execution-unit-invalid", "refused", prepared: false, outcome: false, mutationAttempted: false);
            Assert.Empty(mutator.Applied);
            Assert.DoesNotContain(invalidUnitRepos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ExistingLocalGateMissingIsDistinctFromBlockedAndDoesNotWrite_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var id = Guid.NewGuid().ToString("D");
        AssertEligiblePreview(repos, id);
        var ciWaitPath = RecordCiWait(repos.Caller);
        var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
        var stored = CrossRuntimeReviewStore.Read(repos.Caller, Repo, PullRequest);
        var codexRecord = stored.Records.Single(item => item.Record.Runtime == "codex").RelativePath;
        PublishCanonicalChange(repos, root => File.Delete(Path.Combine(root, codexRecord.Replace('/', Path.DirectorySeparatorChar))));

        var (exit, output) = Run(repos, id, write: true, flagged: true);

        Assert.Equal(1, exit);
        AssertOverrideFacts(output, "cross-runtime-review-missing", "refused", prepared: false, outcome: false, mutationAttempted: false);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("missing", result.RootElement.GetProperty("cross_runtime_review").GetProperty("decision").GetString());
        Assert.False(result.RootElement.GetProperty("ci_wait_cleared").GetBoolean());
        Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));
        Assert.Empty(mutator.Applied);
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json", id)));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
    }

    [Fact]
    public void CallerConfigCannotAuthorizeOrDefeatCanonicalDeclaration_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var id = Guid.NewGuid().ToString("D");
            var canonical = File.ReadAllBytes(Path.Combine(repos.Caller, ".intent-cli", "config.toml"));
            var callerConfigPath = Path.Combine(repos.Caller, ".intent-cli", "config.toml");
            File.WriteAllText(callerConfigPath, "[project]\ndomain = \"intent-cli\"\nartifact_root = \".intent-cli\"\nworktree_root = \".intent-cli/worktrees\"\n");
            Assert.NotEqual(Convert.ToBase64String(canonical), Convert.ToBase64String(File.ReadAllBytes(callerConfigPath)));
            var (exit, output) = Run(repos, id, write: false, flagged: true);
            Assert.True(exit == 0, output);
            AssertOverrideFacts(output, "eligible-missing-posting", "eligible-preview", prepared: false, outcome: false, mutationAttempted: false);
            Assert.Empty(mutator.Applied);
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json", id)));
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var id = Guid.NewGuid().ToString("D");
            var callerConfigPath = Path.Combine(repos.Caller, ".intent-cli", "config.toml");
            var callerBytes = File.ReadAllBytes(callerConfigPath);
            PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(root, ".intent-cli", "config.toml");
                File.WriteAllText(path, "[project]\ndomain = \"intent-cli\"\nartifact_root = \".intent-cli\"\nworktree_root = \".intent-cli/worktrees\"\n");
            });
            Assert.NotEqual(Convert.ToBase64String(callerBytes), Convert.ToBase64String(GitBytes(repos.Bare, "show", "refs/heads/main:.intent-cli/config.toml")));
            var (exit, output) = Run(repos, id, write: false, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, "override-not-applicable", "refused", prepared: false, outcome: false, mutationAttempted: false);
            Assert.Empty(mutator.Applied);
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json", id)));
        }
    }

    [Fact]
    public void PreparedOnlyRetryAppliesOneFreshDeltaAndPreservesUnrelatedLabels_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        const string recoveryReason = "operator's fixture reason";
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true, reason: recoveryReason);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        using (var first = JsonDocument.Parse(firstOutput))
        {
            Assert.True(first.RootElement.GetProperty("approval_posting_override").GetProperty("may_have_applied").GetBoolean());
            var recovery = first.RootElement.GetProperty("approval_posting_override").GetProperty("recovery_command").GetString();
            Assert.NotNull(recovery);
            Assert.Contains("--repo 'J-Tech-Japan/intent-system'", recovery, StringComparison.Ordinal);
            Assert.Contains("--execution-unit 'G861'", recovery, StringComparison.Ordinal);
            Assert.Contains("--actor 'implementation'", recovery, StringComparison.Ordinal);
            Assert.Contains("--team 'intent-cli-dev'", recovery, StringComparison.Ordinal);
            Assert.Contains("--reason 'operator'\\''s fixture reason'", recovery, StringComparison.Ordinal);
        }
        var originalPrepared = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}");
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);

        mutator.ThrowBeforeApply = false;
        mutator.Labels = ["intent-pr-reviewing", "intent-pr-request-update", "customer-label"];
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true, reason: recoveryReason);

        Assert.Equal(0, retryExit);
        using var retry = JsonDocument.Parse(retryOutput);
        var facts = retry.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("observed", facts.GetProperty("disposition").GetString());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.True(facts.GetProperty("outcome_published").GetBoolean());
        Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.Contains("intent-pr-approved", mutator.Labels);
        Assert.Contains("customer-label", mutator.Labels);
        Assert.DoesNotContain("intent-pr-reviewing", mutator.Labels);
        Assert.DoesNotContain("intent-pr-request-update", mutator.Labels);
        Assert.Contains("intent-pr-reviewing", mutator.Applied.Last().Remove);
        Assert.Contains("intent-pr-request-update", mutator.Applied.Last().Remove);
        using (var prepared = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}")))
            Assert.Contains("intent-pr-request-update", prepared.RootElement.GetProperty("intended_remove_labels").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(originalPrepared, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ExecutionUnit == Unit);
        Assert.Equal(2, mutator.Applied.Count);
    }

    [Theory]
    [InlineData("same-runtime", "codex")]
    [InlineData("cross-runtime", "claude")]
    public void PreparedBothMissingCanResumeWhenOneOriginalRelationIsPosted_G861(string remainingMissing, string newlyPostedRuntime)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        postedRecordSelector = _ => false;
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        var preparedPath = AuditPath("prepared.json");
        var originalPrepared = GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}");
        using (var prepared = JsonDocument.Parse(originalPrepared))
        {
            Assert.Equal(new[] { "cross-runtime", "same-runtime" },
                prepared.RootElement.GetProperty("missing_relations").EnumerateArray().Select(item => item.GetString()).OrderBy(item => item, StringComparer.Ordinal));
        }
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);

        mutator.ThrowBeforeApply = false;
        postedRecordSelector = item => item.Record.Runtime == newlyPostedRuntime;
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(0, retryExit);
        using var retry = JsonDocument.Parse(retryOutput);
        Assert.True(retry.RootElement.GetProperty("applied").GetBoolean());
        var facts = retry.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("observed", facts.GetProperty("disposition").GetString());
        Assert.Equal("missing-posting-eligible", facts.GetProperty("authorization_basis").GetString());
        Assert.Equal(originalPrepared, GitBytes(repos.Bare, $"show", $"refs/heads/main:{preparedPath}"));
        using var unchanged = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}"));
        Assert.Equal(new[] { "cross-runtime", "same-runtime" },
            unchanged.RootElement.GetProperty("missing_relations").EnumerateArray().Select(item => item.GetString()).OrderBy(item => item, StringComparer.Ordinal));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed" && item.ExecutionUnit == Unit);
        Assert.Equal(2, mutator.Applied.Count);
        using (var oneLeft = JsonDocument.Parse(retryOutput))
        {
            var solo = oneLeft.RootElement.GetProperty("solo_conductor_review");
            Assert.Equal("review-missing", solo.GetProperty("cause").GetString());
            Assert.Contains($"relation(s): {remainingMissing}", solo.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreparedSingleMissingCannotResumeWhenASecondRelationBecomesMissing_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        var preparedPath = AuditPath("prepared.json");
        var originalPrepared = GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}");
        mutator.ThrowBeforeApply = false;
        postedRecordSelector = _ => false;
        var priorCalls = mutator.Applied.Count;

        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(1, retryExit);
        AssertOverrideFacts(retryOutput, "override-binding-conflict", "refused", prepared: true, outcome: false, mutationAttempted: false);
        Assert.Equal(priorCalls, mutator.Applied.Count);
        Assert.Equal(originalPrepared, GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}"));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
    }

    [Theory]
    [InlineData("head", "json")]
    [InlineData("negative-review", "json")]
    [InlineData("head", "text")]
    [InlineData("negative-review", "text")]
    public void VerifiedOutcomePublicationFactsSurviveLaterRefusal_G861(string veto, string format)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(0, firstExit);
        using var original = JsonDocument.Parse(firstOutput);
        var originalFacts = original.RootElement.GetProperty("approval_posting_override");
        var preparedPath = originalFacts.GetProperty("prepared_path").GetString()!;
        var observedPath = originalFacts.GetProperty("observed_path").GetString()!;
        var preparedCommit = originalFacts.GetProperty("prepared_commit").GetString()!;
        var outcomeCommit = originalFacts.GetProperty("outcome_commit").GetString()!;
        var eventCount = repos.ReadEvents().Count(item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
        var mutationCount = mutator.Applied.Count;

        headReadCount = 0;
        headReadAction = call =>
        {
            if (call != 2) return;
            if (veto == "head") currentHead = new string('c', 40);
            else postedStateOverride = "CHANGES_REQUESTED";
        };
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true, format: format);

        Assert.Equal(1, retryExit);
        Assert.Equal(mutationCount, mutator.Applied.Count);
        Assert.Equal(eventCount, repos.ReadEvents().Count(item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal)));
        var expectedCause = veto == "head" ? SoloConductorApprovalGate.CauseHeadChanged : SoloConductorApprovalGate.CauseReviewBlocked;
        if (format == "json")
        {
            using var retry = JsonDocument.Parse(retryOutput);
            var facts = retry.RootElement.GetProperty("approval_posting_override");
            Assert.Equal(expectedCause, facts.GetProperty("cause").GetString());
            Assert.Equal("refused", facts.GetProperty("disposition").GetString());
            Assert.True(facts.GetProperty("prepared_published").GetBoolean());
            Assert.True(facts.GetProperty("outcome_published").GetBoolean());
            Assert.False(facts.GetProperty("mutation_attempted").GetBoolean());
            Assert.Equal(preparedPath, facts.GetProperty("prepared_path").GetString());
            Assert.Equal(observedPath, facts.GetProperty("observed_path").GetString());
            Assert.Equal(preparedCommit, facts.GetProperty("prepared_commit").GetString());
            Assert.Equal(outcomeCommit, facts.GetProperty("outcome_commit").GetString());
        }
        else
        {
            Assert.Contains($"{expectedCause}:", retryOutput, StringComparison.Ordinal);
            Assert.Contains("prepared_published=true outcome_published=true mutation_attempted=false", retryOutput, StringComparison.Ordinal);
            Assert.Contains($"approval_posting_override_observed_path: {observedPath}", retryOutput, StringComparison.Ordinal);
            Assert.Contains($"approval_posting_override_prepared_commit: {preparedCommit}", retryOutput, StringComparison.Ordinal);
            Assert.Contains($"approval_posting_override_outcome_commit: {outcomeCommit}", retryOutput, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreparedRetryRemovesRequestUpdateEvenWhenApprovedIsAlreadyPresent_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        mutator.ThrowBeforeApply = false;
        mutator.Labels = ["intent-pr-approved", "intent-pr-request-update", "customer-label"];

        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(0, retryExit);
        using var retry = JsonDocument.Parse(retryOutput);
        var facts = retry.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("observed", facts.GetProperty("disposition").GetString());
        Assert.True(retry.RootElement.GetProperty("applied").GetBoolean());
        Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.DoesNotContain("intent-pr-request-update", mutator.Labels);
        Assert.Contains("customer-label", mutator.Labels);
        Assert.Contains("intent-pr-request-update", mutator.Applied.Last().Remove);
        Assert.Equal(2, mutator.Applied.Count);
    }

    [Fact]
    public void PreparedAndObservedRunEventsMustMatchTheirImmutableAudit_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            mutator.ThrowBeforeApply = true;
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, firstExit);
            Assert.True(firstOutput.Contains("label-state-unconfirmed", StringComparison.Ordinal));
            PublishCanonicalChange(repos, root =>
            {
                var path = RuntimeScopedStateResolver.GetScopedRunLogPath(root, Domain, Repo);
                var events = RunLogSerializer.DeserializeAll(File.ReadAllText(path));
                var changed = events.Select(item => item.Event == "approval-evidence-override-prepared"
                    ? item with { Repo = "J-Tech-Japan/other" }
                    : item).ToArray();
                File.WriteAllText(path, string.Join("\n", changed.Select(RunLogSerializer.SerializeLine)) + "\n");
            });
            mutator.ThrowBeforeApply = false;
            var before = mutator.Applied.Count;
            var (exit, output) = Run(repos, OverrideId, write: false, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, "override-audit-unavailable", "refused", prepared: true, outcome: false, mutationAttempted: false);
            Assert.Equal(before, mutator.Applied.Count);
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            mutator.ThrowBeforeApply = true;
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, firstExit);
            Assert.True(firstOutput.Contains("label-state-unconfirmed", StringComparison.Ordinal));
            PublishCanonicalChange(repos, root =>
            {
                var path = Path.Combine(root, AuditPath("prepared.json"));
                var audit = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                audit["team"] = null;
                File.WriteAllText(path, audit.ToJsonString());
            });
            mutator.ThrowBeforeApply = false;
            var before = mutator.Applied.Count;
            var (exit, output) = Run(repos, OverrideId, write: false, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, "override-audit-unavailable", "refused", prepared: true, outcome: false, mutationAttempted: false);
            Assert.Equal(before, mutator.Applied.Count);
        }
    }

    [Fact]
    public void ObservedAuditCannotChangePreparedHolderIdentity_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var (writeExit, writeOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(0, writeExit);
        Assert.True(writeOutput.Contains("\"outcome_published\": true", StringComparison.Ordinal));
        PublishCanonicalChange(repos, root =>
        {
            var path = Path.Combine(root, AuditPath("observed.json"));
            var audit = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            audit["team"] = "different-team";
            File.WriteAllText(path, audit.ToJsonString());
        });
        var before = mutator.Applied.Count;

        var (exit, output) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, exit);
        AssertOverrideFacts(output, "override-binding-conflict", "refused", prepared: true, outcome: true, mutationAttempted: false);
        Assert.Equal(before, mutator.Applied.Count);
    }

    [Theory]
    [InlineData("intended_add_labels")]
    [InlineData("intended_remove_labels")]
    public void PreparedAuditCannotExpandTheApprovedLabelMutation_G861(string field)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        Assert.True(firstOutput.Contains("label-state-unconfirmed", StringComparison.Ordinal));
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));

        PublishCanonicalChange(repos, root =>
        {
            var auditPath = Path.Combine(root, AuditPath("prepared.json"));
            var audit = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(auditPath))!.AsObject();
            audit[field]!.AsArray().Add("unrelated-label-outside-approved-plan");
            File.WriteAllText(auditPath, audit.ToJsonString());
        });
        mutator.ThrowBeforeApply = false;
        var calls = mutator.Applied.Count;

        var (exit, output) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, exit);
        AssertOverrideFacts(output, "override-audit-unavailable", "refused", prepared: true, outcome: false, mutationAttempted: false);
        Assert.Equal(calls, mutator.Applied.Count);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
    }

    [Fact]
    public void PreparedIdentityCannotResumeAfterCanonicalDecidingRecordBytesChange_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);

        using var prepared = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
        var decidingFile = prepared.RootElement.GetProperty("deciding_records")[0].GetProperty("file").GetString()!;
        var originalRecordBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{decidingFile}");
        Assert.NotEmpty(originalRecordBytes);
        PublishCanonicalChange(repos, root =>
        {
            var recordPath = Path.Combine(root, decidingFile.Replace('/', Path.DirectorySeparatorChar));
            File.AppendAllText(recordPath, "\n");
        });
        Assert.NotEqual(Convert.ToBase64String(originalRecordBytes),
            Convert.ToBase64String(GitBytes(repos.Bare, "show", $"refs/heads/main:{decidingFile}")));

        mutator.ThrowBeforeApply = false;
        var calls = mutator.Applied.Count;
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, retryExit);
        AssertOverrideFacts(retryOutput, "override-binding-conflict", "refused", prepared: true, outcome: false, mutationAttempted: false);
        Assert.Equal(calls, mutator.Applied.Count);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
    }

    [Fact]
    public void PreparedIdentityCannotResumeAfterANewCanonicalDecidingRecordIsPublished_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        Assert.Single(mutator.Applied);
        var originalRecords = PublishedRecordBytes(repos);
        var originalPrepared = GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}");

        Pull(repos.Caller);
        WriteActualRequest(repos.Caller, "cursor");
        WriteActualReview(repos.Caller, "cursor");
        Git(repos.Caller, "add", "--", ".intent-cli/cross-runtime-reviews");
        Git(repos.Caller, "-c", "user.name=test", "-c", "user.email=test@example.invalid", "commit", "--quiet", "-m", "publish a new deciding approval");
        Git(repos.Caller, "push", "origin", "HEAD:refs/heads/main");
        Assert.NotEqual(originalRecords.OrderBy(item => item.Key), PublishedRecordBytes(repos).OrderBy(item => item.Key));
        Assert.Equal(originalPrepared, GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));

        mutator.ThrowBeforeApply = false;
        var calls = mutator.Applied.Count;
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(1, retryExit);
        AssertOverrideFacts(retryOutput, "override-binding-conflict", "refused", prepared: true, outcome: false, mutationAttempted: false);
        Assert.Equal(calls, mutator.Applied.Count);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
    }

    [Fact]
    public async Task NewDecidingRecordPublishedAfterPreparationBeforeLateAssessmentBlocksLabels_G861()
    {
        if (OperatingSystem.IsWindows()) return;
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var ciWaitPath = RecordCiWait(repos.Caller);
        var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
        var recordsBefore = PublishedRecordBytes(repos);
        using var pause = new PauseBeforeAuditVerificationClone(repos.Bare, AuditPath("prepared.json"));
        var recordWriter = Task.Run(() =>
        {
            Assert.True(pause.WaitUntilVerificationCloneStarts(TimeSpan.FromSeconds(20)),
                "the prepared pair must be pushed before the verifier clone starts");
            try
            {
                Pull(repos.Caller);
                WriteActualRequest(repos.Caller, "cursor");
                WriteActualReview(repos.Caller, "cursor");
                Git(repos.Caller, "add", "--", ".intent-cli/cross-runtime-reviews");
                Git(repos.Caller, "-c", "user.name=test", "-c", "user.email=test@example.invalid",
                    "commit", "--quiet", "-m", "publish a new deciding approval after prepare");
                Git(repos.Caller, "push", "origin", "HEAD:refs/heads/main");
            }
            finally
            {
                pause.ReleaseVerificationClone();
            }
        });

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);
        await recordWriter;

        Assert.True(File.Exists(pause.Marker), "the PATH gate proves new evidence was inserted after prepared publication and before the verifier clone");
        Assert.Equal(1, exit);
        AssertOverrideFacts(output, "prepared-binding-no-longer-authorizes", "refused", prepared: true, outcome: false, mutationAttempted: false);
        Assert.Equal(recordsBefore.Count + 1, PublishedRecordBytes(repos).Count);
        Assert.Contains(PublishedRecordBytes(repos).Keys, path => path.Contains("-cursor-", StringComparison.Ordinal));
        Assert.Empty(mutator.Applied);
        Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));
        Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
    }

    [Theory]
    [InlineData("duplicate-prepared-event")]
    [InlineData("orphan-observed-audit")]
    public void AuditPairRejectsDuplicateEventsAndOrphanedOutcome_G861(string corruption)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        if (corruption == "duplicate-prepared-event")
        {
            mutator.ThrowBeforeApply = true;
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, firstExit);
            Assert.True(firstOutput.Contains("label-state-unconfirmed", StringComparison.Ordinal));
            using var prepared = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
            var selectedRun = prepared.RootElement.GetProperty("selected_run_log_path").GetString()!;
            PublishCanonicalChange(repos, root =>
            {
                var runPath = Path.Combine(root, selectedRun.Replace('/', Path.DirectorySeparatorChar));
                var events = RunLogSerializer.DeserializeAll(File.ReadAllText(runPath)).ToList();
                var preparedEvent = Assert.Single(events, item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
                events.Add(preparedEvent);
                File.WriteAllText(runPath, string.Join("\n", events.Select(RunLogSerializer.SerializeLine)) + "\n");
            });
        }
        else
        {
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(0, firstExit);
            Assert.True(firstOutput.Contains("outcome_published\": true", StringComparison.Ordinal));
            using var prepared = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json")}"));
            var selectedRun = prepared.RootElement.GetProperty("selected_run_log_path").GetString()!;
            PublishCanonicalChange(repos, root =>
            {
                var runPath = Path.Combine(root, selectedRun.Replace('/', Path.DirectorySeparatorChar));
                var events = RunLogSerializer.DeserializeAll(File.ReadAllText(runPath))
                    .Where(item => !(item.Event == "approval-evidence-override-observed" && item.ResultRef == AuditPath("observed.json")))
                    .ToArray();
                File.WriteAllText(runPath, string.Join("\n", events.Select(RunLogSerializer.SerializeLine)) + "\n");
            });
        }

        var calls = mutator.Applied.Count;
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, retryExit);
        AssertOverrideFacts(retryOutput, "override-audit-unavailable", "refused", prepared: true,
            outcome: corruption == "orphan-observed-audit", mutationAttempted: false);
        Assert.Equal(calls, mutator.Applied.Count);
        var eventsAfterRetry = repos.ReadEvents();
        if (corruption == "duplicate-prepared-event")
            Assert.Equal(2, eventsAfterRetry.Count(item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json")));
        else
            Assert.DoesNotContain(eventsAfterRetry, item => item.Event == "approval-evidence-override-observed");
    }

    [Fact]
    public void PartialLabelsRemainUnresolvedAndIndependentConvergencePublishesObservationWithoutAction_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            mutator.PartialApply = true;
            var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
            Assert.Contains("intent-pr-approved", mutator.Labels);
            Assert.Contains("intent-pr-reviewing", mutator.Labels);
            Assert.Single(mutator.Applied);
            Assert.True(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
            Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared");
            Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            mutator.ThrowBeforeApply = true;
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, firstExit);
            Assert.True(firstOutput.Contains("may_have_applied\": true", StringComparison.Ordinal));
            var callCount = mutator.Applied.Count;
            mutator.Labels = ["intent-pr-approved"];
            mutator.ThrowBeforeApply = false;

            var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);

            Assert.Equal(0, retryExit);
            using var result = JsonDocument.Parse(retryOutput);
            Assert.True(result.RootElement.GetProperty("applied").GetBoolean());
            var facts = result.RootElement.GetProperty("approval_posting_override");
            Assert.Equal("approval-posting-override-observed", facts.GetProperty("cause").GetString());
            Assert.Equal("observed", facts.GetProperty("disposition").GetString());
            Assert.True(facts.GetProperty("prepared_published").GetBoolean());
            Assert.True(facts.GetProperty("outcome_published").GetBoolean());
            Assert.False(facts.GetProperty("mutation_attempted").GetBoolean());
            Assert.Equal(callCount, mutator.Applied.Count);
            Assert.Equal(["intent-pr-approved"], mutator.Labels);
            Assert.StartsWith("Observed the approved transition already converged", result.RootElement.GetProperty("summary").GetString());
            Assert.DoesNotContain("Applied approved transition", result.RootElement.GetProperty("summary").GetString(), StringComparison.Ordinal);
            Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared");
            Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
        }
    }

    [Fact]
    public async Task BetweenPrepareAndLabelsHeadOrNegativeReviewChangeBlocksMutation_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var id = Guid.NewGuid().ToString("D");
            using var pause = new PauseBeforeAuditVerificationClone(repos.Bare, AuditPath("prepared.json", id));
            var changeHead = Task.Run(() =>
            {
                Assert.True(pause.WaitUntilVerificationCloneStarts(TimeSpan.FromSeconds(20)),
                    "the prepared audit must be pushed before the late eligibility check");
                try { currentHead = new string('c', 40); }
                finally { pause.ReleaseVerificationClone(); }
            });
            var (exit, output) = Run(repos, id, write: true, flagged: true);
            await changeHead;
            Assert.Equal(1, exit);
            Assert.True(File.Exists(pause.Marker), "the PATH gate proves the head changed after prepared publication and before late assessment");
            AssertOverrideFacts(output, SoloConductorApprovalGate.CauseHeadChanged, "refused", prepared: true, outcome: false, mutationAttempted: false);
            Assert.Empty(mutator.Applied);
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var id = Guid.NewGuid().ToString("D");
            using var pause = new PauseBeforeAuditVerificationClone(repos.Bare, AuditPath("prepared.json", id));
            var changePostedState = Task.Run(() =>
            {
                Assert.True(pause.WaitUntilVerificationCloneStarts(TimeSpan.FromSeconds(20)),
                    "the prepared audit must be pushed before the late posted-review check");
                try { postedStateOverride = "CHANGES_REQUESTED"; }
                finally { pause.ReleaseVerificationClone(); }
            });
            var (exit, output) = Run(repos, id, write: true, flagged: true);
            await changePostedState;
            Assert.Equal(1, exit);
            Assert.True(File.Exists(pause.Marker), "the PATH gate proves the negative review arrived after prepared publication and before late assessment");
            AssertOverrideFacts(output, SoloConductorApprovalGate.CauseReviewBlocked, "refused", prepared: true, outcome: false, mutationAttempted: false);
            Assert.Empty(mutator.Applied);
        }
    }

    [Fact]
    public void PostedReviewInventoryFailureAndG834BlockAreNotOverridden_G861()
    {
        var cases = new (Action Setup, string Cause)[]
        {
            (() => postedReaderException = new IOException("API inventory was interrupted"), SoloConductorReviewReader.CauseReadUnavailable),
            (() => postedStateOverride = "CHANGES_REQUESTED", SoloConductorApprovalGate.CauseReviewBlocked),
            (() => currentHead = new string('d', 40), SoloConductorApprovalGate.CauseHeadChanged),
        };
        foreach (var (setup, expectedCause) in cases)
        {
            ResetSeams();
            using var repos = CreateEligibleRepositories();
            setup();
            var id = Guid.NewGuid().ToString("D");
            var (exit, output) = Run(repos, id, write: false, flagged: true);
            Assert.Equal(1, exit);
            AssertOverrideFacts(output, expectedCause, "refused", prepared: false, outcome: false, mutationAttempted: false);
            Assert.Empty(mutator.Applied);
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json", id)));
        }

        ResetSeams();
        using var blocked = CreateEligibleRepositories();
        WriteActualReview(blocked.Caller, "claude", "request-changes");
        Git(blocked.Caller, "add", "--", ".intent-cli/cross-runtime-reviews");
        Git(blocked.Caller, "-c", "user.name=test", "-c", "user.email=test@example.invalid", "commit", "--quiet", "-m", "publish blocking canonical review");
        Git(blocked.Caller, "push", "origin", "HEAD:refs/heads/main");
        var (blockedExit, blockedOutput) = Run(blocked, Guid.NewGuid().ToString("D"), write: false, flagged: true);
        Assert.Equal(1, blockedExit);
        AssertOverrideFacts(blockedOutput, "cross-runtime-review-blocked", "refused", prepared: false, outcome: false, mutationAttempted: false);
        Assert.Empty(mutator.Applied);
    }

    [Theory]
    [InlineData("prior-head-request-changes", SoloConductorApprovalGate.CauseReviewBlocked)]
    [InlineData("invalid-scoped-obligation", SoloConductorApprovalGate.CauseInvalidEvidence)]
    [InlineData("unscopable-review", SoloConductorApprovalGate.CauseUnscopableEvidence)]
    [InlineData("unknown-reader-cause", "future-review-gate-cause")]
    public void FlaggedMissingPostingRefusesPriorHeadInvalidUnscopableAndUnknownReviewCauses_G861(
        string scenario, string expectedCause)
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var ciWaitPath = RecordCiWait(repos.Caller);
        var ciWaitBefore = File.ReadAllBytes(ciWaitPath);
        var headBefore = GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        switch (scenario)
        {
            case "prior-head-request-changes":
                postedRowsOverride = request =>
                [
                    ParsePostedRow(request, 99401, GenericReviewBody(Unit, new string('b', 40), "request-changes"),
                        "CHANGES_REQUESTED", new string('b', 40), "prior-reviewer"),
                ];
                break;
            case "invalid-scoped-obligation":
                postedRowsOverride = request =>
                [
                    ParsePostedRow(request, 99402, GenericReviewBody(Unit, Head, "approve", includeKind: false),
                        "COMMENTED", Head, "invalid-reviewer"),
                ];
                break;
            case "unscopable-review":
                postedRowsOverride = request =>
                [
                    ParsePostedRow(request, 99403, GenericReviewBody(Unit, Head, "approve"),
                        "COMMENTED", Head, null, includeLogin: false),
                ];
                break;
            case "unknown-reader-cause":
                postedReadResultOverride = SoloConductorReviewReadResult.Failed(expectedCause,
                    "a future review-reader cause must remain a hard refusal");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(1, exit);
        AssertOverrideFacts(output, expectedCause, "refused", prepared: false, outcome: false, mutationAttempted: false);
        Assert.Empty(mutator.Applied);
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
        Assert.Equal(headBefore, GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(ciWaitBefore, File.ReadAllBytes(ciWaitPath));

        using var result = JsonDocument.Parse(output);
        if (scenario == "unknown-reader-cause")
        {
            Assert.False(result.RootElement.TryGetProperty("solo_conductor_review", out _));
            Assert.Equal("satisfied", result.RootElement.GetProperty("cross_runtime_review").GetProperty("decision").GetString());
        }
        else
        {
            var solo = result.RootElement.GetProperty("solo_conductor_review");
            Assert.Equal(expectedCause, solo.GetProperty("cause").GetString());
            switch (scenario)
            {
                case "prior-head-request-changes":
                var blocker = Assert.Single(solo.GetProperty("obligations").EnumerateArray());
                Assert.Equal("request-changes", blocker.GetProperty("kind").GetString());
                Assert.Equal("prior-reviewer", blocker.GetProperty("identity").GetString());
                Assert.Equal([99401L], blocker.GetProperty("review_ids").EnumerateArray().Select(item => item.GetInt64()));
                break;
                case "invalid-scoped-obligation":
                var invalid = Assert.Single(solo.GetProperty("obligations").EnumerateArray());
                Assert.Equal("invalid-evidence", invalid.GetProperty("kind").GetString());
                Assert.Equal("invalid-reviewer", invalid.GetProperty("identity").GetString());
                break;
                case "unscopable-review":
                Assert.Equal([99403L], solo.GetProperty("unscopable_review_ids").EnumerateArray().Select(item => item.GetInt64()));
                Assert.Equal(SoloConductorApprovalGate.RepairUnavailableIdentity,
                    solo.GetProperty("repair_unavailable_reason").GetString());
                break;
            }
        }
    }

    [Fact]
    public void SameActorAndTeamWithAReacquiredClaimEpochCannotResumePreparedOverride_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var beforeClaim = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{claimPath}")).RootElement.Clone();
        var beforeEpoch = beforeClaim.GetProperty("claimed_at").GetDateTimeOffset();
        var currentModeAt = DateTimeOffset.UtcNow;

        PublishCanonicalChange(repos, root => WriteTeamMode(root, TeamMode.Delivery, currentModeAt));
        Pull(repos.Caller);
        var released = ClaimCommand.RunTransaction(repos.Caller, new ClaimRequest(
            ClaimOperation.Release, $"execution-unit:{Unit}", "implementation", Team,
            "rotate claim epoch fixture", null, true, "json", ClaimCommand.DefaultMaxAttempts));
        Assert.Equal("released", released.Status);
        var acquired = ClaimCommand.RunTransaction(repos.Caller, new ClaimRequest(
            ClaimOperation.Acquire, $"execution-unit:{Unit}", "implementation", Team,
            null, null, true, "json", ClaimCommand.DefaultMaxAttempts));
        Assert.Equal("acquired", acquired.Status);
        PublishCanonicalChange(repos, root => WriteTeamMode(root, TeamMode.SoloConductor, DateTimeOffset.UtcNow));
        Pull(repos.Caller);
        using var afterClaim = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
        Assert.Equal("implementation", afterClaim.RootElement.GetProperty("actor").GetString());
        Assert.Equal(Team, afterClaim.RootElement.GetProperty("team").GetString());
        Assert.NotEqual(beforeEpoch, afterClaim.RootElement.GetProperty("claimed_at").GetDateTimeOffset());

        mutator.ThrowBeforeApply = false;
        var calls = mutator.Applied.Count;
        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(1, retryExit);
        AssertOverrideFacts(retryOutput, "override-binding-conflict", "refused", prepared: true, outcome: false, mutationAttempted: false);
        Assert.Equal(calls, mutator.Applied.Count);
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared");
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
    }

    [Fact]
    public void ScopedQueueAndRunSelectionPreservesExactExistingPrefix_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var scopedRunRelative = Path.GetRelativePath(repos.Caller,
                RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Caller, Domain, Repo));
            var legacyRunRelative = Path.GetRelativePath(repos.Caller, RuntimeScopedStateResolver.GetLegacyRunLogPath(repos.Caller));
            var prefix = Encoding.UTF8.GetBytes(RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = DateTimeOffset.UtcNow.AddMinutes(-2), ExecutionUnit = "G860", Event = "preexisting-fixture",
                By = "fixture", Reason = "prefix without final newline",
            }).TrimEnd('\n'));
            PublishCanonicalChange(repos, root =>
            {
                var scopedRunPath = Path.Combine(root, scopedRunRelative);
                var legacyRunPath = Path.Combine(root, legacyRunRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(scopedRunPath)!);
                File.WriteAllBytes(scopedRunPath, prefix);
                Directory.CreateDirectory(Path.GetDirectoryName(legacyRunPath)!);
                File.WriteAllText(legacyRunPath, "not-json-and-must-not-be-selected\n");
            });
            var snapshotOid = GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim();

            var (exit, output) = Run(repos, Guid.NewGuid().ToString("D"), write: true, flagged: true);

            Assert.Equal(0, exit);
            using var result = JsonDocument.Parse(output);
            var facts = result.RootElement.GetProperty("approval_posting_override");
            using var prepared = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{facts.GetProperty("prepared_path").GetString()}"));
            var selectedQueue = Path.GetRelativePath(repos.Caller,
                RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Caller, Domain, Repo)).Replace(Path.DirectorySeparatorChar, '/');
            var selectedRun = scopedRunRelative.Replace(Path.DirectorySeparatorChar, '/');
            Assert.Equal(selectedQueue, prepared.RootElement.GetProperty("selected_queue_path").GetString());
            Assert.Equal(selectedRun, prepared.RootElement.GetProperty("selected_run_log_path").GetString());
            Assert.Equal("refs/heads/main", prepared.RootElement.GetProperty("canonical_target_ref").GetString());
            Assert.Equal(snapshotOid, prepared.RootElement.GetProperty("canonical_snapshot_oid").GetString());
            Assert.NotEqual(Head, prepared.RootElement.GetProperty("canonical_snapshot_oid").GetString());
            var remoteRun = GitBytes(repos.Bare, "show", $"refs/heads/main:{selectedRun}");
            Assert.True(remoteRun.AsSpan().StartsWith(prefix));
            Assert.Equal((byte)'\n', remoteRun[prefix.Length]);
            var parsedEvents = RunLogSerializer.DeserializeAll(Encoding.UTF8.GetString(remoteRun));
            Assert.Contains(parsedEvents, item => item.Event == "preexisting-fixture" && item.ExecutionUnit == "G860");
            Assert.Single(parsedEvents, item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
            Assert.Single(parsedEvents, item => item.Event == "approval-evidence-override-observed" && item.ExecutionUnit == Unit);
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var scopedQueue = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Caller, Domain, Repo);
            var scopedRun = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Caller, Domain, Repo);
            var legacyQueue = RuntimeScopedStateResolver.GetLegacyQueueStatePath(repos.Caller);
            var legacyRun = RuntimeScopedStateResolver.GetLegacyRunLogPath(repos.Caller);
            var scopedQueueRelative = Path.GetRelativePath(repos.Caller, scopedQueue);
            var scopedRunRelative = Path.GetRelativePath(repos.Caller, scopedRun);
            var legacyRunRelative = Path.GetRelativePath(repos.Caller, legacyRun);
            PublishCanonicalChange(repos, root =>
            {
                File.Delete(Path.Combine(root, scopedQueueRelative));
                var scopedRunPath = Path.Combine(root, scopedRunRelative);
                if (File.Exists(scopedRunPath)) File.Delete(scopedRunPath);
                var legacyRunPath = Path.Combine(root, legacyRunRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(legacyRunPath)!);
                File.WriteAllText(legacyRunPath, RunLogSerializer.SerializeLine(new RunEvent
                {
                    Ts = DateTimeOffset.UtcNow.AddMinutes(-3), ExecutionUnit = "G860", Event = "legacy-prefix",
                    By = "fixture", Reason = "legacy-only fallback",
                }));
            });

            var (exit, output) = Run(repos, Guid.NewGuid().ToString("D"), write: true, flagged: true);

            Assert.Equal(0, exit);
            using var result = JsonDocument.Parse(output);
            using var prepared = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{AuditPath("prepared.json", result.RootElement.GetProperty("approval_posting_override").GetProperty("override_id").GetString())}"));
            Assert.Equal(Path.GetRelativePath(repos.Caller, legacyQueue).Replace(Path.DirectorySeparatorChar, '/'),
                prepared.RootElement.GetProperty("selected_queue_path").GetString());
            Assert.Equal(Path.GetRelativePath(repos.Caller, legacyRun).Replace(Path.DirectorySeparatorChar, '/'),
                prepared.RootElement.GetProperty("selected_run_log_path").GetString());
            var legacyRunContents = GitBytes(repos.Bare, "show", $"refs/heads/main:{Path.GetRelativePath(repos.Caller, legacyRun).Replace(Path.DirectorySeparatorChar, '/')}");
            Assert.Single(RunLogSerializer.DeserializeAll(Encoding.UTF8.GetString(legacyRunContents)),
                item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        }
    }

    [Fact]
    public void PreparedUuidCannotMoveFromLegacyToPreferredScopedLayout_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var scopedQueueRelative = Path.GetRelativePath(repos.Caller,
            RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Caller, Domain, Repo));
        var scopedRunRelative = Path.GetRelativePath(repos.Caller,
            RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Caller, Domain, Repo));
        var legacyQueueRelative = Path.GetRelativePath(repos.Caller,
            RuntimeScopedStateResolver.GetLegacyQueueStatePath(repos.Caller));
        var legacyRunRelative = Path.GetRelativePath(repos.Caller,
            RuntimeScopedStateResolver.GetLegacyRunLogPath(repos.Caller));
        PublishCanonicalChange(repos, root =>
        {
            var scopedQueue = Path.Combine(root, scopedQueueRelative);
            var scopedRun = Path.Combine(root, scopedRunRelative);
            if (File.Exists(scopedQueue)) File.Delete(scopedQueue);
            if (File.Exists(scopedRun)) File.Delete(scopedRun);
            Assert.True(File.Exists(Path.Combine(root, legacyQueueRelative)), "the legacy queue is the initial selected layout");
        });

        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        var preparedPath = AuditPath("prepared.json");
        var preparedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}");
        var legacyRunBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{legacyRunRelative.Replace(Path.DirectorySeparatorChar, '/')}");
        using (var prepared = JsonDocument.Parse(preparedBytes))
            Assert.Equal(legacyQueueRelative.Replace(Path.DirectorySeparatorChar, '/'), prepared.RootElement.GetProperty("selected_queue_path").GetString());
        var beforeActions = mutator.Applied.Count;
        PublishCanonicalChange(repos, root =>
        {
            var legacyQueue = Path.Combine(root, legacyQueueRelative);
            var scopedQueue = Path.Combine(root, scopedQueueRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(scopedQueue)!);
            File.WriteAllBytes(scopedQueue, File.ReadAllBytes(legacyQueue));
        });
        mutator.ThrowBeforeApply = false;

        var (retryExit, retryOutput) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, retryExit);
        AssertOverrideFacts(retryOutput, "override-audit-unavailable", "refused", prepared: true, outcome: false, mutationAttempted: false);
        Assert.Equal(beforeActions, mutator.Applied.Count);
        Assert.Equal(preparedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}"));
        Assert.Equal(legacyRunBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{legacyRunRelative.Replace(Path.DirectorySeparatorChar, '/') }"));
        Assert.False(repos.HasPath("refs/heads/main", scopedRunRelative.Replace(Path.DirectorySeparatorChar, '/')));
        var preservedLegacyEvents = RunLogSerializer.DeserializeAll(Encoding.UTF8.GetString(legacyRunBytes));
        Assert.Single(preservedLegacyEvents, item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        Assert.DoesNotContain(preservedLegacyEvents, item => item.Event == "approval-evidence-override-observed");
    }

    [Fact]
    public void MalformedSelectedRunEventRefusesPreparedUuidWithoutOverwrite_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.ThrowBeforeApply = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "label-state-unconfirmed", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        var preparedPath = AuditPath("prepared.json");
        var preparedBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}");
        var prepared = JsonDocument.Parse(preparedBytes);
        var runPath = prepared.RootElement.GetProperty("selected_run_log_path").GetString()!;
        prepared.Dispose();
        var runBytes = GitBytes(repos.Bare, "show", $"refs/heads/main:{runPath}");
        Assert.Contains(RunLogSerializer.DeserializeAll(Encoding.UTF8.GetString(runBytes)),
            item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        PublishCanonicalChange(repos, root => File.WriteAllText(Path.Combine(root, runPath.Replace('/', Path.DirectorySeparatorChar)), "{not-valid-json\n"));
        mutator.ThrowBeforeApply = false;
        var callsBeforeRetry = mutator.Applied.Count;

        var (retryExit, retryOutput) = Run(repos, OverrideId, write: false, flagged: true);

        Assert.Equal(1, retryExit);
        AssertOverrideFacts(retryOutput, "canonical-run-log-invalid", "refused", prepared: true, outcome: false, mutationAttempted: false);
        Assert.Equal(callsBeforeRetry, mutator.Applied.Count);
        Assert.Equal(preparedBytes, GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}"));
        Assert.Equal(Encoding.UTF8.GetBytes("{not-valid-json\n"), GitBytes(repos.Bare, "show", $"refs/heads/main:{runPath}"));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("observed.json")));
    }

    [Fact]
    public void SameRepoMetadataWriteBranchIsTheOnlyPublicationTarget_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        const string metadataBranch = "host-state";
        Git(repos.Bare, "branch", metadataBranch, "refs/heads/main");
        var mainBefore = GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var configPath = Path.Combine(repos.Caller, ".intent-cli", "config.toml");
        var config = File.ReadAllText(configPath);
        var reviewSection = config.IndexOf("[[cross_runtime_review.teams]]", StringComparison.Ordinal);
        Assert.True(reviewSection > 0);
        File.WriteAllText(configPath,
            config[..reviewSection].TrimEnd() + $"\nsame_repo_topology = true\nmetadata_write_branch = \"{metadataBranch}\"\n\n" + config[reviewSection..]);

        var (exit, output) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        using var prepared = JsonDocument.Parse(GitBytes(repos.Bare,
            "show", $"refs/heads/{metadataBranch}:{AuditPath("prepared.json")}"));
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("refs/heads/host-state", prepared.RootElement.GetProperty("canonical_target_ref").GetString());
        Assert.Equal(mainBefore, prepared.RootElement.GetProperty("canonical_snapshot_oid").GetString());
        Assert.NotEqual(Head, prepared.RootElement.GetProperty("canonical_snapshot_oid").GetString());
        Assert.Equal(mainBefore, GitText(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.True(repos.HasPath($"refs/heads/{metadataBranch}", AuditPath("prepared.json")));
        Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
        var runPath = prepared.RootElement.GetProperty("selected_run_log_path").GetString()!;
        Assert.Single(RunLogSerializer.DeserializeAll(Encoding.UTF8.GetString(
            GitBytes(repos.Bare, "show", $"refs/heads/{metadataBranch}:{runPath}"))),
            item => item.Event == "approval-evidence-override-prepared" && item.ResultRef == AuditPath("prepared.json"));
        Assert.Equal("observed", facts.GetProperty("disposition").GetString());
    }

    [Fact]
    public void PreparedUuidPayloadAndObservedResumeAreClosedAndIdempotent_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            mutator.ThrowBeforeApply = true;
            var (firstExit, _) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, firstExit);
            mutator.ThrowBeforeApply = false;
            var calls = mutator.Applied.Count;
            var (changedExit, changedOutput) = Run(repos, OverrideId, write: false, flagged: true,
                reason: "changed payload for the same immutable identifier");
            Assert.Equal(1, changedExit);
            AssertOverrideFacts(changedOutput, "override-binding-conflict", "refused", prepared: true, outcome: false, mutationAttempted: false);
            Assert.Equal(calls, mutator.Applied.Count);
            Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(0, firstExit);
            var calls = mutator.Applied.Count;
            var eventCount = repos.ReadEvents().Count(item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
            var (resumeExit, resumeOutput) = Run(repos, OverrideId, write: false, flagged: true);
            Assert.Equal(0, resumeExit);
            using var resume = JsonDocument.Parse(resumeOutput);
            var facts = resume.RootElement.GetProperty("approval_posting_override");
            Assert.Equal("eligible-preview", facts.GetProperty("disposition").GetString());
            Assert.True(facts.GetProperty("prepared_published").GetBoolean());
            Assert.True(facts.GetProperty("outcome_published").GetBoolean());
            Assert.False(facts.GetProperty("mutation_attempted").GetBoolean());
            Assert.False(resume.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal(calls, mutator.Applied.Count);
            Assert.Equal(eventCount, repos.ReadEvents().Count(item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal)));
            Assert.True(firstOutput.Contains("\"outcome_published\": true", StringComparison.Ordinal));

            var (observedExit, observedOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(0, observedExit);
            using (var observed = JsonDocument.Parse(observedOutput))
            {
                Assert.True(observed.RootElement.GetProperty("applied").GetBoolean());
                Assert.False(observed.RootElement.GetProperty("approval_posting_override").GetProperty("mutation_attempted").GetBoolean());
                Assert.StartsWith("Observed the approved transition already converged", observed.RootElement.GetProperty("summary").GetString());
                Assert.DoesNotContain("Applied approved transition", observed.RootElement.GetProperty("summary").GetString(), StringComparison.Ordinal);
            }
            Assert.Equal(calls, mutator.Applied.Count);

            mutator.Labels = ["intent-pr-approved", "intent-pr-request-update"];
            var (divergedExit, divergedOutput) = Run(repos, OverrideId, write: true, flagged: true);
            Assert.Equal(1, divergedExit);
            AssertOverrideFacts(divergedOutput, "override-binding-conflict", "refused", prepared: true, outcome: true, mutationAttempted: false);
            Assert.Equal(calls, mutator.Applied.Count);
        }
    }

    [Fact]
    public void LatePostingRepairMayContinueWithoutChangingPreparedMissingTruth_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var repairedAfterInventory = false;
        headReadAction = call =>
        {
            if (call == 8)
            {
                postedRecordSelector = _ => true;
                repairedAfterInventory = true;
            }
        };

        var (exit, output) = Run(repos, Guid.NewGuid().ToString("D"), write: true, flagged: true);

        Assert.Equal(0, exit);
        Assert.True(repairedAfterInventory, $"post-inventory head check wasn't reached; reads={headReadCount}");
        using var result = JsonDocument.Parse(output);
        Assert.Equal("satisfied", result.RootElement.GetProperty("solo_conductor_review").GetProperty("decision").GetString());
        var preparedPath = result.RootElement.GetProperty("approval_posting_override").GetProperty("prepared_path").GetString();
        using var prepared = JsonDocument.Parse(GitBytes(repos.Bare, "show", $"refs/heads/main:{preparedPath}"));
        Assert.Equal([CrossRuntimeReviewRecord.RelationCrossRuntime],
            prepared.RootElement.GetProperty("missing_relations").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Single(repos.ReadEvents(), item => item.Event == "approval-evidence-override-prepared" && item.ExecutionUnit == Unit);
        Assert.Single(mutator.Applied);
    }

    [Fact]
    public void MutatorThrowAfterApplyMaySucceedOnlyAfterExactConvergence_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.ThrowAfterApply = true;

        var (exit, output) = Run(repos, Guid.NewGuid().ToString("D"), write: true, flagged: true);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output);
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("observed", facts.GetProperty("disposition").GetString());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.True(facts.GetProperty("outcome_published").GetBoolean());
        Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.True(facts.GetProperty("may_have_applied").GetBoolean());
        Assert.True(facts.GetProperty("label_state_observed").GetBoolean());
        Assert.True(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.Single(mutator.Applied);
    }

    [Fact]
    public void PostActionReadFailureRetainsPreparedFactsAndResumeDoesNotRepeatLabels_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        mutator.BeforeApply = () => mutator.FailReadLabels = true;
        var (firstExit, firstOutput) = Run(repos, OverrideId, write: true, flagged: true);
        Assert.Equal(1, firstExit);
        AssertOverrideFacts(firstOutput, "post-mutation-observation-unavailable", "unresolved", prepared: true, outcome: false, mutationAttempted: true);
        using (var first = JsonDocument.Parse(firstOutput))
        {
            var facts = first.RootElement.GetProperty("approval_posting_override");
            Assert.False(facts.GetProperty("may_have_applied").GetBoolean());
            Assert.False(facts.GetProperty("label_state_observed").GetBoolean());
        }
        Assert.Single(mutator.Applied);
        var calls = mutator.Applied.Count;
        mutator.FailReadLabels = false;
        mutator.BeforeApply = null;

        var (retryExit, retryOutput) = Run(repos, OverrideId, write: true, flagged: true);

        Assert.Equal(0, retryExit);
        using var retry = JsonDocument.Parse(retryOutput);
        Assert.True(retry.RootElement.GetProperty("approval_posting_override").GetProperty("outcome_published").GetBoolean());
        Assert.False(retry.RootElement.GetProperty("approval_posting_override").GetProperty("mutation_attempted").GetBoolean());
        Assert.Equal(calls, mutator.Applied.Count);
    }

    [Fact]
    public void HeadAdvanceAfterLabelCallIsUnresolvedAndNeverRolledBack_G861()
    {
        ResetSeams();
        using var repos = CreateEligibleRepositories();
        var advancedHead = new string('e', 40);
        mutator.BeforeApply = () => currentHead = advancedHead;

        var (exit, output) = Run(repos, Guid.NewGuid().ToString("D"), write: true, flagged: true);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        var facts = result.RootElement.GetProperty("approval_posting_override");
        Assert.Equal("unresolved", facts.GetProperty("disposition").GetString());
        Assert.Equal("label-state-unconfirmed", facts.GetProperty("cause").GetString());
        Assert.True(facts.GetProperty("prepared_published").GetBoolean());
        Assert.False(facts.GetProperty("outcome_published").GetBoolean());
        Assert.True(facts.GetProperty("mutation_attempted").GetBoolean());
        Assert.Equal(advancedHead, facts.GetProperty("current_head").GetString());
        Assert.Single(mutator.Applied);
        Assert.Contains("intent-pr-approved", mutator.Labels);
        Assert.DoesNotContain("intent-pr-reviewing", mutator.Labels);
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event == "approval-evidence-override-observed");
    }

    [Fact]
    public void ExceptionOptionsAreApprovedOnlyAndRejectMalformedOrImplicitArguments_G861()
    {
        using var repos = CreateEligibleRepositories();
        var baseArgs = new List<string>
        {
            "--repo", Repo, "--pr", PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--transition", "approved", "--execution-unit", Unit, "--head-sha", Head,
            "--reason", "specific deviation", "--override-id", OverrideId,
            "--actor", "implementation", "--team", Team, "--override-loop-evidence",
        };
        var invalid = new (string Name, string[] Args)[]
        {
            ("reason without flag", ["--repo", Repo, "--pr", PullRequest.ToString(), "--transition", "approved", "--reason", "ordinary text"]),
            ("duplicate flag", [.. baseArgs, "--override-loop-evidence"]),
            ("value-bearing boolean", [.. baseArgs, "--override-loop-evidence=true"]),
            ("invalid UUID", [.. baseArgs[..13], "not-a-uuid", .. baseArgs[14..]]),
            ("non-builder actor", [.. baseArgs[..15], "reviewer", .. baseArgs[16..]]),
            ("approved-only transition", [.. baseArgs[..5], "review-start", .. baseArgs[6..]]),
            ("missing explicit repo", ["--pr", PullRequest.ToString(), "--transition", "approved", "--execution-unit", Unit, "--head-sha", Head, "--reason", "x", "--override-id", OverrideId, "--actor", "implementation", "--team", Team, "--override-loop-evidence"]),
            ("missing explicit head", ["--repo", Repo, "--pr", PullRequest.ToString(), "--transition", "approved", "--execution-unit", Unit, "--reason", "x", "--override-id", OverrideId, "--actor", "implementation", "--team", Team, "--override-loop-evidence"]),
        };
        foreach (var (name, args) in invalid)
        {
            using var output = new StringWriter();
            var exit = CommandRouter.Execute(["automation", "pr-transition", .. args], Context(repos.Caller), output);
            Assert.Equal(1, exit);
            Assert.NotEmpty(output.ToString());
            Assert.Empty(mutator.Applied);
            Assert.False(repos.HasPath("refs/heads/main", AuditPath("prepared.json")));
            Assert.True(output.ToString().Contains("--", StringComparison.Ordinal), name + ": " + output);
        }

        using (var help = new StringWriter())
        {
            var helpExit = CommandRouter.Execute(["automation", "pr-transition", "--help"], Context(repos.Caller), help);
            Assert.Equal(0, helpExit);
            Assert.Contains("--override-loop-evidence", help.ToString(), StringComparison.Ordinal);
            Assert.Contains("G856 reports review-missing solely for absent canonical posting", help.ToString(), StringComparison.Ordinal);
        }

        var (ordinaryExit, ordinaryText) = Run(repos, OverrideId, write: false, flagged: false,
            format: "text", extraArguments: ["--reason", "must require flag"]);
        Assert.Equal(1, ordinaryExit);
        Assert.Contains("require --override-loop-evidence", ordinaryText, StringComparison.Ordinal);

        var (otherTransitionExit, otherTransitionOutput) = Run(repos, Guid.NewGuid().ToString("D"), write: true,
            flagged: false, transition: "review-start", format: "json");
        Assert.Equal(0, otherTransitionExit);
        using var otherTransition = JsonDocument.Parse(otherTransitionOutput);
        Assert.False(otherTransition.RootElement.TryGetProperty("approval_posting_override", out _));
        Assert.Single(mutator.Applied);
        Assert.DoesNotContain(repos.ReadEvents(), item => item.Event.StartsWith("approval-evidence-override-", StringComparison.Ordinal));
    }

    [Fact]
    public void TextAndJsonKeepPhaseFactsOnEarlyAndPreparedRefusals_G861()
    {
        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            var (exit, output) = Run(repos, Guid.NewGuid().ToString("D"), write: false, flagged: true,
                team: "other-team", format: "text");
            Assert.Equal(1, exit);
            Assert.Contains("approval_posting_override: requested=true", output, StringComparison.Ordinal);
            Assert.Contains("disposition=refused", output, StringComparison.Ordinal);
            Assert.Contains("cause=claim-holder-mismatch", output, StringComparison.Ordinal);
            Assert.Contains("prepared_published=false", output, StringComparison.Ordinal);
        }

        ResetSeams();
        using (var repos = CreateEligibleRepositories())
        {
            mutator.ThrowBeforeApply = true;
            var (exit, output) = Run(repos, Guid.NewGuid().ToString("D"), write: true, flagged: true, format: "text");
            Assert.Equal(1, exit);
            Assert.Contains("approval_posting_override: requested=true", output, StringComparison.Ordinal);
            Assert.Contains("disposition=unresolved", output, StringComparison.Ordinal);
            Assert.Contains("cause=label-state-unconfirmed", output, StringComparison.Ordinal);
            Assert.Contains("prepared_published=true", output, StringComparison.Ordinal);
            Assert.Contains("outcome_published=false", output, StringComparison.Ordinal);
            Assert.Contains("approval_posting_override_recovery_command:", output, StringComparison.Ordinal);
        }
    }

    private (int Exit, string Output) Run(
        G861Repositories repos,
        string id,
        bool write,
        bool flagged,
        string actor = "implementation",
        string team = Team,
        string unit = Unit,
        string repo = Repo,
        int pr = PullRequest,
        string head = Head,
        string format = "json",
        string transition = "approved",
        string? workdir = null,
        string? reason = null,
        string? extraArgument = null,
        IReadOnlyList<string>? extraArguments = null)
    {
        var args = new List<string>
        {
            "--repo", repo,
            "--pr", pr.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--transition", transition,
            "--execution-unit", unit,
            "--head-sha", head,
            "--format", format,
        };
        if (flagged)
            args.AddRange(["--reason", reason ?? "specific missing posting fixture", "--override-id", id, "--actor", actor, "--team", team, "--override-loop-evidence"]);
        if (workdir is not null) args.AddRange(["--workdir", workdir]);
        if (extraArgument is not null) args.Add(extraArgument);
        if (extraArguments is not null) args.AddRange(extraArguments);
        if (write) args.Add("--write");
        using var output = new StringWriter();
        var context = new CliContext { RepoRoot = repos.Caller, Config = CliConfigLoader.LoadFromFile(Path.Combine(repos.Caller, ".intent-cli", "config.toml")) };
        return (CommandRouter.Execute(["automation", "pr-transition", .. args], context, output), output.ToString());
    }

    private void ResetSeams()
    {
        ApprovalPostingOverride.WriterOperations = ApprovalPostingOverride.WriterOperationsHooks.Default;
        postedRecordSelector = item => item.Record.Runtime == "claude";
        postedStateOverride = null;
        postedReaderException = null;
        postedReadResultOverride = null;
        postedRowsOverride = null;
        headReadAction = null;
        currentHead = Head;
        headReadCount = 0;
        mutator.Labels = ["intent-pr-reviewing"];
        mutator.BeforeApply = null;
        mutator.ThrowBeforeApply = false;
        mutator.ThrowAfterApply = false;
        mutator.PartialApply = false;
        mutator.FailReadLabels = false;
        mutator.Applied.Clear();
    }

    private void AssertEligiblePreview(G861Repositories repos, string id)
    {
        var (exit, output) = Run(repos, id, write: false, flagged: true);
        Assert.True(exit == 0, output);
        AssertOverrideFacts(output, "eligible-missing-posting", "eligible-preview", prepared: false, outcome: false, mutationAttempted: false);
    }

    private static string RecordCiWait(string repoRoot)
    {
        var result = CiWaitStore.Record(repoRoot, new CiWaitRecord
        {
            Domain = Domain,
            Repo = Repo,
            Pr = PullRequest,
            ObservedHead = Head,
            OwedTransition = "approved",
            RecordedAt = DateTimeOffset.UtcNow,
        }, write: true);
        Assert.True(result.Applied, result.Error);
        return result.Path;
    }

    private static void AssertOverrideFacts(
        string output,
        string expectedCause,
        string expectedDisposition,
        bool prepared,
        bool outcome,
        bool mutationAttempted)
    {
        using var result = JsonDocument.Parse(output);
        var root = result.RootElement;
        Assert.False(root.GetProperty("applied").GetBoolean());
        var facts = root.GetProperty("approval_posting_override");
        Assert.True(facts.GetProperty("requested").GetBoolean());
        Assert.Equal(expectedCause, facts.GetProperty("cause").GetString());
        Assert.Equal(expectedDisposition, facts.GetProperty("disposition").GetString());
        Assert.Equal(prepared, facts.GetProperty("prepared_published").GetBoolean());
        Assert.Equal(outcome, facts.GetProperty("outcome_published").GetBoolean());
        Assert.Equal(mutationAttempted, facts.GetProperty("mutation_attempted").GetBoolean());
    }

    private static void PublishCanonicalChange(G861Repositories repos, Action<string> mutate)
    {
        var clone = repos.CloneCurrent();
        try
        {
            mutate(clone);
            Git(clone, "add", "--all", "--", ".intent-cli");
            Git(clone, "-c", "user.name=G861 fixture", "-c", "user.email=g861@example.invalid",
                "commit", "--quiet", "-m", "change one canonical G861 prerequisite");
            Git(clone, "push", "origin", "HEAD:refs/heads/main");
        }
        finally
        {
            try { Directory.Delete(clone, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void RemoveScopedQueueAndRun(G861Repositories repos)
    {
        var scopedQueueRelative = Path.GetRelativePath(repos.Caller,
            RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Caller, Domain, Repo));
        var scopedRunRelative = Path.GetRelativePath(repos.Caller,
            RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Caller, Domain, Repo));
        PublishCanonicalChange(repos, root =>
        {
            var scopedQueue = Path.Combine(root, scopedQueueRelative);
            var scopedRun = Path.Combine(root, scopedRunRelative);
            if (File.Exists(scopedQueue)) File.Delete(scopedQueue);
            if (File.Exists(scopedRun)) File.Delete(scopedRun);
        });
    }

    private G861Repositories CreateEligibleRepositories(params string[] additionalRuntimes)
    {
        var repos = new G861Repositories(tempRoot);
        repos.Initialize();
        Acquire(repos.Caller);
        Pull(repos.Caller);
        var runtimes = new[] { "claude", "codex" }.Concat(additionalRuntimes).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var runtime in runtimes) WriteActualRequest(repos.Caller, runtime);
        foreach (var runtime in runtimes) WriteActualReview(repos.Caller, runtime);
        Git(repos.Caller, "add", "--", ".intent-cli/cross-runtime-reviews");
        Git(repos.Caller, "-c", "user.name=test", "-c", "user.email=test@example.invalid", "commit", "--quiet", "-m", "publish canonical review records");
        Git(repos.Caller, "push", "origin", "HEAD:refs/heads/main");
        return repos;
    }

    private static void WriteActualRequest(string repoRoot, string runtime)
    {
        var outDirectory = Path.Combine(Path.GetDirectoryName(repoRoot)!, "request-" + runtime);
        var clone = repoRoot;
        var args = new[]
        {
            "review", "cross-runtime", "request",
            "--repo", Repo,
            "--pr", PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--head-sha", Head,
            "--execution-unit", Unit,
            "--runtime", runtime,
            "--clone", clone,
            "--out-dir", outDirectory,
            "--format", "json",
        };
        using var output = new StringWriter();
        var exit = CommandRouter.Execute(args, Context(repoRoot), output);
        Assert.True(exit == 0, output.ToString());
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("rendered", result.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(runtime, result.RootElement.GetProperty("runtime").GetString());
        Assert.Equal("seat", result.RootElement.GetProperty("run_by").GetString());
        Assert.Equal(Unit, result.RootElement.GetProperty("execution_unit").GetString());
        Assert.Equal(Head, result.RootElement.GetProperty("head_sha").GetString());
        Assert.Equal(["invocation.txt", "prompt.md", "verdict.schema.json"],
            Directory.EnumerateFileSystemEntries(outDirectory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
        var invocation = File.ReadAllText(Path.Combine(outDirectory, "invocation.txt"));
        Assert.Equal(result.RootElement.GetProperty("invocation").GetString(), invocation.TrimEnd('\n'));
        Assert.Contains("run by the seat", invocation, StringComparison.Ordinal);
        Assert.Contains("read-only", File.ReadAllText(Path.Combine(outDirectory, "prompt.md")), StringComparison.Ordinal);
    }

    private static void Acquire(string repoRoot, string unit = Unit)
    {
        using var output = new StringWriter();
        var exit = ClaimCommand.ExecuteAcquire(Context(repoRoot),
            ["--scope", $"execution-unit:{unit}", "--actor", "implementation", "--team", Team, "--write", "--format", "json"], output);
        Assert.True(exit == 0, output.ToString());
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("acquired", result.RootElement.GetProperty("status").GetString());
    }

    private static void TakeOver(string repoRoot, string displacedHolder, string newActor)
    {
        using var output = new StringWriter();
        var exit = ClaimCommand.ExecuteTakeover(Context(repoRoot),
            ["--scope", $"execution-unit:{Unit}", "--actor", newActor, "--team", Team,
                "--displaced-holder", displacedHolder, "--reason", "fixture changes holder", "--write", "--format", "json"], output);
        Assert.True(exit == 0, output.ToString());
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("taken-over", result.RootElement.GetProperty("status").GetString());
    }

    private static void WriteActualReview(string repoRoot, string runtime, string verdictName = "approve", string? headOverride = null)
    {
        var packetPath = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(repoRoot, Unit), "packet.yaml");
        Assert.True(File.Exists(packetPath), $"canonical packet missing at {packetPath}");
        Assert.True(PacketYamlDocument.TryParseWithLocation(File.ReadAllText(packetPath), out var packet, out var packetError), packetError?.Message);
        Assert.NotNull(packet);
        Assert.True(packet!.Fields.TryGetValue("implementation_issue_packet.domain", out var packetDomain),
            $"canonical packet fields: {string.Join(", ", packet.Fields.Keys)}");
        Assert.Equal(Domain, packetDomain);
        var head = headOverride ?? Head;
        var verdict = JsonSerializer.Serialize(new
        {
            verdict = verdictName,
            head_sha = head,
            blocking_findings = verdictName == "approve"
                ? Array.Empty<object>()
                : new object[] { new { file = "src/G861.cs", line = 12, scenario = "actual negative fixture" } },
            notes = verdictName == "approve" ? new[] { "actual canonical writer fixture" } : Array.Empty<string>(),
        });
        string raw = verdict;
        if (runtime == "claude")
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(G834Fixture("claude-envelope.json")))!.AsObject();
            node["structured_output"] = System.Text.Json.Nodes.JsonNode.Parse(verdict);
            node["result"] = verdict;
            raw = node.ToJsonString();
        }
        else if (runtime == "cursor")
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(G834Fixture("cursor-envelope.json")))!.AsObject();
            node["result"] = verdict;
            raw = node.ToJsonString();
        }
        var rawDirectory = Path.Combine(repoRoot, ".tmp-g861-verdicts");
        Directory.CreateDirectory(rawDirectory);
        var rawPath = Path.Combine(rawDirectory, runtime + ".json");
        File.WriteAllText(rawPath, raw);
        var config = CliConfigLoader.LoadFromFile(Path.Combine(repoRoot, ".intent-cli", "config.toml"));
        var args = new[]
        {
            "record", "--repo", Repo, "--pr", PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--head-sha", head, "--execution-unit", Unit, "--kind", "implementation",
            "--runtime", runtime, "--runtime-version", "fixture-runtime 1.0", "--verdict-file", rawPath,
            "--write", "--format", "json",
        };
        using var output = new StringWriter();
        var exit = ReviewCrossRuntimeCommand.Execute(Context(repoRoot, config), args, output);
        Assert.True(exit == 0, output.ToString());
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("recorded", result.RootElement.GetProperty("outcome").GetString());
        var record = result.RootElement.GetProperty("record");
        Assert.Equal(runtime, record.GetProperty("runtime").GetString());
        Assert.Equal(runtime == "claude" ? CrossRuntimeReviewRecord.RelationSameRuntime : CrossRuntimeReviewRecord.RelationCrossRuntime,
            record.GetProperty("relation").GetString());
        Assert.Equal(head, record.GetProperty("head_sha").GetString());
        Assert.Equal(Unit, record.GetProperty("execution_unit").GetString());
        var recordPath = Path.Combine(repoRoot, result.RootElement.GetProperty("record_file").GetString()!);
        var rawCopyPath = Path.Combine(repoRoot, result.RootElement.GetProperty("raw_verdict_copy").GetString()!);
        Assert.True(File.Exists(recordPath));
        Assert.True(File.Exists(rawCopyPath));
        Assert.Equal(File.ReadAllBytes(rawPath), File.ReadAllBytes(rawCopyPath));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rawPath))).ToLowerInvariant(),
            record.GetProperty("raw_verdict_sha256").GetString());
    }

    private static string G834Fixture(string name) => Path.Combine(RepoVersionPolicySource.RepoRoot(),
        "tests", "IntentSystem.Cli.Tests", "Fixtures", "G834", name);

    private SoloConductorReviewReadResult ReadCanonicalPostedReviews(SoloConductorReviewReadRequest request)
    {
        if (postedReaderException is not null) throw postedReaderException;
        if (postedReadResultOverride is not null) return postedReadResultOverride;
        if (postedRowsOverride is not null)
            return new SoloConductorReviewReadResult { Complete = true, Rows = postedRowsOverride(request) };
        var rows = new List<IndependentReviewRowEvidence>();
        var id = 99301L;
        foreach (var stored in request.LocalReviews.Records.Where(postedRecordSelector))
        {
            var currentId = id++;
            var body = ReviewCrossRuntimeCommand.RenderCommentBody(stored.Record, stored.RelativePath);
            var json = JsonSerializer.Serialize(new
            {
                id = currentId,
                state = postedStateOverride ?? "COMMENTED",
                commit_id = stored.Record.HeadSha,
                body,
                html_url = $"https://github.com/{request.Repo}/pull/{request.PullRequest}#pullrequestreview-{currentId}",
                user = new { login = "independent-reviewer" },
                submitted_at = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            });
            using var document = JsonDocument.Parse(json);
            rows.Add(IndependentReviewEvidence.ParseRow(document.RootElement, request.ExecutionUnit, request.Domain,
                request.Team, request.Repo, request.PullRequest, request.LocalReviews));
        }
        return new SoloConductorReviewReadResult { Complete = true, Rows = rows };
    }

    private static IndependentReviewRowEvidence ParsePostedRow(
        SoloConductorReviewReadRequest request,
        long id,
        string body,
        string state,
        string commitId,
        string? login,
        bool includeLogin = true)
    {
        var row = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["state"] = state,
            ["commit_id"] = commitId,
            ["body"] = body,
            ["html_url"] = $"https://github.com/{request.Repo}/pull/{request.PullRequest}#pullrequestreview-{id}",
            ["submitted_at"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (includeLogin) row["user"] = login is null ? null : new { login };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(row));
        return IndependentReviewEvidence.ParseRow(document.RootElement, request.ExecutionUnit,
            request.Domain, request.Team, request.Repo, request.PullRequest, request.LocalReviews);
    }

    private static string GenericReviewBody(string unit, string head, string verdict, bool includeKind = true) =>
        $"## Independent subagent review: {verdict}\n\n"
        + "- reviewer: independent subagent review\n"
        + $"- execution unit: {unit}\n"
        + (includeKind ? "- kind: implementation\n" : string.Empty)
        + $"- head SHA: {head}\n"
        + $"- verdict: {verdict}\n\n"
        + "### Blocking findings\n\n- none\n\n### Notes\n\nactual posted fixture";

    private static void WriteTeamMode(string root, string mode, DateTimeOffset at)
    {
        var state = TeamModeStore.TryRead(root) ?? throw new InvalidOperationException("The canonical team-mode state is unavailable.");
        TeamModeStore.Write(root, state with
        {
            Entries = state.Entries.Select(item => item.Domain == Domain && item.Team == Team
                ? item with
                {
                    Mode = mode,
                    UpdatedAt = at,
                    Transitions = [.. item.Transitions, new TeamModeTransition { From = item.Mode, To = mode, At = at }],
                }
                : item).ToArray(),
        });
    }

    private static IReadOnlyDictionary<string, string> PublishedRecordBytes(G861Repositories repos)
    {
        var checkout = repos.CloneCurrent();
        var directory = Path.Combine(checkout, ".intent-cli", "cross-runtime-reviews", "j-tech-japan__intent-system", $"pr-{PullRequest}");
        return Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'),
                path => Convert.ToBase64String(File.ReadAllBytes(path)), StringComparer.Ordinal);
    }

    private static string ScopedRunPath(string repoRoot) => Path.GetRelativePath(repoRoot,
        RuntimeScopedStateResolver.GetScopedRunLogPath(repoRoot, Domain, Repo)).Replace(Path.DirectorySeparatorChar, '/');

    private static void Pull(string repoRoot) => Git(repoRoot, "pull", "--ff-only", "origin", "main");

    private static string AuditPath(string file, string? id = null) => $".intent-cli/approval-evidence-overrides/{Unit}/{id ?? OverrideId}/{file}";

    private static CliContext Context(string repoRoot, CliConfig? config = null) => new()
    {
        RepoRoot = repoRoot,
        Config = config ?? CliConfigLoader.LoadFromFile(Path.Combine(repoRoot, ".intent-cli", "config.toml")),
    };

    private static string GitText(string workdir, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workdir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {stderr}");
        return stdout;
    }

    private static byte[] GitBytes(string workdir, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workdir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var memory = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(memory);
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {stderr}");
        return memory.ToArray();
    }

    private static void Git(string workdir, params string[] args) => _ = GitText(workdir, args);

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        public List<(IReadOnlyCollection<string> Add, IReadOnlyCollection<string> Remove)> Applied { get; } = [];
        public IReadOnlyList<string> Labels { get; set; } = ["intent-pr-reviewing"];
        public Action? BeforeApply { get; set; }
        public bool ThrowBeforeApply { get; set; }
        public bool ThrowAfterApply { get; set; }
        public bool PartialApply { get; set; }
        public bool FailReadLabels { get; set; }

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number)
        {
            if (FailReadLabels) throw new IOException("injected post-action label read failure");
            return Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();
        }

        public void ApplyLabelTransitions(string repo, string kind, int number, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            BeforeApply?.Invoke();
            Applied.Add((addLabels.ToArray(), removeLabels.ToArray()));
            if (ThrowBeforeApply) throw new IOException("mutator fixture threw after request may have reached GitHub");
            if (PartialApply)
            {
                Labels = Labels.Concat(addLabels).Distinct(StringComparer.Ordinal).ToArray();
                return;
            }
            var removed = removeLabels.ToHashSet(StringComparer.Ordinal);
            Labels = Labels.Where(label => !removed.Contains(label)).Concat(addLabels).Distinct(StringComparer.Ordinal).ToArray();
            if (ThrowAfterApply) throw new IOException("mutator applied the label request, then lost its response");
        }

        public void ApplyReconcileTransitions(string repo, string kind, int number, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new InvalidOperationException("Unexpected reconcile-label action.");
    }

    private sealed class G861Repositories : IDisposable
    {
        private readonly string basePath;
        private readonly string seed;
        public G861Repositories(string parent)
        {
            basePath = Path.Combine(parent, "repos");
            seed = Path.Combine(basePath, "seed");
            Bare = Path.Combine(basePath, "origin.git");
            Caller = Path.Combine(basePath, "caller");
        }
        public string Bare { get; }
        public string Caller { get; }

        public void Initialize()
        {
            Directory.CreateDirectory(basePath);
            Directory.CreateDirectory(Bare);
            Git(Bare, "init", "--bare", "--quiet");
            Git(Bare, "symbolic-ref", "HEAD", "refs/heads/main");
            Directory.CreateDirectory(seed);
            Git(seed, "init", "--quiet", "--initial-branch=main");
            Git(seed, "config", "user.name", "G861 fixture");
            Git(seed, "config", "user.email", "g861@example.invalid");
            WriteBaseSnapshot(seed);
            Git(seed, "add", "--", ".intent-cli");
            Git(seed, "-c", "user.name=G861 fixture", "-c", "user.email=g861@example.invalid", "commit", "--quiet", "-m", "seed G861 canonical host evidence");
            Git(seed, "remote", "add", "origin", Bare);
            Git(seed, "push", "--quiet", "-u", "origin", "main");
            Git(basePath, "clone", "--quiet", Bare, Caller);
            Git(Caller, "config", "user.name", "G861 fixture");
            Git(Caller, "config", "user.email", "g861@example.invalid");
        }

        public bool HasPath(string reference, string path)
        {
            var result = new ProcessStartInfo("git") { WorkingDirectory = Bare, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            result.ArgumentList.Add("cat-file"); result.ArgumentList.Add("-e"); result.ArgumentList.Add(reference + ":" + path);
            using var process = Process.Start(result)!;
            _ = process.StandardOutput.ReadToEnd(); _ = process.StandardError.ReadToEnd(); process.WaitForExit();
            return process.ExitCode == 0;
        }

        public IReadOnlyList<RunEvent> ReadEvents()
        {
            var path = ScopedRunPath(Caller);
            if (!HasPath("refs/heads/main", path)) return [];
            return RunLogSerializer.DeserializeAll(Encoding.UTF8.GetString(GitBytes(Bare, "show", $"refs/heads/main:{path}")));
        }

        public string CloneCurrent()
        {
            var path = Path.Combine(basePath, "inspect-" + Guid.NewGuid().ToString("N"));
            Git(basePath, "clone", "--quiet", Bare, path);
            return path;
        }

        private static void WriteBaseSnapshot(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
            File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"), $"""
                [project]
                domain = "{Domain}"
                artifact_root = ".intent-cli"
                worktree_root = ".intent-cli/worktrees"

                [[cross_runtime_review.teams]]
                team = "{Domain}/{Team}"
                conductor_runtime = "claude"
                repos = ["{Repo}"]
                """);
            var modeAt = DateTimeOffset.UtcNow;
            TeamModeStore.Write(root, new TeamModeState
            {
                SchemaVersion = TeamModeStore.SchemaVersion,
                Entries = [new TeamModeEntry
                {
                    Domain = Domain, Team = Team, Mode = TeamMode.SoloConductor,
                    UpdatedAt = modeAt,
                    Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.SoloConductor, At = modeAt }],
                }],
            });
            var packetDirectory = Path.Combine(root, ".intent-cli", "issues", Unit);
            Directory.CreateDirectory(packetDirectory);
            File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), $"implementation_issue_packet:\n  domain: {Domain}\n  target_repo: {Repo}\n  source_execution_unit: {Unit}\n");
            File.WriteAllText(Path.Combine(packetDirectory, "github-body.md"), "# G861 canonical approval posting fixture\n");
            File.WriteAllText(Path.Combine(packetDirectory, "review-context.md"), "# G861 review context\n");
            File.WriteAllText(Path.Combine(packetDirectory, "implementation.md"), "# G861 implementation scope\n");
            var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, Domain, Repo);
            Directory.CreateDirectory(Path.GetDirectoryName(queuePath)!);
            var queue = QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1", UpdatedAt = DateTimeOffset.UtcNow,
                Items = [new QueueItem
                {
                    ExecutionUnit = Unit, Title = "G861 approval posting fixture", State = QueueItemState.Active,
                    Dependencies = [], BlockedBy = [], ClarificationReturnPath = "",
                    PacketPaths = new PacketPaths { Implementation = "implementation.md", ReviewContext = "review-context.md", Yaml = $".intent-cli/issues/{Unit}/packet.yaml" },
                    LinkedIssue = new LinkedIssue { Repo = Repo, Number = PullRequest, Url = $"https://github.com/{Repo}/issues/{PullRequest}" },
                    LinkedPr = $"https://github.com/{Repo}/pull/{PullRequest}", WorkerRole = "implementation", ReviewRole = "reviewer", Priority = "normal",
                }],
            });
            File.WriteAllText(queuePath, queue);
            var legacyQueuePath = RuntimeScopedStateResolver.GetLegacyQueueStatePath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(legacyQueuePath)!);
            File.WriteAllText(legacyQueuePath, queue);
        }

        public void Dispose()
        {
            try { Directory.Delete(basePath, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class PushReturnsFailureAfterCommit : IDisposable
    {
        private readonly string previousPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        public PushReturnsFailureAfterCommit(string bare, string phase = "prepared")
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var bin = Path.Combine(Path.GetDirectoryName(bare)!, "push-shim-bin");
            Directory.CreateDirectory(bin);
            Marker = Path.Combine(bin, "remote-push-landed.txt");
            var wrapper = Path.Combine(bin, "git");
            var realGit = FindGit();
            File.WriteAllText(wrapper,
                "#!/bin/sh\nset -u\nPWD=$(pwd)\n"
                + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(bare)}\nMARKER={ShellQuote(Marker)}\nTARGET={ShellQuote(phase)}\n"
                + "case \"$PWD\" in */intent-cli-g861-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
                + "origin=$(\"$REAL\" -C \"$PWD\" remote get-url origin 2>/dev/null || true)\n"
                + "[ \"$origin\" = \"$BARE\" ] || exec \"$REAL\" \"$@\"\n"
                + "if [ \"${1-}\" = push ] && [ ! -e \"$MARKER\" ]; then\n"
                + "  changed=$(\"$REAL\" -C \"$PWD\" diff-tree --no-commit-id --name-only -r HEAD 2>/dev/null || true)\n"
                + "  case \"$changed\" in *\"$TARGET\".json*)\n"
                + "    : > \"$MARKER\"\n"
                + "    \"$REAL\" \"$@\" || exit $?\n"
                + "    exit 1\n;; esac\nfi\n"
                + "exec \"$REAL\" \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
        }
        public string Marker { get; }
        public void Dispose() => Environment.SetEnvironmentVariable("PATH", previousPath);
        private static string FindGit()
        {
            var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "git")).FirstOrDefault(File.Exists);
            return path ?? throw new InvalidOperationException("git executable was not found on PATH.");
        }
        private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private sealed class AdvanceOriginOnPreparedPush : IDisposable
    {
        private readonly string previousPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        public AdvanceOriginOnPreparedPush(string bare, string racer, bool hasBlocker, string phase = "prepared",
            IReadOnlyList<int>? failPostAdvanceCloneOrdinals = null)
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (phase is not ("prepared" or "observed")) throw new ArgumentOutOfRangeException(nameof(phase));
            var bin = Path.Combine(Path.GetDirectoryName(bare)!, "g861-prepared-race-bin");
            Directory.CreateDirectory(bin);
            Marker = Path.Combine(bin, "racer-advanced-origin.txt");
            PostAdvanceCloneAttempts = Path.Combine(bin, "post-advance-clone-attempts.txt");
            var wrapper = Path.Combine(bin, "git");
            var realGit = FindGit();
            var failingCloneOrdinals = string.Join(",", failPostAdvanceCloneOrdinals ?? []);
            File.WriteAllText(wrapper,
                "#!/bin/sh\nset -u\nPWD=$(pwd)\n"
                + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(bare)}\nRACER={ShellQuote(racer)}\nMARKER={ShellQuote(Marker)}\nBLOCKER={ShellQuote(hasBlocker ? "yes" : "no")}\nPHASE={ShellQuote(phase)}\n"
                + $"COUNT={ShellQuote(PostAdvanceCloneAttempts)}\nFAIL_CLONES={ShellQuote(failingCloneOrdinals)}\n"
                + "case \"$PWD\" in */intent-cli-g861-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
                + "if [ \"${1-}\" = clone ] && [ -e \"$MARKER\" ] && [ -n \"$FAIL_CLONES\" ]; then\n"
                + "  count=0; [ ! -f \"$COUNT\" ] || count=$(cat \"$COUNT\"); count=$((count + 1)); printf '%s\\n' \"$count\" > \"$COUNT\"\n"
                + "  case \",$FAIL_CLONES,\" in *\",$count,\"*) echo \"injected canonical verification clone failure $count\" >&2; exit 128 ;; esac\n"
                + "fi\n"
                + "origin=$(\"$REAL\" -C \"$PWD\" remote get-url origin 2>/dev/null || true)\n"
                + "[ \"$origin\" = \"$BARE\" ] || exec \"$REAL\" \"$@\"\n"
                + "if [ \"${1-}\" = push ] && [ ! -e \"$MARKER\" ]; then\n"
                + "  changed=$(\"$REAL\" -C \"$PWD\" diff-tree --no-commit-id --name-only -r HEAD 2>/dev/null || true)\n"
                + "  case \"$changed\" in *approval-evidence-overrides/*\"$PHASE\".json*)\n"
                + "    \"$REAL\" -C \"$RACER\" pull --ff-only origin main || exit $?\n"
                + "    printf '%s\\n' 'unrelated canonical file from the concurrent writer' > \"$RACER/g861-unrelated-racer-advance.txt\"\n"
                + "    \"$REAL\" -C \"$RACER\" add --all\n"
                + "    \"$REAL\" -C \"$RACER\" -c user.name=race -c user.email=race@example.invalid commit --quiet -m 'advance canonical branch concurrently'\n"
                + "    \"$REAL\" -C \"$RACER\" push origin HEAD:refs/heads/main || exit $?\n"
                + "    : > \"$MARKER\"\n"
                + "    ;; esac\nfi\n"
                + "exec \"$REAL\" \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
        }

        public string Marker { get; }
        public string PostAdvanceCloneAttempts { get; }
        public void Dispose() => Environment.SetEnvironmentVariable("PATH", previousPath);

        private static string FindGit()
        {
            var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "git")).FirstOrDefault(File.Exists);
            return path ?? throw new InvalidOperationException("git executable was not found on PATH.");
        }

        private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private sealed class PauseBeforeAuditVerificationClone : IDisposable
    {
        private readonly string previousPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        public PauseBeforeAuditVerificationClone(string bare, string auditPath)
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var bin = Path.Combine(Path.GetDirectoryName(bare)!, "g861-late-record-pause-bin");
            Directory.CreateDirectory(bin);
            Marker = Path.Combine(bin, "prepared-verification-clone-started.txt");
            ReleaseMarker = Path.Combine(bin, "continue-prepared-verification-clone.txt");
            var wrapper = Path.Combine(bin, "git");
            var realGit = FindGit();
            File.WriteAllText(wrapper,
                "#!/bin/sh\nset -u\nPWD=$(pwd)\n"
                + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(bare)}\nMARKER={ShellQuote(Marker)}\nRELEASE={ShellQuote(ReleaseMarker)}\nTARGET={ShellQuote(auditPath)}\n"
                + "case \"$PWD\" in */intent-cli-g861-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
                + "[ \"${1-}\" = clone ] || exec \"$REAL\" \"$@\"\n"
                + "[ ! -e \"$MARKER\" ] || exec \"$REAL\" \"$@\"\n"
                + "\"$REAL\" --git-dir \"$BARE\" cat-file -e \"refs/heads/main:$TARGET\" 2>/dev/null || exec \"$REAL\" \"$@\"\n"
                + ": > \"$MARKER\"\n"
                + "while [ ! -e \"$RELEASE\" ]; do sleep 0.02; done\n"
                + "exec \"$REAL\" \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
        }

        public string Marker { get; }
        private string ReleaseMarker { get; }

        public bool WaitUntilVerificationCloneStarts(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(Marker)) return true;
                Thread.Sleep(20);
            }
            return File.Exists(Marker);
        }

        public void ReleaseVerificationClone() => File.WriteAllText(ReleaseMarker, "continue\n");

        public void Dispose()
        {
            ReleaseVerificationClone();
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }

        private static string FindGit()
        {
            var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "git")).FirstOrDefault(File.Exists);
            return path ?? throw new InvalidOperationException("git executable was not found on PATH.");
        }

        private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private sealed class FailObservedGitOperation : IDisposable
    {
        private readonly string previousPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        public FailObservedGitOperation(string bare, string operation, string phase = "observed")
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var bin = Path.Combine(Path.GetDirectoryName(bare)!, $"observed-{operation}-shim-bin");
            Directory.CreateDirectory(bin);
            Sentinel = Path.Combine(bin, $"{phase}-operation-reached.txt");
            Attempts = Path.Combine(bin, $"{phase}-operation-attempts.txt");
            var wrapper = Path.Combine(bin, "git");
            var realGit = FindGit();
            File.WriteAllText(wrapper,
                "#!/bin/sh\nset -u\nPWD=$(pwd)\n"
                + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(bare)}\nSENTINEL={ShellQuote(Sentinel)}\nATTEMPTS={ShellQuote(Attempts)}\nOP={ShellQuote(operation)}\nPHASE={ShellQuote(phase)}\n"
                + "case \"$PWD\" in */intent-cli-g861-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
                + "origin=$(\"$REAL\" -C \"$PWD\" remote get-url origin 2>/dev/null || true)\n"
                + "[ \"$origin\" = \"$BARE\" ] || exec \"$REAL\" \"$@\"\n"
                + "matches_phase() { for item in \"$@\"; do case \"$item\" in *\"$PHASE\".json*) return 0 ;; esac; done; return 1; }\n"
                + "should_fail=0\n"
                + "if [ \"$OP\" = add ] && [ \"${1-}\" = add ] && matches_phase \"$@\"; then should_fail=1; fi\n"
                + "if [ \"$OP\" = commit ] && [ \"${1-}\" = -c ]; then staged=$(\"$REAL\" -C \"$PWD\" diff --cached --name-only 2>/dev/null || true); case \"$staged\" in *\"$PHASE\".json*) should_fail=1 ;; esac; fi\n"
                + "if [ \"$OP\" = push ] && [ \"${1-}\" = push ]; then changed=$(\"$REAL\" -C \"$PWD\" diff-tree --no-commit-id --name-only -r HEAD 2>/dev/null || true); case \"$changed\" in *\"$PHASE\".json*) should_fail=1 ;; esac; fi\n"
                + "if [ \"$should_fail\" = 1 ]; then count=0; [ ! -f \"$ATTEMPTS\" ] || count=$(cat \"$ATTEMPTS\"); count=$((count + 1)); printf '%s\\n' \"$count\" > \"$ATTEMPTS\"; printf '%s\\n' \"$PWD $OP\" > \"$SENTINEL\"; echo \"injected $PHASE $OP failure\" >&2; exit 1; fi\n"
                + "exec \"$REAL\" \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
        }

        public string Sentinel { get; }
        public string Attempts { get; }
        public void Dispose() => Environment.SetEnvironmentVariable("PATH", previousPath);

        private static string FindGit()
        {
            var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "git")).FirstOrDefault(File.Exists);
            return path ?? throw new InvalidOperationException("git executable was not found on PATH.");
        }

        private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private sealed class GitRetriesIndexLockOnce : IDisposable
    {
        private readonly string previousPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        public GitRetriesIndexLockOnce(string bare)
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var bin = Path.Combine(Path.GetDirectoryName(bare)!, "git-index-lock-shim-bin");
            Directory.CreateDirectory(bin);
            FailedFirstAdd = Path.Combine(bin, "failed-first-add.txt");
            AddAttempts = Path.Combine(bin, "add-attempts.txt");
            var wrapper = Path.Combine(bin, "git");
            var realGit = FindGit();
            File.WriteAllText(wrapper,
                "#!/bin/sh\nset -u\nPWD=$(pwd)\n"
                + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(bare)}\nFAILED={ShellQuote(FailedFirstAdd)}\nCOUNT={ShellQuote(AddAttempts)}\n"
                + "case \"$PWD\" in */intent-cli-g861-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
                + "origin=$(\"$REAL\" -C \"$PWD\" remote get-url origin 2>/dev/null || true)\n"
                + "[ \"$origin\" = \"$BARE\" ] || exec \"$REAL\" \"$@\"\n"
                + "if [ \"${1-}\" = add ]; then\n"
                + "  count=0; [ ! -f \"$COUNT\" ] || count=$(cat \"$COUNT\"); count=$((count + 1)); printf '%s\\n' \"$count\" > \"$COUNT\"\n"
                + "  if [ \"$count\" = 1 ]; then printf '%s\\n' \"$PWD/.git/index.lock\" > \"$FAILED\"; echo \"fatal: Unable to create '$PWD/.git/index.lock': File exists.\" >&2; exit 128; fi\n"
                + "fi\n"
                + "exec \"$REAL\" \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
        }

        public string FailedFirstAdd { get; }
        public string AddAttempts { get; }
        public void Dispose() => Environment.SetEnvironmentVariable("PATH", previousPath);

        private static string FindGit()
        {
            var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "git")).FirstOrDefault(File.Exists);
            return path ?? throw new InvalidOperationException("git executable was not found on PATH.");
        }

        private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
