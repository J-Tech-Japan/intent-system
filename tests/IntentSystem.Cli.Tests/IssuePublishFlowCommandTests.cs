using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class IssuePublishFlowCommandTests : IDisposable
{
    public IssuePublishFlowCommandTests()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = null;
        // G536 review repair: default to "no existing issue on GitHub" so
        // the pre-existing create-path tests (which predate this check)
        // are unaffected; tests covering the new duplicate-refusal path
        // override this per-test.
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
    }

    // ─── G290 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G290_PacketYamlTitle_PreferredOverBodyH1()
    {
        // The SKS-G190 case: packet.yaml has a meaningful title, body has no
        // H1. Title resolution must prefer packet.yaml and report
        // `title_source: packet-yaml` with no `title-fallback` warning.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WritePacketYaml("SKS-G190",
            "SKS-G190 Approval-Gated Production Credential Issuance And Rotation Lifecycle Baseline");
        workspace.WriteGithubBody("SKS-G190", BuildContractBodyWithoutH1());

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["SKS-G190", "--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(
            "SKS-G190 Approval-Gated Production Credential Issuance And Rotation Lifecycle Baseline",
            root.GetProperty("title").GetString());
        Assert.Equal("packet-yaml", root.GetProperty("title_source").GetString());
        Assert.Equal(0, root.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void Execute_G290_NoPacketYaml_FallsBackToBodyH1()
    {
        // Older packets without packet.yaml fall through to the body H1.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245", BuildCompleteContractBody("G245 Add intent-cli issue publish-flow command"));

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G245 Add intent-cli issue publish-flow command", root.GetProperty("title").GetString());
        Assert.Equal("github-body-h1", root.GetProperty("title_source").GetString());
        Assert.Equal(0, root.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void Execute_G290_NoPacketTitleAndNoBodyH1_FallsBackUntitledWithWarning()
    {
        // Last-resort: no packet.yaml AND no body H1 → fallback to
        // "<execution-unit> (untitled)" and surface `title-fallback` warning.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G900", BuildContractBodyWithoutH1());

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G900", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G900 (untitled)", root.GetProperty("title").GetString());
        Assert.Equal("fallback-untitled", root.GetProperty("title_source").GetString());
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("title-fallback", warnings);
    }

    [Fact]
    public void Execute_G290_EmptyPacketTitle_FallsBackToBodyH1()
    {
        // packet.yaml present but title is empty/blank → fall through to body
        // H1 rather than treating empty as a real title.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WritePacketYaml("G246", "");
        workspace.WriteGithubBody("G246", BuildCompleteContractBody("G246 Real H1 title"));

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G246", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G246 Real H1 title", root.GetProperty("title").GetString());
        Assert.Equal("github-body-h1", root.GetProperty("title_source").GetString());
    }

    [Fact]
    public void Execute_GivenCompletePacketDryRun_ReportsValidationOk()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245", BuildCompleteContractBody("G245 Add intent-cli issue publish-flow command"));

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("github_body_present").GetBoolean());
        Assert.Equal(0, root.GetProperty("missing_contract_sections").GetArrayLength());
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.False(root.GetProperty("intent_target_applied").GetBoolean());
        Assert.Equal("G245 Add intent-cli issue publish-flow command", root.GetProperty("title").GetString());
    }

    [Fact]
    public void Execute_GivenIncompleteContract_ReportsMissingSectionsAndExitsNonZero()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245",
            """
            # G245 short body

            ## Goal
            x

            ## In Scope
            - x
            """);

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var missing = root.GetProperty("missing_contract_sections");
        Assert.True(missing.GetArrayLength() > 0);
        var names = missing.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Verification", names);
        Assert.Contains("Acceptance Criteria", names);
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.Contains("incomplete", root.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPacketDirectory_ReportsErrorAndExitsNonZero()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G999", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("packet directory not found", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenWriteWithCompletePacket_CreatesIssueAndReportsNextSteps()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245", BuildCompleteContractBody("G245 Add intent-cli issue publish-flow command"));
        workspace.SeedQueueState("G245", "G245 Add intent-cli issue publish-flow command");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/593");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("created").GetBoolean());
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/593", root.GetProperty("issue_url").GetString());
        Assert.False(root.GetProperty("intent_target_applied").GetBoolean());

        var nextSteps = root.GetProperty("next_steps");
        Assert.True(nextSteps.GetArrayLength() >= 2);
        var stepText = string.Join('|', nextSteps.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("automation issue-publish", stepText, StringComparison.Ordinal);

        Assert.Equal("J-Tech-Japan/intent-system", stub.LastRepo);
        Assert.Equal("G245 Add intent-cli issue publish-flow command", stub.LastTitle);
        Assert.EndsWith("github-body.md", stub.LastBodyFile!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenCreatorFailure_ReportsErrorAndExitsNonZero()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245", BuildCompleteContractBody("G245 Add intent-cli issue publish-flow command"));
        // PR #830 review repair: G363's atomic-seed gate requires
        // the execution-unit to be present in queue-state BEFORE
        // `gh issue create` is invoked. Seed it so this test
        // exercises the gh-create-failure path (not the gate).
        workspace.SeedQueueState("G245", "G245 Add intent-cli issue publish-flow command");

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("gh issue create failed", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.False(document.RootElement.GetProperty("created").GetBoolean());
    }

    [Fact]
    public void Execute_MissingExecutionUnit_ReturnsUsageError()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("execution-unit id is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidExecutionUnitId_ReturnsUsageError()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["bad/id", "--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid execution-unit id", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenWriteWithCompletePacket_PatchesQueueStatePublishYamlAndRunsLog()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        workspace.SeedQueueState("G278", "G278 Fix issue publish-flow durable state synchronization");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/659");
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        var fixedNow = new DateTimeOffset(2026, 5, 6, 12, 30, 0, TimeSpan.Zero);
        IssuePublishFlowCommand.UtcNowFactory = () => fixedNow;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("created").GetBoolean());
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.False(root.GetProperty("idempotent").GetBoolean());
        Assert.Equal(659, root.GetProperty("issue_number").GetInt32());
        Assert.True(root.GetProperty("queue_state_patched").GetBoolean());
        Assert.True(root.GetProperty("publish_yaml_patched").GetBoolean());
        Assert.True(root.GetProperty("runs_appended").GetBoolean());

        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var item = Assert.Single(queueState.Items);
        Assert.NotNull(item.LinkedIssue);
        Assert.Equal("J-Tech-Japan/intent-system", item.LinkedIssue!.Repo);
        Assert.Equal(659, item.LinkedIssue.Number);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/659", item.LinkedIssue.Url);
        Assert.Equal(fixedNow, queueState.UpdatedAt);

        var publishArtifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(workspace.PublishYamlPath("G278")));
        Assert.Equal("issue-created", publishArtifact.PublishStatus);
        Assert.Equal(659, publishArtifact.CreatedIssueNumber);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/659", publishArtifact.CreatedIssueUrl);

        var runsContent = File.ReadAllText(workspace.RunsLogPath);
        var runEvents = RunLogSerializer.DeserializeAll(runsContent);
        var runEvent = Assert.Single(runEvents);
        Assert.Equal("issue-created", runEvent.Event);
        Assert.Equal("G278", runEvent.ExecutionUnit);
        Assert.Equal("issue-publish-flow", runEvent.By);
        Assert.Equal("J-Tech-Japan/intent-system#659", runEvent.LinkedIssue);
        Assert.Equal(fixedNow, runEvent.Ts);
    }

    [Fact]
    public void Execute_GivenWriteWithMissingQueueState_RefusesBeforeCreate()
    {
        // PR #830 review repair: G363's atomic-seed gate refuses
        // BEFORE any GitHub mutation when queue-state.json doesn't
        // exist at all. The previous behavior called `gh issue
        // create`, captured the URL, and then surfaced a
        // post-create synchronization error — an orphan GitHub
        // issue with no queue link. The CreatorFactory stub MUST
        // NOT be invoked.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        // intentionally do NOT seed queue-state.json
        Assert.False(File.Exists(workspace.QueueStatePath));

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/659");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.False(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.False(root.GetProperty("queue_state_patched").GetBoolean());
        var error = root.GetProperty("error").GetString()!;
        // New error mentions the atomic-seed gate and the safe
        // recovery surface (queue-seed-from-packet) — no
        // issue_url because no GitHub mutation occurred.
        Assert.Contains("execution_unit `G278`", error, StringComparison.Ordinal);
        Assert.Contains("atomic-seed gate", error, StringComparison.Ordinal);
        Assert.Contains("queue-seed-from-packet", error, StringComparison.Ordinal);
        Assert.Equal(0, stub.CallCount);

        // queue-state stays absent.
        Assert.False(File.Exists(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_GivenWriteWithQueueStateMissingExecutionUnit_RefusesBeforeCreate()
    {
        // PR #830 review repair: G363's atomic-seed gate fails closed
        // BEFORE any GitHub mutation when queue-state lacks the
        // execution-unit. The previous behavior fell through to
        // `gh issue create` and noticed the queue miss afterward —
        // that violated the atomic-seed contract (orphan GitHub
        // issue, no queue link). The CreatorFactory stub is
        // registered defensively but MUST NOT be invoked.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        // queue-state has a different unit, not G278
        workspace.SeedQueueState("G999", "G999 unrelated unit");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/659");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.False(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.False(root.GetProperty("queue_state_patched").GetBoolean());
        var error = root.GetProperty("error").GetString()!;
        // New error mentions the unit, the atomic-seed gate, and the
        // recommended seed command. Backtick wrap matches the
        // operator-facing structured stop format.
        Assert.Contains("execution_unit `G278`", error, StringComparison.Ordinal);
        Assert.Contains("atomic-seed gate", error, StringComparison.Ordinal);
        Assert.Contains("queue-seed-from-packet", error, StringComparison.Ordinal);
        // Defensive: the gate MUST fire BEFORE any GitHub call.
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public void Execute_GivenWriteWithMalformedQueueState_RefusesBeforeCreate_NoCrash()
    {
        // PR #830 review repair (18:52 comment): when
        // `queue-state.json` exists but is MALFORMED (truncated
        // write, hand-edit typo, partial commit, etc.),
        // `QueueStateSerializer.Deserialize` throws `JsonException`.
        // Before that fix the atomic-seed gate only caught
        // `InvalidOperationException` and `IOException`, so the
        // exception bubbled up and crashed `issue publish-flow
        // --write` instead of returning a structured stop.
        //
        // G536 round-4 review repair: the SHARED
        // `PublishDurableArtifactAnalyzer` now reads queue-state.json
        // FIRST and fails closed on malformed input itself
        // (`queue_state_malformed`) rather than silently treating it
        // as absent — a surviving publish.yaml/runs.jsonl signal must
        // never authorize restoration/repair around a queue-state.json
        // this analyzer could not actually read. The CreatorFactory
        // stub MUST NOT be invoked either way.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        // Write deliberately malformed queue-state.json (truncated
        // mid-object — what a crashed writer might leave).
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.QueueStatePath)!);
        File.WriteAllText(workspace.QueueStatePath, "{ \"items\": [ { \"execution_unit\":");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/659");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        // No crash — structured stop instead.
        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("created").GetBoolean());
        var error = root.GetProperty("error").GetString()!;
        Assert.Contains("queue_state_malformed", error, StringComparison.Ordinal);
        Assert.Contains("failed closed", error, StringComparison.Ordinal);
        // Defensive: no GitHub mutation occurred.
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public void Execute_GivenLinkedIssueInQueueStateButPublishYamlMissing_RestoresPublishYamlAndRunsEventThenSynced()
    {
        // G536: queue-state's linked_issue is the only surviving signal —
        // publish.yaml and runs.jsonl are both missing (as in the field
        // incident's post-stash/ff-sync state). The rerun must RESTORE both
        // missing artifacts (never merely report them absent) before
        // claiming durable_state_synced:true, and must never call gh again.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        workspace.SeedQueueStateWithLinkedIssue(
            "G278",
            "G278 Fix issue publish-flow durable state synchronization",
            repo: "J-Tech-Japan/intent-system",
            issueNumber: 659,
            issueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/659");

        Assert.False(File.Exists(workspace.PublishYamlPath("G278")));

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/9999");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, stub.CallCount);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(659, root.GetProperty("issue_number").GetInt32());
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/659", root.GetProperty("issue_url").GetString());
        // queue-state already had linked_issue — nothing to patch there.
        Assert.False(root.GetProperty("queue_state_patched").GetBoolean());
        // publish.yaml and the runs event were MISSING and are restored this run.
        Assert.True(root.GetProperty("publish_yaml_patched").GetBoolean());
        Assert.True(root.GetProperty("runs_appended").GetBoolean());

        // No duplicate gh issue create was made, but both previously-missing
        // artifacts now exist and reflect the canonical (queue-state) issue.
        var publishArtifact = IssuePublishArtifactYaml.Deserialize(
            File.ReadAllText(workspace.PublishYamlPath("G278")));
        Assert.Equal("issue-created", publishArtifact.PublishStatus);
        Assert.Equal(659, publishArtifact.CreatedIssueNumber);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/659", publishArtifact.CreatedIssueUrl);

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        var restoredEvent = Assert.Single(events);
        Assert.Equal("issue-created", restoredEvent.Event);
        Assert.Equal("G278", restoredEvent.ExecutionUnit);
    }

    [Fact]
    public void Execute_GivenAllThreeArtifactsAlreadyRestored_SecondRerunIsPureNoOp()
    {
        // G536: after a restoring rerun (previous test), a THIRD run must
        // find all three artifacts already correct and make no further
        // writes at all — proving restoration itself is idempotent.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        workspace.SeedQueueStateWithLinkedIssue(
            "G278",
            "G278 Fix issue publish-flow durable state synchronization",
            repo: "J-Tech-Japan/intent-system",
            issueNumber: 659,
            issueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/659");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/9999");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using (var restoringRun = new StringWriter())
        {
            Assert.Equal(0, IssuePublishFlowCommand.Execute(
                workspace.Context,
                ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
                restoringRun));
        }

        var runsAfterRestore = File.ReadAllText(workspace.RunsLogPath);

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, stub.CallCount);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.False(root.GetProperty("queue_state_patched").GetBoolean());
        Assert.False(root.GetProperty("publish_yaml_patched").GetBoolean());
        Assert.False(root.GetProperty("runs_appended").GetBoolean());

        // No duplicate issue-created event was appended.
        Assert.Equal(runsAfterRestore, File.ReadAllText(workspace.RunsLogPath));
        Assert.Single(RunLogSerializer.DeserializeAll(runsAfterRestore));
    }

    [Fact]
    public void Execute_FieldIncidentRegression_G530Issue1164_RestoresLinkageAndRunsEventAfterDurableStateReset()
    {
        // G536 field incident (2026-07-19, publishing G530 as issue #1164):
        // host main advanced concurrently after issue creation, forcing a
        // stash + ff-sync mid-publish. The synced state that resulted had
        // publish.yaml intact but queue-state's linked_issue AND the
        // runs.jsonl issue-created event both reverted to their
        // pre-publish (absent) state. The pre-G536 idempotent rerun
        // reported durable_state_synced:true anyway (a false positive) —
        // this fixture reproduces the exact sequence and proves the rerun
        // now restores BOTH missing artifacts from publish.yaml (the
        // surviving signal) before ever reporting synced.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G530 Facet-aware context supply";
        workspace.WriteGithubBody("G530", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G530", title);

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/1164");
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        var createdAt = new DateTimeOffset(2026, 7, 19, 3, 0, 0, TimeSpan.Zero);
        IssuePublishFlowCommand.UtcNowFactory = () => createdAt;

        // Step 1: normal publish — creates the issue, all three artifacts sync.
        using (var createRun = new StringWriter())
        {
            Assert.Equal(0, IssuePublishFlowCommand.Execute(
                workspace.Context,
                ["G530", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
                createRun));
        }

        Assert.Equal(1, stub.CallCount);
        Assert.True(File.Exists(workspace.PublishYamlPath("G530")));

        // Step 2: simulate the host-main stash+ff-sync — queue-state and
        // runs.jsonl revert to their pre-publish snapshot (as if the
        // concurrent main advance's older versions won the ff-sync), but
        // publish.yaml (written earlier, not part of the reverted files in
        // the field sequence) survives untouched.
        workspace.SeedQueueState("G530", title);
        File.Delete(workspace.RunsLogPath);
        Assert.False(File.Exists(workspace.RunsLogPath));

        // Step 3: rerun. Must restore queue-state's linked_issue and the
        // runs.jsonl issue-created event from publish.yaml's surviving
        // record, make NO new gh call, and only THEN report synced.
        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G530", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, stub.CallCount); // still just the one gh call from step 1

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(1164, root.GetProperty("issue_number").GetInt32());
        Assert.True(root.GetProperty("queue_state_patched").GetBoolean());
        Assert.False(root.GetProperty("publish_yaml_patched").GetBoolean()); // already present, untouched
        Assert.True(root.GetProperty("runs_appended").GetBoolean());

        var restoredQueueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var restoredItem = Assert.Single(restoredQueueState.Items);
        Assert.NotNull(restoredItem.LinkedIssue);
        Assert.Equal("J-Tech-Japan/intent-system", restoredItem.LinkedIssue!.Repo);
        Assert.Equal(1164, restoredItem.LinkedIssue.Number);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/1164", restoredItem.LinkedIssue.Url);

        var restoredEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        var restoredEvent = Assert.Single(restoredEvents);
        Assert.Equal("issue-created", restoredEvent.Event);
        Assert.Equal("G530", restoredEvent.ExecutionUnit);
    }

    [Fact]
    public void Execute_FieldIncidentRegression_G531Issue1166_RunsOnlySignal_RestoresWithoutDuplicateIssueCreate()
    {
        // G536 field incident (2026-07-19, publishing G531 as issue #1166,
        // same concurrent host-main stash+ff-sync sequence as G530/#1164):
        // this time BOTH queue-state's linked_issue AND publish.yaml's
        // issue-created record were lost — runs.jsonl's issue-created event
        // was the ONLY surviving signal. The pre-G536 idempotent-rerun
        // trigger only ever checked publish.yaml / queue-state and
        // completely ignored runs.jsonl as an identity source, so this
        // exact shape fell through to the normal create path and could
        // have produced a SECOND GitHub issue for the same execution unit
        // — the single most severe defect this review repair fixes.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G531 Add read-only intent facet-check scaffold";
        workspace.WriteGithubBody("G531", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G531", title);
        Assert.False(File.Exists(workspace.PublishYamlPath("G531")));

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunsLogPath)!);
        var survivingEvent = new RunEvent
        {
            Ts = new DateTimeOffset(2026, 7, 19, 2, 0, 0, TimeSpan.Zero),
            ExecutionUnit = "G531",
            Event = "issue-created",
            By = "issue-publish-flow",
            LinkedIssue = "J-Tech-Japan/intent-system#1166",
            Reason = "https://github.com/J-Tech-Japan/intent-system/issues/1166",
        };
        File.WriteAllText(workspace.RunsLogPath, RunLogSerializer.SerializeLine(survivingEvent) + "\n");

        // Never actually invoked if the fix holds — throws immediately if
        // the rerun still falls through to a second `gh issue create`.
        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G531", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(1166, root.GetProperty("issue_number").GetInt32());
        Assert.True(root.GetProperty("queue_state_patched").GetBoolean());
        Assert.True(root.GetProperty("publish_yaml_patched").GetBoolean());
        Assert.False(root.GetProperty("runs_appended").GetBoolean()); // event already present, untouched

        var restoredQueueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var restoredItem = Assert.Single(restoredQueueState.Items);
        Assert.NotNull(restoredItem.LinkedIssue);
        Assert.Equal(1166, restoredItem.LinkedIssue!.Number);
        Assert.Equal("J-Tech-Japan/intent-system", restoredItem.LinkedIssue.Repo);

        var restoredArtifact = IssuePublishArtifactYaml.Deserialize(
            File.ReadAllText(workspace.PublishYamlPath("G531")));
        Assert.Equal("issue-created", restoredArtifact.PublishStatus);
        Assert.Equal(1166, restoredArtifact.CreatedIssueNumber);

        // No duplicate issue-created event was appended — still exactly one.
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath)));
    }

    [Fact]
    public void Execute_DryRun_RunsOnlySignal_ReportsWouldRestoreWithoutMutatingAnyFile()
    {
        // G536: dry-run must PLAN only — report the would_restore gap list
        // for the same runs-only-signal shape above, without writing
        // queue-state.json, creating publish.yaml, or touching runs.jsonl.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G531 Add read-only intent facet-check scaffold";
        workspace.WriteGithubBody("G531", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G531", title);

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunsLogPath)!);
        var survivingEvent = new RunEvent
        {
            Ts = new DateTimeOffset(2026, 7, 19, 2, 0, 0, TimeSpan.Zero),
            ExecutionUnit = "G531",
            Event = "issue-created",
            By = "issue-publish-flow",
            LinkedIssue = "J-Tech-Japan/intent-system#1166",
            Reason = "https://github.com/J-Tech-Japan/intent-system/issues/1166",
        };
        File.WriteAllText(workspace.RunsLogPath, RunLogSerializer.SerializeLine(survivingEvent) + "\n");

        var queueStateBefore = File.ReadAllText(workspace.QueueStatePath);
        var runsLogBefore = File.ReadAllText(workspace.RunsLogPath);

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G531", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.False(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(1166, root.GetProperty("issue_number").GetInt32());
        var wouldRestore = root.GetProperty("would_restore").EnumerateArray()
            .Select(e => e.GetString())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "publish_yaml_missing", "queue_linked_issue_missing" },
            wouldRestore);

        // Byte-for-byte: dry-run never writes.
        Assert.Equal(queueStateBefore, File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(runsLogBefore, File.ReadAllText(workspace.RunsLogPath));
        Assert.False(File.Exists(workspace.PublishYamlPath("G531")));
    }

    [Fact]
    public void Execute_GivenConflictingRunsEvents_FailsClosedDistinctFromMissing()
    {
        // G536: two issue-created events for the same execution unit naming
        // DIFFERENT issue numbers is a genuine data contradiction — must
        // fail closed as "conflicting", never be silently collapsed into
        // "missing" (which would invite an unsafe overwrite) or "present"
        // (which would silently pick one side).
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G278", title);

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunsLogPath)!);
        var eventA = new RunEvent
        {
            Ts = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
            ExecutionUnit = "G278",
            Event = "issue-created",
            By = "issue-publish-flow",
            LinkedIssue = "J-Tech-Japan/intent-system#659",
        };
        var eventB = eventA with { LinkedIssue = "J-Tech-Japan/intent-system#700", Ts = eventA.Ts.AddMinutes(5) };
        File.WriteAllText(
            workspace.RunsLogPath,
            RunLogSerializer.SerializeLine(eventA) + "\n" + RunLogSerializer.SerializeLine(eventB) + "\n");

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("runs_event_conflicting", error, StringComparison.Ordinal);
        Assert.Contains("659", error, StringComparison.Ordinal);
        Assert.Contains("700", error, StringComparison.Ordinal);

        // No mutation on refusal.
        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Null(queueStateAfter.Items.Single().LinkedIssue);
        Assert.False(File.Exists(workspace.PublishYamlPath("G278")));
        Assert.Equal(2, RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath)).Count);
    }

    [Fact]
    public void Execute_GivenDuplicateIdenticalRunsEvents_TreatedAsPresentNotConflicting()
    {
        // G536: two issue-created events for the same execution unit naming
        // the SAME issue number is a harmless duplicate (e.g. a retried
        // append) — must be treated as present/fine, not conflicting.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G278", title);

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunsLogPath)!);
        var eventA = new RunEvent
        {
            Ts = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
            ExecutionUnit = "G278",
            Event = "issue-created",
            By = "issue-publish-flow",
            LinkedIssue = "J-Tech-Japan/intent-system#659",
            Reason = "https://github.com/J-Tech-Japan/intent-system/issues/659",
        };
        var eventADuplicate = eventA with { Ts = eventA.Ts.AddMinutes(5) };
        File.WriteAllText(
            workspace.RunsLogPath,
            RunLogSerializer.SerializeLine(eventA) + "\n" + RunLogSerializer.SerializeLine(eventADuplicate) + "\n");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/9999");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, stub.CallCount);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(659, root.GetProperty("issue_number").GetInt32());
        Assert.True(root.GetProperty("queue_state_patched").GetBoolean());
        Assert.True(root.GetProperty("publish_yaml_patched").GetBoolean());
        Assert.False(root.GetProperty("runs_appended").GetBoolean()); // already present, no third event added

        Assert.Equal(2, RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath)).Count);
    }

    [Fact]
    public void Execute_GivenMalformedPublishYaml_FailsClosedNotTreatedAsMissing()
    {
        // G536: an unparseable publish.yaml must fail closed distinctly
        // ("malformed") rather than being silently treated as "missing" —
        // which would invite an unsafe overwrite of a file that might carry
        // a real, just-corrupted issue record.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G278", title);

        var publishYamlPath = workspace.PublishYamlPath("G278");
        Directory.CreateDirectory(Path.GetDirectoryName(publishYamlPath)!);
        File.WriteAllText(publishYamlPath, "{{{ not valid yaml at all :::\n\tthis is garbage");

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("publish_yaml_malformed", error, StringComparison.Ordinal);

        // No mutation on refusal — the malformed file is left for manual inspection.
        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Null(queueStateAfter.Items.Single().LinkedIssue);
        Assert.Equal("{{{ not valid yaml at all :::\n\tthis is garbage", File.ReadAllText(publishYamlPath));
    }

    [Fact]
    public void Execute_GivenUnparseableIssueCreatedRunsEvent_FailsClosedNotTreatedAsMissing()
    {
        // G536: an issue-created run event that carries neither a
        // recognizable `linked_issue` (repo#number) nor a `reason` issue
        // URL is malformed data, not an absent signal — must fail closed
        // rather than being silently skipped as if the event never
        // existed (which is exactly the gap that risked a duplicate
        // `gh issue create` in the field incidents).
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G278", title);

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunsLogPath)!);
        var unparseableEvent = new RunEvent
        {
            Ts = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
            ExecutionUnit = "G278",
            Event = "issue-created",
            By = "issue-publish-flow",
            LinkedIssue = null,
            Reason = "created without a recorded issue reference",
        };
        File.WriteAllText(workspace.RunsLogPath, RunLogSerializer.SerializeLine(unparseableEvent) + "\n");

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("runs_malformed", error, StringComparison.Ordinal);

        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Null(queueStateAfter.Items.Single().LinkedIssue);
        Assert.False(File.Exists(workspace.PublishYamlPath("G278")));
    }

    [Fact]
    public void Execute_GivenQueueLinkedIssueFromDifferentRepo_FailsClosedAsCrossRepoContradiction()
    {
        // G536 round-4 review repair: queue-state's linked_issue naming a
        // DIFFERENT repo than the confirmed `--repo` target — even with a
        // matching issue number — is a genuine identity contradiction, not
        // a same-number coincidence to silently accept.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueStateWithLinkedIssue(
            "G278", title,
            repo: "some-other-org/unrelated-repo",
            issueNumber: 659,
            issueUrl: "https://github.com/some-other-org/unrelated-repo/issues/659");

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("cross_artifact_contradiction", error, StringComparison.Ordinal);

        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("some-other-org/unrelated-repo", queueStateAfter.Items.Single().LinkedIssue!.Repo);
    }

    [Fact]
    public void Execute_GivenPublishYamlForDifferentExecutionUnit_FailsClosedNotTreatedAsMissing()
    {
        // G536 round-4 review repair: a publish.yaml whose own
        // execution_unit field does not match the unit this path is
        // scoped to is corrupted data (e.g. copied from another unit's
        // packet) — must fail closed rather than being silently accepted
        // because the issue number happened to look plausible.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G278", title);

        var publishYamlPath = workspace.PublishYamlPath("G278");
        Directory.CreateDirectory(Path.GetDirectoryName(publishYamlPath)!);
        File.WriteAllText(
            publishYamlPath,
            IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
            {
                ExecutionUnit = "G999",
                PublishStatus = "issue-created",
                PacketPath = ".intent-cli/issues/G999",
                IssueBodyPath = ".intent-cli/issues/G999/github-body.md",
                CreatedIssueNumber = 659,
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/659",
                PublishedLabelName = null,
            }));

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("publish_yaml_malformed", error, StringComparison.Ordinal);
        Assert.Contains("G999", error, StringComparison.Ordinal);

        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Null(queueStateAfter.Items.Single().LinkedIssue);
    }

    [Fact]
    public void Execute_GivenRunsEventFromDifferentRepo_FailsClosedInsteadOfCollapsingToOneIdentity()
    {
        // G536 round-4 review repair: a runs.jsonl issue-created event
        // whose linked_issue names a DIFFERENT repo than another present
        // signal must be a genuine conflict, never silently collapsed into
        // one "identical" identity just because the issue NUMBER matches.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueStateWithLinkedIssue(
            "G278", title,
            repo: "J-Tech-Japan/intent-system",
            issueNumber: 659,
            issueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/659");

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunsLogPath)!);
        var crossRepoEvent = new RunEvent
        {
            Ts = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
            ExecutionUnit = "G278",
            Event = "issue-created",
            By = "issue-publish-flow",
            LinkedIssue = "some-other-org/unrelated-repo#659",
        };
        File.WriteAllText(workspace.RunsLogPath, RunLogSerializer.SerializeLine(crossRepoEvent) + "\n");

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("cross_artifact_contradiction", error, StringComparison.Ordinal);

        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(659, queueStateAfter.Items.Single().LinkedIssue!.Number);
    }

    [Fact]
    public void Execute_GivenNoLocalSignalButUniqueGitHubIssueAlreadyExists_RestoresWithoutCreatingDuplicate()
    {
        // G536 round-4 review repair: when the analyzer finds NO identity
        // across all three local artifacts (e.g. every one was reset/lost
        // but the GitHub issue itself was never re-created), falling
        // straight through to `gh issue create` would produce a genuine
        // duplicate. An exact title+body match on GitHub must instead feed
        // that confirmed identity into the same restoration path — never a
        // `gh issue create` call — restoring all three local artifacts.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        var body = BuildCompleteContractBody(title);
        workspace.WriteGithubBody("G278", body);
        workspace.SeedQueueState("G278", title);
        Assert.False(File.Exists(workspace.PublishYamlPath("G278")));
        Assert.False(File.Exists(workspace.RunsLogPath));

        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new StubExistingIssueChecker(
            GitHubExistingIssueClassification.Unique,
            issueNumber: 8080,
            issueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/8080");
        // Never invoked if the fix holds — this path must restore, not create.
        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(8080, root.GetProperty("issue_number").GetInt32());

        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(8080, queueStateAfter.Items.Single().LinkedIssue!.Number);
        var restoredArtifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(workspace.PublishYamlPath("G278")));
        Assert.Equal(8080, restoredArtifact.CreatedIssueNumber);
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath)));
    }

    [Fact]
    public void Execute_GivenNoLocalSignalAndMultipleMatchingGitHubIssues_FailsClosedNonMutating()
    {
        // G536 round-4 review repair: an ambiguous GitHub-side match (more
        // than one issue with the exact expected title and body) must never
        // be resolved automatically — refuse, never create, never guess.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G278", title);

        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new StubExistingIssueChecker(
            GitHubExistingIssueClassification.Multiple);
        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("multiple issues", error, StringComparison.Ordinal);

        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Null(queueStateAfter.Items.Single().LinkedIssue);
        Assert.False(File.Exists(workspace.PublishYamlPath("G278")));
    }

    [Fact]
    public void Execute_GivenNoLocalSignalAndNoGitHubIssue_CreatesNormally()
    {
        // Counterpart to the tests above: when the GitHub corroboration
        // check genuinely finds nothing, the normal create path proceeds
        // exactly as before G536.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G278", title);

        var checker = new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/659");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(1, stub.CallCount);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("created").GetBoolean());
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
    }

    [Fact]
    public void Execute_GivenRestorationImpossible_FailsLoudNamingMissingArtifactAndRecoveryCommand()
    {
        // G536 acceptance criterion: "with restoration impossible ... exits
        // non-zero naming exactly the missing artifacts and the recovery
        // command." Simulate: publish.yaml survives (the signal) but
        // queue-state.json exists with NO item for this execution unit at
        // all (so TryPatchQueueStateLinkedIssue cannot find anywhere to
        // restore the linked_issue onto).
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueState("G278", title);

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/659");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using (var createRun = new StringWriter())
        {
            Assert.Equal(0, IssuePublishFlowCommand.Execute(
                workspace.Context,
                ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
                createRun));
        }

        // Simulate a queue-state.json that no longer carries ANY item for
        // this execution unit (e.g. a bad hand-edit, or a sync that lost
        // the whole entry) — restoration has nowhere to write onto.
        File.WriteAllText(
            workspace.QueueStatePath,
            QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 7, 19, 3, 0, 0, TimeSpan.Zero),
                Items = Array.Empty<QueueItem>(),
            }));
        File.Delete(workspace.RunsLogPath);

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("durable_state_synced").GetBoolean());
        var error = root.GetProperty("error").GetString();
        Assert.Contains("queue-state.json has no item with execution_unit", error, StringComparison.Ordinal);
        Assert.Contains("issue publish-flow G278 --repo", error, StringComparison.Ordinal);
        Assert.Contains("automation publish-recovery", error, StringComparison.Ordinal);

        // The runs.jsonl event WAS restorable (publish.yaml gave canonical
        // identity) and independent artifact verification is not
        // all-or-nothing — that one still gets fixed even though
        // queue-state could not be.
        Assert.True(root.GetProperty("runs_appended").GetBoolean());
    }

    [Fact]
    public void Execute_GivenContradictoryDurableState_FailsLoudWithoutPickingASide()
    {
        // G536: publish.yaml and queue-state.json disagree on the issue
        // number for the same execution unit — a genuine data
        // contradiction, not a "missing artifact." Must refuse rather than
        // silently trusting either side, and must not mutate anything.
        using var workspace = new IssuePublishFlowWorkspace();
        var title = "G278 Fix issue publish-flow durable state synchronization";
        workspace.WriteGithubBody("G278", BuildCompleteContractBody(title));
        workspace.SeedQueueStateWithLinkedIssue(
            "G278", title,
            repo: "J-Tech-Japan/intent-system",
            issueNumber: 700,
            issueUrl: "https://github.com/J-Tech-Japan/intent-system/issues/700");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/9999");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        // Hand-seed a publish.yaml recording a DIFFERENT issue number than
        // queue-state's linked_issue — simulating a corrupted/foreign sync.
        var publishYamlPath = workspace.PublishYamlPath("G278");
        Directory.CreateDirectory(Path.GetDirectoryName(publishYamlPath)!);
        File.WriteAllText(
            publishYamlPath,
            IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
            {
                ExecutionUnit = "G278",
                PublishStatus = "issue-created",
                PacketPath = ".intent-cli/issues/G278",
                IssueBodyPath = ".intent-cli/issues/G278/github-body.md",
                CreatedIssueNumber = 659,
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/659",
                PublishedLabelName = null,
            }));

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, stub.CallCount);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("durable_state_synced").GetBoolean());
        var error = root.GetProperty("error").GetString();
        Assert.Contains("contradiction", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("659", error, StringComparison.Ordinal);
        Assert.Contains("700", error, StringComparison.Ordinal);

        // Neither artifact was mutated by the refusal.
        var queueStateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(700, queueStateAfter.Items.Single().LinkedIssue!.Number);
        var publishArtifactAfter = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishYamlPath));
        Assert.Equal(659, publishArtifactAfter.CreatedIssueNumber);
    }

    [Fact]
    public void Execute_GivenWriteRerunAfterIssueCreated_IsIdempotentAndDoesNotCallGitHub()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        workspace.SeedQueueState("G278", "G278 Fix issue publish-flow durable state synchronization");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/659");
        IssuePublishFlowCommand.CreatorFactory = () => stub;
        var fixedNow = new DateTimeOffset(2026, 5, 6, 12, 30, 0, TimeSpan.Zero);
        IssuePublishFlowCommand.UtcNowFactory = () => fixedNow;

        using (var first = new StringWriter())
        {
            Assert.Equal(0, IssuePublishFlowCommand.Execute(
                workspace.Context,
                ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
                first));
        }

        Assert.Equal(1, stub.CallCount);
        var runsAfterFirst = File.ReadAllText(workspace.RunsLogPath);

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, stub.CallCount); // unchanged — no second gh call
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.Equal(659, root.GetProperty("issue_number").GetInt32());
        Assert.False(root.GetProperty("queue_state_patched").GetBoolean());
        Assert.False(root.GetProperty("publish_yaml_patched").GetBoolean());
        Assert.False(root.GetProperty("runs_appended").GetBoolean());

        var runsAfterSecond = File.ReadAllText(workspace.RunsLogPath);
        Assert.Equal(runsAfterFirst, runsAfterSecond);
        var events = RunLogSerializer.DeserializeAll(runsAfterSecond);
        Assert.Single(events); // exactly one issue-created event total
    }

    [Fact]
    public void Execute_GivenCreatorFailure_LeavesQueueStatePublishYamlAndRunsUnchanged()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        workspace.SeedQueueState("G278", "G278 Fix issue publish-flow durable state synchronization");

        var queueStateBefore = File.ReadAllText(workspace.QueueStatePath);
        var publishYamlExistsBefore = File.Exists(workspace.PublishYamlPath("G278"));
        var runsLogExistsBefore = File.Exists(workspace.RunsLogPath);

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("created").GetBoolean());
        Assert.False(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.False(root.GetProperty("queue_state_patched").GetBoolean());
        Assert.False(root.GetProperty("publish_yaml_patched").GetBoolean());
        Assert.False(root.GetProperty("runs_appended").GetBoolean());

        Assert.Equal(queueStateBefore, File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(publishYamlExistsBefore, File.Exists(workspace.PublishYamlPath("G278")));
        Assert.Equal(runsLogExistsBefore, File.Exists(workspace.RunsLogPath));
    }

    [Fact]
    public void Execute_GivenDryRun_NeverWritesToQueueStatePublishYamlOrRuns()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        workspace.SeedQueueState("G278", "G278 Fix issue publish-flow durable state synchronization");

        var queueStateBefore = File.ReadAllText(workspace.QueueStatePath);
        var publishYamlExistsBefore = File.Exists(workspace.PublishYamlPath("G278"));
        var runsLogExistsBefore = File.Exists(workspace.RunsLogPath);

        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator(); // would throw if called

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(queueStateBefore, File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(publishYamlExistsBefore, File.Exists(workspace.PublishYamlPath("G278")));
        Assert.Equal(runsLogExistsBefore, File.Exists(workspace.RunsLogPath));
    }

    [Fact]
    public void Execute_OutputAndLocalStateConsistent_DoesNotClaimCreatedTrueWithoutWrites()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G278", BuildCompleteContractBody("G278 Fix issue publish-flow durable state synchronization"));
        workspace.SeedQueueState("G278", "G278 Fix issue publish-flow durable state synchronization");

        var stub = new StubIssueCreator("https://github.com/J-Tech-Japan/intent-system/issues/659");
        IssuePublishFlowCommand.CreatorFactory = () => stub;

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G278", "--repo", "J-Tech-Japan/intent-system", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var created = root.GetProperty("created").GetBoolean();
        var durableSynced = root.GetProperty("durable_state_synced").GetBoolean();

        if (created)
        {
            Assert.True(durableSynced,
                "command must not claim created:true while local durable artifacts remain unmodified");
            Assert.True(File.Exists(workspace.PublishYamlPath("G278")));
            Assert.True(File.Exists(workspace.RunsLogPath));
            var artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(workspace.PublishYamlPath("G278")));
            Assert.Equal("issue-created", artifact.PublishStatus);
        }
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new IssuePublishFlowWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("issue publish-flow", writer.ToString(), StringComparison.Ordinal);
    }

    // ── G298 execution-unit prefix in published title ────────────────────

    [Fact]
    public void Execute_G298_PacketYamlTitleWithoutPrefix_PrependsExecutionUnitInIssueTitle()
    {
        // packet.yaml carries a meaningful title that does NOT begin with the
        // execution unit (the canonical G294 case). The publish-flow result
        // must show the execution unit as a prefix in the GitHub issue title.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WritePacketYaml("G294", "Add host branch policy for main versus main-ai operation");
        workspace.WriteGithubBody("G294", BuildContractBodyWithoutH1());

        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G294", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(
            "G294 Add host branch policy for main versus main-ai operation",
            root.GetProperty("title").GetString());
        Assert.Equal(
            "G294 Add host branch policy for main versus main-ai operation",
            root.GetProperty("issue_title").GetString());
        Assert.Equal("packet-yaml", root.GetProperty("title_source").GetString());
    }

    [Fact]
    public void Execute_G298_PacketYamlTitleAlreadyPrefixed_DoesNotDuplicate()
    {
        // Already-prefixed packet titles must not get the prefix added a second
        // time (SKS-G190 case continues to publish verbatim).
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WritePacketYaml("SKS-G190",
            "SKS-G190 Approval-Gated Production Credential Issuance And Rotation Lifecycle Baseline");
        workspace.WriteGithubBody("SKS-G190", BuildContractBodyWithoutH1());

        using var writer = new StringWriter();
        IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["SKS-G190", "--repo", "J-Tech-Japan/SekibanAsAService", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(
            "SKS-G190 Approval-Gated Production Credential Issuance And Rotation Lifecycle Baseline",
            root.GetProperty("title").GetString());
        Assert.Equal(
            "SKS-G190 Approval-Gated Production Credential Issuance And Rotation Lifecycle Baseline",
            root.GetProperty("issue_title").GetString());
    }

    [Fact]
    public void Execute_G298_BodyH1AlreadyPrefixed_DoesNotDuplicate()
    {
        // Older packets (no packet.yaml) fall back to body H1; the H1 already
        // contains the prefix and must not be duplicated.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G245",
            BuildCompleteContractBody("G245 Add intent-cli issue publish-flow command"));

        using var writer = new StringWriter();
        IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G245", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G245 Add intent-cli issue publish-flow command", root.GetProperty("title").GetString());
        Assert.Equal("G245 Add intent-cli issue publish-flow command", root.GetProperty("issue_title").GetString());
        Assert.Equal("github-body-h1", root.GetProperty("title_source").GetString());
    }

    [Fact]
    public void Execute_G298_BodyH1WithoutPrefix_PrependsExecutionUnit()
    {
        // Synthetic case: body H1 without execution-unit prefix should still
        // get prefixed at publish time so the GitHub issue list correlates.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G298",
            BuildCompleteContractBody("Implement issue title prefix formatter"));

        using var writer = new StringWriter();
        IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G298", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G298 Implement issue title prefix formatter", root.GetProperty("title").GetString());
        Assert.Equal("github-body-h1", root.GetProperty("title_source").GetString());
    }

    [Fact]
    public void Execute_G298_FallbackUntitled_RemainsVerbatim()
    {
        // The deterministic <id> (untitled) fallback already starts with the
        // execution unit; the formatter must not double-prefix it.
        using var workspace = new IssuePublishFlowWorkspace();
        workspace.WriteGithubBody("G900", BuildContractBodyWithoutH1());

        using var writer = new StringWriter();
        IssuePublishFlowCommand.Execute(
            workspace.Context,
            ["G900", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("G900 (untitled)", root.GetProperty("title").GetString());
        Assert.Equal("G900 (untitled)", root.GetProperty("issue_title").GetString());
    }

    [Fact]
    public void FormatIssueTitle_PrependsWhenMissing_KeepsWhenPresent()
    {
        Assert.Equal(
            "G294 Add host branch policy",
            IssuePublishFlowCommand.FormatIssueTitle("G294", "Add host branch policy"));

        Assert.Equal(
            "G294 Add host branch policy",
            IssuePublishFlowCommand.FormatIssueTitle("G294", "G294 Add host branch policy"));

        // Token must match exactly — `G2940 ...` should NOT be treated as already
        // prefixed for execution unit `G294`.
        Assert.Equal(
            "G294 G2940 Other unit",
            IssuePublishFlowCommand.FormatIssueTitle("G294", "G2940 Other unit"));

        Assert.Equal(
            "G294 (untitled)",
            IssuePublishFlowCommand.FormatIssueTitle("G294", null));

        Assert.Equal(
            "G294 (untitled)",
            IssuePublishFlowCommand.FormatIssueTitle("G294", "   "));
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

    /// <summary>
    /// G290: complete contract body with NO leading H1. Mirrors the
    /// SKS-G190-style packet that triggered the `(untitled)` regression.
    /// </summary>
    private static string BuildContractBodyWithoutH1()
    {
        return """
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

    private sealed class StubIssueCreator : IIssueCreator
    {
        private readonly string url;

        public StubIssueCreator(string url)
        {
            this.url = url;
        }

        public string? LastRepo { get; private set; }

        public string? LastTitle { get; private set; }

        public string? LastBodyFile { get; private set; }

        public int CallCount { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            LastRepo = repo;
            LastTitle = title;
            LastBodyFile = bodyFilePath;
            CallCount++;
            return new IssueCreateOutcome(url);
        }
    }

    private sealed class ThrowingIssueCreator : IIssueCreator
    {
        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            throw new InvalidOperationException("simulated gh failure");
        }
    }

    /// <summary>
    /// G536 review repair: replaces the real <c>gh issue list --search</c>
    /// shell-out for tests. Defaults to
    /// <see cref="GitHubExistingIssueClassification.None"/> so pre-existing
    /// tests that exercise the normal create path are unaffected; tests
    /// covering the GitHub-restore or ambiguous-refusal paths construct one
    /// with <see cref="GitHubExistingIssueClassification.Unique"/> or
    /// <see cref="GitHubExistingIssueClassification.Multiple"/>.
    /// </summary>
    private sealed class StubExistingIssueChecker : IGitHubExistingIssueChecker
    {
        private readonly GitHubExistingIssueLookupResult result;

        public StubExistingIssueChecker(
            GitHubExistingIssueClassification classification, int? issueNumber = null, string? issueUrl = null)
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

    private sealed class ThrowingExistingIssueChecker : IGitHubExistingIssueChecker
    {
        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            throw new InvalidOperationException("simulated gh failure");
        }
    }

    private sealed class IssuePublishFlowWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("issue-publish-flow-tests-")
            .FullName;

        public IssuePublishFlowWorkspace()
        {
            Context = new CliContext
            {
                RepoRoot = rootPath,
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

        public CliContext Context { get; }

        public string QueueStatePath => Path.Combine(rootPath, ".intent-cli", "queue-state.json");

        public string RunsLogPath => Path.Combine(rootPath, ".intent-cli", "runs.jsonl");

        public string PublishYamlPath(string executionUnit) =>
            Path.Combine(rootPath, ".intent-cli", "issues", executionUnit, "publish.yaml");

        public void WriteGithubBody(string executionUnit, string content)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "github-body.md"), content);
        }

        public void WritePacketYaml(string executionUnit, string title)
        {
            var directory = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "packet.yaml"),
                $"execution_unit: {executionUnit}\ntitle: {title}\n");
        }

        public void SeedQueueState(string executionUnit, string title) =>
            WriteQueueStateForUnit(executionUnit, title, linkedIssue: null);

        public void SeedQueueStateWithLinkedIssue(
            string executionUnit,
            string title,
            string repo,
            int issueNumber,
            string issueUrl) =>
            WriteQueueStateForUnit(
                executionUnit,
                title,
                linkedIssue: new LinkedIssue
                {
                    Repo = repo,
                    Number = issueNumber,
                    Url = issueUrl,
                });

        private void WriteQueueStateForUnit(string executionUnit, string title, LinkedIssue? linkedIssue)
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
                        ExecutionUnit = executionUnit,
                        Title = title,
                        State = QueueItemState.Queued,
                        Dependencies = Array.Empty<string>(),
                        BlockedBy = Array.Empty<string>(),
                        ClarificationReturnPath = string.Empty,
                        PacketPaths = new PacketPaths
                        {
                            Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                            ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                            Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        },
                        LinkedIssue = linkedIssue,
                        WorkerRole = "child-impl",
                        ReviewRole = "host-review",
                        Priority = "normal",
                    }
                ]
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
    }
}
