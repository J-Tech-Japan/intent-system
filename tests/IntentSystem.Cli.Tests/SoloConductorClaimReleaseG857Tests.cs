using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
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
        RecordKnowledge(repos.Writer, "design", hostCommit);
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
        Assert.Contains("refs/heads/main", Duty(blocked, "knowledge-architect").PublicationStep!, StringComparison.Ordinal);
        Assert.Contains("git -C <canonical-host-checkout> add -- <exact-owned-artifact-paths>", Duty(blocked, "knowledge-architect").PublicationStep!, StringComparison.Ordinal);

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

        var result = Release(repos.Reader);

        Assert.Equal("completion-blocked", result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Equal("implementation", result.Holder);
        Assert.Equal(Team, result.HolderTeam);
        Assert.Equal("refused", result.SoloConductorCompletion?.Decision);
        Assert.Equal(expectedState, Duty(result, dutyId).State);
        foreach (var otherDuty in result.SoloConductorCompletion!.Duties.Where(duty => duty.Id != dutyId))
        {
            var expectedOther = omitted == "queue-absent" && otherDuty.Id is "pr-merged" or "closeout-recorded"
                ? "unavailable"
                : "satisfied";
            Assert.Equal(expectedOther, otherDuty.State);
        }
        var inspection = repos.CloneForInspection();
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
        Assert.Empty(Directory.Exists(Path.Combine(inspection, ClaimCommand.ClaimsDirectory, "history"))
            ? Directory.EnumerateFiles(Path.Combine(inspection, ClaimCommand.ClaimsDirectory, "history"))
            : []);
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
        PublishExactReceipts(repos.Writer, branch);

        var released = Release(repos.Reader);

        Assert.Equal("released", released.Status);
        Assert.Equal($"refs/heads/{branch}", released.TargetRef);
        Assert.Equal($"refs/heads/{branch}", released.SoloConductorCompletion?.CanonicalTargetRef);
        Assert.Equal(Git(repos.Bare, "rev-parse", $"refs/heads/{branch}").Trim(), released.Commit);
        Assert.Equal(Git(repos.Bare, "rev-parse", "refs/heads/main").Trim(), repos.MainSeedHead);
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
            Assert.True(File.Exists(Path.Combine(repos.CloneForInspection(), ClaimCommand.ClaimPath($"execution-unit:{Unit}"))));
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

    private static void Acquire(string repoRoot, string actor = "implementation")
    {
        var acquired = ClaimCommand.RunTransaction(repoRoot, Request(ClaimOperation.Acquire, actor: actor));
        Assert.Equal("acquired", acquired.Status);
    }

    private static ClaimTransactionResult Release(string repoRoot) =>
        ClaimCommand.RunTransaction(repoRoot, Request(ClaimOperation.Release));

    private static ClaimRequest Request(ClaimOperation operation, string actor = "implementation", bool write = true) => new(
        operation,
        $"execution-unit:{Unit}",
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

    private static IReadOnlyList<string> ClaimHistory(string root)
    {
        var history = Path.Combine(root, ClaimCommand.ClaimsDirectory, "history");
        return Directory.Exists(history) ? Directory.GetFiles(history) : [];
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

    private static void RecordKnowledge(string root, string role, string commit)
    {
        using var writer = new StringWriter();
        var exitCode = AutomationKnowledgeWriteBackRecordCommand.Execute(
            Context(root), ["--execution-unit", Unit, "--role", role, "--commit", commit, "--write", "--format", "json"], writer);
        Assert.Equal(0, exitCode);
    }

    private static void RecordGuide(string root, string role, string commit)
    {
        using var writer = new StringWriter();
        var exitCode = AutomationGuideReachabilityRecordCommand.Execute(
            Context(root), ["--execution-unit", Unit, "--role", role, "--commit", commit, "--write", "--format", "json"], writer);
        Assert.Equal(0, exitCode);
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

        public void Dispose() => temp.Dispose();
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

    private const string PacketWithoutKnowledgeDeclarationYaml = """
        implementation_issue_packet:
          domain: intent-cli
          source_execution_unit: G857
          target_repo: J-Tech-Japan/intent-system
        guide_reachability:
          no_role_facing_surface: true
          routes: []
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
