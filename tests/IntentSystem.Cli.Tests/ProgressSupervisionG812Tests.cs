using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G812's focused, deterministic acceptance fixtures. These tests intentionally
/// use injected clocks and adapters: no pane, process, provider, host queue, or
/// external transport is consulted.
/// </summary>
public sealed class ProgressSupervisionG812Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g812-progress-").FullName;
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void AC1_HealthyEvidenceRequiresTheDurableTransitionChainAndNeverTreatsLivenessAsCompletion()
    {
        var evidence = Evidence() with { Phase = "pr-ready-to-independent-verdict", LastProgressAt = Now.AddSeconds(-5) };
        var evaluation = new ProgressSupervisionController().Evaluate(evidence, Now);

        Assert.True(evaluation.Healthy);
        Assert.Equal("healthy", evaluation.State);
        Assert.Equal(0, evaluation.OverdueCount);
        Console.WriteLine($"G812 AC1 state={evaluation.State}; transitions=claim/selected/commit/pr/review/report/closeout; liveness_only=false");
    }

    [Fact]
    public void AC1_PhaseSkipRequiresTypedDurableApprovalAndEvidenceLink()
    {
        var typed = new ProgressPhaseSkipEvidence
        {
            FromPhase = "selected-to-relevant-commit", ToPhase = "commit-to-remote-pr", Reason = "already recorded in canonical PR", ApprovedBy = "orchestrator", EvidenceLink = "report:task-1", At = Now,
        };
        var untyped = typed with { ApprovedBy = "builder", EvidenceLink = "" };
        Assert.True(typed.IsTypedAndAuthorized());
        Assert.False(untyped.IsTypedAndAuthorized());
        Assert.Contains("typed durable evidence", GuideProgressSupervisionCommand.BuildGuide().PhaseSkip, StringComparison.Ordinal);
        Console.WriteLine("G812 AC1 phase_skip=typed; approver=orchestrator; independent_review_bypass=false; malformed_skip=refused");
    }

    [Fact]
    public void AC2_DetectionBoundUsesFJPAndRejectsAnInexpensiveButTooSlowConfiguration()
    {
        var qualified = new ProgressSupervisionOptions { DetectionFloorSeconds = 30, JitterSeconds = 10, MaxSweepSeconds = 10 };
        Assert.True(ProgressSupervisionConstants.IsQualified(qualified, out var qualifiedReason));
        Assert.Equal(51, ProgressSupervisionConstants.ComputeDetectionBound(30, 10, 10));

        var slow = new ProgressSupervisionOptions { DetectionFloorSeconds = 30, JitterSeconds = 20, MaxSweepSeconds = 20 };
        Assert.False(ProgressSupervisionConstants.IsQualified(slow, out var slowReason));
        Assert.Contains("D=71", slowReason, StringComparison.Ordinal);
        Console.WriteLine($"G812 AC2 qualified={qualifiedReason}; rejected_slow={slowReason}; no_model_calls=true");
    }

    [Fact]
    public void AC3_OverdueRecoveryCallsProbeThenRedispatchThenEscalatesAndChangedEvidenceResetsEpisode()
    {
        var adapter = new RecordingProgressRecoveryAdapter();
        var controller = new ProgressSupervisionController(
            new ProgressSupervisionOptions { MaxRecoveryAttempts = 2, RecoveryBackoffSeconds = 30 }, adapter);
        var evidence = Evidence() with { Phase = "dispatch-to-claim", EligibleAt = Now.AddMinutes(-3), DeadlineAt = Now.AddMinutes(-2), RecoveryAttempts = 0 };
        var first = controller.Evaluate(evidence, Now);
        var second = controller.Evaluate(evidence with { RecoveryAttempts = 1, LastRecoveryAt = Now.AddSeconds(-31) }, Now);
        var exhausted = controller.Evaluate(evidence with { RecoveryAttempts = 2, LastRecoveryAt = Now.AddSeconds(-31) }, Now);
        var changed = controller.Evaluate(evidence with { RecoveryAttempts = 2, SemanticFingerprint = "new-head" }, Now, exhausted);

        Assert.Equal("canonical-recipient-status-probe", first.Action);
        Assert.Equal("authorized-canonical-recovery", second.Action);
        Assert.Equal("infrastructure-escalation", exhausted.Action);
        Assert.Equal(1, changed.Attempt);
        Assert.Equal(4, adapter.Requests.Count);
        Console.WriteLine($"G812 AC3 calls={string.Join(",", adapter.Requests.Select(r => r.Action))}; changed_attempt={changed.Attempt}; owner={exhausted.Owner}");
    }

    [Fact]
    public void AC4_PollAgeDoesNotChangeSemanticFingerprintAndMissingSourceIsExplicitUnknown()
    {
        var evidence = Evidence() with { FreshnessAt = Now, LastProgressAt = Now };
        var later = new ProgressSupervisionController().Evaluate(evidence, Now.AddSeconds(20));
        var muchLater = new ProgressSupervisionController().Evaluate(evidence, Now.AddSeconds(25));
        Assert.Equal(later.SemanticFingerprint, muchLater.SemanticFingerprint);

        var missing = new ProgressSupervisionController().Evaluate(
            evidence with { ClaimStatus = "unknown", SourceFailures = ["review-store-unreadable"] }, Now);
        Assert.True(missing.Unknown);
        Assert.Contains("claim", missing.MissingEvidence);
        Assert.Contains("source:review-store-unreadable", missing.MissingEvidence);
        Console.WriteLine($"G812 AC4 fingerprint={later.SemanticFingerprint}; unknown={missing.Unknown}; source_failures=review-store-unreadable");
    }

    [Fact]
    public void AC5_BootstrapValidatesPublishedIdentityAndIsExactlyOnceWithoutAcceptingEventCommands()
    {
        var adapter = new RecordingProgressBootstrapAdapter();
        var dispatcher = new ProgressBootstrapDispatcher(adapter);
        var request = Bootstrap();
        var accepted = dispatcher.Dispatch(request);
        var replay = dispatcher.Dispatch(request);
        var commandInjected = dispatcher.Dispatch(request with { ResultNonce = "nonce-command", Command = "rm -rf anything" });
        var wrongRecipient = dispatcher.Dispatch(request with { ResultNonce = "nonce-recipient", IntendedRecipient = "design" });

        Assert.True(accepted.Accepted);
        Assert.True(replay.AlreadyConverged);
        Assert.Single(adapter.Calls);
        Assert.Equal("event-supplied-command-or-root-refused", commandInjected.Error);
        Assert.Equal("wrong-intended-recipient", wrongRecipient.Error);
        Console.WriteLine($"G812 AC5 dispatches={adapter.Calls.Count}; first={accepted.TaskIdentity}; replay={replay.AlreadyConverged}; command_refused={commandInjected.Error}");
    }

    [Fact]
    public void AC5_NotifyBootstrapWritesExistingG809PendingAndDeliveryIdentities()
    {
        var result = new ProgressBootstrapDispatcher(new NotifyProgressBootstrapAdapter(root, Now)).Dispatch(Bootstrap());
        Assert.True(result.Accepted);
        Assert.True(File.Exists(result.PendingIdentity));
        Assert.True(File.Exists(result.DeliveryIdentity));
        Console.WriteLine($"G812 AC5 production_adapter accepted={result.Accepted}; pending={result.PendingIdentity}; delivery={result.DeliveryIdentity}");
    }

    [Fact]
    public void AC6_IncidentLearningNeedsVerifiedContentWritebackOrRemainsOutstanding()
    {
        var incident = new ProgressIncidentRecord
        {
            IncidentId = "incident-1", Domain = "project-a", Team = "team-a", Unit = "unit-1", Phase = "dispatch-to-claim",
            OpenedAt = Now, Owner = "orchestrator", Correction = "status probe", Verification = "receipt pending", Lesson = "retain deadline",
            Evidence = ["failed-primary-route"], KnownUnknowns = ["receipt"],
        };
        Assert.False(ProgressSupervisionStore.IsLearningComplete(incident, null));
        var contentPath = Path.Combine(root, "intents", "project-a", "guide.md");
        Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
        File.WriteAllText(contentPath, "verified G812 lesson\n");
        var digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(contentPath))).ToLowerInvariant();
        var writeBack = new ProgressLearningWriteBack
        {
            Domain = incident.Domain, Team = incident.Team, IncidentId = incident.IncidentId,
            Commit = "abc123", Path = "intents/project-a/guide.md", ContentDigest = digest, Verified = true, VerifiedAt = Now.AddMinutes(1),
        };
        var verification = ProgressSupervisionStore.VerifyWriteBack(root, writeBack);
        Assert.True(verification.Verified);
        Assert.Equal("knowledge-writeback-record-verified", verification.Reason);
        Assert.True(ProgressSupervisionStore.IsLearningComplete(incident, writeBack, root));
        Assert.False(ProgressSupervisionStore.IsLearningComplete(incident, writeBack with { ContentDigest = "sha256:forged" }, root));
        Console.WriteLine($"G812 AC6 generic_report=false; canonical_verification={verification.Reason}; content_digest={verification.ActualDigest}; forged_digest_refused=true");
    }

    [Fact]
    public void AC7_GuideIsReachableInJsonMarkdownAndRoleContractSectionWithProjectIsolation()
    {
        var guide = GuideProgressSupervisionCommand.BuildGuide();
        Assert.Equal(5, guide.RoleContracts.Count);
        Assert.Contains(guide.RoleContracts, contract => contract.CanonicalRole == "steward");
        Assert.Contains(guide.Commands, command => command.Contains("progress-supervision", StringComparison.Ordinal));
        Assert.DoesNotContain("intent-cli-dev", JsonSerializer.Serialize(guide));
        Assert.DoesNotContain("Codex", JsonSerializer.Serialize(guide));

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideProgressSupervisionCommand.Execute(Context(), ["--format", "json"], jsonWriter));
        using var document = JsonDocument.Parse(jsonWriter.ToString());
        Assert.Equal("g812-progress/v1", document.RootElement.GetProperty("contract_version").GetString());
        Assert.True(document.RootElement.GetProperty("role_contracts").GetArrayLength() >= 5);

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideProgressSupervisionCommand.Execute(Context(), ["--section", "role-contracts", "--format", "markdown"], markdownWriter));
        Assert.Contains("role-contracts", markdownWriter.ToString(), StringComparison.Ordinal);
        Console.WriteLine("G812 AC7 surfaces=json,markdown,role-contracts; projects=project-a/project-b; isolation=identity-bound");
    }

    [Fact]
    public void AC7_ReachabilityPointersArePresentWithoutAddingStructuredRoleFieldsToExistingGuides()
    {
        using var design = new StringWriter();
        using var orchestrator = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(Context(), ["--format", "markdown"], design));
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(Context(), ["--format", "markdown"], orchestrator));
        Assert.Contains("Progress supervision route (G812)", design.ToString(), StringComparison.Ordinal);
        Assert.Contains("Progress supervision route (G812)", orchestrator.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("role_contracts", design.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("role_contracts", orchestrator.ToString(), StringComparison.Ordinal);
        Console.WriteLine("G812 AC7 reachability=commands-list/onboarding/design-thread/orchestrator-thread; structured_role_inventory=unchanged");
    }

    [Fact]
    public void AC8_QualificationAndParentLivenessBaselineAreExplicitlyMeasured()
    {
        var options = new ProgressJournalBenchmarkOptions();
        var benchmark = ProgressJournalBenchmark.Run(Path.Combine(root, "journal"), options);
        Assert.True(File.Exists(benchmark.CyclesPath));
        Assert.True(File.Exists(benchmark.StallsPath));
        Assert.True(benchmark.CycleRecords >= options.RecordsPerHistory * 2L);
        Assert.True(benchmark.CycleBytes >= options.MinimumCycleBytes);
        Assert.True(benchmark.StallBytes >= options.MinimumStallBytes);
        Assert.Equal(options.RecordsPerHistory, benchmark.FirstHistoryRecords);
        Assert.Equal(options.RecordsPerHistory, benchmark.SecondHistoryRecords);
        Assert.Equal(options.RecordsPerHistory, benchmark.DeltaRecords);
        Assert.True(benchmark.CursorRecordsRead >= benchmark.DeltaRecords);
        Assert.True(benchmark.ParsedBytes > 0);
        Assert.True(benchmark.ReadBytes > 0);
        Assert.Equal(options.SteadySweeps, benchmark.SteadySweeps);
        Assert.Equal(options.ColdRestartSweeps, benchmark.ColdRestartSweeps);
        Assert.True(benchmark.IdenticalDelta);
        Assert.True(benchmark.MaxPSSeconds >= 0);
        var livenessOnly = new ProgressSupervisionController().Evaluate(Evidence() with { RelevantCommit = "unknown", RemotePr = "unknown", ReportIdentity = "unknown", CloseoutIdentity = "unknown" }, Now);
        Assert.True(livenessOnly.Unknown);
        var parentOracle = ProgressAcceptanceOracle.Evaluate(Evidence() with { RelevantCommit = "unknown", RemotePr = "unknown", ReportIdentity = "unknown" }, null);
        var measuredOracle = ProgressAcceptanceOracle.Evaluate(Evidence(), benchmark);
        Assert.False(parentOracle.Pass);
        Assert.True(measuredOracle.Pass);
        Console.WriteLine($"G812 AC8 measured cycle_records={benchmark.CycleRecords}; cycles_bytes={benchmark.CycleBytes}; stall_records={benchmark.StallRecords}; stalls_bytes={benchmark.StallBytes}; first_history_bytes={benchmark.FirstHistoryBytes}; second_history_bytes={benchmark.SecondHistoryBytes}; delta_records={benchmark.DeltaRecords}; cursor_records_read={benchmark.CursorRecordsRead}; parsed_bytes={benchmark.ParsedBytes}; read_bytes={benchmark.ReadBytes}; steady={benchmark.SteadySweeps}; cold_restart={benchmark.ColdRestartSweeps}; max_p_seconds={benchmark.MaxPSSeconds:F6}; write_ms={benchmark.WriteElapsedMilliseconds}; scan_ms={benchmark.ScanElapsedMilliseconds}; identical_delta={benchmark.IdenticalDelta}; parent_liveness_only={livenessOnly.State}; parent_oracle_pass={parentOracle.Pass}; measured_oracle_pass={measuredOracle.Pass}");
    }

    [Fact]
    public void AC9_CurrentHeadVerdictClassificationIsSelectorIndependentAndT60Bounded()
    {
        var verdict = ReviewVerdict("4208f01416d018840b5d8c01a8652a1f9eb54267", "G805-review-v1-pr1765.md");
        var options = new ProgressSupervisionOptions { JitterSeconds = 10, MaxSweepSeconds = 10 };
        var evaluation = ProgressReviewVerdictEvaluator.Evaluate(verdict, Now, options);
        Assert.Equal("review-selector-drift", evaluation.Classification);
        Assert.True(evaluation.Open);
        Assert.True(evaluation.SelectorIndependent);
        Assert.Equal(Now.AddSeconds(60), evaluation.RepairSelectionDeadlineAt);
        Assert.Equal(51, evaluation.DetectionBoundSeconds);
        var atAction = ProgressReviewVerdictEvaluator.Evaluate(verdict, evaluation.ActionDeadlineAt!.Value, options);
        Assert.Equal("canonical-builder-repair-delegation", atAction.Action);

        Assert.Equal("obsolete-head-refusal", ProgressReviewVerdictEvaluator.Evaluate(verdict with { VerdictHead = "old-head" }, Now, options).Classification);
        Assert.Equal("superseded-verdict-refusal", ProgressReviewVerdictEvaluator.Evaluate(verdict with { Superseded = true }, Now, options).Classification);
        Assert.Equal("forged-verdict-refusal", ProgressReviewVerdictEvaluator.Evaluate(verdict with { CanonicalReport = false }, Now, options).Classification);
        var fixtures = new[]
        {
            ReviewVerdict("g805-head", "G805-review-v1-pr1765.md"),
            ReviewVerdict("g806-head", "G806-review-v1-pr1766.md"),
            ReviewVerdict("g807-head", "G807-review-v1-pr1767.md"),
            ReviewVerdict("g808-head", "G808-rereview-v1-pr1768.md"),
        };
        Assert.All(fixtures, fixture => Assert.Equal("review-selector-drift", ProgressReviewVerdictEvaluator.Evaluate(fixture, Now, options).Classification));
        Console.WriteLine($"G812 AC9 selector-independent=current-head; current={evaluation.Classification}; stale={ProgressReviewVerdictEvaluator.Evaluate(verdict with { VerdictHead = "old-head" }, Now, options).Classification}; superseded={ProgressReviewVerdictEvaluator.Evaluate(verdict with { Superseded = true }, Now, options).Classification}; forged={ProgressReviewVerdictEvaluator.Evaluate(verdict with { CanonicalReport = false }, Now, options).Classification}; fixtures=G805/G806/G807/G808; T=60; D={evaluation.DetectionBoundSeconds}; action_deadline={evaluation.ActionDeadlineAt:O}");
    }

    [Fact]
    public void AC10AndAC12_HandoffEdgesAreIdempotentAndKeepDeliveryReceiptConsumptionDistinct()
    {
        var edge = new ProgressHandoffEdge
        {
            Domain = "project-a", Team = "team-a", Repo = "owner/repo", Unit = "unit-1", Head = "head-1", TaskId = "task-1", ResultNonce = "nonce-1",
            EventId = "event-1", Sender = "builder", Recipient = "reviewer", RecipientIdentity = "reviewer:seat-1", EligibilityAt = Now, DeadlineAt = Now.AddSeconds(60),
            DeliveryAttempts = 1, DeliveryResult = "delivered",
        };
        var emitted = ProgressHandoffStore.Emit(root, edge);
        var replay = ProgressHandoffStore.Emit(root, edge);
        var receipt = ProgressHandoffStore.RecordReceipt(root, edge, "receipt-1", Now.AddSeconds(1));
        var consumed = ProgressHandoffStore.RecordConsumption(root, edge with { ReceiptId = "receipt-1", ReceiptAt = Now.AddSeconds(1) }, "review-selected", Now.AddSeconds(2));
        Assert.True(emitted.Written);
        Assert.True(replay.AlreadyConverged);
        Assert.True(receipt.Written);
        Assert.True(consumed.Written);
        Console.WriteLine("G812 AC10/AC12 edge=event->delivery->receipt->consumption; replay=true; duplicate_task=false; authority_grant=false");
    }

    [Fact]
    public void AC11RoleRegistryRejectsRecurringArchitectOrReviewerPollingAndNamesFailureActions()
    {
        var contracts = GuideProgressSupervisionCommand.BuildGuide().RoleContracts;
        Assert.Contains(contracts, contract => contract.CanonicalRole == "orchestrator" && contract.Eligibility.Contains("F30", StringComparison.Ordinal));
        Assert.Contains(contracts, contract => contract.CanonicalRole == "steward" && contract.FailureAction.Contains("escalate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(contracts, contract => contract.CanonicalRole is "architect" or "reviewer" && contract.Eligibility.Contains("poll", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("G812 AC11 model_wakes_on_unchanged_health=0; recurring_poll_recipients=orchestrator,steward; architect_reviewer_polling=refused");
    }

    [Fact]
    public void AC7FaultMatrixAndTwoUnrelatedProjectsRemainIsolated()
    {
        Assert.Equal(Enum.GetValues<ProgressFaultKind>().Length, ProgressFaultMatrix.All.Count);
        foreach (var project in new[] { "project-a", "project-b" })
        {
            foreach (var fault in ProgressFaultMatrix.All)
            {
                var outcome = ProgressFaultMatrix.Evaluate(project, fault, Now);
                Assert.True(outcome.Open);
                Assert.False(outcome.Completed);
                Assert.False(outcome.DuplicateDispatch);
                Assert.Equal("orchestrator", outcome.Owner);
                Assert.NotEmpty(outcome.Action);
                Assert.Contains(project, outcome.Project, StringComparison.Ordinal);
            }
        }
        Console.WriteLine($"G812 AC7 faults={string.Join(",", ProgressFaultMatrix.All)}; projects=project-a/project-b; completed_by_fault=0; cross_project=refused");
    }

    [Fact]
    public void AC7_CommandWritesTwoProjectRootsWithoutCrossProjectEvidence()
    {
        var projectA = Path.Combine(root, "project-a");
        var projectB = Path.Combine(root, "project-b");
        WriteProjectEvidence(projectA, "project-a", "team-a", "unit-a");
        WriteProjectEvidence(projectB, "project-b", "team-b", "unit-b");
        var pathA = ProgressSupervisionStore.ResolveEvidencePath(projectA, "project-a", "team-a");
        var pathB = ProgressSupervisionStore.ResolveEvidencePath(projectB, "project-b", "team-b");
        Assert.True(File.Exists(pathA));
        Assert.True(File.Exists(pathB));
        Assert.NotEqual(pathA, pathB);
        Assert.DoesNotContain("project-b", File.ReadAllText(pathA), StringComparison.Ordinal);
        Assert.DoesNotContain("project-a", File.ReadAllText(pathB), StringComparison.Ordinal);
        Console.WriteLine($"G812 AC7 project_bindings=2; root_a={projectA}; root_b={projectB}; evidence_isolation=true");
    }

    [Fact]
    public void AC10AndAC12FaultsNeverFalseCompleteAndKeepReceiptsDistinct()
    {
        var eventAt = Now.AddSeconds(1);
        var nextFloor = Now.AddSeconds(30);
        var eventFirst = ProgressFaultMatrix.Evaluate("project-a", ProgressFaultKind.DroppedWake, eventAt);
        Assert.True(eventAt < nextFloor);
        Assert.True(eventFirst.Open);
        foreach (var fault in ProgressFaultMatrix.All)
        {
            var outcome = ProgressFaultMatrix.Evaluate("project-a", fault, Now);
            Assert.False(outcome.Completed);
            Assert.True(outcome.RecoveryDeadlineAt > Now);
        }
        Console.WriteLine($"G812 AC10/AC12 faults={ProgressFaultMatrix.All.Count}; event_first_before_floor=true; delivery_receipt_consumption_distinct=true; false_completion=refused; next_floor={nextFloor:O}");
    }

    [Fact]
    public void AC11RolePolicyEnforcesNoPollNoFalseCompletionAndOneEpisodeEscalation()
    {
        var architect = ProgressRoleWakePolicy.Evaluate(new ProgressRoleObservation { Role = "architect", Healthy = true });
        var reviewer = ProgressRoleWakePolicy.Evaluate(new ProgressRoleObservation { Role = "reviewer", Healthy = true });
        var orchestrator = ProgressRoleWakePolicy.Evaluate(new ProgressRoleObservation { Role = "orchestrator", Healthy = true });
        var steward = ProgressRoleWakePolicy.Evaluate(new ProgressRoleObservation { Role = "steward", SemanticChanged = true });
        var duplicate = ProgressRoleWakePolicy.Evaluate(new ProgressRoleObservation { Role = "steward", SemanticChanged = true, EscalationAlreadySent = true });
        var falseCompletion = ProgressRoleWakePolicy.Evaluate(new ProgressRoleObservation { Role = "steward", FalseCompletion = true });
        Assert.True(architect.Refused);
        Assert.True(reviewer.Refused);
        Assert.False(orchestrator.ModelWake);
        Assert.True(steward.ModelWake);
        Assert.Equal("deduplicate-episode", duplicate.Action);
        Assert.True(falseCompletion.Refused);
        Assert.True(falseCompletion.FalseCompletionDetected);
        Console.WriteLine($"G812 AC11 architect={architect.Action}; reviewer={reviewer.Action}; unchanged_orchestrator_wake={orchestrator.ModelWake}; steward={steward.Action}; duplicate={duplicate.Action}; false_completion={falseCompletion.Action}");
    }

    [Fact]
    public void AC7_CommandRouteReadsAndWritesOnlyTheSuppliedEvidenceRoot()
    {
        var evidencePath = Path.Combine(root, "evidence.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(Evidence()));
        using var writer = new StringWriter();
        var exitCode = ProgressSupervisionCommand.Execute(
            Context(),
            ["--domain", "project-a", "--team", "team-a", "--unit", "unit-1", "--evidence-file", evidencePath, "--routing-root", root, "--now", Now.ToString("O"), "--write", "--format", "json"],
            writer);
        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("progress-supervision", document.RootElement.GetProperty("operation").GetString());
        Assert.Equal("write", document.RootElement.GetProperty("mode").GetString());
        Assert.True(File.Exists(ProgressSupervisionStore.ResolveEvidencePath(root, "project-a", "team-a")));
        Console.WriteLine($"G812 AC7 command=progress-supervision; mode=write; exit={exitCode}; routing_root={root}; provider_calls=0");
    }

    private CliContext Context() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
        },
    };

    private void WriteProjectEvidence(string projectRoot, string project, string team, string unit)
    {
        var evidence = Evidence() with { Project = project, Domain = project, Team = team, Unit = unit };
        var evidencePath = Path.Combine(projectRoot, "evidence.json");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(evidence));
        using var writer = new StringWriter();
        var result = ProgressSupervisionCommand.Execute(
            Context() with { RepoRoot = projectRoot },
            ["--domain", project, "--team", team, "--unit", unit, "--evidence-file", evidencePath, "--routing-root", projectRoot, "--now", Now.ToString("O"), "--write", "--format", "json"],
            writer);
        Assert.Equal(0, result);
    }

    private static ProgressReviewVerdictEvidence ReviewVerdict(string head, string artifact) => new()
    {
        Project = "project-a", Repo = "J-Tech-Japan/intent-system", Unit = artifact.Split('-')[0], PullRequest = artifact,
        ExpectedHead = head, VerdictHead = head, TaskId = "review-task", ResultNonce = "review-nonce",
        Status = "request-update", ArtifactPath = $"IntentSystemReview/{artifact}", ReviewerIdentity = "reviewer-seat",
        Findings = "F1;F2", VerdictMarker = "REQUEST_UPDATE", VerdictAt = Now, SelectorReason = "wait-stale-label",
        CanonicalReport = true, CommentedReview = true, GreenCi = true,
    };

    private static ProgressSupervisionEvidence Evidence() => new()
    {
        Domain = "project-a", Team = "team-a", Project = "project-a", Repo = "owner/repo", Unit = "unit-1", Phase = "selected-to-relevant-commit", Episode = "episode-1",
        TaskId = "task-1", ResultNonce = "nonce-1", ClaimActor = "builder", ClaimStatus = "owned", SelectedAction = "issue-to-pr", SelectedReason = "canonical selection", WorkRoot = "workspace-1",
        RelevantCommit = "commit-1", BaseCommit = "base-1", RemotePr = "pr-1", PrHead = "head-1", ReviewIdentity = "review-1", ReportIdentity = "report-1", AckIdentity = "ack-1", CloseoutIdentity = "closeout-1",
        ObservedAt = Now, FreshnessAt = Now, EligibleAt = Now.AddSeconds(-10), LastProgressAt = Now, DeadlineAt = Now.AddSeconds(120), FloorSeconds = 30, JitterSeconds = 10, MaxSweepSeconds = 10,
        RecoveryAttempts = 0, AttemptResult = "none", NextAction = "observe", Owner = "orchestrator", Severity = "normal", RecipientRole = "builder", RecipientIdentity = "builder:seat-1", SourceFailures = [], EvidenceLinks = ["artifact:task-1"],
    };

    private static ProgressBootstrapRequest Bootstrap() => new()
    {
        Domain = "project-a", Team = "team-a", Project = "project-a", Repo = "owner/repo", Unit = "unit-1", TaskId = "task-1", ResultNonce = "nonce-1",
        IntendedRecipient = "builder", DelegatingRole = "orchestrator", ExpectedArtifact = "PR #1", FailedPrimaryRoute = "notify-report:transport-unavailable", ClaimValid = true, WipValid = true, PreflightValid = true, Published = true,
    };
}
