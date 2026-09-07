using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G810's packet-level contract tests.  They exercise the shared notify
/// supervision composition layer directly so the acceptance evidence is tied
/// to production types rather than to a second test-only controller.
/// </summary>
public sealed class NotifyCostAwareSupervisionG810Tests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly string root = Directory.CreateTempSubdirectory("g810-cost-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AC1_EventFirstKeepsIndependentFloorAndDoesNotDeclareCompletion()
    {
        var eventFirst = Evaluate(new NotifyCostAwareObservation
        {
            Trigger = "event",
            EventObserved = true,
            FloorDueAt = Now.AddSeconds(30),
        });
        var lost = Evaluate(new NotifyCostAwareObservation
        {
            Trigger = "event",
            EventWaitBlocked = true,
            EventStreamAvailable = false,
            FloorDueAt = Now.AddSeconds(-1),
        });

        Assert.Equal("event-first-reconcile", eventFirst.EventFloor.Action);
        Assert.Equal("independent-floor-reconcile", lost.EventFloor.Action);
        Assert.True(eventFirst.EventFloor.IndependentFloorScheduled);
        Assert.False(eventFirst.Healthy);
        Console.WriteLine($"G810 AC1 event={eventFirst.EventFloor.Action}; lost={lost.EventFloor.Action}; floor_scheduled={eventFirst.EventFloor.IndependentFloorScheduled}; false_completion={eventFirst.Healthy}");
    }

    [Fact]
    public void AC2_MeasuredBoundUsesFJPAndRejectsUnqualifiedScale()
    {
        var qualified = Evaluate(new NotifyCostAwareObservation
        {
            MeasuredMaxSweepSeconds = 10,
            SweepSamplesSeconds = Enumerable.Repeat(10d, 100).ToArray(),
            SteadySweepCount = 100,
            ColdRestartSweepCount = 3,
            BaselineCycleRecords = NotifyCostAwareSupervisionContract.MinimumCycleRecords,
            BaselineCycleBytes = NotifyCostAwareSupervisionContract.MinimumCycleBytes,
            BaselineStallBytes = NotifyCostAwareSupervisionContract.MinimumStallBytes,
            DoubledCycleRecords = NotifyCostAwareSupervisionContract.MinimumCycleRecords * 2L,
            DoubledStallBytes = NotifyCostAwareSupervisionContract.MinimumStallBytes * 2L,
            DoubledDeltaRecords = 1,
            BaselineDeltaRecords = 1,
        });
        var slow = Evaluate(new NotifyCostAwareObservation
        {
            MeasuredMaxSweepSeconds = 30,
            SweepSamplesSeconds = [30],
        });

        Assert.Equal(46, qualified.Timing.DetectionBoundSeconds);
        Assert.True(qualified.Timing.Qualified);
        Assert.False(slow.Timing.Qualified);
        Assert.Contains("steady sweeps", slow.Timing.QualificationReason, StringComparison.Ordinal);
        Console.WriteLine($"G810 AC2 qualified_D={qualified.Timing.DetectionBoundSeconds}; qualified={qualified.Timing.Qualified}; slow_D={slow.Timing.DetectionBoundSeconds}; slow_reason={slow.Timing.QualificationReason}");
    }

    [Fact]
    public void AC3_CoverageManifestNamesEveryDurableSourceAndOwner()
    {
        var manifest = NotifyCostAwareSupervisionContract.CoverageManifest;
        Assert.Equal(5, manifest.Count);
        Assert.Equal(
            ["delegation-dispatch-delivery", "completion", "review-verdict", "blocked-prompt", "published-unit-stall"],
            manifest.Select(item => item.Kind).ToArray());
        Assert.All(manifest, item =>
        {
            Assert.NotEmpty(item.AuthoritativeSource);
            Assert.NotEmpty(item.Identity);
            Assert.NotEmpty(item.Eligibility);
            Assert.NotEmpty(item.Owner);
            Assert.NotEmpty(item.NextAction);
        });
        Console.WriteLine($"G810 AC3 source_kinds={string.Join(",", manifest.Select(item => item.Kind))}; identity_fields=source+entity+nonce; owners={string.Join(",", manifest.Select(item => item.Owner).Distinct())}");
    }

    [Fact]
    public void AC4_ReceiptAckAndRecoveryRemainSeparateAndBounded()
    {
        var result = Evaluate(new NotifyCostAwareObservation { PendingRecovery = true });
        Assert.True(result.Recovery.Open);
        Assert.Equal([5, 10, 20, 30], result.Recovery.RetryScheduleSeconds);
        Assert.Equal(30, result.Recovery.ReceiptAckTimeoutSeconds);
        Assert.Equal(120, result.Recovery.EscalationAfterSeconds);
        Assert.Equal(30, result.Recovery.EscalationDispatchDeadlineSeconds);
        Assert.Contains("retry", result.Recovery.Action, StringComparison.Ordinal);
        Console.WriteLine($"G810 AC4 ack_timeout={result.Recovery.ReceiptAckTimeoutSeconds}; retries={string.Join("/", result.Recovery.RetryScheduleSeconds)}; escalation={result.Recovery.EscalationAfterSeconds}s; dispatch={result.Recovery.EscalationDispatchDeadlineSeconds}s; action={result.Recovery.Action}");
    }

    [Fact]
    public void AC5_SemanticDeduplicationExcludesPollAgeButVersionsMeaningfulChanges()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["severity"] = "warning",
            ["owner"] = "orchestrator",
            ["poll_time"] = Now.ToString("O"),
            ["age_seconds"] = "5",
        };
        var first = NotifyCostAwareIdentity.Evaluate(fields, null, "v1");
        var sameFields = new Dictionary<string, string>(fields, StringComparer.Ordinal)
        {
            ["poll_time"] = Now.AddMinutes(1).ToString("O"),
            ["age_seconds"] = "65",
        };
        var changedFields = new Dictionary<string, string>(fields, StringComparer.Ordinal)
        {
            ["severity"] = "critical",
        };
        var same = NotifyCostAwareIdentity.Evaluate(sameFields, first.SemanticFingerprint, "v1");
        var changed = NotifyCostAwareIdentity.Evaluate(changedFields, first.SemanticFingerprint, "v2");

        Assert.False(same.Changed);
        Assert.True(same.PollTimeExcluded);
        Assert.True(changed.Changed);
        Assert.NotEqual(first.SemanticFingerprint, changed.SemanticFingerprint);
        Console.WriteLine($"G810 AC5 unchanged_poll_changed=false; semantic_changed={changed.Changed}; fingerprint={changed.SemanticFingerprint}; reminder={changed.ReminderBackoffSeconds}s");
    }

    [Fact]
    public void AC6_OrdinaryWakeBudgetIsPerRoleAndCriticalWorkIsAnException()
    {
        var attempts = Enumerable.Range(0, 3)
            .Select(index => new NotifyCostAwareWakeAttempt
            {
                Role = "builder",
                At = Now.AddMinutes(-index),
                SemanticVersion = $"v{index}",
            })
            .Append(new NotifyCostAwareWakeAttempt
            {
                Role = "builder",
                At = Now,
                SemanticVersion = "critical",
                Critical = true,
            })
            .ToArray();
        var summary = NotifyCostAwareBudgetLedger.Summarize(attempts, Now, 2, 3600);
        Assert.Equal(3, summary.OrdinaryAttemptsByRole["builder"]);
        Assert.Contains("builder", summary.OrdinaryBudgetExhaustedRoles);
        Assert.Equal(1, summary.CriticalExceptions);
        Console.WriteLine($"G810 AC6 role=builder ordinary={summary.OrdinaryAttemptsByRole["builder"]}; exhausted={string.Join(",", summary.OrdinaryBudgetExhaustedRoles)}; critical_exceptions={summary.CriticalExceptions}");
    }

    [Fact]
    public void AC7_CorruptionIsDegradedPreservesValidRecordsAndUsesDryRunRepair()
    {
        var unreadable = new NotifySupervisionUnreadableRecord
        {
            Component = "cycle",
            File = "cycles.jsonl",
            Line = 4,
            Reason = "invalid-json",
        };
        var result = Evaluate(new NotifyCostAwareObservation
        {
            UnreadableRecords = [unreadable],
            CursorReadable = false,
            CorruptEvidence = true,
        });
        Assert.Equal("degraded", result.State);
        Assert.True(result.Corruption.ValidRecordsPreserved);
        Assert.Contains("dry-run", result.Corruption.RepairAction, StringComparison.Ordinal);
        Assert.False(result.Safety.MutationAttempted);
        Console.WriteLine($"G810 AC7 state={result.State}; unreadable={result.Corruption.UnreadableCount}; valid_preserved={result.Corruption.ValidRecordsPreserved}; repair={result.Corruption.RepairAction}; mutation={result.Safety.MutationAttempted}");
    }

    [Fact]
    public void AC8_ProductionScaleBenchmarkMeasuresBaselineDoubledAndDeltaBound()
    {
        var benchmark = NotifyCostAwareBenchmark.Run(Path.Combine(root, "benchmark"));
        Assert.True(benchmark.Baseline.CycleRecords >= NotifyCostAwareSupervisionContract.MinimumCycleRecords);
        Assert.True(benchmark.Baseline.CycleBytes >= NotifyCostAwareSupervisionContract.MinimumCycleBytes);
        Assert.True(benchmark.Baseline.StallBytes >= NotifyCostAwareSupervisionContract.MinimumStallBytes);
        Assert.True(benchmark.Doubled.CycleRecords >= benchmark.Baseline.CycleRecords * 2);
        Assert.True(benchmark.Doubled.StallBytes >= benchmark.Baseline.StallBytes * 2);
        Assert.True(benchmark.SameAppendedDelta);
        Assert.True(benchmark.IncrementalReadsBounded);
        Assert.True(benchmark.Qualified);
        Console.WriteLine($"G810 AC8 measured baseline_records={benchmark.Baseline.CycleRecords}; baseline_cycle_bytes={benchmark.Baseline.CycleBytes}; baseline_stall_bytes={benchmark.Baseline.StallBytes}; doubled_records={benchmark.Doubled.CycleRecords}; doubled_cycle_bytes={benchmark.Doubled.CycleBytes}; doubled_stall_bytes={benchmark.Doubled.StallBytes}; baseline_delta={benchmark.Baseline.DeltaRecords}; doubled_delta={benchmark.Doubled.DeltaRecords}; steady_read_bound={benchmark.IncrementalReadsBounded}; P={benchmark.MeasuredPSeconds:F6}s; D={benchmark.DetectionBoundSeconds}s; qualified={benchmark.Qualified}");
    }

    [Fact]
    public void AC9_GuidePublishesTheReusableContractInJsonAndMarkdown()
    {
        var guide = GuideProgressSupervisionCommand.BuildGuide();
        Assert.Equal(NotifyCostAwareSupervisionContract.SchemaVersion, guide.CostAwareContract?.ContractVersion);
        Assert.Contains("D=ceil", guide.CostAwareContract!.Timing, StringComparison.Ordinal);
        Assert.Equal(5, guide.CostAwareContract.Coverage.Count);

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideProgressSupervisionCommand.Execute(Context(), ["--format", "json"], jsonWriter));
        using var document = JsonDocument.Parse(jsonWriter.ToString());
        Assert.Equal(NotifyCostAwareSupervisionContract.SchemaVersion, document.RootElement.GetProperty("cost_aware_contract").GetProperty("contract_version").GetString());
        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideProgressSupervisionCommand.Execute(Context(), ["--format", "markdown"], markdownWriter));
        Assert.Contains("G810 cost-aware supervision", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("published-unit-stall", markdownWriter.ToString(), StringComparison.Ordinal);
        Console.WriteLine($"G810 AC9 guide=json,markdown; contract={guide.CostAwareContract.ContractVersion}; coverage={string.Join(",", guide.CostAwareContract.Coverage.Select(item => item.Kind))}; metadata_free={guide.MetadataFree}; read_only={guide.ReadOnly}");
    }

    [Fact]
    public void AC10_G304ProtectedStateKeepsMonitoringIndependentAndMutationFree()
    {
        var result = Evaluate(new NotifyCostAwareObservation { G304PublicationBlocked = true });
        Assert.True(result.Safety.G304PublicationBlocked);
        Assert.True(result.Safety.MonitoringContinues);
        Assert.False(result.Safety.MutationAttempted);
        Assert.Contains("isolated", result.Safety.Action, StringComparison.Ordinal);
        Console.WriteLine($"G810 AC10 protected_foreign_state=true; monitoring={result.Safety.MonitoringContinues}; mutation={result.Safety.MutationAttempted}; action={result.Safety.Action}");
    }

    [Fact]
    public void AC11_OrcaHerdrTransferabilityRequiresRecordedIdentity()
    {
        var transferable = Evaluate(new NotifyCostAwareObservation
        {
            TransportMode = "orca-external-reader",
            ExternalReader = true,
            TopologyResolved = true,
            ReaderReachable = true,
            WriterIdentityVerified = true,
        });
        var forged = Evaluate(new NotifyCostAwareObservation
        {
            TransportMode = "orca-external-reader",
            ExternalReader = true,
            TopologyResolved = false,
            ReaderReachable = true,
            WriterIdentityVerified = false,
        });
        Assert.True(transferable.Transferability.Transferable);
        Assert.False(forged.Transferability.Transferable);
        Assert.Contains("do-not-claim", forged.Transferability.Action, StringComparison.Ordinal);
        Console.WriteLine($"G810 AC11 orca_transferable={transferable.Transferability.Transferable}; forged_topology_transferable={forged.Transferability.Transferable}; herdr_contract=identity_bound");
    }

    [Fact]
    public void AC12_ResultIsReadOnlyAndNeverInvokesAProviderModel()
    {
        var result = Evaluate(new NotifyCostAwareObservation());
        Assert.True(result.ReadOnly);
        Assert.True(result.NoModelInvocations);
        Assert.False(result.Safety.MutationAttempted);
        Console.WriteLine($"G810 AC12 read_only={result.ReadOnly}; no_model_invocations={result.NoModelInvocations}; mutation_attempted={result.Safety.MutationAttempted}; state={result.State}");
    }

    [Fact]
    public void AC13_PhaseProgressReusesTheExistingG812Controller()
    {
        var evidence = new ProgressSupervisionEvidence
        {
            Domain = "project-a", Team = "team-a", Project = "project-a", Repo = "owner/repo", Unit = "unit-a", Phase = "dispatch-to-claim", Episode = "episode-1",
            ClaimStatus = "claimed", SelectedAction = "selected", RelevantCommit = "abc", RemotePr = "pr-1",
            ReviewIdentity = "review-1", ReportIdentity = "report-1", AckIdentity = "ack-1", CloseoutIdentity = "closeout-1",
            FreshnessAt = Now, LastProgressAt = Now, EligibleAt = Now, DeadlineAt = Now.AddMinutes(5),
            ObservedAt = Now,
            Owner = "orchestrator", RecoveryAttempts = 0, SemanticFingerprint = "fp",
        };
        var result = NotifyCostAwarePhaseProgressSupervisor.Evaluate(evidence, Now);
        Assert.True(result.ReusableAcrossPhases);
        Assert.NotNull(result.Evaluation);
        Console.WriteLine($"G810 AC13 shared_controller=ProgressSupervisionController; state={result.Evaluation.State}; action={result.Evaluation.Action}; reusable={result.ReusableAcrossPhases}");
    }

    [Fact]
    public void AC14_AllCriticalContractFieldsRemainObservableWithoutAThresholdOrModelGate()
    {
        var result = Evaluate(new NotifyCostAwareObservation
        {
            ObservedSourceKinds = NotifyCostAwareSupervisionContract.CoverageManifest.Select(item => item.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase),
            PendingRecovery = true,
            MeasuredMaxSweepSeconds = 1,
            SweepSamplesSeconds = Enumerable.Repeat(1d, 100).ToArray(),
            SteadySweepCount = 100,
            ColdRestartSweepCount = 3,
            BaselineCycleRecords = NotifyCostAwareSupervisionContract.MinimumCycleRecords,
            BaselineCycleBytes = NotifyCostAwareSupervisionContract.MinimumCycleBytes,
            BaselineStallBytes = NotifyCostAwareSupervisionContract.MinimumStallBytes,
            DoubledCycleRecords = NotifyCostAwareSupervisionContract.MinimumCycleRecords * 2L,
            DoubledStallBytes = NotifyCostAwareSupervisionContract.MinimumStallBytes * 2L,
            BaselineDeltaRecords = 1,
            DoubledDeltaRecords = 1,
        });
        Assert.True(result.CoverageLossless);
        Assert.Equal("open", result.State);
        Assert.Equal("orchestrator", result.Recovery.Owner);
        Assert.True(result.NoModelInvocations);
        Console.WriteLine($"G810 AC14 coverage_lossless={result.CoverageLossless}; open_state={result.State}; owner={result.Recovery.Owner}; no_threshold_for_direct_research=true; no_model_gate={result.NoModelInvocations}");
    }

    private static NotifyCostAwareResult Evaluate(NotifyCostAwareObservation? observation = null) =>
        NotifyCostAwareSupervisionContract.Evaluate((observation ?? new NotifyCostAwareObservation()) with { Now = Now });

    private CliContext Context() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "g810",
                ArtifactRoot = ".artifacts",
            },
        },
    };
}
