using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G773: unreadable supervision records are quarantined as exact evidence by
/// an explicit operator command. The normal reader remains read-only.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionRepairUnreadableG773Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "intent-g773-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public NotifySupervisionRepairUnreadableG773Tests()
    {
        Directory.CreateDirectory(root);
        NotifyCommand.UtcNowFactory = () => now;
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifySupervisionStore.RepairUnreadableBeforeLengthRecheck = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultDryRun_EnumeratesEveryUnreadableLineAndLeavesStoreByteIdentical_G773()
    {
        var cyclePath = WriteCycle("readable-cycle");
        var unreadable = Encoding.UTF8.GetBytes("{bad-cycle-json}\r\n");
        File.AppendAllBytes(cyclePath, unreadable);
        var before = SnapshotDirectory(TeamDirectory());

        var (exitCode, output) = RunRepair();

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        var payload = document.RootElement;
        Assert.Equal("supervise-repair-unreadable", payload.GetProperty("operation").GetString());
        Assert.Equal("dry-run", payload.GetProperty("command_mode").GetString());
        Assert.Equal("would-repair", payload.GetProperty("repair_state").GetString());
        var unreadableRecord = Assert.Single(payload.GetProperty("unreadable_records").EnumerateArray());
        Assert.Equal("cycles.jsonl", unreadableRecord.GetProperty("file").GetString());
        Assert.Equal(2, unreadableRecord.GetProperty("line").GetInt32());
        Assert.Equal("invalid-json", unreadableRecord.GetProperty("reason").GetString());
        Assert.Equal("{bad-cycle-json}"u8.Length, unreadableRecord.GetProperty("byte_length").GetInt32());
        Assert.Equal(before, SnapshotDirectory(TeamDirectory()));
        Assert.False(File.Exists(Path.Combine(TeamDirectory(), "cycles.unreadable.jsonl")));
        Assert.False(File.Exists(Path.Combine(TeamDirectory(), "repair-unreadable-audit.jsonl")));
    }

    [Fact]
    public void Write_QuarantinesExactBytes_RewritesOnlyReadableBytes_AndClearsLivenessEvidence_G773()
    {
        var cyclePath = WriteCycle("readable-cycle");
        var stallPath = NotifySupervisionStore.ResolveStallPath(ArtifactRoot(), Domain, Team);
        Assert.True(NotifySupervisionStore.OpenStall(
            stallPath,
            new NotifySupervisionStallRecord
            {
                Key = "readable-stall",
                Kind = "g773",
                OwnerRole = "implementation",
                Source = "g773-test",
                Summary = "readable",
                SurfacedAt = now,
            },
            write: true).Applied);
        var cyclesBefore = File.ReadAllBytes(cyclePath);
        var stallsBefore = File.ReadAllBytes(stallPath);
        var unreadableCycle = Encoding.UTF8.GetBytes("{bad-cycle-json}\n");
        var unreadableStall = Encoding.UTF8.GetBytes("{bad-stall-json}\r\n");
        var existingCycleQuarantine = Encoding.UTF8.GetBytes("{prior-quarantine-evidence}\n");
        File.WriteAllBytes(
            Path.Combine(TeamDirectory(), "cycles.unreadable.jsonl"),
            existingCycleQuarantine);
        File.AppendAllBytes(cyclePath, unreadableCycle);
        File.AppendAllBytes(stallPath, unreadableStall);

        var (exitCode, output) = RunRepair("--write");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        var payload = document.RootElement;
        Assert.Equal("write", payload.GetProperty("command_mode").GetString());
        Assert.Equal("completed-repair", payload.GetProperty("repair_state").GetString());
        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.Equal(2, payload.GetProperty("unreadable_record_count").GetInt32());
        Assert.Equal(cyclesBefore, File.ReadAllBytes(cyclePath));
        Assert.Equal(stallsBefore, File.ReadAllBytes(stallPath));
        Assert.Equal(
            existingCycleQuarantine.Concat(unreadableCycle).ToArray(),
            File.ReadAllBytes(Path.Combine(TeamDirectory(), "cycles.unreadable.jsonl")));
        Assert.Equal(unreadableStall, File.ReadAllBytes(Path.Combine(TeamDirectory(), "stalls.unreadable.jsonl")));

        var auditPath = Path.Combine(TeamDirectory(), "repair-unreadable-audit.jsonl");
        var auditLine = Assert.Single(File.ReadAllLines(auditPath));
        using var audit = JsonDocument.Parse(auditLine);
        Assert.Equal("intent-cli.supervision-repair-unreadable/v1", audit.RootElement.GetProperty("schema").GetString());
        Assert.Equal("repair-unreadable", audit.RootElement.GetProperty("operation").GetString());
        Assert.Equal("completed", audit.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(2, audit.RootElement.GetProperty("unreadable_record_count").GetInt32());
        Assert.True(audit.RootElement.TryGetProperty("writer", out _));
        Assert.True(audit.RootElement.TryGetProperty("occurred_at", out _));
        Assert.Equal(2, audit.RootElement.GetProperty("files").GetArrayLength());

        var (livenessExit, livenessOutput) = RunLiveness(root);
        Assert.Equal(0, livenessExit);
        using var liveness = JsonDocument.Parse(livenessOutput);
        Assert.Equal(0, liveness.RootElement.GetProperty("unreadable_record_count").GetInt32());
    }

    [Fact]
    public void CleanStore_IsStrictNoOpInBothModes_AndNothingToRepairDiffersFromCompletedRepair_G773()
    {
        WriteCycle("clean-cycle");
        var before = SnapshotDirectory(TeamDirectory());

        var (dryRunExit, dryRunOutput) = RunRepair();
        Assert.Equal(0, dryRunExit);
        using (var dryRun = JsonDocument.Parse(dryRunOutput))
        {
            Assert.Equal("nothing-to-repair", dryRun.RootElement.GetProperty("repair_state").GetString());
            Assert.False(dryRun.RootElement.GetProperty("applied").GetBoolean());
        }
        Assert.Equal(before, SnapshotDirectory(TeamDirectory()));

        var (writeExit, writeOutput) = RunRepair("--write");
        Assert.Equal(0, writeExit);
        using (var write = JsonDocument.Parse(writeOutput))
        {
            Assert.Equal("nothing-to-repair", write.RootElement.GetProperty("repair_state").GetString());
            Assert.False(write.RootElement.GetProperty("applied").GetBoolean());
        }
        Assert.Equal(before, SnapshotDirectory(TeamDirectory()));
        Assert.DoesNotContain("completed-repair", writeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void AllCorruptWrite_BecomesEmptyHistory_NotNotFoundOrHealthy_G773()
    {
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(ArtifactRoot(), Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(cyclePath)!);
        File.WriteAllBytes(cyclePath, Encoding.UTF8.GetBytes("{all-corrupt}\n"));

        var (repairExit, _) = RunRepair("--write");
        var (emptyExit, emptyOutput) = RunLiveness(root);
        var missingRoot = Path.Combine(root, "never-existed");
        var (missingExit, missingOutput) = RunLiveness(missingRoot);

        Assert.Equal(0, repairExit);
        Assert.Equal(0, emptyExit);
        Assert.Equal(0, missingExit);
        using var empty = JsonDocument.Parse(emptyOutput);
        using var missing = JsonDocument.Parse(missingOutput);
        Assert.Equal("empty-history", empty.RootElement.GetProperty("supervision_state").GetString());
        Assert.Equal(0, empty.RootElement.GetProperty("unreadable_record_count").GetInt32());
        Assert.Equal("not-found", missing.RootElement.GetProperty("supervision_state").GetString());
        Assert.NotEqual(
            empty.RootElement.GetProperty("supervision_state").GetString(),
            missing.RootElement.GetProperty("supervision_state").GetString());
        Assert.DoesNotContain("healthy", empty.RootElement.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConcurrentGrowthAfterScan_FailsClosedAndDoesNotReplaceTheLiveFile_G773()
    {
        var cyclePath = WriteCycle("readable-cycle");
        var unreadable = Encoding.UTF8.GetBytes("{bad-cycle-json}\n");
        var concurrent = Encoding.UTF8.GetBytes("{concurrent-append}\n");
        File.AppendAllBytes(cyclePath, unreadable);
        var before = File.ReadAllBytes(cyclePath);
        NotifySupervisionStore.RepairUnreadableBeforeLengthRecheck = () =>
            AtomicAppendWriter.Append(cyclePath, concurrent);

        try
        {
            var (exitCode, output) = RunRepair("--write");

            Assert.Equal(1, exitCode);
            Assert.Contains("changed after scan", output, StringComparison.Ordinal);
            Assert.Equal(before.Concat(concurrent).ToArray(), File.ReadAllBytes(cyclePath));
            Assert.False(File.Exists(Path.Combine(TeamDirectory(), "cycles.unreadable.jsonl")));
            Assert.False(File.Exists(Path.Combine(TeamDirectory(), "repair-unreadable-audit.jsonl")));
        }
        finally
        {
            NotifySupervisionStore.RepairUnreadableBeforeLengthRecheck = null;
        }
    }

    [Fact]
    public void ArchiveHistory_IsRepairedWithoutReturningQuarantineToReaders_G773()
    {
        var artifactRoot = ArtifactRoot();
        var archivePath = NotifySupervisionStore.ResolveCycleArchivePath(
            artifactRoot,
            Domain,
            Team,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        File.WriteAllBytes(archivePath, Encoding.UTF8.GetBytes("{bad-archive-json}\n"));

        var (repairExit, _) = RunRepair("--write");
        var state = NotifySupervisionStore.Read(artifactRoot, Domain, Team);

        Assert.Equal(0, repairExit);
        Assert.True(state.Resolved, state.Error);
        Assert.Empty(state.UnreadableRecords);
        Assert.Equal(
            Encoding.UTF8.GetBytes("{bad-archive-json}\n"),
            File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(archivePath)!, "2026-08.unreadable.jsonl")));
    }

    [Fact]
    public void PromptAuditPayloadDamage_UsesTheExistingReaderReasonAndMarkdownListsIt_G773()
    {
        var cyclePath = WriteCycle("readable-cycle");
        File.AppendAllText(cyclePath, "{\"kind\":\"prompt-audit\"}\n", Encoding.UTF8);

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(root),
            [
                "repair-unreadable", "--domain", Domain, "--team", Team,
                "--routing-root", root, "--dry-run", "--format", "markdown",
            ],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("# notify supervise repair-unreadable", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("missing-prompt-audit-payload", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("line 2", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DocumentationMirrorsDescribeVerbatimQuarantineWithoutReconstruction_G773(string language)
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md");
        var document = File.ReadAllText(path);

        Assert.Contains("repair-unreadable", document, StringComparison.Ordinal);
        Assert.Contains("quarantine", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verbatim", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconstruct", document, StringComparison.OrdinalIgnoreCase);
    }

    private string WriteCycle(string cycleId)
    {
        var path = NotifySupervisionStore.ResolveCyclePath(ArtifactRoot(), Domain, Team);
        Assert.True(NotifySupervisionStore.RecordCycle(
            path,
            new NotifySupervisionCycle
            {
                CycleId = cycleId,
                StartedAt = now.AddSeconds(-1),
                CompletedAt = now,
                IntervalSeconds = 300,
            },
            write: true).Applied);
        return path;
    }

    private (int ExitCode, string Output) RunRepair(params string[] mode)
    {
        using var writer = new StringWriter();
        var args = new List<string>
        {
            "repair-unreadable",
            "--domain", Domain,
            "--team", Team,
            "--routing-root", root,
        };
        args.AddRange(mode);
        args.AddRange(["--format", "json"]);
        var exitCode = NotifyCommand.ExecuteSupervise(CreateContext(root), args.ToArray(), writer);
        return (exitCode, writer.ToString());
    }

    private (int ExitCode, string Output) RunLiveness(string repoRoot)
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(repoRoot),
            ["liveness", "--domain", Domain, "--team", Team, "--format", "json"],
            writer);
        return (exitCode, writer.ToString());
    }

    private string ArtifactRoot() => Path.Combine(root, ".intent-cli", "supervision");

    private string TeamDirectory() => NotifySupervisionStore.ResolveDirectory(ArtifactRoot(), Domain, Team);

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

    private static IReadOnlyDictionary<string, FileSnapshot> SnapshotDirectory(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToDictionary(
                    path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
                    path => new FileSnapshot(
                        Convert.ToBase64String(File.ReadAllBytes(path)),
                        new FileInfo(path).Length,
                        File.GetCreationTimeUtc(path),
                        File.GetLastWriteTimeUtc(path)),
                    StringComparer.Ordinal)
            : new Dictionary<string, FileSnapshot>(StringComparer.Ordinal);

    private sealed record FileSnapshot(
        string Bytes,
        long Length,
        DateTime CreationTimeUtc,
        DateTime LastWriteTimeUtc);
}
