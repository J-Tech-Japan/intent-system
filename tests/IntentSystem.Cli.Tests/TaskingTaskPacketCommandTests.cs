using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G191 — coverage for <c>intent-cli tasking task-packet</c>. Class name
/// contains <c>Tasking</c> and <c>TaskPacket</c> so reviewers can match the
/// issue's recommended <c>~Tasking|~Handoff|~TaskPacket</c> filter.
/// </summary>
public sealed class TaskingTaskPacketCommandTests : IDisposable
{
    private readonly Func<DateTimeOffset> originalTimestampFactory;
    private readonly Func<bool>? originalLauncher;
    private readonly Func<DateTimeOffset> originalHandoffTimestampFactory;

    public TaskingTaskPacketCommandTests()
    {
        originalTimestampFactory = TaskingTaskPacketCommand.TimestampFactory;
        originalLauncher = TaskingTaskPacketCommand.NestedProviderLauncher;
        originalHandoffTimestampFactory = TaskingHandoffCommand.TimestampFactory;
    }

    public void Dispose()
    {
        TaskingTaskPacketCommand.TimestampFactory = originalTimestampFactory;
        TaskingTaskPacketCommand.NestedProviderLauncher = originalLauncher;
        TaskingHandoffCommand.TimestampFactory = originalHandoffTimestampFactory;
    }

    [Fact]
    public void Execute_GivenValidHandoff_WritesUnpublishedTaskPacket()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff.json");
        var outPath = workspace.GetPath("task-packet.json");

        TaskingTaskPacketCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 34, 56, 789, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", handoffPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.False(root.GetProperty("is_published").GetBoolean());
        Assert.False(root.GetProperty("is_automation_visible").GetBoolean());
        Assert.Equal("local_only", root.GetProperty("task_packet_status").GetString());
        Assert.Equal("2026-04-29T12:34:56.789Z", root.GetProperty("generated_at_utc").GetString());

