using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class SoloConductorClaimReleaseG857Tests
{
    private const string Unit = "G857";
    private const string Team = "intent-cli-dev";
    private const string Domain = "intent-cli";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const int PullRequest = 1873;

    [Fact]
    public void GuideStepTenPublishesAllRequiredReceiptsBeforeImplementationRelease_G857()
    {
        var step = GuideSoloConductorCommand.BuildGuide().Loop.Single(row => row.Number == 10);
        Assert.Equal("closeout-and-release", step.Id);
        Assert.Contains("metadata_write_branch", step.Instruction, StringComparison.Ordinal);
        Assert.Contains("both `--role architect` and `--role orchestrator`", step.Instruction, StringComparison.Ordinal);
        Assert.Contains("guide reachability with `--role architect`", step.Instruction, StringComparison.Ordinal);
        Assert.Contains("publish only the exact owned receipt paths", step.Instruction, StringComparison.Ordinal);
        Assert.Contains("plain push", step.Instruction, StringComparison.Ordinal);
        Assert.Contains("`--reason` never overrides the gate", step.Instruction, StringComparison.Ordinal);

        var commands = step.Commands.Select(command => command.Command).ToArray();
        var knowledgeArchitect = Array.FindIndex(commands, text => text.Contains("knowledge-writeback-record --execution-unit <unit> --role architect", StringComparison.Ordinal));
        var knowledgeOrchestrator = Array.FindIndex(commands, text => text.Contains("knowledge-writeback-record --execution-unit <unit> --role orchestrator", StringComparison.Ordinal));
        var guideArchitect = Array.FindIndex(commands, text => text.Contains("guide-reachability-record --execution-unit <unit> --role architect", StringComparison.Ordinal));
        var receiptPublication = Array.FindIndex(commands, text => text.Contains("<knowledge-architect-record> <knowledge-orchestrator-record> <guide-architect-record>", StringComparison.Ordinal));
        var release = Array.FindIndex(commands, text => text.Contains("claim release --scope execution-unit:<unit>", StringComparison.Ordinal));
        Assert.All(new[] { knowledgeArchitect, knowledgeOrchestrator, guideArchitect, receiptPublication, release }, index => Assert.True(index >= 0));
        Assert.True(knowledgeArchitect < receiptPublication);
        Assert.True(knowledgeOrchestrator < receiptPublication);
        Assert.True(guideArchitect < receiptPublication);
        Assert.True(receiptPublication < release);
        Assert.Contains("push origin HEAD:refs/heads/<canonical-claim-branch>", commands[receiptPublication], StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalRecordWritersRequirePublicationBeforeRelease_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "design", hostCommit, note: "optional writer note is preserved");
        RecordKnowledge(repos.Writer, "orchestration", hostCommit);
        RecordGuide(repos.Writer, "design", hostCommit);
        RecordGuide(repos.Writer, "orchestration", hostCommit);

        var beforeRelease = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var blocked = Release(repos.Reader);
        Assert.Equal("completion-blocked", blocked.Status);
        Assert.False(blocked.PushSucceeded);
        Assert.Equal("implementation", blocked.Holder);
        Assert.Equal(Team, blocked.HolderTeam);
        Assert.Equal(beforeRelease, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.True(File.Exists(Path.Combine(repos.CloneForInspection(), ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
        Assert.Equal("satisfied", Duty(blocked, "closeout-queue").State);
        Assert.Equal("satisfied", Duty(blocked, "pr-merged").State);
        Assert.Equal("satisfied", Duty(blocked, "closeout-recorded").State);
        Assert.Equal("missing", Duty(blocked, "knowledge-architect").State);
        Assert.Equal("missing", Duty(blocked, "knowledge-orchestrator").State);
        Assert.Equal("missing", Duty(blocked, "guide-reachability").State);
        foreach (var (id, commandToken, role) in new[]
        {
            ("knowledge-architect", "knowledge-writeback-record", "architect"),
            ("knowledge-orchestrator", "knowledge-writeback-record", "orchestrator"),
            ("guide-reachability", "guide-reachability-record", "architect"),
        })
        {
            var duty = Duty(blocked, id);
            Assert.Contains(commandToken, Assert.Single(duty.RecoveryCommands), StringComparison.Ordinal);
            Assert.Contains($"--role {role}", duty.RecoveryCommands[0], StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.PublicationStep!, StringComparison.Ordinal);
            Assert.Contains("git -C <canonical-host-checkout> add -- <exact-owned-artifact-paths>", duty.PublicationStep!, StringComparison.Ordinal);
        }

        var knowledgeArchitect = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect");
        var knowledgeOrchestrator = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "orchestrator");
        var guideRecord = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect");
        var guideOrchestratorRecord = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            GuideReachabilityRecord.RecordRootRelativePath, Unit, "orchestrator");
        Git(repos.Writer, "add", "--", knowledgeArchitect, knowledgeOrchestrator, guideRecord, guideOrchestratorRecord);
        Git(repos.Writer, "-c", "user.name=test", "-c", "user.email=test@example.invalid",
            "commit", "--quiet", "-m", "publish G857 completion receipts");
        Git(repos.Writer, "push", "origin", "HEAD:refs/heads/main");

        var released = Release(repos.Reader);
        Assert.Equal("released", released.Status);
        Assert.True(released.PushSucceeded);
        Assert.Equal("satisfied", released.SoloConductorCompletion?.Decision);
        Assert.Equal("refs/heads/main", released.SoloConductorCompletion?.CanonicalTargetRef);
        Assert.Empty(Assert.Single(Duty(released, "knowledge-architect").Evidence, evidence => evidence.Role == "architect").Targets!);
        Assert.Equal(new[] { "intent_tree" }, Duty(released, "knowledge-architect").DeclaredFacets);
        Assert.Equal(new[] { "intents/intent-cli/intent-tree/means/08-agent-message-orchestration.md" },
            Duty(released, "knowledge-architect").DeclaredTargets);
        Assert.NotEmpty(Duty(released, "guide-reachability").DeclaredRoutes!);
        Assert.NotEmpty(Assert.Single(Duty(released, "guide-reachability").Evidence, evidence => evidence.Role == "architect").GuideSurfaces!);
        Assert.NotEmpty(Assert.Single(Duty(released, "guide-reachability").Evidence, evidence => evidence.Role == "architect").RoutingRoles!);
        using var inspection = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repos.CloneForInspection(), released.HistoryPath!)));
        Assert.Equal("release", inspection.RootElement.GetProperty("operation").GetString());
        var releasedSnapshot = repos.CloneForInspection();
        Assert.False(File.Exists(Path.Combine(releasedSnapshot, ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
        Assert.Single(ClaimHistory(releasedSnapshot));
    }

    [Fact]
    public void ExplicitLoopEvidenceOverrideUsesActualWritersAndPublishesOneAtomicAudit_G860()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: []);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);

        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        var knowledgePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "orchestrator");
        var guidePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect");
        Assert.True(File.Exists(Path.Combine(repos.Writer, knowledgePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(repos.Writer, guidePath.Replace('/', Path.DirectorySeparatorChar))));

        var closeoutOutput = new StringWriter();
        var closeoutExit = CloseoutPrCommand.Execute(Context(repos.Writer),
            ["--pr", PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
             "--repo", Repo, "--domain", Domain, "--pr-merged", "true", "--repair-runs", "--write", "--format", "json"],
            closeoutOutput);
        Assert.Equal(0, closeoutExit);
        using (var closeoutJson = JsonDocument.Parse(closeoutOutput.ToString()))
            Assert.True(closeoutJson.RootElement.GetProperty("runs_appended").GetBoolean());
        var selectedRunsPath = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        var emittedEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(selectedRunsPath));
        Assert.Contains(emittedEvents, item => item.Event == "pr-merged" && item.ExecutionUnit == Unit
            && item.Repo == Repo && item.Pr == PullRequest);
        Assert.Contains(emittedEvents, item => item.Event == "closeout-recorded" && item.ExecutionUnit == Unit
            && item.Repo == Repo && item.Pr == PullRequest);
        var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var queue = QueueStateSerializer.Deserialize(File.ReadAllText(queuePath));
        var completed = Assert.Single(queue.Items!, item => item.ExecutionUnit == Unit);
        Assert.Equal(QueueItemState.Completed, completed.State);
        Assert.Equal($"https://github.com/{Repo}/pull/{PullRequest}", completed.LinkedPr);

        repos.PublishIntentChanges(repos.Writer, "main");
        var canonicalBeforeRelease = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var ordinary = Release(repos.Reader);
        Assert.Equal("completion-blocked", ordinary.Status);
        Assert.Equal("missing", Duty(ordinary, "knowledge-architect").State);
        Assert.Equal("attributed-knowledge-record-missing", Duty(ordinary, "knowledge-architect").Cause);
        Assert.Equal("satisfied", Duty(ordinary, "knowledge-orchestrator").State);
        Assert.Equal("satisfied", Duty(ordinary, "guide-reachability").State);
        Assert.Equal(canonicalBeforeRelease, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());

        using var before = JsonDocument.Parse(Git(repos.Bare, "show", $"refs/heads/main:{ClaimCommand.ClaimPath($"execution-unit:{Unit}")}"));
        var claimedAt = before.RootElement.GetProperty("claimed_at").GetDateTimeOffset();
        var priorLogBytes = File.ReadAllBytes(Path.Combine(repos.CloneForInspection(),
            Path.GetRelativePath(repos.Writer, selectedRunsPath)));
        var snapshotOid = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();

        var output = new StringWriter();
        var exit = ClaimCommand.ExecuteRelease(Context(repos.Reader),
            ["--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
             "--reason", "deliberate-fixture-waiver", "--override-loop-evidence", "--write", "--format", "json"], output);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output.ToString());
        var root = result.RootElement;
        Assert.Equal("released", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("push_succeeded").GetBoolean());
        var historyPath = root.GetProperty("history_path").GetString()!;
        var overrideResult = root.GetProperty("loop_evidence_override");
        Assert.Equal("applied", overrideResult.GetProperty("disposition").GetString());
        Assert.True(overrideResult.GetProperty("published").GetBoolean());
        Assert.Equal("knowledge-architect", Assert.Single(overrideResult.GetProperty("skipped_duties").EnumerateArray()).GetProperty("id").GetString());
        var auditPath = overrideResult.GetProperty("audit_path").GetString()!;
        var expectedAuditPath = $".intent-cli/loop-evidence-overrides/{Unit}/{Path.GetFileName(historyPath)}";
        Assert.Equal(expectedAuditPath, auditPath);
        Assert.EndsWith(".json", auditPath, StringComparison.Ordinal);
        Assert.DoesNotContain(".json.json", auditPath, StringComparison.Ordinal);
        Assert.Equal(historyPath, overrideResult.GetProperty("history_path").GetString());
        var transactionCommit = root.GetProperty("commit").GetString()!;
        Assert.Equal(transactionCommit, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        var auditText = Git(repos.Bare, "show", $"{transactionCommit}:{auditPath}");
        using var audit = JsonDocument.Parse(auditText);
        Assert.Equal(1, audit.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal("claim-release", audit.RootElement.GetProperty("operation").GetString());
        Assert.Equal($"execution-unit:{Unit}", audit.RootElement.GetProperty("scope").GetString());
        Assert.Equal(Unit, audit.RootElement.GetProperty("execution_unit").GetString());
        Assert.Equal(Domain, audit.RootElement.GetProperty("domain").GetString());
        Assert.Equal(Team, audit.RootElement.GetProperty("team").GetString());
        Assert.Equal("implementation", audit.RootElement.GetProperty("actor").GetString());
        Assert.Equal("builder", audit.RootElement.GetProperty("normalized_role").GetString());
        Assert.Equal(claimedAt, audit.RootElement.GetProperty("displaced_claimed_at").GetDateTimeOffset());
        Assert.Equal(Repo, audit.RootElement.GetProperty("target_repo").GetString());
        Assert.Equal(PullRequest, audit.RootElement.GetProperty("linked_pr").GetInt32());
        Assert.Equal(snapshotOid, audit.RootElement.GetProperty("canonical_snapshot_oid").GetString());
        Assert.Equal("refs/heads/main", audit.RootElement.GetProperty("canonical_target_ref").GetString());
        Assert.Equal("deliberate-fixture-waiver", audit.RootElement.GetProperty("reason").GetString());
        Assert.Equal(historyPath, audit.RootElement.GetProperty("release_history_path").GetString());
        Assert.Equal(Path.GetRelativePath(repos.Writer, selectedRunsPath).Replace(Path.DirectorySeparatorChar, '/'),
            audit.RootElement.GetProperty("run_log_path").GetString());
        var historyText = Git(repos.Bare, "show", $"{transactionCommit}:{historyPath}");
        using var history = JsonDocument.Parse(historyText);
        Assert.Equal(audit.RootElement.GetProperty("recorded_at").GetDateTimeOffset(),
            history.RootElement.GetProperty("recorded_at").GetDateTimeOffset());
        Assert.Equal(snapshotOid, history.RootElement.GetProperty("base_commit").GetString());
        var committedRuns = Git(repos.Bare, "show", $"{transactionCommit}:{overrideResult.GetProperty("run_log_path").GetString()}");
        Assert.StartsWith(System.Text.Encoding.UTF8.GetString(priorLogBytes), committedRuns, StringComparison.Ordinal);
        var runEvents = RunLogSerializer.DeserializeAll(committedRuns);
        var overrideEvent = Assert.Single(runEvents, item => item.Event == "loop-evidence-override");
        Assert.Equal(Unit, overrideEvent.ExecutionUnit);
        Assert.Equal("intent-cli claim release", overrideEvent.By);
        Assert.Equal("deliberate-fixture-waiver", overrideEvent.Reason);
        Assert.Equal(Repo, overrideEvent.Repo);
        Assert.Equal(PullRequest, overrideEvent.Pr);
        Assert.Equal($"https://github.com/{Repo}/pull/{PullRequest}", overrideEvent.LinkedPr);
        Assert.Equal("solo-conductor", overrideEvent.TeamMode);
        Assert.Equal("builder", overrideEvent.ActorRole);
        Assert.Equal(auditPath, overrideEvent.ResultRef);
        Assert.DoesNotContain(ClaimCommand.ClaimPath($"execution-unit:{Unit}"),
            Git(repos.Bare, "ls-tree", "-r", "--name-only", transactionCommit));
        Assert.Equal("refused", root.GetProperty("solo_conductor_completion").GetProperty("decision").GetString());
        Assert.Equal("missing", Assert.Single(root.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "knowledge-architect").GetProperty("state").GetString());
        var inspection = repos.CloneForInspection();
        Assert.False(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
        Assert.Single(ClaimHistory(inspection));
    }

    [Fact]
    public void LoopEvidenceOverridePureEligibilityUsesClosedAllowlist_G860()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var gate = EvaluateCanonicalSnapshot(repos.Writer);
        var completion = gate.Completion!;

        var allThreeMissing = ClaimLoopEvidenceOverride.Evaluate(gate);
        Assert.True(allThreeMissing.Eligible);
        Assert.Equal(new[] { "knowledge-architect", "knowledge-orchestrator", "guide-reachability" },
            allThreeMissing.SkippedDuties.Select(duty => duty.Id));
        Assert.Equal(new[] { "architect", "orchestrator", "architect" },
            allThreeMissing.SkippedDuties.Select(duty => duty.Role));

        var onlyArchitectMissing = completion.Duties.Select(duty => duty.Id switch
        {
            "knowledge-orchestrator" => duty with { State = "satisfied", Cause = "attributed-knowledge-record-present" },
            "guide-reachability" => duty with { State = "satisfied", Cause = "architect-guide-record-present" },
            _ => duty,
        }).ToArray();
        var onlyArchitectGate = gate with { Completion = completion with { Duties = onlyArchitectMissing } };
        var onlyArchitect = ClaimLoopEvidenceOverride.Evaluate(onlyArchitectGate);
        Assert.True(onlyArchitect.Eligible);
        Assert.Equal("knowledge-architect", Assert.Single(onlyArchitect.SkippedDuties).Id);

        var satisfiedDuties = completion.Duties.Select(duty => duty.Id switch
        {
            "knowledge-architect" => duty with { State = "satisfied", Cause = "attributed-knowledge-record-present" },
            "knowledge-orchestrator" => duty with { State = "satisfied", Cause = "attributed-knowledge-record-present" },
            "guide-reachability" => duty with { State = "satisfied", Cause = "architect-guide-record-present" },
            _ => duty,
        }).ToArray();
        var satisfied = completion with { Decision = "satisfied", Duties = satisfiedDuties };
        var satisfiedGate = gate with { Completion = satisfied };
        Assert.False(ClaimLoopEvidenceOverride.Evaluate(satisfiedGate).Eligible);
        Assert.Equal("no-missing-receipts", ClaimLoopEvidenceOverride.Evaluate(satisfiedGate).Cause);

        var satisfiedWithUnknownDuty = satisfied with
        {
            Duties = satisfiedDuties.Append(new SoloConductorClaimReleaseDuty
            {
                Id = "future-duty", State = "satisfied", Cause = "future-receipt-present",
                Detail = "synthetic forward-compatible duty", Evidence = [], RecoveryCommands = [],
            }).ToArray(),
        };
        var malformedSatisfied = ClaimLoopEvidenceOverride.Evaluate(gate with { Completion = satisfiedWithUnknownDuty });
        Assert.False(malformedSatisfied.Eligible);
        Assert.Equal("duty-shape-unknown", malformedSatisfied.Cause);
        var duplicateSatisfied = ClaimLoopEvidenceOverride.Evaluate(gate with
        {
            Completion = satisfied with { Duties = satisfiedDuties.Append(Duty(satisfied, "pr-merged")).ToArray() },
        });
        Assert.False(duplicateSatisfied.Eligible);
        Assert.Equal("duty-shape-unknown", duplicateSatisfied.Cause);
        var unsafeSatisfied = ClaimLoopEvidenceOverride.Evaluate(satisfiedGate with { RunLogRelativePath = "../outside.jsonl" });
        Assert.False(unsafeSatisfied.Eligible);
        Assert.Equal("identity-unavailable", unsafeSatisfied.Cause);

        var queue = Duty(completion, "closeout-queue");
        var malformedCases = new[]
        {
            completion.Duties.Append(queue).ToArray(),
            completion.Duties.Append(null!).ToArray(),
            completion.Duties.Append(queue with { Id = "future-required-duty" }).ToArray(),
            completion.Duties.Where(item => item.Id != "closeout-recorded").ToArray(),
            completion.Duties.Select(item => item.Id == "closeout-queue" ? item with { State = "missing" } : item).ToArray(),
            completion.Duties.Select(item => item.Id == "pr-merged" ? item with { Cause = "legacy-closeout-receipt" } : item).ToArray(),
            completion.Duties.Append(new SoloConductorClaimReleaseDuty
            {
                Id = "knowledge-writeback", State = "not-applicable", Cause = "explicit-no-required-duty",
                Detail = "synthetic mixed shape", Evidence = [], RecoveryCommands = [],
            }).ToArray(),
            completion.Duties.Select(item => item.Id == "knowledge-architect" ? item with { Cause = "knowledge-record-unavailable" } : item).ToArray(),
            completion.Duties.Select(item => item.Id == "guide-reachability" ? item with { State = "unknown" } : item).ToArray(),
            completion.Duties.Select(item => item.Id == "guide-reachability" ? item with { Cause = "unknown-guide-cause" } : item).ToArray(),
        };
        foreach (var duties in malformedCases)
        {
            var result = ClaimLoopEvidenceOverride.Evaluate(gate with { Completion = completion with { Duties = duties } });
            Assert.False(result.Eligible);
            Assert.NotEmpty(result.Cause);
        }

        var noSelectedLog = ClaimLoopEvidenceOverride.Evaluate(gate with { RunLogRelativePath = null });
        Assert.False(noSelectedLog.Eligible);
        Assert.Equal("identity-unavailable", noSelectedLog.Cause);
    }

    [Theory]
    [InlineData("knowledge-orchestrator", "orchestrator")]
    [InlineData("guide-reachability", "architect")]
    public void OverrideCanWaiveEachRemainingSingleAllowedDuty_G860(string missingId, string expectedRole)
    {
        using var repos = new ClaimRepositories();
        PrepareActualOverrideSnapshot(repos, missingId);

        var output = new StringWriter();
        Assert.Equal(0, RunOverrideCli(repos.Reader, output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("released", result.RootElement.GetProperty("status").GetString());
        var skipped = Assert.Single(result.RootElement.GetProperty("loop_evidence_override").GetProperty("skipped_duties").EnumerateArray());
        Assert.Equal(missingId, skipped.GetProperty("id").GetString());
        Assert.Equal(expectedRole, skipped.GetProperty("role").GetString());
        var completion = result.RootElement.GetProperty("solo_conductor_completion");
        Assert.Equal("refused", completion.GetProperty("decision").GetString());
        Assert.Equal("missing", Assert.Single(completion.GetProperty("duties").EnumerateArray(),
            duty => duty.GetProperty("id").GetString() == missingId).GetProperty("state").GetString());
        foreach (var other in new[] { "knowledge-architect", "knowledge-orchestrator", "guide-reachability" }.Where(id => id != missingId))
            Assert.Equal("satisfied", Assert.Single(completion.GetProperty("duties").EnumerateArray(),
                duty => duty.GetProperty("id").GetString() == other).GetProperty("state").GetString());
    }

    [Fact]
    public void OverridePreservesAllThreeMissingDutiesAndEmptyOptionalTargets_G860()
    {
        using var repos = new ClaimRepositories();
        PrepareActualOverrideSnapshot(repos, PacketWithEmptyOptionalTargetsYaml,
            ["knowledge-architect", "knowledge-orchestrator", "guide-reachability"]);

        var output = new StringWriter();
        Assert.Equal(0, RunOverrideCli(repos.Reader, output));
        using var result = JsonDocument.Parse(output.ToString());
        var skipped = result.RootElement.GetProperty("loop_evidence_override").GetProperty("skipped_duties").EnumerateArray().ToArray();
        Assert.Equal(new[] { "knowledge-architect", "knowledge-orchestrator", "guide-reachability" },
            skipped.Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(new[] { "architect", "orchestrator", "architect" },
            skipped.Select(item => item.GetProperty("role").GetString()));
        Assert.Empty(skipped[0].GetProperty("declared_targets").EnumerateArray());
        Assert.Empty(skipped[1].GetProperty("declared_targets").EnumerateArray());
        Assert.Equal("released", result.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void OverrideIsNotApplicableWhenNothingIsMissingOrThePacketDeclaresNoDuty_G860()
    {
        using (var complete = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(complete);
            var head = Git(complete.Bare, "rev-parse", "refs/heads/main").Trim();
            var output = new StringWriter();
            Assert.Equal(1, RunOverrideCli(complete.Reader, output));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("override-not-applicable", result.RootElement.GetProperty("status").GetString());
            Assert.Equal("no-missing-receipts", result.RootElement.GetProperty("loop_evidence_override").GetProperty("cause").GetString());
            Assert.Equal(head, Git(complete.Bare, "rev-parse", "refs/heads/main").Trim());
            Assert.Empty(ClaimHistory(complete.CloneForInspection()));
        }

        using (var noDuty = new ClaimRepositories())
        {
            noDuty.PublishSnapshot(queueState: QueueItemState.Completed,
                runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
            Acquire(noDuty.Writer);
            var head = Git(noDuty.Bare, "rev-parse", "refs/heads/main").Trim();
            var output = new StringWriter();
            var exit = ClaimCommand.ExecuteRelease(Context(noDuty.Reader),
                ["--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
                 "--reason", "deliberate-fixture-waiver", "--override-loop-evidence", "--format", "json"], output);
            Assert.Equal(1, exit);
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("override-not-applicable", result.RootElement.GetProperty("status").GetString());
            Assert.Equal("not-applicable", Assert.Single(result.RootElement.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray(),
                duty => duty.GetProperty("id").GetString() == "knowledge-writeback").GetProperty("state").GetString());
            Assert.Equal("not-applicable", Assert.Single(result.RootElement.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray(),
                duty => duty.GetProperty("id").GetString() == "guide-reachability").GetProperty("state").GetString());
            Assert.Equal(head, Git(noDuty.Bare, "rev-parse", "refs/heads/main").Trim());
        }
    }

    [Fact]
    public void OverrideDoesNotApplyToRecordedNonSoloMode_G860()
    {
        using var repos = new ClaimRepositories();
        PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var now = DateTimeOffset.UtcNow;
        TeamModeStore.Write(repos.Writer, new TeamModeState
        {
            SchemaVersion = TeamModeStore.SchemaVersion,
            Entries =
            [
                new TeamModeEntry
                {
                    Domain = Domain,
                    Team = Team,
                    Mode = TeamMode.Delivery,
                    UpdatedAt = now,
                    Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.Delivery, At = now }],
                },
            ],
        });
        repos.PublishIntentChanges(repos.Writer, "main");
        var head = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();

        var output = new StringWriter();
        Assert.Equal(1, RunOverrideCli(repos.Reader, output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("override-not-applicable", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("not-applicable", result.RootElement.GetProperty("loop_evidence_override").GetProperty("cause").GetString());
        Assert.Equal(head, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Empty(ClaimHistory(repos.CloneForInspection()));
    }

    [Theory]
    [InlineData("knowledge-malformed")]
    [InlineData("queue-incomplete")]
    [InlineData("missing-pr-merged")]
    [InlineData("missing-closeout-recorded")]
    public void OverrideRefusesOneDisallowedDeclarationOrCloseoutCondition_G860(string condition)
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var packetPath = Path.Combine(repos.Writer, KnowledgeWriteBackRecord.PacketRootRelativePath, Unit, "packet.yaml");
        var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var runsPath = Path.Combine(repos.Writer, runsRelative.Replace('/', Path.DirectorySeparatorChar));
        switch (condition)
        {
            case "knowledge-malformed":
                File.WriteAllText(packetPath, File.ReadAllText(packetPath).Replace("required: true", "required: maybe", StringComparison.Ordinal));
                break;
            case "queue-incomplete":
            {
                var queue = QueueStateSerializer.Deserialize(File.ReadAllText(queuePath));
                var changed = queue with
                {
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Items = queue.Items!.Select(item => item with
                    {
                        State = item.ExecutionUnit == Unit ? QueueItemState.Active : item.State,
                    }).ToArray(),
                };
                File.WriteAllText(queuePath, QueueStateSerializer.Serialize(changed));
                break;
            }
            case "missing-pr-merged":
            case "missing-closeout-recorded":
            {
                var missingEvent = condition == "missing-pr-merged" ? "pr-merged" : "closeout-recorded";
                var events = RunLogSerializer.DeserializeAll(File.ReadAllText(runsPath));
                Assert.Contains(events, item => item.Event == missingEvent && item.ExecutionUnit == Unit);
                File.WriteAllText(runsPath, string.Join(Environment.NewLine,
                    events.Where(item => !(item.Event == missingEvent && item.ExecutionUnit == Unit))
                        .Select(RunLogSerializer.SerializeLine)) + Environment.NewLine);
                break;
            }
        }
        repos.PublishIntentChanges(repos.Writer, "main");
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var output = new StringWriter();
        Assert.Equal(1, RunOverrideCli(repos.Reader, output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("completion-blocked", result.RootElement.GetProperty("status").GetString());
        var duties = result.RootElement.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray().ToArray();
        if (condition == "knowledge-malformed")
        {
            Assert.Contains(duties, duty => duty.GetProperty("id").GetString() == "knowledge-declaration"
                && duty.GetProperty("state").GetString() == "unavailable");
        }
        else if (condition == "queue-incomplete")
        {
            Assert.Contains(duties, duty => duty.GetProperty("id").GetString() == "closeout-queue"
                && duty.GetProperty("state").GetString() == "missing");
        }
        else
        {
            var missingEvent = condition == "missing-pr-merged" ? "pr-merged" : "closeout-recorded";
            Assert.Contains(duties, duty => duty.GetProperty("id").GetString() == missingEvent
                && duty.GetProperty("state").GetString() == "missing");
            var otherEvent = missingEvent == "pr-merged" ? "closeout-recorded" : "pr-merged";
            Assert.Contains(duties, duty => duty.GetProperty("id").GetString() == otherEvent
                && duty.GetProperty("state").GetString() == "satisfied");
        }
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Empty(ClaimHistory(repos.CloneForInspection()));
        Assert.Equal("refused", result.RootElement.GetProperty("loop_evidence_override").GetProperty("disposition").GetString());
        Assert.False(result.RootElement.GetProperty("loop_evidence_override").GetProperty("published").GetBoolean());
    }

    [Fact]
    public void EligibleOverridePreviewIsTruthfulAndLeavesCallerAndRemoteUntouched_G860()
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        File.WriteAllText(Path.Combine(repos.Reader, "README.md"), "dirty caller edit\n");
        File.WriteAllText(Path.Combine(repos.Reader, "g860-staged.txt"), "staged caller bytes\n");
        Git(repos.Reader, "add", "--", "g860-staged.txt");
        File.WriteAllText(Path.Combine(repos.Reader, "g860-untracked.txt"), "untracked caller bytes\n");
        var callerHead = Git(repos.Reader, "rev-parse", "HEAD").Trim();
        var callerStatus = Git(repos.Reader, "status", "--porcelain=v1");
        var callerIndex = File.ReadAllBytes(Path.Combine(repos.Reader, ".git", "index"));
        var callerRefs = Git(repos.Reader, "for-each-ref", "--format=%(refname) %(objectname)");
        var readme = File.ReadAllBytes(Path.Combine(repos.Reader, "README.md"));
        var stagedFile = File.ReadAllBytes(Path.Combine(repos.Reader, "g860-staged.txt"));
        var untrackedFile = File.ReadAllBytes(Path.Combine(repos.Reader, "g860-untracked.txt"));
        var remoteHead = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var runLog = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
        var staleRoot = Path.Combine(Path.GetTempPath(), $"intent-cli-claim-g860-stale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staleRoot);
        File.WriteAllText(Path.Combine(staleRoot, "sentinel"), "preview must not sweep\n");
        Directory.SetLastWriteTimeUtc(staleRoot, DateTime.UtcNow.AddMinutes(-10));

        var output = new StringWriter();
        var exit = ClaimCommand.ExecuteRelease(Context(repos.Reader),
            ["--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
             "--reason", "deliberate-fixture-waiver", "--override-loop-evidence", "--format", "json"], output);
        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output.ToString());
        var root = result.RootElement;
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("push_succeeded").GetBoolean());
        Assert.Equal("refused", root.GetProperty("solo_conductor_completion").GetProperty("decision").GetString());
        var waiver = root.GetProperty("loop_evidence_override");
        Assert.Equal("eligible-preview", waiver.GetProperty("disposition").GetString());
        Assert.False(waiver.GetProperty("published").GetBoolean());
        Assert.Contains("<release-history-filename>", waiver.GetProperty("history_path").GetString(), StringComparison.Ordinal);
        Assert.Contains("<release-history-filename>", waiver.GetProperty("audit_path").GetString(), StringComparison.Ordinal);
        Assert.Equal(runsRelative, waiver.GetProperty("run_log_path").GetString());
        Assert.Equal(callerHead, Git(repos.Reader, "rev-parse", "HEAD").Trim());
        Assert.Equal(callerStatus, Git(repos.Reader, "status", "--porcelain=v1"));
        Assert.Equal(callerIndex, File.ReadAllBytes(Path.Combine(repos.Reader, ".git", "index")));
        Assert.Equal(callerRefs, Git(repos.Reader, "for-each-ref", "--format=%(refname) %(objectname)"));
        Assert.Equal(readme, File.ReadAllBytes(Path.Combine(repos.Reader, "README.md")));
        Assert.Equal(stagedFile, File.ReadAllBytes(Path.Combine(repos.Reader, "g860-staged.txt")));
        Assert.Equal(untrackedFile, File.ReadAllBytes(Path.Combine(repos.Reader, "g860-untracked.txt")));
        Assert.Equal(remoteHead, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(runLog, Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}"));
        Assert.True(File.Exists(Path.Combine(staleRoot, "sentinel")));
        var inspection = repos.CloneForInspection();
        Assert.Empty(ClaimHistory(inspection));
        Assert.False(Directory.Exists(Path.Combine(inspection, ".intent-cli", "loop-evidence-overrides")));
        Directory.Delete(staleRoot, recursive: true);
    }

    [Theory]
    [InlineData("--override-loop-evidence", "--override-loop-evidence")]
    [InlineData("--override-loop-evidence=true")]
    public void OverrideFlagRejectsDuplicateOrValueBearingFormsBeforeTransaction_G860(params string[] malformedFlag)
    {
        var args = new List<string>
        {
            "--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
            "--reason", "parser fixture",
        };
        args.AddRange(malformedFlag);
        var output = new StringWriter();
        var exit = ClaimCommand.ExecuteRelease(Context(Path.GetTempPath()), args.ToArray(), output);
        Assert.Equal(1, exit);
        Assert.Contains("--override-loop-evidence", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void OverrideStillRequiresNonblankReasonBeforeTransaction_G860(string? reason)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: []);
        Acquire(repos.Writer);
        var before = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var args = new List<string>
        {
            "--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
            "--override-loop-evidence", "--write", "--format", "json",
        };
        if (reason is not null) args.AddRange(["--reason", reason]);
        var output = new StringWriter();
        Assert.Equal(1, ClaimCommand.ExecuteRelease(Context(repos.Reader), args.ToArray(), output));
        Assert.Contains("require --reason", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Empty(ClaimHistory(repos.CloneForInspection()));
    }

    [Fact]
    public void OverrideFlagRejectsUnsupportedOperationsScopesAndActors_G860()
    {
        var supportedArgs = new[]
        {
            "--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
            "--reason", "parser fixture", "--override-loop-evidence", "--write", "--format", "json",
        };
        foreach (var scope in new[] { "release-prep:J-Tech-Japan/intent-system:1.0", "execution-unit:../unsafe", "intent-node:G860", "unknown-scope:G860" })
        {
            var args = (string[])supportedArgs.Clone();
            args[1] = scope;
            var output = new StringWriter();
            Assert.Equal(1, ClaimCommand.ExecuteRelease(Context(Path.GetTempPath()), args, output));
            Assert.Contains("--override-loop-evidence", output.ToString(), StringComparison.Ordinal);
        }

        foreach (var actor in new[] { "architect", "design", "orchestrator" })
        {
            var wrongActor = (string[])supportedArgs.Clone();
            wrongActor[3] = actor;
            var actorOutput = new StringWriter();
            Assert.Equal(1, ClaimCommand.ExecuteRelease(Context(Path.GetTempPath()), wrongActor, actorOutput));
            Assert.Contains("builder-normalized actor", actorOutput.ToString(), StringComparison.Ordinal);
        }

        var acquireOutput = new StringWriter();
        Assert.Equal(1, ClaimCommand.ExecuteAcquire(Context(Path.GetTempPath()), supportedArgs, acquireOutput));
        Assert.Contains("only by claim release", acquireOutput.ToString(), StringComparison.Ordinal);

        foreach (var (route, prefix) in new[]
        {
            (new[] { "claim", "takeover" }, Array.Empty<string>()),
            (new[] { "claim", "verify" }, Array.Empty<string>()),
            (new[] { "claim", "stranded", "migrate" }, Array.Empty<string>()),
        })
        {
            var output = new StringWriter();
            var routeArgs = new List<string> { "--override-loop-evidence" };
            routeArgs.AddRange(supportedArgs.Where(argument => argument != "--override-loop-evidence"));
            Assert.Equal(1, CommandRouter.Execute([.. route, .. prefix, .. routeArgs], Context(Path.GetTempPath()), output));
            Assert.Contains("--override-loop-evidence", output.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlaggedAbsentClaimAndWrongTeamRemainOwnershipRefusals_G860(bool write)
    {
        using (var absent = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(absent, "knowledge-architect");
            var active = Path.Combine(absent.Writer, ClaimCommand.ClaimPath($"execution-unit:{Unit}")
                .Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(active));
            File.Delete(active);
            absent.PublishIntentChanges(absent.Writer, "main");
            var before = Git(absent.Bare, "rev-parse", "refs/heads/main").Trim();
            foreach (var format in new[] { "json", "markdown" })
            {
                var output = new StringWriter();
                Assert.Equal(1, RunOverrideCli(absent.Reader, output, write: write, format: format));
                AssertOwnershipRefusalOutput(output.ToString(), format, "not-held", "claim-not-held");
                Assert.Equal(before, Git(absent.Bare, "rev-parse", "refs/heads/main").Trim());
            }
            Assert.Empty(ClaimHistory(absent.CloneForInspection()));
            Assert.DoesNotContain(Git(absent.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main"),
                $".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal);
        }
        using (var wrongTeam = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(wrongTeam, "knowledge-architect");
            var before = Git(wrongTeam.Bare, "rev-parse", "refs/heads/main").Trim();
            foreach (var (actor, team) in new[] { ("implementation", "another-team"), ("builder", Team) })
            {
                foreach (var format in new[] { "json", "markdown" })
                {
                    var output = new StringWriter();
                    Assert.Equal(1, RunOverrideCli(wrongTeam.Reader, output, actor: actor, team: team, write: write, format: format));
                    AssertOwnershipRefusalOutput(output.ToString(), format, "held", "holder-identity-mismatch");
                    if (format == "json")
                    {
                        using var result = JsonDocument.Parse(output.ToString());
                        Assert.Equal(Team, result.RootElement.GetProperty("holder_team").GetString());
                    }
                    else
                    {
                        Assert.Contains($"- holder_team: {Team}", output.ToString(), StringComparison.Ordinal);
                    }
                    Assert.Equal(before, Git(wrongTeam.Bare, "rev-parse", "refs/heads/main").Trim());
                }
            }

            foreach (var format in new[] { "json", "markdown" })
            {
                var output = new StringWriter();
                Assert.Equal(1, RunOverrideCli(wrongTeam.Reader, output, team: "another-team", write: false,
                    format: format, overrideLoopEvidence: false));
                AssertOwnershipRefusalOmitsOverride(output.ToString(), format, "held");
            }
            Assert.Empty(ClaimHistory(wrongTeam.CloneForInspection()));
            Assert.DoesNotContain(Git(wrongTeam.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main"),
                $".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OverrideAuditUnsafeAncestorAbortsBeforeAnyCanonicalReleaseMutation_G860()
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var collision = Path.Combine(repos.Writer, ".intent-cli", "loop-evidence-overrides", Unit);
        Directory.CreateDirectory(Path.GetDirectoryName(collision)!);
        File.WriteAllText(collision, "occupied by unrelated evidence\n");
        repos.PublishIntentChanges(repos.Writer, "main");

        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var logBefore = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
        var output = new StringWriter();
        var exit = RunOverrideCli(repos.Reader, output);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("error", result.RootElement.GetProperty("status").GetString());
        Assert.Contains("Transaction path ancestor is not a directory: .intent-cli/loop-evidence-overrides/G857", result.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.False(result.RootElement.GetProperty("push_succeeded").GetBoolean());
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
        Assert.Equal(logBefore, Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}"));
        var inspection = repos.CloneForInspection();
        Assert.Empty(ClaimHistory(inspection));
        Assert.False(Directory.Exists(Path.Combine(inspection, ".intent-cli", "loop-evidence-overrides", Unit)));
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("commit")]
    public void OverrideStageAndCommitFailuresAreReachedAfterAuditPreparation_G860(string fault)
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var logBefore = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        var bin = Path.Combine(Path.GetDirectoryName(repos.Reader)!, $"g860-{fault}-bin");
        Directory.CreateDirectory(bin);
        var wrapper = Path.Combine(bin, "git");
        var marker = Path.Combine(bin, "fault-reached.txt");
        var staged = Path.Combine(bin, "stage-succeeded.txt");
        var realGit = FindGitExecutable();
        var transactionClaimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        File.WriteAllText(wrapper,
            "#!/bin/sh\nset -eu\nPWD=$(pwd)\n"
            + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(repos.Bare)}\nMARKER={ShellQuote(marker)}\nSTAGED={ShellQuote(staged)}\nCLAIM={ShellQuote(transactionClaimPath)}\n"
            + "case \"$PWD\" in */intent-cli-claim-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
            + "origin=$(\"$REAL\" -C \"$PWD\" remote get-url origin)\n"
            + "[ \"$origin\" = \"$BARE\" ] || exec \"$REAL\" \"$@\"\n"
            + (fault == "stage"
                ? "if [ \"$1\" = add ] && [ \"$2\" = -- ]; then\n"
                  + "  audit=$(find \"$PWD/.intent-cli/loop-evidence-overrides/G857\" -type f -name '*.json' -print -quit)\n"
                  + "  history=$(find \"$PWD/.intent-cli/claims/history\" -type f -name '*-release.json' -print -quit)\n"
                  + "  runs=$(find \"$PWD/.intent-cli\" -type f -name '*.jsonl' -print | while IFS= read -r file; do grep -q 'loop-evidence-override' \"$file\" && { echo \"$file\"; break; }; done)\n"
                  + "  [ -n \"$audit\" ] && [ -n \"$history\" ] && [ -n \"$runs\" ]\n"
                  + "  [ ! -e \"$PWD/$CLAIM\" ]\n"
                  + "  printf '%s\\n' \"$PWD\" \"$@\" > \"$MARKER\"\n"
                  + "  echo G860_STAGE_FAULT_REACHED >&2\n  exit 1\nfi\n"
                : "if [ \"$1\" = add ] && [ \"$2\" = -- ]; then\n  \"$REAL\" \"$@\"\n  : > \"$STAGED\"\n  exit 0\nfi\n"
                  + "if [ \"$1\" = -c ] && [ \"$2\" = user.name=implementation ] && [ \"$3\" = -c ] && [ \"$4\" = user.email=implementation@claims.invalid ] && [ \"$5\" = commit ] && [ \"$6\" = --quiet ] && [ \"$7\" = -m ]; then\n"
                  + "  [ -f \"$STAGED\" ]\n"
                  + "  audit=$(find \"$PWD/.intent-cli/loop-evidence-overrides/G857\" -type f -name '*.json' -print -quit)\n"
                  + "  history=$(find \"$PWD/.intent-cli/claims/history\" -type f -name '*-release.json' -print -quit)\n"
                  + "  [ -n \"$audit\" ] && [ -n \"$history\" ]\n"
                  + "  printf '%s\\n' \"$PWD\" \"$@\" > \"$MARKER\"\n"
                  + "  echo G860_COMMIT_FAULT_REACHED >&2\n  exit 1\nfi\n")
            + "exec \"$REAL\" \"$@\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
            var output = new StringWriter();
            var exit = RunOverrideCli(repos.Reader, output);
            Assert.Equal(1, exit);
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("error", result.RootElement.GetProperty("status").GetString());
            Assert.False(result.RootElement.GetProperty("push_succeeded").GetBoolean());
            Assert.True(File.Exists(marker), $"the {fault} wrapper branch must be reached");
            var markerLines = File.ReadAllLines(marker);
            Assert.Contains("intent-cli-claim-", markerLines[0], StringComparison.Ordinal);
            if (fault == "stage")
            {
                Assert.Equal("add", markerLines[1]);
                Assert.Equal("--", markerLines[2]);
                Assert.Equal(transactionClaimPath, markerLines[3]);
                Assert.StartsWith(".intent-cli/claims/history/", markerLines[4], StringComparison.Ordinal);
                Assert.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", markerLines[5], StringComparison.Ordinal);
                Assert.Equal(runsRelative, markerLines[6]);
                Assert.Contains("G860_STAGE_FAULT_REACHED", result.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(new[] { "-c", "user.name=implementation", "-c", "user.email=implementation@claims.invalid", "commit", "--quiet", "-m", $"claim: release execution-unit:{Unit}" }, markerLines.Skip(1));
                Assert.Contains("G860_COMMIT_FAULT_REACHED", result.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
                Assert.True(File.Exists(staged), "real git add must succeed before the commit fault");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }

        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
        Assert.Equal(logBefore, Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}"));
        var inspection = repos.CloneForInspection();
        Assert.Empty(ClaimHistory(inspection));
        Assert.False(Directory.Exists(Path.Combine(inspection, ".intent-cli", "loop-evidence-overrides", Unit)));
    }

    [Fact]
    public void OverrideReleaseReportsAppliedPointersWhenPushReturnedFailureAfterExactRemoteCommit_G860()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var previousPath = Environment.GetEnvironmentVariable("PATH");
        var bin = Path.Combine(Path.GetDirectoryName(repos.Reader)!, "g860-may-have-applied-bin");
        Directory.CreateDirectory(bin);
        var wrapper = Path.Combine(bin, "git");
        var marker = Path.Combine(bin, "push-applied-before-error.txt");
        var realGit = FindGitExecutable();
        File.WriteAllText(wrapper,
            "#!/bin/sh\nset -eu\nPWD=$(pwd)\n"
            + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(repos.Bare)}\nMARKER={ShellQuote(marker)}\n"
            + "case \"$PWD\" in */intent-cli-claim-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
            + "origin=$(\"$REAL\" -C \"$PWD\" remote get-url origin)\n"
            + "[ \"$origin\" = \"$BARE\" ] || exec \"$REAL\" \"$@\"\n"
            + "if [ \"$1\" = push ] && [ \"$2\" = origin ]; then\n"
            + "  \"$REAL\" \"$@\"\n"
            + "  printf '%s\\n' \"$PWD\" \"$@\" > \"$MARKER\"\n"
            + "  echo G860_PUSH_APPLIED_THEN_ERROR >&2\n  exit 73\nfi\n"
            + "exec \"$REAL\" \"$@\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
            var output = new StringWriter();
            Assert.Equal(0, RunOverrideCli(repos.Reader, output));
            Assert.True(File.Exists(marker), "the push wrapper must publish, then return a failure");
            var markerText = File.ReadAllText(marker);
            Assert.Contains("push", markerText, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", markerText, StringComparison.Ordinal);
            using var result = JsonDocument.Parse(output.ToString());
            var root = result.RootElement;
            Assert.Equal("released", root.GetProperty("status").GetString());
            Assert.True(root.GetProperty("push_succeeded").GetBoolean());
            var transactionCommit = root.GetProperty("commit").GetString()!;
            Assert.Equal(transactionCommit, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            var overrideResult = root.GetProperty("loop_evidence_override");
            Assert.Equal("applied", overrideResult.GetProperty("disposition").GetString());
            Assert.Equal("allowed-missing-receipts", overrideResult.GetProperty("cause").GetString());
            Assert.True(overrideResult.GetProperty("published").GetBoolean());
            Assert.NotEmpty(overrideResult.GetProperty("audit_path").GetString()!);
            Assert.NotEmpty(overrideResult.GetProperty("history_path").GetString()!);
            Assert.Equal(runsRelative, overrideResult.GetProperty("run_log_path").GetString());
            var tree = Git(repos.Bare, "ls-tree", "-r", "--name-only", transactionCommit);
            Assert.Contains(overrideResult.GetProperty("audit_path").GetString()!, tree, StringComparison.Ordinal);
            Assert.Contains(overrideResult.GetProperty("history_path").GetString()!, tree, StringComparison.Ordinal);
            Assert.Contains(runsRelative, tree, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }
    }

    [Fact]
    public void OverrideRebuildsAuditAfterUnrelatedRemoteAdvance_G860()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var initialHead = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var racer = repos.CreateRacer();
        File.WriteAllText(Path.Combine(racer, "g860-unrelated-advance.txt"), "one unrelated canonical advance\n");
        Git(racer, "add", "--", "g860-unrelated-advance.txt");
        Git(racer, "-c", "user.name=race", "-c", "user.email=race@example.invalid",
            "commit", "--quiet", "-m", "advance canonical ref before override retry");
        var racerHead = Git(racer, "rev-parse", "HEAD").Trim();
        var marker = InstallRacerAdvanceOnFirstPush(repos, racer, "g860-retry-race");
        var previousPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(marker) + Path.PathSeparator + previousPath);
            var output = new StringWriter();
            Assert.Equal(0, RunOverrideCli(repos.Reader, output));
            Assert.True(File.Exists(marker), "the first transaction push must lose to the racer");
            using var result = JsonDocument.Parse(output.ToString());
            var root = result.RootElement;
            Assert.Equal("released", root.GetProperty("status").GetString());
            Assert.Equal(2, root.GetProperty("attempts").GetInt32());
            Assert.Equal(racerHead, root.GetProperty("solo_conductor_completion").GetProperty("canonical_snapshot_oid").GetString());
            var overrideResult = root.GetProperty("loop_evidence_override");
            Assert.Equal("applied", overrideResult.GetProperty("disposition").GetString());
            Assert.True(overrideResult.GetProperty("published").GetBoolean());
            var transactionCommit = root.GetProperty("commit").GetString()!;
            Assert.Equal(transactionCommit, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            var auditPath = overrideResult.GetProperty("audit_path").GetString()!;
            using var audit = JsonDocument.Parse(Git(repos.Bare, "show", $"{transactionCommit}:{auditPath}"));
            Assert.Equal(racerHead, audit.RootElement.GetProperty("canonical_snapshot_oid").GetString());
            var committedRunLog = Git(repos.Bare, "show", $"{transactionCommit}:{runsRelative}");
            Assert.Single(RunLogSerializer.DeserializeAll(committedRunLog), item => item.Event == "loop-evidence-override");
            var auditRows = Git(repos.Bare, "ls-tree", "-r", "--name-only", transactionCommit)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal))
                .ToArray();
            Assert.Single(auditRows);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }

        Assert.NotEqual(initialHead, racerHead);
        var inspection = repos.CloneForInspection();
        Assert.False(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
        Assert.Single(ClaimHistory(inspection));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("partial")]
    public void OverrideRetryUsesOnlyFreshlyMissingReceiptSet_G860(string repair)
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect", "guide-reachability");
        var racer = repos.CreateRacer();
        var racerBase = Git(racer, "rev-parse", "HEAD").Trim();
        var knowledgePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect");
        var guidePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect");
        RecordKnowledge(racer, "architect", racerBase);
        Assert.True(File.Exists(Path.Combine(racer, knowledgePath.Replace('/', Path.DirectorySeparatorChar))));
        if (repair == "all")
        {
            RecordGuide(racer, "architect", racerBase);
            Assert.True(File.Exists(Path.Combine(racer, guidePath.Replace('/', Path.DirectorySeparatorChar))));
        }
        else
        {
            Assert.False(File.Exists(Path.Combine(racer, guidePath.Replace('/', Path.DirectorySeparatorChar))));
        }
        var repairPaths = repair == "all" ? new[] { knowledgePath, guidePath } : [knowledgePath];
        Git(racer, ["add", "--", .. repairPaths]);
        Git(racer, "-c", "user.name=race", "-c", "user.email=race@example.invalid",
            "commit", "--quiet", "-m", $"publish actual {repair} receipt repair during override retry");
        var repairedHead = Git(racer, "rev-parse", "HEAD").Trim();
        var marker = InstallRacerAdvanceOnFirstPush(repos, racer, $"g860-receipt-{repair}-race");
        var previousPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(marker) + Path.PathSeparator + previousPath);
            var output = new StringWriter();
            Assert.Equal(0, RunOverrideCli(repos.Reader, output));
            Assert.True(File.Exists(marker), "the first transaction push must be rejected by the actual receipt repair push");
            using var result = JsonDocument.Parse(output.ToString());
            var root = result.RootElement;
            Assert.Equal("released", root.GetProperty("status").GetString());
            Assert.Equal(2, root.GetProperty("attempts").GetInt32());
            Assert.Equal(repairedHead, root.GetProperty("solo_conductor_completion").GetProperty("canonical_snapshot_oid").GetString());
            var waiver = root.GetProperty("loop_evidence_override");
            var transactionCommit = root.GetProperty("commit").GetString()!;
            Assert.Equal(transactionCommit, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            var finalRunLog = Git(repos.Bare, "show", $"{transactionCommit}:{runsRelative}");
            var overrideEvents = RunLogSerializer.DeserializeAll(finalRunLog)
                .Where(item => item.Event == "loop-evidence-override").ToArray();
            if (repair == "all")
            {
                Assert.Equal("not-used-evidence-now-satisfied", waiver.GetProperty("disposition").GetString());
                Assert.False(waiver.GetProperty("published").GetBoolean());
                Assert.Empty(overrideEvents);
                Assert.DoesNotContain("audit_path", waiver.EnumerateObject().Select(property => property.Name));
                Assert.Equal("satisfied", root.GetProperty("solo_conductor_completion").GetProperty("decision").GetString());
                Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", transactionCommit)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
            }
            else
            {
                Assert.Equal("applied", waiver.GetProperty("disposition").GetString());
                Assert.True(waiver.GetProperty("published").GetBoolean());
                Assert.Equal("refused", root.GetProperty("solo_conductor_completion").GetProperty("decision").GetString());
                var duties = root.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray().ToArray();
                Assert.Equal("satisfied", Assert.Single(duties, item => item.GetProperty("id").GetString() == "knowledge-architect").GetProperty("state").GetString());
                Assert.Equal("missing", Assert.Single(duties, item => item.GetProperty("id").GetString() == "guide-reachability").GetProperty("state").GetString());
                Assert.Equal("guide-reachability", Assert.Single(waiver.GetProperty("skipped_duties").EnumerateArray()).GetProperty("id").GetString());
                Assert.Single(overrideEvents);
                var auditPath = waiver.GetProperty("audit_path").GetString()!;
                using var audit = JsonDocument.Parse(Git(repos.Bare, "show", $"{transactionCommit}:{auditPath}"));
                Assert.Equal(repairedHead, audit.RootElement.GetProperty("canonical_snapshot_oid").GetString());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }
    }

    [Fact]
    public void OverrideSameBasePushRejectionPreservesFailureAndPublishesNothing_G860()
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var head = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var runBefore = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
        var hookMarker = repos.InstallRejectingPreReceiveHook();
        var output = new StringWriter();

        Assert.Equal(1, RunOverrideCli(repos.Reader, output));

        Assert.True(File.Exists(hookMarker), "the actual bare-remote receive hook must reject the transaction push");
        using var result = JsonDocument.Parse(output.ToString());
        var root = result.RootElement;
        Assert.Equal("push-rejected", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("push_succeeded").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("git_push_error").GetString()));
        Assert.Equal(head, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
        Assert.Equal(runBefore, Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}"));
        Assert.Empty(ClaimHistory(repos.CloneForInspection()));
        Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries),
            path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
        Assert.Equal("refused", root.GetProperty("loop_evidence_override").GetProperty("disposition").GetString());
        Assert.False(root.GetProperty("loop_evidence_override").GetProperty("published").GetBoolean());
    }

    [Fact]
    public void OverrideUsesLegacySelectedRunsAndConfiguredMetadataBranch_G860()
    {
        using var repos = new ClaimRepositories();
        const string metadataBranch = "intent-metadata";
        repos.ConfigureMetadataWriteBranch(metadataBranch);
        var defaultHeadBeforeSetup = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var runsRelative = PrepareActualOverrideSnapshot(repos, packetYaml: null,
            missingDutyIds: ["knowledge-architect"], legacyOnly: true);
        Assert.Equal(".intent-cli/runs.jsonl", runsRelative);
        var canonicalBefore = Git(repos.Bare, $"rev-parse", $"refs/heads/{metadataBranch}").Trim();
        Assert.Equal(defaultHeadBeforeSetup, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());

        var localArchitectPath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect");
        var localWriter = repos.CreateRacer();
        Git(localWriter, "checkout", "--quiet", "-b", metadataBranch, $"origin/{metadataBranch}");
        RecordKnowledge(localWriter, "architect", canonicalBefore);
        Assert.True(File.Exists(Path.Combine(localWriter, localArchitectPath.Replace('/', Path.DirectorySeparatorChar))),
            "the actual writer must emit a local unpublished receipt");
        var defaultHeadBeforeRelease = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var output = new StringWriter();

        Assert.Equal(0, RunOverrideCli(repos.Reader, output));

        using var result = JsonDocument.Parse(output.ToString());
        var root = result.RootElement;
        Assert.Equal("released", root.GetProperty("status").GetString());
        Assert.Equal($"refs/heads/{metadataBranch}", root.GetProperty("target_ref").GetString());
        Assert.Equal($"refs/heads/{metadataBranch}", root.GetProperty("solo_conductor_completion").GetProperty("canonical_target_ref").GetString());
        Assert.Equal(defaultHeadBeforeRelease, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        var transactionCommit = root.GetProperty("commit").GetString()!;
        Assert.Equal(transactionCommit, Git(repos.Bare, $"rev-parse", $"refs/heads/{metadataBranch}").Trim());
        var waiver = root.GetProperty("loop_evidence_override");
        Assert.Equal(runsRelative, waiver.GetProperty("run_log_path").GetString());
        Assert.Equal("knowledge-architect", Assert.Single(waiver.GetProperty("skipped_duties").EnumerateArray()).GetProperty("id").GetString());
        var auditPath = waiver.GetProperty("audit_path").GetString()!;
        using var audit = JsonDocument.Parse(Git(repos.Bare, "show", $"{transactionCommit}:{auditPath}"));
        Assert.Equal(canonicalBefore, audit.RootElement.GetProperty("canonical_snapshot_oid").GetString());
        Assert.Equal($"refs/heads/{metadataBranch}", audit.RootElement.GetProperty("canonical_target_ref").GetString());
        Assert.False(audit.RootElement.TryGetProperty("head_sha", out _));
        var committedLegacyRuns = Git(repos.Bare, "show", $"{transactionCommit}:{runsRelative}");
        Assert.Single(RunLogSerializer.DeserializeAll(committedLegacyRuns), item => item.Event == "loop-evidence-override");
        Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", transactionCommit)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries),
            path => path.Contains("scoped", StringComparison.Ordinal) && path.EndsWith("runs.jsonl", StringComparison.Ordinal));
        var duties = root.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray().ToArray();
        Assert.Equal("missing", Assert.Single(duties, item => item.GetProperty("id").GetString() == "knowledge-architect").GetProperty("state").GetString());
    }

    [Fact]
    public void OverridePreservesUnterminatedSelectedRunLogPrefix_G860()
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, packetYaml: null,
            missingDutyIds: ["knowledge-architect"], unterminatedRunLog: true);
        var original = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
        Assert.EndsWith("}", original, StringComparison.Ordinal);
        var priorBytes = System.Text.Encoding.UTF8.GetBytes(original);
        Assert.NotEqual((byte)'\n', priorBytes[^1]);
        Assert.NotEqual((byte)'\r', priorBytes[^1]);

        var output = new StringWriter();
        Assert.Equal(0, RunOverrideCli(repos.Reader, output));
        using var result = JsonDocument.Parse(output.ToString());
        var root = result.RootElement;
        Assert.Equal("released", root.GetProperty("status").GetString());
        var transactionCommit = root.GetProperty("commit").GetString()!;
        var waiver = root.GetProperty("loop_evidence_override");
        var finalLog = Git(repos.Bare, "show", $"{transactionCommit}:{runsRelative}");
        Assert.StartsWith(System.Text.Encoding.UTF8.GetString(priorBytes) + "\n", finalLog, StringComparison.Ordinal);
        Assert.Single(RunLogSerializer.DeserializeAll(finalLog), item => item.Event == "loop-evidence-override");
        Assert.Equal(runsRelative, waiver.GetProperty("run_log_path").GetString());
    }

    [Fact]
    public void OverrideBuilderAliasRequiresExactStoredHolder_G860()
    {
        using (var matching = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(matching, packetYaml: null,
                missingDutyIds: ["knowledge-architect"], claimActor: "builder");
            var output = new StringWriter();
            Assert.Equal(0, RunOverrideCli(matching.Reader, output, actor: "builder"));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("released", result.RootElement.GetProperty("status").GetString());
            var auditPath = result.RootElement.GetProperty("loop_evidence_override").GetProperty("audit_path").GetString()!;
            using var audit = JsonDocument.Parse(Git(matching.Bare,
                "show", $"refs/heads/main:{auditPath}"));
            Assert.Equal("builder", audit.RootElement.GetProperty("actor").GetString());
            Assert.Equal("builder", audit.RootElement.GetProperty("normalized_role").GetString());
        }

        using (var mismatched = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(mismatched, packetYaml: null,
                missingDutyIds: ["knowledge-architect"], claimActor: "implementation");
            var before = Git(mismatched.Bare, "rev-parse", "refs/heads/main").Trim();
            var output = new StringWriter();
            Assert.Equal(1, RunOverrideCli(mismatched.Reader, output, actor: "builder"));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("held", result.RootElement.GetProperty("status").GetString());
            Assert.Equal("implementation", result.RootElement.GetProperty("holder").GetString());
            Assert.Equal(before, Git(mismatched.Bare, "rev-parse", "refs/heads/main").Trim());
            Assert.Empty(ClaimHistory(mismatched.CloneForInspection()));
            Assert.DoesNotContain(Git(mismatched.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void OverridePreviewMarkdownAndLegacyReleaseOutputAreTruthful_G860()
    {
        using (var eligible = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(eligible, "knowledge-architect");
            var output = new StringWriter();
            var exit = ClaimCommand.ExecuteRelease(Context(eligible.Reader),
                ["--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
                 "--reason", "markdown preview", "--override-loop-evidence", "--format", "markdown"], output);
            Assert.Equal(0, exit);
            var markdown = output.ToString();
            Assert.Contains("# Claim planned", markdown, StringComparison.Ordinal);
            Assert.Contains("## Solo-conductor completion", markdown, StringComparison.Ordinal);
            Assert.Contains("\"decision\": \"refused\"", markdown, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"missing\"", markdown, StringComparison.Ordinal);
            Assert.Contains("## Loop-evidence override", markdown, StringComparison.Ordinal);
            Assert.Contains("\"disposition\": \"eligible-preview\"", markdown, StringComparison.Ordinal);
            Assert.Contains("\"published\": false", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("\"head_sha\"", markdown, StringComparison.Ordinal);
        }

        using (var ordinary = new ClaimRepositories())
        {
            ordinary.PublishSnapshot(queueState: QueueItemState.Completed,
                runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
            Acquire(ordinary.Writer);
            var output = new StringWriter();
            var exit = ClaimCommand.ExecuteRelease(Context(ordinary.Reader),
                ["--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
                 "--reason", "ordinary release", "--format", "json", "--write"], output);
            Assert.Equal(0, exit);
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("released", result.RootElement.GetProperty("status").GetString());
            Assert.DoesNotContain("loop_evidence_override", result.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.DoesNotContain(Git(ordinary.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PrTransitionRecognizesApprovedOnlyOverrideAndRejectsOtherTransitions_G860()
    {
        var output = new StringWriter();
        var exit = AutomationPrTransitionCommand.Execute(Context(Path.GetTempPath()),
            ["--repo", Repo, "--pr", PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
             "--transition", "request-update", "--override-loop-evidence", "--format", "json"], output);
        Assert.Equal(1, exit);
        Assert.Contains("--override-loop-evidence is supported only with --transition approved", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("usage:", output.ToString(), StringComparison.OrdinalIgnoreCase);

        var reviewStartOutput = new StringWriter();
        var reviewStartExit = AutomationPrTransitionCommand.Execute(Context(Path.GetTempPath()),
            ["--repo", Repo, "--pr", PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
             "--transition", "review-start", "--override-loop-evidence", "--format", "json"], reviewStartOutput);
        Assert.Equal(1, reviewStartExit);
        Assert.Contains("--override-loop-evidence is supported only with --transition approved", reviewStartOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OverrideRetryExhaustionPreservesCanonicalClaimAndRunLog_G860()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var headBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var runBefore = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
        var racer = repos.CreateRacer();
        var bin = Path.Combine(Path.GetDirectoryName(repos.Reader)!, "g860-retry-exhaustion-bin");
        Directory.CreateDirectory(bin);
        var countPath = Path.Combine(bin, "race-count.txt");
        var wrapper = Path.Combine(bin, "git");
        var realGit = FindGitExecutable();
        File.WriteAllText(wrapper,
            "#!/bin/sh\nset -eu\nPWD=$(pwd)\n"
            + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(repos.Bare)}\nRACER={ShellQuote(racer)}\nCOUNT={ShellQuote(countPath)}\n"
            + "case \"$PWD\" in */intent-cli-claim-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
            + "origin=$(\"$REAL\" -C \"$PWD\" remote get-url origin)\n"
            + "[ \"$origin\" = \"$BARE\" ] || exec \"$REAL\" \"$@\"\n"
            + "if [ \"$1\" = push ] && [ \"$2\" = origin ]; then\n"
            + "  n=0; [ ! -f \"$COUNT\" ] || n=$(cat \"$COUNT\"); n=$((n + 1)); printf '%s\\n' \"$n\" > \"$COUNT\"\n"
            + "  \"$REAL\" -C \"$RACER\" pull --ff-only origin main >/dev/null\n"
            + "  printf 'advance %s\\n' \"$n\" > \"$RACER/g860-retry-advance-$n.txt\"\n"
            + "  \"$REAL\" -C \"$RACER\" add -- \"g860-retry-advance-$n.txt\"\n"
            + "  \"$REAL\" -C \"$RACER\" -c user.name=race -c user.email=race@example.invalid commit --quiet -m \"advance retry $n\"\n"
            + "  \"$REAL\" -C \"$RACER\" push origin HEAD:refs/heads/main\nfi\n"
            + "exec \"$REAL\" \"$@\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var previousPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
            var output = new StringWriter();
            Assert.Equal(1, RunOverrideCli(repos.Reader, output));
            Assert.Equal("2", File.ReadAllText(countPath).Trim());
            using var result = JsonDocument.Parse(output.ToString());
            var root = result.RootElement;
            Assert.Equal("retry-exhausted", root.GetProperty("status").GetString());
            Assert.Equal(2, root.GetProperty("attempts").GetInt32());
            Assert.False(root.GetProperty("push_succeeded").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("git_push_error").GetString()));
            Assert.NotEqual(headBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
            Assert.Equal(runBefore, Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}"));
            Assert.Empty(ClaimHistory(repos.CloneForInspection()));
            Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }
    }

    [Theory]
    [InlineData("closeout")]
    [InlineData("mode")]
    [InlineData("holder")]
    [InlineData("receipt-unavailable")]
    public void OverrideRetryRefusesChangedCloseoutModeHolderOrReceipt_G860(string changedState)
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var racer = repos.CreateRacer();
        var changedPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        switch (changedState)
        {
            case "closeout":
            {
                var runPath = Path.Combine(racer, runsRelative.Replace('/', Path.DirectorySeparatorChar));
                var existing = RunLogSerializer.DeserializeAll(File.ReadAllText(runPath));
                Assert.Contains(existing, item => item.Event == "pr-merged" && item.ExecutionUnit == Unit);
                Assert.Contains(existing, item => item.Event == "closeout-recorded" && item.ExecutionUnit == Unit);
                File.WriteAllText(runPath, string.Join(Environment.NewLine,
                    existing.Where(item => !(item.Event == "pr-merged" && item.ExecutionUnit == Unit))
                        .Select(RunLogSerializer.SerializeLine)) + Environment.NewLine);
                var changed = RunLogSerializer.DeserializeAll(File.ReadAllText(runPath));
                Assert.DoesNotContain(changed, item => item.Event == "pr-merged" && item.ExecutionUnit == Unit);
                Assert.Contains(changed, item => item.Event == "closeout-recorded" && item.ExecutionUnit == Unit);
                break;
            }
            case "mode":
            {
                var modeState = TeamModeStore.TryRead(racer)!;
                var entry = Assert.Single(modeState.Entries, item => item.Domain == Domain && item.Team == Team);
                Assert.Equal(TeamMode.SoloConductor, entry.Mode);
                var transitionAt = DateTimeOffset.UtcNow.AddSeconds(1);
                var changedEntry = entry with
                {
                    Mode = TeamMode.Delivery,
                    UpdatedAt = transitionAt,
                    Transitions = entry.Transitions.Append(new TeamModeTransition
                    {
                        From = TeamMode.SoloConductor, To = TeamMode.Delivery, At = transitionAt,
                    }).ToArray(),
                };
                TeamModeStore.Write(racer, modeState with
                {
                    Entries = modeState.Entries.Select(item => item == entry ? changedEntry : item).ToArray(),
                });
                Assert.Equal(TeamMode.Delivery, TeamModeStore.TryRead(racer)!.Entries.Single(item => item.Domain == Domain && item.Team == Team).Mode);
                break;
            }
            case "holder":
            {
                var claimFile = Path.Combine(racer, changedPath.Replace('/', Path.DirectorySeparatorChar));
                var claim = JsonNode.Parse(File.ReadAllText(claimFile))!.AsObject();
                Assert.Equal("implementation", claim["actor"]!.GetValue<string>());
                claim["actor"] = "different-builder";
                File.WriteAllText(claimFile, claim.ToJsonString() + Environment.NewLine);
                Assert.Equal("different-builder", JsonNode.Parse(File.ReadAllText(claimFile))!["actor"]!.GetValue<string>());
                break;
            }
            case "receipt-unavailable":
            {
                var presentReceipt = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                    KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "orchestrator");
                var receiptPath = Path.Combine(racer, presentReceipt.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(receiptPath));
                var originalReceipt = File.ReadAllText(receiptPath);
                Assert.Contains("\"role\": \"orchestrator\"", originalReceipt, StringComparison.Ordinal);
                File.WriteAllText(receiptPath, "{ malformed raced receipt\n");
                Assert.NotEqual(originalReceipt, File.ReadAllText(receiptPath));
                break;
            }
        }
        Git(racer, "add", "-A", "--", ".intent-cli");
        Git(racer, "-c", "user.name=race", "-c", "user.email=race@example.invalid",
            "commit", "--quiet", "-m", $"change canonical {changedState} before override retry");
        var racerHead = Git(racer, "rev-parse", "HEAD").Trim();
        var marker = InstallRacerAdvanceOnFirstPush(repos, racer, $"g860-{changedState}-race");
        var previousPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(marker) + Path.PathSeparator + previousPath);
            var output = new StringWriter();
            Assert.Equal(1, RunOverrideCli(repos.Reader, output));
            Assert.True(File.Exists(marker));
            using var result = JsonDocument.Parse(output.ToString());
            var root = result.RootElement;
            Assert.NotEqual("released", root.GetProperty("status").GetString());
            Assert.Equal(changedState == "holder" ? 1 : 2, root.GetProperty("attempts").GetInt32());
            Assert.Equal(racerHead, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            if (changedState == "closeout")
            {
                Assert.Equal("completion-blocked", root.GetProperty("status").GetString());
                Assert.Equal("missing", Assert.Single(root.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray(),
                    item => item.GetProperty("id").GetString() == "pr-merged").GetProperty("state").GetString());
            }
            if (changedState == "mode") Assert.Equal("override-not-applicable", root.GetProperty("status").GetString());
            if (changedState == "holder")
            {
                Assert.Equal("held", root.GetProperty("status").GetString());
                Assert.Equal("different-builder", root.GetProperty("holder").GetString());
                var overrideResult = root.GetProperty("loop_evidence_override");
                Assert.True(overrideResult.GetProperty("requested").GetBoolean());
                Assert.Equal("refused", overrideResult.GetProperty("disposition").GetString());
                Assert.Equal("holder-identity-mismatch", overrideResult.GetProperty("cause").GetString());
                Assert.False(overrideResult.GetProperty("published").GetBoolean());
                Assert.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/",
                    overrideResult.GetProperty("audit_path").GetString(), StringComparison.Ordinal);
                Assert.StartsWith(".intent-cli/claims/history/",
                    overrideResult.GetProperty("history_path").GetString(), StringComparison.Ordinal);
                Assert.Equal(runsRelative, overrideResult.GetProperty("run_log_path").GetString());
            }
            if (changedState == "receipt-unavailable")
            {
                Assert.Equal("completion-blocked", root.GetProperty("status").GetString());
                Assert.Equal("knowledge-record-unavailable", Assert.Single(
                    root.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray(),
                    item => item.GetProperty("id").GetString() == "knowledge-orchestrator").GetProperty("cause").GetString());
                Assert.Equal("refused", root.GetProperty("loop_evidence_override").GetProperty("disposition").GetString());
                Assert.False(root.GetProperty("loop_evidence_override").GetProperty("published").GetBoolean());
            }
            Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
            Assert.Empty(ClaimHistory(repos.CloneForInspection()));
            var finalRunsPath = Path.Combine(repos.CloneForInspection(), runsRelative.Replace('/', Path.DirectorySeparatorChar));
            Assert.DoesNotContain(RunLogSerializer.DeserializeAll(File.ReadAllText(finalRunsPath)),
                item => item.Event == "loop-evidence-override");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }
    }

    [Theory]
    [InlineData("queue-absent", "closeout-queue", "missing")]
    [InlineData("queue-incomplete", "closeout-queue", "missing")]
    [InlineData("pr-merged", "pr-merged", "missing")]
    [InlineData("closeout-recorded", "closeout-recorded", "missing")]
    [InlineData("knowledge-architect", "knowledge-architect", "missing")]
    [InlineData("knowledge-orchestrator", "knowledge-orchestrator", "missing")]
    [InlineData("guide-architect", "guide-reachability", "missing")]
    public void EachMissingRequiredDutyBlocksCanonicalRelease_G857(string omitted, string dutyId, string expectedState)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(
            queueState: omitted == "queue-absent" ? null
                : omitted == "queue-incomplete" ? QueueItemState.Active : QueueItemState.Completed,
            runEvents: omitted switch
            {
                "pr-merged" => ["closeout-recorded"],
                "closeout-recorded" => ["pr-merged"],
                _ => ["pr-merged", "closeout-recorded"],
            });
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        if (omitted != "knowledge-architect") RecordKnowledge(repos.Writer, "architect", hostCommit);
        if (omitted != "knowledge-orchestrator") RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        if (omitted != "guide-architect") RecordGuide(repos.Writer, "architect", hostCommit);
        PublishExactReceipts(repos.Writer, "main");

        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var historyBefore = ClaimHistory(repos.CloneForInspection()).ToArray();
        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Equal("implementation", result.Holder);
        Assert.Equal(Team, result.HolderTeam);
        Assert.Equal("refused", result.SoloConductorCompletion?.Decision);
        Assert.Equal(expectedState, Duty(result, dutyId).State);
        if (dutyId == "closeout-queue")
        {
            var duty = Duty(result, dutyId);
            var command = Assert.Single(duty.RecoveryCommands);
            Assert.Contains("intent-cli closeout pr --pr", command, StringComparison.Ordinal);
            Assert.Contains($"--repo {Repo}", command, StringComparison.Ordinal);
            Assert.Contains($"--domain {Domain}", command, StringComparison.Ordinal);
            Assert.Contains("--pr-merged true", command, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.PublicationStep!, StringComparison.Ordinal);
            if (omitted == "queue-absent")
                Assert.Contains("<actual-merged-pr>", command, StringComparison.Ordinal);
            else
                Assert.Contains($"--pr {PullRequest}", command, StringComparison.Ordinal);
            Assert.DoesNotContain("--repair-runs", command, StringComparison.Ordinal);
            Assert.Null(duty.RepairUnavailableReason);
        }
        else if (dutyId is "pr-merged" or "closeout-recorded")
        {
            var duty = Duty(result, dutyId);
            var command = Assert.Single(duty.RecoveryCommands);
            Assert.Contains("intent-cli closeout pr --pr 1873", command, StringComparison.Ordinal);
            Assert.Contains($"--repo {Repo}", command, StringComparison.Ordinal);
            Assert.Contains($"--domain {Domain}", command, StringComparison.Ordinal);
            Assert.Contains("--repair-runs", command, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.PublicationStep!, StringComparison.Ordinal);
        }
        foreach (var otherDuty in result.SoloConductorCompletion!.Duties.Where(duty => duty.Id != dutyId))
        {
            var expectedOther = omitted == "queue-absent" && otherDuty.Id is "pr-merged" or "closeout-recorded"
                ? "unavailable"
                : "satisfied";
            Assert.Equal(expectedOther, otherDuty.State);
        }
        var inspection = repos.CloneForInspection();
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(historyBefore, ClaimHistory(inspection));
    }

    [Fact]
    public void CandidateDryRunReadsFreshCanonicalSnapshotWithoutChangingInvokingCheckout_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        var invokingHead = Git(repos.Reader, "rev-parse", "HEAD").Trim();
        var canonicalHead = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        Assert.NotEqual(invokingHead, canonicalHead);

        var planned = ClaimCommand.RunTransaction(repos.Reader, Request(ClaimOperation.Release, write: false));

        Assert.Equal("completion-blocked", planned.Status);
        Assert.False(planned.PushSucceeded);
        Assert.Equal(canonicalHead, planned.SoloConductorCompletion?.CanonicalSnapshotOid);
        Assert.Equal("refs/heads/main", planned.SoloConductorCompletion?.CanonicalTargetRef);
        Assert.Equal("missing", Duty(planned, "closeout-queue").State);
        Assert.Equal(invokingHead, Git(repos.Reader, "rev-parse", "HEAD").Trim());
        Assert.Equal(canonicalHead, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        var inspection = repos.CloneForInspection();
        Assert.Empty(ClaimHistory(inspection));
    }

    [Fact]
    public void SatisfiedCandidateDryRunPreservesEntireCallerAndCanonicalState_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "design", hostCommit);
        RecordKnowledge(repos.Writer, "orchestration", hostCommit);
        RecordGuide(repos.Writer, "design", hostCommit);
        PublishExactReceipts(repos.Writer, "main");
        PullCanonicalHead(repos.Reader);

        var staleRoot = PreparePreviewCallerState(repos.Reader);
        var hookCount = repos.InstallRejectingPreReceiveHook();
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var before = CaptureTree(repos.Reader);
        var cleaned = new List<string>();
        using var warnings = new StringWriter();
        try
        {
            var result = ClaimCommand.RunTransaction(
                repos.Reader,
                Request(ClaimOperation.Release, write: false),
                warnings,
                path =>
                {
                    cleaned.Add(path);
                    Directory.Delete(path, recursive: true);
                });

            Assert.Equal("planned", result.Status);
            Assert.False(result.PushSucceeded);
            Assert.Null(result.Commit);
            Assert.Null(result.HistoryPath);
            Assert.Equal("satisfied", result.SoloConductorCompletion?.Decision);
            Assert.Equal(canonicalBefore, result.SoloConductorCompletion?.CanonicalSnapshotOid);
            Assert.Single(cleaned);
            Assert.False(Directory.Exists(cleaned[0]));
            Assert.False(File.Exists(cleaned[0] + ".lease"));
            Assert.True(Directory.Exists(staleRoot));
            Assert.False(File.Exists(hookCount));
            Assert.Equal(before.ToArray(), CaptureTree(repos.Reader).ToArray());
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        }
        finally
        {
            if (Directory.Exists(staleRoot)) Directory.Delete(staleRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-duty")]
    [InlineData("malformed-mode")]
    [InlineData("invalid-record-path")]
    public void RefusedCandidateDryRunPreservesEntireCallerAndCanonicalState_G857(string cause)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        PublishExactReceipts(repos.Writer, "main");
        PullCanonicalHead(repos.Reader);

        switch (cause)
        {
            case "missing-duty":
                var guidePath = Path.Combine(repos.Writer, RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                    GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect"));
                File.Delete(guidePath);
                break;
            case "malformed-mode":
                File.WriteAllText(TeamModeStore.ResolvePath(repos.Writer), "{ not a canonical mode document\n");
                break;
            case "invalid-record-path":
                var architectPath = Path.Combine(repos.Writer, RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                    KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect"));
                File.Delete(architectPath);
                Directory.CreateDirectory(architectPath);
                File.WriteAllText(Path.Combine(architectPath, "marker.txt"), "a record path cannot be a directory");
                break;
        }
        repos.PublishIntentChanges(repos.Writer, "main");

        var staleRoot = PreparePreviewCallerState(repos.Reader);
        var hookCount = repos.InstallRejectingPreReceiveHook();
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var before = CaptureTree(repos.Reader);
        var cleaned = new List<string>();
        using var warnings = new StringWriter();
        try
        {
            var result = ClaimCommand.RunTransaction(
                repos.Reader,
                Request(ClaimOperation.Release, write: false),
                warnings,
                path =>
                {
                    cleaned.Add(path);
                    Directory.Delete(path, recursive: true);
                });

            Assert.Equal("completion-blocked", result.Status);
            Assert.False(result.PushSucceeded);
            Assert.Null(result.Commit);
            Assert.Null(result.HistoryPath);
            Assert.Equal(canonicalBefore, result.SoloConductorCompletion?.CanonicalSnapshotOid);
            if (cause == "missing-duty") Assert.Equal("missing", Duty(result, "guide-reachability").State);
            if (cause == "malformed-mode")
            {
                Assert.Equal("unavailable", result.SoloConductorCompletion?.Applicability.State);
                Assert.Equal("team-mode-unavailable", Duty(result, "applicability").Cause);
            }
            if (cause == "invalid-record-path") Assert.Equal("unavailable", Duty(result, "knowledge-architect").State);
            Assert.Single(cleaned);
            Assert.False(Directory.Exists(cleaned[0]));
            Assert.False(File.Exists(cleaned[0] + ".lease"));
            Assert.True(Directory.Exists(staleRoot));
            Assert.False(File.Exists(hookCount));
            Assert.Equal(before.ToArray(), CaptureTree(repos.Reader).ToArray());
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());

            var inspection = repos.CloneForInspection();
            Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
            Assert.Empty(ClaimHistory(inspection));
        }
        finally
        {
            if (Directory.Exists(staleRoot)) Directory.Delete(staleRoot, recursive: true);
        }
    }

    [Fact]
    public void LocalReceiptsCannotSatisfyCanonicalSnapshotGate_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Reader);
        var localCommit = Git(repos.Reader, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Reader, "design", localCommit);
        RecordKnowledge(repos.Reader, "orchestration", localCommit);
        RecordGuide(repos.Reader, "design", localCommit);

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("missing", Duty(result, "knowledge-architect").State);
        Assert.Equal("missing", Duty(result, "knowledge-orchestrator").State);
        Assert.Equal("missing", Duty(result, "guide-reachability").State);
        Assert.Equal("refs/heads/main", result.SoloConductorCompletion?.CanonicalTargetRef);
        Assert.Equal(Git(repos.Bare, "rev-parse", "refs/heads/main").Trim(), result.SoloConductorCompletion?.CanonicalSnapshotOid);
        Assert.Equal("implementation", result.Holder);
        Assert.Equal(Team, result.HolderTeam);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(repos.Reader, ".intent-cli", "knowledge-writebacks", Unit, "records")));
    }

    [Fact]
    public void ConfiguredMetadataWriteBranchIsCanonicalForGateAndRelease_G857()
    {
        using var repos = new ClaimRepositories();
        const string branch = "intent-metadata";
        repos.ConfigureMetadataWriteBranch(branch);
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer, branch);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "design", hostCommit);
        RecordKnowledge(repos.Writer, "orchestration", hostCommit);
        RecordGuide(repos.Writer, "design", hostCommit);

        var metadataBefore = Git(repos.Bare, "rev-parse", $"refs/heads/{branch}").Trim();
        var blockedBeforePublication = Release(repos.Reader);
        Assert.Equal("completion-blocked", blockedBeforePublication.Status);
        Assert.Equal($"refs/heads/{branch}", blockedBeforePublication.SoloConductorCompletion?.CanonicalTargetRef);
        foreach (var id in new[] { "knowledge-architect", "knowledge-orchestrator", "guide-reachability" })
        {
            var duty = Duty(blockedBeforePublication, id);
            Assert.Equal("missing", duty.State);
            Assert.Contains($"refs/heads/{branch}", duty.PublicationStep!, StringComparison.Ordinal);
        }
        Assert.Equal(metadataBefore, Git(repos.Bare, "rev-parse", $"refs/heads/{branch}").Trim());
        Assert.True(File.Exists(Path.Combine(repos.CloneBranchForInspection(branch), ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));

        // Publish the writer-created artifacts to product/main only. The
        // configured metadata ref remains the sole evidence authority.
        var productCheckout = repos.CreateRacer();
        var receiptPaths = ReceiptPaths();
        foreach (var path in receiptPaths)
        {
            var source = Path.Combine(repos.Writer, path.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(productCheckout, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
        Git(productCheckout, ["add", "--", .. receiptPaths]);
        Git(productCheckout, "-c", "user.name=test", "-c", "user.email=test@example.invalid",
            "commit", "--quiet", "-m", "publish receipts to product branch only");
        Git(productCheckout, "push", "origin", "HEAD:refs/heads/main");
        var productAfterReceiptOnly = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();

        var blockedOnProductOnly = Release(repos.Reader);
        Assert.Equal("completion-blocked", blockedOnProductOnly.Status);
        Assert.Equal($"refs/heads/{branch}", blockedOnProductOnly.SoloConductorCompletion?.CanonicalTargetRef);
        Assert.All(new[] { "knowledge-architect", "knowledge-orchestrator", "guide-reachability" }, id =>
            Assert.Equal("missing", Duty(blockedOnProductOnly, id).State));
        Assert.Equal(metadataBefore, Git(repos.Bare, "rev-parse", $"refs/heads/{branch}").Trim());

        PublishExactReceipts(repos.Writer, branch);

        var released = Release(repos.Reader);

        Assert.Equal("released", released.Status);
        Assert.Equal($"refs/heads/{branch}", released.TargetRef);
        Assert.Equal($"refs/heads/{branch}", released.SoloConductorCompletion?.CanonicalTargetRef);
        Assert.Equal(Git(repos.Bare, "rev-parse", $"refs/heads/{branch}").Trim(), released.Commit);
        Assert.Equal(productAfterReceiptOnly, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
    }

    [Fact]
    public void RetryReevaluatesEvidenceAfterRemoteAdvance_G857()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "design", hostCommit);
        RecordKnowledge(repos.Writer, "orchestration", hostCommit);
        RecordGuide(repos.Writer, "design", hostCommit);
        PublishExactReceipts(repos.Writer, "main");
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var racer = repos.CreateRacer();
        var guidePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect");
        var previousPath = Environment.GetEnvironmentVariable("PATH");
        var bin = Path.Combine(Path.GetDirectoryName(racer)!, "race-bin");
        Directory.CreateDirectory(bin);
        var marker = Path.Combine(bin, "injected");
        var wrapper = Path.Combine(bin, "git");
        var realGit = FindGitExecutable();
        File.WriteAllText(wrapper,
            "#!/bin/sh\nset -eu\n"
            + $"if [ \"$1\" = push ] && [ \"$2\" = origin ] && [ ! -e {ShellQuote(marker)} ]; then\n"
            + $"  : > {ShellQuote(marker)}\n"
            + $"  {ShellQuote(realGit)} -C {ShellQuote(racer)} rm -- {ShellQuote(guidePath)}\n"
            + $"  {ShellQuote(realGit)} -C {ShellQuote(racer)} -c user.name=race -c user.email=race@example.invalid commit --quiet -m 'remove canonical guide receipt'\n"
            + $"  {ShellQuote(realGit)} -C {ShellQuote(racer)} push origin HEAD:refs/heads/main\n"
            + "fi\n"
            + $"exec {ShellQuote(realGit)} \"$@\"\n");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
            var result = Release(repos.Reader);
            Assert.Equal("completion-blocked", result.Status);
            Assert.Equal(2, result.Attempts);
            Assert.Equal("missing", Duty(result, "guide-reachability").State);
            Assert.True(File.Exists(marker));
            Assert.Equal("implementation", result.Holder);
            Assert.Equal(Team, result.HolderTeam);
            var advancedHead = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
            Assert.Equal(advancedHead, result.SoloConductorCompletion?.CanonicalSnapshotOid);
            Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
            Assert.True(File.Exists(Path.Combine(repos.CloneForInspection(), ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
            Assert.Empty(ClaimHistory(repos.CloneForInspection()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }
    }

    [Fact]
    public void ExplicitFalseKnowledgeAndNoSurfaceGuideAreValidNotApplicable_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("released", result.Status);
        Assert.Equal("not-applicable", Duty(result, "knowledge-writeback").State);
        Assert.Equal("not-applicable", Duty(result, "guide-reachability").State);
        Assert.Equal("satisfied", Duty(result, "closeout-queue").State);
    }

    [Fact]
    public void CloseoutLearningOnlyFalseKnowledgeDeclarationIsExplicit_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: CloseoutLearningFalsePacketYaml);
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("released", result.Status);
        Assert.Equal("satisfied", result.SoloConductorCompletion?.Decision);
        Assert.Equal("not-applicable", Duty(result, "knowledge-writeback").State);
        Assert.Equal("not-applicable", Duty(result, "guide-reachability").State);
    }

    [Theory]
    [InlineData("empty-knowledge-updates")]
    [InlineData("empty-closeout-learning")]
    [InlineData("empty-facet")]
    public void EmptyEnclosingKnowledgeMappingsAreNotExplicitNoOpDeclarations_G857(string kind)
    {
        using var repos = new ClaimRepositories();
        var packet = kind switch
        {
            "empty-knowledge-updates" => PacketWithKnowledgeFragment("knowledge_updates: {}"),
            "empty-closeout-learning" => PacketWithKnowledgeFragment("closeout_learning: {}"),
            _ => PacketWithKnowledgeFragment("knowledge_updates:\n  intent_tree: {}"),
        };
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: packet);

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(result.IsApplicable);
        Assert.Equal("refused", result.Completion?.Decision);
        Assert.Equal("missing", Duty(result.Completion!, "knowledge-declaration").State);
    }

    [Fact]
    public void AbsentGuideDeclarationIsMissingForSoloRelease_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: PacketWithoutGuideDeclarationYaml);

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(result.IsApplicable);
        Assert.Equal("refused", result.Completion?.Decision);
        Assert.Equal("not-applicable", Duty(result.Completion!, "knowledge-writeback").State);
        Assert.Equal("missing", Duty(result.Completion!, "guide-declaration").State);
    }

    [Fact]
    public void MalformedGuideDeclarationIsUnavailableForSoloRelease_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: PacketWithMalformedGuideDeclarationYaml);

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(result.IsApplicable);
        Assert.Equal("refused", result.Completion?.Decision);
        Assert.Equal("not-applicable", Duty(result.Completion!, "knowledge-writeback").State);
        Assert.Equal("unavailable", Duty(result.Completion!, "guide-declaration").State);
        Assert.Equal("guide-declaration-unavailable", Duty(result.Completion!, "guide-declaration").Cause);
    }

    [Fact]
    public void MalformedGuideRoutesUnderValidDeclarationAreUnavailable_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: PacketWithMalformedGuideRoutesYaml);

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(result.IsApplicable);
        Assert.Equal("refused", result.Completion?.Decision);
        Assert.Equal("not-applicable", Duty(result.Completion!, "knowledge-writeback").State);
        Assert.Equal("unavailable", Duty(result.Completion!, "guide-declaration").State);
        Assert.Equal("guide-declaration-unavailable", Duty(result.Completion!, "guide-declaration").Cause);
    }

    [Fact]
    public void DuplicateArchitectGuideRecordsAtDifferentPathsAreUnavailable_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        var architectPath = Path.Combine(repos.Writer,
            RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect"));
        var duplicatePath = Path.Combine(Path.GetDirectoryName(architectPath)!, "architect-duplicate.json");
        File.Copy(architectPath, duplicatePath);
        repos.PublishIntentChanges(repos.Writer, "main");

        var result = EvaluateCanonicalSnapshot(repos.Writer);
        var duty = Duty(result.Completion!, "guide-reachability");

        Assert.True(result.IsApplicable);
        Assert.Equal("refused", result.Completion?.Decision);
        Assert.Equal("unavailable", duty.State);
        Assert.Equal("duplicate-role-record", duty.Cause);
        Assert.NotNull(duty.RepairUnavailableReason);
        Assert.Equal(2, duty.Evidence.Count(evidence => evidence.Role == "architect"));
    }

    [Fact]
    public void AbsentKnowledgeDeclarationIsNotTreatedAsNoOp_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: PacketWithoutKnowledgeDeclarationYaml);
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("missing", Duty(result, "knowledge-declaration").State);
        Assert.Equal("not-applicable", Duty(result, "guide-reachability").State);
    }

    [Fact]
    public void MalformedKnowledgeDeclarationIsUnavailable_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: PacketWithMalformedKnowledgeDeclarationYaml);
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("unavailable", Duty(result, "knowledge-declaration").State);
        Assert.Equal("not-applicable", Duty(result, "guide-reachability").State);
    }

    [Theory]
    [InlineData("knowledge-required-array", "knowledge-declaration")]
    [InlineData("knowledge-required-map", "knowledge-declaration")]
    [InlineData("knowledge-required-null", "knowledge-declaration")]
    [InlineData("knowledge-required-empty", "knowledge-declaration")]
    [InlineData("knowledge-required-invalid", "knowledge-declaration")]
    [InlineData("closeout-required-array", "knowledge-declaration")]
    [InlineData("closeout-required-map", "knowledge-declaration")]
    [InlineData("closeout-required-null", "knowledge-declaration")]
    [InlineData("closeout-required-empty", "knowledge-declaration")]
    [InlineData("closeout-required-invalid", "knowledge-declaration")]
    [InlineData("knowledge-updates-array-with-false", "knowledge-declaration")]
    [InlineData("knowledge-updates-null-with-false", "knowledge-declaration")]
    [InlineData("closeout-learning-array-with-false", "knowledge-declaration")]
    [InlineData("closeout-learning-null-with-false", "knowledge-declaration")]
    [InlineData("facet-array-with-false", "knowledge-declaration")]
    [InlineData("facet-null-with-false", "knowledge-declaration")]
    [InlineData("malformed-required-with-valid-true-facet", "knowledge-declaration")]
    public void PresentMalformedKnowledgeNodeShapesRefuseCanonicalRelease_G857(string shape, string dutyId)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: PacketWithMalformedDeclarationShape(shape));

        AssertBlockedReleasePreservesCanonicalClaim(repos, dutyId, "knowledge-declaration-unavailable");
    }

    [Theory]
    [InlineData("guide-boolean-array")]
    [InlineData("guide-boolean-map")]
    [InlineData("guide-boolean-null")]
    [InlineData("guide-boolean-empty")]
    [InlineData("guide-boolean-invalid")]
    [InlineData("guide-hidden-no-surface-array")]
    [InlineData("guide-hidden-required-map")]
    [InlineData("guide-hidden-route-alias-map")]
    public void PresentMalformedGuideNodeShapesRefuseCanonicalRelease_G857(string shape)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: PacketWithMalformedGuideShape(shape));

        AssertBlockedReleasePreservesCanonicalClaim(repos, "guide-declaration", "guide-declaration-unavailable");
    }

    [Fact]
    public void PreviewRouteSequenceDeclarationRemainsAccepted_G857()
    {
        using var repos = new ClaimRepositories();
        var packet = ReplaceGuideTail(
            "  - guide_surface: guide workflow task implementation-loop\n    role: implementation\n    target_surface: claim release completion");
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: packet);

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(result.IsApplicable);
        Assert.Equal("missing", Duty(result.Completion!, "guide-reachability").State);
        Assert.DoesNotContain(result.Completion!.Duties, duty => duty.Id == "guide-declaration");
    }

    [Fact]
    public void DuplicateCanonicalKnowledgeAttributionIsUnavailable_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        var architectPath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect");
        var legacyPath = RoleScopedCloseoutRecordStore.ResolveLegacyRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit);
        File.Copy(Path.Combine(repos.Writer, architectPath.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(repos.Writer, legacyPath.Replace('/', Path.DirectorySeparatorChar)));
        PublishExactReceipts(repos.Writer, "main");
        repos.PublishIntentChanges(repos.Writer, "main");

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("unavailable", Duty(result, "knowledge-architect").State);
        Assert.Equal("duplicate-role-record", Duty(result, "knowledge-architect").Cause);
        Assert.Equal("satisfied", Duty(result, "knowledge-orchestrator").State);
        Assert.Equal("satisfied", Duty(result, "guide-reachability").State);
    }

    [Fact]
    public void LegacyNullKnowledgeAttributionDoesNotSatisfyRoleDuty_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        var architectPath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect");
        var legacyPath = RoleScopedCloseoutRecordStore.ResolveLegacyRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit);
        var canonicalRecordPath = Path.Combine(repos.Writer, architectPath.Replace('/', Path.DirectorySeparatorChar));
        var legacyRecordPath = Path.Combine(repos.Writer, legacyPath.Replace('/', Path.DirectorySeparatorChar));
        var legacyRecord = JsonNode.Parse(File.ReadAllText(canonicalRecordPath))!;
        legacyRecord["role"] = null;
        Directory.CreateDirectory(Path.GetDirectoryName(legacyRecordPath)!);
        File.WriteAllText(legacyRecordPath, legacyRecord.ToJsonString());
        File.Delete(canonicalRecordPath);
        PublishExactReceipts(repos.Writer, "main");
        repos.PublishIntentChanges(repos.Writer, "main");

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("missing", Duty(result, "knowledge-architect").State);
        Assert.Equal("satisfied", Duty(result, "knowledge-orchestrator").State);
        Assert.Equal("satisfied", Duty(result, "guide-reachability").State);
    }

    [Fact]
    public void GuideRecipientRolesDoNotReplaceArchitectRecorderAttribution_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "orchestration", hostCommit);
        PublishExactReceipts(repos.Writer, "main");

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("missing", Duty(result, "guide-reachability").State);
        Assert.Contains("record.roles does not substitute", Duty(result, "guide-reachability").Detail, StringComparison.Ordinal);
        Assert.Equal("satisfied", Duty(result, "knowledge-architect").State);
        Assert.Equal("satisfied", Duty(result, "knowledge-orchestrator").State);
    }

    [Fact]
    public void DuplicateMatchingCloseoutEventsRemainSatisfied_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "pr-merged", "closeout-recorded", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        PublishExactReceipts(repos.Writer, "main");

        var result = Release(repos.Reader);

        Assert.Equal("released", result.Status);
        Assert.Equal("satisfied", Duty(result, "pr-merged").State);
        Assert.Equal("satisfied", Duty(result, "closeout-recorded").State);
    }

    [Fact]
    public void HistoricalWrongPrRunEventsDoNotSatisfyCurrentQueueIdentity_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        repos.WriteRunEvents(["pr-merged", "closeout-recorded"], PullRequest + 1);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        PublishExactReceipts(repos.Writer, "main");

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("missing", Duty(result, "pr-merged").State);
        Assert.Equal("missing", Duty(result, "closeout-recorded").State);
        Assert.Equal("satisfied", Duty(result, "closeout-queue").State);
        Assert.Equal("satisfied", Duty(result, "knowledge-architect").State);
        Assert.Equal("satisfied", Duty(result, "guide-reachability").State);
    }

    [Fact]
    public void NonCandidateDryRunRetainsLegacyJsonShape_G857()
    {
        using var repos = new ClaimRepositories();
        using var writer = new StringWriter();
        var exitCode = ClaimCommand.ExecuteRelease(Context(repos.Reader),
            ["--scope", $"execution-unit:{Unit}", "--actor", "architect", "--team", Team,
             "--reason", "legacy preview", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var expected = $$"""
            {
              "status": "planned",
              "scope": "execution-unit:G857",
              "claim_path": "{{ClaimCommand.ClaimPath($"execution-unit:{Unit}")}}",
              "push_succeeded": false,
              "attempts": 0,
              "detail": "Dry-run only. Re-run with --write; ownership exists only after a successful plain push.",
              "preview_status": "preview-through-1.x"
            }
            """ + Environment.NewLine;
        Assert.Equal(expected, writer.ToString());
    }

    [Fact]
    public void NormalizedBuilderAliasStillRequiresExactHeldActorAndTeam_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);

        var result = ClaimCommand.RunTransaction(repos.Reader, Request(ClaimOperation.Release, actor: "builder"));

        Assert.Equal("held", result.Status);
        Assert.Equal("implementation", result.Holder);
        Assert.Equal(Team, result.HolderTeam);
        Assert.Null(result.SoloConductorCompletion);
        Assert.True(File.Exists(Path.Combine(repos.CloneForInspection(), ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
    }

    [Fact]
    public void BuilderActorCanReleaseItsOwnCompletedCanonicalClaim_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer, actor: "builder");
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        PublishExactReceipts(repos.Writer, "main");

        var result = ClaimCommand.RunTransaction(repos.Reader, Request(ClaimOperation.Release, actor: "builder"));

        Assert.Equal("released", result.Status);
        Assert.Equal("satisfied", result.SoloConductorCompletion?.Decision);
        Assert.Equal(Team, result.SoloConductorCompletion?.Team);
        using var history = JsonDocument.Parse(File.ReadAllText(Path.Combine(repos.CloneForInspection(), result.HistoryPath!)));
        Assert.Equal("builder", history.RootElement.GetProperty("actor").GetString());
        Assert.Equal(Team, history.RootElement.GetProperty("team").GetString());
    }

    [Theory]
    [InlineData("url")]
    [InlineData("structured")]
    [InlineData("legacy-number")]
    public void SupportedCanonicalQueuePrIdentityFormsRelease_G857(string form)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        if (form != "url")
        {
            var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
            var queueJson = File.ReadAllText(queuePath);
            var expected = $"\"linked_pr\": \"https://github.com/{Repo}/pull/{PullRequest}\"";
            var replacement = form == "structured"
                ? $"\"linked_pr\": {{\"repo\":\"{Repo}\",\"number\":{PullRequest},\"url\":\"https://github.com/{Repo}/pull/{PullRequest}\"}}"
                : $"\"linked_pr\": \"{PullRequest}\"";
            File.WriteAllText(queuePath, queueJson.Replace(expected, replacement, StringComparison.Ordinal));
            repos.PublishIntentChanges(repos.Writer, "main");
        }
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "design", hostCommit);
        RecordKnowledge(repos.Writer, "orchestration", hostCommit);
        RecordGuide(repos.Writer, "design", hostCommit);
        PublishExactReceipts(repos.Writer, "main");

        var result = Release(repos.Reader);

        Assert.Equal("released", result.Status);
        Assert.Equal(PullRequest, result.SoloConductorCompletion?.LinkedPr);
        Assert.Equal("satisfied", Duty(result, "closeout-queue").State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StructuredLinkedPrNumberMustBeJsonIntegerOnWriteAndPreview_G857(bool write)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var queueJson = File.ReadAllText(queuePath);
        var expected = $"\"linked_pr\": \"https://github.com/{Repo}/pull/{PullRequest}\"";
        Assert.Contains(expected, queueJson, StringComparison.Ordinal);
        var malformed = $"\"linked_pr\": {{\"repo\":\"{Repo}\",\"number\":\"{PullRequest}\",\"url\":\"https://github.com/{Repo}/pull/{PullRequest}\"}}";
        File.WriteAllText(queuePath, queueJson.Replace(expected, malformed, StringComparison.Ordinal));
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Reader);

        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimRelative = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var inspection = repos.CloneForInspection();
        var claimBefore = File.ReadAllBytes(Path.Combine(inspection, claimRelative));
        var historyBefore = ClaimHistory(inspection).ToArray();
        ClaimTransactionResult result;
        string? staleRoot = null;
        if (write)
        {
            result = Release(repos.Reader);
        }
        else
        {
            staleRoot = PreparePreviewCallerState(repos.Reader);
            var callerBefore = CaptureTree(repos.Reader);
            var hookCount = repos.InstallRejectingPreReceiveHook();
            var cleaned = new List<string>();
            using var warnings = new StringWriter();
            try
            {
                result = ClaimCommand.RunTransaction(
                    repos.Reader,
                    Request(ClaimOperation.Release, write: false),
                    warnings,
                    path =>
                    {
                        cleaned.Add(path);
                        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    });

                Assert.Single(cleaned);
                Assert.False(Directory.Exists(cleaned[0]));
                Assert.False(File.Exists(cleaned[0] + ".lease"));
                Assert.True(Directory.Exists(staleRoot));
                Assert.False(File.Exists(hookCount));
                Assert.Equal(callerBefore.ToArray(), CaptureTree(repos.Reader).ToArray());
            }
            finally
            {
                if (Directory.Exists(staleRoot)) Directory.Delete(staleRoot, recursive: true);
            }
        }

        var queueDuty = Duty(result, "closeout-queue");
        var canonicalAfter = repos.CloneForInspection();
        Assert.Equal("completion-blocked", result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Equal("implementation", result.Holder);
        Assert.Equal(Team, result.HolderTeam);
        Assert.Equal("refused", result.SoloConductorCompletion?.Decision);
        Assert.Equal(canonicalBefore, result.SoloConductorCompletion?.CanonicalSnapshotOid);
        Assert.Equal("refs/heads/main", result.SoloConductorCompletion?.CanonicalTargetRef);
        Assert.Equal("unavailable", queueDuty.State);
        Assert.Equal("queue-pr-identity-conflict", queueDuty.Cause);
        Assert.Empty(queueDuty.RecoveryCommands);
        Assert.NotNull(queueDuty.RepairUnavailableReason);
        Assert.Contains("host/queue-owner", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Contains("refs/heads/main", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Contains(Path.GetRelativePath(repos.Writer, queuePath).Replace(Path.DirectorySeparatorChar, '/'),
            queueDuty.Evidence.Select(evidence => evidence.Path));
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(claimBefore, File.ReadAllBytes(Path.Combine(canonicalAfter, claimRelative)));
        Assert.Equal(historyBefore, ClaimHistory(canonicalAfter));
    }

    [Fact]
    public void MissingModeDoesNotChangeLegacyClaimReleaseBehavior_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: []);
        File.Delete(TeamModeStore.ResolvePath(repos.Writer));
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("released", result.Status);
        Assert.True(result.PushSucceeded);
        Assert.Null(result.SoloConductorCompletion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordedSoloTakeoverRemainsAvailableWithMissingPacketAndCloseout_G857(bool write)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: null, runEvents: []);
        var packetPath = Path.Combine(repos.Writer, KnowledgeWriteBackRecord.PacketRootRelativePath, Unit, "packet.yaml");
        var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var runsPath = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        var packetRelative = Path.GetRelativePath(repos.Writer, packetPath);
        File.Delete(packetPath);
        if (File.Exists(queuePath)) File.Delete(queuePath);
        if (File.Exists(runsPath)) File.Delete(runsPath);
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer, actor: "design");

        var claimRelative = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var beforeRoot = repos.CloneForInspection();
        Assert.False(File.Exists(Path.Combine(beforeRoot, packetRelative)));
        Assert.False(File.Exists(Path.Combine(beforeRoot, Path.GetRelativePath(repos.Writer, queuePath))));
        Assert.False(File.Exists(Path.Combine(beforeRoot, Path.GetRelativePath(repos.Writer, runsPath))));
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimBefore = File.ReadAllBytes(Path.Combine(beforeRoot, claimRelative));
        var historyBefore = ClaimHistory(beforeRoot).OrderBy(path => path, StringComparer.Ordinal).ToArray();

        var takeover = Request(ClaimOperation.Takeover, actor: "implementation", write: write) with
        {
            Reason = "explicit reassignment fixture",
            DisplacedHolder = "design",
        };
        var result = ClaimCommand.RunTransaction(repos.Reader, takeover);

        Assert.Equal(write ? "taken-over" : "planned", result.Status);
        Assert.Equal(write, result.PushSucceeded);
        Assert.Equal("execution-unit:G857", result.Scope);
        Assert.Null(result.SoloConductorCompletion);
        Assert.DoesNotContain("solo_conductor_completion", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        if (write)
        {
            Assert.Equal("design", result.DisplacedHolder);
            Assert.NotNull(result.HistoryPath);
            var afterRoot = repos.CloneForInspection();
            using var active = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(afterRoot, claimRelative)));
            Assert.Equal("implementation", active.RootElement.GetProperty("actor").GetString());
            Assert.Equal(Team, active.RootElement.GetProperty("team").GetString());
            var historyAfter = ClaimHistory(afterRoot).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Assert.Equal(historyBefore.Length + 1, historyAfter.Length);
            using var takeoverHistory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(afterRoot, result.HistoryPath!)));
            Assert.Equal("takeover", takeoverHistory.RootElement.GetProperty("operation").GetString());
            Assert.Equal("design", takeoverHistory.RootElement.GetProperty("displaced_holder").GetString());
            Assert.Equal("implementation", takeoverHistory.RootElement.GetProperty("actor").GetString());
            Assert.Equal(takeover.Reason, takeoverHistory.RootElement.GetProperty("reason").GetString());
        }
        else
        {
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            Assert.Null(result.HistoryPath);
            var afterRoot = repos.CloneForInspection();
            Assert.Equal(claimBefore, File.ReadAllBytes(Path.Combine(afterRoot, claimRelative)));
            Assert.Equal(historyBefore, ClaimHistory(afterRoot).OrderBy(path => path, StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasePrepClaimKeepsLegacyReleaseBehaviorWithoutCompletionPayload_G857(bool write)
    {
        using var repos = new ClaimRepositories();
        const string scope = "release-prep:J-Tech-Japan/intent-system:0.30.1";
        repos.PublishSnapshot(queueState: null, runEvents: []);
        Acquire(repos.Writer, actor: "builder", scope: scope);

        var claimRelative = ClaimCommand.ClaimPath(scope);
        var beforeRoot = repos.CloneForInspection();
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimBefore = File.ReadAllBytes(Path.Combine(beforeRoot, claimRelative));
        var historyBefore = ClaimHistory(beforeRoot).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var result = ClaimCommand.RunTransaction(
            repos.Reader,
            Request(ClaimOperation.Release, actor: "builder", write: write, scope: scope));

        Assert.Equal(write ? "released" : "planned", result.Status);
        Assert.Equal(write, result.PushSucceeded);
        Assert.Equal(scope, result.Scope);
        Assert.Null(result.SoloConductorCompletion);
        Assert.DoesNotContain("solo_conductor_completion", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        if (write)
        {
            Assert.NotNull(result.HistoryPath);
            var afterRoot = repos.CloneForInspection();
            Assert.False(File.Exists(Path.Combine(afterRoot, claimRelative)));
            var historyAfter = ClaimHistory(afterRoot).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Assert.Equal(historyBefore.Length + 1, historyAfter.Length);
            using var releaseHistory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(afterRoot, result.HistoryPath!)));
            Assert.Equal("release", releaseHistory.RootElement.GetProperty("operation").GetString());
            Assert.Equal(scope, releaseHistory.RootElement.GetProperty("scope").GetString());
            Assert.Equal("builder", releaseHistory.RootElement.GetProperty("actor").GetString());
            Assert.Equal(Team, releaseHistory.RootElement.GetProperty("team").GetString());
        }
        else
        {
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            Assert.Null(result.HistoryPath);
            var afterRoot = repos.CloneForInspection();
            Assert.Equal(claimBefore, File.ReadAllBytes(Path.Combine(afterRoot, claimRelative)));
            Assert.Equal(historyBefore, ClaimHistory(afterRoot).OrderBy(path => path, StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("delivery")]
    public void EmptyOrResolvedNonSoloModeKeepsLegacyReleaseResult_G857(string modeKind)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: []);
        TeamModeStore.Write(repos.Writer, new TeamModeState
        {
            SchemaVersion = TeamModeStore.SchemaVersion,
            Entries = modeKind == "empty" ? [] : [ModeEntry(TeamMode.Delivery, Team)],
        });
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("released", result.Status);
        Assert.True(result.PushSucceeded);
        Assert.Null(result.SoloConductorCompletion);
        Assert.DoesNotContain("solo_conductor_completion", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductOnlySoloModeDoesNotActivateConfiguredCanonicalMetadataBranch_G857()
    {
        using var repos = new ClaimRepositories();
        const string branch = "intent-metadata";
        repos.ConfigureMetadataWriteBranch(branch);
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: []);
        File.Delete(TeamModeStore.ResolvePath(repos.Writer));
        repos.PublishIntentChanges(repos.Writer, branch);
        Assert.False(File.Exists(TeamModeStore.ResolvePath(repos.Writer)));

        var productCheckout = repos.CreateRacer();
        TeamModeStore.Write(productCheckout, new TeamModeState
        {
            SchemaVersion = TeamModeStore.SchemaVersion,
            Entries = [ModeEntry(TeamMode.SoloConductor, Team)],
        });
        repos.PublishIntentChanges(productCheckout, "main");
        var productWithMode = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();

        Acquire(repos.Writer);
        var result = Release(repos.Reader);

        Assert.Equal("released", result.Status);
        Assert.True(result.PushSucceeded);
        Assert.Null(result.SoloConductorCompletion);
        Assert.Equal($"refs/heads/{branch}", result.TargetRef);
        var metadataSnapshot = repos.CloneBranchForInspection(branch);
        Assert.False(File.Exists(TeamModeStore.ResolvePath(metadataSnapshot)));
        Assert.Equal(productWithMode, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
    }

    [Fact]
    public void MissingConfiguredCanonicalBranchDoesNotFallBackToProductMain_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: []);
        Acquire(repos.Writer);
        const string branch = "intent-metadata";
        repos.ConfigureMetadataWriteBranch(branch);
        Git(repos.Bare, "update-ref", "-d", $"refs/heads/{branch}");
        var mainBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();

        var error = Assert.Throws<InvalidOperationException>(() => Release(repos.Reader));

        Assert.Contains("is absent on origin", error.Message, StringComparison.Ordinal);
        Assert.Equal(mainBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        var inspection = repos.CloneForInspection();
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
        Assert.Empty(ClaimHistory(inspection));
    }

    [Theory]
    [InlineData("design")]
    [InlineData("architect")]
    public void NonBuilderReleaseOnSoloHostDoesNotRequirePacket_G857(string actor)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: []);
        Acquire(repos.Writer, actor);
        PullCanonicalHead(repos.Writer);
        File.Delete(Path.Combine(repos.Writer, GuideReachabilityRecord.ResolvePacketPath(repos.Writer, Unit)));
        repos.PublishIntentChanges(repos.Writer, "main");

        var result = Release(repos.Reader, actor);

        Assert.Equal("released", result.Status);
        Assert.True(result.PushSucceeded);
        Assert.Null(result.SoloConductorCompletion);
    }

    [Theory]
    [InlineData("not-held", "not-held")]
    [InlineData("mismatched-holder", "held")]
    public void CandidateDryRunRetainsNotHeldAndHolderMismatchResults_G857(string setup, string expectedStatus)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        if (setup == "mismatched-holder") Acquire(repos.Writer, "implementation");
        var before = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();

        var result = Release(repos.Reader, actor: "builder", write: false);

        Assert.Equal(expectedStatus, result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Null(result.SoloConductorCompletion);
        Assert.Equal(before, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
    }

    [Theory]
    [InlineData("G857+repair")]
    [InlineData("..")]
    [InlineData("équipe")]
    public void InvalidExecutionUnitCannotBypassSoloReleaseGate_G857(string invalidUnit)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: []);
        var scope = $"execution-unit:{invalidUnit}";
        Acquire(repos.Writer, scope: scope);
        var before = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();

        var preview = Release(repos.Reader, write: false, scope: scope);
        Assert.Equal("completion-blocked", preview.Status);
        Assert.Equal("execution-unit-unavailable", Duty(preview, "applicability").Cause);
        Assert.Equal("unavailable", preview.SoloConductorCompletion?.Applicability.State);
        Assert.Equal(before, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());

        var result = Release(repos.Reader, scope: scope);
        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("execution-unit-unavailable", Duty(result, "applicability").Cause);
        Assert.Equal(before, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        var inspection = repos.CloneForInspection();
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath(scope))));
        Assert.Empty(ClaimHistory(inspection));
    }

    [Fact]
    public void InvalidExecutionUnitKeepsLegacyReleaseWhenNoSoloModeApplies_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: []);
        File.Delete(TeamModeStore.ResolvePath(repos.Writer));
        repos.PublishIntentChanges(repos.Writer, "main");
        var scope = "execution-unit:G857+repair";
        Acquire(repos.Writer, scope: scope);

        var result = Release(repos.Reader, scope: scope);

        Assert.Equal("released", result.Status);
        Assert.True(result.PushSucceeded);
        Assert.Null(result.SoloConductorCompletion);
    }

    [Fact]
    public void MissingScopedQueueUsesValidLegacyQueueAndRunsFallback_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
        MoveScopedQueueAndRunsToLegacy(repos);
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("released", result.Status);
        Assert.Equal("satisfied", Duty(result, "closeout-queue").State);
        Assert.Contains(".intent-cli/queue-state.json", Duty(result, "closeout-queue").Evidence.Select(evidence => evidence.Path));
        Assert.Equal("satisfied", Duty(result, "pr-merged").State);
        Assert.Equal("satisfied", Duty(result, "closeout-recorded").State);
        Assert.Contains(".intent-cli/runs.jsonl", Duty(result, "pr-merged").Evidence.Select(evidence => evidence.Path));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("dangling-symlink")]
    public void UnavailableLegacyQueueCannotBeTreatedAsMissingForFallback_G857(string kind)
    {
        if (OperatingSystem.IsWindows() && kind == "dangling-symlink") return;
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
        MoveScopedQueueAndRunsToLegacy(repos);
        var legacyQueuePath = RuntimeScopedStateResolver.GetLegacyQueueStatePath(repos.Writer);
        File.Delete(legacyQueuePath);
        if (kind == "directory")
        {
            Directory.CreateDirectory(legacyQueuePath);
            File.WriteAllText(Path.Combine(legacyQueuePath, "marker.txt"), "tracked non-regular queue node\n");
        }
        else
        {
            File.CreateSymbolicLink(legacyQueuePath, "missing-legacy-queue-target.json");
        }
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimRelative = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var inspection = repos.CloneForInspection();
        var claimBefore = File.ReadAllBytes(Path.Combine(inspection, claimRelative));
        var historyBefore = ClaimHistory(inspection).ToArray();

        var result = Release(repos.Reader);
        var queueDuty = Duty(result, "closeout-queue");

        Assert.Equal("completion-blocked", result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Equal("implementation", result.Holder);
        Assert.Equal(Team, result.HolderTeam);
        Assert.Equal(canonicalBefore, result.SoloConductorCompletion?.CanonicalSnapshotOid);
        Assert.Equal("unavailable", queueDuty.State);
        Assert.Equal("queue-unavailable", queueDuty.Cause);
        Assert.Empty(queueDuty.RecoveryCommands);
        Assert.Null(queueDuty.PublicationStep);
        Assert.Contains(".intent-cli/queue-state.json", queueDuty.Evidence.Select(evidence => evidence.Path));
        Assert.Contains(".intent-cli/queue-state.json", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Contains("refs/heads/main", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        var canonicalAfter = repos.CloneForInspection();
        Assert.Equal(claimBefore, File.ReadAllBytes(Path.Combine(canonicalAfter, claimRelative)));
        Assert.Equal(historyBefore, ClaimHistory(canonicalAfter));
    }

    [Theory]
    [InlineData("scoped-directory")]
    [InlineData("scoped-dangling-symlink")]
    [InlineData("scoped-inaccessible-parent")]
    public void UnavailableScopedQueueCannotFallBackToCompleteLegacyEvidence_G857(string kind)
    {
        if (OperatingSystem.IsWindows() && kind == "scoped-dangling-symlink") return;
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
        MoveScopedQueueAndRunsToLegacy(repos);
        var scopedQueue = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        switch (kind)
        {
            case "scoped-directory":
                Directory.CreateDirectory(scopedQueue);
                File.WriteAllText(Path.Combine(scopedQueue, "marker.json"), "{}\n");
                break;
            case "scoped-dangling-symlink":
                File.CreateSymbolicLink(scopedQueue, "missing-queue-target.json");
                break;
            case "scoped-inaccessible-parent":
                var runtimeRoot = Path.Combine(repos.Writer, ".intent-cli", RuntimeScopedStateResolver.RuntimeDirectoryName);
                Directory.Delete(runtimeRoot, recursive: true);
                File.WriteAllText(runtimeRoot, "not a directory\n");
                break;
        }
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);
        var before = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("unavailable", Duty(result, "closeout-queue").State);
        Assert.Equal("queue-unavailable", Duty(result, "closeout-queue").Cause);
        Assert.Equal(before, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.True(File.Exists(Path.Combine(repos.CloneForInspection(), ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
        Assert.Equal("unavailable", Duty(result, "pr-merged").State);
        Assert.Equal("unavailable", Duty(result, "closeout-recorded").State);
    }

    [Theory]
    [InlineData("nested", "  source_execution_unit:\n    - G857")]
    [InlineData("nested", "  source_execution_unit: {}")]
    [InlineData("nested", "  source_execution_unit:\n    value: G857")]
    [InlineData("nested", "  source_execution_unit: null")]
    [InlineData("nested", "  source_execution_unit: \"\"")]
    [InlineData("nested", "  source_execution_unit: OTHER")]
    [InlineData("root", "source_execution_unit:\n  - G857")]
    [InlineData("root", "source_execution_unit: {}")]
    [InlineData("root", "source_execution_unit:\n  value: G857")]
    [InlineData("root", "source_execution_unit: null")]
    [InlineData("root", "source_execution_unit: \"\"")]
    [InlineData("root", "source_execution_unit: OTHER")]
    public void PresentSourceExecutionUnitMustBeExactNonemptyScalar_G857(string location, string assertion)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"],
            packetYaml: PacketWithExecutionUnitAssertion(location, assertion));
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("unavailable", result.SoloConductorCompletion?.Applicability.State);
        Assert.Equal("packet-identity-conflict", Duty(result, "applicability").Cause);
    }

    [Fact]
    public void RootLevelExactSourceExecutionUnitAssertionIsAccepted_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"],
            packetYaml: PacketWithExecutionUnitAssertion("root", "source_execution_unit: G857", ExplicitNoDutyPacketYaml));

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(result.IsApplicable);
        Assert.Equal("satisfied", result.Completion?.Decision);
        Assert.DoesNotContain(result.Completion!.Duties, duty => duty.Id == "applicability");
    }

    [Fact]
    public void MissingActualDomainCannotBeFilledByMetadataAliasOnWriteOrPreview_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: PacketMissingDomainWithMetadataAliasYaml);
        Acquire(repos.Writer);
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimRelative = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBytes = File.ReadAllBytes(Path.Combine(repos.CloneForInspection(), claimRelative));
        var historyBefore = ClaimHistory(repos.CloneForInspection()).ToArray();

        foreach (var write in new[] { false, true })
        {
            var result = Release(repos.Reader, write: write);

            Assert.Equal("completion-blocked", result.Status);
            Assert.False(result.PushSucceeded);
            Assert.Equal("packet-domain-missing", Duty(result, "applicability").Cause);
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            var inspection = repos.CloneForInspection();
            Assert.Equal(claimBytes, File.ReadAllBytes(Path.Combine(inspection, claimRelative)));
            Assert.Equal(historyBefore, ClaimHistory(inspection));
        }
    }

    [Fact]
    public void IncompleteQueueWithoutLinkedPrUsesVerifiedPlaceholderRecovery_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Active, runEvents: []);
        var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var queue = JsonNode.Parse(File.ReadAllText(queuePath))!.AsObject();
        queue["items"]!.AsArray()[0]!["linked_pr"] = null;
        File.WriteAllText(queuePath, queue.ToJsonString());
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        var queueDuty = Duty(result, "closeout-queue");
        Assert.Equal("missing", queueDuty.State);
        var command = Assert.Single(queueDuty.RecoveryCommands);
        Assert.Contains("--pr <actual-merged-pr>", command, StringComparison.Ordinal);
        Assert.Contains($"--repo {Repo}", command, StringComparison.Ordinal);
        Assert.Contains($"--domain {Domain}", command, StringComparison.Ordinal);
        Assert.Contains("--pr-merged true", command, StringComparison.Ordinal);
        Assert.DoesNotContain("--repair-runs", command, StringComparison.Ordinal);
        Assert.Contains("refs/heads/main", queueDuty.PublicationStep!, StringComparison.Ordinal);
        Assert.Null(queueDuty.RepairUnavailableReason);
        foreach (var id in new[] { "pr-merged", "closeout-recorded" })
        {
            var duty = Duty(result, id);
            Assert.Equal("unavailable", duty.State);
            Assert.Empty(duty.RecoveryCommands);
            Assert.Contains("actual PR identity", duty.RepairUnavailableReason!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ScopedCompletedQueueWithoutLinkedPrUsesLegacyOnlyLimitation_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var queue = JsonNode.Parse(File.ReadAllText(queuePath))!.AsObject();
        queue["items"]!.AsArray()[0]!["linked_pr"] = null;
        File.WriteAllText(queuePath, queue.ToJsonString());
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimRelative = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var inspection = repos.CloneForInspection();
        var claimBefore = File.ReadAllBytes(Path.Combine(inspection, claimRelative));
        var historyBefore = ClaimHistory(inspection).ToArray();
        var runsPath = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runsPath));
        Assert.Equal(["pr-merged", "closeout-recorded"], runEvents.Select(item => item.Event));
        Assert.All(runEvents, item =>
        {
            Assert.Equal(Repo, item.Repo);
            Assert.Equal(PullRequest, item.Pr);
        });

        var result = Release(repos.Reader);
        var queueDuty = Duty(result, "closeout-queue");

        Assert.Equal("completion-blocked", result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Equal("implementation", result.Holder);
        Assert.Equal(Team, result.HolderTeam);
        Assert.Equal("refused", result.SoloConductorCompletion?.Decision);
        Assert.Equal(canonicalBefore, result.SoloConductorCompletion?.CanonicalSnapshotOid);
        Assert.Equal("missing", queueDuty.State);
        Assert.Equal("linked-pr-missing", queueDuty.Cause);
        Assert.Empty(queueDuty.RecoveryCommands);
        Assert.Null(queueDuty.PublicationStep);
        Assert.Contains("state-doctor", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Contains("legacy queue layout", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Contains("completed item", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Contains("--repair-runs", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Contains("host/architect", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        var queueRelativePath = Path.GetRelativePath(repos.Writer,
            RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo))
            .Replace(Path.DirectorySeparatorChar, '/');
        Assert.Contains(queueRelativePath, queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        Assert.Contains("refs/heads/main", queueDuty.RepairUnavailableReason!, StringComparison.Ordinal);
        foreach (var id in new[] { "pr-merged", "closeout-recorded" })
        {
            var duty = Duty(result, id);
            Assert.Equal("unavailable", duty.State);
            Assert.Equal("current-pr-unavailable", duty.Cause);
            Assert.Empty(duty.RecoveryCommands);
            Assert.Null(duty.PublicationStep);
            Assert.Contains("completed canonical queue item lacks linked_pr", duty.Detail, StringComparison.Ordinal);
            Assert.Contains("responsible host/architect", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.DoesNotContain("closeout-queue recovery command", duty.Detail, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        var canonicalAfter = repos.CloneForInspection();
        Assert.Equal(claimBefore, File.ReadAllBytes(Path.Combine(canonicalAfter, claimRelative)));
        Assert.Equal(historyBefore, ClaimHistory(canonicalAfter));
    }

    [Fact]
    public void LegacyCompletedQueueMissingLinkedPrUsesStateDoctorAndCanonicalPublication_G857()
    {
        using var repos = new ClaimRepositories();
        PrepareLegacyStateDoctorFixture(repos);

        var previousListerFactory = AutomationStateDoctorCommand.CandidateListerFactory;
        var previousProbeRunner = AutomationInstalledCliSurfaceProbe.ProbeRunner;
        var previousPathReader = AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader;
        try
        {
            ConfigureStateDoctorSurfaceProbe(repos.Writer);
            AutomationStateDoctorCommand.CandidateListerFactory = () => new G857StateDoctorLister(PullRequest);

            var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
            var claimRelative = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
            var initialInspection = repos.CloneForInspection();
            var claimBefore = File.ReadAllBytes(Path.Combine(initialInspection, claimRelative));
            var historyBefore = ClaimHistory(initialInspection).ToArray();

            var initialRelease = Release(repos.Reader);
            Assert.Equal("completion-blocked", initialRelease.Status);
            AssertStateDoctorRecoveryRoute(initialRelease);
            Assert.Equal(canonicalBefore, initialRelease.SoloConductorCompletion?.CanonicalSnapshotOid);

            using var preview = RunStateDoctor(repos.Writer, write: false);
            Assert.Equal("read-only", preview.RootElement.GetProperty("mode").GetString());
            var previewFinding = Assert.Single(preview.RootElement.GetProperty("findings").EnumerateArray(), finding =>
                finding.GetProperty("execution_unit").GetString() == Unit
                && finding.GetProperty("category").GetString() == "missing-linked-pr");
            Assert.Equal("high", previewFinding.GetProperty("confidence").GetString());
            Assert.False(previewFinding.GetProperty("applied").GetBoolean());
            var legacyQueue = RuntimeScopedStateResolver.GetLegacyQueueStatePath(repos.Writer);
            var legacyRuns = RuntimeScopedStateResolver.GetLegacyRunLogPath(repos.Writer);
            var readOnlyQueue = QueueStateSerializer.Deserialize(File.ReadAllText(legacyQueue));
            Assert.Null(Assert.Single(readOnlyQueue.Items, item => item.ExecutionUnit == Unit).LinkedPr);
            Assert.Equal(new[] { "pr-merged", "closeout-recorded" },
                RunLogSerializer.DeserializeAll(File.ReadAllText(legacyRuns)).Select(item => item.Event));
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());

            using var write = RunStateDoctor(repos.Writer, write: true);
            Assert.Equal("write", write.RootElement.GetProperty("mode").GetString());
            var writeFinding = Assert.Single(write.RootElement.GetProperty("findings").EnumerateArray(), finding =>
                finding.GetProperty("execution_unit").GetString() == Unit
                && finding.GetProperty("category").GetString() == "missing-linked-pr");
            Assert.True(writeFinding.GetProperty("applied").GetBoolean());
            var repairedQueue = QueueStateSerializer.Deserialize(File.ReadAllText(legacyQueue));
            var repairedItem = Assert.Single(repairedQueue.Items, item => item.ExecutionUnit == Unit);
            Assert.Equal(QueueItemState.Completed, repairedItem.State);
            Assert.Equal($"https://github.com/{Repo}/pull/{PullRequest}", repairedItem.LinkedPr);
            Assert.False(File.Exists(RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo)));
            var repairedRuns = RunLogSerializer.DeserializeAll(File.ReadAllText(legacyRuns));
            Assert.Equal(new[] { "pr-merged", "closeout-recorded" },
                repairedRuns.Where(item => item.Event is "pr-merged" or "closeout-recorded").Select(item => item.Event));
            Assert.Contains(repairedRuns, item => item.Event == "state-doctor-repair" && item.ExecutionUnit == Unit);
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());

            var blockedBeforePublication = Release(repos.Reader);
            Assert.Equal("completion-blocked", blockedBeforePublication.Status);
            AssertStateDoctorRecoveryRoute(blockedBeforePublication);
            Assert.Equal(canonicalBefore, blockedBeforePublication.SoloConductorCompletion?.CanonicalSnapshotOid);
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            var beforePublishInspection = repos.CloneForInspection();
            Assert.Equal(claimBefore, File.ReadAllBytes(Path.Combine(beforePublishInspection, claimRelative)));
            Assert.Equal(historyBefore, ClaimHistory(beforePublishInspection));

            Git(repos.Writer, "add", "--", ".intent-cli/queue-state.json", ".intent-cli/runs.jsonl");
            var stagedPaths = Git(repos.Writer, "diff", "--cached", "--name-only")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { ".intent-cli/queue-state.json", ".intent-cli/runs.jsonl" }, stagedPaths);
            Git(repos.Writer, "-c", "user.name=test", "-c", "user.email=test@example.invalid",
                "commit", "--quiet", "-m", "publish state-doctor queue identity repair");
            Git(repos.Writer, "push", "origin", "HEAD:refs/heads/main");

            var released = Release(repos.Reader);
            Assert.Equal("released", released.Status);
            Assert.True(released.PushSucceeded);
            Assert.Equal("satisfied", released.SoloConductorCompletion?.Decision);
            Assert.Equal(PullRequest, released.SoloConductorCompletion?.LinkedPr);
            Assert.Equal("satisfied", Duty(released, "closeout-queue").State);
            Assert.Equal("satisfied", Duty(released, "pr-merged").State);
            Assert.Equal("satisfied", Duty(released, "closeout-recorded").State);
            var finalInspection = repos.CloneForInspection();
            Assert.False(File.Exists(Path.Combine(finalInspection, claimRelative)));
            Assert.Single(ClaimHistory(finalInspection));
            using var releaseHistory = JsonDocument.Parse(File.ReadAllText(Path.Combine(finalInspection, released.HistoryPath!)));
            Assert.Equal("release", releaseHistory.RootElement.GetProperty("operation").GetString());
        }
        finally
        {
            AutomationStateDoctorCommand.CandidateListerFactory = previousListerFactory;
            AutomationInstalledCliSurfaceProbe.ProbeRunner = previousProbeRunner;
            AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = previousPathReader;
        }
    }

    [Fact]
    public void LegacyCompletedQueueAmbiguousStateDoctorDoesNotGuessPr_G857()
    {
        using var repos = new ClaimRepositories();
        PrepareLegacyStateDoctorFixture(repos);

        var previousListerFactory = AutomationStateDoctorCommand.CandidateListerFactory;
        var previousProbeRunner = AutomationInstalledCliSurfaceProbe.ProbeRunner;
        var previousPathReader = AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader;
        try
        {
            ConfigureStateDoctorSurfaceProbe(repos.Writer);
            AutomationStateDoctorCommand.CandidateListerFactory = () => new G857StateDoctorLister(PullRequest, PullRequest + 1);

            var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
            using var preview = RunStateDoctor(repos.Writer, write: false);
            Assert.NotEmpty(preview.RootElement.GetProperty("unsafe_findings").EnumerateArray());
            using var write = RunStateDoctor(repos.Writer, write: true);
            Assert.NotEmpty(write.RootElement.GetProperty("unsafe_findings").EnumerateArray());

            var legacyQueue = RuntimeScopedStateResolver.GetLegacyQueueStatePath(repos.Writer);
            var queue = QueueStateSerializer.Deserialize(File.ReadAllText(legacyQueue));
            Assert.Null(Assert.Single(queue.Items, item => item.ExecutionUnit == Unit).LinkedPr);
            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(RuntimeScopedStateResolver.GetLegacyRunLogPath(repos.Writer)));
            Assert.Equal(new[] { "pr-merged", "closeout-recorded" }, runEvents.Select(item => item.Event));
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());

            var blocked = Release(repos.Reader);
            Assert.Equal("completion-blocked", blocked.Status);
            AssertStateDoctorRecoveryRoute(blocked);
            Assert.Contains("unique merged-PR closing-issue evidence", Duty(blocked, "closeout-queue").Detail, StringComparison.Ordinal);
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        }
        finally
        {
            AutomationStateDoctorCommand.CandidateListerFactory = previousListerFactory;
            AutomationInstalledCliSurfaceProbe.ProbeRunner = previousProbeRunner;
            AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = previousPathReader;
        }
    }

    [Theory]
    [InlineData("nested-before-metadata")]
    [InlineData("nested-after-metadata")]
    [InlineData("root-only")]
    [InlineData("matching-root-and-nested")]
    public void ActualPacketIdentityIgnoresUnrelatedMetadataAliases_G857(string shape)
    {
        using var repos = new ClaimRepositories();
        var packet = shape switch
        {
            "nested-after-metadata" => PacketIdentityWithMetadataAfterYaml,
            "root-only" => PacketWithRootOnlyActualIdentity(),
            "matching-root-and-nested" => PacketWithMatchingRootIdentity(),
            _ => PacketWithUnrelatedIdentityAliasesYaml,
        };
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: packet);

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(result.IsApplicable);
        Assert.Equal("satisfied", result.Completion?.Decision);
        Assert.Equal(Domain, result.Completion?.Domain);
        Assert.Equal(Repo, result.Completion?.TargetRepo);
    }

    [Fact]
    public void UnboundLegacyNumericRunsAreHistoricalThenRepairAppendsCanonicalPair_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: [], packetYaml: ExplicitNoDutyPacketYaml);
        var runsPath = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        var relativeRunsPath = Path.GetRelativePath(repos.Writer, runsPath);
        var now = DateTimeOffset.UtcNow;
        File.WriteAllLines(runsPath, new[]
        {
            RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = now,
                ExecutionUnit = Unit,
                Event = "pr-merged",
                By = "legacy closeout",
                LinkedPr = PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
                LinkedIssue = "456",
            }),
            RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = now,
                ExecutionUnit = Unit,
                Event = "closeout-recorded",
                By = "legacy closeout",
                LinkedPr = $"#{PullRequest}",
                LinkedIssue = "#456",
            }),
        });
        var legacyPrefix = File.ReadAllText(runsPath);
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var blocked = Release(repos.Reader);

        foreach (var id in new[] { "pr-merged", "closeout-recorded" })
        {
            var duty = Duty(blocked, id);
            Assert.Equal("missing", duty.State);
            Assert.Equal("current-closeout-run-missing", duty.Cause);
            Assert.Contains("--repair-runs", Assert.Single(duty.RecoveryCommands), StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.PublicationStep!, StringComparison.Ordinal);
            Assert.Null(duty.RepairUnavailableReason);
        }

        PullCanonicalHead(repos.Writer);
        using var output = new StringWriter();
        var closeoutExit = CloseoutPrCommand.Execute(Context(repos.Writer),
            ["--pr", PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
             "--repo", Repo, "--domain", Domain, "--pr-merged", "true", "--repair-runs", "--write", "--format", "json"], output);
        Assert.Equal(0, closeoutExit);
        using (var closeoutResult = JsonDocument.Parse(output.ToString()))
            Assert.True(closeoutResult.RootElement.GetProperty("runs_appended").GetBoolean());
        Assert.StartsWith(legacyPrefix, File.ReadAllText(runsPath), StringComparison.Ordinal);

        Git(repos.Writer, "add", "--", relativeRunsPath);
        Git(repos.Writer, "-c", "user.name=test", "-c", "user.email=test@example.invalid",
            "commit", "--quiet", "-m", "publish repaired G857 run receipts");
        Git(repos.Writer, "push", "origin", "HEAD:refs/heads/main");

        var released = Release(repos.Reader);

        Assert.Equal("released", released.Status);
        Assert.True(released.PushSucceeded);
        Assert.Equal("satisfied", Duty(released, "pr-merged").State);
        Assert.Equal("satisfied", Duty(released, "closeout-recorded").State);
    }

    [Fact]
    public void IndependentRepoAllowsLegacyNumericPullRequestButIssueNumberDoesNotReplaceIt_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: [], packetYaml: ExplicitNoDutyPacketYaml);
        var path = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        var now = DateTimeOffset.UtcNow;
        File.WriteAllLines(path, new[] { "pr-merged", "closeout-recorded" }.Select(eventName =>
            RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = now,
                ExecutionUnit = Unit,
                Event = eventName,
                By = "legacy closeout",
                Repo = Repo,
                LinkedPr = $"#{PullRequest}",
                LinkedIssue = "456",
            })));
        repos.PublishIntentChanges(repos.Writer, "main");

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.Equal("satisfied", result.Completion?.Decision);
        Assert.Equal("satisfied", Duty(result.Completion!, "pr-merged").State);
        Assert.Equal("satisfied", Duty(result.Completion!, "closeout-recorded").State);
    }

    [Fact]
    public void ContradictoryLegacyNumericRunIdentityIsUnavailableEvenWithModernPair_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var path = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        File.AppendAllText(path, RunLogSerializer.SerializeLine(new RunEvent
        {
            Ts = DateTimeOffset.UtcNow,
            ExecutionUnit = Unit,
            Event = "pr-merged",
            By = "contradictory legacy closeout",
            Repo = Repo,
            Pr = PullRequest,
            LinkedPr = $"#{PullRequest + 1}",
            LinkedIssue = "456",
        }) + Environment.NewLine);
        repos.PublishIntentChanges(repos.Writer, "main");

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.Equal("unavailable", Duty(result.Completion!, "pr-merged").State);
        Assert.Equal("run-identity-conflict", Duty(result.Completion!, "pr-merged").Cause);
        Assert.Equal("unavailable", Duty(result.Completion!, "closeout-recorded").State);
    }

    [Theory]
    [InlineData("domain-root-sequence", "packet-identity-conflict", "applicability")]
    [InlineData("domain-root-empty", "packet-identity-conflict", "applicability")]
    [InlineData("domain-root-conflict", "packet-identity-conflict", "applicability")]
    [InlineData("domain-nested-map", "packet-identity-conflict", "applicability")]
    [InlineData("domain-nested-null", "packet-identity-conflict", "applicability")]
    [InlineData("domain-metadata-only", "packet-domain-missing", "applicability")]
    [InlineData("target-root-map", "target-repo-identity-conflict", "target-repo")]
    [InlineData("target-root-null", "target-repo-identity-conflict", "target-repo")]
    [InlineData("target-root-conflict", "target-repo-identity-conflict", "target-repo")]
    [InlineData("target-nested-sequence", "target-repo-identity-conflict", "target-repo")]
    [InlineData("target-nested-empty", "target-repo-identity-conflict", "target-repo")]
    [InlineData("target-metadata-only", "target-repo-unavailable", "target-repo")]
    public void PacketIdentityUsesOnlyActualRootAndNestedScalarNodes_G857(string shape, string cause, string dutyId)
    {
        using var repos = new ClaimRepositories();
        var packet = shape switch
        {
            "domain-root-sequence" => AddRootIdentityNode(PacketWithUnrelatedIdentityAliasesYaml, "domain", "  - other"),
            "domain-root-empty" => AddRootIdentityNode(PacketWithUnrelatedIdentityAliasesYaml, "domain", "  \"\""),
            "domain-root-conflict" => AddRootIdentityNode(PacketWithUnrelatedIdentityAliasesYaml, "domain", "  other"),
            "domain-nested-map" => PacketWithUnrelatedIdentityAliasesYaml.Replace(
                "  domain: intent-cli\n", "  domain: {}\n", StringComparison.Ordinal),
            "domain-nested-null" => PacketWithUnrelatedIdentityAliasesYaml.Replace(
                "  domain: intent-cli\n", "  domain: null\n", StringComparison.Ordinal),
            "domain-metadata-only" => PacketWithUnrelatedIdentityAliasesYaml.Replace(
                "  domain: intent-cli\n", string.Empty, StringComparison.Ordinal),
            "target-root-map" => AddRootIdentityNode(PacketWithUnrelatedIdentityAliasesYaml, "target_repo", "  {}"),
            "target-root-null" => AddRootIdentityNode(PacketWithUnrelatedIdentityAliasesYaml, "target_repo", "  null"),
            "target-root-conflict" => AddRootIdentityNode(PacketWithUnrelatedIdentityAliasesYaml, "target_repo", "  other/repo"),
            "target-nested-sequence" => PacketWithUnrelatedIdentityAliasesYaml.Replace(
                "  target_repo: J-Tech-Japan/intent-system\n", "  target_repo:\n    - J-Tech-Japan/intent-system\n", StringComparison.Ordinal),
            "target-nested-empty" => PacketWithUnrelatedIdentityAliasesYaml.Replace(
                "  target_repo: J-Tech-Japan/intent-system\n", "  target_repo: \"\"\n", StringComparison.Ordinal),
            "target-metadata-only" => PacketWithUnrelatedIdentityAliasesYaml.Replace(
                "  target_repo: J-Tech-Japan/intent-system\n", string.Empty, StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: packet);

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.Equal("refused", result.Completion?.Decision);
        Assert.Equal(cause, Duty(result.Completion!, dutyId).Cause);
        Assert.Equal("unavailable", Duty(result.Completion!, dutyId).State);
    }

    [Theory]
    [InlineData("malformed-packet", "packet-unavailable", "applicability")]
    [InlineData("wrong-source-unit", "packet-identity-conflict", "applicability")]
    [InlineData("invalid-target-repo", "target-repo-unavailable", "target-repo")]
    [InlineData("foreign-target-repo", "queue-repo-conflict", "closeout-queue")]
    [InlineData("duplicate-queue", "duplicate-queue-item", "closeout-queue")]
    [InlineData("external-linked-pr", "queue-pr-identity-conflict", "closeout-queue")]
    [InlineData("legacy-number-without-issue", "queue-pr-identity-conflict", "closeout-queue")]
    public void InvalidPacketAndQueueIdentitiesRefuseCanonicalRelease_G857(string kind, string cause, string dutyId)
    {
        using var repos = new ClaimRepositories();
        var packet = kind switch
        {
            "malformed-packet" => "implementation_issue_packet:\n  domain: [unterminated\n",
            "wrong-source-unit" => PacketWithExecutionUnitAssertion("nested", "  source_execution_unit: OTHER"),
            "invalid-target-repo" => PacketYaml.Replace("target_repo: J-Tech-Japan/intent-system", "target_repo: unsafe/../repo", StringComparison.Ordinal),
            "foreign-target-repo" => PacketYaml.Replace("target_repo: J-Tech-Japan/intent-system", "target_repo: other/repo", StringComparison.Ordinal),
            _ => PacketYaml,
        };
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: packet);
        if (kind == "foreign-target-repo")
        {
            var currentQueue = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
            var foreignQueue = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, "other/repo");
            Directory.CreateDirectory(Path.GetDirectoryName(foreignQueue)!);
            File.Copy(currentQueue, foreignQueue);
            repos.PublishIntentChanges(repos.Writer, "main");
        }
        else if (kind is "duplicate-queue" or "external-linked-pr" or "legacy-number-without-issue")
        {
            var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
            var queue = JsonNode.Parse(File.ReadAllText(queuePath))!.AsObject();
            var items = queue["items"]!.AsArray();
            if (kind == "duplicate-queue")
            {
                items.Add(items[0]!.DeepClone());
            }
            else if (kind == "external-linked-pr")
            {
                items[0]!["linked_pr"] = "https://example.com/J-Tech-Japan/intent-system/pull/1873";
            }
            else
            {
                items[0]!["linked_pr"] = PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture);
                items[0]!["linked_issue"] = null;
            }
            File.WriteAllText(queuePath, queue.ToJsonString());
            repos.PublishIntentChanges(repos.Writer, "main");
        }
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.Equal("unavailable", Duty(result, dutyId).State);
        Assert.Equal(cause, Duty(result, dutyId).Cause);
        if (dutyId == "closeout-queue")
        {
            var duty = Duty(result, dutyId);
            Assert.Empty(duty.RecoveryCommands);
            Assert.Contains(".intent-cli/", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("host/queue-owner", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.RepairUnavailableReason!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MalformedCanonicalRunLineMakesBothCloseoutReceiptsUnavailable_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
        var runPath = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        File.AppendAllText(runPath, "{ malformed json line\n");
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        foreach (var id in new[] { "pr-merged", "closeout-recorded" })
        {
            var duty = Duty(result, id);
            Assert.Equal("unavailable", duty.State);
            Assert.Equal("run-log-unavailable", duty.Cause);
            Assert.Empty(duty.RecoveryCommands);
            Assert.Null(duty.PublicationStep);
            Assert.Contains(".intent-cli/", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("--repair-runs", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("cannot correct", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("host/architect", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.RepairUnavailableReason!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SymlinkedCanonicalRunLogIsUnavailableWithRepairRouting_G857()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
        var runPath = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        File.Delete(runPath);
        var externalTarget = Path.Combine(Path.GetDirectoryName(repos.Writer)!, "external-runs.jsonl");
        File.WriteAllText(externalTarget, string.Empty);
        File.CreateSymbolicLink(runPath, externalTarget);
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        foreach (var id in new[] { "pr-merged", "closeout-recorded" })
        {
            var duty = Duty(result, id);
            Assert.Equal("unavailable", duty.State);
            Assert.Equal("run-log-unavailable", duty.Cause);
            Assert.Empty(duty.RecoveryCommands);
            Assert.Contains(".intent-cli/", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("--repair-runs", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("host/architect", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.RepairUnavailableReason!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RecordedDomainFallbackAndExactTeamOverrideAreRespected_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
        TeamModeStore.Write(repos.Writer, new TeamModeState
        {
            SchemaVersion = TeamModeStore.SchemaVersion,
            Entries = [ModeEntry(TeamMode.SoloConductor, team: null)],
        });

        var domainFallback = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(domainFallback.IsApplicable);
        Assert.Equal("satisfied", domainFallback.Completion?.Decision);
        Assert.Equal("domain", domainFallback.Completion?.Applicability.ModeSource);

        TeamModeStore.Write(repos.Writer, new TeamModeState
        {
            SchemaVersion = TeamModeStore.SchemaVersion,
            Entries = [ModeEntry(TeamMode.SoloConductor, team: null), ModeEntry(TeamMode.Delivery, Team)],
        });
        var exactTeamOverride = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.False(exactTeamOverride.IsApplicable);
        Assert.Null(exactTeamOverride.Completion);
    }

    [Theory]
    [InlineData("missing-packet")]
    [InlineData("missing-domain")]
    public void MixedHostWithoutCanonicalPacketDomainRefusesToGuess_G857(string kind)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed,
            runEvents: ["pr-merged", "closeout-recorded"], packetYaml: ExplicitNoDutyPacketYaml);
        TeamModeStore.Write(repos.Writer, new TeamModeState
        {
            SchemaVersion = TeamModeStore.SchemaVersion,
            Entries = [ModeEntry(TeamMode.SoloConductor, "another-team")],
        });
        var packetPath = Path.Combine(repos.Writer, GuideReachabilityRecord.ResolvePacketPath(repos.Writer, Unit));
        if (kind == "missing-packet")
        {
            File.Delete(packetPath);
        }
        else
        {
            File.WriteAllText(packetPath, """
                implementation_issue_packet:
                  source_execution_unit: G857
                  target_repo: J-Tech-Japan/intent-system
                knowledge_updates:
                  intent_tree:
                    required: false
                guide_reachability:
                  no_role_facing_surface: true
                  routes: []
                """);
        }

        var result = EvaluateCanonicalSnapshot(repos.Writer);

        Assert.True(result.IsApplicable);
        Assert.Equal("refused", result.Completion?.Decision);
        Assert.Equal("unavailable", result.Completion?.Applicability.State);
        Assert.Equal("packet-domain-missing", Duty(result.Completion!, "applicability").Cause);
    }

    [Fact]
    public void ConflictingRunIdentityIsUnavailable_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        repos.WriteRunEvents(["pr-merged", "closeout-recorded"], PullRequest, $"https://github.com/other/repo/pull/{PullRequest}");
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        PublishExactReceipts(repos.Writer, "main");

        var result = Release(repos.Reader);

        Assert.Equal("unavailable", Duty(result, "pr-merged").State);
        Assert.Equal("unavailable", Duty(result, "closeout-recorded").State);
        Assert.Equal("satisfied", Duty(result, "closeout-queue").State);
        Assert.Equal("satisfied", Duty(result, "knowledge-architect").State);
        Assert.Equal("satisfied", Duty(result, "guide-reachability").State);
        foreach (var id in new[] { "pr-merged", "closeout-recorded" })
        {
            var duty = Duty(result, id);
            Assert.Empty(duty.RecoveryCommands);
            Assert.Contains(".intent-cli/", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("--repair-runs", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("host/architect", duty.RepairUnavailableReason!, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", duty.RepairUnavailableReason!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("null-items")]
    [InlineData("null-entry")]
    public void NullQueueItemsAreUnavailable_G857(string kind)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var items = kind == "null-items" ? "null" : "[null]";
        repos.WriteCanonicalQueueJson($"{{\"schema_version\":\"1\",\"updated_at\":\"2026-10-08T00:00:00Z\",\"items\":{items}}}");
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("unavailable", Duty(result, "closeout-queue").State);
        Assert.Equal("implementation", result.Holder);
        Assert.Equal(Team, result.HolderTeam);
    }

    [Fact]
    public void StructuredLinkedPrContradictionIsUnavailableEvenWhenUrlMatches_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var queueJson = File.ReadAllText(queuePath);
        var expected = $"\"linked_pr\": \"https://github.com/{Repo}/pull/{PullRequest}\"";
        Assert.Contains(expected, queueJson, StringComparison.Ordinal);
        File.WriteAllText(queuePath, queueJson.Replace(expected,
            $"\"linked_pr\": {{\"repo\":\"other/repo\",\"number\":999,\"url\":\"https://github.com/{Repo}/pull/{PullRequest}\"}}",
            StringComparison.Ordinal));
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("unavailable", Duty(result, "closeout-queue").State);
        Assert.Contains("structured linked_pr.repo", Duty(result, "closeout-queue").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("knowledge-role-directory-symlink")]
    [InlineData("knowledge-dangling-legacy-symlink")]
    [InlineData("knowledge-record-path-directory")]
    [InlineData("guide-role-directory-symlink")]
    [InlineData("guide-dangling-legacy-symlink")]
    [InlineData("guide-record-path-directory")]
    public void InvalidKnowledgeEvidencePathsFailClosed_G857(string kind)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var isGuide = kind.StartsWith("guide-", StringComparison.Ordinal);
        var recordRoot = isGuide ? GuideReachabilityRecord.RecordRootRelativePath : KnowledgeWriteBackRecord.RecordRootRelativePath;
        var basePath = Path.Combine(repos.Writer, recordRoot, Unit);
        Directory.CreateDirectory(basePath);
        switch (kind)
        {
            case "knowledge-role-directory-symlink":
            case "guide-role-directory-symlink":
                var external = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(repos.Writer)!, "external-roles"));
                Directory.CreateSymbolicLink(Path.Combine(basePath, RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName), external.FullName);
                break;
            case "knowledge-dangling-legacy-symlink":
            case "guide-dangling-legacy-symlink":
                File.CreateSymbolicLink(Path.Combine(basePath, "record.json"), Path.Combine(basePath, "absent-target.json"));
                break;
            case "knowledge-record-path-directory":
            case "guide-record-path-directory":
                var records = Directory.CreateDirectory(Path.Combine(basePath, RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName));
                var recordDirectory = Directory.CreateDirectory(Path.Combine(records.FullName, "architect.json"));
                File.WriteAllText(Path.Combine(recordDirectory.FullName, "marker.txt"), "directory must not be ignored");
                break;
        }
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        var affectedDuty = Duty(result, isGuide ? "guide-reachability" : "knowledge-architect");
        Assert.Equal("unavailable", affectedDuty.State);
        Assert.Contains(isGuide ? "guide" : "knowledge", affectedDuty.Cause, StringComparison.Ordinal);
        if (isGuide)
        {
            Assert.Equal("missing", Duty(result, "knowledge-architect").State);
        }
        else
        {
            Assert.Equal("unavailable", Duty(result, "knowledge-orchestrator").State);
        }
    }

    [Theory]
    [InlineData("knowledge-kind")]
    [InlineData("knowledge-unit")]
    [InlineData("knowledge-role")]
    [InlineData("knowledge-commit")]
    [InlineData("knowledge-json")]
    [InlineData("guide-kind")]
    [InlineData("guide-unit")]
    [InlineData("guide-role")]
    [InlineData("guide-commit")]
    [InlineData("guide-json")]
    public void InvalidCanonicalRecordShapesAreUnavailable_G857(string kind)
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        var isGuide = kind.StartsWith("guide-", StringComparison.Ordinal);
        var path = Path.Combine(repos.Writer, RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            isGuide ? GuideReachabilityRecord.RecordRootRelativePath : KnowledgeWriteBackRecord.RecordRootRelativePath,
            Unit, "architect"));
        var shape = kind[(kind.IndexOf('-') + 1)..];
        if (shape == "json")
        {
            File.WriteAllText(path, "{ malformed-json\n");
        }
        else
        {
            var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            switch (shape)
            {
                case "kind": record["artifact_kind"] = "wrong-record-kind"; break;
                case "unit": record["execution_unit"] = "other-unit"; break;
                case "role": record["role"] = "unrecognized-role"; break;
                case "commit": record["host_commit"] = "not-a-commit"; break;
            }
            File.WriteAllText(path, record.ToJsonString());
        }

        var result = EvaluateCanonicalSnapshot(repos.Writer);
        var duty = Duty(result.Completion!, isGuide ? "guide-reachability" : "knowledge-architect");

        Assert.True(result.IsApplicable);
        Assert.Equal("refused", result.Completion?.Decision);
        Assert.Equal("unavailable", duty.State);
        Assert.NotNull(duty.RepairUnavailableReason);
        Assert.Contains(path.Replace(repos.Writer + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/'), duty.Evidence.Select(evidence => evidence.Path));
    }

    [Fact]
    public void UnreadableCanonicalRecordIsUnavailable_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        var architectPath = Path.GetFullPath(Path.Combine(repos.Writer,
            RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect")));
        var originalFactory = GuardedFileRead.ReadAllTextFactory;
        GuardedFileRead.ReadAllTextFactory = path =>
        {
            if (Path.GetFullPath(path) == architectPath)
                throw new IOException("G857 named canonical receipt read failure");
            return File.ReadAllText(path);
        };

        try
        {
            var result = EvaluateCanonicalSnapshot(repos.Writer);
            var duty = Duty(result.Completion!, "knowledge-architect");

            Assert.True(result.IsApplicable);
            Assert.Equal("refused", result.Completion?.Decision);
            Assert.Equal("unavailable", duty.State);
            Assert.Contains("G857 named canonical receipt read failure", duty.Detail, StringComparison.Ordinal);
            Assert.Contains("unreadable", duty.RepairUnavailableReason, StringComparison.Ordinal);
        }
        finally
        {
            GuardedFileRead.ReadAllTextFactory = originalFactory;
        }
    }

    [Fact]
    public void UnavailableCompletionDiagnosticsMatchJsonAndMarkdown_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        var architectPath = Path.Combine(repos.Writer, RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect"));
        File.WriteAllText(architectPath, "{ malformed-json\n");
        PublishExactReceipts(repos.Writer, "main");
        using var json = new StringWriter();
        using var markdown = new StringWriter();
        var arguments = new[]
        {
            "--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
            "--reason", "G857 unavailable diagnostics", "--format",
        };

        Assert.Equal(1, ClaimCommand.ExecuteRelease(Context(repos.Reader), [.. arguments, "json"], json));
        Assert.Equal(1, ClaimCommand.ExecuteRelease(Context(repos.Reader), [.. arguments, "markdown"], markdown));

        using var jsonDocument = JsonDocument.Parse(json.ToString());
        var jsonCompletion = jsonDocument.RootElement.GetProperty("solo_conductor_completion");
        var heading = string.Join(Environment.NewLine, "## Solo-conductor completion", string.Empty, "```json", string.Empty);
        var start = markdown.ToString().IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        start += heading.Length;
        var end = markdown.ToString().IndexOf(Environment.NewLine + "```", start, StringComparison.Ordinal);
        Assert.True(end > start);
        using var markdownDocument = JsonDocument.Parse(markdown.ToString()[start..end]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(jsonCompletion.GetRawText()), JsonNode.Parse(markdownDocument.RootElement.GetRawText())));
        var unavailable = Duty(Assert.IsType<SoloConductorClaimReleaseCompletion>(
            jsonCompletion.Deserialize<SoloConductorClaimReleaseCompletion>()), "knowledge-architect");
        Assert.Equal("unavailable", unavailable.State);
        Assert.Equal("knowledge-record-unavailable", unavailable.Cause);
        Assert.NotNull(unavailable.RepairUnavailableReason);
    }

    [Fact]
    public void CandidateBlockedJsonAndMarkdownExposeEquivalentCompletionDiagnostics_G857()
    {
        using var repos = new ClaimRepositories();
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        Acquire(repos.Writer);
        using var json = new StringWriter();
        using var markdown = new StringWriter();
        var arguments = new[]
        {
            "--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
            "--reason", "G857 diagnostics", "--format",
        };

        Assert.Equal(1, ClaimCommand.ExecuteRelease(Context(repos.Reader), [.. arguments, "json"], json));
        Assert.Equal(1, ClaimCommand.ExecuteRelease(Context(repos.Reader), [.. arguments, "markdown"], markdown));

        using var jsonDocument = JsonDocument.Parse(json.ToString());
        var jsonCompletion = jsonDocument.RootElement.GetProperty("solo_conductor_completion");
        var heading = string.Join(Environment.NewLine, "## Solo-conductor completion", string.Empty, "```json", string.Empty);
        var start = markdown.ToString().IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        start += heading.Length;
        var end = markdown.ToString().IndexOf(Environment.NewLine + "```", start, StringComparison.Ordinal);
        Assert.True(end > start);
        using var markdownDocument = JsonDocument.Parse(markdown.ToString()[start..end]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(jsonCompletion.GetRawText()), JsonNode.Parse(markdownDocument.RootElement.GetRawText())));
        Assert.Equal("missing", Duty(Assert.IsType<SoloConductorClaimReleaseCompletion>(jsonDocument.RootElement
            .GetProperty("solo_conductor_completion").Deserialize<SoloConductorClaimReleaseCompletion>()), "knowledge-architect").State);
    }

    private static SoloConductorClaimReleaseDuty Duty(ClaimTransactionResult result, string id) =>
        Assert.Single(Assert.IsType<SoloConductorClaimReleaseCompletion>(result.SoloConductorCompletion).Duties,
            item => item.Id == id);

    private static SoloConductorClaimReleaseDuty Duty(SoloConductorClaimReleaseCompletion completion, string id) =>
        Assert.Single(completion.Duties, item => item.Id == id);

    private static SoloConductorClaimReleaseGateResult EvaluateCanonicalSnapshot(string repoRoot)
    {
        var snapshotOid = Git(repoRoot, "rev-parse", "HEAD").Trim();
        return SoloConductorClaimReleaseGate.Evaluate(repoRoot, Unit, Team, snapshotOid, "refs/heads/main");
    }

    private static TeamModeEntry ModeEntry(string mode, string? team)
    {
        var now = DateTimeOffset.UtcNow;
        return new TeamModeEntry
        {
            Domain = Domain,
            Team = team,
            Mode = mode,
            UpdatedAt = now,
            Transitions = [new TeamModeTransition { From = TeamMode.Delivery, To = mode, At = now }],
        };
    }

    private static string PreparePreviewCallerState(string root)
    {
        File.WriteAllText(Path.Combine(root, "g857-preview-staged.txt"), "staged contents\n");
        Git(root, "add", "--", "g857-preview-staged.txt");
        File.WriteAllText(Path.Combine(root, "README.md"), "unstaged caller edit\n");
        File.WriteAllText(Path.Combine(root, "g857-preview-untracked.txt"), "untracked caller data\n");
        foreach (var relative in new[]
        {
            ".intent-cli/claims/sentinel.json",
            ".intent-cli/claims/history/sentinel.json",
            ".intent-cli/queue/sentinel.json",
            ".intent-cli/runs/sentinel.jsonl",
            ".intent-cli/knowledge-writebacks/G857/caller-only.json",
            ".intent-cli/guide-reachability/G857/caller-only.json",
            ".intent-cli/ci-wait/sentinel.json",
        })
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "caller-only sentinel\n");
        }

        var staleRoot = Path.Combine(Path.GetTempPath(), $"intent-cli-claim-g857-stale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staleRoot);
        File.WriteAllText(Path.Combine(staleRoot, "owned-sentinel.txt"), "must survive preview");
        Directory.SetLastWriteTimeUtc(staleRoot, DateTime.UtcNow.AddMinutes(-10));
        return staleRoot;
    }

    private static void Acquire(string repoRoot, string actor = "implementation", string? scope = null)
    {
        var acquired = ClaimCommand.RunTransaction(repoRoot, Request(ClaimOperation.Acquire, actor: actor, scope: scope));
        Assert.Equal("acquired", acquired.Status);
    }

    private static ClaimTransactionResult Release(string repoRoot, string actor = "implementation", bool write = true, string? scope = null) =>
        ClaimCommand.RunTransaction(repoRoot, Request(ClaimOperation.Release, actor: actor, write: write, scope: scope));

    private static ClaimRequest Request(ClaimOperation operation, string actor = "implementation", bool write = true, string? scope = null) => new(
        operation,
        scope ?? $"execution-unit:{Unit}",
        actor,
        Team,
        operation == ClaimOperation.Release ? "G857 complete" : null,
        null,
        write,
        "json",
        ClaimCommand.DefaultMaxAttempts);

    private static void PublishExactReceipts(string root, string branch)
    {
        var paths = new[]
            {
                RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect"),
                RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "orchestrator"),
                RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect"),
                RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(GuideReachabilityRecord.RecordRootRelativePath, Unit, "orchestrator"),
            }
            .Where(path => File.Exists(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
        if (paths.Length == 0) return;
        Git(root, ["add", "--", .. paths]);
        Git(root, "-c", "user.name=test", "-c", "user.email=test@example.invalid",
            "commit", "--quiet", "-m", "publish remaining G857 receipts");
        Git(root, "push", "origin", $"HEAD:refs/heads/{branch}");
    }

    private static string[] ReceiptPaths() =>
    [
        RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect"),
        RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "orchestrator"),
        RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect"),
    ];

    private static void MoveScopedQueueAndRunsToLegacy(ClaimRepositories repos)
    {
        var scopedQueue = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var scopedRuns = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        var legacyQueue = RuntimeScopedStateResolver.GetLegacyQueueStatePath(repos.Writer);
        var legacyRuns = RuntimeScopedStateResolver.GetLegacyRunLogPath(repos.Writer);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyQueue)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyRuns)!);
        File.Move(scopedQueue, legacyQueue);
        File.Move(scopedRuns, legacyRuns);
        repos.PublishIntentChanges(repos.Writer, "main");
    }

    private static void PrepareLegacyStateDoctorFixture(ClaimRepositories repos)
    {
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: ["pr-merged", "closeout-recorded"]);
        MoveScopedQueueAndRunsToLegacy(repos);
        var queuePath = RuntimeScopedStateResolver.GetLegacyQueueStatePath(repos.Writer);
        var queue = JsonNode.Parse(File.ReadAllText(queuePath))!.AsObject();
        queue["items"]!.AsArray()[0]!["linked_pr"] = null;
        File.WriteAllText(queuePath, queue.ToJsonString());
        repos.PublishIntentChanges(repos.Writer, "main");
        Acquire(repos.Writer);
        PullCanonicalHead(repos.Writer);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        RecordKnowledge(repos.Writer, "architect", hostCommit);
        RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        RecordGuide(repos.Writer, "architect", hostCommit);
        PublishExactReceipts(repos.Writer, "main");
    }

    private static void ConfigureStateDoctorSurfaceProbe(string root)
    {
        var installedPath = Path.Combine(root, ".intent-cli", "installed-cli-stub");
        File.WriteAllText(installedPath, "state-doctor test fixture\n");
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = () => installedPath;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, _) => new InstalledCliProbeResult(
            0,
            "automation summary host-review-preflight issue-publish pr-transition review-start request-update approved",
            string.Empty);
    }

    private static JsonDocument RunStateDoctor(string root, bool write)
    {
        using var output = new StringWriter();
        var mode = write ? "--write" : "--read-only";
        var exitCode = AutomationStateDoctorCommand.Execute(Context(root),
        [
            "--workdir", root,
            "--repo", Repo,
            "--domain", Domain,
            "--team", Team,
            mode,
            "--format", "json",
        ], output);
        Assert.Equal(0, exitCode);
        return JsonDocument.Parse(output.ToString());
    }

    private static void AssertStateDoctorRecoveryRoute(ClaimTransactionResult result)
    {
        var queueDuty = Duty(result, "closeout-queue");
        Assert.Equal("missing", queueDuty.State);
        Assert.Equal("linked-pr-missing", queueDuty.Cause);
        Assert.Null(queueDuty.RepairUnavailableReason);
        Assert.NotNull(queueDuty.PublicationStep);
        Assert.Contains("refs/heads/main", queueDuty.PublicationStep!, StringComparison.Ordinal);
        Assert.Contains("host-wide, not unit-scoped", queueDuty.Detail, StringComparison.Ordinal);
        Assert.Contains("unique merged-PR closing-issue evidence", queueDuty.Detail, StringComparison.Ordinal);
        Assert.Contains("stage only the exact owned", queueDuty.Detail, StringComparison.Ordinal);
        Assert.Equal(2, queueDuty.RecoveryCommands.Count);
        Assert.Contains("--read-only --format json", queueDuty.RecoveryCommands[0], StringComparison.Ordinal);
        Assert.Contains("--write --format json", queueDuty.RecoveryCommands[1], StringComparison.Ordinal);
        foreach (var command in queueDuty.RecoveryCommands)
        {
            Assert.Contains("intent-cli automation state-doctor", command, StringComparison.Ordinal);
            Assert.Contains("--workdir <canonical-host-checkout>", command, StringComparison.Ordinal);
            Assert.Contains($"--repo {Repo}", command, StringComparison.Ordinal);
            Assert.Contains($"--domain {Domain}", command, StringComparison.Ordinal);
            Assert.Contains($"--team {Team}", command, StringComparison.Ordinal);
        }

        foreach (var id in new[] { "pr-merged", "closeout-recorded" })
        {
            var duty = Duty(result, id);
            Assert.Equal("unavailable", duty.State);
            Assert.Equal("current-pr-unavailable", duty.Cause);
            Assert.Equal(queueDuty.RecoveryCommands, duty.RecoveryCommands);
            Assert.NotNull(duty.PublicationStep);
            Assert.Contains("refs/heads/main", duty.PublicationStep!, StringComparison.Ordinal);
            Assert.Null(duty.RepairUnavailableReason);
            Assert.Contains("host-wide, not unit-scoped", duty.Detail, StringComparison.Ordinal);
        }
    }

    private static string PacketWithExecutionUnitAssertion(
        string location,
        string assertion,
        string? packetYaml = null)
    {
        var yaml = (packetYaml ?? PacketYaml).Replace("  source_execution_unit: G857\n", string.Empty, StringComparison.Ordinal);
        var marker = location == "nested" ? "  target_repo:" : "knowledge_updates:";
        var fragment = location == "nested" ? assertion : assertion;
        if (!yaml.Contains(marker, StringComparison.Ordinal))
            throw new InvalidOperationException($"Packet fixture lacks insertion marker '{marker}'.");
        return yaml.Replace(marker, $"{fragment}\n{marker}", StringComparison.Ordinal);
    }

    private static string AddRootIdentityNode(string yaml, string name, string content) =>
        yaml.Replace("metadata:\n", $"{name}:\n{content}\nmetadata:\n", StringComparison.Ordinal);

    private static string PacketWithRootOnlyActualIdentity()
    {
        var yaml = PacketWithUnrelatedIdentityAliasesYaml
            .Replace("implementation_issue_packet:\n", string.Empty, StringComparison.Ordinal)
            .Replace("  domain: intent-cli\n", string.Empty, StringComparison.Ordinal)
            .Replace("  source_execution_unit: G857\n", string.Empty, StringComparison.Ordinal)
            .Replace("  target_repo: J-Tech-Japan/intent-system\n", string.Empty, StringComparison.Ordinal);
        yaml = AddRootIdentityNode(yaml, "domain", "  intent-cli");
        yaml = AddRootIdentityNode(yaml, "source_execution_unit", "  G857");
        return AddRootIdentityNode(yaml, "target_repo", "  J-Tech-Japan/intent-system");
    }

    private static string PacketWithMatchingRootIdentity()
    {
        var yaml = AddRootIdentityNode(PacketWithUnrelatedIdentityAliasesYaml, "domain", "  intent-cli");
        yaml = AddRootIdentityNode(yaml, "source_execution_unit", "  G857");
        return AddRootIdentityNode(yaml, "target_repo", "  J-Tech-Japan/intent-system");
    }

    private static string PacketWithKnowledgeFragment(string fragment) =>
        ExplicitNoDutyPacketYaml.Replace(
            "knowledge_updates:\n  intent_tree:\n    required: false\n",
            fragment + "\n",
            StringComparison.Ordinal);

    private static string PacketWithMalformedDeclarationShape(string shape)
    {
        const string knowledgeRequired = "    required: false\n";
        const string closeoutRequired = "  write_back_required: false\n";
        return shape switch
        {
            "knowledge-required-array" => ExplicitNoDutyPacketYaml.Replace(knowledgeRequired, "    required: []\n", StringComparison.Ordinal),
            "knowledge-required-map" => ExplicitNoDutyPacketYaml.Replace(knowledgeRequired, "    required: {}\n", StringComparison.Ordinal),
            "knowledge-required-null" => ExplicitNoDutyPacketYaml.Replace(knowledgeRequired, "    required: null\n", StringComparison.Ordinal),
            "knowledge-required-empty" => ExplicitNoDutyPacketYaml.Replace(knowledgeRequired, "    required: \"\"\n", StringComparison.Ordinal),
            "knowledge-required-invalid" => ExplicitNoDutyPacketYaml.Replace(knowledgeRequired, "    required: definitely\n", StringComparison.Ordinal),
            "closeout-required-array" => CloseoutLearningFalsePacketYaml.Replace(closeoutRequired, "  write_back_required: []\n", StringComparison.Ordinal),
            "closeout-required-map" => CloseoutLearningFalsePacketYaml.Replace(closeoutRequired, "  write_back_required: {}\n", StringComparison.Ordinal),
            "closeout-required-null" => CloseoutLearningFalsePacketYaml.Replace(closeoutRequired, "  write_back_required: null\n", StringComparison.Ordinal),
            "closeout-required-empty" => CloseoutLearningFalsePacketYaml.Replace(closeoutRequired, "  write_back_required: \"\"\n", StringComparison.Ordinal),
            "closeout-required-invalid" => CloseoutLearningFalsePacketYaml.Replace(closeoutRequired, "  write_back_required: definitely\n", StringComparison.Ordinal),
            "knowledge-updates-array-with-false" => PacketWithKnowledgeFragment(
                "knowledge_updates: []\ncloseout_learning:\n  write_back_required: false"),
            "knowledge-updates-null-with-false" => PacketWithKnowledgeFragment(
                "knowledge_updates: null\ncloseout_learning:\n  write_back_required: false"),
            "closeout-learning-array-with-false" => PacketWithKnowledgeFragment(
                "knowledge_updates:\n  intent_tree:\n    required: false\ncloseout_learning: []"),
            "closeout-learning-null-with-false" => PacketWithKnowledgeFragment(
                "knowledge_updates:\n  intent_tree:\n    required: false\ncloseout_learning: null"),
            "facet-array-with-false" => PacketWithKnowledgeFragment(
                "knowledge_updates:\n  intent_tree: []\n  adr:\n    required: false"),
            "facet-null-with-false" => PacketWithKnowledgeFragment(
                "knowledge_updates:\n  intent_tree: null\n  adr:\n    required: false"),
            "malformed-required-with-valid-true-facet" => PacketWithKnowledgeFragment(
                "knowledge_updates:\n  intent_tree:\n    required: true\n  adr:\n    required: []"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    private static string PacketWithMalformedGuideShape(string shape) => shape switch
    {
        "guide-boolean-array" => ExplicitNoDutyPacketYaml.Replace(
            "no_role_facing_surface: true", "no_role_facing_surface: []", StringComparison.Ordinal),
        "guide-boolean-map" => ExplicitNoDutyPacketYaml.Replace(
            "no_role_facing_surface: true", "no_role_facing_surface: {}", StringComparison.Ordinal),
        "guide-boolean-null" => ExplicitNoDutyPacketYaml.Replace(
            "no_role_facing_surface: true", "no_role_facing_surface: null", StringComparison.Ordinal),
        "guide-boolean-empty" => ExplicitNoDutyPacketYaml.Replace(
            "no_role_facing_surface: true", "no_role_facing_surface: \"\"", StringComparison.Ordinal),
        "guide-boolean-invalid" => ExplicitNoDutyPacketYaml.Replace(
            "no_role_facing_surface: true", "no_role_facing_surface: definitely", StringComparison.Ordinal),
        "guide-hidden-no-surface-array" => ReplaceGuideTail(
            "  no_role_facing_surface: true\n  no_surface: []\n  routes: []"),
        "guide-hidden-required-map" => ReplaceGuideTail(
            "  no_role_facing_surface: true\n  required: {}\n  routes: []"),
        "guide-hidden-route-alias-map" => ReplaceGuideTail(
            "  no_role_facing_surface: false\n  routes:\n    - guide_surface: guide workflow task implementation-loop\n      role: implementation\n      target_surface: claim release completion\n  guide_routes: {}"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    private static string ReplaceGuideTail(string replacement)
    {
        const string guideTail = "  no_role_facing_surface: true\n  routes: []";
        Assert.Contains(guideTail, ExplicitNoDutyPacketYaml);
        var packet = ExplicitNoDutyPacketYaml.Replace(guideTail, replacement, StringComparison.Ordinal);
        Assert.NotEqual(ExplicitNoDutyPacketYaml, packet);
        return packet;
    }

    private static void AssertBlockedReleasePreservesCanonicalClaim(
        ClaimRepositories repos,
        string dutyId,
        string expectedCause)
    {
        Acquire(repos.Writer);
        var claimRelative = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimBefore = File.ReadAllBytes(Path.Combine(repos.CloneForInspection(), claimRelative));
        var historyBefore = ClaimHistory(repos.CloneForInspection()).ToArray();

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Equal("refused", result.SoloConductorCompletion?.Decision);
        Assert.Equal("unavailable", Duty(result, dutyId).State);
        Assert.Equal(expectedCause, Duty(result, dutyId).Cause);
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        var inspection = repos.CloneForInspection();
        Assert.Equal(claimBefore, File.ReadAllBytes(Path.Combine(inspection, claimRelative)));
        Assert.Equal(historyBefore, ClaimHistory(inspection));
    }

    private static IReadOnlyList<string> ClaimHistory(string root)
    {
        var history = Path.Combine(root, ClaimCommand.ClaimsDirectory, "history");
        return Directory.Exists(history) ? Directory.GetFiles(history, "*", SearchOption.AllDirectories) : [];
    }

    private static string FindGitExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "git.exe" : "git");
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("git executable was not found in PATH.");
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static SortedDictionary<string, string> CaptureTree(string root) => new(
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal),
        StringComparer.Ordinal);

    private static void RecordKnowledge(string root, string role, string commit, string? note = null)
    {
        using var writer = new StringWriter();
        var arguments = new List<string> { "--execution-unit", Unit, "--role", role, "--commit", commit };
        if (note is not null) arguments.AddRange(["--note", note]);
        arguments.AddRange(["--write", "--format", "json"]);
        var exitCode = AutomationKnowledgeWriteBackRecordCommand.Execute(
            Context(root), arguments.ToArray(), writer);
        Assert.Equal(0, exitCode);
    }

    private static void RecordGuide(string root, string role, string commit)
    {
        using var writer = new StringWriter();
        var exitCode = AutomationGuideReachabilityRecordCommand.Execute(
            Context(root), ["--execution-unit", Unit, "--role", role, "--commit", commit, "--write", "--format", "json"], writer);
        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("domain-missing", "applicability", "packet-domain-missing")]
    [InlineData("domain-invalid", "applicability", "packet-domain-missing")]
    [InlineData("domain-conflict", "applicability", "packet-identity-conflict")]
    [InlineData("source-unit-conflict", "applicability", "packet-identity-conflict")]
    [InlineData("team-mode-invalid-duplicate", "applicability", "team-mode-unavailable")]
    [InlineData("target-repo-missing", "target-repo", "target-repo-unavailable")]
    [InlineData("target-repo-invalid", "target-repo", "target-repo-unavailable")]
    [InlineData("target-repo-conflict", "target-repo", "target-repo-identity-conflict")]
    [InlineData("knowledge-declaration-missing", "knowledge-declaration", "knowledge-declaration-missing")]
    [InlineData("knowledge-declaration-malformed", "knowledge-declaration", "knowledge-declaration-unavailable")]
    [InlineData("guide-declaration-missing", "guide-declaration", "guide-declaration-missing")]
    [InlineData("guide-declaration-malformed", "guide-declaration", "guide-declaration-unavailable")]
    [InlineData("knowledge-duplicate-orchestrator", "knowledge-orchestrator", "duplicate-role-record")]
    [InlineData("knowledge-malformed-orchestrator", "knowledge-orchestrator", "knowledge-record-unavailable")]
    [InlineData("guide-duplicate-architect", "guide-reachability", "duplicate-role-record")]
    [InlineData("guide-malformed-architect", "guide-reachability", "guide-record-unavailable")]
    [InlineData("queue-absent", "closeout-queue", "queue-item-missing")]
    [InlineData("queue-link-missing", "closeout-queue", "linked-pr-missing")]
    [InlineData("queue-repo-conflict", "closeout-queue", "queue-pr-identity-conflict")]
    [InlineData("runs-malformed", "pr-merged", "run-log-unavailable")]
    [InlineData("run-identity-conflict", "pr-merged", "run-identity-conflict")]
    public void OverrideFlaggedTransactionRefusesOneForbiddenCanonicalCondition_G860(
        string condition, string expectedDuty, string expectedCause)
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var baseline = EvaluateCanonicalSnapshot(repos.Writer);
        var baselineEligibility = ClaimLoopEvidenceOverride.Evaluate(baseline);
        Assert.True(baselineEligibility.Eligible, baselineEligibility.Detail);
        Assert.Equal("knowledge-architect", Assert.Single(baselineEligibility.SkippedDuties).Id);

        var packetPath = Path.Combine(repos.Writer, KnowledgeWriteBackRecord.PacketRootRelativePath,
            Unit, "packet.yaml");
        var originalPacket = File.ReadAllText(packetPath);
        switch (condition)
        {
            case "domain-missing":
                File.WriteAllText(packetPath, originalPacket.Replace("  domain: intent-cli\n", "", StringComparison.Ordinal));
                break;
            case "domain-invalid":
                File.WriteAllText(packetPath, originalPacket.Replace("domain: intent-cli", "domain: ../invalid", StringComparison.Ordinal));
                break;
            case "domain-conflict":
                File.WriteAllText(packetPath, "domain: another-domain\n" + originalPacket);
                break;
            case "source-unit-conflict":
                File.WriteAllText(packetPath, originalPacket.Replace("source_execution_unit: G857", "source_execution_unit: OTHER", StringComparison.Ordinal));
                break;
            case "team-mode-invalid-duplicate":
            {
                var mode = TeamModeStore.TryRead(repos.Writer)!;
                Assert.Single(mode.Entries, entry => entry.Domain == Domain && entry.Team == Team);
                TeamModeStore.Write(repos.Writer, mode with
                {
                    Entries = [.. mode.Entries, ModeEntry(TeamMode.Delivery, Team)],
                });
                break;
            }
            case "target-repo-missing":
                File.WriteAllText(packetPath, originalPacket.Replace("  target_repo: J-Tech-Japan/intent-system\n", "", StringComparison.Ordinal));
                break;
            case "target-repo-invalid":
                File.WriteAllText(packetPath, originalPacket.Replace("target_repo: J-Tech-Japan/intent-system", "target_repo: ../unsafe", StringComparison.Ordinal));
                break;
            case "target-repo-conflict":
                File.WriteAllText(packetPath, "target_repo: foreign/repo\n" + originalPacket);
                break;
            case "knowledge-declaration-missing":
                File.WriteAllText(packetPath, PacketWithoutKnowledgeDeclarationYaml);
                break;
            case "knowledge-declaration-malformed":
                File.WriteAllText(packetPath, PacketWithMalformedKnowledgeDeclarationYaml);
                break;
            case "guide-declaration-missing":
                File.WriteAllText(packetPath, PacketWithoutGuideDeclarationYaml);
                break;
            case "guide-declaration-malformed":
                File.WriteAllText(packetPath, PacketWithMalformedGuideDeclarationYaml);
                break;
            case "knowledge-duplicate-orchestrator":
            {
                var path = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                    KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "orchestrator");
                var duplicate = Path.Combine(Path.GetDirectoryName(Path.Combine(repos.Writer,
                    path.Replace('/', Path.DirectorySeparatorChar)))!, "duplicate.json");
                File.Copy(Path.Combine(repos.Writer, path.Replace('/', Path.DirectorySeparatorChar)), duplicate);
                Assert.True(File.Exists(duplicate));
                break;
            }
            case "knowledge-malformed-orchestrator":
            {
                var path = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                    KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "orchestrator");
                File.WriteAllText(Path.Combine(repos.Writer, path.Replace('/', Path.DirectorySeparatorChar)), "{ malformed receipt\n");
                break;
            }
            case "guide-malformed-architect":
            {
                var path = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                    GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect");
                File.WriteAllText(Path.Combine(repos.Writer, path.Replace('/', Path.DirectorySeparatorChar)), "{ malformed receipt\n");
                break;
            }
            case "guide-duplicate-architect":
            {
                var scoped = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                    GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect");
                var legacy = GuideReachabilityRecord.ResolveRelativePath(Unit);
                var scopedPath = Path.Combine(repos.Writer, scoped.Replace('/', Path.DirectorySeparatorChar));
                var legacyPath = Path.Combine(repos.Writer, legacy.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
                File.Copy(scopedPath, legacyPath);
                Assert.True(File.Exists(scopedPath));
                Assert.True(File.Exists(legacyPath));
                Assert.Equal("architect", JsonDocument.Parse(File.ReadAllText(scopedPath)).RootElement.GetProperty("role").GetString());
                Assert.Equal("architect", JsonDocument.Parse(File.ReadAllText(legacyPath)).RootElement.GetProperty("role").GetString());
                break;
            }
            case "queue-absent":
            {
                var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
                File.Delete(path);
                break;
            }
            case "queue-link-missing":
            {
                var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
                var queue = QueueStateSerializer.Deserialize(File.ReadAllText(path));
                File.WriteAllText(path, QueueStateSerializer.Serialize(queue with
                {
                    Items = queue.Items!.Select(item => item.ExecutionUnit == Unit ? item with { LinkedPr = null } : item).ToArray(),
                    UpdatedAt = DateTimeOffset.UtcNow,
                }));
                break;
            }
            case "queue-repo-conflict":
            {
                var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
                var queue = QueueStateSerializer.Deserialize(File.ReadAllText(path));
                File.WriteAllText(path, QueueStateSerializer.Serialize(queue with
                {
                    Items = queue.Items!.Select(item => item.ExecutionUnit == Unit
                        ? item with { LinkedPr = "https://github.com/foreign/repository/pull/99" }
                        : item).ToArray(),
                    UpdatedAt = DateTimeOffset.UtcNow,
                }));
                break;
            }
            case "runs-malformed":
                File.WriteAllText(Path.Combine(repos.Writer, runsRelative.Replace('/', Path.DirectorySeparatorChar)), "{ malformed run line\n");
                break;
            case "run-identity-conflict":
            {
                var path = Path.Combine(repos.Writer, runsRelative.Replace('/', Path.DirectorySeparatorChar));
                File.AppendAllText(path, RunLogSerializer.SerializeLine(new RunEvent
                {
                    Ts = DateTimeOffset.UtcNow,
                    ExecutionUnit = Unit,
                    Event = "pr-merged",
                    By = "contradictory selected run identity",
                    Repo = Repo,
                    Pr = PullRequest,
                    LinkedPr = "https://github.com/foreign/repository/pull/99",
                }) + Environment.NewLine);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown G860 forbidden-condition fixture.");
        }

        repos.PublishIntentChanges(repos.Writer, "main");
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var output = new StringWriter();
        Assert.Equal(1, RunOverrideCli(repos.Reader, output));
        using var result = JsonDocument.Parse(output.ToString());
        var root = result.RootElement;
        Assert.Equal("completion-blocked", root.GetProperty("status").GetString());
        Assert.Equal("refused", root.GetProperty("loop_evidence_override").GetProperty("disposition").GetString());
        Assert.False(root.GetProperty("loop_evidence_override").GetProperty("published").GetBoolean());
        var completion = root.GetProperty("solo_conductor_completion");
        var observed = Assert.Single(completion.GetProperty("duties").EnumerateArray(),
            item => item.GetProperty("id").GetString() == expectedDuty);
        Assert.Equal(expectedCause, observed.GetProperty("cause").GetString());
        Assert.NotEqual("satisfied", observed.GetProperty("state").GetString());
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
        var inspection = repos.CloneForInspection();
        Assert.Empty(ClaimHistory(inspection));
        Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries),
            path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
    }

    [Fact]
    public void OverrideExactHeldTeamWinsOverUnrelatedTeamModeEntry_G860()
    {
        using var repos = new ClaimRepositories();
        PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var now = DateTimeOffset.UtcNow;
        TeamModeStore.Write(repos.Writer, new TeamModeState
        {
            SchemaVersion = TeamModeStore.SchemaVersion,
            Entries =
            [
                new TeamModeEntry
                {
                    Domain = Domain,
                    Team = Team,
                    Mode = TeamMode.SoloConductor,
                    UpdatedAt = now,
                    Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.SoloConductor, At = now }],
                },
                new TeamModeEntry
                {
                    Domain = Domain,
                    Team = "other-team",
                    Mode = TeamMode.Delivery,
                    UpdatedAt = now,
                    Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.Delivery, At = now }],
                },
            ],
        });
        repos.PublishIntentChanges(repos.Writer, "main");
        Assert.Equal(2, TeamModeStore.TryRead(repos.Writer)!.Entries.Count);

        var gate = EvaluateCanonicalSnapshot(repos.Writer);
        Assert.True(gate.IsApplicable);
        Assert.Equal("solo-conductor", gate.Completion?.Applicability.Mode);
        Assert.Equal("team", gate.Completion?.Applicability.ModeSource);
        Assert.True(ClaimLoopEvidenceOverride.Evaluate(gate).Eligible);

        var output = new StringWriter();
        Assert.Equal(0, RunOverrideCli(repos.Reader, output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("released", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("applied", result.RootElement.GetProperty("loop_evidence_override").GetProperty("disposition").GetString());
    }

    [Theory]
    [InlineData("knowledge-orchestrator", "knowledge-orchestrator", "knowledge-record-unavailable")]
    [InlineData("guide-architect", "guide-reachability", "guide-record-unavailable")]
    [InlineData("selected-runs", "pr-merged", "run-log-unavailable")]
    public void OverrideFlaggedTransactionRefusesProvenUnreadableCanonicalEvidence_G860(
        string input, string expectedDuty, string expectedCause)
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var baseline = ClaimLoopEvidenceOverride.Evaluate(EvaluateCanonicalSnapshot(repos.Writer));
        Assert.True(baseline.Eligible, baseline.Detail);
        var target = (input switch
        {
            "knowledge-orchestrator" => RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "orchestrator"),
            "guide-architect" => RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
                GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect"),
            _ => runsRelative,
        }).Replace('/', Path.DirectorySeparatorChar);
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var selectedRunsBefore = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
        var originalFactory = GuardedFileRead.ReadAllTextFactory;
        var reached = 0;
        GuardedFileRead.ReadAllTextFactory = path =>
        {
            if (path.EndsWith(target, StringComparison.Ordinal))
            {
                reached++;
                throw new IOException($"G860_UNREADABLE_{input}_REACHED");
            }
            return (originalFactory ?? File.ReadAllText)(path);
        };
        var output = new StringWriter();
        try
        {
            Assert.Equal(1, RunOverrideCli(repos.Reader, output));
        }
        finally
        {
            GuardedFileRead.ReadAllTextFactory = originalFactory;
        }
        Assert.True(reached > 0, $"the {input} read fault must be reached");
        using var result = JsonDocument.Parse(output.ToString());
        var root = result.RootElement;
        Assert.Equal("completion-blocked", root.GetProperty("status").GetString());
        Assert.Equal("refused", root.GetProperty("loop_evidence_override").GetProperty("disposition").GetString());
        Assert.False(root.GetProperty("loop_evidence_override").GetProperty("published").GetBoolean());
        var duties = root.GetProperty("solo_conductor_completion").GetProperty("duties").EnumerateArray().ToArray();
        if (input == "selected-runs")
        {
            foreach (var id in new[] { "pr-merged", "closeout-recorded" })
                Assert.Equal(expectedCause, Assert.Single(duties, item => item.GetProperty("id").GetString() == id).GetProperty("cause").GetString());
        }
        else
        {
            Assert.Equal(expectedCause, Assert.Single(duties, item => item.GetProperty("id").GetString() == expectedDuty).GetProperty("cause").GetString());
        }
        Assert.Contains($"G860_UNREADABLE_{input}_REACHED", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
        Assert.Equal(selectedRunsBefore, Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}"));
        Assert.Empty(ClaimHistory(repos.CloneForInspection()));
        Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries),
            path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitFlagAcceptsIndependentRepoWithLegacyNumericRunIdentity_G860()
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, packetYaml: null,
            missingDutyIds: ["knowledge-architect"], legacyOnly: true);
        var runsPath = Path.Combine(repos.Writer, runsRelative.Replace('/', Path.DirectorySeparatorChar));
        var now = DateTimeOffset.UtcNow;
        File.WriteAllLines(runsPath, new[] { "pr-merged", "closeout-recorded" }.Select(eventName =>
            RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = now,
                ExecutionUnit = Unit,
                Event = eventName,
                By = "legacy closeout writer",
                Repo = Repo,
                LinkedPr = $"#{PullRequest}",
            })));
        repos.PublishIntentChanges(repos.Writer, "main");
        var gate = EvaluateCanonicalSnapshot(repos.Writer);
        Assert.Equal("satisfied", Duty(gate.Completion!, "pr-merged").State);
        Assert.Equal("satisfied", Duty(gate.Completion!, "closeout-recorded").State);
        Assert.True(ClaimLoopEvidenceOverride.Evaluate(gate).Eligible);

        var output = new StringWriter();
        Assert.Equal(0, RunOverrideCli(repos.Reader, output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("released", result.RootElement.GetProperty("status").GetString());
        var waiver = result.RootElement.GetProperty("loop_evidence_override");
        Assert.True(waiver.GetProperty("published").GetBoolean());
        Assert.Equal(".intent-cli/runs.jsonl", waiver.GetProperty("run_log_path").GetString());
        var committed = result.RootElement.GetProperty("commit").GetString()!;
        var eventLine = RunLogSerializer.DeserializeAll(Git(repos.Bare,
            "show", $"{committed}:{runsRelative}")).Single(item => item.Event == "loop-evidence-override");
        Assert.Equal(Repo, eventLine.Repo);
        Assert.Equal(PullRequest, eventLine.Pr);
        Assert.Equal($"https://github.com/{Repo}/pull/{PullRequest}", eventLine.LinkedPr);
    }

    [Fact]
    public void FlaggedIneligiblePreviewKeepsDirtyCallerAndDeletesOnlyItsCandidateClone_G860()
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var packetPath = Path.Combine(repos.Writer, KnowledgeWriteBackRecord.PacketRootRelativePath, Unit, "packet.yaml");
        File.WriteAllText(packetPath, PacketWithMalformedKnowledgeDeclarationYaml);
        repos.PublishIntentChanges(repos.Writer, "main");
        var staleRoot = PreparePreviewCallerState(repos.Reader);
        var callerBefore = CaptureTree(repos.Reader);
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var runBefore = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
        var cleaned = new List<string>();
        using var warnings = new StringWriter();
        try
        {
            var result = ClaimCommand.RunTransaction(repos.Reader,
                Request(ClaimOperation.Release, write: false) with { OverrideLoopEvidence = true },
                warnings,
                path =>
                {
                    cleaned.Add(path);
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                });
            Assert.Equal("completion-blocked", result.Status);
            Assert.Equal("refused", result.LoopEvidenceOverride?.Disposition);
            Assert.Equal("knowledge-declaration-unavailable", Duty(result.SoloConductorCompletion!, "knowledge-declaration").Cause);
            Assert.Single(cleaned);
            Assert.False(Directory.Exists(cleaned[0]));
            Assert.False(File.Exists(cleaned[0] + ".lease"));
            Assert.Equal(callerBefore.ToArray(), CaptureTree(repos.Reader).ToArray());
            Assert.True(Directory.Exists(staleRoot));
            Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
            Assert.Equal(runBefore, Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}"));
            Assert.Empty(ClaimHistory(repos.CloneForInspection()));
            Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(staleRoot)) Directory.Delete(staleRoot, recursive: true);
        }
    }

    [Fact]
    public void CompletedAndNonSoloOverrideAreRejectedInPreviewAndWrite_G860()
    {
        using (var complete = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(complete);
            var completeGate = EvaluateCanonicalSnapshot(complete.Writer);
            Assert.True(completeGate.IsApplicable);
            Assert.Equal("satisfied", completeGate.Completion?.Decision);
            Assert.Equal("no-missing-receipts", ClaimLoopEvidenceOverride.Evaluate(completeGate).Cause);
            foreach (var write in new[] { false, true })
            {
                var before = Git(complete.Bare, "rev-parse", "refs/heads/main").Trim();
                var output = new StringWriter();
                Assert.Equal(1, RunOverrideCli(complete.Reader, output, write: write));
                using var result = JsonDocument.Parse(output.ToString());
                Assert.Equal("override-not-applicable", result.RootElement.GetProperty("status").GetString());
                Assert.Equal("no-missing-receipts", result.RootElement.GetProperty("loop_evidence_override").GetProperty("cause").GetString());
                Assert.Equal(before, Git(complete.Bare, "rev-parse", "refs/heads/main").Trim());
            }
        }
        using (var nonSolo = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(nonSolo, "knowledge-architect");
            var nonSoloAt = DateTimeOffset.UtcNow;
            TeamModeStore.Write(nonSolo.Writer, new TeamModeState
            {
                SchemaVersion = TeamModeStore.SchemaVersion,
                Entries =
                [
                    new TeamModeEntry
                    {
                        Domain = Domain,
                        Team = Team,
                        Mode = TeamMode.Delivery,
                        UpdatedAt = nonSoloAt,
                        Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.Delivery, At = nonSoloAt }],
                    },
                ],
            });
            nonSolo.PublishIntentChanges(nonSolo.Writer, "main");
            Assert.Equal(TeamMode.Delivery, TeamModeStore.TryRead(nonSolo.Writer)!.Entries.Single().Mode);
            var nonSoloGate = EvaluateCanonicalSnapshot(nonSolo.Writer);
            Assert.False(nonSoloGate.IsApplicable);
            Assert.Null(nonSoloGate.Completion);
            foreach (var write in new[] { false, true })
            {
                var before = Git(nonSolo.Bare, "rev-parse", "refs/heads/main").Trim();
                var output = new StringWriter();
                Assert.Equal(1, RunOverrideCli(nonSolo.Reader, output, write: write));
                using var result = JsonDocument.Parse(output.ToString());
                Assert.Equal("override-not-applicable", result.RootElement.GetProperty("status").GetString());
                Assert.Equal("not-applicable", result.RootElement.GetProperty("loop_evidence_override").GetProperty("cause").GetString());
                Assert.Equal(before, Git(nonSolo.Bare, "rev-parse", "refs/heads/main").Trim());
            }
        }
    }

    [Fact]
    public void OrdinaryDesignReleaseHandoffDoesNotCreateOverrideEvidence_G860()
    {
        using var repos = new ClaimRepositories();
        PrepareActualOverrideSnapshot(repos, packetYaml: null,
            missingDutyIds: ["knowledge-architect"], claimActor: "design");
        var output = new StringWriter();
        Assert.Equal(0, ClaimCommand.ExecuteRelease(Context(repos.Reader),
            ["--scope", $"execution-unit:{Unit}", "--actor", "design", "--team", Team,
             "--reason", "ordinary design handoff", "--write", "--format", "json"], output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("released", result.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("loop_evidence_override", result.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries),
            path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
    }

    [Fact]
    public void OverrideDocumentationAndReleaseHelpStateTheVerificationBoundary_G860()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var relative in new[]
        {
            "docs/en/08-command-reference.md", "docs/ja/08-command-reference.md",
            "docs/en/12-agent-message-orchestration.md", "docs/ja/12-agent-message-orchestration.md",
            "docs/en/1.0-compatibility-ledger.md", "docs/ja/1.0-compatibility-ledger.md",
        })
        {
            var content = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("--override-loop-evidence", content, StringComparison.Ordinal);
            Assert.Contains("canonical_snapshot_oid", content, StringComparison.Ordinal);
            Assert.Contains("child PR head", content, StringComparison.Ordinal);
            Assert.True(content.Contains("external authorization", StringComparison.Ordinal)
                || content.Contains("外部承認", StringComparison.Ordinal), relative);
        }
        var step = Assert.Single(GuideSoloConductorCommand.BuildGuide().Loop, item => item.Number == 10);
        Assert.Contains("does not verify external authorization or reason quality", step.Instruction, StringComparison.Ordinal);
        Assert.Contains("canonical_snapshot_oid", step.Instruction, StringComparison.Ordinal);
        Assert.Contains("non-solo-conductor modes", step.Instruction, StringComparison.Ordinal);
        var help = new StringWriter();
        Assert.Equal(0, ClaimCommand.ExecuteRelease(Context(Path.GetTempPath()), ["--help"], help));
        Assert.Contains("--override-loop-evidence", help.ToString(), StringComparison.Ordinal);
        Assert.Contains("does not verify external authorization or reason quality", help.ToString(), StringComparison.Ordinal);
        Assert.Contains("canonical_snapshot_oid", help.ToString(), StringComparison.Ordinal);
        Assert.Contains("non-execution-unit scopes", help.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("guide claim", help.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FlaggedAppliedAndRefusedMarkdownExposeOverrideDecision_G860()
    {
        using (var applied = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(applied, "knowledge-architect");
            var output = new StringWriter();
            Assert.Equal(0, RunOverrideCli(applied.Reader, output, format: "markdown"));
            var markdown = output.ToString();
            Assert.Contains("# Claim released", markdown, StringComparison.Ordinal);
            Assert.Contains("## Loop-evidence override", markdown, StringComparison.Ordinal);
            Assert.Contains("\"disposition\": \"applied\"", markdown, StringComparison.Ordinal);
            Assert.Contains("\"published\": true", markdown, StringComparison.Ordinal);
            Assert.Contains("canonical_snapshot_oid", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("head_sha", markdown, StringComparison.Ordinal);
        }
        using (var refused = new ClaimRepositories())
        {
            PrepareActualOverrideSnapshot(refused, "knowledge-architect");
            var packetPath = Path.Combine(refused.Writer, KnowledgeWriteBackRecord.PacketRootRelativePath, Unit, "packet.yaml");
            File.WriteAllText(packetPath, PacketWithMalformedKnowledgeDeclarationYaml);
            refused.PublishIntentChanges(refused.Writer, "main");
            var output = new StringWriter();
            Assert.Equal(1, RunOverrideCli(refused.Reader, output, format: "markdown"));
            var markdown = output.ToString();
            Assert.Contains("# Claim completion-blocked", markdown, StringComparison.Ordinal);
            Assert.Contains("## Solo-conductor completion", markdown, StringComparison.Ordinal);
            Assert.Contains("\"cause\": \"knowledge-declaration-unavailable\"", markdown, StringComparison.Ordinal);
            Assert.Contains("## Loop-evidence override", markdown, StringComparison.Ordinal);
            Assert.Contains("\"disposition\": \"refused\"", markdown, StringComparison.Ordinal);
            Assert.Contains("\"published\": false", markdown, StringComparison.Ordinal);
            Assert.Empty(ClaimHistory(refused.CloneForInspection()));
        }
    }

    [Fact]
    public void OverrideAuditCollisionAtExactDestinationPreservesForeignFileAndCanonicalState_G860()
    {
        using var repos = new ClaimRepositories();
        PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var gate = EvaluateCanonicalSnapshot(repos.Writer);
        var eligibility = ClaimLoopEvidenceOverride.Evaluate(gate);
        Assert.True(eligibility.Eligible, eligibility.Detail);
        var scope = $"execution-unit:{Unit}";
        var claimPath = Path.Combine(repos.Writer, ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar));
        var holder = JsonSerializer.Deserialize<ClaimRecord>(File.ReadAllText(claimPath))!;
        var request = new ClaimRequest(ClaimOperation.Release, scope, holder.Actor, holder.Team,
            "fixed audit collision", null, true, "json", ClaimCommand.DefaultMaxAttempts)
        { OverrideLoopEvidence = true };
        var historyPath = $".intent-cli/claims/history/G857/20261010T0000000000000Z-release.json";
        var auditPath = Path.Combine(repos.Writer, ".intent-cli", "loop-evidence-overrides", Unit,
            Path.GetFileName(historyPath));
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        var foreignBytes = System.Text.Encoding.UTF8.GetBytes("foreign immutable evidence\n");
        File.WriteAllBytes(auditPath, foreignBytes);
        var canonicalBefore = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimBefore = File.ReadAllBytes(claimPath);
        var runBefore = File.ReadAllBytes(Path.Combine(repos.Writer, gate.RunLogRelativePath!.Replace('/', Path.DirectorySeparatorChar)));
        var exception = Assert.Throws<IOException>(() => ClaimLoopEvidenceOverride.Write(
            repos.Writer, request, holder, gate, eligibility, historyPath, DateTimeOffset.Parse("2026-10-10T00:00:00Z")));
        Assert.Equal($"Loop-evidence override audit destination already exists: .intent-cli/loop-evidence-overrides/{Unit}/{Path.GetFileName(historyPath)}", exception.Message);
        Assert.Equal(foreignBytes, File.ReadAllBytes(auditPath));
        Assert.Equal(claimBefore, File.ReadAllBytes(claimPath));
        Assert.Equal(runBefore, File.ReadAllBytes(Path.Combine(repos.Writer, gate.RunLogRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(canonicalBefore, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Empty(ClaimHistory(repos.CloneForInspection()));
    }

    [Fact]
    public void OverrideAuditSymlinkDestinationDoesNotMutateExternalSentinel_G860()
    {
        using var repos = new ClaimRepositories();
        PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var gate = EvaluateCanonicalSnapshot(repos.Writer);
        var eligibility = ClaimLoopEvidenceOverride.Evaluate(gate);
        Assert.True(eligibility.Eligible, eligibility.Detail);
        var scope = $"execution-unit:{Unit}";
        var claimPath = Path.Combine(repos.Writer, ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar));
        var holder = JsonSerializer.Deserialize<ClaimRecord>(File.ReadAllText(claimPath))!;
        var request = new ClaimRequest(ClaimOperation.Release, scope, holder.Actor, holder.Team,
            "symlink audit collision", null, true, "json", ClaimCommand.DefaultMaxAttempts)
        { OverrideLoopEvidence = true };
        var historyPath = ".intent-cli/claims/history/G857/20261010T0000000000001Z-release.json";
        var auditPath = Path.Combine(repos.Writer, ".intent-cli", "loop-evidence-overrides", Unit,
            Path.GetFileName(historyPath));
        var sentinel = Path.Combine(Path.GetDirectoryName(repos.Bare)!, "g860-external-audit-sentinel.json");
        var sentinelBytes = System.Text.Encoding.UTF8.GetBytes("external sentinel must stay untouched\n");
        File.WriteAllBytes(sentinel, sentinelBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        File.CreateSymbolicLink(auditPath, sentinel);
        var exception = Assert.Throws<IOException>(() => ClaimLoopEvidenceOverride.Write(
            repos.Writer, request, holder, gate, eligibility, historyPath, DateTimeOffset.Parse("2026-10-10T00:00:01Z")));
        Assert.Equal($"Transaction path follows a link: .intent-cli/loop-evidence-overrides/{Unit}/{Path.GetFileName(historyPath)}", exception.Message);
        Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinel));
        Assert.True(File.Exists(auditPath));
        Assert.Equal("external sentinel must stay untouched\n", File.ReadAllText(sentinel));
    }

    [Theory]
    [InlineData("serialize")]
    [InlineData("append")]
    public void OverrideSerializationAndSelectedLogAppendFaultsAreReachedBeforeAnyRemoteMutation_G860(string fault)
    {
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var beforeHead = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        var claimPath = ClaimCommand.ClaimPath($"execution-unit:{Unit}");
        var claimBefore = Git(repos.Bare, "show", $"refs/heads/main:{claimPath}");
        var runBefore = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
        var original = ClaimLoopEvidenceOverride.WriterOperations;
        var reached = 0;
        ClaimLoopEvidenceOverride.WriterOperations = fault == "serialize"
            ? original with
            {
                SerializeAudit = audit =>
                {
                    reached++;
                    Assert.Equal(Unit, audit.ExecutionUnit);
                    Assert.Equal("implementation", audit.Actor);
                    Assert.Equal("knowledge-architect", Assert.Single(audit.SkippedDuties).Id);
                    throw new InvalidOperationException("G860_AUDIT_SERIALIZE_REACHED");
                },
            }
            : original with
            {
                WriteRunLog = (path, bytes) =>
                {
                    reached++;
                    Assert.EndsWith(runsRelative.Replace('/', Path.DirectorySeparatorChar), path, StringComparison.Ordinal);
                    var logText = System.Text.Encoding.UTF8.GetString(bytes);
                    Assert.StartsWith(runBefore.TrimEnd('\n') + "\n", logText, StringComparison.Ordinal);
                    var eventLine = Assert.Single(RunLogSerializer.DeserializeAll(logText), item => item.Event == "loop-evidence-override");
                    Assert.Equal(Unit, eventLine.ExecutionUnit);
                    Assert.Equal(Repo, eventLine.Repo);
                    Assert.Equal(PullRequest, eventLine.Pr);
                    Assert.False(string.IsNullOrWhiteSpace(eventLine.ResultRef));
                    DirectoryInfo? metadataRoot = new(Path.GetDirectoryName(path)!);
                    while (metadataRoot is not null && metadataRoot.Name != ".intent-cli") metadataRoot = metadataRoot.Parent;
                    Assert.NotNull(metadataRoot);
                    var transactionRoot = metadataRoot!.Parent!;
                    var auditPath = Path.Combine(transactionRoot.FullName, eventLine.ResultRef.Replace('/', Path.DirectorySeparatorChar));
                    Assert.True(File.Exists(auditPath), "the immutable audit must exist before selected-log append is attempted");
                    using var audit = JsonDocument.Parse(File.ReadAllText(auditPath));
                    Assert.Equal(Unit, audit.RootElement.GetProperty("execution_unit").GetString());
                    throw new IOException("G860_RUN_LOG_APPEND_REACHED");
                },
            };
        try
        {
            var output = new StringWriter();
            Assert.Equal(1, RunOverrideCli(repos.Reader, output));
            Assert.Equal(1, reached);
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("error", result.RootElement.GetProperty("status").GetString());
            Assert.False(result.RootElement.GetProperty("push_succeeded").GetBoolean());
            Assert.Equal("transaction-error", result.RootElement.GetProperty("loop_evidence_override").GetProperty("cause").GetString());
            Assert.Contains(fault == "serialize" ? "G860_AUDIT_SERIALIZE_REACHED" : "G860_RUN_LOG_APPEND_REACHED",
                result.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            ClaimLoopEvidenceOverride.WriterOperations = original;
        }
        Assert.Equal(beforeHead, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
        Assert.Equal(claimBefore, Git(repos.Bare, "show", $"refs/heads/main:{claimPath}"));
        Assert.Equal(runBefore, Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}"));
        Assert.Empty(ClaimHistory(repos.CloneForInspection()));
        Assert.DoesNotContain(Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries),
            path => path.StartsWith($".intent-cli/loop-evidence-overrides/{Unit}/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectedOverridePushCannotClaimUnrelatedReleaseAtDifferentRemoteCommit_G860()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repos = new ClaimRepositories();
        var runsRelative = PrepareActualOverrideSnapshot(repos, "knowledge-architect");
        var racer = repos.CreateRacer();
        var hostCommit = Git(racer, "rev-parse", "HEAD").Trim();
        var knowledgePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, "architect");
        RecordKnowledge(racer, "architect", hostCommit);
        Assert.True(File.Exists(Path.Combine(racer, knowledgePath.Replace('/', Path.DirectorySeparatorChar))));
        Git(racer, "add", "--", knowledgePath);
        Git(racer, "-c", "user.name=independent", "-c", "user.email=independent@example.invalid",
            "commit", "--quiet", "-m", "publish the final required receipt");
        var bin = Path.Combine(Path.GetDirectoryName(repos.Reader)!, "g860-independent-release-race-bin");
        Directory.CreateDirectory(bin);
        var wrapper = Path.Combine(bin, "git");
        var reached = Path.Combine(bin, "outer-push-reached");
        var continuePush = Path.Combine(bin, "continue-outer-push");
        var realGit = FindGitExecutable();
        File.WriteAllText(wrapper, $"""
            #!/bin/sh
            set -eu
            PWD=$(pwd)
            REAL={ShellQuote(realGit)}
            BARE={ShellQuote(repos.Bare)}
            REACHED={ShellQuote(reached)}
            CONTINUE={ShellQuote(continuePush)}
            case "$PWD" in */intent-cli-claim-*) ;; *) exec "$REAL" "$@" ;; esac
            origin=$("$REAL" -C "$PWD" remote get-url origin)
            [ "$origin" = "$BARE" ] || exec "$REAL" "$@"
            if [ "$1" = push ] && [ "$2" = origin ] && [ ! -e "$REACHED" ]; then
              printf '%s\n' "$PWD" "$3" > "$REACHED"
              while [ ! -e "$CONTINUE" ]; do sleep 0.05; done
            fi
            exec "$REAL" "$@"
            """);
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var previousPath = Environment.GetEnvironmentVariable("PATH");
        var beforeHead = Git(repos.Bare, "rev-parse", "refs/heads/main").Trim();
        Task<(int exit, string output)>? outerTask = null;
        try
        {
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + previousPath);
            outerTask = Task.Run(() =>
            {
                var output = new StringWriter();
                var exit = RunOverrideCli(repos.Reader, output, maxAttempts: 1);
                return (exit, output: output.ToString());
            });
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(reached) && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert.True(File.Exists(reached), "outer flagged push must reach the bounded wrapper gate");
            var attempted = File.ReadAllLines(reached);
            Assert.StartsWith("intent-cli-claim-", Path.GetFileName(attempted[0]).TrimEnd(Path.DirectorySeparatorChar));
            Assert.Contains(":refs/heads/main", attempted[1], StringComparison.Ordinal);
            Git(racer, "push", "origin", "HEAD:refs/heads/main");
            Assert.NotEqual(beforeHead, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            var independentOutput = new StringWriter();
            Assert.Equal(0, ClaimCommand.ExecuteRelease(Context(racer),
                ["--scope", $"execution-unit:{Unit}", "--actor", "implementation", "--team", Team,
                 "--reason", "ordinary release after independent receipt publication", "--write", "--format", "json"], independentOutput));
            using var independent = JsonDocument.Parse(independentOutput.ToString());
            Assert.Equal("released", independent.RootElement.GetProperty("status").GetString());
            Assert.True(independent.RootElement.GetProperty("push_succeeded").GetBoolean());
            var independentCommit = independent.RootElement.GetProperty("commit").GetString()!;
            Assert.NotEqual(beforeHead, independentCommit);
            Assert.NotEqual(attempted[1].Split(':')[0], independentCommit);
            Assert.Single(ClaimHistory(repos.CloneForInspection()));
            File.WriteAllText(continuePush, "continue\n");
            var outer = await outerTask.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, outer.exit);
            using var result = JsonDocument.Parse(outer.output);
            var root = result.RootElement;
            Assert.NotEqual("released", root.GetProperty("status").GetString());
            Assert.False(root.GetProperty("push_succeeded").GetBoolean());
            Assert.NotEqual(attempted[1].Split(':')[0], Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
            Assert.Equal(Git(repos.Bare, "rev-parse", "refs/heads/main").Trim(), independentCommit);
            var waiver = root.GetProperty("loop_evidence_override");
            Assert.True(waiver.GetProperty("requested").GetBoolean());
            Assert.Equal("refused", waiver.GetProperty("disposition").GetString());
            Assert.False(waiver.GetProperty("published").GetBoolean());
            var tree = Git(repos.Bare, "ls-tree", "-r", "--name-only", "refs/heads/main");
            Assert.DoesNotContain($".intent-cli/loop-evidence-overrides/{Unit}/", tree, StringComparison.Ordinal);
            var runLog = Git(repos.Bare, "show", $"refs/heads/main:{runsRelative}");
            Assert.DoesNotContain("loop-evidence-override", runLog, StringComparison.Ordinal);
            var independentHistory = Assert.Single(ClaimHistory(repos.CloneForInspection()));
            using var independentHistoryJson = JsonDocument.Parse(File.ReadAllText(independentHistory));
            Assert.Equal("ordinary release after independent receipt publication", independentHistoryJson.RootElement.GetProperty("reason").GetString());
        }
        finally
        {
            if (File.Exists(reached) && !File.Exists(continuePush)) File.WriteAllText(continuePush, "unblock\n");
            Environment.SetEnvironmentVariable("PATH", previousPath);
            if (outerTask is not null && !outerTask.IsCompleted)
                await outerTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        Assert.NotEqual(beforeHead, Git(repos.Bare, "rev-parse", "refs/heads/main").Trim());
    }

    private static string PrepareActualOverrideSnapshot(ClaimRepositories repos, params string[] missingDutyIds) =>
        PrepareActualOverrideSnapshot(repos, null, missingDutyIds);

    private static string PrepareActualOverrideSnapshot(
        ClaimRepositories repos,
        string? packetYaml,
        IReadOnlyCollection<string> missingDutyIds,
        bool legacyOnly = false,
        bool unterminatedRunLog = false,
        string claimActor = "implementation")
    {
        repos.PublishSnapshot(queueState: QueueItemState.Completed, runEvents: [], packetYaml: packetYaml);
        Acquire(repos.Writer, actor: claimActor);
        PullCanonicalHead(repos.Writer, repos.CanonicalBranchName);
        var hostCommit = Git(repos.Writer, "rev-parse", "HEAD").Trim();
        if (!missingDutyIds.Contains("knowledge-architect", StringComparer.Ordinal))
            RecordKnowledge(repos.Writer, "architect", hostCommit);
        if (!missingDutyIds.Contains("knowledge-orchestrator", StringComparer.Ordinal))
            RecordKnowledge(repos.Writer, "orchestrator", hostCommit);
        if (!missingDutyIds.Contains("guide-reachability", StringComparer.Ordinal))
            RecordGuide(repos.Writer, "architect", hostCommit);

        using var closeoutOutput = new StringWriter();
        var closeoutExit = CloseoutPrCommand.Execute(Context(repos.Writer),
            ["--pr", PullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
             "--repo", Repo, "--domain", Domain, "--pr-merged", "true", "--repair-runs", "--write", "--format", "json"],
            closeoutOutput);
        Assert.Equal(0, closeoutExit);
        using (var closeoutJson = JsonDocument.Parse(closeoutOutput.ToString()))
            Assert.True(closeoutJson.RootElement.GetProperty("runs_appended").GetBoolean());

        var runsPath = RuntimeScopedStateResolver.GetScopedRunLogPath(repos.Writer, Domain, Repo);
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(runsPath));
        Assert.Contains(events, item => item.Event == "pr-merged" && item.ExecutionUnit == Unit && item.Repo == Repo && item.Pr == PullRequest);
        Assert.Contains(events, item => item.Event == "closeout-recorded" && item.ExecutionUnit == Unit && item.Repo == Repo && item.Pr == PullRequest);
        var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(repos.Writer, Domain, Repo);
        var queue = QueueStateSerializer.Deserialize(File.ReadAllText(queuePath));
        var item = Assert.Single(queue.Items!, row => row.ExecutionUnit == Unit);
        Assert.Equal(QueueItemState.Completed, item.State);
        Assert.Equal($"https://github.com/{Repo}/pull/{PullRequest}", item.LinkedPr);

        if (unterminatedRunLog)
        {
            var writtenBytes = File.ReadAllBytes(runsPath);
            Assert.NotEmpty(writtenBytes);
            Assert.Contains((byte)'\n', writtenBytes);
            Assert.Equal((byte)'\n', writtenBytes[^1]);
            File.WriteAllBytes(runsPath, writtenBytes[..^1]);
            Assert.NotEqual((byte)'\n', File.ReadAllBytes(runsPath)[^1]);
        }

        if (legacyOnly)
        {
            var legacyQueuePath = RuntimeScopedStateResolver.GetLegacyQueueStatePath(repos.Writer);
            var legacyRunPath = RuntimeScopedStateResolver.GetLegacyRunLogPath(repos.Writer);
            Directory.CreateDirectory(Path.GetDirectoryName(legacyQueuePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(legacyRunPath)!);
            File.Move(queuePath, legacyQueuePath);
            File.Move(runsPath, legacyRunPath);
            runsPath = legacyRunPath;
        }

        foreach (var dutyId in new[] { "knowledge-architect", "knowledge-orchestrator" })
        {
            var role = dutyId == "knowledge-architect" ? "architect" : "orchestrator";
            var path = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(KnowledgeWriteBackRecord.RecordRootRelativePath, Unit, role);
            if (missingDutyIds.Contains(dutyId, StringComparer.Ordinal))
                Assert.False(File.Exists(Path.Combine(repos.Writer, path.Replace('/', Path.DirectorySeparatorChar))));
            else
            {
                Assert.True(File.Exists(Path.Combine(repos.Writer, path.Replace('/', Path.DirectorySeparatorChar))));
                var record = JsonSerializer.Deserialize<KnowledgeWriteBackRecord>(File.ReadAllText(Path.Combine(repos.Writer, path.Replace('/', Path.DirectorySeparatorChar))))!;
                Assert.Equal(role, record.Role);
            }
        }
        var guidePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(GuideReachabilityRecord.RecordRootRelativePath, Unit, "architect");
        Assert.Equal(missingDutyIds.Contains("guide-reachability", StringComparer.Ordinal),
            !File.Exists(Path.Combine(repos.Writer, guidePath.Replace('/', Path.DirectorySeparatorChar))));

        repos.PublishIntentChanges(repos.Writer, repos.CanonicalBranchName);
        return Path.GetRelativePath(repos.Writer, runsPath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static int RunOverrideCli(
        string repoRoot,
        TextWriter output,
        string actor = "implementation",
        string team = Team,
        bool write = true,
        string format = "json",
        int? maxAttempts = null,
        bool overrideLoopEvidence = true)
    {
        var arguments = new List<string>
        {
            "--scope", $"execution-unit:{Unit}", "--actor", actor, "--team", team,
            "--reason", "deliberate-fixture-waiver",
        };
        if (overrideLoopEvidence) arguments.Add("--override-loop-evidence");
        if (maxAttempts is not null)
            arguments.AddRange(["--max-attempts", maxAttempts.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (write) arguments.Add("--write");
        arguments.AddRange(["--format", format]);
        return ClaimCommand.ExecuteRelease(Context(repoRoot), arguments.ToArray(), output);
    }

    private static void AssertOwnershipRefusalOutput(string output, string format, string status, string cause)
    {
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            Assert.Equal(status, document.RootElement.GetProperty("status").GetString());
        }
        else
        {
            Assert.Contains($"# Claim {status}", output, StringComparison.Ordinal);
        }

        var evidence = LoopOverrideFromOutput(output, format);
        Assert.True(evidence.GetProperty("requested").GetBoolean());
        Assert.Equal("refused", evidence.GetProperty("disposition").GetString());
        Assert.Equal(cause, evidence.GetProperty("cause").GetString());
        Assert.False(evidence.GetProperty("published").GetBoolean());
    }

    private static void AssertOwnershipRefusalOmitsOverride(string output, string format, string status)
    {
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            Assert.Equal(status, document.RootElement.GetProperty("status").GetString());
            Assert.False(document.RootElement.TryGetProperty("loop_evidence_override", out _));
        }
        else
        {
            Assert.Contains($"# Claim {status}", output, StringComparison.Ordinal);
            Assert.DoesNotContain("## Loop-evidence override", output, StringComparison.Ordinal);
        }
    }

    private static JsonElement LoopOverrideFromOutput(string output, string format)
    {
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.GetProperty("loop_evidence_override").Clone();
        }

        var heading = string.Join(Environment.NewLine, "## Loop-evidence override", string.Empty, "```json", string.Empty);
        var start = output.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, "Markdown must include the loop-evidence override JSON section.");
        start += heading.Length;
        var end = output.IndexOf(Environment.NewLine + "```", start, StringComparison.Ordinal);
        Assert.True(end > start, "Markdown must close the loop-evidence override JSON section.");
        using var markdown = JsonDocument.Parse(output[start..end]);
        return markdown.RootElement.Clone();
    }

    private static string InstallRacerAdvanceOnFirstPush(ClaimRepositories repos, string racer, string suffix)
    {
        var bin = Path.Combine(Path.GetDirectoryName(repos.Reader)!, suffix + "-bin");
        Directory.CreateDirectory(bin);
        var marker = Path.Combine(bin, "racer-push-reached.txt");
        var wrapper = Path.Combine(bin, "git");
        var realGit = FindGitExecutable();
        File.WriteAllText(wrapper,
            "#!/bin/sh\nset -eu\nPWD=$(pwd)\n"
            + $"REAL={ShellQuote(realGit)}\nBARE={ShellQuote(repos.Bare)}\nRACER={ShellQuote(racer)}\nMARKER={ShellQuote(marker)}\n"
            + "case \"$PWD\" in */intent-cli-claim-*) ;; *) exec \"$REAL\" \"$@\" ;; esac\n"
            + "origin=$(\"$REAL\" -C \"$PWD\" remote get-url origin)\n"
            + "[ \"$origin\" = \"$BARE\" ] || exec \"$REAL\" \"$@\"\n"
            + "if [ \"$1\" = push ] && [ \"$2\" = origin ] && [ ! -e \"$MARKER\" ]; then\n"
            + "  : > \"$MARKER\"\n"
            + "  \"$REAL\" -C \"$RACER\" push origin HEAD:refs/heads/main\nfi\n"
            + "exec \"$REAL\" \"$@\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return marker;
    }

    private static CliContext Context(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "caller-default-must-not-be-used",
                ArtifactRoot = ".intent-cli",
            },
        },
    };

    private static void PullCanonicalHead(string root, string branch = "main") => Git(root, "pull", "--ff-only", "origin", branch);

    private static string Git(string workdir, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in args) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {error}");
        return output;
    }

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("g857-claim-release-");

        public ClaimRepositories()
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            Writer = Path.Combine(temp.Path, "writer");
            Reader = Path.Combine(temp.Path, "reader");
            Directory.CreateDirectory(Bare);
            Git(Bare, "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Git(seed, "init", "--quiet", "--initial-branch=main");
            Git(seed, "config", "user.name", "seed");
            Git(seed, "config", "user.email", "seed@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "isolated G857 fixture\n");
            Git(seed, "add", "README.md");
            Git(seed, "commit", "--quiet", "-m", "seed");
            MainSeedHead = Git(seed, "rev-parse", "HEAD").Trim();
            Git(seed, "remote", "add", "origin", Bare);
            Git(seed, "push", "--quiet", "-u", "origin", "main");
            Git(Bare, "symbolic-ref", "HEAD", "refs/heads/main");
            Git(temp.Path, "clone", "--quiet", Bare, Writer);
            Git(temp.Path, "clone", "--quiet", Bare, Reader);
        }

        public string Bare { get; }
        public string Writer { get; }
        public string Reader { get; }
        public string MainSeedHead { get; }
        private string CanonicalBranch { get; set; } = "main";
        public string CanonicalBranchName => CanonicalBranch;

        public void ConfigureMetadataWriteBranch(string branch)
        {
            CanonicalBranch = branch;
            Git(Bare, "branch", branch, "refs/heads/main");
            foreach (var root in new[] { Writer, Reader })
            {
                var configPath = Path.Combine(root, ".intent-cli", "config.toml");
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                File.WriteAllText(configPath,
                    "[project]\n"
                    + "domain = \"intent-cli\"\n"
                    + "artifact_root = \".intent-cli\"\n"
                    + "same_repo_topology = true\n"
                    + $"metadata_write_branch = \"{branch}\"\n");
            }
        }

        public void PublishSnapshot(QueueItemState? queueState, IReadOnlyList<string> runEvents, string? packetYaml = null)
        {
            var root = Writer;
            var now = DateTimeOffset.UtcNow;
            TeamModeStore.Write(root, new TeamModeState
            {
                SchemaVersion = TeamModeStore.SchemaVersion,
                Entries =
                [
                    new TeamModeEntry
                    {
                        Domain = Domain,
                        Team = Team,
                        Mode = TeamMode.SoloConductor,
                        UpdatedAt = now,
                        Transitions = [new TeamModeTransition { From = TeamMode.Delivery, To = TeamMode.SoloConductor, At = now }],
                    },
                ],
            });
            var packetDir = Path.Combine(root, KnowledgeWriteBackRecord.PacketRootRelativePath, Unit);
            Directory.CreateDirectory(packetDir);
            File.WriteAllText(Path.Combine(packetDir, "packet.yaml"), packetYaml ?? PacketYaml);

            if (queueState is not null)
            {
                var queuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, Domain, Repo);
                Directory.CreateDirectory(Path.GetDirectoryName(queuePath)!);
                File.WriteAllText(queuePath, QueueStateSerializer.Serialize(new QueueState
                {
                    SchemaVersion = "1",
                    UpdatedAt = now,
                    Items =
                    [
                        new QueueItem
                        {
                            ExecutionUnit = Unit,
                            Title = "G857 completion gate fixture",
                            State = queueState.Value,
                            Dependencies = [],
                            BlockedBy = [],
                            ClarificationReturnPath = "",
                            PacketPaths = new PacketPaths { Implementation = "implementation.md", ReviewContext = "review-context.md", Yaml = $".intent-cli/issues/{Unit}/packet.yaml" },
                            LinkedIssue = new LinkedIssue { Repo = Repo, Number = 1872, Url = $"https://github.com/{Repo}/issues/1872" },
                            LinkedPr = $"https://github.com/{Repo}/pull/{PullRequest}",
                            WorkerRole = "implementation",
                            ReviewRole = "reviewer",
                            Priority = "normal",
                        },
                    ],
                }));
            }

            if (runEvents.Count > 0)
            {
                var runPath = RuntimeScopedStateResolver.GetScopedRunLogPath(root, Domain, Repo);
                Directory.CreateDirectory(Path.GetDirectoryName(runPath)!);
                File.WriteAllLines(runPath, runEvents.Select(eventName => RunLogSerializer.SerializeLine(new RunEvent
                {
                    Ts = now,
                    ExecutionUnit = Unit,
                    Event = eventName,
                    By = "intent-cli closeout pr",
                    Repo = Repo,
                    Pr = PullRequest,
                })));
            }

            Git(root, "add", "--", ".intent-cli");
            Git(root, "-c", "user.name=test", "-c", "user.email=test@example.invalid",
                "commit", "--quiet", "-m", "publish G857 canonical evidence");
            Git(root, "push", "origin", $"HEAD:refs/heads/{CanonicalBranch}");
        }

        public void WriteCanonicalQueueJson(string json)
        {
            var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(Writer, Domain, Repo);
            File.WriteAllText(path, json);
            PublishIntentChanges(Writer, CanonicalBranch);
        }

        public void WriteRunEvents(IReadOnlyList<string> eventNames, int pr, string? linkedPr = null)
        {
            var now = DateTimeOffset.UtcNow;
            var path = RuntimeScopedStateResolver.GetScopedRunLogPath(Writer, Domain, Repo);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, eventNames.Select(eventName => RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = now,
                ExecutionUnit = Unit,
                Event = eventName,
                By = "intent-cli closeout pr",
                Repo = Repo,
                Pr = pr,
                LinkedPr = linkedPr,
            })));
            PublishIntentChanges(Writer, CanonicalBranch);
        }

        public void PublishIntentChanges(string root, string branch)
        {
            Git(root, "add", "-A", "--", ".intent-cli");
            if (Git(root, "diff", "--cached", "--name-only").Trim().Length == 0) return;
            Git(root, "-c", "user.name=test", "-c", "user.email=test@example.invalid",
                "commit", "--quiet", "-m", "publish G857 fixture mutation");
            Git(root, "push", "origin", $"HEAD:refs/heads/{branch}");
        }

        public string CreateRacer()
        {
            var path = Path.Combine(temp.Path, "racer");
            Git(temp.Path, "clone", "--quiet", Bare, path);
            return path;
        }

        public string InstallRejectingPreReceiveHook()
        {
            var marker = Path.Combine(temp.Path, "unexpected-push.txt");
            var hookPath = Path.Combine(Bare, "hooks", "pre-receive");
            File.WriteAllText(hookPath,
                "#!/bin/sh\nset -eu\n"
                + $": > {ShellQuote(marker)}\n"
                + "printf '%s\\n' 'G857 dry-run fixture rejects unexpected push' >&2\n"
                + "exit 1\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(hookPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            return marker;
        }

        public string CloneForInspection()
        {
            var path = Path.Combine(temp.Path, $"inspect-{Guid.NewGuid():N}");
            Git(temp.Path, "clone", "--quiet", Bare, path);
            return path;
        }

        public string CloneBranchForInspection(string branch)
        {
            var path = Path.Combine(temp.Path, $"inspect-{branch}-{Guid.NewGuid():N}");
            Git(temp.Path, "clone", "--quiet", "--single-branch", "--branch", branch, Bare, path);
            return path;
        }

        public void Dispose() => temp.Dispose();
    }

    private sealed class G857StateDoctorLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> mergedCandidates;

        public G857StateDoctorLister(params int[] pullRequestNumbers)
        {
            mergedCandidates = pullRequestNumbers.Select(number => new GitHubAutomationPrCandidate
            {
                Number = number,
                Url = $"https://github.com/{Repo}/pull/{number}",
                State = "MERGED",
                ClosingIssuesReferences = [new GitHubPrClosingIssueReference { Number = 1872 }],
            }).ToArray();
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => mergedCandidates;
    }

    private const string PacketYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: true
            target_paths:
              - intents/intent-cli/intent-tree/means/08-agent-message-orchestration.md
        guide_reachability:
          no_role_facing_surface: false
          routes:
            - guide_surface: "guide workflow task implementation-loop"
              role: implementation
              target_surface: "claim release completion"
        """;

    private const string PacketWithEmptyOptionalTargetsYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: true
            target_paths: []
        guide_reachability:
          no_role_facing_surface: false
          routes:
            - guide_surface: "guide workflow task implementation-loop"
              role: implementation
              target_surface: "claim release completion"
        """;

    private const string PacketMissingDomainWithMetadataAliasYaml = """
        metadata:
          domain: other
          source_execution_unit: OTHER
          target_repo: other/repo
        implementation_issue_packet:
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: false
        guide_reachability:
          no_role_facing_surface: true
          routes: []
        """;

    private const string PacketWithUnrelatedIdentityAliasesYaml = """
        metadata:
          domain: other
          source_execution_unit: OTHER
          target_repo: other/repo
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: false
        guide_reachability:
          no_role_facing_surface: true
          routes: []
        """;

    private const string PacketIdentityWithMetadataAfterYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        metadata:
          domain: other
          source_execution_unit: OTHER
          target_repo: other/repo
        knowledge_updates:
          intent_tree:
            required: false
        guide_reachability:
          no_role_facing_surface: true
          routes: []
        """;

    private const string ExplicitNoDutyPacketYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: false
        guide_reachability:
          no_role_facing_surface: true
          routes: []
        """;

    private const string CloseoutLearningFalsePacketYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        closeout_learning:
          write_back_required: false
        guide_reachability:
          no_role_facing_surface: true
          routes: []
        """;

    private const string PacketWithoutKnowledgeDeclarationYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        guide_reachability:
          no_role_facing_surface: true
          routes: []
        """;

    private const string PacketWithoutGuideDeclarationYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: false
        """;

    private const string PacketWithMalformedGuideDeclarationYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: false
        guide_reachability: []
        """;

    private const string PacketWithMalformedGuideRoutesYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: false
        guide_reachability:
          no_role_facing_surface: false
          routes: true
        """;

    private const string PacketWithMalformedKnowledgeDeclarationYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        knowledge_updates:
          intent_tree:
            required: definitely
        guide_reachability:
          no_role_facing_surface: true
          routes: []
        """;

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
