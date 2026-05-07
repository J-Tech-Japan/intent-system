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
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = null;
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
    public void Execute_GivenWriteWithMissingQueueState_RefusesSuccessAfterCreate()
    {
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
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/659", root.GetProperty("issue_url").GetString());
        var error = root.GetProperty("error").GetString()!;
        Assert.Contains("durable state is not fully synchronized", error, StringComparison.Ordinal);
        Assert.Contains("queue-state.json", error, StringComparison.Ordinal);
        Assert.Contains("automation reconcile", error, StringComparison.Ordinal);

        // queue-state stays absent; reconcile is the operator-driven repair path.
        Assert.False(File.Exists(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_GivenWriteWithQueueStateMissingExecutionUnit_RefusesSuccessAfterCreate()
    {
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
        Assert.Contains("execution_unit 'G278'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenLinkedIssueInQueueStateButPublishYamlMissing_IsIdempotentAndDoesNotCallGitHub()
    {
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
        Assert.False(root.GetProperty("queue_state_patched").GetBoolean());
        Assert.False(root.GetProperty("publish_yaml_patched").GetBoolean());
        Assert.False(root.GetProperty("runs_appended").GetBoolean());

        // queue-state must remain unchanged; no duplicate gh issue create was made
        Assert.False(File.Exists(workspace.PublishYamlPath("G278")));
        Assert.False(File.Exists(workspace.RunsLogPath));
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
