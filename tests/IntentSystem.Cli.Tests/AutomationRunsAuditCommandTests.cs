using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G542: coverage for <c>intent-cli automation runs-audit</c> — one-pass
/// reporting of every malformed runs.jsonl row, within-record repair via
/// <c>--write</c>, and the separate, explicit <c>--apply-inferred</c> peer-
/// convention path. Regression fixtures reproduce the two 2026-07-20 field
/// incidents verbatim (a row missing `ts`+`by`; rows missing
/// `execution_unit` in both documented derivation shapes).
/// </summary>
public sealed class AutomationRunsAuditCommandTests : IDisposable
{
    public AutomationRunsAuditCommandTests()
    {
        AutomationRunsAuditCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationRunsAuditCommand.UtcNowFactory = null;
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Execute_MissingRunsLog_ReturnsCleanReport()
    {
        using var workspace = new RunsAuditWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("clean").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("malformed_row_count").GetInt32());
    }

    [Fact]
    public void Execute_AllValidRows_ReturnsCleanReport()
    {
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"ts":"2026-05-10T10:00:00Z","execution_unit":"G50","event":"issue-created","by":"issue-publish-flow"}""",
            """{"ts":"2026-05-11T10:00:00Z","execution_unit":"G51","event":"pr-merged","by":"intent-cli closeout pr"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("clean").GetBoolean());
        Assert.Equal(2, document.RootElement.GetProperty("total_rows").GetInt32());
    }

    [Fact]
    public void Execute_RegressionFixture_MissingTsAndBy_2026_07_20_Incident1()
    {
        // G542 field incident #1 (2026-05-26, discovered 2026-07-20; host
        // commit 5e011f97): the SKS-G592 `metadata-update.completed-closeout`
        // row, missing `ts` and `by`. `ts` is losslessly derivable from the
        // record's own `timestamp`; `by` has no within-record source.
        //
        // Repair round 2: the literal byte content of host commit 5e011f97
        // is outside this child implementation repo's boundary (G300/G330/
        // G333 -- child implementation loops never inspect parent host
        // state, and that commit lives in the host repo, not intent-system).
        // This fixture instead reconciles on every detail available from
        // the review itself -- the exact execution unit (SKS-G592) and
        // event name (metadata-update.completed-closeout) -- while the
        // missing-field/derivation shape matches the original field report.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"execution_unit":"SKS-G592","event":"metadata-update.completed-closeout","timestamp":"2026-05-26T10:00:00Z"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("clean").GetBoolean());
        var row = Assert.Single(document.RootElement.GetProperty("malformed_rows").EnumerateArray());
        Assert.Equal(1, row.GetProperty("line").GetInt32());
        var missingFields = row.GetProperty("missing_fields").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("ts", missingFields);
        Assert.Contains("by", missingFields);

        var repair = Assert.Single(row.GetProperty("repairs").EnumerateArray());
        Assert.Equal("ts", repair.GetProperty("field").GetString());
        Assert.Equal("2026-05-26T10:00:00Z", repair.GetProperty("value").GetString());
        Assert.Equal("within-record", repair.GetProperty("derivation").GetString());
        Assert.Equal("timestamp", repair.GetProperty("source").GetString());

        var nonDerivable = row.GetProperty("non_derivable_fields").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "by" }, nonDerivable);
    }

    [Fact]
    public void Execute_RegressionFixture_MissingExecutionUnit_FullSixteenRowBatch_BothStatusShapes_2026_07_20_Incident2()
    {
        // G542 field incident #2 (2026-06-02, discovered 2026-07-20; host
        // commit 6a5a71c0): 16 rows missing `execution_unit`, composed of
        // nine `skip-next-slice-due-to-wip` rows (wip[0].eu) and seven
        // `pr-merged-closeout` rows (stage1.eu).
        //
        // Repair round 2: the literal byte content of host commit 6a5a71c0
        // is outside this child implementation repo's boundary (G300/G330/
        // G333) -- it lives in the host repo, not intent-system, and
        // fetching it would mean inspecting parent host state a child
        // implementation loop must never touch. This fixture instead
        // reconciles on the exact composition the review itself stated
        // (nine wip-shaped rows, seven stage1-shaped rows = sixteen), with
        // each row's JSON key order deliberately varied across the batch
        // (ts-first, event-first, by-first, wip/stage1-first, and extra
        // unrelated fields interleaved) -- proving the derivation logic is
        // correct regardless of field order, since the true field order of
        // the original rows cannot be verified from here.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            // Nine skip-next-slice-due-to-wip rows (wip[0].eu source).
            """{"ts":"2026-06-02T09:00:00Z","event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)","wip":[{"eu":"G210","stage":"review"}]}""",
            """{"event":"skip-next-slice-due-to-wip","ts":"2026-06-02T09:01:00Z","by":"automation host-loop-wake (G490)","wip":[{"eu":"G211","stage":"fixing"}]}""",
            """{"by":"automation host-loop-wake (G490)","ts":"2026-06-02T09:02:00Z","event":"skip-next-slice-due-to-wip","wip":[{"eu":"G212","stage":"review"},{"eu":"G213","stage":"queued"}]}""",
            """{"ts":"2026-06-02T09:03:00Z","wip":[{"eu":"G214","stage":"review"}],"event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)"}""",
            """{"ts":"2026-06-02T09:04:00Z","event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)","wip":[{"stage":"review","eu":"G215"}]}""",
            """{"ts":"2026-06-02T09:05:00Z","event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)","wip":[{"eu":"G216","stage":"clarification"}],"reason":"WIP cap reached"}""",
            """{"ts":"2026-06-02T09:06:00Z","event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)","wip":[{"eu":"G217","stage":"review"}]}""",
            """{"ts":"2026-06-02T09:07:00Z","event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)","wip":[{"eu":"G218","stage":"fixing"}]}""",
            """{"ts":"2026-06-02T09:08:00Z","event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)","wip":[{"eu":"G219","stage":"review"}]}""",
            // Seven pr-merged-closeout rows (stage1.eu source).
            """{"ts":"2026-06-02T09:10:00Z","event":"pr-merged-closeout","by":"intent-cli closeout pr","stage1":{"eu":"G220"}}""",
            """{"event":"pr-merged-closeout","ts":"2026-06-02T09:11:00Z","by":"intent-cli closeout pr","stage1":{"eu":"G221"}}""",
            """{"by":"intent-cli closeout pr","ts":"2026-06-02T09:12:00Z","event":"pr-merged-closeout","stage1":{"eu":"G222"}}""",
            """{"ts":"2026-06-02T09:13:00Z","stage1":{"eu":"G223"},"event":"pr-merged-closeout","by":"intent-cli closeout pr"}""",
            """{"ts":"2026-06-02T09:14:00Z","event":"pr-merged-closeout","by":"intent-cli closeout pr","stage1":{"pr":1234,"eu":"G224"}}""",
            """{"ts":"2026-06-02T09:15:00Z","event":"pr-merged-closeout","by":"intent-cli closeout pr","stage1":{"eu":"G225","pr":1235}}""",
            """{"ts":"2026-06-02T09:16:00Z","event":"pr-merged-closeout","by":"intent-cli closeout pr","stage1":{"eu":"G226"},"reason":"stage1 merge closeout"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var rows = document.RootElement.GetProperty("malformed_rows").EnumerateArray().ToArray();
        Assert.Equal(16, rows.Length);
        Assert.Equal(16, document.RootElement.GetProperty("repairs_applied").GetInt32());

        var expectedWipEus = new[] { "G210", "G211", "G212", "G214", "G215", "G216", "G217", "G218", "G219" };
        var expectedStage1Eus = new[] { "G220", "G221", "G222", "G223", "G224", "G225", "G226" };

        var wipRows = rows.Where(r => r.GetProperty("line").GetInt32() is >= 1 and <= 9).ToArray();
        Assert.Equal(9, wipRows.Length);
        foreach (var row in wipRows)
        {
            var repair = Assert.Single(row.GetProperty("repairs").EnumerateArray());
            Assert.Equal("execution_unit", repair.GetProperty("field").GetString());
            Assert.Equal("within-record", repair.GetProperty("derivation").GetString());
            Assert.Equal("wip[0].eu", repair.GetProperty("source").GetString());
            Assert.Contains(repair.GetProperty("value").GetString(), expectedWipEus);
        }

        var stage1Rows = rows.Where(r => r.GetProperty("line").GetInt32() is >= 10 and <= 16).ToArray();
        Assert.Equal(7, stage1Rows.Length);
        foreach (var row in stage1Rows)
        {
            var repair = Assert.Single(row.GetProperty("repairs").EnumerateArray());
            Assert.Equal("execution_unit", repair.GetProperty("field").GetString());
            Assert.Equal("within-record", repair.GetProperty("derivation").GetString());
            Assert.Equal("stage1.eu", repair.GetProperty("source").GetString());
            Assert.Contains(repair.GetProperty("value").GetString(), expectedStage1Eus);
        }

        // Every repaired row re-audits clean, and every derived execution
        // unit landed correctly (including the multi-entry wip[] row,
        // where the FIRST entry — not a later one — must win).
        var rewritten = File.ReadAllText(workspace.RunsLogPath);
        Assert.Contains("\"execution_unit\":\"G212\"", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("\"execution_unit\":\"G213\"", rewritten, StringComparison.Ordinal);

        using var secondWriter = new StringWriter();
        var secondExitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], secondWriter);
        Assert.Equal(0, secondExitCode);
        using var secondDocument = JsonDocument.Parse(secondWriter.ToString());
        // Only the 16 repaired rows plus their runs-repair audit events —
        // all clean of missing required fields now.
        Assert.True(secondDocument.RootElement.GetProperty("clean").GetBoolean());
    }

    [Fact]
    public void Execute_MissingByOnly_ReportsInferredSuggestion_FromUnanimousPeerConvention()
    {
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"ts":"2026-05-10T10:00:00Z","execution_unit":"G50","event":"issue-created","by":"issue-publish-flow"}""",
            """{"ts":"2026-05-11T10:00:00Z","execution_unit":"G51","event":"issue-created","by":"issue-publish-flow"}""",
            """{"ts":"2026-05-26T10:00:00Z","execution_unit":"G278","event":"issue-created"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var row = Assert.Single(document.RootElement.GetProperty("malformed_rows").EnumerateArray());
        Assert.Empty(row.GetProperty("repairs").EnumerateArray());
        Assert.Equal(new[] { "by" }, row.GetProperty("non_derivable_fields").EnumerateArray().Select(e => e.GetString()));

        var suggestion = Assert.Single(row.GetProperty("inferred_suggestions").EnumerateArray());
        Assert.Equal("by", suggestion.GetProperty("field").GetString());
        Assert.Equal("issue-publish-flow", suggestion.GetProperty("value").GetString());
        Assert.Contains("all 2 peer record(s)", suggestion.GetProperty("evidence").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Write_AppliesOnlyWithinRecordRepairs_PreservesUnrelatedBytes_ByteDiff()
    {
        using var workspace = new RunsAuditWorkspace();
        const string validLine = """{"ts":"2026-05-10T10:00:00Z","execution_unit":"G50","event":"issue-created","by":"issue-publish-flow"}""";
        const string malformedLine = """{"execution_unit":"G278","event":"issue-created","timestamp":"2026-05-26T10:00:00Z"}""";
        workspace.WriteRunsLog(validLine, malformedLine);

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var content = File.ReadAllText(workspace.RunsLogPath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // The valid first line is byte-for-byte unchanged.
        Assert.Equal(validLine, lines[0]);

        // The repaired line keeps every original byte of the malformed
        // line — only the missing `ts` is inserted right after the
        // opening brace; `by` (non-derivable) is left absent.
        Assert.Equal(
            """{"ts":"2026-05-26T10:00:00Z","execution_unit":"G278","event":"issue-created","timestamp":"2026-05-26T10:00:00Z"}""",
            lines[1]);
        Assert.Contains(malformedLine.TrimStart('{'), lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\"by\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Write_AppendsOneRunsRepairEventPerRepair_RecordingDerivationClass()
    {
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"execution_unit":"G278","event":"issue-created","timestamp":"2026-05-26T10:00:00Z"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var lines = File.ReadAllText(workspace.RunsLogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // original (patched) row + one runs-repair event (by stays non-derivable, no --apply-inferred)

        using var repairEvent = JsonDocument.Parse(lines[1]);
        Assert.Equal("runs-repair", repairEvent.RootElement.GetProperty("event").GetString());
        Assert.Equal("G278", repairEvent.RootElement.GetProperty("execution_unit").GetString());
        Assert.Equal("intent-cli automation runs-audit", repairEvent.RootElement.GetProperty("by").GetString());
        var reason = repairEvent.RootElement.GetProperty("reason").GetString();
        Assert.Contains("line 1", reason, StringComparison.Ordinal);
        Assert.Contains("ts", reason, StringComparison.Ordinal);
        Assert.Contains("derivation: within-record", reason, StringComparison.Ordinal);
        Assert.Equal(FixedNow, repairEvent.RootElement.GetProperty("ts").GetDateTimeOffset());
    }

    [Fact]
    public void Execute_Write_NeverAppliesByField_WithoutApplyInferred()
    {
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"ts":"2026-05-10T10:00:00Z","execution_unit":"G50","event":"issue-created","by":"issue-publish-flow"}""",
            """{"ts":"2026-05-11T10:00:00Z","execution_unit":"G51","event":"issue-created","by":"issue-publish-flow"}""",
            """{"ts":"2026-05-26T10:00:00Z","execution_unit":"G278","event":"issue-created"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var content = File.ReadAllText(workspace.RunsLogPath);
        // `by` is never written by --write alone: the third row stays
        // exactly as it was, no runs-repair event appended for it.
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain("runs-repair", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"by\":\"issue-publish-flow\",\"ts\"", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ApplyInferred_AppliesPeerConventionSuggestion_RecordsInferredDerivation()
    {
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"ts":"2026-05-10T10:00:00Z","execution_unit":"G50","event":"issue-created","by":"issue-publish-flow"}""",
            """{"ts":"2026-05-11T10:00:00Z","execution_unit":"G51","event":"issue-created","by":"issue-publish-flow"}""",
            """{"ts":"2026-05-26T10:00:00Z","execution_unit":"G278","event":"issue-created"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--apply-inferred", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var lines = File.ReadAllText(workspace.RunsLogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length); // 3 original rows (one patched) + 1 runs-repair event (only `by` needed repair; `ts` already present)

        using var patchedRow = JsonDocument.Parse(lines[2]);
        Assert.Equal("issue-publish-flow", patchedRow.RootElement.GetProperty("by").GetString());

        using var repairEvent = JsonDocument.Parse(lines[3]);
        Assert.Equal("runs-repair", repairEvent.RootElement.GetProperty("event").GetString());
        Assert.Contains("derivation: inferred-peer-convention", repairEvent.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ApplyInferredWithoutWrite_ReturnsUsageError()
    {
        using var workspace = new RunsAuditWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--apply-inferred", "--format", "json"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--apply-inferred requires --write", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnparseableJsonLine_ReportedMalformed_NeverRepaired()
    {
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog("not-json-at-all { garbage");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var row = Assert.Single(document.RootElement.GetProperty("malformed_rows").EnumerateArray());
        Assert.True(row.GetProperty("unparseable").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("repairs_applied").GetInt32());

        // The garbage line is left completely untouched.
        Assert.Equal("not-json-at-all { garbage", File.ReadAllText(workspace.RunsLogPath).TrimEnd('\n'));
    }

    [Fact]
    public void Execute_RowWithAllFourKeysPresent_ButInvalidTsValue_IsNeverSilentlyDropped_G542Repair1()
    {
        // G542 repair round 1: a row can have all four required KEYS
        // present as non-empty JSON strings (so RunsLogRowInspector's own
        // narrower presence check sees nothing wrong) yet still fail
        // RunLogSerializer.DeserializeLine -- the REAL contract publish-
        // flow enforces -- because a value fails to parse, e.g. `ts` is
        // not a real timestamp. Before this repair, that row vanished
        // from the audit (and could report a false-clean audit) even
        // though publish-flow correctly rejected the same row. It must
        // now always survive as an offender.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"ts":"not-a-real-timestamp","execution_unit":"G300","event":"issue-created","by":"issue-publish-flow"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("clean").GetBoolean());
        var row = Assert.Single(document.RootElement.GetProperty("malformed_rows").EnumerateArray());
        Assert.Equal(1, row.GetProperty("line").GetInt32());
        Assert.True(row.GetProperty("unparseable").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("unparseable_detail").GetString()));
        Assert.Empty(row.GetProperty("repairs").EnumerateArray());
    }

    [Fact]
    public void Execute_Write_PreservesUtf8BomAndCrlfLineEndings_ByteExact_G542Repair2()
    {
        // G542 repair round 2: reading with File.ReadAllText auto-detects
        // and silently strips a UTF-8 BOM, and the default WriteAllText
        // encoding never re-adds one -- a plain string-equality assertion
        // cannot detect that loss. This fixture writes a raw UTF-8 BOM
        // plus CRLF line endings at the byte level, repairs one row via
        // --write, and re-reads the file as raw bytes to prove the BOM,
        // every line ending, and every unrelated byte survive untouched.
        using var workspace = new RunsAuditWorkspace();
        const string validLine = """{"ts":"2026-05-10T10:00:00Z","execution_unit":"G50","event":"issue-created","by":"issue-publish-flow"}""";
        const string malformedLine = """{"execution_unit":"G278","event":"issue-created","timestamp":"2026-05-26T10:00:00Z"}""";

        var utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var contentBytes = Encoding.UTF8.GetBytes(validLine + "\r\n" + malformedLine + "\r\n");
        File.WriteAllBytes(workspace.RunsLogPath, utf8Bom.Concat(contentBytes).ToArray());

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var resultBytes = File.ReadAllBytes(workspace.RunsLogPath);

        // The BOM is still the first three bytes.
        Assert.Equal(utf8Bom, resultBytes[..3]);

        var resultText = Encoding.UTF8.GetString(resultBytes, 3, resultBytes.Length - 3);
        var lines = resultText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // The valid first line is byte-for-byte unchanged, and CRLF (not
        // LF) endings are preserved throughout, including after the
        // repaired line and the appended runs-repair audit event.
        Assert.Equal(validLine, lines[0]);
        Assert.Contains(malformedLine.TrimStart('{'), lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\n", resultText.Replace("\r\n", string.Empty), StringComparison.Ordinal);
        Assert.EndsWith("\r\n", Encoding.UTF8.GetString(resultBytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_OwningDomain_ResolvedFromPacketYaml()
    {
        using var workspace = new RunsAuditWorkspace();
        workspace.WritePacketDomain("G500", "sekiban-as-a-service");
        workspace.WriteRunsLog(
            """{"execution_unit":"G500","event":"issue-created","timestamp":"2026-06-01T00:00:00Z"}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var row = Assert.Single(document.RootElement.GetProperty("malformed_rows").EnumerateArray());
        Assert.Equal("sekiban-as-a-service", row.GetProperty("owning_domain").GetString());
        Assert.Contains("packet.yaml", row.GetProperty("owning_domain_detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new RunsAuditWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--help"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("automation runs-audit", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var workspace = new RunsAuditWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--surprise"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class RunsAuditWorkspace : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("runs-audit-tests-").FullName;

        public RunsAuditWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
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

        public CliContext Context { get; }

        public string RunsLogPath => Path.Combine(rootPath, ".intent-cli", "runs.jsonl");

        public void WriteRunsLog(params string[] lines)
        {
            File.WriteAllText(RunsLogPath, string.Join('\n', lines) + "\n");
        }

        public void WritePacketDomain(string executionUnit, string domain)
        {
            var dir = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "packet.yaml"), $"domain: {domain}\nexecution_unit: {executionUnit}\n");
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
