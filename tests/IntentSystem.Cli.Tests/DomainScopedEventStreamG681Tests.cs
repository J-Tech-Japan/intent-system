using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class DomainScopedEventStreamG681Tests : IDisposable
{
    private const string Team = "shared-team";
    private readonly string root = Directory.CreateTempSubdirectory("events-g681-").FullName;

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SameNamedTeamInTwoDomains_WritesDistinctScopedStreamsWithUnchangedSchema_G681()
    {
        WriteLegacyReaderTopology("domain-one");
        WriteLegacyReaderTopology("domain-two");

        var first = Escalate("domain-one", "G681-one");
        var second = Escalate("domain-two", "G681-two");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        var firstPath = ResolveWritePath("domain-one");
        var secondPath = ResolveWritePath("domain-two");
        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(firstPath, first.Output.GetProperty("event_path").GetString());
        Assert.Equal(secondPath, second.Output.GetProperty("event_path").GetString());
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.False(File.Exists(LegacyPath()));

        AssertEvent(File.ReadAllText(firstPath), "G681-one");
        AssertEvent(File.ReadAllText(secondPath), "G681-two");
    }

    [Fact]
    public void ReadPrefersScopedAndFallsBackToLegacyOnlyWhileScopedIsAbsent_G681()
    {
        var legacyPath = LegacyPath();
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "legacy\n");

        Assert.True(NotifyEventWriter.TryResolveReadPath(
            root, "domain-one", Team, recordedReaderPath: null, out var fallback, out var fallbackError),
            fallbackError);
        Assert.Equal(legacyPath, fallback);

        var scopedPath = ResolveWritePath("domain-one");
        Directory.CreateDirectory(Path.GetDirectoryName(scopedPath)!);
        File.WriteAllText(scopedPath, "scoped\n");

        Assert.True(NotifyEventWriter.TryResolveReadPath(
            root, "domain-one", Team, recordedReaderPath: null, out var preferred, out var preferredError),
            preferredError);
        Assert.Equal(scopedPath, preferred);
        Assert.Equal("legacy\n", File.ReadAllText(legacyPath));
    }

    [Fact]
    public void RecordedLegacyReader_ResolvesWritesAndReadsWithoutTopologyEditOrMigration_G681()
    {
        const string domain = "domain-one";
        WriteLegacyReaderTopology(domain);
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, domain, Team);
        var topologyBefore = File.ReadAllBytes(topologyPath);
        var topology = NotifyRoleTopologyStore.Resolve(root, domain, Team).Topology!;
        var target = NotifyRoleTopologyStore.ResolveDeliveryTarget(root, topology, "design");

        Assert.True(target.Resolved);
        Assert.Equal(ResolveWritePath(domain), target.Target);

        var legacyPath = LegacyPath();
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "legacy\n");
        Assert.True(NotifyEventWriter.TryResolveReadPath(
            root, domain, Team, target.Target, out var readPath, out var error), error);
        Assert.Equal(legacyPath, readPath);
        Assert.Equal(topologyBefore, File.ReadAllBytes(topologyPath));
        Assert.True(File.Exists(legacyPath));
        Assert.False(File.Exists(ResolveWritePath(domain)));
    }

    [Fact]
    public void EnglishJapaneseGuidanceAndLedger_NameScopedFallbackAndExactOperatorMove_G681()
    {
        const string scoped = ".intent-cli/events/<domain>/<team>.jsonl";
        const string legacy = ".intent-cli/events/<team>.jsonl";
        const string move = "mkdir -p .intent-cli/events/<domain> && mv .intent-cli/events/<team>.jsonl .intent-cli/events/<domain>/<team>.jsonl";
        var repoRoot = FindRepoRoot();

        foreach (var language in new[] { "en", "ja" })
        {
            var guide = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"));
            var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains(scoped, guide, StringComparison.Ordinal);
            Assert.Contains(legacy, guide, StringComparison.Ordinal);
            Assert.Contains(move, guide, StringComparison.Ordinal);
            Assert.Contains("topology edit", guide, StringComparison.Ordinal);
            Assert.Contains("G681", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
            Assert.Contains(scoped, ledger, StringComparison.Ordinal);
            Assert.Contains(legacy, ledger, StringComparison.Ordinal);
        }
    }

    private (int ExitCode, JsonElement Output) Escalate(string domain, string taskId)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
        [
            "notify", "escalate", "--domain", domain, "--team", Team,
            "--from", "orchestration", "--task-id", taskId,
            "--artifact", "approval", "--summary", "design decision",
            "--write", "--format", "json",
        ], CreateContext(domain), writer);
        return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
    }

    private CliContext CreateContext(string domain) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private void WriteLegacyReaderTopology(string domain)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain,
            team = Team,
            workspace_id = $"workspace-{domain}",
            roles = new
            {
                design = new
                {
                    resident = "external",
                    reader = NotifyEventWriter.LegacyRelativePathFor(Team),
                },
            },
        }));
    }

    private string ResolveWritePath(string domain)
    {
        Assert.True(NotifyEventWriter.TryResolveWritePath(root, domain, Team, out var path, out var error), error);
        return path;
    }

    private string LegacyPath() => Path.GetFullPath(Path.Combine(
        root,
        NotifyEventWriter.LegacyRelativePathFor(Team).Replace('/', Path.DirectorySeparatorChar)));

    private static void AssertEvent(string jsonLine, string unit)
    {
        using var document = JsonDocument.Parse(jsonLine);
        var record = document.RootElement;
        Assert.Equal(6, record.EnumerateObject().Count());
        Assert.True(record.TryGetProperty("timestamp", out _));
        Assert.Equal(Team, record.GetProperty("team").GetString());
        Assert.Equal("escalation", record.GetProperty("kind").GetString());
        Assert.Equal(unit, record.GetProperty("unit").GetString());
        Assert.Equal("design decision", record.GetProperty("summary").GetString());
        Assert.Equal("approval", record.GetProperty("artifact").GetString());
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IntentSystem.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository root from {AppContext.BaseDirectory}.");
    }
}
