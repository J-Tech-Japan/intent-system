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
    public async Task R2_UnsafeBacklogEvidenceSkipsOnlyItsCandidateAndNoWindowClaimReadIsSafe()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        const string unsafePacketUnit = "G858-r2-fifo-packet";
        const string safeUnit = "G858-r2-safe-backlog";
        const string unsafeHistoryUnit = "G858-r2-fifo-claim-history";
        foreach (var unit in new[] { unsafePacketUnit, safeUnit, unsafeHistoryUnit })
        {
            workspace.WriteDebtPacket(unit);
        }

        workspace.WriteQueue([unsafePacketUnit, safeUnit]);
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        File.WriteAllText(workspace.QueueStatePath, QueueStateSerializer.Serialize(queueState with
        {
            Items = queueState.Items.Select(item => item.ExecutionUnit == unsafePacketUnit
                ? item with { ClarificationReturnPath = "intents/foreign-domain/clarifications/open.md" }
                : item).ToArray(),
        }));
        workspace.AppendEvent(new RunEvent
        {
            Ts = Now.AddMinutes(-46),
            ExecutionUnit = "G858-r2-last-activity",
            Event = "run-started",
            By = "fixture",
        });

        var unsafePacketPath = Path.Combine(workspace.Root, ".intent-cli", "issues", unsafePacketUnit, "packet.yaml");
        File.Delete(unsafePacketPath);
        Assert.Equal(0, MkFifo(unsafePacketPath, 0x180));
        Assert.False(CrossRuntimeReviewFileMode.IsRegularFile(unsafePacketPath));

        var unsafeHistoryPath = workspace.ClaimHistoryPath(unsafeHistoryUnit, "unsafe.json");
        Directory.CreateDirectory(Path.GetDirectoryName(unsafeHistoryPath)!);
        Assert.Equal(0, MkFifo(unsafeHistoryPath, 0x180));
        Assert.False(CrossRuntimeReviewFileMode.IsRegularFile(unsafeHistoryPath));

        var baselineTask = Task.Run(() => Report(workspace.Context, now: Now));
        var baselineCompleted = await Task.WhenAny(baselineTask, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(baselineTask, baselineCompleted);
        using var baseline = await baselineTask;

        var cutoff = Now.AddDays(-1);
        var windowTask = Task.Run(() => Report(workspace.Context, ["--since", cutoff.ToString("O")], now: Now));
        var windowCompleted = await Task.WhenAny(windowTask, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(windowTask, windowCompleted);
        using var window = await windowTask;

        var baselineBacklog = Assert.Single(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        var windowBacklog = Assert.Single(window.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal(safeUnit, baselineBacklog.GetProperty("execution_unit").GetString());
        Assert.Equal(safeUnit, windowBacklog.GetProperty("execution_unit").GetString());
        Assert.Equal(baselineBacklog.GetRawText(), windowBacklog.GetRawText());
        Assert.Contains(baseline.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains(unsafePacketPath, StringComparison.Ordinal));
        Assert.Contains(window.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains(unsafePacketPath, StringComparison.Ordinal));
        Assert.Contains(window.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains(unsafeHistoryPath, StringComparison.Ordinal));
        Assert.DoesNotContain(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle
            && item.GetProperty("execution_unit").GetString() is var unit
            && unit != safeUnit);
        Assert.DoesNotContain(window.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle
            && item.GetProperty("execution_unit").GetString() is var unit
            && unit != safeUnit);
    }

    [Fact]
    public async Task R2_FifoActiveClaimSkipsOnlyItsCandidateAndKeepsAnotherStaleClaim()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        const string unsafeUnit = "G858-r2-fifo-active-claim";
        const string safeUnit = "G858-r2-claim-safe-backlog";
        const string validClaimUnit = "G858-r2-valid-stale-claim";
        workspace.WriteDebtPacket(unsafeUnit);
        workspace.WriteDebtPacket(safeUnit);
        workspace.WriteQueue([unsafeUnit, safeUnit]);
        workspace.WriteClaim(validClaimUnit, Team, Now.AddDays(-20));
        workspace.AppendEvent(new RunEvent
        {
            Ts = Now.AddMinutes(-46),
            ExecutionUnit = "G858-r2-claim-last-activity",
            Event = "run-started",
            By = "fixture",
        });
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.ClaimPath(unsafeUnit))!);
        Assert.Equal(0, MkFifo(workspace.ClaimPath(unsafeUnit), 0x180));

        var baselineTask = Task.Run(() => Report(workspace.Context, now: Now, staleMinutes: 0));
        Assert.Same(baselineTask, await Task.WhenAny(baselineTask, Task.Delay(TimeSpan.FromSeconds(3))));
        using var baseline = await baselineTask;
        var windowTask = Task.Run(() => Report(workspace.Context, ["--since", Now.AddDays(-1).ToString("O")], now: Now, staleMinutes: 0));
        Assert.Same(windowTask, await Task.WhenAny(windowTask, Task.Delay(TimeSpan.FromSeconds(3))));
        using var window = await windowTask;

        var baselineBacklog = Assert.Single(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        var windowBacklog = Assert.Single(window.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal(safeUnit, baselineBacklog.GetProperty("execution_unit").GetString());
        Assert.Equal(safeUnit, windowBacklog.GetProperty("execution_unit").GetString());
        Assert.Equal(baselineBacklog.GetRawText(), windowBacklog.GetRawText());
        Assert.Contains(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale
            && item.GetProperty("execution_unit").GetString() == validClaimUnit);
        Assert.Contains(window.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale
            && item.GetProperty("execution_unit").GetString() == validClaimUnit);
        Assert.Contains(baseline.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains(workspace.ClaimPath(unsafeUnit), StringComparison.Ordinal));
        Assert.DoesNotContain(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale
            && item.GetProperty("execution_unit").GetString() == unsafeUnit);
    }

    [Fact]
    public void R2_EmptyBacklogRunLogHasTheSameNoRowsResultWithOrWithoutWindow()
    {
        using var workspace = new AdoptionWorkspace();
        const string unit = "G858-r2-empty-run-log-backlog";
        workspace.WriteDebtPacket(unit);
        workspace.WriteQueue([unit]);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunLogPath)!);
        File.WriteAllBytes(workspace.RunLogPath, []);

        using var baseline = Report(workspace.Context, now: Now);
        using var window = Report(workspace.Context, ["--since", Now.AddDays(-1).ToString("O")], now: Now);
        var baselineExclusion = Assert.Single(baseline.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        var windowExclusion = Assert.Single(window.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal(AutomationStalledWorkCommand.ReasonActivityDataUnusable, baselineExclusion.GetProperty("reason").GetString());
        Assert.Equal(baselineExclusion.GetRawText(), windowExclusion.GetRawText());
        Assert.DoesNotContain(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.DoesNotContain(window.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void R2_UnreadableReceiptsKeepProvenKnowledgeAndGuideDebtVisible()
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

        using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(3));
        Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == knowledgeUnit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == guideUnit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindGuideReachabilityPending);
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == knowledgeUnit
            && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable);
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
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