        var summary = root.GetProperty("summary_line").GetString();
        Assert.NotNull(summary);
        Assert.Contains("UNPUBLISHED", summary, StringComparison.Ordinal);
        Assert.Contains("not automation-visible", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidHandoff_TaskPacketJsonRoundTripsStably()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff-rt.json");
        var outPath = workspace.GetPath("task-packet-rt.json");

        TaskingTaskPacketCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 1, 2, 3, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", handoffPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var roundTripped = JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(roundTripped);
        Assert.Equal("intent-cli", roundTripped!.Domain);
        Assert.False(roundTripped.IsPublished);
        Assert.False(roundTripped.IsAutomationVisible);
        Assert.Equal(TaskingTaskPacketConstants.LocalOnlyStatus, roundTripped.TaskPacketStatus);
        Assert.Equal("local_only", roundTripped.TaskPacketStatus);
        Assert.Equal("2026-04-29T01:02:03.000Z", roundTripped.GeneratedAtUtc);
        Assert.Equal(Path.GetFullPath(outPath), roundTripped.ArtifactPath);
        Assert.Equal(handoffPath, roundTripped.SourceHandoffPath);
        Assert.Contains("UNPUBLISHED", roundTripped.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidHandoff_RecordsSourceHandoffPathAndSha256()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff-sha.json");
        var outPath = workspace.GetPath("task-packet-sha.json");

        var expectedBytes = File.ReadAllBytes(handoffPath);
        var expectedSha = ComputeSha256HexLowercase(expectedBytes);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", handoffPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var packet = JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(packet);
        Assert.Equal(handoffPath, packet!.SourceHandoffPath);
        Assert.Equal(expectedSha, packet.SourceHandoffSha256);
        Assert.Equal(expectedSha, packet.SourceHandoffSha256.ToLowerInvariant());
    }

    [Fact]
    public void Execute_GivenValidHandoff_EmbedsAllThreeSubPacketsCopiedFromHandoff()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff-embed.json");
        var outPath = workspace.GetPath("task-packet-embed.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", handoffPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var sourceHandoff = JsonSerializer.Deserialize<TaskingHandoffPacket>(File.ReadAllText(handoffPath));
        var packet = JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(File.ReadAllText(outPath));

        Assert.NotNull(sourceHandoff);
        Assert.NotNull(packet);
        Assert.NotNull(packet!.EmbeddedStatusBrief);
        Assert.NotNull(packet.EmbeddedContextCollect);
        Assert.NotNull(packet.EmbeddedNextSliceClassify);

        // Compare via re-serialization to assert deep-copy fidelity.
        var canonical = new JsonSerializerOptions { WriteIndented = false };
        Assert.Equal(
            JsonSerializer.Serialize(sourceHandoff!.StatusBrief, canonical),
            JsonSerializer.Serialize(packet.EmbeddedStatusBrief, canonical));
        Assert.Equal(
            JsonSerializer.Serialize(sourceHandoff.ContextCollect, canonical),
            JsonSerializer.Serialize(packet.EmbeddedContextCollect, canonical));
        Assert.Equal(
            JsonSerializer.Serialize(sourceHandoff.NextSliceClassify, canonical),
            JsonSerializer.Serialize(packet.EmbeddedNextSliceClassify, canonical));
    }

    [Fact]
    public void Execute_GivenMissingHandoffFile_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new TaskPacketWorkspace();
        var missingHandoff = workspace.GetPath("does-not-exist.json");
        var outPath = workspace.GetPath("task-packet-missing.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", missingHandoff, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMalformedHandoffJson_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GetPath("malformed-handoff.json");
        File.WriteAllText(handoffPath, "{ this is not : valid json,");
        var outPath = workspace.GetPath("task-packet-malformed.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", handoffPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenHandoffWithWrongStatus_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new TaskPacketWorkspace();
        var validHandoff = workspace.GenerateHandoff("handoff-source.json");
        var json = File.ReadAllText(validHandoff);
        // Flip the contract field from the literal "local_only" to a different value.
        var tampered = json.Replace(
            "\"tasking_handoff_status\": \"local_only\"",
            "\"tasking_handoff_status\": \"published\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        var tamperedPath = workspace.GetPath("handoff-tampered.json");
        File.WriteAllText(tamperedPath, tampered);
        var outPath = workspace.GetPath("task-packet-tampered.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", tamperedPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff-missing-flag.json");

        // Drop --out entirely.
        var args = new[] { "--from-handoff", handoffPath };

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--out", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff-unknown.json");
        var outPath = workspace.GetPath("task-packet-unknown.json");

        var args = new[]
        {
            "--from-handoff", handoffPath,
            "--out", outPath,
            "--bogus", "value"
        };

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff-no-mut.json");

        var queueStatePath = workspace.Context.GetQueueStatePath();
        var runsLogPath = workspace.Context.GetRunLogPath();

        var queueBytes = Encoding.UTF8.GetBytes("{\"schema_version\":\"1\",\"items\":[]}\n");
        var runsBytes = Encoding.UTF8.GetBytes("{\"event\":\"sentinel\",\"ts\":\"2026-04-29T00:00:00Z\"}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        File.WriteAllBytes(queueStatePath, queueBytes);
        File.WriteAllBytes(runsLogPath, runsBytes);

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        var outPath = workspace.GetPath("task-packet-no-mut.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", handoffPath, "--out", outPath },
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
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff-no-provider.json");

        var launcherInvoked = false;
        TaskingTaskPacketCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        var outPath = workspace.GetPath("task-packet-no-provider.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[] { "--from-handoff", handoffPath, "--out", outPath },
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

        var commandFile = Path.Combine(commandsDir, "TaskingTaskPacketCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingTaskPacketAnalyzer.cs");
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
    public void Execute_AlwaysMarksContractFieldsLiteralFalseAndLocalOnly()
    {
        // Maximum-input success path: domain set, queue-state populated,
        // clarifications and bindings present. Even here the contract fields
        // remain literal false / false / "local_only" on the task packet.
        using var workspace = new TaskPacketWorkspace();
        workspace.WriteQueueState("{\"schema_version\":\"1\",\"items\":[]}\n");
        workspace.WriteClarificationOpen(
            "## Current Open Blockers\n- 現時点で child issue cut を要する root blocker はない\n");
        workspace.WriteAutomationBindings("# bindings\n");

        var handoffPath = workspace.GenerateHandoff("handoff-contract.json");
        var outPath = workspace.GetPath("task-packet-contract.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-handoff", handoffPath,
                "--out", outPath,
                "--format", "text"
            },
            writer);

        Assert.Equal(0, exitCode);

        var packet = JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(packet);
        Assert.False(packet!.IsPublished);
        Assert.False(packet.IsAutomationVisible);
        Assert.Equal("local_only", packet.TaskPacketStatus);
        Assert.Equal(TaskingTaskPacketConstants.LocalOnlyStatus, packet.TaskPacketStatus);

        // Defensive: also assert raw JSON tokens (literal false).
        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_published").ValueKind);
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_automation_visible").ValueKind);
        Assert.Equal("local_only", doc.RootElement.GetProperty("task_packet_status").GetString());
    }

    [Fact]
    public void Execute_GivenJsonFormat_StdoutContainsArtifactJson()
    {
        using var workspace = new TaskPacketWorkspace();
        var handoffPath = workspace.GenerateHandoff("handoff-json-fmt.json");
        TaskingTaskPacketCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 5, 0, 0, TimeSpan.Zero);

        var outPath = workspace.GetPath("task-packet-json-fmt.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-handoff", handoffPath,
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);

        var stdoutPacket = JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(writer.ToString());
        var filePacket = JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(stdoutPacket);
        Assert.NotNull(filePacket);

        var canonical = new JsonSerializerOptions { WriteIndented = false };
        Assert.Equal(
            JsonSerializer.Serialize(filePacket, canonical),
            JsonSerializer.Serialize(stdoutPacket, canonical));
    }

    private static string ComputeSha256HexLowercase(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
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

    private sealed class TaskPacketWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-task-packet-tests-")
            .FullName;

        public TaskPacketWorkspace()
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

        /// <summary>
        /// Generate a real G190 handoff artifact in this workspace by invoking
        /// <see cref="TaskingHandoffCommand.Execute"/> directly. Returns the
        /// (relative-to-workspace) path of the resulting handoff file.
        /// </summary>
        public string GenerateHandoff(string filename)
        {
            var handoffPath = GetPath(filename);
            using var sink = new StringWriter();
            var exit = TaskingHandoffCommand.Execute(
                Context,
                new[] { "--out", handoffPath },
                sink);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to generate G190 handoff for tests: exit={exit}, stderr={sink}");
            }

            return handoffPath;
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
