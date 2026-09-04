using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G564: intent-tree co-evolution enforcement. A packet-declared knowledge
/// write-back that never happens used to be invisible — the declaration lived
/// in the packet, the write-back (when it happened) lived in a host commit the
/// detection layer cannot see, and nothing said "done", so nothing could say
/// "not done". These fixtures pin the three halves that close it: a recording
/// surface with evidence, a stalled-work kind that ages the absence, and the
/// guide text that binds the duty into design's cadence.
///
/// Shares <see cref="AutomationStalledWorkSharedStateCollection"/> because it
/// mutates <see cref="AutomationStalledWorkCommand"/>'s process-global
/// <c>CandidateListerFactory</c> / <c>UtcNowFactory</c> seams.
/// </summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class KnowledgeWriteBackG564Tests : IDisposable
{
    /// <summary>
    /// After <see cref="AutomationStalledWorkCommand.KnowledgeWriteBackActivationUtc"/>,
    /// so the fixtures below exercise the in-scope path; the pre-activation
    /// floor has its own fixture.
    /// </summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private const string Repo = "J-Tech-Japan/intent-system";
    private const string HostCommit = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    public KnowledgeWriteBackG564Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyCandidateLister();
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
        AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory = null;
    }

    // ---------------------------------------------------------------- detection

    [Fact]
    public void AClosedUnitWithDeclaredWriteBacks_ProducesAVisibleAgingItem_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md"]);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-180));

        var items = workspace.RunStalledWork();
        var item = Assert.Single(items.EnumerateArray());

        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackPending, item.GetProperty("kind").GetString());
        Assert.Equal("G564", item.GetProperty("execution_unit").GetString());
        Assert.Equal(180, item.GetProperty("age_minutes").GetInt32());
        Assert.False(item.GetProperty("is_informational").GetBoolean());

        // The declared target paths belong in the item — a report that only
        // says "something is pending" cannot be acted on.
        var declared = item.GetProperty("declared_write_back_targets").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Contains("intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md", declared);

        var action = item.GetProperty("recommended_action").GetString()!;
        Assert.Contains("intent-cli automation knowledge-writeback-record", action, StringComparison.Ordinal);
        Assert.Contains("--execution-unit G564", action, StringComparison.Ordinal);
        Assert.Contains("intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md", action, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingTheWriteBack_ClearsThePendingItem_G564()
    {
        // The acceptance walk-through, driven through the REAL surfaces:
        // declare → close out → observe pending → record → observe clearance.
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/intent-cli/intent-tree/means/08-agent-message-orchestration.md"]);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-180));

        Assert.Equal(1, workspace.RunStalledWork().GetArrayLength());

        var record = workspace.RunRecord(["--execution-unit", "G564", "--commit", HostCommit, "--write", "--format", "json"]);
        Assert.Equal(0, record.ExitCode);
        Assert.True(record.Json.GetProperty("applied").GetBoolean());
        Assert.False(record.Json.GetProperty("already_recorded").GetBoolean());
        Assert.True(File.Exists(workspace.RecordPath("G564")));

        Assert.Equal(0, workspace.RunStalledWork().GetArrayLength());
    }

    [Fact]
    public void RecordedButUncommitted_IsDistinctAndNamesThePath_G661()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.InitializeGit();
        workspace.WriteDeclaringPacket("G661", requiredIntentTree: true, targets: ["intents/node-02.md"]);
        workspace.WriteCloseout("G661", FixedNow.AddMinutes(-45));
        workspace.CommitAll("baseline closeout");

        var record = workspace.RunRecord(["--execution-unit", "G661", "--commit", HostCommit, "--write", "--format", "json"]);
        Assert.True(record.Json.GetProperty("commit_push_required_for_other_checkouts").GetBoolean());
        Assert.Contains("committed and pushed", record.Json.GetProperty("durability_guidance").GetString()!, StringComparison.Ordinal);

        var item = Assert.Single(workspace.RunStalledWork().EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackRecordedUncommitted, item.GetProperty("kind").GetString());
        Assert.Equal(".intent-cli/knowledge-writebacks/G661/record.json", item.GetProperty("record_path").GetString());
        Assert.Contains("commit and push", item.GetProperty("recommended_action").GetString()!, StringComparison.Ordinal);
        Assert.Contains("knowledge-writebacks", workspace.GitStatus(), StringComparison.Ordinal);

        workspace.CommitAll("commit writeback record");
        Assert.Equal(0, workspace.RunStalledWork().GetArrayLength());
    }

    [Fact]
    public void AUnitThatDeclaredNothingRequired_NeverAppears_G564()
    {
        // Declining is a legitimate answer. This kind detects broken promises,
        // not slices that legitimately owe the tree nothing.
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: false, targets: []);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-4000));

        Assert.Equal(0, workspace.RunStalledWork().GetArrayLength());
    }

    [Fact]
    public void ClosedOutLearningAlone_IsEnoughToRaiseTheObligation_G564()
    {
        // `closeout_learning.write_back_required` is the second declaration
        // source; a packet using only it must be detected exactly the same.
        using var workspace = new WriteBackWorkspace();
        workspace.WritePacket("G564", """
            implementation_issue_packet:
              source_execution_unit: G564
              domain: intent-cli
            closeout_learning:
              expected: "node 03 gains the audit-state machinery section"
              write_back_required: true
              write_back_targets:
                - intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md
            """);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-90));

        var item = Assert.Single(workspace.RunStalledWork().EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackPending, item.GetProperty("kind").GetString());
        Assert.Contains(
            "closeout_learning",
            item.GetProperty("recommended_action").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableDeclarationMetadata_IsExcludedWithItsPath_NotSilentlyCleared_G564()
    {
        // A present-but-unparseable declaration establishes NEITHER that a
        // write-back is owed nor that it is not. Reading it as `false` would
        // manufacture the exact false all-clear this slice exists to stop.
        using var workspace = new WriteBackWorkspace();
        workspace.WritePacket("G564", """
            implementation_issue_packet:
              source_execution_unit: G564
              domain: intent-cli
            knowledge_updates:
              intent_tree:
                required: yes-please
                target_paths: []
            """);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-120));

        var result = workspace.RunStalledWorkResult();
        Assert.Equal(0, result.GetProperty("items").GetArrayLength());

        var excluded = Assert.Single(result.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackPending, excluded.GetProperty("kind").GetString());
        Assert.Equal(AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable, excluded.GetProperty("reason").GetString());

        var detail = excluded.GetProperty("detail").GetString()!;
        Assert.Contains("packet.yaml", detail, StringComparison.Ordinal);
        Assert.Contains("knowledge_updates.intent_tree.required", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void G670ReadinessExclusionIsReconciledWhenKnowledgeCollectorFindsALaterExclusion_G564()
    {
        // G670 runs before G564. A placeholder packet may initially add its
        // named backlog-ready-idle exclusion, but that preview is not the
        // sole explanation once the later knowledge collector finds its
        // official unreadable-metadata exclusion.
        using var workspace = new WriteBackWorkspace();
        var bindingsPath = Path.Combine(workspace.RootPath, "intents", "intent-cli", "automation", "bindings.md");
        Directory.CreateDirectory(Path.GetDirectoryName(bindingsPath)!);
        File.WriteAllText(bindingsPath, "---\nexecution_unit_regex: '.*'\n---\n");
        Assert.Equal(0, PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G670", "--target-repo", Repo],
            TextWriter.Null));

        workspace.WritePacket("G564", """
            implementation_issue_packet:
              source_execution_unit: G564
              domain: intent-cli
            knowledge_updates:
              intent_tree:
                required: yes-please
                target_paths: []
            """);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-120));
        File.WriteAllText(workspace.Context.GetQueueStatePath(), """
            {
              "schema_version": "1",
              "updated_at": "2026-08-15T12:00:00Z",
              "items": [
                {
                  "execution_unit": "G670",
                  "title": "G670 title",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G670/implementation.md",
                    "review_context": ".intent-cli/issues/G670/review-context.md",
                    "yaml": ".intent-cli/issues/G670/packet.yaml"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        var result = workspace.RunStalledWorkResult();
        Assert.DoesNotContain(
            result.GetProperty("excluded").EnumerateArray(),
            exclusion => exclusion.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle
                && exclusion.GetProperty("reason").GetString() == NextSliceReadinessClass.ContractIncomplete);
        var knowledgeExclusion = Assert.Single(
            result.GetProperty("excluded").EnumerateArray(),
            exclusion => exclusion.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable);
        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackPending, knowledgeExclusion.GetProperty("kind").GetString());
    }

    [Fact]
    public void AnUnreadableRecord_IsExcludedWithItsPath_NotCountedAsCleared_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-120));
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RecordPath("G564"))!);
        File.WriteAllText(workspace.RecordPath("G564"), "{ not json");

        var result = workspace.RunStalledWorkResult();
        Assert.Equal(0, result.GetProperty("items").GetArrayLength());

        var excluded = Assert.Single(result.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable, excluded.GetProperty("reason").GetString());
        Assert.Contains("record.json", excluded.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void UnitsClosedBeforeActivation_AreOutOfScope_UnlessTheOperatorAsksForThem_G564()
    {
        // Retroactive detection is out of scope by contract: an upgrade must
        // not light up every historical unit on its first wake. Asking for it
        // explicitly still works.
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G520", requiredIntentTree: true, targets: ["intents/legacy.md"]);
        workspace.WriteCloseout("G520", AutomationStalledWorkCommand.KnowledgeWriteBackActivationUtc.AddDays(-10));

        Assert.Equal(0, workspace.RunStalledWork().GetArrayLength());

        var retroactive = workspace.RunStalledWork(extraArgs: ["--knowledge-writeback-since", "2026-01-01T00:00:00Z"]);
        var item = Assert.Single(retroactive.EnumerateArray());
        Assert.Equal("G520", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void ARepeatedCloseout_DoesNotResetTheItemAge_G564()
    {
        // Age is measured from when the obligation STARTED. A retried closeout
        // must not make a three-day-old pending write-back look fresh.
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-4320));
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-10));

        var item = Assert.Single(workspace.RunStalledWork().EnumerateArray());
        Assert.Equal(4320, item.GetProperty("age_minutes").GetInt32());
    }

    [Fact]
    public void Heartbeat_CarriesTheKind_AndNamesItInTheMessageBody_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md"]);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-180));

        using var writer = new StringWriter();
        var exit = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", Repo, "--format", "json"],
            writer);
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("stale").GetBoolean());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackPending, item.GetProperty("kind").GetString());

        var body = document.RootElement.GetProperty("message_body").GetString()!;
        Assert.Contains("knowledge-writeback-record", body, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownReport_NamesTheDeclaredTargets_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md"]);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-180));

        using var writer = new StringWriter();
        var exit = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", Repo],
            writer);
        Assert.Equal(0, exit);

        var output = writer.ToString();
        Assert.Contains("knowledge-writeback-pending", output, StringComparison.Ordinal);
        Assert.Contains(
            "declared_write_back_targets: intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md",
            output,
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- recording

    [Fact]
    public void Record_IsIdempotentForTheSameCommit_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);

        var first = workspace.RunRecord(["--execution-unit", "G564", "--commit", HostCommit, "--write", "--format", "json"]);
        Assert.Equal(0, first.ExitCode);
        var firstBytes = File.ReadAllBytes(workspace.RecordPath("G564"));

        var second = workspace.RunRecord(["--execution-unit", "G564", "--commit", HostCommit.ToUpperInvariant(), "--write", "--format", "json"]);
        Assert.Equal(0, second.ExitCode);
        Assert.True(second.Json.GetProperty("already_recorded").GetBoolean());
        Assert.False(second.Json.GetProperty("applied").GetBoolean());

        // Byte-identical: a re-run in a retried closeout wake must not rewrite
        // the evidence (it would move `recorded_at` off the real event).
        Assert.Equal(firstBytes, File.ReadAllBytes(workspace.RecordPath("G564")));
    }

    [Fact]
    public void Record_RefusesConflictingEvidence_RatherThanOverwritingIt_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);
        Assert.Equal(0, workspace.RunRecord(["--execution-unit", "G564", "--commit", HostCommit, "--write", "--format", "json"]).ExitCode);
        var before = File.ReadAllBytes(workspace.RecordPath("G564"));

        var conflict = workspace.RunRecord(["--execution-unit", "G564", "--commit", "0f0f0f0f0f0f0f0f", "--write", "--format", "json"]);
        Assert.Equal(1, conflict.ExitCode);
        Assert.Contains("refusing to replace", conflict.Json.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(workspace.RecordPath("G564")));
    }

    [Fact]
    public void Record_FailsClosedOnAnUnknownExecutionUnit_G564()
    {
        using var workspace = new WriteBackWorkspace();

        var result = workspace.RunRecord(["--execution-unit", "G999", "--commit", HostCommit, "--write", "--format", "json"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unknown execution unit", result.Json.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.RecordPath("G999")));
        Assert.False(Directory.Exists(Path.GetDirectoryName(workspace.RecordPath("G999"))));
    }

    [Theory]
    [InlineData("not-a-sha")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzz")]
    public void Record_FailsClosedOnMalformedEvidence_G564(string commit)
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);

        // Malformed evidence is rejected at the ARGUMENT boundary, before the
        // command touches the packet or the artifact directory at all.
        using var writer = new StringWriter();
        var exit = AutomationKnowledgeWriteBackRecordCommand.Execute(
            workspace.Context, ["--execution-unit", "G564", "--commit", commit, "--write"], writer);

        Assert.Equal(1, exit);
        Assert.Contains("is not a commit SHA", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.RecordPath("G564")));
    }

    [Fact]
    public void Record_WithoutEvidence_IsRefused_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);

        using var writer = new StringWriter();
        var exit = AutomationKnowledgeWriteBackRecordCommand.Execute(
            workspace.Context, ["--execution-unit", "G564", "--write"], writer);

        Assert.Equal(1, exit);
        Assert.Contains("--commit is required", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.RecordPath("G564")));
    }

    [Fact]
    public void Record_DryRunIsTheDefault_AndWritesNothing_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);

        var result = workspace.RunRecord(["--execution-unit", "G564", "--commit", HostCommit, "--format", "json"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("dry-run", result.Json.GetProperty("mode").GetString());
        Assert.False(result.Json.GetProperty("applied").GetBoolean());
        Assert.False(File.Exists(workspace.RecordPath("G564")));
    }

    [Fact]
    public void Record_OnAUnitThatDeclaredNothing_SucceedsButSaysSo_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: false, targets: []);

        var result = workspace.RunRecord(["--execution-unit", "G564", "--commit", HostCommit, "--write", "--format", "json"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Json.GetProperty("declaration_required").GetBoolean());
        var warning = Assert.Single(result.Json.GetProperty("warnings").EnumerateArray());
        Assert.Contains("declared no required knowledge write-back", warning.GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_CarriesTheEvidenceAndDeclaredTargetsIntoTheArtifact_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/declared.md"]);

        var result = workspace.RunRecord([
            "--execution-unit", "G564",
            "--commit", HostCommit,
            "--target", "intents/declared.md",
            "--note", "node 03 gains the audit-state machinery section",
            "--write", "--format", "json"
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("intents/declared.md", result.Json.GetProperty("declared_targets").EnumerateArray().Select(t => t.GetString()));

        var record = KnowledgeWriteBackRecord.Deserialize(File.ReadAllText(workspace.RecordPath("G564")), "G564");
        Assert.Equal(KnowledgeWriteBackRecord.ArtifactKindValue, record.ArtifactKind);
        Assert.Equal("G564", record.ExecutionUnit);
        Assert.Equal(HostCommit, record.HostCommit);
        Assert.Equal(FixedNow, record.RecordedAt);
        Assert.Contains("intents/declared.md", record.Targets);
        Assert.Equal("node 03 gains the audit-state machinery section", record.Note);
    }

    // ------------------------------------------- G564 review repair: identity

    /// <summary>
    /// Review finding 1: the execution unit was interpolated into the packet
    /// and record paths with no canonical-identifier check. A dry run accepted
    /// `../../.intent-cli/issues/G564` and resolved a record path OUTSIDE
    /// `.intent-cli/knowledge-writebacks`; write mode could have escaped the
    /// artifact root entirely.
    /// </summary>
    [Theory]
    [InlineData("../../.intent-cli/issues/G564")]      // the reported traversal
    [InlineData("..")]
    [InlineData("../G564")]
    [InlineData("G564/../../escape")]
    [InlineData("/etc/passwd")]                         // rooted (POSIX)
    [InlineData("C:\\Windows\\system32")]               // rooted (Windows) + separators + colon
    [InlineData("\\\\server\\share")]                   // UNC
    [InlineData("sub/dir")]                             // any separator at all
    [InlineData(".hidden")]                             // leading dot segment
    [InlineData("G564 with space")]
    [InlineData("G564\nG999")]
    public void Record_RejectsNoncanonicalExecutionUnits_BeforeTouchingTheFilesystem_G564(string executionUnit)
    {
        using var workspace = new WriteBackWorkspace();

        using var writer = new StringWriter();
        var exit = AutomationKnowledgeWriteBackRecordCommand.Execute(
            workspace.Context, ["--execution-unit", executionUnit, "--commit", HostCommit, "--write"], writer);

        Assert.Equal(1, exit);
        Assert.Contains("--execution-unit is invalid", writer.ToString(), StringComparison.Ordinal);

        // Nothing anywhere under the workspace was created — not inside the
        // artifact root, and (the actual defect) not outside it either.
        Assert.False(Directory.Exists(Path.Combine(workspace.RootPath, ".intent-cli", "knowledge-writebacks")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(workspace.RootPath, ".intent-cli")));
    }

    [Fact]
    public void RecordPathResolution_ContainsEveryPathBeneathItsArtifactRoot_G564()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), "g564-containment");

        // Canonical unit: contained, as expected.
        var recordRoot = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "knowledge-writebacks"));
        var packetRoot = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "issues"));
        Assert.StartsWith(recordRoot + Path.DirectorySeparatorChar, KnowledgeWriteBackRecord.ResolveFullPath(repoRoot, "G564"), StringComparison.Ordinal);
        Assert.StartsWith(packetRoot + Path.DirectorySeparatorChar, KnowledgeWriteBackRecord.ResolvePacketPath(repoRoot, "G564"), StringComparison.Ordinal);

        // Non-canonical unit: refused at resolution, so no caller can be handed
        // an escaping path even if it skipped the argument-boundary gate.
        Assert.Throws<InvalidOperationException>(() => KnowledgeWriteBackRecord.ResolveFullPath(repoRoot, "../escape"));
        Assert.Throws<InvalidOperationException>(() => KnowledgeWriteBackRecord.ResolvePacketPath(repoRoot, "../escape"));
        Assert.Throws<InvalidOperationException>(() => KnowledgeWriteBackRecord.ResolveRelativePath("../escape"));
    }

    [Fact]
    public void StalledWork_ExcludesANoncanonicalUnitFromTheRunsLog_WithoutDerivingAPathFromIt_G564()
    {
        // The runs log is DATA. A `closeout-recorded` event naming
        // `../../etc` must not become a filesystem probe.
        using var workspace = new WriteBackWorkspace();
        workspace.WriteCloseout("../../etc", FixedNow.AddMinutes(-120));

        var result = workspace.RunStalledWorkResult();

        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
        var excluded = Assert.Single(result.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindKnowledgeWritebackPending, excluded.GetProperty("kind").GetString());
        Assert.Equal(AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable, excluded.GetProperty("reason").GetString());
        Assert.Contains("non-canonical execution unit", excluded.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    // ------------------------------------------- G564 review repair: evidence

    /// <summary>
    /// Review finding 2: `Deserialize` checked only that `execution_unit` and
    /// `host_commit` were non-blank, while stalled-work cleared the item for
    /// ANY deserializable record. A record stored under `G564` naming `G999`
    /// therefore discharged G564's obligation.
    /// </summary>
    [Fact]
    public void ARecordNamingADifferentUnit_DoesNotClearThisUnit_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-120));
        workspace.WriteRawRecord("G564", executionUnit: "G999", hostCommit: HostCommit);

        var result = workspace.RunStalledWorkResult();

        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
        var excluded = Assert.Single(result.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable, excluded.GetProperty("reason").GetString());
        Assert.Contains("record.json", excluded.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.Contains("G999", excluded.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordWithNonShaEvidence_DoesNotClearTheItem_G564()
    {
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);
        workspace.WriteCloseout("G564", FixedNow.AddMinutes(-120));
        workspace.WriteRawRecord("G564", executionUnit: "G564", hostCommit: "written-it-honest");

        var result = workspace.RunStalledWorkResult();

        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
        var excluded = Assert.Single(result.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable, excluded.GetProperty("reason").GetString());
        Assert.Contains("hexadecimal SHA", excluded.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_RefusesToActOnAMisattributedExistingRecord_RatherThanTreatingItAsPriorEvidence_G564()
    {
        // The same validation on the WRITE side: a mis-attributed record must
        // not silently satisfy the idempotency check, and must not be
        // overwritten either — it is unreadable evidence, and repairing it is
        // a deliberate act.
        using var workspace = new WriteBackWorkspace();
        workspace.WriteDeclaringPacket("G564", requiredIntentTree: true, targets: ["intents/x.md"]);
        workspace.WriteRawRecord("G564", executionUnit: "G999", hostCommit: HostCommit);
        var before = File.ReadAllBytes(workspace.RecordPath("G564"));

        var result = workspace.RunRecord(["--execution-unit", "G564", "--commit", HostCommit, "--write", "--format", "json"]);

        Assert.Equal(1, result.ExitCode);
        var error = result.Json.GetProperty("error").GetString()!;
        Assert.Contains("could not be read", error, StringComparison.Ordinal);
        Assert.Contains("G999", error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(workspace.RecordPath("G564")));
    }

    // ---------------------------------------------------------------- router

    [Fact]
    public void CommandRouter_DispatchesAndAdvertisesTheRecordingCommand_G564()
    {
        var router = typeof(CommandRouter);

        var helpField = router.GetField("AutomationCommandHelp",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var lines = (IReadOnlyList<string>?)helpField!.GetValue(null);
        Assert.Contains(lines!, line => line.Contains("knowledge-writeback-record", StringComparison.Ordinal));

        var commandsField = router.GetField("ImplementedCommands",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var outer = (System.Collections.IDictionary)commandsField!.GetValue(null)!;
        var automation = (System.Collections.IDictionary)outer["automation"]!;
        Assert.True(automation.Contains("knowledge-writeback-record"));
        var handler = (Delegate)automation["knowledge-writeback-record"]!;
        Assert.Equal(
            typeof(AutomationKnowledgeWriteBackRecordCommand).GetMethod(
                "Execute",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!,
            handler.Method);
    }

    // ---------------------------------------------------------------- guides

    [Fact]
    public void CloseoutGuide_BindsTheWriteBackToTheSameCadence_AndNamesTheRecordCommand_G564()
    {
        var output = Render(["guide", "closeout", "run", "--domain", "intent-cli", "--repo", Repo]);

        Assert.Contains(IntentTreeCoEvolutionDuty.Duty, output, StringComparison.Ordinal);
        Assert.Contains(IntentTreeCoEvolutionDuty.AuthoringRule, output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation knowledge-writeback-record", output, StringComparison.Ordinal);
        // The item is not cleared by merging — that is the whole point of a
        // record separate from the PR lifecycle.
        Assert.Contains("Merging and closing the PR do NOT clear it", output, StringComparison.Ordinal);
        // And the closeout report is what tells design about it.
        Assert.Contains("closeout report to the design thread NAMES the packet's declared write-backs", output, StringComparison.Ordinal);
    }

    [Fact]
    public void PacketAuthoringGuide_RequiresHonestDeclarations_G564()
    {
        var output = Render(["guide", "workflow", "task", "packet-draft"]);

        Assert.Contains(IntentTreeCoEvolutionDuty.AuthoringRule, output, StringComparison.Ordinal);
        Assert.Contains("knowledge-writeback-pending", output, StringComparison.Ordinal);
        Assert.Contains(
            "co-evolution-duty",
            GuideWorkflowTaskPacketDraftCommand.IntentMaintenancePrompts.Select(p => p.Id));
    }

    [Fact]
    public void DesignThreadGuidance_StatesTheDutyInTheOperatorsTerms_G564()
    {
        var output = Render(["guide", "orchestrator-thread", "--domain", "intent-cli", "--target-repo", Repo, "--agent", "claude"]);

        Assert.Contains(IntentTreeCoEvolutionDuty.Duty, output, StringComparison.Ordinal);
        Assert.Contains(
            GuideRoleVocabulary.ProjectRenderedRoleValues(IntentTreeCoEvolutionDuty.CloseoutCheck),
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrchestratorCloseoutReport_EnumeratesDeclaredWriteBacks_WithoutMutatingHostIntent_G564()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            ["guide", "orchestrator-thread", "--domain", "intent-cli", "--target-repo", Repo, "--agent", "claude", "--format", "json"],
            CreateGuideContext(),
            writer);
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(writer.ToString());
        var rule = FindStringProperty(document.RootElement, "closeout_knowledge_write_back_rule");
        Assert.False(string.IsNullOrWhiteSpace(rule), "the closeout report rule is missing from the orchestrator-thread contract");
        Assert.Contains("knowledge_updates", rule!, StringComparison.Ordinal);
        Assert.Contains("closeout_learning.write_back_required", rule!, StringComparison.Ordinal);
        Assert.Contains("knowledge-writeback-record", rule!, StringComparison.Ordinal);
        // Read-only propagation: reporting an obligation is not writing the tree.
        Assert.Contains("mutates host intent content", rule!, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseNotes_CoverG564_InBothLanguages_G564()
    {
        var repoRoot = FindRepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var notes = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "release-notes-v0.7.0.md"));
            Assert.Contains("G564", notes, StringComparison.Ordinal);
            Assert.Contains("knowledge-writeback-record", notes, StringComparison.Ordinal);
            Assert.Contains("knowledge-writeback-pending", notes, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static string Render(string[] args)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, CreateGuideContext(), writer);
        Assert.True(exit == 0, $"`intent-cli {string.Join(' ', args)}` exited {exit}: {writer}");
        return writer.ToString();
    }

    private static CliContext CreateGuideContext() => new()
    {
        RepoRoot = Path.GetTempPath(),
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }

    private static string? FindStringProperty(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.Ordinal)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    var nested = FindStringProperty(property.Value, name);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindStringProperty(item, name);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Keeps <c>automation stalled-work</c> off the network: without an
    /// injected lister it shells out to `gh`, which passes on a developer
    /// machine with credentials and fails on a CI runner.
    /// </summary>
    private sealed class EmptyCandidateLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();
    }

    private sealed record RecordRun(int ExitCode, JsonElement Json);

    private sealed class WriteBackWorkspace : IDisposable
    {
        private readonly List<JsonDocument> _documents = new();

        public WriteBackWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("knowledge-writeback-g564-").FullName;
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
                    },
                },
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public string RecordPath(string executionUnit) =>
            KnowledgeWriteBackRecord.ResolveFullPath(RootPath, executionUnit);

        public void WritePacket(string executionUnit, string yaml)
        {
            var directory = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"), yaml);
        }

        public void WriteDeclaringPacket(string executionUnit, bool requiredIntentTree, IReadOnlyList<string> targets)
        {
            var targetLines = targets.Count == 0
                ? "    target_paths: []"
                : "    target_paths:\n" + string.Join('\n', targets.Select(target => $"      - {target}"));

            WritePacket(executionUnit, $"""
                implementation_issue_packet:
                  source_execution_unit: {executionUnit}
                  domain: intent-cli
                knowledge_updates:
                  intent_tree:
                    required: {(requiredIntentTree ? "true" : "false")}
                {targetLines}
                    summary: ""
                  adr:
                    required: false
                    target_paths: []
                  diagram:
                    required: false
                    target_paths: []
                  docs:
                    required: false
                    target_paths: []
                closeout_learning:
                  expected: ""
                  write_back_required: false
                  write_back_targets: []
                """);
        }

        /// <summary>
        /// G564 review repair: writes a record artifact with hand-chosen
        /// contents under <paramref name="storedUnder"/>, so a consumer can be
        /// shown a record whose embedded identity or evidence does not match
        /// the unit it is stored for.
        /// </summary>
        public void WriteRawRecord(string storedUnder, string executionUnit, string hostCommit)
        {
            var path = RecordPath(storedUnder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $$"""
                {
                  "artifact_kind": "knowledge-writeback-record",
                  "execution_unit": "{{executionUnit}}",
                  "host_commit": "{{hostCommit}}",
                  "recorded_at": "2026-08-15T12:00:00+00:00",
                  "targets": [],
                  "note": null
                }
                """);
        }

        /// <summary>Appends the canonical `closeout-recorded` runs event `closeout pr --write` emits.</summary>
        public void WriteCloseout(string executionUnit, DateTimeOffset at)
        {
            var runLogPath = Context.GetRunLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(runLogPath)!);
            var line = IntentSystem.Supervisor.Serialization.RunLogSerializer.SerializeLine(
                new IntentSystem.Supervisor.Models.RunEvent
                {
                    Ts = at,
                    ExecutionUnit = executionUnit,
                    Event = "closeout-recorded",
                    By = "intent-cli closeout pr",
                });
            File.AppendAllText(runLogPath, line + Environment.NewLine);
        }

        public JsonElement RunStalledWorkResult(string[]? extraArgs = null)
        {
            using var writer = new StringWriter();
            var args = new List<string> { "--domain", "intent-cli", "--repo", Repo, "--format", "json" };
            args.AddRange(extraArgs ?? Array.Empty<string>());
            var exit = AutomationStalledWorkCommand.Execute(Context, args.ToArray(), writer);
            Assert.True(exit == 0, $"automation stalled-work exited {exit}: {writer}");

            var document = JsonDocument.Parse(writer.ToString());
            _documents.Add(document);
            return document.RootElement;
        }

        public JsonElement RunStalledWork(string[]? extraArgs = null) =>
            RunStalledWorkResult(extraArgs).GetProperty("items");

        public RecordRun RunRecord(string[] args)
        {
            using var writer = new StringWriter();
            var exit = AutomationKnowledgeWriteBackRecordCommand.Execute(Context, args, writer);
            var document = JsonDocument.Parse(writer.ToString());
            _documents.Add(document);
            return new RecordRun(exit, document.RootElement);
        }

        public void InitializeGit()
        {
            RunGit("init", "-q");
            RunGit("config", "user.email", "tests@example.com");
            RunGit("config", "user.name", "Intent CLI Tests");
        }

        public void CommitAll(string message)
        {
            RunGit("add", ".");
            RunGit("commit", "-q", "-m", message);
        }

        public string GitStatus() => RunGit("status", "--short");

        private string RunGit(params string[] args)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = RootPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {error}");
            return output;
        }

        public void Dispose()
        {
            foreach (var document in _documents)
            {
                document.Dispose();
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a leftover temp directory never fails a test.
            }
        }
    }
}
