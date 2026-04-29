using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G192 — coverage for <c>intent-cli tasking task-packet-preview</c>. Class
/// name contains <c>Tasking</c>, <c>TaskPacket</c>, and <c>Preview</c> so
/// reviewers can match the issue's recommended
/// <c>~Tasking|~TaskPacket|~Preview</c> filter.
/// </summary>
public sealed class TaskingTaskPacketPreviewCommandTests : IDisposable
{
    private readonly Func<DateTimeOffset> originalTimestampFactory;
    private readonly Func<bool>? originalLauncher;

    public TaskingTaskPacketPreviewCommandTests()
    {
        // Only snapshot the seams G192 actually mutates. Avoid clobbering
        // G190 / G191 timestamp seams (the workspace helper invokes those
        // commands but does not assert on their timestamps).
        originalTimestampFactory = TaskingTaskPacketPreviewCommand.TimestampFactory;
        originalLauncher = TaskingTaskPacketPreviewCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        TaskingTaskPacketPreviewCommand.TimestampFactory = originalTimestampFactory;
        TaskingTaskPacketPreviewCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenValidTaskPacket_WritesUnpublishedPreviewArtifact()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet.json");
        var outPath = workspace.GetPath("preview.json");

        TaskingTaskPacketPreviewCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 34, 56, 789, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", taskPacketPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.False(root.GetProperty("is_published").GetBoolean());
        Assert.False(root.GetProperty("is_automation_visible").GetBoolean());
        Assert.Equal("local_only", root.GetProperty("preview_status").GetString());
        Assert.Equal("2026-04-29T12:34:56.789Z", root.GetProperty("generated_at_utc").GetString());

        var summary = root.GetProperty("summary_line").GetString();
        Assert.NotNull(summary);
        Assert.Contains("UNPUBLISHED", summary, StringComparison.Ordinal);
        Assert.Contains("not automation-visible", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidTaskPacket_PreviewJsonRoundTripsStably()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-rt.json");
        var outPath = workspace.GetPath("preview-rt.json");

        TaskingTaskPacketPreviewCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 1, 2, 3, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", taskPacketPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var roundTripped =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(roundTripped);
        Assert.Equal("intent-cli", roundTripped!.Domain);
        Assert.False(roundTripped.IsPublished);
        Assert.False(roundTripped.IsAutomationVisible);
        Assert.Equal(TaskingTaskPacketPreviewConstants.LocalOnlyStatus, roundTripped.PreviewStatus);
        Assert.Equal("local_only", roundTripped.PreviewStatus);
        Assert.Equal("2026-04-29T01:02:03.000Z", roundTripped.GeneratedAtUtc);
        Assert.Equal(Path.GetFullPath(outPath), roundTripped.ArtifactPath);
        Assert.Equal(taskPacketPath, roundTripped.SourceTaskPacketPath);
        Assert.Contains("UNPUBLISHED", roundTripped.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidTaskPacket_RecordsAllSourceReferences()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-sha.json");
        var outPath = workspace.GetPath("preview-sha.json");

        var expectedBytes = File.ReadAllBytes(taskPacketPath);
        var expectedSha = ComputeSha256HexLowercase(expectedBytes);

        // Read the source task packet to compare its handoff fields.
        var sourceTaskPacket =
            JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(expectedBytes);
        Assert.NotNull(sourceTaskPacket);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", taskPacketPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var preview =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(preview);
        Assert.Equal(taskPacketPath, preview!.SourceTaskPacketPath);
        Assert.Equal(expectedSha, preview.SourceTaskPacketSha256);
        Assert.Equal(expectedSha, preview.SourceTaskPacketSha256.ToLowerInvariant());
        Assert.Equal(sourceTaskPacket!.SourceHandoffPath, preview.SourceHandoffPath);
        Assert.Equal(sourceTaskPacket.SourceHandoffSha256, preview.SourceHandoffSha256);
    }

    [Fact]
    public void Execute_GivenValidTaskPacket_EmbedsAllSubPacketsAndRecommendedAction()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-embed.json");
        var outPath = workspace.GetPath("preview-embed.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", taskPacketPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var sourceTaskPacket =
            JsonSerializer.Deserialize<TaskingTaskPacketArtifact>(File.ReadAllText(taskPacketPath));
        var preview =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(File.ReadAllText(outPath));

        Assert.NotNull(sourceTaskPacket);
        Assert.NotNull(preview);
        Assert.NotNull(preview!.EmbeddedStatusBrief);
        Assert.NotNull(preview.EmbeddedContextCollect);
        Assert.NotNull(preview.EmbeddedNextSliceClassify);

        var canonical = new JsonSerializerOptions { WriteIndented = false };
        Assert.Equal(
            JsonSerializer.Serialize(sourceTaskPacket!.EmbeddedStatusBrief, canonical),
            JsonSerializer.Serialize(preview.EmbeddedStatusBrief, canonical));
        Assert.Equal(
            JsonSerializer.Serialize(sourceTaskPacket.EmbeddedContextCollect, canonical),
            JsonSerializer.Serialize(preview.EmbeddedContextCollect, canonical));
        Assert.Equal(
            JsonSerializer.Serialize(sourceTaskPacket.EmbeddedNextSliceClassify, canonical),
            JsonSerializer.Serialize(preview.EmbeddedNextSliceClassify, canonical));

        Assert.Equal(sourceTaskPacket.RecommendedWorkerAction, preview.RecommendedWorkerAction);
    }

    [Fact]
    public void Execute_GivenValidTaskPacket_RenderedPreviewMarkdownContainsKeyHeadingsAndStatus()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-md.json");
        var outPath = workspace.GetPath("preview-md.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", taskPacketPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var preview =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(preview);
        var md = preview!.RenderedPreviewMarkdown;
        Assert.Contains("# Worker task preview", md, StringComparison.Ordinal);
        Assert.Contains("## Source references", md, StringComparison.Ordinal);
        Assert.Contains("## Recommended worker action", md, StringComparison.Ordinal);
        Assert.Contains("## Status brief", md, StringComparison.Ordinal);
        Assert.Contains("## Context collect", md, StringComparison.Ordinal);
        Assert.Contains("## Next-slice classify", md, StringComparison.Ordinal);
        Assert.Contains("UNPUBLISHED, local-only, not automation-visible", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingTaskPacketFile_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new PreviewWorkspace();
        var missing = workspace.GetPath("does-not-exist.json");
        var outPath = workspace.GetPath("preview-missing.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", missing, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMalformedTaskPacketJson_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GetPath("malformed-task-packet.json");
        File.WriteAllText(taskPacketPath, "{ this is not : valid json,");
        var outPath = workspace.GetPath("preview-malformed.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", taskPacketPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenTaskPacketWithWrongStatus_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new PreviewWorkspace();
        var validTaskPacket = workspace.GenerateTaskPacket("task-packet-source.json");
        var json = File.ReadAllText(validTaskPacket);
        var tampered = json.Replace(
            "\"task_packet_status\": \"local_only\"",
            "\"task_packet_status\": \"published\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        var tamperedPath = workspace.GetPath("task-packet-tampered.json");
        File.WriteAllText(tamperedPath, tampered);
        var outPath = workspace.GetPath("preview-tampered.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", tamperedPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-missing-flag.json");

        // Drop --out entirely.
        var args = new[] { "--from-task-packet", taskPacketPath };

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--out", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-unknown.json");
        var outPath = workspace.GetPath("preview-unknown.json");

        var args = new[]
        {
            "--from-task-packet", taskPacketPath,
            "--out", outPath,
            "--bogus", "value"
        };

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-no-mut.json");

        var queueStatePath = workspace.Context.GetQueueStatePath();
        var runsLogPath = workspace.Context.GetRunLogPath();

        var queueBytes = Encoding.UTF8.GetBytes("{\"schema_version\":\"1\",\"items\":[]}\n");
        var runsBytes = Encoding.UTF8.GetBytes("{\"event\":\"sentinel\",\"ts\":\"2026-04-29T00:00:00Z\"}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        File.WriteAllBytes(queueStatePath, queueBytes);
        File.WriteAllBytes(runsLogPath, runsBytes);

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        var outPath = workspace.GetPath("preview-no-mut.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", taskPacketPath, "--out", outPath },
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
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-no-provider.json");

        var launcherInvoked = false;
        TaskingTaskPacketPreviewCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        var outPath = workspace.GetPath("preview-no-provider.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[] { "--from-task-packet", taskPacketPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingTaskPacketPreviewCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingTaskPacketPreviewAnalyzer.cs");
        var artifactFile = Path.Combine(commandsDir, "TaskingTaskPacketPreviewArtifact.cs");
        if (!File.Exists(commandFile) || !File.Exists(analyzerFile) || !File.Exists(artifactFile))
        {
            return;
        }

        foreach (var path in new[] { commandFile, analyzerFile, artifactFile })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_AlwaysMarksContractFieldsLiteralFalseAndLocalOnly()
    {
        using var workspace = new PreviewWorkspace();
        workspace.WriteQueueState("{\"schema_version\":\"1\",\"items\":[]}\n");
        workspace.WriteClarificationOpen(
            "## Current Open Blockers\n- 現時点で child issue cut を要する root blocker はない\n");
        workspace.WriteAutomationBindings("# bindings\n");

        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-contract.json");
        var outPath = workspace.GetPath("preview-contract.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-task-packet", taskPacketPath,
                "--out", outPath,
                "--format", "text"
            },
            writer);

        Assert.Equal(0, exitCode);

        var preview =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(preview);
        Assert.False(preview!.IsPublished);
        Assert.False(preview.IsAutomationVisible);
        Assert.Equal("local_only", preview.PreviewStatus);
        Assert.Equal(TaskingTaskPacketPreviewConstants.LocalOnlyStatus, preview.PreviewStatus);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_published").ValueKind);
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_automation_visible").ValueKind);
        Assert.Equal("local_only", doc.RootElement.GetProperty("preview_status").GetString());
    }

    [Fact]
    public void Execute_GivenJsonFormat_StdoutContainsArtifactJson()
    {
        using var workspace = new PreviewWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("task-packet-json-fmt.json");
        TaskingTaskPacketPreviewCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 5, 0, 0, TimeSpan.Zero);

        var outPath = workspace.GetPath("preview-json-fmt.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketPreviewCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-task-packet", taskPacketPath,
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);

        var stdoutPreview =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(writer.ToString());
        var filePreview =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(stdoutPreview);
        Assert.NotNull(filePreview);

        var canonical = new JsonSerializerOptions { WriteIndented = false };
        Assert.Equal(
            JsonSerializer.Serialize(filePreview, canonical),
            JsonSerializer.Serialize(stdoutPreview, canonical));
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

    private sealed class PreviewWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-task-packet-preview-tests-")
            .FullName;

        public PreviewWorkspace()
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
        /// Generate a real G190 handoff and a real G191 task packet derived
        /// from it in this workspace by invoking the actual G190 / G191
        /// commands. Returns the absolute path of the resulting task packet.
        /// </summary>
        public string GenerateTaskPacket(string filename)
        {
            var handoffPath = GetPath("__handoff_" + filename);
            using (var sink = new StringWriter())
            {
                var handoffExit = TaskingHandoffCommand.Execute(
                    Context,
                    new[] { "--out", handoffPath },
                    sink);
                if (handoffExit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G190 handoff for tests: exit={handoffExit}, stderr={sink}");
                }
            }

            var taskPacketPath = GetPath(filename);
            using (var sink = new StringWriter())
            {
                var taskPacketExit = TaskingTaskPacketCommand.Execute(
                    Context,
                    new[] { "--from-handoff", handoffPath, "--out", taskPacketPath },
                    sink);
                if (taskPacketExit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G191 task packet for tests: exit={taskPacketExit}, stderr={sink}");
                }
            }

            return taskPacketPath;
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
