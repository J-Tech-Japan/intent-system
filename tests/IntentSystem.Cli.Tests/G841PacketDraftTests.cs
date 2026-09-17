using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G841 AC14a/15: packet draft parse/read refusal and LE byte identity.</summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G841PacketDraftTests : IDisposable
{
    private const string Unit = "G841PD";
    private const string Title = "G841PF Real title";
    private const string Repo = "J-Tech-Japan/other";

    private readonly string root = Directory.CreateTempSubdirectory("g841-packet-draft-").FullName;

    public G841PacketDraftTests()
    {
        IssuePublishFlowCommand.CreatorFactory = () => new ThrowingIssueCreator();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () =>
            new StubExistingIssueChecker(GitHubExistingIssueClassification.None);
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        G841TestHelpers.WriteHostConfig(root);
        G841TestHelpers.WriteClaim(root, Unit, G841TestHelpers.Team);
        WriteBindings();
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PacketDraft_UnparseablePacket_RefusesWithPacketYamlUnparseable_G841Ac14a()
    {
        WritePacket(G841TestHelpers.UnparseableYaml);
        WriteBorrowedGithubBody();

        var (exitCode, json) = RunPacketDraftDryRun();
        Assert.Equal(0, exitCode);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, RefusalReasons(json)[0]);
        Assert.Contains(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, RefusalReasons(json));
        Assert.False(json.RootElement.GetProperty("contract_publishable").GetBoolean());
    }

    [Fact]
    public void PacketDraft_UnreadablePacket_RefusesWithPacketYamlUnreadable_G841Ac14a()
    {
        WritePacket(G841TestHelpers.LegacyEquivalentPacket());
        WriteBorrowedGithubBody();
        var packetPath = G841TestHelpers.PacketPath(root, Unit);
        using var unreadable = G841TestHelpers.UnreadablePacket(
            packetPath,
            ArmDeniedPacketReader,
            DisarmDeniedPacketReader,
            out _);

        var (exitCode, json) = RunPacketDraftDryRun();
        Assert.Equal(0, exitCode);
        Assert.Equal(
            [PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnreadable],
            RefusalReasons(json));
        Assert.False(json.RootElement.GetProperty("contract_publishable").GetBoolean());
    }

    [Fact]
    public void PacketDraft_UnterminatedFlowSequence_RefusesWithPacketYamlUnparseable_G841R2()
    {
        WritePacket(G841TestHelpers.UnterminatedFlowSequenceYaml);
        WriteBorrowedGithubBody();
        Assert.False(PacketYamlDocument.TryParse(G841TestHelpers.UnterminatedFlowSequenceYaml, out _, out var tryParseError));
        Assert.Throws<InvalidOperationException>(() =>
        {
            var stream = new YamlDotNet.RepresentationModel.YamlStream();
            stream.Load(new StringReader(G841TestHelpers.UnterminatedFlowSequenceYaml));
        });

        var (exitCode, json) = RunPacketDraftDryRun();
        Assert.Equal(0, exitCode);
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable,
            RefusalReasons(json)[0]);
        Assert.False(json.RootElement.GetProperty("contract_publishable").GetBoolean());
    }

    [Fact]
    public void PacketDraft_ReadIntentReferencesRace_RefusesWithoutPreCheck_G841D7()
    {
        WritePacket(G841TestHelpers.LegacyEquivalentPacket());
        WriteBorrowedGithubBody();
        var packetPath = G841TestHelpers.PacketPath(root, Unit);
        var reads = 0;
        PacketFileReader.ReadAllText = path =>
        {
            if (string.Equals(path, packetPath, StringComparison.Ordinal)
                && Interlocked.Increment(ref reads) == 1)
            {
                throw new UnauthorizedAccessException("Access to the path is denied.");
            }

            return File.ReadAllText(path);
        };

        var (exitCode, json) = RunPacketDraftDryRun();
        Assert.Equal(0, exitCode);
        Assert.Equal(
            [PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnreadable],
            RefusalReasons(json));
    }

    [Fact]
    public void PacketDraft_UnreadablePacket_DoesNotCrashWithExit134_G841Ac15()
    {
        WritePacket(G841TestHelpers.LegacyEquivalentPacket());
        WriteBorrowedGithubBody();
        var packetPath = G841TestHelpers.PacketPath(root, Unit);
        using var unreadable = G841TestHelpers.UnreadablePacket(
            packetPath,
            ArmDeniedPacketReader,
            DisarmDeniedPacketReader,
            out _);

        var (exitCode, _) = RunPacketDraftDryRun();
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void PublishFlowAndPacketDraft_UnparseablePacket_SharePacketYamlUnparseableLiteral_G841Ac14a()
    {
        WritePacket(
            """
            implementation_issue_packet:
              issue_title: "G841PF Real title"
              domain: intent-cli
              domain: other-domain
              target_repo: J-Tech-Japan/other
            """);
        WriteBorrowedGithubBody();

        var publish = RunPublishFlow();
        var draft = RunPacketDraftDryRun();

        Assert.Equal(1, publish.ExitCode);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, publish.Cause);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, RefusalReasons(draft.Json)[0]);
    }

    [Fact]
    public void PublishFlowAndPacketDraft_UnreadablePacket_SharePacketYamlUnreadableLiteral_G841Ac14a()
    {
        WritePacket(G841TestHelpers.LegacyEquivalentPacket(domain: G841TestHelpers.Domain, extra: $"  target_repo: {Repo}\n"));
        WriteBorrowedGithubBody();
        var packetPath = G841TestHelpers.PacketPath(root, Unit);
        using var unreadable = G841TestHelpers.UnreadablePacket(
            packetPath,
            ArmDeniedPacketReader,
            DisarmDeniedPacketReader,
            out _);

        var publish = RunPublishFlow();
        var draft = RunPacketDraftDryRun();

        Assert.Equal(1, publish.ExitCode);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnreadable, publish.Cause);
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnreadable,
            Assert.Single(RefusalReasons(draft.Json)));
    }

    [Fact]
    public void PacketDraft_LegacyEquivalentPacket_StayByteIdentical_G841Ac14a()
    {
        WritePacket(
            $"""
            implementation_issue_packet:
              issue_title: "{Title}"
              domain: {G841TestHelpers.Domain}
              target_repo: {Repo}
              source_artifact: "review of PR #1823"
            """);
        File.WriteAllText(
            Path.Combine(G841TestHelpers.PacketDir(root, Unit), "github-body.md"),
            G841TestHelpers.MinimalContractBody(Title));
        File.WriteAllText(Path.Combine(G841TestHelpers.PacketDir(root, Unit), "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(G841TestHelpers.PacketDir(root, Unit), "implementation.md"), "# notes\n");

        var (exitCode, json) = RunPacketDraftDryRun();
        Assert.Equal(0, exitCode);
        Assert.True(json.RootElement.GetProperty("contract_publishable").GetBoolean());
        Assert.Empty(RefusalReasons(json));
        Assert.Equal(0, json.RootElement.GetProperty("missing_canonical_files").GetArrayLength());
        Assert.Equal(0, json.RootElement.GetProperty("missing_contract_sections").GetArrayLength());
    }

    private (int ExitCode, JsonDocument Json) RunPacketDraftDryRun()
    {
        using var writer = new StringWriter();
        var exitCode = PacketDraftCommand.Execute(
            Context(),
            [
                "--execution-unit", Unit,
                "--domain", G841TestHelpers.Domain,
                "--target-repo", Repo,
                "--team", G841TestHelpers.Team,
                "--dry-run",
                "--format", "json",
            ],
            writer);
        return (exitCode, JsonDocument.Parse(writer.ToString()));
    }

    private PublishFlowResult RunPublishFlow()
    {
        using var writer = new StringWriter();
        var args = new[]
        {
            Unit,
            "--repo", Repo,
            "--domain", G841TestHelpers.Domain,
            "--team", G841TestHelpers.Team,
            "--format", "json",
        };

        var exitCode = IssuePublishFlowCommand.Execute(Context(), args, writer);
        using var json = JsonDocument.Parse(writer.ToString());
        var rootElement = json.RootElement;
        return new PublishFlowResult
        {
            ExitCode = exitCode,
            Cause = rootElement.TryGetProperty("cause", out var cause) ? cause.GetString() : null,
        };
    }

    private static string[] RefusalReasons(JsonDocument json) =>
        json.RootElement.GetProperty("refusal_reasons")
            .EnumerateArray()
            .Select(reason => reason.GetString()!)
            .ToArray();

    private CliContext Context() => G841TestHelpers.UngatedContext(root);

    private void WriteBindings()
    {
        var dir = Path.Combine(root, "intents", G841TestHelpers.Domain, "automation");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "bindings.md"),
            "---\nexecution_unit_regex: '^G841PD$'\n---\n");
    }

    private void WritePacket(string yaml)
    {
        G841TestHelpers.WritePacketFiles(root, Unit, yaml, withContractBody: false);
    }

    private void WriteBorrowedGithubBody()
    {
        File.WriteAllText(
            Path.Combine(G841TestHelpers.PacketDir(root, Unit), "github-body.md"),
            G841TestHelpers.MinimalContractBody("Borrowed H1"));
    }

    private sealed class PublishFlowResult
    {
        public required int ExitCode { get; init; }

        public string? Cause { get; init; }
    }

    private sealed class ThrowingIssueCreator : IIssueCreator
    {
        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath) =>
            throw new InvalidOperationException("must not create");
    }

    private sealed class StubExistingIssueChecker(GitHubExistingIssueClassification classification) : IGitHubExistingIssueChecker
    {
        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string title, string body) =>
            new() { Classification = classification };
    }

    private static void ArmDeniedPacketReader()
    {
        PacketFileReader.ReadAllText = _ => throw new UnauthorizedAccessException("Access to the path is denied.");
        PacketFileReader.ReadAllBytes = _ => throw new UnauthorizedAccessException("Access to the path is denied.");
    }

    private static void DisarmDeniedPacketReader()
    {
        PacketFileReader.ReadAllText = File.ReadAllText;
        PacketFileReader.ReadAllBytes = File.ReadAllBytes;
    }
}
