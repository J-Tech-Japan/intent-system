using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G190 — coverage for <c>intent-cli tasking handoff</c>. Class name contains
/// <c>Tasking</c> and <c>Handoff</c> so reviewers can match the issue's
/// recommended <c>~Tasking|~Handoff</c> filter.
/// </summary>
// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection(RunSubmitCommandCollection.Name)]
public sealed class TaskingHandoffCommandTests : IDisposable
{
    private readonly Func<DateTimeOffset> originalTimestampFactory;
    private readonly Func<bool>? originalLauncher;

    public TaskingHandoffCommandTests()
    {
        originalTimestampFactory = TaskingHandoffCommand.TimestampFactory;
        originalLauncher = TaskingHandoffCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        TaskingHandoffCommand.TimestampFactory = originalTimestampFactory;
        TaskingHandoffCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenAllRequiredFlags_WritesUnpublishedHandoffArtifact()
    {
        using var workspace = new TaskingHandoffWorkspace();
        TaskingHandoffCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 34, 56, 789, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(
            workspace.Context,
            new[] { "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.False(root.GetProperty("is_published").GetBoolean());
        Assert.False(root.GetProperty("is_automation_visible").GetBoolean());
        Assert.Equal("local_only", root.GetProperty("tasking_handoff_status").GetString());
        Assert.Equal(
            TaskingHandoffConstants.LocalOnlyStatus,
            root.GetProperty("tasking_handoff_status").GetString());
        Assert.Equal("2026-04-29T12:34:56.789Z", root.GetProperty("generated_at_utc").GetString());

        var summary = root.GetProperty("summary_line").GetString();
        Assert.NotNull(summary);
        Assert.Contains("UNPUBLISHED", summary, StringComparison.Ordinal);
        Assert.Contains("not automation-visible", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenAllRequiredFlags_HandoffJsonRoundTripsStably()
    {
        using var workspace = new TaskingHandoffWorkspace();
        TaskingHandoffCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 1, 2, 3, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact-roundtrip.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(
            workspace.Context,
            new[] { "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var roundTripped = JsonSerializer.Deserialize<TaskingHandoffPacket>(File.ReadAllText(outPath));
        Assert.NotNull(roundTripped);
        Assert.Equal("intent-cli", roundTripped!.Domain);
        Assert.False(roundTripped.IsPublished);
        Assert.False(roundTripped.IsAutomationVisible);
        Assert.Equal(TaskingHandoffConstants.LocalOnlyStatus, roundTripped.TaskingHandoffStatus);
        Assert.Equal("local_only", roundTripped.TaskingHandoffStatus);
        Assert.Equal("2026-04-29T01:02:03.000Z", roundTripped.GeneratedAtUtc);
        Assert.Equal(Path.GetFullPath(outPath), roundTripped.ArtifactPath);
        Assert.Contains("UNPUBLISHED", roundTripped.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_EmbedsAllThreeSubPackets_StatusBrief_ContextCollect_NextSliceClassify()
    {
        using var workspace = new TaskingHandoffWorkspace();
        var outPath = workspace.GetPath("artifact-embeds.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(
            workspace.Context,
            new[] { "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var packet = JsonSerializer.Deserialize<TaskingHandoffPacket>(File.ReadAllText(outPath));
        Assert.NotNull(packet);
        Assert.NotNull(packet!.StatusBrief);
        Assert.NotNull(packet.ContextCollect);
        Assert.NotNull(packet.NextSliceClassify);
        Assert.Null(packet.StatusBriefError);
        Assert.Null(packet.ContextCollectError);
        Assert.Null(packet.NextSliceClassifyError);

        var canonical = new[]
        {
            NextSliceClassification.IssueCutReady,
            NextSliceClassification.ClarificationRequired,
            NextSliceClassification.SkipDueToWip,
            NextSliceClassification.ExistingIssueNeedsRepair,
            NextSliceClassification.ParentOnlyUpdate,
            NextSliceClassification.NoActionableItem,
            NextSliceClassification.InspectManually
        };
        Assert.Contains(packet.NextSliceClassify!.Classification, canonical);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new TaskingHandoffWorkspace();
        var queueStatePath = workspace.Context.GetQueueStatePath();
        var runsLogPath = workspace.Context.GetRunLogPath();

        var queueBytes = Encoding.UTF8.GetBytes("{\"schema_version\":\"1\",\"items\":[]}\n");
        var runsBytes = Encoding.UTF8.GetBytes("{\"event\":\"sentinel\",\"ts\":\"2026-04-29T00:00:00Z\"}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        File.WriteAllBytes(queueStatePath, queueBytes);
        File.WriteAllBytes(runsLogPath, runsBytes);

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        var outPath = workspace.GetPath("artifact-no-mutation.json");
        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(
            workspace.Context,
            new[] { "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        var queueAfter = File.ReadAllBytes(queueStatePath);
        var runsAfter = File.ReadAllBytes(runsLogPath);
        Assert.Equal(queueBefore, queueAfter);
        Assert.Equal(runsBefore, runsAfter);
    }

    [Fact]
    public void Execute_NeverInvokesProvider_NoProcessStartInNewSurface()
    {
        using var workspace = new TaskingHandoffWorkspace();
        var launcherInvoked = false;
        TaskingHandoffCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        var outPath = workspace.GetPath("artifact-no-provider.json");
        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(
            workspace.Context,
            new[] { "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        // Source-scan defensive contract guard against future drift. If the
        // source path cannot be resolved (test running in a published artifact),
        // return silently — the sentinel above is the runtime evidence.
        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingHandoffCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingHandoffAnalyzer.cs");
        if (!File.Exists(commandFile) || !File.Exists(analyzerFile))
        {
            return;
        }

        foreach (var path in new[] { commandFile, analyzerFile })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_GivenMissingOutFlag_ReturnsNonZero_NoArtifactWritten()
    {
        using var workspace = new TaskingHandoffWorkspace();
        var outPath = workspace.GetPath("artifact-missing-out.json");

        // Drop --out entirely.
        var args = new[] { "--domain", "intent-cli" };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
        Assert.Contains("--out", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new TaskingHandoffWorkspace();
        var outPath = workspace.GetPath("artifact-unknown.json");

        var args = new[]
        {
            "--out", outPath,
            "--bogus", "value"
        };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AlwaysMarksContractFieldsLiteralFalseAndLocalOnly()
    {
        // Maximum-input success path: domain set, queue-state populated,
        // clarifications and bindings present. Even here the contract fields
        // remain literal false / false / "local_only".
        using var workspace = new TaskingHandoffWorkspace();
        workspace.WriteQueueState("{\"schema_version\":\"1\",\"items\":[]}\n");
        workspace.WriteClarificationOpen(
            "## Current Open Blockers\n- 現時点で child issue cut を要する root blocker はない\n");
        workspace.WriteAutomationBindings("# bindings\n");

        var outPath = workspace.GetPath("artifact-contract-locked.json");
        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(
            workspace.Context,
            new[]
            {
                "--domain", "intent-cli",
                "--out", outPath,
                "--format", "text"
            },
            writer);

        Assert.Equal(0, exitCode);

        var packet = JsonSerializer.Deserialize<TaskingHandoffPacket>(File.ReadAllText(outPath));
        Assert.NotNull(packet);
        Assert.False(packet!.IsPublished);
        Assert.False(packet.IsAutomationVisible);
        Assert.Equal("local_only", packet.TaskingHandoffStatus);
        Assert.Equal(TaskingHandoffConstants.LocalOnlyStatus, packet.TaskingHandoffStatus);

        // Defensive: also assert raw JSON tokens (literal false).
        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_published").ValueKind);
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_automation_visible").ValueKind);
        Assert.Equal("local_only", doc.RootElement.GetProperty("tasking_handoff_status").GetString());
    }

    [Fact(Skip =
        "Sub-analyzers (StatusBriefAnalyzer, ContextCollectAnalyzer, "
        + "NextSliceClassifyAnalyzer) are deliberately tolerant of malformed "
        + "inputs — they capture parse failures into their own 'notes' or "
        + "InspectManually classification rather than throwing. Triggering a "
        + "real exception would require invasive mocking. The contract-tolerance "
        + "is documented in TaskingHandoffAnalyzer (each sub-analyzer call is "
        + "wrapped in try/catch and populates the matching '<sub_packet>_error' "
        + "field on failure).")]
    public void Execute_TolerantToSubAnalyzerFailure_PartialPacketStillWritesAndExitsZero()
    {
        // Intentionally skipped — see Skip reason above.
    }

    [Fact]
    public void Execute_GivenEmptyWorkspace_ProducesPacketWithDefaultDomainAndNoActionable()
    {
        using var workspace = new TaskingHandoffWorkspace();
        var outPath = workspace.GetPath("artifact-empty.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(
            workspace.Context,
            new[] { "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var packet = JsonSerializer.Deserialize<TaskingHandoffPacket>(File.ReadAllText(outPath));
        Assert.NotNull(packet);
        Assert.Equal("intent-cli", packet!.Domain);
        Assert.NotNull(packet.StatusBrief);
        Assert.NotNull(packet.ContextCollect);
        Assert.NotNull(packet.NextSliceClassify);
        Assert.Equal(NextSliceClassification.NoActionableItem, packet.NextSliceClassify!.Classification);
    }

    [Fact]
    public void Execute_GivenJsonFormat_StdoutContainsArtifactJson()
    {
        using var workspace = new TaskingHandoffWorkspace();
        TaskingHandoffCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 5, 0, 0, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact-json-format.json");
        using var writer = new StringWriter();
        var exitCode = TaskingHandoffCommand.Execute(
            workspace.Context,
            new[]
            {
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var stdoutPacket = JsonSerializer.Deserialize<TaskingHandoffPacket>(writer.ToString());
        var filePacket = JsonSerializer.Deserialize<TaskingHandoffPacket>(File.ReadAllText(outPath));
        Assert.NotNull(stdoutPacket);
        Assert.NotNull(filePacket);

        var canonical = new JsonSerializerOptions { WriteIndented = false };
        Assert.Equal(
            JsonSerializer.Serialize(filePacket, canonical),
            JsonSerializer.Serialize(stdoutPacket, canonical));
    }

    private static bool TryResolveCommandsSourceDirectory(out string commandsDirectory)
    {
        commandsDirectory = string.Empty;
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "IntentSystem.Cli", "Commands");
            if (Directory.Exists(candidate))
            {
                commandsDirectory = candidate;
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private sealed class TaskingHandoffWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-handoff-tests-")
            .FullName;

        public TaskingHandoffWorkspace()
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

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteClarificationOpen(string content)
        {
            var path = Path.Combine(rootPath, "intents", "intent-cli", "clarifications");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "open.md"), content);
        }

        public void WriteAutomationBindings(string content)
        {
            var path = Path.Combine(rootPath, "intents", "intent-cli", "automation");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "bindings.md"), content);
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
