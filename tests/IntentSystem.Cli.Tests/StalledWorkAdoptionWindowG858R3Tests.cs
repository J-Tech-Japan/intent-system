using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

public sealed partial class StalledWorkAdoptionWindowG858Tests
{
    [Fact]
    public void R3_MalformedReceiptsStayDiagnosticOnlyWithoutWindowAndKeepRequiredDebtWithWindow()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var now = cutoff.AddDays(3);
        const string knowledgeUnit = "G858-r3-malformed-knowledge-receipt";
        const string guideUnit = "G858-r3-malformed-guide-receipt";
        workspace.WriteDebtPacket(knowledgeUnit, guideRoute: false);
        workspace.WriteDebtPacket(guideUnit, knowledgeRequired: false);
        workspace.AppendEvent(IssueEvent(knowledgeUnit, cutoff.AddDays(1), "J-Tech-Japan/intent-system#2890",
            "https://github.com/J-Tech-Japan/intent-system/issues/2890"));
        workspace.AppendEvent(IssueEvent(guideUnit, cutoff.AddDays(1), "J-Tech-Japan/intent-system#2891",
            "https://github.com/J-Tech-Japan/intent-system/issues/2891"));
        workspace.WriteCloseoutOnly(knowledgeUnit, cutoff.AddDays(1), 2892);
        workspace.WriteCloseoutOnly(guideUnit, cutoff.AddDays(1), 2893);

        RecordWriteback(workspace.Context, knowledgeUnit, cutoff.AddDays(2));
        using (var writer = new StringWriter())
        {
            var guideExit = AutomationGuideReachabilityRecordCommand.Execute(
                workspace.Context,
                ["--execution-unit", guideUnit, "--commit", new string('a', 40), "--role", "design", "--write", "--format", "json"],
                writer);
            Assert.True(guideExit == 0, writer.ToString());
        }

        WriteMalformedRecord(workspace.Root, KnowledgeWriteBackRecord.RecordRootRelativePath, knowledgeUnit, "design");
        WriteMalformedRecord(workspace.Root, GuideReachabilityRecord.RecordRootRelativePath, guideUnit, "design");

        using var noWindow = Report(workspace.Context, now: now);
        Assert.DoesNotContain(noWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == knowledgeUnit
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending
            || item.GetProperty("execution_unit").GetString() == guideUnit
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindGuideReachabilityPending);
        Assert.Contains(noWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == knowledgeUnit
            && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable);
        Assert.Contains(noWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == guideUnit
            && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonGuideReachabilityMetadataUnreadable);

