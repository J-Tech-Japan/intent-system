using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed partial class StalledWorkAdoptionWindowG858Tests
{
    [Fact]
    public void R2_CanonicalLongExecutionUnitsRemainIndexedAndUnsafeUnitsKeepDebtUnknown()
    {
        var cutoff = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        using (var workspace = new AdoptionWorkspace())
        {
            var longUnits = new[]
            {
                "G" + new string('a', 64),
                "G" + new string('b', 127),
            };
            foreach (var (unit, index) in longUnits.Select((unit, index) => (unit, index)))
            {
                workspace.WriteDebtPacket(unit);
                workspace.AppendEvent(IssueEvent(
                    unit,
                    cutoff.AddTicks(-1),
                    $"J-Tech-Japan/intent-system#{2850 + index}",
                    $"https://github.com/J-Tech-Japan/intent-system/issues/{2850 + index}"));
                workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2860 + index);
            }

            var unrelatedLongUnit = "G" + new string('c', 88);
            workspace.AppendEvent(new RunEvent
            {
                Ts = cutoff.AddDays(-2),
                ExecutionUnit = unrelatedLongUnit,
                Event = "issue-created",
                By = "fixture",
                Repo = "other-org/other-repo",
                LinkedIssue = "https://github.com/other-org/other-repo/issues/2870",
                Reason = "https://github.com/other-org/other-repo/issues/2870",
            });

            using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
            foreach (var unit in longUnits)
            {
                Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                    item.GetProperty("execution_unit").GetString() == unit
                    && item.GetProperty("reason").GetString() == "debt-window-historical"
                    && item.GetProperty("debt_window_evidence_at").GetDateTimeOffset() == cutoff.AddTicks(-1));
            }
            Assert.DoesNotContain(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
                warning.GetString()!.Contains("run-log index is incomplete", StringComparison.Ordinal));
        }

        using (var workspace = new AdoptionWorkspace())
        {
            const string unit = "G858-unsafe-unit-row";
            workspace.WriteDebtPacket(unit);
            workspace.AppendEvent(IssueEvent(unit, cutoff.AddTicks(-1), "J-Tech-Japan/intent-system#2871",
                "https://github.com/J-Tech-Japan/intent-system/issues/2871"));
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2872);
            workspace.AppendRawRunLogLine(
                $$"""{"ts":"{{cutoff:O}}","execution_unit":"../G858-unscopable","event":"issue-created","by":"fixture"}""");

            using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
            Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
            Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
                warning.GetString()!.Contains("supported event with an unsafe or missing execution-unit", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void R2_ValidSymlinkReceiptsKeepBaselineClearanceWithOrWithoutWindow()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        const string knowledgeUnit = "G858-r2-unreadable-knowledge-receipt";
        const string guideUnit = "G858-r2-unreadable-guide-receipt";
        foreach (var (unit, issueNumber, prNumber) in new[]
                 {
                     (knowledgeUnit, 2881, 2882),
                     (guideUnit, 2883, 2884),
                 })
        {
            workspace.WriteDebtPacket(unit);
            workspace.AppendEvent(IssueEvent(unit, cutoff.AddDays(1), $"J-Tech-Japan/intent-system#{issueNumber}",
                $"https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}"));
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), prNumber);
        }

        RecordWriteback(workspace.Context, knowledgeUnit, cutoff.AddDays(2));
        using (var writer = new StringWriter())
        {
            var guideExit = AutomationGuideReachabilityRecordCommand.Execute(workspace.Context,
                ["--execution-unit", guideUnit, "--commit", new string('a', 40), "--role", "design", "--write", "--format", "json"],
                writer);
            Assert.True(guideExit == 0, writer.ToString());
        }

        MakeRecordSymlink(workspace.Root, KnowledgeWriteBackRecord.RecordRootRelativePath, knowledgeUnit, "design", "knowledge");
        MakeRecordSymlink(workspace.Root, GuideReachabilityRecord.RecordRootRelativePath, guideUnit, "design", "guide");

        using var baseline = Report(workspace.Context, now: cutoff.AddDays(3));
        using var activeWindow = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(3));
        foreach (var (unit, pendingKind) in new[]
                 {
                     (knowledgeUnit, AutomationStalledWorkCommand.KindKnowledgeWritebackPending),
                     (guideUnit, AutomationStalledWorkCommand.KindGuideReachabilityPending),
                 })
        {
            Assert.DoesNotContain(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit && item.GetProperty("kind").GetString() == pendingKind);
            Assert.DoesNotContain(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit && item.GetProperty("kind").GetString() == pendingKind);
            var baselineItems = baseline.RootElement.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("execution_unit").GetString() == unit)
                .Select(item => item.GetRawText())
                .ToArray();
            var windowItems = activeWindow.RootElement.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("execution_unit").GetString() == unit)
                .Select(item => item.GetRawText())
                .ToArray();
            Assert.Equal(baselineItems, windowItems);
        }
        Assert.DoesNotContain(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == knowledgeUnit
            && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable);
        Assert.DoesNotContain(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == guideUnit
            && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonGuideReachabilityMetadataUnreadable);
    }

    private static void MakeRecordSymlink(string root, string recordRoot, string unit, string role, string label)
    {
        var relativePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(recordRoot, unit, role);
        var recordPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var targetPath = Path.Combine(root, $"{label}-receipt-target.json");
        File.Move(recordPath, targetPath);
        File.CreateSymbolicLink(recordPath, targetPath);
    }
}
