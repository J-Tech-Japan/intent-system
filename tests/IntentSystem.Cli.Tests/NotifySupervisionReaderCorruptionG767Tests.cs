using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G767: supervision history is read record-by-record. Corruption remains
/// visible evidence rather than becoming either a fatal whole-store read or a
/// clean absence.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionReaderCorruptionG767Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "intent-g767-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public NotifySupervisionReaderCorruptionG767Tests()
    {
        Directory.CreateDirectory(root);
        NotifyCommand.UtcNowFactory = () => now;
        NotifyCommand.ProcessRunnerFactory = null;
    }

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
    public void OneMalformedCycleLine_PreservesReadableCycleAndReportsPartialEvidence_G767()
    {
        var artifactRoot = ArtifactRoot(root);
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        WriteCycle(cyclePath, "readable-cycle", now.AddMinutes(-1));
        File.AppendAllText(cyclePath, "{\"kind\":\"cycle\",\"cycle\":\n");

        var (exitCode, payload) = RunLiveness(root);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(payload);
        var result = document.RootElement;
        Assert.False(result.TryGetProperty("success", out _));
        Assert.Equal(1, result.GetProperty("unreadable_record_count").GetInt32());
        var evidence = Assert.Single(result.GetProperty("unreadable_records").EnumerateArray());
        Assert.Equal("cycles.jsonl", evidence.GetProperty("file").GetString());
        Assert.Equal(2, evidence.GetProperty("line").GetInt32());
        Assert.Contains("partial reading", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);

        var state = NotifySupervisionStore.Read(artifactRoot, Domain, Team);
        Assert.True(state.Resolved, state.Error);
        Assert.Equal("readable-cycle", Assert.Single(state.CycleHistory).CycleId);
    }

    [Fact]
    public void CleanAndOneMalformedDiscriminatingPair_ProducesDifferentAnswers_G767()
    {
        var cleanRoot = Path.Combine(root, "clean");
        var corruptRoot = Path.Combine(root, "corrupt");
        var cleanPath = NotifySupervisionStore.ResolveCyclePath(ArtifactRoot(cleanRoot), Domain, Team);
        var corruptPath = NotifySupervisionStore.ResolveCyclePath(ArtifactRoot(corruptRoot), Domain, Team);
        WriteCycle(cleanPath, "same-readable-cycle", now.AddMinutes(-1));
        WriteCycle(corruptPath, "same-readable-cycle", now.AddMinutes(-1));
        File.AppendAllText(corruptPath, "not-json\n");

        var (cleanExit, cleanPayload) = RunLiveness(cleanRoot);
        var (corruptExit, corruptPayload) = RunLiveness(corruptRoot);

        Assert.Equal(0, cleanExit);
        Assert.Equal(0, corruptExit);
        using var cleanDocument = JsonDocument.Parse(cleanPayload);
        using var corruptDocument = JsonDocument.Parse(corruptPayload);
        var clean = cleanDocument.RootElement;
        var corrupt = corruptDocument.RootElement;
        Assert.Equal(0, clean.GetProperty("unreadable_record_count").GetInt32());
        Assert.False(clean.TryGetProperty("unreadable_records", out _));
        Assert.DoesNotContain("partial reading", clean.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, corrupt.GetProperty("unreadable_record_count").GetInt32());
        Assert.True(corrupt.TryGetProperty("unreadable_records", out _));
        Assert.Contains("partial reading", corrupt.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(corrupt.TryGetProperty("success", out _));
        Assert.NotEqual(
            clean.GetProperty("unreadable_record_count").GetInt32(),
            corrupt.GetProperty("unreadable_record_count").GetInt32());
    }

    [Fact]
    public void AllCorruptAndEmptyDiscriminatingPair_NeverLooksLikeCleanAbsence_G767()
    {
        var emptyRoot = Path.Combine(root, "empty");
        var corruptRoot = Path.Combine(root, "all-corrupt");
        var corruptPath = NotifySupervisionStore.ResolveCyclePath(ArtifactRoot(corruptRoot), Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
        File.WriteAllText(corruptPath, "not-json\n");

        var (emptyExit, emptyPayload) = RunLiveness(emptyRoot);
        var (corruptExit, corruptPayload) = RunLiveness(corruptRoot);

        Assert.Equal(0, emptyExit);
        Assert.Equal(0, corruptExit);
        using var emptyDocument = JsonDocument.Parse(emptyPayload);
        using var corruptDocument = JsonDocument.Parse(corruptPayload);
        var empty = emptyDocument.RootElement;
        var corrupt = corruptDocument.RootElement;
        AssertCleanPayloadMatchesParentOracle(emptyPayload, emptyRoot);
        Assert.True(empty.GetProperty("absent_since_last_cycle").GetBoolean());
        Assert.True(corrupt.GetProperty("absent_since_last_cycle").GetBoolean());
        Assert.Contains("No supervision state was found", empty.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("No completed supervision cycle", corrupt.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("No readable cycle", corrupt.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.False(empty.TryGetProperty("success", out _));
        Assert.False(corrupt.TryGetProperty("success", out _));
        Assert.Equal(0, empty.GetProperty("unreadable_record_count").GetInt32());
        Assert.Equal(1, corrupt.GetProperty("unreadable_record_count").GetInt32());
        Assert.NotEqual(
            empty.GetProperty("unreadable_record_count").GetInt32(),
            corrupt.GetProperty("unreadable_record_count").GetInt32());
    }

    [Fact]
    public void StallsAndPromptAudits_PreserveValidRecordsAndReportMalformedLines_G767()
    {
        var artifactRoot = ArtifactRoot(root);
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        Assert.True(NotifySupervisionStore.OpenStall(
            stallsPath,
            new NotifySupervisionStallRecord
            {
                Key = "g767-valid-stall",
                Kind = "g767-test-stall",
                OwnerRole = "orchestration",
                Source = "g767-test",
                Summary = "valid stall",
                SurfacedAt = now.AddMinutes(-2),
            },
            write: true).Applied);
        File.AppendAllText(stallsPath, "not-json-stall\n");
        Assert.True(NotifySupervisionStore.RecordPromptAudit(
            cyclePath,
            new NotifyPromptAudit
            {
                PromptKey = "g767-prompt",
                Seat = "implementation",
                Pane = "g767:p1",
                AgentKind = "fixture",
                PromptClass = "test",
                Rule = "test",
                Actor = "g767",
                Timestamp = now.AddMinutes(-1),
                Outcome = "observed",
            },
            write: true).Applied);
        File.AppendAllText(cyclePath, "not-json-prompt\n");

        var state = NotifySupervisionStore.Read(artifactRoot, Domain, Team);

        Assert.True(state.Resolved, state.Error);
        Assert.Contains(state.StallHistory, item => item.Key == "g767-valid-stall");
        Assert.Contains(state.PromptAudits, item => item.PromptKey == "g767-prompt");
        using var evidence = JsonDocument.Parse(JsonSerializer.Serialize(
            typeof(NotifySupervisionReadResult)
                .GetProperty("UnreadableRecords")!
                .GetValue(state),
            JsonOptions));
        var records = evidence.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, records.Length);
        Assert.Contains(records, item =>
            item.GetProperty("file").GetString() == "stalls.jsonl"
            && item.GetProperty("line").GetInt32() == 2);
        Assert.Contains(records, item =>
            item.GetProperty("file").GetString() == "cycles.jsonl"
            && item.GetProperty("line").GetInt32() == 2);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DocumentationDescribesRecordLevelCorruptionEvidence_G767(string language)
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md");
        var document = File.ReadAllText(path);

        Assert.Contains("G767", document, StringComparison.Ordinal);
        Assert.Contains("unreadable_record_count", document, StringComparison.Ordinal);
        Assert.Contains("unreadable_records", document, StringComparison.Ordinal);
        Assert.Contains("cycles.jsonl", document, StringComparison.Ordinal);
        Assert.Contains("stalls.jsonl", document, StringComparison.Ordinal);
        Assert.Contains("663,959", document, StringComparison.Ordinal);
        Assert.Contains("0.0014%", document, StringComparison.Ordinal);
        Assert.Contains("fragment", document, StringComparison.OrdinalIgnoreCase);
        if (language == "en")
        {
            Assert.Contains("omits the failure-only", document, StringComparison.Ordinal);
            Assert.Contains("`success` field", document, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("失敗応答専用の `success` field は含めません", document, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FailedResponseRetainsFailureOnlySuccessFalse_G767()
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(root),
            [
                "liveness", "--domain", Domain, "--team", Team,
                "--routing-root", "\0", "--format", "json",
            ],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    private (int ExitCode, string Payload) RunLiveness(string repoRoot)
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(repoRoot),
            ["liveness", "--domain", Domain, "--team", Team, "--format", "json"],
            writer);
        return (exitCode, writer.ToString());
    }

    private void WriteCycle(string path, string cycleId, DateTimeOffset completedAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.True(NotifySupervisionStore.RecordCycle(
            path,
            new NotifySupervisionCycle
            {
                CycleId = cycleId,
                StartedAt = completedAt.AddSeconds(-1),
                CompletedAt = completedAt,
                IntervalSeconds = 300,
            },
            write: true).Applied);
    }

    private static string ArtifactRoot(string repoRoot) => Path.Combine(repoRoot, ".intent-cli", "supervision");

    private static CliContext CreateContext(string repoRoot) => new()
    {
        RepoRoot = repoRoot,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
            },
            Supervision = new SupervisionConfig
            {
                ArtifactRoot = ".intent-cli/supervision",
            },
        },
    };

    private static void AssertCleanPayloadMatchesParentOracle(string payload, string repoRoot)
    {
        using var document = JsonDocument.Parse(payload);
        var actual = document.RootElement
            .EnumerateObject()
            .Select(property => (property.Name, RawValue: property.Value.GetRawText()))
            .ToArray();
        var expectedDirectory = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "supervision", Domain, Team));
        var expected = new[]
        {
            ("operation", JsonSerializer.Serialize("supervise-liveness")),
            ("routing_root", JsonSerializer.Serialize(Path.GetFullPath(repoRoot))),
            ("domain", JsonSerializer.Serialize(Domain)),
            ("team", JsonSerializer.Serialize(Team)),
            ("command_mode", JsonSerializer.Serialize("read-only")),
            // G770 authorizes this exact clean-payload addition so the
            // no-flag path exposes the same state distinction as G769's
            // explicit-root path.
            ("supervision_state", JsonSerializer.Serialize("not-found")),
            ("absent_since_last_cycle", "true"),
            ("scheduler_installation_evidence", JsonSerializer.Serialize("unknown")),
            ("scheduler_live_state", JsonSerializer.Serialize("unknown")),
            ("scheduler_evidence_detail", JsonSerializer.Serialize(
                "durable installation evidence is unavailable; scheduler live state is unknown because no OS lifecycle query was executed")),
            ("scheduler_artifact_paths", "[]"),
            ("commands_executed", JsonSerializer.Serialize(
                "none (persisted supervision state and artifact metadata only)")),
            // The authorized G767 delta from the parent clean payload is this
            // corruption counter, which must remain zero for a clean store.
            ("unreadable_record_count", "0"),
            ("summary", JsonSerializer.Serialize(
                $"Read-only liveness: No supervision state was found at '{expectedDirectory}'; no supervisor process is required to produce this answer. Supervision is absent or beyond its declared bound. Scheduler live state=unknown; durable installation evidence=unknown; the supervisor was not run.")),
        };

        Assert.Equal(expected, actual);
        Assert.False(document.RootElement.TryGetProperty("success", out _));
    }
}
