using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

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
        // Repair round 2: the review supplied the exact parent preimage
        // literal through the durable GitHub review-comment channel (PR
        // #1187, comment id 5019669602) precisely so this child worker does
        // not need to inspect parent host state to reproduce it byte-for-
        // byte -- host commit 5e011f97 itself remains out of reach under
        // the G300/G330/G333 boundary, but its content does not, once
        // handed over through that channel. This is the exact literal row.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"event":"metadata-update.completed-closeout","execution_unit":"SKS-G592","linked_pr_number":1304,"timestamp":"2026-05-26T21:15:46Z","linked_pr_repo":"J-Tech-Japan/SekibanAsAService","linked_pr_url":"https://github.com/J-Tech-Japan/SekibanAsAService/pull/1304","merge_commit":"9cd68df898b3633489c1f1d6e55ca72d1051da0e"}""");

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
        Assert.Equal("2026-05-26T21:15:46Z", repair.GetProperty("value").GetString());
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
        // nine "skip-next-slice-due-to-wip" rows (wip[0].eu) and seven
        // "pr-merged-closeout" rows (stage1.eu).
        //
        // Repair round 2: the review supplied these as the exact parent
        // preimage literals through the durable GitHub review-comment
        // channel (PR #1187, comment id 5019669602), so this child worker
        // does not need to inspect parent host state (host commit 6a5a71c0
        // itself stays out of reach under G300/G330/G333) to reproduce them
        // byte-for-byte. These are the 16 rows verbatim, in their original
        // order.
        //
        // Repair round 2 also revealed the real legacy discriminator shape:
        // every row's own `event` is the literal string "wake-summary" —
        // the branch (wip[0].eu vs stage1.eu) is selected by that record's
        // `status` field, NOT by `event` itself. None of these 16 rows
        // carry `execution_unit` at all.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            /* line 1  wip -> SKS-G675 */ """{"ts": "2026-06-02T16:34:31Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-intent-target-pr", "wip": [{"issue": 1469, "eu": "SKS-G675", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs to review. WIP non-empty: issue #1469 (SKS-G675) is intent-issue-in-progress (child worker engaged, no draft PR yet). Per WIP cap, no new next-slice published. No-Push-On-Idle: wake-summary local only."}""",
            /* line 2  wip -> SKS-G675 */ """{"ts": "2026-06-02T16:39:15Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-pr", "wip": [{"issue": 1469, "eu": "SKS-G675", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs. WIP non-empty: #1469 (SKS-G675) still intent-issue-in-progress, no draft PR yet. No new next-slice. No-Push-On-Idle local only."}""",
            /* line 3  stage1 -> SKS-G675 */ """{"ts": "2026-06-02T16:50:05Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "pr-merged-closeout", "stage1": {"pr": 1470, "eu": "SKS-G675", "ci": "33-pass-2-skip-0-fail", "head": "d3f5ac0", "merge": "455ccd9"}, "message": "Reviewed+merged PR #1470 (SKS-G675): real non-mock Decider backend proves provisioned service flows into developer-config handoff; endpoint extracted byte-for-byte to LocalDeveloperConfigHandoff.Build; evidence productReady=false (G672 honest); no secret/G612 leak; non-G675 files byte-identical to main (no regression). Issue #1469 closed, submodule synced to 455ccd9."}""",
            /* line 4  wip -> SKS-G676 */ """{"ts": "2026-06-02T17:01:17Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-pr", "wip": [{"issue": 1471, "eu": "SKS-G676", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs. WIP non-empty: #1471 (SKS-G676) intent-issue-in-progress (child worker engaged), no draft PR yet. No new next-slice. No-Push-On-Idle local only."}""",
            /* line 5  stage1 -> SKS-G676 */ """{"ts": "2026-06-02T17:09:44Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "pr-merged-closeout", "stage1": {"pr": 1472, "eu": "SKS-G676", "ci": "32-pass-2-skip-0-fail", "head": "d7689957", "merge": "f011cc6"}, "message": "Reviewed+merged PR #1472 (SKS-G676): G672 readiness gate hardened so productReady=(overall==ready AND live_connected); static/in-memory-only never promotes; committed CI evidence productReady=false, finding readiness-withheld-no-connected-live-proof; gate test enforces script logic; non-G676 files byte-identical to main (no regression); no secret/G612 leak. Issue #1471 closed, submodule synced to f011cc6."}""",
            /* line 6  wip -> SKS-G677 */ """{"ts": "2026-06-02T17:20:15Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-pr", "wip": [{"issue": 1473, "eu": "SKS-G677", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs. WIP non-empty: #1473 (SKS-G677) intent-issue-in-progress (child worker engaged), no draft PR yet. No new next-slice. No-Push-On-Idle local only."}""",
            /* line 7  wip -> SKS-G677 */ """{"ts": "2026-06-02T17:25:16Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-pr", "wip": [{"issue": 1473, "eu": "SKS-G677", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs. WIP non-empty: #1473 (SKS-G677) still intent-issue-in-progress, no draft PR yet. No new next-slice. No-Push-On-Idle local only."}""",
            /* line 8  wip -> SKS-G677 */ """{"ts": "2026-06-02T17:30:14Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-pr", "wip": [{"issue": 1473, "eu": "SKS-G677", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs. WIP non-empty: #1473 (SKS-G677) still intent-issue-in-progress, no draft PR yet. No-Push-On-Idle local only."}""",
            /* line 9  stage1 -> SKS-G677 */ """{"ts": "2026-06-02T17:38:38Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "pr-merged-closeout", "stage1": {"pr": 1474, "eu": "SKS-G677", "ci": "33-pass-2-skip-0-fail", "head": "4d991bb", "merge": "4e4552d"}, "message": "Reviewed+merged PR #1474 (SKS-G677): local product readiness exposed in management UI; UI card fully data-driven (ready only when productReady true), real endpoint no route-mock; UI evidence cross-checks readiness.productReady==g672.productReady and overall==ready iff exposed-live; committed CI productReady=false; non-G677 files byte-identical (no regression); no secret/G612 leak. Issue #1473 closed, submodule synced to 4e4552d."}""",
            /* line 10 wip -> SKS-G678 */ """{"ts": "2026-06-02T17:50:15Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-pr", "wip": [{"issue": 1475, "eu": "SKS-G678", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs. WIP non-empty: #1475 (SKS-G678) intent-issue-in-progress (child worker engaged), no draft PR yet. No-Push-On-Idle local only."}""",
            /* line 11 stage1 -> SKS-G678 */ """{"ts": "2026-06-02T18:03:29Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "pr-merged-closeout", "stage1": {"pr": 1476, "eu": "SKS-G678", "ci": "32-pass-2-skip-0-fail", "head": "2a402f9", "merge": "982801d"}, "message": "Reviewed+merged PR #1476 (SKS-G678): developer WASM publish/upload happy path bound to real committed proofs (G644/G652/G663/G657) read by value; overallStatus=ready is doc-path completeness only and does NOT promote productReady (G676 honest); liveReRun not-attempted-ci, blocked never faked; non-G678 files byte-identical (no regression); no secret/raw-bytes/G612 leak. Issue #1475 closed, submodule synced to 982801d."}""",
            /* line 12 wip -> SKS-G679 */ """{"ts": "2026-06-02T18:15:17Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-pr", "wip": [{"issue": 1477, "eu": "SKS-G679", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs. WIP non-empty: #1477 (SKS-G679) intent-issue-in-progress, no draft PR yet. No new next-slice. No-Push-On-Idle local only."}""",
            /* line 13 stage1 -> SKS-G679 */ """{"ts": "2026-06-02T18:25:16Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "pr-merged-closeout", "stage1": {"pr": 1478, "eu": "SKS-G679", "ci": "29-pass-2-skip-0-fail", "head": "e8aef5c", "merge": "319cf98"}, "message": "Reviewed+merged PR #1478 (SKS-G679): non-mock service-credential user-secrets handoff guide; userSecretsSeam verified against real csproj/Program.cs; no committed secret (3 files secret-free, G612 clean, secret lives outside repo in $HOME, synthetic local-rich value verified absent from tracked files); productReady=false (G672 honest); non-G679 files byte-identical (no regression). Issue #1477 closed, submodule synced to 319cf98."}""",
            /* line 14 stage1 -> SKS-G680 */ """{"ts": "2026-06-02T18:43:33Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "pr-merged-closeout", "stage1": {"pr": 1480, "eu": "SKS-G680", "ci": "18-pass-1-skip-0-fail", "head": "5a44044", "merge": "69a816f"}, "message": "Reviewed+merged PR #1480 (SKS-G680): startup-only screenshot quality gate classifies real committed PNGs by filename token and records a finding for startup-only sets (overallStatus=blocked honest, exitCode=0); does not fake startup-only as product proof; readiness links G672; non-G680 files byte-identical (no regression); secret-free/G612 clean. Issue #1479 closed, submodule synced to 69a816f."}""",
            /* line 15 wip -> SKS-G681 */ """{"ts": "2026-06-02T18:54:16Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "skip-next-slice-due-to-wip", "stage1": "no-open-pr", "wip": [{"issue": 1481, "eu": "SKS-G681", "labels": ["intent-target", "intent-issue-in-progress"]}], "local_only": true, "message": "No open PRs. WIP non-empty: #1481 (SKS-G681) intent-issue-in-progress, no draft PR yet. No-Push-On-Idle local only."}""",
            /* line 16 stage1 -> SKS-G681 */ """{"ts": "2026-06-02T19:03:28Z", "event": "wake-summary", "by": "sekiban-host-review-closeout", "status": "pr-merged-closeout", "stage1": {"pr": 1482, "eu": "SKS-G681", "ci": "32-pass-2-skip-0-fail", "head": "33bcee2", "merge": "5e39619"}, "message": "Reviewed+merged PR #1482 (SKS-G681): AppHost local-rich prerequisites aligned with proof scripts; doctor uses real docker/curl/dotnet probes (CI gated, partial), blocked prereq is a finding not faked, alignmentAudit verifies bffPort=5100 across scripts; readiness links G672, productReady=false; non-G681 files byte-identical (no regression); secret-free/G612 clean. Issue #1481 closed, submodule synced to 5e39619."}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var rows = document.RootElement.GetProperty("malformed_rows").EnumerateArray().ToArray();
        Assert.Equal(16, rows.Length);
        Assert.Equal(16, document.RootElement.GetProperty("repairs_applied").GetInt32());

        var wipLineToEu = new Dictionary<int, string>
        {
            [1] = "SKS-G675", [2] = "SKS-G675", [4] = "SKS-G676", [6] = "SKS-G677",
            [7] = "SKS-G677", [8] = "SKS-G677", [10] = "SKS-G678", [12] = "SKS-G679", [15] = "SKS-G681",
        };
        var stage1LineToEu = new Dictionary<int, string>
        {
            [3] = "SKS-G675", [5] = "SKS-G676", [9] = "SKS-G677", [11] = "SKS-G678",
            [13] = "SKS-G679", [14] = "SKS-G680", [16] = "SKS-G681",
        };
        Assert.Equal(9, wipLineToEu.Count);
        Assert.Equal(7, stage1LineToEu.Count);

        foreach (var row in rows)
        {
            var line = row.GetProperty("line").GetInt32();
            var missingFields = row.GetProperty("missing_fields").EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.Equal(new[] { "execution_unit" }, missingFields);

            var repair = Assert.Single(row.GetProperty("repairs").EnumerateArray());
            Assert.Equal("execution_unit", repair.GetProperty("field").GetString());
            Assert.Equal("within-record", repair.GetProperty("derivation").GetString());

            if (wipLineToEu.TryGetValue(line, out var wipEu))
            {
                Assert.Equal("wip[0].eu", repair.GetProperty("source").GetString());
                Assert.Equal(wipEu, repair.GetProperty("value").GetString());
            }
            else
            {
                Assert.True(stage1LineToEu.TryGetValue(line, out var stage1Eu), $"line {line} is not classified as either a wip or a stage1 row");
                Assert.Equal("stage1.eu", repair.GetProperty("source").GetString());
                Assert.Equal(stage1Eu, repair.GetProperty("value").GetString());
            }
        }

        var rewritten = File.ReadAllText(workspace.RunsLogPath);
        foreach (var eu in wipLineToEu.Values.Concat(stage1LineToEu.Values).Distinct())
        {
            Assert.Contains($"\"execution_unit\":\"{eu}\"", rewritten, StringComparison.Ordinal);
        }

        using var secondWriter = new StringWriter();
        var secondExitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], secondWriter);
        Assert.Equal(0, secondExitCode);
        using var secondDocument = JsonDocument.Parse(secondWriter.ToString());
        // Only the 16 repaired rows plus their runs-repair audit events —
        // all clean of missing required fields now.
        Assert.True(secondDocument.RootElement.GetProperty("clean").GetBoolean());
    }

    [Fact]
    public void Execute_MultiEntryWipArray_ExecutionUnitDerivedFromFirstEntryOnly_G542Repair2()
    {
        // The incident #2 batch itself (exact literals from the review) has
        // no multi-entry `wip` array, so this synthetic fixture keeps the
        // "first entry wins" invariant covered independently: a real
        // wake-summary/status row whose `wip` array has more than one
        // entry must still derive from wip[0], never a later entry.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"ts":"2026-06-02T09:00:00Z","event":"wake-summary","by":"sekiban-host-review-closeout","status":"skip-next-slice-due-to-wip","wip":[{"issue":1,"eu":"SKS-G900"},{"issue":2,"eu":"SKS-G901"}]}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var row = Assert.Single(document.RootElement.GetProperty("malformed_rows").EnumerateArray());
        var repair = Assert.Single(row.GetProperty("repairs").EnumerateArray());
        Assert.Equal("SKS-G900", repair.GetProperty("value").GetString());

        var rewritten = File.ReadAllText(workspace.RunsLogPath);
        Assert.Contains("\"execution_unit\":\"SKS-G900\"", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("\"execution_unit\":\"SKS-G901\"", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DirectEventShape_WithoutWakeSummaryWrapper_StillDerivesExecutionUnit_G542Repair2()
    {
        // Direct-event compatibility: a row whose `event` is literally
        // "skip-next-slice-due-to-wip" or "pr-merged-closeout" (no
        // wake-summary/status wrapper) must still derive execution_unit —
        // the wake-summary/status shape is an ADDITIONAL discriminator
        // path, not a replacement for matching on `event` directly.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"ts":"2026-06-02T09:00:00Z","event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)","wip":[{"eu":"G210"}]}""",
            """{"ts":"2026-06-02T09:10:00Z","event":"pr-merged-closeout","by":"intent-cli closeout pr","stage1":{"eu":"G220"}}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var rows = document.RootElement.GetProperty("malformed_rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("G210", rows[0].GetProperty("repairs").EnumerateArray().Single().GetProperty("value").GetString());
        Assert.Equal("wip[0].eu", rows[0].GetProperty("repairs").EnumerateArray().Single().GetProperty("source").GetString());
        Assert.Equal("G220", rows[1].GetProperty("repairs").EnumerateArray().Single().GetProperty("value").GetString());
        Assert.Equal("stage1.eu", rows[1].GetProperty("repairs").EnumerateArray().Single().GetProperty("source").GetString());
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
        // encoding never re-adds one -- a plain string-equality or
        // substring assertion cannot detect that loss, nor can it prove
        // every OTHER byte in the file is unchanged. This test instead
        // builds the complete expected byte array by hand -- the original
        // bytes plus ONLY the canonical `ts` field insertion and the fixed-
        // clock runs-repair event -- and compares it directly against
        // File.ReadAllBytes, so any unrelated byte drift (BOM, line
        // endings, or anything else) would fail the assertion.
        using var workspace = new RunsAuditWorkspace();
        const string validLine = """{"ts":"2026-05-10T10:00:00Z","execution_unit":"G50","event":"issue-created","by":"issue-publish-flow"}""";
        const string malformedLine = """{"execution_unit":"G278","event":"issue-created","timestamp":"2026-05-26T10:00:00Z"}""";
        const string patchedLine = """{"ts":"2026-05-26T10:00:00Z","execution_unit":"G278","event":"issue-created","timestamp":"2026-05-26T10:00:00Z"}""";

        var utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var originalBytes = utf8Bom.Concat(Encoding.UTF8.GetBytes(validLine + "\r\n" + malformedLine + "\r\n")).ToArray();
        File.WriteAllBytes(workspace.RunsLogPath, originalBytes);

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);
        Assert.Equal(0, exitCode);

        var repairEvent = RunLogSerializer.SerializeLine(new RunEvent
        {
            Ts = FixedNow,
            ExecutionUnit = "G278",
            Event = "runs-repair",
            By = "intent-cli automation runs-audit",
            Reason = "line 2: repaired ts (derivation: within-record; source: timestamp)",
        });

        // CRLF strictly dominates the original file (2 CRLF, 0 lone LF), so
        // the appended repair event also uses CRLF -- the only bytes added
        // beyond the original file's own two lines (one of which gained
        // exactly the `"ts":"...",` insertion right after its opening brace).
        var expectedContent = validLine + "\r\n" + patchedLine + "\r\n" + repairEvent + "\r\n";
        var expectedBytes = utf8Bom.Concat(Encoding.UTF8.GetBytes(expectedContent)).ToArray();

        var resultBytes = File.ReadAllBytes(workspace.RunsLogPath);
        Assert.Equal(expectedBytes, resultBytes);
    }

    [Theory]
    [InlineData(3, 1, "\r\n")] // CRLF strictly dominant (3 CRLF vs 1 lone LF) -> CRLF wins.
    [InlineData(1, 3, "\n")]   // Lone LF strictly dominant (1 CRLF vs 3 lone LF) -> LF wins.
    [InlineData(1, 1, "\n")]   // Exact tie -> deterministic tie rule picks LF.
    public void Execute_Write_SelectsTrueDominantLineEnding_ByCountingNotPresence_G542Repair2(
        int crlfCount, int loneLfCount, string expectedAppendedEnding)
    {
        // G542 repair round 2: the prior fix merely checked whether "\r\n"
        // was PRESENT anywhere in the file -- a single stray CRLF in an
        // otherwise LF-dominant file would wrongly flip the whole file's
        // appended events to CRLF. The real fix counts CRLF occurrences
        // against LONE-LF occurrences and picks whichever strictly
        // dominates, with a documented deterministic tie rule (LF wins).
        // This covers both dominance directions plus the exact-tie case.
        using var workspace = new RunsAuditWorkspace();
        const string validLine = """{"ts":"2026-05-10T10:00:00Z","execution_unit":"G50","event":"issue-created","by":"issue-publish-flow"}""";
        const string malformedLine = """{"execution_unit":"G278","event":"issue-created","timestamp":"2026-05-26T10:00:00Z"}""";

        var lines = new List<string>();
        for (var i = 0; i < crlfCount; i++)
        {
            lines.Add(validLine + "\r\n");
        }
        for (var i = 0; i < loneLfCount; i++)
        {
            lines.Add(validLine + "\n");
        }
        // The malformed row is the final line with NO trailing newline of
        // its own, so it contributes zero to either count above -- the
        // CRLF-vs-lone-LF tally is exactly (crlfCount, loneLfCount).
        var content = string.Concat(lines) + malformedLine;
        File.WriteAllBytes(workspace.RunsLogPath, Encoding.UTF8.GetBytes(content));

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--write", "--format", "json"], writer);
        Assert.Equal(0, exitCode);

        var resultText = File.ReadAllText(workspace.RunsLogPath);
        Assert.Contains("runs-repair", resultText, StringComparison.Ordinal);
        var repairLineStart = resultText.IndexOf("\"event\":\"runs-repair\"", StringComparison.Ordinal);
        Assert.True(repairLineStart > 0);

        if (expectedAppendedEnding == "\r\n")
        {
            Assert.EndsWith("\r\n", resultText, StringComparison.Ordinal);
        }
        else
        {
            Assert.EndsWith("\n", resultText, StringComparison.Ordinal);
            Assert.False(resultText.EndsWith("\r\n", StringComparison.Ordinal));
        }
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
