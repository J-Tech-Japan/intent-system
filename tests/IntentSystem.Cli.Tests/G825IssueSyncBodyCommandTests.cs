using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G825 (#1777): issue sync-body updates an already-published child issue body.
/// Every local gate runs before any GitHub call; the write is
/// read-compare-write with read-back verification and says it is not atomic;
/// nothing but the body changes.
/// </summary>
[Collection("IssueSyncBodySharedState")]
public sealed class G825IssueSyncBodyCommandTests : IDisposable
{
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G900";
    private const int IssueNumber = 1900;

    private readonly string root;
    private readonly CliContext context;

    public G825IssueSyncBodyCommandTests()
    {
        root = Directory.CreateTempSubdirectory("g825-host-").FullName;
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "issues"));
        context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" }
            }
        };
        IssueSyncBodyCommand.TimestampFactory = () => new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
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

    // ── refusals: no GitHub call ───────────────────────────────────────────

    [Fact]
    public void Refuses_WhenPacketDirectoryMissing_WithoutCallingGitHub()
    {
        var client = Install(new FakeBodyClient("remote"));
        var result = Run(write: true);
        AssertRefused(result, "packet-missing", client);
    }

    [Fact]
    public void Refuses_WhenBodyMissing_WithoutCallingGitHub()
    {
        Directory.CreateDirectory(PacketDir);
        var client = Install(new FakeBodyClient("remote"));
        AssertRefused(Run(write: true), "body-missing", client);
    }

    [Fact]
    public void Refuses_WhenBodyFailsValidation_WithoutCallingGitHub()
    {
        Directory.CreateDirectory(PacketDir);
        File.WriteAllText(BodyPath, "## Goal\n\nOnly a goal.\n");
        WritePublish($"https://github.com/{Repo}/issues/{IssueNumber}");
        var client = Install(new FakeBodyClient("remote"));
        AssertRefused(Run(write: true), "body-invalid", client);
    }

    [Fact]
    public void Refuses_WhenNotPublished_WithoutCallingGitHub()
    {
        WriteBody(ValidBody("v2"));
        var client = Install(new FakeBodyClient("remote"));
        AssertRefused(Run(write: true), "not-published", client);
    }

    [Theory]
    [InlineData("https://github.com/J-Tech-Japan/SekibanWasmRuntime/issues/1900", "foreign-repository")]
    [InlineData("https://ghe.example.com/J-Tech-Japan/intent-system/issues/1900", "issue-repository-unproven")]
    [InlineData("https://github.com/J-Tech-Japan/intent-system/issues/1901", "issue-repository-unproven")]
    public void Refuses_WhenIssueDoesNotBindToRepo_WithoutCallingGitHub(string url, string reason)
    {
        WriteBody(ValidBody("v2"));
        WritePublish(url);
        var client = Install(new FakeBodyClient("remote"));
        AssertRefused(Run(write: true), reason, client);
    }

    // ── dry-run ────────────────────────────────────────────────────────────

    [Fact]
    public void DryRun_ReportsDigestsAndLineCounts_WithOneReadAndNoWrite()
    {
        Publish(ValidBody("v2"));
        var remote = ValidBody("v1");
        var client = Install(new FakeBodyClient(remote));
        var runsBefore = RunsBytes();

        var result = Run(write: false);

        Assert.Equal(0, result.Exit);
        Assert.Equal(IssueSyncBodyCommand.OutcomeDiffers, result.Json.GetProperty("outcome").GetString());
        Assert.Equal(Sha(ValidBody("v2")), result.Json.GetProperty("local_sha256").GetString());
        Assert.Equal(Sha(remote), result.Json.GetProperty("remote_sha256").GetString());
        Assert.Equal(1, result.Json.GetProperty("lines_added").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("lines_removed").GetInt32());
        Assert.Equal(1, client.Reads);
        Assert.Equal(0, client.Updates);
        Assert.Equal(runsBefore, RunsBytes());
        AssertGuarantee(result);
    }

    [Fact]
    public void DryRun_IdenticalBody_IsNoOp()
    {
        Publish(ValidBody("v2"));
        var client = Install(new FakeBodyClient(ValidBody("v2")));

        var result = Run(write: false);

        Assert.Equal(IssueSyncBodyCommand.OutcomeNoOp, result.Json.GetProperty("outcome").GetString());
        Assert.Equal(0, client.Updates);
    }

    // ── write ──────────────────────────────────────────────────────────────

    [Fact]
    public void Write_ReplacesBodyOnce_VerifiesByReadBack_AndChangesNothingElse()
    {
        Publish(ValidBody("v2"));
        var hostFiles = SnapshotHostFiles();
        var remote = ValidBody("v1");
        var client = Install(new FakeBodyClient(remote));

        var result = Run(write: true, expected: Sha(remote));

        Assert.Equal(0, result.Exit);
        Assert.Equal(IssueSyncBodyCommand.OutcomeApplied, result.Json.GetProperty("outcome").GetString());
        Assert.Equal(1, client.Updates);
        Assert.Equal(2, client.Reads);
        Assert.Equal(ValidBody("v2"), client.Body);
        Assert.Equal(Sha(ValidBody("v2")), result.Json.GetProperty("remote_after_sha256").GetString());
        Assert.False(result.Json.GetProperty("may_have_applied").GetBoolean());
        AssertGuarantee(result);
        AssertHostFilesUnchanged(hostFiles);

        var runEvent = Assert.Single(RunsLines());
        Assert.Contains($"\"event\":\"{IssueSyncBodyCommand.EventSynced}\"", runEvent, StringComparison.Ordinal);
        Assert.Contains(Sha(remote), runEvent, StringComparison.Ordinal);
        Assert.Contains(Sha(ValidBody("v2")), runEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ExpectedDigestMismatch_RefusesAsConcurrentEdit_WithZeroWrites()
    {
        Publish(ValidBody("v2"));
        var client = Install(new FakeBodyClient(ValidBody("edited by a human after review")));
        var runsBefore = RunsBytes();

        var result = Run(write: true, expected: Sha(ValidBody("v1")));

        Assert.Equal(1, result.Exit);
        Assert.Equal(IssueSyncBodyCommand.OutcomeConcurrentEdit, result.Json.GetProperty("outcome").GetString());
        Assert.Equal(0, client.Updates);
        Assert.Equal(runsBefore, RunsBytes());
        AssertGuarantee(result);
    }

    [Fact]
    public void Write_RemoteAlreadyMatches_IsNoOpWithoutUpdate_AndRecordsNoOpEvent()
    {
        Publish(ValidBody("v2"));
        var client = Install(new FakeBodyClient(ValidBody("v2")));

        var result = Run(write: true);

        Assert.Equal(0, result.Exit);
        Assert.Equal(IssueSyncBodyCommand.OutcomeNoOp, result.Json.GetProperty("outcome").GetString());
        Assert.Equal(0, client.Updates);
        Assert.Contains($"\"event\":\"{IssueSyncBodyCommand.EventNoOp}\"", Assert.Single(RunsLines()), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ReadBackMismatch_ReportsVerificationFailed_AndDoesNotRetry()
    {
        Publish(ValidBody("v2"));
        var client = Install(new FakeBodyClient(ValidBody("v1")) { BodyAfterUpdate = ValidBody("someone else's edit") });

        var result = Run(write: true);

        Assert.Equal(1, result.Exit);
        Assert.Equal(IssueSyncBodyCommand.OutcomeVerificationFailed, result.Json.GetProperty("outcome").GetString());
        Assert.True(result.Json.GetProperty("may_have_applied").GetBoolean());
        Assert.Equal($"gh issue view {IssueNumber} --repo {Repo} --json body", result.Json.GetProperty("recovery_command").GetString());
        Assert.Equal(1, client.Updates);
        Assert.Contains($"\"event\":\"{IssueSyncBodyCommand.EventUnverified}\"", Assert.Single(RunsLines()), StringComparison.Ordinal);
        AssertGuarantee(result);
    }

    [Fact]
    public void Write_UpdateThrows_ReportsVerificationFailedAsMayHaveApplied()
    {
        Publish(ValidBody("v2"));
        var client = Install(new FakeBodyClient(ValidBody("v1")) { ThrowOnUpdate = true });

        var result = Run(write: true);

        Assert.Equal(IssueSyncBodyCommand.OutcomeVerificationFailed, result.Json.GetProperty("outcome").GetString());
        Assert.True(result.Json.GetProperty("may_have_applied").GetBoolean());
        Assert.Equal(1, client.Updates);
    }

    [Fact]
    public void Write_ReadBackDiffersOnlyByTrailingNewline_IsVerified_AndSaysSo()
    {
        Publish(ValidBody("v2"));
        var client = Install(new FakeBodyClient(ValidBody("v1")) { BodyAfterUpdate = ValidBody("v2").TrimEnd('\n') });

        var result = Run(write: true, format: "markdown");

        Assert.Equal(0, result.Exit);
        Assert.Contains("outcome: **applied**", result.Text, StringComparison.Ordinal);
        Assert.Contains("only by a trailing newline", result.Text, StringComparison.Ordinal);
        Assert.Contains("cannot be excluded", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsWriteWithDryRun_AndMalformedDigest()
    {
        using var w1 = new StringWriter();
        Assert.Equal(1, IssueSyncBodyCommand.Execute(context, [Unit, "--repo", Repo, "--write", "--dry-run"], w1));
        using var w2 = new StringWriter();
        Assert.Equal(1, IssueSyncBodyCommand.Execute(context, [Unit, "--repo", Repo, "--expected-remote-sha256", "abc"], w2));
        Assert.Contains("64-character hex", w2.ToString(), StringComparison.Ordinal);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private string PacketDir => Path.Combine(root, ".intent-cli", "issues", Unit);

    private string BodyPath => Path.Combine(PacketDir, "github-body.md");

    private static string ValidBody(string marker)
    {
        var sb = new StringBuilder();
        foreach (var heading in IssueValidateBodyValidator.RequiredHeadings)
        {
            sb.Append("## ").Append(heading).Append("\n\n");
            sb.Append(heading switch
            {
                "Target Repo / Path / Part" => "Repository: `J-Tech-Japan/intent-system`\n\n- Target paths: `src/IntentSystem.Cli/Commands/Example.cs`\n",
                "Related Links" => "- https://github.com/J-Tech-Japan/intent-system/issues/1777\n",
                _ => $"Content for {heading}.\n",
            });
            sb.Append('\n');
        }
        sb.Append("contract-version: ").Append(marker).Append('\n');
        var body = sb.ToString();
        Assert.True(IssueValidateBodyValidator.Validate("fixture.md", body, requireTargetPathsDeclaration: true).IsValid,
            "the G825 fixture body no longer satisfies the publish contract validator");
        return body;
    }

    private void WriteBody(string body)
    {
        Directory.CreateDirectory(PacketDir);
        File.WriteAllText(BodyPath, body);
        File.WriteAllText(Path.Combine(PacketDir, "packet.yaml"), "implementation_issue_packet:\n  source_execution_unit: G900\n");
    }

    private void WritePublish(string url)
    {
        Directory.CreateDirectory(PacketDir);
        File.WriteAllText(Path.Combine(root, IssuePublishArtifactPathResolver.Resolve(Unit)), IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
        {
            ExecutionUnit = Unit,
            PublishStatus = "issue-created",
            PacketPath = $".intent-cli/issues/{Unit}/packet.yaml",
            IssueBodyPath = $".intent-cli/issues/{Unit}/github-body.md",
            CreatedIssueNumber = IssueNumber,
            CreatedIssueUrl = url,
            PublishedLabelName = "intent-target",
            LifecycleState = "published"
        }));
    }

    private void Publish(string body)
    {
        WriteBody(body);
        WritePublish($"https://github.com/{Repo}/issues/{IssueNumber}");
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), "{\"items\":[]}\n");
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "claims", "g900.json"), "{\"scope\":\"execution-unit:G900\"}\n");
    }

    private static FakeBodyClient Install(FakeBodyClient client)
    {
        IssueSyncBodyCommand.BodyClientFactory = () => client;
        return client;
    }

    private RunResult Run(bool write, string? expected = null, string format = "json")
    {
        var args = new List<string> { Unit, "--repo", Repo, write ? "--write" : "--dry-run", "--format", format };
        if (expected is not null)
        {
            args.AddRange(["--expected-remote-sha256", expected]);
        }

        using var writer = new StringWriter();
        var exit = IssueSyncBodyCommand.Execute(context, args.ToArray(), writer);
        var text = writer.ToString();
        JsonElement json = default;
        if (format == "json")
        {
            using var document = JsonDocument.Parse(text);
            json = document.RootElement.Clone();
        }
        return new RunResult(exit, json, text);
    }

    private static void AssertRefused(RunResult result, string reason, FakeBodyClient client)
    {
        Assert.Equal(1, result.Exit);
        Assert.Equal(IssueSyncBodyCommand.OutcomeRefused, result.Json.GetProperty("outcome").GetString());
        Assert.Equal(reason, result.Json.GetProperty("reason_code").GetString());
        Assert.Equal(0, client.Reads);
        Assert.Equal(0, client.Updates);
        AssertGuarantee(result);
    }

    private static void AssertGuarantee(RunResult result) =>
        Assert.Equal(IssueSyncBodyCommand.ConcurrencyGuarantee, result.Json.GetProperty("concurrency_guarantee").GetString());

    private string RunsPath => context.GetRunLogPath();

    private byte[] RunsBytes() => File.Exists(RunsPath) ? File.ReadAllBytes(RunsPath) : [];

    private string[] RunsLines() => File.Exists(RunsPath)
        ? File.ReadAllLines(RunsPath).Where(line => line.Length > 0).ToArray()
        : [];

    private Dictionary<string, byte[]> SnapshotHostFiles() =>
        Directory.EnumerateFiles(Path.Combine(root, ".intent-cli"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    private void AssertHostFilesUnchanged(Dictionary<string, byte[]> before)
    {
        foreach (var (path, bytes) in before)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"host file changed: {path}");
        }
        var added = Directory.EnumerateFiles(Path.Combine(root, ".intent-cli"), "*", SearchOption.AllDirectories)
            .Except(before.Keys)
            .Where(path => !string.Equals(path, RunsPath, StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(added);
    }

    private static string Sha(string text) => IssuePrepareCommand.ComputeSha256Hex(Encoding.UTF8.GetBytes(text));

    private readonly record struct RunResult(int Exit, JsonElement Json, string Text);

    private sealed class FakeBodyClient(string initialBody) : IGitHubIssueBodyClient
    {
        public string Body { get; private set; } = initialBody;

        public string? BodyAfterUpdate { get; init; }

        public bool ThrowOnUpdate { get; init; }

        public int Reads { get; private set; }

        public int Updates { get; private set; }

        public string ReadBody(string repo, int issueNumber)
        {
            Assert.Equal(Repo, repo);
            Assert.Equal(IssueNumber, issueNumber);
            Reads++;
            return Body;
        }

        public void UpdateBody(string repo, int issueNumber, string bodyFilePath)
        {
            Assert.Equal(Repo, repo);
            Assert.Equal(IssueNumber, issueNumber);
            Updates++;
            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException("simulated network failure after send");
            }
            Body = BodyAfterUpdate ?? File.ReadAllText(bodyFilePath);
        }
    }
}
