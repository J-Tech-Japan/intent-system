using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class G846IssueValidateBodyTests
{
    [Theory]
    [InlineData(57999, 0, false, false)]
    [InlineData(58000, 0, false, true)]
    [InlineData(58001, 0, false, true)]
    [InlineData(65535, 0, false, true)]
    [InlineData(65536, 0, false, true)]
    [InlineData(65537, 1, true, false)]
    public void BoundaryCases_ReportTheInclusiveBands(int bytes, int expectedExit, bool tooLarge, bool warning)
    {
        using var workspace = new BodyWorkspace("g846-validate-");
        var path = workspace.WriteBody("body.md", G846BodyFixtures.BodyBytes(bytes));

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", path, "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(expectedExit, exitCode);
        Assert.Equal(bytes, root.GetProperty("body_bytes").GetInt32());
        Assert.Equal(tooLarge, root.GetProperty("body_too_large").GetBoolean());
        Assert.Equal(warning, root.GetProperty("body_size_warning").GetBoolean());
        Assert.Equal(!tooLarge, root.GetProperty("is_valid").GetBoolean());
        if (tooLarge)
        {
            Assert.Contains("issue-body-too-large", root.GetProperty("body_size_reason").GetString(), StringComparison.Ordinal);
            Assert.Contains("65536", root.GetProperty("body_size_reason").GetString(), StringComparison.Ordinal);
        }
        else if (warning)
        {
            Assert.Contains("issue-body-size-warning", root.GetProperty("body_size_reason").GetString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("body_size_reason").ValueKind);
        }
    }

    [Fact]
    public void BomFixture_ReportsRawBytesNotDecodedCharacters()
    {
        using var workspace = new BodyWorkspace("g846-validate-bom-");
        var path = workspace.WriteBody("body-bom.md", G846BodyFixtures.BodyBytes(65539, bom: true));

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", path, "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(1, exitCode);
        Assert.Equal(65539, root.GetProperty("body_bytes").GetInt32());
        Assert.True(root.GetProperty("body_too_large").GetBoolean());
        Assert.Contains("65539", root.GetProperty("body_size_reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedBody_PreservesHeadingRefusalAndAddsNamedSizeRefusal()
    {
        using var workspace = new BodyWorkspace("g846-validate-both-");
        var body = Encoding.UTF8.GetBytes("## Goal\n\nonly one section\n");
        var bytes = new byte[65537];
        body.CopyTo(bytes, 0);
        Array.Fill(bytes, (byte)'x', body.Length, bytes.Length - body.Length);
        var path = workspace.WriteBody("body.md", bytes);

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", path, "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(1, exitCode);
        Assert.False(root.GetProperty("is_valid").GetBoolean());
        Assert.True(root.GetProperty("body_too_large").GetBoolean());
        Assert.True(root.GetProperty("missing_headings").GetArrayLength() > 0);
        Assert.Contains("issue-body-too-large", root.GetProperty("body_size_reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PureValidator_DoesNotFoldSizeIntoTheHeadingVerdict()
    {
        var body = G846BodyFixtures.Decode(G846BodyFixtures.BodyBytes(65539, bom: true));

        var result = IssueValidateBodyValidator.Validate(
            "body.md",
            body,
            requireTargetPathsDeclaration: true);

        Assert.True(result.IsValid);
        Assert.False(result.BodyTooLarge);
        Assert.Null(result.BodySizeReason);
    }

    [Fact]
    public void TextRenderer_PrintsExactlyOneSizeLine()
    {
        using var workspace = new BodyWorkspace("g846-validate-text-");
        var path = workspace.WriteBody("body.md", G846BodyFixtures.BodyBytes(58000));

        using var writer = new StringWriter();
        Assert.Equal(0, IssueValidateBodyCommand.Execute(workspace.Context, ["--from-file", path], writer));

        Assert.Equal(1, writer.ToString().Split("Body size:", StringSplitOptions.None).Length - 1);
        Assert.Contains("issue-body-size-warning", writer.ToString(), StringComparison.Ordinal);
    }
}

public sealed class G846PacketDraftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedBody_AddsRefusalInBothModes(bool dryRun)
    {
        using var workspace = new PacketWorkspace("g846-draft-");
        workspace.WritePacket("G846", G846BodyFixtures.BodyBytes(65539, bom: true));

        var result = PacketDraftCommand.Draft(
            workspace.Context,
            "G846",
            "intent-cli",
            "J-Tech-Japan/intent-system",
            dryRun);

        Assert.Contains("issue-body-too-large", result.RefusalReasons);
        Assert.False(result.ContractPublishable);
        Assert.Contains(result.RecommendedActions, action =>
            action.Contains("65539", StringComparison.Ordinal)
            && action.Contains("65536", StringComparison.Ordinal));
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarningBand_AppendsWarningAndKeepsExitPayloadShape(bool dryRun)
    {
        using var workspace = new PacketWorkspace("g846-draft-warning-");
        workspace.WritePacket("G846", G846BodyFixtures.BodyBytes(58002, bom: true));

        var result = PacketDraftCommand.Draft(
            workspace.Context,
            "G846",
            "intent-cli",
            "J-Tech-Japan/intent-system",
            dryRun);

        Assert.Equal(["issue-body-size-warning"], result.Warnings);
        Assert.DoesNotContain("issue-body-too-large", result.RefusalReasons);
    }

    [Fact]
    public void OverLimitDefaultMode_StillScaffoldsMissingCanonicalFileAndPreservesExistingBytes()
    {
        using var workspace = new PacketWorkspace("g846-draft-snapshot-");
        var packetDirectory = workspace.PacketDirectory("G846");
        Directory.CreateDirectory(packetDirectory);
        var packetYaml = Encoding.UTF8.GetBytes("hand-authored packet.yaml\n");
        var implementation = Encoding.UTF8.GetBytes("hand-authored implementation\n");
        var body = G846BodyFixtures.BodyBytes(70000);
        File.WriteAllBytes(Path.Combine(packetDirectory, "packet.yaml"), packetYaml);
        File.WriteAllBytes(Path.Combine(packetDirectory, "implementation.md"), implementation);
        File.WriteAllBytes(Path.Combine(packetDirectory, "github-body.md"), body);

        var before = new Dictionary<string, Snapshot>(StringComparer.Ordinal)
        {
            ["packet.yaml"] = Snapshot.For(packetYaml),
            ["implementation.md"] = Snapshot.For(implementation),
            ["github-body.md"] = Snapshot.For(body),
        };

        var result = PacketDraftCommand.Draft(
            workspace.Context,
            "G846",
            "intent-cli",
            "J-Tech-Japan/intent-system",
            dryRun: false);

        Assert.Contains("issue-body-too-large", result.RefusalReasons);
        Assert.Equal("created", result.Files.Single(file => file.Name == "review-context.md").Status);
        Assert.True(File.Exists(Path.Combine(packetDirectory, "review-context.md")));
        foreach (var (name, expected) in before)
        {
            Assert.Equal(expected, Snapshot.For(File.ReadAllBytes(Path.Combine(packetDirectory, name))));
        }
    }

    [Fact]
    public void MarkdownOutput_AlwaysIncludesWarningsLine()
    {
        using var workspace = new PacketWorkspace("g846-draft-text-");
        workspace.WritePacket("G846", G846BodyFixtures.BodyBytes(58002, bom: true));

        using var writer = new StringWriter();
        var exitCode = PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G846", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("- warnings:\n  - issue-body-size-warning", writer.ToString(), StringComparison.Ordinal);
    }

    private readonly record struct Snapshot(int Length, string Sha256)
    {
        public static Snapshot For(byte[] bytes) => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    }
}

[Collection("WorkerNextActionSharedState")]
public sealed class G846PublishFlowTests : IDisposable
{
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G846";

    public G846PublishFlowTests()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = () => new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new CountingExistingIssueChecker(
            GitHubExistingIssueClassification.None);
    }

    public void Dispose()
    {
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = null;
    }

    [Fact]
    public void OversizedDryRun_RefusesBeforeExistingIssueCheckerAndCreator()
    {
        using var workspace = new PublishWorkspace();
        workspace.WriteBody(G846BodyFixtures.BodyBytes(70000));
        workspace.WritePacketYaml();
        var checker = new CountingExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new CountingCreator();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exitCode, root) = workspace.Run(write: false);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        Assert.Equal("issue-body-too-large", root.GetProperty("cause").GetString());
        Assert.Contains("70000", root.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Contains("65536", root.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Child Issue Contract is incomplete", root.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedWrite_RefusesAfterReadOnlyLookupBeforeCreatorAndDurableWrites()
    {
        using var workspace = new PublishWorkspace();
        workspace.WriteBody(G846BodyFixtures.BodyBytes(70000));
        workspace.WritePacketYaml();
        workspace.WriteQueueState(linkedIssue: null);
        var before = workspace.SnapshotDurableFiles();
        var checker = new CountingExistingIssueChecker(GitHubExistingIssueClassification.None);
        var creator = new CountingCreator();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exitCode, root) = workspace.Run(write: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        Assert.Equal("issue-body-too-large", root.GetProperty("cause").GetString());
        Assert.Equal(before, workspace.SnapshotDurableFiles());
    }

    [Fact]
    public void AlreadyPublishedOversizedDryRun_ContinuesAndAppendsWarning()
    {
        using var workspace = new PublishWorkspace();
        workspace.WriteBody(G846BodyFixtures.BodyBytes(70000));
        workspace.WritePacketYaml();
        workspace.WritePublishYaml(1900);
        var creator = new CountingCreator();
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exitCode, root) = workspace.Run(write: false);

        Assert.Equal(0, exitCode);
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.Contains("issue-body-too-large", root.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(0, creator.CallCount);
    }

    [Fact]
    public void AlreadyPublishedOversizedWrite_RepairsDurableStateAndContinues()
    {
        using var workspace = new PublishWorkspace();
        workspace.WriteBody(G846BodyFixtures.BodyBytes(70000));
        workspace.WritePacketYaml();
        workspace.WriteQueueState(
            new LinkedIssue
            {
                Repo = Repo,
                Number = 1900,
                Url = $"https://github.com/{Repo}/issues/1900"
            });
        workspace.WritePublishYaml(1900);
        var creator = new CountingCreator();
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exitCode, root) = workspace.Run(write: true);

        Assert.Equal(0, exitCode);
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.True(root.GetProperty("durable_state_synced").GetBoolean());
        Assert.Contains("issue-body-too-large", root.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(0, creator.CallCount);
    }

    [Fact]
    public void GithubSourcedAlreadyPublishedOversizedWrite_RestoresInsteadOfRefusing()
    {
        using var workspace = new PublishWorkspace();
        workspace.WriteBody(G846BodyFixtures.BodyBytes(70000));
        workspace.WritePacketYaml();
        workspace.WriteQueueState(linkedIssue: null);
        var checker = new CountingExistingIssueChecker(
            GitHubExistingIssueClassification.Unique,
            1900,
            $"https://github.com/{Repo}/issues/1900");
        var creator = new CountingCreator();
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => checker;
        IssuePublishFlowCommand.CreatorFactory = () => creator;

        var (exitCode, root) = workspace.Run(write: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, checker.CallCount);
        Assert.Equal(0, creator.CallCount);
        Assert.True(root.GetProperty("idempotent").GetBoolean());
        Assert.Contains("issue-body-too-large", root.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()));
        Assert.True(File.Exists(workspace.PublishYamlPath));
        Assert.True(File.Exists(workspace.RunsPath));
    }

    [Theory]
    [InlineData(57999, 0, false)]
    [InlineData(58000, 0, true)]
    [InlineData(58001, 0, true)]
    [InlineData(65535, 0, true)]
    [InlineData(65536, 0, true)]
    [InlineData(65537, 1, false)]
    public void BoundaryCases_AreConsistentOnPublishFlow(int bytes, int expectedExit, bool expectedWarning)
    {
        using var workspace = new PublishWorkspace();
        workspace.WriteBody(G846BodyFixtures.BodyBytes(bytes));
        workspace.WritePacketYaml();
        var (exitCode, root) = workspace.Run(write: false);

        Assert.Equal(expectedExit, exitCode);
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(expectedWarning, warnings.Contains("issue-body-size-warning"));
        if (expectedExit == 1)
        {
            Assert.Equal("issue-body-too-large", root.GetProperty("cause").GetString());
        }
    }

    [Fact]
    public void ByteDistinguishingBomFixtures_StayInTheCorrectPublishBands()
    {
        using var workspace = new PublishWorkspace();
        workspace.WritePacketYaml();

        workspace.WriteBody(G846BodyFixtures.BodyBytes(58002, bom: true));
        var (warningExit, warningRoot) = workspace.Run(write: false);
        Assert.Equal(0, warningExit);
        Assert.Contains("issue-body-size-warning", warningRoot.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()));

        workspace.WriteBody(G846BodyFixtures.BodyBytes(65539, bom: true));
        var (oversizedExit, oversizedRoot) = workspace.Run(write: false);
        Assert.Equal(1, oversizedExit);
        Assert.Equal("issue-body-too-large", oversizedRoot.GetProperty("cause").GetString());
        Assert.Contains("65539", oversizedRoot.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    private sealed class PublishWorkspace : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("g846-publish-").FullName;

        public PublishWorkspace()
        {
            Context = new CliContext
            {
                RepoRoot = root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public CliContext Context { get; }

        private string PacketDirectory => Path.Combine(root, ".intent-cli", "issues", Unit);

        public string PublishYamlPath => Path.Combine(PacketDirectory, "publish.yaml");

        public string RunsPath => Path.Combine(root, ".intent-cli", "runs.jsonl");

        public void WriteBody(byte[] bytes)
        {
            Directory.CreateDirectory(PacketDirectory);
            File.WriteAllBytes(Path.Combine(PacketDirectory, "github-body.md"), bytes);
        }

        public void WritePacketYaml()
        {
            Directory.CreateDirectory(PacketDirectory);
            File.WriteAllText(
                Path.Combine(PacketDirectory, "packet.yaml"),
                "implementation_issue_packet:\n  source_execution_unit: G846\n  issue_title: G846 test\n  domain: intent-cli\n  target_repo: J-Tech-Japan/intent-system\n");
        }

        public void WritePublishYaml(int issueNumber)
        {
            Directory.CreateDirectory(PacketDirectory);
            File.WriteAllText(PublishYamlPath, IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
            {
                ExecutionUnit = Unit,
                PublishStatus = "issue-created",
                PacketPath = $".intent-cli/issues/{Unit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{Unit}/github-body.md",
                CreatedIssueNumber = issueNumber,
                CreatedIssueUrl = $"https://github.com/{Repo}/issues/{issueNumber}",
                PublishedLabelName = "intent-target",
                LifecycleState = "published"
            }));
        }

        public void WriteQueueState(LinkedIssue? linkedIssue)
        {
            var queuePath = Path.Combine(root, ".intent-cli", "queue-state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(queuePath)!);
            File.WriteAllText(queuePath, QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero),
                Items =
                [
                    new QueueItem
                    {
                        ExecutionUnit = Unit,
                        Title = "G846 test",
                        State = QueueItemState.Queued,
                        Dependencies = [],
                        BlockedBy = [],
                        ClarificationReturnPath = string.Empty,
                        PacketPaths = new PacketPaths
                        {
                            Implementation = $".intent-cli/issues/{Unit}/implementation.md",
                            ReviewContext = $".intent-cli/issues/{Unit}/review-context.md",
                            Yaml = $".intent-cli/issues/{Unit}/packet.yaml"
                        },
                        LinkedIssue = linkedIssue,
                        WorkerRole = "child-impl",
                        ReviewRole = "host-review",
                        Priority = "normal"
                    }
                ]
            }));
        }

        public Dictionary<string, byte[]> SnapshotDurableFiles() =>
            new(StringComparer.Ordinal)
            {
                ["queue-state.json"] = ReadOptional(Path.Combine(root, ".intent-cli", "queue-state.json")),
                ["publish.yaml"] = ReadOptional(PublishYamlPath),
                ["runs.jsonl"] = ReadOptional(RunsPath)
            };

        public (int ExitCode, JsonElement Root) Run(bool write)
        {
            using var writer = new StringWriter();
            var args = new List<string> { Unit, "--repo", Repo, "--format", "json" };
            if (write)
            {
                args.Add("--write");
            }

            var exitCode = IssuePublishFlowCommand.Execute(Context, args.ToArray(), writer);
            using var document = JsonDocument.Parse(writer.ToString());
            return (exitCode, document.RootElement.Clone());
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static byte[] ReadOptional(string path) => File.Exists(path) ? File.ReadAllBytes(path) : [];
    }

    private sealed class CountingCreator : IIssueCreator
    {
        public int CallCount { get; private set; }

        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
        {
            CallCount++;
            return new IssueCreateOutcome($"https://github.com/{repo}/issues/2000");
        }
    }

    private sealed class CountingExistingIssueChecker(
        GitHubExistingIssueClassification classification,
        int? issueNumber = null,
        string? issueUrl = null) : IGitHubExistingIssueChecker
    {
        public int CallCount { get; private set; }

        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
        {
            CallCount++;
            return new GitHubExistingIssueLookupResult
            {
                Classification = classification,
                IssueNumber = issueNumber,
                IssueUrl = issueUrl
            };
        }
    }
}

[Collection("IssueSyncBodySharedState")]
public sealed class G846SyncBodyTests : IDisposable
{
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G846";
    private readonly string root = Directory.CreateTempSubdirectory("g846-sync-").FullName;
    private readonly CliContext context;

    public G846SyncBodyTests()
    {
        context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" }
            }
        };
        IssueSyncBodyCommand.TimestampFactory = () => new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        IssueSyncBodyCommand.BodyClientFactory = () => new GhCliGitHubIssueBodyClient();
        IssueSyncBodyCommand.TimestampFactory = () => DateTimeOffset.UtcNow;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedBomBody_RefusesBeforeRemoteReadInBothModes(bool write)
    {
        var client = new CountingBodyClient(G846BodyFixtures.Decode(G846BodyFixtures.BodyBytes(50000)));
        IssueSyncBodyCommand.BodyClientFactory = () => client;
        WritePublishedBody(G846BodyFixtures.BodyBytes(65539, bom: true));

        var result = Run(write);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("body-too-large", result.Root.GetProperty("reason_code").GetString());
        Assert.Equal("refused", result.Root.GetProperty("outcome").GetString());
        Assert.Equal(65539, result.Root.GetProperty("local_bytes").GetInt32());
        Assert.False(result.Root.GetProperty("may_have_applied").GetBoolean());
        Assert.Contains("65539", result.Root.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("65536", result.Root.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, client.ReadCount);
        Assert.Equal(0, client.UpdateCount);
        Assert.DoesNotContain("body-invalid", result.Root.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WarningBand_PreservesWarningWhenRemoteReadFails()
    {
        var client = new CountingBodyClient(string.Empty) { ThrowOnRead = true };
        IssueSyncBodyCommand.BodyClientFactory = () => client;
        WritePublishedBody(G846BodyFixtures.BodyBytes(58002, bom: true));

        var result = Run(write: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("remote-read-failed", result.Root.GetProperty("reason_code").GetString());
        Assert.Equal(["issue-body-size-warning"], result.Root.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal(1, client.ReadCount);
    }

    [Theory]
    [InlineData(57999, 0, false)]
    [InlineData(58000, 0, true)]
    [InlineData(58001, 0, true)]
    [InlineData(65535, 0, true)]
    [InlineData(65536, 0, true)]
    [InlineData(65537, 1, false)]
    public void BoundaryCases_AreConsistentOnSyncBody(int bytes, int expectedExit, bool expectedWarning)
    {
        var client = new CountingBodyClient(G846BodyFixtures.Decode(G846BodyFixtures.BodyBytes(50000)));
        IssueSyncBodyCommand.BodyClientFactory = () => client;
        WritePublishedBody(G846BodyFixtures.BodyBytes(bytes));

        var result = Run(write: false);

        Assert.Equal(expectedExit, result.ExitCode);
        var warnings = result.Root.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(expectedWarning, warnings.Contains("issue-body-size-warning"));
        if (expectedExit == 1 && bytes == 65537)
        {
            Assert.Equal("body-too-large", result.Root.GetProperty("reason_code").GetString());
            Assert.Equal(0, client.ReadCount);
        }
    }

    [Fact]
    public void MarkdownOutput_AlwaysIncludesWarningsLine()
    {
        var client = new CountingBodyClient(G846BodyFixtures.Decode(G846BodyFixtures.BodyBytes(50000)));
        IssueSyncBodyCommand.BodyClientFactory = () => client;
        WritePublishedBody(G846BodyFixtures.BodyBytes(58002, bom: true));

        using var writer = new StringWriter();
        var exitCode = IssueSyncBodyCommand.Execute(
            context,
            [Unit, "--repo", Repo, "--dry-run", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("- warnings:\n  - issue-body-size-warning", writer.ToString(), StringComparison.Ordinal);
    }

    private string PacketDirectory => Path.Combine(root, ".intent-cli", "issues", Unit);

    private string BodyPath => Path.Combine(PacketDirectory, "github-body.md");

    private void WritePublishedBody(byte[] bytes)
    {
        Directory.CreateDirectory(PacketDirectory);
        File.WriteAllBytes(BodyPath, bytes);
        File.WriteAllText(Path.Combine(PacketDirectory, "packet.yaml"), "implementation_issue_packet:\n  source_execution_unit: G846\n");
        File.WriteAllText(Path.Combine(PacketDirectory, "publish.yaml"), IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
        {
            ExecutionUnit = Unit,
            PublishStatus = "issue-created",
            PacketPath = $".intent-cli/issues/{Unit}/packet.yaml",
            IssueBodyPath = BodyPath,
            CreatedIssueNumber = 1900,
            CreatedIssueUrl = $"https://github.com/{Repo}/issues/1900",
            PublishedLabelName = "intent-target",
            LifecycleState = "published"
        }));
    }

    private (int ExitCode, JsonElement Root) Run(bool write)
    {
        using var writer = new StringWriter();
        var exitCode = IssueSyncBodyCommand.Execute(
            context,
            [Unit, "--repo", Repo, write ? "--write" : "--dry-run", "--format", "json"],
            writer);
        using var document = JsonDocument.Parse(writer.ToString());
        return (exitCode, document.RootElement.Clone());
    }

    private sealed class CountingBodyClient(string body) : IGitHubIssueBodyClient
    {
        private string currentBody = body;

        public int ReadCount { get; private set; }

        public int UpdateCount { get; private set; }

        public bool ThrowOnRead { get; init; }

        public string ReadBody(string repo, int issueNumber)
        {
            ReadCount++;
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("fake remote read failure");
            }

            return currentBody;
        }

        public void UpdateBody(string repo, int issueNumber, string bodyFilePath)
        {
            UpdateCount++;
            currentBody = File.ReadAllText(bodyFilePath);
        }
    }
}

public sealed class G846DocumentationTests
{
    [Fact]
    public void EnglishAndJapaneseDocsDescribeTheEarlySizeContractAndHedge()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var path in new[]
        {
            Path.Combine(root, "docs", "en", "04-packets-issues.md"),
            Path.Combine(root, "docs", "ja", "04-packets-issues.md"),
            Path.Combine(root, "docs", "en", "1.0-compatibility-ledger.md"),
            Path.Combine(root, "docs", "ja", "1.0-compatibility-ledger.md")
        })
        {
            var content = File.ReadAllText(path);
            Assert.Contains("HardLimitBytes = 65536", content, StringComparison.Ordinal);
            Assert.Contains("WarningThresholdBytes = 58000", content, StringComparison.Ordinal);
            Assert.Contains("issue-body-too-large", content, StringComparison.Ordinal);
            Assert.Contains("body-too-large", content, StringComparison.Ordinal);
            Assert.Contains("issue-body-size-warning", content, StringComparison.Ordinal);
            Assert.Contains("body_bytes", content, StringComparison.Ordinal);
            Assert.Contains("body_too_large", content, StringComparison.Ordinal);
            Assert.Contains("body_size_warning", content, StringComparison.Ordinal);
            Assert.Contains("body_size_reason", content, StringComparison.Ordinal);
            Assert.Contains("warnings", content, StringComparison.Ordinal);
            Assert.Contains("65,536 is intent-cli's own conservative limit", content, StringComparison.Ordinal);
            Assert.Contains("GitHub's boundary, inclusivity and unit were not verified", content, StringComparison.Ordinal);
            Assert.Contains("roughly 96,000-character failure", content, StringComparison.Ordinal);
        }
    }
}

internal static class G846BodyFixtures
{
    public static byte[] BodyBytes(int totalBytes, bool bom = false)
    {
        var content = Encoding.UTF8.GetBytes(ValidBody());
        var prefix = bom ? new byte[] { 0xEF, 0xBB, 0xBF } : [];
        var contentLength = totalBytes - prefix.Length;
        Assert.True(contentLength >= content.Length, $"fixture size {totalBytes} is smaller than the deterministic contract body");
        var padding = new byte[contentLength - content.Length];
        for (var index = 0; index < padding.Length; index++)
        {
            padding[index] = (byte)('a' + (index % 26));
        }

        return [.. prefix, .. content, .. padding];
    }

    public static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static string ValidBody()
    {
        var builder = new StringBuilder("# G846 deterministic fixture\n\n");
        foreach (var heading in IssueValidateBodyValidator.RequiredHeadings)
        {
            builder.Append("## ").Append(heading).Append("\n\n");
            if (heading == "Target Repo / Path / Part")
            {
                builder.Append("- Repository: J-Tech-Japan/intent-system\n");
                builder.Append("- Target paths: src/IntentSystem.Cli/Commands, tests/IntentSystem.Cli.Tests\n");
            }
            else if (heading == "Related Links")
            {
                builder.Append("- https://github.com/J-Tech-Japan/intent-system/issues/1835\n");
            }
            else
            {
                builder.Append("Deterministic fixture content for ").Append(heading).Append(".\n");
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }
}

internal sealed class BodyWorkspace : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g846-body-").FullName;

    public BodyWorkspace(string _)
    {
        Context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" }
            }
        };
    }

    public CliContext Context { get; }

    public string WriteBody(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

internal sealed class PacketWorkspace : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g846-packet-").FullName;

    public PacketWorkspace(string _)
    {
        Context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }

    public CliContext Context { get; }

    public string PacketDirectory(string unit) => Path.Combine(root, ".intent-cli", "issues", unit);

    public void WritePacket(string unit, byte[] body)
    {
        var packetDirectory = PacketDirectory(unit);
        Directory.CreateDirectory(packetDirectory);
        File.WriteAllBytes(Path.Combine(packetDirectory, "github-body.md"), body);
        File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), $"source_execution_unit: {unit}\n");
        File.WriteAllText(Path.Combine(packetDirectory, "implementation.md"), "implementation\n");
        File.WriteAllText(Path.Combine(packetDirectory, "review-context.md"), "review\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