        using var activeWindow = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: now);
        Assert.Contains(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == knowledgeUnit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.Contains(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == guideUnit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindGuideReachabilityPending);
        Assert.Contains(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == knowledgeUnit
            && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable);
        Assert.Contains(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == guideUnit
            && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonGuideReachabilityMetadataUnreadable);
    }

    [Fact]
    public void R3_ValidSymlinkStaleClaimKeepsItsLiveFindingWithOrWithoutWindow()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        const string unit = "G858-r3-symlink-stale-claim";
        workspace.WriteClaim(unit, Team, Now.AddDays(-20));
        var claimPath = workspace.ClaimPath(unit);
        var claimTargetPath = Path.Combine(workspace.Root, "stale-claim-target.json");
        File.Move(claimPath, claimTargetPath);
        File.CreateSymbolicLink(claimPath, claimTargetPath);
        Assert.True(CrossRuntimeReviewFileMode.IsSymlink(claimPath));

        using var baseline = Report(workspace.Context, now: Now, staleMinutes: 0);
        using var activeWindow = Report(workspace.Context, ["--since", Now.AddDays(-1).ToString("O")], now: Now, staleMinutes: 0);
        var baselineClaim = Assert.Single(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale
            && item.GetProperty("execution_unit").GetString() == unit);
        var activeClaim = Assert.Single(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale
            && item.GetProperty("execution_unit").GetString() == unit);
        Assert.Equal(baselineClaim.GetRawText(), activeClaim.GetRawText());
    }

    [Fact]
    public void R3_ValidSymlinkBacklogPacketAndRunLogKeepTheSameLiveCandidateWithOrWithoutWindow()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        const string unit = "G858-r3-symlink-backlog-candidate";
        workspace.WriteDebtPacket(unit);
        workspace.WriteQueue([unit]);
        workspace.AppendEvent(new RunEvent
        {
            Ts = Now.AddMinutes(-46),
            ExecutionUnit = unit,
            Event = "run-started",
            By = "fixture",
        });

        var packetPath = Path.Combine(workspace.Root, ".intent-cli", "issues", unit, "packet.yaml");
        var packetTargetPath = Path.Combine(workspace.Root, "valid-packet-target.yaml");
        File.Move(packetPath, packetTargetPath);
        File.CreateSymbolicLink(packetPath, packetTargetPath);
        var runLogTargetPath = Path.Combine(workspace.Root, "valid-run-log-target.jsonl");
        File.Move(workspace.RunLogPath, runLogTargetPath);
        File.CreateSymbolicLink(workspace.RunLogPath, runLogTargetPath);
        Assert.True(CrossRuntimeReviewFileMode.IsSymlink(packetPath));
        Assert.True(CrossRuntimeReviewFileMode.IsSymlink(workspace.RunLogPath));

        using var baseline = Report(workspace.Context, now: Now);
        using var activeWindow = Report(workspace.Context, ["--since", Now.AddDays(-1).ToString("O")], now: Now);
        var baselineBacklog = Assert.Single(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        var activeBacklog = Assert.Single(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal(unit, baselineBacklog.GetProperty("execution_unit").GetString());
        Assert.Equal(unit, activeBacklog.GetProperty("execution_unit").GetString());
        Assert.Equal(baselineBacklog.GetRawText(), activeBacklog.GetRawText());
    }

    [Fact]
    public async Task R3_FifoClaimHistoryKeepsDebtUnknownAndDoesNotRemoveUnrelatedBacklog()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        var cutoff = Now.AddDays(-1);
        const string debtUnit = "G858-r3-claim-history-debt";
        const string backlogUnit = "G858-r3-claim-history-backlog";
        workspace.WriteDebtPacket(debtUnit);
        workspace.AppendEvent(IssueEvent(debtUnit, cutoff.AddDays(-2), "J-Tech-Japan/intent-system#2894",
            "https://github.com/J-Tech-Japan/intent-system/issues/2894"));
        workspace.WriteCloseoutOnly(debtUnit, cutoff.AddDays(-1), 2895);
        workspace.WriteDebtPacket(backlogUnit);
        workspace.WriteQueue([backlogUnit]);
        workspace.AppendEvent(new RunEvent
        {
            Ts = Now.AddMinutes(-46),
            ExecutionUnit = backlogUnit,
            Event = "run-started",
            By = "fixture",
        });

        var historyPath = workspace.ClaimHistoryPath(debtUnit, "unsafe.json");
        Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
        Assert.Equal(0, MkFifo(historyPath, 0x180));
        Assert.False(CrossRuntimeReviewFileMode.IsRegularFile(historyPath));

        using var baseline = Report(workspace.Context, now: Now);
        var activeWindowTask = Task.Run(() => Report(workspace.Context, ["--since", cutoff.ToString("O")], now: Now));
        Assert.Same(activeWindowTask, await Task.WhenAny(activeWindowTask, Task.Delay(TimeSpan.FromSeconds(3))));
        using var activeWindow = await activeWindowTask;

        var baselineBacklog = Assert.Single(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        var activeBacklog = Assert.Single(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal(backlogUnit, baselineBacklog.GetProperty("execution_unit").GetString());
        Assert.Equal(backlogUnit, activeBacklog.GetProperty("execution_unit").GetString());
        Assert.Equal(baselineBacklog.GetRawText(), activeBacklog.GetRawText());
        Assert.Contains(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == debtUnit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.Contains(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == debtUnit
            && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        Assert.Contains(activeWindow.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains(historyPath, StringComparison.Ordinal));
    }

    private static void WriteMalformedRecord(string root, string recordRoot, string unit, string role)
    {
        var relativePath = RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(recordRoot, unit, role);
        var recordPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(recordPath, "{ not json");
    }
}
