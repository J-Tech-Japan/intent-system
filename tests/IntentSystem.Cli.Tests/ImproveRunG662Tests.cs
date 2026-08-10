using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G662: durable improve-run evidence, declared-window recency, facet-check
/// honesty, and negative boundaries (no scheduling, auto-run, or debt class).
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class ImproveRunG662Tests : IDisposable
{
    private readonly string _repoRoot = Directory.CreateTempSubdirectory("g662-improve-").FullName;

    [Fact]
    public void Record_Write_AppendsRequiredFactsWithoutQualityVerdict()
    {
        var context = CreateContext();
        var first = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        ImproveRunRecordCommand.UtcNowFactory = () => first;

        using var dryRunWriter = new StringWriter();
        var dryRunExit = ImproveRunRecordCommand.Execute(
            context,
            ["--domain", "intent-cli", "--mode", "implementation-aware", "--artifact", "intents/intent-cli/intent-tree/02-mission.md", "--format", "json"],
            dryRunWriter);
        Assert.Equal(0, dryRunExit);
        using (var dryRun = JsonDocument.Parse(dryRunWriter.ToString()))
        {
            Assert.False(dryRun.RootElement.GetProperty("applied").GetBoolean());
            Assert.False(dryRun.RootElement.GetProperty("quality_assessed").GetBoolean());
        }

        using var writer = new StringWriter();
        var exit = ImproveRunRecordCommand.Execute(
            context,
            ["--domain", "intent-cli", "--mode", "implementation-aware", "--artifact", "intents/intent-cli/intent-tree/02-mission.md", "--artifact", "docs/en/04-packets-issues.md", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("applied").GetBoolean());
        Assert.Equal("preview-through-1.x", root.GetProperty("preview_status").GetString());
        Assert.False(root.GetProperty("quality_assessed").GetBoolean());
        var record = root.GetProperty("record");
        Assert.Equal("intent-cli", record.GetProperty("domain").GetString());
        Assert.Equal("implementation-aware", record.GetProperty("mode").GetString());
        Assert.Equal(first, record.GetProperty("recorded_at").GetDateTimeOffset());
        Assert.Equal(2, record.GetProperty("touched_artifacts").GetArrayLength());

        var path = ImproveRunStore.ResolvePath(context.ResolveArtifactRootPath(), "intent-cli");
        Assert.Single(File.ReadAllLines(path));
    }

    [Fact]
    public void Next_LapsedWindowRecommendsPasteReadyRealignment_ThenFreshRecordIsSilent()
    {
        var context = CreateContext();
        var first = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        ImproveWindowCommand.UtcNowFactory = () => first;
        Assert.Equal(0, ImproveWindowCommand.Execute(
            context,
            ["--domain", "intent-cli", "--days", "7", "--write", "--format", "json"],
            TextWriter.Null));
        ImproveRunRecordCommand.UtcNowFactory = () => first;
        Assert.Equal(0, ImproveRunRecordCommand.Execute(
            context,
            ["--domain", "intent-cli", "--mode", "light", "--artifact", "intents/intent-cli/intent-tree/02-mission.md", "--write", "--format", "json"],
            TextWriter.Null));

        var lapsedAt = first.AddDays(8);
        GuideNextCommand.UtcNowFactory = () => lapsedAt;
        using var lapsedWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(context, ["--domain", "intent-cli", "--format", "json"], lapsedWriter));
        using (var lapsed = JsonDocument.Parse(lapsedWriter.ToString()))
        {
            var status = lapsed.RootElement.GetProperty("realignment");
            Assert.True(status.GetProperty("lapsed").GetBoolean());
            Assert.True(status.GetProperty("recommendation_included").GetBoolean());
            var action = lapsed.RootElement.GetProperty("decision_set").EnumerateArray()
                .Single(item => item.GetProperty("action").GetString() == GuideNextCommand.ActionRealignment);
            Assert.Contains("intent-cli improve --domain intent-cli", action.GetProperty("suggested_prompt").GetString()!, StringComparison.Ordinal);
            Assert.Contains("improve record", action.GetProperty("suggested_prompt").GetString()!, StringComparison.Ordinal);
            Assert.Contains("timestamp recency only", action.GetProperty("when_to_choose").GetString()!, StringComparison.OrdinalIgnoreCase);
        }

        ImproveRunRecordCommand.UtcNowFactory = () => lapsedAt;
        Assert.Equal(0, ImproveRunRecordCommand.Execute(
            context,
            ["--domain", "intent-cli", "--mode", "implementation-aware", "--artifact", "docs/en/04-packets-issues.md", "--write", "--format", "json"],
            TextWriter.Null));

        using var freshWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(context, ["--domain", "intent-cli", "--format", "json"], freshWriter));
        using var fresh = JsonDocument.Parse(freshWriter.ToString());
        Assert.False(fresh.RootElement.GetProperty("realignment").GetProperty("recommendation_included").GetBoolean());
        Assert.DoesNotContain(
            fresh.RootElement.GetProperty("decision_set").EnumerateArray(),
            item => item.GetProperty("action").GetString() == GuideNextCommand.ActionRealignment);
        Assert.Equal(2, File.ReadAllLines(ImproveRunStore.ResolvePath(context.ResolveArtifactRootPath(), "intent-cli")).Length);
    }

    [Fact]
    public void Next_NoDeclaredRunInventsNoCadence_AndTeamIsOptional()
    {
        var context = CreateContext();
        foreach (var arguments in new[]
                 {
                     new[] { "--domain", "intent-cli", "--format", "json" },
                     new[] { "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json" },
                 })
        {
            using var writer = new StringWriter();
            Assert.Equal(0, GuideNextCommand.Execute(context, arguments, writer));
            using var document = JsonDocument.Parse(writer.ToString());
            var realignment = document.RootElement.GetProperty("realignment");
            Assert.True(realignment.GetProperty("checked").GetBoolean());
            Assert.False(realignment.GetProperty("declared").GetBoolean());
            Assert.False(realignment.GetProperty("recommendation_included").GetBoolean());
        }
    }

    [Fact]
    public void Next_DeclaredWindowWithNoRun_RecommendsRealignment()
    {
        var context = CreateContext();
        ImproveWindowCommand.UtcNowFactory = () => new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, ImproveWindowCommand.Execute(
            context,
            ["--domain", "intent-cli", "--days", "14", "--write", "--format", "json"],
            TextWriter.Null));

        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(context, ["--domain", "intent-cli", "--format", "json"], writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var status = document.RootElement.GetProperty("realignment");
        Assert.True(status.GetProperty("declared").GetBoolean());
        Assert.False(status.GetProperty("run_recorded").GetBoolean());
        Assert.True(status.GetProperty("recommendation_included").GetBoolean());
        Assert.Contains(
            document.RootElement.GetProperty("decision_set").EnumerateArray(),
            item => item.GetProperty("action").GetString() == GuideNextCommand.ActionRealignment);
    }

    [Fact]
    public void Guides_StateFacetNoDataAndNegativeBoundaries()
    {
        using var packetWriter = new StringWriter();
        Assert.Equal(0, GuideWorkflowTaskPacketDraftCommand.Execute(CreateContext(), ["--format", "json"], packetWriter));
        var packet = packetWriter.ToString();
        Assert.Contains("facet-check-before-publish", packet, StringComparison.Ordinal);
        Assert.Contains("no_facet_data: true", packet, StringComparison.Ordinal);
        Assert.Contains("DID NOT RUN", packet, StringComparison.Ordinal);
        Assert.Contains("never means the packet passed", packet, StringComparison.Ordinal);
        Assert.Contains("currently has no facet nodes", packet, StringComparison.Ordinal);

        using var nextWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(CreateContext(), ["--domain", "intent-cli", "--format", "json"], nextWriter));
        var next = nextWriter.ToString();
        Assert.Contains("does NOT schedule, cron, or auto-run", next, StringComparison.Ordinal);
        Assert.Contains("does not create a stalled-work debt class", next, StringComparison.Ordinal);
        Assert.Contains("never grades", next, StringComparison.OrdinalIgnoreCase);

        using var improveWriter = new StringWriter();
        Assert.Equal(0, GuideImproveCommand.Execute(CreateContext(), ["--domain", "intent-cli", "--format", "json"], improveWriter));
        using var improve = JsonDocument.Parse(improveWriter.ToString());
        Assert.Equal("preview-through-1.x", improve.RootElement.GetProperty("record_preview_status").GetString());
        Assert.Contains("improve record", improve.RootElement.GetProperty("record_command").GetString()!, StringComparison.Ordinal);
        Assert.Contains("improve window", improve.RootElement.GetProperty("window_command").GetString()!, StringComparison.Ordinal);
        Assert.Contains("never grades", improve.RootElement.GetProperty("record_semantics").GetString()!, StringComparison.OrdinalIgnoreCase);

        using var helpWriter = new StringWriter();
        Assert.Equal(0, GuideHelpCommand.Execute(CreateContext(), ["--format", "json"], helpWriter));
        Assert.Contains("durable run evidence", helpWriter.ToString(), StringComparison.OrdinalIgnoreCase);

        using var catalogWriter = new StringWriter();
        Assert.Equal(0, GuideCommandsListCommand.Execute(CreateContext(), ["--format", "json"], catalogWriter));
        Assert.Contains("improve record", catalogWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("independently declared recency window", catalogWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Documentation_CarriesEnglishJapaneseAndPreviewParity()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var commands = File.ReadAllText(Path.Combine(root, "docs", language, "08-command-reference.md"));
            Assert.Contains("intent-cli improve record", commands, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", commands, StringComparison.Ordinal);
            Assert.Contains("realignment", commands, StringComparison.Ordinal);
            Assert.Contains("stalled-work", commands, StringComparison.Ordinal);

            var packets = File.ReadAllText(Path.Combine(root, "docs", language, "04-packets-issues.md"));
            Assert.Contains("intent-cli intent facet-check", packets, StringComparison.Ordinal);
            Assert.Contains("no_facet_data: true", packets, StringComparison.Ordinal);
            Assert.Contains("facet node", packets, StringComparison.Ordinal);

            var ledger = File.ReadAllText(Path.Combine(root, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains(".intent-cli/improve/<domain>/runs.jsonl", ledger, StringComparison.Ordinal);
            Assert.Contains("`improve record`", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        ImproveRunRecordCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
        ImproveWindowCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
        GuideNextCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
        Directory.Delete(_repoRoot, recursive: true);
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = _repoRoot,
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
