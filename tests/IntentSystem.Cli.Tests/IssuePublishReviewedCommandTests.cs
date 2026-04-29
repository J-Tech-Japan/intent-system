using System.Reflection;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IssuePublishReviewedCommandTests : IDisposable
{
    private readonly Func<IGhIssueCreator> originalCreatorFactory;
    private readonly Func<DateTimeOffset> originalTimestampFactory;
    private readonly Func<DateTimeOffset> originalPrepareTimestampFactory;

    public IssuePublishReviewedCommandTests()
    {
        originalCreatorFactory = IssuePublishReviewedCommand.GhIssueCreatorFactory;
        originalTimestampFactory = IssuePublishReviewedCommand.TimestampFactory;
        originalPrepareTimestampFactory = IssuePrepareCommand.TimestampFactory;
    }

    public void Dispose()
    {
        IssuePublishReviewedCommand.GhIssueCreatorFactory = originalCreatorFactory;
        IssuePublishReviewedCommand.TimestampFactory = originalTimestampFactory;
        IssuePrepareCommand.TimestampFactory = originalPrepareTimestampFactory;
    }

    [Fact]
    public void Execute_GivenReviewedValidPacket_CreatesIssueAndUpdatesPacket()
    {
        using var workspace = new IssuePublishWorkspace();
        var (bodyPath, packetPath) = workspace.PrepareValidPacket();

        var fakeCreator = new RecordingGhIssueCreator("https://github.com/owner/repo/issues/473");
        IssuePublishReviewedCommand.GhIssueCreatorFactory = () => fakeCreator;
        IssuePublishReviewedCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 28, 13, 0, 0, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = IssuePublishReviewedCommand.Execute(
            workspace.Context,
            ["--from-file", packetPath, "--repo", "owner/repo"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Single(fakeCreator.Calls);
        var call = fakeCreator.Calls[0];
        Assert.Equal("owner/repo", call.Repo);
        Assert.Equal("G184 sample title", call.Title);
        Assert.Equal(bodyPath, call.BodyFilePath);

        Assert.Contains("Published G184 as https://github.com/owner/repo/issues/473", writer.ToString(), StringComparison.Ordinal);

        var packet = JsonSerializer.Deserialize<IssueReviewedPublishPacket>(File.ReadAllText(packetPath));
        Assert.NotNull(packet);
        Assert.True(packet!.Published);
        Assert.Equal(473, packet.IssueNumber);
        Assert.Equal("https://github.com/owner/repo/issues/473", packet.IssueUrl);
        Assert.Equal("2026-04-28T13:00:00.000Z", packet.PublishedAtUtc);
    }

    [Fact]
    public void Execute_GivenMissingReviewedMarker_ReturnsNonZeroBeforeGhCall()
    {
        using var workspace = new IssuePublishWorkspace();
        var (_, packetPath) = workspace.PrepareValidPacket();

        // Mutate the on-disk packet so reviewed=false.
        var packet = JsonSerializer.Deserialize<IssueReviewedPublishPacket>(File.ReadAllText(packetPath))!;
        File.WriteAllText(packetPath, JsonSerializer.Serialize(packet with { Reviewed = false }));

        var fakeCreator = new RecordingGhIssueCreator("unused");
        IssuePublishReviewedCommand.GhIssueCreatorFactory = () => fakeCreator;

        using var writer = new StringWriter();
        var exitCode = IssuePublishReviewedCommand.Execute(
            workspace.Context,
            ["--from-file", packetPath, "--repo", "owner/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Empty(fakeCreator.Calls);
        Assert.Contains("missing reviewed marker", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenAlreadyPublished_ReturnsNonZeroBeforeGhCall()
    {
        using var workspace = new IssuePublishWorkspace();
        var (_, packetPath) = workspace.PrepareValidPacket();

        var packet = JsonSerializer.Deserialize<IssueReviewedPublishPacket>(File.ReadAllText(packetPath))!;
        File.WriteAllText(packetPath, JsonSerializer.Serialize(packet with
        {
            Published = true,
            IssueNumber = 1,
            IssueUrl = "https://github.com/owner/repo/issues/1",
            PublishedAtUtc = "2026-04-01T00:00:00.000Z"
        }));

        var fakeCreator = new RecordingGhIssueCreator("unused");
        IssuePublishReviewedCommand.GhIssueCreatorFactory = () => fakeCreator;

        using var writer = new StringWriter();
        var exitCode = IssuePublishReviewedCommand.Execute(
            workspace.Context,
            ["--from-file", packetPath, "--repo", "owner/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Empty(fakeCreator.Calls);
        Assert.Contains("already published", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenInvalidValidationStatus_ReturnsNonZeroBeforeGhCall()
    {
        using var workspace = new IssuePublishWorkspace();
        var (_, packetPath) = workspace.PrepareValidPacket();

        var packet = JsonSerializer.Deserialize<IssueReviewedPublishPacket>(File.ReadAllText(packetPath))!;
        File.WriteAllText(packetPath, JsonSerializer.Serialize(packet with { ValidationStatus = "invalid" }));

        var fakeCreator = new RecordingGhIssueCreator("unused");
        IssuePublishReviewedCommand.GhIssueCreatorFactory = () => fakeCreator;

        using var writer = new StringWriter();
        var exitCode = IssuePublishReviewedCommand.Execute(
            workspace.Context,
            ["--from-file", packetPath, "--repo", "owner/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Empty(fakeCreator.Calls);
        Assert.Contains("validation status is not valid", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenSourceBodyNoLongerValid_ReturnsNonZeroBeforeGhCall()
    {
        using var workspace = new IssuePublishWorkspace();
        var (bodyPath, packetPath) = workspace.PrepareValidPacket();

        // Corrupt the source body so the validator now rejects it.
        File.WriteAllText(bodyPath, "# Just a title with no required sections.\n");

        var fakeCreator = new RecordingGhIssueCreator("unused");
        IssuePublishReviewedCommand.GhIssueCreatorFactory = () => fakeCreator;

        using var writer = new StringWriter();
        var exitCode = IssuePublishReviewedCommand.Execute(
            workspace.Context,
            ["--from-file", packetPath, "--repo", "owner/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Empty(fakeCreator.Calls);
        Assert.Contains("source body no longer passes validation", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewedValidPacket_NeverPassesLabelToGh()
    {
        // Contract assertion: the IGhIssueCreator interface itself has no
        // label parameter, and the captured call records show no label.
        var createMethod = typeof(IGhIssueCreator).GetMethod(
            nameof(IGhIssueCreator.CreateIssue),
            BindingFlags.Public | BindingFlags.Instance)!;
        var parameterNames = createMethod.GetParameters().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(parameterNames, name => name is not null
            && name.Contains("label", StringComparison.OrdinalIgnoreCase));

        using var workspace = new IssuePublishWorkspace();
        var (_, packetPath) = workspace.PrepareValidPacket();

        var fakeCreator = new RecordingGhIssueCreator("https://github.com/owner/repo/issues/200");
        IssuePublishReviewedCommand.GhIssueCreatorFactory = () => fakeCreator;
        IssuePublishReviewedCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 28, 14, 0, 0, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = IssuePublishReviewedCommand.Execute(
            workspace.Context,
            ["--from-file", packetPath, "--repo", "owner/repo"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Single(fakeCreator.Calls);
        var call = fakeCreator.Calls[0];
        // The recorded call surface only contains repo/title/bodyFilePath.
        Assert.DoesNotContain("intent-target", call.Repo, StringComparison.Ordinal);
        Assert.DoesNotContain("intent-target", call.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("--label", call.Repo, StringComparison.Ordinal);
        Assert.DoesNotContain("--label", call.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("--label", call.BodyFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenSourceBodyChangedButStillValid_ReturnsNonZeroBeforeGhCall()
    {
        // Regression for the G184 reviewed-publish boundary: if the body file
        // is replaced with DIFFERENT but still G183-contract-valid Markdown
        // after `issue prepare`, `publish-reviewed` must refuse before any
        // GitHub mutation because the recorded SHA-256 no longer matches.
        using var workspace = new IssuePublishWorkspace();
        var (bodyPath, packetPath) = workspace.PrepareValidPacket();

        // Replace the body with a different but still-valid body. Appending
        // any non-whitespace content changes the bytes (and therefore the
        // sha256) without removing or invalidating any required heading.
        var mutatedBody = IssuePrepareCommandTests.CompleteValidBody
            + "\n\nAdditional prose appended after review approved the packet.\n";
        File.WriteAllText(bodyPath, mutatedBody);

        var fakeCreator = new RecordingGhIssueCreator("unused");
        IssuePublishReviewedCommand.GhIssueCreatorFactory = () => fakeCreator;

        using var writer = new StringWriter();
        var exitCode = IssuePublishReviewedCommand.Execute(
            workspace.Context,
            ["--from-file", packetPath, "--repo", "owner/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Empty(fakeCreator.Calls);
        Assert.Contains("sha256", writer.ToString(), StringComparison.OrdinalIgnoreCase);

        // Packet must remain unpublished on disk.
        var packet = JsonSerializer.Deserialize<IssueReviewedPublishPacket>(File.ReadAllText(packetPath))!;
        Assert.False(packet.Published);
        Assert.Null(packet.IssueNumber);
        Assert.Null(packet.IssueUrl);
    }

    [Fact]
    public void Execute_GivenMissingPacketFile_ReturnsNonZero()
    {
        using var workspace = new IssuePublishWorkspace();
        var fakeCreator = new RecordingGhIssueCreator("unused");
        IssuePublishReviewedCommand.GhIssueCreatorFactory = () => fakeCreator;

        using var writer = new StringWriter();
        var exitCode = IssuePublishReviewedCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("missing-packet.json"), "--repo", "owner/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Empty(fakeCreator.Calls);
        Assert.Contains("does not exist", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class RecordingGhIssueCreator : IGhIssueCreator
    {
        private readonly string urlToReturn;

        public RecordingGhIssueCreator(string urlToReturn)
        {
            this.urlToReturn = urlToReturn;
        }

        public List<RecordedCall> Calls { get; } = new();

        public string CreateIssue(string repo, string title, string bodyFilePath)
        {
            Calls.Add(new RecordedCall(repo, title, bodyFilePath));
            return urlToReturn;
        }

        public sealed record RecordedCall(string Repo, string Title, string BodyFilePath);
    }

    internal sealed class IssuePublishWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("issue-publish-reviewed-tests-")
            .FullName;

        public IssuePublishWorkspace()
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
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public string GetPath(string relative) => Path.Combine(rootPath, relative);

        public (string BodyPath, string PacketPath) PrepareValidPacket()
        {
            var bodyPath = GetPath("body.md");
            File.WriteAllText(bodyPath, IssuePrepareCommandTests.CompleteValidBody);
            var packetPath = GetPath("packet.json");

            IssuePrepareCommand.TimestampFactory =
                () => new DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);

            using var writer = new StringWriter();
            var exit = IssuePrepareCommand.Execute(
                Context,
                ["--from-file", bodyPath, "--execution-unit", "G184", "--title", "G184 sample title", "--out", packetPath],
                writer);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to prepare valid packet for test: exit={exit}, output={writer}");
            }

            return (bodyPath, packetPath);
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
