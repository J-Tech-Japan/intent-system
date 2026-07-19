using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class AutomationPublishRecoveryCommandTests : IDisposable
{
    public AutomationPublishRecoveryCommandTests()
    {
        AutomationPublishRecoveryCommand.CandidateListerFactory = null;
    }

    public void Dispose()
    {
        AutomationPublishRecoveryCommand.CandidateListerFactory = null;
    }

    [Fact]
    public void Execute_DryRun_ProducesHighConfidenceRepair_ButDoesNotMutateQueueState()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: null, linkedPr: null));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(706, "Closes #703") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());

        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Null(queueAfter.Items[0].LinkedIssue);
        Assert.Null(queueAfter.Items[0].LinkedPr);
    }

    [Fact]
    public void Execute_Write_AppliesRepair_AndUpdatesQueueState()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: null, linkedPr: null));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(706, "Closes #703") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("write", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("applied_count").GetInt32());

        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var item = queueAfter.Items[0];
        Assert.NotNull(item.LinkedIssue);
        Assert.Equal(703, item.LinkedIssue!.Number);
        Assert.Equal("J-Tech-Japan/intent-system", item.LinkedIssue.Repo);
        Assert.Contains("/pull/706", item.LinkedPr!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AmbiguousMultiplePrs_DoesNotWrite_EvenWithWriteFlag()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: null, linkedPr: null));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(706, "Closes #703"), BuildPr(707, "Closes #703") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());

        // Mutation invariant.
        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Null(queueAfter.Items[0].LinkedIssue);
        Assert.Null(queueAfter.Items[0].LinkedPr);
    }

    [Fact]
    public void Execute_G536FieldIncidentFixture_ReportsSamePartialStateGapAsPublishFlow()
    {
        // G536 review repair acceptance criterion: "publish-recovery on the
        // same fixture reports the identical gap set" — not merely its own
        // unrelated "no-closing-pr" vocabulary. Reproduces the exact
        // durable-state shape from the field incident (2026-07-19, G530 as
        // issue #1164) BEFORE any PR exists: queue-state's linked_issue/
        // linked_pr are both null, but publish.yaml already records the
        // created issue and runs.jsonl has no issue-created event. Both
        // `automation publish-recovery` and `issue publish-flow`'s
        // idempotent rerun now consult the SAME
        // PublishDurableArtifactAnalyzer — this test proves that parity
        // directly by running BOTH commands against the identical on-disk
        // fixture and asserting their gap lists are byte-for-byte equal,
        // instead of asserting one command's kind string in isolation.
        using var workspace = new RecoveryWorkspace();
        var title = "G530 Facet-aware context supply";
        workspace.WriteQueueState(BuildQueueState("G530", linkedIssue: null, linkedPr: null));
        workspace.WritePublishArtifact("G530", createdIssueNumber: 1164);
        workspace.WriteGithubBody("G530", BuildCompleteContractBody(title));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(Array.Empty<GitHubAutomationPrCandidate>());

        using var recoveryWriter = new StringWriter();
        var recoveryExit = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            recoveryWriter);

        Assert.Equal(0, recoveryExit);
        using var recoveryDoc = JsonDocument.Parse(recoveryWriter.ToString());
        Assert.Equal(0, recoveryDoc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        var unsafeStops = recoveryDoc.RootElement.GetProperty("unsafe_stops");
        Assert.Equal(1, unsafeStops.GetArrayLength());
        var stop = unsafeStops[0];
        Assert.Equal("G530", stop.GetProperty("execution_unit").GetString());
        var recoveryGaps = stop.GetProperty("durable_artifact_gaps").EnumerateArray()
            .Select(e => e.GetString())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        using var publishFlowWriter = new StringWriter();
        var publishFlowExit = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G530", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            publishFlowWriter);

        Assert.Equal(0, publishFlowExit);
        using var publishFlowDoc = JsonDocument.Parse(publishFlowWriter.ToString());
        Assert.True(publishFlowDoc.RootElement.GetProperty("idempotent").GetBoolean());
        Assert.False(publishFlowDoc.RootElement.GetProperty("durable_state_synced").GetBoolean());
        var wouldRestore = publishFlowDoc.RootElement.GetProperty("would_restore").EnumerateArray()
            .Select(e => e.GetString())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(wouldRestore);
        Assert.Equal(wouldRestore, recoveryGaps);

        // Mutation invariant: dry-run never writes, on either surface.
        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Null(queueAfter.Items[0].LinkedIssue);
        Assert.Null(queueAfter.Items[0].LinkedPr);
    }

    private static string BuildCompleteContractBody(string title)
    {
        return $"""
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
    }

    [Fact]
    public void Execute_AlreadyLinkedItem_NotIncluded()
    {
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 703, Url = "https://github.com/J-Tech-Japan/intent-system/issues/703" };
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: li, linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/706"));
        workspace.WritePublishArtifact("G300", createdIssueNumber: 703);

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(706, "Closes #703") });

        using var writer = new StringWriter();
        AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());
    }

    // --- G315: queue-state-backed linked_pr lane (no publish.yaml needed) ----

    [Fact]
    public void Execute_LinkedIssuePresentNoPr_DryRun_ReportsG315HighConfidenceRepair()
    {
        // SKS-G219-style fixture: queue already has linked_issue=#558,
        // linked_pr=null. PR #559 closes #558. No publish.yaml needed.
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #558") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());

        var repair = doc.RootElement.GetProperty("safe_repairs")[0];
        Assert.Equal(
            PublishRecoveryAnalyzer.RepairTypeLinkedIssueClosingPr,
            repair.GetProperty("type").GetString());
        Assert.Equal(558, repair.GetProperty("linked_issue_number").GetInt32());
        Assert.Equal(559, repair.GetProperty("linked_pr_number").GetInt32());
    }

    [Fact]
    public void Execute_G390_SameRepoMetadata_UniqueLinkedPrRepair_IsRepairReady()
    {
        // G390: same-repo metadata topology + a PR #3639-style fixture where the
        // PR uniquely closes the linked issue and only linked_pr is missing →
        // a high-confidence writeable metadata-branch repair.
        using var workspace = new RecoveryWorkspace(sameRepoMetadata: true);
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #558") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            "same-repo-metadata-linkage-repair-ready",
            doc.RootElement.GetProperty("same_repo_metadata_linkage_classification").GetString());
    }

    [Fact]
    public void Execute_G390_SingleRootTopology_ClassificationIsNotApplicable()
    {
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #558") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            "not-applicable",
            doc.RootElement.GetProperty("same_repo_metadata_linkage_classification").GetString());
    }

    [Fact]
    public void Execute_LinkedIssuePresentNoPr_Write_FillsLinkedPr_PreservesLinkedIssue()
    {
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #558") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var item = queueAfter.Items[0];
        Assert.NotNull(item.LinkedIssue);
        Assert.Equal(558, item.LinkedIssue!.Number);
        Assert.Equal("J-Tech-Japan/intent-system", item.LinkedIssue.Repo);
        // The original linked_issue URL must be preserved verbatim — the
        // G315 lane only fills in linked_pr.
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/558", item.LinkedIssue.Url);
        Assert.Contains("/pull/559", item.LinkedPr!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G390_Write_AppendsLinkedPrRecoveredRunEvent()
    {
        // G390 review Finding 1: a --write recovery must record a durable
        // runs.jsonl event so the linked_pr repair is auditable and
        // closeout-plan can observe it.
        using var workspace = new RecoveryWorkspace(sameRepoMetadata: true);
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #558") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var runsPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        Assert.True(File.Exists(runsPath), "expected runs.jsonl to be appended on --write recovery");
        var runs = File.ReadAllText(runsPath);
        Assert.Contains(AutomationPublishRecoveryCommand.RecoveryRunEventName, runs, StringComparison.Ordinal);
        Assert.Contains("SKS-G219", runs, StringComparison.Ordinal);
        Assert.Contains("559", runs, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G390_DryRun_DoesNotAppendRunEvent()
    {
        using var workspace = new RecoveryWorkspace(sameRepoMetadata: true);
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #558") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var runsPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        Assert.False(File.Exists(runsPath), "dry-run must not append a run event");
    }

    [Fact]
    public void Execute_G391_SameRepoArtifactOnly_WriteThenCloseoutPlanBecomesReady()
    {
        // G391 review follow-up: end-to-end AC evidence for the AIC-style
        // same-repo (intent-metadata) topology. An artifact-only queue item
        // (linked_issue = null, linked_pr = null) plus a publish.yaml whose
        // created issue (#3641) is uniquely closed by PR #3642:
        //   1. `publish-recovery --pr 3642 --write` promotes the artifact
        //      evidence to a high-confidence repair, writes linked_pr (and
        //      linked_issue) to the same-repo metadata root, and appends a run
        //      event; then
        //   2. `review closeout-plan --pr 3642` becomes ready.
        using var workspace = new RecoveryWorkspace(sameRepoMetadata: true);
        workspace.WriteQueueState(BuildQueueState("G391", linkedIssue: null, linkedPr: null));
        workspace.WritePublishArtifact("G391", createdIssueNumber: 3641);
        // Complete child-issue contract so closeout-plan has no packet gaps.
        File.WriteAllText(
            Path.Combine(workspace.RootPath, ".intent-cli", "issues", "G391", "github-body.md"),
            BuildCompleteContractBodyForCloseout());

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(3642, "Closes #3641") });

        using var recoveryWriter = new StringWriter();
        var recoveryExit = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--pr", "3642", "--write", "--format", "json"],
            recoveryWriter);

        Assert.Equal(0, recoveryExit);
        using (var recoveryDoc = JsonDocument.Parse(recoveryWriter.ToString()))
        {
            Assert.Equal(1, recoveryDoc.RootElement.GetProperty("applied_count").GetInt32());
            Assert.Equal(
                "same-repo-metadata-linkage-repair-ready",
                recoveryDoc.RootElement.GetProperty("same_repo_metadata_linkage_classification").GetString());
        }
        // linked_pr (and linked_issue) written to the same-repo metadata root.
        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Contains("/pull/3642", queueAfter.Items[0].LinkedPr!, StringComparison.Ordinal);
        Assert.NotNull(queueAfter.Items[0].LinkedIssue);
        Assert.Equal(3641, queueAfter.Items[0].LinkedIssue!.Number);
        // Durable run event appended (G390).
        Assert.Contains(
            AutomationPublishRecoveryCommand.RecoveryRunEventName,
            File.ReadAllText(Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl")),
            StringComparison.Ordinal);

        // closeout-plan retry now becomes ready.
        using var closeoutWriter = new StringWriter();
        var closeoutExit = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--pr", "3642", "--format", "json"],
            closeoutWriter);

        Assert.Equal(0, closeoutExit);
        using var closeoutDoc = JsonDocument.Parse(closeoutWriter.ToString());
        Assert.True(closeoutDoc.RootElement.GetProperty("ready").GetBoolean());
    }

    private static string BuildCompleteContractBodyForCloseout() =>
        "## Goal\nx\n\n"
        + "## Why This Slice Exists Now\nx\n\n"
        + "## Current Observed State\nx\n\n"
        + "## Accepted Baseline You May Assume\nx\n\n"
        + "## Target Repo / Path / Part\nx\n\n"
        + "## In Scope\n- x\n\n"
        + "## Out Of Scope\n- x\n\n"
        + "## Acceptance Criteria\n- x\n\n"
        + "## Verification\nx\n\n"
        + "## Related Links\n- x\n\n"
        + "## Base Branch Policy\nPolicy: `direct-main`\nExpected PR base branch: `main`\n";

    [Fact]
    public void Execute_LinkedIssuePresentNoPr_NoClosingPr_StaysUnsafe_NoMutation()
    {
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue
        {
            Repo = "J-Tech-Japan/intent-system",
            Number = 558,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/558"
        };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li, linkedPr: null));

        // PR exists but doesn't close #558 — operator must repair the PR
        // body before host metadata can recover.
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "no closing reference here") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());
        Assert.Equal(
            PublishRecoveryAnalyzer.UnsafeNoClosingPrForLinkedIssue,
            doc.RootElement.GetProperty("unsafe_stops")[0].GetProperty("kind").GetString());

        // Mutation invariant — the queue row stays unchanged.
        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Null(queueAfter.Items[0].LinkedPr);
        Assert.NotNull(queueAfter.Items[0].LinkedIssue);
    }

    [Fact]
    public void Execute_RequiresRepoFlag()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G300", linkedIssue: null, linkedPr: null));
        using var writer = new StringWriter();

        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    // --- G522: --domain scoped candidate filtering ----

    [Fact]
    public void Execute_ExplicitDomain_ExcludesCandidateContradictingItAsUnsafeStop()
    {
        // G522 (tightened per PR #1146 review): explicit `--domain` is
        // checked against EACH candidate's own packet-declared domain. A
        // candidate whose packet declares a DIFFERENT domain must not
        // silently join the scan (the "misidentified SKS-G512 for an
        // intent-cli workstream" bug) — it must surface as a structured
        // domain-contradiction unsafe stop instead.
        using var workspace = new RecoveryWorkspace();
        var liIntentCli = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var liOtherDomain = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 512,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/512" };
        workspace.WriteQueueState(BuildQueueStateMulti(
            ("G346", liIntentCli, null),
            ("SKS-G512", liOtherDomain, null)));
        workspace.WritePacketDomain("G346", "intent-cli");
        workspace.WritePacketDomain("SKS-G512", "sekiban-as-a-service");

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "Closes #795"), BuildPr(900, "Closes #512") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal(1, root.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal("G346", root.GetProperty("safe_repairs")[0].GetProperty("execution_unit").GetString());
        var stops = root.GetProperty("unsafe_stops").EnumerateArray().ToArray();
        Assert.Contains(stops, s => s.GetProperty("execution_unit").GetString() == "SKS-G512"
            && s.GetProperty("kind").GetString() == "domain-contradiction");
    }

    [Fact]
    public void Execute_DomainOmitted_DerivesEachCandidateFromItsOwnPacketMetadata()
    {
        // G522: with `--domain` omitted, each candidate's domain is derived
        // from its OWN packet metadata (no cross-candidate scoping is
        // requested) — a candidate with a resolvable domain still
        // participates even alongside a candidate from a different domain,
        // as long as EVERY included candidate's domain was explicitly
        // derived (never silently assumed).
        using var workspace = new RecoveryWorkspace();
        var liIntentCli = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var liOtherDomain = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 512,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/512" };
        workspace.WriteQueueState(BuildQueueStateMulti(
            ("G346", liIntentCli, null),
            ("SKS-G512", liOtherDomain, null)));
        workspace.WritePacketDomain("G346", "intent-cli");
        workspace.WritePacketDomain("SKS-G512", "sekiban-as-a-service");

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "Closes #795"), BuildPr(900, "Closes #512") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("domain").ValueKind);
        Assert.Equal(2, root.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, root.GetProperty("unsafe_stops").GetArrayLength());
    }

    [Fact]
    public void Execute_DomainOmittedAndNoPacketDeclaresADomain_AllCandidatesFailLoud()
    {
        // G522: a candidate with NO derivable domain (no `--domain`, no
        // packet-declared domain) must never silently participate in an
        // unfiltered scan — it becomes a structured domain-underivable
        // unsafe stop instead.
        using var workspace = new RecoveryWorkspace();
        var liIntentCli = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var liOtherDomain = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 512,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/512" };
        workspace.WriteQueueState(BuildQueueStateMulti(
            ("G346", liIntentCli, null),
            ("SKS-G512", liOtherDomain, null)));
        // No packet.yaml written for either unit — no domain to derive.

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "Closes #795"), BuildPr(900, "Closes #512") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("safe_repairs").GetArrayLength());
        var stops = root.GetProperty("unsafe_stops").EnumerateArray().ToArray();
        Assert.Equal(2, stops.Length);
        Assert.All(stops, s => Assert.Equal("domain-underivable", s.GetProperty("kind").GetString()));
    }

    [Fact]
    public void Execute_ScopedToPr_CandidateContradictsExplicitDomain_ProducesSingleDomainStop()
    {
        // G522: the `--pr`-scoped path must ALSO enforce domain isolation —
        // a domain-contradicting candidate must not produce a safe repair
        // just because it was the one PR-scoped candidate.
        using var workspace = new RecoveryWorkspace();
        var li795 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        workspace.WriteQueueState(BuildQueueState("SKS-G219", linkedIssue: li795, linkedPr: null));
        workspace.WritePacketDomain("SKS-G219", "sekiban-as-a-service");

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(559, "Closes #795") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--pr", "559", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(1, root.GetProperty("unsafe_stops").GetArrayLength());
        var stop = root.GetProperty("unsafe_stops")[0];
        Assert.Equal("SKS-G219", stop.GetProperty("execution_unit").GetString());
        Assert.Equal("domain-contradiction", stop.GetProperty("kind").GetString());
    }

    // --- G351: --pr scoped recovery ----

    [Fact]
    public void Execute_G351_ScopedToPr_G346Fixture_ProducesSingleRepair_DryRun()
    {
        // G351 AC fixture: queue item G346 has linked_issue=#795, linked_pr=null.
        // An unrelated G999 item also has missing linked_pr. --pr 796 scopes
        // the result to only G346 — G999 must not appear.
        using var workspace = new RecoveryWorkspace();
        var li795 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var li888 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 888,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/888" };
        var qs = BuildQueueStateMulti(
            ("G346", li795, null),
            ("G999", li888, null));
        workspace.WriteQueueState(qs);

        // PR #796 closes #795; PR #900 closes #888.
        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "Closes #795"), BuildPr(900, "Closes #888") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--pr", "796", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.Equal(796, root.GetProperty("selected_pr").GetInt32());
        Assert.Equal(1, root.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, root.GetProperty("unsafe_stops").GetArrayLength());
        var repair = root.GetProperty("safe_repairs")[0];
        Assert.Equal("G346", repair.GetProperty("execution_unit").GetString());
        Assert.Equal(795, repair.GetProperty("linked_issue_number").GetInt32());
        Assert.Equal(796, repair.GetProperty("linked_pr_number").GetInt32());
    }

    [Fact]
    public void Execute_G351_ScopedToPr_Write_AppliesRepairForSelectedPrOnly()
    {
        // G351 AC: --pr --write applies the repair for the selected PR's
        // linked queue item and does NOT mutate unrelated G999 item.
        using var workspace = new RecoveryWorkspace();
        var li795 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var li888 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 888,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/888" };
        workspace.WriteQueueState(BuildQueueStateMulti(("G346", li795, null), ("G999", li888, null)));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "Closes #795"), BuildPr(900, "Closes #888") });

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--pr", "796", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(1, doc.RootElement.GetProperty("applied_count").GetInt32());

        var queueAfter = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        var g346 = queueAfter.Items.First(i => i.ExecutionUnit == "G346");
        var g999 = queueAfter.Items.First(i => i.ExecutionUnit == "G999");
        // G346 got the repair.
        Assert.Contains("/pull/796", g346.LinkedPr!, StringComparison.Ordinal);
        // G999 was NOT mutated.
        Assert.Null(g999.LinkedPr);
    }

    [Fact]
    public void Execute_G351_ScopedToPr_InvalidPrNumber_ReturnsError()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WriteQueueState(BuildQueueState("G346", linkedIssue: null, linkedPr: null));
        using var writer = new StringWriter();

        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--pr", "not-a-number"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G351_ScopedToPr_NoPrInOpenList_ReturnsEmptyNoOp()
    {
        // G351: when the selected PR is not in the open list (already merged),
        // the scoped result is empty — no repairs and no unsafe stops.
        using var workspace = new RecoveryWorkspace();
        var li = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        workspace.WriteQueueState(BuildQueueState("G346", linkedIssue: li, linkedPr: null));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(900, "Closes #900") }); // PR #796 is NOT in the list

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--pr", "796", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());
    }

    [Fact]
    public void Execute_G351_ScopedToPr_PrHasNoClosingRef_ProducesConciseScopedUnsafeStop()
    {
        // G351 snapshot verification: with --pr, when the selected PR has no
        // closing reference, the output contains exactly one unsafe stop (scoped)
        // and does NOT include stops from unrelated queue items.
        using var workspace = new RecoveryWorkspace();
        var li795 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 795,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/795" };
        var li888 = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 888,
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/888" };
        workspace.WriteQueueState(BuildQueueStateMulti(("G346", li795, null), ("G999", li888, null)));

        AutomationPublishRecoveryCommand.CandidateListerFactory = () => new FakePrLister(
            new[] { BuildPr(796, "PR without closing reference") }); // no Closes #N

        using var writer = new StringWriter();
        var exitCode = AutomationPublishRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--pr", "796", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        // Exactly one concise unsafe stop, not a flood of two stops.
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repairs").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("unsafe_stops").GetArrayLength());
        var stop = doc.RootElement.GetProperty("unsafe_stops")[0];
        // The stop must mention the selected PR number.
        Assert.Contains("796", stop.GetProperty("reason").GetString()!, StringComparison.Ordinal);
    }

    private static string BuildQueueStateMulti(params (string ExecutionUnit, LinkedIssue? LinkedIssue, string? LinkedPr)[] items)
    {
        var queueItems = items.Select(i => new QueueItem
        {
            ExecutionUnit = i.ExecutionUnit,
            Title = $"{i.ExecutionUnit} title",
            State = QueueItemState.Queued,
            Dependencies = Array.Empty<string>(),
            BlockedBy = Array.Empty<string>(),
            ClarificationReturnPath = string.Empty,
            PacketPaths = new PacketPaths
            {
                Yaml = $".intent-cli/issues/{i.ExecutionUnit}/packet.yaml",
                Implementation = $".intent-cli/issues/{i.ExecutionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{i.ExecutionUnit}/review-context.md"
            },
            LinkedIssue = i.LinkedIssue,
            LinkedPr = i.LinkedPr,
            WorkerRole = "Claude",
            ReviewRole = "Codex",
            Priority = "normal"
        }).ToArray();
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 5, 14, 0, 0, 0, TimeSpan.Zero),
            Items = queueItems
        };
        return QueueStateSerializer.Serialize(state);
    }

    private static string BuildQueueState(string executionUnit, LinkedIssue? linkedIssue, string? linkedPr)
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
            Items = new[]
            {
                new QueueItem
                {
                    ExecutionUnit = executionUnit,
                    Title = $"{executionUnit} title",
                    State = QueueItemState.Queued,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md"
                    },
                    LinkedIssue = linkedIssue,
                    LinkedPr = linkedPr,
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = "normal"
                }
            }
        };
        return QueueStateSerializer.Serialize(state);
    }

    private static GitHubAutomationPrCandidate BuildPr(int number, string body) =>
        new()
        {
            Number = number,
            Title = $"PR {number}",
            Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
            Body = body,
            CreatedAt = "2026-05-08T00:00:00Z",
            UpdatedAt = "2026-05-08T00:00:00Z",
            Labels = Array.Empty<GitHubAutomationLabel>(),
            State = "OPEN"
        };

    private sealed class FakePrLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        public FakePrLister(IReadOnlyList<GitHubAutomationPrCandidate> prs) => this.prs = prs;
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class RecoveryWorkspace : IDisposable
    {
        public RecoveryWorkspace(bool sameRepoMetadata = false)
        {
            RootPath = Directory.CreateTempSubdirectory("publish-recovery-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                        // G390: same-repo metadata topology (metadata on a
                        // dedicated branch) so the linkage classification is
                        // exercised at the command level.
                        SameRepoTopology = sameRepoMetadata,
                        MetadataWriteBranch = sameRepoMetadata ? "intent-metadata" : string.Empty,
                    }
                }
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void WriteQueueState(string toml)
        {
            File.WriteAllText(Context.GetQueueStatePath(), toml);
        }

        public void WritePublishArtifact(string executionUnit, int createdIssueNumber)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = executionUnit,
                // G536 review repair: use the same canonical status
                // `IssuePublishFlowCommand.PublishStatusIssueCreated`
                // ("issue-created") that `PublishDurableArtifactAnalyzer`
                // recognizes, so this fixture's publish.yaml is a genuine
                // "present" signal for the shared analyzer, not silently
                // treated as absent.
                PublishStatus = IssuePublishFlowCommand.PublishStatusIssueCreated,
                PacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{executionUnit}/github-body.md",
                CreatedIssueNumber = createdIssueNumber,
                CreatedIssueUrl = $"https://github.com/J-Tech-Japan/intent-system/issues/{createdIssueNumber}",
                PublishedLabelName = "intent-target"
            };
            File.WriteAllText(Path.Combine(dir, "publish.yaml"), IssuePublishArtifactYaml.Serialize(artifact));
        }

        /// <summary>
        /// G522: write a minimal packet.yaml declaring `domain:` for a
        /// candidate execution unit, so <c>automation publish-recovery</c>
        /// can derive that candidate's domain from its own packet metadata.
        /// </summary>
        public void WritePacketDomain(string executionUnit, string domain)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "packet.yaml"), $"domain: {domain}\n");
        }

        /// <summary>
        /// G536 review repair: seeds a complete Child Issue Contract body so
        /// <c>issue publish-flow</c> can also run against this exact
        /// workspace/fixture, for cross-command gap-parity assertions.
        /// </summary>
        public void WriteGithubBody(string executionUnit, string content)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "github-body.md"), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
