using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G207: Tests for <c>intent-cli metadata validate</c>. Cover the cases
/// listed in #519 Acceptance Criteria: valid metadata, missing packet
/// file, missing standalone issue section, publish/queue linked-issue
/// mismatch, completed item missing closeout evidence, legacy workflow shapes,
/// and label-policy warning. Plus the no-mutation invariants.
/// </summary>
public sealed class MetadataValidateCommandTests : IDisposable
{
    public MetadataValidateCommandTests()
    {
        MetadataValidateCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        MetadataValidateCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_GivenParentHostSchemaShape_ReturnsValidWithExitZero()
    {
        // G207 follow-up regression for #519 review:
        // The actual parent-host packet schema nests fields under
        // `implementation_issue_packet:` and `issue:`, and uses `state` /
        // object-shaped `linked_issue` / `linked_pr` in queue-state.json.
        // The validator must recognize that real shape — earlier fixtures
        // used flat keys and missed this contract.
        using var ws = new MetadataValidateWorkspace();
        var unitDir = Path.Combine(ws.RootPath, ".intent-cli", "issues", "G207");
        Directory.CreateDirectory(unitDir);

        File.WriteAllText(Path.Combine(unitDir, "packet.yaml"), """
            implementation_issue_packet:
              issue_title: "G207 Add intent packet metadata validation command"
              issue_kind: feature
              source_execution_unit: G207
              goal: "Add a deterministic local intent-cli command..."
              target_repo: J-Tech-Japan/intent-system
              target_path: .
              target_part: "intent-cli metadata validation"
            """);

        File.WriteAllText(Path.Combine(unitDir, "github-body.md"), """
            ## Goal
            ...
            ## Why This Slice Exists Now
            ...
            ## Current Observed State
            ...
            ## Accepted Baseline You May Assume
            ...
            ## Target Repo / Path / Part
            ...
            ## In Scope
            ...
            ## Out Of Scope
            ...
            ## Acceptance Criteria
            ...
            ## Verification
            ...
            ## Related Links
            ...
            """);

        File.WriteAllText(Path.Combine(unitDir, "review-context.md"),
            "## Execution Unit\nG207\n## Child Repo\n...\n## Linked Issue\n...\n"
            + "## Linked PR\n...\n## Accepted Baseline\n...\n"
            + "## Deterministic Review Checks\n...\n## Closeout Lookahead\n...\n");

        File.WriteAllText(Path.Combine(unitDir, "implementation.md"), "notes\n");

        // Real host publish.yaml shape: nested issue.{number,url}.
        File.WriteAllText(Path.Combine(unitDir, "publish.yaml"), """
            execution_unit: G207
            issue:
              number: 519
              url: https://github.com/J-Tech-Japan/intent-system/issues/519
              status: published
              intent_target_label_applied: true
              published_at: 2026-04-30T07:05:32Z
            """);

        // Real host queue-state.json shape: top-level items[] with
        // object-shaped linked_issue (no linked_pr while queued).
        var queueState = """
            {
              "schema_version": "1",
              "updated_at": "2026-04-30T07:20:25Z",
              "items": [
                {
                  "execution_unit": "G207",
                  "title": "G207 Add intent packet metadata validation command",
                  "state": "queued",
                  "dependencies": ["G205", "G206"],
                  "linked_issue": {
                    "repo": "J-Tech-Japan/intent-system",
                    "number": 519,
                    "url": "https://github.com/J-Tech-Japan/intent-system/issues/519"
                  }
                }
              ]
            }
            """;
        File.WriteAllText(
            Path.Combine(ws.RootPath, ".intent-cli", "queue-state.json"),
            queueState);

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G207", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.True(result.Valid,
            $"expected the host-schema fixture to validate, errors={string.Join(", ", result.Errors.Select(e => e.Code))}");
    }

