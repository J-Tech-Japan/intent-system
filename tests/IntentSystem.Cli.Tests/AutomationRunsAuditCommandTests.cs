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
        // G542 field incident #1 (2026-05-26, discovered 2026-07-20): a row
        // missing `ts` and `by`. `ts` is losslessly derivable from the
        // record's own `timestamp`; `by` has no within-record source.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"execution_unit":"G278","event":"issue-created","timestamp":"2026-05-26T10:00:00Z"}""");

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
    public void Execute_RegressionFixture_MissingExecutionUnit_BothStatusShapes_2026_07_20_Incident2()
    {
        // G542 field incident #2 (2026-06-02, discovered 2026-07-20): 16
        // rows missing `execution_unit`, in two documented derivation
        // shapes: skip-next-slice-due-to-wip (wip[0].eu) and
        // pr-merged-closeout (stage1.eu). This fixture covers both shapes.
        using var workspace = new RunsAuditWorkspace();
        workspace.WriteRunsLog(
            """{"ts":"2026-06-02T09:00:00Z","event":"skip-next-slice-due-to-wip","by":"automation host-loop-wake (G490)","wip":[{"eu":"G200","stage":"review"}]}""",
            """{"ts":"2026-06-02T09:05:00Z","event":"pr-merged-closeout","by":"intent-cli closeout pr","stage1":{"eu":"G201"}}""");

        using var writer = new StringWriter();
        var exitCode = AutomationRunsAuditCommand.Execute(workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var rows = document.RootElement.GetProperty("malformed_rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);

        var wipRow = rows.Single(r => r.GetProperty("line").GetInt32() == 1);
        var wipRepair = Assert.Single(wipRow.GetProperty("repairs").EnumerateArray());
        Assert.Equal("execution_unit", wipRepair.GetProperty("field").GetString());
        Assert.Equal("G200", wipRepair.GetProperty("value").GetString());
        Assert.Equal("within-record", wipRepair.GetProperty("derivation").GetString());
        Assert.Equal("wip[0].eu", wipRepair.GetProperty("source").GetString());

        var stage1Row = rows.Single(r => r.GetProperty("line").GetInt32() == 2);
        var stage1Repair = Assert.Single(stage1Row.GetProperty("repairs").EnumerateArray());
        Assert.Equal("execution_unit", stage1Repair.GetProperty("field").GetString());
        Assert.Equal("G201", stage1Repair.GetProperty("value").GetString());
        Assert.Equal("within-record", stage1Repair.GetProperty("derivation").GetString());
        Assert.Equal("stage1.eu", stage1Repair.GetProperty("source").GetString());
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
