using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed partial class StalledWorkAdoptionWindowG858Tests
{
    [Theory]
    [InlineData("12:00")]
    [InlineData("2026-08-20")]
    [InlineData("2026-08-20T12:00:00")]
    [InlineData("08/20/2026 12:00:00Z")]
    [InlineData("2026-02-30T00:00:00Z")]
    [InlineData("2026-08-20T00:00:00+25:00")]
    [InlineData("2026-08-20T00:00:00Z trailing")]
    [InlineData("2026-08-20T00:00Z")]
    public void R4_SinceRejectsIncompleteOrInvalidInstantsBeforeCandidateIo(string value)
    {
        using var workspace = new AdoptionWorkspace();
        var candidateListerCalls = 0;
        AutomationStalledWorkCommand.CandidateListerFactory = () =>
        {
            candidateListerCalls++;
            return new EmptyLister();
        };

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", Domain, "--repo", Repo, "--stale-minutes", "0", "--since", value, "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--since requires an ISO-8601 instant", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, candidateListerCalls);
        Assert.False(File.Exists(workspace.RunLogPath));
    }

    [Theory]
    [InlineData("2026-08-20T00:00:00Z", "2026-08-20T00:00:00+00:00")]
    [InlineData("2026-08-20T00:00:00.1234567Z", "2026-08-20T00:00:00.1234567+00:00")]
    [InlineData("2026-08-20T03:00:00+03:00", "2026-08-20T00:00:00+00:00")]
    public void R4_SinceAcceptsUtcFractionAndEquivalentOffsetInstants(string value, string normalized)
    {
        using var workspace = new AdoptionWorkspace();
        using var result = Report(workspace.Context, ["--since", value]);

        Assert.Equal(DateTimeOffset.Parse(normalized, System.Globalization.CultureInfo.InvariantCulture),
            result.RootElement.GetProperty("debt_window").GetProperty("cutoff").GetDateTimeOffset());
    }

    [Fact]
    public void R4_RunLogSymlinkRetainsBothPendingDutiesAndBaselineItems()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        var now = cutoff.AddDays(1);
        const string unit = "G858-r4-symlink-runlog";
        workspace.WriteDebtPacket(unit);
        workspace.AppendEvent(IssueEvent(unit, cutoff.AddHours(1), "J-Tech-Japan/intent-system#2941",
            "https://github.com/J-Tech-Japan/intent-system/issues/2941"));
        workspace.WriteCloseoutOnly(unit, cutoff.AddHours(2), 2942);

        var targetPath = Path.Combine(workspace.Root, "regular-runs-target.jsonl");
        File.Move(workspace.RunLogPath, targetPath);
        File.CreateSymbolicLink(workspace.RunLogPath, targetPath);
        Assert.True(CrossRuntimeReviewFileMode.IsSymlink(workspace.RunLogPath));

        using var baseline = Report(workspace.Context, now: now);
        using var activeWindow = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: now);
        Assert.True(baseline.RootElement.GetProperty("stalled").GetBoolean());
        Assert.True(activeWindow.RootElement.GetProperty("stalled").GetBoolean());
        foreach (var kind in new[]
                 {
                     AutomationStalledWorkCommand.KindKnowledgeWritebackPending,
                     AutomationStalledWorkCommand.KindGuideReachabilityPending,
                 })
        {
            var baselineItem = Assert.Single(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit && item.GetProperty("kind").GetString() == kind);
            var activeItem = Assert.Single(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit && item.GetProperty("kind").GetString() == kind);
            Assert.Equal(baselineItem.GetRawText(), activeItem.GetRawText());
            Assert.Contains(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("kind").GetString() == kind
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        }
        Assert.Contains(activeWindow.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("run-log index is incomplete", StringComparison.Ordinal)
            && warning.GetString()!.Contains(workspace.RunLogPath, StringComparison.Ordinal));
        Assert.DoesNotContain(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("reason").GetString() == "debt-window-historical");
    }

    [Fact]
    public void R4_PacketSymlinkRetainsBothPendingDutiesAndBaselineItems()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        var now = cutoff.AddDays(1);
        const string unit = "G858-r4-symlink-packet";
        workspace.WriteDebtPacket(unit);
        workspace.AppendEvent(IssueEvent(unit, cutoff.AddDays(-1), "J-Tech-Japan/intent-system#2943",
            "https://github.com/J-Tech-Japan/intent-system/issues/2943"));
        workspace.WriteCloseoutOnly(unit, cutoff.AddHours(2), 2944);

        var packetPath = Path.Combine(workspace.Root, ".intent-cli", "issues", unit, "packet.yaml");
        var targetPath = Path.Combine(workspace.Root, "regular-packet-target.yaml");
        File.Move(packetPath, targetPath);
        File.CreateSymbolicLink(packetPath, targetPath);
        Assert.True(CrossRuntimeReviewFileMode.IsSymlink(packetPath));

        using var baseline = Report(workspace.Context, now: now);
        using var activeWindow = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: now);
        Assert.True(baseline.RootElement.GetProperty("stalled").GetBoolean());
        Assert.True(activeWindow.RootElement.GetProperty("stalled").GetBoolean());
        foreach (var kind in new[]
                 {
                     AutomationStalledWorkCommand.KindKnowledgeWritebackPending,
                     AutomationStalledWorkCommand.KindGuideReachabilityPending,
                 })
        {
            var baselineItem = Assert.Single(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit && item.GetProperty("kind").GetString() == kind);
            var activeItem = Assert.Single(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit && item.GetProperty("kind").GetString() == kind);
            Assert.Equal(baselineItem.GetRawText(), activeItem.GetRawText());
            Assert.Contains(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("kind").GetString() == kind
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        }
        Assert.Contains(activeWindow.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("debt window retained", StringComparison.Ordinal)
            && warning.GetString()!.Contains(unit, StringComparison.Ordinal)
            && warning.GetString()!.Contains("packet-identity-unavailable", StringComparison.Ordinal));
        Assert.DoesNotContain(activeWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("reason").GetString() == "debt-window-historical");
    }
}