    [Fact]
    public void Execute_HostSchemaCompletedWithLinkedPrObject_IsValid()
    {
        // Host queue-state encodes a completed item's linked_pr as an
        // object with `number`. The validator must read the number from
        // the object form and not flag CompletedMissingClosure.
        using var ws = new MetadataValidateWorkspace();
        var unitDir = Path.Combine(ws.RootPath, ".intent-cli", "issues", "G177");
        Directory.CreateDirectory(unitDir);
        File.WriteAllText(Path.Combine(unitDir, "packet.yaml"), """
            implementation_issue_packet:
              issue_title: "G177 done"
              source_execution_unit: G177
            """);
        File.WriteAllText(Path.Combine(unitDir, "github-body.md"), """
            ## Goal
            ## Why This Slice Exists Now
            ## Current Observed State
            ## Accepted Baseline You May Assume
            ## Target Repo / Path / Part
            ## In Scope
            ## Out Of Scope
            ## Acceptance Criteria
            ## Verification
            ## Related Links
            """);
        File.WriteAllText(Path.Combine(unitDir, "review-context.md"),
            "## Execution Unit\n## Child Repo\n## Linked Issue\n## Linked PR\n"
            + "## Accepted Baseline\n## Deterministic Review Checks\n## Closeout Lookahead\n");
        File.WriteAllText(Path.Combine(unitDir, "implementation.md"), "x\n");
        File.WriteAllText(
            Path.Combine(ws.RootPath, ".intent-cli", "queue-state.json"),
            """
            {
              "items": [
                {
                  "execution_unit": "G177",
                  "state": "completed",
                  "linked_issue": { "number": 459 },
                  "linked_pr":    { "number": 460 }
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G177", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.True(result.Valid,
            $"expected completed object-shaped linked_pr to validate, errors={string.Join(", ", result.Errors.Select(e => e.Code))}");
        Assert.DoesNotContain(result.Errors, e =>
            e.Code == MetadataValidateConstants.Codes.CompletedMissingClosure);
    }

    [Fact]
    public void Execute_GivenShippedLegacyPublishAndLinkedPrUrl_IsValidWithCompatibilityWarnings()
    {
        // G715: these are the durable keys emitted by the shipped issue
        // publish / closeout workflows, not the newer validator-only shape.
        const string executionUnit = "SKS-G890";
        const int issueNumber = 1549;
        const int prNumber = 1550;
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket(executionUnit, issueNumber, linkedPr: null, status: "completed");

        var unitDir = Path.Combine(ws.RootPath, ".intent-cli", "issues", executionUnit);
        File.WriteAllText(Path.Combine(unitDir, "publish.yaml"), $"""
            publish_status: issue-created
            created_issue_number: {issueNumber}
            created_issue_url: "https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}"
            """);

        var queueEntry = new Dictionary<string, object?>
        {
            ["execution_unit"] = executionUnit,
            ["state"] = "completed",
            ["linked_issue"] = new Dictionary<string, object?>
            {
                ["repo"] = "J-Tech-Japan/intent-system",
                ["number"] = issueNumber,
                ["url"] = $"https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}",
            },
            ["linked_pr"] = $"https://github.com/J-Tech-Japan/intent-system/pull/{prNumber}",
        };
        File.WriteAllText(
            Path.Combine(ws.RootPath, ".intent-cli", "queue-state.json"),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["items"] = new[] { queueEntry },
            }));
        File.WriteAllText(
            Path.Combine(ws.RootPath, ".intent-cli", "runs.jsonl"),
            BuildCloseoutRuns(executionUnit, prNumber, includeLinkageRecovery: true));

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", executionUnit, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, finding =>
            finding.Code == MetadataValidateConstants.Codes.PublishLegacyIssueIdentity
            && finding.Message.Contains("superseded", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, finding =>
            finding.Code == MetadataValidateConstants.Codes.QueueLegacyLinkedPrUrl
            && finding.Message.Contains("superseded", StringComparison.Ordinal));
        Assert.Contains(result.CheckedFiles, path =>
            path == ".intent-cli/runs.jsonl");
    }

    [Fact]
    public void Execute_GivenCloseoutRunsEvidenceWithoutLinkedPr_IsValid()
    {
        // The closeout events are an independent durable proof of completion;
        // a validator upgrade must not require the newer queue linkage object
        // when the shipped runs evidence is present.
        const string executionUnit = "SKS-G891";
        const int prNumber = 1551;
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket(executionUnit, linkedIssue: 1550, linkedPr: null, status: "completed");
        File.WriteAllText(
            Path.Combine(ws.RootPath, ".intent-cli", "runs.jsonl"),
            BuildCloseoutRuns(executionUnit, prNumber, includeLinkageRecovery: true));

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", executionUnit, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.True(result.Valid);
        Assert.DoesNotContain(result.Errors, finding =>
            finding.Code == MetadataValidateConstants.Codes.CompletedMissingClosure);
        Assert.Contains(result.Warnings, finding =>
            finding.Code == MetadataValidateConstants.Codes.RunsCloseoutEvidence
            && finding.Message.Contains("closeout-recorded", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_HostSchemaPublishQueueIssueMismatch_DetectsAcrossNestedShapes()
    {
        // Cross-file consistency must still fire when publish.yaml's
        // nested `issue.number` disagrees with queue-state's object-shaped
        // `linked_issue.number`.
        using var ws = new MetadataValidateWorkspace();
        var unitDir = Path.Combine(ws.RootPath, ".intent-cli", "issues", "G207");
        Directory.CreateDirectory(unitDir);
        File.WriteAllText(Path.Combine(unitDir, "packet.yaml"), """
            implementation_issue_packet:
              issue_title: "G207 mismatch"
              source_execution_unit: G207
            """);
        File.WriteAllText(Path.Combine(unitDir, "github-body.md"), """
            ## Goal
            ## Why This Slice Exists Now
            ## Current Observed State
            ## Accepted Baseline You May Assume
            ## Target Repo / Path / Part
            ## In Scope
            ## Out Of Scope
            ## Acceptance Criteria
            ## Verification
            ## Related Links
            """);
        File.WriteAllText(Path.Combine(unitDir, "review-context.md"),
            "## Execution Unit\n## Child Repo\n## Linked Issue\n## Linked PR\n"
            + "## Accepted Baseline\n## Deterministic Review Checks\n## Closeout Lookahead\n");
        File.WriteAllText(Path.Combine(unitDir, "implementation.md"), "x\n");
        File.WriteAllText(Path.Combine(unitDir, "publish.yaml"), """
            execution_unit: G207
            issue:
              number: 999
              url: https://github.com/example/repo/issues/999
              status: published
            """);
        File.WriteAllText(
            Path.Combine(ws.RootPath, ".intent-cli", "queue-state.json"),
            """
            {
              "items": [
                {
                  "execution_unit": "G207",
                  "state": "queued",
                  "linked_issue": { "number": 519 }
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G207", "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e =>
            e.Code == MetadataValidateConstants.Codes.PublishQueueIssueMismatch);
    }

    [Fact]
    public void Execute_GivenValidMetadata_ReturnsValidWithExitZero()
    {
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.True(result.Valid);
        Assert.Equal("G206", result.ExecutionUnit);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Execute_GivenMissingPacketFile_ReturnsErrorAndExitOne()
    {
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");
        // Remove just the packet.yaml.
        File.Delete(Path.Combine(ws.RootPath, ".intent-cli", "issues", "G206", "packet.yaml"));

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e =>
            e.Code == MetadataValidateConstants.Codes.PacketFileMissing);
    }

    [Fact]
    public void Execute_GivenGithubBodyMissingSection_ReturnsError()
    {
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");
        // Overwrite github-body.md to omit "Acceptance Criteria".
        var bodyPath = Path.Combine(ws.RootPath, ".intent-cli", "issues", "G206", "github-body.md");
        File.WriteAllText(bodyPath, """
            ## Goal
            ...
            ## Why This Slice Exists Now
            ...
            ## Current Observed State
            ...
            ## Accepted Baseline You May Assume
            ...
            ## Target Repo / Path / Part
            ...
            ## In Scope
            ...
            ## Out Of Scope
            ...
            ## Verification
            ...
            ## Related Links
            ...
            """);

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e =>
            e.Code == MetadataValidateConstants.Codes.GithubBodyMissingSection
            && e.Message.Contains("Acceptance Criteria", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenNonStandaloneGithubBodyHeading_ReturnsMissingSectionError()
    {
        // PR #824 review repair #6: substring matching on github-body
        // section headings accepted non-standalone variants like
        // `## My Goal` or `## Goal - notes`. The exact-match (plus
        // compound `<section> / suffix` tolerance) rejects those so a
        // packet cannot pass the contract without the required exact
        // sections. `## My Goal` does not equal `Goal` and does not
        // start with `Goal /`, so it is flagged.
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");
        var bodyPath = Path.Combine(ws.RootPath, ".intent-cli", "issues", "G206", "github-body.md");
        File.WriteAllText(bodyPath, """
            ## My Goal
            ...
            ## Why This Slice Exists Now
            ...
            ## Current Observed State
            ...
            ## Accepted Baseline You May Assume
            ...
            ## Target Repo / Path / Part
            ...
            ## In Scope
            ...
            ## Out Of Scope
            ...
            ## Acceptance Criteria
            ...
            ## Verification
            ...
            ## Related Links
            ...
            """);

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e =>
            e.Code == MetadataValidateConstants.Codes.GithubBodyMissingSection
            && e.Message.Contains("'Goal'", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenPublishQueueIssueMismatch_ReturnsError()
    {
        using var ws = new MetadataValidateWorkspace();
        // queue-state says issue 517; publish.yaml says issue 999. That's
        // a hard inconsistency.
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");
        var publishPath = Path.Combine(ws.RootPath, ".intent-cli", "issues", "G206", "publish.yaml");
        File.WriteAllText(publishPath, """
            execution_unit: G206
            issue_number: 999
            issue_url: https://github.com/example/repo/issues/999
            status: published
            """);

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e =>
            e.Code == MetadataValidateConstants.Codes.PublishQueueIssueMismatch);
    }

    [Fact]
    public void Execute_GivenCompletedQueueItemWithoutLinkedPr_ReturnsError()
    {
        using var ws = new MetadataValidateWorkspace();
        // status=completed but linked_pr null → missing closeout evidence.
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: null, status: "completed");

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        var finding = Assert.Single(result.Errors, e =>
            e.Code == MetadataValidateConstants.Codes.CompletedMissingClosure);
        Assert.Contains("genuinely incomplete", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenLabelPolicyMisplacedPrCreated_EmitsWarningButStillValidIfNoErrors()
    {
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");
        // Misplaced label policy: intent-pr-created on the PR position.
        var publishPath = Path.Combine(ws.RootPath, ".intent-cli", "issues", "G206", "publish.yaml");
        File.WriteAllText(publishPath, """
            execution_unit: G206
            issue_number: 517
            issue_url: https://github.com/example/repo/issues/517
            status: published
            pr_label: intent-pr-created
            """);

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.True(result.Valid);
        Assert.Contains(result.Warnings, w =>
            w.Code == MetadataValidateConstants.Codes.LabelPolicyMisplacedPrCreated);
    }

    [Fact]
    public void Execute_GivenQueueStateMissing_ReturnsErrorWithExitOne()
    {
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");
        File.Delete(Path.Combine(ws.RootPath, ".intent-cli", "queue-state.json"));

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.NotEqual(0, exitCode);
        var result = JsonSerializer.Deserialize<MetadataValidateResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e =>
            e.Code == MetadataValidateConstants.Codes.QueueStateMissing);
    }

    [Fact]
    public void Execute_MissingExecutionUnit_ReturnsNonZero()
    {
        using var ws = new MetadataValidateWorkspace();
        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--execution-unit is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TextFormat_RendersStableSections()
    {
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: null, status: "completed");

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206" },
            writer);

        Assert.NotEqual(0, exitCode);
        var raw = writer.ToString();
        Assert.Contains("# Metadata validation for G206", raw, StringComparison.Ordinal);
        Assert.Contains("- valid: false", raw, StringComparison.Ordinal);
        Assert.Contains("## Errors", raw, StringComparison.Ordinal);
        Assert.Contains("## Checked files", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutput_IncludesCamelCaseAliasesForExecutionUnitAndCheckedFiles()
    {
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");

        using var writer = new StringWriter();
        var exitCode = MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var raw = writer.ToString();
        Assert.Contains("\"execution_unit\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"executionUnit\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"checked_files\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"checkedFiles\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");
        var launcherInvoked = false;
        MetadataValidateCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        // Walk both valid and invalid paths.
        Assert.Equal(0, MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer));

        File.Delete(Path.Combine(ws.RootPath, ".intent-cli", "issues", "G206", "packet.yaml"));
        writer.GetStringBuilder().Clear();
        Assert.NotEqual(0, MetadataValidateCommand.Execute(
            ws.Context,
            new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
            writer));

        Assert.False(launcherInvoked,
            "MetadataValidateCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_LeavesPacketWorkspaceByteEquivalent()
    {
        // The validator is read-only — no file may be created or modified
        // by execution. Snapshot the entire packet workspace before and
        // after a couple of validation passes.
        using var ws = new MetadataValidateWorkspace();
        ws.WriteValidPacket("G206", linkedIssue: 517, linkedPr: 518, status: "completed");
        var before = ws.SnapshotWorkspace();

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, MetadataValidateCommand.Execute(
                ws.Context,
                new[] { "--root", ws.RootPath, "--execution-unit", "G206", "--format", "json" },
                writer));
            Assert.Equal(0, MetadataValidateCommand.Execute(
                ws.Context,
                new[] { "--root", ws.RootPath, "--execution-unit", "G206" },
                writer));
        }

        var after = ws.SnapshotWorkspace();
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterHash),
                $"file disappeared after run: {path}");
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void SourceScan_AnalyzerAndCommand_ContainNoProcessStartOrGhMutationLiterals()
    {
        // Strip C# comments before scanning so the doc-comments naming
        // forbidden mutations do not cause false positives.
        var analyzer = StripCsharpComments(File.ReadAllText(LocateSourceFile("MetadataValidateAnalyzer.cs")));
        var command = StripCsharpComments(File.ReadAllText(LocateSourceFile("MetadataValidateCommand.cs")));
        var combined = analyzer + "\n" + command;

        Assert.DoesNotContain("Process.Start(", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue edit", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr edit", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr merge", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr close", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr reopen", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr comment", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr review", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveReviewThread", combined, StringComparison.Ordinal);
    }

    private static string StripCsharpComments(string source)
    {
        var noBlock = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*[\s\S]*?\*/", string.Empty);
        var noLine = System.Text.RegularExpressions.Regex.Replace(
            noBlock, @"//.*?$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return noLine;
    }

    private static string BuildCloseoutRuns(
        string executionUnit,
        int prNumber,
        bool includeLinkageRecovery)
    {
        var events = new List<string>
        {
            $"{{\"ts\":\"2026-08-18T00:00:00Z\",\"execution_unit\":\"{executionUnit}\",\"event\":\"pr-merged\",\"by\":\"intent-cli closeout pr\",\"repo\":\"J-Tech-Japan/intent-system\",\"pr\":{prNumber}}}",
            $"{{\"ts\":\"2026-08-18T00:01:00Z\",\"execution_unit\":\"{executionUnit}\",\"event\":\"closeout-recorded\",\"by\":\"intent-cli closeout pr\",\"repo\":\"J-Tech-Japan/intent-system\",\"pr\":{prNumber}}}",
        };
        if (includeLinkageRecovery)
        {
            events.Add(
                $"{{\"ts\":\"2026-08-18T00:02:00Z\",\"execution_unit\":\"{executionUnit}\",\"event\":\"linkage-recovered\",\"by\":\"intent-cli review closeout-plan\",\"repo\":\"J-Tech-Japan/intent-system\",\"pr\":{prNumber}}}");
        }
        return string.Join(Environment.NewLine, events) + Environment.NewLine;
    }

    private static string LocateSourceFile(string fileName)
    {
        var directory = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(directory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "IntentSystem.Cli", "Commands", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate source file {fileName} from {directory}");
    }

    private sealed class MetadataValidateWorkspace : IDisposable
    {
        public MetadataValidateWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("metadata-validate-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli", "issues"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        /// <summary>
        /// Writes a coherent packet bundle for the given execution unit so
        /// each test only has to mutate the one file the test cares about.
        /// </summary>
        public void WriteValidPacket(
            string executionUnit,
            int linkedIssue,
            int? linkedPr,
            string status)
        {
            var unitDir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(unitDir);

            File.WriteAllText(Path.Combine(unitDir, "packet.yaml"), $"""
                execution_unit: {executionUnit}
                title: {executionUnit} valid packet
                target_repo: J-Tech-Japan/intent-system
                """);

            File.WriteAllText(Path.Combine(unitDir, "github-body.md"), """
                ## Goal
                Goal text.

                ## Why This Slice Exists Now
                Why text.

                ## Current Observed State
                State text.

                ## Accepted Baseline You May Assume
                Baseline text.

                ## Target Repo / Path / Part
                J-Tech-Japan/intent-system

                ## In Scope
                Scope text.

                ## Out Of Scope
                Not in scope.

                ## Acceptance Criteria
                Criteria.

                ## Verification
                Verification text.

                ## Related Links
                - link
                """);

            File.WriteAllText(Path.Combine(unitDir, "review-context.md"), """
                ## Execution Unit
                G206

                ## Child Repo
                J-Tech-Japan/intent-system

                ## Linked Issue
                #517

                ## Linked PR
                #518

                ## Accepted Baseline
                baseline

                ## Deterministic Review Checks
                checks

                ## Closeout Lookahead
                lookahead
                """);

            File.WriteAllText(Path.Combine(unitDir, "implementation.md"), "Implementation notes.\n");

            File.WriteAllText(Path.Combine(unitDir, "publish.yaml"), $"""
                execution_unit: {executionUnit}
                issue_number: {linkedIssue}
                issue_url: https://github.com/example/repo/issues/{linkedIssue}
                status: published
                """);

            // Construct the queue-state with one entry for this unit.
            var entry = new Dictionary<string, object?>
            {
                ["execution_unit"] = executionUnit,
                ["status"] = status,
                ["linked_issue"] = linkedIssue,
            };
            if (linkedPr is { } pr)
            {
                entry["linked_pr"] = pr;
            }
            var queueState = new Dictionary<string, object?>
            {
                ["entries"] = new[] { entry }
            };
            File.WriteAllText(
                Path.Combine(RootPath, ".intent-cli", "queue-state.json"),
                JsonSerializer.Serialize(queueState,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        public IReadOnlyDictionary<string, string> SnapshotWorkspace()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                snapshot[path] = hash;
            }
            return snapshot;
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
