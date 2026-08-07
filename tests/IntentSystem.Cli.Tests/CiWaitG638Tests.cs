using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class CiWaitG638Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g638-ci-wait-").FullName;

    [Fact]
    public void RecordAndClear_AreDurableAndDoNotStartAProcess()
    {
        var record = new CiWaitRecord
        {
            Domain = "intent-cli",
            Repo = "J-Tech-Japan/intent-system",
            Pr = 1379,
            ObservedHead = "abcdef1234567890",
            OwedTransition = "review-start",
            RecordedAt = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero),
        };

        var planned = CiWaitStore.Record(root, record, write: false);
        Assert.False(planned.Applied);
        Assert.False(File.Exists(CiWaitStore.ResolvePath(root)));

        var written = CiWaitStore.Record(root, record, write: true);
        Assert.True(written.Applied);
        var open = CiWaitStore.ReadOpen(root, record.Domain, record.Repo);
        Assert.Null(open.Error);
        Assert.Equal(record, Assert.Single(open.Records));

        var cleared = CiWaitStore.ClearForTransition(root, record.Repo, record.Pr, record.OwedTransition);
        Assert.True(cleared.Applied);
        Assert.Empty(CiWaitStore.ReadOpen(root, record.Domain, record.Repo).Records);
    }

    [Fact]
    public void CommandJson_NamesRecordAndClearLifecycle()
    {
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
            },
        };

        using var recordWriter = new StringWriter();
        var recordExit = AutomationCiWaitCommand.Execute(
            context,
            ["record", "--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--pr", "1379",
                "--head", "abcdef1234567890", "--transition", "review-start", "--write", "--format", "json"],
            recordWriter);
        Assert.Equal(0, recordExit);
        using var recordJson = JsonDocument.Parse(recordWriter.ToString());
        Assert.True(recordJson.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal("review-start", recordJson.RootElement.GetProperty("records")[0].GetProperty("owed_transition").GetString());

        using var clearWriter = new StringWriter();
        var clearExit = AutomationCiWaitCommand.Execute(
            context,
            ["clear", "--repo", "J-Tech-Japan/intent-system", "--pr", "1379", "--transition", "review-start",
                "--write", "--format", "json"],
            clearWriter);
        Assert.Equal(0, clearExit);
        using var clearJson = JsonDocument.Parse(clearWriter.ToString());
        Assert.True(clearJson.RootElement.GetProperty("applied").GetBoolean());
        Assert.Empty(clearJson.RootElement.GetProperty("records").EnumerateArray());
    }

    [Fact]
    public void GuidanceAndPromiseNameDurableWaitAndSleepingReportWarning()
    {
        var english = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", "ja", "12-agent-message-orchestration.md"));
        Assert.Contains("automation ci-wait record", english, StringComparison.Ordinal);
        Assert.Contains("ci-head-moved", english, StringComparison.Ordinal);
        Assert.Contains("recipient_warning", english, StringComparison.Ordinal);
        Assert.Contains("automation ci-wait record", japanese, StringComparison.Ordinal);
        Assert.Contains("ci-head-moved", japanese, StringComparison.Ordinal);
        Assert.Contains("recipient_warning", japanese, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
